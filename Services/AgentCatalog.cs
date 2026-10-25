using System.Net.Http;
using System.Text.Json;

namespace ValoTools.Services;

public sealed class AgentCatalog : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public async Task<List<Agent>> GetAgentsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(
            "https://valorant-api.com/v1/agents?isPlayableCharacter=true", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var result = new List<Agent>();
        foreach (var item in json.RootElement.GetProperty("data").EnumerateArray())
        {
            if (!item.GetProperty("isPlayableCharacter").GetBoolean()) continue;
            var id = item.GetProperty("uuid").GetString();
            var name = item.GetProperty("displayName").GetString();
            var icon = item.GetProperty("displayIcon").GetString();
            var portrait = item.GetProperty("fullPortrait").GetString();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(icon)) continue;
            var role = item.TryGetProperty("role", out var roleJson) && roleJson.ValueKind == JsonValueKind.Object
                ? roleJson.GetProperty("displayName").GetString() : null;
            var roleIcon = roleJson.ValueKind == JsonValueKind.Object
                && roleJson.TryGetProperty("displayIcon", out var iconJson) ? iconJson.GetString() : null;
            result.Add(new Agent { Id = id, Name = name, Role = role ?? "Agent", IconUrl = icon,
                PortraitUrl = portrait ?? icon, RoleIconUrl = roleIcon });
        }
        return result.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public void Dispose() => _http.Dispose();
}
