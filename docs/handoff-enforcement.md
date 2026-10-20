# Handoff: enforcement (build steps 4 and 6)

This document is for whoever builds the enforcement layer. Steps 1–3 and 5 of the
[build order](design.md#10-build-order) are done; this is the part that was deliberately
left for a third party. It explains what exists, the seams it plugs into, exactly what
to build, and the rules it must not break.

Read [design.md](design.md) first (the whole product) and the two research notes:
[tamper resistance on an admin account](research/admin-tamper-resistance.md) and
[tech stack & extension](research/tech-stack-and-extension.md). This document assumes both.

## 1. Where the boundary is

Everything above enforcement already works: the session engine decides, second by second,
what *should* be locked. **Nothing acts on that decision yet.** Your job is to make it real,
without weakening any of the safety net that already surrounds it.

The one fact the rest of the app produces for you, every tick, is a `SessionStatus`
(`src/BeProductive.Core/Sessions/SessionStatus.cs`). The fields that matter here:

| Field | Meaning for enforcement |
|---|---|
| `Restriction` | `None` / `Gate` / `Focus` / `Break` — what kind of lock is in force now |
| `AdminToolsBlocked` | true while admin tools (Task Manager, cmd, PowerShell, Terminal, regedit, Services) must stay blocked — i.e. all day until the planned time is done, breaks included |
| `PresetId` | the active preset; look it up with `IRulesStore.Find(id)` to get its allowed apps, blocked domains and terminal switches |
| `Phase`, `Outcome` | finer state if you need it |

The matching rules already decided (do **not** re-decide them):
- **Focus:** only the preset's `AllowedApps` + the always-allowed set + Chrome; distraction
  domains + the preset's `BlockedDomains` blocked; Chrome policies on.
- **Break:** everything allowed except the hard blocklist (empty in v1); admin tools still
  blocked if `AdminToolsBlocked`.
- **Gate / Postponed / Free:** see design.md §2. Postponed and Free lock nothing.
- **Terminals:** a preset allows them via `Preset.AllowTerminal` / `AllowElevatedTerminal`.
  A preset that allows terminals keeps them during its breaks too; every other preset, and
  all of `AdminToolsBlocked`, blocks them.
- **Music:** no music app is always-allowed. Spotify is in a preset's `AllowedApps` only
  where the user added it. Plain background-noise audio files must stay playable everywhere.

## 2. The safety net you must go through (non-negotiable)

Step 1 built a write-ahead safety net. **Every machine change must go through it** — never
touch the registry, hosts file or processes directly. This is what guarantees the user can
never be permanently locked out, and it is the whole reason the project is safe to run.

`SystemChanges` (`src/BeProductive.Core/SafetyNet/SystemChanges.cs`), already injected, is the
only door to the machine:

- `SetRegistryValue(keyPath, name, kind, data)` — records the old value, then writes.
- `SetHostsBlocklist(domains)` — rewrites only the app's marked section of the hosts file.
- `RenewLease(expiresAt)` — **call this every tick while a lock is active.** Applied changes
  carry a lease (default 15 min); if nothing renews it, the service undoes everything. This is
  what recovers the machine after a crash or a hang.
- `RestoreAll(reason)` / `RestoreIfExpired()` — undo. Already called on stop, restart and
  lease expiry by `SafetyNetWorker`.

Rules baked into `SystemChanges`, which you inherit for free and must not circumvent:
- Every change is journalled (`journal.json`) **before** it is made, with the prior value.
- `EnforcementGate.Mode` gates all of it: `DryRun` (default — only logs), `Panic` (the
  `PANIC` file is present — undo and enforce nothing), `Live`. **Honour the gate.** New process
  killing must call `EnforcementGate.TryEnforce(...)` the same way, so dry-run and panic work.
- Restores always run regardless of mode (undoing is always safe).
- The Windows implementations of `IRegistry` / `IHostsFile` / `IDnsCache` live in
  `src/BeProductive.Windows`. Never write a Microsoft-owned domain to the hosts file
  (Defender resets those — see the research note).

Process killing has **no** equivalent yet — you add it. It is the one kind of change that
isn't a stored value to put back, so model it as "stop enforcing" rather than "restore": when
focus ends, you simply stop killing. But it must still obey `EnforcementGate` and be logged.

## 3. Step 4 — enforcement

Build a new enforcer in the service that runs each tick off the current `SessionStatus`.
Suggested shape: a hosted service alongside the existing `SessionWorker` /`SafetyNetWorker`
(`src/BeProductive.Service`), or an interface in Core called from `SessionService.TickAsync`.
Keep the OS-specific parts behind interfaces in `BeProductive.Windows`, exactly as the hosts
/ registry / DNS code already is, so Linux can follow later (see §5).

What it does on each tick, driven only by `SessionStatus` + the active `Preset`:

1. **Apps (allowlist during focus).** Enumerate the user's own session processes (`psutil`
   equivalent is `System.Diagnostics` / WMI; see the research note on *session* vs system
   processes — never touch system/other-user processes). Kill anything not in
   {preset `AllowedApps`} ∪ {always-allowed set} ∪ {Chrome} ∪ {terminals, iff the preset
   allows them}. Build blocklist mode first; an allowlist that mis-classifies a system
   process can freeze the session. Log each kill as a blocked attempt (see §4).
2. **Websites.** `SystemChanges.SetHostsBlocklist(globalDistractionList ∪ preset.BlockedDomains)`
   during focus; narrower or empty during breaks. The global list (YouTube, Reddit, Instagram,
   TikTok, X, Facebook, Netflix, Twitch) belongs in config/`settings.json`, not hardcoded.
3. **Chrome policies** via `SetRegistryValue` under
   `HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Google\Chrome`: `DnsOverHttpsMode=off` (so Secure DNS
   can't bypass the hosts file), `IncognitoModeAvailability=1`, `DeveloperToolsAvailability=2`.
   Close non-Chrome browsers during focus (they're just not-allowed apps — step 1 of this list).
4. **Admin-tool policies** while `AdminToolsBlocked`: `DisableTaskMgr`, `DisableCMD`,
   `DisableRegistryTools`, MMC snap-in restriction — per the research note's Tier-1 list. These
   are per-user policies under the user's hive (`HKEY_USERS\<SID>\...`); the service runs as
   SYSTEM, so resolve the interactive user's SID. **Re-apply on change** (the research note
   explains `RegNotifyChangeKeyValue` misses `RegRestoreKey`, so pair it with the 30 s timer the
   safety net already ticks on).
5. **Renew the lease** (`RenewLease`) every tick a lock is active, and let the existing restore
   path clear everything the moment the lock ends, the service stops, or the panic file appears.

Verify against the research note's **tiered table**: ship Tier 1 and the safe parts of Tier 2;
**skip Tier 3** (App Control/WDAC can brick boot; the Task-Manager debugger-redirect trick is
flagged by Defender).

### Keep the safety-first order
Do the enforcer in this sub-order so a bug can't lock you out mid-development:
dry-run logging first (it already defaults on) → one change type at a time, each with its
restore proven → only then flip `Enforcement:DryRun=false` on the test VM. The research note
says to test real enforcement in a Windows VM, with `restore.ps1` on a USB stick, before ever
running Live on the real machine. `restore.ps1` already knows how to undo registry + hosts
changes from the journal; if you add a new journalled change type, extend `restore.ps1` to
undo it too.

## 4. Recording blocked attempts (connects to stats, already built)

Stats (step 5) already reads events through `ISessionStore`. To make blocked attempts show up,
record them as events: add a kind constant to `SessionEventKinds`
(`src/BeProductive.Core/Sessions/SessionStore.cs`) such as `blocked-app`, call
`ISessionStore.RecordEvent(...)`, and surface the count in `StatsBuilder`
(`src/BeProductive.Core/Sessions/Stats.cs`) next to pauses/postpones. The CLI `bp stats` and the
stats window pick it up automatically from the `StatsSnapshot`. Same pattern for tamper attempts
in step 6. Don't invent a separate store.

## 5. Step 6 — turning on the real lock

This is the tamper-resistance layer. Again, everything goes through the safety net; the goal
(research note) is *maximum friction for an admin*, not the impossible goal of stopping one.

- **Service self-protection:** service security descriptor denying stop/delete/config +
  `sc failure` recovery actions. The installer (`scripts/install.ps1`) is where this is wired;
  it already creates the service, so extend it. PPL is **not** available (needs an ELAM driver).
- **UI watchdog:** the service relaunches the per-user app/gate if it's killed. A Windows
  service can't draw UI itself, so it starts the app in the interactive session (the research
  note covers this). The app is already a separate process started at sign-in by the installer.
- **Uninstall needs a recovery code:** an uninstall custom action that asks the service to
  verify a code (reuse `RecoveryCodeService.RedeemAsync`, same as `bp exit`). Don't add a second
  code system.
- **Log every tamper attempt** as an event (§4) so it shows in stats.
- **Leave Safe Mode as the escape hatch** — do *not* register the service to run in Safe Mode.

## 6. Must-not-break list

- Never bypass `SystemChanges` / `EnforcementGate`. No direct registry/hosts/process writes.
- Keep `DryRun` and the `PANIC` file working for every new kind of enforcement.
- Keep `RenewLease` called while locked, so a crash always self-heals within the lease.
- Never kill system or other-users' processes; only the interactive user's session.
- Never write Microsoft-owned domains to the hosts file.
- Keep OS-specific code behind the `BeProductive.Windows` interfaces; Core stays portable.
- Extend `restore.ps1` for any new journalled change type.
- Recovery codes and the "tighten now, loosen tomorrow" rule are settled — reuse, don't rebuild.

## 7. Map of what's already here

| Path | What it gives you |
|---|---|
| `SessionStatus` / `Restriction` | the per-tick decision you enforce (don't re-derive) |
| `SystemChanges` | the only sanctioned door to the machine (journalled, leased, reversible) |
| `EnforcementGate` | DryRun / Panic / Live mode — gate every change through it |
| `IRegistry` / `IHostsFile` / `IDnsCache` (`BeProductive.Windows`) | the OS primitives, already Windows-implemented |
| `IRulesStore.Find(presetId)` | the active preset's allowed apps, domains, terminal switches |
| `ISessionStore.RecordEvent` + `SessionEventKinds` | how blocked/tamper attempts reach stats |
| `StatsBuilder` | where to surface new counts |
| `RecoveryCodeService` | the exit mechanism to reuse for uninstall |
| `scripts/restore.ps1` | emergency undo; extend for new change types |
| `scripts/install.ps1` | where service hardening + the watchdog get wired |
| `docs/research/*` | the authoritative how-to for the Windows specifics |
