using System.Globalization;
using System.Runtime.InteropServices;

namespace CodexMeter;

static class TrayGauge
{
    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    public static Icon Create(double usedPercent)
    {
        using var bitmap = Render(32, Math.Round(usedPercent).ToString("0", CultureInfo.InvariantCulture), usedPercent);
        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    public static Bitmap CreateLogo(int size) => Render(size, "GPT", 24);


    static Bitmap Render(int size, string value, double usedPercent)
    {
        float scale = size / 32f;
        // Keep transparent space below the gauge so it sits slightly higher
        // in the Windows taskbar button without clipping the ring.
        float gaugeScale = 26f / 30f;
        float gaugeX = 3f;
        float gaugeY = 2.5f;
        float G(float coordinate) => coordinate * gaugeScale;
        var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);
            using var background = new SolidBrush(Color.FromArgb(33, 33, 33));
            graphics.FillEllipse(background, gaugeX * scale, gaugeY * scale, G(30) * scale, G(30) * scale);
            using var track = new Pen(Color.FromArgb(76, 76, 76), G(3.2f) * scale);
            graphics.DrawEllipse(track, (gaugeX + G(2.2f)) * scale, (gaugeY + G(2.2f)) * scale, G(25.6f) * scale, G(25.6f) * scale);
            var color = usedPercent >= 90 ? Color.FromArgb(255, 111, 97)
                : usedPercent >= 75 ? Color.FromArgb(255, 180, 90)
                : Color.FromArgb(16, 163, 127);
            using var progress = new Pen(color, G(3.2f) * scale) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            graphics.DrawArc(progress, (gaugeX + G(2.2f)) * scale, (gaugeY + G(2.2f)) * scale, G(25.6f) * scale, G(25.6f) * scale, -90, (float)(Math.Clamp(usedPercent, 0, 100) * 3.6));
            float baseFontSize = value == "GPT" ? 8.2f : value.Length >= 3 ? 8.2f : 10.5f;
            using var font = new Font("Segoe UI", baseFontSize * gaugeScale * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            var valueSize = TextRenderer.MeasureText(graphics, value, font, Size.Empty, TextFormatFlags.NoPadding);
            float valueCenterX = (gaugeX + G(15)) * scale;
            float valueCenterY = (gaugeY + G(15)) * scale;
            var valuePoint = new Point(
                (int)Math.Round(valueCenterX - valueSize.Width / 2f),
                (int)Math.Round(valueCenterY - valueSize.Height / 2f));
            TextRenderer.DrawText(graphics, value, font, valuePoint, Color.White, TextFormatFlags.NoPadding);
        }
        return bitmap;
    }
}

enum TaskbarProgressState
{
    NoProgress = 0,
    Normal = 0x2,
    Error = 0x4,
    Paused = 0x8
}
