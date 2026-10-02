using System.Windows.Media;

namespace TCTV.Core;

/// <summary>
/// Erzeugt aus einer Original-Farbtabelle (<see cref="Theme"/>, $lf[3][11]) ein
/// vollständiges, modernes Farbsystem (Fenster, Karten, Ränder, Hover, Akzent)
/// und legt es als <c>DynamicResource</c>-Brushes in den Anwendungsressourcen ab.
/// Dadurch folgt die gesamte Oberfläche automatisch dem gewählten Theme.
/// </summary>
public static class ThemeManager
{
    public static Theme Current { get; private set; } = Theme.Default;

    /// <summary>Setzt alle Brush-Ressourcen (Schlüssel "Brush.*").</summary>
    public static void Apply(Theme theme)
    {
        Current = theme;

        System.Windows.ResourceDictionary? resources = System.Windows.Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        foreach (KeyValuePair<string, Color> entry in Build(theme))
        {
            resources[entry.Key] = new SolidColorBrush(entry.Value);
        }
    }

    private static Dictionary<string, Color> Build(Theme theme)
    {
        bool dark = theme.DarkShell || theme.RowIndex > 0;

        Color window = theme.Background;

        // Bei dunklen Themes stammen Fläche, Rahmen und Sekundärtext direkt aus
        // der Farbtabelle – beim Standardthema "Dark orange" sind das exakt die
        // Werte von neutrino-images.de (#424242 Flächen, #616161 Rahmen,
        // #9E9E9E Sekundärtext, #FD6D13 Akzent).
        Color card = dark ? theme.Color1 : Colors.White;
        Color border = dark ? theme.Color6 : Mix(window, Colors.Black, 0.12);
        Color text = dark ? theme.Color10 : theme.Text;
        Color muted = dark ? theme.Text : Color.FromRgb(0x6B, 0x72, 0x80);
        Color accent = theme.Color2;
        Color accentText = Luminance(accent) > 0.6 ? Color.FromRgb(0x1B, 0x1B, 0x1B) : Colors.White;

        return new Dictionary<string, Color>
        {
            // Fenster/Flächen
            ["Brush.Window"] = window,
            ["Brush.Shell"] = Color.FromArgb(theme.Opacity, window.R, window.G, window.B),
            ["Brush.Card"] = card,
            ["Brush.Input"] = dark ? Mix(card, window, 0.40) : Colors.White,
            ["Brush.Chip"] = dark ? Mix(card, Colors.White, 0.12) : Mix(window, Colors.Black, 0.06),
            ["Brush.Border"] = border,

            // Text
            ["Brush.Text"] = text,
            ["Brush.Muted"] = muted,

            // Akzent
            ["Brush.Accent"] = accent,
            ["Brush.AccentText"] = accentText,
            ["Brush.AccentHover"] = Mix(accent, Colors.White, 0.18),
            ["Brush.AccentPressed"] = Mix(accent, Colors.Black, 0.25),
            ["Brush.Selected"] = Mix(card, accent, 0.30),
            ["Brush.GhostHover"] = dark ? Mix(card, Colors.White, 0.10) : Mix(window, Colors.Black, 0.06),

            // Zustände / Sonstiges
            ["Brush.ScrollThumb"] = dark ? Mix(window, Colors.White, 0.35) : Mix(window, Colors.Black, 0.25),
            ["Brush.Success"] = theme.Color9,
            ["Brush.Danger"] = theme.Color8,
        };
    }

    /// <summary>Lineare Mischung zweier Farben (t = 0..1, 1 = komplett <paramref name="b"/>).</summary>
    private static Color Mix(Color a, Color b, double t)
    {
        byte Channel(byte x, byte y) => (byte)Math.Round(x * (1 - t) + y * t);
        return Color.FromArgb(
            255,
            Channel(a.R, b.R),
            Channel(a.G, b.G),
            Channel(a.B, b.B));
    }

    private static double Luminance(Color color) => (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
}
