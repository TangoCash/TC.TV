using System.Windows;
using TCTV.Core;
using Localization = TCTV.Core.Localization;

namespace TCTV.Views;

/// <summary>Ein Eintrag der Hilfe-Übersicht (Funktion + Kurzbeschreibung).</summary>
public sealed class HelpEntry
{
    public required string Title { get; init; }

    public required string Text { get; init; }
}

/// <summary>
/// Hilfe-Übersicht: erklärt alle Funktionen des Programms in Kürze.
/// </summary>
public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        TitleText.Text = Localization.T("help");
        CloseButton.Content = Localization.T("back");
        HelpList.ItemsSource = BuildEntries(Localization.IsEnglish);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private static HelpEntry E(bool en, string deTitle, string enTitle, string deText, string enText)
        => new() { Title = en ? enTitle : deTitle, Text = en ? enText : deText };

    private static IReadOnlyList<HelpEntry> BuildEntries(bool en)
    {
        return new List<HelpEntry>
        {
            E(en,
                "Ein-/Ausblenden", "Show / hide",
                "Das Fenster erscheint, wenn der Mauszeiger an den Bildschirmrand fährt, und verschwindet, wenn er sich entfernt. Ein Klick auf das Tray-Icon heftet es an.",
                "The window appears when the mouse reaches the screen edge and hides again when it moves away. Click the tray icon to pin it."),
            E(en,
                "Bouquet", "Bouquet",
                "Über die Auswahlliste oben wählst du die Sendergruppe (Bouquet).",
                "Use the drop-down at the top to choose the channel group (bouquet)."),
            E(en,
                "Sender & EPG", "Channel & EPG",
                "Ein Klick auf einen Sender zeigt die laufende und die nächste Sendung im Detailbereich.",
                "Click a channel to show the current and next programme in the details area."),
            E(en,
                "Stream starten", "Start stream",
                "Doppelklick auf einen Sender, der Stream-Knopf oder Rechtsklick → „Aktuellen Sender streamen“ startet die Wiedergabe in VLC.",
                "Double-click a channel, use the Stream button or right-click → \"Stream current channel\" to start playback in VLC."),
            E(en,
                "Stop", "Stop",
                "Der Stop-Knopf oder die Taste S beendet die laufende VLC-Wiedergabe.",
                "The Stop button or the S key stops the running VLC playback."),
            E(en,
                "EPG aktualisieren", "Refresh EPG",
                "Der Aktualisieren-Knopf lädt Sender und EPG des aktuellen Bouquets neu.",
                "The Refresh button reloads channels and EPG of the current bouquet."),
            E(en,
                "Weiteres EPG", "More EPG",
                "Rechtsklick auf einen Sender → „Weiteres EPG“ zeigt alle Sendungen dieses Senders.",
                "Right-click a channel → \"More EPG\" shows all programmes of this channel."),
            E(en,
                "Programm", "Programme guide",
                "Der Programm-Knopf öffnet die EPG-Übersicht aller Sender – mit Suchfeld.",
                "The Programme button opens the EPG overview of all channels – with search."),
            E(en,
                "Aufnahme setzen", "Set recording",
                "Rechtsklick auf eine Sendung (Programm oder Weiteres EPG) → „Aufnehmen“ legt einen Aufnahme-Timer mit Start und Ende laut EPG an.",
                "Right-click a programme (guide or more EPG) → \"Record\" creates a recording timer with EPG start and end times."),
            E(en,
                "Aufnahmen", "Recordings",
                "Der Aufnahmen-Knopf zeigt die Timer und laufenden Aufnahmen der Box; dort lassen sich Timer löschen.",
                "The Recordings button shows the box's timers and running recordings; you can delete timers there."),
            E(en,
                "Video", "Video",
                "Der Video-Knopf listet Aufnahmen und Videos der Box – mit der Option „inkl. Unterordner“.",
                "The Video button lists the box's recordings and videos – with the \"incl. subfolders\" option."),
            E(en,
                "Einstellungen", "Settings",
                "Sprache, Design, VLC-Optionen und Einblende-Verhalten.",
                "Language, theme, VLC options and fade behaviour."),
            E(en,
                "Profile", "Profiles",
                "Verwaltung der Empfänger-Profile (IP, Ports, API-Version).",
                "Manage receiver profiles (IP, ports, API version)."),
            E(en,
                "Vollbild", "Fullscreen",
                "Die Taste F oder der Vollbild-Knopf verbreitert das Fenster.",
                "The F key or the Fullscreen button widens the window."),
            E(en,
                "Tray-Icon", "Tray icon",
                "Das Tray-Icon blendet das Fenster ein/aus und öffnet dieses Menü.",
                "The tray icon shows/hides the window and opens this menu."),
            E(en,
                "Beenden", "Exit",
                "Über das Tray-Menü wird TC.TV beendet.",
                "Use the tray menu to quit TC.TV."),
        };
    }
}
