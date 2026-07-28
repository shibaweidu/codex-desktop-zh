import Foundation

final class AppLogger {
    static let shared = AppLogger()

    let directoryURL: URL
    let fileURL: URL
    private let queue = DispatchQueue(label: "CodexZhLauncher.AppLogger")

    init(fileManager: FileManager = .default) {
        let logs = fileManager.urls(for: .libraryDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Logs", isDirectory: true)
            .appendingPathComponent("CodexZhLauncher", isDirectory: true)
        directoryURL = logs
        fileURL = logs.appendingPathComponent("launcher.log")
    }

    func write(_ message: String) {
        let targetDirectory = directoryURL
        let targetFile = fileURL
        queue.async {
            do {
                try FileManager.default.createDirectory(at: targetDirectory, withIntermediateDirectories: true)
                let formatter = DateFormatter()
                formatter.locale = Locale(identifier: "en_US_POSIX")
                formatter.dateFormat = "yyyy-MM-dd HH:mm:ss.SSS"
                let data = "\(formatter.string(from: Date()))  \(message)\n".data(using: .utf8)!
                if FileManager.default.fileExists(atPath: targetFile.path) {
                    let handle = try FileHandle(forWritingTo: targetFile)
                    try handle.seekToEnd()
                    try handle.write(contentsOf: data)
                    try handle.close()
                } else {
                    try data.write(to: targetFile, options: .atomic)
                }
            } catch {
                // Logging must never interrupt localization or shutdown.
            }
        }
    }
}
