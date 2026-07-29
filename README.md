# Codex Desktop 中文汉化增强工具

[English](README.en.md) | 简体中文

![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS-5B8DEF)
![Architecture](https://img.shields.io/badge/architecture-x64%20%7C%20arm64-6D7280)
![Version](https://img.shields.io/badge/version-v0.6.1-C4B5FD)
![License](https://img.shields.io/badge/license-MIT-31B77A)

**Codex Desktop 中文汉化增强工具**是一款面向 Windows 和 macOS 的 Codex 汉化启动器。它支持 Microsoft Store、便携版和 macOS App Bundle，可启用中文界面、翻译 Electron 原生菜单，并在 Codex 已运行时安全关闭后以中文模式重新启动。

项目不解包、不替换 `app.asar`，不修改 Microsoft Store 安装目录，也不读取 Codex 账号、令牌或项目文件。

当前版本：`0.6.1`

## 主要功能

- 自动检测 Microsoft Store 版 Codex Desktop
- 支持手动选择便携版 `Codex.exe` 或 `ChatGPT.exe`
- 自动检测 `/Applications` 和 `~/Applications` 中的 `Codex.app` / `ChatGPT.app`
- 原生 SwiftUI macOS 13+ 界面，分别支持 Apple Silicon 与 Intel
- 调用 Codex 设置接口启用官方 `zh-CN` 界面资源
- 运行时翻译文件、编辑、视图、窗口、帮助等 Electron 原生菜单
- 自动记录新版 Codex 中尚未覆盖的菜单标签
- Codex 已运行时显示“关闭并汉化重启”
- 先请求正常退出，5 秒后仅强制终止经过路径验证的剩余进程
- 深色 WPF / SwiftUI 界面、实时状态和本地诊断日志
- 启动时自动检查 GitHub Releases，新版本仅提示、不静默安装
- 关于界面提供版本、手动检查更新、反馈支持和项目仓库入口
- 不要求管理员权限，不修改 Codex 官方文件

## 下载与使用

从 [GitHub Releases](../../releases/latest) 下载对应系统的文件和 `SHA256SUMS.txt`：

| 系统 | 下载文件 |
|---|---|
| Windows 10/11 x64 | `Codex-Zh-Launcher-Windows-x64.exe` |
| Apple Silicon（M1/M2/M3/M4） | `Codex-Zh-Launcher-macOS-arm64.zip` |
| Intel Mac | `Codex-Zh-Launcher-macOS-x64.zip` |

Windows 直接运行 EXE。macOS 解压 ZIP 后，将应用拖到“应用程序”，首次启动请右键应用并选择“打开”。若仍被阻止，前往“系统设置 > 隐私与安全性”，在安全性区域为该应用选择“仍要打开”，确认来源后再继续。不要启用“任何来源”，也不要关闭 Gatekeeper。

1. 运行对应平台的“Codex 汉化增强工具”。
2. 等待工具自动检测 Codex Desktop。
3. Codex 未运行时，点击“汉化并启动”。
4. Codex 已运行时，点击“关闭并汉化重启”，阅读提示后确认。
5. 在运行日志中查看中文界面和原生菜单的验证结果。

软件关闭后，Codex 不会立刻变回英文。`localeOverride` 会由 Codex 保存；原生菜单翻译属于当前进程的运行时补丁，因此以后需要完整菜单汉化时仍建议通过本工具启动。

## 状态说明

| 状态 | 含义 |
|---|---|
| 汉化已完成 | 中文界面和当前全部原生菜单均通过验证 |
| 界面已汉化，菜单仍有遗漏 | 中文界面生效，但新版 Codex 出现了尚未收录的菜单标签 |
| 菜单已汉化，界面待确认 | 原生菜单已翻译，但页面语言检测暂时无法确认 |
| 汉化未完全生效 | 语言设置接口和菜单验证均未完全通过，具体原因会显示在日志中 |

这些状态描述的是本次启动的验证结果，不代表 Codex 安装损坏，也不代表必须重新安装客户端。

## 安全关闭机制

Windows 只处理当前 Store 安装目录或所选便携版目录中的 `Codex.exe` / `ChatGPT.exe`。macOS 只处理所选 `Codex.app` / `ChatGPT.app` Bundle 内的主程序和 Helper 进程。

- 关闭前始终显示确认对话框
- 先请求主窗口正常退出并等待 5 秒
- 强制终止前重新验证 PID、可执行路径和进程启动时间
- 路径无法验证或 PID 已被复用时拒绝终止
- 强制终止后最多等待 8 秒
- 仍有候选进程时停止自动重启

## 工作原理

启动器为本次 Codex 进程分配两个随机回环端口：

- Chromium DevTools 端口：调用 Codex 设置桥写入 `localeOverride`
- Electron 主进程 inspector：在内存中翻译原生菜单及之后重建的菜单

两个端口都绑定到 `127.0.0.1`。原生菜单补丁只存在于本次进程内，Codex 官方文件始终保持不变。

## 兼容性

| 平台 | 安装类型 | 支持状态 |
|---|---|---|
| Windows 10/11 x64 | Microsoft Store | 支持 |
| Windows 10/11 x64 | 便携版 | 支持 |
| Windows ARM64 | 任意 | 未验证 |
| macOS 13+ Apple Silicon | App Bundle | 支持 |
| macOS 13+ Intel | App Bundle | 支持 |
| Linux | 任意 | 暂不支持 |

OpenAI 更新 Electron 启动参数、设置接口或菜单结构后，本工具可能需要同步适配。遇到兼容问题时，请提交“Codex 版本兼容”Issue，并附上诊断输出和已脱敏日志。

## 构建与测试

Windows 项目使用系统自带的 .NET Framework 4.8 C# 编译器和 WPF。macOS 项目使用 Swift 5.9、Swift Package Manager 和 SwiftUI。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\test-shutdown.ps1
```

命令行检查：

```powershell
.\dist\Codex-Zh-Launcher-Windows-x64.exe --self-test
.\dist\Codex-Zh-Launcher-Windows-x64.exe --diagnostics
```

以下命令会实际启动 Codex，运行前需要完全退出客户端：

```powershell
.\dist\Codex-Zh-Launcher-Windows-x64.exe --launch-zh
.\dist\Codex-Zh-Launcher-Windows-x64.exe --launch-en
```

macOS 构建与测试：

```bash
bash scripts/test-macos.sh
bash scripts/build-macos.sh arm64   # Apple Silicon
bash scripts/build-macos.sh x86_64  # Intel
```

打包后的 macOS 应用也提供相同的 CLI：

```bash
"/Applications/Codex 汉化增强工具.app/Contents/MacOS/CodexZhLauncherMac" --self-test
"/Applications/Codex 汉化增强工具.app/Contents/MacOS/CodexZhLauncherMac" --diagnostics
"/Applications/Codex 汉化增强工具.app/Contents/MacOS/CodexZhLauncherMac" --launch-zh
"/Applications/Codex 汉化增强工具.app/Contents/MacOS/CodexZhLauncherMac" --launch-en
```

## 上传到 GitHub 的文件

必须上传的源码和构建文件：

```text
src/
tests/
macos/
shared/
scripts/
build.ps1
test-shutdown.ps1
```

建议同时上传：

```text
.github/
.gitignore
.gitattributes
README.md
README.en.md
CHANGELOG.md
CONTRIBUTING.md
SECURITY.md
LICENSE
THIRD_PARTY_NOTICES.md
docs/
```

不要提交：

```text
dist/             编译后的 exe，应放入 GitHub Releases
obj/              编译、测试和兼容性检查临时文件
macos/.build/      Swift Package 构建目录
macos/.artifacts/  macOS App Bundle 暂存目录
*.log             本地日志
*.user / *.suo    本机 IDE 配置
Codex app.asar    Codex 官方受版权保护的安装文件
账号、令牌、用户配置或项目内容
```

详细发布流程参见 [docs/PUBLISHING.md](docs/PUBLISHING.md)。

## 日志与隐私

日志位置：

```text
%LOCALAPPDATA%\CodexZhLauncher\logs\launcher.log
```

macOS 日志位于：

```text
~/Library/Logs/CodexZhLauncher/launcher.log
```

日志包含 Codex 安装类型、版本、汉化验证结果和进程关闭统计。提交 Issue 前应检查并移除不希望公开的本机路径或其他信息。

## 常见问题

### 工具会自动更新吗？

工具会在启动后检查本仓库的最新 GitHub Release，发现新版时询问是否打开下载页面，也可以在“关于”界面手动检查。当前版本不会静默下载或覆盖正在使用的 EXE / App Bundle。

### macOS 没有 Apple Developer ID 签名还能检查更新吗？

可以。更新检查和下载提示不依赖 Apple Developer ID。未签名和未公证主要影响自动替换后的首次打开与系统信任，因此当前 macOS 版本只提供可信来源提示，不自动覆盖 `.app`。

### Codex 更新后还可以继续使用吗？

小版本更新通常可以继续使用。如果设置接口、Electron 参数或菜单标签发生变化，工具可能显示具体未生效环节，并需要发布兼容更新。

### 为什么普通快捷方式启动后菜单还是英文？

原生菜单补丁依赖启动时注入的本地 inspector 参数。普通快捷方式启动的既有进程无法补加这些参数，需要通过本工具重新启动。

### 工具会修改 Codex 安装包吗？

不会。工具不修改 `app.asar`、Microsoft Store 安装目录或 Codex 可执行文件。

### Windows 提示无法验证发布者怎么办？

当前构建可能没有商业代码签名。请只从本仓库 Releases 下载，并使用 `SHA256SUMS.txt` 校验文件；不要关闭 Windows 的整体安全保护。

### macOS 为什么提示无法验证开发者？

macOS ZIP 使用免费的 ad-hoc 签名，没有 Apple Developer ID 签名和公证，因此首次打开可能出现 Gatekeeper 提示。请使用上文的右键“打开”或“隐私与安全性 > 仍要打开”，不要执行关闭 Gatekeeper、允许任何来源或自动清除 quarantine 的命令。

## 赞助支持

本项目由 [Kao La API](https://www.appkaola.com) 赞助支持。赞助关系不代表 OpenAI、Microsoft 或 Codex 对该服务的认可或官方合作。

## 贡献与安全

- 提交代码前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)
- 安全问题请按照 [SECURITY.md](SECURITY.md) 私下报告
- 版本变化参见 [CHANGELOG.md](CHANGELOG.md)

## 许可证与声明

项目采用 [MIT License](LICENSE)，实现为独立编写，不包含 CodexPlusPlus 源码，也不分发 Codex Desktop 安装包。

Codex、OpenAI 和相关商标归其权利人所有。本项目不是 OpenAI、Microsoft 或 Codex 官方产品。
