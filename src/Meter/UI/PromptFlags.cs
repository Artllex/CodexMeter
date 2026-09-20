namespace CodexMeter;

static class PromptFlags
{
    public static List<PromptFlag> Items(PromptUsage? prompt)
    {
        var flags = new List<PromptFlag>();
        if (prompt == null) return flags;
        if (prompt.FileCount > 0) flags.Add(new PromptFlag($"Files ({prompt.FileCount})", Color.FromArgb(91, 156, 255)));
        if (prompt.PictureCount > 0) flags.Add(new PromptFlag($"Picture ({prompt.PictureCount})", Color.FromArgb(230, 100, 173)));
        if (prompt.WorkedOnCode) flags.Add(new PromptFlag("Code", Color.FromArgb(77, 202, 218)));
        if (prompt.TestsRun) flags.Add(new PromptFlag("Test", Color.FromArgb(124, 205, 116)));
        if (prompt.ProjectBuilt) flags.Add(new PromptFlag("Build", Color.FromArgb(238, 195, 73)));
        if (prompt.GitCommit) flags.Add(new PromptFlag("Commit", Color.FromArgb(244, 149, 72)));
        if (prompt.GitPush) flags.Add(new PromptFlag("Push", Color.FromArgb(242, 112, 92)));
        if (prompt.PullRequest) flags.Add(new PromptFlag("PR", Color.FromArgb(186, 125, 242)));
        if (prompt.PackageBuilt) flags.Add(new PromptFlag("Package", Color.FromArgb(218, 170, 84)));
        if (prompt.ReleaseCreated) flags.Add(new PromptFlag("Release", Color.FromArgb(255, 105, 145)));
        if (prompt.GeneratedPicture) flags.Add(new PromptFlag("Pic. Gen.", Color.FromArgb(168, 112, 244)));
        if (prompt.UsedWeb) flags.Add(new PromptFlag("Web", Color.FromArgb(75, 176, 235)));
        if (prompt.UsedBrowser) flags.Add(new PromptFlag("Browser", Color.FromArgb(67, 199, 184)));
        if (prompt.McpTools.Count > 0) flags.Add(new PromptFlag("MCP (" + string.Join(", ", prompt.McpTools.OrderBy(x => x)) + ")", Color.FromArgb(88, 207, 142)));
        if (prompt.DependenciesChanged) flags.Add(new PromptFlag("Dependencies", Color.FromArgb(215, 160, 80)));
        if (prompt.InstalledSoftware) flags.Add(new PromptFlag("Install", Color.FromArgb(123, 187, 104)));
        if (prompt.DocumentsChanged) flags.Add(new PromptFlag("Docs", Color.FromArgb(115, 157, 232)));
        if (prompt.AgentCount > 0) flags.Add(new PromptFlag($"Agent ({prompt.AgentCount})", Color.FromArgb(196, 119, 224)));
        if (prompt.AutomationChanged) flags.Add(new PromptFlag("Automation", Color.FromArgb(72, 192, 207)));
        return flags;
    }
    public static List<PromptFlag> StatusItems(PromptUsage? prompt)
    {
        var statuses = new List<PromptFlag>();
        if (prompt == null) return statuses;
        if (prompt.Failed) statuses.Add(new PromptFlag("Failed", Color.FromArgb(240, 92, 92)));
        if (prompt.Cancelled) statuses.Add(new PromptFlag("Cancelled", Color.FromArgb(164, 164, 164)));
        if (prompt.Partial) statuses.Add(new PromptFlag("Partial", Color.FromArgb(245, 183, 75)));
        return statuses;
    }
    public static string Text(PromptUsage? prompt)
    {
        return string.Join("  ·  ", Items(prompt).Select(flag => flag.Text));
    }
}
