using System.Runtime.InteropServices;

namespace CodexMeter;

static class UiDpi
{
    const uint MonitorDefaultToNearest = 2;
    const int EffectiveDpi = 0;

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    public static float ScaleForDpi(int dpi) => Math.Max(1f, dpi / 96f);

    public static float ScaleForScreen(Screen? screen)
    {
        var target = screen ?? Screen.PrimaryScreen;
        if (target != null)
        {
            var center = new Point(target.Bounds.Left + target.Bounds.Width / 2, target.Bounds.Top + target.Bounds.Height / 2);
            try
            {
                var monitor = MonitorFromPoint(center, MonitorDefaultToNearest);
                if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, EffectiveDpi, out uint dpiX, out _) == 0)
                    return ScaleForDpi((int)dpiX);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        using var graphics = Graphics.FromHwnd(IntPtr.Zero);
        return ScaleForDpi((int)Math.Round(graphics.DpiX));
    }
}
