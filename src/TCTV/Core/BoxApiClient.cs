using System.Net;
using System.Net.Http;

namespace TCTV.Core;

/// <summary>
/// Gemeinsame Basis beider Box-Schnittstellen. Hält das Profil, die abgeleiteten
/// Adressen und den HTTP-Zugriff; welche Endpunkte angesprochen werden, entscheidet
/// die jeweilige Unterklasse (<see cref="LegacyControlApi"/>, <see cref="NeutrinoV1Api"/>).
/// </summary>
public abstract class BoxApiClient : IDisposable
{
    private bool _disposed;

    protected BoxApiClient(Profile profile, TimeSpan? timeout, bool useBasicAuthentication, bool useCookies = false)
    {
        Profile = profile;

        HttpClientHandler handler = new()
        {
            PreAuthenticate = true,
        };

        if (useBasicAuthentication && profile.WebAuthentication)
        {
            handler.Credentials = new NetworkCredential(profile.WebUser, profile.WebPassword);
        }

        if (useCookies)
        {
            // Sitzungs-Cookie der neuen API (POST /api/v1/login) wird automatisch
            // mitgeschickt und bei jeder Antwort ergänzt.
            handler.UseCookies = true;
            handler.CookieContainer = new CookieContainer();
        }

        HttpClient client = new(handler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(8),
        };
        client.DefaultRequestHeaders.ConnectionClose = true;

        _http = client;
    }

    protected readonly HttpClient _http;

    public Profile Profile { get; }

    public string Host => Profile.Host;

    public string WebPort => Profile.WebPort;

    public string StreamingPort => Profile.StreamingPort;

    /// <summary>Web-Port der Box (API-Schnittstelle).</summary>
    public string BaseUrl => $"http://{Profile.Host}:{Profile.WebPort}";

    /// <summary>Basis-URL für VLC-Streams (Streaming-Port der Box, streamts).</summary>
    public string StreamBaseUrl => $"http://{Profile.Host}:{Profile.StreamingPort}";

    /// <summary>Basis-URL des Web-/Dateiservers der Box (Port "Webserver Port").</summary>
    public string WebBaseUrl => $"http://{Profile.Host}:{Profile.WebPort}";

    /// <summary>Dekodiert eine Antwort der Box (siehe <see cref="TextEncoding.Decode"/>).</summary>
    protected static string DecodeText(byte[] bytes) => TextEncoding.Decode(bytes);

    public abstract Task<string?> GetTextAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Prüft die Verbindung zur Box.</summary>
    public abstract Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default);

    public abstract Task<BoxMode> GetModeAsync(CancellationToken cancellationToken = default);

    /// <summary>Rohzeit der Box (Unix-Sekunden als Text) oder null, wenn nicht abfragbar.</summary>
    public abstract Task<string?> GetTimeRawAsync(CancellationToken cancellationToken = default);

    /// <summary>Standby-Zustand ("on"/"off") als Text.</summary>
    public abstract Task<string?> GetStandbyAsync(CancellationToken cancellationToken = default);

    public abstract Task TurnStandbyAsync(bool on, bool cecOff, CancellationToken cancellationToken = default);

    public abstract Task<List<Bouquet>> GetBouquetsAsync(CancellationToken cancellationToken = default);

    /// <summary>Sender eines Bouquets inklusive erster/zweiter EPG-Sendung.</summary>
    public abstract Task<List<Channel>> GetBouquetAsync(string bouquetNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Wurzelverzeichnisse für Videos/Aufnahmen. <paramref name="dirToken"/> ist
    /// "recordingdir", "moviedir" oder "allmoviedirs".
    /// </summary>
    public abstract Task<List<string>> GetVideoRootsAsync(string dirToken, CancellationToken cancellationToken = default);

    public abstract Task<List<FileEntry>> GetMoviesAsync(string absolutePath, bool recursive, CancellationToken cancellationToken = default);

    public abstract Task<List<FileEntry>> GetFileListAsync(string absolutePath, CancellationToken cancellationToken = default);

    /// <summary>HTTP-Adresse einer Datei auf der Box (für VLC).</summary>
    public abstract string BuildHttpFileUrl(string absolutePath);

    /// <summary>
    /// Adresse einer Datei für einen Player (VLC). Die alte API liefert Dateien ohne
    /// Anmeldung, die neue verlangt einen kurzlebigen Token in der Adresse.
    /// </summary>
    public abstract Task<string> GetFileUrlAsync(string absolutePath, CancellationToken cancellationToken = default);

    public abstract Task<List<TimerItem>> GetTimersAsync(CancellationToken cancellationToken = default);

    /// <summary>Timer in der Kompaktform (nur die Felder für den Aufnahme-Test).</summary>
    public abstract Task<List<TimerItem>> GetTimerLinesAsync(CancellationToken cancellationToken = default);

    public abstract Task ZapToAsync(string channelId, CancellationToken cancellationToken = default);

    /// <summary>Umschalten ohne Sender (Verbindungstest des Originals).</summary>
    public abstract Task<string?> ZapToProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sender des aktuell von der Box belegten Transponders (je Zeile "ID NAME")
    /// oder null, wenn die Box das nicht meldet.
    /// </summary>
    public abstract Task<string?> GetCurrentTpChannelsAsync(CancellationToken cancellationToken = default);

    /// <summary>Live-Adresse eines Senders laut Box oder null, wenn nicht ermittelbar.</summary>
    public abstract Task<string?> GetStreamUrlAsync(string channelId, CancellationToken cancellationToken = default);

    public abstract Task CreateTimerAsync(
        string alarm,
        string stop,
        string channelId,
        string announce,
        CancellationToken cancellationToken = default);

    /// <summary>Aktuell laufende Aufnahmen.</summary>
    public abstract Task<List<RunningRecording>> GetRunningRecordingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Aktuelle Zeit der Box als Unix-Sekunden.</summary>
    public abstract Task<long> GetBoxTimeAsync(CancellationToken cancellationToken = default);

    /// <summary>VLC-Stream-Adresse eines Senders.</summary>
    public abstract string BuildVlcStreamUrl(string channelId);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        OnDispose();
        _http.Dispose();
    }

    protected virtual void OnDispose()
    {
    }
}