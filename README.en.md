# CodexSteamBridge

[简体中文](README.md) · Windows x64 · v0.2.0 experimental · MIT

[Download Windows package](https://github.com/users-114514/CodexSteamBridge/releases/download/v0.2.0/CodexSteamBridge-v0.2.0-windows-x64.zip) · [Release and SHA-256](https://github.com/users-114514/CodexSteamBridge/releases/tag/v0.2.0)

Show “Playing Codex” in Steam while local interactive Codex CLI or VS Code Codex tasks run. All tasks finishing ends the presence process. CLI use does not require VS Code.

## Install / upgrade

1. Extract the Windows ZIP and double-click `CodexSteamBridge.exe`. Default current-user location: `%LOCALAPPDATA%\Programs\CodexSteamBridge`. Close Steam yourself before creating/changing shortcuts or uninstalling. The tool never starts/restarts Steam or other games.
2. Verify Steam, accounts and Codex profiles. Add/edit/remove homes and enable CLI / extension independently; choose Install / repair to apply everything. Select a native `codex.exe` or npm / NVM `codex.cmd` / `codex.ps1` launcher. At least one supported client and Steam are required.
3. Review the seven notification commands in CLI `/hooks` or the extension's native Review hooks interface. Restart CLI sessions / reload VS Code, select Check status and submit a new task. The tool never grants trust.

Hooks are local commands Codex invokes for submission, completion and cancellation. No extra API key is needed; Codex must already be authenticated and working.

From v0.1.1, run the new package and repair the original installation. Its home becomes the default profile; unchanged legacy `event` commands and trust remain, as do account IDs, shortcut IDs and artwork. Old runtime state is cleared; the next new task activates integration. New homes use `event --profile <id>` and require their own native review. Failed multi-home changes roll back together.

## Lifecycle

| Trigger | Result |
| --- | --- |
| Windows login, opening editor/terminal/CLI, choosing an old session, idle input | No bridge background process |
| New user submission in interactive `codex`, `resume` or `fork` | One silent Steam request if running and configured |
| Waiting for approval or an answer | Remain playing while the task is active |
| Terminal closes | Continue if the actual execution process continues; retire its work when it exits |
| Multiple CLI / extension windows and homes | One shared task worker and one Steam presence process |
| All tasks complete, fail or cancel | End presence and worker |
| Steam missing or stopped | Exit immediately; no Steam launch, retry or idle discovery |
| Steam running but signed out | Wait up to 60 seconds, ending early if tasks finish |
| Unconfigured account, Steam exit/restart/logout/account change | End this activation; recheck on the next new task |
| Manual stop or bridge crash | No automatic recovery for that submission; a new user task may start it |
| Steam Play | Restore current work; exit when idle; never launch an editor or create a task |

Continuations, children and duplicates cannot reactivate suppressed work. CLI in VS Code's terminal remains CLI. Registered launchers are resolved on each new task, including npm / NVM links; a fixed old version directory removed by an upgrade must be corrected in the manager.

Launch acknowledgement: 10 seconds maximum, no repeated request. Check interval: 250 ms. Presence exits after a 4-second heartbeat loss. Local normal completion exit target: 2 seconds; Steam friends propagation is separate. Unknown source or format retires only that session and reports the current reason.

## Management / privacy

After reboot, trusted notifications start the tool on the next task. The manager handles status, repair, enable/disable, profile changes and uninstall. Steam accounts share global shortcut settings. Uninstall removes only this tool's registered hooks/trust, owned shortcuts and unmodified distributed artwork.

No startup tasks, logs, telemetry or updates. No prompt/body/screenshot/event history storage. Operational state contains identifiers, read cursors, process identities and current errors only; transcript bodies are processed in memory. Public artwork is original; existing local images are retained.

## Build / scope

Windows x64, .NET Framework 4.8, C# / Windows Forms; the tool needs no Node.js or Python runtime.

```powershell
.\Build.ps1
.\Test.ps1
.\Package.ps1
```

Tests use isolated fixtures and real process chains without a model account. GitHub Actions builds clean source and publishes tagged artifacts. See [COMPATIBILITY.md](COMPATIBILITY.md) for tested versions and pending checks, and [CHANGELOG.md](CHANGELOG.md) for upgrade changes.

Excluded: automated `exec` / `review`, WSL, remote / SSH, containers, macOS, Linux and ARM64. Independent of OpenAI, Valve and Microsoft. MIT grants no third-party trademark rights.
