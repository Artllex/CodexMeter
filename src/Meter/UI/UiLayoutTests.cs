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
