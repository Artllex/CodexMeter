using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;

namespace CodexMeter;

static class UiMetrics
{
    // All constants are logical pixels. Convert only at the control boundary.
    public const int SelectionRowHeight = 30;
    public static int LogicalHeight(int measuredPixels, float scale) =>
        (int)Math.Ceiling(measuredPixels / scale);
}

static class UiControls
{
    public static Label Text(string text, float size, Color color, bool bold = false) => new()
    {
        Text = text, Dock = DockStyle.Fill, Margin = Padding.Empty,
        Font = UiTheme.Font(size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = color, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft
    };
}
