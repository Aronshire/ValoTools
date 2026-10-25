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
        using var response = await _local.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task<JsonDocument?> RemoteAsync(RiotContext context, HttpMethod method, string endpoint,
        CancellationToken cancellationToken, bool allowNotFound)
    {
        var url = $"https://glz-{context.Route.Region}-1.{context.Route.Shard}.a.pvp.net/{endpoint}";
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        request.Headers.Add("X-Riot-Entitlements-JWT", context.EntitlementToken);
        request.Headers.Add("X-Riot-ClientVersion", context.Version);
        request.Headers.Add("X-Riot-ClientPlatform", Platform);
        if (method == HttpMethod.Post) request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await _remote.SendAsync(request, cancellationToken);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"VALORANT API HTTP {(int)response.StatusCode}", null, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
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
