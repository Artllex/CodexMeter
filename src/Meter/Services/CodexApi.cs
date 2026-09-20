using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace CodexMeter;

static class Api
{
    public static async Task<JsonObject> Read()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        var exe = Directory.Exists(root) ? Directory.GetFiles(root, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        var info = new ProcessStartInfo(exe ?? "codex.exe", "app-server --stdio") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(info) ?? throw new Exception("Nie można uruchomić Codex.");
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        async Task<JsonNode> Call(int id, string method, object? parameters = null)
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { id, method, @params = parameters }));
            await process.StandardInput.FlushAsync();
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync().WaitAsync(timeout.Token);
                if (line == null) throw new Exception("Połączenie z Codex zostało zamknięte. Uruchom Codex i sprawdź logowanie.");
                JsonNode? message;
                try { message = JsonNode.Parse(line); } catch { continue; }
                if (message?["id"]?.ToString() != id.ToString()) continue;
                if (message["error"] != null) throw new Exception(message["error"]?["message"]?.ToString() ?? "Błąd odczytu konta.");
                return message["result"] ?? new JsonObject();
            }
        }
        try
        {
            await Call(1, "initialize", new { clientInfo = new { name = "codex_meter", title = "Codex Meter", version = "1.0.0" }, capabilities = new { experimentalApi = true } });
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}");
            var result = new JsonObject();
            result["limits"] = (await Call(2, "account/rateLimits/read")).DeepCopy();
            try { result["usage"] = (await Call(3, "account/usage/read")).DeepCopy(); }
            catch (Exception ex) { result["usageError"] = ex.Message; }
            result["fetchedAt"] = DateTimeOffset.Now.ToString("O");
            return result;
        }
        finally { try { if (!process.HasExited) process.Kill(true); } catch { } }
    }
    public static JsonNode DeepCopy(this JsonNode node) => JsonNode.Parse(node.ToJsonString())!;
}
