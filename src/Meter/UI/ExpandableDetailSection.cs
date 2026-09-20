namespace CodexMeter;

/// <summary>Owns the editor, expansion button and preferred height of a detail section.</summary>
sealed class ExpandableDetailSection : Panel
{
    public RichTextBox Editor { get; }
    public ActionButton Toggle { get; }
    public Panel ButtonHost { get; }
    readonly Func<int, int> scale;
    public ExpandableDetailSection(Func<int, int> scale, Font font, bool expandable, bool wrap)
    {
        this.scale = scale;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        BackColor = UiTheme.Surface;
        Editor = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
            ScrollBars = RichTextBoxScrollBars.None, BackColor = BackColor,
            ForeColor = UiTheme.Text, Font = font, DetectUrls = false,
            WordWrap = wrap, Margin = Padding.Empty
        };
        ButtonHost = new Panel { Dock = DockStyle.Right, Width = scale(34), Visible = expandable, BackColor = BackColor };
        Toggle = new ActionButton { Text = "⌄", Location = new Point(scale(4), 0), Size = new Size(scale(30), scale(26)), Font = UiTheme.Font(9), TabStop = true, AccessibleName = L.Pick("Rozwiń lub zwiń sekcję", "Expand or collapse section") };
        ButtonHost.Controls.Add(Toggle);
        Controls.Add(Editor);
        Controls.Add(ButtonHost);
    }
    public int ContentHeight()
    {
        int width = Math.Max(scale(80), Width - (ButtonHost.Visible ? ButtonHost.Width : 0));
        var flags = TextFormatFlags.TextBoxControl | (Editor.WordWrap ? TextFormatFlags.WordBreak : TextFormatFlags.Default);
        return Math.Max(scale(26), TextRenderer.MeasureText(Editor.Text, Editor.Font, new Size(width, int.MaxValue), flags).Height + scale(6));
    }
    public void SetViewport(int height, bool expanded)
    {
        Editor.ScrollBars = expanded && height < ContentHeight()
            ? (Editor.WordWrap ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both)
            : RichTextBoxScrollBars.None;
    }
}
