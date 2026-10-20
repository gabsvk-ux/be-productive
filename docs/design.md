# Be Productive — Design (v1)

A personal focus app for Windows that locks the laptop down to your work until you've done your daily focus time. Linux comes after the Windows version is complete.

Background research: [tech stack & browser extension](research/tech-stack-and-extension.md), [tamper resistance on an admin account](research/admin-tamper-resistance.md).

## 1. Scope

- **Users:** just me. No installer polish, code signing or onboarding in v1.
- **Platform:** Windows first. Linux later, sharing the code that isn't tied to one OS.
- **Account:** the daily account stays a **local administrator**. Full protection against an admin is impossible, so the goal is *maximum friction*: every bypass needs several deliberate, elevated steps (estimated 5–10 min for someone who knows how), far more than an impulse.

## 2. Daily flow

1. **Gate.** At the first login of the day, and on every login or wake from sleep until the minimum is done, a full-screen, always-on-top gate appears. You:
   - pick a **preset** (Uni, Projects, Programming, …),
   - plan the day's focus time (at least the minimum; you can plan more),
   - or **postpone** the start to a time (no later than 20:00). Postponing is limited to **3 per week**; the week resets Monday 04:00. With none left, the button is hidden.
2. **Focus blocks (pomodoro).** Choose the length before each block or during a break: **15/5, 25/5 or 45/15**.
   - **Pause** is allowed during focus. Restrictions stay on, the timer stops, and a pause lasts at most 10 min before focus resumes on its own.
   - Breaks **cannot** be paused.
   - **Long break** (default 20 min, adjustable) replaces the short break after every 4th block, but only if more focus time is planned after it.
3. **Breaks.** Everything is unlocked except the **hard blocklist** (empty in v1) and **admin tools** (see §5). When a break ends, a full-screen prompt you can't dismiss asks for the next block's length, and the lock comes back within 60 s.
4. **Switching preset** is allowed during breaks; it applies from the next block.
5. **Done.** Once the planned time is complete, the laptop is free. Time added after that is **soft**: you can stop it any time with no penalty, and it still counts in the stats.

### Rules

| Rule | Value |
|---|---|
| Daily minimum | **100 focus minutes** (1h40), configurable per weekday |
| What counts | Focus time only (not breaks or paused time). A block that started before the minimum is reached runs to its end |
| Day reset | 04:00 |
| Planned time | Binding, locked just like the minimum |
| Missed day | Logged, not carried over |
| Sleep or shutdown mid-block | Resume if you're back within 30 min; otherwise the block is closed and its focus time kept |
| Notifications | End of focus block: sound plus a Windows notification. End of break: sound plus a full-screen prompt to pick the next block |

## 3. Presets

A preset **is** the tracking category. Each preset has:
- **Allowed apps** (an allowlist; everything else is killed during focus)
- **Folder shortcuts** shown on the focus screen. These are shortcuts only; folders aren't enforced in v1.
- **Extra blocked domains**, added to the global distraction list
- **Default block length**

Each block can have an optional free-text note ("Calculus ch. 4").

**Always allowed** in every preset: the app itself, Settings, Calculator, the screenshot tool, the password manager, and a music app.
**Browsers:** only Chrome is allowed during focus. Edge, Firefox and every other browser are killed.

**Changing settings:** changes that tighten things apply right away. Changes that loosen things (allowing an app, removing a blocked domain, lowering the minimum) only apply **from the next day**.

## 4. Enforcement

- **Apps:** the service watches running processes (about every 1 s) and kills anything in your session that isn't allowed. It never touches system processes or other users' processes. Every kill is logged as a blocked attempt.
- **Websites:** whole domains are blocked in the hosts file (`0.0.0.0`) and the DNS cache is flushed. The global distraction list (YouTube, Reddit, Instagram, …) plus the preset's extras. Microsoft domains are never written (Defender resets those).
- **Chrome policies** (registry): Secure DNS (`DnsOverHttpsMode`) off, Incognito disabled, DevTools blocked.
- **Later (v2):** a Chrome extension (`declarativeNetRequest` + native messaging) for blocking or allowing individual paths and videos.

## 5. Tamper resistance (v1)

Admin tools (Task Manager, cmd, PowerShell, Terminal, regedit, Services/MMC) are blocked **all day until the minimum is done, breaks included**.

- Per-user policies `DisableTaskMgr`, `DisableCMD`, `DisableRegistryTools` and `RestrictToPermittedSnapins`, written into your account's registry settings by the service
- Re-applied when changed (change notification **plus** a 30 s timer) for the policies, the hosts file and the Chrome policies
- Service permissions deny stop/delete/config changes; recovery actions restart it after a kill
- The UI process is relaunched by the service if it's closed
- Uninstall requires a recovery code (a custom uninstall step that asks the service)
- Every tamper attempt is logged and shown in stats

**Skipped:** App Control/WDAC (one bad policy can stop Windows from booting), the debugger trick for redirecting Task Manager (malware use it), and protected processes (not available to normal apps).

## 6. Emergency exit — recovery codes

- At setup, the app generates ~10 **one-time codes**. Your friend writes them down or prints them, and the app stores **only hashes**.
- Using one ends the lock, logs the use and counts the day as failed.
- When **3 or fewer** are left, the app prompts you to make a new set. When the **last** one is used, it makes a new set automatically.
- New codes are **emailed to your friend** through a transactional email API, using a send-only key fixed to your friend's address. The codes are never shown on screen. Old codes stay valid until the email is confirmed sent; if sending fails, the app retries and warns you.
- **v2:** challenge-response. The PC shows a challenge, your friend signs it with a key only they have, and the PC stores only the matching public key.

## 7. Safety net (built before any enforcement)

- **Dry-run mode** (on by default) only logs what it *would* kill or block.
- **Panic file:** in development builds, creating `C:\ProgramData\BeProductive\PANIC` switches off live enforcement and undoes everything within 30 s.
- All policies are tied to a session with an expiry time and **always removed** when the service stops, a session expires or the app is uninstalled.
- A `restore.ps1` script, run as admin, undoes everything. Keep a copy on a USB stick.
- **Safe Mode** stays a working escape (the service isn't registered to run there).

## 8. Tracking

Stored locally in SQLite: focus time per category per day, notes, apps used during focus, blocked attempts, tamper attempts, recovery-code uses, postpones. The stats screen shows day, week, category and streaks.

## 9. Architecture

| Part | Technology |
|---|---|
| Background blocker | .NET 8+ Worker Service (`UseWindowsService`; `UseSystemd` later), running as LocalSystem |
| UI (gate, timer, presets, stats) | Avalonia (C#), a separate per-user process |
| UI ↔ service | Named pipes with access controls |
| Storage | SQLite |

OS-specific code (installing the service, launching the UI from the service, hosts file and DNS flush, telling system processes apart, hardening the gate window, browser policies) sits behind small interfaces, so Linux can be added without touching the main logic.

## 10. Build order

1. Recovery codes + dev mode + safety net
2. Service, timer and session state, SQLite
3. Gate screen + pomodoro UI
4. Enforcement: killing apps, then the hosts file + Chrome policies
5. Stats screen
6. Turning on the real lock: tamper measures, service restarts after kills, the gate coming back

## Later

- Linux port
- Chrome extension: path- and video-level allowing
- File Explorer watching to enforce folders
- Challenge-response recovery
- Hard blocklist contents
