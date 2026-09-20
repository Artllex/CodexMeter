using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;

namespace CodexMeter;

sealed class ChartHoverPopup : Form
{
    readonly Panel conversationHost;
    readonly Label modelValue;
    readonly Label promptValue;
    readonly Label dateValue;
    readonly Label inputValue;
    readonly Label outputValue;
    readonly Label flagsField;
    readonly FlagLine flagsValue;
    readonly Label statusField;
    readonly FlagLine statusValue;
    readonly TableLayoutPanel layout;
    readonly Func<int, int> P;

    public ChartHoverPopup()
    {
        float scale = UiDpi.ScaleForScreen(Screen.FromPoint(Cursor.Position));
        P = value => Math.Max(1, (int)Math.Round(value * scale));
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(P(360), P(180));
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Text;
        Padding = new Padding(P(9), P(8), P(9), P(8));
        layout = new MetadataTable { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Margin = Padding.Empty, Padding = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, P(102)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 6; row++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, P(27)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        Label Field(string text) => UiControls.Text(text, 8.5f, Color.FromArgb(178, 178, 178), bold: true);
        Label Value(bool bold = false) => UiControls.Text("", 8.5f, ForeColor, bold);
        conversationHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        modelValue = Value(); promptValue = Value(); dateValue = Value(); inputValue = Value(); outputValue = Value();
        layout.Controls.Add(Field(L.Pick("Rozmowa", "Conversation")), 0, 0); layout.Controls.Add(conversationHost, 1, 0);
        layout.Controls.Add(Field(L.Pick("Model", "Model")), 0, 1); layout.Controls.Add(modelValue, 1, 1);
        layout.Controls.Add(Field(L.Pick("Zapytanie", "Prompt")), 0, 2); layout.Controls.Add(promptValue, 1, 2);
        layout.Controls.Add(Field(L.Pick("Data i godzina", "Date and time")), 0, 3); layout.Controls.Add(dateValue, 1, 3);
        layout.Controls.Add(Field(L.Pick("Tokeny IN", "Input tokens")), 0, 4); layout.Controls.Add(inputValue, 1, 4);
        layout.Controls.Add(Field(L.Pick("Tokeny OUT", "Output tokens")), 0, 5); layout.Controls.Add(outputValue, 1, 5);
        flagsField = Field(L.Pick("Flagi", "Flags"));
        flagsValue = new FlagLine { Dock = DockStyle.Fill, Margin = Padding.Empty, AlignTop = true, ForeColor = ForeColor, Font = UiTheme.Font(7.5f, FontStyle.Bold) };
        layout.Controls.Add(flagsField, 0, 6); layout.Controls.Add(flagsValue, 1, 6);
        statusField = Field(L.Pick("Status", "Status"));
        statusValue = new FlagLine { Dock = DockStyle.Fill, Margin = Padding.Empty, AlignTop = true, ForeColor = ForeColor, Font = UiTheme.Font(7.5f, FontStyle.Bold) };
        layout.Controls.Add(statusField, 0, 7); layout.Controls.Add(statusValue, 1, 7);
        flagsField.TextAlign = statusField.TextAlign = ContentAlignment.TopLeft;
        flagsField.Padding = statusField.Padding = new Padding(0, P(2), 0, 0);
        Controls.Add(layout);
    }

    public void ShowBucket(ChartBucket bucket, Point anchor)
    {
        var prompt = bucket.Prompt;
        string conversation = string.IsNullOrWhiteSpace(prompt?.Conversation) ? L.LocalConversation : prompt.Conversation;
        string order = prompt?.ConversationIndex > 0 ? $"[{prompt.ConversationIndex}] " : "";
        foreach (Control child in conversationHost.Controls.Cast<Control>().ToArray()) { conversationHost.Controls.Remove(child); child.Dispose(); }
        conversationHost.Controls.Add(new ConversationLine("", conversation, order) { Dock = DockStyle.Fill, ForeColor = ForeColor, Font = UiTheme.Font(8.5f, FontStyle.Bold) });
        string model = string.IsNullOrWhiteSpace(prompt?.Model) ? L.Pick("niedostępny", "unavailable") : prompt.Model;
        modelValue.Text = model + " · " + L.Thinking(prompt?.ReasoningEffort ?? "");
        string request = string.IsNullOrWhiteSpace(prompt?.Text) ? L.NoData : string.Join(" ", prompt.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        promptValue.Text = request.Length > 72 ? request[..72].TrimEnd() + "…" : request;
        dateValue.Text = bucket.At?.ToLocalTime().ToString("dd.MM.yyyy · HH:mm:ss", L.Culture) ?? L.NoData;
        inputValue.Text = L.Short(bucket.InputTokens);
        inputValue.ForeColor = TokenVisuals.Input(bucket.InputTokens);
        outputValue.Text = L.Short(bucket.OutputTokens);
        outputValue.ForeColor = TokenVisuals.Output(bucket.OutputTokens);
        var flags = PromptFlags.Items(prompt);
        bool showFlags = flags.Count > 0;
        flagsField.Visible = flagsValue.Visible = showFlags;
        flagsValue.Flags = flags;
        int flagsHeight = showFlags ? FlagLine.RequiredHeight(flags, flagsValue.Font, P(240), P(24)) : 0;
        layout.RowStyles[6].Height = flagsHeight;
        var statuses = PromptFlags.StatusItems(prompt);
        bool showStatus = statuses.Count > 0;
        statusField.Visible = statusValue.Visible = showStatus;
        statusValue.Flags = statuses;
        int statusHeight = showStatus ? P(27) : 0;
        layout.RowStyles[7].Height = statusHeight;
        ClientSize = new Size(P(360), P(180) + flagsHeight + statusHeight);
        var area = Screen.FromPoint(anchor).WorkingArea;
        int x = anchor.X + 14, y = anchor.Y + 14;
        if (x + Width > area.Right) x = anchor.X - Width - 14;
        if (y + Height > area.Bottom) y = anchor.Y - Height - 14;
        Location = new Point(Math.Max(area.Left, x), Math.Max(area.Top, y));
        if (!Visible) Show();
        else Invalidate(true);
    }

    public void HidePopup() { if (Visible) Hide(); }
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
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Color.FromArgb(88, 88, 88));
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
}
