using ValoTools.Services;

static void Equal(RegionRoute? actual, string region, string shard)
{
    if (actual?.Region != region || actual.Shard != shard)
        throw new Exception($"Beklenen {region}/{shard}, gelen {actual?.Region}/{actual?.Shard}");
}

Equal(RegionResolver.FromSessionArguments(["-ares-endpoint=https://glz-eu-1.eu.a.pvp.net"], false), "eu", "eu");
Equal(RegionResolver.FromSessionArguments(["-ares-deployment=br"], false), "br", "na");
Equal(RegionResolver.FromSessionArguments(["-ares-deployment=na"], true), "na", "pbe");
Equal(RegionResolver.FromLog("old glz-eu-1.eu.a.pvp.net new glz-latam-1.na.a.pvp.net"), "latam", "na");
Equal(RegionResolver.FromRegion("AP"), "ap", "ap");
if (RegionResolver.FromRegion("TR") is not null) throw new Exception("Bilinmeyen bölge kabul edildi.");
Console.WriteLine("Bölge ayrıştırma kontrolleri geçti.");
