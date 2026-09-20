namespace CodexMeter;

record TokenSample(string Key, DateTimeOffset At, long Tokens, string SessionId = "", long InputTokens = 0, long OutputTokens = 0);
class PromptUsage
{
    public string Key { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string Text { get; set; } = "";
    public string OriginalText { get; set; } = "";
    public string RawParameters { get; set; } = "";
    public string ProjectLocation { get; set; } = "";
    public long Tokens { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public bool Complete { get; set; }
    public string SessionId { get; set; } = "";
    public string Conversation { get; set; } = "";
    public string Model { get; set; } = "";
    public string ReasoningEffort { get; set; } = "";
    public int ConversationIndex { get; set; }
    public int FileCount { get; set; }
    public int PictureCount { get; set; }
    public bool WorkedOnCode { get; set; }
    public bool TestsRun { get; set; }
    public bool ProjectBuilt { get; set; }
    public bool GitCommit { get; set; }
    public bool GitPush { get; set; }
    public bool PullRequest { get; set; }
    public bool PackageBuilt { get; set; }
    public bool ReleaseCreated { get; set; }
    public bool GeneratedPicture { get; set; }
    public bool UsedWeb { get; set; }
    public bool UsedBrowser { get; set; }
    public bool DependenciesChanged { get; set; }
    public bool InstalledSoftware { get; set; }
    public bool DocumentsChanged { get; set; }
    public int AgentCount { get; set; }
    public bool AutomationChanged { get; set; }
    public bool Failed { get; set; }
    public bool Cancelled { get; set; }
    public bool Partial { get; set; }
    public HashSet<string> McpTools { get; } = new(StringComparer.OrdinalIgnoreCase);
}
record LocalUsage(List<TokenSample> Samples, List<PromptUsage> Prompts, int Files, int Errors);
