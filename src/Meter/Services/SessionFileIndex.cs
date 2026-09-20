namespace CodexMeter;

static class SessionFileIndex
{
    static readonly object Gate = new();
    static string cachedHome = "";
    static string[] paths = Array.Empty<string>();
    static DateTime expires;
    public static void Invalidate() { lock (Gate) expires = DateTime.MinValue; }
    public static string[] Get(string home)
    {
        lock (Gate)
        {
            if (cachedHome == home && DateTime.UtcNow < expires) return paths;
            var next = new List<string>();
            foreach (string folder in new[] { "sessions", "archived_sessions" })
            {
                string directory = Path.Combine(home, folder);
                if (Directory.Exists(directory))
                    next.AddRange(Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.AllDirectories));
            }
            cachedHome = home;
            paths = next.ToArray();
            expires = DateTime.UtcNow.AddMinutes(3);
            return paths;
        }
    }
}
