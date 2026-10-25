using System.Text.RegularExpressions;

namespace ValoTools.Services;

public sealed record RegionRoute(string Region, string Shard)
{
    public string Label => Region == Shard ? Region.ToUpperInvariant() : $"{Region.ToUpperInvariant()} / {Shard.ToUpperInvariant()}";
}

public static partial class RegionResolver
{
    private static readonly Dictionary<string, string> Shards = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eu"] = "eu", ["na"] = "na", ["latam"] = "na", ["br"] = "na",
        ["ap"] = "ap", ["kr"] = "kr"
    };

    [GeneratedRegex(@"glz-(?<region>na|latam|br|eu|ap|kr)-1\.(?<shard>na|pbe|eu|ap|kr)\.a\.pvp\.net", RegexOptions.IgnoreCase)]
    private static partial Regex GlzAddress();

    [GeneratedRegex(@"(?:^|\s)--?ares-deployment=(?<region>na|latam|br|eu|ap|kr)(?:\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex Deployment();

    public static RegionRoute? FromSessionArguments(IEnumerable<string> arguments, bool isPbe)
    {
        var text = string.Join(' ', arguments);
        var address = GlzAddress().Match(text);
        if (address.Success) return new RegionRoute(
            address.Groups["region"].Value.ToLowerInvariant(),
            address.Groups["shard"].Value.ToLowerInvariant());

        var deployment = Deployment().Match(text);
        return deployment.Success ? FromRegion(deployment.Groups["region"].Value, isPbe) : null;
    }

    public static RegionRoute? FromLog(string? logText)
    {
        if (string.IsNullOrEmpty(logText)) return null;
        var matches = GlzAddress().Matches(logText);
        if (matches.Count == 0) return null;
        var address = matches[^1];
        return new RegionRoute(address.Groups["region"].Value.ToLowerInvariant(),
            address.Groups["shard"].Value.ToLowerInvariant());
    }

    public static RegionRoute? FromRegion(string? region, bool isPbe = false)
    {
        if (string.IsNullOrWhiteSpace(region) || !Shards.TryGetValue(region.Trim(), out var shard)) return null;
        return new RegionRoute(region.Trim().ToLowerInvariant(), isPbe && shard == "na" ? "pbe" : shard);
    }
}
