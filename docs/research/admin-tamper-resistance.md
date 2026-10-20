# Tamper resistance against a local administrator

Date: 2026-10-20. Scope: making the Windows focus blocker hard to bypass on impulse when the daily account is a **local administrator**, with one deliberate exit: one-time recovery codes held by a friend. Stack (decided): .NET Worker Service as LocalSystem, Avalonia per-user UI, SQLite, named pipes.

**Method note.** As before, the page-fetch tool couldn't resolve hostnames, so every claim comes from official-domain search results (learn.microsoft.com, microsoft.com/wdsi, support.microsoft.com, chromeenterprise.google, datatracker.ietf.org, attack.mitre.org, and the vendors' own sites). The full pages were not read. **[inference]** marks my reasoning, not a source's statement. **[verify]** marks a point to check on the linked page or by testing before you rely on it.

---

## 1. Summary

- **An administrator always wins.** Admins hold `SeTakeOwnershipPrivilege` over "any securable object", services, registry keys, files and processes included ([Take ownership](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2012-r2-and-2012/dn221976(v=ws.11))). `SeDebugPrivilege` grants process access "even if the security descriptor does not allow it" ([OpenProcess](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openprocess)). Everything below adds friction. None of it is a wall.
- **The useful friction comes from UAC.** Every real bypass (taking ownership, `SeDebugPrivilege`, editing HKLM, stopping services) needs an *elevated* token. The user's normal, non-elevated session lacks these privileges **[inference: standard UAC split-token behaviour]**. So most measures turn "one click" into "elevate, know the right tool, run several commands", which is the stated goal.
- **Task Manager:** set `DisableTaskMgr=1` in the user's hive (`HKEY_USERS\<SID>`) from the service during focus. It is a supported per-user policy on Windows 10 2004+ and Windows 11 ([ADMX_CtrlAltDel CSP](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-ctrlaltdel)). Watch the key and re-apply on change. Skip the IFEO debugger hijack: it is a catalogued malware technique ([MITRE T1546.012](https://attack.mitre.org/techniques/T1546/012/)).
- **Protected Process Light is not available** to us. It needs an ELAM driver, which requires Microsoft Virus Initiative membership ([Protecting anti-malware services](https://learn.microsoft.com/en-us/windows/win32/services/protecting-anti-malware-services-), [ELAM overview](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/early-launch-antimalware)).
- **Recovery codes:** store only hashes and never show plaintext on this machine. **Do not use TOTP verified locally.** TOTP is symmetric, so the PC would have to hold the same seed as the friend, and an admin can read it **[inference]**. If codes must regenerate without the user seeing them, the clean design is asymmetric: the friend holds a private key, and the PC stores only the public key (§9).
- **Cold Turkey, Freedom and FocusMe** all rely on "uninstaller refuses while locked" plus a watchdog. None of them claims to defeat a determined admin (§8).

---

## 2. Findings by question

### 2.1 Blocking Task Manager
- **Policy.** "Remove Task Manager" (User Configuration > System > Ctrl+Alt+Del Options) maps to `DisableTaskMgr` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System`. When it is enabled, starting Task Manager shows a "policy prevents this" message ([ADMX_CtrlAltDel CSP](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-ctrlaltdel), [troubleshooting article](https://learn.microsoft.com/en-us/troubleshoot/windows-server/system-management-components/task-manager-disabled-by-administrator)). It is user-scoped and also blocks admins, which one admin reported in a [Q&A thread](https://learn.microsoft.com/en-us/archive/msdn-technet-forums/35dd3cd6-cc67-476f-82e2-058293e6f657).
- **Immediacy.** Task Manager checks the value at launch, so a new value applies to the next Ctrl+Shift+Esc without logoff. An already-open Task Manager window keeps running, so the service should also close it **[inference, verify]**. Whether the Ctrl+Alt+Del screen hides or greys out the "Task Manager" entry on Windows 11 is not stated in the results **[verify by test]**.
- **Writing it from LocalSystem.** While the user is logged on, their hive is loaded under `HKEY_USERS\<SID>`. LocalSystem can open `HKU\<SID>\Software\...\Policies\System` and write there **[inference: standard registry access; LocalSystem has full control of user hives]**. Get the SID from the session's token (`WTSQueryUserToken`).
- **Admin bypass.** Delete the value with regedit, reg.exe or PowerShell (no elevation needed, because HKCU belongs to the user). Or use any third-party process tool, or `taskkill`. That is why re-applying it on change (§2.5) matters.
- **DisallowRun.** Applies only to programs started by File Explorer. Microsoft notes that the command prompt is not affected, and matching is by file name, so a renamed copy runs ([DisallowRun](https://admx.help/?Category=Windows_10_2016&Policy=Microsoft.Policies.WindowsTools::DisallowApps)). Weak.
- **IFEO `Debugger` for taskmgr.exe** (`HKLM\...\Image File Execution Options\taskmgr.exe`). This is how Process Explorer's "Replace Task Manager" works ([Q&A](https://learn.microsoft.com/en-us/answers/questions/4042289/process-explorer-not-replacing-task-manager-any-mo)). It is MITRE T1546.012: "malware can register invalid debuggers that redirect and effectively disable" system tools ([MITRE](https://attack.mitre.org/techniques/T1546/012/), [detection DET0422](https://attack.mitre.org/detectionstrategies/DET0422/)). The results don't confirm whether Defender detects or reverts it **[verify]**. Security tools may delete it ([Q&A](https://learn.microsoft.com/en-us/answers/questions/724285/process-explorer-will-not-revert-back-to-task-mana)). If the service dies, Task Manager stays broken until the value is removed. It is also exactly the behaviour that gets a binary flagged. **Skip.**

### 2.2 Restricting admin tools
- **`DisableCMD`** ("Prevent access to the command prompt") can also block .cmd/.bat files. Microsoft warns against this where logon/startup scripts run ([ShellCommandPromptRegEditTools CSP](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-shellcommandpromptregedittools)). The registry location is `HKCU\Software\Policies\Microsoft\Windows\System`. Value 2 reportedly blocks the prompt but allows scripts **[verify]**.
- **`DisableRegistryTools`** ("Prevent access to registry editing tools") blocks regedit.exe ([same CSP](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-shellcommandpromptregedittools)). Whether reg.exe honours it is **[verify]**.
- **PowerShell and Windows Terminal** are not covered by either policy **[inference]**. PowerShell can delete every value above in one line. Blocking it needs DisallowRun (weak) or App Control.
- **MMC.** `RestrictToPermittedSnapins` (`HKCU\Software\Policies\Microsoft\MMC`) blocks all snap-ins that aren't explicitly permitted, which covers services.msc and gpedit ([ADMX_MMC CSP](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-mmc), [admx.help](https://admx.help/?Category=Windows_10_2016&Policy=Microsoft.Policies.ManagementConsole::MMC_Restrict_To_Permitted_Snapins)). One tester reported it did not block ([Q&A](https://learn.microsoft.com/en-us/answers/questions/1418043/how-to-block-all-the-msc-commands-windows-in-windo)) **[verify]**.
- **App Control for Business (WDAC)** policies "are effective on all editions", Home included, though the cmdlets aren't available on Home ([feature availability](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/feature-availability)). **AppLocker** no longer needs a specific edition on Windows 10 2004+ and Windows 11 (KB5024351), but it gets only security fixes ([AppLocker requirements](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/applocker/requirements-to-use-applocker)). An admin can remove an unsigned policy. Signed App Control policies resist removal, but a mistake can make the machine unbootable **[verify, inference]**. Too heavy for v1.

### 2.3 Service self-protection
- **Service DACL.** `SetServiceObjectSecurity` / `sc sdset` sets the service's descriptor in SDDL ([API](https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-setserviceobjectsecurity), [sc sdset](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2012-r2-and-2012/cc742037(v=ws.11)), [access rights](https://learn.microsoft.com/en-us/windows/win32/services/service-security-and-access-rights)). Denying `SERVICE_STOP`, `DELETE`, `SERVICE_CHANGE_CONFIG` and `WRITE_DAC` to `BA` makes `sc stop` and services.msc fail.
- **Bypass.** An elevated admin can take ownership and rewrite the DACL ([Taking ownership in C++](https://learn.microsoft.com/en-us/windows/win32/secauthz/taking-object-ownership-in-c--?redirectedfrom=MSDN)). They can also edit `HKLM\SYSTEM\CurrentControlSet\Services\<name>` (`Start=4`) and reboot. Moderate friction, but cheap to add.
- **Recovery actions.** `sc failure <svc> reset= … actions= restart/…` allows up to three actions ([sc failure](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2012-r2-and-2012/cc742019(v=ws.11))). They fire only on abnormal exit. `sc failureflag 1` extends them to stops that report an error ([failureflag](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2012-R2-and-2012/cc742011(v=ws.11)?redirectedfrom=MSDN)). A process kill counts as abnormal, so the SCM restarts the service **[inference, verify]**.
- **Watchdog pair.** Two services that restart each other, plus recovery actions on both. The pair survives a single kill. `taskkill /f /im a.exe /im b.exe` kills both at once, but then recovery actions bring them back **[inference]**. The UI agent is relaunched by the service in the user's session via `CreateProcessAsUser` (see the tech-stack doc).
- **PPL.** Not available to us: it requires an MVI-member ELAM driver ([Protecting anti-malware services](https://learn.microsoft.com/en-us/windows/win32/services/protecting-anti-malware-services-), [ELAM](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/early-launch-antimalware)).
- **Process DACL** denying `PROCESS_TERMINATE` to Administrators stops non-elevated killers. An elevated process with `SeDebugPrivilege` enabled bypasses it ([OpenProcess](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openprocess), [Privilege constants](https://learn.microsoft.com/en-us/windows/win32/secauthz/privilege-constants)). Cheap. Apply it to the UI agent too.

### 2.4 Hosts file
- **Defender.** It "blocks any attempt to modify the hosts file to insert entries for specific, protected domains" and resets the file on remediation ([HostsFileHijack](https://www.microsoft.com/en-us/wdsi/threats/malware-encyclopedia-description?name=SettingsModifier%3AWin32%2FHostsFileHijack&threatid=265754), [support article](https://support.microsoft.com/en-us/topic/hosts-file-is-detected-as-malware-in-windows-defender-4320fa8b-0d54-1129-db85-61f095144521)). Users blocking telemetry have been flagged ([Q&A](https://learn.microsoft.com/en-us/answers/questions/3758720/settingsmodifier-win32-hostsfilehijack?forum=windows-all)). Rules:
  - Never write Microsoft or OS domains.
  - Keep our entries in a marked block.
  - Don't ask the user to exclude hosts from Defender.
  - Expect an occasional reset and re-apply after it **[inference]**.
- **Re-writing.** Re-write on `FileSystemWatcher` events, plus a periodic hash check, because watchers can miss events **[inference]**. The hosts file can be ACL'd so only SYSTEM can write it. An admin can take ownership ([Take ownership](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2012-r2-and-2012/dn221976(v=ws.11))). A wrong ACL could also break other software, so restore the default ACL on session end **[inference]**.

### 2.5 Re-applying registry policies
- **Notification.** `RegNotifyChangeKeyValue` reports value, subkey, attribute and security-descriptor changes, and key deletion ([API](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regnotifychangekeyvalue)). It fires **once per call** and must be re-armed. It does **not** see changes made with `RegRestoreKey`, so also re-check on a timer.
- **What to watch.** `HKLM\Software\Policies\Google\Chrome` (`IncognitoModeAvailability`, `DeveloperToolsAvailability=2`, `DnsOverHttpsMode`; see [Incognito](https://chromeenterprise.google/policies/incognito-mode-availability/) and [DevTools](https://chromeenterprise.google/intl/en_ca/policies/developer-tools-availability/)), and `HKU\<SID>\…\Policies\System` and `\Explorer`.
- **On a change:** re-apply, log a tamper event, and optionally extend the session.

### 2.6 Safe Mode
- **SafeBoot keys.** Safe Mode loads only the services listed under `HKLM\SYSTEM\CurrentControlSet\Control\SafeBoot\Minimal` and `\Network` ([BcdLibrary_SafeBoot](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/bcd/bcdlibrary-safeboot), [Not boot in normal mode](https://learn.microsoft.com/en-us/troubleshoot/windows-server/performance/not-boot-in-normal-mode-driver-issue)). Adding our service there is possible but undocumented for third parties **[verify]**. An admin can't password-protect Safe Mode ([Q&A](https://learn.microsoft.com/en-us/answers/questions/b2423161-06b8-46ab-8054-2df7228b0d80/is-it-possible-to-disable-or-password-protect-safe?forum=windows-all)).
- **Is it a bypass?** Yes. But it needs a reboot and several menu steps, which is already well above impulse level.
- **Recommendation.** Don't register for Safe Mode, and keep it as the documented last-resort escape. On the next normal boot, detect that the previous boot was Safe Mode (or that policies were removed offline), log it, and show it in stats **[inference]**.

### 2.7 Uninstall protection
- **ARP properties are cosmetic.** `ARPNOREMOVE` hides the Remove button, but `msiexec /x` still works ([ARPNOREMOVE](https://learn.microsoft.com/en-us/windows/win32/msi/arpnoremove), [Configuring ARP](https://learn.microsoft.com/en-us/windows/win32/msi/configuring-add-remove-programs-with-windows-installer)).
- **What actually gates uninstall:**
  1. An uninstall custom action asks the service over the pipe, and fails unless no session is active or a recovery code is entered. This mirrors Cold Turkey's "uninstaller fails when you click Next".
  2. The service DACL denies `DELETE`.
  3. The install directory is ACL'd read-only for Administrators.
- **Bypass.** All three are bypassed by ownership plus manual deletion **[inference]**.
- **The uninstall action must always restore policies, hosts and ACLs** (§2.10).

### 2.8 Commercial products (vendor docs only)
- **Cold Turkey.** Locking a block "will make the uninstaller fail when you click 'Next'". The vendor admits that "limitations in the operating system" prevent apps from being truly hard to delete ([support](https://getcoldturkey.com/support/)). Locks can be extended but not shortened, and "Frozen Turkey" locks, logs off or shuts down the PC ([user guide](https://getcoldturkey.com/support/user-guide/)). No admin or Safe Mode claims.
- **Freedom.** Locked Mode prevents quitting the app during a session. With protection enabled, uninstall "is not possible on Windows". An early exit is allowed once every 7 days ([Locked Mode](https://support.freedom.to/en/articles/1802927-locked-mode), [prevent uninstallation](https://support.freedom.to/en/articles/6116638-how-to-prevent-uninstallation-of-the-freedom-app), [ending a session](https://support.freedom.to/en/articles/1776713-ending-a-block-session)).
- **FocusMe.** Offers "Protect Uninstall", "Block 3rd Party Uninstallers", an "Extra Protection Service" that restarts FocusMe if it is closed, and "Enforced Mode" ([FAQ](https://focusme.com/faq/), [uninstall doc](https://focusme.com/docs/how-to-uninstall-focusme/)).
- **Takeaway.** Our plan (uninstall gate, watchdog, recovery code) is in line with the market **[inference]**.

### 2.9 Recovery-code regeneration and delivery
- **Storage.** Generate about 10 codes of at least 80 random bits each and store only salted hashes (PBKDF2 or SHA-256 is fine at that entropy) **[inference]**. Send them, then zero the buffers. An elevated admin could still read them from service memory during the few milliseconds they exist, or patch the binary. That is acceptable friction **[inference]**.
- **SMTP or API credentials on the machine are readable by an admin.** DPAPI LocalMachine scope lets *any user* decrypt; Microsoft calls it "almost equivalent to no encryption" on workstations ([ProtectedData blog](https://learn.microsoft.com/en-us/archive/blogs/shawnfa/managed-dpapi-part-i-protecteddata), [CryptProtectData](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)). What the user could *do* with a send-only key matters more:
  - (a) Change the recipient to themselves. Mitigate by pinning the friend's address at setup; changing it requires a recovery code.
  - (b) Read a "Sent" folder. Use a send-only transactional API key, not a mailbox password.
  - (c) Intercept TLS. That needs installing a root CA, which is high friction.

  **[inference]**
- **Delivery failure.** Never invalidate the old codes until the new set is confirmed: an API 2xx response at minimum, ideally a confirmation link the friend clicks. Prompt for regeneration when 3 codes remain, not 0. Retry with backoff. If everything fails, the old codes stay valid and Safe Mode remains the escape **[inference]**.
- **TOTP (RFC 6238).** The verifier needs the shared secret, so local verification puts the seed on disk, where an admin can mint codes **[inference]**. That is weaker than hashed codes.
  - **Clock skew:** the RFC recommends a limited window of steps and recording drift. A verifier "MUST NOT accept the second attempt" of the same OTP ([RFC 6238](https://datatracker.ietf.org/doc/html/rfc6238)).
  - **Use TOTP only if a server does the verification.**
- **Better v2: challenge-response with an asymmetric key.**
  1. The PC shows a random nonce.
  2. The friend's app or a tiny web page signs it with a private key the friend holds (for example Ed25519).
  3. The PC verifies the signature against a stored public key.

  Nothing secret lives on the PC, nothing needs regenerating, and clock skew doesn't matter. The friend must be reachable, though. Keep 2-3 hashed offline codes as a backup **[inference]**.

### 2.10 Lockout risks and safeguards
- **Every machine-changing measure is session-scoped with an expiry.** The service writes `{appliedPolicies, originalValues, expiresAt}` to SQLite *before* applying. On every start and on `OnStop`, if no valid session exists, it restores the original values **[inference]**.
- **Uninstall and an explicit `--restore-all` command** revert hosts, policies and ACLs. Ship a plain `restore.ps1` in the install directory, and print it out once.
- **Dev mode** (build flag or config): no service DACL, no Task Manager or CMD policies, 1-minute sessions, and a hard-coded test code.
- **Never apply** DisableCMD with scripts blocked, signed App Control policies, or IFEO hijacks. These are the measures that can break login scripts or boot.
- **Safe Mode** (service not registered there) plus `restore.ps1` is the ultimate escape. Test it before the first real session.

---

## 3. Tiered friction measures

| Tier | Measure | Effort | Blocks | Admin bypass | Lockout risk |
|---|---|---|---|---|---|
| **1** | `DisableTaskMgr` in `HKU\<SID>` during focus, re-applied on change | Low | Ctrl+Shift+Esc, Task Manager | Delete the value (regedit/PowerShell), use another process tool | Low (HKCU, auto-restored) |
| **1** | Service DACL denying stop/delete/config/WRITE_DAC to BA | Low | `sc stop`, services.msc, `sc delete` | Elevate, take ownership, rewrite DACL; or edit Services registry key and reboot | Low-med (keep a restore path) |
| **1** | Recovery actions (`sc failure` + `failureflag`) and a UI relaunch loop | Low | Killing the service or UI | Kill and disable faster than the restart delay; change config after ownership | Low |
| **1** | Process DACL denying `PROCESS_TERMINATE` | Low | Non-elevated kill | Elevated `SeDebugPrivilege` | Low |
| **1** | Hosts re-write (watcher + timer), Chrome policies re-applied via `RegNotifyChangeKeyValue` + timer | Low-med | Editing hosts, enabling DoH/Incognito/DevTools | Another browser (handled by process allowlist), stopping the service first | Low (avoid MS domains) |
| **1** | Uninstall custom action gated by the service + recovery code | Med | Add/Remove Programs, `msiexec /x` | Stop the service, delete files manually | Med (must always restore) |
| **1** | Hashed one-time codes, no plaintext on disk | Low | Reading codes from disk | Memory read or binary patch during generation | Med if delivery fails (keep old codes) |
| **2** | `DisableRegistryTools`, `DisableCMD` (value 2 [verify]), `RestrictToPermittedSnapins` during focus | Low | regedit, cmd, MMC snap-ins | PowerShell / Terminal, reg.exe [verify] | Low-med |
| **2** | Watchdog service pair | Med | Single-service kill or stop | Kill both, then disable both after ownership | Med (a bug could loop) |
| **2** | Hosts file ACL: SYSTEM-only write during focus | Low | Editing hosts with notepad as admin | Take ownership | Low-med (restore ACL) |
| **2** | DisallowRun list (powershell.exe, pwsh.exe, wt.exe, procexp*.exe) | Low | Explorer-launched tools | Renamed copy, launch from another process | Low |
| **2** | Tamper log + "you bypassed" summary next boot (Safe Mode detection) | Low | Nothing (accountability) | n/a | None |
| **3** | App Control for Business policy (unsigned) | High | Running unlisted binaries | Remove the policy as admin | High |
| **3** | Signed App Control policy | Very high | Policy removal | Very hard | **Very high (boot/brick)** |
| **3** | IFEO `Debugger` on taskmgr.exe | Low | Task Manager | Delete the key | Med; malware-like (T1546.012) |
| **3** | Service registered in SafeBoot | Low | Safe Mode bypass | Remove the SafeBoot key, WinRE offline edit | **High** (removes the escape hatch) |
| **3** | PPL / kernel driver | n/a | Everything user-mode | n/a | Not available (ELAM/MVI) |
| **3** | Locally verified TOTP | Low | Nothing extra | Read the seed | Weaker than hashed codes; skip |

---

## 4. Recommendation for v1

1. **Build the safety net first.**
   - Session-scoped apply with stored original values.
   - Restore on stop, expiry and uninstall.
   - `--restore-all` and `restore.ps1`.
   - Dev mode.
   - A tested Safe Mode escape.
2. **Ship all of Tier 1:**
   - `DisableTaskMgr` (the explicit ask).
   - Service DACL, recovery actions with `failureflag`, and a process DACL.
   - A UI agent the service relaunches.
   - Re-applied hosts and Chrome policies.
   - Uninstall gated by the service.
   - About 10 hashed recovery codes delivered by email through a transactional API with a send-only key. The friend's address is pinned. A new set is generated at 3 remaining, and old codes are invalidated only once delivery is confirmed.
3. **Add from Tier 2:**
   - `DisableRegistryTools`, `RestrictToPermittedSnapins`, and a DisallowRun entry for PowerShell/Terminal/Process Explorer during focus.
   - The tamper log.
   - Defer the watchdog pair until the single service has proven stable.
4. **Skip Tier 3.** Leave Safe Mode as the deliberate, high-effort escape.
5. **v2:** replace emailed codes with asymmetric challenge-response from the friend's device (or a tiny server). That removes regeneration and credential storage entirely.

**Honest bottom line:** an elevated admin who knows what to do can undo v1 in about 5-10 minutes (take ownership, stop the service, run restore, or boot to Safe Mode) **[inference]**. That is far beyond an impulse, which is the goal. The tamper log makes every bypass visible afterwards.
