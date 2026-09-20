using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;

namespace CodexMeter;

sealed class CompletionPopup : Form
{
    static readonly List<CompletionPopup> Active = new();
    readonly System.Windows.Forms.Timer closeTimer = new() { Interval = 10000 };
    readonly bool closesAutomatically;
    readonly Screen targetScreen;
    bool lifetimeElapsed;

    CompletionPopup(PromptUsage prompt, bool closesAutomatically = false, Screen? targetScreen = null)
    {
        this.closesAutomatically = closesAutomatically;
        this.targetScreen = targetScreen ?? Screen.FromPoint(Cursor.Position);
        float dpiScale = UiDpi.ScaleForScreen(this.targetScreen);
        int P(int value) => Math.Max(1, (int)Math.Round(value * dpiScale));
        var activityFlags = PromptFlags.Items(prompt);
        var statusFlags = PromptFlags.StatusItems(prompt);
        bool hasPromptFlags = activityFlags.Count > 0, hasPromptStatus = statusFlags.Count > 0;
        var popupFlagFont = UiTheme.Font(7.3f);
        int flagsHeight = hasPromptFlags ? Math.Max(24, UiMetrics.LogicalHeight(FlagLine.RequiredHeight(activityFlags, popupFlagFont, P(306), P(24)), dpiScale)) : 0;
        int statusHeight = hasPromptStatus ? 24 : 0;
        string projectLocation = prompt.ProjectLocation;
        bool hasProjectLocation = !string.IsNullOrWhiteSpace(projectLocation);
        int metadataHeight = (hasPromptFlags ? flagsHeight : 0) + statusHeight + (hasProjectLocation ? 24 : 0);
        string rawParameters = FormatRawParameters(prompt.RawParameters);
        int parameterLineCount = string.IsNullOrWhiteSpace(rawParameters) ? 0 : rawParameters.Split(Environment.NewLine).Length;
        bool hasParameters = parameterLineCount > 0;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(P(440), P(256 + metadataHeight));
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Text;
        Padding = new Padding(P(12), P(10), P(12), P(10));

        var layout = new MetadataTable
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8 + (hasPromptFlags ? 1 : 0) + (hasPromptStatus ? 1 : 0) + (hasProjectLocation ? 1 : 0) + (hasParameters ? 2 : 0),
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, P(110)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(28)));
        for (int row = 0; row < 2; row++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(24)));
        if (hasPromptFlags) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(flagsHeight)));
        if (hasPromptStatus) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(statusHeight)));
        if (hasProjectLocation) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(24)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(6)));
        for (int row = 0; row < 2; row++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(24)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(6)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(26)));
        if (hasParameters)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(6)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(26)));
        }

        var fieldColor = UiTheme.Muted;
        var valueColor = UiTheme.Text;
        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = L.Pick("Zakończono przetwarzanie", "Processing completed"),
            Font = UiTheme.Font(10, FontStyle.Bold),
            ForeColor = valueColor,
            TextAlign = ContentAlignment.MiddleLeft
        };
        string conversation = string.IsNullOrWhiteSpace(prompt.Conversation) ? L.LocalConversation : prompt.Conversation;
        string model = string.IsNullOrWhiteSpace(prompt.Model) ? L.Pick("niedostępny", "unavailable") : prompt.Model;
        string reasoningEffort = prompt.ReasoningEffort;
        var itemFont = UiTheme.Font(8.3f);
        Label FieldLabel(string text) => UiControls.Text(text, 8.3f, fieldColor, bold: true);
        Label FieldValue(string text, Color? color = null) => UiControls.Text(text, 8.3f, color ?? valueColor);
        Control ModelValue() => new ModelLine(model, reasoningEffort) { Dock = DockStyle.Fill, Margin = Padding.Empty, Font = itemFont, ForeColor = valueColor };
        string conversationOrder = prompt.ConversationIndex > 0 ? $"[{prompt.ConversationIndex}] " : "";
        var context = new ConversationLine("", conversation, conversationOrder) { Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = valueColor, Font = UiTheme.Font(8.3f, FontStyle.Bold) };
        var separator = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, P(3), 0, P(4)), BackColor = UiTheme.Separator, Height = P(1) };
        var contentSeparator = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, P(3), 0, P(4)), BackColor = UiTheme.Separator, Height = P(1) };
        string popupText = string.IsNullOrWhiteSpace(prompt.OriginalText) ? prompt.Text : prompt.OriginalText;
        string fullText = popupText.Replace("\r\n", "\n").Replace("\n", Environment.NewLine).Trim();
        var contentSections = PromptContent.Sections(fullText);
        string requestText = PromptContent.RequestPreview(contentSections);
        string singleLineText = string.Join(" ", requestText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        string collapsedText = singleLineText.Length > 72 ? singleLineText[..72].TrimEnd() + "…" : singleLineText;
        bool canExpandContent = singleLineText.Length > 72 || fullText.Contains(Environment.NewLine, StringComparison.Ordinal);
        var contentPanel = new ExpandableDetailSection(P, itemFont, canExpandContent, wrap: true);
        var fullPrompt = contentPanel.Editor;
        fullPrompt.Text = collapsedText;
        var expandContent = contentPanel.Toggle;
        var parametersSeparator = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, P(3), 0, P(3)), BackColor = UiTheme.Separator, Height = P(1), Visible = hasParameters };
        var parametersPanel = new ExpandableDetailSection(P, itemFont, hasParameters, wrap: false) { Visible = hasParameters };
        var parametersJsonFont = UiTheme.Font(7.2f, family: "Consolas");
        var fullParameters = parametersPanel.Editor;
        string collapsedParameters = L.Pick($"{parameterLineCount} linii…", $"{parameterLineCount} lines…");
        var expandParameters = parametersPanel.Toggle;
        var titleBar = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var close = new Button { Text = "X", Dock = DockStyle.Right, Width = P(32), FlatStyle = FlatStyle.Flat, BackColor = UiTheme.Raised, ForeColor = valueColor, Font = UiTheme.Font(8), TabStop = false };
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
        int locationRow = hasProjectLocation ? nextMetadataRow++ : -1;
        int separatorRow = nextMetadataRow;
        int inputRow = separatorRow + 1;
        int outputRow = separatorRow + 2;
        int contentSeparatorRow = separatorRow + 3;
        int contentRow = separatorRow + 4;
        int parametersSeparatorRow = hasParameters ? contentRow + 1 : -1;
        int parametersRow = hasParameters ? contentRow + 2 : -1;
        if (hasPromptFlags)
        {
            var flagsLabel = FieldLabel(L.Pick("Flagi", "Flags"));
            bool flagsWrap = flagsHeight > 24;
            if (flagsWrap)
            {
                flagsLabel.TextAlign = ContentAlignment.TopLeft;
                flagsLabel.Padding = new Padding(0, P(2), 0, 0);
            }
            layout.Controls.Add(flagsLabel, 0, flagsRow);
            layout.Controls.Add(new FlagLine(activityFlags) { Dock = DockStyle.Fill, Margin = Padding.Empty, AlignTop = flagsWrap, ForeColor = valueColor, Font = popupFlagFont }, 1, flagsRow);
        }
        if (hasPromptStatus)
        {
            layout.Controls.Add(FieldLabel(L.Pick("Status", "Status")), 0, statusRow);
            layout.Controls.Add(new FlagLine(statusFlags) { Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = valueColor, Font = UiTheme.Font(7.3f, FontStyle.Bold) }, 1, statusRow);
        }
        if (hasProjectLocation)
        {
            var locationLink = new LinkLabel { Text = projectLocation, Dock = DockStyle.Fill, Margin = Padding.Empty, Font = itemFont, LinkColor = Color.FromArgb(102, 190, 237), ActiveLinkColor = Color.FromArgb(153, 211, 246), VisitedLinkColor = Color.FromArgb(102, 190, 237), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand };
            locationLink.LinkClicked += (_, _) =>
            {
                if (Directory.Exists(projectLocation)) Process.Start(new ProcessStartInfo { FileName = projectLocation, UseShellExecute = true });
            };
            layout.Controls.Add(FieldLabel(L.Pick("Lokalizacja", "Location")), 0, locationRow);
            layout.Controls.Add(locationLink, 1, locationRow);
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
        void UpdateContentLayout()
        {
            int contentHeight = contentPanel.ContentHeight();
            int parametersHeight = hasParameters ? parametersPanel.ContentHeight() : 0;
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
            contentPanel.SetViewport(contentHeight, contentExpanded);
            parametersPanel.SetViewport(parametersHeight, parametersExpanded);
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
        var popup = new CompletionPopup(prompt, closeAutomatically, Screen.FromPoint(Cursor.Position));
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
            ProjectLocation = "C:\\CODE\\projekty\\CodexMeter",
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
        int collapsedHeight = popup.Height;
        int sectionIndex = 0;
        foreach (var expander in Descendants(popup).OfType<ActionButton>().ToArray())
        {
            expander.PerformClick();
            Application.DoEvents();
            using var expandedImage = new Bitmap(popup.Width, popup.Height);
            popup.DrawToBitmap(expandedImage, new Rectangle(Point.Empty, expandedImage.Size));
            string expandedPath = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + $"-expanded-{sectionIndex++}" + Path.GetExtension(path));
            expandedImage.Save(expandedPath, System.Drawing.Imaging.ImageFormat.Png);
            if (expander.Text != "⌃") throw new InvalidOperationException("Section did not expand.");
            expander.PerformClick();
            Application.DoEvents();
            if (popup.Height != collapsedHeight || expander.Text != "⌄") throw new InvalidOperationException("Section did not restore its collapsed layout.");
        }
        popup.Hide();
    }

    static void Reposition()
    {
        var bottoms = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            var popup = Active[i];
            if (popup.IsDisposed) continue;
            var area = popup.targetScreen.WorkingArea;
            float scale = UiDpi.ScaleForScreen(popup.targetScreen);
            int margin = Math.Max(1, (int)Math.Round(12 * scale));
            int spacing = Math.Max(1, (int)Math.Round(8 * scale));
            int bottom = bottoms.TryGetValue(popup.targetScreen.DeviceName, out int existing) ? existing : area.Bottom - margin;
            popup.Location = new Point(area.Right - popup.Width - margin, bottom - popup.Height);
            bottoms[popup.targetScreen.DeviceName] = bottom - popup.Height - spacing;
        }
    }

    static string Short(long value) => L.Short(value);
}
