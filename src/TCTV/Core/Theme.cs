using System.Windows.Media;

namespace TCTV.Core;

/// <summary>
/// Farbtabellen. Zeilen 0 und 1 stammen aus der Original-Tabelle $lf[3][11];
/// Zeile 2 ist an das Erscheinungsbild von neutrino-images.de angelehnt
/// (Material-Grautöne #212121/#424242/#616161 mit Akzent #FD6D13).
/// </summary>
public sealed class Theme
{
    /// <summary>Reihen der Farbtabelle $lf[3][11].</summary>
    private static readonly string[][] Rows =
    {
        // 0: hell (Original $lf[0])
        new[] { "0xE6E6E6", "0xE6E6E6", "0x008028", "0x000000", "0xD8D8D8", "0x000000", "", "", "0xFF0000", "0x3CB102", "0x070090" },
        // 1: dunkelgrün (Original $lf[1])
        new[] { "0x2F2F2F", "0x1B1B1B", "0xB7EA40", "0x999999", "0xFFFFFF", "0x999999", "0x6C6C6C", "0xB7EA40", "0xE33331", "0x02B13C", "0xFFFFFF" },
        // 2: dunkelorange - Farben von neutrino-images.de (custom.css des Forums)
        new[] { "0x212121", "0x424242", "0xFD6D13", "0x9E9E9E", "0xE0E0E0", "0x757575", "0x616161", "0xFFEE58", "0xF44336", "0x02B13C", "0xE0E0E0" },
    };

    private readonly Color[] _colors = new Color[11];

    private Theme(int rowIndex, string iniValue, byte opacity, bool darkShell)
    {
        RowIndex = rowIndex;
        IniValue = iniValue;
        Opacity = opacity;
        DarkShell = darkShell;
        string[] row = Rows[rowIndex];
        for (int i = 0; i < _colors.Length; i++)
        {
            _colors[i] = Parse(row[i], i);
        }
    }

    /// <summary>Zeile der Original-Tabelle $lf (0..2).</summary>
    public int RowIndex { get; }

    /// <summary>Wert, wie er in der INI unter Benutzerdaten\Theme steht ("1", "2", "3" oder "4").</summary>
    public string IniValue { get; }

    /// <summary>Deckkraft des Fensters wie bei WinSetTrans (0..255).</summary>
    public byte Opacity { get; }

    /// <summary>
    /// Theme "1" verwendet im Original einen fast transparenten dunklen Rahmen
    /// (GUISetBkColor 0x252525, WinSetTrans 50).
    /// </summary>
    public bool DarkShell { get; }

    /// <summary>Fensterhintergrund ($fh) - bzw. 0x252525 beim Dunkel-Overlay.</summary>
    public Color Background => DarkShell ? Color.FromRgb(0x25, 0x25, 0x25) : _colors[0];

    public Color Color1 => _colors[1];
    public Color Color2 => _colors[2];

    /// <summary>Textfarbe ($fk).</summary>
    public Color Text => _colors[3];

    public Color Color4 => _colors[4];
    public Color Color5 => _colors[5];
    public Color Color6 => _colors[6];
    public Color Color7 => _colors[7];
    public Color Color8 => _colors[8];
    public Color Color9 => _colors[9];
    public Color Color10 => _colors[10];

    /// <summary>Fensterdeckkraft als Faktor (WinSetTrans-Wert / 255).</summary>
    public double OpacityFactor => Opacity / 255.0;

    public static Theme Default => FromIniValue(string.Empty);

    /// <summary>
    /// Bildet den INI-Wert auf Farbtabelle, Deckkraft und Sonderfall
    /// "Dunkel-Overlay" ab.
    ///
    ///   "3" (oder leer/unbekannt) = Dark orange - Standardthema (neutrino-images)
    ///   "2"                       = Dark grün
    ///   "4"                       = Hell
    ///   "1"                       = TC.TV-Overlay (hell, dunkler transparenter Rahmen)
    /// </summary>
    public static Theme FromIniValue(string? iniValue)
    {
        switch ((iniValue ?? string.Empty).Trim())
        {
            case "1":
                return new Theme(0, "1", 50, true);
            case "2":
                return new Theme(1, "2", 200, false);
            case "4":
                return new Theme(0, "4", 200, false);
            default:
                // Dark orange ist das Standarddesign (auch bei leerem Schlüssel,
                // also bei einer noch vom Original stammenden INI).
                return new Theme(2, "3", 200, false);
        }
    }

    public Brush BackgroundBrush => new SolidColorBrush(Background);

    public Brush TextBrush => new SolidColorBrush(Text);

    private static Color Parse(string value, int index)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            // Leere Felder der Originaltabelle sinnvoll belegen.
            return index switch
            {
                6 => Color.FromRgb(0x6C, 0x6C, 0x6C),
                7 => Color.FromRgb(0xB7, 0xEA, 0x40),
                _ => Colors.Black,
            };
        }

        string hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
        {
            return Color.FromRgb((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
        }

        return Colors.Black;
    }
}
