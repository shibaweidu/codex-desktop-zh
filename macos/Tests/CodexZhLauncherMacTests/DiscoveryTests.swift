import Foundation
import XCTest
@testable import CodexZhLauncherMac

final class DiscoveryTests: XCTestCase {
    private var root: URL!

    override func setUpWithError() throws {
        root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: root)
    }

    func testBuildInstallReadsInfoPlist() throws {
        let bundle = try makeBundle(name: "Codex", executable: "Codex")
        let defaults = UserDefaults(suiteName: UUID().uuidString)!
        let discovery = CodexDiscovery(defaults: defaults)
        let install = try discovery.buildInstall(bundleURL: bundle, kind: "测试")

        XCTAssertEqual(install.version, "26.7.1")
        XCTAssertEqual(install.bundleIdentifier, "com.example.codex")
        XCTAssertEqual(install.executableURL.lastPathComponent, "Codex")
    }

    func testRejectsSymlinkedExecutableOutsideBundle() throws {
        let bundle = try makeBundle(name: "Codex", executable: "Codex", createExecutable: false)
        let outside = root.appendingPathComponent("outside")
        FileManager.default.createFile(atPath: outside.path, contents: Data("#!/bin/sh\n".utf8))
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: outside.path)
        let executable = bundle.appendingPathComponent("Contents/MacOS/Codex")
        try FileManager.default.createSymbolicLink(at: executable, withDestinationURL: outside)

        XCTAssertThrowsError(try CodexDiscovery().buildInstall(bundleURL: bundle, kind: "测试"))
    }

    func testPathBoundaryDoesNotAcceptSiblingPrefix() {
        XCTAssertTrue(CodexDiscovery.isPath("/Applications/Codex.app/Contents/MacOS/Codex", within: "/Applications/Codex.app"))
        XCTAssertFalse(CodexDiscovery.isPath("/Applications/Codex.app.old/Contents/MacOS/Codex", within: "/Applications/Codex.app"))
    }

    func testManualSelectionPersistsValidatedPath() throws {
        let bundle = try makeBundle(name: "ChatGPT", executable: "ChatGPT")
        let suite = "CodexZhLauncherTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let discovery = CodexDiscovery(defaults: defaults)

        let install = try discovery.useBundle(bundle)
        XCTAssertEqual(defaults.string(forKey: CodexDiscovery.savedPathKey), install.bundleURL.path)
        XCTAssertEqual(discovery.detect()?.bundleURL, install.bundleURL)
    }

    @discardableResult
    private func makeBundle(name: String, executable: String, createExecutable: Bool = true) throws -> URL {
        let bundle = root.appendingPathComponent("\(name).app", isDirectory: true)
        let macOS = bundle.appendingPathComponent("Contents/MacOS", isDirectory: true)
        try FileManager.default.createDirectory(at: macOS, withIntermediateDirectories: true)
        let plist: [String: Any] = [
            "CFBundleExecutable": executable,
            "CFBundleIdentifier": "com.example.codex",
            "CFBundleDisplayName": name,
            "CFBundleShortVersionString": "26.7.1"
        ]
        let data = try PropertyListSerialization.data(fromPropertyList: plist, format: .xml, options: 0)
        try data.write(to: bundle.appendingPathComponent("Contents/Info.plist"))
        if createExecutable {
            let target = macOS.appendingPathComponent(executable)
            FileManager.default.createFile(atPath: target.path, contents: Data("#!/bin/sh\n".utf8))
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: target.path)
        }
        return bundle
    }
}
