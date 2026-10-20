using System.IO;
using System.Reflection;

namespace TCTV.Core;

/// <summary>
/// Zugriff auf die Sprachdateien (.lang). Jede Zeile hat die Form
/// "schluessel=wert" (z. B. "settings=Einstellungen"). "\n" in einem Wert steht
/// für einen Zeilenumbruch im Text.
///
/// Sprachen werden als &lt;name&gt;.lang abgelegt (z. B. "deutsch.lang",
/// "english.lang", "francais.lang"). Eingebettet sind deutsch und english;
/// weitere Sprachen lassen sich einfach als Datei in den Lang-Ordner der
/// Datenablage legen, ohne das Programm neu zu bauen.
/// </summary>
public static class Localization
{
    private const string ResourcePrefix = "TCTV.Languages.";

    private static Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Name der aktiven Sprache (z. B. "deutsch", "english", "francais").</summary>
    public static string CurrentLanguage { get; private set; } = "deutsch";

    /// <summary>Alle gefundenen Sprachen (eingebettet und Lang-Ordner).</summary>
    public static IReadOnlyList<string> AvailableLanguages { get; private set; } = new List<string> { "deutsch", "english" };

    /// <summary>Liefert den Text zum Schlüssel <paramref name="key"/> in der aktiven Sprache.</summary>
    public static string T(string key)
        => _strings.TryGetValue(key, out string? value) ? value : key;

    public static bool IsEnglish => string.Equals(CurrentLanguage, "english", StringComparison.OrdinalIgnoreCase);

    /// <summary>Anzeigename einer Sprache: optional "#name=…" als erste Zeile, sonst Name mit großem Anfangsbuchstaben.</summary>
    public static string DisplayName(string language)
    {
        string name = Normalize(language);
        foreach (string line in ReadRawLines(name))
        {
            if (line.StartsWith("#name=", StringComparison.OrdinalIgnoreCase))
            {
                string display = line["#name=".Length..].Trim();
                if (display.Length > 0)
                {
                    return display;
                }
            }
        }

        return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>Wechselt die Sprache. Unbekannte Namen fallen auf "deutsch" zurück.</summary>
    public static void SetLanguage(string? name)
    {
        string language = Normalize(name);
        AvailableLanguages = DiscoverLanguages();

        if (!AvailableLanguages.Contains(language))
        {
            language = "deutsch";
        }

        _strings = Load(language);
        CurrentLanguage = language;
    }

    private static string Normalize(string? name)
    {
        string value = (name ?? string.Empty).Trim().ToLowerInvariant();
        return value.Length == 0 ? "deutsch" : value;
    }

    private static IReadOnlyList<string> DiscoverLanguages()
    {
        SortedSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        foreach (string resource in Assembly.GetExecutingAssembly().GetManifestResourceNames())
        {
            if (TryLanguageName(resource, out string name))
            {
                names.Add(name);
            }
        }

        if (Directory.Exists(AppPaths.LangDir))
        {
            foreach (string file in Directory.GetFiles(AppPaths.LangDir, "*.lang"))
            {
                names.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        return names.ToList();
    }

    private static bool TryLanguageName(string resourceName, out string name)
    {
        name = string.Empty;
        if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string rest = resourceName[ResourcePrefix.Length..];
        if (!rest.EndsWith(".lang", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        name = rest[..^".lang".Length];
        return name.Length > 0;
    }

    private static Dictionary<string, string> Load(string language)
        => Parse(ReadRawLines(language));

    /// <summary>Rohzeilen einer Sprache: Datei im Lang-Ordner, sonst eingebettete Ressource.</summary>
    private static string[] ReadRawLines(string language)
    {
        string diskFile = Path.Combine(AppPaths.LangDir, language + ".lang");
        if (File.Exists(diskFile))
        {
            return File.ReadAllLines(diskFile);
        }

        Stream? stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(ResourcePrefix + language + ".lang");
        if (stream is null)
        {
            return Array.Empty<string>();
        }

        using StreamReader reader = new(stream);
        return reader.ReadToEnd().Split('\n');
    }

    private static Dictionary<string, string> Parse(string[] rawLines)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in rawLines)
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("#"))
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..];
            if (key.Length == 0)
            {
                continue;
            }

            result[key] = value.Replace("\\n", "\n");
        }

        return result;
    }
}
