using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;

namespace CodexMeter;

partial class MeterForm
{
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
}
