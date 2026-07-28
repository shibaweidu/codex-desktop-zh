import Foundation
import XCTest
@testable import CodexZhLauncherMac

final class ShutdownCoordinatorTests: XCTestCase {
    func testGracefulExitReport() async {
        let process = snapshot(pid: 101, main: true)
        let service = FakeProcessService(processes: [process])
        service.onGraceful = { service.processes = [] }
        let clock = TestClock()
        let coordinator = ShutdownCoordinator(
            processService: service,
            sleeper: AdvancingSleeper(clock: clock),
            clock: clock,
            pollInterval: 0.25
        )

        let report = await coordinator.shutdown(install: install(), gracefulTimeout: 5, forceTimeout: 8)
        XCTAssertTrue(report.success)
        XCTAssertEqual(report.gracefulRequested, 1)
        XCTAssertEqual(report.gracefulExited, 1)
        XCTAssertEqual(report.forceAttempted, 0)
    }

    func testForceTerminatesOnlyRevalidatedSnapshot() async {
        let main = snapshot(pid: 201, main: true)
        let helper = snapshot(pid: 202, main: false)
        let service = FakeProcessService(processes: [main, helper])
        service.onForce = { item in service.processes.removeAll { $0.identity == item.identity } }
        let clock = TestClock()
        let coordinator = ShutdownCoordinator(
            processService: service,
            sleeper: AdvancingSleeper(clock: clock),
            clock: clock,
            pollInterval: 1
        )

        let report = await coordinator.shutdown(install: install(), gracefulTimeout: 1, forceTimeout: 1)
        XCTAssertTrue(report.success)
        XCTAssertEqual(report.forceAttempted, 2)
        XCTAssertEqual(report.forceTerminated, 2)
    }

    func testPIDReuseIsReportedAndNotTerminated() async {
        let original = snapshot(pid: 301, marker: "old", main: true)
        let replacement = snapshot(pid: 301, marker: "new", main: true)
        let service = FakeProcessService(processes: [original])
        service.onGraceful = { service.processes = [replacement] }
        service.forceError = DarwinProcessService.ProcessError.identityChanged(301)
        let clock = TestClock()
        let coordinator = ShutdownCoordinator(
            processService: service,
            sleeper: AdvancingSleeper(clock: clock),
            clock: clock,
            pollInterval: 1
        )

        let report = await coordinator.shutdown(install: install(), gracefulTimeout: 1, forceTimeout: 0)
        XCTAssertFalse(report.success)
        XCTAssertEqual(report.remainingCount, 1)
        XCTAssertTrue(report.failedProcessIDs.contains(301))
    }

    func testUnverifiedCandidateBlocksSuccess() async {
        let service = FakeProcessService(processes: [], unverified: [401])
        let clock = TestClock()
        let report = await ShutdownCoordinator(
            processService: service,
            sleeper: AdvancingSleeper(clock: clock),
            clock: clock
        ).shutdown(install: install())

        XCTAssertFalse(report.success)
        XCTAssertEqual(report.remainingCount, 1)
        XCTAssertTrue(report.failedProcessIDs.contains(401))
    }

    func testRespawnDuringGracefulWaitIsAlsoClosed() async {
        let main = snapshot(pid: 501, main: true)
        let respawned = snapshot(pid: 502, marker: "respawn", main: false)
        let service = FakeProcessService(processes: [main])
        service.onForce = { item in service.processes.removeAll { $0.identity == item.identity } }
        let clock = TestClock()
        var inserted = false
        let sleeper = AdvancingSleeper(clock: clock) {
            guard !inserted else { return }
            inserted = true
            service.processes.append(respawned)
        }
        let coordinator = ShutdownCoordinator(
            processService: service,
            sleeper: sleeper,
            clock: clock,
            pollInterval: 1
        )

        let report = await coordinator.shutdown(install: install(), gracefulTimeout: 1, forceTimeout: 1)
        XCTAssertTrue(report.success)
        XCTAssertEqual(report.forceAttempted, 2)
        XCTAssertEqual(report.forceTerminated, 2)
    }

    private func install() -> CodexInstall {
        CodexInstall(
            kind: "test",
            displayName: "Codex",
            version: "1",
            bundleURL: URL(fileURLWithPath: "/tmp/Codex.app"),
            executableURL: URL(fileURLWithPath: "/tmp/Codex.app/Contents/MacOS/Codex"),
            bundleIdentifier: "com.example.codex"
        )
    }

    private func snapshot(pid: Int32, marker: String = "start", main: Bool) -> ProcessSnapshot {
        ProcessSnapshot(
            pid: pid,
            name: main ? "Codex" : "Codex Helper",
            executablePath: main ? "/tmp/Codex.app/Contents/MacOS/Codex" : "/tmp/Codex.app/Contents/Frameworks/Codex Helper",
            startMarker: marker,
            isMainExecutable: main
        )
    }
}

private final class FakeProcessService: CodexProcessServing {
    var processes: [ProcessSnapshot]
    var unverified: [Int32]
    var onGraceful: (() -> Void)?
    var onForce: ((ProcessSnapshot) -> Void)?
    var forceError: Error?

    init(processes: [ProcessSnapshot], unverified: [Int32] = []) {
        self.processes = processes
        self.unverified = unverified
    }

    func scan(install: CodexInstall) -> ProcessScan {
        ProcessScan(verified: processes, unverifiedCandidatePIDs: unverified)
    }

    func requestGracefulExit(snapshot: ProcessSnapshot, install: CodexInstall) -> Bool {
        onGraceful?()
        return true
    }

    func forceTerminate(snapshot: ProcessSnapshot, install: CodexInstall) throws {
        if let forceError { throw forceError }
        onForce?(snapshot)
    }
}

private final class TestClock: ClockProviding {
    var now = Date(timeIntervalSince1970: 0)
}

private struct AdvancingSleeper: Sleeping {
    let clock: TestClock
    var onSleep: (() -> Void)? = nil
    func sleep(seconds: TimeInterval) async {
        clock.now.addTimeInterval(seconds)
        onSleep?()
    }
}
