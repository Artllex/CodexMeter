namespace CodexMeter;

/// <summary>Application-owned fonts: consumers borrow them and must not dispose them.</summary>
static class UiTheme
{
    public static readonly Color Surface = Color.FromArgb(35, 35, 35);
    public static readonly Color MainSurface = Color.FromArgb(33, 33, 33);
    public static readonly Color MenuSurface = Color.FromArgb(47, 47, 47);
    public static readonly Color MainText = Color.FromArgb(236, 236, 236);
    public static readonly Color MainMuted = Color.FromArgb(180, 180, 180);
    public static readonly Color Accent = Color.FromArgb(16, 163, 127);
    public static readonly Color Raised = Color.FromArgb(45, 45, 45);
    public static readonly Color Text = Color.FromArgb(242, 242, 242);
    public static readonly Color Muted = Color.FromArgb(170, 170, 170);
    public static readonly Color Hover = Color.FromArgb(70, 83, 111);
    public static readonly Color Pressed = Color.FromArgb(61, 72, 96);
    public static readonly Color Separator = Color.FromArgb(78, 78, 78);
    static readonly Dictionary<(string Family, float Size, FontStyle Style), Font> Fonts = new();
    static UiTheme()
    {
        Application.ApplicationExit += (_, _) =>
        {
            foreach (var font in Fonts.Values) font.Dispose();
            Fonts.Clear();
        };
    }
    public static Font Font(float size, FontStyle style = FontStyle.Regular, string family = "Segoe UI")
    {
        var key = (family, size, style);
        if (!Fonts.TryGetValue(key, out var font)) Fonts[key] = font = new Font(family, size, style);
        return font;
    }
}
