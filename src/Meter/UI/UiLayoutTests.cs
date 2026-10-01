namespace CodexMeter;

static class UiLayoutTests
{
    public static void Run(string directory)
    {
        if (UiDpi.ScaleForDpi(96) != 1f || UiDpi.ScaleForDpi(144) != 1.5f || UiDpi.ScaleForDpi(192) != 2f)
            throw new InvalidOperationException("DPI scale conversion failed.");
        foreach (float scale in new[] { 1f, 1.5f, 2f, 2.5f })
        {
            int S(int value) => (int)Math.Round(value * scale);
            var prompt = new PromptUsage { Conversation = "Test", Model = "gpt-5.6-terra", ReasoningEffort = "low", Text = "Layout verification", WorkedOnCode = true, TestsRun = true, ProjectBuilt = true, GitCommit = true, GitPush = true, GeneratedPicture = true, UsedBrowser = true };
            using var host = new Form { AutoScaleMode = AutoScaleMode.None, ClientSize = new Size(S(300), S(400)), BackColor = Color.FromArgb(35,35,35) };
            using var card = new PromptHistoryCard(prompt, S, Color.White, Color.Silver, () => { });
            host.Controls.Add(card);
            host.Show();
            Application.DoEvents();
            var table = card.Controls.OfType<MetadataTable>().Single();
            int bottom = 0;
            for (int row = 0; row < table.RowCount; row++)
            {
                var value = table.GetControlFromPosition(1, row)!;
                if (value.Top < bottom || value.Bottom > card.Height)
                    throw new InvalidOperationException("Metadata rows overlap or exceed the card.");
                bottom = value.Bottom;
                if (value is FlagLine flags && value.Height < FlagLine.RequiredHeight(flags.Flags, flags.Font, value.Width, 0))
                    throw new InvalidOperationException("Flag row clips wrapped content.");
            }
            using var bitmap = new Bitmap(card.Width, card.Height);
            card.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(directory, $"history-layout-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png"));
        }
        using (var meter = new MeterForm(test: true))
        {
            meter.Show();
            Application.DoEvents();
            var trayMenu = meter.TrayMenu;
            int menuRowHeight = (int)Math.Round(UiMetrics.SelectionRowHeight * UiDpi.ScaleForScreen(Screen.FromControl(meter)));
            if (trayMenu.Renderer is not DarkMenuRenderer || trayMenu.Items.OfType<ToolStripMenuItem>().Any(item => item.Height != menuRowHeight))
                throw new InvalidOperationException("Tray menu styling differs from selector rows.");
            var xItem = (ToolStripMenuItem)trayMenu.Items[2];
            var xMenu = (ToolStripDropDownMenu)xItem.DropDown;
            if (xMenu.Renderer is not DarkMenuRenderer || xMenu.Items.OfType<ToolStripMenuItem>().Any(item => item.Height != menuRowHeight))
                throw new InvalidOperationException("Tray submenu styling differs from selector rows.");
            if (xMenu.Width >= trayMenu.Width)
                throw new InvalidOperationException("X submenu is too wide to open beside the tray menu.");
            var area = Screen.FromControl(meter).WorkingArea;
            trayMenu.Show(new Point(area.Left + 20, area.Top + 20));
            Application.DoEvents();
            using (var menuBitmap = new Bitmap(trayMenu.Width, trayMenu.Height))
            {
                trayMenu.DrawToBitmap(menuBitmap, new Rectangle(Point.Empty, menuBitmap.Size));
                menuBitmap.Save(Path.Combine(directory, "tray-menu.png"));
            }
            xItem.ShowDropDown();
            Application.DoEvents();
            if (Math.Abs(xMenu.Left - trayMenu.Right) > 2)
                throw new InvalidOperationException($"X submenu does not touch the parent menu: parent={trayMenu.Bounds}, item={xItem.Bounds}, submenu={xMenu.Bounds}.");
            using (var submenuBitmap = new Bitmap(xMenu.Width, xMenu.Height))
            {
                xMenu.DrawToBitmap(submenuBitmap, new Rectangle(Point.Empty, submenuBitmap.Size));
                submenuBitmap.Save(Path.Combine(directory, "tray-submenu.png"));
            }
            xMenu.Close();
            trayMenu.Close();
            int nearRightX = area.Right - trayMenu.Width - xMenu.Width - 20;
            if (nearRightX >= area.Left + 20)
            {
                trayMenu.Show(new Point(nearRightX, area.Top + 20));
                Application.DoEvents();
                xItem.ShowDropDown();
                Application.DoEvents();
                if (Math.Abs(xMenu.Left - trayMenu.Right) > 2)
                    throw new InvalidOperationException($"X submenu separated near the right screen edge: parent={trayMenu.Bounds}, submenu={xMenu.Bounds}.");
                using var joined = new Bitmap(trayMenu.Width + xMenu.Width, Math.Max(trayMenu.Height, xMenu.Bottom - trayMenu.Top));
                using (var rootImage = new Bitmap(trayMenu.Width, trayMenu.Height))
                using (var childImage = new Bitmap(xMenu.Width, xMenu.Height))
                using (var graphics = Graphics.FromImage(joined))
                {
                    trayMenu.DrawToBitmap(rootImage, new Rectangle(Point.Empty, rootImage.Size));
                    xMenu.DrawToBitmap(childImage, new Rectangle(Point.Empty, childImage.Size));
                    graphics.DrawImageUnscaled(rootImage, 0, 0);
                    graphics.DrawImageUnscaled(childImage, xMenu.Left - trayMenu.Left, xMenu.Top - trayMenu.Top);
                }
                joined.Save(Path.Combine(directory, "tray-menu-with-submenu.png"));
                xMenu.Close();
                trayMenu.Close();
            }
            meter.ApplyDpiForTest(144);
            Application.DoEvents();
            int expectedWidth = (int)Math.Round(320 * 1.5);
            if (meter.ClientSize.Width != expectedWidth)
                throw new InvalidOperationException($"Main panel did not rebuild at 150% DPI: expected {expectedWidth}, actual {meter.ClientSize.Width}.");
            if (!Screen.FromControl(meter).WorkingArea.Contains(meter.Bounds))
                throw new InvalidOperationException("DPI relayout moved the panel outside the working area.");
        }
        using (var host = new Form { ClientSize = new Size(600, 500) })
        {
            var factory = new SelectionMenuFactory();
            var selector = factory.Create(host, x => x, new[] { "Input", "Output" }, 0, 0, _ => { });
            host.Controls.Add(selector);
            host.Show();
            selector.PerformClick();
            Application.DoEvents();
            var menu = factory.ActiveMenu ?? throw new InvalidOperationException("Selector did not open.");
            if (!menu.Visible || menu.Items.Count != 2 || menu.Items.Cast<ToolStripItem>().Any(item => item.Height != UiMetrics.SelectionRowHeight))
                throw new InvalidOperationException("Selector rows have inconsistent heights.");
            menu.Close();
            using var section = new ExpandableDetailSection(x => x, UiTheme.Font(8.3f), true, true) { Dock = DockStyle.None, Width = 250 };
            host.Controls.Add(section);
            section.Editor.Text = "Short preview";
            int collapsed = section.ContentHeight();
            section.Editor.Text = string.Join(Environment.NewLine, Enumerable.Repeat("Expanded text", 12));
            if (section.ContentHeight() <= collapsed) throw new InvalidOperationException("Expanded section did not grow.");
            host.Close();
        }
        CompletionPopup.ExportPreview(Path.Combine(directory, "ui-popup.png"));
        File.WriteAllText(Path.Combine(directory, "ui-layout-test.txt"), "PASS: DPI conversion and main-panel relayout, history bounds, wrapped flags at four layout scales, selector opening and detail-section growth. Popup previews exported.");
    }
}
