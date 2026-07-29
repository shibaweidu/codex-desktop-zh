import Foundation

struct UpdateCheckResult: Equatable {
    let updateAvailable: Bool
    let currentVersion: String
    let latestVersion: String
    let releaseURL: URL
    let assetName: String?
    let assetURL: URL?
    let assetSize: Int64
    let checksumURL: URL?

    var message: String {
        updateAvailable ? "发现新版本 v\(latestVersion)" : "当前已是最新版本 v\(currentVersion)"
    }
}

enum UpdateCheckError: LocalizedError {
    case invalidResponse
    case invalidVersion(String)
    case invalidReleaseURL

    var errorDescription: String? {
        switch self {
        case .invalidResponse: return "GitHub 返回的最新版本无效。"
        case .invalidVersion(let value): return "无法识别版本号：\(value)"
        case .invalidReleaseURL: return "更新地址不是本项目的 GitHub Release。"
        }
    }
}

struct GitHubUpdateChecker {
    private struct ReleasePayload: Decodable {
        let tagName: String
        let htmlURL: URL
        let draft: Bool
        let prerelease: Bool
        let assets: [AssetPayload]

        enum CodingKeys: String, CodingKey {
            case tagName = "tag_name"
            case htmlURL = "html_url"
            case draft
            case prerelease
            case assets
        }
    }

    private struct AssetPayload: Decodable {
        let name: String
        let browserDownloadURL: URL
        let size: Int64

        enum CodingKeys: String, CodingKey {
            case name
            case browserDownloadURL = "browser_download_url"
            case size
        }
    }

    static let repositoryURL = URL(string: "https://github.com/shibaweidu/codex-desktop-zh")!
    static let feedbackURL = URL(string: "https://github.com/shibaweidu/codex-desktop-zh/issues/new/choose")!
    static let latestReleaseURL = URL(string: "https://github.com/shibaweidu/codex-desktop-zh/releases/latest")!
    private static let apiURL = URL(string: "https://api.github.com/repos/shibaweidu/codex-desktop-zh/releases/latest")!
    static var expectedAssetName: String {
        #if arch(arm64)
        return "Codex-Zh-Launcher-macOS-arm64.zip"
        #else
        return "Codex-Zh-Launcher-macOS-x64.zip"
        #endif
    }

    func check(currentVersion: String) async throws -> UpdateCheckResult {
        var request = URLRequest(url: Self.apiURL)
        request.timeoutInterval = 12
        request.setValue("CodexZhLauncher/\(currentVersion)", forHTTPHeaderField: "User-Agent")
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        let (data, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
            throw UpdateCheckError.invalidResponse
        }
        return try Self.evaluate(data: data, currentVersion: currentVersion)
    }

    static func evaluate(data: Data, currentVersion: String) throws -> UpdateCheckResult {
        let release = try JSONDecoder().decode(ReleasePayload.self, from: data)
        guard !release.draft, !release.prerelease else { throw UpdateCheckError.invalidResponse }
        guard release.htmlURL.scheme == "https",
              release.htmlURL.host?.lowercased() == "github.com",
              release.htmlURL.path.lowercased().hasPrefix("/shibaweidu/codex-desktop-zh/releases/")
        else { throw UpdateCheckError.invalidReleaseURL }

        let latest = try versionComponents(release.tagName)
        let current = try versionComponents(currentVersion)
        let available = compare(latest, current) == .orderedDescending
        var updateAsset: AssetPayload?
        var checksumAsset: AssetPayload?
        if available {
            updateAsset = release.assets.first { $0.name == expectedAssetName }
            checksumAsset = release.assets.first { $0.name == "SHA256SUMS.txt" }
            guard let updateAsset, updateAsset.size > 0, checksumAsset != nil else {
                throw UpdateCheckError.invalidResponse
            }
            _ = try validateAssetURL(updateAsset.browserDownloadURL, name: updateAsset.name)
            _ = try validateAssetURL(checksumAsset!.browserDownloadURL, name: checksumAsset!.name)
        }
        return UpdateCheckResult(
            updateAvailable: available,
            currentVersion: current.map(String.init).joined(separator: "."),
            latestVersion: latest.map(String.init).joined(separator: "."),
            releaseURL: release.htmlURL,
            assetName: updateAsset?.name,
            assetURL: updateAsset?.browserDownloadURL,
            assetSize: updateAsset?.size ?? 0,
            checksumURL: checksumAsset?.browserDownloadURL
        )
    }

    private static func validateAssetURL(_ url: URL, name: String) throws -> URL {
        guard url.scheme == "https",
              url.host?.lowercased() == "github.com",
              url.path.lowercased().hasPrefix("/shibaweidu/codex-desktop-zh/releases/download/"),
              url.lastPathComponent == name
        else { throw UpdateCheckError.invalidReleaseURL }
        return url
    }

    private static func versionComponents(_ value: String) throws -> [Int] {
        var normalized = value.trimmingCharacters(in: .whitespacesAndNewlines)
        if normalized.first == "v" || normalized.first == "V" { normalized.removeFirst() }
        normalized = normalized
            .split(whereSeparator: { $0 == "-" || $0 == "+" })
            .first
            .map(String.init) ?? ""
        let parts = normalized.split(separator: ".")
        let components = parts.compactMap { Int($0) }
        guard components.count >= 2, components.count == parts.count else {
            throw UpdateCheckError.invalidVersion(value)
        }
        return components
    }

    private static func compare(_ left: [Int], _ right: [Int]) -> ComparisonResult {
        for index in 0..<max(left.count, right.count) {
            let lhs = index < left.count ? left[index] : 0
            let rhs = index < right.count ? right[index] : 0
            if lhs < rhs { return .orderedAscending }
            if lhs > rhs { return .orderedDescending }
        }
        return .orderedSame
    }
}
