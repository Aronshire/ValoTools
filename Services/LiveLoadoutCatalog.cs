using System.Net.Http;
using System.Text.Json;

namespace ValoTools.Services;

internal sealed record WeaponSkinLine(string Weapon, string Skin, string? SkinImage, string? Buddy, string? BuddyImage);

internal static class LiveLoadoutCatalog
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static Dictionary<string, string>? _weapons;
    private static Dictionary<string, (string Name, string? Image)>? _skins;
    private static Dictionary<string, (string Name, string? Image)>? _buddies;

    public static async Task<List<WeaponSkinLine>> DescribeAsync(LivePlayerLoadout player)
    {
        await LoadAsync();
        return player.Weapons.Select(weapon =>
        {
            (string Name, string? Image)? skin = weapon.SkinId is null ? null : _skins!.GetValueOrDefault(weapon.SkinId);
            (string Name, string? Image)? buddy = weapon.BuddyId is null ? null : _buddies!.GetValueOrDefault(weapon.BuddyId);
            return new WeaponSkinLine(
                _weapons!.GetValueOrDefault(weapon.WeaponId, "Unknown weapon"),
                skin?.Name ?? "Default skin", skin?.Image,
                buddy?.Name ?? "", buddy?.Image);
        }).ToList();
    }

    private static async Task LoadAsync()
    {
        if (_weapons is not null) return;
        var weaponsTask = Http.GetStringAsync("https://valorant-api.com/v1/weapons");
        var buddiesTask = Http.GetStringAsync("https://valorant-api.com/v1/buddies");
        using var weaponJson = JsonDocument.Parse(await weaponsTask);
        using var buddyJson = JsonDocument.Parse(await buddiesTask);
        _weapons = new(StringComparer.OrdinalIgnoreCase);
        _skins = new(StringComparer.OrdinalIgnoreCase);
        _buddies = new(StringComparer.OrdinalIgnoreCase);
        foreach (var weapon in weaponJson.RootElement.GetProperty("data").EnumerateArray())
        {
            _weapons[weapon.GetProperty("uuid").GetString() ?? ""] = weapon.GetProperty("displayName").GetString() ?? "Weapon";
            if (!weapon.TryGetProperty("skins", out var skins)) continue;
            foreach (var skin in skins.EnumerateArray())
                if (skin.TryGetProperty("chromas", out var chromas))
                    foreach (var chroma in chromas.EnumerateArray())
                        _skins[chroma.GetProperty("uuid").GetString() ?? ""] = (
                            chroma.GetProperty("displayName").GetString() ?? "Skin",
                            GetString(chroma, "fullRender") ?? GetString(chroma, "displayIcon"));
        }
        foreach (var buddy in buddyJson.RootElement.GetProperty("data").EnumerateArray())
            _buddies[buddy.GetProperty("uuid").GetString() ?? ""] = (
                buddy.GetProperty("displayName").GetString() ?? "Buddy", GetString(buddy, "displayIcon"));
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
