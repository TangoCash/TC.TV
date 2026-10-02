using System.Text;

namespace TCTV.Core;

/// <summary>
/// Textdekodierung für Daten der Box und lokale Konfigurationsdateien.
/// </summary>
public static class TextEncoding
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Dekodiert Bytes als UTF-8; ist der Inhalt kein gültiges UTF-8, wird
    /// ersatzweise Latin-1 verwendet.
    ///
    /// Hintergrund: die Neutrino-API gibt XML/JSON immer als UTF-8 aus
    /// (yhook.cpp: outValue -&gt; convert_UTF8_To_UTF8_XML, Content-Type
    /// "charset=UTF-8"). Wird ein UTF-8-Text als Latin-1 gelesen, entstehen
    /// Zeichensalat wie "MÃ¤rz" statt "März". Umgekehrt schreiben ältere
    /// AutoIt-Fassungen ihre INI in ANSI, daher der Latin-1-Rückfall.
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>Dekodiert den Inhalt einer Textdatei und liefert die Zeilen.</summary>
    public static string[] ReadAllLinesOf(string path)
    {
        return Decode(File.ReadAllBytes(path))
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');
    }
}
