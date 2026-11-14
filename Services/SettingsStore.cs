using System.IO;
using System.Text.Json;

namespace ValoTools.Services;

public static class SettingsStore
{
    private static string FilePath => SettingsPath("ValoTools");
    private static string LegacyFilePath => SettingsPath("ValoInsta");
    private sealed record AppSettings(string? AgentId, bool DiscordPresenceEnabled);
    private static string SettingsPath(string appName) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName, "settings.json");

    public static string? LoadAgentId() => ReadSettings()?.AgentId;
    public static bool LoadDiscordPresenceEnabled() => ReadSettings()?.DiscordPresenceEnabled ?? false;

    private static AppSettings? ReadSettings()
    {
        foreach (var path in new[] { FilePath, LegacyFilePath })
        {
            try
            {
                if (!File.Exists(path)) continue;
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                var root = json.RootElement;
                var agentId = root.TryGetProperty("agentId", out var agent) && agent.ValueKind == JsonValueKind.String
                    ? agent.GetString() : null;
                var discordEnabled = root.TryGetProperty("discordPresenceEnabled", out var discord) &&
                                     discord.ValueKind == JsonValueKind.True;
                return new AppSettings(agentId, discordEnabled);
            }
            catch { }
        }
        return null;
    }

    public static void SaveAgentId(string id)
    {
        var settings = ReadSettings();
        Save(new AppSettings(id, settings?.DiscordPresenceEnabled ?? false));
    }

    public static void SaveDiscordPresenceEnabled(bool enabled)
    {
        var settings = ReadSettings();
        Save(new AppSettings(settings?.AgentId, enabled));
    }

    private static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new
            {
                agentId = settings.AgentId,
                discordPresenceEnabled = settings.DiscordPresenceEnabled
            }));
        }
        catch { /* Kaydedilememesi uygulama özelliklerini engellemez. */ }
    }
}
