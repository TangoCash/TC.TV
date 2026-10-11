using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace TCTV.Core;

/// <summary>
/// Neue REST-Schnittstelle der Box: <c>/api/v1/...</c> mit JSON-Antworten.
///
/// Umgesetzt nach dem Quelltext des NI-Images (github.com/neutrino-images/ni-neutrino,
/// <c>src/httpd/ep/*.cpp</c>):
///   * Lesen darf jeder im eigenen Netz (<c>lan_read</c>), Schreiben und Systemzugriffe
///     brauchen eine Sitzung (<c>POST /api/v1/login</c>) – Basic-Authentifizierung
///     kennt die neue API nicht, deshalb wird hier angemeldet.
///   * Verandernde Aufrufe (POST/PUT/PATCH/DELETE) verlangen zusätzlich den zweiten
///     Token aus der Anmeldung im Kopf <c>X-CSRF-Token</c>.
/// </summary>
public sealed class NeutrinoV1Api : BoxApiClient
{
    /// <summary>Wurzel aller Routen der neuen API.</summary>
    public const string Prefix = "/api/v1";

    /// <summary>Kopfzeile für den zweiten Token (Vorgabe der Box).</summary>
    public const string DefaultCsrfHeader = "X-CSRF-Token";

    // Obergrenzen der Box (src/httpd/ep/ep_channels.cpp, src/coreapi/epg.h)
    private const int ChannelPageSize = 500;   // kMaxPage
    private const int MaxChannelPages = 20;
    private const int EpgGridPageSize = 100;   // MAX_GRID_CHANNELS
    private const int MaxEpgGridPages = 20;
    private const int MaxRecursiveDirs = 200;
    private const int MaxRecursiveEntries = 5000;

    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private bool _loginTried;
    private bool _authenticated;
    private string _csrf = string.Empty;
    private string _csrfHeader = DefaultCsrfHeader;
    private DateTimeOffset _sessionExpiry = DateTimeOffset.MinValue;

    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string _mediaToken = string.Empty;
    private DateTimeOffset _mediaTokenExpiry = DateTimeOffset.MinValue;

    // Senderliste der Box als Zwischenspeicher (Transponder-Zuordnung, Namen).
    private readonly SemaphoreSlim _channelGate = new(1, 1);
    private List<Channel> _channels = new();
    private Dictionary<string, string> _transponders = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _channelsTime = DateTimeOffset.MinValue;

    public NeutrinoV1Api(Profile profile, TimeSpan? timeout = null)
        : base(profile, timeout, useBasicAuthentication: false, useCookies: true)
    {
    }

    // ---------------------------------------------------------------------
    // JSON-Hilfen
    // ---------------------------------------------------------------------

    private static string JsonText(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static long JsonLong(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt64(out long number) ? number : 0,
            JsonValueKind.String => long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0,
            _ => 0,
        };
    }

    private static bool JsonBool(JsonElement element, string name, bool fallback = false)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback,
        };
    }

    private static JsonElement JsonArray(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Array
            ? value
            : default;

    /// <summary>
    /// Vergleicht Sender-IDs der Box: sie sind hexadezimal und werden je Endpunkt
    /// mit bzw. ohne führende Nullen geschrieben.
    /// </summary>
    public static string NormalizeChannelId(string? channelId)
    {
        string id = (channelId ?? string.Empty).Trim();
        if (id.Length == 0)
        {
            return string.Empty;
        }

        return ulong.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsed)
            ? parsed.ToString("x", CultureInfo.InvariantCulture)
            : id.ToLowerInvariant();
    }

    private static JsonDocument? TryParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------------
    // HTTP-Zugriff
    // ---------------------------------------------------------------------

    private async Task<HttpResponseMessage?> SendAnonymousAsync(
        HttpMethod method, string path, string? jsonBody, bool withCsrf, CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = new(method, BaseUrl + path);
            if (jsonBody is not null)
            {
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            }

            if (withCsrf && _csrf.Length > 0)
            {
                request.Headers.TryAddWithoutValidation(_csrfHeader, _csrf);
            }

            return await _http
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<HttpResponseMessage?> SendAsync(
        HttpMethod method, string path, string? jsonBody, bool withSession, CancellationToken cancellationToken)
    {
        if (withSession)
        {
            await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        }

        return await SendAnonymousAsync(method, path, jsonBody, withSession, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Holt eine Antwort als Text; null, wenn die Box ablehnt oder schweigt.</summary>
    private async Task<string?> RequestTextAsync(
        HttpMethod method, string path, string? jsonBody, bool withSession, CancellationToken cancellationToken)
    {
        using HttpResponseMessage? response =
            await SendAsync(method, path, jsonBody, withSession, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return DecodeText(bytes);
    }

    /// <summary>Holt eine JSON-Antwort; der Aufrufer gibt das Dokument frei.</summary>
    private async Task<JsonDocument?> RequestJsonAsync(
        HttpMethod method, string path, string? jsonBody, bool withSession, CancellationToken cancellationToken)
    {
        string? text = await RequestTextAsync(method, path, jsonBody, withSession, cancellationToken).ConfigureAwait(false);
        return TryParseJson(text);
    }

    public override async Task<string?> GetTextAsync(string path, CancellationToken cancellationToken = default)
        => await RequestTextAsync(HttpMethod.Get, path, null, withSession: false, cancellationToken).ConfigureAwait(false);

    // ---------------------------------------------------------------------
    // Anmeldung (die neue API kennt kein Basic; Schreiben braucht eine Sitzung)
    // ---------------------------------------------------------------------

    private async Task EnsureSessionAsync(CancellationToken cancellationToken)
    {
        if (_authenticated && DateTimeOffset.UtcNow < _sessionExpiry)
        {
            return;
        }

        // Falsche Zugangsdaten nicht bei jeder Anfrage erneut probieren.
        if (_loginTried && !_authenticated)
        {
            return;
        }

        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_authenticated && DateTimeOffset.UtcNow < _sessionExpiry)
            {
                return;
            }

            if (_loginTried && !_authenticated)
            {
                return;
            }

            string user = (Profile.WebUser ?? string.Empty).Trim();
            if (user.Length == 0)
            {
                _loginTried = true;
                return;
            }

            _loginTried = true;
            string body = "{\"user\":" + JsonSerializer.Serialize(user)
                + ",\"password\":" + JsonSerializer.Serialize(Profile.WebPassword ?? string.Empty) + "}";

            using HttpResponseMessage? response =
                await SendAnonymousAsync(HttpMethod.Post, Prefix + "/login", body, withCsrf: false, cancellationToken)
                    .ConfigureAwait(false);

            if (response is null || !response.IsSuccessStatusCode)
            {
                return;
            }

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument? document = TryParseJson(DecodeText(bytes));
            if (document is null)
            {
                return;
            }

            JsonElement root = document.RootElement;
            _authenticated = JsonBool(root, "authenticated");
            _csrf = JsonText(root, "csrf");

            string header = JsonText(root, "csrf_header");
            _csrfHeader = header.Length > 0 ? header : DefaultCsrfHeader;

            long expires = JsonLong(root, "expires_in");
            _sessionExpiry = expires > 0
                ? DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, expires - 60))
                : DateTimeOffset.UtcNow.AddMinutes(5);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    /// <summary>Ist eine Sitzung mit Schreibrechten vorhanden?</summary>
    public async Task<bool> HasSessionAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        return _authenticated;
    }

    // ---------------------------------------------------------------------
    // Systemabfragen
    // ---------------------------------------------------------------------

    /// <summary>Prüft die Verbindung zur Box (GET /api/v1/system/info).</summary>
    public override async Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default)
        => await RequestJsonAsync(HttpMethod.Get, Prefix + "/system/info", null, withSession: false, cancellationToken)
            .ConfigureAwait(false) is not null;

    /// <summary>
    /// Modus der Box. Die neue API hat keinen eigenen "getmode": der Standby kommt aus
    /// /api/v1/system/standby, TV/Radio aus der Art des laufenden Senders.
    /// </summary>
    public override async Task<BoxMode> GetModeAsync(CancellationToken cancellationToken = default)
    {
        string? standby = await GetStandbyAsync(cancellationToken).ConfigureAwait(false);
        if (standby == "on")
        {
            return BoxMode.Standby;
        }

        using JsonDocument? document = await RequestJsonAsync(
            HttpMethod.Get, Prefix + "/channels/current", null, withSession: false, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return BoxMode.Unknown;
        }

        return JsonText(document.RootElement, "kind") switch
        {
            "tv" or "webtv" => BoxMode.Tv,
            "radio" or "webradio" => BoxMode.Radio,
            _ => BoxMode.Unknown,
        };
    }

    /// <summary>
    /// Die neue API nennt keinen Zeitstempel; die Uhrzeit kommt daher von der lokalen
    /// Uhr (Timer und Sendungen werden ohnehin in Unix-Sekunden ausgetauscht).
    /// </summary>
    public override Task<string?> GetTimeRawAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    /// <summary>Standby-Zustand aus /api/v1/system/standby ("on"/"off").</summary>
    public override async Task<string?> GetStandbyAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument? document = await RequestJsonAsync(
            HttpMethod.Get, Prefix + "/system/standby", null, withSession: false, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        return JsonBool(document.RootElement, "on") ? "on" : "off";
    }

    /// <summary>
    /// Schaltet die Box in den Standby bzw. ein (POST /api/v1/system/standby).
    /// Braucht Systemrechte, daher eine Sitzung. CEC kennt die neue API nicht.
    /// </summary>
    public override async Task TurnStandbyAsync(bool on, bool cecOff, CancellationToken cancellationToken = default)
    {
        string body = "{\"on\":" + (on ? "true" : "false") + "}";
        await RequestTextAsync(HttpMethod.Post, Prefix + "/system/standby", body, withSession: true, cancellationToken)
            .ConfigureAwait(false);
    }

    public override Task<long> GetBoxTimeAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    // ---------------------------------------------------------------------
    // Bouquets und Sender
    // ---------------------------------------------------------------------

    /// <summary>Alle Bouquets der Box (GET /api/v1/bouquets).</summary>
    public override async Task<List<Bouquet>> GetBouquetsAsync(CancellationToken cancellationToken = default)
    {
        string? text = await RequestTextAsync(HttpMethod.Get, Prefix + "/bouquets", null, withSession: false, cancellationToken)
            .ConfigureAwait(false);

        return text is null ? new List<Bouquet>() : ParseBouquets(text);
    }

    /// <summary>
    /// Sender eines Bouquets samt erster/zweiter Sendung. Die neue API trennt beides:
    /// die Sender kommen aus /api/v1/channels, die Sendungen aus /api/v1/epg/grid.
    /// </summary>
    public override async Task<List<Channel>> GetBouquetAsync(string bouquetNumber, CancellationToken cancellationToken = default)
    {
        List<Channel> channels = await GetChannelsAsync(bouquetNumber, cancellationToken).ConfigureAwait(false);
        if (channels.Count == 0)
        {
            return channels;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Dictionary<string, List<EpgEvent>> guide = await GetEpgGridAsync(
                bouquetNumber, now - (3 * 3600), now + (12 * 3600), cancellationToken)
            .ConfigureAwait(false);
        ApplyEpg(channels, guide, now);
        return channels;
    }

    /// <summary>
    /// EPG eines einzelnen Senders (für "weiteres EPG"): holt den EPG-Raster mit
    /// einem größeren Zeitfenster und liefert nur die Sendungen dieses Senders.
    /// </summary>
    public override async Task<List<EpgEvent>> GetChannelEpgAsync(string channelId, string bouquetNumber, CancellationToken cancellationToken = default)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Dictionary<string, List<EpgEvent>> guide = await GetEpgGridAsync(
                bouquetNumber, now - (3 * 3600), now + (72 * 3600), cancellationToken)
            .ConfigureAwait(false);

        if (!guide.TryGetValue(NormalizeChannelId(channelId), out List<EpgEvent>? events) || events.Count == 0)
        {
            return new List<EpgEvent>();
        }

        events.Sort((left, right) => left.StartUnix.CompareTo(right.StartUnix));
        return events;
    }

    /// <summary>Sender eines Bouquets; blättert über <c>next_cursor</c>.</summary>
    private async Task<List<Channel>> GetChannelsAsync(string bouquetNumber, CancellationToken cancellationToken)
    {
        List<Channel> result = new();
        string baseQuery = "?bouquet=" + Uri.EscapeDataString(bouquetNumber)
            + "&limit=" + ChannelPageSize.ToString(CultureInfo.InvariantCulture);
        string cursor = string.Empty;

        for (int page = 0; page < MaxChannelPages; page++)
        {
            string path = Prefix + "/channels" + baseQuery
                + (cursor.Length == 0 ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor));
            string? text = await RequestTextAsync(HttpMethod.Get, path, null, withSession: false, cancellationToken)
                .ConfigureAwait(false);
            if (text is null)
            {
                break;
            }

            List<Channel> parsed = ParseChannelPage(text, out cursor, null);
            result.AddRange(parsed);
            if (cursor.Length == 0 || parsed.Count == 0)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Gesamte TV-Senderliste der Box (für Transponder-Vergleich und Sendernamen),
    /// kurz zwischengespeichert.
    /// </summary>
    private async Task<List<Channel>> GetChannelCacheAsync(CancellationToken cancellationToken)
    {
        if (_channels.Count > 0 && DateTimeOffset.UtcNow - _channelsTime < TimeSpan.FromSeconds(60))
        {
            return _channels;
        }

        await _channelGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_channels.Count > 0 && DateTimeOffset.UtcNow - _channelsTime < TimeSpan.FromSeconds(60))
            {
                return _channels;
            }

            List<Channel> all = new();
            Dictionary<string, string> transponders = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
            string cursor = string.Empty;

            for (int page = 0; page < MaxChannelPages; page++)
            {
                string path = Prefix + "/channels?limit=" + ChannelPageSize.ToString(CultureInfo.InvariantCulture)
                    + (cursor.Length == 0 ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor));
                string? text = await RequestTextAsync(HttpMethod.Get, path, null, withSession: false, cancellationToken)
                    .ConfigureAwait(false);
                if (text is null)
                {
                    break;
                }

                List<Channel> parsed = ParseChannelPage(text, out cursor, transponders);
                if (parsed.Count == 0)
                {
                    break;
                }

                foreach (Channel channel in parsed)
                {
                    names[NormalizeChannelId(channel.Id)] = channel.Name;
                }

                all.AddRange(parsed);
                if (cursor.Length == 0)
                {
                    break;
                }
            }

            if (all.Count > 0)
            {
                _channels = all;
                _transponders = transponders;
                _names = names;
                _channelsTime = DateTimeOffset.UtcNow;
            }

            return _channels;
        }
        finally
        {
            _channelGate.Release();
        }
    }

    /// <summary>
    /// Sendungen eines Bouquets (GET /api/v1/epg/grid) im Zeitfenster
    /// <paramref name="from"/> bis <paramref name="to"/>.
    /// </summary>
    private async Task<Dictionary<string, List<EpgEvent>>> GetEpgGridAsync(
        string bouquetNumber, long from, long to, CancellationToken cancellationToken)
    {
        Dictionary<string, List<EpgEvent>> result = new(StringComparer.OrdinalIgnoreCase);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string baseQuery = "?bouquet=" + Uri.EscapeDataString(bouquetNumber)
            + "&from=" + from.ToString(CultureInfo.InvariantCulture)
            + "&to=" + to.ToString(CultureInfo.InvariantCulture)
            + "&limit=" + EpgGridPageSize.ToString(CultureInfo.InvariantCulture);
        string cursor = string.Empty;

        for (int page = 0; page < MaxEpgGridPages; page++)
        {
            string path = Prefix + "/epg/grid" + baseQuery
                + (cursor.Length == 0 ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor));
            string? text = await RequestTextAsync(HttpMethod.Get, path, null, withSession: false, cancellationToken)
                .ConfigureAwait(false);
            if (text is null)
            {
                break;
            }

            cursor = MergeEpgGrid(text, now, result);
            if (cursor.Length == 0)
            {
                break;
            }
        }

        return result;
    }

    // ---------------------------------------------------------------------
    // Auswertung der JSON-Antworten (auch für den Selbsttest)
    // ---------------------------------------------------------------------

    /// <summary>Wertet &lt;bouquet-list&gt; aus: {"items":[{"id":1,"name":"..."}]}.</summary>
    public static List<Bouquet> ParseBouquets(string json)
    {
        List<Bouquet> result = new();
        using JsonDocument? document = TryParseJson(json);
        if (document is null)
        {
            return result;
        }

        JsonElement items = JsonArray(document.RootElement, "items");
        if (items.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement item in items.EnumerateArray())
        {
            string name = JsonText(item, "name");
            long number = JsonLong(item, "id");
            result.Add(new Bouquet
            {
                Number = number > 0 ? number.ToString(CultureInfo.InvariantCulture) : string.Empty,
                Name = name,
            });
        }

        return result;
    }

    /// <summary>Wertet eine Seite von /api/v1/channels aus ({"items":[...],"next_cursor":"..."}).</summary>
    public static List<Channel> ParseChannels(string json) => ParseChannelPage(json, out _, null);

    /// <summary>Sender-ID (normalisiert) auf Transponderschlüssel, wie die Senderliste sie nennt.</summary>
    public static Dictionary<string, string> ParseChannelTransponders(string json)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        ParseChannelPage(json, out _, result);
        return result;
    }

    private static List<Channel> ParseChannelPage(string json, out string nextCursor, Dictionary<string, string>? transponders)
    {
        List<Channel> result = new();
        nextCursor = string.Empty;

        using JsonDocument? document = TryParseJson(json);
        if (document is null)
        {
            return result;
        }

        JsonElement root = document.RootElement;
        JsonElement items = JsonArray(root, "items");
        if (items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                string id = JsonText(item, "id");
                if (id.Length == 0)
                {
                    continue;
                }

                long number = JsonLong(item, "number");
                result.Add(new Channel
                {
                    Number = number > 0 ? number.ToString(CultureInfo.InvariantCulture) : string.Empty,
                    Id = id,
                    ShortId = ShortIdOf(id),
                    Name = JsonText(item, "name"),
                });

                if (transponders is not null)
                {
                    string key = TransponderKeyOf(item);
                    if (key.Length > 0)
                    {
                        transponders[NormalizeChannelId(id)] = key;
                    }
                }
            }
        }

        nextCursor = JsonText(root, "next_cursor");
        return result;
    }

    /// <summary>Die unteren 48 Bit der Sender-ID – so benennt die Box ihre Logodateien.</summary>
    private static string ShortIdOf(string channelId)
        => channelId.Length <= 12 ? channelId : channelId[^12..];

    /// <summary>
    /// Der Transponder eines Senders: Satellitenposition, Transponder-Schlüssel,
    /// Transportstrom und Ursprungsnetz. Leer, wenn die Box nichts davon nennt
    /// (z. B. bei Web-TV, das keinen Tuner braucht).
    /// </summary>
    private static string TransponderKeyOf(JsonElement item)
    {
        long frequencyKey = JsonLong(item, "freq_id");
        long transportStream = JsonLong(item, "transport_stream_id");
        if (frequencyKey == 0 && transportStream == 0)
        {
            return string.Empty;
        }

        return JsonLong(item, "satellite_position").ToString(CultureInfo.InvariantCulture)
            + ":" + frequencyKey.ToString(CultureInfo.InvariantCulture)
            + ":" + transportStream.ToString(CultureInfo.InvariantCulture)
            + ":" + JsonLong(item, "original_network_id").ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Wertet eine Seite von /api/v1/epg/grid aus und hängt die Sendungen an
    /// <paramref name="target"/> (Schlüssel: normalisierte Sender-ID). Liefert den
    /// <c>next_cursor</c> der Seite.
    /// </summary>
    private static string MergeEpgGrid(string json, long now, Dictionary<string, List<EpgEvent>> target)
    {
        using JsonDocument? document = TryParseJson(json);
        if (document is null)
        {
            return string.Empty;
        }

        JsonElement root = document.RootElement;
        JsonElement items = JsonArray(root, "items");
        if (items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement row in items.EnumerateArray())
            {
                string channelId = JsonText(row, "channel_id");
                if (channelId.Length == 0)
                {
                    continue;
                }

                string key = NormalizeChannelId(channelId);
                if (!target.TryGetValue(key, out List<EpgEvent>? list))
                {
                    list = new List<EpgEvent>();
                    target[key] = list;
                }

                JsonElement events = JsonArray(row, "events");
                if (events.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement item in events.EnumerateArray())
                {
                    list.Add(ToEpgEvent(item, now));
                }
            }
        }

        return JsonText(root, "next_cursor");
    }

    /// <summary>Wertet eine Antwort von /api/v1/epg/grid eigenständig aus (Selbsttest).</summary>
    public static Dictionary<string, List<EpgEvent>> ParseEpgGrid(string json, long now)
    {
        Dictionary<string, List<EpgEvent>> result = new(StringComparer.OrdinalIgnoreCase);
        MergeEpgGrid(json, now, result);
        return result;
    }

    /// <summary>
    /// Eine Sendung aus der neuen API als <see cref="EpgEvent"/>: Titel, Beginn,
    /// Dauer und der daraus errechnete Fortschritt in Prozent.
    /// </summary>
    private static EpgEvent ToEpgEvent(JsonElement item, long now)
    {
        string id = JsonText(item, "id");
        string title = JsonText(item, "title");
        long start = JsonLong(item, "start");
        long duration = JsonLong(item, "duration");
        long elapsed = duration > 0 ? Math.Clamp(now - start, 0, duration) : 0;
        int totalMinutes = (int)((duration + 59) / 60);
        int elapsedMinutes = (int)(elapsed / 60);

        return new EpgEvent
        {
            EventId = id,
            Description = title,
            Info1 = title,
            Info2 = JsonText(item, "description"),
            StartTime = start > 0
                ? DateTimeOffset.FromUnixTimeSeconds(start).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)
                : string.Empty,
            TimeTotal = totalMinutes.ToString(CultureInfo.InvariantCulture),
            TimeElapsed = elapsedMinutes.ToString(CultureInfo.InvariantCulture),
            Percentage = duration > 0
                ? ((int)(elapsed * 100 / duration)).ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            StartUnix = start,
            DurationSeconds = duration,
        };
    }

    /// <summary>
    /// Setzt erste (laufende bzw. nächste) und zweite Sendung eines Senders aus der
    /// Sendungsliste. Entspricht dem, was die alte API als &lt;firstEPG&gt;/&lt;secondEPG&gt;
    /// in einem Aufruf mitliefert.
    /// </summary>
    public static void ApplyEpg(List<Channel> channels, Dictionary<string, List<EpgEvent>> guide, long now)
    {
        foreach (Channel channel in channels)
        {
            if (!guide.TryGetValue(NormalizeChannelId(channel.Id), out List<EpgEvent>? events) || events.Count == 0)
            {
                continue;
            }

            events.Sort((left, right) => left.StartUnix.CompareTo(right.StartUnix));

            int running = events.FindIndex(e => e.StartUnix <= now
                && (e.DurationSeconds <= 0 || now < e.StartUnix + e.DurationSeconds));
            if (running < 0)
            {
                running = events.FindIndex(e => e.StartUnix >= now);
            }

            if (running < 0)
            {
                running = 0;
            }

            channel.FirstEpg = events[running];
            channel.SecondEpg = running + 1 < events.Count ? events[running + 1] : new EpgEvent();
        }
    }

    // ---------------------------------------------------------------------
    // Timer und laufende Aufnahmen
    // ---------------------------------------------------------------------

    /// <summary>Timer der Box (GET /api/v1/timers).</summary>
    public override async Task<List<TimerItem>> GetTimersAsync(CancellationToken cancellationToken = default)
    {
        string? text = await RequestTextAsync(HttpMethod.Get, Prefix + "/timers", null, withSession: false, cancellationToken)
            .ConfigureAwait(false);
        if (text is null)
        {
            return new List<TimerItem>();
        }

        // Sendernamen stehen nur in der Senderliste, die Timer nennen nur die ID.
        await GetChannelCacheAsync(cancellationToken).ConfigureAwait(false);
        return ParseTimers(text, key => _names.TryGetValue(key, out string? name) ? name : string.Empty);
    }

    /// <summary>Die neue API liefert die Timer immer vollständig (kein Kompaktformat).</summary>
    public override Task<List<TimerItem>> GetTimerLinesAsync(CancellationToken cancellationToken = default)
        => GetTimersAsync(cancellationToken);

    /// <summary>Wertet &lt;timer-list&gt; aus: {"items":[{"id":17,"kind":"record",...}]}.</summary>
    public static List<TimerItem> ParseTimers(string json, Func<string, string>? channelName = null)
    {
        List<TimerItem> result = new();
        using JsonDocument? document = TryParseJson(json);
        if (document is null)
        {
            return result;
        }

        JsonElement items = JsonArray(document.RootElement, "items");
        if (items.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement item in items.EnumerateArray())
        {
            long id = JsonLong(item, "id");
            long start = JsonLong(item, "start");
            long stop = JsonLong(item, "stop");
            string channelId = JsonText(item, "channel_id");

            result.Add(new TimerItem
            {
                Id = id > 0 ? id.ToString(CultureInfo.InvariantCulture) : string.Empty,
                Type = JsonText(item, "kind"),
                Title = JsonText(item, "title"),
                ChannelId = channelId,
                ChannelName = channelName is null ? string.Empty : channelName(NormalizeChannelId(channelId)),
                RecordingDir = JsonText(item, "recording_dir"),
                Start = LocalTimeText(start),
                End = LocalTimeText(stop),
                AlarmTime = start,
                StopTime = stop,
            });
        }

        return result;
    }

    /// <summary>Laufende Aufnahmen (GET /api/v1/recordings).</summary>
    public override async Task<List<RunningRecording>> GetRunningRecordingsAsync(CancellationToken cancellationToken = default)
    {
        string? text = await RequestTextAsync(HttpMethod.Get, Prefix + "/recordings", null, withSession: false, cancellationToken)
            .ConfigureAwait(false);

        return text is null ? new List<RunningRecording>() : ParseRecordings(text);
    }

    /// <summary>Wertet &lt;recording-list&gt; aus.</summary>
    public static List<RunningRecording> ParseRecordings(string json)
    {
        List<RunningRecording> result = new();
        using JsonDocument? document = TryParseJson(json);
        if (document is null)
        {
            return result;
        }

        JsonElement items = JsonArray(document.RootElement, "items");
        if (items.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement item in items.EnumerateArray())
        {
            result.Add(new RunningRecording
            {
                EventId = JsonLong(item, "id"),
                ChannelId = JsonText(item, "channel_id"),
                Start = JsonLong(item, "start"),
                Stop = 0,
            });
        }

        return result;
    }

    /// <summary>
    /// Erstellt einen Aufnahme-Timer (POST /api/v1/timers, kind = record). Die alte
    /// API bekam lokale Zeitangaben, die neue erwartet Unix-Sekunden.
    /// </summary>
    public override async Task CreateTimerAsync(
        string alarm,
        string stop,
        string channelId,
        string announce,
        CancellationToken cancellationToken = default)
    {
        StringBuilder body = new();
        body.Append("{\"kind\":\"record\",\"channel_id\":").Append(JsonSerializer.Serialize(channelId));

        if (TryParseLocalUnix(alarm, out long start))
        {
            body.Append(",\"start\":").Append(start.ToString(CultureInfo.InvariantCulture));
        }

        if (TryParseLocalUnix(stop, out long stopUnix))
        {
            body.Append(",\"stop\":").Append(stopUnix.ToString(CultureInfo.InvariantCulture));
        }

        if (TryParseLocalUnix(announce, out long announceUnix))
        {
            body.Append(",\"announce\":").Append(announceUnix.ToString(CultureInfo.InvariantCulture));
        }

        body.Append('}');

        await RequestTextAsync(HttpMethod.Post, Prefix + "/timers", body.ToString(), withSession: true, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Wandelt eine lokale Zeitangabe ("yyyy-MM-ddTHH:mm:ss" und übliche Schreibweisen)
    /// in Unix-Sekunden. Zahlen gelten bereits als Unix-Sekunden; "0" und leer ergeben false.
    /// </summary>
    public static bool TryParseLocalUnix(string? text, out long unixSeconds)
    {
        unixSeconds = 0;
        string value = (text ?? string.Empty).Trim();
        if (value.Length == 0 || value == "0")
        {
            return false;
        }

        string[] formats =
        {
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm",
            "yyyy-MM-dd HH:mm",
            "dd.MM.yyyy HH:mm:ss",
            "dd.MM.yyyy HH:mm",
        };

        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
        {
            unixSeconds = new DateTimeOffset(local).ToUnixTimeSeconds();
            return true;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long already))
        {
            unixSeconds = already;
            return true;
        }

        return false;
    }

    // ---------------------------------------------------------------------
    // Dateien (Aufnahmen und Videos)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Wurzelverzeichnisse für den Video-Browser. Die neue API nennt nur das
    /// Aufnahmeverzeichnis (Einstellung <c>network_nfs_recordingdir</c>); die
    /// Verzeichnisse des Movie-Browsers stehen in keiner Route der neuen API und
    /// werden daher über die alte Datei-Schnittstelle (/control/getdir) desselben
    /// Webservers erfragt.
    /// </summary>
    public override async Task<List<string>> GetVideoRootsAsync(string dirToken, CancellationToken cancellationToken = default)
    {
        List<string> result = new();
        bool wantsRecording = dirToken is "recordingdir" or "allmoviedirs" or "";
        bool wantsMovies = dirToken is "moviedir" or "allmoviedirs";

        if (wantsRecording)
        {
            string? recording = await GetSettingAsync("recording", "network_nfs_recordingdir", cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(recording) && !result.Contains(recording))
            {
                result.Add(recording);
            }
        }

        if (wantsMovies)
        {
            string? xml = await RequestTextAsync(
                HttpMethod.Get,
                "/control/getdir?dir=" + Uri.EscapeDataString(dirToken) + "&format=xml",
                null,
                withSession: false,
                cancellationToken).ConfigureAwait(false);

            if (xml is not null)
            {
                foreach (string dir in LegacyControlApi.ParseDirectoriesXml(xml))
                {
                    if (!result.Contains(dir))
                    {
                        result.Add(dir);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Liest eine Einstellung aus /api/v1/settings/{section}.</summary>
    private async Task<string?> GetSettingAsync(string section, string key, CancellationToken cancellationToken)
    {
        string? text = await RequestTextAsync(
            HttpMethod.Get,
            Prefix + "/settings/" + Uri.EscapeDataString(section),
            null,
            withSession: false,
            cancellationToken).ConfigureAwait(false);

        return text is null ? null : ParseSetting(text, key);
    }

    /// <summary>Wert einer Einstellung aus {"items":[{"id":"...","value":"..."}]}.</summary>
    public static string? ParseSetting(string json, string key)
    {
        using JsonDocument? document = TryParseJson(json);
        if (document is null)
        {
            return null;
        }

        JsonElement items = JsonArray(document.RootElement, "items");
        if (items.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement item in items.EnumerateArray())
        {
            if (string.Equals(JsonText(item, "id"), key, StringComparison.OrdinalIgnoreCase))
            {
                return JsonText(item, "value");
            }
        }

        return null;
    }

    /// <summary>
    /// Verzeichnisinhalt (GET /api/v1/storage/files). Die neue API verlangt dafür
    /// Systemrechte, deshalb wird eine Sitzung angemeldet.
    /// </summary>
    public override async Task<List<FileEntry>> GetFileListAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        string? text = await RequestTextAsync(
            HttpMethod.Get,
            Prefix + "/storage/files?path=" + Uri.EscapeDataString(absolutePath),
            null,
            withSession: true,
            cancellationToken).ConfigureAwait(false);

        return text is null ? new List<FileEntry>() : ParseFiles(text, absolutePath);
    }

    /// <summary>Wertet eine Verzeichnisliste der neuen API aus ({"items":[{"id":"...","kind":"dir"}]}).</summary>
    public static List<FileEntry> ParseFiles(string json, string parentPath)
    {
        List<FileEntry> result = new();
        using JsonDocument? document = TryParseJson(json);
        if (document is null)
        {
            return result;
        }

        JsonElement items = JsonArray(document.RootElement, "items");
        if (items.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        string parent = (parentPath ?? string.Empty).TrimEnd('/');
        foreach (JsonElement item in items.EnumerateArray())
        {
            string name = JsonText(item, "id");
            if (name.Length == 0 || name == "." || name == "..")
            {
                continue;
            }

            result.Add(new FileEntry
            {
                Name = name,
                FullPath = parent.Length == 0 ? "/" + name : parent + "/" + name,
                IsDirectory = string.Equals(JsonText(item, "kind"), "dir", StringComparison.OrdinalIgnoreCase),
                Size = JsonLong(item, "size"),
            });
        }

        return result;
    }

    /// <summary>
    /// Videos eines Ordners. Die neue API kennt keine Rekursion, deshalb läuft die
    /// Suche hier selbst über die Unterordner (mit Obergrenzen gegen Endlosschleifen).
    /// </summary>
    public override async Task<List<FileEntry>> GetMoviesAsync(string absolutePath, bool recursive, CancellationToken cancellationToken = default)
    {
        List<FileEntry> result = new();
        Queue<string> pending = new();
        pending.Enqueue(absolutePath);

        int listed = 0;
        while (pending.Count > 0 && listed < MaxRecursiveDirs)
        {
            string path = pending.Dequeue();
            listed++;

            List<FileEntry> entries = await GetFileListAsync(path, cancellationToken).ConfigureAwait(false);
            foreach (FileEntry entry in entries)
            {
                if (entry.IsDirectory)
                {
                    if (recursive)
                    {
                        pending.Enqueue(entry.FullPath);
                    }

                    continue;
                }

                result.Add(entry);
                if (result.Count >= MaxRecursiveEntries)
                {
                    return result;
                }
            }

            if (!recursive)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Adresse einer Datei ohne Token. Die neue Schnittstelle gibt Dateien nur über
    /// /api/v1/storage/file heraus; für einen Player gehört ein Token dazu
    /// (siehe <see cref="GetFileUrlAsync"/>).
    /// </summary>
    public override string BuildHttpFileUrl(string absolutePath)
        => WebBaseUrl + Prefix + "/storage/file?path=" + Uri.EscapeDataString(absolutePath ?? string.Empty);

    /// <summary>
    /// Adresse einer Datei für VLC, mit dem kurzlebigen Medien-Token der Box. Der
    /// Token wird über POST /api/v1/token/media geholt (Systemrechte nötig) und
    /// zwischengespeichert, bis er abläuft.
    /// </summary>
    public override async Task<string> GetFileUrlAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        string url = BuildHttpFileUrl(absolutePath);
        string token = await GetMediaTokenAsync(cancellationToken).ConfigureAwait(false);
        return token.Length == 0 ? url : url + "&token=" + Uri.EscapeDataString(token);
    }

    private async Task<string> GetMediaTokenAsync(CancellationToken cancellationToken)
    {
        if (_mediaToken.Length > 0 && DateTimeOffset.UtcNow < _mediaTokenExpiry)
        {
            return _mediaToken;
        }

        await _tokenGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_mediaToken.Length > 0 && DateTimeOffset.UtcNow < _mediaTokenExpiry)
            {
                return _mediaToken;
            }

            using JsonDocument? document = await RequestJsonAsync(
                HttpMethod.Post, Prefix + "/token/media", null, withSession: true, cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                return string.Empty;
            }

            long expires = JsonLong(document.RootElement, "expires_in");
            _mediaToken = JsonText(document.RootElement, "token");
            _mediaTokenExpiry = expires > 0
                ? DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, expires - 30))
                : DateTimeOffset.UtcNow.AddMinutes(5);

            return _mediaToken;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    // ---------------------------------------------------------------------
    // Umschalten und Streaming
    // ---------------------------------------------------------------------

    /// <summary>
    /// Umschalten auf einen Sender (POST /api/v1/zap), ohne die Box aus dem Standby
    /// zu wecken: <c>wake</c> bleibt false. Der Zap dient hier nur dem Streamen –
    /// die Adresse liefert <c>GET /api/v1/stream/{id}</c> – und darf eine Box im
    /// Standby nicht einschalten. Eine Box im Standby verweigert den Zap deshalb
    /// mit 409 (BoxInStandby) und bleibt im Standby; der Stream über die Adresse
    /// funktioniert trotzdem, weil der Tuner erst beim Öffnen der Adresse belegt wird.
    /// </summary>
    public override async Task ZapToAsync(string channelId, CancellationToken cancellationToken = default)
    {
        string body = BuildZapBody(channelId);
        await RequestTextAsync(HttpMethod.Post, Prefix + "/zap", body, withSession: true, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Body von <c>POST /api/v1/zap</c>: <c>wake</c> ist immer false.</summary>
    public static string BuildZapBody(string channelId)
        => "{\"channel_id\":" + JsonSerializer.Serialize(channelId) + ",\"wake\":false}";

    /// <summary>Verbindungstest anstelle von /control/zapto (das die neue API nicht kennt).</summary>
    public override async Task<string?> ZapToProbeAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument? document = await RequestJsonAsync(
            HttpMethod.Get, Prefix + "/system/info", null, withSession: false, cancellationToken).ConfigureAwait(false);

        return document is null ? null : "ok";
    }

    /// <summary>Live-Adresse eines Senders, wie die Box sie nennt (GET /api/v1/stream/{id}).</summary>
    public override async Task<string?> GetStreamUrlAsync(string channelId, CancellationToken cancellationToken = default)
    {
        using JsonDocument? document = await RequestJsonAsync(
            HttpMethod.Get,
            Prefix + "/stream/" + Uri.EscapeDataString(channelId),
            null,
            withSession: false,
            cancellationToken).ConfigureAwait(false);

        if (document is null)
        {
            return null;
        }

        string url = JsonText(document.RootElement, "url");
        return url.Length == 0 ? null : url;
    }

    /// <summary>VLC-Stream-Adresse eines Senders.</summary>
    public override string BuildVlcStreamUrl(string channelId)
        => StreamBaseUrl + "/id=" + channelId;

    /// <summary>
    /// Sender, die auf demselben Transponder liegen wie das, was die Box gerade
    /// empfängt (laufender Sender und laufende Aufnahmen). Die neue API kennt kein
    /// <c>/control/currenttpchannels</c>; die Zuordnung kommt aus der Senderliste.
    /// </summary>
    public override async Task<string?> GetCurrentTpChannelsAsync(CancellationToken cancellationToken = default)
    {
        List<Channel> channels = await GetChannelCacheAsync(cancellationToken).ConfigureAwait(false);
        if (channels.Count == 0 || _transponders.Count == 0)
        {
            return null;
        }

        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        NoteTransponder(await GetCurrentChannelIdAsync(cancellationToken).ConfigureAwait(false), keys);

        foreach (RunningRecording recording in await GetRunningRecordingsAsync(cancellationToken).ConfigureAwait(false))
        {
            NoteTransponder(recording.ChannelId, keys);
        }

        if (keys.Count == 0)
        {
            return null;
        }

        StringBuilder list = new();
        foreach (Channel channel in channels)
        {
            if (_transponders.TryGetValue(NormalizeChannelId(channel.Id), out string? key) && keys.Contains(key))
            {
                list.Append(channel.Id).Append(' ').Append(channel.Name).Append('\n');
            }
        }

        return list.ToString();
    }

    private void NoteTransponder(string? channelId, HashSet<string> keys)
    {
        string key = NormalizeChannelId(channelId);
        if (key.Length > 0 && _transponders.TryGetValue(key, out string? transponder) && transponder.Length > 0)
        {
            keys.Add(transponder);
        }
    }

    private async Task<string?> GetCurrentChannelIdAsync(CancellationToken cancellationToken)
    {
        using JsonDocument? document = await RequestJsonAsync(
            HttpMethod.Get, Prefix + "/channels/current", null, withSession: false, cancellationToken).ConfigureAwait(false);

        return document is null ? null : JsonText(document.RootElement, "id");
    }

    private static string LocalTimeText(long unixSeconds)
        => unixSeconds <= 0
            ? string.Empty
            : DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
}