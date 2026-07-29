import XCTest
@testable import CodexZhLauncherMac

final class MacUpdateInstallerTests: XCTestCase {
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
