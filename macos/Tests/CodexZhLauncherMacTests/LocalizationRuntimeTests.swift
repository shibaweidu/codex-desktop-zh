import Foundation
import XCTest
@testable import CodexZhLauncherMac

final class LocalizationRuntimeTests: XCTestCase {
    func testChineseLaunchAppliesLocaleAndMacMenu() async throws {
        let processService = RestartingProcessService()
        let launcher = FakeLauncher(processService: processService)
        let devTools = FakeDevTools()
        let runtime = LocalizationRuntime(
            processService: processService,
            launcher: launcher,
            devTools: devTools,
            resources: try SharedResources(rootURL: sharedDirectory()),
            sleeper: ImmediateSleeper()
        )

        let report = try await runtime.launch(install: install(), locale: "zh-CN")

        XCTAssertTrue(report.complete)
        XCTAssertEqual(report.processID, 900)
        XCTAssertEqual(launcher.launches.count, 1)
        XCTAssertEqual(processService.gracefulExitCount, 0)
        XCTAssertTrue(launcher.launches[0].contains("--remote-debugging-address=127.0.0.1"))
        XCTAssertTrue(launcher.launches[0].contains("--remote-allow-origins=http://127.0.0.1:9222"))
        XCTAssertTrue(launcher.launches[0].contains("--lang=zh-CN"))
        let expressions = await devTools.expressions
        XCTAssertTrue(expressions.contains { $0.contains("72216192") && $0.contains("enable_i18n") })
        XCTAssertTrue(expressions.contains { $0.contains("隐藏其他应用") })
        XCTAssertTrue(expressions.contains { $0.contains("localeOverride") })
        let installedScripts = await devTools.installedScripts
        XCTAssertEqual(installedScripts.count, 1)
        XCTAssertTrue(installedScripts[0].contains("locale_source"))
        let operations = await devTools.operations
        XCTAssertLessThan(
            try XCTUnwrap(operations.firstIndex(of: "install-bootstrap")),
            try XCTUnwrap(operations.firstIndex(of: "set-locale"))
        )
        let evaluatedURLs = await devTools.evaluatedURLs
        XCTAssertTrue(evaluatedURLs.contains { $0.contains("/empty") })
        XCTAssertTrue(evaluatedURLs.contains { $0.contains("/content") })
        let localeApplicationURL = await devTools.localeApplicationURL
        let verificationURL = await devTools.verificationURL
        XCTAssertTrue(localeApplicationURL?.contains("/empty") == true)
        XCTAssertTrue(verificationURL?.contains("/content") == true)
    }

    func testEnglishLaunchSkipsMenuInjection() async throws {
        let launcher = FakeLauncher()
        let devTools = FakeDevTools()
        let runtime = LocalizationRuntime(
            processService: EmptyProcessService(),
            launcher: launcher,
            devTools: devTools,
            resources: try SharedResources(rootURL: sharedDirectory()),
            sleeper: ImmediateSleeper()
        )

        let report = try await runtime.launch(install: install(), locale: "en-US")

        XCTAssertTrue(report.complete)
        XCTAssertEqual(launcher.launches.count, 1)
        let expressions = await devTools.expressions
        XCTAssertFalse(expressions.contains { $0.contains("Menu.setApplicationMenu") })
    }

    private func install() -> CodexInstall {
        CodexInstall(
            kind: "test",
            displayName: "Codex",
            version: "1",
            bundleURL: URL(fileURLWithPath: "/tmp/Codex.app"),
            executableURL: URL(fileURLWithPath: "/usr/bin/true"),
            bundleIdentifier: "com.example.codex"
        )
    }

    private func sharedDirectory() -> URL {
        URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .appendingPathComponent("shared", isDirectory: true)
    }
}

final class DevToolsClientTests: XCTestCase {
    func testWebSocketOriginMatchesRandomLoopbackPort() throws {
        let url = try XCTUnwrap(URL(string: "ws://127.0.0.1:43821/devtools/page/abc"))
        XCTAssertEqual(DevToolsClient.origin(forWebSocketURL: url), "http://127.0.0.1:43821")
    }
}

private final class FakeLauncher: AppLaunching {
    var nextPort: UInt16 = 9222
    var launches: [[String]] = []
    private let processService: RestartingProcessService?

    init(processService: RestartingProcessService? = nil) {
        self.processService = processService
    }

    func reserveLoopbackPort() throws -> UInt16 {
        defer { nextPort += 1 }
        return nextPort
    }

    func launch(install: CodexInstall, arguments: [String]) async throws -> Int32 {
        launches.append(arguments)
        processService?.markRunning()
        return Int32(899 + launches.count)
    }
}

private final class RestartingProcessService: CodexProcessServing {
    private(set) var gracefulExitCount = 0
    private var running = false

    func markRunning() { running = true }

    func scan(install: CodexInstall) -> ProcessScan {
        guard running else { return ProcessScan() }
        return ProcessScan(verified: [ProcessSnapshot(
            pid: 900,
            name: "Codex",
            executablePath: install.executableURL.path,
            startMarker: "1:1",
            isMainExecutable: true
        )])
    }

    func requestGracefulExit(snapshot: ProcessSnapshot, install: CodexInstall) -> Bool {
        gracefulExitCount += 1
        running = false
        return true
    }

    func forceTerminate(snapshot: ProcessSnapshot, install: CodexInstall) throws {
        running = false
    }
}

private actor FakeDevTools: DevToolsServing {
    var expressions: [String] = []
    var evaluatedURLs: [String] = []
    var installedScripts: [String] = []
    var operations: [String] = []
    var localeApplicationURL: String?
    var verificationURL: String?

    func listTargets(port: UInt16) async throws -> [DevToolsTarget] {
        [
            DevToolsTarget(
                id: "empty",
                type: "page",
                title: "shell",
                url: "app://shell",
                webSocketDebuggerUrl: "ws://127.0.0.1:\(port)/devtools/empty"
            ),
            DevToolsTarget(
                id: "content",
                type: "iframe",
                title: "Codex",
                url: "https://chatgpt.com/codex",
                webSocketDebuggerUrl: "ws://127.0.0.1:\(port)/devtools/content"
            )
        ]
    }

    func waitForTarget(port: UInt16, preferredType: String, timeout: TimeInterval) async throws -> DevToolsTarget {
        DevToolsTarget(
            id: "1",
            type: preferredType,
            title: "test",
            url: nil,
            webSocketDebuggerUrl: "ws://127.0.0.1:\(port)/devtools/test"
        )
    }

    func installNewDocumentScript(webSocketURL: String, script: String) async throws -> String? {
        installedScripts.append(script)
        operations.append("install-bootstrap")
        return "script-1"
    }

    func evaluate(webSocketURL: String, expression: String, awaitPromise: Bool) async throws -> String? {
        expressions.append(expression)
        evaluatedURLs.append(webSocketURL)
        if expression.contains("codexZhProbe") {
            if webSocketURL.contains("/empty") {
                return "{\"codexZhProbe\":true,\"hasBridge\":true,\"textLength\":0,\"readyState\":\"complete\",\"documentLanguage\":\"en\"}"
            }
            return "{\"codexZhProbe\":true,\"hasBridge\":false,\"textLength\":240,\"readyState\":\"complete\",\"documentLanguage\":\"zh-CN\"}"
        }
        if expression.contains("localeOverride") {
            localeApplicationURL = webSocketURL
            operations.append("set-locale")
        }
        if expression.contains("zhMarkers") { verificationURL = webSocketURL }
        return "{\"status\":\"ok\"}"
    }
}

private struct EmptyProcessService: CodexProcessServing {
    func scan(install: CodexInstall) -> ProcessScan { ProcessScan() }
    func requestGracefulExit(snapshot: ProcessSnapshot, install: CodexInstall) -> Bool { false }
    func forceTerminate(snapshot: ProcessSnapshot, install: CodexInstall) throws {}
}

private struct ImmediateSleeper: Sleeping {
    func sleep(seconds: TimeInterval) async {}
}
