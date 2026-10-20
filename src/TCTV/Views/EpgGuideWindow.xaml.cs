using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using TCTV.Core;
using Localization = TCTV.Core.Localization;

namespace TCTV.Views;

/// <summary>Anzeigeobjekt einer Sendung in der Programmübersicht.</summary>
public sealed class EpgGuideEntry
{
    public EpgGuideEntry(string channelId, string channelName, EpgEvent epgEvent)
    {
        ChannelId = channelId;
        ChannelName = channelName;
        Event = epgEvent;
    }

    public string ChannelId { get; }

    public string ChannelName { get; }

    public EpgEvent Event { get; }

    public string Start => FormatStart(Event);

    public string Duration => Event.TotalMinutes > 0 ? Event.TotalMinutes + " " + Localization.T("minutes") : string.Empty;

    public string Title => string.IsNullOrEmpty(Event.Info1) ? Event.Description : Event.Info1;

    public string Text => Event.Info2;

    private static string FormatStart(EpgEvent e)
    {
        if (e.StartUnix > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds(e.StartUnix).ToLocalTime().ToString("ddd dd.MM. HH:mm");
        }

        return e.StartTime;
    }
}

/// <summary>
/// Programmübersicht (EPG-Grid): zeigt die Sendungen aller Sender des aktuellen
/// Bouquets nach Sender gruppiert und bietet eine Suche.
/// </summary>
public partial class EpgGuideWindow : Window
{
    private readonly AppSession _session = App.Session;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _bouquet;
    private List<EpgGuideEntry> _all = new();
    private bool _selfTest;

    public EpgGuideWindow(string bouquetNumber)
    {
        InitializeComponent();
        _bouquet = bouquetNumber;
        ApplyLocalization();
        Loaded += async (_, _) =>
        {
            if (!_selfTest)
            {
                await ReloadAsync();
            }
        };
    }

    private void ApplyLocalization()
    {
        TitleText.Text = Localization.T("programme");
        SearchLabel.Text = Localization.T("search");
        CloseButton.Content = Localization.T("back");
        EmptyHintText.Text = Localization.T("no_epg");
        RecordMenuItem.Header = Localization.T("record");
        ImdbMenuItem.Header = Localization.T("imdb");
        TheTvdbMenuItem.Header = Localization.T("thetvdb");
    }

    private async Task ReloadAsync()
    {
        try
        {
            List<EpgChannel> guide = await _session.Client.GetBouquetEpgAsync(_bouquet, 72, _cts.Token);
            _all = guide
                .SelectMany(c => c.Events.Select(e => new EpgGuideEntry(c.ChannelId, c.ChannelName, e)))
                .OrderBy(e => e.Event.StartUnix)
                .ThenBy(e => e.ChannelName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            ApplyFilter();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyFilter()
    {
        string query = SearchBox.Text?.Trim() ?? string.Empty;
        IEnumerable<EpgGuideEntry> filtered = _all;
        if (query.Length > 0)
        {
            filtered = _all.Where(e =>
                e.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || e.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
                || e.ChannelName.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        List<EpgGuideEntry> list = filtered.ToList();
        ListCollectionView view = new(list);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(EpgGuideEntry.ChannelName)));
        EventList.ItemsSource = view;
        EmptyHint.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

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
        if (EventList.SelectedItem is not EpgGuideEntry entry)
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
                entry.ChannelId,
                "0",
                _cts.Token);

            MessageBox.Show(
                this,
                entry.ChannelName + Environment.NewLine + start.Value.ToString("dd.MM.yyyy HH:mm") + " – " + stop.Value.ToString("HH:mm"),
                Localization.T("set_timer"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnImdb(object sender, RoutedEventArgs e)
    {
        if (EventList.SelectedItem is EpgGuideEntry entry)
        {
            App.OpenImdb(entry.Title);
        }
    }

    private void OnTheTvdb(object sender, RoutedEventArgs e)
    {
        if (EventList.SelectedItem is EpgGuideEntry entry)
        {
            App.OpenTheTvdb(entry.Title);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>Nur für Selbsttest/--render: ein Fenster mit Beispieldaten.</summary>
    internal static EpgGuideWindow CreateForSelfTest()
    {
        EpgGuideWindow window = new("1") { _selfTest = true };
        window._all = new List<EpgGuideEntry>
        {
            new("361d03f300012b66", "ZDF HD", new EpgEvent { Info1 = "heute journal", Info2 = "Nachrichten", StartTime = "21:00", TimeTotal = "30", StartUnix = 1704139200, DurationSeconds = 1800 }),
            new("3f300012b66", "Das Erste HD", new EpgEvent { Info1 = "Tagesschau", Info2 = "Nachrichten", StartTime = "20:15", TimeTotal = "15", StartUnix = 1704136500, DurationSeconds = 900 }),
            new("3f300012b66", "Das Erste HD", new EpgEvent { Info1 = "Tatort", Info2 = "Krimi", StartTime = "21:30", TimeTotal = "90", StartUnix = 1704141000, DurationSeconds = 5400 }),
        }.OrderBy(e => e.Event.StartUnix).ToList();
        window.ApplyFilter();
        return window;
    }
}
