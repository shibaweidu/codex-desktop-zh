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
        XCTAssertEqual(report.processID, 901)
        XCTAssertEqual(launcher.launches.count, 2)
        XCTAssertEqual(processService.gracefulExitCount, 1)
        XCTAssertTrue(launcher.launches[1].contains("--remote-debugging-address=127.0.0.1"))
        XCTAssertTrue(launcher.launches[1].contains("--remote-allow-origins=http://127.0.0.1:9224"))
        XCTAssertTrue(launcher.launches[1].contains("--lang=zh-CN"))
        let expressions = await devTools.expressions
        XCTAssertTrue(expressions.contains { $0.contains("隐藏其他应用") })
        XCTAssertTrue(expressions.contains { $0.contains("localeOverride") })
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

    func waitForTarget(port: UInt16, preferredType: String, timeout: TimeInterval) async throws -> DevToolsTarget {
        DevToolsTarget(
            id: "1",
            type: preferredType,
            title: "test",
            url: nil,
            webSocketDebuggerUrl: "ws://127.0.0.1:\(port)/devtools/test"
        )
    }

    func evaluate(webSocketURL: String, expression: String, awaitPromise: Bool) async throws -> String? {
        expressions.append(expression)
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
