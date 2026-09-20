using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;

namespace CodexMeter;

static class AppMessages
{
    const int HWND_BROADCAST = 0xFFFF;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string message);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);
    public static readonly int ShowWindow = RegisterWindowMessage("CodexMeter.ShowWindow.v1");
    public static void ShowExisting() => PostMessage((IntPtr)HWND_BROADCAST, ShowWindow, IntPtr.Zero, IntPtr.Zero);
}

static class Program
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--export-gpt-logo"))
        {
            string output = args.SkipWhile(x => x != "--export-gpt-logo").Skip(1).FirstOrDefault()
                ?? Path.Combine(AppContext.BaseDirectory, "CodexMeter-GPT-Logo.png");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            using var logo = TrayGauge.CreateLogo(1024);
            logo.Save(output, System.Drawing.Imaging.ImageFormat.Png);
            return;
        }
        if (args.Contains("--export-gpt-branding"))
        {
            string root = args.SkipWhile(x => x != "--export-gpt-branding").Skip(1).FirstOrDefault()
                ?? throw new ArgumentException("Brak katalogu projektu dla eksportu brandingu.");
            TrayGauge.ExportBranding(Path.GetFullPath(root));
            return;
        }
        if (args.Contains("--probe"))
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "probe.json"), Api.Read().GetAwaiter().GetResult().ToJsonString(new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "probe.json"), new JsonObject { ["error"] = ex.Message }.ToJsonString()); Environment.ExitCode = 1; }
            return;
        }
        // Keep the taskbar window separate from the fixed MSIX package icon.
        // Windows can then use the live meter icon assigned to the window.
        SetCurrentProcessExplicitAppUserModelID("Artllex.CodexMeter.Dynamic");
        using var mutex = new Mutex(true, args.Contains("--selftest") || args.Contains("--popup-preview") ? "Local\\CodexMeterPreview" : "Local\\CodexMeterArkadiusz", out bool first);
        if (!first) { AppMessages.ShowExisting(); return; }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--popup-preview"))
        {
            string output = args.SkipWhile(x => x != "--popup-preview").Skip(1).FirstOrDefault()
                ?? Path.Combine(AppContext.BaseDirectory, "popup-preview.png");
            CompletionPopup.ExportPreview(Path.GetFullPath(output));
            return;
        }
        if (args.Contains("--selftest"))
        {
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "analytics-test.txt"), Analytics.SelfTest(AppContext.BaseDirectory));
                using (var gauge = TrayGauge.Create(94))
                using (var gaugeBitmap = gauge.ToBitmap())
                    gaugeBitmap.Save(Path.Combine(AppContext.BaseDirectory, "tray-gauge-94.png"));
                using var form = new MeterForm(true);
                if (!form.ShowInTaskbar) throw new InvalidOperationException("Główne okno nie jest widoczne na pasku zadań.");
                form.Show(); Application.DoEvents();
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(AppContext.BaseDirectory, "preview.png"));
                form.ExpandForPreview(); Application.DoEvents();
                using var expanded = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(expanded, new Rectangle(Point.Empty, expanded.Size));
                expanded.Save(Path.Combine(AppContext.BaseDirectory, "preview-expanded.png"));
                form.VerifyInteractions();
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "selftest.txt"), "PASS: rendered compact panel and tray icon");
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "selftest.txt"), "FAIL: " + ex);
                Environment.ExitCode = 1;
            }
            return;
        }
        Application.Run(new MeterForm());
    }
}

sealed class CompletionPopup : Form
{
    static readonly List<CompletionPopup> Active = new();
    readonly System.Windows.Forms.Timer closeTimer = new() { Interval = 10000 };

    CompletionPopup(PromptUsage prompt)
    {
        using var screenGraphics = Graphics.FromHwnd(IntPtr.Zero);
        float dpiScale = Math.Max(1f, screenGraphics.DpiX / 96f);
        int P(int value) => Math.Max(1, (int)Math.Round(value * dpiScale));

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(P(360), P(205));
        BackColor = Color.FromArgb(35, 35, 35);
        ForeColor = Color.FromArgb(242, 242, 242);
        Padding = new Padding(P(12), P(10), P(12), P(10));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, P(116)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(34)));
        for (int row = 0; row < 5; row++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(30)));

        var fieldColor = Color.FromArgb(170, 170, 170);
        var valueColor = Color.FromArgb(242, 242, 242);
        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Zakończono przetwarzanie",
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = valueColor,
            TextAlign = ContentAlignment.MiddleLeft
        };
        string preview = string.Join(" ", prompt.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (preview.Length > 34) preview = preview[..34] + "…";
        string conversation = string.IsNullOrWhiteSpace(prompt.Conversation) ? "Rozmowa lokalna" : prompt.Conversation;
        string model = string.IsNullOrWhiteSpace(prompt.Model) ? "Model: niedostępny" : prompt.Model;
        string effort = string.IsNullOrWhiteSpace(prompt.ReasoningEffort) ? "myślenie: niedostępne" : "myślenie: " + prompt.ReasoningEffort;
        var itemFont = new Font("Segoe UI", 9.5f);
        Label FieldLabel(string text) => new() { Text = text, Dock = DockStyle.Fill, Font = itemFont, ForeColor = fieldColor, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
        Label FieldValue(string text) => new() { Text = text, Dock = DockStyle.Fill, Font = itemFont, ForeColor = valueColor, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
        var context = new ConversationLine("", conversation) { Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = valueColor, Font = itemFont };
        layout.Controls.Add(title, 0, 0);
        layout.SetColumnSpan(title, 2);
        layout.Controls.Add(FieldLabel("Tokeny IN"), 0, 1);
        layout.Controls.Add(FieldValue(Short(prompt.InputTokens)), 1, 1);
        layout.Controls.Add(FieldLabel("Tokeny OUT"), 0, 2);
        layout.Controls.Add(FieldValue(Short(prompt.OutputTokens)), 1, 2);
        layout.Controls.Add(FieldLabel("Zapytanie"), 0, 3);
        layout.Controls.Add(FieldValue(preview), 1, 3);
        layout.Controls.Add(FieldLabel("Rozmowa"), 0, 4);
        layout.Controls.Add(context, 1, 4);
        layout.Controls.Add(FieldLabel("Model"), 0, 5);
        layout.Controls.Add(FieldValue($"{model} · {effort}"), 1, 5);
        Controls.Add(layout);
        foreach (Control control in Controls.Cast<Control>().Append(this)) control.Click += (_, _) => Close();
        closeTimer.Tick += (_, _) => { closeTimer.Stop(); Close(); };
        FormClosed += (_, _) =>
        {
            closeTimer.Dispose();
            Active.Remove(this);
            Reposition();
        };
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return parameters;
        }
    }

    public static void Display(PromptUsage prompt)
    {
        var popup = new CompletionPopup(prompt);
        Active.Add(popup);
        Reposition();
        popup.Show();
        popup.closeTimer.Start();
    }

    public static void ExportPreview(string path)
    {
        var sample = new PromptUsage
        {
            At = DateTimeOffset.Now,
            Text = "Dodajmy jednolitą typografię oraz czytelne pola w powiadomieniu.",
            InputTokens = 459_000,
            OutputTokens = 949,
            Conversation = "✅ 🐙 Ⓧ DownloadLens",
            Model = "gpt-5.6-terra",
            ReasoningEffort = "low"
        };
        using var popup = new CompletionPopup(sample);
        popup.Show();
        Application.DoEvents();
        using var image = new Bitmap(popup.Width, popup.Height);
        popup.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        popup.Hide();
    }

    static void Reposition()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(Point.Empty);
        int bottom = area.Bottom - 12;
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            var popup = Active[i];
            if (popup.IsDisposed) continue;
            popup.Location = new Point(area.Right - popup.Width - 12, bottom - popup.Height);
            bottom -= popup.Height + 8;
        }
    }

    static string Short(long value) => value >= 1_000_000 ? $"{value / 1_000_000.0:0.#} mln" : value >= 1_000 ? $"{value / 1_000.0:0.#} tys." : value.ToString("N0", CultureInfo.GetCultureInfo("pl-PL"));
}

static class Api
{
    public static async Task<JsonObject> Read()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        var exe = Directory.Exists(root) ? Directory.GetFiles(root, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        var info = new ProcessStartInfo(exe ?? "codex.exe", "app-server --stdio") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(info) ?? throw new Exception("Nie można uruchomić Codex.");
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        async Task<JsonNode> Call(int id, string method, object? parameters = null)
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { id, method, @params = parameters }));
            await process.StandardInput.FlushAsync();
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync().WaitAsync(timeout.Token);
                if (line == null) throw new Exception("Połączenie z Codex zostało zamknięte. Uruchom Codex i sprawdź logowanie.");
                JsonNode? message;
                try { message = JsonNode.Parse(line); } catch { continue; }
                if (message?["id"]?.ToString() != id.ToString()) continue;
                if (message["error"] != null) throw new Exception(message["error"]?["message"]?.ToString() ?? "Błąd odczytu konta.");
                return message["result"] ?? new JsonObject();
            }
        }
        try
        {
            await Call(1, "initialize", new { clientInfo = new { name = "codex_meter", title = "Codex Meter", version = "1.0.0" }, capabilities = new { experimentalApi = true } });
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}");
            var result = new JsonObject();
            result["limits"] = (await Call(2, "account/rateLimits/read")).DeepCopy();
            try { result["usage"] = (await Call(3, "account/usage/read")).DeepCopy(); }
            catch (Exception ex) { result["usageError"] = ex.Message; }
            result["fetchedAt"] = DateTimeOffset.Now.ToString("O");
            return result;
        }
        finally { try { if (!process.HasExited) process.Kill(true); } catch { } }
    }
    public static JsonNode DeepCopy(this JsonNode node) => JsonNode.Parse(node.ToJsonString())!;
}

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

    public static void ExportBranding(string projectRoot)
    {
        string images = Path.Combine(projectRoot, "src", "WidgetPackage", "Images");
        string provider = Path.Combine(projectRoot, "src", "WidgetPackage", "ProviderAssets");
        string meterAssets = Path.Combine(projectRoot, "src", "Meter", "assets");
        SavePng(Path.Combine(provider, "CodexMeter_Icon.png"), 1254, 1254, 0.92f);
        SavePng(Path.Combine(images, "Square150x150Logo.png"), 300, 300, 0.82f);
        SavePng(Path.Combine(images, "Square150x150Logo.scale-200.png"), 300, 300, 0.82f);
        SavePng(Path.Combine(images, "Square44x44Logo.png"), 88, 88, 0.86f);
        SavePng(Path.Combine(images, "Square44x44Logo.scale-200.png"), 88, 88, 0.86f);
        SavePng(Path.Combine(images, "Square44x44Logo.targetsize-24_altform-unplated.png"), 24, 24, 0.96f);
        SavePng(Path.Combine(images, "StoreLogo.png"), 50, 50, 0.88f);
        SavePng(Path.Combine(images, "LockScreenLogo.scale-200.png"), 48, 48, 0.88f);
        SavePng(Path.Combine(images, "Wide310x150Logo.png"), 620, 300, 0.82f);
        SavePng(Path.Combine(images, "Wide310x150Logo.scale-200.png"), 620, 300, 0.82f);
        SavePng(Path.Combine(images, "SplashScreen.png"), 1240, 600, 0.82f);
        SavePng(Path.Combine(images, "SplashScreen.scale-200.png"), 1240, 600, 0.82f);
        SaveIco(Path.Combine(meterAssets, "codex-info.ico"));
    }

    static void SavePng(string path, int width, int height, float fill)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var canvas = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(canvas);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        int size = Math.Max(1, (int)Math.Round(Math.Min(width, height) * fill));
        using var logo = CreateLogo(size);
        graphics.DrawImage(logo, (width - size) / 2, (height - size) / 2, size, size);
        canvas.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    static void SaveIco(string path)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var images = new List<byte[]>();
        foreach (int size in sizes)
        {
            using var bitmap = CreateLogo(size);
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            images.Add(stream.ToArray());
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(images[i].Length); writer.Write(offset);
            offset += images[i].Length;
        }
        foreach (byte[] image in images) writer.Write(image);
    }

    static Bitmap Render(int size, string value, double usedPercent)
    {
        float scale = size / 32f;
        var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);
            using var background = new SolidBrush(Color.FromArgb(33, 33, 33));
            graphics.FillEllipse(background, scale, scale, 30 * scale, 30 * scale);
            using var track = new Pen(Color.FromArgb(76, 76, 76), 3.2f * scale);
            graphics.DrawEllipse(track, 3.2f * scale, 3.2f * scale, 25.6f * scale, 25.6f * scale);
            var color = usedPercent >= 90 ? Color.FromArgb(255, 111, 97)
                : usedPercent >= 75 ? Color.FromArgb(255, 180, 90)
                : Color.FromArgb(16, 163, 127);
            using var progress = new Pen(color, 3.2f * scale) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            graphics.DrawArc(progress, 3.2f * scale, 3.2f * scale, 25.6f * scale, 25.6f * scale, -90, (float)(Math.Clamp(usedPercent, 0, 100) * 3.6));
            float baseFontSize = value == "GPT" ? 8.2f : value.Length >= 3 ? 8.2f : 10.5f;
            using var font = new Font("Segoe UI", baseFontSize * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(graphics, value, font, new Rectangle((int)(4 * scale), (int)(5 * scale), (int)(24 * scale), (int)(22 * scale)), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
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

[ComImport, Guid("56FDF344-FD6D-11D0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
class TaskbarList { }

[ComImport, Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ITaskbarList3
{
    [PreserveSig] int HrInit();
    [PreserveSig] int AddTab(IntPtr hwnd);
    [PreserveSig] int DeleteTab(IntPtr hwnd);
    [PreserveSig] int ActivateTab(IntPtr hwnd);
    [PreserveSig] int SetActiveAlt(IntPtr hwnd);
    [PreserveSig] int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
    [PreserveSig] int SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
    [PreserveSig] int SetProgressState(IntPtr hwnd, TaskbarProgressState state);
    [PreserveSig] int RegisterTab(IntPtr hwndTab, IntPtr hwndMdi);
    [PreserveSig] int UnregisterTab(IntPtr hwndTab);
    [PreserveSig] int SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
    [PreserveSig] int SetTabActive(IntPtr hwndTab, IntPtr hwndMdi, uint reserved);
    [PreserveSig] int ThumbBarAddButtons(IntPtr hwnd, uint count, IntPtr buttons);
    [PreserveSig] int ThumbBarUpdateButtons(IntPtr hwnd, uint count, IntPtr buttons);
    [PreserveSig] int ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList);
    [PreserveSig] int SetOverlayIcon(IntPtr hwnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
    [PreserveSig] int SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string tooltip);
    [PreserveSig] int SetThumbnailClip(IntPtr hwnd, IntPtr clip);
}

class MeterForm : Form
{
    static readonly Color Surface = Color.FromArgb(33, 33, 33);
    static readonly Color Raised = Color.FromArgb(47, 47, 47);
    static readonly Color MainText = Color.FromArgb(236, 236, 236);
    static readonly Color MutedText = Color.FromArgb(180, 180, 180);
    static readonly Color Accent = Color.FromArgb(16, 163, 127);
    const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 0x2, WM_SETICON = 0x80, ICON_SMALL = 0, ICON_BIG = 1;
    const int WM_CLOSE = 0x10, WM_SYSCOMMAND = 0x112, SC_CLOSE = 0xF060, SC_MINIMIZE = 0xF020;
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string message);
    static readonly int TaskbarButtonCreatedMessage = RegisterWindowMessage("TaskbarButtonCreated");
    readonly NotifyIcon tray;
    readonly Panel body = new() { AutoScroll = true, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
    readonly Panel titleBar = new() { Dock = DockStyle.Top };
    readonly ToolTip tips = new() { AutoPopDelay = 15000 };
    readonly Label status = new() { AutoSize = false, Size = new Size(280, 18), ForeColor = Color.Gray, Font = new Font("Segoe UI", 8), Location = new Point(20, 233) };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 180000 };
    readonly System.Windows.Forms.Timer sessionDebounce = new() { Interval = 700 };
    readonly List<FileSystemWatcher> sessionWatchers = new();
    HashSet<string>? completedPromptKeys;
    readonly string dataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexMeter", "data");
    bool busy, quitting, keepOnTaskbar = true;
    bool chartOpen, promptHistoryOpen, localBusy;
    int chartMode = 1;
    LocalUsage? local;
    string? localError;
    string? remoteError;
    readonly Icon appIcon;
    Icon? trayGaugeIcon;
    ITaskbarList3? taskbar;
    double? latestUsed;
    bool taskbarMeterApplied;
    bool taskbarButtonCreated;
    string taskbarMeterResult = "nie uruchomiono";
    readonly float layoutScale;
    int S(int value) => Math.Max(1, (int)Math.Round(value * layoutScale));
    JsonObject? current;
    static string Number(long n) => n.ToString("N0", CultureInfo.GetCultureInfo("pl-PL"));
    public MeterForm(bool test = false)
    {
        appIcon = LoadAppIcon();
        using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) layoutScale = graphics.DpiX / 96f;
        Text = "Codex"; ClientSize = new Size(S(320), S(300)); AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None; MaximizeBox = false; MinimizeBox = true;
        BackColor = Surface; ForeColor = MainText; Font = new Font("Segoe UI", 9);
        StartPosition = FormStartPosition.Manual; ShowInTaskbar = true; Icon = appIcon;
        LoadSettings();
        titleBar.Height = S(36);
        body.Location = new Point(0, titleBar.Height); body.Size = new Size(ClientSize.Width, ClientSize.Height - titleBar.Height);
        PositionPanel();
        BuildTitleBar();
        Controls.Add(body); body.BringToFront(); titleBar.BringToFront();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Pokaż zużycie", null, (_, _) => Reveal());
        menu.Items.Add("Odśwież", null, async (_, _) => { await RefreshData(); await RefreshLocal(); });
        var xBehavior = new ToolStripMenuItem("Przycisk X");
        var minimizeWithX = new ToolStripMenuItem("Minimalizuj do paska zadań") { Checked = keepOnTaskbar };
        var closeWithX = new ToolStripMenuItem("Ukryj z paska zadań — zostaw w zasobniku") { Checked = !keepOnTaskbar };
        void SetXBehavior(bool keep)
        {
            keepOnTaskbar = keep;
            minimizeWithX.Checked = keep;
            closeWithX.Checked = !keep;
            SaveSettings();
        }
        minimizeWithX.Click += (_, _) => SetXBehavior(true);
        closeWithX.Click += (_, _) => SetXBehavior(false);
        xBehavior.DropDownItems.Add(minimizeWithX);
        xBehavior.DropDownItems.Add(closeWithX);
        menu.Items.Add(xBehavior);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Zakończ", null, (_, _) => { quitting = true; Close(); });
        tray = new NotifyIcon { Icon = Icon, Text = "Codex — odczytywanie zużycia", Visible = true, ContextMenuStrip = menu };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Reveal(); };
        FormClosed += (_, _) =>
        {
            timer.Stop(); timer.Dispose(); sessionDebounce.Stop(); sessionDebounce.Dispose();
            foreach (var watcher in sessionWatchers) watcher.Dispose();
            tray.Dispose(); trayGaugeIcon?.Dispose(); ReleaseTaskbar(); tips.Dispose(); appIcon.Dispose(); Application.ExitThread();
        };
        Shown += async (_, _) =>
        {
            for (int attempt = 0; attempt < 30 && !taskbarMeterApplied; attempt++)
            {
                ApplyTaskbarMeter();
                if (!taskbarMeterApplied) await Task.Delay(200);
            }
        };
        timer.Tick += async (_, _) => { await RefreshData(); await RefreshLocal(); };
        sessionDebounce.Tick += async (_, _) => { sessionDebounce.Stop(); await RefreshLocal(false); };
        if (test) current = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "probe.json"))) as JsonObject;
        else Shown += async (_, _) => { LoadCache(); await RefreshData(); await RefreshLocal(); StartSessionWatchers(); timer.Start(); };
        Render();
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (trayGaugeIcon != null)
        {
            SendMessage(Handle, WM_SETICON, (IntPtr)ICON_SMALL, trayGaugeIcon.Handle);
            SendMessage(Handle, WM_SETICON, (IntPtr)ICON_BIG, trayGaugeIcon.Handle);
        }
        ApplyTaskbarMeter();
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        ReleaseTaskbar();
        base.OnHandleDestroyed(e);
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == AppMessages.ShowWindow)
        {
            Reveal();
            return;
        }
        if (message.Msg == TaskbarButtonCreatedMessage)
        {
            taskbarButtonCreated = true;
            base.WndProc(ref message);
            ApplyTaskbarMeter();
            return;
        }
        bool taskbarCommand = message.Msg == WM_SYSCOMMAND;
        bool taskbarClose = taskbarCommand && (message.WParam.ToInt64() & 0xFFF0) == SC_CLOSE;
        bool taskbarMinimize = taskbarCommand && (message.WParam.ToInt64() & 0xFFF0) == SC_MINIMIZE;
        if (!quitting && taskbarMinimize)
        {
            MinimizeToTaskbar();
            return;
        }
        if (!quitting && (message.Msg == WM_CLOSE || taskbarClose))
        {
            if (keepOnTaskbar)
            {
                MinimizeToTaskbar();
                return;
            }
            HideToTray();
            return;
        }
        if ((message.Msg == WM_CLOSE || taskbarClose) && tray is not null) tray.Visible = false;
        base.WndProc(ref message);
    }
    static Icon LoadAppIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "codex-info.ico");
        return File.Exists(path) ? new Icon(path) : (Icon)SystemIcons.Information.Clone();
    }
    void UpdateTrayGauge(double? used, double? remaining, long? reset)
    {
        if (!used.HasValue) return;
        latestUsed = used.Value;
        var next = TrayGauge.Create(used.Value);
        tray.Icon = next;
        Icon = next;
        if (IsHandleCreated)
        {
            SendMessage(Handle, WM_SETICON, (IntPtr)ICON_SMALL, next.Handle);
            SendMessage(Handle, WM_SETICON, (IntPtr)ICON_BIG, next.Handle);
        }
        var previous = trayGaugeIcon;
        trayGaugeIcon = next;
        ApplyTaskbarMeter();
        previous?.Dispose();
        string resetText = reset.HasValue ? DateTimeOffset.FromUnixTimeSeconds(reset.Value).ToLocalTime().ToString("dd.MM HH:mm") : "?";
        string tooltip = $"ChatGPT/Codex: {used:0.#}% użyte · {remaining:0.#}% zostało · reset {resetText}";
        tray.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];
    }
    void ApplyTaskbarMeter()
    {
        if (!IsHandleCreated || !latestUsed.HasValue || trayGaugeIcon == null) return;
        try
        {
            taskbar ??= (ITaskbarList3)new TaskbarList();
            taskbar.HrInit();
            double used = Math.Clamp(latestUsed.Value, 0, 100);
            int stateResult = taskbar.SetProgressState(Handle, TaskbarProgressState.NoProgress);
            int overlayResult = taskbar.SetOverlayIcon(Handle, IntPtr.Zero, null!);
            taskbarMeterApplied = stateResult >= 0 && overlayResult >= 0;
            taskbarMeterResult = $"progress-cleared=0x{stateResult:X8}, overlay-cleared=0x{overlayResult:X8}";
            if (taskbarMeterApplied)
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "taskbar-state.txt"), $"PASS: {used:0}% · {taskbarMeterResult}");
        }
        catch (Exception ex) { taskbarMeterResult = ex.GetType().Name + ": " + ex.Message; ReleaseTaskbar(); }
    }
    void ReleaseTaskbar()
    {
        if (taskbar != null && Marshal.IsComObject(taskbar)) Marshal.FinalReleaseComObject(taskbar);
        taskbar = null;
    }
    void BuildTitleBar()
    {
        titleBar.BackColor = Surface;
        var title = new Label { Text = "Codex", Location = new Point(S(14), 0), Size = new Size(S(250), S(36)), TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10), ForeColor = MainText };
        var close = new Button { Text = "X", Location = new Point(S(278), 0), Size = new Size(S(42), S(36)), TabStop = false, FlatStyle = FlatStyle.Flat, BackColor = Raised, ForeColor = MainText, Font = new Font("Segoe UI", 8) };
        close.FlatAppearance.BorderSize = 0; close.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 43, 28); close.FlatAppearance.MouseDownBackColor = Color.FromArgb(153, 30, 22);
        tips.SetToolTip(close, "Minimalizuj lub ukryj do zasobnika — zgodnie z ustawieniem PPM");
        close.Click += (_, _) => Close();
        void Drag(object? sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero); } }
        titleBar.MouseDown += Drag; title.MouseDown += Drag;
        titleBar.Controls.Add(title); titleBar.Controls.Add(close); close.BringToFront(); Controls.Add(titleBar); titleBar.BringToFront();
    }
    void PositionPanel()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(Math.Max(area.Left, area.Right - Width - S(12)), Math.Max(area.Top, area.Bottom - Height - S(12)));
    }
    void MinimizeToTaskbar()
    {
        ShowInTaskbar = true;
        WindowState = FormWindowState.Minimized;
        ApplyTaskbarMeter();
    }
    void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }
    void LoadSettings()
    {
        try
        {
            string path = Path.Combine(dataDir, "settings.json");
            if (File.Exists(path)) keepOnTaskbar = JsonNode.Parse(File.ReadAllText(path))?["keepOnTaskbar"]?.GetValue<bool>() ?? true;
        }
        catch { keepOnTaskbar = true; }
    }
    void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "settings.json"), new JsonObject { ["keepOnTaskbar"] = keepOnTaskbar }.ToJsonString());
        }
        catch { }
    }
    void Reveal() { ShowInTaskbar = true; PositionPanel(); Show(); WindowState = FormWindowState.Normal; ApplyTaskbarMeter(); Activate(); }
    public void ExpandForPreview()
    {
        local = Analytics.Read(); chartOpen = true; promptHistoryOpen = true; Render();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "local-check.json"), JsonSerializer.Serialize(new { local.Files, local.Errors, Samples = local.Samples.Count, Prompts = local.Prompts.Count, Tokens = local.Samples.Sum(x => x.Tokens) }));
    }
    public void VerifyInteractions()
    {
        var chartData = new JsonObject();
        var chartSamples = new List<TokenSample>
        {
            new("widget-test-1", DateTimeOffset.UtcNow.AddMinutes(-30), 125),
            new("widget-test-2", DateTimeOffset.UtcNow.AddMinutes(-90), 375)
        };
        AddWidgetUsageRanges(chartData, new LocalUsage(chartSamples, new(), 1, 0));
        if (chartData["widgetHourlyUsage"]?["buckets"] is not JsonArray widgetBuckets || widgetBuckets.Count != 24 || chartData["widgetHourlyUsage"]?["totalTokens"]?.GetValue<long>() != 500)
            throw new InvalidOperationException("Błąd danych wykresu 24 h dla widżetu.");
        if (chartData["widgetUsageRanges"]?["7d"]?["buckets"] is not JsonArray sevenDayBuckets || sevenDayBuckets.Count != 7 ||
            chartData["widgetUsageRanges"]?["8h"]?["buckets"] is not JsonArray eightHourBuckets || eightHourBuckets.Count != 8 ||
            chartData["widgetUsageRanges"]?["1h"]?["buckets"] is not JsonArray oneHourBuckets || oneHourBuckets.Count != 12)
            throw new InvalidOperationException("Błąd zakresów 7d/8h/1h dla wykresu widżetu.");
        var taskbarDeadline = DateTime.UtcNow.AddSeconds(7);
        while (!taskbarMeterApplied && DateTime.UtcNow < taskbarDeadline)
        {
            Application.DoEvents();
            Thread.Sleep(25);
        }
        ApplyTaskbarMeter();
        if (!taskbarMeterResult.Contains("progress-cleared=0x00000000, overlay-cleared=0x00000000"))
            throw new InvalidOperationException($"Windows nie przyjął dynamicznej ikony: {taskbarMeterResult}; TaskbarButtonCreated={taskbarButtonCreated}");
        var closeButton = titleBar.Controls.OfType<Button>().Single(button => button.Text == "X");
        if (closeButton.FlatAppearance.MouseOverBackColor == closeButton.BackColor)
            throw new InvalidOperationException("Przycisk X nie ma widocznego stanu hover.");
        var saved = current;
        var date = DateTime.Today;
        current = new JsonObject { ["usage"] = new JsonObject { ["dailyUsageBuckets"] = new JsonArray(
            new JsonObject { ["startDate"] = date.ToString("yyyy-MM-dd"), ["tokens"] = 100L },
            new JsonObject { ["startDate"] = date.AddDays(-1).ToString("yyyy-MM-dd"), ["tokens"] = 250L }) } };
        for (int mode = 0; mode < 4; mode++)
        {
            chartMode = mode;
            var points = ChartPoints();
            if (points.Count != new[] { 24, 30, 12, 12 }[mode]) throw new Exception("Błąd liczby przedziałów wykresu.");
            if (mode > 0 && points.Sum(x => x.Tokens ?? 0) != 350) throw new Exception("Błąd agregacji dziennej/tygodniowej/miesięcznej.");
            if (mode == 1 && points.Count(x => x.Tokens == null) != 28) throw new Exception("Brak danych został zamieniony na zero.");
            Render(); Application.DoEvents();
            using var image = new Bitmap(Width, Height);
            DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save(Path.Combine(AppContext.BaseDirectory, $"view-{mode}.png"));
        }
        current = saved; chartMode = 1; Render();
        var sample = new PromptUsage { At = DateTimeOffset.Now, Text = "Przykładowy prompt do sprawdzenia podglądu.\n\nTreść pozostaje czytelna, można ją zaznaczyć i skopiować.", Tokens = 123456, Complete = true };
        using var dialog = CreatePromptDialog(sample, 1);
        dialog.Show(this); Application.DoEvents();
        using var shot = new Bitmap(dialog.Width, dialog.Height);
        dialog.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
        shot.Save(Path.Combine(AppContext.BaseDirectory, "prompt-preview.png"));
        dialog.Close();
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "analytics-test.txt"), "\nPASS: four chart modes, weekly/monthly aggregation, missing days and prompt dialog");
    }
    async Task RefreshLocal(bool showBusy = true)
    {
        if (localBusy) return;
        localBusy = true; localError = null; if (showBusy) Render();
        var completedNow = new List<PromptUsage>();
        try
        {
            var next = await Task.Run(Analytics.Read);
            var nextCompleted = next.Prompts.Where(x => x.Complete).Select(x => x.Key).ToHashSet();
            if (completedPromptKeys != null)
                completedNow = next.Prompts.Where(x => x.Complete && !completedPromptKeys.Contains(x.Key)).OrderBy(x => x.At).ToList();
            completedPromptKeys = nextCompleted;
            local = next;
        }
        catch (Exception ex) { localError = ex.Message; }
        finally
        {
            localBusy = false;
            if (!IsDisposed)
            {
                if (showBusy || completedNow.Count > 0) Render();
                foreach (var prompt in completedNow) CompletionPopup.Display(prompt);
            }
        }
    }
    void StartSessionWatchers()
    {
        if (sessionWatchers.Count > 0) return;
        string home = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        void QueueRefresh()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(() => { sessionDebounce.Stop(); sessionDebounce.Start(); }));
            }
            catch (InvalidOperationException) { }
        }
        foreach (string folder in new[] { "sessions", "archived_sessions" })
        {
            string path = Path.Combine(home, folder);
            if (!Directory.Exists(path)) continue;
            var watcher = new FileSystemWatcher(path, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Changed += (_, _) => QueueRefresh();
            watcher.Created += (_, _) => QueueRefresh();
            watcher.Renamed += (_, _) => QueueRefresh();
            watcher.EnableRaisingEvents = true;
            sessionWatchers.Add(watcher);
        }
    }
    void LoadCache()
    {
        try { current = JsonNode.Parse(File.ReadAllText(Path.Combine(dataDir, "latest.json"))) as JsonObject; Render(); status.Text = "Aktualizowanie…"; } catch { }
    }
    async Task RefreshData()
    {
        if (busy) return; busy = true; status.Text = "Aktualizowanie…";
        try
        {
            var next = await Api.Read();
            try
            {
                var localUsage = local ?? await Task.Run(Analytics.Read);
                AddWidgetUsageRanges(next, localUsage);
            }
            catch (Exception ex)
            {
                next["widgetHourlyUsageError"] = ex.Message;
            }
            if (IsDisposed) return;
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "latest.tmp"), next.ToJsonString());
            File.Move(Path.Combine(dataDir, "latest.tmp"), Path.Combine(dataDir, "latest.json"), true);
            File.AppendAllText(Path.Combine(dataDir, "history.jsonl"), next.ToJsonString() + Environment.NewLine);
            current = next; remoteError = null; Render();
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                status.Text = "Dane nieaktualne · ponowię za 3 min";
                tips.SetToolTip(status, ex.Message);
                remoteError = ex.Message;
                tray.Text = "Codex — dane nieaktualne";
            }
        }
        finally { busy = false; }
    }
    static void AddWidgetUsageRanges(JsonObject target, LocalUsage localUsage)
    {
        var now = DateTimeOffset.UtcNow;
        var currentHour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        JsonObject HourRange(int hours)
        {
            var buckets = new JsonArray();
            long total = 0;
            for (int index = hours - 1; index >= 0; index--)
            {
                var start = currentHour.AddHours(-index);
                var end = start.AddHours(1);
                long tokens = localUsage.Samples.Where(sample => sample.At >= start && sample.At < end).Sum(sample => sample.Tokens);
                total += tokens;
                buckets.Add(new JsonObject { ["start"] = start.ToString("O"), ["tokens"] = tokens });
            }
            return new JsonObject
            {
                ["buckets"] = buckets,
                ["totalTokens"] = total,
                ["from"] = currentHour.AddHours(-(hours - 1)).ToLocalTime().ToString("HH:mm"),
                ["to"] = "teraz"
            };
        }

        var sevenDays = new JsonArray();
        long sevenDayTotal = 0;
        var localToday = DateTime.Today;
        for (int index = 6; index >= 0; index--)
        {
            var day = localToday.AddDays(-index);
            long tokens = localUsage.Samples.Where(sample => sample.At.ToLocalTime().Date == day).Sum(sample => sample.Tokens);
            sevenDayTotal += tokens;
            sevenDays.Add(new JsonObject { ["start"] = day.ToString("yyyy-MM-dd"), ["tokens"] = tokens });
        }

        var currentFiveMinutes = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute / 5 * 5, 0, TimeSpan.Zero);
        var oneHour = new JsonArray();
        long oneHourTotal = 0;
        for (int index = 11; index >= 0; index--)
        {
            var start = currentFiveMinutes.AddMinutes(-5 * index);
            var end = start.AddMinutes(5);
            long tokens = localUsage.Samples.Where(sample => sample.At >= start && sample.At < end).Sum(sample => sample.Tokens);
            oneHourTotal += tokens;
            oneHour.Add(new JsonObject { ["start"] = start.ToString("O"), ["tokens"] = tokens });
        }

        var twentyFourHours = HourRange(24);
        target["widgetHourlyUsage"] = JsonNode.Parse(twentyFourHours.ToJsonString());
        target["widgetUsageRanges"] = new JsonObject
        {
            ["7d"] = new JsonObject
            {
                ["buckets"] = sevenDays,
                ["totalTokens"] = sevenDayTotal,
                ["from"] = localToday.AddDays(-6).ToString("dd.MM"),
                ["to"] = "dzisiaj"
            },
            ["24h"] = twentyFourHours,
            ["8h"] = HourRange(8),
            ["1h"] = new JsonObject
            {
                ["buckets"] = oneHour,
                ["totalTokens"] = oneHourTotal,
                ["from"] = currentFiveMinutes.AddMinutes(-55).ToLocalTime().ToString("HH:mm"),
                ["to"] = "teraz"
            }
        };
    }
    Label TextAt(string text, int x, int y, int width, int height, float size = 10, Color? color = null, bool bold = false)
    {
        var label = new Label { Text = text, Location = new Point(S(x), S(y)), Size = new Size(S(width), S(height)), Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color ?? ForeColor };
        body.Controls.Add(label); return label;
    }
    void Render()
    {
        SuspendLayout();
        body.SuspendLayout();
        body.Controls.Remove(status);
        foreach (Control control in body.Controls.Cast<Control>().ToArray()) control.Dispose();
        var limits = current?["limits"];
        var bucket = limits?["rateLimitsByLimitId"]?["codex"] ?? limits?["rateLimits"];
        var windows = new[] { bucket?["primary"], bucket?["secondary"] }.Where(x => x?["usedPercent"] != null).ToList();
        var window = windows.OrderByDescending(x => x!["usedPercent"]!.GetValue<double>()).FirstOrDefault();
        var used = window?["usedPercent"]?.GetValue<double?>();
        var reset = window?["resetsAt"]?.GetValue<long?>();
        var remaining = used.HasValue ? Math.Clamp(100 - used.Value, 0, 100) : (double?)null;
        var accent = remaining <= 10 ? Color.FromArgb(255, 180, 90) : Accent;
        TextAt("Pozostały limit", 20, 14, 280, 23, 9, MutedText);
        TextAt(remaining.HasValue ? $"{remaining:0.#}%" : "—", 19, 37, 280, 34, 20, accent, true);
        var bar = new Panel { Location = new Point(S(20), S(78)), Size = new Size(S(280), S(4)), BackColor = Color.FromArgb(48, 53, 62) };
        if (remaining.HasValue) bar.Controls.Add(new Panel { BackColor = accent, Size = new Size((int)Math.Round(S(280) * remaining.Value / 100), S(4)) });
        body.Controls.Add(bar);
        var resetLabel = TextAt("Reset: " + (reset.HasValue ? DateTimeOffset.FromUnixTimeSeconds(reset.Value).ToLocalTime().ToString("dd.MM · HH:mm") : "brak danych"), 20, 93, 280, 20, 8, MutedText);
        tips.SetToolTip(resetLabel, "Limit Codex o największym wykorzystaniu. Czas lokalny.");
        var todayStart = DateTime.Today;
        long? today = local == null ? null : local.Samples.Where(x => x.At.ToLocalTime().Date == todayStart).Sum(x => x.Tokens);
        TextAt("Tokeny dzisiaj", 20, 130, 140, 23, 9, MutedText);
        var todayValue = TextAt(today.HasValue ? Number(today.Value) : "brak danych", 155, 130, 145, 23);
        todayValue.TextAlign = ContentAlignment.TopRight;
        tips.SetToolTip(todayValue, "Tokeny z lokalnie zapisanych sesji Codex od północy czasu Windows.");
        status.Text = current?["fetchedAt"] is JsonNode stamp && DateTimeOffset.TryParse(stamp.ToString(), out var time) ? "Aktualizacja " + time.ToLocalTime().ToString("HH:mm") : "Oczekiwanie na dane…";
        tips.SetToolTip(status, "Odświeżanie co 3 minuty. Odśwież ręcznie w menu ikony przy zegarze.");
        if (remoteError != null) { status.Text = "Dane nieaktualne · ponowię za 3 min"; tips.SetToolTip(status, remoteError); }
        int y = 166;
        var chartToggle = SectionButton((chartOpen ? "▾" : "▸") + "  Wykres użycia", y);
        chartToggle.Click += async (_, _) => { chartOpen = !chartOpen; Render(); if (chartOpen && local == null) await RefreshLocal(); };
        y += 34;
        if (chartOpen) y = RenderChart(y);
        var promptHistoryToggle = SectionButton((promptHistoryOpen ? "▾" : "▸") + "  Ostatnie prompty", y);
        promptHistoryToggle.Click += async (_, _) => { promptHistoryOpen = !promptHistoryOpen; Render(); if (promptHistoryOpen && local == null) await RefreshLocal(); };
        y += 34;
        if (promptHistoryOpen) y = RenderPromptHistory(y);
        status.Location = new Point(S(20), S(y + 7)); status.Size = new Size(S(280), S(18)); body.Controls.Add(status);
        int targetHeight = y + 34;
        ClientSize = new Size(S(320), Math.Min(S(targetHeight + 36), Math.Max(S(346), Screen.FromControl(this).WorkingArea.Height - S(24))));
        body.Size = new Size(ClientSize.Width, ClientSize.Height - titleBar.Height);
        body.AutoScrollMinSize = new Size(0, S(targetHeight));
        if (used.HasValue) UpdateTrayGauge(used, remaining, reset);
        else tray.Text = "ChatGPT — brak danych o wykorzystaniu";
        body.ResumeLayout();
        ResumeLayout();
        PositionPanel();
    }

    Button SectionButton(string text, int y)
    {
        var button = new Button { Text = text, Location = new Point(S(17), S(y)), Size = new Size(S(283), S(30)), TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat, ForeColor = ForeColor, BackColor = BackColor, TabStop = true };
        button.FlatAppearance.BorderSize = 0; body.Controls.Add(button); return button;
    }
    Button SelectAt(string[] choices, int selected, int y, Action<int> changed)
    {
        var select = new Button { Text = choices[selected] + "    ▾", Location = new Point(S(20), S(y)), Size = new Size(S(280), S(27)), FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, BackColor = Raised, ForeColor = MainText, Font = new Font("Segoe UI", 8.5f), TabStop = true };
        select.FlatAppearance.BorderColor = Color.FromArgb(73, 73, 73);
        select.Click += (_, _) =>
        {
            var menu = new ContextMenuStrip { BackColor = Raised, ForeColor = MainText, ShowImageMargin = false, Font = new Font("Segoe UI", 9) };
            bool selectionQueued = false;
            for (int i = 0; i < choices.Length; i++)
            {
                int index = i; var item = menu.Items.Add((i == selected ? "✓  " : "    ") + choices[i]);
                item.Click += (_, _) =>
                {
                    if (selectionQueued) return;
                    selectionQueued = true;
                    BeginInvoke((Action)(() =>
                    {
                        // The drop-down finishes closing after Click. Update only afterwards.
                        if (!menu.IsDisposed) menu.Dispose();
                        if (IsDisposed) return;
                        changed(index);
                        Render();
                    }));
                };
            }
            menu.Show(select, new Point(0, select.Height));
        };
        body.Controls.Add(select); return select;
    }
    int RenderChart(int y)
    {
        SelectAt(new[] { "Godzinowy · ostatnie 24 h", "Dzienny · ostatnie 30 dni", "Tygodniowy · ostatnie 12 tyg.", "Miesięczny · ostatnie 12 mies." }, chartMode, y, i => chartMode = i);
        y += 33;
        var points = ChartPoints();
        if (chartMode == 0 && local == null)
            TextAt(localBusy ? "Odczytywanie historii…" : "Brak lokalnych danych", 20, y, 280, 150, 9, Color.Gray);
        else body.Controls.Add(new UsageChart(points) { Location = new Point(S(20), S(y)), Size = new Size(S(280), S(150)), BackColor = BackColor });
        y += 155;
        var caption = TextAt(chartMode == 0 ? "Lokalnie · czas Windows" : "Konto · kreska = brak danych", 20, y, 280, 21, 8, Color.Gray);
        tips.SetToolTip(caption, chartMode == 0 ? "Tokeny z zapisanych lokalnie sesji, również pomocniczych. Pozostałe urządzenia nie są uwzględnione. Najedź na punkt, aby zobaczyć liczbę." : "Dni według serwera. Tygodnie od poniedziałku, miesiące kalendarzowe. Niepełne okresy zawierają tylko dostępne dni — szczegóły po najechaniu.");
        return y + 28;
    }
    List<ChartBucket> ChartPoints()
    {
        var points = new List<ChartBucket>();
        if (chartMode == 0)
        {
            var now = DateTimeOffset.UtcNow;
            var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
            for (int i = 23; i >= 0; i--)
            {
                var start = hour.AddHours(-i); var end = start.AddHours(1);
                long? total = local == null ? null : local.Samples.Where(x => x.At >= start && x.At < end).Sum(x => x.Tokens);
                string label = start.ToLocalTime().ToString("HH:mm");
                points.Add(new ChartBucket(label, total, start.ToLocalTime().ToString("dd.MM HH:mm") + " · " + (total.HasValue ? Number(total.Value) : "brak danych") + " tokenów lokalnie"));
            }
            return points;
        }
        var days = new Dictionary<DateTime, long>();
        if (current?["usage"]?["dailyUsageBuckets"] is JsonArray daily)
            foreach (var item in daily)
                if (DateTime.TryParseExact(item?["startDate"]?.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && item?["tokens"] is JsonNode value) days[day] = value.GetValue<long>();
        DateTime today = DateTime.Today;
        int count = chartMode == 1 ? 30 : 12;
        var anchor = chartMode == 2 ? today.AddDays(-((int)today.DayOfWeek + 6) % 7) : chartMode == 3 ? new DateTime(today.Year, today.Month, 1) : today;
        for (int i = count - 1; i >= 0; i--)
        {
            var start = chartMode == 3 ? anchor.AddMonths(-i) : anchor.AddDays(-i * (chartMode == 2 ? 7 : 1));
            var end = chartMode == 3 ? start.AddMonths(1) : start.AddDays(chartMode == 2 ? 7 : 1);
            var available = days.Where(x => x.Key >= start && x.Key < end).ToList();
            long? tokens = available.Count == 0 ? null : available.Sum(x => x.Value);
            int expected = (int)(end - start).TotalDays;
            string label = start.ToString(chartMode == 3 ? "MM.yy" : "dd.MM");
            string detail = start.ToString("dd.MM.yyyy") + (chartMode == 1 ? "" : " – " + end.AddDays(-1).ToString("dd.MM.yyyy"));
            detail += tokens.HasValue ? "\n" + Number(tokens.Value) + " tokenów" : "\nBrak danych";
            if (chartMode != 1) detail += $"\nDostępne dni: {available.Count}/{expected}";
            points.Add(new ChartBucket(label, tokens, detail));
        }
        return points;
    }
    int RenderPromptHistory(int y)
    {
        var note = TextAt("Lokalna historia · 5 najnowszych", 20, y, 280, 20, 8, MutedText);
        tips.SetToolTip(note, "Pięć ostatnich promptów zapisanych przez Codex Meter. Kliknij pozycję, aby zobaczyć treść, datę i liczbę tokenów.");
        y += 23;
        var prompts = local?.Prompts.OrderByDescending(x => x.At).Take(5).ToList();
        if (prompts == null || prompts.Count == 0)
        {
            var label = TextAt(localBusy ? "Odczytywanie historii…" : localError != null ? "Nie udało się odczytać historii" : "Brak zapisanych promptów", 20, y, 280, 30, 9, Color.Gray);
            if (localError != null) tips.SetToolTip(label, localError);
            return y + 35;
        }
        const int rowHeight = 61;
        var list = new Panel { Location = new Point(S(17), S(y)), Size = new Size(S(286), S(prompts.Count * rowHeight)), AutoScroll = true };
        for (int i = 0; i < prompts.Count; i++)
        {
            var prompt = prompts[i]; var position = i + 1;
            string preview = string.Join(" ", prompt.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (preview.Length > 26) preview = preview[..26] + "…";
            string conversation = string.IsNullOrWhiteSpace(prompt.Conversation) ? "Rozmowa lokalna" : prompt.Conversation;
            if (conversation.Length > 27) conversation = conversation[..27] + "…";
            string model = string.IsNullOrWhiteSpace(prompt.Model) ? "Model: niedostępny" : prompt.Model;
            string effort = string.IsNullOrWhiteSpace(prompt.ReasoningEffort) ? "myślenie: niedostępne" : "myślenie: " + prompt.ReasoningEffort;
            var row = new Panel { Location = new Point(0, S(i * rowHeight)), Size = new Size(S(265), S(rowHeight - 1)), Cursor = Cursors.Hand };
            var inputText = "IN " + UsageChart.Short(prompt.InputTokens);
            var outputText = "  OUT " + UsageChart.Short(prompt.OutputTokens);
            var itemFont = new Font("Segoe UI", 7.3f);
            int inputWidth = TextRenderer.MeasureText(inputText, itemFont).Width;
            int outputWidth = TextRenderer.MeasureText(outputText, itemFont).Width;
            var input = new Label { Text = inputText, Location = new Point(S(4), S(2)), Size = new Size(inputWidth, S(18)), Font = itemFont, ForeColor = Accent };
            var output = new Label { Text = outputText, Location = new Point(S(4) + inputWidth, S(2)), Size = new Size(outputWidth, S(18)), Font = itemFont, ForeColor = Color.FromArgb(129, 230, 184) };
            var content = new Label { Text = " · " + preview, Location = new Point(S(4) + inputWidth + outputWidth, S(2)), Size = new Size(Math.Max(1, S(257) - inputWidth - outputWidth), S(18)), Font = itemFont, ForeColor = ForeColor };
            var context = new ConversationLine(prompt.At.ToLocalTime().ToString("HH:mm"), conversation) { Location = new Point(S(4), S(21)), Size = new Size(S(257), S(18)), ForeColor = ForeColor };
            var settings = new Label { Text = $"{model} · {effort}", Location = new Point(S(4), S(40)), Size = new Size(S(257), S(18)), Font = itemFont, ForeColor = ForeColor };
            row.Controls.Add(input); row.Controls.Add(output); row.Controls.Add(content); row.Controls.Add(context); row.Controls.Add(settings);
            string tooltip = $"Prompt {position} · IN {Number(prompt.InputTokens)} · OUT {Number(prompt.OutputTokens)}\n{prompt.At.ToLocalTime():dd.MM.yyyy HH:mm}\n{conversation}\n{model} · {effort}" + (prompt.Complete ? "" : " · w toku / brak zakończenia");
            foreach (Control control in row.Controls.Cast<Control>().Append(row))
            {
                tips.SetToolTip(control, tooltip);
                control.Click += (_, _) => ShowPrompt(prompt, position);
                control.MouseEnter += (_, _) => row.BackColor = Raised;
                control.MouseLeave += (_, _) => { if (!row.ClientRectangle.Contains(row.PointToClient(Cursor.Position))) row.BackColor = BackColor; };
            }
            list.Controls.Add(row);
        }
        body.Controls.Add(list);
        return y + prompts.Count * rowHeight + 10;
    }
    void ShowPrompt(PromptUsage prompt, int rank)
    {
        using var dialog = CreatePromptDialog(prompt, rank);
        dialog.ShowDialog(this);
    }
    Form CreatePromptDialog(PromptUsage prompt, int rank)
    {
        var dialog = new Form { Text = $"Prompt {rank}", ClientSize = new Size(S(470), S(290)), MinimumSize = new Size(S(340), S(230)), BackColor = BackColor, ForeColor = ForeColor, ShowInTaskbar = false, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, Font = new Font("Segoe UI", 10), AutoScaleMode = AutoScaleMode.None };
        string conversation = string.IsNullOrWhiteSpace(prompt.Conversation) ? "Rozmowa lokalna" : prompt.Conversation;
        string model = string.IsNullOrWhiteSpace(prompt.Model) ? "Model: niedostępny" : prompt.Model;
        string effort = string.IsNullOrWhiteSpace(prompt.ReasoningEffort) ? "myślenie: niedostępne" : "myślenie: " + prompt.ReasoningEffort;
        var header = new Label { Dock = DockStyle.Top, Height = S(66), Padding = new Padding(S(12), S(8), 0, 0), Font = new Font("Segoe UI Emoji", 9), Text = $"{prompt.At.ToLocalTime():dd.MM.yyyy · HH:mm}    IN {Number(prompt.InputTokens)} · OUT {Number(prompt.OutputTokens)}\n{conversation}\n{model} · {effort}" };
        var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(30, 34, 42), ForeColor = ForeColor, Text = prompt.Text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine) };
        var holder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(12), 0, S(12), S(12)) }; holder.Controls.Add(text);
        dialog.Controls.Add(holder); dialog.Controls.Add(header);
        dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
        dialog.Shown += (_, _) => { text.SelectionStart = 0; text.SelectionLength = 0; };
        return dialog;
    }
}
