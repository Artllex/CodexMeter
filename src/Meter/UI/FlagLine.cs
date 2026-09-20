namespace CodexMeter;

/// <summary>Uses one measured layout for both height calculation and painting.</summary>
sealed class FlagLine : Control
{
    const int IconSize = 12, IconGap = 5, ItemGap = 12, RowGap = 4;
    IReadOnlyList<PromptFlag> flags = Array.Empty<PromptFlag>();
    (PromptFlag Flag, Point Origin)[]? positions;
    int contentHeight;
    bool alignTop;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<PromptFlag> Flags
    {
        get => flags;
        set { flags = value.ToArray(); ResetLayout(); }
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AlignTop
    {
        get => alignTop;
        set { alignTop = value; Invalidate(); }
    }
    public FlagLine(IReadOnlyList<PromptFlag>? flags = null)
    {
        Flags = flags ?? Array.Empty<PromptFlag>();
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }
    void ResetLayout() { positions = null; Invalidate(); }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); ResetLayout(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); ResetLayout(); }
    static ((PromptFlag Flag, Point Origin)[] Items, int Height) Measure(IReadOnlyList<PromptFlag> flags, Font font, int width)
    {
        var items = new (PromptFlag, Point)[flags.Count];
        int x = 0, y = 0;
        for (int i = 0; i < flags.Count; i++)
        {
            int textWidth = TextRenderer.MeasureText(flags[i].Text, font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            int itemWidth = IconSize + IconGap + textWidth + ItemGap;
            if (x > 0 && x + itemWidth > width) { y += font.Height + RowGap; x = 0; }
            items[i] = (flags[i], new Point(x, y));
            x += itemWidth;
        }
        return (items, flags.Count == 0 ? 0 : y + font.Height);
    }
    public static int RequiredHeight(IReadOnlyList<PromptFlag> flags, Font font, int width, int minimumHeight) =>
        flags.Count == 0 ? 0 : Math.Max(minimumHeight, Measure(flags, font, width).Height);
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (positions == null) (positions, contentHeight) = Measure(flags, Font, Width);
        int offset = AlignTop ? 0 : Math.Max(0, (Height - contentHeight) / 2);
        foreach (var (flag, origin) in positions)
        {
            int y = origin.Y + offset;
            if (y + Font.Height > Height) break;
            using var brush = new SolidBrush(flag.Color);
            e.Graphics.FillEllipse(brush, origin.X, y + Math.Max(0, (Font.Height - IconSize) / 2), IconSize, IconSize);
            TextRenderer.DrawText(e.Graphics, flag.Text, Font, new Point(origin.X + IconSize + IconGap, y),
                ForeColor, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
    }
}
