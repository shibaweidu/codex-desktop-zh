import Foundation

struct CodexInstall: Equatable {
    let kind: String
    let displayName: String
    let version: String
    let bundleURL: URL
    let executableURL: URL
    let bundleIdentifier: String

    var isValid: Bool {
        FileManager.default.fileExists(atPath: executableURL.path)
    }
}

struct ProcessSnapshot: Equatable, Hashable {
    let pid: Int32
    let name: String
    let executablePath: String
    let startMarker: String
    let isMainExecutable: Bool

    var identity: String { "\(pid):\(startMarker)" }
}

struct ProcessScan {
    var verified: [ProcessSnapshot] = []
    var unverifiedCandidatePIDs: [Int32] = []

    var totalPotentialCount: Int {
        verified.count + unverifiedCandidatePIDs.count
    }
}

struct ShutdownReport {
    var initialCount = 0
    var gracefulRequested = 0
    var gracefulExited = 0
    var forceAttempted = 0
    var forceTerminated = 0
    var remainingCount = 0
    var failedProcessIDs: [Int32] = []
    var errors: [String] = []

    var success: Bool { remainingCount == 0 }

    mutating func addFailure(pid: Int32, message: String) {
        if !failedProcessIDs.contains(pid) { failedProcessIDs.append(pid) }
        if !errors.contains(message) { errors.append(message) }
    }
}

struct LaunchReport {
    var started = false
    var localeApplied = false
    var menuApplied = false
    var processID: Int32 = 0
    var rendererPort: UInt16 = 0
    var inspectorPort: UInt16 = 0
    var message = ""
    var localeDetail = ""
    var menuDetail = ""

    var complete: Bool { started && localeApplied && menuApplied }
}

struct DevToolsTarget: Decodable {
    let id: String?
    let type: String
    let title: String
    let url: String?
    let webSocketDebuggerUrl: String?
}

protocol CodexProcessServing {
    func scan(install: CodexInstall) -> ProcessScan
    func requestGracefulExit(snapshot: ProcessSnapshot, install: CodexInstall) -> Bool
    func forceTerminate(snapshot: ProcessSnapshot, install: CodexInstall) throws
}

protocol Sleeping {
    func sleep(seconds: TimeInterval) async
}

protocol ClockProviding {
    var now: Date { get }
}

protocol AppLaunching {
    func reserveLoopbackPort() throws -> UInt16
    func launch(install: CodexInstall, arguments: [String]) async throws -> Int32
}

protocol DevToolsServing {
    func waitForTarget(port: UInt16, preferredType: String, timeout: TimeInterval) async throws -> DevToolsTarget
    func listTargets(port: UInt16) async throws -> [DevToolsTarget]
    func installNewDocumentScript(webSocketURL: String, script: String) async throws -> String?
    func evaluate(webSocketURL: String, expression: String, awaitPromise: Bool) async throws -> String?
}

extension DevToolsServing {
    func listTargets(port: UInt16) async throws -> [DevToolsTarget] {
        [try await waitForTarget(port: port, preferredType: "page", timeout: 2)]
    }
}

struct SystemClock: ClockProviding {
    var now: Date { Date() }
}

struct SystemSleeper: Sleeping {
    func sleep(seconds: TimeInterval) async {
        guard seconds > 0 else { return }
        try? await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
    }
}
