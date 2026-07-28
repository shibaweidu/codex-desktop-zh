import Darwin
import Foundation

struct AppLauncher: AppLaunching {
    let processService: CodexProcessServing
    let sleeper: Sleeping

    init(processService: CodexProcessServing, sleeper: Sleeping = SystemSleeper()) {
        self.processService = processService
        self.sleeper = sleeper
    }

    func reserveLoopbackPort() throws -> UInt16 {
        let descriptor = socket(AF_INET, SOCK_STREAM, 0)
        guard descriptor >= 0 else { throw LaunchError.socketFailure(systemError()) }
        defer { Darwin.close(descriptor) }

        var address = sockaddr_in()
        address.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
        address.sin_family = sa_family_t(AF_INET)
        address.sin_port = in_port_t(0)
        address.sin_addr = in_addr(s_addr: inet_addr("127.0.0.1"))
        let bindResult = withUnsafePointer(to: &address) { pointer in
            pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                Darwin.bind(descriptor, $0, socklen_t(MemoryLayout<sockaddr_in>.size))
            }
        }
        guard bindResult == 0 else { throw LaunchError.socketFailure(systemError()) }

        var bound = sockaddr_in()
        var length = socklen_t(MemoryLayout<sockaddr_in>.size)
        let nameResult = withUnsafeMutablePointer(to: &bound) { pointer in
            pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                getsockname(descriptor, $0, &length)
            }
        }
        guard nameResult == 0 else { throw LaunchError.socketFailure(systemError()) }
        return UInt16(bigEndian: bound.sin_port)
    }

    func launch(install: CodexInstall, arguments: [String]) async throws -> Int32 {
        let before = Set(processService.scan(install: install).verified.map(\.identity))
        let process = Process()
        let errorPipe = Pipe()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/open")
        process.arguments = ["-n", install.bundleURL.path, "--args"] + arguments
        process.standardOutput = FileHandle.nullDevice
        process.standardError = errorPipe
        try process.run()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else {
            let data = errorPipe.fileHandleForReading.readDataToEndOfFile()
            let detail = String(decoding: data, as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)
            throw LaunchError.openFailed(detail.isEmpty ? "exit \(process.terminationStatus)" : detail)
        }

        for _ in 0..<100 {
            let current = processService.scan(install: install).verified
            if let main = current.first(where: { $0.isMainExecutable && !before.contains($0.identity) }) {
                return main.pid
            }
            await sleeper.sleep(seconds: 0.1)
        }
        throw LaunchError.mainProcessTimeout
    }

    private func systemError() -> String {
        String(cString: strerror(errno))
    }

    enum LaunchError: LocalizedError {
        case socketFailure(String)
        case openFailed(String)
        case mainProcessTimeout

        var errorDescription: String? {
            switch self {
            case .socketFailure(let detail): return "无法分配本地调试端口：\(detail)"
            case .openFailed(let detail): return "Codex 启动失败：\(detail)"
            case .mainProcessTimeout: return "Codex 已请求启动，但未能确认新的主进程。"
            }
        }
    }
}
