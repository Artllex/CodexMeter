using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;

namespace CodexMeter;

partial class MeterForm
{
    int RenderChart(int y)
    {
        var timeRanges = L.Polish
            ? new[] { "Wpisy (godzina)", "Wpisy (4 godziny)", "Dzień (24 godz.)", "Tydzień (7 dni)" }
            : new[] { "Entries (hour)", "Entries (4 hours)", "Day (24 hours)", "Week (7 days)" };
        chartMode = Math.Clamp(chartMode, 0, timeRanges.Length - 1);
        SelectAt(timeRanges, chartMode, y, i => chartMode = i, x: 20, width: 137, selectedLabel: i => timeRanges[i].Split(" (", StringSplitOptions.None)[0]);
        SelectAt(
            L.Polish ? new[] { "Tokeny IN", "Tokeny OUT" } : new[] { "Input", "Output" },
            chartTokenMode,
            y,
            i => chartTokenMode = i,
            x: 163,
            width: 137);
        y += 33;
        var conversations = new[] { L.Pick("Wszystkie rozmowy", "All conversations") }
            .Concat(ChartConversations())
            .ToArray();
        int selectedConversation = Math.Max(0, Array.IndexOf(conversations, chartConversationFilter));
        if (selectedConversation == 0 && chartConversationFilter.Length > 0) chartConversationFilter = "";
        SelectAt(conversations, selectedConversation, y, i => chartConversationFilter = i == 0 ? "" : conversations[i], searchable: true);
        y += 33;
        var points = ChartPoints();
        if (local == null)
            TextAt(localBusy ? L.Pick("Odczytywanie historii…", "Reading history…") : L.Pick("Brak lokalnych danych", "No local data"), 20, y, 280, 150, 9, Color.Gray);
        else body.Controls.Add(new UsageChart(points, selected => ShowPrompt(selected, 0)) { Location = new Point(S(20), S(y)), Size = new Size(S(280), S(150)), BackColor = BackColor });
        y += 155;
        TextAt(L.Pick("Lokalnie · czas Windows", "Local · Windows time"), 20, y, 280, 21, 8, Color.Gray);
        return y + 28;
    }
    List<ChartBucket> ChartPoints()
    {
        var points = new List<ChartBucket>();
        TimeSpan range = ChartRange();
        var from = DateTimeOffset.UtcNow - range;
        if (local != null)
        {
            var promptsBySession = local.Prompts.Where(x => !string.IsNullOrWhiteSpace(x.SessionId)).GroupBy(x => x.SessionId).ToDictionary(group => group.Key, group => group.OrderBy(x => x.At).ToList());
            foreach (var sample in local.Samples.Where(x => x.At >= from).OrderBy(x => x.At))
            {
                var localTime = sample.At.ToLocalTime();
                PromptUsage? prompt = promptsBySession.TryGetValue(sample.SessionId, out var sessionPrompts) ? sessionPrompts.LastOrDefault(x => x.At <= sample.At) ?? sessionPrompts.FirstOrDefault() : null;
                string conversation = string.IsNullOrWhiteSpace(prompt?.Conversation) ? L.LocalConversation : prompt.Conversation;
                if (chartConversationFilter.Length > 0 && !string.Equals(chartConversationFilter, conversation, StringComparison.Ordinal)) continue;
                string model = string.IsNullOrWhiteSpace(prompt?.Model) ? L.Pick("niedostępny", "unavailable") : prompt.Model;
                string effort = L.Thinking(prompt?.ReasoningEffort ?? "");
                string request = string.IsNullOrWhiteSpace(prompt?.Text) ? L.NoData : string.Join(" ", prompt.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                if (request.Length > 36) request = request[..36] + "…";
                long tokens = chartTokenMode == 0 ? sample.InputTokens : sample.OutputTokens;
                string tokenKind = chartTokenMode == 0 ? L.Pick("Tokeny IN", "Input tokens") : L.Pick("Tokeny OUT", "Output tokens");
                string order = prompt?.ConversationIndex > 0 ? $"[{prompt.ConversationIndex}] " : "";
                string detail = string.Join("\n", L.Pick("Rozmowa: ", "Conversation: ") + order + conversation, L.Pick("Model: ", "Model: ") + model + " · " + effort, L.Pick("Zapytanie: ", "Prompt: ") + request, L.Pick("Data i godzina: ", "Date and time: ") + localTime.ToString("dd.MM.yyyy · HH:mm:ss", L.Culture), tokenKind + ": " + Number(tokens));
                points.Add(new ChartBucket(localTime.ToString(range.TotalHours <= 24 ? "HH:mm" : "dd.MM"), tokens, detail, sample.At, conversation, prompt, tokenKind, sample.InputTokens, sample.OutputTokens));
            }
        }
        if (points.Count == 0) points.Add(new ChartBucket("", null, L.NoData));
        return points;
    }
    TimeSpan ChartRange() => chartMode switch
    {
        0 => TimeSpan.FromHours(1),
        1 => TimeSpan.FromHours(4),
        2 => TimeSpan.FromHours(24),
        3 => TimeSpan.FromDays(7),
        _ => TimeSpan.FromDays(7)
    };
    IEnumerable<string> ChartConversations()
    {
        if (local == null) return Enumerable.Empty<string>();
        var conversationsBySession = local.Prompts
            .Where(x => !string.IsNullOrWhiteSpace(x.SessionId))
            .GroupBy(x => x.SessionId)
            .ToDictionary(group => group.Key, group => group.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Conversation))?.Conversation ?? L.LocalConversation);
        var from = DateTimeOffset.UtcNow - ChartRange();
        return local.Samples
            .Where(x => x.At >= from)
            .Select(x => conversationsBySession.TryGetValue(x.SessionId, out var conversation) ? conversation : L.LocalConversation)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.CurrentCulture);
    }
}
