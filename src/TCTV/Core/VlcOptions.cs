namespace TCTV.Core;

/// <summary>
/// VLC-Fensteroptionen. Die Optionstexte entsprechen genau den vom Original
/// zusammengesetzten Zeichenketten $l0 .. $l9 (Abschnitt "VLC Fenster").
/// </summary>
public sealed class VlcOptions
{
    /// <summary>"Vordergrund" -&gt; $l0.</summary>
    public bool VideoOnTop { get; set; }

    /// <summary>"Hinweis" -&gt; $l1.</summary>
    public bool SuppressNotification { get; set; } = true;

    /// <summary>"Fenstergröße_fest" -&gt; $l2.</summary>
    public bool FixedWindowSize { get; set; } = true;

    /// <summary>"Minimiert" -&gt; $l3.</summary>
    public bool StartMinimized { get; set; } = true;

    /// <summary>"Videotitelentfernung" -&gt; $l4.</summary>
    public bool NoVideoTitleShow { get; set; } = true;

    /// <summary>"Minimalansicht" -&gt; $l5.</summary>
    public bool MinimalView { get; set; } = true;

    /// <summary>"Deinterlace" -&gt; $l6.</summary>
    public bool Deinterlace { get; set; } = true;

    /// <summary>"Vollbild" -&gt; $l7.</summary>
    public bool FullScreen { get; set; }

    /// <summary>"086i" - unterdrückt $l8/$l9 (aeltere VLC-Versionen).</summary>
    public bool LegacyVlc { get; set; }

    /// <summary>"VLC erhalten beim Zap" - VLC bleibt bei Senderwechsel erhalten.</summary>
    public bool KeepVlcOnZap { get; set; } = true;

    /// <summary>"Beenden" - VLC wird mit TC.TV beendet.</summary>
    public bool CloseVlcWithApp { get; set; }

    /// <summary>Setzt die Optionen aus der Ini (Abschnitt "VLC Fenster").</summary>
    public static VlcOptions FromIni(IniFile ini)
    {
        return new VlcOptions
        {
            VideoOnTop = ini.ReadInt("VLC Fenster", "Vordergrund") == 1,
            SuppressNotification = ReadDefaultOn(ini, "Hinweis"),
            FixedWindowSize = ReadDefaultOn(ini, "Fenstergröße_fest"),
            StartMinimized = ReadDefaultOn(ini, "Minimiert"),
            NoVideoTitleShow = ReadDefaultOn(ini, "Videotitelentfernung"),
            MinimalView = ReadDefaultOn(ini, "Minimalansicht"),
            Deinterlace = ReadDefaultOn(ini, "Deinterlace"),
            FullScreen = ini.ReadInt("VLC Fenster", "Vollbild") == 1,
            LegacyVlc = ini.ReadInt("VLC Fenster", "086i") == 1,
            KeepVlcOnZap = ReadDefaultOn(ini, "VLC erhalten beim Zap"),
            CloseVlcWithApp = ini.ReadInt("VLC Fenster", "Beenden") == 1,
        };
    }

    public void SaveTo(IniFile ini)
    {
        ini.Write("VLC Fenster", "Vordergrund", VideoOnTop ? "1" : "0");
        WriteDefaultOn(ini, "Hinweis", SuppressNotification);
        WriteDefaultOn(ini, "Fenstergröße_fest", FixedWindowSize);
        WriteDefaultOn(ini, "Minimiert", StartMinimized);
        WriteDefaultOn(ini, "Videotitelentfernung", NoVideoTitleShow);
        WriteDefaultOn(ini, "Minimalansicht", MinimalView);
        WriteDefaultOn(ini, "Deinterlace", Deinterlace);
        ini.Write("VLC Fenster", "Vollbild", FullScreen ? "1" : "0");
        ini.Write("VLC Fenster", "086i", LegacyVlc ? "1" : "0");
        WriteDefaultOn(ini, "VLC erhalten beim Zap", KeepVlcOnZap);
        ini.Write("VLC Fenster", "Beenden", CloseVlcWithApp ? "1" : "0");
    }

    /// <summary>Optionen, die standardmaessig aktiv sind, wenn der Schluessel fehlt.</summary>
    private static bool ReadDefaultOn(IniFile ini, string key)
    {
        string value = ini.Read("VLC Fenster", key, string.Empty);
        return value.Length == 0 || value == "1";
    }

    private static void WriteDefaultOn(IniFile ini, string key, bool value) => ini.Write("VLC Fenster", key, value ? "1" : "0");

    /// <summary>
    /// Liefert die VLC-Kommandozeilenoptionen in der Reihenfolge des Originals
    /// ($l0 $l1 $l2 $l3 $l4 $l5 $l8 $l6 $l7 $l9). Bei Dateiwiedergabe wird $l9
    /// ausgelassen, weil dort ein explizites "--one-instance" angehaengt wird.
    /// </summary>
    public string BuildOptionString(bool includeOneInstance = true)
    {
        string result = string.Empty;
        result += VideoOnTop ? " --video-on-top" : string.Empty;
        result += SuppressNotification ? " --qt-notification=0" : string.Empty;
        result += FixedWindowSize ? " --no-qt-video-autoresize" : string.Empty;
        result += StartMinimized ? " --qt-start-minimized" : string.Empty;
        result += NoVideoTitleShow ? " --no-video-title-show" : string.Empty;
        result += MinimalView ? " --qt-minimal-view" : string.Empty;
        result += LegacyVlc ? string.Empty : " --no-qt-name-in-title";
        result += Deinterlace ? " --deinterlace=-1" : string.Empty;
        result += FullScreen ? " --fullscreen" : string.Empty;
        if (includeOneInstance && !LegacyVlc)
        {
            result += " --one-instance";
        }

        return result;
    }
}
