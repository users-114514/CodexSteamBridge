# Changelog

## v0.2.0 (experimental)

- Add interactive CLI integration independent of VS Code, with executable and invocation checks.
- Schema 3: multiple homes, per-client switches, scoped deduplication and compatibility errors.
- Preserve legacy commands and trust; reset runtime state on upgrade. New homes require native review.
- Follow execution-process identity across detached terminals; isolate broken sessions and continuations.
- Expand CLI/profile, transactional install and real-process tests; exclude build outputs from source packages.

## 0.1.1 — 2026-11-09

- Build the notification receiver as a console executable so the existing trusted Windows PowerShell command forwards Codex's JSON input; keep the manager and Steam presence as windowless GUI executables.
- Decode task notifications explicitly as UTF-8, preserving Chinese and other non-ASCII paths independently of the Windows console code page.
- Start the task worker without inheriting notification pipe handles, allowing the one-shot receiver to finish while the worker remains active.
- Add process regression checks for the exact registered command with Unicode/spaced paths and for a live task worker releasing the receiver's output pipes.
- Keep the Visual Studio project building both executable types and disable duplicate target-framework metadata generation.

## 0.1.0 — 2026-11-08

- Windows Forms manager in Simplified Chinese and English; per-user installation, repair and uninstall.
- Steam account discovery and non-Steam shortcut/artwork installation without restarting the client.
- On-demand lifecycle; no login task, no automatic restart, no retries for missing/stopped Steam.
- Native Codex hook trust review; configuration merge and file transaction rollback.
- Original release artwork, isolated tests, source package and GitHub release workflow.
