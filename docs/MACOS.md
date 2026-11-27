# macOS Codex 中文汉化安装教程

适用：macOS 13+。分别提供 Apple Silicon 与 Intel 应用包。

## 选择下载包

在“苹果菜单 > 关于本机”查看芯片类型：

| 本机芯片 | Release 文件 |
|---|---|
| Apple M 系列 | `Codex-Zh-Launcher-macOS-arm64.zip` |
| Intel | `Codex-Zh-Launcher-macOS-x64.zip` |

从 [最新 Release](https://github.com/shibaweidu/codex-desktop-zh/releases/latest) 下载对应 ZIP 和 `SHA256SUMS.txt`。ZIP 内是可执行的 `.app` 应用；ZIP 是压缩包装，DMG 是磁盘映像，两者均不是处理器架构。

## 首次打开

解压后，将“Codex 汉化增强工具.app”拖入“应用程序”。先尝试右键应用选择“打开”；若系统仍阻止，在“系统设置 > 隐私与安全性”中为该应用选择“仍要打开”，并核实下载来源。

发布包采用免费的 ad-hoc 签名，未提供 Apple Developer ID 签名与公证，因此不能保证没有 Gatekeeper 提示。本工具不提供关闭 Gatekeeper、允许任何来源或清除 quarantine 的功能。

## 使用

1. 工具自动扫描 `/Applications` 与 `~/Applications` 下的 `Codex.app` / `ChatGPT.app`。
2. 未发现应用时手动选择 `.app`。
3. 未运行时点击“汉化并启动”。
4. 已运行时保存工作，再确认“关闭并汉化重启”。
5. 分别检查中文界面与原生菜单结果，失败时查看详细日志。

“下载 Codex”使用社区镜像元数据，按版本和架构校验更新；替换系统 Applications 中的客户端可能需要 macOS 授权。汉化运行时补丁本身不修改 Codex.app。

日志位于 `~/Library/Logs/CodexZhLauncher/launcher.log`。v0.8.3 的两种 Mac 架构均已通过 CI 构建与测试；Windows 的本机汉化验收及 Mac CI 不代表 macOS 已通过真实 Codex 实机验收。

返回 [项目介绍](../README.md) · [更新说明](UPDATES.md) · [问题排查](TROUBLESHOOTING.md)
