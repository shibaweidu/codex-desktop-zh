# Changelog

本项目遵循 [Semantic Versioning](https://semver.org/)。

## [0.7.6] - 2026-07-30

### Fixed

- macOS 只选择带真实 `#root` 应用内容的渲染目标，不再把空白外层页面当作 Codex 界面
- i18n 引导不再重定义 `__STATSIG__` 全局属性，避免阻断新版 Codex 初始化
- 只有实际补丁到 Statsig 客户端和动态配置后才报告引导成功
- 补齐 macOS 原生菜单中的系统编辑和朗读标签，并忽略 ChatGPT/Codex 品牌名

## [0.7.5] - 2026-07-29

### Fixed

- macOS 在页面加载前启用 Codex 动态配置 `72216192` 中的 `enable_i18n`，使官方中文资源实际参与渲染
- 强制动态配置的 `locale_source` 为 `SYSTEM`，并同步覆盖 `navigator.language` / `navigator.languages`
- 通过 `Page.addScriptToEvaluateOnNewDocument` 注册补丁，写入 `localeOverride` 后在同一进程内刷新，避免完整重启导致补丁丢失
- DevTools 命令增加通用发送逻辑及 CSP 安全求值选项，日志记录 i18n 引导安装结果

## [0.7.4] - 2026-07-29

### Fixed

- macOS 枚举并探测 `page`、`iframe` 与 `webview` 调试目标，避免把空的外层窗口当作 Codex 实际界面
- 写入语言设置时选择带 `electronBridge` 的目标，验证时选择含有实际页面文本的目标
- 页面日志增加最终目标类型和探测文本长度，便于识别新版 Codex 的渲染结构变化
- Electron 应用菜单为空时报告 `partial`，不再以零检查项误报菜单汉化完成

## [0.7.3] - 2026-07-29

### Fixed

- macOS 写入 `localeOverride` 后完整关闭并重新启动 Codex，使语言资源在主进程启动阶段重新加载
- 仅在最终页面语言或中文界面标记通过校验后报告汉化成功，避免设置写入成功造成误报
- macOS 原生菜单注入兼容新版 Node/Electron，增加 `getBuiltinModule` 与 `createRequire` 加载路径
- 页面校验等待实际内容加载，并记录文本长度、页面语言及中英文标记统计

## [0.7.2] - 2026-07-29

### Fixed

- macOS 自动更新暂存路径统一解析为真实路径，避免系统临时目录别名导致 App Bundle 误判
- 更新辅助进程重新校验暂存路径前执行相同的路径规范化
- 更新包改为直接解析 `Info.plist`，避免 `Bundle` 缓存影响版本和主程序校验
- macOS 更新器集成测试覆盖 Release ZIP 解压和带符号链接父目录的暂存 App

## [0.7.1] - 2026-07-29

### Fixed

- macOS 等待 DevTools WebSocket 握手成功后再执行汉化脚本，并使用 CDP 文本帧
- macOS 为随机回环调试端口配置精确 Origin，兼容新版 Codex / Electron 的连接校验
- WebSocket 握手失败时记录具体错误，不再只显示“Socket 未连接”

## [0.7.0] - 2026-07-29

### Added

- Windows 一键下载、SHA-256 校验、备份覆盖、自动重启和失败回滚
- macOS 按架构下载 ZIP，校验 Bundle ID、版本、架构与 ad-hoc 签名后覆盖重启
- 独立临时更新进程，当前工具退出后再替换 EXE 或 App Bundle
- 安装目录不可写、App Translocation 或校验失败时提供手动下载回退

### Security

- 更新文件名、GitHub 仓库路径、Release 地址和下载大小均执行固定约束
- 更新流程只替换当前汉化增强工具，不关闭或修改 Codex

## [0.6.1] - 2026-07-29

### Added

- Windows 与 macOS 顶部增加 Kao La API 赞助支持链接
- 关于界面和中英文 README 增加赞助信息与关系声明

## [0.6.0] - 2026-07-29

### Added

- Windows 与 macOS 启动后后台检查 GitHub Releases，发现新版时显示下载提示
- 关于界面显示当前版本、检查更新、反馈支持和 GitHub 仓库入口
- 更新响应版本比较、Release 地址校验和离线自动测试

### Security

- 首期更新功能不静默下载或覆盖程序，只打开固定仓库的 GitHub Release 页面

## [0.5.1] - 2026-07-29

### Fixed

- Windows Release EXE 始终嵌入项目自有的多尺寸淡紫色应用图标
- 窗口标题栏、任务栏和界面页眉统一使用同一图标资源
- 增大图标有效绘制区域，改善桌面大、中、小图标下的显示尺寸

## [0.5.0] - 2026-07-28

### Added

- macOS 13+ 原生 SwiftUI 应用，支持 Apple Silicon 与 Intel 独立 ZIP
- App Bundle 自动检测、手动选择、Info.plist 解析和路径保存
- 基于 libproc 的主进程与 Helper 进程安全枚举、PID 身份复核和强制关闭
- macOS DevTools、语言设置、菜单汉化、GUI 日志和 CLI 命令
- ad-hoc 签名、Bundle 校验、ZIP 解压检查和跨平台 Release 汇总流程
- `common`、`windows`、`macos` 分组的共享翻译与脚本资源

### Changed

- Windows 与 macOS 共用汉化脚本和菜单翻译数据
- Windows Release 资产固定命名为 `Codex-Zh-Launcher-Windows-x64.exe`

## [0.4.1] - 2026-07-28

### Added

- 淡紫色应用 Logo、运行状态和重启按钮视觉样式
- 新版 Codex 原生菜单标签翻译：Reload、Force Reload、Toggle Developer Tools、Minimize、Zoom
- 语言设置请求格式兼容性自检

### Changed

- 适配 Codex Desktop `26.721.4979.0` 的设置接口请求格式
- 汉化未完全生效时，明确区分中文界面与原生菜单状态
- 菜单翻译条目从 81 项增加到 86 项

## [0.4.0] - 2026-07-28

### Added

- Codex 运行时主按钮切换为“关闭并汉化重启”
- 先正常退出、超时后强制终止的关闭流程
- PID、可执行路径和启动时间重新验证
- 结构化关闭统计、可见日志和本地日志
- 等待期间出现新目标进程的重新扫描与处理
- 隔离进程测试夹具

### Changed

- 精简深色桌面界面
- 增加标准最小化、最大化和关闭按钮
- 使用 Codex 官方图标作为应用标识

## [0.3.0] - 2026-07-28

### Added

- Microsoft Store 与便携版 Codex 自动检测
- 官方 `localeOverride` 设置调用
- Electron 原生菜单运行时翻译
- 自检、诊断和本地日志
