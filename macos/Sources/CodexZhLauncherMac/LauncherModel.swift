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
    static let version = "0.5.0"

    @Published private(set) var install: CodexInstall?
    @Published private(set) var runningCount = 0
    @Published private(set) var busy = false
    @Published private(set) var statusTitle = "正在检测 Codex"
    @Published private(set) var statusDetail = ""
    @Published private(set) var statusIsWarning = false
    @Published private(set) var entries: [LogEntry] = []
    @Published var showsRestartConfirmation = false

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
        refreshDetection(announce: true)
        pollingTask = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: 1_000_000_000)
                guard let self, !self.busy else { continue }
                self.refreshRunningState()
            }
        }
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
        install = discovery.detect()
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
        guard install != nil else { selectApplication(); return }
        if runningCount > 0 {
            showsRestartConfirmation = true
        } else {
            launch(locale: "zh-CN")
        }
    }

    func confirmRestart() {
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
            setError("无法打开日志目录：\(error.localizedDescription)")
        }
    }

    private func launch(locale: String) {
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
        } catch {
            setError(error.localizedDescription)
        }
        busy = false
        refreshRunningState()
    }

    private func refreshRunningState() {
        runningCount = install.map { processService.scan(install: $0).totalPotentialCount } ?? 0
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
