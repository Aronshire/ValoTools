using System.Text.Json;
using ValoTools.Services;

static AvailableUpdate? Parse(string tag, string name, string url, string digest, long size = 48_000_000)
{
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(new
    {
        tag_name = tag,
        assets = new[] { new { name, state = "uploaded", size, digest, browser_download_url = url } }
    }));
    return UpdateService.ParseLatestRelease(json.RootElement, new Version(1, 2, 0));
}

const string hash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
const string url = "https://github.com/Aronshire/ValoTools/releases/download/v1.3.0/ValoTools-Setup-1.3.0.exe";
if (Parse("v1.3.0", "ValoTools-Setup-1.3.0.exe", url, hash)?.Version != new Version(1, 3, 0))
    throw new Exception("Yeni sürüm bulunamadı.");
if (Parse("v1.2.0", "ValoTools-Setup-1.2.0.exe", url, hash) is not null)
    throw new Exception("Aynı sürüm güncelleme sayıldı.");
if (Parse("v1.3.0", "ValoTools-Setup-1.3.0.exe", url.Replace("github.com", "example.com"), hash) is not null)
    throw new Exception("Yabancı indirme adresi kabul edildi.");
if (Parse("v1.3.0", "ValoTools-Setup-1.3.0.exe", url, "sha256:invalid") is not null)
    throw new Exception("Geçersiz özet kabul edildi.");
if (Parse("v1.3.0", "ValoTools-Setup-1.3.0.exe", url, hash, 300_000_000) is not null)
    throw new Exception("Aşırı büyük dosya kabul edildi.");
Console.WriteLine("Güncelleme sürümü ve kurulum varlığı kontrolleri geçti.");
