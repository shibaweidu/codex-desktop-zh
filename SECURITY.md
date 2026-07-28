# 安全策略

## 支持版本

| 版本 | 安全更新 |
|---|---|
| 0.5.x | 支持 |
| 0.4.x 及更早版本 | 不再支持 |

## 报告安全问题

请不要通过公开 Issue 披露以下问题：

- 可以终止安装目录外进程的路径验证绕过
- PID 复用导致的错误进程终止
- 调试端口暴露到非回环地址
- 凭据、令牌或用户项目内容泄露
- Release 二进制与公开源码不一致

建议在 GitHub 仓库启用 **Private vulnerability reporting**，并通过仓库的 Security 页面提交私密报告。报告应包含：

- 受影响版本
- Windows/macOS 和 Codex Desktop 版本
- 可复现步骤
- 预期结果与实际结果
- 已脱敏日志或最小复现代码

维护者确认问题前，请不要公开利用细节。确认后将根据影响范围提供修复版本和安全公告。

## 下载安全

只从本仓库的 GitHub Releases 下载二进制文件，并使用同一 Release 中的 `SHA256SUMS.txt` 校验。项目不会要求关闭 Windows Defender、SmartScreen 或 macOS Gatekeeper。
