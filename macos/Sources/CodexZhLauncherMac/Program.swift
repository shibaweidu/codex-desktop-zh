import AppKit
import Darwin
import Foundation
import SwiftUI

@main
enum Program {
    @MainActor
    static func main() {
        let arguments = Array(CommandLine.arguments.dropFirst())
        if let command = arguments.first {
            Task {
                let exitCode = await runCLI(command: command)
                fflush(stdout)
                fflush(stderr)
                Darwin.exit(exitCode)
            }
            dispatchMain()
        }

        let application = NSApplication.shared
        let delegate = ApplicationDelegate()
        application.delegate = delegate
        application.setActivationPolicy(.regular)
        application.run()
    }

    private static func runCLI(command: String) async -> Int32 {
        do {
            let resources = try SharedResources()
            if command == "--self-test" {
                print(try resources.selfTest())
                return 0
            }

            let discovery = CodexDiscovery()
            let processService = DarwinProcessService()
            let install = discovery.detect()
            if command == "--diagnostics" {
                print("Codex Localization Enhancer 0.5.0")
                print("os=\(ProcessInfo.processInfo.operatingSystemVersionString)")
                #if arch(arm64)
                print("architecture=arm64")
                #elseif arch(x86_64)
                print("architecture=x64")
                #else
                print("architecture=unknown")
                #endif
                guard let install else {
                    print("codex=not-found")
                    print("log=\(AppLogger.shared.fileURL.path)")
                    return 2
                }
                print("codex=found")
                print("kind=\(install.kind)")
                print("version=\(install.version)")
                print("bundle_id=\(install.bundleIdentifier)")
                print("location=\(install.bundleURL.path)")
                print("running_processes=\(processService.scan(install: install).totalPotentialCount)")
                print("log=\(AppLogger.shared.fileURL.path)")
                return 0
            }

            guard command == "--launch-zh" || command == "--launch-en" else {
                throw CLIError.unknownArgument
            }
            guard let install else { throw CLIError.installNotFound }
            let runtime = LocalizationRuntime(
                processService: processService,
                launcher: AppLauncher(processService: processService),
                resources: resources
            )
            let locale = command == "--launch-zh" ? "zh-CN" : "en-US"
            let report = try await runtime.launch(install: install, locale: locale)
            print(report.message)
            print("pid=\(report.processID); renderer_port=\(report.rendererPort); inspector_port=\(report.inspectorPort)")
            print("locale=\(report.localeApplied); menu=\(report.menuApplied)")
            return report.complete ? 0 : 2
        } catch {
            fputs("error: \(error.localizedDescription)\n", stderr)
            AppLogger.shared.write("command.failed \(error)")
            return 1
        }
    }

    enum CLIError: LocalizedError {
        case unknownArgument
        case installNotFound

        var errorDescription: String? {
            switch self {
            case .unknownArgument: return "未知参数。可用参数：--diagnostics、--self-test、--launch-zh、--launch-en"
            case .installNotFound: return "未检测到 Codex.app 或 ChatGPT.app。"
            }
        }
    }
}

@MainActor
final class ApplicationDelegate: NSObject, NSApplicationDelegate {
    private var window: NSWindow?
    private let model = LauncherModel()

    func applicationDidFinishLaunching(_ notification: Notification) {
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 820, height: 620),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = "Codex 汉化增强工具"
        window.minSize = NSSize(width: 720, height: 560)
        window.center()
        window.appearance = NSAppearance(named: .darkAqua)
        window.contentViewController = NSHostingController(rootView: LauncherView(model: model))
        window.makeKeyAndOrderFront(nil)
        self.window = window
        buildMenus()
        NSApplication.shared.activate(ignoringOtherApps: true)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    private func buildMenus() {
        let main = NSMenu()
        let appItem = NSMenuItem()
        main.addItem(appItem)
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "关于 Codex 汉化增强工具", action: #selector(NSApplication.orderFrontStandardAboutPanel(_:)), keyEquivalent: "")
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "隐藏 Codex 汉化增强工具", action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
        let hideOthers = appMenu.addItem(withTitle: "隐藏其他应用", action: #selector(NSApplication.hideOtherApplications(_:)), keyEquivalent: "h")
        hideOthers.keyEquivalentModifierMask = [.command, .option]
        appMenu.addItem(withTitle: "显示全部", action: #selector(NSApplication.unhideAllApplications(_:)), keyEquivalent: "")
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "退出 Codex 汉化增强工具", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu

        let windowItem = NSMenuItem()
        windowItem.title = "窗口"
        main.addItem(windowItem)
        let windowMenu = NSMenu(title: "窗口")
        windowMenu.addItem(withTitle: "最小化", action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
        windowMenu.addItem(withTitle: "缩放", action: #selector(NSWindow.performZoom(_:)), keyEquivalent: "")
        windowMenu.addItem(.separator())
        windowMenu.addItem(withTitle: "前置全部窗口", action: #selector(NSApplication.arrangeInFront(_:)), keyEquivalent: "")
        windowItem.submenu = windowMenu
        NSApplication.shared.windowsMenu = windowMenu
        NSApplication.shared.mainMenu = main
    }
}
