using System.Text.Json;
using System.Net;

namespace CodexMeter;

record TokenSample(string Key, DateTimeOffset At, long Tokens);
class PromptUsage
{
    public string Key { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string Text { get; set; } = "";
    public long Tokens { get; set; }
    public bool Complete { get; set; }
}
record LocalUsage(List<TokenSample> Samples, List<PromptUsage> Prompts, int Files, int Errors);

static class Analytics
{
    static readonly Dictionary<string, (long Size, DateTime Modified, LocalUsage Data)> Cache = new();
    static string Str(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static long Num(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.TryGetInt64(out var n) ? n : 0;
    public static LocalUsage Read()
    {
        var home = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrEmpty(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var results = new List<LocalUsage>();
        int errors = 0;
        foreach (var dir in new[] { Path.Combine(home, "sessions"), Path.Combine(home, "archived_sessions") })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.AllDirectories))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (!Cache.TryGetValue(file, out var entry) || entry.Size != info.Length || entry.Modified != info.LastWriteTimeUtc)
                    {
                        var data = Parse(file);
                        Cache[file] = (info.Length, info.LastWriteTimeUtc, data);
                    }
                    results.Add(Cache[file].Data);
                }
                catch (IOException) { errors++; }
                catch (UnauthorizedAccessException) { errors++; }
            }
        }
        return new LocalUsage(results.SelectMany(x => x.Samples).DistinctBy(x => x.Key).OrderBy(x => x.At).ToList(), results.SelectMany(x => x.Prompts).GroupBy(x => x.Key).Select(g => g.OrderByDescending(x => x.Tokens).First()).ToList(), results.Count, errors + results.Sum(x => x.Errors));
    }
    internal static LocalUsage Parse(string file)
    {
        var samples = new List<TokenSample>(); var prompts = new List<PromptUsage>();
        var session = file; bool root = true; DateTimeOffset created = DateTimeOffset.MinValue;
        long previous = 0; bool seen = false; string turn = ""; PromptUsage? active = null;
        var texts = new HashSet<string>(); int errors = 0;
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!(line.Contains("session_meta") || line.Contains("token_count") || line.Contains("task_started") || line.Contains("task_complete") || line.Contains("user_message") || (line.Contains("response_item") && line.Contains("\"user\"")))) continue;
            try
            {
                using var doc = JsonDocument.Parse(line); var e = doc.RootElement;
                if (!e.TryGetProperty("payload", out var p)) continue;
                string type = Str(e, "type");
                if (type == "session_meta")
                {
                    session = Str(p, "id");
                    root = !p.TryGetProperty("source", out var source) || source.ValueKind == JsonValueKind.String;
                    DateTimeOffset.TryParse(Str(p, "timestamp"), out created);
                    continue;
                }
                if (!DateTimeOffset.TryParse(Str(e, "timestamp"), out var at)) continue;
                string kind = Str(p, "type");
                if (type == "event_msg" && kind == "token_count")
                {
                    if (!p.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object || !info.TryGetProperty("total_token_usage", out var total)) continue;
                    long cumulative = Num(total, "total_tokens");
                    long last = info.TryGetProperty("last_token_usage", out var lastUsage) ? Num(lastUsage, "total_tokens") : 0;
                    long delta = !seen ? Math.Min(cumulative, last) : cumulative >= previous ? cumulative - previous : Math.Min(cumulative, last);
                    previous = cumulative; seen = true;
                    if (at < created || delta <= 0) continue;
                    samples.Add(new TokenSample(session + ":" + at.ToString("O") + ":" + cumulative, at, delta));
                    if (active != null) active.Tokens += delta;
                    continue;
                }
                if (at < created) continue;
                if (type == "event_msg" && kind == "task_started")
                {
                    turn = Str(p, "turn_id"); active = null; texts.Clear(); continue;
                }
                if (type == "event_msg" && kind == "task_complete")
                {
                    if (active != null) active.Complete = true;
                    active = null; continue;
                }
                if (!root) continue;
                string text = "";
                if (type == "event_msg" && kind == "user_message") text = Str(p, "message");
                else if (type == "response_item" && Str(p, "role") == "user" && p.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                    text = string.Join("\n", content.EnumerateArray().Select(x => Str(x, "text")).Where(x => x.Length > 0));
                text = Clean(text);
                if (text.Length == 0 || !texts.Add(text)) continue;
                if (active == null)
                {
                    active = new PromptUsage { Key = session + ":" + (turn.Length > 0 ? turn : at.ToString("O")), At = at, Text = text };
                    prompts.Add(active);
                }
                else active.Text += "\n\n— Doprecyzowanie —\n" + text;
            }
            catch (JsonException) { errors++; }
        }
        return new LocalUsage(samples, prompts.Where(x => x.Tokens > 0).ToList(), 1, errors);
    }
    static string Clean(string text)
    {
        text = text.Trim();
        if (text.StartsWith("<recommended_plugins>") || text.StartsWith("# AGENTS.md instructions") || text.StartsWith("<environment_context>") || text.StartsWith("<permissions instructions>") || text.StartsWith("<turn_aborted>") || text.StartsWith("<subagent_notification>")) return "";
        if (text.StartsWith("<send_user_message_question_reply>"))
        {
            try
            {
                var json = text[(text.IndexOf('>') + 1)..text.LastIndexOf('<')];
                using var doc = JsonDocument.Parse(json);
                text = string.Join("\n", doc.RootElement.EnumerateArray().Select(x => Str(x, "answer")));
            }
            catch { return ""; }
        }
        return WebUtility.HtmlDecode(text);
    }

    public static string SelfTest(string directory)
    {
        string path = Path.Combine(directory, "analytics-fixture.jsonl");
        var lines = new List<string>();
        void Add(string type, object payload, string at = "2026-09-06T10:00:00Z") => lines.Add(JsonSerializer.Serialize(new { timestamp = at, type, payload }));
        Add("session_meta", new { id = "test", source = "vscode", timestamp = "2026-09-06T09:00:00Z" });
        Add("event_msg", new { type = "task_started", turn_id = "a" });
        Add("response_item", new { role = "user", content = new[] { new { text = "Pierwszy prompt" } } });
        void Tokens(long total, long last, string at) => Add("event_msg", new { type = "token_count", info = new { total_token_usage = new { total_tokens = total }, last_token_usage = new { total_tokens = last } } }, at);
        Tokens(100, 100, "2026-09-06T10:00:01Z"); Tokens(100, 100, "2026-09-06T10:00:02Z"); Tokens(150, 50, "2026-09-06T10:00:03Z");
        Add("event_msg", new { type = "task_complete" });
        Add("event_msg", new { type = "task_started", turn_id = "b" });
        Add("event_msg", new { type = "user_message", message = "Drugi prompt" });
        Add("response_item", new { role = "user", content = new[] { new { text = "Drugi prompt" } } });
        Tokens(175, 25, "2026-09-06T10:01:01Z");
        File.WriteAllLines(path, lines.Append("{partial"));
        var result = Parse(path);
        if (result.Samples.Sum(x => x.Tokens) != 175 || result.Prompts.Count != 2 || result.Prompts[0].Tokens != 150 || result.Prompts[1].Tokens != 25 || result.Prompts[1].Text != "Drugi prompt") throw new Exception("Błąd sumowania tokenów lub przypisania promptu.");
        return "PASS: deltas, duplicate counters, prompt boundaries and duplicate messages";
    }
}

record ChartBucket(string Label, long? Tokens, string Detail);
class UsageChart : Control
{
    readonly List<ChartBucket> buckets;
    readonly ToolTip tip = new();
    int hovered = -1;
    public UsageChart(List<ChartBucket> buckets)
    {
        this.buckets = buckets; DoubleBuffered = true; TabStop = false;
        MouseMove += (_, e) =>
        {
            int index = Math.Clamp((int)((e.X - 2) / ((Width - 4.0) / Math.Max(1, buckets.Count))), 0, Math.Max(0, buckets.Count - 1));
            if (index == hovered || buckets.Count == 0) return;
            hovered = index; tip.SetToolTip(this, buckets[index].Detail);
        };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics;
        using var dim = new SolidBrush(Color.FromArgb(180, 180, 180)); using var ink = new SolidBrush(Color.FromArgb(16, 163, 127));
        using var missing = new Pen(Color.FromArgb(85, 90, 100)); using var line = new Pen(Color.FromArgb(48, 53, 62));
        using var font = new Font("Segoe UI", 8);
        long max = Math.Max(1, buckets.Max(x => x.Tokens ?? 0));
        g.DrawString(Short(max) + " tokenów", font, dim, 0, 0);
        int baseline = Height - 24; float chartHeight = Math.Max(1, Height - 50);
        g.DrawLine(line, 0, baseline, Width, baseline);
        float step = (Width - 4f) / Math.Max(1, buckets.Count);
        for (int i = 0; i < buckets.Count; i++)
        {
            float x = 2 + i * step, width = Math.Max(2, step - 3);
            if (buckets[i].Tokens is long value)
            {
                float height = chartHeight * value / max;
                if (height > 0) g.FillRectangle(ink, x, baseline - height, width, height);
            }
            else { g.DrawLine(missing, x, baseline - 2, x + width, baseline - 6); }
        }
        if (buckets.Count > 0)
        {
            g.DrawString(buckets[0].Label, font, dim, 0, baseline + 5);
            string last = buckets[^1].Label; g.DrawString(last, font, dim, Width - g.MeasureString(last, font).Width, baseline + 5);
        }
    }
    public static string Short(long n) => n >= 1000000 ? $"{n / 1000000.0:0.#} mln" : n >= 1000 ? $"{n / 1000.0:0.#} tys." : n.ToString();
    protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
}
