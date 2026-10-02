using System.IO;

namespace TCTV.Core;

/// <summary>
/// Verwaltet die Empfängerprofile. Das Original speichert sie als Dateien
/// "&lt;Name&gt;.conf3" im Arbeitsverzeichnis; der aktive Profilname steht in
/// TC.TV.ini unter "Benutzerdaten"/"Profil".
/// </summary>
public sealed class ProfileStore
{
    public const string Extension = ".conf3";

    public ProfileStore(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    /// <summary>Namen (ohne Endung) aller vorhandenen Profile, alphabetisch sortiert.</summary>
    public List<string> GetProfileNames()
    {
        List<string> names = new();
        if (!System.IO.Directory.Exists(Directory))
        {
            return names;
        }

        foreach (string file in System.IO.Directory.GetFiles(Directory, "*" + Extension))
        {
            names.Add(Path.GetFileNameWithoutExtension(file));
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public string GetPath(string name) => Path.Combine(Directory, name + Extension);

    public Profile Load(string name) => Profile.Load(GetPath(name), name);

    public void Save(Profile profile)
    {
        profile.Save(GetPath(profile.Name));
    }

    public void Delete(string name)
    {
        string path = GetPath(name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Erzeugt ein neues Profil mit den Standardwerten des Originals.</summary>
    public Profile Create(string name)
    {
        Profile profile = new()
        {
            Name = name,
            WebPort = "80",
            StreamingPort = "31339",
            WebUser = "root",
            WebPassword = "coolstream",
        };

        profile.Save(GetPath(name));
        return profile;
    }

    /// <summary>Schlägt einen freien Profilnamen nach dem Muster "Box100", "Box99" ... vor.</summary>
    public string SuggestName()
    {
        for (int suffix = 100; suffix > 0; suffix--)
        {
            string candidate = "Box" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!File.Exists(GetPath(candidate)))
            {
                return candidate;
            }
        }

        return "Box" + Guid.NewGuid().ToString("N")[..6];
    }

    /// <summary>
    /// Stellt sicher, dass ein Profil vorhanden und in der Ini ausgewählt ist.
    /// Liefert den Namen des aktiven Profils (ohne Endung).
    /// </summary>
    public string EnsureCurrent(IniFile ini, AppSettings settings)
    {
        string configured = (settings.ProfileFileName ?? string.Empty).Trim();
        if (configured.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            configured = configured[..^Extension.Length];
        }

        if (configured.Length > 0 && File.Exists(GetPath(configured)))
        {
            return configured;
        }

        List<string> existing = GetProfileNames();
        if (existing.Count > 0)
        {
            settings.ProfileFileName = existing[0] + Extension;
            ini.Write("Benutzerdaten", "Profil", settings.ProfileFileName);
            return existing[0];
        }

        string name = SuggestName();
        Create(name);
        settings.ProfileFileName = name + Extension;
        ini.Write("Benutzerdaten", "Profil", settings.ProfileFileName);
        return name;
    }
}
