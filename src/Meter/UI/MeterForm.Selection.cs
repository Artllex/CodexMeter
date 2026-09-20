namespace CodexMeter;

partial class MeterForm
{
    readonly SelectionMenuFactory selectionMenus = new();
    Button SelectAt(string[] choices, int selected, int y, Action<int> changed, bool searchable = false, int x = 20, int width = 280, int verticalTextOffset = 0, Func<int, string>? selectedLabel = null)
    {
        var control = selectionMenus.Create(this, S, choices, selected, y, index => { changed(index); Render(); },
            searchable, x, width, verticalTextOffset, selectedLabel);
        body.Controls.Add(control);
        return control;
    }
}
