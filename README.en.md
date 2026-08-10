# Codex Context HUD

English · [简体中文](README.md)

<p align="center">
  <img src="assets/hero.gif" alt="Animated Codex Context HUD demo" width="100%">
</p>

<p align="center">
  <a href="https://github.com/wtf12345789/codex-context-hud/releases"><img src="https://img.shields.io/github/v/release/wtf12345789/codex-context-hud?display_name=tag&sort=semver" alt="Release"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-4f7fd7" alt="Windows 10 and 11">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-d9a853" alt="MIT license"></a>
</p>

A compact context HUD for Codex Desktop. It keeps the native context ring and adds remaining account quota plus active-task compaction stages immediately to its left. The toolbar stays icon-only until you hover for exact values.

Compactions use native gray at 1–3, soft yellow at 4–6, soft red at 7–9, and solid black at 10+. After a task switch, motion waits for statistics to finish loading. There is no detached overlay and the default path no longer scans or caches large session JSONL files.

## Install

Requires the Microsoft Store build of Codex Desktop on Windows 10/11. Download and extract the [latest release](https://github.com/wtf12345789/codex-context-hud/releases/latest), then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install.ps1
```

If Codex is already open, save your work and exit it yourself, then launch **Codex with Context HUD** from the Start menu. The installer and launcher never force-close or restart Codex.

Uninstall with:

```powershell
powershell -ExecutionPolicy Bypass -File .\Uninstall.ps1
```

## Security and source

The HUD attaches through a local debugging endpoint bound to `127.0.0.1`. It does not modify Codex installation files or upload conversations, logs, or credentials. See [`RendererHudBridge.cs`](RendererHudBridge.cs) and [`RendererHudScript.js`](RendererHudScript.js).

Run `.\Build.ps1` to build from source or `.\Package.ps1` to create the portable package. Licensed under the [MIT License](LICENSE).
