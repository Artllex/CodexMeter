namespace CodexMeter;

/// <summary>A single drawing surface keeps colored runs on the same baseline.</summary>
sealed class ModelLine : Control
{
    readonly string model;
    readonly string effort;
    public ModelLine(string model, string effort)
    {
        this.model = model;
        this.effort = effort;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        AccessibleName = model + " · " + L.Thinking(effort);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        string prefix = model + " · " + L.Pick("myślenie: ", "thinking: ");
        string value = string.IsNullOrWhiteSpace(effort) ? L.Pick("niedostępne", "unavailable") : effort;
        int height = TextRenderer.MeasureText(e.Graphics, "Ag", Font, Size.Empty, flags).Height;
        int y = Math.Max(0, (ClientSize.Height - height) / 2);
        TextRenderer.DrawText(e.Graphics, prefix, Font, new Point(0, y), ForeColor, flags);
        int x = TextRenderer.MeasureText(e.Graphics, prefix, Font, Size.Empty, flags).Width;
        TextRenderer.DrawText(e.Graphics, value, Font, new Point(x, y), ThinkingColor(effort), flags);
    }
    static Color ThinkingColor(string effort) => effort.Trim().ToLowerInvariant() switch
    {
        "low" => Color.FromArgb(128, 225, 156),
        "medium" => Color.FromArgb(245, 198, 76),
        "high" => Color.FromArgb(255, 151, 81),
        "xhigh" or "max" or "ultra" => Color.FromArgb(242, 97, 91),
        _ => UiTheme.Text
    };

}
