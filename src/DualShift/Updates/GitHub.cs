using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace DualShift.Updates;

public sealed record ReleaseAsset(string Name, string Url, long Size, string? Sha256);

public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>Zjištění nejnovějšího vydání na GitHubu a stažení jeho souborů.</summary>
public static class GitHub
{
    private const long MaxDownloadBytes = 200L * 1024 * 1024;

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // GitHub API vyžaduje User-Agent; záměrně obecný, bez údajů o uživateli.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DualShift-Updater");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    public static async Task<ReleaseInfo> GetLatestAsync(string repo, CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest", ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return Parse(doc.RootElement);
    }

    internal static ReleaseInfo Parse(JsonElement root)
    {
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        string page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var arr))
        {
            foreach (var a in arr.EnumerateArray())
            {
                string? digest = a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                assets.Add(new ReleaseAsset(
                    a.GetProperty("name").GetString() ?? "",
                    a.GetProperty("browser_download_url").GetString() ?? "",
                    a.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
                    digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].ToLowerInvariant() : null));
            }
        }
        return new ReleaseInfo(ParseVersion(tag) ?? new Version(0, 0, 0, 0), tag, page, assets);
    }

    /// <summary>„v1.5.230.0“, „1.22.0“ … → verze se čtyřmi složkami (chybějící = 0), aby šly porovnávat.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().TrimStart('v', 'V');
        int end = 0;
        while (end < t.Length && (char.IsDigit(t[end]) || t[end] == '.')) end++;
        return Version.TryParse(t[..end].Trim('.'), out var v) ? Normalize(v) : null;
    }

    public static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    /// <summary>Instalátor pro architekturu Windows (x64 / arm64); ViGEmBus má jeden společný pro všechny.</summary>
    public static ReleaseAsset? PickInstaller(IEnumerable<ReleaseAsset> assets, Architecture os)
    {
        var exes = assets.Where(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
        string token = os == Architecture.Arm64 ? "arm64" : "x64";
        return exes.FirstOrDefault(a => a.Name.Contains(token, StringComparison.OrdinalIgnoreCase))
               ?? (exes.Count == 1 ? exes[0] : null);
    }

    /// <summary>Stažení souboru vydání. Povolené jsou jen adresy GitHubu přes HTTPS.</summary>
    public static async Task DownloadAsync(ReleaseAsset asset, string path, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(asset.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unexpected download URL: {asset.Url}");

        using var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength > MaxDownloadBytes)
            throw new InvalidOperationException("File too large.");

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long total = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            total += n;
            if (total > MaxDownloadBytes) throw new InvalidOperationException("File too large.");
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
        }
    }

    public static async Task<string> Sha256Async(Stream stream) =>
        Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();

    /// <summary>Otevřít stránku vydání v prohlížeči – jen adresy na github.com.</summary>
    public static void OpenPage(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
