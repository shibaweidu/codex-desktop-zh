import Foundation

final class DevToolsClient: DevToolsServing {
    private let session: URLSession
    private let sleeper: Sleeping

    init(session: URLSession = .shared, sleeper: Sleeping = SystemSleeper()) {
        self.session = session
        self.sleeper = sleeper
    }

    func waitForTarget(port: UInt16, preferredType: String, timeout: TimeInterval) async throws -> DevToolsTarget {
        let deadline = Date().addingTimeInterval(timeout)
        var lastError: Error?
        while Date() < deadline {
            do {
                let targets = try await readTargets(port: port)
                let usable = targets.filter { !($0.webSocketDebuggerUrl ?? "").isEmpty }
                if let preferred = usable.first(where: { $0.type.caseInsensitiveCompare(preferredType) == .orderedSame }) {
                    return preferred
                }
                if preferredType.caseInsensitiveCompare("node") != .orderedSame, let fallback = usable.first {
                    return fallback
                }
            } catch {
                lastError = error
            }
            await sleeper.sleep(seconds: 0.3)
        }
        let suffix = lastError.map { " 最后错误：\($0.localizedDescription)" } ?? ""
        throw ClientError.targetTimeout(port, suffix)
    }

    func evaluate(webSocketURL: String, expression: String, awaitPromise: Bool) async throws -> String? {
        guard let url = URL(string: webSocketURL) else { throw ClientError.invalidWebSocketURL }
        let socket = session.webSocketTask(with: url)
        socket.resume()
        defer { socket.cancel(with: .normalClosure, reason: nil) }

        let request: [String: Any] = [
            "id": 1,
            "method": "Runtime.evaluate",
            "params": [
                "expression": expression,
                "awaitPromise": awaitPromise,
                "returnByValue": true,
                "userGesture": true
            ]
        ]
        let data = try JSONSerialization.data(withJSONObject: request)
        try await socket.send(.data(data))

        return try await withThrowingTaskGroup(of: String?.self) { group in
            group.addTask {
                while true {
                    let message = try await socket.receive()
                    let payload: Data
                    switch message {
                    case .data(let data): payload = data
                    case .string(let string): payload = Data(string.utf8)
                    @unknown default: continue
                    }
                    guard let object = try JSONSerialization.jsonObject(with: payload) as? [String: Any],
                          (object["id"] as? NSNumber)?.intValue == 1 else { continue }
                    if let error = object["error"] {
                        throw ClientError.protocolError(String(describing: error))
                    }
                    guard let result = object["result"] as? [String: Any] else { return nil }
                    if let exception = result["exceptionDetails"] {
                        throw ClientError.evaluationFailed(String(describing: exception))
                    }
                    let inner = result["result"] as? [String: Any]
                    return inner?["value"].map { String(describing: $0) }
                }
            }
            group.addTask {
                try await Task.sleep(nanoseconds: 12_000_000_000)
                throw ClientError.evaluationTimeout
            }
            let result = try await group.next() ?? nil
            group.cancelAll()
            return result
        }
    }

    private func readTargets(port: UInt16) async throws -> [DevToolsTarget] {
        guard let url = URL(string: "http://127.0.0.1:\(port)/json/list") else {
            throw ClientError.invalidTargetURL
        }
        var request = URLRequest(url: url)
        request.timeoutInterval = 2
        let (data, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
            throw ClientError.invalidHTTPResponse
        }
        return try JSONDecoder().decode([DevToolsTarget].self, from: data)
    }

    enum ClientError: LocalizedError {
        case invalidTargetURL
        case invalidWebSocketURL
        case invalidHTTPResponse
        case targetTimeout(UInt16, String)
        case evaluationTimeout
        case protocolError(String)
        case evaluationFailed(String)

        var errorDescription: String? {
            switch self {
            case .invalidTargetURL: return "本地 DevTools 地址无效。"
            case .invalidWebSocketURL: return "DevTools WebSocket 地址无效。"
            case .invalidHTTPResponse: return "本地 DevTools 返回了无效响应。"
            case .targetTimeout(let port, let suffix): return "等待本地调试目标超时（127.0.0.1:\(port)）。\(suffix)"
            case .evaluationTimeout: return "汉化脚本执行超时。"
            case .protocolError(let detail): return "DevTools 协议错误：\(detail)"
            case .evaluationFailed(let detail): return "汉化脚本执行失败：\(detail)"
            }
        }
    }
}
