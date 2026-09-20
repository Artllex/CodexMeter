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

sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public static readonly Color HoverColor = Color.FromArgb(70, 83, 111);
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        bool hovered = e.Item.Selected || e.Item.BackColor == HoverColor;
        using var brush = new SolidBrush(hovered ? HoverColor : e.ToolStrip?.BackColor ?? Color.FromArgb(45, 45, 45));
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
    }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Color.FromArgb(88, 88, 88));
        e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Color.FromArgb(78, 78, 78));
        e.Graphics.DrawLine(pen, 6, e.Item.Height / 2, e.Item.Width - 6, e.Item.Height / 2);
    }
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
    readonly bool closesAutomatically;
    bool lifetimeElapsed;

    CompletionPopup(PromptUsage prompt, bool closesAutomatically = false)
    {
        this.closesAutomatically = closesAutomatically;
        using var screenGraphics = Graphics.FromHwnd(IntPtr.Zero);
        float dpiScale = Math.Max(1f, screenGraphics.DpiX / 96f);
        int P(int value) => Math.Max(1, (int)Math.Round(value * dpiScale));
        var activityFlags = PromptFlags.Items(prompt);
        var statusFlags = PromptFlags.StatusItems(prompt);
        bool hasPromptFlags = activityFlags.Count > 0, hasPromptStatus = statusFlags.Count > 0;
        int flagsHeight = activityFlags.Count > 12 ? 78 : activityFlags.Count > 6 ? 60 : activityFlags.Count > 3 ? 42 : 24;
        int statusHeight = hasPromptStatus ? 24 : 0;
        int metadataHeight = (hasPromptFlags ? flagsHeight : 0) + statusHeight;
        string rawParameters = FormatRawParameters(prompt.RawParameters);
        int parameterLineCount = string.IsNullOrWhiteSpace(rawParameters) ? 0 : rawParameters.Split(Environment.NewLine).Length;
        bool hasParameters = parameterLineCount > 0;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(P(440), P(256 + metadataHeight));
        BackColor = Color.FromArgb(35, 35, 35);
        ForeColor = Color.FromArgb(242, 242, 242);
        Padding = new Padding(P(12), P(10), P(12), P(10));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8 + (hasPromptFlags ? 1 : 0) + (hasPromptStatus ? 1 : 0) + (hasParameters ? 2 : 0),
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, P(110)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(28)));
        for (int row = 0; row < 2; row++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(24)));
        if (hasPromptFlags) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(flagsHeight)));
        if (hasPromptStatus) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(statusHeight)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(6)));
        for (int row = 0; row < 2; row++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(24)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(6)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(26)));
        if (hasParameters)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(6)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(26)));
        }

        var fieldColor = Color.FromArgb(170, 170, 170);
        var valueColor = Color.FromArgb(242, 242, 242);
        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = L.Pick("Zakończono przetwarzanie", "Processing completed"),
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = valueColor,
            TextAlign = ContentAlignment.MiddleLeft
        };
        string preview = string.Join(" ", prompt.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (preview.Length > 34) preview = preview[..34] + "…";
        string conversation = string.IsNullOrWhiteSpace(prompt.Conversation) ? L.LocalConversation : prompt.Conversation;
        string model = string.IsNullOrWhiteSpace(prompt.Model) ? L.Pick("niedostępny", "unavailable") : prompt.Model;
        string effort = L.Thinking(prompt.ReasoningEffort);
        var itemFont = new Font("Segoe UI", 8.3f);
        Label FieldLabel(string text) => new() { Text = text, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8.3f, FontStyle.Bold), ForeColor = fieldColor, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
        Label FieldValue(string text, Color? color = null) => new() { Text = text, Dock = DockStyle.Fill, Font = itemFont, ForeColor = color ?? valueColor, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
        Panel ModelValue()
        {
            var host = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var modelLabel = new Label { Text = model + " · ", Dock = DockStyle.Left, AutoSize = true, Font = itemFont, ForeColor = valueColor, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
            host.Controls.Add(FieldValue(L.Thinking(effort), ThinkingColor(effort)));
            host.Controls.Add(modelLabel);
            return host;
        }
        string conversationOrder = prompt.ConversationIndex > 0 ? $"[{prompt.ConversationIndex}] " : "";
        var context = new ConversationLine("", conversation, conversationOrder) { Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = valueColor, Font = new Font("Segoe UI", 8.3f, FontStyle.Bold) };
        var separator = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, P(3), 0, P(4)), BackColor = Color.FromArgb(78, 78, 78), Height = P(1) };
        var contentSeparator = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, P(3), 0, P(4)), BackColor = Color.FromArgb(78, 78, 78), Height = P(1) };
        string popupText = string.IsNullOrWhiteSpace(prompt.OriginalText) ? prompt.Text : prompt.OriginalText;
        string fullText = popupText.Replace("\r\n", "\n").Replace("\n", Environment.NewLine).Trim();
        var contentSections = PromptContent.Sections(fullText);
        string requestText = PromptContent.RequestPreview(contentSections);
        string singleLineText = string.Join(" ", requestText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        string collapsedText = singleLineText.Length > 72 ? singleLineText[..72].TrimEnd() + "…" : singleLineText;
        bool canExpandContent = singleLineText.Length > 72 || fullText.Contains(Environment.NewLine, StringComparison.Ordinal);
        var contentPanel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var fullPrompt = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, ScrollBars = RichTextBoxScrollBars.None, BorderStyle = BorderStyle.None, BackColor = BackColor, ForeColor = valueColor, Font = itemFont, DetectUrls = false, WordWrap = true, Text = collapsedText, Margin = Padding.Empty };
        var expandHost = new Panel { Dock = DockStyle.Right, Width = P(34), BackColor = BackColor, Visible = canExpandContent };
        var expandContent = new Button { Text = "⌄", Location = new Point(P(4), 0), Size = new Size(P(30), P(26)), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = valueColor, Font = new Font("Segoe UI", 9), TabStop = false };
        expandContent.FlatAppearance.BorderSize = 0; expandContent.FlatAppearance.MouseOverBackColor = Color.FromArgb(70, 83, 111); expandContent.FlatAppearance.MouseDownBackColor = Color.FromArgb(61, 72, 96);
        expandHost.Controls.Add(expandContent);
        contentPanel.Controls.Add(fullPrompt); contentPanel.Controls.Add(expandHost);
        var parametersSeparator = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, P(3), 0, P(3)), BackColor = Color.FromArgb(78, 78, 78), Height = P(1), Visible = hasParameters };
        var parametersPanel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Visible = hasParameters };
        var parametersJsonFont = new Font("Consolas", 7.2f);
        var fullParameters = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, ScrollBars = RichTextBoxScrollBars.None, BorderStyle = BorderStyle.None, BackColor = BackColor, ForeColor = valueColor, Font = itemFont, DetectUrls = false, WordWrap = false, Text = "", Margin = Padding.Empty };
        string collapsedParameters = L.Pick($"{parameterLineCount} linii…", $"{parameterLineCount} lines…");
        var expandParametersHost = new Panel { Dock = DockStyle.Right, Width = P(34), BackColor = BackColor, Visible = hasParameters };
        var expandParameters = new Button { Text = "⌄", Location = new Point(P(4), 0), Size = new Size(P(30), P(26)), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = valueColor, Font = new Font("Segoe UI", 9), TabStop = false };
        expandParameters.FlatAppearance.BorderSize = 0; expandParameters.FlatAppearance.MouseOverBackColor = Color.FromArgb(70, 83, 111); expandParameters.FlatAppearance.MouseDownBackColor = Color.FromArgb(61, 72, 96);
        expandParametersHost.Controls.Add(expandParameters);
        parametersPanel.Controls.Add(fullParameters); parametersPanel.Controls.Add(expandParametersHost);
        var titleBar = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var close = new Button { Text = "X", Dock = DockStyle.Right, Width = P(32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = valueColor, Font = new Font("Segoe UI", 8), TabStop = false };
        close.FlatAppearance.BorderSize = 0; close.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 43, 28); close.FlatAppearance.MouseDownBackColor = Color.FromArgb(153, 30, 22);
        close.Click += (_, _) => Close();
        titleBar.Controls.Add(title); titleBar.Controls.Add(close);
        layout.Controls.Add(titleBar, 0, 0);
        layout.SetColumnSpan(titleBar, 2);
        layout.Controls.Add(FieldLabel(L.Pick("Rozmowa", "Conversation")), 0, 1);
        layout.Controls.Add(context, 1, 1);
        layout.Controls.Add(FieldLabel(L.Pick("Model", "Model")), 0, 2);
        layout.Controls.Add(ModelValue(), 1, 2);
        int nextMetadataRow = 3;
        int flagsRow = hasPromptFlags ? nextMetadataRow++ : -1;
        int statusRow = hasPromptStatus ? nextMetadataRow++ : -1;
        int separatorRow = nextMetadataRow;
        int inputRow = separatorRow + 1;
        int outputRow = separatorRow + 2;
        int contentSeparatorRow = separatorRow + 3;
        int contentRow = separatorRow + 4;
        int parametersSeparatorRow = hasParameters ? contentRow + 1 : -1;
        int parametersRow = hasParameters ? contentRow + 2 : -1;
        if (hasPromptFlags)
        {
            layout.Controls.Add(FieldLabel(L.Pick("Flagi", "Flags")), 0, flagsRow);
            layout.Controls.Add(new FlagLine(activityFlags) { Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = valueColor, Font = new Font("Segoe UI", 7.3f) }, 1, flagsRow);
        }
        if (hasPromptStatus)
        {
            layout.Controls.Add(FieldLabel(L.Pick("Status", "Status")), 0, statusRow);
            layout.Controls.Add(new FlagLine(statusFlags) { Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = valueColor, Font = new Font("Segoe UI", 7.3f, FontStyle.Bold) }, 1, statusRow);
        }
        layout.Controls.Add(separator, 0, separatorRow);
        layout.SetColumnSpan(separator, 2);
        layout.Controls.Add(FieldLabel(L.Pick("Tokeny IN", "Input tokens")), 0, inputRow);
        layout.Controls.Add(FieldValue(Short(prompt.InputTokens), TokenVisuals.Input(prompt.InputTokens)), 1, inputRow);
        layout.Controls.Add(FieldLabel(L.Pick("Tokeny OUT", "Output tokens")), 0, outputRow);
        layout.Controls.Add(FieldValue(Short(prompt.OutputTokens), TokenVisuals.Output(prompt.OutputTokens)), 1, outputRow);
        layout.Controls.Add(contentSeparator, 0, contentSeparatorRow);
        layout.SetColumnSpan(contentSeparator, 2);
        var contentFieldLabel = FieldLabel(L.Pick("Zapytanie", "Prompt"));
        contentFieldLabel.TextAlign = ContentAlignment.TopLeft;
        contentFieldLabel.Padding = new Padding(0, P(4), 0, 0);
        layout.Controls.Add(contentFieldLabel, 0, contentRow);
        layout.Controls.Add(contentPanel, 1, contentRow);
        if (hasParameters)
        {
            layout.Controls.Add(parametersSeparator, 0, parametersSeparatorRow);
            layout.SetColumnSpan(parametersSeparator, 2);
            var parametersFieldLabel = FieldLabel(L.Pick("Parametry", "Parameters"));
            parametersFieldLabel.TextAlign = ContentAlignment.TopLeft;
            parametersFieldLabel.Padding = new Padding(0, P(4), 0, 0);
            layout.Controls.Add(parametersFieldLabel, 0, parametersRow);
            layout.Controls.Add(parametersPanel, 1, parametersRow);
        }
        Controls.Add(layout);
        bool contentExpanded = false;
        bool parametersExpanded = false;
        int MeasureTextHeight(RichTextBox box, Panel panel, int reservedWidth)
        {
            layout.PerformLayout();
            int textWidth = Math.Max(P(80), panel.Width - reservedWidth);
            return TextRenderer.MeasureText(box.Text, box.Font, new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + P(6);
        }
        void UpdateContentLayout()
        {
            int contentHeight = Math.Max(P(26), MeasureTextHeight(fullPrompt, contentPanel, canExpandContent ? expandHost.Width : 0));
            int parametersHeight = hasParameters ? Math.Max(P(26), MeasureTextHeight(fullParameters, parametersPanel, expandParametersHost.Width)) : 0;
            int fixedPanelHeight = P(156 + metadataHeight);
            int maximumClientHeight = Screen.FromControl(this).WorkingArea.Height - P(24);
            int parametersSpacing = hasParameters ? P(6) : 0;
            int totalHeight = fixedPanelHeight + contentHeight + parametersSpacing + parametersHeight;
            if (totalHeight > maximumClientHeight)
            {
                int available = Math.Max(P(52), maximumClientHeight - fixedPanelHeight - parametersSpacing);
                if (contentExpanded && parametersExpanded)
                {
                    int contentShare = Math.Max(P(26), available / 2);
                    contentHeight = Math.Min(contentHeight, contentShare);
                    parametersHeight = Math.Max(P(26), available - contentHeight);
                }
                else if (contentExpanded) contentHeight = Math.Max(P(26), available - parametersHeight);
                else if (parametersExpanded) parametersHeight = Math.Max(P(26), available - contentHeight);
            }
            fullPrompt.ScrollBars = contentExpanded && contentHeight < MeasureTextHeight(fullPrompt, contentPanel, canExpandContent ? expandHost.Width : 0) ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.None;
            fullParameters.ScrollBars = hasParameters && parametersExpanded && parametersHeight < MeasureTextHeight(fullParameters, parametersPanel, expandParametersHost.Width) ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.None;
            layout.RowStyles[contentRow].Height = contentHeight;
            if (hasParameters) layout.RowStyles[parametersRow].Height = parametersHeight;
            ClientSize = new Size(ClientSize.Width, fixedPanelHeight + contentHeight + parametersSpacing + parametersHeight);
            Reposition();
        }
        void SetContentExpanded(bool expanded)
        {
            expanded &= canExpandContent;
            contentExpanded = expanded;
            if (expanded) PromptContent.WriteTo(fullPrompt, contentSections, valueColor, fieldColor);
            else fullPrompt.Text = collapsedText;
            expandContent.Text = expanded ? "⌃" : "⌄";
            UpdateContentLayout();
        }
        void SetParametersExpanded(bool expanded)
        {
            if (!hasParameters) return;
            parametersExpanded = expanded;
            fullParameters.Font = expanded ? parametersJsonFont : itemFont;
            fullParameters.Text = expanded ? rawParameters : collapsedParameters;
            expandParameters.Text = expanded ? "⌃" : "⌄";
            UpdateContentLayout();
        }
        expandContent.Click += (_, _) => SetContentExpanded(!contentExpanded);
        expandParameters.Click += (_, _) => SetParametersExpanded(!parametersExpanded);
        SetContentExpanded(false);
        SetParametersExpanded(false);
        void KeepVisible(object? sender, EventArgs e)
        {
            if (closesAutomatically && lifetimeElapsed) closeTimer.Stop();
        }
        void CloseAfterLeave(object? sender, EventArgs e)
        {
            BeginInvoke((Action)(() =>
            {
                if (closesAutomatically && lifetimeElapsed && !IsDisposed && !Bounds.Contains(Cursor.Position))
                {
                    closeTimer.Interval = 1000;
                    closeTimer.Start();
                }
            }));
        }
        void WatchHover(Control control)
        {
            control.MouseEnter += KeepVisible;
            control.MouseLeave += CloseAfterLeave;
            foreach (Control child in control.Controls) WatchHover(child);
        }
        WatchHover(this);
        closeTimer.Tick += (_, _) =>
        {
            closeTimer.Stop();
            if (!lifetimeElapsed)
            {
                lifetimeElapsed = true;
                if (Bounds.Contains(Cursor.Position)) return;
            }
            Close();
        };
        FormClosed += (_, _) =>
        {
            closeTimer.Dispose();
            Active.Remove(this);
            Reposition();
        };
    }

    protected override bool ShowWithoutActivation => true;
    static string FormatRawParameters(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return "";
        var values = new List<string>();
        foreach (string line in rawJson.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                values.Add(JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (JsonException) { values.Add(line); }
        }
        return string.Join(Environment.NewLine + Environment.NewLine, values);
    }
    static Color ThinkingColor(string effort) => effort.Trim().ToLowerInvariant() switch
    {
        "low" => Color.FromArgb(128, 225, 156),
        "medium" => Color.FromArgb(245, 198, 76),
        "high" => Color.FromArgb(255, 151, 81),
        "xhigh" or "max" or "ultra" => Color.FromArgb(242, 97, 91),
        _ => Color.FromArgb(242, 242, 242)
    };
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

    public static void Display(PromptUsage prompt, bool closeAutomatically = true)
    {
        var popup = new CompletionPopup(prompt, closeAutomatically);
        Active.Add(popup);
        Reposition();
        popup.Show();
        if (closeAutomatically) popup.closeTimer.Start();
    }

    public static void ExportPreview(string path)
    {
        var sample = new PromptUsage
        {
            At = DateTimeOffset.Now,
            Text = "Dodajmy jednolitą typografię oraz czytelne pola w powiadomieniu. Po rozwinięciu pokażmy pełną treść i zwiększmy wysokość okna dokładnie o potrzebne miejsce.",
            OriginalText = "# Files mentioned by the user:\n\n## projekt.cs: C:\\CODE\\projekt.cs\n\nDistinguish instructions in attached documents from the user's request.\n\n## My request:\nDodajmy jednolitą typografię oraz czytelne pola w powiadomieniu. Po rozwinięciu pokażmy pełną treść.\n\n<image name=[Image #1] path=\"C:\\CODE\\temp\\podglad.png\">\n</image>",
            RawParameters = "{\"timestamp\":\"2026-09-06T10:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"user_message\",\"message\":\"Dodajmy jednolitą typografię.\"}}",
            InputTokens = 459_000,
            OutputTokens = 949,
            Conversation = "✅ 🐙 Ⓧ DownloadLens",
            Model = "gpt-5.6-terra",
            ReasoningEffort = "low",
            ConversationIndex = 14,
            FileCount = 2,
            PictureCount = 1,
            WorkedOnCode = true,
            TestsRun = true,
            ProjectBuilt = true,
            GitCommit = true,
            GitPush = true,
            PackageBuilt = true,
            ReleaseCreated = true,
            GeneratedPicture = true,
            UsedWeb = true,
            UsedBrowser = true,
            DocumentsChanged = true,
            AgentCount = 2,
            Partial = true
        };
        sample.McpTools.Add("figma: get_file");
        using var popup = new CompletionPopup(sample);
        popup.Show();
        Application.DoEvents();
        using var image = new Bitmap(popup.Width, popup.Height);
        popup.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
        var expander = Descendants(popup).OfType<Button>().FirstOrDefault(button => button.Text != "X");
        if (expander != null)
        {
            expander.PerformClick();
            Application.DoEvents();
            using var expandedImage = new Bitmap(popup.Width, popup.Height);
            popup.DrawToBitmap(expandedImage, new Rectangle(Point.Empty, expandedImage.Size));
            string expandedPath = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "-expanded" + Path.GetExtension(path));
            expandedImage.Save(expandedPath, System.Drawing.Imaging.ImageFormat.Png);
        }
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
    int chartMode = 0, chartTokenMode = 0;
    string chartConversationFilter = "";
    ContextMenuStrip? activeSelectMenu;
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
        current = saved; chartMode = 0; Render();
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "analytics-test.txt"), "\nPASS: chart ranges and completion popup");
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
        chartToggle.Click += async (_, _) => { chartOpen = !chartOpen; if (chartOpen) promptHistoryOpen = false; Render(); if (chartOpen && local == null) await RefreshLocal(); };
        y += 34;
        if (chartOpen) y = RenderChart(y);
        var promptHistoryToggle = SectionButton((promptHistoryOpen ? "▾" : "▸") + "  " + L.Pick("Ostatnie prompty", "Recent prompts"), y);
        promptHistoryToggle.Click += async (_, _) => { promptHistoryOpen = !promptHistoryOpen; if (promptHistoryOpen) chartOpen = false; Render(); if (promptHistoryOpen && local == null) await RefreshLocal(); };
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
    Button SelectAt(string[] choices, int selected, int y, Action<int> changed, bool searchable = false, int x = 20, int width = 280, int verticalTextOffset = 0)
    {
        var select = new Button { Text = choices[selected], Location = new Point(S(x), S(y)), Size = new Size(S(width), S(27)), Padding = new Padding(S(10), S(verticalTextOffset * 2), S(36), 0), FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, BackColor = Raised, ForeColor = MainText, Font = new Font("Segoe UI", 8.5f), TabStop = true };
        select.FlatAppearance.BorderColor = Color.FromArgb(73, 73, 73);
        select.FlatAppearance.MouseOverBackColor = DarkMenuRenderer.HoverColor;
        select.FlatAppearance.MouseDownBackColor = DarkMenuRenderer.HoverColor;
        bool suppressNextOpen = false;
        void CloseOpenMenu()
        {
            if (activeSelectMenu is { IsDisposed: false, Visible: true })
            {
                suppressNextOpen = true;
                activeSelectMenu.Close();
            }
        }
        select.MouseDown += (_, _) => CloseOpenMenu();
        select.Click += (_, _) =>
        {
            if (suppressNextOpen) { suppressNextOpen = false; return; }
            var menu = new ContextMenuStrip { BackColor = Raised, ForeColor = MainText, ShowImageMargin = false, Font = new Font("Segoe UI", 9), Renderer = new DarkMenuRenderer() };
            activeSelectMenu = menu;
            menu.Closing += (_, _) =>
            {
                // Windows dismisses a ContextMenuStrip before the owner receives Click.
                // Remember that click so the same control acts strictly as a toggle.
                if (select.RectangleToScreen(select.ClientRectangle).Contains(Cursor.Position)) suppressNextOpen = true;
            };
            menu.Closed += (_, _) => { if (activeSelectMenu == menu) activeSelectMenu = null; };
            bool selectionQueued = false;
            TextBox? search = null;
            void PopulateChoices()
            {
                foreach (var item in menu.Items.OfType<ToolStripMenuItem>().ToArray())
                {
                    menu.Items.Remove(item);
                    item.Dispose();
                }
                string filter = search?.Text.Trim() ?? "";
                for (int i = 0; i < choices.Length; i++)
                {
                    if (filter.Length > 0 && choices[i].IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                    int index = i; var item = menu.Items.Add((i == selected ? "✓  " : "    ") + choices[i]);
                    item.Padding = new Padding(S(5), S(2), S(5), S(2));
                    item.MouseEnter += (_, _) => { item.BackColor = DarkMenuRenderer.HoverColor; item.Invalidate(); };
                    item.MouseLeave += (_, _) => { item.BackColor = Color.Empty; item.Invalidate(); };
                    item.Click += (_, _) =>
                    {
                        if (selectionQueued) return;
                        selectionQueued = true;
                        BeginInvoke((Action)(() =>
                        {
                            if (!menu.IsDisposed) menu.Dispose();
                            if (IsDisposed) return;
                            changed(index);
                            Render();
                        }));
                    };
                }
            }
            if (searchable)
            {
                const int searchWidth = 252;
                const int searchHeightPx = 65;
                const int dividerLeftPx = 63, dividerHeightPx = 55;
                const int fieldLeftPx = 73, rightPaddingPx = 10;
                var searchPanel = new Panel { Size = new Size(S(searchWidth), searchHeightPx), BackColor = Raised, Margin = Padding.Empty };
                var icon = new Label { Text = "\uE721", Location = new Point(-6, 2), Size = new Size(dividerLeftPx, searchHeightPx), ForeColor = MainText, BackColor = Raised, Font = new Font("Segoe Fluent Icons", 14), TextAlign = ContentAlignment.MiddleCenter };
                var divider = new Panel { Location = new Point(dividerLeftPx, (searchHeightPx - dividerHeightPx) / 2), Size = new Size(1, dividerHeightPx), BackColor = Color.FromArgb(100, 100, 100) };
                search = new TextBox { Size = new Size(searchPanel.Width - fieldLeftPx - rightPaddingPx, S(23)), BackColor = Raised, ForeColor = MainText, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 9), PlaceholderText = L.Pick("Szukaj rozmowy…", "Search conversations…") };
                search.Location = new Point(fieldLeftPx, Math.Max(0, (searchHeightPx - search.Height) / 2));
                searchPanel.Controls.Add(icon); searchPanel.Controls.Add(divider); searchPanel.Controls.Add(search);
                menu.Items.Add(new ToolStripControlHost(searchPanel) { AutoSize = false, Size = searchPanel.Size, Margin = new Padding(S(7), S(6), S(7), S(5)), BackColor = Raised });
                menu.Items.Add(new ToolStripSeparator());
                const int arrowHeight = 18;
                const int rowHeight = 30;
                var listShell = new Panel { Size = new Size(S(252), S(220)), BackColor = Raised };
                var viewport = new Panel { Location = Point.Empty, Size = listShell.Size, BackColor = Raised };
                var up = new Label { Text = "⌃", Height = S(arrowHeight), ForeColor = MainText, BackColor = Raised, Font = new Font("Segoe UI", 10), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
                var down = new Label { Text = "⌄", Height = S(arrowHeight), ForeColor = MainText, BackColor = Raised, Font = new Font("Segoe UI", 10), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
                listShell.Controls.Add(viewport); listShell.Controls.Add(up); listShell.Controls.Add(down);
                menu.Items.Add(new ToolStripControlHost(listShell) { AutoSize = false, Size = listShell.Size, Margin = new Padding(S(7), 0, S(7), 0), BackColor = Raised });
                int firstVisibleRow = 0;
                int renderedCapacity = 1;
                int repeatDirection = 0;
                var repeatTimer = new System.Windows.Forms.Timer { Interval = 320 };
                List<int> filteredIndices = [];
                void StopRepeating() => repeatTimer.Stop();
                void RenderVisibleRows()
                {
                    filteredIndices = Enumerable.Range(0, choices.Length)
                        .Where(i => string.IsNullOrWhiteSpace(search!.Text) || choices[i].Contains(search.Text, StringComparison.CurrentCultureIgnoreCase))
                        .ToList();
                    int rowPixels = S(rowHeight);
                    int arrowPixels = S(arrowHeight);
                    int capacityWithoutArrows = Math.Max(1, listShell.Height / rowPixels);
                    bool needsScrolling = filteredIndices.Count > capacityWithoutArrows;
                    renderedCapacity = needsScrolling
                        ? Math.Max(1, (listShell.Height - (2 * arrowPixels)) / rowPixels)
                        : capacityWithoutArrows;
                    int maximumFirstRow = Math.Max(0, filteredIndices.Count - renderedCapacity);
                    firstVisibleRow = Math.Clamp(firstVisibleRow, 0, maximumFirstRow);
                    bool showUp = firstVisibleRow > 0;
                    bool showDown = firstVisibleRow < maximumFirstRow;
                    up.Bounds = new Rectangle(0, 0, listShell.Width, arrowPixels);
                    down.Bounds = new Rectangle(0, listShell.Height - arrowPixels, listShell.Width, arrowPixels);
                    viewport.Bounds = new Rectangle(
                        0,
                        showUp ? arrowPixels : 0,
                        listShell.Width,
                        listShell.Height - (showUp ? arrowPixels : 0) - (showDown ? arrowPixels : 0));
                    up.Visible = showUp;
                    down.Visible = showDown;
                    foreach (Control child in viewport.Controls.Cast<Control>().ToArray()) { viewport.Controls.Remove(child); child.Dispose(); }
                    int rowsToRender = Math.Min(renderedCapacity, filteredIndices.Count - firstVisibleRow);
                    for (int row = 0; row < rowsToRender; row++)
                    {
                        int index = filteredIndices[firstVisibleRow + row];
                        var item = new Button { Text = (index == selected ? "✓  " : "    ") + choices[index], Location = new Point(0, row * rowPixels), Size = new Size(viewport.Width, rowPixels), Padding = new Padding(S(5), 0, S(5), 0), FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, ForeColor = MainText, BackColor = Raised, Font = new Font("Segoe UI", 9), TabStop = false };
                        item.FlatAppearance.BorderSize = 0; item.FlatAppearance.MouseOverBackColor = DarkMenuRenderer.HoverColor;
                        item.Click += (_, _) => { if (!selectionQueued) { selectionQueued = true; menu.Close(); changed(index); Render(); } };
                        item.MouseWheel += (_, e) => MoveOne(e.Delta < 0 ? 1 : -1);
                        viewport.Controls.Add(item);
                    }
                }
                bool CanMove(int direction) => direction < 0 ? firstVisibleRow > 0 : firstVisibleRow + renderedCapacity < filteredIndices.Count;
                bool MoveOne(int direction)
                {
                    if (!CanMove(direction)) { StopRepeating(); return false; }
                    firstVisibleRow += direction;
                    RenderVisibleRows();
                    return true;
                }
                void SetArrowHover(Label arrow, bool hovered) => arrow.BackColor = hovered ? DarkMenuRenderer.HoverColor : Raised;
                void StartRepeating(int direction)
                {
                    repeatDirection = direction;
                    if (!MoveOne(direction) || !CanMove(direction)) return;
                    repeatTimer.Interval = 320;
                    repeatTimer.Start();
                }
                repeatTimer.Tick += (_, _) =>
                {
                    if (!MoveOne(repeatDirection)) return;
                    repeatTimer.Interval = 90;
                };
                foreach (var (arrow, direction) in new[] { (up, -1), (down, 1) })
                {
                    arrow.MouseEnter += (_, _) => SetArrowHover(arrow, true);
                    arrow.MouseLeave += (_, _) => { SetArrowHover(arrow, false); StopRepeating(); };
                    arrow.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) StartRepeating(direction); };
                    arrow.MouseUp += (_, _) => StopRepeating();
                }
                menu.Closed += (_, _) => { repeatTimer.Stop(); repeatTimer.Dispose(); };
                viewport.MouseWheel += (_, e) => MoveOne(e.Delta < 0 ? 1 : -1);
                search.TextChanged += (_, _) => { firstVisibleRow = 0; RenderVisibleRows(); };
                RenderVisibleRows();
                menu.Show(select, new Point(0, select.Height));
                search.Focus();
                return;
            }
            PopulateChoices();
            menu.Show(select, new Point(0, select.Height));
            if (search != null) search.Focus();
        };
        var arrow = new Label { Text = "▾", Location = new Point(select.Width - S(28) - 1, 1), Size = new Size(S(28), select.Height - 2), BackColor = Raised, ForeColor = MainText, Font = new Font("Segoe UI", 8.5f), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
        void UpdateSelectHover()
        {
            bool hovered = select.RectangleToScreen(select.ClientRectangle).Contains(Cursor.Position);
            select.BackColor = hovered ? DarkMenuRenderer.HoverColor : Raised;
            arrow.BackColor = hovered ? DarkMenuRenderer.HoverColor : Raised;
        }
        select.MouseEnter += (_, _) => UpdateSelectHover();
        select.MouseLeave += (_, _) => UpdateSelectHover();
        arrow.MouseEnter += (_, _) => UpdateSelectHover();
        arrow.MouseLeave += (_, _) => UpdateSelectHover();
        arrow.MouseDown += (_, _) => { UpdateSelectHover(); CloseOpenMenu(); };
        arrow.Click += (_, _) => select.PerformClick();
        select.Controls.Add(arrow);
        body.Controls.Add(select); return select;
    }
    int RenderChart(int y)
    {
        SelectAt(L.Polish
            ? new[] { "Wpisy (godzina)", "Wpisy (4 godziny)", "Dzień (24 godz.)", "Tydzień (7 dni)", "Miesiąc (30 dni)" }
            : new[] { "Entries (hour)", "Entries (4 hours)", "Day (24 hours)", "Week (7 days)", "Month (30 days)" }, chartMode, y, i => chartMode = i, x: 20, width: 137, verticalTextOffset: 2);
        SelectAt(
            L.Polish ? new[] { "Tokeny IN", "Tokeny OUT" } : new[] { "Input", "Output" },
            chartTokenMode,
            y,
            i => chartTokenMode = i,
            x: 163,
            width: 137);
        y += 33;
        var conversations = new[] { L.Pick("Wszystkie rozmowy", "All conversations") }
            .Concat(ChartConversations())
            .ToArray();
        int selectedConversation = Math.Max(0, Array.IndexOf(conversations, chartConversationFilter));
        if (selectedConversation == 0 && chartConversationFilter.Length > 0) chartConversationFilter = "";
        SelectAt(conversations, selectedConversation, y, i => chartConversationFilter = i == 0 ? "" : conversations[i], searchable: true);
        y += 33;
        var points = ChartPoints();
        if (local == null)
            TextAt(localBusy ? L.Pick("Odczytywanie historii…", "Reading history…") : L.Pick("Brak lokalnych danych", "No local data"), 20, y, 280, 150, 9, Color.Gray);
        else body.Controls.Add(new UsageChart(points, selected => ShowPrompt(selected, 0)) { Location = new Point(S(20), S(y)), Size = new Size(S(280), S(150)), BackColor = BackColor });
        y += 155;
        TextAt(L.Pick("Lokalnie · czas Windows", "Local · Windows time"), 20, y, 280, 21, 8, Color.Gray);
        return y + 28;
    }
    List<ChartBucket> ChartPoints()
    {
        var points = new List<ChartBucket>();
        TimeSpan range = ChartRange();
        var from = DateTimeOffset.UtcNow - range;
        if (local != null)
        {
            var promptsBySession = local.Prompts.Where(x => !string.IsNullOrWhiteSpace(x.SessionId)).GroupBy(x => x.SessionId).ToDictionary(group => group.Key, group => group.OrderBy(x => x.At).ToList());
            foreach (var sample in local.Samples.Where(x => x.At >= from).OrderBy(x => x.At))
            {
                var localTime = sample.At.ToLocalTime();
                PromptUsage? prompt = promptsBySession.TryGetValue(sample.SessionId, out var sessionPrompts) ? sessionPrompts.LastOrDefault(x => x.At <= sample.At) ?? sessionPrompts.FirstOrDefault() : null;
                string conversation = string.IsNullOrWhiteSpace(prompt?.Conversation) ? L.LocalConversation : prompt.Conversation;
                if (chartConversationFilter.Length > 0 && !string.Equals(chartConversationFilter, conversation, StringComparison.Ordinal)) continue;
                string model = string.IsNullOrWhiteSpace(prompt?.Model) ? L.Pick("niedostępny", "unavailable") : prompt.Model;
                string effort = L.Thinking(prompt?.ReasoningEffort ?? "");
                string request = string.IsNullOrWhiteSpace(prompt?.Text) ? L.NoData : string.Join(" ", prompt.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                if (request.Length > 36) request = request[..36] + "…";
                long tokens = chartTokenMode == 0 ? sample.InputTokens : sample.OutputTokens;
                string tokenKind = chartTokenMode == 0 ? L.Pick("Tokeny IN", "Input tokens") : L.Pick("Tokeny OUT", "Output tokens");
                string order = prompt?.ConversationIndex > 0 ? $"[{prompt.ConversationIndex}] " : "";
                string detail = string.Join("\n", L.Pick("Rozmowa: ", "Conversation: ") + order + conversation, L.Pick("Model: ", "Model: ") + model + " · " + effort, L.Pick("Zapytanie: ", "Prompt: ") + request, L.Pick("Data i godzina: ", "Date and time: ") + localTime.ToString("dd.MM.yyyy · HH:mm:ss", L.Culture), tokenKind + ": " + Number(tokens));
                points.Add(new ChartBucket(localTime.ToString(range.TotalHours <= 24 ? "HH:mm" : "dd.MM"), tokens, detail, sample.At, conversation, prompt, tokenKind, sample.InputTokens, sample.OutputTokens));
            }
        }
        if (points.Count == 0) points.Add(new ChartBucket("", null, L.NoData));
        return points;
    }
    TimeSpan ChartRange() => chartMode switch
    {
        0 => TimeSpan.FromHours(1),
        1 => TimeSpan.FromHours(4),
        2 => TimeSpan.FromHours(24),
        3 => TimeSpan.FromDays(7),
        _ => TimeSpan.FromDays(30)
    };
    IEnumerable<string> ChartConversations()
    {
        if (local == null) return Enumerable.Empty<string>();
        var conversationsBySession = local.Prompts
            .Where(x => !string.IsNullOrWhiteSpace(x.SessionId))
            .GroupBy(x => x.SessionId)
            .ToDictionary(group => group.Key, group => group.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Conversation))?.Conversation ?? L.LocalConversation);
        var from = DateTimeOffset.UtcNow - ChartRange();
        return local.Samples
            .Where(x => x.At >= from)
            .Select(x => conversationsBySession.TryGetValue(x.SessionId, out var conversation) ? conversation : L.LocalConversation)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.CurrentCulture);
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
        const int rowGap = 8;
        var activityByPrompt = prompts.Select(PromptFlags.Items).ToList();
        var statusByPrompt = prompts.Select(PromptFlags.StatusItems).ToList();
        var contentHeights = Enumerable.Range(0, prompts.Count).Select(index =>
        {
            int flagsHeight = activityByPrompt[index].Count == 0 ? 0 : Math.Clamp(17 * (int)Math.Ceiling(activityByPrompt[index].Count / 3.0), 17, 102);
            int statusHeight = statusByPrompt[index].Count > 0 ? 20 : 0;
            return 101 + flagsHeight + statusHeight;
        }).ToList();
        int totalHeight = contentHeights.Sum() + prompts.Count * rowGap;
        var list = new Panel { Location = new Point(S(17), S(y)), Size = new Size(S(286), S(totalHeight)) };
        int rowTop = 0;
        for (int i = 0; i < prompts.Count; i++)
        {
            var prompt = prompts[i]; var position = i + 1;
            int rowContentHeight = contentHeights[i];
            string preview = string.Join(" ", prompt.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (preview.Length > 34) preview = preview[..34] + "…";
            string conversation = string.IsNullOrWhiteSpace(prompt.Conversation) ? L.LocalConversation : prompt.Conversation;
            string model = string.IsNullOrWhiteSpace(prompt.Model) ? L.Pick("niedostępny", "unavailable") : prompt.Model;
            string effort = L.Thinking(prompt.ReasoningEffort);
            var row = new Panel { Location = new Point(0, S(rowTop)), Size = new Size(S(280), S(rowContentHeight)), Cursor = Cursors.Hand };
            var itemFont = new Font("Segoe UI", 7.3f);
            Label Field(string text, int top) => new() { Text = text, Location = new Point(S(4), S(top)), Size = new Size(S(74), S(17)), Font = new Font("Segoe UI", 7.3f, FontStyle.Bold), ForeColor = MutedText };
            Label Value(string text, int top, Color? color = null) => new() { Text = text, Location = new Point(S(82), S(top)), Size = new Size(S(175), S(17)), Font = itemFont, ForeColor = color ?? ForeColor, AutoEllipsis = true };
            string conversationOrder = prompt.ConversationIndex > 0 ? $"[{prompt.ConversationIndex}] " : "";
            var context = new ConversationLine("", conversation, conversationOrder) { Location = new Point(S(82), S(2)), Size = new Size(S(175), S(17)), ForeColor = ForeColor, Font = new Font("Segoe UI", 7.3f, FontStyle.Bold) };
            row.Controls.Add(Field(L.Pick("Rozmowa", "Conversation"), 2)); row.Controls.Add(context);
            row.Controls.Add(Field(L.Pick("Model", "Model"), 20)); row.Controls.Add(Value($"{model} · {effort}", 20));
            row.Controls.Add(Field(L.Pick("Zapytanie", "Prompt"), 38)); row.Controls.Add(Value(preview, 38));
            row.Controls.Add(Field(L.Pick("Tokeny IN", "Input tokens"), 57)); row.Controls.Add(Value(UsageChart.Short(prompt.InputTokens), 57, TokenVisuals.Input(prompt.InputTokens)));
            row.Controls.Add(Field(L.Pick("Tokeny OUT", "Output tokens"), 75)); row.Controls.Add(Value(UsageChart.Short(prompt.OutputTokens), 75, TokenVisuals.Output(prompt.OutputTokens)));
            var activityFlags = activityByPrompt[i];
            int nextDetailTop = 93;
            if (activityFlags.Count > 0)
            {
                row.Controls.Add(Field(L.Pick("Flagi", "Flags"), 93));
                int flagHeight = Math.Clamp(17 * (int)Math.Ceiling(activityFlags.Count / 3.0), 17, 102);
                row.Controls.Add(new FlagLine(activityFlags) { Location = new Point(S(82), S(93)), Size = new Size(S(175), S(flagHeight)), AlignTop = true, ForeColor = ForeColor, Font = new Font("Segoe UI", 6.8f) });
                nextDetailTop += flagHeight;
            }
            var statuses = statusByPrompt[i];
            if (statuses.Count > 0)
            {
                row.Controls.Add(Field(L.Pick("Status", "Status"), nextDetailTop));
                row.Controls.Add(new FlagLine(statuses) { Location = new Point(S(82), S(nextDetailTop)), Size = new Size(S(175), S(17)), ForeColor = ForeColor, Font = new Font("Segoe UI", 6.8f, FontStyle.Bold) });
            }
            string occurredAt = prompt.At.ToLocalTime().ToString("dd.MM.yyyy · HH:mm", L.Culture);
            foreach (Control control in row.Controls.Cast<Control>().Append(row))
            {
                control.Click += (_, _) => ShowPrompt(prompt, position);
                tips.SetToolTip(control, occurredAt);
            }
            list.Controls.Add(row);
            rowTop += rowContentHeight + rowGap;
            if (i < prompts.Count - 1)
                list.Controls.Add(new Panel { Location = new Point(S(1), S(rowTop - rowGap / 2)), Size = new Size(S(278), S(1)), BackColor = Color.FromArgb(96, 96, 96) });
        }
        body.Controls.Add(list);
        return y + totalHeight + 2;
    }
    void ShowPrompt(PromptUsage prompt, int rank)
    {
        CompletionPopup.Display(prompt, closeAutomatically: false);
    }
}
