#if os(macOS)
import Foundation
import Darwin
import XCTest
@testable import CodexZhLauncherMac

final class DarwinProcessServiceTests: XCTestCase {
    func testForeignSameNameProcessIsIgnored() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        let bundle = root.appendingPathComponent("Codex.app", isDirectory: true)
        let bundleExecutable = bundle.appendingPathComponent("Contents/MacOS/Codex")
        let foreignExecutable = root.appendingPathComponent("foreign/Codex")
        try FileManager.default.createDirectory(at: bundleExecutable.deletingLastPathComponent(), withIntermediateDirectories: true)
        try FileManager.default.createDirectory(at: foreignExecutable.deletingLastPathComponent(), withIntermediateDirectories: true)
        try FileManager.default.copyItem(at: URL(fileURLWithPath: "/bin/sleep"), to: bundleExecutable)
        try FileManager.default.copyItem(at: URL(fileURLWithPath: "/bin/sleep"), to: foreignExecutable)
        let info: [String: Any] = [
            "CFBundleExecutable": "Codex",
            "CFBundleIdentifier": "com.example.codex",
            "CFBundleShortVersionString": "1"
        ]
        let plist = try PropertyListSerialization.data(fromPropertyList: info, format: .xml, options: 0)
        try plist.write(to: bundle.appendingPathComponent("Contents/Info.plist"))
        let install = try CodexDiscovery().buildInstall(bundleURL: bundle, kind: "测试")

        let inside = Process()
        inside.executableURL = bundleExecutable
        inside.arguments = ["30"]
        let outside = Process()
        outside.executableURL = foreignExecutable
        outside.arguments = ["30"]
        try inside.run()
        try outside.run()
        defer {
            if inside.isRunning { inside.terminate() }
            if outside.isRunning { outside.terminate() }
            inside.waitUntilExit()
            outside.waitUntilExit()
            try? FileManager.default.removeItem(at: root)
        }

        usleep(200_000)
        let service = DarwinProcessService()
        let scan = service.scan(install: install)
        XCTAssertTrue(scan.verified.contains { $0.pid == inside.processIdentifier })
        XCTAssertFalse(scan.verified.contains { $0.pid == outside.processIdentifier })

        let target = try XCTUnwrap(scan.verified.first { $0.pid == inside.processIdentifier })
        try service.forceTerminate(snapshot: target, install: install)
        inside.waitUntilExit()
        XCTAssertTrue(outside.isRunning)
    }
}
#endif
