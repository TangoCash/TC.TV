using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;

namespace TCTV.Core;

/// <summary>
/// Client für den DVBViewer Recording Service. Kapselt genau die Endpunkte, die
/// das Original verwendet (/control/info, /control/zapto, /control/getbouquet ...).
/// </summary>
public sealed class RecordingServiceClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly Profile _profile;
    private bool _disposed;

    public RecordingServiceClient(Profile profile, TimeSpan? timeout = null)
    {
        _profile = profile;
        HttpClientHandler handler = new()
        {
            PreAuthenticate = true,
        };

        if (profile.WebAuthentication)
        {
            handler.Credentials = new NetworkCredential(profile.WebUser, profile.WebPassword);
        }

        _http = new HttpClient(handler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(8),
        };
        _http.DefaultRequestHeaders.ConnectionClose = true;
    }

    public Profile Profile => _profile;

    public string Host => _profile.Host;

    public string WebPort => _profile.WebPort;

    public string StreamingPort => _profile.StreamingPort;

    public string BaseUrl => $"http://{_profile.Host}:{_profile.WebPort}";

    /// <summary>Basis-URL für VLC-Streams (Streaming-Port der Box, streamts).</summary>
    public string StreamBaseUrl => $"http://{_profile.Host}:{_profile.StreamingPort}";

    /// <summary>Basis-URL des Web-/Dateiservers der Box (Port "Webserver Port").</summary>
    public string WebBaseUrl => $"http://{_profile.Host}:{_profile.WebPort}";

    public async Task<string?> GetTextAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await _http
                .GetAsync(BaseUrl + path, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return DecodeText(bytes);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Dekodiert eine Antwort der Box (siehe <see cref="TextEncoding.Decode"/>).
    /// </summary>
    public static string DecodeText(byte[] bytes) => TextEncoding.Decode(bytes);

    /// <summary>Prüft die Verbindung zur Box (/control/info).</summary>
    public async Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default)
        => await GetTextAsync("/control/info", cancellationToken).ConfigureAwait(false) is not null;

    public async Task<BoxMode> GetModeAsync(CancellationToken cancellationToken = default)
    {
        string? mode = await GetTextAsync("/control/getmode", cancellationToken).ConfigureAwait(false);
        if (mode is null)
        {
            return BoxMode.Unknown;
        }

        string value = mode.Trim();
        if (value.Contains("standby", StringComparison.OrdinalIgnoreCase))
        {
            return BoxMode.Standby;
        }

        if (value.Contains("radio", StringComparison.OrdinalIgnoreCase))
        {
            return BoxMode.Radio;
        }

        return value.Contains("tv", StringComparison.OrdinalIgnoreCase) ? BoxMode.Tv : BoxMode.Unknown;
    }

    public async Task<string?> GetTimeRawAsync(CancellationToken cancellationToken = default)
    {
        string? value = await GetTextAsync("/control/gettime?rawtime", cancellationToken).ConfigureAwait(false);
        return value?.Trim();
    }

    /// <summary>Zap-Modus ("on"/"off" usw.).</summary>
    public async Task<string?> GetStandbyAsync(CancellationToken cancellationToken = default)
    {
        string? value = await GetTextAsync("/control/standby", cancellationToken).ConfigureAwait(false);
        return value?.Replace("\r", string.Empty).Replace("\n", string.Empty);
    }

    /// <summary>Schaltet die Box in den Standby bzw. ein (optional mit CEC).</summary>
    public async Task TurnStandbyAsync(bool on, bool cecOff, CancellationToken cancellationToken = default)
    {
        string path = on ? "/control/standby?on" : "/control/standby?off" + (cecOff ? "&cec=off" : string.Empty);
        await GetTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Liste aller Bouquets: Zeilen der Form "NR NAME".</summary>
    public async Task<List<Bouquet>> GetBouquetsAsync(CancellationToken cancellationToken = default)
    {
        List<Bouquet> result = new();
        string? text = await GetTextAsync("/control/getbouquets", cancellationToken).ConfigureAwait(false);
        if (text is null)
        {
            return result;
        }

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim('\r', ' ', '\t');
            if (line.Length == 0)
            {
                continue;
            }

            int space = line.IndexOf(' ');
            string number = space > 0 ? line[..space] : line;
            string name = space > 0 ? line[(space + 1)..].Trim() : string.Empty;
            result.Add(new Bouquet { Number = number, Name = name });
        }

        return result;
    }

    public async Task<List<Channel>> GetBouquetAsync(string bouquetNumber, CancellationToken cancellationToken = default)
    {
        string? xml = await GetTextAsync(
            "/control/getbouquet?bouquet=" + Uri.EscapeDataString(bouquetNumber) + "&epg=true&format=xml",
            cancellationToken).ConfigureAwait(false);

        return xml is null ? new List<Channel>() : ParseBouquet(xml);
    }

    /// <summary>Parst die &lt;channel&gt;-Eintraege aus getbouquet?format=xml.</summary>
    public static List<Channel> ParseBouquet(string xml)
    {
        List<Channel> channels = new();
        try
        {
            XDocument doc = XDocument.Parse(xml, LoadOptions.None);
            foreach (XElement element in doc.Descendants("channel"))
            {
                Channel channel = new()
                {
                    Number = Value(element, "number"),
                    Id = Value(element, "id"),
                    ShortId = Value(element, "short_id"),
                    Name = Value(element, "name"),
                    Logo = Value(element, "logo"),
                    BouquetNumber = Value(element, "bouquetnr"),
                    IsActiveChannel = Value(element, "isActiveChannel"),
                };

                XElement? first = element.Element("firstEPG");
                if (first is not null)
                {
                    channel.FirstEpg = ParseEpg(first);
                }

                XElement? second = element.Element("secondEPG");
                if (second is not null)
                {
                    channel.SecondEpg = ParseEpg(second);
                }

                channels.Add(channel);
            }
        }
        catch (System.Xml.XmlException)
        {
            // Ungueltige/abgeschnittene Antwort => leere Liste.
        }

        return channels;
    }

    private static EpgEvent ParseEpg(XElement element) => new()
    {
        EventId = Value(element, "eventid"),
        Description = Value(element, "description"),
        Info1 = Value(element, "info1"),
        Info2 = Value(element, "info2"),
        StartTime = Value(element, "startTime"),
        TimeTotal = Value(element, "timeTotal"),
        TimeElapsed = Value(element, "timeElapsed"),
        Percentage = Value(element, "percentage"),
    };

    private static string Value(XElement? parent, string name) => parent?.Element(name)?.Value ?? string.Empty;

    /// <summary>
    /// Wurzelverzeichnisse für Videos/Aufnahmen (/control/getdir). Liefert die
    /// ABSOLUTEN Pfade der Box:
    ///   dir=recordingdir  -> Aufnahmeverzeichnis (network_nfs_recordingdir)
    ///   dir=moviedir      -> die Verzeichnisse aus moviebrowser.conf
    ///   dir=allmoviedirs  -> beide
    /// </summary>
    public async Task<List<string>> GetVideoRootsAsync(string dirToken, CancellationToken cancellationToken = default)
    {
        string? xml = await GetTextAsync(
            "/control/getdir?dir=" + Uri.EscapeDataString(dirToken) + "&format=xml",
            cancellationToken).ConfigureAwait(false);

        return xml is null ? new List<string>() : ParseDirectoriesXml(xml);
    }

    /// <summary>Parst &lt;dirs&gt;&lt;item&gt;&lt;dir&gt;PFAD&lt;/dir&gt;&lt;/item&gt;...&lt;/dirs&gt;.</summary>
    public static List<string> ParseDirectoriesXml(string xml)
    {
        List<string> result = new();
        foreach (XElement dir in LoadItems(xml, "dirs"))
        {
            string path = Value(dir, "dir");
            if (path.Length > 0 && !result.Contains(path))
            {
                result.Add(path);
            }
        }

        return result;
    }

    /// <summary>
    /// Videos eines Ordners (/control/getmovies?dir=&lt;absoluter Pfad&gt;).
    /// Die Box liefert title + absoluten Pfad + Groesse.
    /// </summary>
    public async Task<List<FileEntry>> GetMoviesAsync(string absolutePath, bool recursive, CancellationToken cancellationToken = default)
    {
        string? xml = await GetTextAsync(
            "/control/getmovies?dir=" + Uri.EscapeDataString(absolutePath) +
            "&subdirs=" + (recursive ? "true" : "false") + "&format=xml",
            cancellationToken).ConfigureAwait(false);

        return xml is null ? new List<FileEntry>() : ParseMoviesXml(xml);
    }

    /// <summary>Parst &lt;movies&gt;&lt;item&gt;&lt;title/&gt;&lt;path/&gt;&lt;size/&gt;&lt;/item&gt;...&lt;/movies&gt;.</summary>
    public static List<FileEntry> ParseMoviesXml(string xml)
    {
        List<FileEntry> result = new();
        foreach (XElement item in LoadItems(xml, "movies"))
        {
            string path = Value(item, "path");
            if (path.Length == 0)
            {
                continue;
            }

            string title = Value(item, "title");
            result.Add(new FileEntry
            {
                Name = title.Length > 0 ? title : Path.GetFileName(path),
                FullPath = path,
                IsDirectory = false,
                Size = ParseLong(Value(item, "size")),
            });
        }

        return result;
    }

    /// <summary>
    /// Verzeichnisinhalt eines absoluten Pfades (/control/file?action=list).
    /// Liefert Ordner und Dateien mit fullname (absoluter Pfad), type_str und size.
    /// </summary>
    public async Task<List<FileEntry>> GetFileListAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        string? xml = await GetTextAsync(
            "/control/file?action=list&path=" + Uri.EscapeDataString(absolutePath) + "&format=xml",
            cancellationToken).ConfigureAwait(false);

        return xml is null ? new List<FileEntry>() : ParseFileListXml(xml);
    }

    /// <summary>Parst &lt;filelist&gt;&lt;item&gt;&lt;name/&gt;&lt;type_str/&gt;&lt;fullname/&gt;&lt;size/&gt;&lt;/item&gt;...&lt;/filelist&gt;.</summary>
    public static List<FileEntry> ParseFileListXml(string xml)
    {
        List<FileEntry> result = new();
        foreach (XElement item in LoadItems(xml, "filelist"))
        {
            string name = Value(item, "name");
            if (name.Length == 0)
            {
                continue;
            }

            string typeStr = Value(item, "type_str");
            string type = Value(item, "type");
            string fullname = Value(item, "fullname");
            bool isDirectory = string.Equals(typeStr, "dir", StringComparison.OrdinalIgnoreCase) || type == "4";

            result.Add(new FileEntry
            {
                Name = name,
                FullPath = fullname.Length > 0 ? fullname : name,
                IsDirectory = isDirectory,
                Size = ParseLong(Value(item, "size")),
            });
        }

        return result;
    }

    /// <summary>
    /// Liefert die &lt;item&gt;-Elemente einer Liste. Toleriert Antworten ohne
    /// Wurzelelement (plain) sowie ein <c>data</c>-Element (JSON-nah).
    /// </summary>
    private static List<XElement> LoadItems(string xml, string listElement)
    {
        List<XElement> result = new();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return result;
        }

        try
        {
            XDocument doc = XDocument.Parse(xml, LoadOptions.None);
            XElement? list = doc.Descendants(listElement).FirstOrDefault() ?? doc.Root;
            if (list is null)
            {
                return result;
            }

            foreach (XElement item in list.Elements("item"))
            {
                result.Add(item);
            }
        }
        catch (System.Xml.XmlException)
        {
        }

        return result;
    }

    /// <summary>
    /// Baut die HTTP-URL einer Datei auf der Box. Der nhttpd ist mit
    /// Y_CONFIG_FEATUE_SENDFILE_CAN_ACCESS_ALL und mod_sendfile.sendAll gebaut und
    /// liefert absolute Pfade (inkl. Range-Requests für .ts/.mkv/.avi/...).
    /// </summary>
    public string BuildHttpFileUrl(string absolutePath)
    {
        string path = (absolutePath ?? string.Empty).Trim();
        if (path.Length > 0 && path[0] != '/')
        {
            path = "/" + path;
        }

        return "http://" + _profile.Host + ":" + _profile.WebPort + EscapePath(path);
    }

    /// <summary>Kodiert jeden Pfadabschnitt einzeln, damit "/" erhalten bleibt.</summary>
    private static string EscapePath(string path)
    {
        string[] segments = path.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] = Uri.EscapeDataString(segments[i]);
        }

        return string.Join("/", segments);
    }

    /// <summary>Timer als XML (/control/timer?format=xml).</summary>
    public async Task<List<TimerItem>> GetTimersAsync(CancellationToken cancellationToken = default)
    {
        string? xml = await GetTextAsync("/control/timer?format=xml", cancellationToken).ConfigureAwait(false);
        return xml is null ? new List<TimerItem>() : ParseTimersXml(xml);
    }

    /// <summary>Parst die Timer-XML der Box (Wurzel &lt;timer&gt;, Einträge &lt;timer_list&gt;/&lt;timer&gt;).</summary>
    public static List<TimerItem> ParseTimersXml(string xml)
    {
        List<TimerItem> result = new();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return result;
        }

        try
        {
            XDocument doc = XDocument.Parse(xml, LoadOptions.None);
            foreach (XElement element in doc.Descendants("timer"))
            {
                // Die Box liefert <timer> sowohl als Wurzelelement
                // (<timer><config/><timer_list><timer>...</timer></timer_list></timer>)
                // als auch je Timer. Nur echte Eintraege besitzen ein <id>.
                if (element.Element("id") is null)
                {
                    continue;
                }

                result.Add(new TimerItem
                {
                    Id = Value(element, "id"),
                    Type = Value(element, "type"),
                    Title = Value(element, "title"),
                    ChannelName = Value(element, "channel_name"),
                    ChannelId = Value(element, "channel_id"),
                    RecordingDir = Value(element, "recording_dir"),
                    Start = Value(element.Element("start")?.Element("normal"), "text"),
                    End = Value(element.Element("stop")?.Element("normal"), "text"),
                    AlarmTime = UnixSeconds(element, "alarm"),
                    StopTime = UnixSeconds(element, "stop"),
                });
            }
        }
        catch (System.Xml.XmlException)
        {
        }

        return result;
    }

    /// <summary>Unix-Zeit aus &lt;name&gt;&lt;normal&gt;&lt;digits&gt;... (siehe _SendTime in der Box).</summary>
    private static long UnixSeconds(XElement parent, string name)
    {
        string digits = Value(parent.Element(name)?.Element("normal"), "digits");
        return long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0;
    }

    /// <summary>Umschalten der Box auf einen Sender (Parameter = &lt;id&gt;).</summary>
    public async Task ZapToAsync(string channelId, CancellationToken cancellationToken = default)
        => await GetTextAsync("/control/zapto?" + channelId, cancellationToken).ConfigureAwait(false);

    /// <summary>/control/zapto ohne Parameter (Verbindungstest des Originals).</summary>
    public async Task<string?> ZapToProbeAsync(CancellationToken cancellationToken = default)
        => await GetTextAsync("/control/zapto", cancellationToken).ConfigureAwait(false);

    public async Task<string?> GetCurrentTpChannelsAsync(CancellationToken cancellationToken = default)
        => await GetTextAsync("/control/currenttpchannels", cancellationToken).ConfigureAwait(false);

    /// <summary>Fordert die Box auf, die Live-URL neu aufzubauen (ReciProxy).</summary>
    public async Task<string?> BuildLiveUrlAsync(CancellationToken cancellationToken = default)
        => await GetTextAsync("/control/build_live_url", cancellationToken).ConfigureAwait(false);

    /// <summary>Erstellt einen neuen Timer (Typ 5 = Aufnahme).</summary>
    public async Task CreateTimerAsync(
        string alarm,
        string stop,
        string channelId,
        string announce,
        CancellationToken cancellationToken = default)
    {
        string path =
            $"/control/timer?action=new&type=5&alarm={Uri.EscapeDataString(alarm)}" +
            $"&stop={Uri.EscapeDataString(stop)}&announce={Uri.EscapeDataString(announce)}" +
            $"&channel_id={Uri.EscapeDataString(channelId)}";

        await GetTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Timer-Liste im Kompaktformat (/control/timer?format=id). Eine Zeile je
    /// Timer mit den Feldern:
    /// eventID eventType eventRepeat repeatCount announceTime alarmTime stopTime channelID
    /// </summary>
    public async Task<List<TimerItem>> GetTimerLinesAsync(CancellationToken cancellationToken = default)
    {
        string? text = await GetTextAsync("/control/timer?format=id", cancellationToken).ConfigureAwait(false);
        return text is null ? new List<TimerItem>() : ParseTimerLines(text);
    }

    /// <summary>Parst die Zeilen aus /control/timer?format=id.</summary>
    public static List<TimerItem> ParseTimerLines(string text)
    {
        List<TimerItem> result = new();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 8)
            {
                continue;
            }

            result.Add(new TimerItem
            {
                Id = parts[0],
                Type = parts[1],
                AlarmTime = ParseLong(parts[5]),
                StopTime = ParseLong(parts[6]),
                ChannelId = parts[7],
            });
        }

        return result;
    }

    /// <summary>
    /// Liefert die aktuell laufenden Aufnahmen. Grundlage ist - wie im Original -
    /// der Vergleich der Boxzeit mit Start-/Endzeit der Timer
    /// (/control/timer?format=id gegen /control/gettime?rawtime).
    /// </summary>
    public async Task<List<RunningRecording>> GetRunningRecordingsAsync(CancellationToken cancellationToken = default)
    {
        List<RunningRecording> result = new();
        List<TimerItem> timers = await GetTimerLinesAsync(cancellationToken).ConfigureAwait(false);
        if (timers.Count == 0)
        {
            return result;
        }

        long now = await GetBoxTimeAsync(cancellationToken).ConfigureAwait(false);
        foreach (TimerItem timer in timers)
        {
            if (!timer.IsRunning(now))
            {
                continue;
            }

            result.Add(new RunningRecording
            {
                EventId = ParseLong(timer.Id),
                ChannelId = timer.ChannelId,
                Start = timer.AlarmTime,
                Stop = timer.StopTime,
            });
        }

        return result;
    }

    /// <summary>Aktuelle Zeit der Box als Unix-Sekunden (Fallback: lokale Uhr).</summary>
    public async Task<long> GetBoxTimeAsync(CancellationToken cancellationToken = default)
    {
        string? raw = await GetTimeRawAsync(cancellationToken).ConfigureAwait(false);
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds)
            ? seconds
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private static long ParseLong(string value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result) ? result : 0;

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

    /// <summary>VLC-Stream-URL fuer einen Sender (ReciProxy/Transcoding).</summary>
    public string BuildVlcStreamUrl(string channelId, bool reciProxy)
        => reciProxy ? $"{StreamBaseUrl}/ReciProxy" : $"{StreamBaseUrl}/id={channelId}";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http.Dispose();
    }
}
