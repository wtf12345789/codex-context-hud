# Codex Context HUD

[English](README.en.md) · 简体中文

<p align="center">
  <img src="assets/hero.png" alt="Codex Context HUD — 原生化的上下文、压缩次数与账户额度 HUD" width="100%">
</p>

<p align="center">
  <a href="https://github.com/wtf12345789/codex-context-hud/actions/workflows/build.yml"><img src="https://github.com/wtf12345789/codex-context-hud/actions/workflows/build.yml/badge.svg" alt="Windows build"></a>
  <a href="https://github.com/wtf12345789/codex-context-hud/releases"><img src="https://img.shields.io/github/v/release/wtf12345789/codex-context-hud?display_name=tag&sort=semver" alt="Release"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-4f7fd7" alt="Windows 10 and 11">
  <img src="https://img.shields.io/badge/network-localhost%20only-86a58e" alt="Localhost only">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-d9a853" alt="MIT license"></a>
</p>

把长会话真正需要的状态放回 Codex 输入框：保留原生上下文圆环，在它左侧补上压缩阶段和账户额度。没有额外悬浮窗，不会在侧栏切换时漂移，也不会用一排文字破坏原生工具栏。

> 非 OpenAI 官方项目。它不会修改 Codex 安装文件；通过仅监听 `127.0.0.1` 的 Chromium 调试端口，把一段可审计脚本挂载到 Codex 渲染器。

## 一眼看懂

| 指示器 | 表达什么 | 设计 |
| --- | --- | --- |
| 细额度条 | 账户主额度窗口剩余百分比 | 柔和鼠尾草绿；会话切换后短暂强调 |
| 三条压缩竖杠 | 当前会话已发生的上下文压缩 | 1–3 次原生灰、4–6 次柔黄、7–9 次柔红、10 次及以上全黑 |
| Codex 原生圆环 | 当前上下文窗口用量 | 直接复用 Codex 自带组件，不重复显示百分比 |

HUD 默认只显示图形。悬停约半秒后，合并信息卡会显示准确的账户额度百分比和压缩次数。切换会话时先等待原生历史加载稳定，再播放一轮约 0.9 秒的克制动效；中途数据刷新不会乱跳。

## 为什么是这个形态

- **跟 Codex 一起排版。** HUD 是输入框工具栏的一部分，左右侧栏开合和窗口布局变化由 Codex 自己完成。
- **当前会话优先。** 压缩事件按稳定 ID 去重；会话切换后从 Codex 原生会话状态读取，不扫描大型 JSONL。
- **账户额度独立。** 额度来自 Codex 本地运行态的主限额窗口，不绑进每个会话缓存。
- **安静但有反馈。** 常态没有持续呼吸或闪烁；仅在会话切换并完成加载后播放一次。
- **不碰安装文件。** 不补丁、不替换 Codex 资源；Codex 更新后只需重新启动本地桥接。

## 安装

要求：Windows 10/11、Microsoft Store 版 Codex Desktop。

1. 从 [Releases](https://github.com/wtf12345789/codex-context-hud/releases) 下载 `CodexContextHUD-portable.zip` 并解压。
2. 在解压目录打开 PowerShell，运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Install.ps1
```

如需先核验下载，可用 `Get-FileHash .\CodexContextHUD-portable.zip -Algorithm SHA256` 与 Release 中的 `.sha256` 文件对照。

3. 如果 Codex 当前已按普通方式运行，请先自行保存工作并退出。安装器不会替你关闭或重启 Codex。
4. 从开始菜单打开 **Codex with Context HUD**。以后也从这个入口启动 Codex。

安装器默认安装到 `%LOCALAPPDATA%\CodexContextHUD`，创建 HUD 登录启动项和开始菜单启动器。后台 HUD 会等待 Codex，并在页面重载或 Codex 下次启动后自动重连。

可选参数：

```powershell
# 换用其他本地端口
.\Install.ps1 -Port 9241

# 不创建登录启动项
.\Install.ps1 -NoStartup

# 不创建开始菜单启动器（仅适合自行手动启动）
.\Install.ps1 -NoStartMenu

# 安装后不立即启动 HUD 后台宿主
.\Install.ps1 -NoLaunch
```

直接从便携目录运行也可以：

```powershell
.\Launch-CodexWithHUD.ps1 -Port 9231
```

如果 Codex 已在运行但没有启用对应端口，启动器只会提示你手动退出，不会强制结束进程。

## 更新与卸载

安装新版时直接再次运行新版 `Install.ps1`。更新过程只替换并重启 HUD，不操作 Codex。

从安装包或安装目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Uninstall.ps1
```

卸载器只结束 HUD、删除自身文件和快捷方式；已经运行的 Codex 保持不变。

## 隐私与安全边界

- 调试端口显式绑定 `127.0.0.1`，桥接器只连接回环地址，并只接受 Codex 主页面目标。
- 默认渲染器模式不读取会话 JSONL，不保存提示词、回复、任务标题或对话正文。
- 注入脚本只读取压缩事件 ID、原生上下文组件、当前会话标识和账户限额百分比。
- 不调用模型 API，不向外部服务器上传日志、Cookie、Token、凭据或账号数据。
- Chromium 调试端口本身具备较高页面权限；请勿把端口暴露到局域网或公网，也不要运行来源不明的本地程序。

全部桥接和渲染脚本都随发布包对应源码一起公开，可直接审查 [`RendererHudBridge.cs`](RendererHudBridge.cs) 和 [`RendererHudScript.js`](RendererHudScript.js)。

## 排障

**HUD 没出现**

- 确认 Codex 是从 **Codex with Context HUD** 启动，而不是原始快捷方式。
- 确认安装和启动时使用同一个端口。
- 本机检查端点：`Invoke-RestMethod http://127.0.0.1:9231/json/list`。

**提示 Codex 已在运行**

- 这是安全保护。保存工作后自行退出 Codex，再重新打开开始菜单启动器。

**Codex 更新后消失**

- HUD 会等待并自动重连；如果 Codex 的工具栏结构发生破坏性变化，请提交 Issue，但不要附带私人会话内容或凭据。

## 从源码构建

```powershell
.\Build.ps1
```

使用 Windows 自带 .NET Framework C# 编译器，无需下载依赖。构建会执行外置兼容层和渲染器桥接两组最小自测；若当前 HUD 正在运行，只会重启同路径 HUD。

生成 Release 便携包：

```powershell
.\Package.ps1
```

## 技术路线

- C# 宿主负责本地 CDP 目标白名单、脚本注入、断线等待与自动重连。
- Shadow DOM HUD 插入 Codex 输入框工具栏，直接交给原生布局处理侧栏和窗口变化。
- 压缩次数从 Codex 原生会话历史按事件 ID 去重；账户额度从本地运行态限额事件读取。
- 旧外置 WinForms HUD 仅作为 `--legacy-overlay` 兼容模式保留，不是默认运行路径。

## 已知限制

- 仅支持 Windows Codex Desktop，目前启动器面向 Microsoft Store 安装版。
- 依赖 Chromium 调试端口和非公开渲染器结构；Codex 大版本更新后可能需要适配。
- 账户额度是 Codex 报告的主限额窗口剩余比例，不是货币余额或账单金额。
- 本项目尚不是 Codex 官方插件，无法从原始 Codex 快捷方式自动获得调试端口。

## License

[MIT](LICENSE)
