import Foundation
import XCTest
@testable import CodexZhLauncherMac

final class WorkspaceServicesTests: XCTestCase {
    private func manifest(url: String = "https://persistent.oaistatic.com/Codex.dmg", hash: String = String(repeating: "a", count: 64), length: Int64 = 3, downloadable: Bool = true, current: Bool = true) throws -> Data {
        let archive = "Codex-darwin-arm64-26.1.2.zip"
        let entry: [String: Any] = ["downloadable": downloadable, "currentForCodexVersion": current, "bundleShortVersion": "26.1.2", "bundleVersion": "42", "contentLength": length, "sha256": hash, "url": url, "sparkleArchiveFileName": archive, "sparkleArchiveBundleShortVersion": "26.1.2", "bundleIdentifier": "com.openai.codex", "teamIdentifier": "2DC432GLL2", "appcast": ["enclosureLength": 3], "latestChecksums": [archive: String(repeating: "b", count: 64)]]
        let intelArchive = "Codex-darwin-x64-26.1.2.zip"
        var intel = entry; intel["sparkleArchiveFileName"] = intelArchive; intel["latestChecksums"] = [intelArchive: String(repeating: "c", count: 64)]
        return try JSONSerialization.data(withJSONObject: ["codexVersion": "WRONG", "derived": ["latestChecksums": [archive: String(repeating: "b", count: 64), intelArchive: String(repeating: "c", count: 64)]], "sources": ["macos": ["arm64": entry, "x64": intel]]])
    }
    func testDownloadPlanUsesPlatformVersion() throws {
        let plan = try MacDownloadPlan.parse(manifest(), architecture: "arm64")
        XCTAssertEqual(plan.version, "26.1.2")
        XCTAssertEqual(plan.packageVersion, "42")
        XCTAssertEqual(plan.architecture, "arm64")
        XCTAssertEqual(plan.url.host, "codexapp.agentsmirror.com")
        XCTAssertEqual(plan.archiveURL.path, "/latest/mac/arm64/Codex-darwin-arm64-26.1.2.zip")
        XCTAssertEqual(plan.archiveSHA256, String(repeating: "b", count: 64))
    }
    func testMirrorArchitecture() throws {
        XCTAssertEqual(try MacDownloadPlan.parse(manifest(), architecture: "x64").url.lastPathComponent, "mac-intel")
        XCTAssertEqual(try MacDownloadPlan.parse(manifest(), architecture: "x64").archiveURL.path, "/latest/mac/intel/Codex-darwin-x64-26.1.2.zip")
    }
    func testVersionComparisonAndNoDowngrade() throws {
        XCTAssertFalse(try MacDownloadPlan.needsUpdate(installed: "26.1111.71025", latest: "26.1111.71025"))
        XCTAssertFalse(try MacDownloadPlan.needsUpdate(installed: "27.0.0", latest: "26.1111.71025"))
        XCTAssertTrue(try MacDownloadPlan.needsUpdate(installed: "26.3.4", latest: "26.1111.71025"))
        XCTAssertThrowsError(try MacDownloadPlan.needsUpdate(installed: "unknown", latest: "26.1111.71025"))
    }
    func testBuildVersionBreaksTiesWhenMarketingVersionIsUnchanged() throws {
        XCTAssertTrue(try MacDownloadPlan.needsUpdate(installed: "26.1111.71025", installedBuild: "28660",
                                                       latest: "26.1111.71025", latestBuild: "28661"))
        XCTAssertFalse(try MacDownloadPlan.needsUpdate(installed: "26.1111.71025", installedBuild: "28662",
                                                        latest: "26.1111.71025", latestBuild: "28661"))
        XCTAssertFalse(try MacDownloadPlan.needsUpdate(installed: "26.1111.71025", installedBuild: "28661",
                                                        latest: "26.1111.71025", latestBuild: "28661"))
        XCTAssertThrowsError(try MacDownloadPlan.needsUpdate(installed: "26.1111.71025", latest: "26.1111.71025",
                                                              latestBuild: "28661"))
        XCTAssertThrowsError(try MacDownloadPlan.needsUpdate(installed: "26..1", latest: "26.1111.71025"))
    }
    func testInvalidManifestRejected() throws {
        XCTAssertThrowsError(try MacDownloadPlan.parse(manifest(), architecture: "x86"))
        XCTAssertThrowsError(try MacDownloadPlan.parse(manifest(downloadable: false), architecture: "arm64"))
        XCTAssertThrowsError(try MacDownloadPlan.parse(manifest(current: false), architecture: "arm64"))
        XCTAssertThrowsError(try MacDownloadPlan.parse(manifest(length: -1), architecture: "arm64"))
        XCTAssertThrowsError(try MacDownloadPlan.parse(manifest(hash: "bad"), architecture: "arm64"))
        XCTAssertThrowsError(try MacDownloadPlan.parse(manifest(url: "https://example.com/Codex.dmg"), architecture: "arm64"))
        XCTAssertThrowsError(try MacDownloadPlan.parse(manifest(url: "http://persistent.oaistatic.com/Codex.dmg"), architecture: "arm64"))
        XCTAssertThrowsError(try MacDownloadPlan.parse(Data("{}".utf8), architecture: "arm64"))
    }
    func testChecksumAndLengthOnTemporaryFile() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: url) }
        try Data("abc".utf8).write(to: url)
        XCTAssertEqual(try MacCodexDownloads.checksum(url, expectedLength: 3), "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")
        XCTAssertThrowsError(try MacCodexDownloads.checksum(url, expectedLength: 4))
    }
    func testThemeResourcesAndBounds() throws {
        let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().appendingPathComponent("shared")
        let resources = try SharedResources(rootURL: root)
        for theme in WorkspaceTheme.all {
            let script = try resources.buildThemeScript(id: theme.id, fontSize: 14)
            XCTAssertFalse(script.contains("__THEME_JSON__"))
            XCTAssertTrue(script.contains("codex-zh-launcher-theme"))
        }
        XCTAssertThrowsError(try resources.buildThemeScript(id: "bad", fontSize: 14))
        XCTAssertThrowsError(try resources.buildThemeScript(id: "violet", fontSize: 19))
    }
}
