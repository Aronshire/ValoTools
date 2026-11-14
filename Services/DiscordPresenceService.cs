using DiscordRPC;
using DiscordRPC.Logging;
using System.Net.Http;
using System.Text.Json;

namespace ValoTools.Services;

internal sealed record DiscordPresenceUpdate(string Status, string Preview, bool IsConnected);
internal sealed record ValorantMapInfo(string Name, string? ImageUrl);

internal sealed class DiscordPresenceService(RiotClient riot, Func<IReadOnlyCollection<Agent>> getAgents)
{
    // Existing ValoCord Discord application ID, retained so its registered application remains usable.
    private const string ClientId = "1025869206253748295";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static Dictionary<string, ValorantMapInfo>? _maps;

    public event Action<DiscordPresenceUpdate>? StatusChanged;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        DiscordRpcClient? client = null;
        string? lastPresenceKey = null;
        string? lastPhase = null;
        DateTimeOffset phaseStartedAt = DateTimeOffset.UtcNow;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (client is null)
                    {
                        DebugConsole.WriteLine("[DISCORD RPC] Trying to initialize Discord IPC connection.");
                        client = CreateClient();
                        var initialized = client.Initialize();
                        DebugConsole.WriteLine($"[DISCORD RPC] Initialize returned {initialized}; IsInitialized={client.IsInitialized}.");
                        if (!initialized)
                        {
                            client.Dispose();
                            client = null;
                            Publish("DISCORD BULUNAMADI", "Discord masaüstü uygulamasını açın", false);
                        }
                        else
                        {
                            Publish("DISCORD BAĞLANDI", "VALORANT durumu bekleniyor", true);
                        }
                    }

                    if (client is not null)
                    {
                        var game = await riot.GetGameStateForDiscordAsync(cancellationToken);
                        if (game is null)
                        {
                            if (lastPresenceKey is not null)
                            {
                                if (client.IsInitialized) client.ClearPresence();
                                lastPresenceKey = null;
                                lastPhase = null;
                            }
                            Publish("DISCORD BAĞLI", "VALORANT oturumu bekleniyor", true);
                        }
                        else
                        {
                            if (!string.Equals(game.SessionLoopState, lastPhase, StringComparison.OrdinalIgnoreCase))
                            {
                                lastPhase = game.SessionLoopState;
                                phaseStartedAt = DateTimeOffset.UtcNow;
                            }

                            var (presence, key) = await CreatePresenceAsync(game, phaseStartedAt, cancellationToken);
                            if (!string.Equals(key, lastPresenceKey, StringComparison.Ordinal))
                            {
                                client.SetPresence(presence);
                                lastPresenceKey = key;
                                DebugConsole.WriteLine($"[DISCORD RPC] Presence queued: {presence.Details}; state={presence.State ?? "(empty)"}.");
                            }
                            Publish("RICH PRESENCE AKTİF", presence.Details ?? "VALORANT", true);
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteLine($"[DISCORD RPC] Exception: {ex.GetType().Name}: {ex.Message}");
                    client?.Dispose();
                    client = null;
                    lastPresenceKey = null;
                    lastPhase = null;
                    Publish("DISCORD RPC HATASI", ex.Message, false);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(client is null ? 6 : 8), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            if (client is not null)
            {
                try
                {
                    if (client.IsInitialized) client.ClearPresence();
                }
                catch { }
                client.Dispose();
            }
            Publish("DISCORD RPC KAPALI", "Rich Presence devre dışı", false);
        }
    }

    private DiscordRpcClient CreateClient()
    {
        ILogger logger = DebugConsole.IsEnabled
            ? new ConsoleLogger(LogLevel.Info, true)
            : new NullLogger();
        var client = new DiscordRpcClient(ClientId, logger: logger);
        client.OnReady += (_, _) =>
        {
            DebugConsole.WriteLine("[DISCORD RPC] Discord sent READY.");
            Publish("DISCORD BAĞLANDI", "Discord Rich Presence hazır", true);
        };
        client.OnClose += (_, _) =>
        {
            DebugConsole.WriteLine("[DISCORD RPC] Discord closed the RPC connection.");
            Publish("DISCORD YENİDEN BAĞLANIYOR", "Discord bağlantısı kesildi", false);
        };
        client.OnConnectionFailed += (_, _) => DebugConsole.WriteLine("[DISCORD RPC] Connection attempt failed.");
        client.OnError += (_, error) =>
        {
            DebugConsole.WriteLine($"[DISCORD RPC] Discord error {error.Code}: {error.Message}");
            Publish("DISCORD RPC HATASI", $"{error.Code}: {error.Message}", false);
        };
        return client;
    }

    private async Task<(RichPresence Presence, string Key)> CreatePresenceAsync(
        ValorantGameState game, DateTimeOffset phaseStartedAt, CancellationToken cancellationToken)
    {
        var mode = FormatMode(game.QueueId);
        var details = game.SessionLoopState.ToUpperInvariant() switch
        {
            "STARTING_GAME" => "Oyun başlatılıyor",
            "MENUS" => string.IsNullOrWhiteSpace(mode) ? "Lobi" : $"Lobi • {mode}",
            "PREGAME" => "Ajan seçimi",
            "INGAME" when game.AllyScore is not null && game.EnemyScore is not null =>
                $"Oyunda • {game.AllyScore} - {game.EnemyScore}",
            "INGAME" => "Oyunda",
            _ => "VALORANT oynuyor"
        };

        string? mapImage = null;
        string? mapName = null;
        if (!string.IsNullOrWhiteSpace(game.MapId))
        {
            try
            {
                var maps = await GetMapsAsync(cancellationToken);
                if (maps.TryGetValue(game.MapId, out var map))
                {
                    mapImage = map.ImageUrl;
                    mapName = map.Name;
                }
            }
            catch { /* Map artwork is optional; presence text still works when the catalog is unavailable. */ }
        }

        string? agentImage = null;
        string? agentName = null;
        if (game.SessionLoopState.Equals("INGAME", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(game.AgentId))
        {
            var agent = getAgents().FirstOrDefault(item =>
                string.Equals(item.Id, game.AgentId, StringComparison.OrdinalIgnoreCase));
            if (agent is not null)
            {
                agentImage = agent.IconUrl;
                agentName = agent.Name;
            }
        }

        var presence = new RichPresence
        {
            Type = ActivityType.Playing,
            Details = details,
            State = string.IsNullOrWhiteSpace(mode) ? null : mode,
            Timestamps = new Timestamps { Start = phaseStartedAt.UtcDateTime },
            Assets = mapImage is null && agentImage is null
                ? null
                : new Assets
                {
                    LargeImageKey = mapImage,
                    LargeImageText = mapName,
                    SmallImageKey = agentImage,
                    SmallImageText = agentName
                }
        };

        // Keep the phase start out of the key: the RPC library suppresses identical updates itself.
        var key = JsonSerializer.Serialize(new { details, mode, mapImage, mapName, agentImage, agentName, phase = game.SessionLoopState });
        return (presence, key);
    }

    private static string FormatMode(string? queueId)
    {
        if (string.IsNullOrWhiteSpace(queueId)) return "";
        return queueId.Trim().ToLowerInvariant() switch
        {
            "competitive" => "Rekabetçi",
            "unrated" => "Derecesiz",
            "swiftplay" => "Hızlı Oyun",
            "deathmatch" => "Ölüm Maçı",
            "ggteam" or "escalation" => "Tırmanış",
            "onefa" or "replication" => "Kopya",
            "spikerush" => "Spike Rush",
            "customgame" or "custom" => "Özel Oyun",
            _ => queueId
        };
    }

    private static async Task<Dictionary<string, ValorantMapInfo>> GetMapsAsync(CancellationToken cancellationToken)
    {
        if (_maps is not null) return _maps;
        using var response = await Http.GetAsync("https://valorant-api.com/v1/maps", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var maps = new Dictionary<string, ValorantMapInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in json.RootElement.GetProperty("data").EnumerateArray())
        {
            var name = ReadString(item, "displayName");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var map = new ValorantMapInfo(name, ReadString(item, "splash") ?? ReadString(item, "displayIcon"));
            var url = ReadString(item, "mapUrl");
            var uuid = ReadString(item, "uuid");
            if (!string.IsNullOrWhiteSpace(url)) maps[url] = map;
            if (!string.IsNullOrWhiteSpace(uuid)) maps[uuid] = map;
        }
        _maps = maps;
        return maps;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private void Publish(string status, string preview, bool connected) =>
        StatusChanged?.Invoke(new DiscordPresenceUpdate(status, preview, connected));
}
