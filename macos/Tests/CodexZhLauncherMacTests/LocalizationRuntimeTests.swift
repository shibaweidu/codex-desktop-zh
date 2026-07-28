import Foundation
import XCTest
@testable import CodexZhLauncherMac

final class LocalizationRuntimeTests: XCTestCase {
    func testChineseLaunchAppliesLocaleAndMacMenu() async throws {
        let launcher = FakeLauncher()
        let devTools = FakeDevTools()
        let runtime = LocalizationRuntime(
            processService: EmptyProcessService(),
            launcher: launcher,
            devTools: devTools,
            resources: try SharedResources(rootURL: sharedDirectory()),
            sleeper: ImmediateSleeper()
        )

        let report = try await runtime.launch(install: install(), locale: "zh-CN")

        XCTAssertTrue(report.complete)
        XCTAssertEqual(report.processID, 900)
        XCTAssertTrue(launcher.arguments.contains("--remote-debugging-address=127.0.0.1"))
        XCTAssertTrue(launcher.arguments.contains("--lang=zh-CN"))
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

private final class FakeLauncher: AppLaunching {
    var nextPort: UInt16 = 9222
    var arguments: [String] = []

    func reserveLoopbackPort() throws -> UInt16 {
        defer { nextPort += 1 }
        return nextPort
    }

    func launch(install: CodexInstall, arguments: [String]) async throws -> Int32 {
        self.arguments = arguments
        return 900
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
