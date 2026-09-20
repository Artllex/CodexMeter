using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;

namespace CodexMeter;

sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public static readonly Color HoverColor = UiTheme.Hover;
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
