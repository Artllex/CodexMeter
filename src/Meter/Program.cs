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
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--probe"))
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "probe.json"), Api.Read().GetAwaiter().GetResult().ToJsonString(new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "probe.json"), new JsonObject { ["error"] = ex.Message }.ToJsonString()); Environment.ExitCode = 1; }
            return;
        }
        using var mutex = new Mutex(true, args.Contains("--selftest") ? "Local\\CodexMeterPreview" : "Local\\CodexMeterArkadiusz", out bool first);
        if (!first) { AppMessages.ShowExisting(); return; }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
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
        const int size = 32;
        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var background = new SolidBrush(Color.FromArgb(33, 33, 33));
            graphics.FillEllipse(background, 1, 1, 30, 30);
            using var track = new Pen(Color.FromArgb(76, 76, 76), 3.2f);
            graphics.DrawEllipse(track, 3.2f, 3.2f, 25.6f, 25.6f);
            var color = usedPercent >= 90 ? Color.FromArgb(255, 111, 97)
                : usedPercent >= 75 ? Color.FromArgb(255, 180, 90)
                : Color.FromArgb(16, 163, 127);
            using var progress = new Pen(color, 3.2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            graphics.DrawArc(progress, 3.2f, 3.2f, 25.6f, 25.6f, -90, (float)(Math.Clamp(usedPercent, 0, 100) * 3.6));
            string value = Math.Round(usedPercent).ToString("0", CultureInfo.InvariantCulture);
            float fontSize = value.Length >= 3 ? 8.2f : 10.5f;
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(graphics, value, font, new Rectangle(4, 5, 24, 22), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally { DestroyIcon(handle); }
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
    const int WM_CLOSE = 0x10, WM_SYSCOMMAND = 0x112, SC_CLOSE = 0xF060;
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
    readonly string dataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexMeter", "data");
    bool busy, quitting;
    bool chartOpen, rankingOpen, localBusy;
    int chartMode = 1, rankingPeriod = 1;
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
        FormBorderStyle = FormBorderStyle.None; MaximizeBox = false; MinimizeBox = false;
        BackColor = Surface; ForeColor = MainText; Font = new Font("Segoe UI", 9);
        StartPosition = FormStartPosition.Manual; ShowInTaskbar = true; Icon = appIcon;
        titleBar.Height = S(36);
        body.Location = new Point(0, titleBar.Height); body.Size = new Size(ClientSize.Width, ClientSize.Height - titleBar.Height);
        PositionPanel();
        BuildTitleBar();
        Controls.Add(body); body.BringToFront(); titleBar.BringToFront();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Pokaż zużycie", null, (_, _) => Reveal());
        menu.Items.Add("Odśwież", null, async (_, _) => { await RefreshData(); await RefreshLocal(); });
        menu.Items.Add("Zakończ", null, (_, _) => { quitting = true; Close(); });
        tray = new NotifyIcon { Icon = Icon, Text = "Codex — odczytywanie zużycia", Visible = true, ContextMenuStrip = menu };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Reveal(); };
        FormClosed += (_, _) => { timer.Stop(); timer.Dispose(); tray.Dispose(); trayGaugeIcon?.Dispose(); ReleaseTaskbar(); tips.Dispose(); appIcon.Dispose(); Application.ExitThread(); };
        Shown += async (_, _) =>
        {
            for (int attempt = 0; attempt < 30 && !taskbarMeterApplied; attempt++)
            {
                ApplyTaskbarMeter();
                if (!taskbarMeterApplied) await Task.Delay(200);
            }
        };
        timer.Tick += async (_, _) => { await RefreshData(); if (chartOpen || rankingOpen) await RefreshLocal(); };
        if (test) current = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "probe.json"))) as JsonObject;
        else Shown += async (_, _) => { LoadCache(); await RefreshData(); timer.Start(); };
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
        bool taskbarClose = message.Msg == WM_SYSCOMMAND && (message.WParam.ToInt64() & 0xFFF0) == SC_CLOSE;
        if (!quitting && (message.Msg == WM_CLOSE || taskbarClose))
        {
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
            var state = used >= 90 ? TaskbarProgressState.Error : used >= 75 ? TaskbarProgressState.Paused : TaskbarProgressState.Normal;
            int stateResult = taskbar.SetProgressState(Handle, state);
            int valueResult = taskbar.SetProgressValue(Handle, (ulong)Math.Round(used * 10), 1000);
            int overlayResult = taskbar.SetOverlayIcon(Handle, trayGaugeIcon.Handle, $"{used:0}% użyte");
            taskbarMeterApplied = stateResult >= 0 && valueResult >= 0 && overlayResult >= 0;
            taskbarMeterResult = $"state=0x{stateResult:X8}, value=0x{valueResult:X8}, overlay=0x{overlayResult:X8}";
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
        tips.SetToolTip(close, "Schowaj do zasobnika");
        close.Click += (_, _) => HideToTray();
        void Drag(object? sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero); } }
        titleBar.MouseDown += Drag; title.MouseDown += Drag;
        titleBar.Controls.Add(title); titleBar.Controls.Add(close); close.BringToFront(); Controls.Add(titleBar); titleBar.BringToFront();
    }
    void PositionPanel()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(Math.Max(area.Left, area.Right - Width - S(12)), Math.Max(area.Top, area.Bottom - Height - S(12)));
    }
    void HideToTray()
    {
        ShowInTaskbar = true;
        WindowState = FormWindowState.Minimized;
        ApplyTaskbarMeter();
    }
    void Reveal() { ShowInTaskbar = true; PositionPanel(); Show(); WindowState = FormWindowState.Normal; ApplyTaskbarMeter(); Activate(); }
    public void ExpandForPreview()
    {
        local = Analytics.Read(); chartOpen = true; rankingOpen = true; Render();
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
        if (!taskbarMeterResult.Contains("state=0x00000000, value=0x00000000"))
            throw new InvalidOperationException($"Windows nie przyjął dynamicznego paska postępu: {taskbarMeterResult}; TaskbarButtonCreated={taskbarButtonCreated}");
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
    async Task RefreshLocal()
    {
        if (localBusy) return;
        localBusy = true; localError = null; Render();
        try { local = await Task.Run(Analytics.Read); }
        catch (Exception ex) { localError = ex.Message; }
        finally { localBusy = false; if (!IsDisposed) Render(); }
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
                var localUsage = await Task.Run(Analytics.Read);
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
        var daily = current?["usage"]?["dailyUsageBuckets"] as JsonArray;
        var today = daily?.FirstOrDefault(x => x?["startDate"]?.ToString() == DateTime.Now.ToString("yyyy-MM-dd"))?["tokens"]?.GetValue<long?>();
        TextAt("Tokeny dzisiaj", 20, 130, 140, 23, 9, MutedText);
        var todayValue = TextAt(today.HasValue ? Number(today.Value) : "brak danych", 155, 130, 145, 23);
        todayValue.TextAlign = ContentAlignment.TopRight;
        tips.SetToolTip(todayValue, "Dzienne zestawienie serwera może być opóźnione. Brak danych nie oznacza zera.");
        TextAt("Tokeny od resetu", 20, 160, 150, 23, 9, MutedText);
        var resetValue = TextAt("niedostępne", 170, 160, 130, 23);
        resetValue.TextAlign = ContentAlignment.TopRight;
        tips.SetToolTip(resetValue, "Serwer nie udostępnia dokładnej liczby tokenów od ostatniego resetu.");
        status.Text = current?["fetchedAt"] is JsonNode stamp && DateTimeOffset.TryParse(stamp.ToString(), out var time) ? "Aktualizacja " + time.ToLocalTime().ToString("HH:mm") : "Oczekiwanie na dane…";
        tips.SetToolTip(status, "Odświeżanie co 3 minuty. Odśwież ręcznie w menu ikony przy zegarze.");
        if (remoteError != null) { status.Text = "Dane nieaktualne · ponowię za 3 min"; tips.SetToolTip(status, remoteError); }
        int y = 198;
        var chartToggle = SectionButton((chartOpen ? "▾" : "▸") + "  Wykres użycia", y);
        chartToggle.Click += async (_, _) => { chartOpen = !chartOpen; Render(); if (chartOpen && local == null) await RefreshLocal(); };
        y += 34;
        if (chartOpen) y = RenderChart(y);
        var rankingToggle = SectionButton((rankingOpen ? "▾" : "▸") + "  Najcięższe prompty", y);
        rankingToggle.Click += async (_, _) => { rankingOpen = !rankingOpen; Render(); if (rankingOpen && local == null) await RefreshLocal(); };
        y += 34;
        if (rankingOpen) y = RenderRanking(y);
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
            for (int i = 0; i < choices.Length; i++)
            {
                int index = i; var item = menu.Items.Add((i == selected ? "✓  " : "    ") + choices[i]);
                item.Click += (_, _) => { changed(index); Render(); menu.Dispose(); };
            }
            menu.Closed += (_, _) => menu.Dispose(); menu.Show(select, new Point(0, select.Height));
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
        tips.SetToolTip(caption, chartMode == 0 ? "Tokeny z zapisanych lokalnie sesji, również pomocniczych. Pozostałe urządzenia nie są uwzględnione. Najedź na słupek, aby zobaczyć liczbę." : "Dni według serwera. Tygodnie od poniedziałku, miesiące kalendarzowe. Niepełne okresy zawierają tylko dostępne dni — szczegóły po najechaniu.");
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
    int RenderRanking(int y)
    {
        SelectAt(new[] { "Ostatnie 24 godziny", "Ostatnie 7 dni", "Ostatnie 30 dni", "Ostatnie 90 dni" }, rankingPeriod, y, i => rankingPeriod = i);
        y += 33;
        var note = TextAt("Praca agenta · wszystkie wywołania", 20, y, 280, 20, 8, MutedText);
        tips.SetToolTip(note, "Suma tokenów wszystkich wywołań modelu podczas realizacji: duży kontekst, wejście (także cache), wyjście i rozumowanie. To nie jest liczba tokenów w tekście promptu. Wyklucza sesje pomocnicze. Kliknij pozycję, aby zobaczyć treść i datę.");
        y += 23;
        var since = DateTimeOffset.Now.AddDays(-new[] { 1, 7, 30, 90 }[rankingPeriod]);
        var top = local?.Prompts.Where(x => x.At >= since).OrderByDescending(x => x.Tokens).Take(10).ToList();
        if (top == null || top.Count == 0)
        {
            var label = TextAt(localBusy ? "Odczytywanie historii…" : localError != null ? "Nie udało się odczytać historii" : "Brak promptów w tym okresie", 20, y, 280, 30, 9, Color.Gray);
            if (localError != null) tips.SetToolTip(label, localError);
            return y + 35;
        }
        var list = new Panel { Location = new Point(S(17), S(y)), Size = new Size(S(286), S(Math.Min(5, top.Count) * 34)), AutoScroll = true };
        for (int i = 0; i < top.Count; i++)
        {
            var prompt = top[i]; var rank = i + 1;
            string preview = string.Join(" ", prompt.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (preview.Length > 23) preview = preview[..23] + "…";
            var button = new Button { Location = new Point(0, S(i * 34)), Size = new Size(S(265), S(33)), FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 8), ForeColor = ForeColor, Text = $"{rank}.  {UsageChart.Short(prompt.Tokens)} · {preview}" };
            button.FlatAppearance.BorderSize = 0;
            tips.SetToolTip(button, $"Top {rank} · {Number(prompt.Tokens)} tokenów\n{prompt.At.ToLocalTime():dd.MM.yyyy HH:mm}" + (prompt.Complete ? "" : " · w toku / brak zakończenia"));
            button.Click += (_, _) => ShowPrompt(prompt, rank);
            list.Controls.Add(button);
        }
        body.Controls.Add(list);
        return y + Math.Min(5, top.Count) * 34 + 10;
    }
    void ShowPrompt(PromptUsage prompt, int rank)
    {
        using var dialog = CreatePromptDialog(prompt, rank);
        dialog.ShowDialog(this);
    }
    Form CreatePromptDialog(PromptUsage prompt, int rank)
    {
        var dialog = new Form { Text = $"Top {rank} · prompt", ClientSize = new Size(S(470), S(290)), MinimumSize = new Size(S(340), S(230)), BackColor = BackColor, ForeColor = ForeColor, ShowInTaskbar = false, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, Font = new Font("Segoe UI", 10), AutoScaleMode = AutoScaleMode.None };
        var header = new Label { Dock = DockStyle.Top, Height = S(48), Padding = new Padding(S(12), S(12), 0, 0), Text = $"{prompt.At.ToLocalTime():dd.MM.yyyy · HH:mm}    {Number(prompt.Tokens)} tokenów" };
        var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(30, 34, 42), ForeColor = ForeColor, Text = prompt.Text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine) };
        var holder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(12), 0, S(12), S(12)) }; holder.Controls.Add(text);
        dialog.Controls.Add(holder); dialog.Controls.Add(header);
        dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
        dialog.Shown += (_, _) => { text.SelectionStart = 0; text.SelectionLength = 0; };
        return dialog;
    }
}
