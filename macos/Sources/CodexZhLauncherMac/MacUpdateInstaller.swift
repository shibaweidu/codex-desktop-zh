import AppKit
import CryptoKit
import Darwin
import Foundation

enum MacUpdateError: LocalizedError {
    case unavailable
    case oversizedDownload
    case checksumMissing
    case checksumMismatch
    case invalidBundle
    case invalidArchitecture
    case invalidSignature
    case targetNotWritable
    case invalidPaths
    case processFailed(String)

    var errorDescription: String? {
        switch self {
        case .unavailable: return "没有可安装的更新文件。"
        case .oversizedDownload: return "更新文件大小无效。"
        case .checksumMissing: return "SHA256SUMS.txt 中没有目标更新文件。"
        case .checksumMismatch: return "更新文件 SHA-256 校验失败。"
        case .invalidBundle: return "更新包中的 App Bundle 无效。"
        case .invalidArchitecture: return "更新包架构与当前 Mac 不匹配。"
        case .invalidSignature: return "更新包签名验证失败。"
        case .targetNotWritable: return "当前应用目录没有覆盖更新权限。"
        case .invalidPaths: return "更新路径验证失败。"
        case .processFailed(let message): return message
        }
    }
}

enum MacUpdateInstaller {
    private static let maxArchiveBytes = 150 * 1024 * 1024
    private static let maxChecksumBytes = 1024 * 1024
    private static let bundleIdentifier = "com.codexzh.launcher"

    @MainActor
    static func prepareAndLaunch(update: UpdateCheckResult) async throws {
        guard update.updateAvailable,
              let assetName = update.assetName,
              let assetURL = update.assetURL,
              let checksumURL = update.checksumURL
        else { throw MacUpdateError.unavailable }

        let targetBundle = Bundle.main.bundleURL.standardizedFileURL
        guard targetBundle.pathExtension.lowercased() == "app",
              !targetBundle.path.contains("/AppTranslocation/"),
              targetBundle == targetBundle.resolvingSymlinksInPath(),
              FileManager.default.isWritableFile(atPath: targetBundle.deletingLastPathComponent().path)
        else { throw MacUpdateError.targetNotWritable }

        let archive = try await download(assetURL, maximumBytes: maxArchiveBytes)
        guard update.assetSize <= 0 || Int64(archive.count) == update.assetSize else {
            throw MacUpdateError.oversizedDownload
        }
        let checksumData = try await download(checksumURL, maximumBytes: maxChecksumBytes)
        let checksumText = String(decoding: checksumData, as: UTF8.self)
        let expectedHash = try parseExpectedHash(checksumText, assetName: assetName)
        guard sha256(archive) == expectedHash.lowercased() else { throw MacUpdateError.checksumMismatch }

        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("CodexZhLauncher-update-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let archiveURL = root.appendingPathComponent(assetName)
        try archive.write(to: archiveURL, options: .atomic)
        let extracted = root.appendingPathComponent("extracted", isDirectory: true)
        try FileManager.default.createDirectory(at: extracted, withIntermediateDirectories: true)
        try run("/usr/bin/ditto", arguments: ["-x", "-k", archiveURL.path, extracted.path])

        guard let stagedBundle = try FileManager.default.contentsOfDirectory(
            at: extracted,
            includingPropertiesForKeys: nil
        ).first(where: { $0.pathExtension.lowercased() == "app" }) else {
            throw MacUpdateError.invalidBundle
        }
        let stagedExecutable = try validateBundle(stagedBundle, expectedVersion: update.latestVersion)

        let helper = root.appendingPathComponent("CodexZhLauncherUpdater")
        try FileManager.default.copyItem(at: stagedExecutable, to: helper)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: helper.path)
        let process = Process()
        process.executableURL = helper
        process.arguments = [
            "--apply-update-macos",
            String(ProcessInfo.processInfo.processIdentifier),
            targetBundle.path,
            stagedBundle.path,
            root.path,
            update.latestVersion
        ]
        try process.run()
    }

    static func apply(arguments: [String]) -> Int32 {
        guard arguments.count == 6, let parentPID = Int32(arguments[1]) else { return 2 }
        do {
            let target = URL(fileURLWithPath: arguments[2], isDirectory: true).standardizedFileURL
            let staged = URL(fileURLWithPath: arguments[3], isDirectory: true).standardizedFileURL
            let root = URL(fileURLWithPath: arguments[4], isDirectory: true).standardizedFileURL
            let version = arguments[5]
            try validateApplyPaths(target: target, staged: staged, root: root)
            _ = try validateBundle(staged, expectedVersion: version)
            try waitForExit(pid: parentPID)

            let backup = target.deletingLastPathComponent()
                .appendingPathComponent(".CodexZhLauncher-update-backup-\(UUID().uuidString).app", isDirectory: true)
            try FileManager.default.moveItem(at: target, to: backup)
            do {
                try FileManager.default.copyItem(at: staged, to: target)
                _ = try validateBundle(target, expectedVersion: version)
                try run("/usr/bin/open", arguments: ["-n", target.path])
                try? FileManager.default.removeItem(at: backup)
                try? FileManager.default.removeItem(at: root)
                return 0
            } catch {
                try? FileManager.default.removeItem(at: target)
                if FileManager.default.fileExists(atPath: backup.path) {
                    try? FileManager.default.moveItem(at: backup, to: target)
                }
                throw error
            }
        } catch {
            AppLogger.shared.write("update.apply.failed \(error)")
            return 1
        }
    }

    static func parseExpectedHash(_ contents: String, assetName: String) throws -> String {
        for line in contents.split(whereSeparator: { $0.isNewline }) {
            let fields = line.split(whereSeparator: { $0.isWhitespace })
            guard fields.count >= 2 else { continue }
            let hash = String(fields[0])
            let name = String(fields.last!).trimmingCharacters(in: CharacterSet(charactersIn: "*"))
            if name == assetName, hash.count == 64, hash.allSatisfy({ $0.isHexDigit }) {
                return hash.lowercased()
            }
        }
        throw MacUpdateError.checksumMissing
    }

    private static func validateBundle(_ bundleURL: URL, expectedVersion: String) throws -> URL {
        guard bundleURL.pathExtension.lowercased() == "app",
              bundleURL == bundleURL.resolvingSymlinksInPath(),
              let bundle = Bundle(url: bundleURL),
              bundle.bundleIdentifier == bundleIdentifier,
              bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String == expectedVersion,
              let executable = bundle.executableURL,
              FileManager.default.isExecutableFile(atPath: executable.path)
        else { throw MacUpdateError.invalidBundle }

        do { try run("/usr/bin/codesign", arguments: ["--verify", "--deep", "--strict", bundleURL.path]) }
        catch { throw MacUpdateError.invalidSignature }
        let architectures = try output("/usr/bin/lipo", arguments: ["-archs", executable.path])
            .split(whereSeparator: { $0.isWhitespace })
            .map(String.init)
        #if arch(arm64)
        guard architectures.contains("arm64") else { throw MacUpdateError.invalidArchitecture }
        #else
        guard architectures.contains("x86_64") else { throw MacUpdateError.invalidArchitecture }
        #endif
        return executable
    }

    private static func validateApplyPaths(target: URL, staged: URL, root: URL) throws {
        let temporary = FileManager.default.temporaryDirectory.resolvingSymlinksInPath().path + "/"
        let resolvedRoot = root.resolvingSymlinksInPath().path
        let resolvedStaged = staged.resolvingSymlinksInPath().path
        guard resolvedRoot.hasPrefix(temporary),
              resolvedStaged.hasPrefix(resolvedRoot + "/"),
              target.pathExtension.lowercased() == "app",
              target == target.resolvingSymlinksInPath(),
              !target.path.contains("/AppTranslocation/"),
              FileManager.default.fileExists(atPath: target.path),
              FileManager.default.isWritableFile(atPath: target.deletingLastPathComponent().path)
        else { throw MacUpdateError.invalidPaths }
    }

    private static func waitForExit(pid: Int32) throws {
        let deadline = Date().addingTimeInterval(30)
        while kill(pid, 0) == 0 || errno == EPERM {
            if Date() >= deadline { throw MacUpdateError.processFailed("等待旧版本退出超时。") }
            usleep(250_000)
        }
    }

    private static func download(_ url: URL, maximumBytes: Int) async throws -> Data {
        var request = URLRequest(url: url)
        request.timeoutInterval = 90
        request.setValue("CodexZhLauncher/\(LauncherModel.version)", forHTTPHeaderField: "User-Agent")
        let (data, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode),
              !data.isEmpty, data.count <= maximumBytes
        else { throw MacUpdateError.oversizedDownload }
        return data
    }

    private static func sha256(_ data: Data) -> String {
        SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
    }

    @discardableResult
    private static func run(_ executable: String, arguments: [String]) throws -> Int32 {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = arguments
        process.standardOutput = FileHandle.nullDevice
        process.standardError = FileHandle.nullDevice
        try process.run()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else {
            throw MacUpdateError.processFailed("更新验证命令执行失败：\(URL(fileURLWithPath: executable).lastPathComponent)")
        }
        return process.terminationStatus
    }

    private static func output(_ executable: String, arguments: [String]) throws -> String {
        let pipe = Pipe()
        let process = Process()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = arguments
        process.standardOutput = pipe
        process.standardError = FileHandle.nullDevice
        try process.run()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else {
            throw MacUpdateError.processFailed("更新验证命令执行失败：\(URL(fileURLWithPath: executable).lastPathComponent)")
        }
        return String(decoding: pipe.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self)
    }
}
