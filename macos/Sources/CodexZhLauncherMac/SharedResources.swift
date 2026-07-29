import Foundation

struct TranslationGroups: Decodable {
    let common: [String: String]
    let windows: [String: String]
    let macos: [String: String]
}

struct SharedResources {
    private let rootURL: URL

    init(rootURL: URL? = nil) throws {
        if let rootURL {
            self.rootURL = rootURL
            return
        }
        if let override = ProcessInfo.processInfo.environment["CODEX_ZH_SHARED_DIR"], !override.isEmpty {
            self.rootURL = URL(fileURLWithPath: override, isDirectory: true)
            return
        }
        guard let resources = Bundle.main.resourceURL else {
            throw ResourceError.missingDirectory
        }
        self.rootURL = resources.appendingPathComponent("shared", isDirectory: true)
    }

    func buildLocaleScript(locale: String) throws -> String {
        let template = try read("locale-script.js")
        let localeData = try JSONSerialization.data(withJSONObject: locale, options: [.fragmentsAllowed])
        let encoded = String(decoding: localeData, as: UTF8.self)
        return try replacing(template, placeholder: "__LOCALE_JSON__", value: encoded)
    }

    func buildI18nBootstrap() throws -> String {
        try read("i18n-bootstrap.js")
    }

    func buildMenuScript(platform: String = "macos") throws -> String {
        let groups: TranslationGroups = try decode("menu-translations.json")
        var translations = groups.common
        let platformTranslations = platform == "macos" ? groups.macos : groups.windows
        platformTranslations.forEach { translations[$0.key] = $0.value }
        let data = try JSONSerialization.data(withJSONObject: translations, options: [.sortedKeys])
        let encoded = String(decoding: data, as: UTF8.self)
        let platformData = try JSONSerialization.data(withJSONObject: platform, options: [.fragmentsAllowed])
        let encodedPlatform = String(decoding: platformData, as: UTF8.self)
        var script = try read("menu-script.js")
        script = try replacing(script, placeholder: "__TRANSLATIONS_JSON__", value: encoded)
        return try replacing(script, placeholder: "__PLATFORM_JSON__", value: encodedPlatform)
    }

    func selfTest() throws -> String {
        let locale = try buildLocaleScript(locale: "zh-CN")
        let bootstrap = try buildI18nBootstrap()
        let menu = try buildMenuScript()
        let groups: TranslationGroups = try decode("menu-translations.json")
        guard locale.contains("vscode://codex/set-setting"),
              locale.contains("JSON.stringify({ key: 'localeOverride', value: locale })"),
              !locale.contains("params: { key: 'localeOverride'"),
              bootstrap.contains("72216192"),
              bootstrap.contains("enable_i18n"),
              bootstrap.contains("locale_source"),
              bootstrap.contains("getDynamicConfig"),
              menu.contains("Menu.setApplicationMenu"),
              menu.contains("隐藏其他应用"),
              menu.contains("前置全部窗口"),
              menu.contains("platform = \"macos\""),
              !menu.localizedCaseInsensitiveContains("app.asar") else {
            throw ResourceError.selfTestFailed
        }
        return "i18n-bootstrap=ok; locale-script=ok; menu-script=ok; translations=\(groups.common.count + groups.macos.count)"
    }

    private func read(_ name: String) throws -> String {
        let url = rootURL.appendingPathComponent(name)
        guard FileManager.default.fileExists(atPath: url.path) else {
            throw ResourceError.missingFile(name)
        }
        return try String(contentsOf: url, encoding: .utf8)
    }

    private func decode<T: Decodable>(_ name: String) throws -> T {
        let data = try Data(contentsOf: rootURL.appendingPathComponent(name))
        return try JSONDecoder().decode(T.self, from: data)
    }

    private func replacing(_ source: String, placeholder: String, value: String) throws -> String {
        guard source.contains(placeholder) else { throw ResourceError.missingPlaceholder(placeholder) }
        let output = source.replacingOccurrences(of: placeholder, with: value)
        guard !output.contains(placeholder) else { throw ResourceError.unresolvedPlaceholder(placeholder) }
        return output
    }

    enum ResourceError: LocalizedError {
        case missingDirectory
        case missingFile(String)
        case missingPlaceholder(String)
        case unresolvedPlaceholder(String)
        case selfTestFailed

        var errorDescription: String? {
            switch self {
            case .missingDirectory: return "找不到共享汉化资源目录。"
            case .missingFile(let name): return "缺少共享资源：\(name)"
            case .missingPlaceholder(let value): return "共享脚本缺少占位符：\(value)"
            case .unresolvedPlaceholder(let value): return "共享脚本占位符未替换：\(value)"
            case .selfTestFailed: return "共享汉化资源自检失败。"
            }
        }
    }
}
