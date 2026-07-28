# 参与贡献

感谢你帮助改进 Codex Desktop 中文汉化增强工具。兼容性问题通常来自 Codex 更新后的设置接口或原生菜单变化，因此高质量的版本信息和脱敏日志非常重要。

## 开发环境

- Windows 10 或 Windows 11 x64
- .NET Framework 4.8
- Windows PowerShell 5.1 或 PowerShell 7
- Microsoft Store 版或便携版 Codex Desktop（仅手动集成测试需要）

macOS 开发需要 macOS 13+、Xcode Command Line Tools 和 Swift 5.9。真实 Codex 仅用于明确的人工集成测试。

项目使用 Windows 自带的 C# 编译器，不要求 Visual Studio 或 .NET SDK。

## 本地检查

提交代码前运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\test-shutdown.ps1
```

然后运行：

```powershell
.\dist\Codex-Zh-Launcher-Windows-x64.exe --self-test
.\dist\Codex-Zh-Launcher-Windows-x64.exe --diagnostics
```

macOS 上运行：

```bash
bash scripts/test-macos.sh
bash scripts/build-macos.sh "$(uname -m)"
```

`test-shutdown.ps1` 只使用临时测试进程，不应关闭用户当前运行的真实 Codex。

## 提交菜单翻译

菜单翻译位于 `shared/menu-translations.json`，脚本位于 `shared/`。新增条目时：

1. 保留英文标签的大小写和标点。
2. 同时覆盖三个点 `...` 与省略号 `…` 变体（如果 Codex 同时使用）。
3. 放入 `common` 或对应平台字典，并使用符合平台习惯的中文命令名称。
4. 更新 `SelfTest()`，确保关键兼容条目不会被误删。
5. 在 `CHANGELOG.md` 中记录新增标签。

## 进程安全要求

涉及关闭进程的变更必须保持以下约束：

- Windows 只处理 `Codex.exe` / `ChatGPT.exe`，macOS 只处理 App Bundle 内的主程序和 Helper
- 可执行路径必须属于当前检测到的安装目录
- 终止前重新验证 PID、路径和启动时间
- 路径不可读取时拒绝终止
- 自动测试不得操作真实 Codex

降低这些限制的提交不会被接受。

## Pull Request

- 每个 PR 聚焦一个问题或功能。
- 描述受影响的 Codex 版本和安装类型。
- UI 变更附上更新前后的截图。
- 不要提交 `dist/`、`obj/`、日志、Codex 安装文件或用户数据。
- 确保自动构建、自检和隔离关闭测试全部通过。

## 报告问题

优先使用仓库提供的 Issue 模板。日志中可能包含安装路径，上传前请先检查并脱敏。安全漏洞不要创建公开 Issue，请按照 `SECURITY.md` 报告。
