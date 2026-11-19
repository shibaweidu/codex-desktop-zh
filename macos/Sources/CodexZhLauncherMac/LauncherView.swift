import AppKit
import SwiftUI

private enum WorkspacePage: String, CaseIterable, Identifiable {
    case overview = "概览", downloads = "下载 Codex", localization = "界面汉化", themes = "主题外观", diagnostics = "日志诊断"
    var id: String { rawValue }
    var symbol: String {
        switch self {
        case .overview: return "square.grid.2x2"
        case .downloads: return "arrow.down.circle"
        case .localization: return "character.bubble"
        case .themes: return "paintpalette"
        case .diagnostics: return "text.alignleft"
        }
    }
}

struct LauncherView: View {
    @ObservedObject var model: LauncherModel
    @State private var page: WorkspacePage = .overview
    @State private var hasAutoCheckedCodexUpdate = false
    @State private var themeID = "violet"
    @State private var toolTarget = false
    @State private var fontSize = 14.0
    @State private var logSearch = ""
    @State private var errorsOnly = false

    private var accent: Color {
        let hex = WorkspaceTheme.find(model.toolTheme)?.accent ?? "#c4b5fd"
        return hex == "#6b557f" ? color("#c4b5fd") : color(hex)
    }
    private var selectedTheme: WorkspaceTheme { WorkspaceTheme.find(themeID) ?? WorkspaceTheme.all[0] }
    private var architecture: String {
        #if arch(arm64)
        return "arm64"
        #else
        return "x64"
        #endif
    }

    var body: some View {
        HStack(spacing: 0) {
            sidebar
            Divider().overlay(Color.white.opacity(0.06))
            VStack(alignment: .leading, spacing: 0) {
                Text("工作台 / \(page.rawValue)")
                    .font(.system(size: 12)).foregroundStyle(.secondary)
                    .padding(.horizontal, 32).padding(.top, 22).padding(.bottom, 10)
                ScrollView {
                    VStack(alignment: .leading, spacing: 22) {
                        switch page {
                        case .overview: overview
                        case .downloads: downloads
                        case .localization: localization
                        case .themes: themes
                        case .diagnostics: diagnostics
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.horizontal, 32).padding(.top, 10).padding(.bottom, 28)
                }
                Divider()
                HStack(spacing: 10) {
                    if model.working { ProgressView().controlSize(.small) }
                    Circle().fill(accent).frame(width: 6, height: 6)
                    Text(model.downloads.busy ? model.downloads.status : model.statusTitle)
                        .font(.system(size: 11)).foregroundStyle(.secondary).lineLimit(1)
                    Spacer()
                    Text("本地运行").font(.system(size: 11)).foregroundStyle(.secondary)
                }.padding(.horizontal, 24).frame(height: 43)
            }
        }
        .frame(minWidth: 960, idealWidth: 1140, minHeight: 680, idealHeight: 800)
        .background(color("#1c1d23"))
        .preferredColorScheme(.dark).tint(accent)
        .onAppear {
            restoreThemeSelection()
            fontSize = Double(max(12, min(18, UserDefaults.standard.object(forKey: "workspace.fontSize") as? Int ?? 14)))
        }
        .onChange(of: toolTarget) { _ in restoreThemeSelection() }
        .onChange(of: page) { value in
            if value == .downloads && !hasAutoCheckedCodexUpdate {
                hasAutoCheckedCodexUpdate = true
                model.checkCodexUpdate(architecture: architecture)
            }
        }
        .alert("关闭并汉化重启？", isPresented: $model.showsRestartConfirmation) {
            Button("取消", role: .cancel) {}
            Button("关闭并重启", role: .destructive) { model.confirmRestart() }
        } message: {
            Text("Codex 正在执行的任务会被中断，未保存的输入可能丢失。工具会先请求正常退出，等待 5 秒后才强制终止仍在运行且路径验证通过的进程。")
        }
        .alert("发现新版本", isPresented: $model.showsUpdateAlert) {
            Button("稍后", role: .cancel) {}
            Button("立即更新") { model.installAvailableUpdate() }
        } message: {
            Text((model.availableUpdate?.message ?? "GitHub Releases 中有可用的新版本。") + "。工具将退出、覆盖并自动重启。")
        }
        .alert("自动更新失败", isPresented: $model.showsUpdateFailureAlert) {
            Button("取消", role: .cancel) {}
            Button("打开下载页") { model.openAvailableUpdate() }
        } message: { Text(model.updateFailureMessage) }
        .alert("安装 Codex 更新？", isPresented: $model.showsCodexUpdateConfirmation) {
            Button("取消", role: .cancel) {}
            Button("安装并重启", role: .destructive) { model.installCodexUpdate() }
        } message: {
            Text("将用社区镜像中已校验的版本覆盖 \(model.install?.bundleURL.lastPathComponent ?? "Codex.app")。先退出 Codex并保存工作；如果应用位于系统 Applications，macOS 会请求管理员授权后再写入。不会强制关闭 Codex，也不会修改 Gatekeeper 设置。")
        }
        .alert("Codex 更新未完成", isPresented: $model.showsCodexUpdateFailure) {
            Button("知道了", role: .cancel) {}
        } message: { Text(model.codexUpdateFailureMessage) }
        .sheet(isPresented: $model.showsAbout) { AboutView(model: model) }
    }

    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 12) {
                Group {
                    if let icon = model.installedIcon { Image(nsImage: icon).resizable().renderingMode(.template) }
                    else { Image(systemName: "curlybraces.square.fill").resizable() }
                }.scaledToFit().foregroundStyle(accent).frame(width: 46, height: 46).accessibilityHidden(true)
                VStack(alignment: .leading, spacing: 3) {
                    Text("Codex").font(.system(size: 23, weight: .semibold))
                    Text("汉化增强工具").font(.system(size: 11)).foregroundStyle(.secondary)
                }
            }.padding(.bottom, 32).padding(.horizontal, 6)
            ForEach(WorkspacePage.allCases) { item in
                Button { page = item } label: {
                    Label(item.rawValue, systemImage: item.symbol)
                        .font(.system(size: 13, weight: page == item ? .semibold : .regular))
                        .frame(maxWidth: .infinity, minHeight: 43, alignment: .leading)
                        .padding(.horizontal, 13)
                        .foregroundStyle(page == item ? accent : Color.white.opacity(0.72))
                        .background(page == item ? accent.opacity(0.12) : Color.clear, in: RoundedRectangle(cornerRadius: 10))
                }.buttonStyle(.plain).padding(.bottom, 7)
            }
            Spacer(minLength: 25)
            Button { model.openSponsor() } label: {
                VStack(alignment: .leading, spacing: 6) {
                    HStack { Text("KAO LA API").font(.system(size: 14, weight: .semibold)); Spacer(); Image(systemName: "arrow.up.right").font(.system(size: 10)) }
                    Text("一站式顶级大模型调用平台").font(.system(size: 11)).foregroundStyle(.secondary)
                    Text("appkaola.com").font(.system(size: 10)).foregroundStyle(.tertiary)
                }.padding(14).background(Color.white.opacity(0.04), in: RoundedRectangle(cornerRadius: 12))
                    .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.white.opacity(0.08)))
            }.buttonStyle(.plain).help("打开 www.appkaola.com")
            Button { model.showsAbout = true } label: {
                Label("设置与关于", systemImage: "gearshape").frame(maxWidth: .infinity, minHeight: 43, alignment: .leading)
            }.buttonStyle(.plain).padding(.top, 15).disabled(model.working)
            Text("本地运行     v\(LauncherModel.version)").font(.system(size: 10)).foregroundStyle(.secondary).padding(.top, 8)
        }.padding(.horizontal, 18).padding(.top, 28).padding(.bottom, 20)
            .frame(width: 222).background(.ultraThinMaterial)
    }

    private var overview: some View {
        Group {
            heading("一切就绪，开始创造。", "中文启动与个性化，一处管理。")
            materialCard {
                Text("Codex Desktop").font(.system(size: 25, weight: .semibold))
                Text(model.installDetail).font(.system(size: 12)).foregroundStyle(.secondary)
                Divider().padding(.vertical, 8)
                Label(model.statusTitle, systemImage: model.statusIsWarning ? "exclamationmark.circle" : "checkmark.circle").foregroundStyle(accent)
                Text(model.statusDetail).font(.system(size: 12)).foregroundStyle(.secondary).textSelection(.enabled)
                HStack(spacing: 12) {
                    Button(model.primaryTitle, action: model.primaryAction).buttonStyle(.borderedProminent).controlSize(.large)
                    if model.runningCount > 0 { Text("\(model.runningCount) 个进程").font(.system(size: 11)).foregroundStyle(.secondary) }
                }.padding(.vertical, 10).disabled(model.working)
                HStack(spacing: 16) {
                    Button("以英文启动", action: model.launchEnglish).disabled(model.runningCount > 0 || model.install == nil)
                    Button("选择应用", action: model.selectApplication)
                    Button("重新检测") { model.refreshDetection(announce: true) }
                }.disabled(model.working)
            }
            HStack {
                Button("下载与安装") { page = .downloads }.controlSize(.large)
                Button("换个外观") { page = .themes }.controlSize(.large)
            }
            Text("最近操作").font(.system(size: 14, weight: .semibold))
            materialCard {
                if model.entries.isEmpty { Text("等待操作记录").foregroundStyle(.secondary) }
                ForEach(Array(model.entries.suffix(3))) { entry in
                    Text("\(entry.time)  \(String(entry.message.prefix(180)))").font(.system(size: 12)).foregroundStyle(.secondary).lineLimit(3)
                }
            }
        }
    }

    private var downloads: some View {
        Group {
            heading("Codex 更新", "只使用 Wangnov 社区镜像；自动识别本机版本与芯片。")
            materialCard {
                Text(model.installDetail).font(.system(size: 12)).foregroundStyle(.secondary).textSelection(.enabled)
                Text("已自动识别芯片：\(architecture == "arm64" ? "Apple Silicon" : "Intel")").font(.system(size: 12)).foregroundStyle(.secondary)
                Text(model.downloads.status).font(.system(size: 12)).foregroundStyle(.secondary).textSelection(.enabled)
                if model.downloads.file != nil && model.runningCount > 0 {
                    Label("Codex 正在运行。先保存工作并退出，之后即可安装更新。", systemImage: "exclamationmark.triangle.fill")
                        .font(.system(size: 12)).foregroundStyle(color("#c4b5fd"))
                }
                Button(model.downloads.file == nil ? "检查并下载更新" : "安装并重启 Codex") {
                    if model.downloads.file == nil { model.checkCodexUpdate(architecture: architecture) }
                    else { model.requestCodexUpdateInstall() }
                }.controlSize(.large).disabled(model.working || model.install == nil || (model.downloads.file != nil && model.runningCount > 0))
            }.disabled(model.working)
            materialCard {
                if model.downloads.busy {
                    ProgressView().progressViewStyle(.linear)
                    Button("取消下载") { model.downloads.cancel() }
                }
                Text("安装前校验镜像清单、ZIP SHA-256、Apple 签名、Team ID 与芯片架构。系统 Applications 可能要求管理员授权；不关闭 Gatekeeper。")
                    .font(.system(size: 12)).foregroundStyle(.secondary)
                Button("重新检测版本") { model.checkCodexUpdate(architecture: architecture) }.disabled(model.working || model.install == nil)
                Link("社区镜像项目", destination: URL(string: "https://github.com/Wangnov/codex-app-mirror")!)
            }
        }
    }

    private var localization: some View {
        Group {
            heading("界面汉化", "沿用现有语言、原生菜单和安全关闭流程。")
            materialCard {
                ForEach(["1   确认安装与进程路径", "2   设置简体中文并加载官方资源", "3   验证页面实际语言", "4   检查原生菜单与未覆盖标签"], id: \.self) { Text($0).padding(.vertical, 8) }
                Text("页面与菜单分别验证。未完成的步骤和遗漏标签会写入日志，不把请求成功当成全部汉化完成。")
                    .font(.system(size: 12)).foregroundStyle(.secondary)
                Button("转到启动控制") { page = .overview }
                Button("查看运行日志") { page = .diagnostics }
            }
        }
    }

    private var themes: some View {
        Group {
            heading("让工作台，有你的样子。", "项目自有纯色预设，不下载第三方素材和脚本。")
            materialCard {
                Picker("应用对象", selection: $toolTarget) {
                    Text("Codex 运行时配色").tag(false)
                    Text("工具强调色").tag(true)
                }
                Picker("配色预设", selection: $themeID) { ForEach(WorkspaceTheme.all) { Text($0.name).tag($0.id) } }
                Text("今天，想构建什么？").font(.system(size: fontSize + 5, weight: .semibold))
                    .foregroundStyle(color(selectedTheme.foreground)).frame(maxWidth: .infinity, minHeight: 110, alignment: .leading)
                    .padding(.horizontal, 24).background(color(selectedTheme.background), in: RoundedRectangle(cornerRadius: 12))
                if !toolTarget {
                    Text("预览字号：\(Int(fontSize))")
                    Slider(value: $fontSize, in: 12...18, step: 1).accessibilityLabel("预览字号")
                }
                Text(model.themeStatus).font(.system(size: 12)).foregroundStyle(.secondary)
                HStack {
                    Button("应用所选配色") { model.applyTheme(id: themeID, font: Int(fontSize), tool: toolTarget) }
                    Button("恢复默认") { model.applyTheme(id: "default", font: 14, tool: toolTarget); restoreThemeSelection() }
                }.disabled(model.working)
                Text("Codex 配色仅在本工具启动的会话中生效；完整刷新后需重新应用。不修改原生设置、菜单或代码语法颜色。")
                    .font(.system(size: 11)).foregroundStyle(.secondary)
            }
        }
    }

    private var diagnostics: some View {
        Group {
            heading("每一步，都看得见。", "本机日志、汉化结果和失败原因。")
            HStack {
                Button("打开日志目录", action: model.openLogFolder)
                Button("复制日志") { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(model.logText, forType: .string) }
                Button("导出日志", action: model.exportLogs)
            }
            TextField("搜索日志", text: $logSearch).textFieldStyle(.roundedBorder)
            Toggle("仅提示与错误", isOn: $errorsOnly)
            materialCard {
                if filteredEntries.isEmpty { Text("没有匹配的日志").font(.system(size: 12)).foregroundStyle(.secondary) }
                ForEach(filteredEntries) { entry in
                    Text("\(entry.time)  \(String(entry.message.prefix(6000)))").font(.system(size: 12)).foregroundStyle(.secondary).textSelection(.enabled)
                }
            }
        }
    }

    private var filteredEntries: [LogEntry] {
        model.entries.filter { entry in
            (logSearch.isEmpty || entry.message.localizedCaseInsensitiveContains(logSearch)) &&
            (!errorsOnly || ["失败", "未完成", "未通过", "未找到", "未检测", "错误", "取消", "遗漏", "未应用"].contains(where: { entry.message.contains($0) }))
        }
    }
    private func restoreThemeSelection() {
        let id = toolTarget ? model.toolTheme : UserDefaults.standard.string(forKey: "workspace.codexTheme") ?? "violet"
        themeID = WorkspaceTheme.find(id) == nil ? "violet" : id
    }
    private func heading(_ title: String, _ detail: String) -> some View {
        VStack(alignment: .leading, spacing: 7) {
            Text(title).font(.system(size: 26, weight: .semibold))
            Text(detail).font(.system(size: 12)).foregroundStyle(.secondary)
        }.padding(.bottom, 2)
    }
    private func materialCard<Content: View>(@ViewBuilder _ content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 15, content: content)
            .frame(maxWidth: .infinity, alignment: .leading).padding(24)
            .background(LinearGradient(colors: [color("#34353f"), color("#292a32")], startPoint: .topLeading, endPoint: .bottomTrailing), in: RoundedRectangle(cornerRadius: 18))
            .overlay(RoundedRectangle(cornerRadius: 18).stroke(Color.white.opacity(0.10)))
            .shadow(color: Color.black.opacity(0.18), radius: 10, y: 5)
    }
    private func color(_ hex: String) -> Color {
        let value = UInt32(hex.dropFirst(), radix: 16) ?? 0xc4b5fd
        return Color(red: Double((value >> 16) & 255) / 255, green: Double((value >> 8) & 255) / 255, blue: Double(value & 255) / 255)
    }
}
