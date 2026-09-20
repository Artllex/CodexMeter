using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;

namespace CodexMeter;

partial class MeterForm
{
    async Task RefreshLocal(bool showBusy = true)
    {
        if (localBusy) return;
        localBusy = true; localError = null; if (showBusy) Render();
        var completedNow = new List<PromptUsage>();
        try
        {
            var next = await Task.Run(Analytics.Read);
            var nextCompleted = next.Prompts.Where(x => x.Complete).Select(x => x.Key).ToHashSet();
            if (completedPromptKeys != null)
                completedNow = next.Prompts.Where(x => x.Complete && !completedPromptKeys.Contains(x.Key)).OrderBy(x => x.At).ToList();
            completedPromptKeys = nextCompleted;
            local = next;
        }
        catch (Exception ex) { localError = ex.Message; }
        finally
        {
            localBusy = false;
            if (!IsDisposed)
            {
                if (showBusy || completedNow.Count > 0) Render();
                foreach (var prompt in completedNow) CompletionPopup.Display(prompt);
            }
        }
    }
    void StartSessionWatchers()
    {
        if (sessionWatchers.Count > 0) return;
        string home = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        void QueueRefresh()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(() => { sessionDebounce.Stop(); sessionDebounce.Start(); }));
            }
            catch (InvalidOperationException) { }
        }
        foreach (string folder in new[] { "sessions", "archived_sessions" })
        {
            string path = Path.Combine(home, folder);
            if (!Directory.Exists(path)) continue;
            var watcher = new FileSystemWatcher(path, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Changed += (_, _) => QueueRefresh();
            watcher.Created += (_, _) => { SessionFileIndex.Invalidate(); QueueRefresh(); };
            watcher.Renamed += (_, _) => { SessionFileIndex.Invalidate(); QueueRefresh(); };
            watcher.Deleted += (_, _) => { SessionFileIndex.Invalidate(); QueueRefresh(); };
            watcher.Error += (_, e) => { Diagnostics.Report("Session watcher", e.GetException()); SessionFileIndex.Invalidate(); QueueRefresh(); };
            watcher.EnableRaisingEvents = true;
            sessionWatchers.Add(watcher);
        }
    }
    void LoadCache()
    {
        try { current = JsonNode.Parse(File.ReadAllText(Path.Combine(dataDir, "latest.json"))) as JsonObject; Render(); status.Text = L.Pick("Aktualizowanie…", "Updating…"); } catch (Exception ex) { Diagnostics.Report("Load cache", ex); }
    }
    async Task RefreshData()
    {
        if (busy) return; busy = true; status.Text = L.Pick("Aktualizowanie…", "Updating…");
        try
        {
            var next = await Api.Read();
            if (IsDisposed) return;
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "latest.tmp"), next.ToJsonString());
            File.Move(Path.Combine(dataDir, "latest.tmp"), Path.Combine(dataDir, "latest.json"), true);
            File.AppendAllText(Path.Combine(dataDir, "history.jsonl"), next.ToJsonString() + Environment.NewLine);
            current = next; remoteError = null; Render();
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                status.Text = L.Pick("Dane nieaktualne · ponowię za 3 min", "Data is stale · retrying in 3 min");
                tips.SetToolTip(status, ex.Message);
                remoteError = ex.Message;
                tray.Text = L.Pick("Codex Meter — dane nieaktualne", "Codex Meter — data is stale");
            }
        }
        finally { busy = false; }
    }
}
