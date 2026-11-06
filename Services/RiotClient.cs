using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ValoTools.Services;

public sealed record RiotContext(string PlayerId, string AccessToken, string EntitlementToken,
    string Version, RegionRoute Route);

public sealed record PregameState(string Phase, string? PlayerSelectionState);

public sealed record ValorantPresence(string? GameName, string? GameTag)
{
    public string? DisplayName => string.IsNullOrWhiteSpace(GameName) ? null :
        string.IsNullOrWhiteSpace(GameTag) ? GameName : $"{GameName}#{GameTag}";
}

public sealed class RiotClient : IDisposable
{
    private const string AgentItemTypeId = "01bb38e1-da47-4e6a-9b3d-945fe4655707";
    private static readonly HashSet<string> DefaultAgentIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "9f0d8ba9-4140-b941-57d3-a7ad57c6b417", // Brimstone
        "569fdd95-4d10-43ab-ca70-79becc718b46", // Sage
        "320b2a48-4d9b-a075-30f1-1f93a9b638fa", // Sova
        "eb93336a-449b-9c1b-0a54-a891f7921d69", // Phoenix
        "add6443a-41bd-e414-f6ad-e58d267f4e95"  // Jett
    };
    private static readonly string Platform = Convert.ToBase64String(Encoding.UTF8.GetBytes(
        "{\"platformType\":\"PC\",\"platformOS\":\"Windows\",\"platformOSVersion\":\"10.0.19042.1.256.64bit\",\"platformChipset\":\"Unknown\"}"));
    private readonly HttpClient _local;
    private readonly HttpClient _remote = new() { Timeout = TimeSpan.FromSeconds(8) };

    public RiotClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (request, _, _, _) =>
                request.RequestUri?.Host == "127.0.0.1"
        };
        _local = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<ValorantPresence?> GetValorantPresenceAsync(CancellationToken cancellationToken)
    {
        var lockfile = ReadLockfile();
        if (lockfile is null) return null;

        using var sessions = await LocalGetAsync(lockfile.Value, "product-session/v1/external-sessions", cancellationToken);
        var hasValorantSession = sessions.RootElement.EnumerateObject().Any(p =>
            p.Value.TryGetProperty("productId", out var id) && id.GetString() == "valorant");
        if (!hasValorantSession) return null;

        try
        {
            using var chat = await LocalGetAsync(lockfile.Value, "chat/v1/session", cancellationToken);
            var root = chat.RootElement;
            var name = root.TryGetProperty("game_name", out var gameName) ? gameName.GetString() : null;
            var tag = root.TryGetProperty("game_tag", out var gameTag) ? gameTag.GetString() : null;
            return new ValorantPresence(name, tag);
        }
        catch (HttpRequestException) { return new ValorantPresence(null, null); }
        catch (JsonException) { return new ValorantPresence(null, null); }
    }

    public async Task<RiotContext?> GetContextAsync(CancellationToken cancellationToken)
    {
        var lockfile = ReadLockfile();
        if (lockfile is null) return null;
        using var sessions = await LocalGetAsync(lockfile.Value, "product-session/v1/external-sessions", cancellationToken);
        var session = sessions.RootElement.EnumerateObject()
            .Select(p => p.Value)
            .FirstOrDefault(v => v.TryGetProperty("productId", out var id) && id.GetString() == "valorant");
        if (session.ValueKind == JsonValueKind.Undefined) return null;
        var version = session.GetProperty("version").GetString();
        if (string.IsNullOrWhiteSpace(version)) throw new InvalidOperationException("VALORANT sürümü okunamadı.");

        var arguments = session.GetProperty("launchConfiguration").GetProperty("arguments")
            .EnumerateArray().Select(a => a.GetString() ?? "").ToArray();
        var isPbe = session.TryGetProperty("patchlineId", out var patchline) && patchline.GetString() == "pbe";
        var route = RegionResolver.FromSessionArguments(arguments, isPbe);
        if (route is null)
        {
            try
            {
                using var locale = await LocalGetAsync(lockfile.Value, "riotclient/region-locale", cancellationToken);
                if (locale.RootElement.TryGetProperty("region", out var region))
                    route = RegionResolver.FromRegion(region.GetString(), isPbe);
            }
            catch (HttpRequestException) { }
        }
        route ??= RegionResolver.FromLog(ReadRecentShooterLog());
        if (route is null) throw new InvalidOperationException("Bölge otomatik algılanamadı. VALORANT'ı açıp yeniden deneyin.");

        using var token = await LocalGetAsync(lockfile.Value, "entitlements/v1/token", cancellationToken);
        var root = token.RootElement;
        return new RiotContext(
            root.GetProperty("subject").GetString() ?? throw new InvalidOperationException("Oyuncu kimliği eksik."),
            root.GetProperty("accessToken").GetString() ?? throw new InvalidOperationException("Erişim tokenı eksik."),
            root.GetProperty("token").GetString() ?? throw new InvalidOperationException("Entitlement tokenı eksik."),
            version, route);
    }

    public async Task<string?> GetPregameMatchIdAsync(RiotContext context, CancellationToken cancellationToken)
    {
        using var result = await RemoteAsync(context, HttpMethod.Get,
            $"pregame/v1/players/{Uri.EscapeDataString(context.PlayerId)}", cancellationToken, allowNotFound: true);
        return result?.RootElement.GetProperty("MatchID").GetString();
    }

    public async Task<string?> GetCurrentGameMatchIdAsync(RiotContext context, CancellationToken cancellationToken)
    {
        using var current = await RemoteAsync(context, HttpMethod.Get,
            $"core-game/v1/players/{Uri.EscapeDataString(context.PlayerId)}", cancellationToken, allowNotFound: true);
        return current?.RootElement.TryGetProperty("MatchID", out var id) == true ? id.GetString() : null;
    }

    public async Task<Dictionary<string, (string GameName, string TagLine)>> GetPlayerNamesAsync(
        RiotContext context, IReadOnlyCollection<string> playerIds, CancellationToken cancellationToken)
    {
        var url = $"https://pd.{context.Route.Shard}.a.pvp.net/name-service/v2/players";
        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        AddRemoteHeaders(request, context);
        request.Content = new StringContent(JsonSerializer.Serialize(playerIds), Encoding.UTF8, "application/json");
        DebugConsole.WriteLine($"[HTTP] PUT {url} ({playerIds.Count} player IDs)");
        using var response = await _remote.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        DebugConsole.WriteLine($"[HTTP] {(int)response.StatusCode} {response.StatusCode}; name response body hidden");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Name service HTTP {(int)response.StatusCode}", null, response.StatusCode);

        using var json = JsonDocument.Parse(body);
        var names = new Dictionary<string, (string GameName, string TagLine)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in json.RootElement.EnumerateArray())
        {
            var subject = item.TryGetProperty("Subject", out var subjectValue) ? subjectValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(subject)) continue;
            names[subject] = (
                item.TryGetProperty("GameName", out var gameName) ? gameName.GetString() ?? "" : "",
                item.TryGetProperty("TagLine", out var tagLine) ? tagLine.GetString() ?? "" : "");
        }
        return names;
    }

    public async Task<List<LivePlayerLoadout>?> GetLivePlayerLoadoutsAsync(RiotContext context, string matchId, CancellationToken cancellationToken)
    {
        using var match = await RemoteAsync(context, HttpMethod.Get,
            $"core-game/v1/matches/{Uri.EscapeDataString(matchId)}", cancellationToken, allowNotFound: true);
        using var loadouts = await RemoteAsync(context, HttpMethod.Get,
            $"core-game/v1/matches/{Uri.EscapeDataString(matchId)}/loadouts", cancellationToken, allowNotFound: true);
        if (match is null || loadouts is null) return null;

        var playerInfo = new Dictionary<string, (string Team, string Agent)>(StringComparer.OrdinalIgnoreCase);
        foreach (var player in match.RootElement.GetProperty("Players").EnumerateArray())
        {
            var subject = player.GetProperty("Subject").GetString();
            if (string.IsNullOrWhiteSpace(subject)) continue;
            playerInfo[subject] = (
                player.TryGetProperty("TeamID", out var team) ? team.GetString() ?? "Team" : "Team",
                player.TryGetProperty("CharacterID", out var agent) ? agent.GetString() ?? "" : "");
        }

        var result = new List<LivePlayerLoadout>();
        foreach (var entry in loadouts.RootElement.GetProperty("Loadouts").EnumerateArray())
        {
            var loadout = entry.GetProperty("Loadout");
            var subject = loadout.GetProperty("Subject").GetString();
            if (string.IsNullOrWhiteSpace(subject)) continue;
            var (team, agent) = playerInfo.GetValueOrDefault(subject, ("Team", ""));
            var weapons = new List<LiveWeaponItem>();
            if (loadout.TryGetProperty("Items", out var items) && items.ValueKind == JsonValueKind.Object)
            {
                foreach (var item in items.EnumerateObject())
                {
                    var weaponId = item.Value.TryGetProperty("ID", out var idValue) ? idValue.GetString() : item.Name;
                    if (string.IsNullOrWhiteSpace(weaponId)) continue;
                    string? skinId = null;
                    string? buddyId = null;
                    if (item.Value.TryGetProperty("Sockets", out var sockets))
                    {
                        skinId = GetSocketItemId(sockets, "3ad1b2b2-acdb-4524-852f-954a76ddae0a")
                            ?? GetSocketItemId(sockets, "e7c63390-eda7-46e0-bb7a-a6abdacd2433");
                        buddyId = GetSocketItemId(sockets, "77258665-71d1-4623-bc72-44db9bd5b3b3")
                            ?? GetSocketItemId(sockets, "dd3bf334-87f3-40bd-b043-682a57a8dc3a");
                    }
                    weapons.Add(new LiveWeaponItem(weaponId, skinId, buddyId));
                }
            }
            result.Add(new LivePlayerLoadout(subject, team, agent, weapons));
        }
        return result;
    }

    private static string? GetSocketItemId(JsonElement sockets, string socketId) =>
        sockets.TryGetProperty(socketId, out var socket) && socket.TryGetProperty("Item", out var item) &&
        item.TryGetProperty("ID", out var id) ? id.GetString() : null;

    public async Task<HashSet<string>> GetOwnedAgentIdsAsync(RiotContext context, CancellationToken cancellationToken)
    {
        var url = $"https://pd.{context.Route.Shard}.a.pvp.net/store/v1/entitlements/" +
                  $"{Uri.EscapeDataString(context.PlayerId)}/{AgentItemTypeId}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddRemoteHeaders(request, context);
        DebugConsole.WriteLine($"[HTTP] GET {url.Replace(Uri.EscapeDataString(context.PlayerId), "{puuid}", StringComparison.OrdinalIgnoreCase)}");
        using var response = await _remote.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        DebugConsole.WriteLine($"[HTTP] {(int)response.StatusCode} {response.StatusCode} ({responseBody.Length} chars)");
        DebugConsole.WriteLine($"[HTTP BODY] {LimitForConsole(responseBody)}");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Owned agents API HTTP {(int)response.StatusCode} {response.StatusCode}",
                null, response.StatusCode);
        using var json = JsonDocument.Parse(responseBody);

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entitlementLists = new List<JsonElement>();
        if (json.RootElement.TryGetProperty("Entitlements", out var directEntitlements) &&
            directEntitlements.ValueKind == JsonValueKind.Array)
            entitlementLists.Add(directEntitlements);
        else if (json.RootElement.TryGetProperty("EntitlementsByTypes", out var groups) &&
                 groups.ValueKind == JsonValueKind.Array)
        {
            foreach (var group in groups.EnumerateArray())
                if (group.TryGetProperty("ItemTypeID", out var itemTypeId) &&
                    itemTypeId.ValueKind == JsonValueKind.String &&
                    string.Equals(itemTypeId.GetString(), AgentItemTypeId, StringComparison.OrdinalIgnoreCase) &&
                    group.TryGetProperty("Entitlements", out var nestedEntitlements) &&
                    nestedEntitlements.ValueKind == JsonValueKind.Array)
                    entitlementLists.Add(nestedEntitlements);
        }
        else
        {
            var fields = json.RootElement.ValueKind == JsonValueKind.Object
                ? string.Join(", ", json.RootElement.EnumerateObject().Select(property => property.Name))
                : json.RootElement.ValueKind.ToString();
            throw new JsonException($"Ajan sahipliği yanıtı beklenen biçimde değil ({fields}).");
        }

        if (entitlementLists.Count == 0)
            throw new JsonException("Ajan sahipliği yanıtında Agents ItemTypeID grubu bulunamadı.");

        foreach (var entitlements in entitlementLists)
            foreach (var entitlement in entitlements.EnumerateArray())
                if (entitlement.TryGetProperty("ItemID", out var itemId) &&
                    itemId.ValueKind == JsonValueKind.String && itemId.GetString() is { Length: > 0 } id)
                    ids.Add(id);
        ids.UnionWith(DefaultAgentIds);
        return ids;
    }

    public async Task<PregameState?> GetPregameStateAsync(RiotContext context, string matchId, CancellationToken cancellationToken)
    {
        using var result = await RemoteAsync(context, HttpMethod.Get,
            $"pregame/v1/matches/{Uri.EscapeDataString(matchId)}", cancellationToken, allowNotFound: true);
        if (result is null) return null;
        var root = result.RootElement;
        var phase = root.GetProperty("PregameState").GetString() ?? "";
        if (phase != "character_select_active") return new PregameState(phase, null);
        if (!root.TryGetProperty("AllyTeam", out var team) || team.ValueKind != JsonValueKind.Object) return null;
        var players = team.GetProperty("Players").EnumerateArray();
        foreach (var player in players)
        {
            if (player.GetProperty("Subject").GetString() == context.PlayerId)
                return new PregameState(phase, player.GetProperty("CharacterSelectionState").GetString());
        }
        throw new InvalidOperationException("Maç verisinde oyuncu bulunamadı.");
    }

    public async Task<bool> LockAgentAsync(RiotContext context, string matchId, string agentId, CancellationToken cancellationToken)
    {
        using var result = await RemoteAsync(context, HttpMethod.Post,
            $"pregame/v1/matches/{Uri.EscapeDataString(matchId)}/lock/{Uri.EscapeDataString(agentId)}",
            cancellationToken, allowNotFound: false);
        if (result is null) return false;
        var players = result.RootElement.GetProperty("AllyTeam").GetProperty("Players").EnumerateArray();
        foreach (var player in players)
        {
            if (player.GetProperty("Subject").GetString() == context.PlayerId)
                return player.GetProperty("CharacterSelectionState").GetString() == "locked"
                    && string.Equals(player.GetProperty("CharacterID").GetString(), agentId, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private async Task<JsonDocument> LocalGetAsync((int Port, string Password) lockfile, string endpoint, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{lockfile.Port}/{endpoint}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{lockfile.Password}")));
        DebugConsole.WriteLine($"[LOCAL] GET https://127.0.0.1:{lockfile.Port}/{endpoint} (credentials hidden)");
        using var response = await _local.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        DebugConsole.WriteLine($"[LOCAL] {(int)response.StatusCode} {response.StatusCode}; response body hidden for safety");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(body);
    }

    private async Task<JsonDocument?> RemoteAsync(RiotContext context, HttpMethod method, string endpoint,
        CancellationToken cancellationToken, bool allowNotFound)
    {
        var url = $"https://glz-{context.Route.Region}-1.{context.Route.Shard}.a.pvp.net/{endpoint}";
        using var request = new HttpRequestMessage(method, url);
        AddRemoteHeaders(request, context);
        if (method == HttpMethod.Post) request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        DebugConsole.WriteLine($"[HTTP] {method} {url}");
        using var response = await _remote.SendAsync(request, cancellationToken);
        DebugConsole.WriteLine($"[HTTP] {(int)response.StatusCode} {response.StatusCode}; response body hidden for safety");
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"VALORANT API HTTP {(int)response.StatusCode}", null, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string LimitForConsole(string value) => value.Length <= 12000
        ? value : value[..12000] + " … [truncated]";

    private static void AddRemoteHeaders(HttpRequestMessage request, RiotContext context)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        request.Headers.Add("X-Riot-Entitlements-JWT", context.EntitlementToken);
        request.Headers.Add("X-Riot-ClientVersion", context.Version);
        request.Headers.Add("X-Riot-ClientPlatform", Platform);
    }

    private static (int Port, string Password)? ReadLockfile()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var file = Path.Combine(folder, "Riot Games", "Riot Client", "Config", "lockfile");
        if (!File.Exists(file)) return null;
        // Riot Client dosyayı açık tutarken File.ReadAllText paylaşım hatası verebilir.
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var parts = reader.ReadToEnd().Trim().Split(':');
        if (parts.Length < 5 || !int.TryParse(parts[2], out var port) || port is < 1 or > 65535 || parts[3].Length == 0)
            throw new InvalidOperationException("Riot lockfile biçimi geçersiz.");
        return (port, parts[3]);
    }

    private static string? ReadRecentShooterLog()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var file = Path.Combine(folder, "VALORANT", "Saved", "Logs", "ShooterGame.log");
        if (!File.Exists(file)) return null;
        if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromMinutes(15)) return null;
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(Math.Max(0, stream.Length - 2_000_000), SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose() { _local.Dispose(); _remote.Dispose(); }
}

public sealed record LivePlayerLoadout(string PlayerId, string Team, string AgentId, List<LiveWeaponItem> Weapons)
{
    public string GameName { get; init; } = "";
    public string TagLine { get; init; } = "";
    public string AgentName { get; init; } = "";
    public string DisplayName => string.IsNullOrWhiteSpace(GameName)
        ? AgentName : string.IsNullOrWhiteSpace(TagLine) ? GameName : $"{GameName}#{TagLine}";
}
public sealed record LiveWeaponItem(string WeaponId, string? SkinId, string? BuddyId);
