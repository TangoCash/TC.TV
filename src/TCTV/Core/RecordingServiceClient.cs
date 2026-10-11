using System.Globalization;

namespace TCTV.Core;

/// <summary>
/// Client für die Box – Fassade über beide Schnittstellen. Je nach Profil
/// ("Alte API (/control/)" oder "Neue API (/api/v1/)") spricht er
/// <see cref="LegacyControlApi"/> oder <see cref="NeutrinoV1Api"/> an; die
/// Fenster sehen davon nichts.
/// </summary>
public sealed class RecordingServiceClient : IDisposable
{
    private readonly BoxApiClient _api;

    public RecordingServiceClient(Profile profile, TimeSpan? timeout = null)
    {
        _api = profile.ApiVersion == BoxApi.V1
            ? new NeutrinoV1Api(profile, timeout)
            : new LegacyControlApi(profile, timeout);
    }

    public Profile Profile => _api.Profile;

    /// <summary>Welche Schnittstelle dieses Profil benutzt.</summary>
    public BoxApi ApiVersion => _api.Profile.ApiVersion;

    public string Host => _api.Host;

    public string WebPort => _api.WebPort;

    public string StreamingPort => _api.StreamingPort;

    public string BaseUrl => _api.BaseUrl;

    /// <summary>Basis-URL für VLC-Streams (Streaming-Port der Box, streamts).</summary>
    public string StreamBaseUrl => _api.StreamBaseUrl;

    /// <summary>Basis-URL des Web-/Dateiservers der Box (Port "Webserver Port").</summary>
    public string WebBaseUrl => _api.WebBaseUrl;

    /// <summary>Holt eine Antwort als Text (nur für Sonderfälle/Tests).</summary>
    public Task<string?> GetTextAsync(string path, CancellationToken cancellationToken = default)
        => _api.GetTextAsync(path, cancellationToken);

    /// <summary>Prüft die Verbindung zur Box.</summary>
    public Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default)
        => _api.IsConnectedAsync(cancellationToken);

    public Task<BoxMode> GetModeAsync(CancellationToken cancellationToken = default)
        => _api.GetModeAsync(cancellationToken);

    public Task<string?> GetTimeRawAsync(CancellationToken cancellationToken = default)
        => _api.GetTimeRawAsync(cancellationToken);

    public Task<string?> GetStandbyAsync(CancellationToken cancellationToken = default)
        => _api.GetStandbyAsync(cancellationToken);

    public Task TurnStandbyAsync(bool on, bool cecOff, CancellationToken cancellationToken = default)
        => _api.TurnStandbyAsync(on, cecOff, cancellationToken);

    public Task<List<Bouquet>> GetBouquetsAsync(CancellationToken cancellationToken = default)
        => _api.GetBouquetsAsync(cancellationToken);

    /// <summary>Sender eines Bouquets inklusive erster/zweiter EPG-Sendung.</summary>
    public Task<List<Channel>> GetBouquetAsync(string bouquetNumber, CancellationToken cancellationToken = default)
        => _api.GetBouquetAsync(bouquetNumber, cancellationToken);

    /// <summary>EPG-Sendungen eines einzelnen Senders (für "weiteres EPG").</summary>
    public Task<List<EpgEvent>> GetChannelEpgAsync(string channelId, string bouquetNumber, CancellationToken cancellationToken = default)
        => _api.GetChannelEpgAsync(channelId, bouquetNumber, cancellationToken);

    public Task<List<string>> GetVideoRootsAsync(string dirToken, CancellationToken cancellationToken = default)
        => _api.GetVideoRootsAsync(dirToken, cancellationToken);

    public Task<List<FileEntry>> GetMoviesAsync(string absolutePath, bool recursive, CancellationToken cancellationToken = default)
        => _api.GetMoviesAsync(absolutePath, recursive, cancellationToken);

    public Task<List<FileEntry>> GetFileListAsync(string absolutePath, CancellationToken cancellationToken = default)
        => _api.GetFileListAsync(absolutePath, cancellationToken);

    public string BuildHttpFileUrl(string absolutePath) => _api.BuildHttpFileUrl(absolutePath);

    /// <summary>Adresse einer Datei für VLC (die neue API hängt einen Medien-Token an).</summary>
    public Task<string> GetFileUrlAsync(string absolutePath, CancellationToken cancellationToken = default)
        => _api.GetFileUrlAsync(absolutePath, cancellationToken);

    public Task<List<TimerItem>> GetTimersAsync(CancellationToken cancellationToken = default)
        => _api.GetTimersAsync(cancellationToken);

    public Task<List<TimerItem>> GetTimerLinesAsync(CancellationToken cancellationToken = default)
        => _api.GetTimerLinesAsync(cancellationToken);

    public Task ZapToAsync(string channelId, CancellationToken cancellationToken = default)
        => _api.ZapToAsync(channelId, cancellationToken);

    public Task<string?> ZapToProbeAsync(CancellationToken cancellationToken = default)
        => _api.ZapToProbeAsync(cancellationToken);

    public Task<string?> GetCurrentTpChannelsAsync(CancellationToken cancellationToken = default)
        => _api.GetCurrentTpChannelsAsync(cancellationToken);

    /// <summary>Live-Adresse eines Senders laut Box (nur die neue API kennt sie).</summary>
    public Task<string?> GetStreamUrlAsync(string channelId, CancellationToken cancellationToken = default)
        => _api.GetStreamUrlAsync(channelId, cancellationToken);

    public Task CreateTimerAsync(
        string alarm,
        string stop,
        string channelId,
        string announce,
        CancellationToken cancellationToken = default)
        => _api.CreateTimerAsync(alarm, stop, channelId, announce, cancellationToken);

    public Task<List<RunningRecording>> GetRunningRecordingsAsync(CancellationToken cancellationToken = default)
        => _api.GetRunningRecordingsAsync(cancellationToken);

    public Task<long> GetBoxTimeAsync(CancellationToken cancellationToken = default)
        => _api.GetBoxTimeAsync(cancellationToken);

    public string BuildVlcStreamUrl(string channelId) => _api.BuildVlcStreamUrl(channelId);

    public void Dispose() => _api.Dispose();

    /// <summary>Dekodiert eine Antwort der Box (siehe <see cref="TextEncoding.Decode"/>).</summary>
    public static string DecodeText(byte[] bytes) => TextEncoding.Decode(bytes);

    // -----------------------------------------------------------------
    // Auswerter der alten API (/control/, XML) – Kurzformen für Aufrufer
    // und Selbsttest; die neue API wertet JSON aus (NeutrinoV1Api).
    // -----------------------------------------------------------------

    public static List<Channel> ParseBouquet(string xml) => LegacyControlApi.ParseBouquet(xml);

    public static List<string> ParseDirectoriesXml(string xml) => LegacyControlApi.ParseDirectoriesXml(xml);

    public static List<FileEntry> ParseMoviesXml(string xml) => LegacyControlApi.ParseMoviesXml(xml);

    public static List<FileEntry> ParseFileListXml(string xml) => LegacyControlApi.ParseFileListXml(xml);

    public static List<TimerItem> ParseTimersXml(string xml) => LegacyControlApi.ParseTimersXml(xml);

    public static List<TimerItem> ParseTimerLines(string text) => LegacyControlApi.ParseTimerLines(text);

    /// <summary>
    /// Vergleicht Sender-IDs tolerant: die Box liefert sie je Endpunkt mit bzw.
    /// ohne fuehrende Nullen (PRINTF_CHANNEL_ID_TYPE_NO_LEADING_ZEROS).
    /// </summary>
    public static bool ChannelIdsMatch(string? first, string? second)
    {
        string left = (first ?? string.Empty).Trim();
        string right = (second ?? string.Empty).Trim();
        if (left.Length == 0 || right.Length == 0)
        {
            return false;
        }

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ulong.TryParse(left, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong a)
            && ulong.TryParse(right, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong b)
            && a == b;
    }

    /// <summary>Prueft, ob eine Sender-ID in der Liste von /control/currenttpchannels vorkommt.</summary>
    public static bool TransponderContains(string? transponderList, string channelId)
    {
        if (string.IsNullOrEmpty(transponderList))
        {
            return false;
        }

        foreach (string rawLine in transponderList.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            int space = line.IndexOf(' ');
            string id = space > 0 ? line[..space] : line;
            if (ChannelIdsMatch(id, channelId))
            {
                return true;
            }
        }

        return false;
    }
}