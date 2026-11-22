using System.IO;
using System.Globalization;
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

public sealed record ValorantGameState(string SessionLoopState, string? QueueId, string? MapId,
    string? AgentId, int? AllyScore, int? EnemyScore);

public sealed record RiotChatParticipant(string PlayerId, string GameName, string GameTag, string Name);

public sealed record RiotChatConversation(string Id, string Type, bool DirectMessages, bool HistoryAvailable,
    int UnreadCount, IReadOnlyList<RiotChatParticipant> Participants, string DisplayName, string Subtitle, string Initials)
{
    public bool IsSelected { get; set; }
}

public sealed record RiotChatOverview(string CurrentPlayerId, IReadOnlyList<RiotChatConversation> Conversations);

public sealed record RiotChatMessage(string ConversationId, string SenderDisplayName, string Body,
    DateTimeOffset? SentAt, bool IsOutgoing)
{
    public string TimeLabel => SentAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "";
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

    public async Task<RiotChatOverview> GetChatConversationsAsync(CancellationToken cancellationToken)
    {
        var lockfile = ReadLockfile();
        if (lockfile is null) throw new InvalidOperationException("Riot Client açık değil.");

        using var conversationResponse = await LocalGetAsync(lockfile.Value, "chat/v6/conversations", cancellationToken);
        if (!conversationResponse.RootElement.TryGetProperty("conversations", out var conversationList) ||
            conversationList.ValueKind != JsonValueKind.Array)
            throw new JsonException("Sohbet listesi beklenen biçimde değil.");

        var participantsTask = LocalGetAsync(lockfile.Value, "chat/v5/participants", cancellationToken);
        var sessionTask = LocalGetAsync(lockfile.Value, "chat/v1/session", cancellationToken);
        await Task.WhenAll(participantsTask, sessionTask);
        using var participantsResponse = await participantsTask;
        using var sessionResponse = await sessionTask;

        var currentPlayerId = ReadString(sessionResponse.RootElement, "puuid") ?? "";
        var participantsByConversation = new Dictionary<string, List<RiotChatParticipant>>(StringComparer.OrdinalIgnoreCase);
        if (participantsResponse.RootElement.TryGetProperty("participants", out var participantList) &&
            participantList.ValueKind == JsonValueKind.Array)
        {
            foreach (var participant in participantList.EnumerateArray())
            {
                var conversationId = ReadString(participant, "cid");
                if (string.IsNullOrWhiteSpace(conversationId)) continue;
                if (!participantsByConversation.TryGetValue(conversationId, out var conversationParticipants))
                {
                    conversationParticipants = [];
                    participantsByConversation[conversationId] = conversationParticipants;
                }

                conversationParticipants.Add(new RiotChatParticipant(
                    ReadString(participant, "puuid") ?? "",
                    ReadString(participant, "game_name") ?? "",
                    ReadString(participant, "game_tag") ?? "",
                    ReadString(participant, "name") ?? ReadString(participant, "pid") ?? ""));
            }
        }

        var conversations = new List<RiotChatConversation>();
        foreach (var item in conversationList.EnumerateArray())
        {
            var id = ReadString(item, "cid");
            if (string.IsNullOrWhiteSpace(id)) continue;

            var type = ReadString(item, "type") ?? "chat";
            var participants = participantsByConversation.GetValueOrDefault(id) ?? [];
            var otherParticipants = participants.Where(participant =>
                string.IsNullOrWhiteSpace(currentPlayerId) ||
                !string.Equals(participant.PlayerId, currentPlayerId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (otherParticipants.Length == 0) otherParticipants = participants.ToArray();
            var names = otherParticipants.Select(GetChatParticipantDisplayName)
                .Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
            var unreadCount = ReadInt(item, "unread_count") ?? 0;
            var isGroup = type.Equals("groupchat", StringComparison.OrdinalIgnoreCase);
            var displayName = isGroup
                ? names.Length switch
                {
                    0 => "Grup sohbeti",
                    1 => names[0],
                    2 => $"{names[0]}, {names[1]}",
                    _ => $"{names[0]}, {names[1]} +{names.Length - 2}"
                }
                : names.FirstOrDefault() ?? "İsimsiz sohbet";
            var subtitle = isGroup
                ? $"Grup sohbeti • {otherParticipants.Length} katılımcı"
                : "Birebir mesajlaşma";
            var initials = displayName.Trim().Length == 0 ? "•" : displayName.Trim()[..1].ToUpperInvariant();

            conversations.Add(new RiotChatConversation(id, type,
                ReadBool(item, "direct_messages"), ReadBool(item, "message_history"), unreadCount,
                participants, displayName, subtitle, initials));
        }

        return new RiotChatOverview(currentPlayerId, conversations
            .OrderByDescending(conversation => conversation.UnreadCount)
            .ThenBy(conversation => conversation.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray());
    }

    public async Task<IReadOnlyList<RiotChatMessage>> GetChatHistoryAsync(
        string conversationId, string currentPlayerId, CancellationToken cancellationToken)
    {
        var lockfile = ReadLockfile();
        if (lockfile is null) throw new InvalidOperationException("Riot Client açık değil.");

        var endpoint = $"chat/v6/messages?cid={Uri.EscapeDataString(conversationId)}";
        using var response = await LocalGetAsync(lockfile.Value, endpoint, cancellationToken);
        if (!response.RootElement.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            throw new JsonException("Mesaj geçmişi beklenen biçimde değil.");

        var result = new List<(int Index, RiotChatMessage Message)>();
        var index = 0;
        foreach (var item in messages.EnumerateArray())
        {
            var playerId = ReadString(item, "puuid") ?? "";
            var gameName = ReadString(item, "game_name") ?? "";
            var gameTag = ReadString(item, "game_tag") ?? "";
            var sender = !string.IsNullOrWhiteSpace(currentPlayerId) &&
                         string.Equals(playerId, currentPlayerId, StringComparison.OrdinalIgnoreCase)
                ? "Sen"
                : !string.IsNullOrWhiteSpace(gameName)
                    ? string.IsNullOrWhiteSpace(gameTag) ? gameName : $"{gameName}#{gameTag}"
                    : ReadString(item, "name") ?? "Bilinmeyen oyuncu";

            result.Add((index++, new RiotChatMessage(
                ReadString(item, "cid") ?? conversationId,
                sender,
                ReadString(item, "body") ?? "",
                ReadChatTimestamp(item),
                !string.IsNullOrWhiteSpace(currentPlayerId) &&
                string.Equals(playerId, currentPlayerId, StringComparison.OrdinalIgnoreCase))));
        }

        return result.OrderBy(entry => entry.Message.SentAt ?? DateTimeOffset.MinValue)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Message)
            .ToArray();
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

    public async Task<ValorantGameState?> GetGameStateForDiscordAsync(CancellationToken cancellationToken)
    {
        var lockfile = ReadLockfile();
        if (lockfile is null)
        {
            DebugConsole.WriteLine("[DISCORD STATE] No Riot lockfile found.");
            return null;
        }
        var context = await GetContextAsync(cancellationToken);
        if (context is null)
        {
            DebugConsole.WriteLine("[DISCORD STATE] Riot session context is not ready.");
            return null;
        }

        using var presences = await LocalGetAsync(lockfile.Value, "chat/v4/presences", cancellationToken);
        if (!presences.RootElement.TryGetProperty("presences", out var list))
        {
            DebugConsole.WriteLine("[DISCORD STATE] Presence response has no 'presences' field.");
            return null;
        }
        if (list.ValueKind != JsonValueKind.Array)
        {
            DebugConsole.WriteLine($"[DISCORD STATE] Presence list has unexpected JSON type: {list.ValueKind}.");
            return null;
        }

        JsonElement playerPresence = default;
        var presenceCount = list.GetArrayLength();
        foreach (var entry in list.EnumerateArray())
        {
            if (entry.TryGetProperty("puuid", out var puuid) &&
                string.Equals(puuid.GetString(), context.PlayerId, StringComparison.OrdinalIgnoreCase))
            {
                playerPresence = entry;
                break;
            }
        }
        if (playerPresence.ValueKind != JsonValueKind.Object)
        {
            DebugConsole.WriteLine($"[DISCORD STATE] No self-presence matched in {presenceCount} entries.");
            return null;
        }
        if (!playerPresence.TryGetProperty("private", out var privateValue) ||
            privateValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(privateValue.GetString()))
        {
            DebugConsole.WriteLine("[DISCORD STATE] Self-presence has no private game-state payload.");
            return null;
        }

        using var privateData = JsonDocument.Parse(Convert.FromBase64String(privateValue.GetString()!));
        var details = privateData.RootElement;
        // Riot has moved match fields into nested matchPresenceData / partyPresenceData
        // objects in newer presence payloads. Keep the flat lookup for older clients,
        // then search nested objects so the game phase still resolves across versions.
        var state = ReadString(details, "sessionLoopState") ?? ReadNestedString(details, "sessionLoopState");
        if (string.IsNullOrWhiteSpace(state))
        {
            var propertyNames = details.ValueKind == JsonValueKind.Object
                ? string.Join(",", details.EnumerateObject().Select(property => property.Name))
                : details.ValueKind.ToString();
            DebugConsole.WriteLine($"[DISCORD STATE] Private payload has no sessionLoopState; fields={propertyNames}.");
            return null;
        }
        var queueId = ReadString(details, "queueId") ?? ReadNestedString(details, "queueId");
        DebugConsole.WriteLine($"[DISCORD STATE] Detected phase {state}; queue={(string.IsNullOrWhiteSpace(queueId) ? "empty" : "present")}.");
        var allyScore = ReadInt(details, "partyOwnerMatchScoreAllyTeam");
        var enemyScore = ReadInt(details, "partyOwnerMatchScoreEnemyTeam");
        string? mapId = null;
        string? agentId = null;

        if (state.Equals("PREGAME", StringComparison.OrdinalIgnoreCase))
        {
            var matchId = await GetPregameMatchIdAsync(context, cancellationToken);
            if (!string.IsNullOrWhiteSpace(matchId))
            {
                using var match = await RemoteAsync(context, HttpMethod.Get,
                    $"pregame/v1/matches/{Uri.EscapeDataString(matchId)}", cancellationToken, allowNotFound: true);
                if (match is not null)
                {
                    mapId = ReadString(match.RootElement, "MapID");
                    if (string.IsNullOrWhiteSpace(queueId)) queueId = ReadString(match.RootElement, "ProvisioningFlow");
                    if (match.RootElement.TryGetProperty("AllyTeam", out var allyTeam) &&
                        allyTeam.TryGetProperty("Players", out var players) && players.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var player in players.EnumerateArray())
                            if (string.Equals(ReadString(player, "Subject"), context.PlayerId, StringComparison.OrdinalIgnoreCase))
                            {
                                agentId = ReadString(player, "CharacterID");
                                break;
                            }
                    }
                }
            }
        }
        else if (state.Equals("INGAME", StringComparison.OrdinalIgnoreCase))
        {
            var matchId = await GetCurrentGameMatchIdAsync(context, cancellationToken);
            if (!string.IsNullOrWhiteSpace(matchId))
            {
                using var match = await RemoteAsync(context, HttpMethod.Get,
                    $"core-game/v1/matches/{Uri.EscapeDataString(matchId)}", cancellationToken, allowNotFound: true);
                if (match is not null)
                {
                    mapId = ReadString(match.RootElement, "MapID");
                    if (string.IsNullOrWhiteSpace(queueId)) queueId = ReadString(match.RootElement, "ProvisioningFlow");
                    if (match.RootElement.TryGetProperty("Players", out var players) && players.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var player in players.EnumerateArray())
                            if (string.Equals(ReadString(player, "Subject"), context.PlayerId, StringComparison.OrdinalIgnoreCase))
                            {
                                agentId = ReadString(player, "CharacterID");
                                break;
                            }
                    }
                }
            }
        }

        return new ValorantGameState(state, queueId, mapId, agentId, allyScore, enemyScore);
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

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool ReadBool(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static string GetChatParticipantDisplayName(RiotChatParticipant participant) =>
        !string.IsNullOrWhiteSpace(participant.GameName)
            ? string.IsNullOrWhiteSpace(participant.GameTag)
                ? participant.GameName
                : $"{participant.GameName}#{participant.GameTag}"
            : participant.Name;

    private static DateTimeOffset? ReadChatTimestamp(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("time", out var value)) return null;
        string? raw = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(raw)) return null;

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixMilliseconds))
        {
            try { return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds); }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string? ReadNestedString(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var child in element.EnumerateObject())
            {
                if (string.Equals(child.Name, property, StringComparison.OrdinalIgnoreCase) &&
                    child.Value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(child.Value.GetString()))
                    return child.Value.GetString();

                var nested = ReadNestedString(child.Value, property);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var nested = ReadNestedString(child, property);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }

        return null;
    }

    private static int? ReadInt(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number : null;
    }

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
        var queryIndex = endpoint.IndexOf('?');
        var logEndpoint = queryIndex >= 0 ? $"{endpoint[..queryIndex]}?[query hidden]" : endpoint;
        DebugConsole.WriteLine($"[LOCAL] GET https://127.0.0.1:{lockfile.Port}/{logEndpoint} (credentials hidden)");
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
