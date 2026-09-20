using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;

namespace CodexMeter;

static class L
{
    public static bool Polish => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("pl", StringComparison.OrdinalIgnoreCase);
    public static CultureInfo Culture => Polish ? CultureInfo.GetCultureInfo("pl-PL") : CultureInfo.GetCultureInfo("en-US");
    public static string Pick(string pl, string en) => Polish ? pl : en;
    public static string NoData => Pick("brak danych", "no data");
    public static string LocalConversation => Pick("Rozmowa lokalna", "Local conversation");
    public static string Thinking(string effort) => string.IsNullOrWhiteSpace(effort) ? Pick("myślenie: niedostępne", "thinking: unavailable") : Pick("myślenie: ", "thinking: ") + effort;
    public static string Short(long value) => value >= 1_000_000 ? $"{value / 1_000_000.0:0.#}{Pick(" mln", "M")}" : value >= 1_000 ? $"{value / 1_000.0:0.#}{Pick(" tys.", "K")}" : value.ToString("N0", Culture);
}

static class TokenVisuals
{
    static readonly Color Low = Color.FromArgb(176, 246, 194);
    static readonly Color Medium = Color.FromArgb(255, 221, 128);
    static readonly Color High = Color.FromArgb(255, 151, 151);

    // Input includes conversation context and is normally much larger than output.
    // The bands are intentionally independent.
    public static Color Input(long value) => value >= 1_000_000 ? High : value >= 250_000 ? Medium : Low;
    public static Color Output(long value) => value >= 5_000 ? High : value >= 1_000 ? Medium : Low;
}

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
        ClientSize = new Size(P(360), P(214));
        BackColor = Color.FromArgb(35, 35, 35);
        ForeColor = Color.FromArgb(242, 242, 242);
        Padding = new Padding(P(12), P(10), P(12), P(10));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, P(111)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(34)));
        for (int row = 0; row < 3; row++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(30)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(8)));
        for (int row = 0; row < 2; row++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(30)));

        var fieldColor = Color.FromArgb(170, 170, 170);
        var valueColor = Color.FromArgb(242, 242, 242);
        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = L.Pick("Zakończono przetwarzanie", "Processing completed"),
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = valueColor,
            TextAlign = ContentAlignment.MiddleLeft
        };
        string preview = string.Join(" ", prompt.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (preview.Length > 34) preview = preview[..34] + "…";
        string conversation = string.IsNullOrWhiteSpace(prompt.Conversation) ? L.LocalConversation : prompt.Conversation;
        string model = string.IsNullOrWhiteSpace(prompt.Model) ? L.Pick("niedostępny", "unavailable") : prompt.Model;
        string effort = L.Thinking(prompt.ReasoningEffort);
        var itemFont = new Font("Segoe UI", 9.5f);
        Label FieldLabel(string text) => new() { Text = text, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = fieldColor, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
        Label FieldValue(string text, Color? color = null) => new() { Text = text, Dock = DockStyle.Fill, Font = itemFont, ForeColor = color ?? valueColor, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
        var context = new ConversationLine("", conversation) { Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = valueColor, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        var separator = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, P(3), 0, P(4)), BackColor = Color.FromArgb(78, 78, 78), Height = P(1) };
        layout.Controls.Add(title, 0, 0);
        layout.SetColumnSpan(title, 2);
        layout.Controls.Add(FieldLabel(L.Pick("Rozmowa", "Conversation")), 0, 1);
        layout.Controls.Add(context, 1, 1);
        layout.Controls.Add(FieldLabel(L.Pick("Model", "Model")), 0, 2);
        layout.Controls.Add(FieldValue($"{model} · {effort}"), 1, 2);
        layout.Controls.Add(FieldLabel(L.Pick("Zapytanie", "Prompt")), 0, 3);
        layout.Controls.Add(FieldValue(preview), 1, 3);
        layout.Controls.Add(separator, 0, 4);
        layout.SetColumnSpan(separator, 2);
        layout.Controls.Add(FieldLabel(L.Pick("Tokeny IN", "Input tokens")), 0, 5);
        layout.Controls.Add(FieldValue(Short(prompt.InputTokens), TokenVisuals.Input(prompt.InputTokens)), 1, 5);
        layout.Controls.Add(FieldLabel(L.Pick("Tokeny OUT", "Output tokens")), 0, 6);
        layout.Controls.Add(FieldValue(Short(prompt.OutputTokens), TokenVisuals.Output(prompt.OutputTokens)), 1, 6);
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

    static string Short(long value) => L.Short(value);
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
    const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 0x2, WM_SETICON = 0x80, WM_SETREDRAW = 0x0B, ICON_SMALL = 0, ICON_BIG = 1;
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
    string taskbarMeterResult = "not started";
    readonly float layoutScale;
    int S(int value) => Math.Max(1, (int)Math.Round(value * layoutScale));
    JsonObject? current;
    static string Number(long n) => n.ToString("N0", L.Culture);
    public MeterForm(bool test = false)
    {
        appIcon = LoadAppIcon();
        using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) layoutScale = graphics.DpiX / 96f;
        Text = "Codex Meter"; ClientSize = new Size(S(320), S(300)); AutoScaleMode = AutoScaleMode.None;
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
        menu.Items.Add(L.Pick("Pokaż zużycie", "Show usage"), null, (_, _) => Reveal());
        menu.Items.Add(L.Pick("Odśwież", "Refresh"), null, async (_, _) => { await RefreshData(); await RefreshLocal(); });
        var xBehavior = new ToolStripMenuItem(L.Pick("Przycisk X", "X button"));
        var minimizeWithX = new ToolStripMenuItem(L.Pick("Minimalizuj do paska zadań", "Minimize to taskbar")) { Checked = keepOnTaskbar };
        var closeWithX = new ToolStripMenuItem(L.Pick("Ukryj z paska zadań — zostaw w zasobniku", "Hide from taskbar — keep in notification area")) { Checked = !keepOnTaskbar };
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
        menu.Items.Add(L.Pick("Zakończ", "Exit"), null, (_, _) => { quitting = true; Close(); });
        tray = new NotifyIcon { Icon = Icon, Text = L.Pick("Codex Meter — odczytywanie zużycia", "Codex Meter — reading usage"), Visible = true, ContextMenuStrip = menu };
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
        string tooltip = L.Polish
            ? $"{used:0.#}% użyte · {remaining:0.#}% zostało · reset {resetText}"
            : $"{used:0.#}% used · {remaining:0.#}% remaining · reset {resetText}";
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
        var title = new Label { Text = "Codex Meter", Location = new Point(S(14), 0), Size = new Size(S(250), S(36)), TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10), ForeColor = MainText };
        var close = new Button { Text = "X", Location = new Point(S(278), 0), Size = new Size(S(42), S(36)), TabStop = false, FlatStyle = FlatStyle.Flat, BackColor = Raised, ForeColor = MainText, Font = new Font("Segoe UI", 8) };
        close.FlatAppearance.BorderSize = 0; close.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 43, 28); close.FlatAppearance.MouseDownBackColor = Color.FromArgb(153, 30, 22);
        tips.SetToolTip(close, L.Pick("Minimalizuj lub ukryj do zasobnika — zgodnie z ustawieniem PPM", "Minimize or hide in the notification area — based on the right-click setting"));
        close.Click += (_, _) => Close();
        void Drag(object? sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero); } }
        titleBar.MouseDown += Drag; title.MouseDown += Drag;
        titleBar.Controls.Add(title); titleBar.Controls.Add(close); close.BringToFront(); Controls.Add(titleBar); titleBar.BringToFront();
    }
    void PositionPanel(Screen? target = null)
    {
        var area = (target ?? Screen.FromPoint(Cursor.Position)).WorkingArea;
        int x = Math.Clamp(area.Right - Width - S(12), area.Left, Math.Max(area.Left, area.Right - Width));
        int y = Math.Clamp(area.Bottom - Height - S(12), area.Top, Math.Max(area.Top, area.Bottom - Height));
        SetBounds(x, y, Width, Height, BoundsSpecified.Location);
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
    void Reveal()
    {
        var target = Screen.FromPoint(Cursor.Position);
        bool restoring = WindowState == FormWindowState.Minimized;
        if (restoring) Hide();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        PositionPanel(target);
        if (!Visible) Show();
        PositionPanel(target);
        ApplyTaskbarMeter();
        Activate();
        BeginInvoke((Action)(() => PositionPanel(target)));
    }
    public void ExpandForPreview()
    {
        local = Analytics.Read(); chartOpen = true; promptHistoryOpen = true; Render();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "local-check.json"), JsonSerializer.Serialize(new { local.Files, local.Errors, Samples = local.Samples.Count, Prompts = local.Prompts.Count, Tokens = local.Samples.Sum(x => x.Tokens) }));
    }
    public void VerifyInteractions()
    {
        var taskbarDeadline = DateTime.UtcNow.AddSeconds(7);
        while (!taskbarMeterApplied && DateTime.UtcNow < taskbarDeadline)
        {
            Application.DoEvents();
            Thread.Sleep(25);
        }
        ApplyTaskbarMeter();
        PositionPanel();
        if (!Screen.FromControl(this).WorkingArea.Contains(Bounds))
            throw new InvalidOperationException("Panel znajduje się poza obszarem roboczym ekranu.");
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
        try { current = JsonNode.Parse(File.ReadAllText(Path.Combine(dataDir, "latest.json"))) as JsonObject; Render(); status.Text = L.Pick("Aktualizowanie…", "Updating…"); } catch { }
    }
    async Task RefreshData()
    {
        if (busy) return; busy = true; status.Text = L.Pick("Aktualizowanie…", "Updating…");
        try
        {
            var next = await Api.Read();
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
                status.Text = L.Pick("Dane nieaktualne · ponowię za 3 min", "Data is stale · retrying in 3 min");
                tips.SetToolTip(status, ex.Message);
                remoteError = ex.Message;
                tray.Text = L.Pick("Codex Meter — dane nieaktualne", "Codex Meter — data is stale");
            }
        }
        finally { busy = false; }
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
        bool bodyRedrawPaused = body.IsHandleCreated;
        if (bodyRedrawPaused) SendMessage(body.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
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
        TextAt(L.Pick("Pozostały limit", "Remaining limit"), 20, 14, 280, 23, 9, MutedText);
        TextAt(remaining.HasValue ? $"{remaining:0.#}%" : "—", 19, 37, 280, 34, 20, accent, true);
        var bar = new Panel { Location = new Point(S(20), S(78)), Size = new Size(S(280), S(4)), BackColor = Color.FromArgb(48, 53, 62) };
        if (remaining.HasValue) bar.Controls.Add(new Panel { BackColor = accent, Size = new Size((int)Math.Round(S(280) * remaining.Value / 100), S(4)) });
        body.Controls.Add(bar);
        var resetLabel = TextAt(L.Pick("Reset: ", "Reset: ") + (reset.HasValue ? DateTimeOffset.FromUnixTimeSeconds(reset.Value).ToLocalTime().ToString("dd.MM · HH:mm", L.Culture) : L.NoData), 20, 93, 280, 20, 8, MutedText);
        tips.SetToolTip(resetLabel, L.Pick("Limit Codex o największym wykorzystaniu. Czas lokalny.", "The Codex limit with the highest usage. Local time."));
        var todayStart = DateTime.Today;
        long? today = local == null ? null : local.Samples.Where(x => x.At.ToLocalTime().Date == todayStart).Sum(x => x.Tokens);
        TextAt(L.Pick("Tokeny dzisiaj", "Tokens today"), 20, 130, 140, 23, 9, MutedText);
        var todayValue = TextAt(today.HasValue ? Number(today.Value) : L.NoData, 155, 130, 145, 23);
        todayValue.TextAlign = ContentAlignment.TopRight;
        tips.SetToolTip(todayValue, L.Pick("Tokeny z lokalnie zapisanych sesji Codex od północy czasu Windows.", "Tokens from locally stored Codex sessions since local midnight."));
        status.Text = current?["fetchedAt"] is JsonNode stamp && DateTimeOffset.TryParse(stamp.ToString(), out var time) ? L.Pick("Aktualizacja ", "Updated ") + time.ToLocalTime().ToString("HH:mm", L.Culture) : L.Pick("Oczekiwanie na dane…", "Waiting for data…");
        tips.SetToolTip(status, L.Pick("Odświeżanie co 3 minuty. Odśwież ręcznie w menu ikony przy zegarze.", "Refreshes every 3 minutes. Refresh manually from the notification-area icon menu."));
        if (remoteError != null) { status.Text = L.Pick("Dane nieaktualne · ponowię za 3 min", "Data is stale · retrying in 3 min"); tips.SetToolTip(status, remoteError); }
        int y = 166;
        var chartToggle = SectionButton((chartOpen ? "▾" : "▸") + "  " + L.Pick("Wykres użycia", "Usage chart"), y);
        chartToggle.Click += async (_, _) => { chartOpen = !chartOpen; Render(); if (chartOpen && local == null) await RefreshLocal(); };
        y += 34;
        if (chartOpen) y = RenderChart(y);
        var promptHistoryToggle = SectionButton((promptHistoryOpen ? "▾" : "▸") + "  " + L.Pick("Ostatnie prompty", "Recent prompts"), y);
        promptHistoryToggle.Click += async (_, _) => { promptHistoryOpen = !promptHistoryOpen; Render(); if (promptHistoryOpen && local == null) await RefreshLocal(); };
        y += 34;
        if (promptHistoryOpen) y = RenderPromptHistory(y);
        status.Location = new Point(S(20), S(y + 7)); status.Size = new Size(S(280), S(18)); body.Controls.Add(status);
        int targetHeight = y + 34;
        ClientSize = new Size(S(320), Math.Min(S(targetHeight + 36), Math.Max(S(346), Screen.FromControl(this).WorkingArea.Height - S(24))));
        body.Size = new Size(ClientSize.Width, ClientSize.Height - titleBar.Height);
        body.AutoScrollMinSize = new Size(0, S(targetHeight));
        if (used.HasValue) UpdateTrayGauge(used, remaining, reset);
        else tray.Text = L.Pick("Codex Meter — brak danych o wykorzystaniu", "Codex Meter — no usage data");
        body.ResumeLayout();
        if (bodyRedrawPaused)
        {
            SendMessage(body.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            body.Invalidate(true);
            body.Update();
        }
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
        SelectAt(L.Polish
            ? new[] { "Godzinowy · ostatnie 24 h", "Dzienny · ostatnie 30 dni", "Tygodniowy · ostatnie 12 tyg.", "Miesięczny · ostatnie 12 mies." }
            : new[] { "Hourly · last 24 h", "Daily · last 30 days", "Weekly · last 12 weeks", "Monthly · last 12 months" }, chartMode, y, i => chartMode = i);
        y += 33;
        var points = ChartPoints();
        if (chartMode == 0 && local == null)
            TextAt(localBusy ? L.Pick("Odczytywanie historii…", "Reading history…") : L.Pick("Brak lokalnych danych", "No local data"), 20, y, 280, 150, 9, Color.Gray);
        else body.Controls.Add(new UsageChart(points) { Location = new Point(S(20), S(y)), Size = new Size(S(280), S(150)), BackColor = BackColor });
        y += 155;
        var caption = TextAt(chartMode == 0 ? L.Pick("Lokalnie · czas Windows", "Local · Windows time") : L.Pick("Konto · kreska = brak danych", "Account · dash = no data"), 20, y, 280, 21, 8, Color.Gray);
        tips.SetToolTip(caption, chartMode == 0 ? L.Pick("Tokeny z zapisanych lokalnie sesji, również pomocniczych. Pozostałe urządzenia nie są uwzględnione. Najedź na punkt, aby zobaczyć liczbę.", "Tokens from locally stored sessions, including helper sessions. Other devices are not included. Hover a point to see its value.") : L.Pick("Dni według serwera. Tygodnie od poniedziałku, miesiące kalendarzowe. Niepełne okresy zawierają tylko dostępne dni — szczegóły po najechaniu.", "Days come from the server. Weeks start Monday and months are calendar months. Incomplete periods include only available days — hover for details."));
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
                points.Add(new ChartBucket(label, total, start.ToLocalTime().ToString("g", L.Culture) + " · " + (total.HasValue ? Number(total.Value) : L.NoData) + L.Pick(" tokenów lokalnie", " local tokens")));
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
            detail += tokens.HasValue ? "\n" + Number(tokens.Value) + L.Pick(" tokenów", " tokens") : "\n" + L.Pick("Brak danych", "No data");
            if (chartMode != 1) detail += L.Pick($"\nDostępne dni: {available.Count}/{expected}", $"\nAvailable days: {available.Count}/{expected}");
            points.Add(new ChartBucket(label, tokens, detail));
        }
        return points;
    }
    int RenderPromptHistory(int y)
    {
        var note = TextAt(L.Pick("Lokalna historia · 3 najnowsze", "Local history · 3 newest"), 20, y, 280, 20, 8, MutedText);
        tips.SetToolTip(note, L.Pick("Trzy ostatnie prompty zapisane przez Codex Meter. Kliknij pozycję, aby zobaczyć treść, datę i liczbę tokenów.", "The three most recent prompts saved by Codex Meter. Click an item to see its text, date, and token count."));
        y += 23;
        var prompts = local?.Prompts.OrderByDescending(x => x.At).Take(3).ToList();
        if (prompts == null || prompts.Count == 0)
        {
            var label = TextAt(localBusy ? L.Pick("Odczytywanie historii…", "Reading history…") : localError != null ? L.Pick("Nie udało się odczytać historii", "Could not read history") : L.Pick("Brak zapisanych promptów", "No saved prompts"), 20, y, 280, 30, 9, Color.Gray);
            if (localError != null) tips.SetToolTip(label, localError);
            return y + 35;
        }
        const int rowContentHeight = 101;
        const int rowGap = 8;
        const int rowHeight = rowContentHeight + rowGap;
        var list = new Panel { Location = new Point(S(17), S(y)), Size = new Size(S(286), S(prompts.Count * rowHeight)) };
        for (int i = 0; i < prompts.Count; i++)
        {
            var prompt = prompts[i]; var position = i + 1;
            string preview = string.Join(" ", prompt.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (preview.Length > 34) preview = preview[..34] + "…";
            string conversation = string.IsNullOrWhiteSpace(prompt.Conversation) ? L.LocalConversation : prompt.Conversation;
            string model = string.IsNullOrWhiteSpace(prompt.Model) ? L.Pick("niedostępny", "unavailable") : prompt.Model;
            string effort = L.Thinking(prompt.ReasoningEffort);
            var row = new Panel { Location = new Point(0, S(i * rowHeight)), Size = new Size(S(280), S(rowContentHeight)), Cursor = Cursors.Hand };
            var itemFont = new Font("Segoe UI", 7.3f);
            Label Field(string text, int top) => new() { Text = text, Location = new Point(S(4), S(top)), Size = new Size(S(74), S(17)), Font = new Font("Segoe UI", 7.3f, FontStyle.Bold), ForeColor = MutedText };
            Label Value(string text, int top, Color? color = null) => new() { Text = text, Location = new Point(S(82), S(top)), Size = new Size(S(175), S(17)), Font = itemFont, ForeColor = color ?? ForeColor, AutoEllipsis = true };
            var context = new ConversationLine("", conversation) { Location = new Point(S(82), S(2)), Size = new Size(S(175), S(17)), ForeColor = ForeColor, Font = new Font("Segoe UI", 7.3f, FontStyle.Bold) };
            row.Controls.Add(Field(L.Pick("Rozmowa", "Conversation"), 2)); row.Controls.Add(context);
            row.Controls.Add(Field(L.Pick("Model", "Model"), 20)); row.Controls.Add(Value($"{model} · {effort}", 20));
            row.Controls.Add(Field(L.Pick("Zapytanie", "Prompt"), 38)); row.Controls.Add(Value(preview, 38));
            row.Controls.Add(Field(L.Pick("Tokeny IN", "Input tokens"), 57)); row.Controls.Add(Value(UsageChart.Short(prompt.InputTokens), 57, TokenVisuals.Input(prompt.InputTokens)));
            row.Controls.Add(Field(L.Pick("Tokeny OUT", "Output tokens"), 75)); row.Controls.Add(Value(UsageChart.Short(prompt.OutputTokens), 75, TokenVisuals.Output(prompt.OutputTokens)));
            string occurredAt = prompt.At.ToLocalTime().ToString("dd.MM.yyyy · HH:mm", L.Culture);
            foreach (Control control in row.Controls.Cast<Control>().Append(row))
            {
                control.Click += (_, _) => ShowPrompt(prompt, position);
                tips.SetToolTip(control, occurredAt);
            }
            list.Controls.Add(row);
            if (i < prompts.Count - 1)
                list.Controls.Add(new Panel { Location = new Point(S(1), S((i + 1) * rowHeight - rowGap / 2)), Size = new Size(S(278), S(1)), BackColor = Color.FromArgb(96, 96, 96) });
        }
        body.Controls.Add(list);
        return y + prompts.Count * rowHeight + 2;
    }
    void ShowPrompt(PromptUsage prompt, int rank)
    {
        using var dialog = CreatePromptDialog(prompt, rank);
        dialog.ShowDialog(this);
    }
    Form CreatePromptDialog(PromptUsage prompt, int rank)
    {
        string compactTitle = string.Join(" ", prompt.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        string dialogTitle = string.IsNullOrWhiteSpace(compactTitle)
            ? $"{L.Pick("Szczegóły promptu", "Prompt details")} {rank}"
            : (compactTitle.Length <= 54 ? compactTitle : compactTitle[..54] + "…");
        var dialog = new Form
        {
            Text = dialogTitle,
            ClientSize = new Size(S(470), S(330)),
            MinimumSize = new Size(S(340), S(230)),
            BackColor = Surface,
            ForeColor = MainText,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            FormBorderStyle = FormBorderStyle.None,
            Font = new Font("Segoe UI", 10),
            AutoScaleMode = AutoScaleMode.None
        };
        string conversation = string.IsNullOrWhiteSpace(prompt.Conversation) ? L.LocalConversation : prompt.Conversation;
        string model = string.IsNullOrWhiteSpace(prompt.Model) ? L.Pick("niedostępny", "unavailable") : prompt.Model;
        string effort = L.Thinking(prompt.ReasoningEffort);
        var titleBar = new Panel { Dock = DockStyle.Top, Height = S(36), BackColor = Surface };
        var title = new Label { Text = dialog.Text, Location = new Point(S(14), 0), Size = new Size(S(400), S(36)), TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = MainText };
        var close = new Button { Text = "X", Dock = DockStyle.Right, Width = S(42), FlatStyle = FlatStyle.Flat, BackColor = Raised, ForeColor = MainText, Font = new Font("Segoe UI", 8), TabStop = false };
        close.FlatAppearance.BorderSize = 0; close.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 43, 28); close.FlatAppearance.MouseDownBackColor = Color.FromArgb(153, 30, 22);
        close.Click += (_, _) => dialog.Close();
        void Drag(object? sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(dialog.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero); } }
        titleBar.MouseDown += Drag; title.MouseDown += Drag;
        titleBar.Controls.Add(title); titleBar.Controls.Add(close);

        var fields = new TableLayoutPanel { Dock = DockStyle.Top, Height = S(119), Padding = new Padding(S(12), S(5), S(12), 0), ColumnCount = 2, RowCount = 4 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(84))); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 4; row++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, S(27)));
        var fieldColor = Color.FromArgb(170, 170, 170);
        Label Field(string value) => new() { Text = value, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = fieldColor, TextAlign = ContentAlignment.MiddleLeft };
        Label Value(string value, Color? color = null) => new() { Text = value, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8.5f), ForeColor = color ?? MainText, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        var dialogConversation = new ConversationLine("", conversation) { Dock = DockStyle.Fill, ForeColor = MainText, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
        fields.Controls.Add(Field(L.Pick("Rozmowa", "Conversation")), 0, 0); fields.Controls.Add(dialogConversation, 1, 0);
        fields.Controls.Add(Field(L.Pick("Model", "Model")), 0, 1); fields.Controls.Add(Value($"{model} · {effort}"), 1, 1);
        fields.Controls.Add(Field(L.Pick("Tokeny IN", "Input tokens")), 0, 2); fields.Controls.Add(Value(Number(prompt.InputTokens), TokenVisuals.Input(prompt.InputTokens)), 1, 2);
        fields.Controls.Add(Field(L.Pick("Tokeny OUT", "Output tokens")), 0, 3); fields.Controls.Add(Value(Number(prompt.OutputTokens), TokenVisuals.Output(prompt.OutputTokens)), 1, 3);
        var divider = new Panel { Dock = DockStyle.Top, Height = S(1), Margin = new Padding(S(12), 0, S(12), 0), BackColor = Color.FromArgb(78, 78, 78) };
        var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.None, BorderStyle = BorderStyle.None, BackColor = Surface, ForeColor = MainText, Font = new Font("Segoe UI", 9), Text = prompt.Text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine) };
        var holder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(12), S(10), S(12), S(12)), BackColor = Surface }; holder.Controls.Add(text);
        dialog.Controls.Add(holder); dialog.Controls.Add(divider); dialog.Controls.Add(fields); dialog.Controls.Add(titleBar);
        dialog.Shown += (_, _) => { text.SelectionStart = 0; text.SelectionLength = 0; };
        return dialog;
    }
}
