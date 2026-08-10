# Codex Context HUD

English · [简体中文](README.md)

<p align="center">
  <img src="assets/hero.png" alt="Codex Context HUD — native-feeling context, compaction and quota indicators" width="100%">
</p>

<p align="center">
  <a href="https://github.com/wtf12345789/codex-context-hud/actions/workflows/build.yml"><img src="https://github.com/wtf12345789/codex-context-hud/actions/workflows/build.yml/badge.svg" alt="Windows build"></a>
  <a href="https://github.com/wtf12345789/codex-context-hud/releases"><img src="https://img.shields.io/github/v/release/wtf12345789/codex-context-hud?display_name=tag&sort=semver" alt="Release"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-4f7fd7" alt="Windows 10 and 11">
  <img src="https://img.shields.io/badge/network-localhost%20only-86a58e" alt="Localhost only">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-d9a853" alt="MIT license"></a>
</p>

Put long-session state back where you work: keep Codex's native context ring, then add compaction stages and remaining account quota immediately to its left. No detached overlay, no sidebar drift, and no row of labels fighting the native toolbar.

> Unofficial OpenAI project. It does not modify Codex installation files. An auditable renderer script is attached through a Chromium debugging endpoint bound to `127.0.0.1` only.

## Read it at a glance

| Indicator | Meaning | Visual language |
| --- | --- | --- |
| Thin quota bar | Remaining percentage in the primary account limit window | Soft sage, with a short emphasis after task switches |
| Three compression bars | Context compactions in the active task | Native gray for 1–3, soft yellow for 4–6, soft red for 7–9, solid black at 10+ |
| Native Codex ring | Current context-window usage | Reuses the built-in component instead of printing another percentage |

The HUD stays icon-only. Hover for about half a second to see exact quota and compaction values in one card. After a task switch, it waits for native history to settle before playing one restrained ~0.9s reveal; ordinary background updates do not flash.

## Why this shape

- **Native layout ownership.** The HUD lives in the composer toolbar, so Codex handles sidebars and window layout.
- **Active-task correctness.** Compactions are deduplicated by stable event ID and read from native task state, without scanning large JSONL files.
- **Account-level quota.** The primary limit window is read from local runtime state instead of being cached per task.
- **Quiet feedback.** No permanent pulse. Motion plays once, after a switched task has loaded.
- **No app patching.** Codex resources are never replaced or modified.

## Install

Requirements: Windows 10/11 and the Microsoft Store build of Codex Desktop.

1. Download and extract `CodexContextHUD-portable.zip` from [Releases](https://github.com/wtf12345789/codex-context-hud/releases).
2. Open PowerShell in the extracted directory and run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install.ps1
```

To verify the download first, compare `Get-FileHash .\CodexContextHUD-portable.zip -Algorithm SHA256` with the `.sha256` file attached to the release.

3. If Codex is already running normally, save your work and exit it yourself. The installer never closes or restarts Codex.
4. Open **Codex with Context HUD** from the Start menu. Use this entry for future launches as well.

The default install location is `%LOCALAPPDATA%\CodexContextHUD`. A background HUD shortcut is added at login, while the Start menu launcher opens Codex with its loopback debugging port. The HUD waits safely and reconnects after page reloads or later Codex starts.

Options:

```powershell
.\Install.ps1 -Port 9241
.\Install.ps1 -NoStartup
.\Install.ps1 -NoStartMenu
.\Install.ps1 -NoLaunch
```

Portable use is supported too:

```powershell
.\Launch-CodexWithHUD.ps1 -Port 9231
```

If Codex is already running without the selected port, the launcher asks you to exit manually and never terminates the process.

## Update and uninstall

Run the newer `Install.ps1` to update. Only the HUD is replaced and restarted; Codex is untouched.

Run from either the release folder or the installed folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\Uninstall.ps1
```

The uninstaller removes only the HUD process, its known files, and its shortcuts. A running Codex instance remains open.

## Privacy and security boundary

- The debugging endpoint is explicitly bound to `127.0.0.1`; the bridge accepts loopback WebSockets and allowlisted Codex main-page targets only.
- Default renderer mode does not read session JSONL or store prompts, responses, task titles, or conversation text.
- The injected script reads only compaction event IDs, the native context component, active-task identity, and account rate-limit percentages.
- No model API calls and no uploads of logs, cookies, tokens, credentials, or account data.
- A Chromium debugging port has elevated page access by nature. Never expose it to a LAN/WAN and avoid untrusted local software while it is enabled.

The complete bridge and renderer source ships in this repository: [`RendererHudBridge.cs`](RendererHudBridge.cs) and [`RendererHudScript.js`](RendererHudScript.js).

## Troubleshooting

**The HUD is missing**

- Launch Codex from **Codex with Context HUD**, not its original shortcut.
- Use the same port during installation and launch.
- Check locally with `Invoke-RestMethod http://127.0.0.1:9231/json/list`.

**The launcher says Codex is already running**

- This is the safety guard. Save your work, exit Codex yourself, and invoke the Start menu launcher again.

**It disappeared after a Codex update**

- The host waits and reconnects automatically. A breaking toolbar change may still require an update; never attach private task content or credentials to a public issue.

## Build from source

```powershell
.\Build.ps1
```

The build uses the .NET Framework compiler included with Windows, downloads no dependencies, and runs both legacy compatibility and renderer-bridge self-tests. If this exact HUD build is running, only that HUD process is restarted.

Create the portable release package:

```powershell
.\Package.ps1
```

## Architecture

- A C# host allowlists the local CDP target, injects the renderer, waits through downtime, and reconnects after disconnects.
- A Shadow DOM HUD joins the native composer toolbar, leaving sidebar and window movement to Codex layout.
- Stable native event IDs provide compaction counts; local rate-limit events provide remaining account quota.
- The old detached WinForms HUD remains available only as `--legacy-overlay` compatibility mode and is no longer the default.

## Limitations

- Windows Codex Desktop only; the launcher currently targets the Microsoft Store installation.
- Depends on a Chromium debugging port and undocumented renderer structure, so major Codex updates may require compatibility work.
- Account quota means the remaining primary rate-limit window reported by Codex, not currency or billing balance.
- This is not an official Codex plugin, so the original Codex shortcut cannot automatically provide the required debugging port.

## License

[MIT](LICENSE)
