# Changelog

本项目遵循 [Semantic Versioning](https://semver.org/)。

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
