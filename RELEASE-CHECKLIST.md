# Release verification

## v0.2.0 local verification — 2026-11-10

- Original 35 functional tests and six process checks pass. Added 12 CLI/profile groups and a real fixture process scenario covering detached execution, no-daemon, two homes sharing one worker, UTF-8/spaced paths, pipe release under three seconds, owner crash isolation and last-task exit under two seconds.
- Installed schema 3 locally. Existing seven hook commands and config.toml trust bytes remained identical; shortcut/account/artwork settings are retained. Upgrade starts no work.
- Used the installed standalone CLI 0.167.0 (default startup) and an official npm native CLI 0.163.0 (`--no-daemon`) for real first-task Steam smoke tests. Both started the configured presence process and returned to idle on completion. The downloaded binary is an ignored development fixture, not redistributed.
- CLI 0.163.0 also resumed the 0.167.0 smoke conversation with a new user task, then forked it with another task. Both created a new presence activation and returned to idle on completion.
- Pending manual matrix: full real shared-daemon/native terminal-close interactions per version, resume/fork selection and approval flows, Windows 11 desktop, actual NVM switches, standard-user desktop installation, Steam exit races and friend-side latency. Isolated tests are not claims that these manual cases passed.
- Release requires clean-source rebuild, green main/tag Actions runs, and downloaded attachment hash verification.
- Pre-release package review caught an upgrade-test fixture writing synthetic Steam data beside the build output. The fixture now uses its own Steam directory, and binary packaging uses an explicit product-file list. No test userdata is distributed.

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

## v0.1.1 local verification — 2026-11-09

- Reproduced v0.1.0's notification input loss through the exact installed Windows PowerShell command; direct execution received the same input successfully.
- Corrected the receiver executable type and worker handle inheritance. All 35 functional tests and six process checks passed, including both new regressions.
- The first hosted build exposed a console-code-page mismatch in the notification regression. The harness now sends actual UTF-8 bytes and the receiver explicitly decodes UTF-8; the mismatch was reproduced locally before applying the fix.
- Installed the local repair without changing hook definitions, trust records, Steam account configuration or artwork. All seven handlers remain trusted.
- Recovered the actual current task through Steam Play: active worker/presence acknowledged in 753 ms. A duplicate current-task notification returned `{}` in 290 ms without creating another epoch.
- After reloading VS Code, the user confirmed that a genuinely new task automatically restored the Steam playing status.
- v0.1.1 contains the repair; the original v0.1.0 tag and attachments are retained as historical artifacts. Release attachments must come from the tagged GitHub Actions build.

## Local verification — 2026-11-08

- Windows 10 22H2 x64: 35 isolated feature/configuration/transaction tests passed.
- Four process checks passed, including missing Steam, completion, manual stop and worker crash.
- Live Steam: automatic launch acknowledged in 885 ms; duplicate task notification returned `{}` without creating a new epoch. Manual stop ended all tool processes in 337 ms without relaunch.
- Existing installation upgraded in place; Steam shortcut bytes and original artwork hashes unchanged; seven native hooks remain trusted; legacy scheduled task removed.
- Source archive extracted into a separate directory and rebuilt successfully; archive checks found no account IDs, local configuration or session data.
- Not yet executed: an independent Windows 11 standard-user machine, GitHub hosted workflow, real Steam client exit race while another game is running.
