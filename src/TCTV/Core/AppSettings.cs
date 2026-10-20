namespace TCTV.Core;

/// <summary>
/// Allgemeine Programmeinstellungen (Abschnitte "Benutzerdaten" und "Optionen"
/// der Datei TC.TV_Workdir\TC.TV.ini).
/// </summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "deutsch";

    /// <summary>
    /// Ini-Wert aus Benutzerdaten\Theme. "3" = Dark orange (Standarddesign),
    /// "2" = Dark grün, "4" = Hell, "1" = TC.TV-Overlay.
    /// </summary>
    public string ThemeValue { get; set; } = Theme.Default.IniValue;

    /// <summary>Dateiname des aktiven Profils, z. B. "Box100.conf3".</summary>
    public string ProfileFileName { get; set; } = string.Empty;

    /// <summary>Benutzerdaten\Bouquet – zuletzt gewähltes Bouquet (wird beim Start wiederhergestellt).</summary>
    public string LastBouquetNumber { get; set; } = string.Empty;

    public string VlcPath { get; set; } = string.Empty;

    /// <summary>Optionen\Rekursive Videoanzeige.</summary>
    public bool RecursiveVideoDisplay { get; set; } = true;

    /// <summary>Optionen\fade-in-Verzögerung in Millisekunden.</summary>
    public int FadeInDelayMs { get; set; }

    /// <summary>Optionen\fade-in-Auto-Aktualisierung.</summary>
    public bool FadeInAutoRefresh { get; set; }

    /// <summary>Optionen\fade-in-Erster Bildschirm.</summary>
    public bool FadeInFirstScreen { get; set; }

    /// <summary>Optionen\Rechter Rand (true = Fenster am rechten Rand).</summary>
    public bool RightEdge { get; set; } = true;

    /// <summary>Optionen\Traspondersender markieren.</summary>
    public bool MarkTransponderChannels { get; set; }

    /// <summary>Optionen\Videomodus.</summary>
    public bool VideoMode { get; set; }

    /// <summary>Optionen\Video-Hauptverzeichnisse.</summary>
    public bool VideoMainDirectories { get; set; }

    /// <summary>Optionen\Updateprüfung.</summary>
    public bool UpdateCheck { get; set; } = true;

    /// <summary>Benutzerdaten\erweiterte Einstellungen.</summary>
    public bool AdvancedSettings { get; set; }

    public Theme Theme => Theme.FromIniValue(ThemeValue);

    public static AppSettings Load(IniFile ini) => new()
    {
        Language = ini.Read("Benutzerdaten", "Sprache", "deutsch"),
        ThemeValue = Default(ini.Read("Benutzerdaten", "Theme", string.Empty), "3"),
        ProfileFileName = ini.Read("Benutzerdaten", "Profil", string.Empty),
        LastBouquetNumber = ini.Read("Benutzerdaten", "Bouquet", string.Empty),
        VlcPath = ini.Read("Benutzerdaten", "VLC Pfad", string.Empty),
        AdvancedSettings = ini.ReadInt("Benutzerdaten", "erweiterte Einstellungen") == 1,
        RecursiveVideoDisplay = ini.ReadInt("Optionen", "Rekursive Videoanzeige", 1) == 1,
        FadeInDelayMs = ini.ReadInt("Optionen", "fade-in-Verzögerung in Millisekunden", 0),
        FadeInAutoRefresh = ini.ReadInt("Optionen", "fade-in-Auto-Aktualisierung") == 1,
        FadeInFirstScreen = ini.ReadInt("Optionen", "fade-in-Erster Bildschirm") == 1,
        RightEdge = ini.ReadInt("Optionen", "Rechter Rand", 1) == 1,
        MarkTransponderChannels = ini.ReadInt("Optionen", "Traspondersender markieren") == 1,
        VideoMode = ini.ReadInt("Optionen", "Videomodus") == 1,
        VideoMainDirectories = ini.ReadInt("Optionen", "Video-Hauptverzeichnisse") == 1,
        UpdateCheck = ini.ReadInt("Optionen", "Updateprüfung", 1) == 1,
    };

    public void Save(IniFile ini)
    {
        ini.Write("Benutzerdaten", "Sprache", Language);
        if (string.IsNullOrEmpty(ThemeValue))
        {
            ini.DeleteKey("Benutzerdaten", "Theme");
        }
        else
        {
            ini.Write("Benutzerdaten", "Theme", ThemeValue);
        }
        if (!string.IsNullOrEmpty(ProfileFileName))
        {
            ini.Write("Benutzerdaten", "Profil", ProfileFileName);
        }

        if (!string.IsNullOrEmpty(LastBouquetNumber))
        {
            ini.Write("Benutzerdaten", "Bouquet", LastBouquetNumber);
        }

        if (AdvancedSettings)
        {
            ini.Write("Benutzerdaten", "erweiterte Einstellungen", "1");
        }

        ini.Write("Optionen", "Rekursive Videoanzeige", RecursiveVideoDisplay ? "1" : "0");
        ini.Write("Optionen", "fade-in-Verzögerung in Millisekunden", FadeInDelayMs.ToString());
        ini.Write("Optionen", "fade-in-Auto-Aktualisierung", FadeInAutoRefresh ? "1" : "0");
        ini.Write("Optionen", "fade-in-Erster Bildschirm", FadeInFirstScreen ? "1" : "0");
        ini.Write("Optionen", "Rechter Rand", RightEdge ? "1" : "-1");
        ini.Write("Optionen", "Traspondersender markieren", MarkTransponderChannels ? "1" : "0");
        ini.Write("Optionen", "Videomodus", VideoMode ? "1" : "0");
        ini.Write("Optionen", "Video-Hauptverzeichnisse", VideoMainDirectories ? "1" : "0");
        ini.Write("Optionen", "Updateprüfung", UpdateCheck ? "1" : "0");
    }

    /// <summary>
    /// Legt beim ersten Start dieselben Standardwerte an wie das Original
    /// (fade-in-*, Rechter Rand, Rekursive Videoanzeige, VLC-Fensteroptionen).
    /// </summary>
    public static void EnsureDefaults(IniFile ini)
    {
        WriteIfEmpty(ini, "Benutzerdaten", "Sprache", "deutsch");
        WriteIfEmpty(ini, "Benutzerdaten", "Theme", "3");
        WriteIfEmpty(ini, "Optionen", "fade-in-Auto-Aktualisierung", "0");
        WriteIfEmpty(ini, "Optionen", "fade-in-Erster Bildschirm", "0");
        WriteIfEmpty(ini, "Optionen", "fade-in-Verzögerung in Millisekunden", "0");
        WriteIfEmpty(ini, "Optionen", "Rechter Rand", "1");
        WriteIfEmpty(ini, "Optionen", "Rekursive Videoanzeige", "1");
        WriteIfEmpty(ini, "Optionen", "Einblende-Modus", "-1");
        WriteIfEmpty(ini, "VLC Fenster", "086i", "0");
        WriteIfEmpty(ini, "VLC Fenster", "Beenden", "0");
        WriteIfEmpty(ini, "VLC Fenster", "Deinterlace", "1");
        WriteIfEmpty(ini, "VLC Fenster", "Fenstergröße_fest", "1");
        WriteIfEmpty(ini, "VLC Fenster", "Hinweis", "1");
        WriteIfEmpty(ini, "VLC Fenster", "Minimalansicht", "1");
        WriteIfEmpty(ini, "VLC Fenster", "Minimiert", "1");
        WriteIfEmpty(ini, "VLC Fenster", "Rahmen_schmal", "1");
        WriteIfEmpty(ini, "VLC Fenster", "Videotitelentfernung", "1");
        WriteIfEmpty(ini, "VLC Fenster", "VLC erhalten beim Zap", "1");
        WriteIfEmpty(ini, "VLC Fenster", "Vollbild", "0");
        WriteIfEmpty(ini, "VLC Fenster", "Vordergrund", "0");
        WriteIfEmpty(ini, "VLC Fenster", "XML", "1");
    }

    private static void WriteIfEmpty(IniFile ini, string section, string key, string value)
    {
        if (ini.Read(section, key, string.Empty).Length == 0)
        {
            ini.Write(section, key, value);
        }
    }

    private static string Default(string value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value;
}
