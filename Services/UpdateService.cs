using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace ValoTools.Services;

public sealed record AvailableUpdate(Version Version, Uri DownloadUrl, long Size, string Sha256);

public sealed class UpdateService
{
    private const string Owner = "Aronshire";
    private const string Repository = "ValoTools";
    private const long MaximumInstallerSize = 250L * 1024 * 1024;
    private static readonly Uri LatestReleaseUrl = new($"https://api.github.com/repos/{Owner}/{Repository}/releases/latest");
    private static readonly HttpClient Client = CreateClient();

    public static Version CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);

    public static bool IsInstalled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{A79A221F-41E8-4EE9-9B2F-B9E4524B381E}_is1");
            var installLocation = key?.GetValue("InstallLocation") as string;
            if (string.IsNullOrWhiteSpace(installLocation)) return false;
            var installedExe = Path.GetFullPath(Path.Combine(installLocation, "ValoTools.exe"));
            return string.Equals(installedExe, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(LatestReleaseUrl, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseLatestRelease(document.RootElement, CurrentVersion);
    }

    public static AvailableUpdate? ParseLatestRelease(JsonElement release, Version currentVersion)
    {
        if (!release.TryGetProperty("tag_name", out var tagElement)) return null;
        var tag = tagElement.GetString();
        if (tag is null || !Version.TryParse(tag.TrimStart('v', 'V'), out var version)
            || version <= currentVersion) return null;
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

        var expectedName = $"ValoTools-Setup-{version.ToString(3)}.exe";
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != expectedName) continue;
            if (asset.GetProperty("state").GetString() != "uploaded") return null;
            var size = asset.GetProperty("size").GetInt64();
            var digest = asset.GetProperty("digest").GetString();
            var download = asset.GetProperty("browser_download_url").GetString();
            if (size <= 0 || size > MaximumInstallerSize || digest is null || download is null
                || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return null;
            var sha256 = digest[7..];
            if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit)) return null;
            if (!Uri.TryCreate(download, UriKind.Absolute, out var url)
                || url.Scheme != Uri.UriSchemeHttps || url.Host != "github.com"
                || url.AbsolutePath != $"/{Owner}/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{expectedName}") return null;
            return new AvailableUpdate(version, url, size, sha256.ToLowerInvariant());
        }
        return null;
    }

    public async Task<string> DownloadAsync(AvailableUpdate update, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ValoTools", "Updates");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"ValoTools-Setup-{update.Version.ToString(3)}.exe");
        if (File.Exists(destination) && await HasExpectedHashAsync(destination, update.Sha256, cancellationToken))
        {
            progress?.Report(100);
            return destination;
        }

        var temporary = destination + ".download";
        try
        {
            using var response = await Client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long contentLength && contentLength != update.Size)
                throw new InvalidDataException("İndirilen dosyanın boyutu sürüm bilgisiyle eşleşmiyor.");
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int lastPercent = -1;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    total += read;
                    if (total > update.Size) throw new InvalidDataException("İndirilen dosya beklenenden büyük.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    var percent = (int)(total * 100 / update.Size);
                    if (percent != lastPercent) { progress?.Report(percent); lastPercent = percent; }
                }
                if (total != update.Size) throw new InvalidDataException("İndirilen dosya eksik.");
            }
            if (!await HasExpectedHashAsync(temporary, update.Sha256, cancellationToken))
                throw new InvalidDataException("Kurulum dosyasının SHA-256 özeti doğrulanamadı.");
            File.Move(temporary, destination, true);
            progress?.Report(100);
            return destination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void StartInstaller(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static async Task<bool> HasExpectedHashAsync(string path, string expected, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ValoTools-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
