using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;

namespace CodexMeter;

partial class MeterForm
{
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
        var pinnedBounds = Bounds;
        PositionPanel(Screen.AllScreens[^1]);
        if (Bounds.Location != pinnedBounds.Location)
            throw new InvalidOperationException("Odświeżenie lub zmiana ekranu przesunęły przypięty panel.");
        HideToTray();
        Application.DoEvents();
        RevealFromTray();
        Application.DoEvents();
        if (!Visible || WindowState != FormWindowState.Normal)
            throw new InvalidOperationException("Kliknięcie ikony zasobnika nie przywróciło panelu.");
        if (Bounds.Location != pinnedBounds.Location)
            throw new InvalidOperationException("Kliknięcie ikony zasobnika przesunęło przypięty panel.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "taskbar-test.txt"),
            taskbarMeterResult.Contains("progress-cleared=0x00000000, overlay-cleared=0x00000000")
                ? "PASS: taskbar integration"
                : $"FAIL: {taskbarMeterResult}; TaskbarButtonCreated={taskbarButtonCreated}");
        var closeButton = titleBar.Controls.OfType<Button>().Single(button => button.Text == "X");
        if (closeButton.FlatAppearance.MouseOverBackColor == closeButton.BackColor)
            throw new InvalidOperationException("Przycisk X nie ma widocznego stanu hover.");
        VerifyChartRanges();
    }
    void VerifyChartRanges()
    {
        var saved = local;
        var savedFilter = chartConversationFilter;
        int savedMode = chartMode;
        try
        {
            chartConversationFilter = "";
            var at = DateTimeOffset.UtcNow;
            local = new LocalUsage(
                new[] { 0.5, 2.0, 12.0, 48.0 }.Select((hours, i) => new TokenSample($"fixture-{i}", at.AddHours(-hours), 100, InputTokens: 100)).ToList(),
                new List<PromptUsage>(), 0, 0);
            for (int mode = 0; mode < 4; mode++)
            {
                chartMode = mode;
                if (ChartPoints().Count != mode + 1) throw new InvalidOperationException("Chart time-range filtering failed.");
            }
        }
        finally { local = saved; chartConversationFilter = savedFilter; chartMode = savedMode; }
    }
}
