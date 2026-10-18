namespace TCTV.Core;

/// <summary>Ein Bouquet (Sendergruppe), wie von /control/getbouquets geliefert (Zeile "NR NAME").</summary>
public sealed class Bouquet
{
    public string Number { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Anzahl Fernsehsender (nur neue API, /api/v1/bouquets; sonst 0).</summary>
    public long TvCount { get; set; }

    /// <summary>Anzahl Radiosender (nur neue API, /api/v1/bouquets; sonst 0).</summary>
    public long RadioCount { get; set; }

    public override string ToString() => Name;
}

/// <summary>EPG-Eintrag eines Senders (&lt;firstEPG&gt;/&lt;secondEPG&gt;).</summary>
public sealed class EpgEvent
{
    public string EventId { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Info1 { get; set; } = string.Empty;

    public string Info2 { get; set; } = string.Empty;

    public string StartTime { get; set; } = string.Empty;

    public string TimeTotal { get; set; } = string.Empty;

    public string TimeElapsed { get; set; } = string.Empty;

    public string Percentage { get; set; } = string.Empty;

    /// <summary>
    /// Beginn als Unix-Sekunden. Nur die neue API (/api/v1/epg/grid) nennt ihn; die
    /// alte API liefert nur Text. 0 = unbekannt.
    /// </summary>
    public long StartUnix { get; set; }

    /// <summary>
    /// Dauer in Sekunden (neue API; 0 = unbekannt).
    /// </summary>
    public long DurationSeconds { get; set; }

    public bool IsEmpty => string.IsNullOrEmpty(Description) && string.IsNullOrEmpty(EventId);

    /// <summary>Headertext wie im Original: info1 + Zeilenumbruch + info2.</summary>
    public string Header => (Info1 + Environment.NewLine + Info2).Trim();

    /// <summary>Dauer in Minuten als Zahl (0, wenn unbekannt).</summary>
    public int TotalMinutes => int.TryParse(TimeTotal, out int v) ? v : 0;

    /// <summary>Startzeit als lokale DateTime, wenn bestimmbar (sonst null).</summary>
    public DateTime? StartDateTime()
    {
        if (StartUnix > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds(StartUnix).ToLocalTime().DateTime;
        }

        return DateTime.TryParse(StartTime, out DateTime parsed) ? parsed : null;
    }

    /// <summary>Endezeit als lokale DateTime, wenn bestimmbar (sonst null).</summary>
    public DateTime? EndDateTime()
    {
        DateTime? start = StartDateTime();
        if (start is null)
        {
            return null;
        }

        if (DurationSeconds > 0)
        {
            return start.Value.AddSeconds(DurationSeconds);
        }

        if (TotalMinutes > 0)
        {
            return start.Value.AddMinutes(TotalMinutes);
        }

        return null;
    }
}

/// <summary>Ein Sender aus /control/getbouquet?format=xml (&lt;channel&gt;).</summary>
public sealed class Channel
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Wert fuer /control/zapto?&lt;id&gt;.</summary>
    public string Id { get; set; } = string.Empty;

    public string ShortId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Logo { get; set; } = string.Empty;

    public string BouquetNumber { get; set; } = string.Empty;

    public string IsActiveChannel { get; set; } = string.Empty;

    /// <summary>Art des Senders ("tv", "radio", …; nur neue API).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>true für Radiosender.</summary>
    public bool IsRadio => Kind.Equals("radio", StringComparison.OrdinalIgnoreCase)
        || Kind.Equals("webradio", StringComparison.OrdinalIgnoreCase);

    public EpgEvent FirstEpg { get; set; } = new();

    public EpgEvent SecondEpg { get; set; } = new();

    public override string ToString() => Name;
}

/// <summary>
/// Ein Verzeichnis-/Dateieintrag der Box. Die Pfade sind absolute Pfade auf der
/// Box (aus /control/file?action=list bzw. /control/getmovies) und werden
/// direkt für die HTTP-Wiedergabe verwendet.
/// </summary>
public sealed class FileEntry
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Absoluter Pfad auf der Box (Feld "fullname" bzw. "path").</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>true = Verzeichnis (type_str "dir" bzw. type 4).</summary>
    public bool IsDirectory { get; set; }

    /// <summary>Dateigroesse in Byte (0, wenn unbekannt).</summary>
    public long Size { get; set; }

    public override string ToString() => Name;
}

/// <summary>Ein Timer aus /control/timer (format=xml bzw. format=id).</summary>
public sealed class TimerItem
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Timer-Typ als Text (format=xml) bzw. Zahl (format=id).</summary>
    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string ChannelName { get; set; } = string.Empty;

    public string ChannelId { get; set; } = string.Empty;

    public string RecordingDir { get; set; } = string.Empty;

    /// <summary>Startzeit als Text (z. B. "01.01.2024 20:15").</summary>
    public string Start { get; set; } = string.Empty;

    /// <summary>Endzeit als Text.</summary>
    public string End { get; set; } = string.Empty;

    /// <summary>Startzeit als Unix-Sekunden (alarmTime).</summary>
    public long AlarmTime { get; set; }

    /// <summary>Endzeit als Unix-Sekunden (stopTime); 0 = kein Ende (z. B. Erinnerung).</summary>
    public long StopTime { get; set; }

    /// <summary>True, wenn der Timer jetzt laeuft (Start &lt;= jetzt &lt; Ende).</summary>
    public bool IsRunning(long now) => AlarmTime > 0 && StopTime > 0 && now >= AlarmTime && now < StopTime;

    public override string ToString() => Title;
}

/// <summary>Eine aktuell laufende Aufnahme (aus /control/timer?format=id).</summary>
public sealed class RunningRecording
{
    public long EventId { get; init; }

    public string ChannelId { get; init; } = string.Empty;

    public long Start { get; init; }

    public long Stop { get; init; }
}

/// <summary>EPG eines Senders für die Programmübersicht (alle Sendungen eines Bouquets).</summary>
public sealed class EpgChannel
{
    public string ChannelId { get; init; } = string.Empty;

    public string ChannelName { get; init; } = string.Empty;

    public List<EpgEvent> Events { get; init; } = new();
}

/// <summary>Vom Client benoetigte Serverdaten (Modus/Standby).</summary>
public enum BoxMode
{
    Unknown,
    Tv,
    Radio,
    Standby,
}
