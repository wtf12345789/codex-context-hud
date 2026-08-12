# Changelog

All notable changes to this project will be documented here.

## 0.2.3 - 2026-08-12

- Launch Codex through the Windows package activation API instead of running its WindowsApps executable directly.
- Start the HUD only after the loopback debugging endpoint is ready.
- Remove the legacy sign-in startup shortcut during installation to avoid polling while Codex is closed.
- Use the installed Codex app icon for the dedicated launcher while keeping its name unchanged.
- Show a loading state instead of a false zero while compaction history is still synchronizing.

## 0.2.2 - 2026-08-10

- Resolved provisional `client-new-thread` sidebar IDs through the active composer's real conversation ID.
- Restored historical compaction counts for affected tasks instead of leaving the HUD at zero.
- Added live `item/started` and `item/completed` compaction handling with stable-ID deduplication.
- Prevented partial snapshots from lowering an already observed compaction count.

## 0.2.1 - 2026-08-10

- Made empty and filled compaction stages easier to distinguish with wider bars and visible tracks.
- Replaced the low-contrast 10+ black stage with a soft violet critical highlight.
- Lengthened and thickened the quota bar, added a visible empty track, and introduced green/yellow/red quota states.
- Kept the session-switch reveal motion while reducing the thicker quota bar's vertical pulse.

## 0.2.0 - 2026-08-10

- Moved the default HUD into the Codex composer toolbar, beside the native context ring.
- Added account quota and stable-ID compaction tracking without per-task JSONL scanning.
- Added compact stage bars, a unified delayed hover card, and task-switch-only reveal motion.
- Kept 10+ compactions visibly saturated in the critical black state.
- Added fallback mounting for tasks where the native context ring is absent.
- Added a loopback-only launcher, renderer host singleton, wait/reconnect behavior, and safe installer shortcuts.
- Kept the detached WinForms HUD as an explicit `--legacy-overlay` compatibility mode.
- Reworked Chinese and English positioning, installation, privacy, and troubleshooting documentation.

## 0.1.0 - 2026-08-05

- Initial public release candidate.
- Active-task context usage and compaction count.
- Borderless click-through Windows HUD beside the Codex composer.
- Task, left-sidebar, and right-panel tracking.
- DWM-synchronized monotonic spatial motion with animation exclusion.
- Per-user install, login startup, uninstall, and portable ZIP packaging.
