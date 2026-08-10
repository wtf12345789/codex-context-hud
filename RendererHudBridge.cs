using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CodexContextHUD
{
    internal static class RendererHudScript
    {
        internal const string ResourceName = "CodexContextHUD.RendererHudScript.js";

        internal static string Load()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null) throw new InvalidOperationException("缺少渲染器 HUD 脚本资源。");
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                    return reader.ReadToEnd();
            }
        }
    }

    internal sealed class RendererHudProtocolState
    {
        internal string ThreadId;
        internal int Compressions;
        internal double ContextPercent = -1;
        internal double RemainingQuotaPercent = -1;

        private readonly HashSet<string> compactionKeys = new HashSet<string>(StringComparer.Ordinal);

        internal bool ApplyJson(string json)
        {
            JavaScriptSerializer serializer = NewSerializer();
            object root = serializer.DeserializeObject(json);
            return Visit(root, null);
        }

        private bool Visit(object value, string parentKey)
        {
            bool changed = false;
            IDictionary<string, object> map = value as IDictionary<string, object>;
            if (map != null)
            {
                object methodValue;
                object paramsValue;
                if (TryGet(map, out methodValue, "method") && methodValue != null &&
                    TryGet(map, out paramsValue, "params"))
                    changed |= ApplyNotification(Convert.ToString(methodValue), paramsValue);

                changed |= ApplyTokenUsage(map);
                changed |= ApplyRateLimit(map);
                changed |= ApplyCompaction(map, parentKey);

                foreach (KeyValuePair<string, object> pair in map)
                    changed |= Visit(pair.Value, pair.Key);
                return changed;
            }

            IEnumerable list = value as IEnumerable;
            if (list != null && !(value is string))
            {
                foreach (object item in list) changed |= Visit(item, parentKey);
            }
            return changed;
        }

        private bool ApplyNotification(string method, object parameters)
        {
            IDictionary<string, object> map = parameters as IDictionary<string, object>;
            if (map == null) return false;

            bool changed = false;
            if (method == "thread/tokenUsage/updated")
            {
                changed |= ApplyThreadId(map);
                object usage;
                if (TryGet(map, out usage, "tokenUsage", "token_usage"))
                {
                    IDictionary<string, object> usageMap = usage as IDictionary<string, object>;
                    if (usageMap != null) changed |= ApplyTokenUsage(usageMap);
                }
            }
            else if (method == "account/rateLimits/updated")
            {
                changed |= ApplyRateLimit(map);
            }
            else if (method == "thread/compacted")
            {
                changed |= RegisterCompaction(map, "notification");
            }
            return changed;
        }

        private bool ApplyThreadId(IDictionary<string, object> map)
        {
            object value;
            if (!TryGet(map, out value, "threadId", "thread_id") || value == null) return false;
            string next = Convert.ToString(value);
            if (string.IsNullOrWhiteSpace(next) || next == ThreadId) return false;
            ThreadId = next;
            return true;
        }

        private bool ApplyTokenUsage(IDictionary<string, object> map)
        {
            double used;
            double window;
            if (!TryNumber(map, out window, "modelContextWindow", "model_context_window", "contextWindow", "context_window") || window <= 0)
                return false;

            if (!TryNumber(map, out used, "totalTokens", "total_tokens"))
            {
                object last;
                IDictionary<string, object> lastMap;
                if (!TryGet(map, out last, "lastTokenUsage", "last_token_usage") ||
                    (lastMap = last as IDictionary<string, object>) == null ||
                    !TryNumber(lastMap, out used, "totalTokens", "total_tokens")) return false;
            }

            double next = Clamp(used * 100.0 / window, 0, 100);
            if (Math.Abs(next - ContextPercent) < .0001) return false;
            ContextPercent = next;
            return true;
        }

        private bool ApplyRateLimit(IDictionary<string, object> map)
        {
            double used;
            if (TryNumber(map, out used, "usedPercent", "used_percent"))
                return SetRemaining(100 - used);

            object primary;
            IDictionary<string, object> primaryMap;
            if (TryGet(map, out primary, "primary", "primaryLimit", "primary_limit") &&
                (primaryMap = primary as IDictionary<string, object>) != null &&
                TryNumber(primaryMap, out used, "usedPercent", "used_percent"))
                return SetRemaining(100 - used);

            object limits;
            if (TryGet(map, out limits, "rateLimits", "rate_limits", "limits"))
            {
                IDictionary<string, object> limitsMap = limits as IDictionary<string, object>;
                if (limitsMap != null) return ApplyRateLimit(limitsMap);
            }
            return false;
        }

        private bool SetRemaining(double value)
        {
            double next = Clamp(value, 0, 100);
            if (Math.Abs(next - RemainingQuotaPercent) < .0001) return false;
            RemainingQuotaPercent = next;
            return true;
        }

        private bool ApplyCompaction(IDictionary<string, object> map, string parentKey)
        {
            object typeValue;
            if (!TryGet(map, out typeValue, "type") || typeValue == null) return false;
            string type = Convert.ToString(typeValue);
            if (type != "context-compaction" && type != "contextCompaction") return false;

            object completed;
            if (TryGet(map, out completed, "completed") && completed is bool && !(bool)completed)
                return false;
            return RegisterCompaction(map, parentKey ?? "item");
        }

        private bool RegisterCompaction(IDictionary<string, object> map, string source)
        {
            object id;
            string key;
            if (TryGet(map, out id, "id", "itemId", "item_id") && id != null)
                key = Convert.ToString(id);
            else
            {
                object thread;
                object turn;
                TryGet(map, out thread, "threadId", "thread_id");
                TryGet(map, out turn, "turnId", "turn_id");
                key = source + ":" + Convert.ToString(thread) + ":" + Convert.ToString(turn);
            }
            if (!compactionKeys.Add(key)) return false;
            Compressions++;
            return true;
        }

        private static JavaScriptSerializer NewSerializer()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 16 * 1024 * 1024;
            serializer.RecursionLimit = 256;
            return serializer;
        }

        private static bool TryGet(IDictionary<string, object> map, out object value, params string[] names)
        {
            foreach (string name in names)
                if (map.TryGetValue(name, out value)) return true;
            value = null;
            return false;
        }

        private static bool TryNumber(IDictionary<string, object> map, out double value, params string[] names)
        {
            object raw;
            if (TryGet(map, out raw, names) && raw != null &&
                double.TryParse(Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value)) return true;
            value = 0;
            return false;
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }

    internal sealed class CdpTarget
    {
        internal string Title;
        internal string Url;
        internal string WebSocketDebuggerUrl;
    }

    internal static class CdpTargetDiscovery
    {
        internal static CdpTarget Find(int port)
        {
            string endpoint = "http://127.0.0.1:" + port + "/json/list";
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.Timeout = 2000;
            request.ReadWriteTimeout = 2000;
            request.Proxy = null;
            string json;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                json = reader.ReadToEnd();

            object[] targets = new JavaScriptSerializer().DeserializeObject(json) as object[];
            if (targets == null) return null;
            CdpTarget fallback = null;
            foreach (object raw in targets)
            {
                IDictionary<string, object> map = raw as IDictionary<string, object>;
                if (map == null || Get(map, "type") != "page") continue;
                CdpTarget target = new CdpTarget {
                    Title = Get(map, "title"),
                    Url = Get(map, "url"),
                    WebSocketDebuggerUrl = Get(map, "webSocketDebuggerUrl")
                };
                if (!IsAllowedTarget(target) || !IsLoopbackWebSocket(target.WebSocketDebuggerUrl)) continue;
                if (string.Equals(target.Url, "app://-/index.html", StringComparison.OrdinalIgnoreCase))
                    return target;
                if (fallback == null) fallback = target;
            }
            return fallback;
        }

        internal static bool IsAllowedTarget(CdpTarget target)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.Url) ||
                string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl)) return false;
            Uri uri;
            if (!Uri.TryCreate(target.Url, UriKind.Absolute, out uri)) return false;
            if (uri.Scheme == "app")
                return string.Equals(uri.AbsolutePath, "/index.html", StringComparison.OrdinalIgnoreCase) &&
                    uri.Query.IndexOf("avatar-overlay", StringComparison.OrdinalIgnoreCase) < 0;
            if (uri.Scheme != "file") return false;
            string path = uri.AbsolutePath.Replace('\\', '/');
            return path.IndexOf("/OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (path.EndsWith("/webview/index.html", StringComparison.OrdinalIgnoreCase) ||
                 path.EndsWith("/app/index.html", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsLoopbackWebSocket(string address)
        {
            Uri uri;
            return Uri.TryCreate(address, UriKind.Absolute, out uri) &&
                   (uri.Scheme == "ws" || uri.Scheme == "wss") && uri.IsLoopback;
        }

        private static string Get(IDictionary<string, object> map, string key)
        {
            object value;
            return map.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : null;
        }
    }

    internal sealed class CdpConnection : IDisposable
    {
        private readonly ClientWebSocket socket = new ClientWebSocket();
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private int nextId;

        internal void ConnectAndInject(CdpTarget target, string script)
        {
            socket.ConnectAsync(new Uri(target.WebSocketDebuggerUrl), CancellationToken.None).GetAwaiter().GetResult();
            SendCommand("Runtime.enable", null);
            IDictionary<string, object> reply = SendCommand("Runtime.evaluate", new Dictionary<string, object> {
                { "expression", script }, { "awaitPromise", false }, { "returnByValue", true }
            });
            object result;
            IDictionary<string, object> resultMap;
            if (reply.TryGetValue("result", out result) &&
                (resultMap = result as IDictionary<string, object>) != null &&
                resultMap.ContainsKey("exceptionDetails"))
                throw new InvalidOperationException("渲染器脚本执行失败：" +
                    serializer.Serialize(resultMap["exceptionDetails"]));
        }

        internal void WaitUntilClosed()
        {
            while (socket.State == WebSocketState.Open)
            {
                try { ReceiveText(); }
                catch { return; }
            }
        }

        private IDictionary<string, object> SendCommand(string method, object parameters)
        {
            int id = Interlocked.Increment(ref nextId);
            Dictionary<string, object> command = new Dictionary<string, object> {
                { "id", id }, { "method", method }
            };
            if (parameters != null) command["params"] = parameters;
            byte[] data = Encoding.UTF8.GetBytes(serializer.Serialize(command));
            socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true,
                CancellationToken.None).GetAwaiter().GetResult();

            while (true)
            {
                string message = ReceiveText();
                IDictionary<string, object> reply = serializer.DeserializeObject(message) as IDictionary<string, object>;
                object replyId;
                if (reply != null && reply.TryGetValue("id", out replyId) && Convert.ToInt32(replyId) == id)
                {
                    if (reply.ContainsKey("error"))
                        throw new InvalidOperationException("CDP 命令失败：" + serializer.Serialize(reply["error"]));
                    return reply;
                }
            }
        }

        private string ReceiveText()
        {
            byte[] buffer = new byte[32 * 1024];
            using (MemoryStream stream = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None)
                        .GetAwaiter().GetResult();
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new InvalidOperationException("CDP 连接已关闭。");
                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public void Dispose()
        {
            socket.Dispose();
        }
    }

    internal static class RendererHudHost
    {
        internal const int RetryDelayMilliseconds = 750;

        internal static int Run(string[] args)
        {
            int port;
            if (args.Length < 2 || !int.TryParse(args[1], out port) || port < 1024 || port > 65535)
                return 2;
            bool created;
            using (Mutex mutex = new Mutex(true,
                "Local\\CodexContextHUD.Renderer." + port, out created))
            {
                if (!created) return 0;
                string script = RendererHudScript.Load();
                while (true)
                {
                    try
                    {
                        CdpTarget target = CdpTargetDiscovery.Find(port);
                        if (target != null)
                        {
                            using (CdpConnection connection = new CdpConnection())
                            {
                                connection.ConnectAndInject(target, script);
                                connection.WaitUntilClosed();
                            }
                        }
                    }
                    catch { }
                    Thread.Sleep(RetryDelayMilliseconds);
                }
            }
        }
    }

    internal static class RendererHudSelfTest
    {
        internal static int Run(string outputPath)
        {
            bool ok = false;
            string result;
            try
            {
                string script = RendererHudScript.Load();
                RendererHudProtocolState state = new RendererHudProtocolState();
                state.ApplyJson("{\"method\":\"thread/tokenUsage/updated\",\"params\":{\"threadId\":\"t1\",\"tokenUsage\":{\"totalTokens\":64000,\"modelContextWindow\":128000}}}");
                state.ApplyJson("{\"result\":{\"turns\":[{\"items\":[{\"id\":\"c1\",\"type\":\"context-compaction\",\"completed\":true},{\"id\":\"c1\",\"type\":\"context-compaction\",\"completed\":true},{\"id\":\"c2\",\"type\":\"contextCompaction\",\"completed\":true}]}]}}");
                state.ApplyJson("{\"method\":\"account/rateLimits/updated\",\"params\":{\"rateLimits\":{\"primary\":{\"used_percent\":37}}}}");

                bool scriptOk = script.IndexOf("codex-context-hud.renderer.v1", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("attachShadow", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("account/rateLimits/updated", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("stableIds", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("applyCompactionSnapshot", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("composerConversationId", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("client-new-thread:", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("item/completed", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("Math.max(state.compressions, snapshot.count)", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("tooltipShowTimer", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("animateCompressionBars", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("animateQuotaFill", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("scheduleSessionMotion", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("state.compressions >= 10 ? length", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("#AF8CE0", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("width=\"2.6\"", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("'.62'", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("width=\"22\"", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("clamp(state.quotaPercent) * 22 / 100", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("data-codex-intelligence-trigger", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("dispose", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("上下文", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("class=\"dot\"", StringComparison.Ordinal) < 0 &&
                    script.IndexOf("session-cache", StringComparison.OrdinalIgnoreCase) < 0 &&
                    script.IndexOf(".jsonl", StringComparison.OrdinalIgnoreCase) < 0 &&
                    script.IndexOf("fetch(", StringComparison.OrdinalIgnoreCase) < 0 &&
                    script.IndexOf("localStorage", StringComparison.OrdinalIgnoreCase) < 0 &&
                    script.IndexOf(".title =", StringComparison.Ordinal) < 0 &&
                    script.IndexOf("cookie", StringComparison.OrdinalIgnoreCase) < 0;
                bool targetOk = CdpTargetDiscovery.IsAllowedTarget(new CdpTarget {
                        Url = "app://-/index.html",
                        WebSocketDebuggerUrl = "ws://127.0.0.1:9222/devtools/page/0"
                    }) && !CdpTargetDiscovery.IsAllowedTarget(new CdpTarget {
                        Url = "app://-/index.html?initialRoute=%2Favatar-overlay",
                        WebSocketDebuggerUrl = "ws://127.0.0.1:9222/devtools/page/overlay"
                    }) && CdpTargetDiscovery.IsAllowedTarget(new CdpTarget {
                        Url = "file:///C:/Program%20Files/WindowsApps/OpenAI.Codex_1/webview/index.html",
                        WebSocketDebuggerUrl = "ws://127.0.0.1:9222/devtools/page/1"
                    }) && !CdpTargetDiscovery.IsAllowedTarget(new CdpTarget {
                        Url = "file:///C:/Program%20Files/WindowsApps/OpenAI.Codex_1/app/other.html",
                        WebSocketDebuggerUrl = "ws://127.0.0.1:9222/devtools/page/other"
                    }) && !CdpTargetDiscovery.IsAllowedTarget(new CdpTarget {
                        Url = "https://example.com/", WebSocketDebuggerUrl = "ws://127.0.0.1:9222/devtools/page/2"
                    });
                bool hostOk = RendererHudHost.RetryDelayMilliseconds >= 500;
                ok = scriptOk && targetOk && hostOk && state.ThreadId == "t1" &&
                    Math.Abs(state.ContextPercent - 50) < .001 && state.Compressions == 2 &&
                    Math.Abs(state.RemainingQuotaPercent - 63) < .001;
                result = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "ok={0};script={1};target={2};thread={3};compressions={4};context={5:0.0};quota={6:0.0}",
                    ok, scriptOk, targetOk, state.ThreadId, state.Compressions,
                    state.ContextPercent, state.RemainingQuotaPercent);
            }
            catch (Exception error)
            {
                result = "ok=False;error=" + error.GetType().Name + ":" + error.Message;
            }
            if (!string.IsNullOrWhiteSpace(outputPath)) File.WriteAllText(outputPath, result, Encoding.UTF8);
            return ok ? 0 : 1;
        }
    }
}
