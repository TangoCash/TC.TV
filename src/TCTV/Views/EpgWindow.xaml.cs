using System.Windows;
using TCTV.Core;
using Localization = TCTV.Core.Localization;

namespace TCTV.Views;

/// <summary>Anzeigeobjekt einer EPG-Sendung im EPG-Fenster.</summary>
public sealed class EpgEntry
{
    private readonly EpgEvent _event;

    public EpgEntry(EpgEvent epgEvent)
    {
        _event = epgEvent;
    }

    public string Start => FormatStart(_event);

    public string Duration => _event.TotalMinutes > 0
        ? _event.TotalMinutes + " " + Localization.T(29)
        : string.Empty;

    public string Title => string.IsNullOrEmpty(_event.Info1) ? _event.Description : _event.Info1;

    public string Text => _event.Info2;

    private static string FormatStart(EpgEvent e)
    {
        // Die neue API liefert nur Unix-Sekunden, die alte einen fertigen Text.
        if (e.StartUnix > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds(e.StartUnix).ToLocalTime().ToString("ddd dd.MM. HH:mm");
        }

        return e.StartTime;
    }
}

/// <summary>
/// Zeigt das erweiterte EPG eines einzelnen Senders (Rechtsklick → „Weiteres EPG“).
/// </summary>
public partial class EpgWindow : Window
{
    public EpgWindow(string channelName, IReadOnlyList<EpgEvent> events)
    {
        InitializeComponent();
        HeaderText.Text = channelName;
        CloseButton.Content = Localization.T(240);

        if (events.Count == 0)
        {
            EventList.ItemsSource = new[]
            {
                new EpgEntry(new EpgEvent { Info1 = Localization.T(2) }),
            };
        }
        else
        {
            EventList.ItemsSource = events.Select(e => new EpgEntry(e)).ToList();
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>Nur für Selbsttest/--render: ein Fenster mit Beispieldaten.</summary>
    internal static EpgWindow CreateForSelfTest()
    {
        List<EpgEvent> events = new()
        {
            new EpgEvent { Info1 = "Tagesschau", Info2 = "Nachrichten", StartTime = "20:15", TimeTotal = "15", StartUnix = 1704136500, DurationSeconds = 900 },
            new EpgEvent { Info1 = "heute journal", Info2 = string.Empty, StartTime = "21:00", TimeTotal = "30", StartUnix = 1704139200, DurationSeconds = 1800 },
            new EpgEvent { Info1 = "Tatort", Info2 = "Krimi", StartTime = "21:30", TimeTotal = "90", StartUnix = 1704141000, DurationSeconds = 5400 },
        };
        return new EpgWindow("Das Erste HD", events);
    }
}
