using System.Text.Json;
using System.Net;

namespace CodexMeter;

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
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int errors = 0;
        {
            foreach (var file in SessionFileIndex.Get(home))
            {
                seenFiles.Add(file);
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
        foreach (var removed in Cache.Keys.Where(path => !seenFiles.Contains(path)).ToArray()) Cache.Remove(removed);
        var prompts = results.SelectMany(x => x.Prompts).GroupBy(x => x.Key).Select(g => g.MaxBy(x => x.Tokens)!).ToList();
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
    internal static LocalUsage Parse(string file) => SessionParser.Parse(file);
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
