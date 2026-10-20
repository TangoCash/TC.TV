using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    public EpgEvent Event => _event;

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
    private readonly AppSession _session = App.Session;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _channelId;
    private int _detailLoadVersion;

    public EpgWindow(string channelId, string channelName, IReadOnlyList<EpgEvent> events)
    {
        InitializeComponent();
        _channelId = channelId;
        HeaderText.Text = channelName;
        CloseButton.Content = Localization.T(240);
        RecordMenuItem.Header = Localization.T(340);
        ImdbMenuItem.Header = Localization.T(10);
        TheTvdbMenuItem.Header = Localization.T(11);

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
            EventList.SelectedIndex = 0;
        }
    }

    private void OnImdb(object sender, RoutedEventArgs e)
    {
        if (EventList.SelectedItem is EpgEntry entry)
        {
            App.OpenImdb(entry.Title);
        }
    }

    private void OnTheTvdb(object sender, RoutedEventArgs e)
    {
        if (EventList.SelectedItem is EpgEntry entry)
        {
            App.OpenTheTvdb(entry.Title);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>Zeigt die Details der markierten Sendung in der Detailkarte.</summary>
    private void OnEventSelected(object sender, SelectionChangedEventArgs e)
    {
        if (EventList.SelectedItem is not EpgEntry entry || entry.Event.IsEmpty)
        {
            DetailCard.Visibility = Visibility.Collapsed;
            return;
        }

        EpgEvent evt = entry.Event;
        DetailTitle.Text = entry.Title;

        string info = entry.Start;
        if (!string.IsNullOrEmpty(entry.Duration))
        {
            info += "   " + entry.Duration;
        }

        if (!string.IsNullOrEmpty(evt.Info2))
        {
            info += Environment.NewLine + evt.Info2;
        }

        DetailInfo.Text = info;

        // Die alte API liefert die Beschreibung bereits mit, die neue API lädt sie nach.
        DetailDescription.Text = _session.Client.ApiVersion == BoxApi.Legacy ? evt.Description : string.Empty;

        int index = EventList.SelectedIndex;
        if (index >= 0 && index + 1 < EventList.Items.Count && EventList.Items[index + 1] is EpgEntry next)
        {
            DetailNext.Text = (next.Start + "   " + next.Title).Trim();
            DetailSeparator.Visibility = Visibility.Visible;
        }
        else
        {
            DetailNext.Text = string.Empty;
            DetailSeparator.Visibility = Visibility.Collapsed;
        }

        DetailCard.Visibility = Visibility.Visible;
        _ = LoadDescriptionAsync(evt);
    }

    /// <summary>
    /// Lädt die ausführliche Beschreibung der markierten Sendung nach (neue API).
    /// Antworten einer zwischenzeitlich geänderten Auswahl werden verworfen.
    /// </summary>
    private async Task LoadDescriptionAsync(EpgEvent evt)
    {
        if (_session.Client.ApiVersion != BoxApi.V1)
        {
            return;
        }

        if (string.IsNullOrEmpty(evt.EventId) || evt.StartUnix <= 0)
        {
            return;
        }

        int version = ++_detailLoadVersion;
        try
        {
            // Kurz warten, damit schnelles Durchklicken nicht jede Sendung lädt.
            await Task.Delay(200, _cts.Token);
            if (version != _detailLoadVersion)
            {
                return;
            }

            string? longDescription = await _session.Client.GetEventLongDescriptionAsync(
                evt.EventId, evt.StartUnix, _cts.Token);
            if (version != _detailLoadVersion)
            {
                return;
            }

            if (!string.IsNullOrEmpty(longDescription))
            {
                DetailDescription.Text = longDescription;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Wählt beim Rechtsklick das EPG-Ereignis unter dem Cursor aus.</summary>
    private void OnEventRightClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(EventList, e.OriginalSource as DependencyObject) is ListBoxItem item)
        {
            item.IsSelected = true;
        }
    }

    /// <summary>Setzt einen Aufnahme-Timer für das markierte EPG-Ereignis (Start/Ende laut EPG).</summary>
    private async void OnRecord(object sender, RoutedEventArgs e)
    {
        if (EventList.SelectedItem is not EpgEntry entry || string.IsNullOrEmpty(_channelId))
        {
            return;
        }

        DateTime? start = entry.Event.StartDateTime();
        DateTime? stop = entry.Event.EndDateTime();
        if (start is null || stop is null || stop <= start)
        {
            return;
        }

        try
        {
            await _session.Client.CreateTimerAsync(
                start.Value.ToString("yyyy-MM-ddTHH:mm:ss"),
                stop.Value.ToString("yyyy-MM-ddTHH:mm:ss"),
                _channelId,
                "0",
                _cts.Token);

            MessageBox.Show(
                this,
                (HeaderText.Text ?? string.Empty) + Environment.NewLine + start.Value.ToString("dd.MM.yyyy HH:mm") + " – " + stop.Value.ToString("HH:mm"),
                Localization.T(9),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Entfernt bereits beendete Sendungen, damit die Liste mit der laufenden
    /// (bzw. der nächsten) Sendung beginnt. Sendungen ohne bekannten Beginn
    /// werden beibehalten.
    /// </summary>
    public static List<EpgEvent> FilterUpcoming(IEnumerable<EpgEvent> events)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        List<EpgEvent> result = new();

        foreach (EpgEvent item in events)
        {
            if (item.StartUnix <= 0)
            {
                result.Add(item);
                continue;
            }

            long end = item.DurationSeconds > 0
                ? item.StartUnix + item.DurationSeconds
                : item.StartUnix;
            if (end > now)
            {
                result.Add(item);
            }
        }

        result.Sort((left, right) => left.StartUnix.CompareTo(right.StartUnix));
        return result;
    }

    /// <summary>Nur für Selbsttest/--render: ein Fenster mit Beispieldaten.</summary>
    internal static EpgWindow CreateForSelfTest()
    {
        List<EpgEvent> events = new()
        {
            new EpgEvent { Info1 = "Tagesschau", Info2 = "Nachrichten", StartTime = "20:15", TimeTotal = "15", StartUnix = 1704136500, DurationSeconds = 900 },
            new EpgEvent { Info1 = "heute journal", Info2 = string.Empty, StartTime = "21:00", TimeTotal = "30", StartUnix = 1704139200, DurationSeconds = 1800 },
            new EpgEvent { Info1 = "Tatort", Info2 = "Krimi", StartTime = "21:30", TimeTotal = "90", StartUnix = 1704141000, DurationSeconds = 5400 },
        };
        return new EpgWindow("3f300012b66", "Das Erste HD", events);
    }
}
