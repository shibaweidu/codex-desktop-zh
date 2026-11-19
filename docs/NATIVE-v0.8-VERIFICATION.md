# v0.8.0 原生实现验收记录

日期：2026-11-19。Windows 本地验收通过；macOS 双架构编译和测试由 v0.8.0 发布工作流执行。本机未进行 macOS 实机验收。

## 交付文件

Windows 最新本地可执行文件：`F:\CODEX\codex-zh-launcher\dist\v0.8.0-release\Codex-Zh-Launcher-Windows-x64.exe`。

SHA-256：`CA64AC39151AA4CA07383FFD30EF6EF66569AA914FD2C815F86D68D6DD6D15E7`，可用 `Get-FileHash -Algorithm SHA256` 复核。

此前默认 `dist/` 中的测试实例发生文件占用（编译器 CS0016）；改用独立输出目录构建成功。发布工作流会从本次源代码重新构建平台资产。

## 本轮实现

- 原生 WPF 与 SwiftUI 五页工作台：概览、下载 Codex、界面汉化、主题外观、日志诊断。
- 深灰材质、淡紫强调色、圆角控件与轻微阴影；左侧赞助入口；保留系统窗口按钮。
- 独立客户端下载服务与工具自更新分离。客户端更新固定使用社区镜像，下载记录锁定平台/架构版本、长度和 SHA-256，校验通过后由用户确认安装。
- 自动识别 Windows Store 版本与系统架构；macOS 识别应用版本、构建号和芯片架构。
- 六款自有运行时配色和字号预览，平台共用 theme-script.js；仅连接本工具启动且进程身份可验证的会话。
- 设置保存失败显示原因并回退内存偏好；日志搜索、过滤、复制、导出；自动检查工具更新开关。
- 保留已有语言、菜单翻译和路径验证关闭内核。启动/下载/换肤/自更新互斥；退出下载时等待清理；更新程序接管时允许正常退出。

## 已执行结果

| 检查 | 结果 | 范围 |
|---|---|---|
| Windows Release 构建 | 通过 | 最终 v0.8.0 目录 EXE |
| `--self-test` | 通过，退出码 0 | 语言、菜单、更新器、共享主题资源 |
| `--diagnostics` | 通过，退出码 0 | 只读；Store 包 26.1111.2228.0；检测到运行进程 |
| `test-workspace.ps1 -LiveManifest .\obj\mirror-manifest.json` | 46 项通过 | 下载解析、哈希/大小、流式取消、设置与主题资源、四平台/架构实时清单解析 |
| `test-shutdown.ps1` | 5 组通过 | 临时进程路径过滤、正常退出、超时强杀、重生、自然退出 |
| Chromium 样式隔离测试 | 21 项通过 | 六配色、跨平台预设一致、实际计算样式、恢复、回滚、非法输入、无 bridge/root |
| 原生 WPF 界面 | 部分通过 | 五页检查、深色下拉、配色预览、日志搜索及清空恢复、版本与关于入口；最终关于弹窗已复查无重叠 |
| 重启取消 | 已观察取消日志 | `ui.restart.cancelled`，真实 Codex 仍为 12 个进程；未确认关闭 |
| `git diff --check` | 通过 | 无空白错误 |

在线清单读取自 `https://codexapp.agentsmirror.com/latest/manifest`，schema 5，generatedAt 2026-11-18T02:22:28Z。Windows 应用版本 26.1111.21308 与 MSIX 包版本 26.1111.2228.0 分开展示；macOS 应用版本 26.1111.71025。清单来自社区项目，不将其 SHA-256 称为 OpenAI 签名。

## 调试中发现并处理

- 原关于弹窗新增设置容器遗漏 Grid.Row，导致标题与说明重叠；已补 Row=1 并重建；最终 v0.8.0 目录程序已实测复查，标题、说明、更新开关及按钮无重叠。
- Windows 默认下拉为亮色；改原生深色模板，补字段级可访问名称和按钮焦点/禁用反馈。
- 工具强调色此前仅改变一次主按钮，未统一保持；改为保存后及启动时同步主按钮、选中导航、进度，悬停/按下状态使用相应配色。
- 切换安装及主进程身份变化会清理过期会话；主题应用重新校验 PID 和启动标识。
- 旧错误状态不会覆盖新关闭失败；英文启动不再显示中文启动成功。
- 退出保护不再阻断自更新交接；关于窗口的更新操作复用主窗口同一互斥路径。
- 测试环境最初缺少 Playwright 对应的 headless shell；使用本机已有的 Chromium 1228 完成真实 DOM 隔离测试。CI 则为固定 Playwright 1.62.1 安装匹配 Chromium。

## 仍未验证，不能当成发布验收通过

- 本机为 Windows，未安装 Swift/Xcode，不能本地编译 macOS。发布前须确认 GitHub Actions 的 Apple Silicon / Intel 构建与测试均成功；CI 不替代 Mac 实机验收。
- Apple Silicon/Intel 的完整构建、ad-hoc 签名、首次打开、DMG 下载和实际启动链路需要 Mac/CI 验收。
- 没有自动关闭、安装或修改真实 Codex。新版 Codex 的真实汉化、菜单、运行时换肤和会话重启应由用户在无运行任务时手动验收。
- 未下载约 GB 级安装包；未实际覆盖安装客户端。单元测试验证了小型流的长度/哈希/取消，不能代替完整网络下载与安装测试。
- 窗口最小尺寸、多显示器和不同 DPI、键盘完整导航、GUI 导出/剪贴板、下载过程中关窗、真实工具自更新交接仍需补充系统级验收。
- 本轮桌面输入多次被人工操作/最小化打断；不把未完成的窗口操作计为通过，也不把网页原型测试算入上述原生结果。

## 本地重放

```powershell
cd F:\CODEX\codex-zh-launcher
.\build.ps1 -Configuration Release -OutputDirectory dist\v0.8.0-release
.\test-workspace.ps1 -LiveManifest .\obj\mirror-manifest.json
.\test-shutdown.ps1
# 这两项只读，不启动或关闭真实客户端
& .\dist\v0.8.0-release\Codex-Zh-Launcher-Windows-x64.exe --self-test
& .\dist\v0.8.0-release\Codex-Zh-Launcher-Windows-x64.exe --diagnostics
```

浏览器隔离测试（需要 Node 与 Playwright；生产程序不依赖它）：

```sh
npm install --no-save --package-lock=false playwright@1.62.1
npx playwright install chromium
node tests/theme-script.test.cjs
```

已有其他 Chromium 时可通过 `PLAYWRIGHT_CHROMIUM_EXECUTABLE` 指定路径；未配置时使用 Playwright 默认浏览器。

macOS 验收：

```sh
bash scripts/test-macos.sh
CODEX_ZH_SHARED_DIR="$PWD/shared" swift run --package-path macos CodexZhLauncherMac --self-test
bash scripts/build-macos.sh arm64
bash scripts/build-macos.sh x86_64
```

## 人工验收顺序

1. 使用最终目录的 EXE，确认关于版本 0.8.0、左侧赞助、五页切换与无文字遮挡。
2. 下载页核对芯片/架构与来源，完成下载后检查结果；取消时确认未验证文件没有变成可安装文件。
3. 主题页先只预览，确认不改变真实 Codex；工具强调色保存后重开确认保持。
4. 在 Codex 没有未完成任务时，经确认执行中文启动/关闭重启；查看界面与菜单分别验证的日志。
5. 应用 Codex 配色，确认根界面颜色；点击恢复确认原有外观恢复；不把代码区/原生菜单未变色当成完整皮肤能力。
6. 导出脱敏日志；在双架构 Mac 与 Windows 多 DPI 上完成余下系统级检查。确认 CI 全部通过后，再依据 Releases 资产进行下载验收。
