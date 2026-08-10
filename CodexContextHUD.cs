using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Automation;

[assembly: AssemblyTitle("Codex Context HUD")]
[assembly: AssemblyDescription("Native-feeling context, compaction, and quota HUD for Codex Desktop")]
[assembly: AssemblyProduct("Codex Context HUD")]
[assembly: AssemblyVersion("0.2.1.0")]
[assembly: AssemblyFileVersion("0.2.1.0")]
[assembly: AssemblyInformationalVersion("0.2.1")]

namespace CodexContextHUD
{
    internal static class Program
    {
        private static Mutex instanceMutex;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--renderer-self-test")
                return RendererHudSelfTest.Run(args.Length > 1 ? args[1] : null);
            if (args.Length > 0 && args[0] == "--renderer-attach")
                return RendererHudHost.Run(args);

            string sessionsRoot = FindSessionsRoot();

            if (args.Length > 0 && args[0] == "--self-test")
                return RunSelfTest(sessionsRoot, args.Length > 1 ? args[1] : null);
            if (args.Length > 0 && args[0] == "--warm-cache")
                return WarmCache(sessionsRoot);
            if (args.Length > 1 && args[0] == "--render-preview")
                return RenderPreview(sessionsRoot, args[1]);
            if (args.Length == 0)
                return RendererHudHost.Run(new[] { "--renderer-attach", "9231" });
            if (args[0] != "--legacy-overlay") return 2;

            bool created;
            instanceMutex = new Mutex(true, "Local\\CodexContextHUD.LegacyOverlay", out created);
            if (!created) return 0;

            NativeMethods.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new HudForm(sessionsRoot));
            GC.KeepAlive(instanceMutex);
            return 0;
        }

        private static string FindSessionsRoot()
        {
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] homes = {
                Environment.GetEnvironmentVariable("CODEX_HOME"),
                Path.Combine(user, ".codex")
            };

            foreach (string home in homes.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                string sessions = Path.Combine(home, "sessions");
                if (Directory.Exists(sessions)) return sessions;
            }
            return Path.Combine(user, ".codex", "sessions");
        }

        private static int RunSelfTest(string sessionsRoot, string outputPath)
        {
            FocusReader focus = new FocusReader();
            string latestLog = FocusReader.FindLatest(FocusReader.FindLogsRoot());
            if (latestLog != null) focus.Load(latestLog);
            string latest = SessionReader.FindByThreadId(sessionsRoot, focus.ThreadId);
            if (latest == null) latest = SessionReader.FindLatest(sessionsRoot);
            SessionReader reader = new SessionReader();
            if (latest != null) reader.Load(latest);
            bool dataOk = latest == null ||
                          (reader.Compressions >= 0 &&
                           (reader.ContextPercent < 0 || reader.ContextPercent <= 100));
            bool ok = dataOk &&
                      HudForm.CompressionSeverity(1) == 0 &&
                      HudForm.CompressionSeverity(2) == 1 &&
                      HudForm.CompressionSeverity(3) == 2 &&
                      HudForm.ContextSeverity(87) == 2 &&
                      SessionReader.RemainingFromUsedPercent(88) == 12 &&
                      HudForm.Clamp(20, 0, 10) == 10 &&
                      HudForm.BottomAnchoredY(100, 20, 5) == 75 &&
                       HudForm.FixedHudX(0, 1000) == 210 &&
                       Math.Abs(HudForm.FollowLinear(0, 100, 16, 5200) - 83.2) < .1 &&
                       Math.Abs(HudForm.FollowLinear(90, 100, 16, 5200) - 100) < .1 &&
                       Math.Abs(HudForm.SpatialMotionProgress(100) - .5904) < .0001 &&
                       Math.Abs(HudForm.SpatialMotionProgress(500) - 1) < .0001 &&
                       HudForm.SpatialMotionProgress(200) >
                           HudForm.SpatialMotionProgress(100) &&
                       HudForm.SpatialMotionProgress(400) >
                           HudForm.SpatialMotionProgress(200) &&
                       Math.Abs(HudForm.SpatialMotionProgress(400) - .9984) < .0001 &&
                       Math.Abs(HudForm.SpatialMotionProgress(499) - 1) < .0001 &&
                       HudForm.SidebarStateFromName("隐藏边栏") == 1 &&
                       HudForm.SidebarStateFromName("显示边栏") == 0 &&
                       HudForm.SidebarStateFromName("Hide sidebar") == 1 &&
                       Math.Abs(HudForm.TimingElapsedMs(10,
                           10 + Stopwatch.Frequency) - 1000) < .0001 &&
                       Math.Abs(HudForm.SidebarMotionTarget(100, 344, true) - 272) < .1 &&
                       Math.Abs(HudForm.SidebarMotionTarget(272, 344, false) - 100) < .1 &&
                      HudForm.PointDistanceSquared(
                          new NativeMethods.POINT { X = 10, Y = 20 },
                          new NativeMethods.POINT { X = 13, Y = 24 }) == 25 &&
                      FocusReader.ExtractActiveThreadId(
                          "thread_stream_view_activity_changed active=true conversationId=11111111-2222-4333-8444-555555555555 rendererWindowFocused=true") ==
                          "11111111-2222-4333-8444-555555555555" &&
                      Math.Abs(HudForm.CubicBezier(0, .2, .8, .2, 1)) < 0.001 &&
                      Math.Abs(HudForm.CubicBezier(1, .2, .8, .2, 1) - 1) < 0.001;
            string result = string.Format(
                "ok={0};compressions={1};context={2};session={3};thread={4}",
                ok, reader.Compressions, reader.ContextPercent,
                latest == null ? "none" : Path.GetFileName(latest),
                focus.ThreadId ?? "none");
            if (!string.IsNullOrWhiteSpace(outputPath)) File.WriteAllText(outputPath, result, Encoding.UTF8);
            return ok ? 0 : 1;
        }

        private static int RenderPreview(string sessionsRoot, string outputPath)
        {
            try
            {
                NativeMethods.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (HudForm form = new HudForm(sessionsRoot))
                using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.CreateControl();
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                return 0;
            }
            catch { return 1; }
        }

        private static int WarmCache(string sessionsRoot)
        {
            try
            {
                new SessionCache(sessionsRoot).Warm();
                return 0;
            }
            catch { return 1; }
        }
    }

    internal sealed class ThreadCatalog : IDisposable
    {
        private static readonly Regex Id = new Regex(
            "\\\"id\\\"\\s*:\\s*\\\"([0-9a-f-]{36})\\\"", RegexOptions.Compiled);
        private static readonly Regex Name = new Regex(
            "\\\"thread_name\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"", RegexOptions.Compiled);
        private readonly string indexPath;
        private readonly Dictionary<string, string> byTitle =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> allIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object sync = new object();
        private DateTime loadedWriteUtc;

        internal ThreadCatalog(string sessionsRoot)
        {
            string home = Directory.GetParent(sessionsRoot).FullName;
            indexPath = Path.Combine(home, "session_index.jsonl");
            Reload();
        }

        internal string FindId(string title)
        {
            lock (sync)
            {
                Reload();
                string id;
                if (byTitle.TryGetValue(title, out id)) return id;
                return byTitle.Where(item => title.StartsWith(item.Key, StringComparison.Ordinal))
                    .OrderByDescending(item => item.Key.Length)
                    .Select(item => item.Value)
                    .FirstOrDefault();
            }
        }

        internal bool ContainsId(string id)
        {
            lock (sync)
            {
                Reload();
                return allIds.Contains(id);
            }
        }

        private void Reload()
        {
            lock (sync)
            {
                if (!File.Exists(indexPath)) return;
                try
                {
                    DateTime writeUtc = File.GetLastWriteTimeUtc(indexPath);
                    if (writeUtc == loadedWriteUtc) return;
                    byTitle.Clear();
                    allIds.Clear();
                    foreach (string line in File.ReadLines(indexPath))
                    {
                        Match id = Id.Match(line);
                        Match name = Name.Match(line);
                        if (id.Success && name.Success)
                        {
                            byTitle[Regex.Unescape(name.Groups[1].Value)] = id.Groups[1].Value;
                            allIds.Add(id.Groups[1].Value);
                        }
                    }
                    loadedWriteUtc = writeUtc;
                }
                catch { }
            }
        }

        public void Dispose() { }
    }

    internal sealed class SessionReader
    {
        private static readonly Regex Compacted = new Regex(
            "\\\"type\\\"\\s*:\\s*\\\"compacted\\\"", RegexOptions.Compiled);
        private static readonly Regex WindowNumber = new Regex(
            "\\\"window_number\\\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);
        private static readonly Regex TokenCount = new Regex(
            "\\\"type\\\"\\s*:\\s*\\\"token_count\\\"", RegexOptions.Compiled);
        private static readonly Regex ContextWindow = new Regex(
            "\\\"model_context_window\\\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);
        private static readonly Regex LastUsage = new Regex(
            "\\\"last_token_usage\\\"\\s*:\\s*\\{([^}]*)\\}", RegexOptions.Compiled);
        private static readonly Regex TotalTokens = new Regex(
            "\\\"total_tokens\\\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);
        private static readonly Regex UsedPercent = new Regex(
            "\\\"used_percent\\\"\\s*:\\s*(\\d+(?:\\.\\d+)?)", RegexOptions.Compiled);
        private static readonly Regex ResetsAt = new Regex(
            "\\\"resets_at\\\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);

        private long offset;
        private string partial = "";

        internal string PathName { get; private set; }
        internal int Compressions { get; private set; }
        internal int ContextPercent { get; private set; }
        internal int RemainingQuotaPercent { get; private set; }
        internal long QuotaResetsAt { get; private set; }
        internal long SnapshotOffset
        {
            get { return Math.Max(0, offset - Encoding.UTF8.GetByteCount(partial ?? "")); }
        }

        internal SessionReader()
        {
            ContextPercent = -1;
            RemainingQuotaPercent = -1;
            QuotaResetsAt = 0;
        }

        internal static string FindLatest(string root)
        {
            try
            {
                if (!Directory.Exists(root)) return null;
                return Directory.EnumerateFiles(root, "rollout-*.jsonl", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Select(file => file.FullName)
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        internal static string FindByThreadId(string root, string threadId)
        {
            try
            {
                if (!Directory.Exists(root) || string.IsNullOrWhiteSpace(threadId)) return null;
                return Directory.EnumerateFiles(root, "*" + threadId + ".jsonl", SearchOption.AllDirectories)
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        internal void Load(string path)
        {
            PathName = path;
            offset = 0;
            partial = "";
            Compressions = 0;
            ContextPercent = -1;
            RemainingQuotaPercent = -1;
            QuotaResetsAt = 0;

            try
            {
                using (FileStream stream = OpenRead(path))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null) Parse(line, false);
                    offset = stream.Length;
                }
                RefreshQuotaFromTail();
            }
            catch { }
        }

        internal void Clear()
        {
            PathName = null;
            offset = 0;
            partial = "";
            Compressions = 0;
            ContextPercent = -1;
            RemainingQuotaPercent = -1;
            QuotaResetsAt = 0;
        }

        internal void Restore(string path, long savedOffset, int compressions,
            int contextPercent, int remainingQuotaPercent, long quotaResetsAt)
        {
            PathName = path;
            offset = savedOffset;
            partial = "";
            Compressions = compressions;
            ContextPercent = contextPercent;
            RemainingQuotaPercent = remainingQuotaPercent;
            QuotaResetsAt = quotaResetsAt;
            Append();
        }

        internal void Append()
        {
            if (PathName == null) return;
            try
            {
                using (FileStream stream = OpenRead(PathName))
                {
                    if (stream.Length < offset) { Load(PathName); return; }
                    stream.Seek(offset, SeekOrigin.Begin);
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true))
                    {
                        string text = partial + reader.ReadToEnd();
                        offset = stream.Length;
                        string[] lines = text.Split('\n');
                        partial = text.EndsWith("\n", StringComparison.Ordinal) ? "" : lines[lines.Length - 1];
                        int complete = partial.Length == 0 ? lines.Length : lines.Length - 1;
                        for (int i = 0; i < complete; i++)
                            Parse(lines[i].TrimEnd('\r'), true);
                    }
                }
            }
            catch { }
        }

        private static FileStream OpenRead(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
        }

        private void Parse(string line, bool includeQuota)
        {
            if (line.IndexOf("\"compacted\"", StringComparison.Ordinal) >= 0 &&
                Compacted.IsMatch(line))
            {
                Match match = WindowNumber.Match(line);
                int window;
                if (match.Success && int.TryParse(match.Groups[1].Value, out window))
                    Compressions = Math.Max(Compressions, window);
                else
                    Compressions++;
                return;
            }

            if (line.IndexOf("\"token_count\"", StringComparison.Ordinal) < 0 ||
                !TokenCount.IsMatch(line)) return;

            if (includeQuota) ParseQuota(line);

            Match windowMatch = ContextWindow.Match(line);
            Match usageMatch = LastUsage.Match(line);
            if (!windowMatch.Success || !usageMatch.Success) return;

            Match totalMatch = TotalTokens.Match(usageMatch.Groups[1].Value);
            long windowSize, total;
            if (!totalMatch.Success ||
                !long.TryParse(windowMatch.Groups[1].Value, out windowSize) ||
                !long.TryParse(totalMatch.Groups[1].Value, out total) || windowSize <= 0) return;

            ContextPercent = Math.Max(0, Math.Min(100,
                (int)Math.Round(total * 100.0 / windowSize, MidpointRounding.AwayFromZero)));
        }

        private bool ParseQuota(string line)
        {
            int rateLimitsIndex = line.IndexOf("\"rate_limits\"", StringComparison.Ordinal);
            if (rateLimitsIndex < 0) return false;
            Match usedMatch = UsedPercent.Match(line, rateLimitsIndex);
            double usedPercent;
            if (!usedMatch.Success || !double.TryParse(usedMatch.Groups[1].Value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out usedPercent)) return false;

            RemainingQuotaPercent = RemainingFromUsedPercent(usedPercent);
            Match resetMatch = ResetsAt.Match(line, rateLimitsIndex);
            long resetAt;
            if (resetMatch.Success && long.TryParse(resetMatch.Groups[1].Value, out resetAt))
                QuotaResetsAt = resetAt;
            return true;
        }

        internal void RefreshQuotaFromTail()
        {
            if (string.IsNullOrWhiteSpace(PathName)) return;
            try
            {
                using (FileStream stream = OpenRead(PathName))
                {
                    int byteCount = (int)Math.Min(stream.Length, 1024 * 1024);
                    if (byteCount <= 0) return;
                    stream.Seek(-byteCount, SeekOrigin.End);
                    byte[] buffer = new byte[byteCount];
                    int read = 0;
                    while (read < byteCount)
                    {
                        int next = stream.Read(buffer, read, byteCount - read);
                        if (next <= 0) break;
                        read += next;
                    }
                    string[] lines = Encoding.UTF8.GetString(buffer, 0, read).Split('\n');
                    for (int i = lines.Length - 1; i >= 0; i--)
                    {
                        string line = lines[i].TrimEnd('\r');
                        if (line.IndexOf("\"token_count\"", StringComparison.Ordinal) >= 0 &&
                            ParseQuota(line)) return;
                    }
                }
            }
            catch { }
        }

        internal static int RemainingFromUsedPercent(double usedPercent)
        {
            return Math.Max(0, Math.Min(100,
                (int)Math.Round(100 - usedPercent, MidpointRounding.AwayFromZero)));
        }
    }

    internal sealed class SessionCache
    {
        private sealed class Entry
        {
            internal long Offset;
            internal int Compressions;
            internal int ContextPercent;
        }

        private readonly string sessionsRoot;
        private readonly string cachePath;
        private readonly object sync = new object();
        private readonly Dictionary<string, Entry> entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        internal SessionCache(string sessionsRoot)
        {
            this.sessionsRoot = sessionsRoot;
            cachePath = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
                "CodexContextHUD", "session-cache.tsv");
            Load();
        }

        private void Load()
        {
            if (!File.Exists(cachePath)) return;
            try
            {
                foreach (string line in File.ReadLines(cachePath))
                {
                    string[] fields = line.Split('\t');
                    long offset;
                    int compressions, context;
                    if ((fields.Length < 4 || fields.Length > 6) ||
                        !long.TryParse(fields[1], out offset) ||
                        !int.TryParse(fields[2], out compressions) ||
                        !int.TryParse(fields[3], out context)) continue;
                    string path = Encoding.UTF8.GetString(Convert.FromBase64String(fields[0]));
                    entries[path] = new Entry {
                        Offset = offset,
                        Compressions = compressions,
                        ContextPercent = context
                    };
                }
            }
            catch { entries.Clear(); }
        }

        internal bool TryRestore(string path, out SessionReader reader)
        {
            reader = null;
            try
            {
                Entry entry;
                lock (sync)
                    if (!entries.TryGetValue(path, out entry)) return false;
                if (!File.Exists(path) || new FileInfo(path).Length < entry.Offset) return false;
                reader = new SessionReader();
                reader.Restore(path, entry.Offset, entry.Compressions,
                    entry.ContextPercent, -1, 0);
                return true;
            }
            catch { reader = null; return false; }
        }

        internal void Update(SessionReader reader)
        {
            if (reader == null || string.IsNullOrWhiteSpace(reader.PathName)) return;
            lock (sync)
                entries[reader.PathName] = new Entry {
                    Offset = reader.SnapshotOffset,
                    Compressions = reader.Compressions,
                    ContextPercent = reader.ContextPercent
                };
        }

        internal void Warm()
        {
            if (!Directory.Exists(sessionsRoot)) return;
            foreach (string path in Directory.EnumerateFiles(
                sessionsRoot, "rollout-*.jsonl", SearchOption.AllDirectories))
            {
                SessionReader reader;
                if (!TryRestore(path, out reader))
                {
                    reader = new SessionReader();
                    reader.Load(path);
                }
                Update(reader);
            }
            Save();
        }

        internal void Save()
        {
            try
            {
                string[] lines;
                lock (sync)
                    lines = entries.Select(item => string.Join("\t",
                        Convert.ToBase64String(Encoding.UTF8.GetBytes(item.Key)),
                        item.Value.Offset, item.Value.Compressions,
                        item.Value.ContextPercent)).ToArray();
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                File.WriteAllLines(cachePath, lines, Encoding.UTF8);
            }
            catch { }
        }
    }

    internal sealed class FocusReader
    {
        private static readonly Regex ConversationId = new Regex(
            "conversationId=([0-9a-f-]{36})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private long offset;
        private string partial = "";

        internal string PathName { get; private set; }
        internal string ThreadId { get; private set; }

        internal static string FindLogsRoot()
        {
            try
            {
                string packages = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
                string package = Directory.EnumerateDirectories(packages, "OpenAI.Codex_*")
                    .OrderByDescending(path => Directory.GetLastWriteTimeUtc(path)).FirstOrDefault();
                if (package == null) return null;
                string logs = Path.Combine(package, "LocalCache", "Local", "Codex", "Logs");
                return Directory.Exists(logs) ? logs : null;
            }
            catch { return null; }
        }

        internal static string FindLatest(string root)
        {
            try
            {
                if (!Directory.Exists(root)) return null;
                string[] files = Directory.EnumerateFiles(root, "*.log", SearchOption.AllDirectories).ToArray();
                string[] main = files.Where(path => Path.GetFileName(path)
                    .IndexOf("-t0-", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                return (main.Length > 0 ? main : files)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Select(file => file.FullName).FirstOrDefault();
            }
            catch { return null; }
        }

        internal void Load(string path)
        {
            PathName = path;
            offset = 0;
            partial = "";
            try
            {
                using (FileStream stream = OpenRead(path))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null) Parse(line);
                    offset = stream.Length;
                }
            }
            catch { }
        }

        internal void Append()
        {
            if (PathName == null) return;
            try
            {
                using (FileStream stream = OpenRead(PathName))
                {
                    if (stream.Length < offset) { Load(PathName); return; }
                    stream.Seek(offset, SeekOrigin.Begin);
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true))
                    {
                        string text = partial + reader.ReadToEnd();
                        offset = stream.Length;
                        string[] lines = text.Split('\n');
                        partial = text.EndsWith("\n", StringComparison.Ordinal) ? "" : lines[lines.Length - 1];
                        int complete = partial.Length == 0 ? lines.Length : lines.Length - 1;
                        for (int i = 0; i < complete; i++) Parse(lines[i].TrimEnd('\r'));
                    }
                }
            }
            catch { }
        }

        private static FileStream OpenRead(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
        }

        private void Parse(string line)
        {
            string threadId = ExtractActiveThreadId(line);
            if (threadId != null) ThreadId = threadId;
        }

        internal static string ExtractActiveThreadId(string line)
        {
            if (string.IsNullOrEmpty(line) ||
                line.IndexOf("thread_stream_view_activity_changed", StringComparison.OrdinalIgnoreCase) < 0 ||
                line.IndexOf(" active=true ", StringComparison.OrdinalIgnoreCase) < 0 ||
                line.IndexOf(" rendererWindowFocused=true", StringComparison.OrdinalIgnoreCase) < 0)
                return null;
            Match match = ConversationId.Match(line);
            return match.Success ? match.Groups[1].Value : null;
        }
    }

    internal sealed class HudForm : Form
    {
        private const int HudContentWidth = 260;
        private const int HudContentHeight = 32;
        private SessionReader reader = new SessionReader();
        private readonly FocusReader focusReader = new FocusReader();
        private readonly Dictionary<string, SessionReader> readerCache =
            new Dictionary<string, SessionReader>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> sessionPathCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ThreadCatalog catalog;
        private readonly SessionCache sessionCache;
        private readonly string sessionsRoot;
        private readonly string logsRoot;
        private readonly object pendingLock = new object();
        private readonly System.Windows.Forms.Timer timer;
        private readonly System.Windows.Forms.Timer switchSettleTimer;
        private readonly FileSystemWatcher sessionWatcher;
        private readonly FileSystemWatcher logWatcher;
        private readonly Font boldFont;
        private string pendingSessionPath;
        private string pendingLogPath;
        private int sessionDispatchPending;
        private int logDispatchPending;
        private string currentThreadId;
        private string directThreadId;
        private DateTime directThreadUntil;
        private uint lastForegroundPid;
        private volatile bool lastForegroundIsCodex;
        private IntPtr ownerWindow;
        private int observedCompressions;
        private int observedContext;
        private int observedQuotaPercent = -1;
        private long observedQuotaResetsAt;
        private float displayedContext;
        private float animationFromContext;
        private float animationSpin;
        private float animationIconScale = 1f;
        private DateTime animationStarted;
        private int animationDurationMs;
        private bool switchAnimation;
        private bool compressionBump;
        private bool switchPending;
        private bool animationActive;
        private bool animationPausedForNativeMotion;
        private DateTime animationPauseStartedUtc;
        private double animationPausedElapsedMs;
        private bool animationDeferredForNativeMotion;
        private bool animationDeferredForce;
        private string pendingCommitThreadId;
        private int pollTicks;
        private int focusPollTicks;
        private int sessionPollTicks;
        private readonly object anchorStateLock = new object();
        private Rectangle anchorRect;
        private bool anchorIsPermission;
        private DateTime anchorLastEventUtc;
        private double anchorLastGapMs = 32;
        private long requestedAnchorWindow;
        private Thread anchorThread;
        private volatile bool anchorWorkerStopping;
        private AutomationElement anchorSubscribedElement;
        private bool anchorSubscriptionIsPermission;
        private readonly AutomationPropertyChangedEventHandler anchorBoundsHandler;
        private AutomationElement sidebarToggleSubscribedElement;
        private readonly AutomationPropertyChangedEventHandler sidebarNameHandler;
        private AutomationElement rightPanelToggleSubscribedElement;
        private readonly AutomationPropertyChangedEventHandler rightPanelToggleHandler;
        private bool sidebarStateKnown;
        private bool sidebarOpen;
        private Rectangle sidebarToggleRect;
        private bool sidebarPredictionPending;
        private bool sidebarPredictionExpectedOpen;
        private long sidebarPredictionTimestamp;
        private double sidebarWidthPx;
        private double sidebarTransitionWidthPx;
        private Rectangle rightPanelToggleRect;
        private bool rightPanelStateKnown;
        private bool rightPanelOpen;
        private bool rightPanelPredictionPending;
        private bool rightPanelPredictionExpectedOpen;
        private long rightPanelPredictionTimestamp;
        private bool rightPanelLearningPending;
        private double rightPanelLearningStartAnchorX;
        private double rightPanelMotionWidthPx;
        private int sidebarTransitionVersion;
        private bool sidebarTransitionOpening;
        private DateTime sidebarTransitionStartedUtc;
        private long sidebarTransitionStartedTimestamp;
        private int observedSidebarTransitionVersion;
        private readonly object nativeMotionLock = new object();
        private double renderedHudX = double.NaN;
        private double renderedHudY = double.NaN;
        private volatile bool nativeMotionActive;
        private readonly AutoResetEvent nativeMotionWake = new AutoResetEvent(false);
        private Thread nativeMotionThread;
        private volatile bool nativeMotionStopping;
        private volatile int nativeMotionPumpReady;
        private int nativeMotionFrameQueued;
        private int nativeMotionRenderedFrames;
        private long nativeMotionLastFrameTimestamp;
        private double nativeMotionMinFrameMs;
        private double nativeMotionMaxFrameMs;
        private int nativeMotionGeneration;
        private double nativeMotionStartX;
        private double nativeMotionFinalX;
        private long nativeMotionStartedTimestamp;
        private IntPtr nativeMotionWindow;
        private int nativeMotionY;
        private volatile bool nativeCanvasActive;
        private int nativeCanvasLeft;
        private int nativeCanvasWidth;
        private int nativeCanvasBaseWidth;
        private int nativeCanvasBaseHeight;
        private double nativeMotionStartOffsetX;
        private double nativeMotionFinalOffsetX;
        private double contentOffsetX;
        private DateTime lastPositionFrame = DateTime.UtcNow;
        private NativeMethods.LowLevelMouseProc mouseProc;
        private IntPtr mouseHook;
        private Thread mouseHookThread;
        private uint mouseHookThreadId;
        private volatile bool mouseHookStopping;
        private volatile int mouseHookReady;
        private int mouseUpCount;
        private int sidebarPointerHitCount;
        private readonly object motionTimingLock = new object();
        private int motionTimingSequence;
        private string motionTimingSource = "none";
        private long motionTimingPointerTimestamp;
        private long motionTimingPositionTimestamp;
        private long motionTimingStartTimestamp;
        private long motionTimingFirstHudFrameTimestamp;
        private long motionTimingFirstAnchorFrameTimestamp;
        private NativeMethods.POINT hoverPoint;
        private DateTime hoverChanged;
        private bool hoverPending;
        private readonly object hoverLookupLock = new object();
        private NativeMethods.POINT pendingHoverLookupPoint;
        private int pendingHoverLookupVersion;
        private bool hoverLookupWorkerActive;
        private NativeMethods.POINT resolvedHoverPoint;
        private string resolvedHoverThreadId;
        private DateTime resolvedHoverAt;
        private int clickLookupVersion;
        private readonly HashSet<string> preloadPending =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal HudForm(string sessionsRoot)
        {
            this.sessionsRoot = sessionsRoot;
            catalog = new ThreadCatalog(sessionsRoot);
            sessionCache = new SessionCache(sessionsRoot);
            mouseProc = MouseHook;
            anchorBoundsHandler = AnchorBoundsChanged;
            sidebarNameHandler = SidebarNameChanged;
            rightPanelToggleHandler = RightPanelToggleChanged;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(43, 43, 44);
            TransparencyKey = BackColor;
            Font = new Font("Segoe UI Variable Text", 7f, FontStyle.Regular, GraphicsUnit.Point);
            boldFont = new Font(Font, FontStyle.Bold);
            ClientSize = new Size(HudContentWidth, HudContentHeight);
            DoubleBuffered = true;

            logsRoot = FocusReader.FindLogsRoot();
            string latestLog = FocusReader.FindLatest(logsRoot);
            if (latestLog != null)
            {
                focusReader.Load(latestLog);
                currentThreadId = focusReader.ThreadId;
            }

            string current = SessionReader.FindByThreadId(sessionsRoot, currentThreadId);
            if (current == null) current = SessionReader.FindLatest(sessionsRoot);
            if (current != null)
            {
                SessionReader cached;
                if (sessionCache.TryRestore(current, out cached)) reader = cached;
                else reader.Load(current);
                if (reader.RemainingQuotaPercent < 0) reader.RefreshQuotaFromTail();
                sessionCache.Update(reader);
                if (!string.IsNullOrWhiteSpace(currentThreadId))
                {
                    readerCache[currentThreadId] = reader;
                    sessionPathCache[currentThreadId] = current;
                }
            }
            observedCompressions = reader.Compressions;
            observedContext = reader.ContextPercent;
            observedQuotaPercent = reader.RemainingQuotaPercent;
            observedQuotaResetsAt = reader.QuotaResetsAt;
            displayedContext = reader.ContextPercent;
            UpdateDiagnosticTitle();

            if (Directory.Exists(sessionsRoot))
            {
                sessionWatcher = new FileSystemWatcher(sessionsRoot, "rollout-*.jsonl");
                sessionWatcher.IncludeSubdirectories = true;
                sessionWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime;
                sessionWatcher.Changed += QueueSession;
                sessionWatcher.Created += QueueSession;
                sessionWatcher.EnableRaisingEvents = true;
            }

            if (Directory.Exists(logsRoot))
            {
                logWatcher = new FileSystemWatcher(logsRoot, "*.log");
                logWatcher.IncludeSubdirectories = true;
                logWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime;
                logWatcher.Changed += QueueLog;
                logWatcher.Created += QueueLog;
                logWatcher.EnableRaisingEvents = true;
            }

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 16;
            timer.Tick += Tick;
            timer.Start();

            switchSettleTimer = new System.Windows.Forms.Timer();
            switchSettleTimer.Interval = 220;
            switchSettleTimer.Tick += CompleteSettledSwitch;

            anchorThread = new Thread(AnchorWorker);
            anchorThread.Name = "Codex HUD anchor reader";
            anchorThread.IsBackground = true;
            anchorThread.Start();

            mouseHookThread = new Thread(MouseHookWorker);
            mouseHookThread.Name = "Codex HUD mouse input clock";
            mouseHookThread.IsBackground = true;
            mouseHookThread.Priority = ThreadPriority.AboveNormal;
            mouseHookThread.Start();

            nativeMotionThread = new Thread(NativeMotionWorker);
            nativeMotionThread.Name = "Codex HUD DWM motion clock";
            nativeMotionThread.IsBackground = true;
            nativeMotionThread.Priority = ThreadPriority.AboveNormal;
            nativeMotionThread.Start();

            AnimateIfChanged(true);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW |
                              NativeMethods.WS_EX_NOACTIVATE |
                              NativeMethods.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyWindowRegion(Width, Height);
        }

        private void ApplyWindowRegion(int width, int height)
        {
            IntPtr region = NativeMethods.CreateRoundRectRgn(0, 0,
                width + 1, height + 1, 12, 12);
            NativeMethods.SetWindowRgn(Handle, region, true);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WM_APP_NATIVE_MOTION_FRAME)
            {
                Interlocked.Exchange(ref nativeMotionFrameQueued, 0);
                if (nativeMotionActive) AdvanceNativeMotionFrame();
                return;
            }
            base.WndProc(ref message);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            int offsetX;
            lock (nativeMotionLock) offsetX = (int)Math.Round(contentOffsetX);

            int compressionSeverity = CompressionSeverity(observedCompressions);
            Color compressionColor = compressionSeverity == 2 ? Color.FromArgb(224, 104, 104) :
                compressionSeverity == 1 ? Color.FromArgb(217, 168, 83) : Color.FromArgb(119, 151, 230);
            int shownContext = displayedContext < 0 ? -1 : (int)Math.Round(displayedContext);
            double elapsed = switchAnimation
                ? ContentAnimationElapsedMs() : double.MaxValue;
            int ringColorValue = switchAnimation && elapsed < 390
                ? (int)Math.Round(animationFromContext) : observedContext;
            int contextSeverity = ContextSeverity(ringColorValue);
            Color contextColor = contextSeverity == 2 ? Color.FromArgb(224, 104, 104) :
                contextSeverity == 1 ? Color.FromArgb(217, 168, 83) :
                contextSeverity == 0 ? Color.FromArgb(91, 178, 122) : Color.FromArgb(111, 115, 123);
            Color targetContextColor = ContextSeverity(observedContext) == 2 ? Color.FromArgb(224, 104, 104) :
                ContextSeverity(observedContext) == 1 ? Color.FromArgb(217, 168, 83) :
                ContextSeverity(observedContext) == 0 ? Color.FromArgb(91, 178, 122) : Color.FromArgb(111, 115, 123);
            int quotaPercent = Volatile.Read(ref observedQuotaPercent);
            Color quotaColor = QuotaColor(quotaPercent);
            Color labelColor = Color.FromArgb(190, 190, 194);

            GraphicsState iconState = e.Graphics.Save();
            e.Graphics.TranslateTransform(offsetX + 10, 16);
            e.Graphics.ScaleTransform(animationIconScale, animationIconScale);
            e.Graphics.RotateTransform(animationSpin);
            e.Graphics.TranslateTransform(-(offsetX + 10), -16);
            using (Pen iconPen = new Pen(compressionColor, 1.6f))
            {
                iconPen.StartCap = LineCap.Round;
                iconPen.EndCap = LineCap.Round;
                e.Graphics.DrawArc(iconPen, offsetX + 4, 10, 12, 12, 25, 125);
                e.Graphics.DrawArc(iconPen, offsetX + 4, 10, 12, 12, 205, 125);
            }
            using (Brush iconBrush = new SolidBrush(compressionColor))
            {
                e.Graphics.FillPolygon(iconBrush, new[] { new PointF(offsetX + 3, 14), new PointF(offsetX + 3, 11), new PointF(offsetX + 7, 13) });
                e.Graphics.FillPolygon(iconBrush, new[] { new PointF(offsetX + 17, 18), new PointF(offsetX + 17, 21), new PointF(offsetX + 13, 19) });
            }
            e.Graphics.Restore(iconState);

            TextFormatFlags metricFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            Size compressionLabelSize = TextRenderer.MeasureText(e.Graphics, "压缩", Font,
                new Size(int.MaxValue, Height), metricFlags);
            int compressionValueX = 19 + compressionLabelSize.Width + 2;
            TextRenderer.DrawText(e.Graphics, "压缩", Font,
                new Rectangle(offsetX + 19, 0, compressionLabelSize.Width, Height), labelColor, metricFlags);
            DrawAnimatedMetric(e.Graphics,
                observedCompressions.ToString(),
                new Rectangle(offsetX + compressionValueX, 0, 70 - compressionValueX, Height),
                switchAnimation ? compressionColor : Color.FromArgb(239, 239, 241),
                switchAnimation ? Math.Min(1, elapsed / 500.0) : 1, switchAnimation,
                metricFlags);

            using (Pen divider = new Pen(Color.FromArgb(55, 55, 58), 1f))
                e.Graphics.DrawLine(divider, offsetX + 74, 8, offsetX + 74, Height - 8);

            Rectangle ring = new Rectangle(offsetX + 82, 9, 14, 14);
            using (Pen ringTrack = new Pen(Color.FromArgb(70, 70, 74), 2.5f))
                e.Graphics.DrawEllipse(ringTrack, ring);
            if (shownContext >= 0)
            {
                using (Pen ringValue = new Pen(contextColor, 2.5f))
                {
                    ringValue.StartCap = LineCap.Round;
                    ringValue.EndCap = LineCap.Round;
                    e.Graphics.DrawArc(ringValue, ring, -90f, shownContext * 3.6f);
                }
            }

            string context = observedContext < 0 ? "?" : observedContext + "%";
            Size contextLabelSize = TextRenderer.MeasureText(e.Graphics, "上下文", Font,
                new Size(int.MaxValue, Height), metricFlags);
            int contextValueX = 101 + contextLabelSize.Width + 2;
            TextRenderer.DrawText(e.Graphics, "上下文", Font,
                new Rectangle(offsetX + 101, 0, contextLabelSize.Width, Height), labelColor, metricFlags);
            DrawAnimatedMetric(e.Graphics, context,
                new Rectangle(offsetX + contextValueX, 0,
                    172 - contextValueX, Height),
                switchAnimation ? targetContextColor : Color.FromArgb(239, 239, 241),
                switchAnimation ? Math.Max(0, Math.Min(1, (elapsed - 390) / 520.0)) : 1,
                switchAnimation && elapsed >= 390, metricFlags);

            using (Pen divider = new Pen(Color.FromArgb(55, 55, 58), 1f))
                e.Graphics.DrawLine(divider, offsetX + 176, 8, offsetX + 176, Height - 8);

            Rectangle battery = new Rectangle(offsetX + 184, 11, 11, 10);
            using (Pen batteryPen = new Pen(
                quotaPercent < 0 ? Color.FromArgb(111, 115, 123) : quotaColor, 1.4f))
            {
                batteryPen.LineJoin = LineJoin.Round;
                e.Graphics.DrawRectangle(batteryPen, battery);
                e.Graphics.DrawLine(batteryPen, offsetX + 196, 14, offsetX + 196, 18);
            }
            if (quotaPercent > 0)
            {
                int batteryFill = Math.Max(1,
                    (int)Math.Round(7 * quotaPercent / 100.0));
                using (Brush batteryBrush = new SolidBrush(quotaColor))
                    e.Graphics.FillRectangle(batteryBrush,
                        offsetX + 186, 13, batteryFill, 6);
            }

            Size quotaLabelSize = TextRenderer.MeasureText(e.Graphics, "额度", Font,
                new Size(int.MaxValue, Height), metricFlags);
            TextRenderer.DrawText(e.Graphics, "额度", Font,
                new Rectangle(offsetX + 201, 0, quotaLabelSize.Width, Height), labelColor, metricFlags);
            string quota = quotaPercent < 0 ? "—" : quotaPercent + "%";
            TextRenderer.DrawText(e.Graphics, quota, Font,
                new Rectangle(offsetX + 201 + quotaLabelSize.Width + 2, 0,
                    HudContentWidth - (201 + quotaLabelSize.Width + 2) - 1, Height),
                quotaPercent < 0 ? Color.FromArgb(151, 155, 164) : quotaColor, metricFlags);
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount),
                (int)(from.B + (to.B - from.B) * amount));
        }

        private void DrawAnimatedMetric(Graphics graphics, string text, Rectangle bounds,
            Color color, double progress, bool animate, TextFormatFlags flags)
        {
            if (!animate)
            {
                TextRenderer.DrawText(graphics, text, Font, bounds, color, flags);
                return;
            }
            float y, scale, opacity, glow;
            FlipFrame(progress, out y, out scale, out opacity, out glow);
            Color lit = Blend(color, Color.White, glow);
            Color draw = Blend(BackColor, lit, opacity);
            TextRenderer.DrawText(graphics, text, boldFont,
                new Rectangle(bounds.X - 2, bounds.Y + (int)Math.Round(y),
                    bounds.Width + 4, bounds.Height), draw, flags);
        }

        private static void FlipFrame(double progress, out float y, out float scale,
            out float opacity, out float glow)
        {
            double eased = CubicBezier(Math.Max(0, Math.Min(1, progress)), .2, .8, .2, 1);
            if (eased <= .62)
            {
                float local = (float)(eased / .62);
                y = 7f - 8f * local;
                scale = .92f + .20f * local;
                opacity = .18f + .82f * local;
                glow = .35f - .16f * local;
            }
            else
            {
                float local = (float)((eased - .62) / .38);
                y = -1f + local;
                scale = 1.12f - .12f * local;
                opacity = 1f;
                glow = .19f * (1f - local);
            }
        }

        internal static int CompressionSeverity(int count)
        {
            return count >= 3 ? 2 : count == 2 ? 1 : 0;
        }

        internal static int ContextSeverity(int percent)
        {
            return percent < 0 ? -1 : percent >= 85 ? 2 : percent >= 70 ? 1 : 0;
        }

        private static Color QuotaColor(int quotaPercent)
        {
            return quotaPercent < 0 ? Color.FromArgb(111, 115, 123) :
                quotaPercent <= 15 ? Color.FromArgb(224, 104, 104) :
                quotaPercent <= 30 ? Color.FromArgb(217, 168, 83) :
                Color.FromArgb(91, 178, 122);
        }

        private void QueueSession(object sender, FileSystemEventArgs e)
        {
            bool queued = false;
            lock (pendingLock)
            {
                if (string.Equals(e.FullPath, reader.PathName, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(currentThreadId) &&
                     e.Name.IndexOf(currentThreadId, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    pendingSessionPath = e.FullPath;
                    queued = true;
                }
            }
            if (queued) ScheduleSessionDispatch();
        }

        private void QueueLog(object sender, FileSystemEventArgs e)
        {
            if (e.Name.IndexOf("-t0-", StringComparison.OrdinalIgnoreCase) < 0) return;
            lock (pendingLock)
            {
                pendingLogPath = e.FullPath;
            }
            ScheduleLogDispatch();
        }

        private void ScheduleSessionDispatch()
        {
            if (Interlocked.Exchange(ref sessionDispatchPending, 1) != 0) return;
            try { BeginInvoke((MethodInvoker)ProcessPendingSession); }
            catch (InvalidOperationException) { Interlocked.Exchange(ref sessionDispatchPending, 0); }
        }

        private void ScheduleLogDispatch()
        {
            if (Interlocked.Exchange(ref logDispatchPending, 1) != 0) return;
            try { BeginInvoke((MethodInvoker)ProcessPendingLog); }
            catch (InvalidOperationException) { Interlocked.Exchange(ref logDispatchPending, 0); }
        }

        private void ProcessPendingSession()
        {
            string sessionPath;
            lock (pendingLock)
            {
                sessionPath = pendingSessionPath;
                pendingSessionPath = null;
            }
            Interlocked.Exchange(ref sessionDispatchPending, 0);

            if (sessionPath != null && !switchPending)
            {
                if (string.Equals(sessionPath, reader.PathName, StringComparison.OrdinalIgnoreCase)) reader.Append();
                else if (!string.IsNullOrWhiteSpace(currentThreadId) &&
                         Path.GetFileName(sessionPath).IndexOf(currentThreadId, StringComparison.OrdinalIgnoreCase) >= 0)
                    reader.Load(sessionPath);
                AnimateIfChanged();
            }

            lock (pendingLock)
                if (pendingSessionPath != null) ScheduleSessionDispatch();
        }

        private void ProcessPendingLog()
        {
            string logPath;
            lock (pendingLock)
            {
                logPath = pendingLogPath;
                pendingLogPath = null;
            }
            Interlocked.Exchange(ref logDispatchPending, 0);

            if (logPath != null)
            {
                if (string.Equals(logPath, focusReader.PathName, StringComparison.OrdinalIgnoreCase)) focusReader.Append();
                else focusReader.Load(logPath);
                ApplyFocusedThread();
            }

            lock (pendingLock)
                if (pendingLogPath != null) ScheduleLogDispatch();
        }

        private void Tick(object sender, EventArgs e)
        {
            // Fallback for the tiny startup window before the form handle exists.
            lock (pendingLock)
            {
                if (pendingLogPath != null) ScheduleLogDispatch();
                if (pendingSessionPath != null) ScheduleSessionDispatch();
            }
            PositionBesideCodex();
            if (animationActive && !nativeMotionActive) Animate(null, EventArgs.Empty);
            if (!lastForegroundIsCodex) return;
            // The shell transition owns this half-second. Keep file/UIA hit-test work
            // off the WinForms message pump so WM_TIMER can render every frame.
            if (nativeMotionActive) return;
            NativeMethods.POINT settledHover = new NativeMethods.POINT();
            bool resolveHover = false;
            lock (hoverLookupLock)
            {
                if (hoverPending && (DateTime.UtcNow - hoverChanged).TotalMilliseconds >= 45)
                {
                    hoverPending = false;
                    settledHover = hoverPoint;
                    resolveHover = true;
                }
            }
            if (resolveHover) QueuePreloadTaskAtPoint(settledHover);

            if (++focusPollTicks >= 3)
            {
                focusPollTicks = 0;
                string latestLog = null;
                if (++pollTicks >= 110)
                {
                    pollTicks = 0;
                    latestLog = FocusReader.FindLatest(logsRoot);
                }
                if (latestLog != null &&
                    !string.Equals(latestLog, focusReader.PathName, StringComparison.OrdinalIgnoreCase))
                    focusReader.Load(latestLog);
                else
                    focusReader.Append();

                ApplyFocusedThread();
            }
            if (++sessionPollTicks >= 13)
            {
                sessionPollTicks = 0;
                if (!switchPending)
                {
                    reader.Append();
                    AnimateIfChanged();
                }
            }
        }

        private void ApplyFocusedThread()
        {
            string threadId = focusReader.ThreadId;
            if (!string.IsNullOrWhiteSpace(directThreadId))
            {
                if (string.Equals(threadId, directThreadId,
                    StringComparison.OrdinalIgnoreCase))
                    directThreadId = null;
                else if (DateTime.UtcNow < directThreadUntil)
                    return;
                else
                    directThreadId = null;
            }
            ApplyThreadId(threadId);
        }

        private void ApplyThreadId(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId) ||
                string.Equals(threadId, currentThreadId, StringComparison.OrdinalIgnoreCase)) return;

            sessionCache.Update(reader);
            ThreadPool.QueueUserWorkItem(delegate { sessionCache.Save(); });
            currentThreadId = threadId;
            BeginPendingSwitch(threadId);
            SessionReader next;
            if (readerCache.TryGetValue(threadId, out next))
            {
                next.Append();
                reader = next;
                ScheduleSettledSwitch(threadId);
                return;
            }

            string focused;
            sessionPathCache.TryGetValue(threadId, out focused);
            ThreadPool.QueueUserWorkItem(delegate
            {
                if (focused == null) focused = SessionReader.FindByThreadId(sessionsRoot, threadId);
                SessionReader loaded;
                if (focused == null || !sessionCache.TryRestore(focused, out loaded))
                {
                    loaded = new SessionReader();
                    if (focused != null) loaded.Load(focused);
                }
                sessionCache.Update(loaded);
                sessionCache.Save();
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (focused != null) sessionPathCache[threadId] = focused;
                        readerCache[threadId] = loaded;
                        if (!string.Equals(threadId, currentThreadId, StringComparison.OrdinalIgnoreCase)) return;
                        reader = loaded;
                        ScheduleSettledSwitch(threadId);
                    });
                }
                catch (InvalidOperationException) { }
            });
        }

        private void BeginPendingSwitch(string threadId)
        {
            switchSettleTimer.Stop();
            pendingCommitThreadId = threadId;
            switchPending = true;
            animationActive = false;
            animationPausedForNativeMotion = false;
            switchAnimation = false;
            compressionBump = false;
            displayedContext = observedContext;
            animationSpin = 0f;
            animationIconScale = 1f;
            Invalidate();
        }

        private void ScheduleSettledSwitch(string threadId)
        {
            if (!string.Equals(threadId, currentThreadId, StringComparison.OrdinalIgnoreCase)) return;
            pendingCommitThreadId = threadId;
            switchSettleTimer.Stop();
            switchSettleTimer.Start();
        }

        private void CompleteSettledSwitch(object sender, EventArgs e)
        {
            switchSettleTimer.Stop();
            string threadId = pendingCommitThreadId;
            if (string.IsNullOrWhiteSpace(threadId) ||
                !string.Equals(threadId, currentThreadId, StringComparison.OrdinalIgnoreCase)) return;
            pendingCommitThreadId = null;
            switchPending = false;
            AnimateIfChanged(true);
        }

        private IntPtr MouseHook(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && lastForegroundIsCodex && IsHandleCreated)
            {
                NativeMethods.MSLLHOOKSTRUCT info =
                    (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                        data, typeof(NativeMethods.MSLLHOOKSTRUCT));
                NativeMethods.POINT point = info.Point;
                if (message == (IntPtr)NativeMethods.WM_MOUSEMOVE)
                {
                    lock (hoverLookupLock)
                    {
                        hoverPoint = point;
                        hoverChanged = DateTime.UtcNow;
                        hoverPending = true;
                    }
                }
                else if (message == (IntPtr)NativeMethods.WM_LBUTTONDOWN)
                {
                    QueueCodexClick(point);
                }
                else if (message == (IntPtr)NativeMethods.WM_LBUTTONUP)
                {
                    Interlocked.Increment(ref mouseUpCount);
                    if (!PublishRightPanelPointerTrigger(point))
                        PublishSidebarPointerTrigger(point);
                }
            }
            return NativeMethods.CallNextHookEx(mouseHook, code, message, data);
        }

        private void MouseHookWorker()
        {
            mouseHookThreadId = NativeMethods.GetCurrentThreadId();
            mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL,
                mouseProc, NativeMethods.GetModuleHandle(null), 0);
            mouseHookReady = mouseHook == IntPtr.Zero ? -Marshal.GetLastWin32Error() : 1;
            try
            {
                BeginInvoke((MethodInvoker)UpdateDiagnosticTitle);
            }
            catch (InvalidOperationException) { }
            NativeMethods.MSG message;
            while (!mouseHookStopping &&
                NativeMethods.GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(ref message);
                NativeMethods.DispatchMessage(ref message);
            }
            if (mouseHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(mouseHook);
                mouseHook = IntPtr.Zero;
            }
        }

        private string TaskIdAtPoint(NativeMethods.POINT point)
        {
            try
            {
                AutomationElement element = AutomationElement.FromPoint(
                    new System.Windows.Point(point.X, point.Y));
                for (int depth = 0; element != null && depth < 8; depth++)
                {
                    string name = element.Current.Name;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        string threadId = catalog.FindId(name);
                        if (!string.IsNullOrWhiteSpace(threadId)) return threadId;
                    }
                    element = TreeWalker.ControlViewWalker.GetParent(element);
                }
            }
            catch { }
            return null;
        }

        internal static int PointDistanceSquared(NativeMethods.POINT left, NativeMethods.POINT right)
        {
            int dx = left.X - right.X;
            int dy = left.Y - right.Y;
            return dx * dx + dy * dy;
        }

        private void QueueCodexClick(NativeMethods.POINT point)
        {
            int version = Interlocked.Increment(ref clickLookupVersion);
            string cached = null;
            lock (hoverLookupLock)
            {
                if (!string.IsNullOrWhiteSpace(resolvedHoverThreadId) &&
                    (DateTime.UtcNow - resolvedHoverAt).TotalMilliseconds <= 700 &&
                    PointDistanceSquared(point, resolvedHoverPoint) <= 24 * 24)
                    cached = resolvedHoverThreadId;
            }

            if (!string.IsNullOrWhiteSpace(cached))
            {
                PostResolvedClick(version, cached);
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                string threadId = TaskIdAtPoint(point);
                PostResolvedClick(version, threadId);
            });
        }

        private void PostResolvedClick(int version, string threadId)
        {
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (version != clickLookupVersion || !lastForegroundIsCodex ||
                        string.IsNullOrWhiteSpace(threadId)) return;
                    directThreadId = threadId;
                    directThreadUntil = DateTime.UtcNow.AddMilliseconds(220);
                    ApplyThreadId(threadId);
                });
            }
            catch (InvalidOperationException) { }
        }

        private void QueuePreloadTaskAtPoint(NativeMethods.POINT point)
        {
            lock (hoverLookupLock)
            {
                pendingHoverLookupPoint = point;
                pendingHoverLookupVersion++;
                if (hoverLookupWorkerActive) return;
                hoverLookupWorkerActive = true;
            }
            ThreadPool.QueueUserWorkItem(delegate { HoverLookupWorker(); });
        }

        private void HoverLookupWorker()
        {
            while (true)
            {
                NativeMethods.POINT point;
                int version;
                lock (hoverLookupLock)
                {
                    point = pendingHoverLookupPoint;
                    version = pendingHoverLookupVersion;
                }

                string threadId = TaskIdAtPoint(point);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        bool current;
                        lock (hoverLookupLock)
                        {
                            current = version == pendingHoverLookupVersion;
                            if (current)
                            {
                                resolvedHoverPoint = point;
                                resolvedHoverThreadId = threadId;
                                resolvedHoverAt = DateTime.UtcNow;
                            }
                        }
                        if (current && !string.IsNullOrWhiteSpace(threadId))
                            PreloadThread(threadId);
                    });
                }
                catch (InvalidOperationException) { }

                lock (hoverLookupLock)
                {
                    if (version == pendingHoverLookupVersion)
                    {
                        hoverLookupWorkerActive = false;
                        return;
                    }
                }
            }
        }

        private void PreloadThread(string threadId)
        {
            if (readerCache.ContainsKey(threadId) || preloadPending.Contains(threadId)) return;
            preloadPending.Add(threadId);
            string focused;
            sessionPathCache.TryGetValue(threadId, out focused);
            ThreadPool.QueueUserWorkItem(delegate
            {
                if (focused == null) focused = SessionReader.FindByThreadId(sessionsRoot, threadId);
                SessionReader loaded;
                if (focused == null || !sessionCache.TryRestore(focused, out loaded))
                {
                    loaded = new SessionReader();
                    if (focused != null) loaded.Load(focused);
                }
                sessionCache.Update(loaded);
                sessionCache.Save();
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (focused != null) sessionPathCache[threadId] = focused;
                        if (!readerCache.ContainsKey(threadId)) readerCache[threadId] = loaded;
                        preloadPending.Remove(threadId);
                    });
                }
                catch (InvalidOperationException) { }
            });
        }

        private void UpdateDiagnosticTitle()
        {
            int motionFrames;
            double motionMin;
            double motionMax;
            int timingSequence;
            string timingSource;
            long timingPointer;
            long timingPosition;
            long timingStart;
            long timingFirstHudFrame;
            long timingFirstAnchorFrame;
            lock (nativeMotionLock)
            {
                motionFrames = nativeMotionRenderedFrames;
                motionMin = nativeMotionMinFrameMs;
                motionMax = nativeMotionMaxFrameMs;
            }
            lock (motionTimingLock)
            {
                timingSequence = motionTimingSequence;
                timingSource = motionTimingSource;
                timingPointer = motionTimingPointerTimestamp;
                timingPosition = motionTimingPositionTimestamp;
                timingStart = motionTimingStartTimestamp;
                timingFirstHudFrame = motionTimingFirstHudFrameTimestamp;
                timingFirstAnchorFrame = motionTimingFirstAnchorFrameTimestamp;
            }
            Text = string.Format(
                "CodexContextHUD thread={0} compressions={1} context={2} quota={3} hook={4} ups={5} sidebarHits={6} motionFrames={7} frameMs={8:0.0}-{9:0.0} timing={10}:{11} queueMs={12:0.0} startMs={13:0.0} hudFirstMs={14:0.0} anchorEventMs={15:0.0}",
                currentThreadId ?? "none", reader.Compressions, reader.ContextPercent,
                Volatile.Read(ref observedQuotaPercent),
                mouseHookReady, Interlocked.CompareExchange(ref mouseUpCount, 0, 0),
                Interlocked.CompareExchange(ref sidebarPointerHitCount, 0, 0),
                motionFrames, motionMin, motionMax, timingSequence, timingSource,
                TimingElapsedMs(timingPointer, timingPosition),
                TimingElapsedMs(timingPointer, timingStart),
                TimingElapsedMs(timingPointer, timingFirstHudFrame),
                TimingElapsedMs(timingPointer, timingFirstAnchorFrame));
        }

        private void BeginMotionTiming(string source, long pointerTimestamp)
        {
            lock (motionTimingLock)
            {
                unchecked { motionTimingSequence++; }
                motionTimingSource = source;
                motionTimingPointerTimestamp = pointerTimestamp;
                motionTimingPositionTimestamp = 0;
                motionTimingStartTimestamp = 0;
                motionTimingFirstHudFrameTimestamp = 0;
                motionTimingFirstAnchorFrameTimestamp = 0;
            }
        }

        private void MarkMotionTimingPosition()
        {
            lock (motionTimingLock)
                if (motionTimingPointerTimestamp > 0 && motionTimingPositionTimestamp == 0)
                    motionTimingPositionTimestamp = Stopwatch.GetTimestamp();
        }

        private void MarkMotionTimingStart()
        {
            lock (motionTimingLock)
                if (motionTimingPointerTimestamp > 0 && motionTimingStartTimestamp == 0)
                    motionTimingStartTimestamp = Stopwatch.GetTimestamp();
        }

        private void MarkMotionTimingFirstHudFrame(long timestamp)
        {
            lock (motionTimingLock)
                if (motionTimingPointerTimestamp > 0 && motionTimingFirstHudFrameTimestamp == 0)
                    motionTimingFirstHudFrameTimestamp = timestamp;
        }

        private void MarkMotionTimingFirstAnchorFrame(long timestamp)
        {
            lock (motionTimingLock)
                if (motionTimingPointerTimestamp > 0 && motionTimingFirstAnchorFrameTimestamp == 0)
                    motionTimingFirstAnchorFrameTimestamp = timestamp;
        }

        internal static double TimingElapsedMs(long started, long ended)
        {
            if (started <= 0 || ended <= 0 || ended < started) return -1;
            return (ended - started) * 1000.0 / Stopwatch.Frequency;
        }

        private void AnimateIfChanged(bool force = false)
        {
            if (nativeMotionActive)
            {
                animationDeferredForNativeMotion = true;
                animationDeferredForce |= force;
                return;
            }
            bool quotaChanged = SyncQuotaFromReader(reader);
            if (!force && reader.Compressions == observedCompressions &&
                reader.ContextPercent == observedContext)
            {
                UpdateDiagnosticTitle();
                if (quotaChanged) Invalidate();
                return;
            }
            int previousCompressions = observedCompressions;
            animationFromContext = displayedContext < 0 ? reader.ContextPercent : displayedContext;
            observedCompressions = reader.Compressions;
            observedContext = reader.ContextPercent;
            UpdateDiagnosticTitle();
            animationStarted = DateTime.UtcNow;
            switchAnimation = force;
            compressionBump = !force && observedCompressions > previousCompressions;
            animationDurationMs = force ? 910 : compressionBump ? 900 : 800;
            animationSpin = 0f;
            animationIconScale = 1f;
            animationActive = true;
            Invalidate();
        }

        private bool SyncQuotaFromReader(SessionReader source)
        {
            if (source == null || source.RemainingQuotaPercent < 0) return false;
            int current = Volatile.Read(ref observedQuotaPercent);
            long resetAt = source.QuotaResetsAt;
            bool newerWindow = resetAt > 0 && resetAt > observedQuotaResetsAt;
            bool sameWindowIsLower = resetAt == observedQuotaResetsAt &&
                (current < 0 || source.RemainingQuotaPercent < current);
            if (!newerWindow && !sameWindowIsLower) return false;
            observedQuotaResetsAt = resetAt;
            Interlocked.Exchange(ref observedQuotaPercent, source.RemainingQuotaPercent);
            return true;
        }

        private void Animate(object sender, EventArgs e)
        {
            if (nativeMotionActive)
            {
                PauseContentAnimationForNativeMotion();
                return;
            }
            double elapsed = (DateTime.UtcNow - animationStarted).TotalMilliseconds;
            double progress = Math.Min(1.0, elapsed / animationDurationMs);
            if (switchAnimation)
            {
                if (elapsed < 320)
                {
                    double eased = CubicBezier(elapsed / 320.0, .4, 0, .6, 1);
                    displayedContext = animationFromContext * (float)(1 - eased);
                }
                else if (elapsed < 390)
                    displayedContext = 0;
                else
                {
                    double eased = CubicBezier(Math.Min(1, (elapsed - 390) / 420.0),
                        .18, .85, .28, 1);
                    displayedContext = observedContext < 0 ? -1f : observedContext * (float)eased;
                }

                double icon = CubicBezier(Math.Min(1, elapsed / 760.0), .2, .8, .2, 1);
                if (icon <= .58)
                {
                    float local = (float)(icon / .58);
                    animationSpin = 250f * local;
                    animationIconScale = 1f + .1f * local;
                }
                else
                {
                    float local = (float)((icon - .58) / .42);
                    animationSpin = 250f + 110f * local;
                    animationIconScale = 1.1f - .1f * local;
                }
            }
            else
            {
                double eased = CubicBezier(progress, .2, .8, .2, 1);
                displayedContext = observedContext < 0 ? -1f :
                    animationFromContext + (observedContext - animationFromContext) * (float)eased;
                if (compressionBump)
                {
                    if (eased <= .38)
                    {
                        float local = (float)(eased / .38);
                        animationSpin = 110f * local;
                        animationIconScale = 1f - .7f * local;
                    }
                    else if (eased <= .72)
                    {
                        float local = (float)((eased - .38) / .34);
                        animationSpin = 110f + 150f * local;
                        animationIconScale = .3f + .9f * local;
                    }
                    else
                    {
                        float local = (float)((eased - .72) / .28);
                        animationSpin = 260f + 100f * local;
                        animationIconScale = 1.2f - .2f * local;
                    }
                }
            }
            Invalidate();
            if (progress >= 1.0)
            {
                displayedContext = observedContext;
                animationSpin = 0f;
                animationIconScale = 1f;
                switchAnimation = false;
                compressionBump = false;
                animationActive = false;
            }
        }

        private double ContentAnimationElapsedMs()
        {
            if (animationPausedForNativeMotion) return animationPausedElapsedMs;
            return (DateTime.UtcNow - animationStarted).TotalMilliseconds;
        }

        private void PauseContentAnimationForNativeMotion()
        {
            if (!animationActive || animationPausedForNativeMotion) return;
            animationPauseStartedUtc = DateTime.UtcNow;
            animationPausedElapsedMs = Math.Max(0,
                (animationPauseStartedUtc - animationStarted).TotalMilliseconds);
            animationPausedForNativeMotion = true;
        }

        private void ResumeContentAnimationAfterNativeMotion()
        {
            if (animationPausedForNativeMotion)
            {
                animationStarted = animationStarted.Add(
                    DateTime.UtcNow - animationPauseStartedUtc);
                animationPausedForNativeMotion = false;
                animationPausedElapsedMs = 0;
            }

            if (animationDeferredForNativeMotion)
            {
                bool force = animationDeferredForce;
                animationDeferredForNativeMotion = false;
                animationDeferredForce = false;
                AnimateIfChanged(force);
            }
        }

        private void PositionBesideCodex()
        {
            MarkMotionTimingPosition();
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                Interlocked.Exchange(ref requestedAnchorWindow, 0);
                CancelNativeMotion();
                Hide();
                return;
            }

            uint pid;
            NativeMethods.GetWindowThreadProcessId(foreground, out pid);
            if (pid != lastForegroundPid)
            {
                lastForegroundPid = pid;
                try
                {
                    Process process = Process.GetProcessById((int)pid);
                    lastForegroundIsCodex = process.ProcessName.Equals(
                        "ChatGPT", StringComparison.OrdinalIgnoreCase);
                }
                catch { lastForegroundIsCodex = false; }
            }
            if (!lastForegroundIsCodex)
            {
                Interlocked.Exchange(ref requestedAnchorWindow, 0);
                CancelNativeMotion();
                Hide();
                return;
            }

            IntPtr codexWindow = foreground;
            uint ownerPid;
            if (ownerWindow != IntPtr.Zero &&
                NativeMethods.IsWindow(ownerWindow) &&
                NativeMethods.IsWindowVisible(ownerWindow))
            {
                NativeMethods.GetWindowThreadProcessId(ownerWindow, out ownerPid);
                if (ownerPid == pid) codexWindow = ownerWindow;
            }
            else
            {
                IntPtr rootOwner = NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOTOWNER);
                if (rootOwner != IntPtr.Zero) codexWindow = rootOwner;
            }

            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(codexWindow, out rect) || rect.Right <= rect.Left)
            {
                Interlocked.Exchange(ref requestedAnchorWindow, 0);
                CancelNativeMotion();
                Hide();
                return;
            }

            bool wasHidden = !Visible;
            bool ownerChanged = ownerWindow != codexWindow;
            if (wasHidden)
            {
                Show();
                AnimateIfChanged(true);
            }
            if (wasHidden || ownerWindow != codexWindow)
            {
                NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWLP_HWNDPARENT, codexWindow);
                ownerWindow = codexWindow;
            }

            NativeMethods.RECT hudRect;
            int hudWidth = Width;
            int hudHeight = Height;
            if (NativeMethods.GetWindowRect(Handle, out hudRect) && hudRect.Right > hudRect.Left)
            {
                hudWidth = hudRect.Right - hudRect.Left;
                hudHeight = hudRect.Bottom - hudRect.Top;
            }
            lock (nativeMotionLock)
            {
                if (nativeCanvasActive && nativeCanvasBaseWidth > 0)
                {
                    hudWidth = nativeCanvasBaseWidth;
                    hudHeight = nativeCanvasBaseHeight;
                }
            }
            int x = FixedHudX(rect.Left, rect.Right - rect.Left);
            int y = BottomAnchoredY(rect.Bottom, hudHeight, 29);
            Rectangle currentAnchor;
            bool currentAnchorIsPermission;
            bool recentAnchorEvent;
            double anchorEventGapMs;
            DateTime anchorEventUtc;
            UpdateToolbarAnchor(codexWindow, out currentAnchor,
                out currentAnchorIsPermission, out recentAnchorEvent,
                out anchorEventGapMs, out anchorEventUtc);
            if (!currentAnchor.IsEmpty)
            {
                if (currentAnchorIsPermission)
                {
                    x = currentAnchor.Right + 12;
                    y = currentAnchor.Top + (currentAnchor.Height - hudHeight) / 2;
                }
                else
                {
                    x = currentAnchor.Left + 155;
                    y = currentAnchor.Bottom + 5;
                }
            }
            x = Clamp(x, rect.Left, Math.Max(rect.Left, rect.Right - hudWidth));
            y = Clamp(y, rect.Top, Math.Max(rect.Top, rect.Bottom - hudHeight));

            DateTime frameNow = DateTime.UtcNow;
            double frameMs = Math.Max(1, Math.Min(40, (frameNow - lastPositionFrame).TotalMilliseconds));
            lastPositionFrame = frameNow;
            int transitionVersion;
            bool transitionOpening;
            double observedSidebarWidth;
            long transitionStartedTimestamp;
            GetSidebarMotionState(out transitionVersion, out transitionOpening,
                out observedSidebarWidth, out transitionStartedTimestamp);
            bool positionOwnedByDwmThread;
            if (wasHidden || ownerChanged ||
                double.IsNaN(renderedHudX) || double.IsNaN(renderedHudY))
            {
                CancelNativeMotion();
                lock (nativeMotionLock) renderedHudX = x;
                renderedHudY = y;
                observedSidebarTransitionVersion = transitionVersion;
                positionOwnedByDwmThread = false;
            }
            else
            {
                if (transitionVersion != observedSidebarTransitionVersion)
                {
                    observedSidebarTransitionVersion = transitionVersion;
                    double width = observedSidebarWidth > 100
                        ? observedSidebarWidth : DefaultSidebarWidth(codexWindow);
                    double currentRenderedX;
                    lock (nativeMotionLock) currentRenderedX = renderedHudX;
                    double finalX = SidebarMotionTarget(
                        currentRenderedX, width, transitionOpening);
                    finalX = Clamp((int)Math.Round(finalX), rect.Left,
                        Math.Max(rect.Left, rect.Right - hudWidth));
                    if (width > 100)
                        StartNativeMotion(Handle, currentRenderedX, finalX, y,
                            hudWidth, hudHeight, transitionStartedTimestamp);
                }

                if (nativeMotionActive && nativeMotionPumpReady <= 0)
                    AdvanceNativeMotionFrame();
                positionOwnedByDwmThread = nativeCanvasActive;
                if (!positionOwnedByDwmThread)
                {
                    lock (nativeMotionLock)
                        renderedHudX = FollowLinear(renderedHudX, x, frameMs, 9000.0);
                }
                renderedHudY = FollowLinear(renderedHudY, y, frameMs, 6000.0);
            }

            double displayRenderedX;
            lock (nativeMotionLock) displayRenderedX = renderedHudX;
            int displayX = Clamp((int)Math.Round(displayRenderedX), rect.Left,
                Math.Max(rect.Left, rect.Right - hudWidth));
            int displayY = Clamp((int)Math.Round(renderedHudY), rect.Top,
                Math.Max(rect.Top, rect.Bottom - hudHeight));

            if (!positionOwnedByDwmThread)
                NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOP, displayX, displayY, 0, 0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }

        private void StartNativeMotion(IntPtr window, double startX, double finalX,
            int y, int baseWidth, int baseHeight, long sourceTimestamp)
        {
            MarkMotionTimingStart();
            int start = (int)Math.Round(startX);
            int final = (int)Math.Round(finalX);
            int canvasLeft = Math.Min(start, final);
            int travel = Math.Abs(final - start);
            int canvasWidth = baseWidth + travel;
            lock (nativeMotionLock)
            {
                unchecked { nativeMotionGeneration++; }
                nativeMotionWindow = window;
                nativeMotionStartX = startX;
                nativeMotionFinalX = finalX;
                nativeMotionY = y;
                nativeCanvasLeft = canvasLeft;
                nativeCanvasWidth = canvasWidth;
                nativeCanvasBaseWidth = baseWidth;
                nativeCanvasBaseHeight = baseHeight;
                nativeMotionStartOffsetX = startX - canvasLeft;
                nativeMotionFinalOffsetX = finalX - canvasLeft;
                contentOffsetX = nativeMotionStartOffsetX;
                nativeMotionStartedTimestamp = sourceTimestamp > 0
                    ? sourceTimestamp : Stopwatch.GetTimestamp();
                nativeMotionRenderedFrames = 0;
                nativeMotionLastFrameTimestamp = 0;
                nativeMotionMinFrameMs = 0;
                nativeMotionMaxFrameMs = 0;
                nativeCanvasActive = true;
                nativeMotionActive = true;
            }
            PauseContentAnimationForNativeMotion();
            // Resize/reposition the transparent surface once. The visible pixels stay
            // at startX because contentOffsetX compensates for the new canvas origin.
            NativeMethods.SetWindowPos(window, NativeMethods.HWND_TOP,
                canvasLeft, y, canvasWidth, baseHeight,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            ApplyWindowRegion(canvasWidth, baseHeight);
            Invalidate();
            Update();
            nativeMotionWake.Set();
        }

        private void NativeMotionWorker()
        {
            nativeMotionPumpReady = 1;
            while (!nativeMotionStopping)
            {
                nativeMotionWake.WaitOne();
                while (!nativeMotionStopping && nativeMotionActive)
                {
                    if (IsHandleCreated && !IsDisposed &&
                        Interlocked.Exchange(ref nativeMotionFrameQueued, 1) == 0)
                    {
                        if (!NativeMethods.PostMessage(Handle,
                            NativeMethods.WM_APP_NATIVE_MOTION_FRAME,
                            IntPtr.Zero, IntPtr.Zero))
                            Interlocked.Exchange(ref nativeMotionFrameQueued, 0);
                    }
                    // This is the compositor's clock, not a guessed 16ms sleep.
                    // Posting before the flush gives the UI thread the current
                    // vertical-blank interval to paint the next frame.
                    if (NativeMethods.DwmFlush() != 0) Thread.Sleep(8);
                }
            }
            nativeMotionPumpReady = 0;
        }

        private void CancelNativeMotion()
        {
            bool restoreCanvas;
            IntPtr window;
            int x;
            int y;
            int width;
            int height;
            lock (nativeMotionLock)
            {
                unchecked { nativeMotionGeneration++; }
                restoreCanvas = nativeCanvasActive;
                window = nativeMotionWindow;
                x = (int)Math.Round(renderedHudX);
                y = nativeMotionY;
                width = nativeCanvasBaseWidth;
                height = nativeCanvasBaseHeight;
                nativeMotionActive = false;
                nativeCanvasActive = false;
                contentOffsetX = 0;
            }
            if (restoreCanvas && window != IntPtr.Zero && width > 0 && height > 0 &&
                IsHandleCreated && !IsDisposed)
            {
                NativeMethods.SetWindowPos(window, NativeMethods.HWND_TOP,
                    x, y, width, height, NativeMethods.SWP_NOACTIVATE);
                ApplyWindowRegion(width, height);
                Invalidate();
            }
            ResumeContentAnimationAfterNativeMotion();
        }

        private void ApplyNativeMotionFrame(int generation, double globalX,
            double localOffsetX, bool complete)
        {
            IntPtr window = IntPtr.Zero;
            int finalX = 0;
            int y = 0;
            int width = 0;
            int height = 0;
            lock (nativeMotionLock)
            {
                if (generation != nativeMotionGeneration || !nativeCanvasActive) return;
                renderedHudX = globalX;
                contentOffsetX = localOffsetX;
                if (complete)
                {
                    window = nativeMotionWindow;
                    finalX = (int)Math.Round(nativeMotionFinalX);
                    y = nativeMotionY;
                    width = nativeCanvasBaseWidth;
                    height = nativeCanvasBaseHeight;
                    renderedHudX = nativeMotionFinalX;
                    contentOffsetX = 0;
                    nativeMotionActive = false;
                    nativeCanvasActive = false;
                }
            }

            if (complete)
            {
                // Collapse the transparent canvas at the exact final visual position.
                NativeMethods.SetWindowPos(window, NativeMethods.HWND_TOP,
                    finalX, y, width, height,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
                ApplyWindowRegion(width, height);
                UpdateDiagnosticTitle();
                ResumeContentAnimationAfterNativeMotion();
            }
            Invalidate();
            Update();
        }

        private void AdvanceNativeMotionFrame()
        {
            int generation;
            double startX;
            double finalX;
            double startOffsetX;
            double finalOffsetX;
            long started;
            lock (nativeMotionLock)
            {
                if (!nativeMotionActive) return;
                long frameTimestamp = Stopwatch.GetTimestamp();
                if (nativeMotionLastFrameTimestamp != 0)
                {
                    double frameMs = (frameTimestamp - nativeMotionLastFrameTimestamp) *
                        1000.0 / Stopwatch.Frequency;
                    if (nativeMotionMinFrameMs <= 0 || frameMs < nativeMotionMinFrameMs)
                        nativeMotionMinFrameMs = frameMs;
                    if (frameMs > nativeMotionMaxFrameMs)
                        nativeMotionMaxFrameMs = frameMs;
                }
                nativeMotionLastFrameTimestamp = frameTimestamp;
                nativeMotionRenderedFrames++;
                if (nativeMotionRenderedFrames == 1)
                    MarkMotionTimingFirstHudFrame(frameTimestamp);
                generation = nativeMotionGeneration;
                startX = nativeMotionStartX;
                finalX = nativeMotionFinalX;
                startOffsetX = nativeMotionStartOffsetX;
                finalOffsetX = nativeMotionFinalOffsetX;
                started = nativeMotionStartedTimestamp;
            }

            double elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000.0 /
                Stopwatch.Frequency;
            double progress = SpatialMotionProgress(elapsedMs);
            double nextX = startX + (finalX - startX) * progress;
            double nextOffsetX = startOffsetX +
                (finalOffsetX - startOffsetX) * progress;
            ApplyNativeMotionFrame(generation, nextX, nextOffsetX,
                elapsedMs >= 500);
        }

        internal static double FollowLinear(double current, double target, double frameMs,
            double maxPixelsPerSecond)
        {
            double delta = target - current;
            if (Math.Abs(delta) < .45) return target;
            double limit = maxPixelsPerSecond * frameMs / 1000.0;
            if (Math.Abs(delta) <= limit) return target;
            return current + Math.Sign(delta) * limit;
        }

        internal static double SpatialMotionProgress(double elapsedMs)
        {
            if (elapsedMs <= 0) return 0;
            if (elapsedMs >= 500) return 1;
            // One non-physical curve is shared by both directions; only the signed
            // distance changes. It continuously decelerates to the target and has no
            // spring force, overshoot, reversal or settling phase.
            double progress = elapsedMs / 500.0;
            double remaining = 1 - progress;
            return 1 - remaining * remaining * remaining * remaining;
        }

        internal static int SidebarStateFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return -1;
            if (name.IndexOf("隐藏边栏", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("隐藏侧边栏", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Hide sidebar", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            if (name.IndexOf("显示边栏", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("显示侧边栏", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Show sidebar", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
            return -1;
        }

        internal static double SidebarMotionTarget(double startX, double sidebarWidth,
            bool opening)
        {
            return startX + (opening ? 1 : -1) * sidebarWidth / 2.0;
        }

        private static double DefaultSidebarWidth(IntPtr codexWindow)
        {
            try
            {
                uint dpi = NativeMethods.GetDpiForWindow(codexWindow);
                if (dpi >= 96 && dpi <= 768) return 275.0 * dpi / 96.0;
            }
            catch { }
            return 275;
        }

        private static double DefaultRightPanelMotionWidth(IntPtr codexWindow)
        {
            // UIA and GetWindowRect both report physical pixels. The observed
            // composer travel is already DPI-scaled, so do not scale it twice.
            return 1040;
        }

        internal static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        internal static int BottomAnchoredY(int ownerBottom, int hudHeight, int bottomOffset)
        {
            return ownerBottom - hudHeight - bottomOffset;
        }

        internal static int FixedHudX(int ownerLeft, int ownerWidth)
        {
            return ownerLeft + ownerWidth * 21 / 100;
        }

        internal static double CubicBezier(double progress,
            double x1, double y1, double x2, double y2)
        {
            progress = Math.Max(0, Math.Min(1, progress));
            double value = progress;
            for (int i = 0; i < 6; i++)
            {
                double inverse = 1 - value;
                double x = 3 * inverse * inverse * value * x1 +
                    3 * inverse * value * value * x2 + value * value * value;
                double derivative = 3 * inverse * inverse * x1 +
                    6 * inverse * value * (x2 - x1) +
                    3 * value * value * (1 - x2);
                if (Math.Abs(derivative) < .000001) break;
                value = Math.Max(0, Math.Min(1, value - (x - progress) / derivative));
            }
            double remaining = 1 - value;
            return 3 * remaining * remaining * value * y1 +
                3 * remaining * value * value * y2 + value * value * value;
        }

        private void UpdateToolbarAnchor(IntPtr codexWindow,
            out Rectangle currentRect, out bool currentIsPermission,
            out bool recentAnchorEvent, out double eventGapMs,
            out DateTime eventUtc)
        {
            Interlocked.Exchange(ref requestedAnchorWindow, codexWindow.ToInt64());
            lock (anchorStateLock)
            {
                currentRect = anchorRect;
                currentIsPermission = anchorIsPermission;
                recentAnchorEvent =
                    (DateTime.UtcNow - anchorLastEventUtc).TotalMilliseconds <= 70;
                eventGapMs = anchorLastGapMs;
                eventUtc = anchorLastEventUtc;
            }
        }

        private void GetSidebarMotionState(out int version, out bool opening,
            out double width, out long startedTimestamp)
        {
            lock (anchorStateLock)
            {
                version = sidebarTransitionVersion;
                opening = sidebarTransitionOpening;
                width = sidebarTransitionWidthPx;
                startedTimestamp = sidebarTransitionStartedTimestamp;
            }
        }

        private void AnchorBoundsChanged(object sender, AutomationPropertyChangedEventArgs e)
        {
            if (e.Property != AutomationElement.BoundingRectangleProperty ||
                !(e.NewValue is System.Windows.Rect)) return;
            var bounds = (System.Windows.Rect)e.NewValue;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            MarkMotionTimingFirstAnchorFrame(Stopwatch.GetTimestamp());
            Rectangle published = Rectangle.FromLTRB(
                (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
                (int)Math.Round(bounds.Right), (int)Math.Round(bounds.Bottom));
            DateTime now = DateTime.UtcNow;
            lock (anchorStateLock)
            {
                double gap = (now - anchorLastEventUtc).TotalMilliseconds;
                anchorLastEventUtc = now;
                if (gap > 0) anchorLastGapMs = Math.Min(10000, gap);
                anchorRect = published;
                anchorIsPermission = anchorSubscriptionIsPermission;
                if (rightPanelLearningPending)
                {
                    double learned = Math.Abs(published.Left -
                        rightPanelLearningStartAnchorX) * 2.0;
                    if (learned >= 100)
                        rightPanelMotionWidthPx = learned;
                    if ((now - sidebarTransitionStartedUtc).TotalMilliseconds >= 480)
                        rightPanelLearningPending = false;
                }
            }
        }

        private void SidebarNameChanged(object sender, AutomationPropertyChangedEventArgs e)
        {
            if (e.Property == AutomationElement.NameProperty)
                PublishSidebarState(e.NewValue as string, true);
            else if (e.Property == AutomationElement.BoundingRectangleProperty &&
                e.NewValue is System.Windows.Rect)
                PublishSidebarToggleBounds((System.Windows.Rect)e.NewValue);
        }

        private void PublishSidebarToggleBounds(System.Windows.Rect bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            Rectangle published = Rectangle.FromLTRB(
                (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
                (int)Math.Round(bounds.Right), (int)Math.Round(bounds.Bottom));
            lock (anchorStateLock) sidebarToggleRect = published;
        }

        private void RightPanelToggleChanged(object sender,
            AutomationPropertyChangedEventArgs e)
        {
            if (e.Property == TogglePattern.ToggleStateProperty &&
                e.NewValue is ToggleState)
                PublishRightPanelState((ToggleState)e.NewValue, true);
            else if (e.Property == AutomationElement.BoundingRectangleProperty &&
                e.NewValue is System.Windows.Rect)
                PublishRightPanelToggleBounds((System.Windows.Rect)e.NewValue);
        }

        private void PublishRightPanelToggleBounds(System.Windows.Rect bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            Rectangle published = Rectangle.FromLTRB(
                (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
                (int)Math.Round(bounds.Right), (int)Math.Round(bounds.Bottom));
            lock (anchorStateLock) rightPanelToggleRect = published;
        }

        private bool PublishRightPanelPointerTrigger(NativeMethods.POINT point)
        {
            long timestamp = Stopwatch.GetTimestamp();
            DateTime now = DateTime.UtcNow;
            bool nativeSlotHit = false;
            IntPtr codexWindow = new IntPtr(Interlocked.Read(ref requestedAnchorWindow));
            NativeMethods.RECT windowRect;
            if (codexWindow != IntPtr.Zero &&
                NativeMethods.GetWindowRect(codexWindow, out windowRect))
            {
                nativeSlotHit = point.X >= windowRect.Right - 60 &&
                    point.X <= windowRect.Right - 4 &&
                    point.Y >= windowRect.Top + 44 &&
                    point.Y <= windowRect.Top + 106;
            }

            lock (anchorStateLock)
            {
                bool automationHit = !rightPanelToggleRect.IsEmpty &&
                    rightPanelToggleRect.Contains(point.X, point.Y);
                if (!rightPanelStateKnown || (!automationHit && !nativeSlotHit))
                    return false;

                Interlocked.Increment(ref sidebarPointerHitCount);
                BeginMotionTiming("right", timestamp);
                bool nextOpen = !rightPanelOpen;
                rightPanelOpen = nextOpen;
                rightPanelPredictionPending = true;
                rightPanelPredictionExpectedOpen = nextOpen;
                rightPanelPredictionTimestamp = timestamp;
                rightPanelLearningPending = !anchorRect.IsEmpty;
                rightPanelLearningStartAnchorX = anchorRect.Left;

                // The main composer moves opposite the right panel. Its center shifts
                // by half of the panel width, so reuse the same shell spring with the
                // direction reversed. The first run uses the current Codex default;
                // subsequent runs learn the exact resized width from the settled anchor.
                sidebarTransitionOpening = !nextOpen;
                sidebarTransitionWidthPx = rightPanelMotionWidthPx >= 100
                    ? rightPanelMotionWidthPx : DefaultRightPanelMotionWidth(codexWindow);
                sidebarTransitionStartedUtc = now;
                sidebarTransitionStartedTimestamp = timestamp;
                unchecked { sidebarTransitionVersion++; }
            }
            try { BeginInvoke((MethodInvoker)PositionBesideCodex); }
            catch (InvalidOperationException) { }
            return true;
        }

        private void PublishRightPanelState(ToggleState state, bool isTransitionEvent)
        {
            bool nextOpen = state == ToggleState.On;
            DateTime now = DateTime.UtcNow;
            lock (anchorStateLock)
            {
                if (isTransitionEvent && rightPanelPredictionPending)
                {
                    bool fresh = (Stopwatch.GetTimestamp() -
                        rightPanelPredictionTimestamp) * 1000.0 /
                        Stopwatch.Frequency <= 1500;
                    if (fresh && nextOpen == rightPanelPredictionExpectedOpen)
                    {
                        rightPanelPredictionPending = false;
                        rightPanelOpen = nextOpen;
                        rightPanelStateKnown = true;
                        return;
                    }
                    rightPanelPredictionPending = false;
                }
                bool changed = rightPanelStateKnown && rightPanelOpen != nextOpen;
                rightPanelStateKnown = true;
                rightPanelOpen = nextOpen;
                if (isTransitionEvent && changed)
                {
                    sidebarTransitionOpening = !nextOpen;
                    sidebarTransitionWidthPx = rightPanelMotionWidthPx >= 100
                        ? rightPanelMotionWidthPx : 1040;
                    sidebarTransitionStartedUtc = now;
                    sidebarTransitionStartedTimestamp = Stopwatch.GetTimestamp();
                    unchecked { sidebarTransitionVersion++; }
                }
            }
        }

        private void PublishSidebarPointerTrigger(NativeMethods.POINT point)
        {
            long timestamp = Stopwatch.GetTimestamp();
            DateTime now = DateTime.UtcNow;
            bool nativeSlotHit = false;
            IntPtr codexWindow = new IntPtr(Interlocked.Read(ref requestedAnchorWindow));
            NativeMethods.RECT windowRect;
            if (codexWindow != IntPtr.Zero &&
                NativeMethods.GetWindowRect(codexWindow, out windowRect))
            {
                nativeSlotHit = point.X >= windowRect.Left + 8 &&
                    point.X <= windowRect.Left + 54 &&
                    point.Y >= windowRect.Top + 8 &&
                    point.Y <= windowRect.Top + 50;
            }
            lock (anchorStateLock)
            {
                bool automationHit = !sidebarToggleRect.IsEmpty &&
                    sidebarToggleRect.Contains(point.X, point.Y);
                if (!sidebarStateKnown || (!automationHit && !nativeSlotHit)) return;
                Interlocked.Increment(ref sidebarPointerHitCount);
                BeginMotionTiming("left", timestamp);

                bool nextOpen = !sidebarOpen;
                sidebarOpen = nextOpen;
                sidebarPredictionPending = true;
                sidebarPredictionExpectedOpen = nextOpen;
                sidebarPredictionTimestamp = timestamp;
                sidebarTransitionOpening = nextOpen;
                sidebarTransitionWidthPx = sidebarWidthPx;
                sidebarTransitionStartedUtc = now;
                sidebarTransitionStartedTimestamp = timestamp;
                unchecked { sidebarTransitionVersion++; }
            }
            try
            {
                BeginInvoke((MethodInvoker)PositionBesideCodex);
            }
            catch (InvalidOperationException) { }
        }

        private void PublishSidebarState(string name, bool isTransitionEvent)
        {
            int state = SidebarStateFromName(name);
            if (state < 0) return;
            DateTime now = DateTime.UtcNow;
            lock (anchorStateLock)
            {
                bool nextOpen = state == 1;
                if (isTransitionEvent && sidebarPredictionPending)
                {
                    bool predictionIsFresh = (Stopwatch.GetTimestamp() -
                        sidebarPredictionTimestamp) * 1000.0 /
                        Stopwatch.Frequency <= 1500;
                    if (predictionIsFresh && nextOpen == sidebarPredictionExpectedOpen)
                    {
                        sidebarPredictionPending = false;
                        sidebarOpen = nextOpen;
                        sidebarStateKnown = true;
                        return;
                    }
                    sidebarPredictionPending = false;
                }
                bool changed = sidebarStateKnown && sidebarOpen != nextOpen;
                sidebarStateKnown = true;
                sidebarOpen = nextOpen;
                if (isTransitionEvent && changed)
                {
                    sidebarTransitionOpening = nextOpen;
                    sidebarTransitionWidthPx = sidebarWidthPx;
                    sidebarTransitionStartedUtc = now;
                    sidebarTransitionStartedTimestamp = Stopwatch.GetTimestamp();
                    unchecked { sidebarTransitionVersion++; }
                }
            }
        }

        private static AutomationElement FindSidebarToggle(AutomationElement root)
        {
            System.Windows.Rect rootBounds = root.Current.BoundingRectangle;
            AutomationElementCollection buttons = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            foreach (AutomationElement button in buttons)
            {
                try
                {
                    System.Windows.Rect bounds = button.Current.BoundingRectangle;
                    if (bounds.Left <= rootBounds.Left + 80 &&
                        SidebarStateFromName(button.Current.Name) >= 0) return button;
                }
                catch { }
            }
            return null;
        }

        private static AutomationElement FindRightPanelToggle(AutomationElement root)
        {
            System.Windows.Rect rootBounds = root.Current.BoundingRectangle;
            AutomationElementCollection buttons = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            AutomationElement rightmostToggle = null;
            double rightmostEdge = double.MinValue;
            foreach (AutomationElement button in buttons)
            {
                try
                {
                    System.Windows.Rect bounds = button.Current.BoundingRectangle;
                    object pattern;
                    if (bounds.Width > 0 && bounds.Height > 0 &&
                        bounds.Right >= rootBounds.Right - 65 &&
                        bounds.Top >= rootBounds.Top + 40 &&
                        bounds.Top <= rootBounds.Top + 110 &&
                        button.TryGetCurrentPattern(TogglePattern.Pattern, out pattern))
                    {
                        // Recent Codex builds place the bottom-panel Toggle directly
                        // left of the task-side-panel Toggle. Both satisfy the old
                        // geometry filter; the task side panel is always the rightmost.
                        if (bounds.Right > rightmostEdge)
                        {
                            rightmostToggle = button;
                            rightmostEdge = bounds.Right;
                        }
                    }
                }
                catch { }
            }
            return rightmostToggle;
        }

        private static AutomationElement FindComposerAnchor(AutomationElement root,
            out bool isPermission)
        {
            isPermission = false;
            string[] permissionNames = {
                "完全访问", "完整访问", "Full access", "Allow full access"
            };
            foreach (string name in permissionNames)
            {
                AutomationElement element = root.FindFirst(TreeScope.Descendants,
                    new AndCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty,
                            ControlType.Button),
                        new PropertyCondition(AutomationElement.NameProperty, name)));
                if (element != null)
                {
                    isPermission = true;
                    return element;
                }
            }

            string[] composerNames = {
                "随心输入", "询问任何问题", "Ask anything", "Message Codex"
            };
            foreach (string name in composerNames)
            {
                AutomationElement element = root.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.NameProperty, name));
                if (element != null) return element;
            }

            // Language-independent fallback for a Codex build whose accessible names
            // are new: use the large editable control in the bottom composer area.
            System.Windows.Rect rootBounds = root.Current.BoundingRectangle;
            AutomationElementCollection edits = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            AutomationElement best = null;
            double bestWidth = 0;
            foreach (AutomationElement edit in edits)
            {
                try
                {
                    System.Windows.Rect bounds = edit.Current.BoundingRectangle;
                    if (bounds.Width >= 280 && bounds.Height >= 20 &&
                        bounds.Bottom >= rootBounds.Bottom - 240 &&
                        bounds.Bottom <= rootBounds.Bottom + 2 &&
                        bounds.Width > bestWidth)
                    {
                        best = edit;
                        bestWidth = bounds.Width;
                    }
                }
                catch { }
            }
            return best;
        }

        private static AutomationElement FindSidebarPanel(AutomationElement root)
        {
            AutomationElementCollection groups = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Group));
            foreach (AutomationElement group in groups)
            {
                try
                {
                    string className = group.Current.ClassName ?? string.Empty;
                    if (className.IndexOf("app-shell-left-panel",
                        StringComparison.OrdinalIgnoreCase) >= 0) return group;
                }
                catch { }
            }
            return null;
        }

        private void RemoveAnchorSubscription()
        {
            AutomationElement subscribed = anchorSubscribedElement;
            anchorSubscribedElement = null;
            if (subscribed != null)
            {
                try
                {
                    Automation.RemoveAutomationPropertyChangedEventHandler(
                        subscribed, anchorBoundsHandler);
                }
                catch { }
            }
            AutomationElement sidebar = sidebarToggleSubscribedElement;
            sidebarToggleSubscribedElement = null;
            if (sidebar != null)
            {
                try
                {
                    Automation.RemoveAutomationPropertyChangedEventHandler(
                        sidebar, sidebarNameHandler);
                }
                catch { }
            }
            AutomationElement rightPanel = rightPanelToggleSubscribedElement;
            rightPanelToggleSubscribedElement = null;
            if (rightPanel != null)
            {
                try
                {
                    Automation.RemoveAutomationPropertyChangedEventHandler(
                        rightPanel, rightPanelToggleHandler);
                }
                catch { }
            }
        }

        private void AnchorWorker()
        {
            IntPtr workerWindow = IntPtr.Zero;
            AutomationElement workerElement = null;
            AutomationElement workerSidebarPanel = null;
            bool workerIsPermission = false;
            int sidebarProbeTicks = 0;

            while (!anchorWorkerStopping)
            {
                IntPtr requested = new IntPtr(Interlocked.Read(ref requestedAnchorWindow));
                if (requested == IntPtr.Zero || !NativeMethods.IsWindow(requested))
                {
                    Thread.Sleep(80);
                    continue;
                }

                if (requested != workerWindow)
                {
                    RemoveAnchorSubscription();
                    workerWindow = requested;
                    workerElement = null;
                    workerSidebarPanel = null;
                    sidebarProbeTicks = 0;
                    lock (anchorStateLock)
                    {
                        anchorRect = Rectangle.Empty;
                        anchorIsPermission = false;
                        anchorLastGapMs = 32;
                        anchorLastEventUtc = DateTime.MinValue;
                        sidebarStateKnown = false;
                        sidebarToggleRect = Rectangle.Empty;
                        sidebarPredictionPending = false;
                        sidebarWidthPx = 0;
                        sidebarTransitionWidthPx = 0;
                        rightPanelStateKnown = false;
                        rightPanelToggleRect = Rectangle.Empty;
                        rightPanelPredictionPending = false;
                        rightPanelLearningPending = false;
                        rightPanelMotionWidthPx = 0;
                    }
                }

                try
                {
                    if (workerElement == null)
                    {
                        AutomationElement root = AutomationElement.FromHandle(workerWindow);
                        workerElement = FindComposerAnchor(root, out workerIsPermission);
                        if (workerElement == null)
                        {
                            Thread.Sleep(250);
                            continue;
                        }
                        anchorSubscriptionIsPermission = workerIsPermission;
                        anchorSubscribedElement = workerElement;
                        Automation.AddAutomationPropertyChangedEventHandler(
                            workerElement, TreeScope.Element, anchorBoundsHandler,
                            AutomationElement.BoundingRectangleProperty);

                        AutomationElement sidebarToggle = FindSidebarToggle(root);
                        if (sidebarToggle != null)
                        {
                            sidebarToggleSubscribedElement = sidebarToggle;
                            Automation.AddAutomationPropertyChangedEventHandler(
                                sidebarToggle, TreeScope.Element, sidebarNameHandler,
                                AutomationElement.NameProperty,
                                AutomationElement.BoundingRectangleProperty);
                            PublishSidebarState(sidebarToggle.Current.Name, false);
                            PublishSidebarToggleBounds(
                                sidebarToggle.Current.BoundingRectangle);
                        }
                        AutomationElement rightPanelToggle =
                            FindRightPanelToggle(root);
                        if (rightPanelToggle != null)
                        {
                            rightPanelToggleSubscribedElement = rightPanelToggle;
                            Automation.AddAutomationPropertyChangedEventHandler(
                                rightPanelToggle, TreeScope.Element,
                                rightPanelToggleHandler,
                                TogglePattern.ToggleStateProperty,
                                AutomationElement.BoundingRectangleProperty);
                            object togglePattern;
                            if (rightPanelToggle.TryGetCurrentPattern(
                                TogglePattern.Pattern, out togglePattern))
                            {
                                PublishRightPanelState(
                                    ((TogglePattern)togglePattern).Current.ToggleState,
                                    false);
                            }
                            PublishRightPanelToggleBounds(
                                rightPanelToggle.Current.BoundingRectangle);
                        }
                        workerSidebarPanel = FindSidebarPanel(root);
                    }

                    var bounds = workerElement.Current.BoundingRectangle;
                    if (bounds.Width <= 0 || bounds.Height <= 0)
                    {
                        RemoveAnchorSubscription();
                        workerElement = null;
                        Thread.Sleep(80);
                        continue;
                    }

                    Rectangle published = Rectangle.FromLTRB(
                        (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
                        (int)Math.Round(bounds.Right), (int)Math.Round(bounds.Bottom));
                    lock (anchorStateLock)
                    {
                        anchorRect = published;
                        anchorIsPermission = workerIsPermission;
                    }

                    bool panelShouldExist;
                    DateTime panelTransitionUtc;
                    lock (anchorStateLock)
                    {
                        panelShouldExist = sidebarStateKnown && sidebarOpen;
                        panelTransitionUtc = sidebarTransitionStartedUtc;
                    }
                    if (panelShouldExist && ++sidebarProbeTicks >= 3)
                    {
                        sidebarProbeTicks = 0;
                        if (workerSidebarPanel == null)
                        {
                            try
                            {
                                AutomationElement root = AutomationElement.FromHandle(workerWindow);
                                workerSidebarPanel = FindSidebarPanel(root);
                            }
                            catch { workerSidebarPanel = null; }
                        }
                        if (workerSidebarPanel != null &&
                            (panelTransitionUtc == DateTime.MinValue ||
                             (DateTime.UtcNow - panelTransitionUtc).TotalMilliseconds >= 520))
                        {
                            try
                            {
                                var panelBounds = workerSidebarPanel.Current.BoundingRectangle;
                                if (panelBounds.Width >= 100 && panelBounds.Width <= 1200)
                                {
                                    lock (anchorStateLock) sidebarWidthPx = panelBounds.Width;
                                }
                            }
                            catch { workerSidebarPanel = null; }
                        }
                    }
                }
                catch
                {
                    RemoveAnchorSubscription();
                    workerElement = null;
                    workerSidebarPanel = null;
                    Thread.Sleep(80);
                }

                // Property-change events carry live movement/state. This poll is only
                // a stale-element health check, so keep it off the compositor cadence.
                Thread.Sleep(80);
            }
            RemoveAnchorSubscription();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                anchorWorkerStopping = true;
                mouseHookStopping = true;
                nativeMotionStopping = true;
                nativeMotionWake.Set();
                if (mouseHookThreadId != 0)
                    NativeMethods.PostThreadMessage(mouseHookThreadId,
                        NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                CancelNativeMotion();
                if (nativeMotionThread != null && nativeMotionThread.IsAlive)
                    nativeMotionThread.Join(250);
                nativeMotionWake.Dispose();
                catalog.Dispose();
                if (sessionWatcher != null) sessionWatcher.Dispose();
                if (logWatcher != null) logWatcher.Dispose();
                timer.Dispose();
                switchSettleTimer.Dispose();
                boldFont.Dispose();
                Font.Dispose();
                sessionCache.Update(reader);
                sessionCache.Save();
            }
            base.Dispose(disposing);
        }
    }

    internal static class NativeMethods
    {
        internal const int WS_EX_TRANSPARENT = 0x20;
        internal const int WS_EX_TOOLWINDOW = 0x80;
        internal const int WS_EX_NOACTIVATE = 0x08000000;
        internal const int WH_MOUSE_LL = 14;
        internal const int WM_LBUTTONDOWN = 0x0201;
        internal const int WM_LBUTTONUP = 0x0202;
        internal const int WM_MOUSEMOVE = 0x0200;
        internal const int WM_APP_NATIVE_MOTION_FRAME = 0x8000 + 41;
        internal const uint WM_QUIT = 0x0012;
        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_SHOWWINDOW = 0x0040;
        internal const int GWLP_HWNDPARENT = -8;
        internal static readonly IntPtr HWND_TOP = IntPtr.Zero;

        internal delegate IntPtr LowLevelMouseProc(int code, IntPtr message, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT { internal int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MSG
        {
            internal IntPtr HWnd;
            internal uint Message;
            internal UIntPtr WParam;
            internal IntPtr LParam;
            internal uint Time;
            internal POINT Point;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT { internal int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        internal static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        internal static extern int GetMessage(out MSG message, IntPtr window,
            uint minimum, uint maximum);

        [DllImport("user32.dll")]
        internal static extern bool TranslateMessage(ref MSG message);

        [DllImport("user32.dll")]
        internal static extern IntPtr DispatchMessage(ref MSG message);

        [DllImport("user32.dll")]
        internal static extern bool PostThreadMessage(uint threadId, uint message,
            IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool PostMessage(IntPtr hWnd, int message,
            IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmFlush();

        [DllImport("user32.dll")]
        internal static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

        [DllImport("user32.dll")]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter,
            int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

        [StructLayout(LayoutKind.Sequential)]
        internal struct MSLLHOOKSTRUCT
        {
            internal POINT Point;
            internal uint MouseData;
            internal uint Flags;
            internal uint Time;
            internal UIntPtr ExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int idHook,
            LowLevelMouseProc callback, IntPtr module, uint threadId);

        [DllImport("user32.dll")]
        internal static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hook,
            int code, IntPtr message, IntPtr data);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr GetModuleHandle(string moduleName);

        internal const uint GA_ROOTOWNER = 3;

        [DllImport("gdi32.dll")]
        internal static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

        [DllImport("user32.dll")]
        internal static extern int SetWindowRgn(IntPtr hWnd, IntPtr region, bool redraw);
    }
}
