using System.Globalization;
using System.Net.Http;
using System.Xml.Linq;

namespace TCTV.Core;

/// <summary>
/// Alte Schnittstelle des Recording Service: <c>/control/...</c> mit Text- und
/// XML-Antworten. Das ist die Schnittstelle, die das Originalprogramm nutzt; sie
/// wird im Profil über "Alte API (/control/)" gewählt.
/// </summary>
public sealed class LegacyControlApi : BoxApiClient
{
    public LegacyControlApi(Profile profile, TimeSpan? timeout = null)
        : base(profile, timeout, useBasicAuthentication: true)
    {
    }

    /// <summary>Holt eine Antwort als Text (null bei Fehler/unerreichbarer Box).</summary>
    public override async Task<string?> GetTextAsync(string path, CancellationToken cancellationToken = default)
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

    /// <summary>Prüft die Verbindung zur Box (/control/info).</summary>
    public override async Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default)
        => await GetTextAsync("/control/info", cancellationToken).ConfigureAwait(false) is not null;

    public override async Task<BoxMode> GetModeAsync(CancellationToken cancellationToken = default)
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

    public override async Task<string?> GetTimeRawAsync(CancellationToken cancellationToken = default)
    {
        string? value = await GetTextAsync("/control/gettime?rawtime", cancellationToken).ConfigureAwait(false);
        return value?.Trim();
    }

    /// <summary>Zap-Modus ("on"/"off" usw.).</summary>
    public override async Task<string?> GetStandbyAsync(CancellationToken cancellationToken = default)
    {
        string? value = await GetTextAsync("/control/standby", cancellationToken).ConfigureAwait(false);
        return value?.Replace("\r", string.Empty).Replace("\n", string.Empty);
    }

    /// <summary>Schaltet die Box in den Standby bzw. ein (optional mit CEC).</summary>
    public override async Task TurnStandbyAsync(bool on, bool cecOff, CancellationToken cancellationToken = default)
    {
        string path = on ? "/control/standby?on" : "/control/standby?off" + (cecOff ? "&cec=off" : string.Empty);
        await GetTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Aktuelle Zeit der Box als Unix-Sekunden (Fallback: lokale Uhr).</summary>
    public override async Task<long> GetBoxTimeAsync(CancellationToken cancellationToken = default)
    {
        string? raw = await GetTimeRawAsync(cancellationToken).ConfigureAwait(false);
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds)
            ? seconds
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    /// <summary>Liste aller Bouquets: Zeilen der Form "NR NAME".</summary>
    public override async Task<List<Bouquet>> GetBouquetsAsync(CancellationToken cancellationToken = default)
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

    public override async Task<List<Channel>> GetBouquetAsync(string bouquetNumber, CancellationToken cancellationToken = default)
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
    public override async Task<List<string>> GetVideoRootsAsync(string dirToken, CancellationToken cancellationToken = default)
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
    public override async Task<List<FileEntry>> GetMoviesAsync(string absolutePath, bool recursive, CancellationToken cancellationToken = default)
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
                Name = title.Length > 0 ? title : System.IO.Path.GetFileName(path),
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
    public override async Task<List<FileEntry>> GetFileListAsync(string absolutePath, CancellationToken cancellationToken = default)
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
    public override string BuildHttpFileUrl(string absolutePath)
    {
        string path = (absolutePath ?? string.Empty).Trim();
        if (path.Length > 0 && path[0] != '/')
        {
            path = "/" + path;
        }

        return "http://" + Profile.Host + ":" + Profile.WebPort + EscapePath(path);
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

    /// <summary>Die alte API liefert Dateien ohne Anmeldung – die Adresse genügt.</summary>
    public override Task<string> GetFileUrlAsync(string absolutePath, CancellationToken cancellationToken = default)
        => Task.FromResult(BuildHttpFileUrl(absolutePath));

    /// <summary>Timer als XML (/control/timer?format=xml).</summary>
    public override async Task<List<TimerItem>> GetTimersAsync(CancellationToken cancellationToken = default)
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
    public override async Task ZapToAsync(string channelId, CancellationToken cancellationToken = default)
        => await GetTextAsync("/control/zapto?" + channelId, cancellationToken).ConfigureAwait(false);

    /// <summary>/control/zapto ohne Parameter (Verbindungstest des Originals).</summary>
    public override async Task<string?> ZapToProbeAsync(CancellationToken cancellationToken = default)
        => await GetTextAsync("/control/zapto", cancellationToken).ConfigureAwait(false);

    public override async Task<string?> GetCurrentTpChannelsAsync(CancellationToken cancellationToken = default)
        => await GetTextAsync("/control/currenttpchannels", cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Die alte API meldet keine fertige Stream-Adresse; die Adresse wird aus dem
    /// Streaming-Port des Profils gebaut (siehe <see cref="BuildVlcStreamUrl"/>).
    /// </summary>
    public override Task<string?> GetStreamUrlAsync(string channelId, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    /// <summary>Erstellt einen neuen Timer (Typ 5 = Aufnahme).</summary>
    public override async Task CreateTimerAsync(
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
    public override async Task<List<TimerItem>> GetTimerLinesAsync(CancellationToken cancellationToken = default)
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
    public override async Task<List<RunningRecording>> GetRunningRecordingsAsync(CancellationToken cancellationToken = default)
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

    /// <summary>VLC-Stream-URL fuer einen Sender.</summary>
    public override string BuildVlcStreamUrl(string channelId)
        => $"{StreamBaseUrl}/id={channelId}";

    private static long ParseLong(string value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result) ? result : 0;
}