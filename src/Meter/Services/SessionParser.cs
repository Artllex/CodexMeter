using System.Text.Json;
using System.Net;

namespace CodexMeter;

static class SessionParser
{
    static string Str(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static long Num(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.TryGetInt64(out var n) ? n : 0;
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

}
