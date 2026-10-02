using TCTV.Generated;

namespace TCTV.Core;

public enum Language
{
    Deutsch = 0,
    English = 1,
}

/// <summary>Zugriff auf die Sprach-Tabelle ($ei) des Originalprogramms.</summary>
public static class Localization
{
    public static Language Current { get; set; } = Language.Deutsch;

    /// <summary>Liefert den Text der Zeile <paramref name="index"/> in der aktuellen Sprache.</summary>
    public static string T(int index)
    {
        if (index < 0 || index >= LocalizationTable.Count)
        {
            return string.Empty;
        }

        string value = LocalizationTable.Strings[index, (int)Current];
        return value ?? string.Empty;
    }

    public static string LanguageFolder => Current == Language.English ? "english" : "deutsch";

    public static Language Parse(string? name)
        => string.Equals(name, "english", StringComparison.OrdinalIgnoreCase) ? Language.English : Language.Deutsch;
}
