# Tech stack and browser-extension research

Date: 2026-10-20. Scope: a personal focus/distraction-blocker. Windows first, Linux later.

**Method note.** In this environment the page-fetch tool couldn't resolve hostnames, so every claim below comes from official-domain search results (Microsoft Learn, Chrome for Developers, chromeenterprise.google, support.google.com, chromium.googlesource.com, tauri.app, docs.avaloniaui.net, electronjs.org, doc.qt.io, Mozilla docs). The full pages were not read. Claims marked **[inference]** are my reasoning, not something a source states. Before relying on an item marked **[verify]**, open the linked page and check it.

**Assumptions about the developer.** Modest general programming skill, comfortable at the Python/JS level, no Rust, no C#. C# is a statically typed, GC-managed language. For someone coming from Python or JS it is a smaller jump than Rust **[inference]**.

---

## 1. Summary and recommendation

**Recommended stack: C# / .NET 8+ for everything, with a Worker Service for the privileged service and Avalonia for the UI.**

- The two hardest parts are the service and process enforcement. On Windows these have first-party, in-box .NET support (`AddWindowsService`, `System.Diagnostics.Process`, named pipes). The same host also supports systemd through `UseSystemd`, so the service code moves to Linux with little change.
- Avalonia keeps the UI in C#. It runs on Windows and Linux and supports `Topmost` and `WindowState.FullScreen`. That avoids a UI rewrite later at almost no extra cost over WPF.
- Tauri requires Rust for its core. Electron and Python have no first-party Windows-service story. Both add a second language or third-party glue exactly where the work is hardest.
- **Cross-platform vs. Windows-first rewrite.** One codebase is *not* meaningfully harder if the OS-specific parts sit behind small interfaces from day one. Roughly 60-70% of the code (timer and session logic, presets, rules, SQLite, stats, UI, IPC contract) is shareable. The OS-specific remainder (about 30-40%) has to be written per OS whatever stack is chosen **[inference: estimate by component, see §3]**. Build and ship Windows only, but keep the Linux seams.
- **Browser extension: not in v1. Plan for it in v2.** In v1, use hosts-file blocking plus Chrome policies set in the registry (`DnsOverHttpsMode=off`, `IncognitoModeAvailability`, `DeveloperToolsAvailability`). Also allowlist only one browser during focus sessions. Path-level blocking (`youtube.com/shorts`, per-video allow) needs an extension. Making that extension tamper-resistant on a non-domain-joined PC needs a Chrome Web Store listing or Chrome Enterprise Core enrollment.

**Top risks:** Session 0 isolation, meaning the service can't show the gate UI itself. Microsoft Defender flagging hosts edits. The gate window being bypassable (Task Manager, a second desktop, Wayland later). Self-lockout through a bug, so the recovery password must be designed first.

---

## 2. Stack comparison

| | (a) .NET + WPF/WinUI | (b) .NET + Avalonia | (c) Tauri | (d) Electron | (e) Python + PySide6 |
|---|---|---|---|---|---|
| Languages | C# | C# | Rust (core) + JS/TS UI (+ sidecar language) | JS/TS | Python |
| Windows Service | First-party (`AddWindowsService`) | Same | Rust code, or sidecar in another language | No first-party support | No first-party support (third-party pywin32) |
| systemd later | First-party (`UseSystemd`) | Same | Same as above | Same as above | Same as above |
| Topmost full-screen gate | Yes (Windows) | `Topmost` + `FullScreen` on Win/macOS/Linux | Yes, via window API | `kiosk`, `setAlwaysOnTop(…, 'screen-saver')`; always-on-top not on Wayland | `WindowStaysOnTopHint` (a hint; some X11 window managers ignore it) |
| Linux UI | Rewrite | Same code | Same code (WebKitGTK) | Same code | Same code |
| Beginner friction | Low | Low-medium | High (Rust) | Medium (service and native glue) | Medium (packaging, service glue) |

### (a) / (b) .NET Worker Service + WPF or Avalonia
- **Service.** The `Microsoft.Extensions.Hosting.WindowsServices` package adds `AddWindowsService`. It only activates when the process runs as a Windows Service ([MS Learn: Windows Service with BackgroundService](https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service), [API ref](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.windowsservicelifetimehostbuilderextensions.addwindowsservice?view=net-10.0-pp)). `UseSystemd` (package `Microsoft.Extensions.Hosting.Systemd`) sets `SystemdLifetime`, sends start/stop notifications (the unit needs `Type=notify`), and is likewise a no-op outside systemd ([API ref](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.systemdhostbuilderextensions.usesystemd?view=net-9.0-pp), [Worker Services](https://learn.microsoft.com/en-us/dotnet/core/extensions/workers)). You can call both on one host.
- **Surviving kills and reboots.** `sc failure <svc> reset= … actions= restart/5000` configures SCM recovery actions. It wraps `ChangeServiceConfig2(SERVICE_CONFIG_FAILURE_ACTIONS)` ([sc failure](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2012-r2-and-2012/cc742019(v=ws.11)), [ChangeServiceConfig2](https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-changeserviceconfig2a)). Microsoft's guidance for critical services is to restart after 1 min, restart after 2 min, then reboot, with a 300 s reset ([Guidelines for Services](https://learn.microsoft.com/en-us/windows/win32/rstmgr/guidelines-for-services)). An administrator can still stop and disable the service. Real tamper resistance against yourself only works if you use a standard (non-admin) daily account **[inference]**.
- **Process APIs.** `System.Diagnostics.Process` covers enumeration, `SessionId`, and `Kill` ([Process class](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process)) **[verify]**. To avoid killing OS processes, enforce only on processes in the interactive user's session, never session 0, and keep a protected-name list **[inference]**.
- **IPC.** `NamedPipeServerStream` and `NamedPipeClientStream`, with an ACL set through `PipeSecurity` / `NamedPipeServerStreamAcl.Create`. Custom ACLs need `CurrentUserOnly=false`. Don't grant `CreateNewInstance` to clients ([named pipes how-to](https://learn.microsoft.com/en-us/dotnet/standard/io/how-to-use-named-pipes-for-network-interprocess-communication), [gRPC over named pipes](https://learn.microsoft.com/en-us/aspnet/core/grpc/interprocess-namedpipes?view=aspnetcore-9.0)). On Linux, the same `System.IO.Pipes` API runs over Unix domain sockets **[verify]**.
- **Avalonia.** Supports Windows, macOS and Linux and needs .NET 8+ for desktop ([Supported platforms](https://docs.avaloniaui.net/docs/supported-platforms)). `Topmost` is listed as supported on all three desktop OSes, and `WindowState` includes `FullScreen` ([Window management](https://docs.avaloniaui.net/docs/app-development/window-management), [WindowState](https://docs.avaloniaui.net/api/avalonia/controls/windowstate)). WPF/WinUI are Windows-only ([Avalonia vs WinUI](https://avaloniaui.net/winui-compare)). Choosing WPF would mean rewriting the UI for Linux.

### (c) Tauri
- The app core is Rust. Sidecars (`bundle.externalBin` in `tauri.conf.json`, binaries suffixed with the target triple) let you bundle and launch an external executable in any language, including a Node sidecar ([Embedding External Binaries](https://v2.tauri.app/develop/sidecar/), [Node.js as a sidecar](https://v2.tauri.app/learn/sidecar-nodejs/)). A sidecar is a child process the app spawns through the shell plugin, which needs `shell:allow-execute`/`allow-spawn` permissions. It is **not** a Windows Service, so the privileged service would have to be a separate program installed independently **[inference]**.
- **How much Rust?** You could keep Rust down to the scaffolded `main.rs`, do all logic in JS, and write the service in C# or Python. At that point Tauri adds a third toolchain (Rust + JS + service language) to save writing UI in C# **[inference]**. That is poor fit for a beginner.

### (d) Electron
- Good gate-window support. Use the `kiosk` option or `setKiosk`, plus `setAlwaysOnTop(true, 'screen-saver')`, which sits above the Windows taskbar. Always-on-top is **not supported on Wayland** ([BrowserWindow](https://www.electronjs.org/docs/latest/api/browser-window/)).
- No first-party service host. The privileged part would need a separate Node service through third-party wrappers, or a different language **[inference]**. Large runtime footprint.

### (e) Python + PySide6
- Packaging uses `pyside6-deploy`, a Nuitka wrapper that produces a standalone folder by default ([pyside6-deploy](https://doc.qt.io/qtforpython-6/deployment/deployment-pyside6-deploy.html)). On Windows it needs `dumpbin` from Visual Studio ([release notes](https://doc.qt.io/qtforpython-6/release_notes/pyside6_release_notes.html)).
- `Qt::WindowStaysOnTopHint` is only a *request* to the window system. Some X11 window managers ignore it, and Qt suggests calling `raise()` periodically as a workaround ([Qt namespace](https://doc.qt.io/qt-6/qt.html), [QSplashScreen](https://doc.qt.io/qtforpython-6/PySide6/QtWidgets/QSplashScreen.html)).
- The Windows Service needs third-party pywin32 and freezing a service executable. That is the most fragile part of this option **[inference]**. The language is the most familiar, but the glue is the weakest.

---

## 3. One cross-platform codebase vs. Windows-first then rewrite

**Inherently OS-specific (written per OS whatever the stack):**

| Component | Windows | Linux | Source |
|---|---|---|---|
| Service install, recovery | SCM, `sc create` / `sc failure` | systemd unit, `Restart=`, `Type=notify` | [sc failure](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2012-r2-and-2012/cc742019(v=ws.11)), [UseSystemd](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.systemdhostbuilderextensions.usesystemd?view=net-9.0-pp) |
| Launching UI from the service | Session 0 isolation: the service can't show UI. It must use `WTSQueryUserToken` + `CreateProcessAsUser` (LocalSystem), or a per-user autostart agent | per-user autostart or systemd user unit **[inference]** | [Launching interactive process from service](https://learn.microsoft.com/en-us/archive/blogs/winsdk/launching-an-interactive-process-from-windows-service-in-windows-vista-and-later) |
| Hosts file + DNS flush | `%SystemRoot%\System32\drivers\etc\hosts` (Defender may flag blocking entries) | `/etc/hosts`; the flush step depends on the resolver | [Defender hosts detection](https://support.microsoft.com/en-us/topic/hosts-file-is-detected-as-malware-in-windows-defender-4320fa8b-0d54-1129-db85-61f095144521) |
| Process identity / "is OS process" | session ID, system paths | UID, cgroup **[inference]** | [Process](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process) |
| Gate window hardening | topmost + suppress task switching | X11 OK; Wayland compositors may refuse always-on-top | [Electron note on Wayland](https://www.electronjs.org/docs/latest/api/browser-window/) |
| Browser policy | registry `HKLM\SOFTWARE\Policies\Google\Chrome` | `/etc/opt/chrome/policies/managed` **[verify]** | [Registry policies](https://support.google.com/chrome/a/answer/9131254?hl=en) |

**Shareable:** the Pomodoro/session state machine (15/5, 25/5, 45/15, the 10-minute pause rule, no pausing during breaks), presets, the daily-minimum and 3-per-week postpone rules, allowlist/blocklist evaluation, the SQLite schema, stats, the IPC message contract, and the whole UI if it is in Avalonia.

**Verdict.** The OS-specific list is identical in both approaches, so a later rewrite saves nothing on it. The rewrite approach adds re-doing the UI (WPF to something else) and usually untangling Windows calls spread through the logic. With .NET + Avalonia the extra cost now is about writing four small interfaces (`IServiceHost` glue, `IHostsFile`, `IProcessEnforcer`, `ISessionLauncher`) and keeping Win32 calls behind them **[inference]**.

---

## 4. Desktop-only vs. desktop + Chrome extension

### Hosts-file limits
- The hosts file matches **whole hostnames only**. No paths, no wildcards, so `youtube.com/shorts` can't be blocked and `*.reddit.com` has to be listed host by host **[inference: hosts format]**.
- **Chrome Secure DNS.** Chromium has two resolvers. The *system resolver* (`getaddrinfo`) lets the OS consult the hosts file. The *built-in async resolver* is used when Secure DNS is not OFF and has its own HOSTS parser (`dns_hosts.h`). `HostResolverManager` checks HOSTS before running DnsClient queries ([net/dns README](https://chromium.googlesource.com/chromium/src/+/main/net/dns/README.md), [host_resolver_manager.h](https://chromium.googlesource.com/chromium/src/+/master/net/dns/host_resolver_manager.h), [dns_hosts.h](https://chromium.googlesource.com/chromium/src/+/refs/heads/main/net/dns/dns_hosts.h)). Chrome's DoH probably still honours hosts entries, but the search results didn't state it outright **[verify]**. Belt and braces: set `DnsOverHttpsMode = off` ([policy](https://chromeenterprise.google/intl/en_uk/policies/dns-over-https-mode/)). That page also says that when the policy is unset, *managed* devices send no DoH queries.
- **Firefox TRR.** With `network.trr.mode` 3 (TRR-only), the system resolver, and so the hosts file, is bypassed unless hosts are listed in `network.trr.excluded-domains`. The `DNSOverHTTPS` enterprise policy maps Enabled/Fallback to modes 2, 3 and 5 and can lock DoH off ([Firefox TRR docs](https://firefox-source-docs.mozilla.org/networking/dns/dns-over-https-trr.html), [Mozilla wiki TRR](https://wiki.mozilla.org/Trusted_Recursive_Resolver), [DNSOverHTTPS policy](https://firefox-admin-docs.mozilla.org/reference/policies/dnsoverhttps/)). Simplest v1 approach: don't allowlist Firefox during focus sessions **[inference]**.
- **Defender.** Microsoft Defender's `SettingsModifier:Win32/HostsFileHijack` detection targets suspicious hosts edits and can reset the file. Microsoft's fix for intentional edits is a Defender exclusion ([threat description](https://www.microsoft.com/en-us/wdsi/threats/malware-encyclopedia-description?name=SettingsModifier%3AWin32%2FHostsFileHijack&threatid=265754), [support article](https://support.microsoft.com/en-us/topic/hosts-file-is-detected-as-malware-in-windows-defender-4320fa8b-0d54-1129-db85-61f095144521)). Test with the real blocklist early.

### Extension capabilities
- `declarativeNetRequest` (MV3) supports URL-filter and regex rules with block, allow, redirect and upgradeScheme actions. Dynamic rules persist across sessions. Limits: 30,000 "safe" dynamic rules (Chrome 121+), 5,000 unsafe/redirect rules, and 1,000 regex rules. Redirects need `declarativeNetRequestWithHostAccess` ([DNR API](https://developer.chrome.com/docs/extensions/reference/api/declarativeNetRequest)). That covers blocking `youtube.com/shorts/*` and per-video allow rules (a higher-priority `allow` for `watch?v=<id>`) **[inference from API]**.
- **Native messaging.** The host manifest (`type: "stdio"`, `allowed_origins` with no wildcards) is registered at `HKLM` or `HKCU\SOFTWARE\Google\Chrome\NativeMessagingHosts\<name>`, and the extension needs the `nativeMessaging` permission ([Native messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging)). Chrome launches the host as a child process. Make that host a thin relay to the service's named pipe **[inference]**.

### Tamper resistance via Chrome policy (Windows)
- Policies can be set in the registry on machines that aren't domain-joined ([Manage Chrome policies with Windows registry](https://support.google.com/chrome/a/answer/9131254?hl=en)). The key is `HKLM\Software\Policies\Google\Chrome\…`.
- **`ExtensionInstallForcelist`.** Entries are `extensionID;updateURL`. Extensions from outside the Chrome Web Store can be force-installed only if the machine is joined to Active Directory or Azure AD, or enrolled in Chrome Enterprise Core ([policy page](https://chromeenterprise.google/policies/extension-install-forcelist/), [Windows extension policies](https://support.google.com/chrome/a/answer/7532015?hl=en)). On an unmanaged personal PC, a self-hosted CRX will **not** force-install. The options:
  1. Publish to the Chrome Web Store, possibly as *unlisted*. Unlisted items are still Web Store items, so the forcelist should accept them **[inference, verify]**.
  2. Enroll the browser in Chrome Enterprise Core with `CloudManagementEnrollmentToken`. This needs a Google Admin console and acceptance of the Chrome Enterprise Core license ([enroll browsers](https://support.google.com/chrome/a/answer/9301891?hl=en), [token policy](https://chromeenterprise.google/intl/en_uk/policies/cloud-management-enrollment-token/)).
- **`IncognitoModeAvailability`.** Can disable Incognito, which closes the obvious extension bypass ([policy](https://chromeenterprise.google/policies/incognito-mode-availability/)).
- **`DeveloperToolsAvailability`.** Value 2 blocks DevTools. Value 0, the default, already blocks DevTools on policy-installed extensions ([policy](https://chromeenterprise.google/policies/developer-tools-availability/)). Also consider `ExtensionDeveloperModeSettings` ([policy](https://chromeenterprise.google/policies/extension-developer-mode-settings/)).
- Any of these policies are plain registry values. An admin user can delete them, so they hold only as long as the daily account is non-admin and the service re-applies them **[inference]**.

### v1 vs. later
- **v1 (desktop only):** hosts blocking, DNS flush, and the service re-writing hosts and Chrome policy keys if they're changed. Set `DnsOverHttpsMode=off`, `IncognitoModeAvailability=disabled` and `DeveloperToolsAvailability=2`. During focus sessions, the process allowlist permits only Chrome as a browser.
- **v2:** a DNR extension for path-level and per-video rules, a native-messaging relay to the service, and Chrome Web Store (unlisted) publication so `ExtensionInstallForcelist` works on an unmanaged PC.

---

## 5. Final recommendation and risks

**Build it in .NET 8+ C#: a Worker Service (`AddWindowsService` + `UseSystemd`), an Avalonia UI, named-pipe IPC, and SQLite.** Ship Windows only, with OS code behind interfaces. Leave the Chrome extension to v2.

Rationale: one beginner-learnable language, and first-party support for the riskiest pieces (service lifetime, recovery, process control, pipe ACLs). The same host already supports systemd, and the Avalonia UI carries over to Linux unchanged. Tauri's Rust core and Electron/Python's missing service host each add friction exactly where a beginner is least likely to succeed.

**Risks**
1. **Session 0.** The service can't draw the gate itself. You need a per-user UI agent that the service relaunches when it's killed, or `CreateProcessAsUser` from LocalSystem ([MS blog](https://learn.microsoft.com/en-us/archive/blogs/winsdk/launching-an-interactive-process-from-windows-service-in-windows-vista-and-later)).
2. **Self-tamper.** Any admin can stop the service, edit hosts or delete policies. Use a standard user account, keep the recovery password outside the service's normal path, and record tampering events.
3. **Lockout bugs.** Killing the wrong process, or a gate that never closes, can make the PC unusable. Ship a tested recovery-password exit and a safe-mode or uninstall path first.
4. **Defender** may revert hosts edits ([threat description](https://www.microsoft.com/en-us/wdsi/threats/malware-encyclopedia-description?name=SettingsModifier%3AWin32%2FHostsFileHijack&threatid=265754)).
5. **Gate bypass.** Ctrl+Alt+Del / Task Manager can't be fully blocked from user mode **[inference]**. The service should re-show the gate and log the bypass.
6. **Linux/Wayland.** Always-on-top may not be honoured ([Electron BrowserWindow](https://www.electronjs.org/docs/latest/api/browser-window/)), and the gate may need compositor-specific work. Scope this at the Linux phase.
7. **Extension forcing on an unmanaged PC** depends on Web Store publication or Chrome Enterprise Core ([ExtensionInstallForcelist](https://chromeenterprise.google/policies/extension-install-forcelist/)).
