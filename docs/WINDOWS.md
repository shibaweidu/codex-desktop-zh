# Windows Codex 下载、安装与中文汉化教程

适用：Windows 10/11 x64，Microsoft Store 版或便携版 Codex Desktop。

## 下载汉化工具

从 [最新 Release](https://github.com/shibaweidu/codex-desktop-zh/releases/latest) 下载 `Codex-Zh-Launcher-Windows-x64.exe` 与 `SHA256SUMS.txt`。EXE 可直接运行，无需安装本工具。

可用 PowerShell 检查文件哈希，与同一 Release 的校验文件对照：

```powershell
Get-FileHash .\Codex-Zh-Launcher-Windows-x64.exe -Algorithm SHA256
```

## 检测和汉化 Codex

1. 打开工具，等待自动检测 Microsoft Store 安装。
2. 使用便携版时，点击“选择应用”，选择对应目录中的 `Codex.exe` 或 `ChatGPT.exe`。
3. Codex 未运行时，点击“汉化并启动”。
4. 已运行时，保存任务与输入，再点击“关闭并汉化重启”并确认。工具先请求正常退出，5 秒后才处理经过路径与 PID 校验的剩余进程。
5. 查看概览状态与“日志诊断”中的语言、菜单详情。

v0.8.3 开发构建提供“修复并验证（不重启）”。已有可验证调试接口时可直接修复当前会话；普通快捷方式启动的会话通常需要通过工具重新启动。最新 Release 是否包含此功能，以其更新说明为准。

## 尚未安装或需要更新 Codex

打开“下载 Codex”，工具使用 [Wangnov 社区镜像](https://github.com/Wangnov/codex-app-mirror) 的版本信息，对照本机包版本。已是相同或更新版本时跳过下载；有新版时下载校验，确认后通过系统 AppX 为当前账户部署。

下载支持断点续传及校验缓存。系统部署前需要退出 Codex；便携版通过原渠道更新。不要把“汉化工具更新”和“Codex 客户端更新”混为一谈，见 [更新说明](UPDATES.md)。

## 日志

日志位于 `%LOCALAPPDATA%\CodexZhLauncher\logs\launcher.log`，也可在“日志诊断”筛选、复制或导出。出现失败时保留具体错误码，提交前移除本机路径等不希望公开的信息。

返回 [项目介绍](../README.md) · [问题排查](TROUBLESHOOTING.md)
