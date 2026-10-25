using System.Net;
using System.Net.Http;

namespace ValoTools.Services;

public sealed record MonitorUpdate(string Status, string Message, string? Region = null, bool IsError = false);

public sealed class MatchMonitor(RiotClient client, Agent agent, Action<MonitorUpdate> report)
{
    private readonly Dictionary<string, int> _attempts = [];
    private string? _currentMatch;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try { await TickAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex) { report(new MonitorUpdate("BAĞLANTI HATASI", ex.Message, IsError: true)); }
            await Task.Delay(850, cancellationToken);
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var context = await client.GetContextAsync(cancellationToken);
        if (context is null)
        {
            _currentMatch = null;
            report(new MonitorUpdate("VALORANT BEKLENİYOR", "Oyunu açtığında bölge otomatik algılanacak."));
            return;
        }

        var matchId = await client.GetPregameMatchIdAsync(context, cancellationToken);
        if (string.IsNullOrWhiteSpace(matchId))
        {
            _currentMatch = null;
            report(new MonitorUpdate("MAÇ BEKLENİYOR", "Ajan seçimi ekranı bekleniyor.", context.Route.Label));
            return;
        }

        if (_currentMatch != matchId)
        {
            _currentMatch = matchId;
            report(new MonitorUpdate("MAÇ BULUNDU", "Ajan seçimi kontrol ediliyor...", context.Route.Label));
        }
        if (_attempts.GetValueOrDefault(matchId) >= 3) return;

        var state = await client.GetPregameStateAsync(context, matchId, cancellationToken);
        if (state is null || state.Phase != "character_select_active") return;
        if (state.PlayerSelectionState == "locked")
        {
            _attempts[matchId] = 3;
            report(new MonitorUpdate("AJAN KİLİTLİ", "Bu maçta ajan zaten kilitlenmiş.", context.Route.Label));
            return;
        }

        _attempts[matchId] = _attempts.GetValueOrDefault(matchId) + 1;
        try
        {
            var locked = await client.LockAgentAsync(context, matchId, agent.Id, cancellationToken);
            if (locked)
            {
                _attempts[matchId] = 3;
                report(new MonitorUpdate("AJAN KİLİTLENDİ", $"{agent.Name} başarıyla kilitlendi.", context.Route.Label));
            }
            else
            {
                report(new MonitorUpdate("SONUÇ BELİRSİZ", "Kilitleme isteği gönderildi; oyunda kontrol edin.", context.Route.Label, true));
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.Conflict)
        {
            _attempts[matchId] = 3;
            report(new MonitorUpdate("KİLİTLEME BAŞARISIZ", ex.Message, context.Route.Label, true));
        }
    }
}
