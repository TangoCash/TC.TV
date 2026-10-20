namespace TCTV.Core;

/// <summary>
/// Welche HTTP-Schnittstelle der Box angesprochen wird. Im Profil wählbar
/// (<c>Profildaten\API-Version</c>) und ohne Angabe die alte API, damit sich
/// bestehende Profile unverändert verhalten.
/// </summary>
public enum BoxApi
{
    /// <summary>
    /// Alte Schnittstelle des Recording Service: <c>/control/...</c> mit Text-
    /// und XML-Antworten (das Originalprogramm nutzt nur diese).
    /// </summary>
    Legacy = 0,

    /// <summary>
    /// Neue REST-Schnittstelle des Images: <c>/api/v1/...</c> mit JSON-Antworten.
    /// Umgesetzt wie im Quelltext von <c>nick neutrino</c> (ni-neutrino,
    /// <c>src/httpd/ep/*.cpp</c>).
    /// </summary>
    V1 = 1,
}

/// <summary>Umrechnung zwischen <see cref="BoxApi"/> und dem Ini-Wert des Profils.</summary>
public static class BoxApis
{
    /// <summary>Ini-Wert der alten API (Standard).</summary>
    public const string IniLegacy = "alt";

    /// <summary>Ini-Wert der neuen API.</summary>
    public const string IniV1 = "neu";

    /// <summary>
    /// Liest den Ini-Wert. Alles, was nicht eindeutig "neu" ist, gilt als alte API –
    /// so bleiben Profile aus früheren Programmversionen (ohne den Schlüssel) gültig.
    /// </summary>
    public static BoxApi Parse(string? value)
    {
        string text = (value ?? string.Empty).Trim();
        if (text.Equals(IniV1, StringComparison.OrdinalIgnoreCase)
            || text.Equals("v1", StringComparison.OrdinalIgnoreCase)
            || text.Equals("new", StringComparison.OrdinalIgnoreCase)
            || text.Equals("2", StringComparison.OrdinalIgnoreCase))
        {
            return BoxApi.V1;
        }

        return BoxApi.Legacy;
    }

    public static string ToIniValue(BoxApi api) => api == BoxApi.V1 ? IniV1 : IniLegacy;

    /// <summary>Bezeichnung für die Oberfläche (wie im Profilfenster).</summary>
    public static string DisplayName(BoxApi api)
        => Localization.T(api == BoxApi.V1 ? "api_new" : "api_old");
}