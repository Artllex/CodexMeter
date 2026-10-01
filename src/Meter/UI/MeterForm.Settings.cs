using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;

namespace CodexMeter;

partial class MeterForm
{
    void LoadSettings()
    {
        try
        {
            string path = Path.Combine(dataDir, "settings.json");
            if (File.Exists(path))
            {
                var settings = JsonNode.Parse(File.ReadAllText(path));
                keepOnTaskbar = settings?["keepOnTaskbar"]?.GetValue<bool>() ?? true;
                showCompletionCardsAutomatically = settings?["showCompletionCardsAutomatically"]?.GetValue<bool>() ?? true;
            }
        }
        catch (Exception ex) { Diagnostics.Report("Load settings", ex); keepOnTaskbar = true; showCompletionCardsAutomatically = true; }
    }
    void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "settings.json"), new JsonObject
            {
                ["keepOnTaskbar"] = keepOnTaskbar,
                ["showCompletionCardsAutomatically"] = showCompletionCardsAutomatically
            }.ToJsonString());
        }
        catch (Exception ex) { Diagnostics.Report("Save settings", ex); }
    }
}
