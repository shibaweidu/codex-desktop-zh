import Foundation

struct LocalizationRuntime {
    let processService: CodexProcessServing
    let launcher: AppLaunching
    let devTools: DevToolsServing
    let resources: SharedResources
    let sleeper: Sleeping
    let logger: AppLogger

    init(
        processService: CodexProcessServing,
        launcher: AppLaunching,
        devTools: DevToolsServing = DevToolsClient(),
        resources: SharedResources,
        sleeper: Sleeping = SystemSleeper(),
        logger: AppLogger = .shared
    ) {
        self.processService = processService
        self.launcher = launcher
        self.devTools = devTools
        self.resources = resources
        self.sleeper = sleeper
        self.logger = logger
    }

    func launch(install: CodexInstall, locale: String, progress: ((String) -> Void)? = nil) async throws -> LaunchReport {
        guard install.isValid else { throw RuntimeError.invalidInstall }
        let running = processService.scan(install: install).totalPotentialCount
        guard running == 0 else { throw RuntimeError.alreadyRunning(running) }

        var report = LaunchReport()
        var started = try await startCodex(install: install, locale: locale, restarting: false, progress: progress)
        report.rendererPort = started.rendererPort
        report.inspectorPort = started.inspectorPort
        report.processID = started.processID
        report.started = true
        emit("Codex 已启动，正在连接本地汉化接口。", progress)

        var localeConfigured = false
        do {
            report.localeDetail = try await applyLocale(port: started.rendererPort, locale: locale)
            localeConfigured = hasStatus(report.localeDetail, "ok")
        } catch {
            report.localeDetail = "setting-error=\(error.localizedDescription)"
        }

        if locale.lowercased().hasPrefix("zh"), localeConfigured {
            emit("中文设置已写入，正在完整重启 Codex 以加载语言资源。", progress)
            await sleeper.sleep(seconds: 0.8)
            let shutdown = await ShutdownCoordinator(
                processService: processService,
                sleeper: sleeper,
                logger: logger
            ).shutdown(install: install) { message in
                emit(message, progress)
            }
            guard shutdown.success else {
                throw RuntimeError.restartFailed(shutdown.remainingCount)
            }
            await sleeper.sleep(seconds: 0.75)
            started = try await startCodex(install: install, locale: locale, restarting: true, progress: progress)
            report.rendererPort = started.rendererPort
            report.inspectorPort = started.inspectorPort
            report.processID = started.processID
            report.localeDetail = join(report.localeDetail, "restart=ok")
            emit("Codex 已重新启动，正在验证最终界面。", progress)
        }

        let finalRendererPort = report.rendererPort
        let finalInspectorPort = report.inspectorPort
        async let menuResult = applyMenuIfNeeded(port: finalInspectorPort, locale: locale)
        do {
            await sleeper.sleep(seconds: 1.5)
            let verification = try await verifyLocale(port: finalRendererPort, locale: locale)
            report.localeApplied = hasStatus(verification, "ok")
            report.localeDetail = join(report.localeDetail, "verification=\(verification)")
        } catch {
            report.localeDetail = join(report.localeDetail, "verification-error=\(error.localizedDescription)")
        }
        emit(report.localeApplied ? "已确认 Codex 中文界面生效。" : "界面语言尚未确认，详细信息已写入日志。", progress)

        do {
            report.menuDetail = try await menuResult
            report.menuApplied = hasStatus(report.menuDetail, "ok")
        } catch {
            report.menuDetail = "menu-error=\(error.localizedDescription)"
        }
        emit(report.menuApplied ? "原生菜单已覆盖当前全部标签。" : "仍有原生菜单未翻译，遗漏项已显示在日志中。", progress)

        if report.complete {
            report.message = locale.lowercased().hasPrefix("zh")
                ? "汉化完成：中文界面和当前全部原生菜单已生效。"
                : "英文版已启动，语言设置已恢复。"
        } else if report.localeApplied {
            report.message = "中文界面已生效，但原生菜单仍有未翻译项；详情请查看运行日志。"
        } else if report.menuApplied && locale.lowercased().hasPrefix("zh") {
            report.message = "原生菜单已汉化，但中文界面状态未能确认；详情请查看运行日志。"
        } else {
            report.message = "中文界面设置未生效，原生菜单仍有未翻译项；详情请查看运行日志。"
        }
        logger.write("locale.detail \(report.localeDetail)")
        logger.write("menu.detail \(report.menuDetail)")
        logger.write("launch.complete pid=\(report.processID) locale=\(report.localeApplied) menu=\(report.menuApplied)")
        return report
    }

    private func startCodex(
        install: CodexInstall,
        locale: String,
        restarting: Bool,
        progress: ((String) -> Void)?
    ) async throws -> (processID: Int32, rendererPort: UInt16, inspectorPort: UInt16) {
        let rendererPort = try launcher.reserveLoopbackPort()
        var inspectorPort: UInt16
        repeat { inspectorPort = try launcher.reserveLoopbackPort() }
        while inspectorPort == rendererPort
        let arguments = [
            "--remote-debugging-address=127.0.0.1",
            "--remote-debugging-port=\(rendererPort)",
            "--remote-allow-origins=http://127.0.0.1:\(rendererPort)",
            "--inspect=127.0.0.1:\(inspectorPort)",
            "--lang=\(locale)"
        ]
        emit(restarting ? "正在以中文设置重新启动 Codex。" : "正在启动 Codex。", progress)
        logger.write("launch.begin kind=\(install.kind) locale=\(locale) restart=\(restarting) renderer_port=\(rendererPort) inspector_port=\(inspectorPort)")
        let processID = try await launcher.launch(install: install, arguments: arguments)
        return (processID, rendererPort, inspectorPort)
    }

    private func applyLocale(port: UInt16, locale: String) async throws -> String {
        let target = try await devTools.waitForTarget(port: port, preferredType: "page", timeout: 25)
        guard let socket = target.webSocketDebuggerUrl else { throw RuntimeError.missingWebSocket }
        return try await devTools.evaluate(
            webSocketURL: socket,
            expression: resources.buildLocaleScript(locale: locale),
            awaitPromise: true
        ) ?? ""
    }

    private func applyMenu(port: UInt16) async throws -> String {
        let target = try await devTools.waitForTarget(port: port, preferredType: "node", timeout: 20)
        guard let socket = target.webSocketDebuggerUrl else { throw RuntimeError.missingWebSocket }
        return try await devTools.evaluate(
            webSocketURL: socket,
            expression: resources.buildMenuScript(platform: "macos"),
            awaitPromise: false
        ) ?? ""
    }

    private func applyMenuIfNeeded(port: UInt16, locale: String) async throws -> String {
        guard locale.lowercased().hasPrefix("zh") else {
            return "{\"status\":\"ok\",\"reason\":\"english-mode\"}"
        }
        return try await applyMenu(port: port)
    }

    private func verifyLocale(port: UInt16, locale: String) async throws -> String {
        let target = try await devTools.waitForTarget(port: port, preferredType: "page", timeout: 12)
        guard let socket = target.webSocketDebuggerUrl else { throw RuntimeError.missingWebSocket }
        let localeData = try JSONSerialization.data(withJSONObject: locale, options: [.fragmentsAllowed])
        let encoded = String(decoding: localeData, as: UTF8.self)
        let script = """
        (async function () {
          var requested = \(encoded);
          var started = Date.now();
          while ((!document.body || (document.body.innerText || '').length < 40) && Date.now() - started < 10000) {
            await new Promise(function (resolve) { window.setTimeout(resolve, 200); });
          }
          var text = document.body ? document.body.innerText || '' : '';
          var zhMarkers = ['新建任务', '拉取请求', '已安排', '插件', '设置', '搜索'];
          var enMarkers = ['New task', 'Pull requests', 'Scheduled', 'Plugins', 'Settings', 'Search'];
          var count = function (markers) { return markers.reduce(function (n, marker) { return n + (text.indexOf(marker) >= 0 ? 1 : 0); }, 0); };
          var zhMatches = count(zhMarkers), enMatches = count(enMarkers);
          var expectsChinese = requested.toLowerCase().indexOf('zh') === 0;
          var documentLanguage = document.documentElement.lang || '';
          var languageMatches = documentLanguage.toLowerCase().indexOf(expectsChinese ? 'zh' : 'en') === 0;
          var markerMatches = expectsChinese ? (zhMatches > 0 && enMatches === 0) : enMatches > 0;
          return JSON.stringify({ status: (languageMatches || markerMatches) ? 'ok' : 'partial', requested: requested, navigatorLanguage: navigator.language, documentLanguage: documentLanguage, textLength: text.length, zhMarkers: zhMatches, enMarkers: enMatches });
        })()
        """
        return try await devTools.evaluate(webSocketURL: socket, expression: script, awaitPromise: true) ?? ""
    }

    private func hasStatus(_ json: String, _ expected: String) -> Bool {
        guard let data = json.data(using: .utf8),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let status = object["status"] as? String else { return false }
        return status.caseInsensitiveCompare(expected) == .orderedSame
    }

    private func join(_ first: String, _ second: String) -> String {
        first.isEmpty ? second : first + "; " + second
    }

    private func emit(_ message: String, _ progress: ((String) -> Void)?) {
        logger.write("progress \(message)")
        progress?(message)
    }

    enum RuntimeError: LocalizedError {
        case invalidInstall
        case alreadyRunning(Int)
        case missingWebSocket
        case restartFailed(Int)

        var errorDescription: String? {
            switch self {
            case .invalidInstall: return "未检测到可用的 Codex Desktop。"
            case .alreadyRunning(let count): return "Codex 仍在运行（检测到 \(count) 个候选进程）。"
            case .missingWebSocket: return "DevTools 目标缺少 WebSocket 地址。"
            case .restartFailed(let count): return "中文设置已写入，但完整重启失败（仍有 \(count) 个候选进程）。"
            }
        }
    }
}
