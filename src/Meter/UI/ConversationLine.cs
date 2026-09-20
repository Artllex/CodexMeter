using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;

namespace CodexMeter;

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
        Font = UiTheme.Font(7.3f);
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
