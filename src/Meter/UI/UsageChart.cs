using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;

namespace CodexMeter;

class UsageChart : Control
{
    readonly List<ChartBucket> buckets;
    readonly ChartHoverPopup hoverPopup = new();
    int hovered = -1;
    public UsageChart(List<ChartBucket> buckets, Action<PromptUsage>? openPrompt = null)
    {
        this.buckets = buckets; DoubleBuffered = true; TabStop = false;
        MouseMove += (_, e) =>
        {
            int index = NearestPoint(e.X);
            if (index == hovered || buckets.Count == 0) return;
            hovered = index;
            if (buckets[index].Prompt != null && buckets[index].Tokens.HasValue)
                hoverPopup.ShowBucket(buckets[index], PointToScreen(new Point(e.X, e.Y)));
            else hoverPopup.HidePopup();
        };
        MouseLeave += (_, _) => { hovered = -1; hoverPopup.HidePopup(); };
        MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && buckets.Count > 0 && buckets[NearestPoint(e.X)].Prompt is PromptUsage prompt)
                openPrompt?.Invoke(prompt);
        };
    }
    bool UsesTimeAxis => buckets.Count > 1 && buckets.All(x => x.At.HasValue);
    static readonly Color[] SeriesPalette =
    [
        Color.FromArgb(77, 183, 229), Color.FromArgb(149, 112, 255), Color.FromArgb(255, 168, 76),
        Color.FromArgb(88, 207, 142), Color.FromArgb(244, 104, 152), Color.FromArgb(232, 205, 77),
        Color.FromArgb(73, 214, 202), Color.FromArgb(255, 126, 112), Color.FromArgb(168, 213, 91),
        Color.FromArgb(193, 139, 238)
    ];
    float PointX(int index, int left, int right)
    {
        float width = Math.Max(1, Width - left - right);
        if (buckets.Count <= 1) return left + width / 2;
        if (!UsesTimeAxis) return left + width * index / (buckets.Count - 1);
        long first = buckets[0].At!.Value.UtcTicks, last = buckets[^1].At!.Value.UtcTicks;
        return last <= first ? left + width * index / (buckets.Count - 1) : left + width * (buckets[index].At!.Value.UtcTicks - first) / (float)(last - first);
    }
    int NearestPoint(int x)
    {
        if (buckets.Count == 0) return 0;
        int left = 8, right = 8;
        return Enumerable.Range(0, buckets.Count).MinBy(i => Math.Abs(PointX(i, left, right) - x));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics;
        using var dim = new SolidBrush(Color.FromArgb(180, 180, 180));
        using var missing = new Pen(Color.FromArgb(85, 90, 100)); using var grid = new Pen(Color.FromArgb(72, 72, 72));
        using var font = new Font("Segoe UI", 8);
        long max = Math.Max(1, buckets.Count == 0 ? 0 : buckets.Max(x => x.Tokens ?? 0));
        var axisLabels = Enumerable.Range(0, 5)
            .Select(level => Short((long)Math.Round(max * (4 - level) / 4.0)))
            .ToArray();
        // GDI text widths change with the system's DPI. Measure the actual labels
        // instead of relying on a fixed margin.
        int left = (int)Math.Ceiling(axisLabels.Max(label => g.MeasureString(label, font).Width)) + 12;
        // Keep the X-axis labels inside the drawing area at high DPI as well.
        int right = 8, top = 10, baseline = Height - 42;
        float chartWidth = Math.Max(1, Width - left - right), chartHeight = Math.Max(1, baseline - top);
        for (int level = 0; level <= 4; level++)
        {
            float y = top + chartHeight * level / 4f;
            g.DrawLine(grid, left, y, Width - right, y);
            string label = axisLabels[level];
            g.DrawString(label, font, dim, left - g.MeasureString(label, font).Width - 5, y - 7);
        }
        var points = new PointF?[buckets.Count];
        for (int i = 0; i < buckets.Count; i++)
        {
            float x = PointX(i, left, right);
            if (buckets[i].Tokens is long value)
            {
                float y = baseline - chartHeight * value / max;
                points[i] = new PointF(x, y);
            }
            else g.DrawLine(missing, x - 3, baseline - 2, x + 3, baseline - 6);
        }
        var previousBySeries = new Dictionary<string, (PointF Point, DateTimeOffset? At)>();
        var colorsBySeries = new Dictionary<string, Color>();
        for (int i = 0; i < points.Length; i++)
        {
            var point = points[i];
            if (point.HasValue)
            {
                if (!colorsBySeries.TryGetValue(buckets[i].Series, out var color))
                {
                    color = SeriesPalette[colorsBySeries.Count % SeriesPalette.Length];
                    colorsBySeries[buckets[i].Series] = color;
                }
                var currentAt = buckets[i].At;
                if (previousBySeries.TryGetValue(buckets[i].Series, out var previous) && currentAt is DateTimeOffset currentTime && previous.At is DateTimeOffset previousTime && currentTime - previousTime <= TimeSpan.FromMinutes(15))
                {
                    using var line = new Pen(color, 2.5f) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round, StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
                    g.DrawLine(line, previous.Point, new PointF(point.Value.X, previous.Point.Y));
                    g.DrawLine(line, new PointF(point.Value.X, previous.Point.Y), point.Value);
                }
                using var dot = new SolidBrush(color);
                g.FillEllipse(dot, point.Value.X - 2.5f, point.Value.Y - 2.5f, 5, 5);
                previousBySeries[buckets[i].Series] = (point.Value, buckets[i].At);
            }
        }
        if (buckets.Count > 0)
        {
            g.DrawString(buckets[0].Label, font, dim, left, baseline + 6);
            string last = buckets[^1].Label;
            g.DrawString(last, font, dim, Width - right - g.MeasureString(last, font).Width, baseline + 6);
        }
    }
    public static string Short(long n) => L.Short(n);
    protected override void Dispose(bool disposing) { if (disposing) hoverPopup.Dispose(); base.Dispose(disposing); }
}
