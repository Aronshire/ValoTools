using System.IO;
using System.Text.Json;

namespace ValoTools.Services;

public static class SettingsStore
{
    private static string FilePath => SettingsPath("ValoTools");
    private static string LegacyFilePath => SettingsPath("ValoInsta");
    private static string SettingsPath(string appName) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName, "settings.json");

    public static string? LoadAgentId() => ReadAgentId(FilePath) ?? ReadAgentId(LegacyFilePath);

    private static string? ReadAgentId(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.GetProperty("agentId").GetString();
        }
        catch { return null; }
    }

    public static void SaveAgentId(string id)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new { agentId = id }));
        }
        catch { /* Kaydedilememesi ajan seçimini engellemez. */ }
    }
}
