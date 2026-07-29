import Foundation
import XCTest
@testable import CodexZhLauncherMac

final class SharedResourcesTests: XCTestCase {
    func testSharedResourcesResolveAllPlaceholders() throws {
        let resources = try SharedResources(rootURL: sharedDirectory())
        let bootstrap = try resources.buildI18nBootstrap()
        let locale = try resources.buildLocaleScript(locale: "zh-CN")
        let menu = try resources.buildMenuScript(platform: "macos")

        XCTAssertTrue(bootstrap.contains("72216192"))
        XCTAssertTrue(bootstrap.contains("enable_i18n"))
        XCTAssertTrue(bootstrap.contains("locale_source"))
        XCTAssertTrue(bootstrap.contains("getDynamicConfig"))
        XCTAssertTrue(locale.contains("localeOverride"))
        XCTAssertFalse(locale.contains("__LOCALE_JSON__"))
        XCTAssertTrue(menu.contains("隐藏其他应用"))
        XCTAssertTrue(menu.contains("前置全部窗口"))
        XCTAssertTrue(menu.contains("createRequire"))
        XCTAssertTrue(menu.contains("getBuiltinModule"))
        XCTAssertFalse(menu.contains("__TRANSLATIONS_JSON__"))
        XCTAssertNoThrow(try resources.selfTest())
    }

    func testPlatformTranslationsOverrideCommon() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        try "72216192 enable_i18n locale_source getDynamicConfig".write(
            to: root.appendingPathComponent("i18n-bootstrap.js"), atomically: true, encoding: .utf8
        )
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
