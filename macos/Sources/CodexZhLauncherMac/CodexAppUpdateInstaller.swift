import AppKit
import Darwin
import Foundation

enum CodexAppUpdateInstaller {
    private static let codexBundleID = "com.openai.codex"
    private static let launcherBundleID = "com.codexzh.launcher"
    static func prepareAndLaunch(archive: URL, targetBundle: URL, launcherBundle: URL, plan: MacDownloadPlan) throws {
        let target = targetBundle.standardizedFileURL.resolvingSymlinksInPath()
        guard target.pathExtension.lowercased() == "app", target == targetBundle.standardizedFileURL,
              !target.path.contains("/AppTranslocation/"), FileManager.default.fileExists(atPath: target.path) else {
            throw WorkspaceServiceError.invalid("Codex 所在路径无效或经过 App Translocation，不能自动覆盖。")
        }
        guard try MacDownloadPlan.needsUpdate(installed: installedVersion(at: target),
                                               installedBuild: installedBuildVersion(at: target),
                                               latest: plan.version, latestBuild: plan.packageVersion) else {
            throw WorkspaceServiceError.invalid("Codex 已是相同或更新版本，拒绝降级覆盖。")
        }
        _ = try validate(target, version: installedVersion(at: target), packageVersion: installedBuildVersion(at: target),
                         team: plan.teamIdentifier, architecture: plan.architecture, bundleIdentifier: plan.bundleIdentifier)
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("CodexZh-CodexUpdate-\(UUID().uuidString)", isDirectory: true)
        let resolvedRoot = root.standardizedFileURL.resolvingSymlinksInPath()
        try FileManager.default.createDirectory(at: resolvedRoot, withIntermediateDirectories: true)
        do {
            let extracted = resolvedRoot.appendingPathComponent("extracted", isDirectory: true)
            try FileManager.default.createDirectory(at: extracted, withIntermediateDirectories: true)
            try run("/usr/bin/ditto", ["-x", "-k", archive.path, extracted.path])
            let apps = try FileManager.default.contentsOfDirectory(at: extracted, includingPropertiesForKeys: nil)
                .filter { $0.pathExtension.lowercased() == "app" }
            guard apps.count == 1, let staged = apps.first else {
                throw WorkspaceServiceError.invalid("社区镜像更新 ZIP 中没有唯一的 Codex 应用。")
            }
            _ = try validate(staged, version: plan.version, packageVersion: plan.packageVersion,
                             team: plan.teamIdentifier, architecture: plan.architecture,
                             bundleIdentifier: plan.bundleIdentifier)
            guard let helperSource = Bundle(url: launcherBundle)?.executableURL else {
                throw WorkspaceServiceError.invalid("无法定位安全更新程序。")
            }
            let process = Process()
            process.executableURL = helperSource
            process.arguments = ["--apply-codex-update-macos", String(getpid()), target.path, staged.path, resolvedRoot.path,
                                plan.version, plan.packageVersion, plan.bundleIdentifier, plan.teamIdentifier, plan.architecture, launcherBundle.path]
            try process.run()
        } catch {
            try? FileManager.default.removeItem(at: resolvedRoot)
            throw error
        }
    }

    static func apply(arguments: [String], restart: Bool = true) -> Int32 {
        guard arguments.count == 11, let parentPID = Int32(arguments[1]) else { return 2 }
        let target = URL(fileURLWithPath: arguments[2], isDirectory: true).standardizedFileURL
        let staged = URL(fileURLWithPath: arguments[3], isDirectory: true).standardizedFileURL.resolvingSymlinksInPath()
        let root = URL(fileURLWithPath: arguments[4], isDirectory: true).standardizedFileURL.resolvingSymlinksInPath()
        let version = arguments[5], packageVersion = arguments[6], bundleID = arguments[7], team = arguments[8], architecture = arguments[9]
        let launcherBundle = URL(fileURLWithPath: arguments[10], isDirectory: true).standardizedFileURL
        do {
            let tmp = FileManager.default.temporaryDirectory.resolvingSymlinksInPath().path + "/"
            guard root.path.hasPrefix(tmp), staged.path.hasPrefix(root.path + "/"),
                  target.pathExtension.lowercased() == "app", target == target.resolvingSymlinksInPath(),
                  !target.path.contains("/AppTranslocation/"), FileManager.default.fileExists(atPath: target.path) else {
                throw WorkspaceServiceError.invalid("Codex 覆盖更新路径验证失败。")
            }
            guard launcherBundle.pathExtension.lowercased() == "app", launcherBundle == launcherBundle.resolvingSymlinksInPath(),
                  let launcherInfo = NSDictionary(contentsOf: launcherBundle.appendingPathComponent("Contents/Info.plist")) as? [String: Any],
                  launcherInfo["CFBundleIdentifier"] as? String == launcherBundleID else {
                throw WorkspaceServiceError.invalid("无法验证汉化工具的重启路径。")
            }
            _ = try validate(staged, version: version, packageVersion: packageVersion, team: team, architecture: architecture, bundleIdentifier: bundleID)
            guard try MacDownloadPlan.needsUpdate(installed: installedVersion(at: target),
                                                   installedBuild: installedBuildVersion(at: target),
                                                   latest: version, latestBuild: packageVersion) else {
                throw WorkspaceServiceError.invalid("Codex 已更新到相同或更高版本，取消旧更新包安装。")
            }
            _ = try validate(target, version: installedVersion(at: target), packageVersion: installedBuildVersion(at: target),
                             team: team, architecture: architecture, bundleIdentifier: bundleID)
            let current = try CodexDiscovery().buildInstall(bundleURL: target, kind: "updater-check")
            guard DarwinProcessService().scan(install: current).totalPotentialCount == 0 else {
                throw WorkspaceServiceError.invalid("检测到 Codex 正在运行，旧版应用未更改。请退出后重试。")
            }
            try waitForExit(parentPID)
            let parent = target.deletingLastPathComponent()
            let backup = parent.appendingPathComponent(".Codex-update-backup-\(UUID().uuidString).app", isDirectory: true)
            if FileManager.default.isWritableFile(atPath: parent.path) {
                try FileManager.default.moveItem(at: target, to: backup)
                do {
                    try run("/usr/bin/ditto", ["--rsrc", "--extattr", "--acl", staged.path, target.path])
                    _ = try validate(target, version: version, packageVersion: packageVersion, team: team, architecture: architecture, bundleIdentifier: bundleID)
                    try? FileManager.default.removeItem(at: backup)
                } catch {
                    try? FileManager.default.removeItem(at: target)
                    if FileManager.default.fileExists(atPath: backup.path) { try? FileManager.default.moveItem(at: backup, to: target) }
                    throw error
                }
            } else {
                try replaceWithAdministratorAuthorization(target: target, staged: staged, backup: backup,
                                                           version: version, packageVersion: packageVersion,
                                                           team: team, architecture: architecture,
                                                           bundleIdentifier: bundleID)
            }
            if restart { try run("/usr/bin/open", ["-n", launcherBundle.path, "--args", "--launch-zh"]) }
            try? FileManager.default.removeItem(at: root)
            return 0
        } catch {
            AppLogger.shared.write("codex.update.apply.failed \(error.localizedDescription)")
            if restart, FileManager.default.fileExists(atPath: launcherBundle.path) {
                try? run("/usr/bin/open", ["-n", launcherBundle.path])
            }
            return 1
        }
    }

    private static func replaceWithAdministratorAuthorization(target: URL, staged: URL, backup: URL,
                                                               version: String, packageVersion: String, team: String,
                                                               architecture: String, bundleIdentifier: String) throws {
        let targetPath = shellLiteral(target.path), stagedPath = shellLiteral(staged.path)
        let backupPath = shellLiteral(backup.path)
        let infoPath = shellLiteral(target.appendingPathComponent("Contents/Info.plist").path)
        let id = shellLiteral(bundleIdentifier), expectedVersion = shellLiteral(version)
        let expectedPackageVersion = shellLiteral(packageVersion)
        let teamID = shellLiteral(team)
        let expectedArchitecture = shellLiteral(architecture == "arm64" ? "arm64" : "x86_64")
        let command = "set -e; set -o pipefail; /usr/bin/codesign --verify --deep --strict \(stagedPath); /bin/mv \(targetPath) \(backupPath); if /usr/bin/ditto --rsrc --extattr --acl \(stagedPath) \(targetPath) && /usr/bin/codesign --verify --deep --strict \(targetPath) && /usr/bin/codesign -dv --verbose=4 \(targetPath) 2>&1 | /usr/bin/grep -Fq \(teamID) && [ \"$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' \(infoPath))\" = \(id) ] && [ \"$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' \(infoPath))\" = \(expectedVersion) ] && [ \"$(/usr/libexec/PlistBuddy -c 'Print :CFBundleVersion' \(infoPath))\" = \(expectedPackageVersion) ] && executable=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' \(infoPath)) && printf '%s' \"$executable\" | /usr/bin/grep -Eq '^[A-Za-z0-9._-]+$' && [ -x \(targetPath)/Contents/MacOS/$executable ] && /usr/bin/lipo -verify_arch \(expectedArchitecture) \(targetPath)/Contents/MacOS/$executable; then /bin/rm -rf \(backupPath) || true; else /bin/rm -rf \(targetPath); /bin/mv \(backupPath) \(targetPath); exit 1; fi"
        let appleScript = "do shell script \(appleScriptLiteral(command)) with administrator privileges with prompt \(appleScriptLiteral("Install the verified Codex update."))"
        try run("/usr/bin/osascript", ["-e", appleScript])
    }

    private static func shellLiteral(_ value: String) -> String {
        "'" + value.replacingOccurrences(of: "'", with: "'\"'\"'" ) + "'"
    }

    private static func appleScriptLiteral(_ value: String) -> String {
        "\"" + value.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"") + "\""
    }

    private static func validate(_ bundle: URL, version: String, packageVersion: String? = nil, team: String, architecture: String,
                                 bundleIdentifier: String = codexBundleID) throws -> URL {
        let resolved = bundle.standardizedFileURL.resolvingSymlinksInPath()
        guard bundle.pathExtension.lowercased() == "app",
              let info = NSDictionary(contentsOf: resolved.appendingPathComponent("Contents/Info.plist")) as? [String: Any],
              info["CFBundleIdentifier"] as? String == bundleIdentifier,
              info["CFBundleShortVersionString"] as? String == version,
              (packageVersion == nil || info["CFBundleVersion"] as? String == packageVersion),
              let name = info["CFBundleExecutable"] as? String else {
            throw WorkspaceServiceError.invalid("更新包中的 Codex Bundle ID 或版本不匹配。")
        }
        let executable = resolved.appendingPathComponent("Contents/MacOS/\(name)").standardizedFileURL.resolvingSymlinksInPath()
        guard CodexDiscovery.isPath(executable.path, within: resolved.path), FileManager.default.isExecutableFile(atPath: executable.path) else {
            throw WorkspaceServiceError.invalid("更新包主程序路径无效。")
        }
        try run("/usr/bin/codesign", ["--verify", "--deep", "--strict", resolved.path])
        let details = try output("/usr/bin/codesign", ["-dv", "--verbose=4", resolved.path])
        guard details.contains("TeamIdentifier=\(team)") else { throw WorkspaceServiceError.invalid("Codex Apple Developer Team ID 不匹配。") }
        let arches = try output("/usr/bin/lipo", ["-archs", executable.path]).split(whereSeparator: \.isWhitespace).map(String.init)
        guard arches.contains(architecture == "arm64" ? "arm64" : "x86_64") else {
            throw WorkspaceServiceError.invalid("Codex 更新包与本机处理器架构不匹配。")
        }
        return executable
    }

    private static func waitForExit(_ pid: Int32) throws {
        let deadline = Date().addingTimeInterval(30)
        while true {
            let result = kill(pid, 0)
            if result == -1 && errno == ESRCH { return }
            if result == -1 && errno != EPERM { return }
            if Date() >= deadline { throw WorkspaceServiceError.invalid("等待更新工具退出超时，旧版 Codex 未更改。") }
            usleep(250_000)
        }
    }

    private static func installedVersion(at bundle: URL) throws -> String {
        guard let info = NSDictionary(contentsOf: bundle.appendingPathComponent("Contents/Info.plist")) as? [String: Any],
              let version = info["CFBundleShortVersionString"] as? String else {
            throw WorkspaceServiceError.invalid("无法读取现有 Codex 版本，拒绝覆盖。")
        }
        return version
    }

    private static func installedBuildVersion(at bundle: URL) throws -> String {
        guard let info = NSDictionary(contentsOf: bundle.appendingPathComponent("Contents/Info.plist")) as? [String: Any],
              let version = info["CFBundleVersion"] as? String else {
            throw WorkspaceServiceError.invalid("无法读取现有 Codex 构建版本，拒绝覆盖。")
        }
        return version
    }

    @discardableResult private static func run(_ executable: String, _ arguments: [String]) throws -> Int32 {
        let process = Process(); process.executableURL = URL(fileURLWithPath: executable); process.arguments = arguments
        process.standardOutput = FileHandle.nullDevice; process.standardError = FileHandle.nullDevice
        try process.run(); process.waitUntilExit()
        guard process.terminationStatus == 0 else { throw WorkspaceServiceError.invalid("更新校验命令失败：\(URL(fileURLWithPath: executable).lastPathComponent)") }
        return process.terminationStatus
    }

    private static func output(_ executable: String, _ arguments: [String]) throws -> String {
        let pipe = Pipe(), process = Process()
        process.executableURL = URL(fileURLWithPath: executable); process.arguments = arguments
        process.standardOutput = pipe; process.standardError = pipe
        try process.run(); let data = pipe.fileHandleForReading.readDataToEndOfFile(); process.waitUntilExit()
        guard process.terminationStatus == 0 else { throw WorkspaceServiceError.invalid("更新信息读取失败：\(URL(fileURLWithPath: executable).lastPathComponent)") }
        return String(decoding: data, as: UTF8.self)
    }
}
