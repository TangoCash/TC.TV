using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace TCTV.Core;

/// <summary>Ergebnis einer Updateprüfung gegen die GitHub-Releases des Projekts.</summary>
public sealed class UpdateInfo
{
    /// <summary>Tag des Releases, z. B. "v0.4".</summary>
    public required string Tag { get; init; }

    /// <summary>Versionsnummer des Releases (ohne "v"-Präfix, 4-teilig).</summary>
    public required Version Version { get; init; }

    /// <summary>Link zur Release-Seite auf GitHub.</summary>
    public required string ReleaseUrl { get; init; }

    /// <summary>Release-Notizen (Markdown, kann leer sein).</summary>
    public required string Changelog { get; init; }

    /// <summary>Download-Anhänge des Releases (die vier EXE-Varianten).</summary>
    public required IReadOnlyList<ReleaseAsset> Assets { get; init; }

    /// <summary>true, wenn das Release neuer als die installierte Version ist.</summary>
    public bool IsNewer { get; init; }
}

/// <summary>Ein Download-Anhang eines GitHub-Release (z. B. TC.TV-win-x64.exe).</summary>
public sealed class ReleaseAsset
{
    /// <summary>Dateiname, z. B. "TC.TV-win-x64-standalone.exe".</summary>
    public required string Name { get; init; }

    /// <summary>Direkte Download-Adresse.</summary>
    public required string DownloadUrl { get; init; }

    /// <summary>Größe in Byte (0, wenn unbekannt).</summary>
    public long Size { get; init; }

    /// <summary>SHA-256-Hexwert (ohne "sha256:"-Präfix; leer, wenn nicht vorhanden).</summary>
    public string Sha256 { get; init; } = string.Empty;
}

/// <summary>
/// Prüft über die GitHub-API, ob eine neuere Version von TC.TV veröffentlicht wurde.
/// Die installierte Version stammt aus dem Assembly-Namen (<c>Version</c> im csproj)
/// und wird gegen den Tag des aktuellsten (stabilen) Releases verglichen.
/// </summary>
public static class UpdateChecker
{
    /// <summary>Release-Seite für den Browser.</summary>
    public const string ReleasesUrl = "https://github.com/TangoCash/TC.TV/releases";

    /// <summary>API-Endpunkt für das neueste stabile Release.</summary>
    public const string LatestApiUrl = "https://api.github.com/repos/TangoCash/TC.TV/releases/latest";

    private static readonly Version Current =
        typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>Installierte Version (AssemblyVersion, z. B. 0.3.0.0).</summary>
    public static Version CurrentVersion => Current;

    /// <summary>Fragt das neueste Release ab; null, wenn die Abfrage fehlschlug.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
            // Die GitHub-API verlangt einen User-Agent, sonst antwortet sie mit 403.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TC.TV");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            string json = await client.GetStringAsync(LatestApiUrl, cancellationToken).ConfigureAwait(false);
            return ParseRelease(json, Current);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Wertet eine GitHub-Release-Antwort aus (für den Selbsttest separat).</summary>
    internal static UpdateInfo? ParseRelease(string json, Version current)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string tag = GetString(root, "tag_name");
            if (tag.Length == 0)
            {
                return null;
            }

            // Entwürfe und Vorabversionen werden nicht angeboten.
            if (GetBool(root, "draft") || GetBool(root, "prerelease"))
            {
                return null;
            }

            Version latest = ParseTag(tag);
            return new UpdateInfo
            {
                Tag = tag,
                Version = latest,
                ReleaseUrl = GetString(root, "html_url") is { Length: > 0 } url ? url : ReleasesUrl,
                Changelog = GetString(root, "body"),
                Assets = ParseAssets(root),
                IsNewer = latest > Normalize(current),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Macht aus einem Release-Tag ("v0.4", "1.2.3") eine 4-teilige Version.</summary>
    internal static Version ParseTag(string tag)
    {
        string digits = tag ?? string.Empty;
        if (digits.Length > 0 && (digits[0] == 'v' || digits[0] == 'V'))
        {
            digits = digits[1..];
        }

        return Normalize(digits);
    }

    /// <summary>Kürzt eine Version um überflüssige Nullen (0.4.0.0 → "0.4").</summary>
    public static string Display(Version version)
    {
        if (version.Revision > 0)
        {
            return version.ToString(4);
        }

        if (version.Build > 0)
        {
            return version.ToString(3);
        }

        return version.ToString(2);
    }

    /// <summary>Ergänzt fehlende Komponenten zu 0, damit der Vergleich stabil bleibt.</summary>
    private static Version Normalize(Version version) => new(
        Math.Max(0, version.Major),
        Math.Max(0, version.Minor),
        Math.Max(0, version.Build),
        Math.Max(0, version.Revision));

    private static Version Normalize(string value)
    {
        string[] parts = (value ?? string.Empty).Split('.');
        int[] numbers = new int[4];
        for (int i = 0; i < numbers.Length; i++)
        {
            numbers[i] = i < parts.Length && int.TryParse(parts[i], out int parsed) && parsed >= 0 ? parsed : 0;
        }

        return new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    private static string GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;

    private static bool GetBool(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement element)
           && element.ValueKind is JsonValueKind.True or JsonValueKind.False
           && element.GetBoolean();

    /// <summary>Wählt den zur laufenden Instanz passenden Download (Architektur + Variante).</summary>
    public static ReleaseAsset? SelectAsset(UpdateInfo update)
        => SelectAsset(
            update,
            RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "x86" : "x64",
            IsSelfContained());

    /// <summary>Varianten-Auswahl mit expliziten Werten (für den Selbsttest).</summary>
    internal static ReleaseAsset? SelectAsset(UpdateInfo update, string architecture, bool standalone)
    {
        string wanted = $"TC.TV-win-{architecture}{(standalone ? "-standalone" : "")}.exe";
        foreach (ReleaseAsset asset in update.Assets)
        {
            if (string.Equals(asset.Name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return asset;
            }
        }

        return null;
    }

    /// <summary>
    /// true, wenn die laufende EXE selbstenthalten ist (Standalone). Beide
    /// Auslieferungen sind Einzeldateien, unterscheiden sich aber um ein Vielfaches
    /// in der Größe: Standalone bündelt die Laufzeit (ca. 66–72 MB), SingleFile
    /// nicht (ca. 1 MB).
    /// </summary>
    internal static bool IsSelfContained()
    {
        string exe = Environment.ProcessPath ?? string.Empty;
        if (exe.Length == 0)
        {
            return false;
        }

        try
        {
            return new FileInfo(exe).Length > SelfContainedMinSize;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Ab dieser Größe gilt eine Einzeldatei-EXE als selbstenthalten.</summary>
    private const long SelfContainedMinSize = 30_000_000;

    /// <summary>Lädt einen Anhang herunter und prüft – wenn vorhanden – die SHA-256-Prüfsumme.</summary>
    public static async Task DownloadAsync(ReleaseAsset asset, string targetPath, CancellationToken cancellationToken = default)
    {
        using HttpClient client = new() { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TC.TV");

        using HttpResponseMessage response = await client.GetAsync(
            asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using (FileStream file = File.Create(targetPath))
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(asset.Sha256))
        {
            string actual = ComputeSha256(targetPath);
            if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Die Prüfsumme der heruntergeladenen Datei stimmt nicht überein.");
            }
        }
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static IReadOnlyList<ReleaseAsset> ParseAssets(JsonElement root)
    {
        List<ReleaseAsset> assets = new();
        if (root.TryGetProperty("assets", out JsonElement element) && element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (ParseAsset(item) is { } asset)
                {
                    assets.Add(asset);
                }
            }
        }

        return assets;
    }

    private static ReleaseAsset? ParseAsset(JsonElement element)
    {
        string name = GetString(element, "name");
        string url = GetString(element, "browser_download_url");
        if (name.Length == 0 || url.Length == 0)
        {
            return null;
        }

        string digest = GetString(element, "digest");
        if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            digest = digest[7..];
        }

        return new ReleaseAsset
        {
            Name = name,
            DownloadUrl = url,
            Size = GetLong(element, "size"),
            Sha256 = digest,
        };
    }

    private static long GetLong(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement element)
           && element.ValueKind == JsonValueKind.Number
           && element.TryGetInt64(out long value)
            ? value
            : 0;
}
