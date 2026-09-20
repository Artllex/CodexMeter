namespace CodexMeter;

partial class MeterForm
{
    int RenderPromptHistory(int y)
    {
        var note = TextAt(L.Pick("Lokalna historia · 3 najnowsze", "Local history · 3 newest"), 20, y, 280, 20, 8, MutedText);
        tips.SetToolTip(note, L.Pick("Trzy ostatnie prompty zapisane przez Codex Meter. Kliknij pozycję, aby zobaczyć treść, datę i liczbę tokenów.", "The three most recent prompts saved by Codex Meter. Click an item to see its text, date, and token count."));
        y += 23;
        var prompts = local?.Prompts.OrderByDescending(x => x.At).Take(3).ToList();
        if (prompts == null || prompts.Count == 0)
        {
            var label = TextAt(localBusy ? L.Pick("Odczytywanie historii…", "Reading history…") : localError != null ? L.Pick("Nie udało się odczytać historii", "Could not read history") : L.Pick("Brak zapisanych promptów", "No saved prompts"), 20, y, 280, 30, 9, Color.Gray);
            if (localError != null) tips.SetToolTip(label, localError);
            return y + 35;
        }
        var list = new FlowLayoutPanel
        {
            Location = new Point(S(17), S(y)), Width = S(286),
            AutoSize = false,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        int listHeight = 0;
        for (int i = 0; i < prompts.Count; i++)
        {
            var prompt = prompts[i];
            int rank = i + 1;
            var card = new PromptHistoryCard(prompt, S, ForeColor, MutedText, () => ShowPrompt(prompt, rank))
            {
                Margin = new Padding(0, 0, 0, S(4))
            };
            list.Controls.Add(card);
            listHeight += card.Height + card.Margin.Vertical;
            if (i < prompts.Count - 1)
            {
                var separator = new Panel { Width = S(278), Height = S(1), BackColor = Color.FromArgb(96, 96, 96), Margin = new Padding(S(1), 0, 0, S(4)) };
                list.Controls.Add(separator);
                listHeight += separator.Height + separator.Margin.Vertical;
            }
        }
        list.Height = listHeight;
        body.Controls.Add(list);
        list.PerformLayout();
        return y + UiMetrics.LogicalHeight(listHeight, layoutScale) + 2;
    }
}
