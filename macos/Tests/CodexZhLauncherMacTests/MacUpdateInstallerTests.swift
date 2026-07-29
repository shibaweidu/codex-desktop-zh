import XCTest
@testable import CodexZhLauncherMac

final class MacUpdateInstallerTests: XCTestCase {
    func testCanonicalStagingURLResolvesParentDirectoryLink() throws {
        let container = FileManager.default.temporaryDirectory
            .appendingPathComponent("CodexZhLauncher-path-test-\(UUID().uuidString)", isDirectory: true)
        let realDirectory = container.appendingPathComponent("real", isDirectory: true)
        let linkedDirectory = container.appendingPathComponent("linked", isDirectory: true)
        try FileManager.default.createDirectory(at: realDirectory, withIntermediateDirectories: true)
        try FileManager.default.createSymbolicLink(at: linkedDirectory, withDestinationURL: realDirectory)
        defer { try? FileManager.default.removeItem(at: container) }

        let staged = linkedDirectory.appendingPathComponent("Codex 汉化增强工具.app", isDirectory: true)
        try FileManager.default.createDirectory(at: staged, withIntermediateDirectories: true)
        let expected = realDirectory.appendingPathComponent("Codex 汉化增强工具.app", isDirectory: true)
            .standardizedFileURL.resolvingSymlinksInPath()

        XCTAssertNotEqual(staged.standardizedFileURL, expected)
        XCTAssertEqual(MacUpdateInstaller.canonicalStagingURL(staged), expected)
    }

    func testChecksumParserSelectsExactAsset() throws {
        let expected = String(repeating: "a", count: 64)
        let other = String(repeating: "b", count: 64)
        let contents = """
        \(other)  another.zip
        \(expected)  Codex-Zh-Launcher-macOS-arm64.zip
        """

        XCTAssertEqual(
            try MacUpdateInstaller.parseExpectedHash(
                contents,
                assetName: "Codex-Zh-Launcher-macOS-arm64.zip"
            ),
            expected
        )
    }

    func testChecksumParserRejectsMissingAsset() {
        XCTAssertThrowsError(try MacUpdateInstaller.parseExpectedHash(
            String(repeating: "a", count: 64) + "  another.zip",
            assetName: "missing.zip"
        ))
    }
}
