import Foundation

struct ShutdownCoordinator {
    let processService: CodexProcessServing
    let sleeper: Sleeping
    let clock: ClockProviding
    let pollInterval: TimeInterval
    let logger: AppLogger

    init(
        processService: CodexProcessServing,
        sleeper: Sleeping = SystemSleeper(),
        clock: ClockProviding = SystemClock(),
        pollInterval: TimeInterval = 0.25,
        logger: AppLogger = .shared
    ) {
        self.processService = processService
        self.sleeper = sleeper
        self.clock = clock
        self.pollInterval = pollInterval
        self.logger = logger
    }

    func shutdown(
        install: CodexInstall,
        gracefulTimeout: TimeInterval = 5,
        forceTimeout: TimeInterval = 8,
        progress: ((String) -> Void)? = nil
    ) async -> ShutdownReport {
        var report = ShutdownReport()
        let initial = processService.scan(install: install)
        report.initialCount = initial.totalPotentialCount
        addUnverified(initial, report: &report, message: "无法读取候选进程路径，已拒绝关闭")
        emit("已识别 \(initial.verified.count) 个可安全关闭的 Codex 进程。", progress: progress)

        guard !initial.verified.isEmpty else {
            report.remainingCount = initial.unverifiedCandidatePIDs.count
            return report
        }

        for snapshot in initial.verified where snapshot.isMainExecutable {
            if processService.requestGracefulExit(snapshot: snapshot, install: install) {
                report.gracefulRequested += 1
            }
        }
        if report.gracefulRequested > 0 {
            emit("已请求 Codex 正常退出，最多等待 \(Int(ceil(gracefulTimeout))) 秒。", progress: progress)
        } else {
            emit("未找到可响应关闭请求的主应用，将在等待后处理剩余进程。", progress: progress)
        }

        await waitForVerifiedProcesses(install: install, timeout: gracefulTimeout)
        let afterGraceful = processService.scan(install: install)
        report.gracefulExited = countNoLongerRunning(before: initial.verified, after: afterGraceful.verified)
        if afterGraceful.verified.isEmpty {
            addUnverified(afterGraceful, report: &report, message: "关闭后仍存在路径不可验证的候选进程")
            report.remainingCount = afterGraceful.unverifiedCandidatePIDs.count
            emit(report.success ? "Codex 已正常退出。" : "仍有路径不可验证的候选进程，已停止自动重启。", progress: progress)
            return report
        }

        emit("正常退出等待结束，准备强制终止 \(afterGraceful.verified.count) 个剩余进程。", progress: progress)
        let forceSnapshots = afterGraceful.verified
        var forceRequested: [ProcessSnapshot] = []
        for snapshot in forceSnapshots {
            report.forceAttempted += 1
            do {
                try processService.forceTerminate(snapshot: snapshot, install: install)
                forceRequested.append(snapshot)
            } catch {
                report.addFailure(pid: snapshot.pid, message: error.localizedDescription)
            }
        }

        await waitForVerifiedProcesses(install: install, timeout: forceTimeout)
        let finalScan = processService.scan(install: install)
        report.forceTerminated = countNoLongerRunning(before: forceRequested, after: finalScan.verified)
        addUnverified(finalScan, report: &report, message: "结束时仍存在路径不可验证的候选进程")
        for remaining in finalScan.verified {
            report.addFailure(pid: remaining.pid, message: "PID \(remaining.pid) 仍在运行。")
        }
        report.remainingCount = finalScan.totalPotentialCount
        emit(report.success ? "全部 Codex 进程已关闭。" : "仍有 \(report.remainingCount) 个候选进程未关闭，已停止自动重启。", progress: progress)
        return report
    }

    private func waitForVerifiedProcesses(install: CodexInstall, timeout: TimeInterval) async {
        let deadline = clock.now.addingTimeInterval(max(0, timeout))
        while clock.now < deadline {
            if processService.scan(install: install).verified.isEmpty { return }
            let remaining = deadline.timeIntervalSince(clock.now)
            if remaining <= 0 { return }
            await sleeper.sleep(seconds: min(pollInterval, remaining))
        }
    }

    private func countNoLongerRunning(before: [ProcessSnapshot], after: [ProcessSnapshot]) -> Int {
        let identities = Set(after.map(\.identity))
        return before.filter { !identities.contains($0.identity) }.count
    }

    private func addUnverified(_ scan: ProcessScan, report: inout ShutdownReport, message: String) {
        for pid in scan.unverifiedCandidatePIDs {
            report.addFailure(pid: pid, message: "\(message)：PID \(pid)")
        }
    }

    private func emit(_ message: String, progress: ((String) -> Void)?) {
        logger.write("shutdown \(message)")
        progress?(message)
    }
}
