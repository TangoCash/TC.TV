using System.IO;

namespace TCTV.Core;

/// <summary>
/// Ein Empfängerprofil. Entspricht der Profil-INI im Arbeitsverzeichnis
/// (Abschnitt "Profildaten" sowie "VLC Fenster"/Multituner).
/// </summary>
public sealed class Profile
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Host/DynDNS-Adresse der Box (Schluessel "IP der Box" bzw. "DynDNS").</summary>
    public string Host { get; set; } = string.Empty;

    public string WebPort { get; set; } = "80";

    public string StreamingPort { get; set; } = "31339";

    public bool WebAuthentication { get; set; }

    public string WebUser { get; set; } = "root";

    public string WebPassword { get; set; } = "nii";

    public bool ReciProxy { get; set; }

    /// <summary>"Extern" - Verbindung ueber DynDNS statt lokaler IP.</summary>
    public bool External { get; set; }

    /// <summary>"Umschalten erzwingen".</summary>
    public bool ForceSwitch { get; set; }

    /// <summary>"Favoriten" - nur Favoriten (ubouquets.xml) anzeigen.</summary>
    public bool FavoritesOnly { get; set; }

    /// <summary>"VLC Fenster"/"Multituner".</summary>
    public bool MultiTuner { get; set; }

    /// <summary>ReciProxy-Startverzoegerung (ms), wie im Original mit "000" ergaenzt.</summary>
    public string ReciProxyDelay { get; set; } = "0";

    public static Profile Load(string iniPath, string name)
    {
        IniFile ini = new(iniPath);
        return Load(ini, name);
    }

    public static Profile Load(IniFile ini, string name)
    {
        Profile profile = new() { Name = name };

        profile.External = ini.ReadInt("Profildaten", "Extern") == 1;
        profile.ReciProxy = ini.ReadInt("Profildaten", "ReciProxy") == 1;
        profile.ForceSwitch = ini.ReadInt("Profildaten", "Umschalten erzwingen") == 1;
        profile.FavoritesOnly = ini.ReadInt("Profildaten", "Favoriten") == 1;
        profile.MultiTuner = ini.ReadInt("VLC Fenster", "Multituner") == 1;

        // Extern => DynDNS, sonst die lokale IP.
        profile.Host = profile.External
            ? FirstNonEmpty(ini.Read("Profildaten", "DynDNS"), ini.Read("Profildaten", "IP der Box"))
            : FirstNonEmpty(ini.Read("Profildaten", "IP der Box"), ini.Read("Profildaten", "DynDNS"));

        profile.WebPort = Default(ini.Read("Profildaten", "Webserver Port"), "80");
        profile.StreamingPort = Default(ini.Read("Profildaten", "Streaming Port"), "31339");
        profile.WebAuthentication = ini.ReadInt("Profildaten", "Web-Authentifikation") == 1;
        profile.WebUser = Default(ini.Read("Profildaten", "Webserver Nutzer"), "root");
        profile.WebPassword = Default(ini.Read("Profildaten", "Webserver Passwort"), "coolstream");
        profile.ReciProxyDelay = Default(ini.Read("ReciProxy", "Startverzögerung"), "0");

        return profile;
    }

    public void Save(string iniPath)
    {
        IniFile ini = new(iniPath);
        Save(ini);
        ini.Save();
    }

    public void Save(IniFile ini)
    {
        ini.Write("Profildaten", "Extern", External ? "1" : "0");
        if (External)
        {
            ini.Write("Profildaten", "DynDNS", Host);
        }
        else
        {
            ini.Write("Profildaten", "IP der Box", Host);
        }

        ini.Write("Profildaten", "Webserver Port", WebPort);
        ini.Write("Profildaten", "Streaming Port", StreamingPort);
        ini.Write("Profildaten", "Web-Authentifikation", WebAuthentication ? "1" : "0");
        ini.Write("Profildaten", "Webserver Nutzer", WebUser);
        ini.Write("Profildaten", "Webserver Passwort", WebPassword);
        ini.Write("Profildaten", "ReciProxy", ReciProxy ? "1" : "0");
        ini.Write("Profildaten", "Umschalten erzwingen", ForceSwitch ? "1" : "0");
        if (FavoritesOnly)
        {
            ini.Write("Profildaten", "Favoriten", "1");
        }
        else
        {
            ini.DeleteKey("Profildaten", "Favoriten");
        }

        ini.Write("VLC Fenster", "Multituner", MultiTuner ? "1" : "0");
        ini.Write("ReciProxy", "Startverzögerung", ReciProxyDelay);
    }

    private static string FirstNonEmpty(string a, string b) => !string.IsNullOrEmpty(a) ? a : b;

    private static string Default(string value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value;

    /// <summary>Vollstaendiger Dateiname der Profil-INI (Name wird ggf. um ".ini" ergaenzt).</summary>
    public string IniFileName =>
        Name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) ? Name : Name + ".ini";
}
