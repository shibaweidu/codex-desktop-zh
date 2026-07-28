# GitHub 发布指南

## 仓库信息

推荐仓库名：

```text
codex-desktop-zh
```

推荐 Description：

```text
Windows 与 macOS Codex Desktop 中文汉化增强工具，支持安全关闭、中文重启和原生菜单翻译。
```

推荐 Topics：

```text
openai-codex
codex-desktop
codex-chinese
chinese-localization
localization
i18n
electron
windows
macos
swiftui
wpf
dotnet
chinese
```

## 上传范围

运行以下命令可以查看将被提交的文件：

```powershell
git status --short
git check-ignore -v dist obj
```

上传 `src/`、`tests/`、`macos/`、`shared/`、`scripts/`、构建和测试脚本、`.github/`、文档、许可证与第三方声明。不要上传 `dist/`、`obj/`、`macos/.build/`、`macos/.artifacts/`、日志、Codex 安装文件、账号数据或用户项目内容。

## 创建仓库

在 GitHub 创建一个空仓库，不要自动生成 README、License 或 `.gitignore`，然后在项目目录运行：

```powershell
git init
git branch -M main
git add .
git status
git commit -m "Initial release: Codex Chinese localization enhancer v0.5.0"
git remote add origin https://github.com/OWNER/codex-desktop-zh.git
git push -u origin main
```

将 `OWNER` 替换为实际 GitHub 用户名或组织名。首次提交前必须检查 `git status`，确认没有 `dist/`、`obj/` 和本地日志。

## About 与搜索优化

在仓库右侧 **About** 区域填写 Description、Topics 和项目网站。README 首段已经自然覆盖以下搜索词：

- Codex 汉化
- Codex 中文
- Codex Desktop 中文版
- Codex Desktop 汉化工具
- OpenAI Codex Windows
- Codex Chinese localization

不要在标题或正文中重复堆砌关键词，也不要使用“破解”“官方中文版”或“去签名”等容易误导的描述。

## 截图与 Social Preview

建议使用 `0.5.0` 的 Windows 与 macOS 实际窗口截图，并确保截图中没有用户名、项目名称、路径、任务内容或其他个人信息。

推荐文件：

```text
docs/images/codex-desktop-zh-main.png
docs/images/codex-desktop-zh-restart.png
docs/images/codex-desktop-zh-log.png
```

截图建议尺寸至少为 1200 像素宽。GitHub Social Preview 使用 `1280 x 640` PNG，在仓库 **Settings > General > Social preview** 中上传。

## 自动化检查

推送或创建 Pull Request 后，`.github/workflows/build.yml` 会分别执行 Windows 与 macOS 构建：

1. Release 构建
2. `--self-test`
3. `--diagnostics`
4. 隔离关闭测试
5. Swift 单元测试、两个 macOS 架构构建、ad-hoc 签名与 ZIP 检查
6. 上传各平台构建 Artifact

这些测试不会关闭用户真实运行的 Codex。

## 创建 Release

确认 `main` 分支自动化通过后创建并推送版本标签：

```powershell
git tag -a v0.5.0 -m "Codex 汉化增强工具 v0.5.0"
git push origin v0.5.0
```

`.github/workflows/release.yml` 会重新构建和测试，并创建 GitHub Release，附加：

```text
Codex-Zh-Launcher-Windows-x64.exe
Codex-Zh-Launcher-macOS-arm64.zip
Codex-Zh-Launcher-macOS-x64.zip
SHA256SUMS.txt
```

发布后在 Windows 10/11 x64、Apple Silicon Mac 与 Intel Mac 上分别复核下载、SHA-256、首次打开、Gatekeeper 提示、自动检测和汉化启动。macOS 包只有 ad-hoc 签名，不应描述为已公证或无安全提示。

## 发布检查清单

- 版本号已同步到 Windows、macOS Info.plist 构建模板、主窗口和 README
- `CHANGELOG.md` 已更新
- Release 构建成功
- 自检、诊断和隔离关闭测试通过
- 菜单翻译已在当前 Codex 版本验证
- 截图不包含个人或项目数据
- Release 文件 SHA-256 与 `SHA256SUMS.txt` 一致
- README 的兼容性和已知限制准确
- 明确标注非 OpenAI 官方项目
- macOS 未关闭 Gatekeeper、未清除 quarantine、未修改 Codex.app
