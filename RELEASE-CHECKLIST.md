# Release verification

The package is an experimental local build until a GitHub Actions run and the
additional OS checks below have actually completed. Passing a unit test is not
evidence of a live Steam friend-list update on another machine.

- Build and isolated lifecycle/configuration/installation tests: run `Test.ps1`.
- Clean source archive rebuild: extract source archive and run `Package.ps1`.
- Native hook review: trust seven handlers through Codex; Check status must confirm.
- Local Steam: verify silent launch, task completion, stop suppression and manual Play.
- Missing/stopped Steam: verify no periodic process and no client auto-launch, including a concurrent client exit.
- Additional Windows 11 x64 standard-user machine: install, repair, uninstall, task lifecycle.
- GitHub Actions: workflow must be green before publishing the experimental release.

No credentials, local settings, runtime state, screenshots, copied conversation
records or development fixtures may be included in the release archives.

## Local verification — 2026-11-08

- Windows 10 22H2 x64: 35 isolated feature/configuration/transaction tests passed.
- Four process checks passed, including missing Steam, completion, manual stop and worker crash.
- Live Steam: automatic launch acknowledged in 885 ms; duplicate task notification returned `{}` without creating a new epoch. Manual stop ended all tool processes in 337 ms without relaunch.
- Existing installation upgraded in place; Steam shortcut bytes and original artwork hashes unchanged; seven native hooks remain trusted; legacy scheduled task removed.
- Source archive extracted into a separate directory and rebuilt successfully; archive checks found no account IDs, local configuration or session data.
- Not yet executed: an independent Windows 11 standard-user machine, GitHub hosted workflow, real Steam client exit race while another game is running.
