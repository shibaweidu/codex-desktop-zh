# 原型验收记录

验收日期：2026-11-19。环境：Windows、独立 Chrome/Chromium 浏览器会话 `codex-ui-62ad7dba5e79`。

## 已验证

| 项目 | 结果 | 证据 |
|---|---|---|
| 五页导航、来源切换、版本区别、获取确认/取消/模拟完成、主题预览/保存/恢复、日志筛选、重启确认、左侧赞助与材质层加载 | 34 项断言通过 | `interaction-results.json` |
| 1440×960、1024×768、375×812 下五个页面 | 15 个页面/尺寸组合均无横向溢出，左侧赞助入口保持可见 | `layout-*-results.json` |
| 系统减少动态效果偏好 | 已生效，Toast 的计算 transition 为 `0s` | 同上，`reducedMotion: true` |
| 五个页面的默认外观 axe-core 检查 | 0 项 violation；每页有 1 类渐变背景对比度待人工检查项 | `accessibility-results.json` |
| 六种工具配色的主题页 axe-core 检查 | 每款 0 项 violation，1 类渐变背景对比度待人工检查项 | `palette-accessibility-results.json` |
| 材质配色对比度样本 | 6 款配色共 36 个正文、辅助文字、导航和按钮保守顶光样本均达到 4.5:1；不冒充完整逐像素验收 | `material-contrast-results.json` |
| 关于弹窗按 Escape 关闭 | 弹窗关闭、焦点返回触发按钮均为 true | 浏览器实际键盘检查 |
| 纸白工具配色刷新后保持 | localStorage 为 `paper`，根节点主题为 `paper`，背景为 `#f3f2ef` | 浏览器刷新检查，检查后恢复默认 |
| JavaScript / JSON 语法 | `node --check` 通过；所有 JSON 可解析 | 本地终端检查 |
| 浏览器运行错误 | 未记录未捕获错误 | `agent-browser errors` |
| 视觉检查 | 实际渲染并查看首页、下载与换肤截图 | `screenshots/` |
| 预览服务 | `http://127.0.0.1:18777/` 返回 200，标题匹配 | 本机 HTTP 检查 |

初次无障碍检查发现区域命名、正文链接区分、无角色标签等问题，已修复。纸白配色的强调色和辅助文字对比度也已调整。本轮增加苹果风格材质后，axe 无法自动确定渐变/半透明表面后方的实际颜色，记录为 `incomplete`，不是声称对比度全部自动通过；另用保守材质颜色样本和实际截图进行补充检查。自动检查与样本检查不等于完整的屏幕阅读器或逐像素人工认证。

材质加载测试最初在 `file://` 下读取 `CSSStyleSheet.cssRules` 被浏览器同源机制阻止，已改为检查样式表已加载及赞助入口的实际计算样式，最终 34 项交互测试全部通过。这不是应用功能或后端访问失败。

## 重新运行

页面可直接双击 `index.html`。若需本机 HTTP 预览：

```powershell
python -m http.server 18777 --bind 127.0.0.1 --directory F:\CODEX\codex-zh-launcher\docs\ui-v0.8
```

浏览器交互测试需要 Node 与 `agent-browser`（首次 npx 获取工具需联网），与原型本身的离线使用无关：

```powershell
$session = 'codex-ui-review'
npx --yes agent-browser --session $session open file:///F:/CODEX/codex-zh-launcher/docs/ui-v0.8/index.html
Get-Content -Raw F:\CODEX\codex-zh-launcher\docs\ui-v0.8\test-interactions.js |
  npx --yes agent-browser --session $session eval --stdin

npx --yes agent-browser --session $session set viewport 1024 768
npx --yes agent-browser --session $session set media reduced-motion
Get-Content -Raw F:\CODEX\codex-zh-launcher\docs\ui-v0.8\test-layout.js |
  npx --yes agent-browser --session $session eval --stdin

npx --yes agent-browser --session $session a11y --json
npx --yes agent-browser --session $session close
```

交互测试需从新载入的页面开始，测试中只操作原型状态和原型 localStorage。

## 尚未验证 / 不属于本次交付

- 下载、签名与 SHA-256 本地验证、断点续传、实际安装和权限异常处理。
- Codex 主题接口与运行时注入、真实客户端中文页面和原生菜单验证。
- 真实进程发现、强制关闭、进程重生与 PID 复用。本原型不访问任何进程。
- WPF / SwiftUI 新页面移植、macOS 实机显示、两个架构构建与签名、公证。
- 新版工具的覆盖更新及 Release。本次不更改生产版本号，不提交或推送仓库。

上述项目不能用“原型按钮能点击”作为验收成功的证据。现有应用源码、汉化逻辑和构建流程均保持原样。
