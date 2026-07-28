using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexZhLauncher
{
    internal sealed class DevToolsTarget
    {
        public string Id;
        public string Type;
        public string Title;
        public string Url;
        public string WebSocketDebuggerUrl;
    }

    internal static class DevToolsClient
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer
        {
            MaxJsonLength = 1024 * 1024 * 8,
            RecursionLimit = 100
        };

        public static async Task<DevToolsTarget> WaitForTargetAsync(
            int port,
            string preferredType,
            TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow.Add(timeout);
            Exception lastError = null;

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var targets = await ReadTargetsAsync(port);
                    DevToolsTarget fallback = null;
                    foreach (var target in targets)
                    {
                        if (String.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl)) continue;
                        if (fallback == null) fallback = target;
                        if (target.Type.Equals(preferredType, StringComparison.OrdinalIgnoreCase)) return target;
                    }
                    if (fallback != null && preferredType != "node") return fallback;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
                await Task.Delay(300);
            }

            var suffix = lastError == null ? String.Empty : " 最后错误：" + lastError.Message;
            throw new TimeoutException("等待本地调试目标超时（127.0.0.1:" + port + "）。" + suffix);
        }

        public static async Task<string> EvaluateAsync(string webSocketUrl, string expression, bool awaitPromise)
        {
            using (var socket = new ClientWebSocket())
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
            {
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
                await socket.ConnectAsync(new Uri(webSocketUrl), timeout.Token);

                var message = new Dictionary<string, object>();
                message["id"] = 1;
                message["method"] = "Runtime.evaluate";
                message["params"] = new Dictionary<string, object>
                {
                    { "expression", expression },
                    { "awaitPromise", awaitPromise },
                    { "returnByValue", true },
                    { "userGesture", true }
                };

                var payload = Encoding.UTF8.GetBytes(Serializer.Serialize(message));
                await socket.SendAsync(
                    new ArraySegment<byte>(payload),
                    WebSocketMessageType.Text,
                    true,
                    timeout.Token);

                while (socket.State == WebSocketState.Open)
                {
                    var responseText = await ReceiveTextAsync(socket, timeout.Token);
                    var response = Serializer.Deserialize<Dictionary<string, object>>(responseText);
                    object id;
                    if (response == null || !response.TryGetValue("id", out id) || Convert.ToInt32(id) != 1) continue;

                    object error;
                    if (response.TryGetValue("error", out error))
                        throw new InvalidOperationException("DevTools 返回错误：" + Serializer.Serialize(error));

                    return ExtractEvaluationValue(response) ?? responseText;
                }
                throw new InvalidOperationException("DevTools WebSocket 已断开。");
            }
        }

        private static async Task<List<DevToolsTarget>> ReadTargetsAsync(int port)
        {
            using (var client = new WebClient())
            {
                client.Encoding = Encoding.UTF8;
                var json = await client.DownloadStringTaskAsync(
                    new Uri("http://127.0.0.1:" + port + "/json/list"));
                var items = Serializer.Deserialize<object[]>(json);
                var targets = new List<DevToolsTarget>();
                if (items == null) return targets;

                foreach (var item in items)
                {
                    var values = item as Dictionary<string, object>;
                    if (values == null) continue;
                    targets.Add(new DevToolsTarget
                    {
                        Id = ReadString(values, "id"),
                        Type = ReadString(values, "type") ?? String.Empty,
                        Title = ReadString(values, "title") ?? String.Empty,
                        Url = ReadString(values, "url") ?? String.Empty,
                        WebSocketDebuggerUrl = ReadString(values, "webSocketDebuggerUrl")
                    });
                }
                return targets;
            }
        }

        private static async Task<string> ReceiveTextAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[16384];
            using (var output = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new InvalidOperationException("DevTools 主动关闭了连接。");
                    output.Write(buffer, 0, result.Count);
                    if (output.Length > 8 * 1024 * 1024)
                        throw new InvalidOperationException("DevTools 响应超过安全上限。");
                }
                while (!result.EndOfMessage);
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }

        private static string ExtractEvaluationValue(Dictionary<string, object> response)
        {
            object resultObject;
            if (!response.TryGetValue("result", out resultObject)) return null;
            var result = resultObject as Dictionary<string, object>;
            if (result == null) return null;

            object exception;
            if (result.TryGetValue("exceptionDetails", out exception))
                throw new InvalidOperationException("注入脚本执行失败：" + Serializer.Serialize(exception));

            object innerObject;
            if (!result.TryGetValue("result", out innerObject)) return null;
            var inner = innerObject as Dictionary<string, object>;
            if (inner == null) return null;

            object value;
            return inner.TryGetValue("value", out value) && value != null
                ? Convert.ToString(value)
                : null;
        }

        private static string ReadString(Dictionary<string, object> values, string key)
        {
            object value;
            return values.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : null;
        }
    }
}

