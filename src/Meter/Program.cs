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
        using var mutex = new Mutex(true, args.Contains("--selftest") || args.Contains("--popup-preview") || args.Contains("--ui-selftest") ? "Local\\CodexMeterPreview" : "Local\\CodexMeterArkadiusz", out bool first);
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
        if (args.Contains("--ui-selftest"))
        {
            UiLayoutTests.Run(AppContext.BaseDirectory);
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
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "selftest.txt"), "PASS: panel rendering, interaction checks and chart ranges. See taskbar-test.txt for OS integration.");
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

partial class MeterForm : Form
{
    static readonly Color Surface = UiTheme.MainSurface;
    static readonly Color Raised = UiTheme.MenuSurface;
    static readonly Color MainText = UiTheme.MainText;
    static readonly Color MutedText = UiTheme.MainMuted;
    static readonly Color Accent = UiTheme.Accent;
    const int WM_SETICON = 0x80, WM_SETREDRAW = 0x0B, ICON_SMALL = 0, ICON_BIG = 1;
    const int WM_CLOSE = 0x10, WM_SYSCOMMAND = 0x112, SC_CLOSE = 0xF060, SC_MINIMIZE = 0xF020;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string message);
    static readonly int TaskbarButtonCreatedMessage = RegisterWindowMessage("TaskbarButtonCreated");
    readonly NotifyIcon tray;
    readonly Panel body = new() { AutoScroll = false, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
    readonly Panel titleBar = new() { Dock = DockStyle.Top };
    readonly ToolTip tips = new() { AutoPopDelay = 15000 };
    readonly Label status = new() { AutoSize = false, Size = new Size(280, 18), ForeColor = Color.Gray, Font = UiTheme.Font(8), Location = new Point(20, 233) };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 180000 };
    readonly System.Windows.Forms.Timer sessionDebounce = new() { Interval = 700 };
    readonly List<FileSystemWatcher> sessionWatchers = new();
    HashSet<string>? completedPromptKeys;
    readonly string dataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexMeter", "data");
    bool busy, quitting, keepOnTaskbar = true;
    bool chartOpen, promptHistoryOpen, localBusy;
    int chartMode = 0, chartTokenMode = 0;
    string chartConversationFilter = "";
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
    float layoutScale;
    Screen? pinnedScreen;
    int bodyContentHeight;
    int S(int value) => Math.Max(1, (int)Math.Round(value * layoutScale));
    JsonObject? current;
    static string Number(long n) => n.ToString("N0", L.Culture);
    public MeterForm(bool test = false)
    {
        appIcon = LoadAppIcon();
        layoutScale = UiDpi.ScaleForScreen(Screen.FromPoint(Cursor.Position));
        Text = "Codex Meter"; ClientSize = new Size(S(320), S(300)); AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None; MaximizeBox = false; MinimizeBox = true;
        BackColor = Surface; ForeColor = MainText; Font = UiTheme.Font(9);
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
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) RevealFromTray(); };
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
        if (test) current = TestRemoteUsage();
        else Shown += async (_, _) => { LoadCache(); await RefreshData(); await RefreshLocal(); StartSessionWatchers(); timer.Start(); };
        Render();
    }
    void BuildTitleBar()
    {
        titleBar.BackColor = Surface;
        var title = new Label { Text = "Codex Meter", Location = new Point(S(14), 0), Size = new Size(S(250), S(36)), TextAlign = ContentAlignment.MiddleLeft, Font = UiTheme.Font(10), ForeColor = MainText };
        var close = new Button { Text = "X", Location = new Point(S(278), 0), Size = new Size(S(42), S(36)), TabStop = false, FlatStyle = FlatStyle.Flat, BackColor = Raised, ForeColor = MainText, Font = UiTheme.Font(8) };
        close.FlatAppearance.BorderSize = 0; close.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 43, 28); close.FlatAppearance.MouseDownBackColor = Color.FromArgb(153, 30, 22);
        tips.SetToolTip(close, L.Pick("Minimalizuj lub ukryj do zasobnika — zgodnie z ustawieniem PPM", "Minimize or hide in the notification area — based on the right-click setting"));
        close.Click += (_, _) => Close();
        titleBar.Controls.Add(title); titleBar.Controls.Add(close); close.BringToFront(); Controls.Add(titleBar); titleBar.BringToFront();
    }
    static JsonObject TestRemoteUsage() => new()
    {
        ["fetchedAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        ["limits"] = new JsonObject
        {
            ["rateLimits"] = new JsonObject
            {
                ["primary"] = new JsonObject
                {
                    ["usedPercent"] = 40.0,
                    ["resetsAt"] = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds()
                }
            }
        }
    };
    void LayoutChrome()
    {
        titleBar.Height = S(36);
        var title = titleBar.Controls.OfType<Label>().Single();
        title.Location = new Point(S(14), 0);
        title.Size = new Size(S(250), S(36));
        var close = titleBar.Controls.OfType<Button>().Single();
        close.Location = new Point(S(278), 0);
        close.Size = new Size(S(42), S(36));
        body.Location = new Point(0, titleBar.Height);
        body.Size = new Size(ClientSize.Width, Math.Max(0, ClientSize.Height - titleBar.Height));
    }
    void ApplyDpi(int dpi, Screen target)
    {
        float nextScale = UiDpi.ScaleForDpi(dpi);
        if (Math.Abs(nextScale - layoutScale) < 0.001f) return;
        layoutScale = nextScale;
        LayoutChrome();
        Render();
        PositionPanel(target);
    }
    internal void ApplyDpiForTest(int dpi) => ApplyDpi(dpi, Screen.FromControl(this));
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyDpi(e.DeviceDpiNew, Screen.FromRectangle(e.SuggestedRectangle));
    }
    void PositionPanel(Screen? target = null)
    {
        // The panel is anchored once. Refreshes and tray clicks must never move it.
        var area = (pinnedScreen ??= target ?? Screen.FromPoint(Cursor.Position)).WorkingArea;
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
        // Do not preserve native scrollbars while the hidden form is being restored.
        body.AutoScroll = false;
        body.AutoScrollMinSize = Size.Empty;
        ShowInTaskbar = false;
        Hide();
    }
    void Reveal()
    {
        ShowInTaskbar = true;
        if (!Visible) Show();
        WindowState = FormWindowState.Normal;
        PositionPanel();
        ApplyTaskbarMeter();
        BringToFront();
        Activate();
        BeginInvoke((Action)(() => { UpdateBodyScrolling(); PositionPanel(); }));
    }
    void RevealFromTray() => Reveal();
    Label TextAt(string text, int x, int y, int width, int height, float size = 10, Color? color = null, bool bold = false)
    {
        var label = new Label { Text = text, Location = new Point(S(x), S(y)), Size = new Size(S(width), S(height)), Font = UiTheme.Font(size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color ?? ForeColor };
        body.Controls.Add(label); return label;
    }
    void Render()
    {
        SuspendLayout();
        body.SuspendLayout();
        body.AutoScroll = false;
        body.AutoScrollMinSize = Size.Empty;
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
        chartToggle.Click += async (_, _) => { chartOpen = !chartOpen; if (chartOpen) promptHistoryOpen = false; body.AutoScrollPosition = Point.Empty; Render(); if (chartOpen && local == null) await RefreshLocal(); };
        y += 34;
        if (chartOpen) y = RenderChart(y);
        var promptHistoryToggle = SectionButton((promptHistoryOpen ? "▾" : "▸") + "  " + L.Pick("Ostatnie prompty", "Recent prompts"), y);
        promptHistoryToggle.Click += async (_, _) => { promptHistoryOpen = !promptHistoryOpen; if (promptHistoryOpen) chartOpen = false; body.AutoScrollPosition = Point.Empty; Render(); if (promptHistoryOpen && local == null) await RefreshLocal(); };
        y += 34;
        if (promptHistoryOpen) y = RenderPromptHistory(y);
        status.Location = new Point(S(20), S(y + 7)); status.Size = new Size(S(280), S(18)); body.Controls.Add(status);
        int targetHeight = y + 34;
        ClientSize = new Size(S(320), Math.Min(S(targetHeight + 36), Math.Max(S(346), Screen.FromControl(this).WorkingArea.Height - S(24))));
        body.Size = new Size(ClientSize.Width, ClientSize.Height - titleBar.Height);
        bodyContentHeight = S(targetHeight);
        if (used.HasValue) UpdateTrayGauge(used, remaining, reset);
        else tray.Text = L.Pick("Codex Meter — brak danych o wykorzystaniu", "Codex Meter — no usage data");
        body.ResumeLayout();
        body.PerformLayout();
        if (bodyRedrawPaused)
        {
            SendMessage(body.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            body.Invalidate(true);
            body.Update();
        }
        ResumeLayout();
        UpdateBodyScrolling();
        PositionPanel();
    }

    void UpdateBodyScrolling()
    {
        bool needsScroll = bodyContentHeight > body.ClientSize.Height;
        body.AutoScroll = needsScroll;
        body.AutoScrollMinSize = needsScroll ? new Size(0, bodyContentHeight) : Size.Empty;
    }

    Button SectionButton(string text, int y)
    {
        var button = new Button { Text = text, Location = new Point(S(17), S(y)), Size = new Size(S(283), S(30)), TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat, ForeColor = ForeColor, BackColor = BackColor, TabStop = true };
        button.FlatAppearance.BorderSize = 0; body.Controls.Add(button); return button;
    }
    void ShowPrompt(PromptUsage prompt, int rank)
    {
        CompletionPopup.Display(prompt, closeAutomatically: false);
    }
}
