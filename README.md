# Codex Context HUD

[English](README.en.md) · 简体中文

<p align="center">
  <img src="assets/hero.png" alt="Codex Context HUD — Windows local read-only context usage and compaction HUD" width="100%">
</p>

<p align="center">
  <a href="https://github.com/wtf12345789/codex-context-hud/actions/workflows/build.yml"><img src="https://github.com/wtf12345789/codex-context-hud/actions/workflows/build.yml/badge.svg" alt="Windows build"></a>
  <a href="https://github.com/wtf12345789/codex-context-hud/releases"><img src="https://img.shields.io/github/v/release/wtf12345789/codex-context-hud?display_name=tag&sort=semver" alt="Release"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-4f7fd7" alt="Windows 10 and 11">
  <img src="https://img.shields.io/badge/network-none-5bb27a" alt="No network calls">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-d9a853" alt="MIT license"></a>
</p>

一个面向 Windows Codex Desktop 的只读、本地、无注入 HUD：在输入框旁持续显示当前任务的上下文压缩次数和上下文用量，并跟随任务切换、左右侧栏与输入框位置同步移动。

> 非 OpenAI 官方项目，不修改 Codex 安装文件，也不依赖 Codex++、DevTools、CDP、Node.js、Rust 或模型 API。

## 为什么做这个

Codex Desktop 的长任务需要随时知道两件事：当前上下文压力，以及已经发生过多少次压缩。官方社区已有大量恢复常驻上下文指示器的诉求，但现有替代方案通常是应用内脚本注入或应用外配额面板。

本项目专注于一个更窄的目标：以独立 Windows HUD 的方式贴近 Codex 输入区，显示当前任务的压缩次数和上下文用量，并尽量保持原生视觉融合度。

## 实际效果

<p align="center">
  <img src="assets/motion-demo.gif" alt="HUD follows the Codex composer while the right panel opens and closes" width="92%">
</p>

平移期间只有位置发生变化；数字、上下文环、压缩图标和颜色动画会冻结，移动完成后再继续。

## 功能

- 当前任务切换后自动更新压缩次数和上下文用量。
- HUD 贴近 Codex 输入框，仅在 Codex 位于前台时显示。
- 无背景、边框和阴影，整窗鼠标穿透，不遮挡 Codex 操作。
- 左右侧栏切换时使用 DWM 垂直同步和平滑单调位移；平移期间其他 HUD 动画冻结。
- 上下文用量在 70% / 85% 进入黄色 / 红色；压缩 2 次为黄色，至少 3 次为红色。
- 只读取本机 Codex 会话与运行态信号，不联网、不调用模型、不读取或保存对话正文。
- 当前用户登录后自动运行；Codex 未打开时保持隐藏。

## 安装

要求：Windows 10/11、Codex Desktop。

1. 从 [Releases](https://github.com/wtf12345789/codex-context-hud/releases) 下载 `CodexContextHUD-portable.zip`。
2. 解压后在该目录打开 PowerShell。
3. 运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Install.ps1
```

默认安装到 `%LOCALAPPDATA%\CodexContextHUD`，创建当前用户登录启动项并立即启动 HUD。安装器只会更新或启动 HUD，不会关闭、终止或重启 Codex。

可选参数：

```powershell
# 不创建登录启动项
.\Install.ps1 -NoStartup

# 安装后不立即启动
.\Install.ps1 -NoLaunch
```

## 卸载

从解压后的安装包目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Uninstall.ps1
```

卸载器只退出 HUD 并删除自身安装文件和启动项，不操作 Codex。

## 隐私与安全边界

- 默认定位 `$CODEX_HOME\sessions`；未设置时使用 `%USERPROFILE%\.codex\sessions`。
- 会话文件采用增量读取，只解析任务标识、token 使用量和压缩事件所需的结构化字段。
- Windows UI Automation 仅用于读取 Codex 控件位置和侧栏状态。
- 不上传文件、日志、提示词、对话正文、Cookie、Token 或账号信息。
- 提交 Issue 时请勿附加原始 Codex JSONL、日志、截图中的私人任务名或任何凭据。

## 实现说明

- 单文件 C# / WinForms HUD，使用 Windows 自带 .NET Framework 编译器。
- UIA 后台线程负责输入区与侧栏状态；低级鼠标事件只用于侧栏切换的即时预测。
- 空间位移使用固定透明画布和 DWM 帧时钟，避免逐帧移动外层 HWND。
- 左右位移共用 500ms 严格单调四次减速曲线，只改变目标位移符号，不使用弹簧。
- 右上角存在多个 Toggle 时选择最右侧任务侧栏控件，避免底部面板状态干扰方向。

## 从源码构建

```powershell
.\Build.ps1
```

脚本使用 Windows 自带的 .NET Framework C# 编译器，完成后执行最小自测，不下载依赖。构建过程可能仅重启正在运行的 HUD，不会操作 Codex。

生成与 GitHub Release 相同结构的便携包：

```powershell
.\Package.ps1
```

该脚本会重新构建、生成 `SHA256SUMS.txt` 并输出 `CodexContextHUD-portable.zip`。

## 已知限制

- 仅支持 Windows Codex Desktop。
- 这不是官方插件 API；Codex 大幅调整 UI Automation 树、日志格式或本地会话结构后，可能需要兼容补丁。
- 上下文百分比来自 Codex 本地运行态数据，应视为产品侧报告值，而不是重新分词得到的独立精确计数。

## 类似项目

| 项目 | 形态 | 平台 | 当前任务上下文 | 压缩次数 | 无渲染页注入 |
| --- | --- | --- | ---: | ---: | ---: |
| **Codex Context HUD** | 输入框旁独立 HUD | Windows | ✅ | ✅ | ✅ |
| [codex-context-used-meter](https://github.com/Minghou-Lei/codex-context-used-meter) | Codex++ 用户脚本 | Windows / macOS | ✅ | — | — |
| [CodexBar](https://github.com/steipete/CodexBar) | 菜单栏用量中心 | macOS | 侧重账号额度 | — | ✅ |

`codex-context-used-meter` 功能最接近，并提供 Provider 余额与历史图；本项目选择更窄的独立 Windows HUD 路线，不依赖 Codex++ 或渲染页注入，同时显示压缩次数。另见 [openai/codex#23794](https://github.com/openai/codex/issues/23794) 中对 Codex Desktop 常驻上下文指示器的社区诉求。

## License

[MIT](LICENSE)
