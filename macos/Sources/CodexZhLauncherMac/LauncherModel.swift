import AppKit
import Combine
import Foundation
import UniformTypeIdentifiers

struct LogEntry: Identifiable {
    let id = UUID()
    let time: String
    let message: String
}

@MainActor
final class LauncherModel: ObservableObject {
    static let version = "0.8.3"
    static let sponsorURL = URL(string: "https://www.appkaola.com")!

    @Published private(set) var install: CodexInstall?
    @Published private(set) var runningCount = 0
    @Published private(set) var busy = false
    @Published private(set) var statusTitle = "正在检测 Codex"
    @Published private(set) var statusDetail = ""
    @Published private(set) var statusIsWarning = false
    @Published private(set) var entries: [LogEntry] = []
    @Published var showsRestartConfirmation = false
    @Published var showsAbout = false
    @Published var showsUpdateAlert = false
    @Published var showsUpdateFailureAlert = false
    @Published var showsCodexUpdateConfirmation = false
    @Published var showsCodexUpdateFailure = false
    @Published private(set) var checkingForUpdates = false
    @Published private(set) var updating = false
    @Published private(set) var updateStatus = "尚未检查更新"
    @Published private(set) var availableUpdate: UpdateCheckResult?
    @Published private(set) var updateFailureMessage = ""
    @Published private(set) var codexUpdateFailureMessage = ""
    @Published var themeStatus = "只更改运行时样式，不修改安装文件、原生设置或代码语法颜色。"
    @Published private(set) var toolTheme = UserDefaults.standard.string(forKey: "workspace.toolTheme") ?? "violet"
    let downloads = MacCodexDownloads()
    private var downloadObservation: AnyCancellable?
    private var lastLaunchReport: LaunchReport?
    private var launchIdentity: String?
    private(set) var allowsUpdateExit = false
    var working: Bool { busy || updating || downloads.busy }

    private let discovery: CodexDiscovery
    private let processService: DarwinProcessService
    private let logger: AppLogger
    private var pollingTask: Task<Void, Never>?

    init(
        discovery: CodexDiscovery = CodexDiscovery(),
        processService: DarwinProcessService = DarwinProcessService(),
        logger: AppLogger = .shared
    ) {
        self.discovery = discovery
        self.processService = processService
        self.logger = logger
        downloads.onEvent = { [weak self] message in self?.append(message) }
        downloadObservation = downloads.objectWillChange.sink { [weak self] _ in self?.objectWillChange.send() }
        refreshDetection(announce: true)
        pollingTask = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: 1_000_000_000)
                guard let self, !self.busy else { continue }
                self.refreshRunningState()
            }
        }
        if UserDefaults.standard.object(forKey: "workspace.autoCheck") as? Bool ?? true { checkForUpdates(showPrompt: true) }
    }

    deinit { pollingTask?.cancel() }

    var primaryTitle: String { runningCount > 0 ? "关闭并汉化重启" : "汉化并启动" }
    var installTitle: String { install?.displayName ?? "未检测到 Codex" }
    var installDetail: String {
        guard let install else { return "请手动选择 Codex.app 或 ChatGPT.app" }
        return "版本 \(install.version)  ·  \(install.kind)"
    }

    var installedIcon: NSImage? {
        guard let install else { return nil }
        return NSWorkspace.shared.icon(forFile: install.bundleURL.path)
    }

    func refreshDetection(announce: Bool = false) {
        guard !working else { return }
        let detected = discovery.detect()
        if detected != install { lastLaunchReport = nil; launchIdentity = nil }
        install = detected
        refreshRunningState()
        if let install {
            statusTitle = runningCount > 0 ? "Codex 正在运行" : "已准备就绪"
            statusDetail = install.bundleURL.path
            statusIsWarning = runningCount > 0
            if announce { append("已检测到 \(install.displayName)，版本 \(install.version)。") }
        } else {
            statusTitle = "未检测到 Codex"
            statusDetail = "请手动选择应用"
            statusIsWarning = true
            if announce { append("自动检测未找到 Codex.app。") }
        }
    }

    func selectApplication() {
        guard !working else { return }
        let panel = NSOpenPanel()
        panel.title = "选择 Codex 应用"
        panel.prompt = "选择"
        panel.canChooseDirectories = false
        panel.canChooseFiles = true
        panel.allowsMultipleSelection = false
        panel.allowedContentTypes = [.applicationBundle]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            install = try discovery.useBundle(url)
            lastLaunchReport = nil
            launchIdentity = nil
            refreshRunningState()
            statusTitle = "已准备就绪"
            statusDetail = url.path
            statusIsWarning = false
            append("已选择 \(install?.displayName ?? url.lastPathComponent)。")
        } catch {
            setError(error.localizedDescription)
        }
    }

    func primaryAction() {
        guard !working else { return }
        guard install != nil else { selectApplication(); return }
        if runningCount > 0 {
            showsRestartConfirmation = true
        } else {
            launch(locale: "zh-CN")
        }
    }

    func confirmRestart() {
        guard !working else { return }
        showsRestartConfirmation = false
        guard let install else { return }
        busy = true
        statusTitle = "正在关闭 Codex"
        statusDetail = "先正常退出，5 秒后处理剩余进程"
        statusIsWarning = true
        Task {
            let coordinator = ShutdownCoordinator(processService: processService, logger: logger)
            let report = await coordinator.shutdown(install: install) { [weak self] message in
                Task { @MainActor in self?.append(message) }
            }
            append("关闭统计：请求 \(report.gracefulRequested)，正常退出 \(report.gracefulExited)，强制终止 \(report.forceTerminated)，剩余 \(report.remainingCount)。")
            if !report.success {
                busy = false
                statusTitle = "无法关闭全部 Codex 进程"
                statusDetail = report.errors.joined(separator: "；")
                statusIsWarning = true
                refreshRunningState()
                return
            }
            append("全部目标进程已退出，准备中文重启。")
            try? await Task.sleep(nanoseconds: 750_000_000)
            await performLaunch(install: install, locale: "zh-CN")
        }
    }

    func launchEnglish() {
        guard !working else { return }
        guard runningCount == 0 else {
            setError("请先完全退出 Codex，再使用英文启动。")
            return
        }
        launch(locale: "en-US")
    }

    func openLogFolder() {
        do {
            try FileManager.default.createDirectory(at: logger.directoryURL, withIntermediateDirectories: true)
            NSWorkspace.shared.open(logger.directoryURL)
        } catch {
            append("无法打开日志目录：\(error.localizedDescription)")
        }
    }

    func checkForUpdates(showPrompt: Bool = false) {
        guard !checkingForUpdates, !working else { return }
        checkingForUpdates = true
        updateStatus = "正在连接 GitHub Releases..."
        Task {
            do {
                let result = try await GitHubUpdateChecker().check(currentVersion: Self.version)
                availableUpdate = result.updateAvailable ? result : nil
                updateStatus = result.message
                logger.write("update.check \(result.message)")
                if showPrompt && result.updateAvailable && !working { showsUpdateAlert = true }
            } catch {
                availableUpdate = nil
                updateStatus = "检查失败：\(error.localizedDescription)"
                logger.write("update.check.failed \(error.localizedDescription)")
            }
            checkingForUpdates = false
        }
    }

    func openAvailableUpdate() {
        NSWorkspace.shared.open(availableUpdate?.releaseURL ?? GitHubUpdateChecker.latestReleaseURL)
    }

    func installAvailableUpdate() {
        guard let update = availableUpdate, !working else { return }
        updating = true
        updateStatus = "正在下载并校验 v\(update.latestVersion)..."
        Task {
            do {
                try await MacUpdateInstaller.prepareAndLaunch(update: update)
                updateStatus = "校验完成，正在覆盖并重启..."
                logger.write("update.apply.start version=\(update.latestVersion)")
                allowsUpdateExit = true
                NSApplication.shared.terminate(nil)
            } catch {
                updating = false
                updateFailureMessage = error.localizedDescription
                updateStatus = "自动更新失败：\(error.localizedDescription)"
                showsUpdateFailureAlert = true
                logger.write("update.prepare.failed \(error)")
            }
        }
    }

    func openRepository() {
        NSWorkspace.shared.open(GitHubUpdateChecker.repositoryURL)
    }

    func openFeedback() {
        NSWorkspace.shared.open(GitHubUpdateChecker.feedbackURL)
    }

    func openSponsor() {
        NSWorkspace.shared.open(Self.sponsorURL)
    }

    func checkCodexUpdate(architecture: String) {
        guard !working else { return }
        guard let install else { statusTitle = "未检测到 Codex"; statusDetail = "请先选择 Codex.app"; return }
        downloads.start(install: install, architecture: architecture)
    }

    func requestCodexUpdateInstall() {
        guard !working, downloads.file != nil, downloads.plan != nil else { return }
        showsCodexUpdateConfirmation = true
    }

    func installCodexUpdate() {
        showsCodexUpdateConfirmation = false
        guard !working, let install else { return }
        refreshRunningState()
        guard runningCount == 0 else {
            codexUpdateFailureMessage = "请先保存工作并完全退出 Codex，再重新点击安装更新。不会自动强制关闭正在运行的 Codex。"
            showsCodexUpdateFailure = true
            return
        }
        updating = true
        downloads.report("正在验证应用签名并准备覆盖更新…")
        Task {
            do {
                try await downloads.installUpdate(install: install, launcherBundle: Bundle.main.bundleURL)
                downloads.report("已启动安全更新程序。旧版工具即将退出，Codex 更新后会自动重启。")
                logger.write("codex.update.apply.start version=\(downloads.plan?.version ?? "unknown")")
                allowsUpdateExit = true
                NSApplication.shared.terminate(nil)
            } catch {
                updating = false
                codexUpdateFailureMessage = error.localizedDescription
                downloads.report("更新未应用：\(error.localizedDescription)")
                showsCodexUpdateFailure = true
                logger.write("codex.update.prepare.failed \(error.localizedDescription)")
            }
        }
    }

    var logText: String { entries.map { "\($0.time)  \($0.message)" }.joined(separator: "\n") }

    func exportLogs() {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "codex-launcher-log.txt"
        panel.allowedContentTypes = [.plainText]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do { try logText.write(to: url, atomically: true, encoding: .utf8) }
        catch { append("日志导出失败：\(error.localizedDescription)") }
    }

    private func launch(locale: String) {
        guard !working else { return }
        guard let install else { return }
        busy = true
        Task { await performLaunch(install: install, locale: locale) }
    }

    private func performLaunch(install: CodexInstall, locale: String) async {
        statusTitle = locale == "zh-CN" ? "正在汉化启动" : "正在以英文启动"
        statusDetail = "正在连接 Codex 本地接口"
        statusIsWarning = false
        do {
            let resources = try SharedResources()
            let launcher = AppLauncher(processService: processService)
            let runtime = LocalizationRuntime(
                processService: processService,
                launcher: launcher,
                resources: resources,
                logger: logger
            )
            let report = try await runtime.launch(install: install, locale: locale) { [weak self] message in
                Task { @MainActor in self?.append(message) }
            }
            lastLaunchReport = report
            launchIdentity = processService.scan(install: install).verified.first { $0.pid == report.processID && $0.isMainExecutable }?.identity
            if locale == "zh-CN" {
                statusTitle = report.complete ? "汉化已完成" : "汉化未完全生效"
            } else {
                statusTitle = report.complete ? "英文版已启动" : "英文设置未完全生效"
            }
            statusDetail = report.message
            statusIsWarning = !report.complete
            append(report.message)
            if !report.localeDetail.isEmpty { append("界面：\(report.localeDetail)") }
            if !report.menuDetail.isEmpty { append("菜单：\(report.menuDetail)") }
            let theme = UserDefaults.standard.string(forKey: "workspace.codexTheme") ?? "default"
            if theme != "default", WorkspaceTheme.find(theme) != nil {
                do {
                    let font = max(12, min(18, UserDefaults.standard.object(forKey: "workspace.fontSize") as? Int ?? 14))
                    append("启动配色：" + (try await applySessionTheme(report: report, id: theme, font: font)))
                } catch { append("启动配色未应用：\(error.localizedDescription)") }
            }
        } catch {
            setError(error.localizedDescription)
        }
        busy = false
        refreshRunningState()
    }

    private func refreshRunningState() {
        let scan = install.map { processService.scan(install: $0) } ?? ProcessScan()
        runningCount = scan.totalPotentialCount
        if let identity = launchIdentity, !scan.verified.contains(where: { $0.identity == identity }) {
            lastLaunchReport = nil; launchIdentity = nil
        }
        if runningCount == 0 { lastLaunchReport = nil; launchIdentity = nil }
    }

    func applyTheme(id: String, font: Int, tool: Bool) {
        guard !working, (id == "default" || WorkspaceTheme.find(id) != nil), (12...18).contains(font) else { return }
        if tool {
            toolTheme = id == "default" ? "violet" : id
            UserDefaults.standard.set(toolTheme, forKey: "workspace.toolTheme")
            themeStatus = "工具强调色已保存，继续使用深色原生材质。"
            return
        }
        refreshRunningState()
        guard runningCount > 0, let report = lastLaunchReport, launchIdentity != nil else {
            UserDefaults.standard.set(id, forKey: "workspace.codexTheme")
            UserDefaults.standard.set(font, forKey: "workspace.fontSize")
            themeStatus = "偏好已保存，尚未应用到 Codex。请通过本工具启动后再应用。"
            return
        }
        busy = true
        Task {
            defer { busy = false }
            do {
                let result = try await applySessionTheme(report: report, id: id, font: font)
                let data = Data(result.utf8)
                let values = try JSONSerialization.jsonObject(with: data) as? [String: Any]
                if values?["status"] as? String == "ok" {
                    UserDefaults.standard.set(id, forKey: "workspace.codexTheme")
                    UserDefaults.standard.set(font, forKey: "workspace.fontSize")
                    themeStatus = id == "default" ? "已移除工具注入的样式，恢复应用原有外观。" : "根界面配色已验证；原生菜单和代码语法颜色未修改。"
                } else { themeStatus = "配色未通过验证，未保留新样式。" }
                append("配色结果：\(result)")
            } catch { themeStatus = "配色未完成：\(error.localizedDescription)"; append(themeStatus) }
        }
    }

    private func applySessionTheme(report: LaunchReport, id: String, font: Int) async throws -> String {
        guard let install, let identity = launchIdentity,
              processService.scan(install: install).verified.contains(where: { $0.identity == identity && $0.pid == report.processID }) else {
            throw WorkspaceServiceError.invalid("原启动会话已结束或无法验证，请通过本工具重新启动 Codex。")
        }
        return try await WorkspaceThemeClient.apply(port: report.rendererPort, id: id, font: font)
    }

    private func setError(_ message: String) {
        busy = false
        statusTitle = "操作未完成"
        statusDetail = message
        statusIsWarning = true
        append(message)
        logger.write("ui.error \(message)")
    }

    private func append(_ message: String) {
        let formatter = DateFormatter()
        formatter.dateFormat = "HH:mm:ss"
        entries.append(LogEntry(time: formatter.string(from: Date()), message: message))
        if entries.count > 300 { entries.removeFirst(entries.count - 300) }
        logger.write("ui \(message)")
    }
}
