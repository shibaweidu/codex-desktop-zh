import AppKit
import Combine
import CryptoKit
import Foundation

struct WorkspaceTheme: Identifiable {
    let id: String
    let name: String
    let background: String
    let sidebar: String
    let foreground: String
    let accent: String

    static let all: [WorkspaceTheme] = [
        .init(id: "violet", name: "雾紫", background: "#1c1d23", sidebar: "#27272f", foreground: "#f5f4f8", accent: "#c4b5fd"),
        .init(id: "midnight", name: "午夜蓝", background: "#121c29", sidebar: "#192538", foreground: "#e1eafd", accent: "#9bbdff"),
        .init(id: "graphite", name: "石墨", background: "#202020", sidebar: "#292929", foreground: "#eeeeee", accent: "#c9c9c9"),
        .init(id: "forest", name: "松绿", background: "#17211e", sidebar: "#1e2d28", foreground: "#e3eee7", accent: "#a2c9ae"),
        .init(id: "paper", name: "纸白", background: "#f0efed", sidebar: "#e4e3df", foreground: "#35323a", accent: "#6b557f"),
        .init(id: "contrast", name: "高对比", background: "#080809", sidebar: "#17171b", foreground: "#ffffff", accent: "#dfcfff")
    ]
    static func find(_ id: String) -> WorkspaceTheme? { all.first { $0.id == id } }
}

enum WorkspaceServiceError: LocalizedError {
    case invalid(String)
    var errorDescription: String? { if case .invalid(let text) = self { return text }; return nil }
}

enum WorkspaceThemeClient {
    static func apply(port: UInt16, id: String, font: Int) async throws -> String {
        guard port > 0 else { throw WorkspaceServiceError.invalid("请先通过本工具启动 Codex。") }
        let client = DevToolsClient()
        let script = try SharedResources().buildThemeScript(id: id, fontSize: font)
        let targets = try await client.listTargets(port: port)
        for target in targets where ["page", "iframe", "webview"].contains(target.type) {
            guard let socket = target.webSocketDebuggerUrl else { continue }
            let ready = try? await client.evaluate(webSocketURL: socket, expression: "JSON.stringify({ready:!!(document.querySelector('#root')&&window.electronBridge)})", awaitPromise: false)
            guard ready?.contains("\"ready\":true") == true else { continue }
            return try await client.evaluate(webSocketURL: socket, expression: script, awaitPromise: false) ?? "{}"
        }
        throw WorkspaceServiceError.invalid("未找到本工具启动的 Codex 渲染页面，请重新启动后再试。")
    }
}

struct MacDownloadPlan: Sendable {
    let version: String
    let packageVersion: String
    let architecture: String
    let url: URL
    let length: Int64
    let sha256: String
    let archiveURL: URL
    let archiveName: String
    let archiveLength: Int64
    let archiveSHA256: String
    let bundleIdentifier: String
    let teamIdentifier: String

    static func parse(_ data: Data, architecture: String) throws -> MacDownloadPlan {
        guard ["x64", "arm64"].contains(architecture), data.count <= 4 * 1024 * 1024,
              let root = try JSONSerialization.jsonObject(with: data) as? [String: Any],
              let sources = root["sources"] as? [String: Any], let mac = sources["macos"] as? [String: Any],
              let entry = mac[architecture] as? [String: Any], entry["downloadable"] as? Bool == true,
              entry["currentForCodexVersion"] as? Bool == true,
              let version = entry["bundleShortVersion"] as? String,
              version.range(of: "^[0-9A-Za-z.\\-]{1,80}$", options: .regularExpression) != nil,
              let packageVersion = entry["bundleVersion"] as? String,
              packageVersion.range(of: "^[0-9]+$", options: .regularExpression) != nil,
              let bytes = entry["contentLength"] as? NSNumber, bytes.int64Value > 0, bytes.int64Value <= 4 * 1024 * 1024 * 1024,
              let hash = entry["sha256"] as? String, hash.range(of: "^[a-fA-F0-9]{64}$", options: .regularExpression) != nil,
              let address = entry["url"] as? String, let official = URL(string: address),
              official.scheme == "https", official.host == "persistent.oaistatic.com", official.pathExtension == "dmg",
              let archiveName = entry["sparkleArchiveFileName"] as? String,
              archiveName.range(of: "^Codex-darwin-(arm64|x64)-[0-9A-Za-z.\\-]+\\.zip$", options: .regularExpression) != nil,
              archiveName.contains("darwin-\(architecture)-"),
              let archiveVersion = entry["sparkleArchiveBundleShortVersion"] as? String, archiveVersion == version,
              let archiveLength = (entry["appcast"] as? [String: Any])?["enclosureLength"] as? NSNumber,
              archiveLength.int64Value > 0, archiveLength.int64Value <= 4 * 1024 * 1024 * 1024,
              let derived = root["derived"] as? [String: Any], let checksums = derived["latestChecksums"] as? [String: String],
              let archiveHash = checksums[archiveName], archiveHash.range(of: "^[a-fA-F0-9]{64}$", options: .regularExpression) != nil,
              let bundleID = entry["bundleIdentifier"] as? String, bundleID == "com.openai.codex",
              let teamID = entry["teamIdentifier"] as? String, teamID == "2DC432GLL2" else {
            throw WorkspaceServiceError.invalid("该架构没有有效的官方 DMG 或校验信息。")
        }
        let mirrorArchitecture = architecture == "arm64" ? "arm64" : "intel"
        let archiveURL = URL(string: "https://codexapp.agentsmirror.com/latest/mac/\(mirrorArchitecture)/\(archiveName)")!
        return .init(version: version, packageVersion: packageVersion, architecture: architecture,
                     url: URL(string: "https://codexapp.agentsmirror.com/latest/\(architecture == "arm64" ? "mac-arm64" : "mac-intel")")!,
                     length: bytes.int64Value, sha256: hash.lowercased(), archiveURL: archiveURL,
                     archiveName: archiveName, archiveLength: archiveLength.int64Value,
                     archiveSHA256: archiveHash.lowercased(), bundleIdentifier: bundleID, teamIdentifier: teamID)
    }

    static func needsUpdate(installed: String, installedBuild: String? = nil,
                            latest: String, latestBuild: String? = nil) throws -> Bool {
        func numericParts(_ value: String, minimumCount: Int) -> [Int]? {
            let values = value.split(separator: ".", omittingEmptySubsequences: false)
            guard values.count >= minimumCount, values.allSatisfy({ !$0.isEmpty && $0.allSatisfy(\.isNumber) }) else { return nil }
            let numbers = values.compactMap { Int($0) }
            return numbers.count == values.count ? numbers : nil
        }
        func compare(_ current: [Int], _ target: [Int]) -> ComparisonResult {
            for index in 0..<max(current.count, target.count) {
                let left = index < current.count ? current[index] : 0
                let right = index < target.count ? target[index] : 0
                if left < right { return .orderedAscending }
                if left > right { return .orderedDescending }
            }
            return .orderedSame
        }
        guard let current = numericParts(installed, minimumCount: 2), let target = numericParts(latest, minimumCount: 2) else {
            throw WorkspaceServiceError.invalid("无法确认客户端版本，已停止更新。")
        }
        let versionOrder = compare(current, target)
        if versionOrder != .orderedSame { return versionOrder == .orderedAscending }

        guard let installedBuild, let latestBuild else {
            if installedBuild == nil && latestBuild == nil { return false }
            throw WorkspaceServiceError.invalid("版本号相同，但无法读取两边的构建号；已停止更新以避免误判。")
        }
        guard let currentBuild = numericParts(installedBuild, minimumCount: 1),
              let targetBuild = numericParts(latestBuild, minimumCount: 1) else {
            throw WorkspaceServiceError.invalid("无法比较 Codex 构建版本，已停止更新。")
        }
        return compare(currentBuild, targetBuild) == .orderedAscending
    }
}

@MainActor
final class MacCodexDownloads: ObservableObject {
    @Published private(set) var busy = false
    @Published private(set) var status = "尚未开始下载"
    @Published private(set) var file: URL?
    @Published private(set) var plan: MacDownloadPlan?
    private var task: Task<Void, Never>?
    var onEvent: ((String) -> Void)?
    private func record(_ message: String) { AppLogger.shared.write(message); onEvent?(message) }

    func start(install: CodexInstall, architecture: String) {
        guard !busy else { return }
        busy = true; file = nil; plan = nil; status = "正在读取社区镜像版本清单…"
        record(status)
        task = Task {
            var temporary: URL?
            defer {
                if let temporary {
                    do { try FileManager.default.removeItem(at: temporary) }
                    catch { status += " 临时文件清理失败：\(temporary.path)，\(error.localizedDescription)"; record(status) }
                }
                busy = false; task = nil
            }
            do {
                let manifest = URL(string: "https://codexapp.agentsmirror.com/latest/manifest")!
                let (bytes, response) = try await URLSession.shared.bytes(for: URLRequest(url: manifest, timeoutInterval: 30))
                guard let manifestResponse=response as? HTTPURLResponse,manifestResponse.statusCode==200,
                      ["codexapp.agentsmirror.com","codexapp-r2.agentsmirror.com","codexapp-s3.agentsmirror.com"].contains(manifestResponse.url?.host ?? "") else { throw WorkspaceServiceError.invalid("版本清单请求失败或重定向到了未许可的来源。") }
                var data = Data()
                for try await byte in bytes {
                    guard data.count < 4 * 1024 * 1024 else { throw WorkspaceServiceError.invalid("下载清单过大。") }
                    data.append(byte)
                }
                let plan = try MacDownloadPlan.parse(data, architecture: architecture)
                try Task.checkCancellation()
                guard install.bundleIdentifier == plan.bundleIdentifier else { throw WorkspaceServiceError.invalid("检测到的应用不是社区镜像支持的 Codex Bundle。") }
                guard try MacDownloadPlan.needsUpdate(installed: install.version, installedBuild: install.buildVersion,
                                                       latest: plan.version, latestBuild: plan.packageVersion) else {
                    status = "已是最新版本 · 本机 \(install.version) (\(install.buildVersion ?? "构建号未知")) · 镜像 \(plan.version) (\(plan.packageVersion))"
                    record(status); return
                }
                self.plan = plan
                status = "发现更新 \(plan.version) (构建 \(plan.packageVersion))，正在通过社区镜像下载（约 \(plan.archiveLength / 1024 / 1024) MB）…"
                record("codex.update.begin current=\(install.version)/\(install.buildVersion ?? "unknown") target=\(plan.version)/\(plan.packageVersion) arch=\(architecture) sha256=\(plan.archiveSHA256)")
                var archiveRequest = URLRequest(url: plan.archiveURL, timeoutInterval: 120)
                archiveRequest.setValue("CodexZhLauncher/\(LauncherModel.version)", forHTTPHeaderField: "User-Agent")
                let (downloaded, result) = try await URLSession.shared.download(for: archiveRequest)
                temporary = downloaded
                guard let http = result as? HTTPURLResponse, http.statusCode == 200, http.url?.scheme == "https",
                      ["codexapp.agentsmirror.com", "codexapp-r2.agentsmirror.com", "codexapp-s3.agentsmirror.com"].contains(http.url?.host ?? "") else {
                    throw WorkspaceServiceError.invalid("更新包下载失败或重定向到了未许可的来源。")
                }
                status = "下载完成，正在校验 ZIP 大小和 SHA-256…"
                record(status)
                let checksum = try await Task.detached(priority: .utility) { try Self.checksum(downloaded, expectedLength: plan.archiveLength) }.value
                try Task.checkCancellation()
                guard checksum == plan.archiveSHA256 else { throw WorkspaceServiceError.invalid("ZIP SHA-256 不匹配，已拒绝更新。") }
                let folder = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask)[0].appendingPathComponent("Codex", isDirectory: true)
                try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
                let saved = folder.appendingPathComponent(plan.archiveName)
                try? FileManager.default.removeItem(at: saved)
                try FileManager.default.moveItem(at: downloaded, to: saved)
                temporary = nil; file = saved
                status = "SHA-256 校验通过。点击“安装并重启 Codex”确认后开始更新。"
                record("codex.update.verified version=\(plan.version) path=\(saved.path)")
            } catch {
                status = Task.isCancelled ? "下载已取消，未验证文件已清理。" : "下载失败：\(error.localizedDescription)"
                record("codex.download \(status)")
            }
        }
    }
    func installUpdate(install: CodexInstall, launcherBundle: URL) async throws {
        guard !busy, let file, let plan, FileManager.default.fileExists(atPath: file.path) else {
            throw WorkspaceServiceError.invalid("没有可安装的已校验更新包。请重新检查并下载。")
        }
        let archive = file
        let expected = plan.archiveSHA256
        let archiveSize = plan.archiveLength
        let checksum = try await Task.detached(priority: .utility) { try Self.checksum(archive, expectedLength: archiveSize) }.value
        guard checksum == expected else { throw WorkspaceServiceError.invalid("更新包已发生变化，SHA-256 复核失败。请重新下载。") }
        guard try MacDownloadPlan.needsUpdate(installed: install.version, installedBuild: install.buildVersion,
                                               latest: plan.version, latestBuild: plan.packageVersion) else {
            throw WorkspaceServiceError.invalid("Codex 已是相同或更新版本，拒绝降级。请重新检查更新。")
        }
        try await Task.detached(priority: .utility) {
            try CodexAppUpdateInstaller.prepareAndLaunch(archive: archive, targetBundle: install.bundleURL, launcherBundle: launcherBundle, plan: plan)
        }.value
    }
    func cancel() { task?.cancel() }
    func report(_ message: String) { status = message; record(message) }
    nonisolated static func checksum(_ file: URL, expectedLength: Int64) throws -> String {
        let attributes = try FileManager.default.attributesOfItem(atPath: file.path)
        guard (attributes[.size] as? NSNumber)?.int64Value == expectedLength else { throw WorkspaceServiceError.invalid("安装包大小与选定版本不符。") }
        let handle = try FileHandle(forReadingFrom: file); defer { try? handle.close() }
        var hash = SHA256()
        while let data = try handle.read(upToCount: 1024 * 1024), !data.isEmpty { hash.update(data: data) }
        return hash.finalize().map { String(format: "%02x", $0) }.joined()
    }
}
