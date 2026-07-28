import SwiftUI

struct LauncherView: View {
    @ObservedObject var model: LauncherModel
    private let accent = Color(red: 0.77, green: 0.71, blue: 0.99)
    private let warning = Color(red: 0.68, green: 0.58, blue: 0.95)

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider().overlay(Color.white.opacity(0.08))
            content
        }
        .frame(minWidth: 720, idealWidth: 820, minHeight: 560, idealHeight: 620)
        .background(Color(red: 0.075, green: 0.08, blue: 0.095))
        .preferredColorScheme(.dark)
        .alert("关闭并汉化重启？", isPresented: $model.showsRestartConfirmation) {
            Button("取消", role: .cancel) {}
            Button("关闭并重启", role: .destructive) { model.confirmRestart() }
        } message: {
            Text("Codex 正在执行的任务会被中断，未保存的输入可能丢失。工具会先请求正常退出，等待 5 秒后才强制终止仍在运行且路径验证通过的进程。")
        }
    }

    private var header: some View {
        HStack(spacing: 12) {
            logo
            VStack(alignment: .leading, spacing: 2) {
                Text("Codex 汉化增强工具")
                    .font(.system(size: 17, weight: .semibold))
                Text("v\(LauncherModel.version)")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }
            Spacer()
            iconButton("doc.badge.gearshape", help: "选择 Codex.app") { model.selectApplication() }
            iconButton("folder", help: "打开日志目录") { model.openLogFolder() }
        }
        .padding(.horizontal, 24)
        .frame(height: 72)
    }

    private var logo: some View {
        Group {
            if let icon = model.installedIcon {
                Image(nsImage: icon)
                    .resizable()
                    .renderingMode(.template)
                    .foregroundStyle(accent)
            } else {
                Image(systemName: "curlybraces.square.fill")
                    .resizable()
                    .foregroundStyle(accent)
            }
        }
        .scaledToFit()
        .frame(width: 38, height: 38)
        .accessibilityHidden(true)
    }

    private var content: some View {
        VStack(alignment: .leading, spacing: 18) {
            HStack(spacing: 14) {
                Circle()
                    .fill(model.statusIsWarning ? warning : accent)
                    .frame(width: 9, height: 9)
                    .shadow(color: (model.statusIsWarning ? warning : accent).opacity(0.45), radius: 5)
                VStack(alignment: .leading, spacing: 4) {
                    Text(model.statusTitle).font(.system(size: 15, weight: .semibold))
                    Text(model.statusDetail)
                        .font(.system(size: 12))
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                        .truncationMode(.middle)
                }
                Spacer()
                if model.busy { ProgressView().controlSize(.small).tint(accent) }
            }

            HStack {
                VStack(alignment: .leading, spacing: 4) {
                    Text(model.installTitle).font(.system(size: 14, weight: .medium))
                    Text(model.installDetail).font(.system(size: 12)).foregroundStyle(.secondary)
                }
                Spacer()
                if model.runningCount > 0 {
                    Text("\(model.runningCount) 个进程")
                        .font(.system(size: 11, weight: .medium))
                        .foregroundStyle(accent)
                }
            }
            .padding(.vertical, 2)

            VStack(alignment: .leading, spacing: 8) {
                Text("运行日志")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(.secondary)
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 7) {
                            ForEach(model.entries) { entry in
                                HStack(alignment: .firstTextBaseline, spacing: 10) {
                                    Text(entry.time)
                                        .font(.system(size: 11, design: .monospaced))
                                        .foregroundStyle(.tertiary)
                                    Text(entry.message)
                                        .font(.system(size: 12))
                                        .foregroundStyle(Color.white.opacity(0.82))
                                        .textSelection(.enabled)
                                }
                                .id(entry.id)
                            }
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(14)
                    }
                    .background(Color.black.opacity(0.2))
                    .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.white.opacity(0.08)))
                    .clipShape(RoundedRectangle(cornerRadius: 6))
                    .onChange(of: model.entries.count) { _ in
                        if let id = model.entries.last?.id { proxy.scrollTo(id, anchor: .bottom) }
                    }
                }
            }
            .frame(maxHeight: .infinity)

            HStack(spacing: 10) {
                Button("英文启动") { model.launchEnglish() }
                    .buttonStyle(.borderless)
                    .foregroundStyle(.secondary)
                    .disabled(model.busy || model.install == nil || model.runningCount > 0)
                Spacer()
                Button(action: model.primaryAction) {
                    HStack(spacing: 8) {
                        Image(systemName: model.runningCount > 0 ? "arrow.clockwise" : "play.fill")
                        Text(model.primaryTitle)
                    }
                    .font(.system(size: 13, weight: .semibold))
                    .frame(minWidth: 168, minHeight: 32)
                }
                .buttonStyle(.borderedProminent)
                .tint(model.runningCount > 0 ? warning : accent)
                .foregroundStyle(Color(red: 0.08, green: 0.07, blue: 0.11))
                .disabled(model.busy)
            }
        }
        .padding(.horizontal, 24)
        .padding(.vertical, 20)
    }

    private func iconButton(_ symbol: String, help: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol).frame(width: 28, height: 28)
        }
        .buttonStyle(.plain)
        .foregroundStyle(.secondary)
        .help(help)
    }
}
