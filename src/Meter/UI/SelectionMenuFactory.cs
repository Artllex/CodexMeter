using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;

namespace CodexMeter;

sealed class SelectionMenuFactory
{
    ContextMenuStrip? activeSelectMenu;
    internal ContextMenuStrip? ActiveMenu => activeSelectMenu;
    static Color Raised => UiTheme.MenuSurface;
    static Color MainText => UiTheme.MainText;
    public Button Create(Control owner, Func<int, int> S, string[] choices, int selected, int y, Action<int> changed, bool searchable = false, int x = 20, int width = 280, int verticalTextOffset = 0, Func<int, string>? selectedLabel = null)
    {
        bool showConversationIcons = searchable && selected > 0;
        var select = new Button { Text = showConversationIcons ? "" : selectedLabel?.Invoke(selected) ?? choices[selected], Location = new Point(S(x), S(y)), Size = new Size(S(width), S(27)), Padding = new Padding(S(10), S(verticalTextOffset * 2), S(36), 0), FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, BackColor = Raised, ForeColor = MainText, Font = UiTheme.Font(8.5f), TabStop = true };
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
            var menu = new ContextMenuStrip { BackColor = Raised, ForeColor = MainText, ShowImageMargin = false, Font = UiTheme.Font(8.5f), Renderer = new DarkMenuRenderer() };
            activeSelectMenu = menu;
            menu.Closing += (_, _) =>
            {
                // Windows dismisses a ContextMenuStrip before the owner receives Click.
                // Remember that click so the same control acts strictly as a toggle.
                if (select.RectangleToScreen(select.ClientRectangle).Contains(Cursor.Position)) suppressNextOpen = true;
            };
            menu.Closed += (_, _) => { if (activeSelectMenu == menu) activeSelectMenu = null; };
            select.Disposed += (_, _) => menu.Dispose();
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
                    // Keep ordinary dropdown entries at the same 30-logical-pixel
                    // row height as searchable conversation rows.
                    item.AutoSize = false;
                    item.Size = new Size(S(width), S(UiMetrics.SelectionRowHeight));
                    item.Padding = new Padding(S(5), 0, S(5), 0);
                    item.TextAlign = ContentAlignment.MiddleLeft;
                    item.MouseEnter += (_, _) => { item.BackColor = DarkMenuRenderer.HoverColor; item.Invalidate(); };
                    item.MouseLeave += (_, _) => { item.BackColor = Color.Empty; item.Invalidate(); };
                    item.Click += (_, _) =>
                    {
                        if (selectionQueued) return;
                        selectionQueued = true;
                        owner.BeginInvoke((Action)(() =>
                        {
                            if (!menu.IsDisposed) menu.Dispose();
                            if (owner.IsDisposed) return;
                            changed(index);

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
                var icon = new Label { Text = "\uE721", Location = new Point(-6, 2), Size = new Size(dividerLeftPx, searchHeightPx), ForeColor = MainText, BackColor = Raised, Font = UiTheme.Font(14, family: "Segoe Fluent Icons"), TextAlign = ContentAlignment.MiddleCenter };
                var divider = new Panel { Location = new Point(dividerLeftPx, (searchHeightPx - dividerHeightPx) / 2), Size = new Size(1, dividerHeightPx), BackColor = Color.FromArgb(100, 100, 100) };
                search = new TextBox { Size = new Size(searchPanel.Width - fieldLeftPx - rightPaddingPx, S(23)), BackColor = Raised, ForeColor = MainText, BorderStyle = BorderStyle.None, Font = UiTheme.Font(8.5f), PlaceholderText = L.Pick("Szukaj rozmowy…", "Search conversations…") };
                search.Location = new Point(fieldLeftPx, Math.Max(0, (searchHeightPx - search.Height) / 2));
                searchPanel.Controls.Add(icon); searchPanel.Controls.Add(divider); searchPanel.Controls.Add(search);
                menu.Items.Add(new ToolStripControlHost(searchPanel) { AutoSize = false, Size = searchPanel.Size, Margin = new Padding(S(7), S(6), S(7), S(5)), BackColor = Raised });
                menu.Items.Add(new ToolStripSeparator());
                const int arrowHeight = 18;
                const int rowHeight = UiMetrics.SelectionRowHeight;
                var listShell = new Panel { Size = new Size(S(252), S(220)), BackColor = Raised };
                var viewport = new Panel { Location = Point.Empty, Size = listShell.Size, BackColor = Raised };
                var up = new Label { Text = "⌃", Height = S(arrowHeight), ForeColor = MainText, BackColor = Raised, Font = UiTheme.Font(10), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
                var down = new Label { Text = "⌄", Height = S(arrowHeight), ForeColor = MainText, BackColor = Raised, Font = UiTheme.Font(10), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
                listShell.Controls.Add(viewport); listShell.Controls.Add(up); listShell.Controls.Add(down);
                menu.Items.Add(new ToolStripControlHost(listShell) { AutoSize = false, Size = listShell.Size, Margin = new Padding(S(7), 0, S(7), 0), BackColor = Raised });
                int firstVisibleRow = 0;
                int renderedCapacity = 1;
                List<int> filteredIndices = [];
                RepeatScrollController? repeat = null;
                repeat = new RepeatScrollController(MoveOne);
                void StopRepeating() => repeat?.Stop();
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
                        var item = new Panel { Location = new Point(0, row * rowPixels), Size = new Size(viewport.Width, rowPixels), BackColor = Raised, Cursor = Cursors.Hand };
                        var check = new Label { Text = index == selected ? "✓" : "", Location = new Point(S(5), 0), Size = new Size(S(20), rowPixels), ForeColor = MainText, BackColor = Raised, Font = UiTheme.Font(10), TextAlign = ContentAlignment.MiddleCenter };
                        var conversation = new ConversationLine("", choices[index]) { Location = new Point(S(28), 0), Size = new Size(Math.Max(1, item.Width - S(33)), rowPixels), ForeColor = MainText, BackColor = Raised, Font = UiTheme.Font(8.5f), Cursor = Cursors.Hand };
                        void ChooseConversation() { if (!selectionQueued) { selectionQueued = true; menu.Close(); changed(index); } }
                        void SetConversationHover(bool hovered)
                        {
                            var color = hovered ? DarkMenuRenderer.HoverColor : Raised;
                            item.BackColor = color; check.BackColor = color; conversation.BackColor = color;
                            conversation.Invalidate();
                        }
                        foreach (Control control in new Control[] { item, check, conversation })
                        {
                            control.Click += (_, _) => ChooseConversation();
                            control.MouseEnter += (_, _) => SetConversationHover(true);
                            control.MouseLeave += (_, _) => SetConversationHover(false);
                            control.MouseWheel += (_, e) => MoveOne(e.Delta < 0 ? 1 : -1);
                        }
                        item.Controls.Add(check); item.Controls.Add(conversation);
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
                foreach (var (arrow, direction) in new[] { (up, -1), (down, 1) })
                {
                    arrow.MouseEnter += (_, _) => SetArrowHover(arrow, true);
                    arrow.MouseLeave += (_, _) => { SetArrowHover(arrow, false); StopRepeating(); };
                    arrow.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) repeat.Start(direction); };
                    arrow.MouseUp += (_, _) => StopRepeating();
                }
                menu.Closed += (_, _) => repeat.Stop();
                menu.Disposed += (_, _) => repeat.Dispose();
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
        var arrow = new Label { Text = "▾", Location = new Point(select.Width - S(28) - 1, 1), Size = new Size(S(28), select.Height - 2), BackColor = Raised, ForeColor = MainText, Font = UiTheme.Font(8.5f), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
        ConversationLine? selectedConversation = null;
        if (showConversationIcons)
        {
            selectedConversation = new ConversationLine("", choices[selected]) { Location = new Point(S(10), 0), Size = new Size(select.Width - S(46), select.Height - 2), ForeColor = MainText, BackColor = Raised, Font = UiTheme.Font(8.5f), Cursor = Cursors.Hand };
            selectedConversation.Click += (_, _) => select.PerformClick();
            select.Controls.Add(selectedConversation);
        }
        void UpdateSelectHover()
        {
            bool hovered = select.RectangleToScreen(select.ClientRectangle).Contains(Cursor.Position);
            select.BackColor = hovered ? DarkMenuRenderer.HoverColor : Raised;
            arrow.BackColor = hovered ? DarkMenuRenderer.HoverColor : Raised;
            if (selectedConversation != null) { selectedConversation.BackColor = select.BackColor; selectedConversation.Invalidate(); }
        }
        select.MouseEnter += (_, _) => UpdateSelectHover();
        select.MouseLeave += (_, _) => UpdateSelectHover();
        arrow.MouseEnter += (_, _) => UpdateSelectHover();
        arrow.MouseLeave += (_, _) => UpdateSelectHover();
        arrow.MouseDown += (_, _) => { UpdateSelectHover(); CloseOpenMenu(); };
        arrow.Click += (_, _) => select.PerformClick();
        select.Controls.Add(arrow); arrow.BringToFront();
        return select;
    }
}
