using ValoTools.Services;

using var client = new RiotClient();
var context = await client.GetContextAsync(CancellationToken.None);
if (context is null)
{
    Console.WriteLine("VALORANT oturumu bulunamadı.");
    Environment.ExitCode = 1;
}
else
{
    Console.WriteLine($"Yerel bağlantı hazır. Bölge: {context.Route.Label}. Sürüm bilgisi mevcut.");
    var matchId = await client.GetPregameMatchIdAsync(context, CancellationToken.None);
    Console.WriteLine(matchId is null ? "Maç bekleme isteği başarılı; aktif pregame yok." : "Aktif pregame algılandı.");
}
