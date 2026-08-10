# Changelog

All notable changes to this project will be documented here.

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
