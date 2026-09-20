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
            if (File.Exists(path)) keepOnTaskbar = JsonNode.Parse(File.ReadAllText(path))?["keepOnTaskbar"]?.GetValue<bool>() ?? true;
        }
        catch (Exception ex) { Diagnostics.Report("Load settings", ex); keepOnTaskbar = true; }
    }
    void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "settings.json"), new JsonObject { ["keepOnTaskbar"] = keepOnTaskbar }.ToJsonString());
        }
        catch (Exception ex) { Diagnostics.Report("Save settings", ex); }
    }
}
