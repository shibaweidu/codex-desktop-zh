import Foundation
import XCTest
@testable import CodexZhLauncherMac

final class UpdateCheckerTests: XCTestCase {
    func testNewerReleaseIsAvailable() throws {
        let result = try GitHubUpdateChecker.evaluate(
            data: release(tag: "v0.6.1"),
            currentVersion: "0.6.0"
        )

        XCTAssertTrue(result.updateAvailable)
        XCTAssertEqual(result.latestVersion, "0.6.1")
        XCTAssertEqual(result.releaseURL.host, "github.com")
    }

    func testCurrentReleaseIsNotAvailable() throws {
        let result = try GitHubUpdateChecker.evaluate(
            data: release(tag: "v0.6.0"),
            currentVersion: "0.6.0"
        )

        XCTAssertFalse(result.updateAvailable)
        XCTAssertEqual(result.message, "当前已是最新版本 v0.6.0")
    }

    func testForeignReleaseURLIsRejected() {
        XCTAssertThrowsError(try GitHubUpdateChecker.evaluate(
            data: release(tag: "v9.0.0", url: "https://example.com/releases/tag/v9.0.0"),
            currentVersion: "0.6.0"
        ))
    }

    private func release(
        tag: String,
        url: String? = nil
    ) -> Data {
        let releaseURL = url ?? "https://github.com/shibaweidu/codex-desktop-zh/releases/tag/\(tag)"
        return Data("""
        {"tag_name":"\(tag)","html_url":"\(releaseURL)","draft":false,"prerelease":false}
        """.utf8)
    }
}
