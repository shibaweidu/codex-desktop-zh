import AppKit
import SwiftUI

struct AboutView: View {
    @ObservedObject var model: LauncherModel
    @Environment(\.dismiss) private var dismiss
    @State private var showsInstallConfirmation = false
    private let accent = Color(red: 0.77, green: 0.71, blue: 0.99)

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 16) {
                Image(nsImage: NSApplication.shared.applicationIconImage)
                    .resizable()
                    .frame(width: 58, height: 58)
                VStack(alignment: .leading, spacing: 4) {
                    Text("Codex 汉化增强工具")
                        .font(.system(size: 19, weight: .semibold))
                    Text("版本 \(LauncherModel.version)")
                        .font(.system(size: 12))
                        .foregroundStyle(.secondary)
                }
                Spacer()
                Button { dismiss() } label: {
                    Image(systemName: "xmark").frame(width: 24, height: 24)
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
                .help("关闭")
            }

            Text("为 Windows 和 macOS 提供 Codex Desktop 中文界面、原生菜单翻译和安全重启支持。")
                .font(.system(size: 13))
                .foregroundStyle(.secondary)
                .lineSpacing(4)
                .padding(.top, 22)

            Divider().padding(.vertical, 20)

            HStack(spacing: 12) {
                VStack(alignment: .leading, spacing: 4) {
                    Text("软件更新").font(.system(size: 12, weight: .semibold))
                    Text(model.updateStatus)
                        .font(.system(size: 12))
                        .foregroundStyle(model.availableUpdate == nil ? Color.secondary : accent)
                }
                Spacer()
                if model.checkingForUpdates || model.updating {
                    ProgressView().controlSize(.small)
                }
                Button(model.availableUpdate == nil ? "检查更新" : "重新检查") {
                    model.checkForUpdates()
                }
                .disabled(model.checkingForUpdates || model.updating)
            }

            if model.availableUpdate != nil {
                Button("立即更新") { showsInstallConfirmation = true }
                    .buttonStyle(.borderedProminent)
                    .tint(accent)
                    .foregroundStyle(Color(red: 0.08, green: 0.07, blue: 0.11))
                    .padding(.top, 14)
            }

            Spacer()

            HStack(spacing: 10) {
                Button("Kao La API 赞助支持") { model.openSponsor() }
                    .buttonStyle(.plain)
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                    .help("打开 www.appkaola.com")
                Spacer()
                Button("反馈支持") { model.openFeedback() }
                Button("GitHub 仓库") { model.openRepository() }
            }
        }
        .padding(26)
        .frame(width: 500, height: 360)
        .background(Color(red: 0.075, green: 0.08, blue: 0.095))
        .preferredColorScheme(.dark)
        .alert("安装更新？", isPresented: $showsInstallConfirmation) {
            Button("取消", role: .cancel) {}
            Button("退出并更新") { model.installAvailableUpdate() }
        } message: {
            Text("工具将下载新版本，退出当前进程后覆盖 App Bundle 并自动重启。")
        }
    }
}
