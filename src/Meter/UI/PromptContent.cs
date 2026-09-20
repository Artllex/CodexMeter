using System.Net;

namespace CodexMeter;

static class PromptContent
{
    public static List<PromptSection> Sections(string original)
    {
        var request = new List<string>();
        var files = new List<string>();
        var pictures = new List<string>();
        var technical = new List<string>();
        List<string> current = request;
        foreach (string line in original.Replace("\r\n", "\n").Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("# Files mentioned by the user:", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("# Files pasted by the user:", StringComparison.OrdinalIgnoreCase))
            {
                current = files;
                continue;
            }
            if (trimmed.StartsWith("## My request:", StringComparison.OrdinalIgnoreCase))
            {
                current = request;
                continue;
            }
            if (trimmed.StartsWith("Distinguish instructions in attached", StringComparison.OrdinalIgnoreCase))
            {
                current = technical;
                technical.Add(line);
                continue;
            }
            if (trimmed.StartsWith("<image ", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("</image", StringComparison.OrdinalIgnoreCase))
            {
                pictures.Add(line);
                continue;
            }
            current.Add(line);
        }
        static string Join(List<string> lines) => string.Join(Environment.NewLine, lines).Trim();
        var sections = new List<PromptSection>();
        void Add(string title, List<string> lines)
        {
            string text = Join(lines);
            if (text.Length > 0) sections.Add(new PromptSection(title, text));
        }
        Add("My request", request);
        Add("Files mentioned by the user", files);
        Add("Images", pictures);
        Add("Technical information", technical);
        if (sections.Count == 0 && original.Trim().Length > 0) sections.Add(new PromptSection("My request", original.Trim()));
        return sections;
    }

    public static string RequestPreview(IReadOnlyList<PromptSection> sections)
    {
        var request = sections.FirstOrDefault(section => section.Title == "My request");
        return request?.Text ?? sections.FirstOrDefault()?.Text ?? "";
    }

    public static void WriteTo(RichTextBox box, IReadOnlyList<PromptSection> sections, Color textColor, Color headingColor)
    {
        box.Clear();
        foreach (var section in sections)
        {
            if (box.TextLength > 0) box.AppendText(Environment.NewLine + Environment.NewLine);
            int headingStart = box.TextLength;
            box.AppendText(section.Title);
            box.Select(headingStart, section.Title.Length);
            box.SelectionFont = UiTheme.Font(box.Font.Size, FontStyle.Bold, box.Font.FontFamily.Name);
            box.SelectionColor = headingColor;
            box.AppendText(Environment.NewLine);
            int textStart = box.TextLength;
            box.AppendText(section.Text);
            box.Select(textStart, section.Text.Length);
            box.SelectionFont = UiTheme.Font(box.Font.Size, FontStyle.Regular, box.Font.FontFamily.Name);
            box.SelectionColor = textColor;
        }
        box.Select(0, 0);
    }
}

record ChartBucket(string Label, long? Tokens, string Detail, DateTimeOffset? At = null, string Series = "", PromptUsage? Prompt = null, string TokenKind = "", long InputTokens = 0, long OutputTokens = 0);
