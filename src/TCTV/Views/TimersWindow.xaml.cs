using System.Windows;
using System.Windows.Controls;
using TCTV.Core;
using Localization = TCTV.Core.Localization;

namespace TCTV.Views;

/// <summary>Anzeigeobjekt eines Timers in der Timer-Liste.</summary>
public sealed class TimerEntry
{
    private readonly TimerItem _timer;

    public TimerEntry(TimerItem timer)
    {
        _timer = timer;
    }

    public TimerItem Timer => _timer;

    public string Title => string.IsNullOrEmpty(_timer.Title) ? Localization.T(219) : _timer.Title;

    public string Channel => string.IsNullOrEmpty(_timer.ChannelName) ? _timer.ChannelId : _timer.ChannelName;

    public string Time => string.Join(" – ", new[] { _timer.Start, _timer.End }.Where(s => s.Length > 0));
}

/// <summary>
/// Timer-/Aufnahme-Verwaltung: zeigt laufende Aufnahmen und die Timer der Box
/// und erlaubt das Löschen von Timern.
/// </summary>
public partial class TimersWindow : Window
{
    private readonly AppSession _session = App.Session;
    private readonly CancellationTokenSource _cts = new();
    private bool _selfTest;

    public TimersWindow()
    {
        InitializeComponent();
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
        TitleText.Text = Localization.T(337);
        RunningHeader.Text = Localization.T(338);
        TimerHeader.Text = Localization.T(337);
        RefreshButton.Content = Localization.T(304);
        DeleteButton.Content = Localization.T(23);
        CloseButton.Content = Localization.T(240);
        EmptyHintText.Text = Localization.T(2);
    }

    private async Task ReloadAsync()
    {
        try
        {
            List<TimerItem> timers = await _session.Client.GetTimersAsync(_cts.Token);
            List<RunningRecording> running = await _session.Client.GetRunningRecordingsAsync(_cts.Token);

            // Kanalnamen der laufenden Aufnahmen aus den Timern ableiten.
            Dictionary<string, string> names = timers
                .Where(t => t.ChannelName.Length > 0)
                .GroupBy(t => t.ChannelId)
                .ToDictionary(g => g.Key, g => g.First().ChannelName, StringComparer.OrdinalIgnoreCase);

            if (running.Count == 0)
            {
                RunningPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                RunningPanel.Visibility = Visibility.Visible;
                RunningText.Text = string.Join(", ", running.Select(r =>
                    names.TryGetValue(r.ChannelId, out string? name) ? name : r.ChannelId));
            }

            TimerList.ItemsSource = timers.Select(t => new TimerEntry(t)).ToList();
            DeleteButton.IsEnabled = false;
            EmptyHint.Visibility = timers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnTimerSelected(object sender, SelectionChangedEventArgs e)
        => DeleteButton.IsEnabled = TimerList.SelectedItem is TimerEntry;

    private void OnRefresh(object sender, RoutedEventArgs e) => _ = ReloadAsync();

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (TimerList.SelectedItem is not TimerEntry entry)
        {
            return;
        }

        try
        {
            await _session.Client.DeleteTimerAsync(entry.Timer.Id, _cts.Token);
            await ReloadAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>Nur für Selbsttest/--render: ein Fenster mit Beispieldaten.</summary>
    internal static TimersWindow CreateForSelfTest()
    {
        TimersWindow window = new() { _selfTest = true };
        window.RunningPanel.Visibility = Visibility.Visible;
        window.RunningText.Text = "Das Erste HD";
        window.TimerList.ItemsSource = new[]
        {
            new TimerEntry(new TimerItem { Id = "17", Title = "Tatort", ChannelName = "Das Erste HD", Start = "20:15", End = "22:00" }),
            new TimerEntry(new TimerItem { Id = "18", Title = "heute journal", ChannelName = "ZDF HD", Start = "21:00", End = "21:30" }),
        };
        window.EmptyHint.Visibility = Visibility.Collapsed;
        return window;
    }
}
