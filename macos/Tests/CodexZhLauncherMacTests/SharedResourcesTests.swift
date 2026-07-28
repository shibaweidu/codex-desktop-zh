import Foundation
import XCTest
@testable import CodexZhLauncherMac

final class SharedResourcesTests: XCTestCase {
    func testSharedResourcesResolveAllPlaceholders() throws {
        let resources = try SharedResources(rootURL: sharedDirectory())
        let locale = try resources.buildLocaleScript(locale: "zh-CN")
        let menu = try resources.buildMenuScript(platform: "macos")

        XCTAssertTrue(locale.contains("localeOverride"))
        XCTAssertFalse(locale.contains("__LOCALE_JSON__"))
        XCTAssertTrue(menu.contains("隐藏其他应用"))
        XCTAssertTrue(menu.contains("前置全部窗口"))
        XCTAssertFalse(menu.contains("__TRANSLATIONS_JSON__"))
        XCTAssertNoThrow(try resources.selfTest())
    }

    func testPlatformTranslationsOverrideCommon() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        try "__LOCALE_JSON__".write(to: root.appendingPathComponent("locale-script.js"), atomically: true, encoding: .utf8)
        try "var translations = __TRANSLATIONS_JSON__; var platform = __PLATFORM_JSON__;".write(
            to: root.appendingPathComponent("menu-script.js"), atomically: true, encoding: .utf8
        )
        let json = "{\"common\":{\"Quit\":\"退出\"},\"windows\":{},\"macos\":{\"Quit\":\"退出应用\"}}"
        try json.write(to: root.appendingPathComponent("menu-translations.json"), atomically: true, encoding: .utf8)

        let output = try SharedResources(rootURL: root).buildMenuScript(platform: "macos")
        XCTAssertTrue(output.contains("退出应用"))
        XCTAssertFalse(output.contains("\"Quit\":\"退出\""))
    }

    private func sharedDirectory() -> URL {
        URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .appendingPathComponent("shared", isDirectory: true)
    }
}
