namespace CodexMeter;

/// <summary>History metadata flows through table rows; no per-field Y coordinates.</summary>
sealed class PromptHistoryCard : UserControl
{
    readonly List<Font> ownedFonts = new();
    public PromptHistoryCard(PromptUsage prompt, Func<int, int> scale, Color text, Color muted, Action open)
    {
        AutoScaleMode = AutoScaleMode.None;
        Width = scale(280);
        Cursor = Cursors.Hand;
        var table = new MetadataTable { Dock = DockStyle.Top, Width = Width, Padding = new Padding(scale(4), scale(2), scale(23), 0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, scale(78)));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int total = table.Padding.Vertical;
        Font MakeFont(float size, bool bold = false)
        {
            var font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
            ownedFonts.Add(font);
            return font;
        }
        var regular = MakeFont(7.3f);
        var bold = MakeFont(7.3f, true);
        void Add(string title, Control value, int height)
        {
            int row = table.RowCount++;
            var label = new Label { Text = title, Font = bold, ForeColor = muted, Dock = DockStyle.Fill, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft };
            if (value is FlagLine) label.TextAlign = ContentAlignment.TopLeft;
            value.Dock = DockStyle.Fill;
            value.Margin = Padding.Empty;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            table.Controls.Add(label, 0, row);
            table.Controls.Add(value, 1, row);
            total += height;
        }
        Label Value(string value, Color? color = null) => new() { Text = value, Font = regular, ForeColor = color ?? text, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        string conversation = string.IsNullOrWhiteSpace(prompt.Conversation) ? L.LocalConversation : prompt.Conversation;
        Add(L.Pick("Rozmowa", "Conversation"), new ConversationLine("", conversation, prompt.ConversationIndex > 0 ? $"[{prompt.ConversationIndex}] " : "") { Font = bold, ForeColor = text }, scale(18));
        Add("Model", Value((string.IsNullOrWhiteSpace(prompt.Model) ? L.Pick("niedostępny", "unavailable") : prompt.Model) + " · " + L.Thinking(prompt.ReasoningEffort)), scale(18));
        string preview = string.Join(" ", prompt.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        Add(L.Pick("Zapytanie", "Prompt"), Value(preview.Length > 34 ? preview[..34] + "…" : preview), scale(19));
        Add(L.Pick("Tokeny IN", "Input tokens"), Value(L.Short(prompt.InputTokens), TokenVisuals.Input(prompt.InputTokens)), scale(18));
        Add(L.Pick("Tokeny OUT", "Output tokens"), Value(L.Short(prompt.OutputTokens), TokenVisuals.Output(prompt.OutputTokens)), scale(18));
        void AddFlags(string title, IReadOnlyList<PromptFlag> flags, bool isStatus)
        {
            if (flags.Count == 0) return;
            var font = MakeFont(6.8f, isStatus);
            var line = new FlagLine(flags) { Font = font, ForeColor = text, AlignTop = true };
            int height = FlagLine.RequiredHeight(flags, font, scale(175), scale(17));
            Add(title, line, height);
        }
        AddFlags(L.Pick("Flagi", "Flags"), PromptFlags.Items(prompt), false);
        AddFlags("Status", PromptFlags.StatusItems(prompt), true);
        table.Height = Height = total;
        Controls.Add(table);
        void Wire(Control control)
        {
            control.Click += (_, _) => open();
            foreach (Control child in control.Controls) Wire(child);
        }
        Wire(this);
        AccessibleName = conversation + " · " + preview;
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { foreach (var font in ownedFonts) font.Dispose(); ownedFonts.Clear(); }
    }
}
