import AppKit
import Foundation

struct CodexDiscovery {
    static let savedPathKey = "portableBundlePath"

    private let fileManager: FileManager
    private let defaults: UserDefaults

    init(fileManager: FileManager = .default, defaults: UserDefaults = .standard) {
        self.fileManager = fileManager
        self.defaults = defaults
    }

    func detect() -> CodexInstall? {
        if let saved = defaults.string(forKey: Self.savedPathKey),
           let install = try? buildInstall(bundleURL: URL(fileURLWithPath: saved), kind: "已保存路径") {
            return install
        }

        let homeApplications = fileManager.homeDirectoryForCurrentUser
            .appendingPathComponent("Applications", isDirectory: true)
        let candidates = [
            URL(fileURLWithPath: "/Applications/Codex.app", isDirectory: true),
            homeApplications.appendingPathComponent("Codex.app", isDirectory: true),
            URL(fileURLWithPath: "/Applications/ChatGPT.app", isDirectory: true),
            homeApplications.appendingPathComponent("ChatGPT.app", isDirectory: true)
        ]
        let installs = candidates.compactMap { try? buildInstall(bundleURL: $0, kind: "自动检测") }
        return installs.sorted { left, right in
            if left.bundleURL.lastPathComponent != right.bundleURL.lastPathComponent {
                return left.bundleURL.lastPathComponent == "Codex.app"
            }
            return left.version.compare(right.version, options: .numeric) == .orderedDescending
        }.first
    }

    func useBundle(_ bundleURL: URL) throws -> CodexInstall {
        let install = try buildInstall(bundleURL: bundleURL, kind: "手动选择")
        defaults.set(install.bundleURL.path, forKey: Self.savedPathKey)
        return install
    }

    func clearSavedBundle() {
        defaults.removeObject(forKey: Self.savedPathKey)
    }

    func buildInstall(bundleURL: URL, kind: String) throws -> CodexInstall {
        let resolvedBundle = bundleURL.standardizedFileURL.resolvingSymlinksInPath()
        guard resolvedBundle.pathExtension.lowercased() == "app" else {
            throw DiscoveryError.notApplicationBundle
        }
        var isDirectory: ObjCBool = false
        guard fileManager.fileExists(atPath: resolvedBundle.path, isDirectory: &isDirectory), isDirectory.boolValue else {
            throw DiscoveryError.bundleNotFound
        }
        let infoURL = resolvedBundle.appendingPathComponent("Contents/Info.plist")
        guard let info = NSDictionary(contentsOf: infoURL) as? [String: Any],
              let executableName = info["CFBundleExecutable"] as? String,
              !executableName.isEmpty else {
            throw DiscoveryError.invalidInfoPlist
        }
        guard ["Codex", "ChatGPT"].contains(where: {
            $0.caseInsensitiveCompare(executableName) == .orderedSame
        }) else {
            throw DiscoveryError.unsupportedApplication
        }
        let executable = resolvedBundle
            .appendingPathComponent("Contents/MacOS", isDirectory: true)
            .appendingPathComponent(executableName)
            .standardizedFileURL.resolvingSymlinksInPath()
        guard Self.isPath(executable.path, within: resolvedBundle.path),
              fileManager.isExecutableFile(atPath: executable.path) else {
            throw DiscoveryError.invalidExecutable
        }
        let identifier = (info["CFBundleIdentifier"] as? String) ?? ""
        guard !identifier.isEmpty else { throw DiscoveryError.invalidInfoPlist }
        let version = (info["CFBundleShortVersionString"] as? String)
            ?? (info["CFBundleVersion"] as? String)
            ?? "-"
        let displayName = (info["CFBundleDisplayName"] as? String)
            ?? (info["CFBundleName"] as? String)
            ?? resolvedBundle.deletingPathExtension().lastPathComponent
        return CodexInstall(
            kind: kind,
            displayName: "\(displayName)（macOS）",
            version: version,
            bundleURL: resolvedBundle,
            executableURL: executable,
            bundleIdentifier: identifier
        )
    }

    static func isPath(_ path: String, within directory: String) -> Bool {
        let candidate = URL(fileURLWithPath: path).standardizedFileURL.resolvingSymlinksInPath().path
        let root = URL(fileURLWithPath: directory).standardizedFileURL.resolvingSymlinksInPath().path
        guard root != "/" else { return candidate.hasPrefix("/") }
        return candidate == root || candidate.hasPrefix(root + "/")
    }

    enum DiscoveryError: LocalizedError {
        case bundleNotFound
        case notApplicationBundle
        case invalidInfoPlist
        case invalidExecutable
        case unsupportedApplication

        var errorDescription: String? {
            switch self {
            case .bundleNotFound: return "找不到所选 Codex.app。"
            case .notApplicationBundle: return "请选择 Codex.app 或 ChatGPT.app。"
            case .invalidInfoPlist: return "应用包缺少有效的 Info.plist 信息。"
            case .invalidExecutable: return "应用包中的主程序无效或路径越界。"
            case .unsupportedApplication: return "请选择主程序为 Codex 或 ChatGPT 的应用。"
            }
        }
    }
}
