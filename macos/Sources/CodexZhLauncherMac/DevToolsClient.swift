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

    func listTargets(port: UInt16) async throws -> [DevToolsTarget] {
        try await readTargets(port: port)
    }

    func evaluate(webSocketURL: String, expression: String, awaitPromise: Bool) async throws -> String? {
        guard let url = URL(string: webSocketURL) else { throw ClientError.invalidWebSocketURL }
        let openDelegate = WebSocketOpenDelegate()
        let webSocketSession = URLSession(
            configuration: .ephemeral,
            delegate: openDelegate,
            delegateQueue: nil
        )
        var socketRequest = URLRequest(url: url)
        socketRequest.timeoutInterval = 12
        if let origin = Self.origin(forWebSocketURL: url) {
            socketRequest.setValue(origin, forHTTPHeaderField: "Origin")
        }
        let socket = webSocketSession.webSocketTask(with: socketRequest)
        socket.resume()
        defer {
            socket.cancel(with: .normalClosure, reason: nil)
            webSocketSession.invalidateAndCancel()
        }

        do {
            try await waitUntilOpen(socket: socket, delegate: openDelegate)
        } catch let error as ClientError {
            throw error
        } catch {
            throw ClientError.connectionFailed(error.localizedDescription)
        }

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
        do {
            try await socket.send(.string(String(decoding: data, as: UTF8.self)))
        } catch {
            throw ClientError.connectionFailed(error.localizedDescription)
        }

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
                socket.cancel(with: .goingAway, reason: nil)
                throw ClientError.evaluationTimeout
            }
            let result = try await group.next() ?? nil
            group.cancelAll()
            return result
        }
    }

    static func origin(forWebSocketURL url: URL) -> String? {
        guard let host = url.host, let port = url.port else { return nil }
        return "http://\(host):\(port)"
    }

    private func waitUntilOpen(
        socket: URLSessionWebSocketTask,
        delegate: WebSocketOpenDelegate
    ) async throws {
        try await withThrowingTaskGroup(of: Void.self) { group in
            group.addTask { try await delegate.waitUntilOpen() }
            group.addTask {
                try await Task.sleep(nanoseconds: 8_000_000_000)
                socket.cancel(with: .goingAway, reason: nil)
                throw ClientError.connectionTimeout
            }
            _ = try await group.next()
            group.cancelAll()
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
        case connectionTimeout
        case connectionFailed(String)
        case socketClosedBeforeOpen
        case evaluationTimeout
        case protocolError(String)
        case evaluationFailed(String)

        var errorDescription: String? {
            switch self {
            case .invalidTargetURL: return "本地 DevTools 地址无效。"
            case .invalidWebSocketURL: return "DevTools WebSocket 地址无效。"
            case .invalidHTTPResponse: return "本地 DevTools 返回了无效响应。"
            case .targetTimeout(let port, let suffix): return "等待本地调试目标超时（127.0.0.1:\(port)）。\(suffix)"
            case .connectionTimeout: return "连接 DevTools WebSocket 超时。"
            case .connectionFailed(let detail): return "连接 DevTools WebSocket 失败：\(detail)"
            case .socketClosedBeforeOpen: return "DevTools WebSocket 在握手完成前已关闭。"
            case .evaluationTimeout: return "汉化脚本执行超时。"
            case .protocolError(let detail): return "DevTools 协议错误：\(detail)"
            case .evaluationFailed(let detail): return "汉化脚本执行失败：\(detail)"
            }
        }
    }
}

private final class WebSocketOpenDelegate: NSObject, URLSessionWebSocketDelegate {
    private let lock = NSLock()
    private var result: Result<Void, Error>?
    private var continuation: CheckedContinuation<Void, Error>?

    func waitUntilOpen() async throws {
        try await withCheckedThrowingContinuation { continuation in
            lock.lock()
            if let result {
                lock.unlock()
                continuation.resume(with: result)
                return
            }
            self.continuation = continuation
            lock.unlock()
        }
    }

    func urlSession(
        _ session: URLSession,
        webSocketTask: URLSessionWebSocketTask,
        didOpenWithProtocol protocol: String?
    ) {
        resolve(.success(()))
    }

    func urlSession(
        _ session: URLSession,
        webSocketTask: URLSessionWebSocketTask,
        didCloseWith closeCode: URLSessionWebSocketTask.CloseCode,
        reason: Data?
    ) {
        resolve(.failure(DevToolsClient.ClientError.socketClosedBeforeOpen))
    }

    func urlSession(
        _ session: URLSession,
        task: URLSessionTask,
        didCompleteWithError error: Error?
    ) {
        if let error {
            resolve(.failure(DevToolsClient.ClientError.connectionFailed(error.localizedDescription)))
        } else {
            resolve(.failure(DevToolsClient.ClientError.socketClosedBeforeOpen))
        }
    }

    private func resolve(_ newResult: Result<Void, Error>) {
        lock.lock()
        guard result == nil else {
            lock.unlock()
            return
        }
        result = newResult
        let continuation = continuation
        self.continuation = nil
        lock.unlock()
        continuation?.resume(with: newResult)
    }
}
