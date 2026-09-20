namespace CodexMeter;

sealed class ActionButton : Button
{
    public ActionButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseOverBackColor = DarkMenuRenderer.HoverColor;
        FlatAppearance.MouseDownBackColor = UiTheme.Pressed;
        TextAlign = ContentAlignment.MiddleCenter;
        Margin = Padding.Empty;
        BackColor = UiTheme.Raised;
        ForeColor = UiTheme.Text;
    }
}
