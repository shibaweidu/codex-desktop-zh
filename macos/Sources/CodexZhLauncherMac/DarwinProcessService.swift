import AppKit
import CLibProcBridge
import Darwin
import Foundation

final class DarwinProcessService: CodexProcessServing {
    private let candidatePrefixes = ["Codex", "ChatGPT"]
    private let pathBufferSize = 4096

    func scan(install: CodexInstall) -> ProcessScan {
        var result = ProcessScan()
        for pid in allProcessIDs() where pid > 0 {
            guard let path = executablePath(pid: pid) else {
                if let name = processName(pid: pid), isCandidateName(name) {
                    result.unverifiedCandidatePIDs.append(pid)
                }
                continue
            }
            guard CodexDiscovery.isPath(path, within: install.bundleURL.path) else { continue }
            let mainExecutable = pathsEqual(path, install.executableURL.path)
            let executableName = URL(fileURLWithPath: path).lastPathComponent
            guard mainExecutable || isHelperName(executableName) else { continue }
            guard let startMarker = processStartMarker(pid: pid) else {
                result.unverifiedCandidatePIDs.append(pid)
                continue
            }
            result.verified.append(ProcessSnapshot(
                pid: pid,
                name: executableName,
                executablePath: URL(fileURLWithPath: path).standardizedFileURL.resolvingSymlinksInPath().path,
                startMarker: startMarker,
                isMainExecutable: mainExecutable
            ))
        }
        result.verified.sort { $0.pid < $1.pid }
        result.unverifiedCandidatePIDs = Array(Set(result.unverifiedCandidatePIDs)).sorted()
        return result
    }

    func requestGracefulExit(snapshot: ProcessSnapshot, install: CodexInstall) -> Bool {
        guard snapshot.isMainExecutable, revalidated(snapshot, install: install) != nil else { return false }
        guard let application = NSRunningApplication(processIdentifier: pid_t(snapshot.pid)) else { return false }
        return application.terminate()
    }

    func forceTerminate(snapshot: ProcessSnapshot, install: CodexInstall) throws {
        let currentScan = scan(install: install)
        guard let current = currentScan.verified.first(where: { $0.pid == snapshot.pid }) else {
            return
        }
        guard current.identity == snapshot.identity,
              current.executablePath == snapshot.executablePath else {
            throw ProcessError.identityChanged(snapshot.pid)
        }
        if Darwin.kill(snapshot.pid, SIGKILL) != 0 {
            throw ProcessError.killFailed(snapshot.pid, String(cString: strerror(errno)))
        }
    }

    private func revalidated(_ expected: ProcessSnapshot, install: CodexInstall) -> ProcessSnapshot? {
        scan(install: install).verified.first {
            $0.pid == expected.pid && $0.identity == expected.identity &&
            $0.executablePath == expected.executablePath
        }
    }

    private func allProcessIDs() -> [Int32] {
        let estimatedCount = max(128, Int(cz_proc_list_all_pids(nil, 0)) + 64)
        var pids = [Int32](repeating: 0, count: estimatedCount)
        let count = pids.withUnsafeMutableBufferPointer { buffer in
            cz_proc_list_all_pids(buffer.baseAddress, Int32(buffer.count))
        }
        guard count > 0 else { return [] }
        return Array(pids.prefix(min(Int(count), pids.count)))
    }

    private func executablePath(pid: Int32) -> String? {
        var buffer = [CChar](repeating: 0, count: pathBufferSize)
        let length = buffer.withUnsafeMutableBufferPointer { pointer in
            cz_proc_pid_path(pid, pointer.baseAddress, UInt32(pointer.count))
        }
        guard length > 0 else { return nil }
        return String(cString: buffer)
    }

    private func processName(pid: Int32) -> String? {
        var buffer = [CChar](repeating: 0, count: 256)
        let length = buffer.withUnsafeMutableBufferPointer { pointer in
            cz_proc_name(pid, pointer.baseAddress, UInt32(pointer.count))
        }
        guard length > 0 else { return nil }
        return String(cString: buffer)
    }

    private func processStartMarker(pid: Int32) -> String? {
        var seconds: UInt64 = 0
        var microseconds: UInt64 = 0
        guard cz_proc_start_time(pid, &seconds, &microseconds) == 1 else { return nil }
        return "\(seconds):\(microseconds)"
    }

    private func isCandidateName(_ name: String) -> Bool {
        candidatePrefixes.contains { name.caseInsensitiveCompare($0) == .orderedSame } || isHelperName(name)
    }

    private func isHelperName(_ name: String) -> Bool {
        candidatePrefixes.contains {
            name.range(of: "\($0) Helper", options: [.anchored, .caseInsensitive]) != nil
        }
    }

    private func pathsEqual(_ left: String, _ right: String) -> Bool {
        URL(fileURLWithPath: left).standardizedFileURL.resolvingSymlinksInPath().path ==
            URL(fileURLWithPath: right).standardizedFileURL.resolvingSymlinksInPath().path
    }

    enum ProcessError: LocalizedError {
        case identityChanged(Int32)
        case killFailed(Int32, String)

        var errorDescription: String? {
            switch self {
            case .identityChanged(let pid): return "PID \(pid) 已被复用或路径发生变化，已拒绝终止。"
            case .killFailed(let pid, let detail): return "强制终止 PID \(pid) 失败：\(detail)"
            }
        }
    }
}
