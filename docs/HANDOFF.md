# Be Productive — project handoff

A complete handoff for anyone (human or AI) picking this project up. It covers what the app is, every
decision made, the architecture, how it's built and tested, what's verified vs not, the bugs found and fixed,
how to run and set it up, and what's left. Read this top to bottom and you have the whole context.

Last updated after step 6 (tamper resistance) and the lock-screen bug fixes.

---

## 1. What it is

**Be Productive** is a personal desktop focus / distraction blocker for **Windows** (a Linux port is possible
later — the code is structured for it). Until you complete a daily amount of focus time, the laptop is
restricted to the apps and sites you need for work.

- **It is for one person** (the repo owner), who keeps a **local administrator** account on their own laptop.
  Because an administrator can always defeat any user-mode lock eventually, the design goal is **maximum
  friction**, not perfect prevention. The research concluded ~5–10 minutes of deliberate, elevated effort is
  the realistic ceiling for an admin, and the app aims to make casual/impulsive bypass much harder than that.
- **Repo:** `gabsvk-ux/be-productive` on GitHub.
- **Working branch:** `claude/desktop-distraction-blocker-1hktob`. **All work is on this branch**; the default
  branch does not contain it. No PR has been opened (don't open one unless asked).
- **Build/test:** `dotnet build` and `dotnet test` from the repo root. **Currently 142 tests, all passing**
  (118 Core + 24 App). Clean build, working tree clean, everything pushed.

---

## 2. How it behaves (the product)

1. **Start-of-day gate.** At the first login of the day — and on every login/wake until the day's minimum is
   done — a **full-screen window that can't be closed** forces you to either plan the day or postpone. You pick
   a **preset** (Uni, Projects, Programming…), choose how much focus to commit (≥ the weekday minimum), pick
   the first block length, and Start; or **Postpone** to later today (max 3/week, no later than 20:00).
2. **Pomodoro blocks.** 15/5, 25/5 or 45/15 (focus/break minutes), chosen before each block or during a break.
   A **long break** (default 20 min) replaces the short one after every 4th completed block *if* more focus is
   still planned. **Pause** is allowed during focus (max 10 min, then auto-resumes); **no pause during breaks**.
   A block that starts before the target is reached runs to its end.
3. **During focus:** only the preset's allowed apps + an always-allowed set + Chrome run; distraction domains
   are blocked; Task Manager / cmd / regedit / terminals are blocked (unless the preset allows terminals).
4. **Breaks:** everything is allowed except the (empty by default) hard blocklist; admin tools stay blocked.
   When the break ends a full-screen prompt asks for the next block length (or the preset default starts after
   60 s). You can switch preset during a break.
5. **Done:** once the planned time is banked, the laptop is free. You can add **extra time** (stoppable any
   time, counts in stats).
6. **Need out early:** use a **recovery code** from a friend (`bp exit <code>` or the app). The day is marked
   failed. Each code works once.
7. **Stats:** focus per day/preset, streaks, and counts of pauses, postpones, recovery exits, blocked app
   launches and tamper attempts. Viewable in the app (7/30/365-day views with a chart) and `bp stats`.

### Settled design decisions (don't re-litigate without asking)
- **Minimum:** 100 focus minutes/day default, configurable **per weekday** (0 = no gate that day). "Focus time
  only" counts (not breaks/pauses).
- **Day boundary:** resets at **04:00** (a 1 AM session counts as the day before). Missed days are logged, not
  carried over.
- **Postpone:** ≤ 3/week (week starts Monday 04:00), latest start 20:00.
- **Planned time is binding**; extra time added afterward is soft (stoppable).
- **Admin tools blocked** until the planned time is done, **breaks included**.
- **Terminals:** per-preset switches `AllowTerminal` and `AllowElevatedTerminal` (admin terminals, e.g. for
  installing things during setup — meant to be turned off afterward). Programming allows terminals by default.
- **Music:** no music app is always-allowed. Spotify goes in the presets that want it (Projects, Programming).
  Plain background-noise audio files should stay playable everywhere (they're files, not an app, so they're
  not killed).
- **Websites:** blocked by **whole domain** via the hosts file. Per-video/path blocking (e.g. allow one
  YouTube tutorial) is a future Chrome-extension feature, deliberately deferred.
- **Recovery:** one-time codes held by a friend; new sets are **emailed to the friend** (Resend API) and
  **never shown on your screen**. Challenge-response (friend signs from their phone) is a planned v2 upgrade.
- **"Tighten now, loosen tomorrow":** making a rule stricter (removing an allowed app, blocking a site,
  turning a terminal off, raising a minimum) applies immediately; **loosening applies only from the next day**,
  so you can't use settings to escape today.

---

## 3. Architecture

Five projects + two test projects (`BeProductive.sln`), .NET 8, C#.

| Project | Role |
|---|---|
| `src/BeProductive.Core` | OS-agnostic: the session engine, SQLite storage, the pipe (IPC) protocol, recovery codes, the enforcement planner/enforcer/watchdog, and the safety net (change journal + restore + the enforcement gate). No Windows APIs. |
| `src/BeProductive.Windows` | Windows implementations behind Core interfaces: registry, hosts file, DNS flush, process guard, enforcement policies, app launcher, user-session/SID. |
| `src/BeProductive.Service` | The background service (a Windows Service, runs as **SYSTEM** when installed). Hosts five workers (below). |
| `src/BeProductive.App` | The Avalonia desktop app: gate, dashboard, next-block prompt, settings, stats, notifications, tray icon. A pipe client. |
| `src/BeProductive.Cli` | `bp` — the same day commands as the app plus setup/safety (`codes`, `stats`, `restore`, `uninstall-authorize`). |
| `tests/BeProductive.Core.Tests` | 118 unit tests (engine, enforcement, safety net, recovery, stats, rules, SQLite, watchdog). |
| `tests/BeProductive.App.Tests` | 24 headless Avalonia tests; they also render screenshots into `docs/screenshots/`. |

### The core idea
The **`SessionEngine`** is pure and clock-driven (time is passed in, never read from the wall clock), which is
why it's heavily unit-testable. Each tick it decides what *should* be locked and produces a **`SessionStatus`**
(`Restriction` = None/Gate/Focus/Break/AwaitingNext, plus `AdminToolsBlocked` and the active `PresetId`). State
persists in SQLite (`C:\ProgramData\BeProductive\be-productive.db`) so restarts/reboots resume.

The **service** (`SessionService`) owns the engine, applies commands under a lock, saves state, and answers the
UI/CLI over a **named pipe** (`\\.\pipe\BeProductive`, ACL'd for the signed-in user). Workers in
`src/BeProductive.Service` (registered in `Program.cs`, in order):
- `SafetyNetWorker` — undoes leftovers at start; checks panic/lease on a timer; undoes everything on stop.
- `RecoveryCodeWorker` — retries emailing a new code set when all are used.
- `SessionWorker` — ticks the engine + enforcer once a second.
- `IpcServerWorker` — the pipe server.
- `WatchdogWorker` — relaunches the gate/app if killed while locked (step 6).

### Enforcement (step 4)
- **`EnforcementPlanner`** (pure, Core) turns `SessionStatus` + the active `Preset` into an **`EnforcementPlan`**:
  which domains to block, which policy groups to set (Chrome, admin-tools), and how to police processes
  (`KillMode`: allowlist during focus; blocklist-terminals during breaks while admin tools are blocked).
- **`Enforcer`** (Core) reconciles the plan onto the machine each tick. It only re-applies hosts/policies when
  the plan changes or a re-assert interval passes (default 20 s, to undo tampering), renews the lease, and
  undoes everything when unlocked or on panic. Kills go via the gate too.

### The safety net — the single most important invariant
**Every machine change goes through `SystemChanges`** (Core/SafetyNet), and **every change or kill goes through
`EnforcementGate`**. Never write the registry/hosts or kill a process directly. This is what makes the app
safe to run.
- `SystemChanges` writes a **write-ahead journal** (`journal.json`) recording the prior value *before* each
  change, **leases** the change (auto-undo if not renewed within ~15 min — survives a crash/hang), and can
  `RestoreAll`.
- `EnforcementGate.Mode` is **DryRun** (default — logs only), **Panic** (a panic file exists → undo + enforce
  nothing), or **Live**.
- OS primitives live behind interfaces (`IRegistry`, `IHostsFile`, `IDnsCache`, `IProcessGuard`,
  `IEnforcementPolicies`, `IAppLauncher`), Windows-implemented in `BeProductive.Windows`, no-op/`Unsupported`
  on other platforms so Core + tests build and run on Linux.

### Tamper resistance (step 6)
- **Watchdog** (`Watchdog` + `IAppLauncher`/`WindowsAppLauncher`, driven by `WatchdogWorker`): while locked, if
  the app/gate is killed it's relaunched and the kill is recorded as a tamper attempt. Honours DryRun/panic
  (only logs there). `WindowsAppLauncher` starts the app in the signed-in session via `CreateProcessAsUser`.
- **Tamper events** (`SessionEventKinds.TamperAttempt`) surface in stats alongside blocked-app counts.
- **Uninstall needs a code**: `bp uninstall-authorize` reuses `RecoveryCodeService`; `install.ps1 -Uninstall`
  requires a valid code when codes are set up.
- **Config hardening (armed install).** Editing `settings.json` can't weaken a real install. When the
  service runs as an installed Windows service it is *armed* (`EnforcementArming`, set from
  `WindowsServiceHelpers.IsWindowsService()`): `EnforcementGate` ignores `DryRun` and the panic file, so
  the config can only make the lock stricter, never turn it off. A console/dev run stays unarmed so DryRun
  and the panic file still work. `install.ps1` also `icacls`-locks the data directory to read-only for the
  signed-in user (a standard-user account then can't edit the config or drop a panic file without the admin
  password), and `ConfigWatchWorker` records a tamper attempt if `settings.json` changes while running.
- **Service self-protection** (in `install.ps1`): `sc failure` recovery actions (restart if killed) + a
  security descriptor denying Stop/Pause/Change-config to administrators (they keep WRITE_DAC so it can be
  reset). `restore.ps1` resets the descriptor + recovery actions before stopping, so recovery always works.

---

## 4. Build order and status — **all six steps built**

1. ✅ Skeleton, recovery codes, dry-run mode, the safety net.
2. ✅ Session engine, SQLite tracking, the service + pipe.
3. ✅ Desktop app (gate, dashboard, next-block, settings, tray) + rules editing (terminal switches,
   tighten-now/loosen-tomorrow).
4. ✅ Enforcement: kill not-allowed apps, hosts-file domain blocking, Chrome + admin-tool policies.
5. ✅ Stats screen + `bp stats`.
6. ✅ Tamper resistance: watchdog, service self-protection, uninstall lock, tamper-attempt stats.

There is **no remaining planned step.** The code is feature-complete.

---

## 5. What is verified vs NOT

This repo was developed in a Linux cloud container, so **Windows-specific behaviour is compile- and
logic-checked but mostly not executed here.** The user tests on a **Windows 10 VM (VirtualBox, with
snapshots).**

**Confirmed working on the VM so far:**
- Build and all tests pass; the app runs; the full-screen gate shows; the UI↔service pipe works.
- **Panic file works** (creating it stops enforcement and undoes changes).
- **Recovery code works** (`bp exit <code>` ends the lock).

**Bugs found during live testing and fixed (all pushed):**
- Service crashed when a 2nd pipe client connected — pipe ACL needed the service's own account. (`1364f25`)
- Under `dotnet run`, app + service are both `dotnet.exe`; the enforcer killed them (and itself). Added
  `dotnet` + the service's own process tree to never-kill. **Lesson: test live enforcement with the installed
  service, not `dotnet run`.** (`dba1cc2`)
- Access-denied (service not elevated) spammed stack traces; now one throttled, actionable warning. (`6dbc97c`)
- Panic-file recovery hardened so a live service can't kill its own PowerShell mid-recovery. (`c7eb952`)
- **Black screen at the lock/login screen**: enforcement was killing `LogonUI`/`LockApp`; at boot before
  sign-in it would kill the login UI too (lock-yourself-out risk). Fixed by not killing when no user is signed
  in + a never-kill safe-list of logon/lock/UAC/credential UI. (`dcecd5a`)
- **Regression from that fix**: an unreliable "session locked" check (the Windows `WTSINFOEX` flag is
  version-dependent) made the guard think the session was always locked, so nothing got blocked after a
  restart. Removed the lock detection; kept the robust parts. (`8e01d27`)

**NOT yet verified on Windows (needs the VM):**
- Enforcement actually killing apps / blocking sites / setting policies with the **installed** service, Live.
- The watchdog relaunching the gate via `CreateProcessAsUser`.
- Service self-protection: casual `Stop-Service` denied; killed service restarts (`sc failure`); the service
  security-descriptor SDDL.
- `install.ps1 -Uninstall -Code …` end to end.
- `restore.ps1` recovering a **hardened** install.
- The lock-screen / boot-login fix (Win+L shows a normal lock screen; blocking resumes after a reboot).
- Resend email delivery of recovery codes against the real API.
- The armed-install config hardening and the data-directory ACL lockdown (`install.ps1` icacls).

**The step-6 live test checklist** is in `scripts/test-step6.ps1` (tests 1–3 automated; test 4, `restore.ps1`,
manual). The README's "Setting it up for real use" and "Safety first" sections are the operational guide.

---

## 6. How to build, run, test

```bash
dotnet build
dotnet test                       # 142 tests
```
Dev-run on any OS (enforcement is no-op off Windows; DryRun on by default so it only logs):
```bash
dotnet run --project src/BeProductive.Service   # terminal 1 (the service)
dotnet run --project src/BeProductive.App       # terminal 2 (the app)
dotnet run --project src/BeProductive.Cli -- status
```
Render the UI screenshots: `SCREENSHOT_DIR=docs/screenshots dotnet test` (headless Avalonia, uses Skia).

**On Windows, for real:** `.\scripts\install.ps1` from an admin PowerShell installs the service (SYSTEM), the
app (at sign-in) and `bp`, and applies the step-6 hardening. Remove with
`.\scripts\install.ps1 -Uninstall -Code <recovery code>`.

### Important runtime facts
- **Data dir:** `C:\ProgramData\BeProductive` (db, journal.json, recovery-codes.json, settings.json, PANIC).
  Overridable with a `DataDirectory` setting / env var (tests and dev use a temp dir).
- **Config:** `settings.json` in the data dir; see `docs/settings.example.json` for every section
  (`Enforcement`, `Sessions`, `Watchdog`, `Recovery`, `RecoveryDelivery`). `appsettings.json` ships dev defaults
  (DryRun true).
- **Live enforcement needs admin/SYSTEM** (hosts file + HKLM). Run the service elevated or installed.
- **Escapes** (in order of intended use): recovery code (friend) → `restore.ps1` (admin) → Safe Mode (service
  doesn't run there). The panic file is a **dev kill-switch**; see §7.

---

## 7. The panic file — read this before changing enforcement

The panic file (`C:\ProgramData\BeProductive\PANIC`, or the no-admin `C:\Users\Public\BEPRODUCTIVE-PANIC.txt`)
turns enforcement off within ~30 s. It's deliberately trivial to create so a **bug can't trap you while
testing**. But on a real locked-down install it's a one-click bypass, which defeats the lock. Decision made
with the user:
- **Keep it available** (default unchanged) because it's the testing safety net.
- The service **logs a loud warning at startup** whenever enforcement is Live with the panic file enabled.
- **For real daily use, set `Enforcement:PanicFileEnabled: false`.** Then the only ways out are the recovery
  code and `restore.ps1`/Safe Mode — the intended friction. `restore.ps1` does **not** depend on the panic file.

The user said: do NOT default it to false; the warning + docs are enough.

---

## 8. Must-not-break rules (for any future work)

- Never bypass `SystemChanges` / `EnforcementGate`. No direct registry/hosts/process writes.
- Keep **DryRun** and the **panic file** working for every new kind of enforcement.
- Keep **`RenewLease`** called while locked, so a crash always self-heals within the lease.
- **Never kill system or other users' processes**, the logon/lock/UAC UI, `dotnet`, or the service's own
  process tree. Only kill in the signed-in user's session, and only when a user is signed in.
- Never write Microsoft-owned domains to the hosts file (Defender resets them).
- Keep OS-specific code behind the `BeProductive.Windows` interfaces; Core stays portable and testable on Linux.
- Extend `restore.ps1` for any new journalled change type, and keep it independent of the panic file.
- Recovery codes and "tighten now, loosen tomorrow" are settled — reuse, don't rebuild.
- Commit messages end with the attribution trailer the environment specifies. Don't open a PR unless asked.

---

## 9. File map (where things live)

**Core / Sessions** — the engine and model:
`SessionEngine` (rules), `SessionService` (service-side owner + commands), `SessionState`/`SessionStatus`
(persisted state + per-tick snapshot), `Preset`/`BlockKind`/`SessionOptions`, `RuleEditor`
(tighten-now/loosen-tomorrow), `SessionStore` (interfaces + event kinds), `Stats`/`StatsBuilder` (tracking).

**Core / Enforcement** — what to lock and how:
`EnforcementPlanner` (pure plan), `Enforcer` (reconcile), `EnforcementPlan`, `EnforcementGate` (DryRun/Panic/
Live + panic files), `EnforcementOptions` (global domains, always-allowed apps, terminals, reassert interval),
`ProcessGuard` (interface), `AppLauncher` (interface), `Watchdog`.

**Core / SafetyNet** — the reversible machine layer:
`SystemChanges` (the one door), `ChangeJournal`, `HostsSection` (the managed hosts block), `Platform`
(IRegistry/IHostsFile/IDnsCache + Unsupported).

**Core / Recovery / Storage / Ipc:** `RecoveryCode*` (codes, hashing, store, Resend delivery, service),
`SqliteStore` (migrations + all queries), `IpcProtocol` (requests/responses over the pipe).

**Windows:** `Windows{Registry,HostsFile,DnsCache,ProcessGuard,EnforcementPolicies,AppLauncher,UserSession}`.
`WindowsProcessGuard` has the never-kill list and the "only when signed in" guard; `WindowsUserSession` has
`ActiveSessionId`/`ActiveUserSid`/`IsUserSignedIn` (WTS + `CreateProcessAsUser` token plumbing is in
`WindowsAppLauncher`).

**Service:** `Program.cs` (host + worker registration), the five workers, `appsettings.json`.

**App (Avalonia):** `WindowCoordinator` (decides which window shows from status, owns the tray), `AppViewModel`
(pipe client + status), view-models per screen, `Views/*` (LockWindow base, Gate/NextBlock full-screen,
Dashboard, Settings, Stats with `ColumnChart`, Notification).

**CLI:** `Program.cs` (command dispatch), `StatusPrinter`, `StatsPrinter`.

**Scripts:** `install.ps1` (install + hardening + coded uninstall), `restore.ps1` (emergency undo, panic-
independent), `test-step6.ps1` (live tamper-resistance checks).

**Docs:** `design.md` (full v1 design), `handoff-enforcement.md` (enforcement/step-6 spec + seams),
`research/tech-stack-and-extension.md` and `research/admin-tamper-resistance.md` (the research the stack and
Windows specifics are based on — note Tier-3 tamper measures are intentionally skipped), `settings.example.json`.

---

## 10. What's left / future (none committed to)

- **Finish the Windows live verification** in §5 — the only outstanding work on the current feature set.
- Future, explicitly deferred: Linux port; a Chrome extension for path/video-level blocking; folder
  enforcement (folders are shortcuts only today); challenge-response recovery (friend approves from phone);
  a hard blocklist for breaks (empty today). None are planned or scheduled.
