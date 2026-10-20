# Be Productive

A personal focus app for Windows: until you've done your daily focus time, the laptop is limited to the apps and sites you need for work. See [docs/design.md](docs/design.md) for the full design.

**Status: step 1 of the [build order](docs/design.md#10-build-order).** So far there are recovery codes, dry-run mode and the safety net. Nothing is blocked yet.

## Layout

| Path | What it is |
|---|---|
| `src/BeProductive.Core` | Everything not tied to one OS: recovery codes, the enforcement switch, the change journal and restore |
| `src/BeProductive.Windows` | Windows implementations (registry, hosts file, DNS flush) |
| `src/BeProductive.Service` | The background service (a Windows Service) |
| `src/BeProductive.Cli` | `bp`, a command-line tool for codes, status and restore until the UI exists |
| `tests/BeProductive.Core.Tests` | Unit tests |
| `scripts/restore.ps1` | Emergency undo. Keep a copy on a USB stick |
| `scripts/install-service.ps1` | Dev helper to install the service |

## Getting started (Windows)

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Build and test:
   ```powershell
   dotnet build
   dotnet test
   ```
3. Run the service in a console window (Ctrl+C stops it):
   ```powershell
   dotnet run --project src/BeProductive.Service
   ```
   Or install it as a real Windows service from an admin terminal: `.\scripts\install-service.ps1`.
4. Use the CLI (from an admin terminal, because the state lives in `C:\ProgramData\BeProductive`):
   ```powershell
   dotnet run --project src/BeProductive.Cli -- status
   dotnet run --project src/BeProductive.Cli -- codes init
   ```

## Safety first

The app makes changes that could lock you out of your own PC, so every one of them goes through these safeguards:

- **Dry run is on by default.** `Enforcement:DryRun` is `true`, so the service only *logs* what it would block. Turn it off only when testing enforcement on purpose.
- **Panic file.** While `Enforcement:PanicFileEnabled` is `true`, creating `C:\ProgramData\BeProductive\PANIC` switches live enforcement off, and everything applied is undone within 30 s.
- **Change journal.** Every change is written to `C:\ProgramData\BeProductive\journal.json` *before* it's made, together with what was there before. The service undoes everything when it stops, at its next start after a crash, and whenever a change's lease (15 min by default) runs out without being renewed.
- **`bp restore`** undoes everything listed in the journal.
- **`scripts/restore.ps1`** is the last resort. Run it as admin and it disables and stops the service, undoes the journal, cleans the hosts file and flushes DNS:
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\scripts\restore.ps1
  ```
- **Safe Mode** always works: the service doesn't run there.

## Recovery codes

- `bp codes init` creates the first 10 codes and shows them **once**, for your friend to write down. The PC keeps only hashes.
- `bp codes redeem <code>` uses one. Each works once.
- With 3 or fewer left you're offered a new set (`bp codes regenerate`). Using the last one sends a new set automatically, and the service keeps retrying every 5 minutes until it goes through.
- New sets are emailed to your friend and never shown on screen. The old codes keep working until the email is confirmed sent.

Email goes through [Resend](https://resend.com). Copy [docs/settings.example.json](docs/settings.example.json) to `C:\ProgramData\BeProductive\settings.json` and fill in:
- **`ApiKey`:** create a *sending-only* key.
- **`From`:** an address on a domain you've verified with Resend.
- **`To`:** your friend's address.
