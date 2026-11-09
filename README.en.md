# CodexSteamBridge

[简体中文](README.md) · Windows x64 · v0.1.0 experimental · MIT

Shows “Playing Codex” through a non-Steam shortcut while Codex works in local VS Code. All tasks finishing ends the presence process.

## Install

1. Extract the release and double-click `CodexSteamBridge.exe`. Default location: `%LOCALAPPDATA%\Programs\CodexSteamBridge`. Close Steam yourself before library changes or uninstalling. The tool never closes/restarts Steam or restores other games.
2. Verify detected paths and select Steam accounts. Choose Install / repair. Existing installations upgrade in place, preserving shortcut IDs, titles and artwork.
3. In the Codex composer, select Review hooks and trust the seven handlers. Reload existing VS Code windows, select Check status in the manager, then submit a new task. The installer never grants hook trust on your behalf.

## Lifecycle

No login startup or idle daemon. A new user task can start the worker only if Steam is already running. Missing/stopped Steam or an unconfigured account ends that attempt without retries. A running but signed-out client gets up to 60 seconds; finishing the task cancels the wait.

A launch request is sent once, silently, with a 10-second acknowledgement deadline. All tasks finishing ends both processes. Stopping the tool, a crash, restarting Steam or switching accounts suppresses recovery for the current task. A new user task can try again. Steam Play explicitly resumes current work, or exits if there is none; it does not open VS Code or create tasks.

Polling interval: 250 ms. A stale worker heartbeat ends presence after 4 seconds. Target local task-completion exit: within 2 seconds. Friend-list propagation is controlled by Steam.

## Privacy and maintenance

No logs, telemetry, background downloads or auto-update. Only operational identifiers, transcript offsets and process liveness metadata are persisted. Registered conversation increments are parsed in memory; prompts and messages are not stored by this tool.

Use the manager to repair, enable/disable, inspect trust or uninstall. Add shortcuts for new accounts while Steam is closed. Uninstall preserves unrelated hooks, shortcuts and user-modified artwork.

## Build

Windows x64 and .NET Framework 4.8; no Python, Node.js or additional runtime required.

```powershell
.\Build.ps1
.\Test.ps1
.\Package.ps1
```

See [COMPATIBILITY.md](COMPATIBILITY.md) for limitations and measured coverage. GitHub Actions builds/tests and produces release attachments for `v0.1.0` tags. The project and release artwork use the MIT license. This project is independent of OpenAI, Valve and Microsoft; no trademark rights are granted.
