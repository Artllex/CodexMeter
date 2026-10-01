using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;

namespace CodexMeter;

sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public static readonly Color HoverColor = UiTheme.Hover;
    public static ContextMenuStrip CreateMenu() => new()
    {
        BackColor = UiTheme.MenuSurface,
        ForeColor = UiTheme.MainText,
        ShowImageMargin = false,
        ShowCheckMargin = false,
        Font = UiTheme.Font(8.5f),
        Renderer = new DarkMenuRenderer()
    };

    public static void StyleRow(ToolStripItem item, int width, int height)
    {
        item.AutoSize = false;
        item.Size = new Size(width, height);
        item.Padding = new Padding(5, 0, 5, 0);
        item.TextAlign = ContentAlignment.MiddleLeft;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        bool hovered = e.Item.Selected || e.Item.BackColor == HoverColor;
        using var brush = new SolidBrush(hovered ? HoverColor : e.ToolStrip?.BackColor ?? UiTheme.Raised);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
    }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Color.FromArgb(88, 88, 88));
        e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(UiTheme.Separator);
        e.Graphics.DrawLine(pen, 6, e.Item.Height / 2, e.Item.Width - 6, e.Item.Height / 2);
    }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = UiTheme.MainText;
        var state = e.Graphics.Save();
        // Leave a little breathing room between submenu arrows and the menu edge.
        e.Graphics.TranslateTransform(-Math.Max(1, (e.Item?.Height ?? UiMetrics.SelectionRowHeight) / 8f), 0);
        base.OnRenderArrow(e);
        e.Graphics.Restore(state);
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        int left = e.TextRectangle.Left;
        var rowTextBounds = new Rectangle(
            left,
            0,
            Math.Max(1, e.Item.Width - left - 6),
            e.Item.Height);
        TextRenderer.DrawText(
            e.Graphics,
            e.Text,
            e.TextFont,
            rowTextBounds,
            e.TextColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}
