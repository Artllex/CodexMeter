using System.Text.Json;
using System.Net;

namespace CodexMeter;

record TokenSample(string Key, DateTimeOffset At, long Tokens, string SessionId = "", long InputTokens = 0, long OutputTokens = 0);
class PromptUsage
{
    public string Key { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string Text { get; set; } = "";
    public string OriginalText { get; set; } = "";
    public string RawParameters { get; set; } = "";
    public string ProjectLocation { get; set; } = "";
    public long Tokens { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public bool Complete { get; set; }
    public string SessionId { get; set; } = "";
    public string Conversation { get; set; } = "";
    public string Model { get; set; } = "";
    public string ReasoningEffort { get; set; } = "";
    public int ConversationIndex { get; set; }
    public int FileCount { get; set; }
    public int PictureCount { get; set; }
    public bool WorkedOnCode { get; set; }
    public bool TestsRun { get; set; }
    public bool ProjectBuilt { get; set; }
    public bool GitCommit { get; set; }
    public bool GitPush { get; set; }
    public bool PullRequest { get; set; }
    public bool PackageBuilt { get; set; }
    public bool ReleaseCreated { get; set; }
    public bool GeneratedPicture { get; set; }
    public bool UsedWeb { get; set; }
    public bool UsedBrowser { get; set; }
    public bool DependenciesChanged { get; set; }
    public bool InstalledSoftware { get; set; }
    public bool DocumentsChanged { get; set; }
    public int AgentCount { get; set; }
    public bool AutomationChanged { get; set; }
    public bool Failed { get; set; }
    public bool Cancelled { get; set; }
    public bool Partial { get; set; }
    public HashSet<string> McpTools { get; } = new(StringComparer.OrdinalIgnoreCase);
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
        var conversationNames = ReadConversationNames(home);
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
        var prompts = results.SelectMany(x => x.Prompts).GroupBy(x => x.Key).Select(g => g.OrderByDescending(x => x.Tokens).First()).ToList();
        foreach (var prompt in prompts)
            if (conversationNames.TryGetValue(prompt.SessionId, out var title)) prompt.Conversation = title;
        foreach (var conversation in prompts.GroupBy(prompt => prompt.SessionId))
        {
            int index = 1;
            foreach (var prompt in conversation.OrderBy(prompt => prompt.At)) prompt.ConversationIndex = index++;
        }
        return new LocalUsage(results.SelectMany(x => x.Samples).DistinctBy(x => x.Key).OrderBy(x => x.At).ToList(), prompts, results.Count, errors + results.Sum(x => x.Errors));
    }
    static Dictionary<string, string> ReadConversationNames(string home)
    {
        var names = new Dictionary<string, string>();
        string path = Path.Combine(home, "session_index.jsonl");
        if (!File.Exists(path)) return names;
        try
        {
            foreach (string line in File.ReadLines(path))
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    string id = Str(doc.RootElement, "id"), title = Str(doc.RootElement, "thread_name");
                    if (id.Length > 0 && title.Length > 0) names[id] = title;
                }
                catch (JsonException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return names;
    }
    internal static LocalUsage Parse(string file)
    {
        var samples = new List<TokenSample>(); var prompts = new List<PromptUsage>();
        var session = file; bool root = true; DateTimeOffset created = DateTimeOffset.MinValue;
        string conversation = "", model = "", reasoningEffort = "", projectLocation = "";
        long previous = 0, previousInput = 0, previousOutput = 0;
        bool seen = false; string turn = ""; PromptUsage? active = null;
        var texts = new HashSet<string>(); int errors = 0;
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!(line.Contains("session_meta") || line.Contains("thread_settings_applied") || line.Contains("turn_context") || line.Contains("token_count") || line.Contains("task_started") || line.Contains("task_complete") || line.Contains("task_failed") || line.Contains("turn_aborted") || line.Contains("user_message") || line.Contains("response_item"))) continue;
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
                    conversation = Str(p, "title");
                    projectLocation = Str(p, "cwd");
                    if (projectLocation.Length == 0) projectLocation = Str(p, "workdir");
                    continue;
                }
                if (type == "turn_context")
                {
                    model = Str(p, "model");
                    reasoningEffort = Str(p, "effort");
                    if (reasoningEffort.Length == 0) reasoningEffort = Str(p, "reasoning_effort");
                    continue;
                }
                if (!DateTimeOffset.TryParse(Str(e, "timestamp"), out var at)) continue;
                string kind = Str(p, "type");
                if (type == "event_msg" && kind == "thread_settings_applied")
                {
                    if (p.TryGetProperty("thread_settings", out var settings) && settings.ValueKind == JsonValueKind.Object)
                    {
                        model = Str(settings, "model");
                        reasoningEffort = Str(settings, "reasoning_effort");
                    }
                    continue;
                }
                if (type == "event_msg" && kind == "token_count")
                {
                    if (!p.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object || !info.TryGetProperty("total_token_usage", out var total)) continue;
                    long cumulative = Num(total, "total_tokens");
                    long last = info.TryGetProperty("last_token_usage", out var lastUsage) ? Num(lastUsage, "total_tokens") : 0;
                    long cumulativeInput = Num(total, "input_tokens");
                    long cumulativeOutput = Num(total, "output_tokens");
                    long lastInput = info.TryGetProperty("last_token_usage", out lastUsage) ? Num(lastUsage, "input_tokens") : 0;
                    long lastOutput = info.TryGetProperty("last_token_usage", out lastUsage) ? Num(lastUsage, "output_tokens") : 0;
                    long delta = !seen ? Math.Min(cumulative, last) : cumulative >= previous ? cumulative - previous : Math.Min(cumulative, last);
                    long deltaInput = !seen ? Math.Min(cumulativeInput, lastInput) : cumulativeInput >= previousInput ? cumulativeInput - previousInput : Math.Min(cumulativeInput, lastInput);
                    long deltaOutput = !seen ? Math.Min(cumulativeOutput, lastOutput) : cumulativeOutput >= previousOutput ? cumulativeOutput - previousOutput : Math.Min(cumulativeOutput, lastOutput);
                    previous = cumulative; previousInput = cumulativeInput; previousOutput = cumulativeOutput; seen = true;
                    if (at < created || delta <= 0) continue;
                    samples.Add(new TokenSample(session + ":" + at.ToString("O") + ":" + cumulative, at, delta, session, Math.Max(0, deltaInput), Math.Max(0, deltaOutput)));
                    if (active != null)
                    {
                        active.Tokens += delta;
                        active.InputTokens += Math.Max(0, deltaInput);
                        active.OutputTokens += Math.Max(0, deltaOutput);
                    }
                    continue;
                }
                if (at < created) continue;
                if (type == "event_msg" && kind == "task_started")
                {
                    turn = Str(p, "turn_id"); active = null; texts.Clear(); continue;
                }
                if (type == "event_msg" && kind == "task_complete")
                {
                    if (active != null)
                    {
                        active.Complete = true;
                        string status = Str(p, "status");
                        active.Failed = status.Equals("failed", StringComparison.OrdinalIgnoreCase);
                        active.Cancelled = status.Equals("cancelled", StringComparison.OrdinalIgnoreCase) || status.Equals("canceled", StringComparison.OrdinalIgnoreCase);
                        active.Partial = status.Equals("partial", StringComparison.OrdinalIgnoreCase);
                    }
                    active = null; continue;
                }
                if (type == "event_msg" && kind is "task_failed" or "turn_aborted")
                {
                    if (active != null)
                    {
                        active.Failed = kind == "task_failed";
                        active.Cancelled = kind == "turn_aborted";
                    }
                    active = null; continue;
                }
                if (type == "response_item" && active != null)
                {
                    DetectToolUsage(p, active);
                }
                if (!root) continue;
                string text = "";
                if (type == "event_msg" && kind == "user_message") text = Str(p, "message");
                else if (type == "response_item" && Str(p, "role") == "user" && p.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                    text = string.Join("\n", content.EnumerateArray().Select(x => Str(x, "text")).Where(x => x.Length > 0));
                string attachmentSource = text;
                text = Clean(text);
                if (text.Length == 0 || !texts.Add(text)) continue;
                if (active == null)
                {
                    if (conversation.Length == 0) conversation = ConversationFallback(text);
                    active = new PromptUsage
                    {
                        Key = session + ":" + (turn.Length > 0 ? turn : at.ToString("O")),
                        At = at,
                        Text = text,
                        OriginalText = Original(attachmentSource),
                        RawParameters = e.GetRawText(),
                        ProjectLocation = projectLocation,
                        SessionId = session,
                        Conversation = conversation,
                        Model = model,
                        ReasoningEffort = reasoningEffort
                    };
                    prompts.Add(active);
                }
                else
                {
                    active.Text += "\n\n— Doprecyzowanie —\n" + text;
                    active.OriginalText += "\n\n— Doprecyzowanie —\n" + Original(attachmentSource);
                    active.RawParameters += "\n" + e.GetRawText();
                }
                CountAttachments(attachmentSource, active);
            }
            catch (JsonException) { errors++; }
        }
        var completedPrompts = prompts.Where(x => x.Tokens > 0).OrderBy(x => x.At).ToList();
        for (int index = 0; index < completedPrompts.Count; index++) completedPrompts[index].ConversationIndex = index + 1;
        return new LocalUsage(samples, completedPrompts, 1, errors);
    }
    static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".heic" };
    static void CountAttachments(string text, PromptUsage prompt)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"(?:[A-Za-z]:[\\/]|/)[^\r\n<>\""']+?\.[A-Za-z0-9]{2,8}"))
            paths.Add(match.Value.TrimEnd('.', ',', ')', ']'));
        foreach (string path in paths)
        {
            if (ImageExtensions.Contains(Path.GetExtension(path))) prompt.PictureCount++;
            else prompt.FileCount++;
        }
    }
    static void DetectToolUsage(JsonElement payload, PromptUsage prompt)
    {
        string payloadType = Str(payload, "type");
        if (payloadType is not ("function_call" or "custom_tool_call")) return;
        string name = Str(payload, "name");
        string raw = payload.GetRawText();
        string input = Str(payload, "arguments");
        if (input.Length == 0) input = Str(payload, "input");
        string searchable = name + " " + input;
        bool Match(string pattern) => System.Text.RegularExpressions.Regex.IsMatch(searchable, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        prompt.WorkedOnCode |= Match(@"apply_patch|tools\.apply_patch|(?:edit|write)_(?:file|code)");
        prompt.TestsRun |= Match(@"\bdotnet\s+test\b|\bnpm\s+(?:run\s+)?test\b|\bpytest\b|\bcargo\s+test\b|\bgo\s+test\b|--selftest\b|\bvalidate(?:\.py)?\b");
        prompt.ProjectBuilt |= Match(@"\bdotnet\s+build\b|\bnpm\s+run\s+build\b|\bcargo\s+build\b|\bgo\s+build\b|\bmsbuild\b");
        prompt.GitCommit |= Match(@"\bgit\s+(?:-c\s+\S+\s+)*commit\b");
        prompt.GitPush |= Match(@"\bgit\s+(?:-c\s+\S+\s+)*push\b");
        prompt.PullRequest |= Match(@"\bgh\s+pr\s+(?:create|edit|merge)\b|attach_artifact[\s\S]*pull_request|pull_request[\s\S]*(?:create|update|merge)");
        prompt.PackageBuilt |= Match(@"\bdotnet\s+publish\b|\bnpm\s+pack\b|\bcargo\s+package\b|\bCompress-Archive\b|\bISCC(?:\.exe)?\b|\bmakeappx(?:\.exe)?\b");
        prompt.ReleaseCreated |= Match(@"\bgh\s+release\s+create\b|/releases/new\b|\bpublish(?:ed)?\s+(?:a\s+)?release\b");
        if (searchable.Contains("image_gen", StringComparison.OrdinalIgnoreCase) || searchable.Contains("imagegen", StringComparison.OrdinalIgnoreCase)) prompt.GeneratedPicture = true;
        prompt.UsedWeb |= Match(@"web__run|search_query|image_query");
        prompt.UsedBrowser |= Match(@"cua_repl|createBrowserTab|getTab\s*\(");
        bool dependencyChange = Match(@"\bnpm\s+(?:install|add|update)\b|\bpnpm\s+(?:install|add|update)\b|\byarn\s+(?:install|add|upgrade)\b|\bdotnet\s+add\s+\S+\s+package\b|\bpip(?:3)?\s+install\b|\buv\s+add\b|\bcargo\s+add\b");
        prompt.DependenciesChanged |= dependencyChange;
        if (!dependencyChange) prompt.InstalledSoftware |= Match(@"request_plugin_install|\bwinget\s+install\b|\bchoco\s+install\b|\bmsiexec(?:\.exe)?\b|\bdotnet\s+tool\s+install\b");
        prompt.DocumentsChanged |= Match(@"documents|render_docx|\.(?:docx|pdf|xlsx|pptx)\b|apply_patch[\s\S]*\.(?:md|txt)\b");
        prompt.AgentCount += System.Text.RegularExpressions.Regex.Matches(searchable, @"(?:collaboration\.)?spawn_agent\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
        prompt.AutomationChanged |= Match(@"automation_update|create_automation|update_automation");
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(raw, @"mcp__([A-Za-z0-9_]+)__([A-Za-z0-9_]+)"))
            prompt.McpTools.Add(match.Groups[1].Value + ": " + match.Groups[2].Value);
        if (raw.Contains("image_gen__imagegen", StringComparison.OrdinalIgnoreCase)) prompt.GeneratedPicture = true;
    }
    static string ConversationFallback(string text)
    {
        string compact = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 36 ? compact : compact[..36] + "…";
    }
    static string Original(string text) => WebUtility.HtmlDecode(text).Trim();
    static string Clean(string text)
    {
        text = WebUtility.HtmlDecode(text).Trim();
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
        const string marker = "My request:";
        int markerIndex = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            int lineStart = text.LastIndexOf('\n', markerIndex);
            string heading = text[(lineStart + 1)..markerIndex].Trim();
            if (heading.All(c => c == '#' || char.IsWhiteSpace(c)))
                text = text[(markerIndex + marker.Length)..].Trim();
        }
        return text;
    }

    public static string SelfTest(string directory)
    {
        string path = Path.Combine(directory, "analytics-fixture.jsonl");
        var lines = new List<string>();
        void Add(string type, object payload, string at = "2026-09-06T10:00:00Z") => lines.Add(JsonSerializer.Serialize(new { timestamp = at, type, payload }));
        Add("session_meta", new { id = "test", source = "vscode", timestamp = "2026-09-06T09:00:00Z", cwd = "C:\\CODE\\test-project" });
        Add("event_msg", new { type = "thread_settings_applied", thread_settings = new { model = "gpt-test", reasoning_effort = "high" } });
        Add("event_msg", new { type = "task_started", turn_id = "a" });
        Add("response_item", new { role = "user", content = new[] { new { text = "Pierwszy prompt" } } });
        Add("response_item", new { type = "custom_tool_call_output", output = "Tekst wyniku zawiera mcp__figma__get_file, image_gen__imagegen oraz git commit, ale nie jest wywołaniem." });
        void Tokens(long total, long last, long input, long output, string at) => Add("event_msg", new { type = "token_count", info = new { total_token_usage = new { total_tokens = total, input_tokens = input, output_tokens = output }, last_token_usage = new { total_tokens = last, input_tokens = Math.Min(input, last), output_tokens = Math.Min(output, last) } } }, at);
        Tokens(100, 100, 80, 20, "2026-09-06T10:00:01Z"); Tokens(100, 100, 80, 20, "2026-09-06T10:00:02Z"); Tokens(150, 50, 120, 30, "2026-09-06T10:00:03Z");
        Add("event_msg", new { type = "task_complete" });
        Add("event_msg", new { type = "task_started", turn_id = "b" });
        Add("event_msg", new { type = "user_message", message = "# Files mentioned by the user:\n\n## obraz.png: C:\\temp\\obraz.png\n## dane.csv: C:\\temp\\dane.csv\n\n## My request:\nDrugi prompt&#x20;" });
        Add("response_item", new { role = "user", content = new[] { new { text = "Drugi prompt" } } });
        Add("response_item", new { type = "custom_tool_call", name = "exec", input = "dotnet test src/Meter/App.csproj; dotnet build src/Meter/App.csproj; git commit -m test; git push origin main; dotnet publish src/Meter/App.csproj; gh pr create; gh release create v1; npm install pakiet" });
        Add("response_item", new { type = "custom_tool_call", name = "exec", input = "apply_patch docs/readme.md" });
        Add("response_item", new { type = "function_call", name = "web__run", arguments = "{\"search_query\":[]}" });
        Add("response_item", new { type = "function_call", name = "mcp__cua_repl__js", arguments = "{}" });
        Add("response_item", new { type = "function_call", name = "request_plugin_install", arguments = "{}" });
        Add("response_item", new { type = "function_call", name = "spawn_agent", arguments = "{}" });
        Add("response_item", new { type = "function_call", name = "automation_update", arguments = "{}" });
        Add("response_item", new { type = "function_call", name = "image_gen__imagegen", arguments = "{}" });
        Add("response_item", new { type = "function_call", name = "mcp__figma__get_file", arguments = "{}" });
        Tokens(175, 25, 138, 37, "2026-09-06T10:01:01Z");
        Add("event_msg", new { type = "task_complete", status = "partial" }, "2026-09-06T10:01:02Z");
        File.WriteAllLines(path, lines.Append("{partial"));
        var result = Parse(path);
        var flagged = result.Prompts[1];
        if (result.Samples.Sum(x => x.Tokens) != 175 || result.Prompts.Count != 2 || result.Prompts[0].Tokens != 150 || result.Prompts[0].InputTokens != 120 || result.Prompts[0].OutputTokens != 30 || flagged.Tokens != 25 || flagged.Text != "Drugi prompt" || !flagged.OriginalText.Contains("## My request:") || !flagged.RawParameters.Contains("user_message") || flagged.ProjectLocation != "C:\\CODE\\test-project" || flagged.OriginalText.Contains("&#x20;") || result.Prompts[0].Model != "gpt-test" || result.Prompts[0].ReasoningEffort != "high") throw new Exception("Błąd sumowania tokenów lub metadanych promptu.");
        if (result.Prompts[0].WorkedOnCode || result.Prompts[0].GitCommit || result.Prompts[0].GeneratedPicture || result.Prompts[0].McpTools.Count > 0) throw new Exception("Tekst wyniku narzędzia został błędnie uznany za wykonaną akcję.");
        if (flagged.PictureCount != 1 || flagged.FileCount != 1 || !flagged.WorkedOnCode || !flagged.TestsRun || !flagged.ProjectBuilt || !flagged.GitCommit || !flagged.GitPush || !flagged.PullRequest || !flagged.PackageBuilt || !flagged.ReleaseCreated || !flagged.GeneratedPicture || !flagged.UsedWeb || !flagged.UsedBrowser || !flagged.DependenciesChanged || !flagged.InstalledSoftware || !flagged.DocumentsChanged || flagged.AgentCount != 1 || !flagged.AutomationChanged || !flagged.Partial || !flagged.McpTools.Contains("figma: get_file")) throw new Exception("Błąd wykrywania flag aktywności promptu.");
        if (PromptContent.Sections("Zwykłe zapytanie").FirstOrDefault()?.Title != "My request") throw new Exception("Zwykłe zapytanie nie trafiło do sekcji My request.");
        return "PASS: deltas, prompt boundaries, metadata and activity flags";
    }
}

record PromptFlag(string Text, Color Color);

static class PromptFlags
{
    public static List<PromptFlag> Items(PromptUsage? prompt)
    {
        var flags = new List<PromptFlag>();
        if (prompt == null) return flags;
        if (prompt.FileCount > 0) flags.Add(new PromptFlag($"Files ({prompt.FileCount})", Color.FromArgb(91, 156, 255)));
        if (prompt.PictureCount > 0) flags.Add(new PromptFlag($"Picture ({prompt.PictureCount})", Color.FromArgb(230, 100, 173)));
        if (prompt.WorkedOnCode) flags.Add(new PromptFlag("Code", Color.FromArgb(77, 202, 218)));
        if (prompt.TestsRun) flags.Add(new PromptFlag("Test", Color.FromArgb(124, 205, 116)));
        if (prompt.ProjectBuilt) flags.Add(new PromptFlag("Build", Color.FromArgb(238, 195, 73)));
        if (prompt.GitCommit) flags.Add(new PromptFlag("Commit", Color.FromArgb(244, 149, 72)));
        if (prompt.GitPush) flags.Add(new PromptFlag("Push", Color.FromArgb(242, 112, 92)));
        if (prompt.PullRequest) flags.Add(new PromptFlag("PR", Color.FromArgb(186, 125, 242)));
        if (prompt.PackageBuilt) flags.Add(new PromptFlag("Package", Color.FromArgb(218, 170, 84)));
        if (prompt.ReleaseCreated) flags.Add(new PromptFlag("Release", Color.FromArgb(255, 105, 145)));
        if (prompt.GeneratedPicture) flags.Add(new PromptFlag("Pic. Gen.", Color.FromArgb(168, 112, 244)));
        if (prompt.UsedWeb) flags.Add(new PromptFlag("Web", Color.FromArgb(75, 176, 235)));
        if (prompt.UsedBrowser) flags.Add(new PromptFlag("Browser", Color.FromArgb(67, 199, 184)));
        if (prompt.McpTools.Count > 0) flags.Add(new PromptFlag("MCP (" + string.Join(", ", prompt.McpTools.OrderBy(x => x)) + ")", Color.FromArgb(88, 207, 142)));
        if (prompt.DependenciesChanged) flags.Add(new PromptFlag("Dependencies", Color.FromArgb(215, 160, 80)));
        if (prompt.InstalledSoftware) flags.Add(new PromptFlag("Install", Color.FromArgb(123, 187, 104)));
        if (prompt.DocumentsChanged) flags.Add(new PromptFlag("Docs", Color.FromArgb(115, 157, 232)));
        if (prompt.AgentCount > 0) flags.Add(new PromptFlag($"Agent ({prompt.AgentCount})", Color.FromArgb(196, 119, 224)));
        if (prompt.AutomationChanged) flags.Add(new PromptFlag("Automation", Color.FromArgb(72, 192, 207)));
        return flags;
    }
    public static List<PromptFlag> StatusItems(PromptUsage? prompt)
    {
        var statuses = new List<PromptFlag>();
        if (prompt == null) return statuses;
        if (prompt.Failed) statuses.Add(new PromptFlag("Failed", Color.FromArgb(240, 92, 92)));
        if (prompt.Cancelled) statuses.Add(new PromptFlag("Cancelled", Color.FromArgb(164, 164, 164)));
        if (prompt.Partial) statuses.Add(new PromptFlag("Partial", Color.FromArgb(245, 183, 75)));
        return statuses;
    }
    public static string Text(PromptUsage? prompt)
    {
        return string.Join("  ·  ", Items(prompt).Select(flag => flag.Text));
    }
}

sealed class FlagLine : Control
{
    IReadOnlyList<PromptFlag> flags = Array.Empty<PromptFlag>();
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<PromptFlag> Flags { get => flags; set { flags = value; Invalidate(); } }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AlignTop { get; set; }
    public FlagLine(IReadOnlyList<PromptFlag>? flags = null) { Flags = flags ?? Array.Empty<PromptFlag>(); DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        int x = 0, y = AlignTop ? 0 : Math.Max(0, (Height - Font.Height) / 2);
        foreach (var flag in flags)
        {
            Size size = TextRenderer.MeasureText(flag.Text, Font, new Size(int.MaxValue, Font.Height), TextFormatFlags.NoPadding);
            const int iconSize = 12;
            int itemWidth = iconSize + 5 + size.Width + 12;
            if (x > 0 && x + itemWidth > Width)
            {
                x = 0;
                y += Font.Height + 4;
            }
            if (y + Font.Height > Height) break;
            using var brush = new SolidBrush(flag.Color);
            e.Graphics.FillEllipse(brush, x, y + Math.Max(0, (Font.Height - iconSize) / 2), iconSize, iconSize);
            TextRenderer.DrawText(e.Graphics, flag.Text, Font, new Point(x + iconSize + 5, y), ForeColor, TextFormatFlags.NoPadding);
            x += itemWidth;
        }
    }
}

record PromptSection(string Title, string Text);

static class PromptContent
{
    public static List<PromptSection> Sections(string original)
    {
        var request = new List<string>();
        var files = new List<string>();
        var pictures = new List<string>();
        var technical = new List<string>();
        List<string> current = request;
        foreach (string line in original.Replace("\r\n", "\n").Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("# Files mentioned by the user:", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("# Files pasted by the user:", StringComparison.OrdinalIgnoreCase))
            {
                current = files;
                continue;
            }
            if (trimmed.StartsWith("## My request:", StringComparison.OrdinalIgnoreCase))
            {
                current = request;
                continue;
            }
            if (trimmed.StartsWith("Distinguish instructions in attached", StringComparison.OrdinalIgnoreCase))
            {
                current = technical;
                technical.Add(line);
                continue;
            }
            if (trimmed.StartsWith("<image ", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("</image", StringComparison.OrdinalIgnoreCase))
            {
                pictures.Add(line);
                continue;
            }
            current.Add(line);
        }
        static string Join(List<string> lines) => string.Join(Environment.NewLine, lines).Trim();
        var sections = new List<PromptSection>();
        void Add(string title, List<string> lines)
        {
            string text = Join(lines);
            if (text.Length > 0) sections.Add(new PromptSection(title, text));
        }
        Add("My request", request);
        Add("Files mentioned by the user", files);
        Add("Images", pictures);
        Add("Technical information", technical);
        if (sections.Count == 0 && original.Trim().Length > 0) sections.Add(new PromptSection("My request", original.Trim()));
        return sections;
    }

    public static string RequestPreview(IReadOnlyList<PromptSection> sections)
    {
        var request = sections.FirstOrDefault(section => section.Title == "My request");
        return request?.Text ?? sections.FirstOrDefault()?.Text ?? "";
    }

    public static void WriteTo(RichTextBox box, IReadOnlyList<PromptSection> sections, Color textColor, Color headingColor)
    {
        box.Clear();
        foreach (var section in sections)
        {
            if (box.TextLength > 0) box.AppendText(Environment.NewLine + Environment.NewLine);
            int headingStart = box.TextLength;
            box.AppendText(section.Title);
            box.Select(headingStart, section.Title.Length);
            box.SelectionFont = new Font(box.Font, FontStyle.Bold);
            box.SelectionColor = headingColor;
            box.AppendText(Environment.NewLine);
            int textStart = box.TextLength;
            box.AppendText(section.Text);
            box.Select(textStart, section.Text.Length);
            box.SelectionFont = new Font(box.Font, FontStyle.Regular);
            box.SelectionColor = textColor;
        }
        box.Select(0, 0);
    }
}

record ChartBucket(string Label, long? Tokens, string Detail, DateTimeOffset? At = null, string Series = "", PromptUsage? Prompt = null, string TokenKind = "", long InputTokens = 0, long OutputTokens = 0);

sealed class ChartHoverPopup : Form
{
    readonly Panel conversationHost;
    readonly Label modelValue;
    readonly Label promptValue;
    readonly Label dateValue;
    readonly Label inputValue;
    readonly Label outputValue;
    readonly Label flagsField;
    readonly FlagLine flagsValue;
    readonly Label statusField;
    readonly FlagLine statusValue;
    readonly TableLayoutPanel layout;
    readonly Func<int, int> P;

    public ChartHoverPopup()
    {
        using var graphics = Graphics.FromHwnd(IntPtr.Zero);
        float scale = Math.Max(1f, graphics.DpiX / 96f);
        P = value => Math.Max(1, (int)Math.Round(value * scale));
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(P(360), P(180));
        BackColor = Color.FromArgb(35, 35, 35);
        ForeColor = Color.FromArgb(242, 242, 242);
        Padding = new Padding(P(9), P(8), P(9), P(8));
        layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Margin = Padding.Empty, Padding = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, P(102)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 6; row++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(27)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        Label Field(string text) => new() { Text = text, Dock = DockStyle.Fill, Margin = Padding.Empty, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = Color.FromArgb(178, 178, 178), TextAlign = ContentAlignment.MiddleLeft };
        Label Value(bool bold = false) => new() { Dock = DockStyle.Fill, Margin = Padding.Empty, Font = new Font("Segoe UI", 8.5f, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = ForeColor, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        conversationHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        modelValue = Value(); promptValue = Value(); dateValue = Value(); inputValue = Value(); outputValue = Value();
        layout.Controls.Add(Field(L.Pick("Rozmowa", "Conversation")), 0, 0); layout.Controls.Add(conversationHost, 1, 0);
        layout.Controls.Add(Field(L.Pick("Model", "Model")), 0, 1); layout.Controls.Add(modelValue, 1, 1);
        layout.Controls.Add(Field(L.Pick("Zapytanie", "Prompt")), 0, 2); layout.Controls.Add(promptValue, 1, 2);
        layout.Controls.Add(Field(L.Pick("Data i godzina", "Date and time")), 0, 3); layout.Controls.Add(dateValue, 1, 3);
        layout.Controls.Add(Field(L.Pick("Tokeny IN", "Input tokens")), 0, 4); layout.Controls.Add(inputValue, 1, 4);
        layout.Controls.Add(Field(L.Pick("Tokeny OUT", "Output tokens")), 0, 5); layout.Controls.Add(outputValue, 1, 5);
        flagsField = Field(L.Pick("Flagi", "Flags"));
        flagsValue = new FlagLine { Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = ForeColor, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold) };
        layout.Controls.Add(flagsField, 0, 6); layout.Controls.Add(flagsValue, 1, 6);
        statusField = Field(L.Pick("Status", "Status"));
        statusValue = new FlagLine { Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = ForeColor, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold) };
        layout.Controls.Add(statusField, 0, 7); layout.Controls.Add(statusValue, 1, 7);
        Controls.Add(layout);
    }

    public void ShowBucket(ChartBucket bucket, Point anchor)
    {
        var prompt = bucket.Prompt;
        string conversation = string.IsNullOrWhiteSpace(prompt?.Conversation) ? L.LocalConversation : prompt.Conversation;
        string order = prompt?.ConversationIndex > 0 ? $"[{prompt.ConversationIndex}] " : "";
        foreach (Control child in conversationHost.Controls.Cast<Control>().ToArray()) { conversationHost.Controls.Remove(child); child.Dispose(); }
        conversationHost.Controls.Add(new ConversationLine("", conversation, order) { Dock = DockStyle.Fill, ForeColor = ForeColor, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) });
        string model = string.IsNullOrWhiteSpace(prompt?.Model) ? L.Pick("niedostępny", "unavailable") : prompt.Model;
        modelValue.Text = model + " · " + L.Thinking(prompt?.ReasoningEffort ?? "");
        string request = string.IsNullOrWhiteSpace(prompt?.Text) ? L.NoData : string.Join(" ", prompt.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        promptValue.Text = request.Length > 72 ? request[..72].TrimEnd() + "…" : request;
        dateValue.Text = bucket.At?.ToLocalTime().ToString("dd.MM.yyyy · HH:mm:ss", L.Culture) ?? L.NoData;
        inputValue.Text = L.Short(bucket.InputTokens);
        inputValue.ForeColor = TokenVisuals.Input(bucket.InputTokens);
        outputValue.Text = L.Short(bucket.OutputTokens);
        outputValue.ForeColor = TokenVisuals.Output(bucket.OutputTokens);
        var flags = PromptFlags.Items(prompt);
        bool showFlags = flags.Count > 0;
        flagsField.Visible = flagsValue.Visible = showFlags;
        flagsValue.Flags = flags;
        int flagsHeight = showFlags ? P(flags.Count > 12 ? 96 : flags.Count > 6 ? 72 : 48) : 0;
        layout.RowStyles[6].Height = flagsHeight;
        var statuses = PromptFlags.StatusItems(prompt);
        bool showStatus = statuses.Count > 0;
        statusField.Visible = statusValue.Visible = showStatus;
        statusValue.Flags = statuses;
        int statusHeight = showStatus ? P(27) : 0;
        layout.RowStyles[7].Height = statusHeight;
        ClientSize = new Size(P(360), P(180) + flagsHeight + statusHeight);
        var area = Screen.FromPoint(anchor).WorkingArea;
        int x = anchor.X + 14, y = anchor.Y + 14;
        if (x + Width > area.Right) x = anchor.X - Width - 14;
        if (y + Height > area.Bottom) y = anchor.Y - Height - 14;
        Location = new Point(Math.Max(area.Left, x), Math.Max(area.Top, y));
        if (!Visible) Show();
        else Invalidate(true);
    }

    public void HidePopup() { if (Visible) Hide(); }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return parameters;
        }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Color.FromArgb(88, 88, 88));
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
}

class UsageChart : Control
{
    readonly List<ChartBucket> buckets;
    readonly ChartHoverPopup hoverPopup = new();
    int hovered = -1;
    public UsageChart(List<ChartBucket> buckets, Action<PromptUsage>? openPrompt = null)
    {
        this.buckets = buckets; DoubleBuffered = true; TabStop = false;
        MouseMove += (_, e) =>
        {
            int index = NearestPoint(e.X);
            if (index == hovered || buckets.Count == 0) return;
            hovered = index;
            if (buckets[index].Prompt != null && buckets[index].Tokens.HasValue)
                hoverPopup.ShowBucket(buckets[index], PointToScreen(new Point(e.X, e.Y)));
            else hoverPopup.HidePopup();
        };
        MouseLeave += (_, _) => { hovered = -1; hoverPopup.HidePopup(); };
        MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && buckets.Count > 0 && buckets[NearestPoint(e.X)].Prompt is PromptUsage prompt)
                openPrompt?.Invoke(prompt);
        };
    }
    bool UsesTimeAxis => buckets.Count > 1 && buckets.All(x => x.At.HasValue);
    static readonly Color[] SeriesPalette =
    [
        Color.FromArgb(77, 183, 229), Color.FromArgb(149, 112, 255), Color.FromArgb(255, 168, 76),
        Color.FromArgb(88, 207, 142), Color.FromArgb(244, 104, 152), Color.FromArgb(232, 205, 77),
        Color.FromArgb(73, 214, 202), Color.FromArgb(255, 126, 112), Color.FromArgb(168, 213, 91),
        Color.FromArgb(193, 139, 238)
    ];
    float PointX(int index, int left, int right)
    {
        float width = Math.Max(1, Width - left - right);
        if (buckets.Count <= 1) return left + width / 2;
        if (!UsesTimeAxis) return left + width * index / (buckets.Count - 1);
        long first = buckets[0].At!.Value.UtcTicks, last = buckets[^1].At!.Value.UtcTicks;
        return last <= first ? left + width * index / (buckets.Count - 1) : left + width * (buckets[index].At!.Value.UtcTicks - first) / (float)(last - first);
    }
    int NearestPoint(int x)
    {
        if (buckets.Count == 0) return 0;
        int left = 8, right = 8;
        return Enumerable.Range(0, buckets.Count).MinBy(i => Math.Abs(PointX(i, left, right) - x));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics;
        using var dim = new SolidBrush(Color.FromArgb(180, 180, 180));
        using var missing = new Pen(Color.FromArgb(85, 90, 100)); using var grid = new Pen(Color.FromArgb(72, 72, 72));
        using var font = new Font("Segoe UI", 8);
        long max = Math.Max(1, buckets.Count == 0 ? 0 : buckets.Max(x => x.Tokens ?? 0));
        var axisLabels = Enumerable.Range(0, 5)
            .Select(level => Short((long)Math.Round(max * (4 - level) / 4.0)))
            .ToArray();
        // GDI text widths change with the system's DPI. Measure the actual labels
        // instead of relying on a fixed margin.
        int left = (int)Math.Ceiling(axisLabels.Max(label => g.MeasureString(label, font).Width)) + 12;
        // Keep the X-axis labels inside the drawing area at high DPI as well.
        int right = 8, top = 10, baseline = Height - 42;
        float chartWidth = Math.Max(1, Width - left - right), chartHeight = Math.Max(1, baseline - top);
        for (int level = 0; level <= 4; level++)
        {
            float y = top + chartHeight * level / 4f;
            g.DrawLine(grid, left, y, Width - right, y);
            string label = axisLabels[level];
            g.DrawString(label, font, dim, left - g.MeasureString(label, font).Width - 5, y - 7);
        }
        var points = new PointF?[buckets.Count];
        for (int i = 0; i < buckets.Count; i++)
        {
            float x = PointX(i, left, right);
            if (buckets[i].Tokens is long value)
            {
                float y = baseline - chartHeight * value / max;
                points[i] = new PointF(x, y);
            }
            else g.DrawLine(missing, x - 3, baseline - 2, x + 3, baseline - 6);
        }
        var previousBySeries = new Dictionary<string, (PointF Point, DateTimeOffset? At)>();
        var colorsBySeries = new Dictionary<string, Color>();
        for (int i = 0; i < points.Length; i++)
        {
            var point = points[i];
            if (point.HasValue)
            {
                if (!colorsBySeries.TryGetValue(buckets[i].Series, out var color))
                {
                    color = SeriesPalette[colorsBySeries.Count % SeriesPalette.Length];
                    colorsBySeries[buckets[i].Series] = color;
                }
                var currentAt = buckets[i].At;
                if (previousBySeries.TryGetValue(buckets[i].Series, out var previous) && currentAt is DateTimeOffset currentTime && previous.At is DateTimeOffset previousTime && currentTime - previousTime <= TimeSpan.FromMinutes(15))
                {
                    using var line = new Pen(color, 2.5f) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round, StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
                    g.DrawLine(line, previous.Point, new PointF(point.Value.X, previous.Point.Y));
                    g.DrawLine(line, new PointF(point.Value.X, previous.Point.Y), point.Value);
                }
                using var dot = new SolidBrush(color);
                g.FillEllipse(dot, point.Value.X - 2.5f, point.Value.Y - 2.5f, 5, 5);
                previousBySeries[buckets[i].Series] = (point.Value, buckets[i].At);
            }
        }
        if (buckets.Count > 0)
        {
            g.DrawString(buckets[0].Label, font, dim, left, baseline + 6);
            string last = buckets[^1].Label;
            g.DrawString(last, font, dim, Width - right - g.MeasureString(last, font).Width, baseline + 6);
        }
    }
    public static string Short(long n) => L.Short(n);
    protected override void Dispose(bool disposing) { if (disposing) hoverPopup.Dispose(); base.Dispose(disposing); }
}

class ConversationLine : Control
{
    readonly string time;
    readonly string orderPrefix;
    readonly string title;
    readonly List<string> icons = new();

    public ConversationLine(string time, string conversation, string orderPrefix = "")
    {
        this.time = time;
        this.orderPrefix = orderPrefix;
        title = ExtractIcons(conversation);
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 7.3f);
    }

    string ExtractIcons(string text)
    {
        string remaining = text.TrimStart();
        while (true)
        {
            string? found = new[] { "✅", "🐙", "Ⓧ", "🚀" }.FirstOrDefault(icon => remaining.StartsWith(icon, StringComparison.Ordinal));
            if (found == null) return remaining;
            icons.Add(found);
            remaining = remaining[found.Length..].TrimStart();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        string prefix = (string.IsNullOrWhiteSpace(time) ? "" : time + " · ") + orderPrefix;
        var prefixSize = TextRenderer.MeasureText(g, prefix, Font, Size.Empty, TextFormatFlags.NoPadding);
        int y = Math.Max(0, (Height - prefixSize.Height) / 2);
        TextRenderer.DrawText(g, prefix, Font, new Point(0, y), ForeColor, TextFormatFlags.NoPadding);
        int iconSize = Math.Min(Height - 2, Math.Max(14, (int)Math.Round(14 * DeviceDpi / 96f)));
        int x = prefixSize.Width + (prefix.Length > 0 ? 4 : 0);
        foreach (string icon in icons)
        {
            DrawIcon(g, icon, new Rectangle(x, Math.Max(0, (Height - iconSize) / 2), iconSize, iconSize));
            x += iconSize + 3;
        }
        TextRenderer.DrawText(g, title, Font, new Rectangle(x, 0, Math.Max(1, Width - x), Height), ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    static void DrawIcon(Graphics g, string icon, Rectangle r)
    {
        float scale = r.Width / 18f;
        if (icon == "✅")
        {
            using var fill = new SolidBrush(Color.FromArgb(64, 190, 125));
            using var check = new Pen(Color.White, Math.Max(1.8f, 2.1f * scale)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            g.FillRectangle(fill, r);
            g.DrawLines(check, new[] { new PointF(r.X + 3 * scale, r.Y + 9 * scale), new PointF(r.X + 7 * scale, r.Y + 13 * scale), new PointF(r.X + 15 * scale, r.Y + 4 * scale) });
        }
        else if (icon == "🐙")
        {
            using var body = new SolidBrush(Color.FromArgb(226, 104, 151));
            using var eye = new SolidBrush(Color.FromArgb(45, 35, 50));
            g.FillEllipse(body, r.X + 3 * scale, r.Y + 1 * scale, 12 * scale, 11 * scale);
            for (int i = 0; i < 4; i++) g.FillEllipse(body, r.X + (1 + i * 4) * scale, r.Y + 9 * scale, 5 * scale, 7 * scale);
            g.FillEllipse(eye, r.X + 6 * scale, r.Y + 5 * scale, 1.7f * scale, 2 * scale);
            g.FillEllipse(eye, r.X + 10.5f * scale, r.Y + 5 * scale, 1.7f * scale, 2 * scale);
        }
        else if (icon == "Ⓧ")
        {
            using var ring = new Pen(Color.FromArgb(205, 210, 216), Math.Max(1.4f, 1.7f * scale));
            using var cross = new Pen(Color.FromArgb(225, 228, 232), Math.Max(1.3f, 1.6f * scale));
            g.DrawEllipse(ring, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
            g.DrawLine(cross, r.X + 5 * scale, r.Y + 5 * scale, r.X + 13 * scale, r.Y + 13 * scale);
            g.DrawLine(cross, r.X + 13 * scale, r.Y + 5 * scale, r.X + 5 * scale, r.Y + 13 * scale);
        }
        else if (icon == "🚀")
        {
            using var body = new SolidBrush(Color.FromArgb(220, 225, 232));
            using var nose = new SolidBrush(Color.FromArgb(238, 87, 87));
            using var window = new SolidBrush(Color.FromArgb(68, 175, 230));
            using var flame = new SolidBrush(Color.FromArgb(255, 174, 66));
            g.FillEllipse(body, r.X + 5 * scale, r.Y + 2 * scale, 9 * scale, 13 * scale);
            g.FillPie(nose, r.X + 5 * scale, r.Y + 1 * scale, 9 * scale, 8 * scale, 190, 160);
            g.FillEllipse(window, r.X + 8 * scale, r.Y + 6 * scale, 3.5f * scale, 3.5f * scale);
            g.FillEllipse(flame, r.X + 7 * scale, r.Y + 13 * scale, 5 * scale, 4 * scale);
        }
    }
}
