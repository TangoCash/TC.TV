using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TCTV.Core;
using Forms = System.Windows.Forms;
using Localization = TCTV.Core.Localization;

namespace TCTV.Views;

/// <summary>Ein Sender inklusive der im Original vorberechneten Anzeigetexte.</summary>
public sealed class ChannelItem
{
    public ChannelItem(Channel channel)
    {
        Channel = channel;
    }

    public Channel Channel { get; }

    public string Number => Channel.Number;

    public string Name => Channel.Name;

    /// <summary>Startzeit der laufenden Sendung (z. B. "20:15").</summary>
    public string EpgStart => Channel.FirstEpg.StartTime;

    public string Display => string.IsNullOrEmpty(Channel.Number)
        ? Channel.Name
        : Channel.Number + "  " + Channel.Name;

    public string EpgLine
    {
        get
        {
            string description = Channel.FirstEpg.Description;
            if (string.IsNullOrEmpty(description))
            {
                return Localization.T(2);
            }

            int minutes = Channel.FirstEpg.TotalMinutes;
            return minutes > 0
                ? description + "   (" + minutes + " " + Localization.T(29) + ")"
                : description;
        }
    }

    /// <summary>
    /// Fortschritt der laufenden Sendung (0..1) für den Balken unter dem EPG.
    /// Grundlage sind die Felder der Box (&lt;percentage&gt; 0..100,
    /// &lt;timeElapsed&gt;/&lt;timeTotal&gt; in Minuten, &lt;startTime&gt;).
    /// </summary>
    public double Progress => ComputeProgress(Channel.FirstEpg, DateTime.Now.TimeOfDay);

    /// <summary>
    /// Der Balken erscheint, sobald die Sendungsdauer bekannt ist – auch bei 0 %
    /// (gerade begonnen), damit man den Fortschritt mitverfolgen kann.
    /// </summary>
    public bool HasProgress => Channel.FirstEpg.TotalMinutes > 0;

    /// <summary>
    /// Berechnet den Fortschritt aus den EPG-Feldern der Box. Reihenfolge:
    /// percentage (0..100) → timeElapsed/timeTotal → Startzeit gegen Uhrzeit.
    /// </summary>
    public static double ComputeProgress(EpgEvent epg, TimeSpan now)
    {
        int total = epg.TotalMinutes;
        if (total <= 0)
        {
            // Ohne bekannte Dauer lässt sich kein Fortschritt bestimmen.
            // (Die Box meldet in diesem Fall percentage = 100.)
            return 0;
        }

        if (double.TryParse(epg.Percentage, NumberStyles.Float, CultureInfo.InvariantCulture, out double percent))
        {
            return Math.Clamp(percent / 100.0, 0, 1);
        }

        if (int.TryParse(epg.TimeElapsed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int elapsed))
        {
            return Math.Clamp((double)elapsed / total, 0, 1);
        }

        if (TimeSpan.TryParse(epg.StartTime, CultureInfo.InvariantCulture, out TimeSpan start))
        {
            double minutes = (now - start).TotalMinutes;
            if (minutes < 0)
            {
                minutes += TimeSpan.FromDays(1).TotalMinutes; // Sendung über Mitternacht
            }

            return Math.Clamp(minutes / total, 0, 1);
        }

        return 0;
    }
}

/// <summary>
/// Hauptfenster: halbtransparentes Einblendfenster am Bildschirmrand mit
/// Sender-/EPG-Liste, Bouquet-Auswahl und Werkzeugleiste.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppSession _session = App.Session;
    private readonly DispatcherTimer _clockTimer = new();
    private readonly DispatcherTimer _fadeTimer = new();
    private readonly CancellationTokenSource _cts = new();

    private Forms.NotifyIcon? _tray;
    private List<Channel> _channels = new();
    private Channel? _selected;
    private bool _suppressBouquetChange;
    private bool _fadePinned;
    private bool _compact;
    private bool _exiting;

    /// <summary>Erlaubt das Schliessen trotz Tray-Verhalten (Selbsttest/Rendering).</summary>
    internal bool AllowClose { get; set; }

    /// <summary>
    /// Nur für die Entwicklung (<c>--render</c>): füllt die Senderliste mit
    /// Beispieldaten, damit die Darstellung auch ohne Box geprüft werden kann.
    /// </summary>
    internal void LoadDemoChannels()
    {
        static Channel Make(string number, string name, string start, string total, string elapsed, string percent, string text, string info1, string info2) => new()
        {
            Number = number,
            Id = number,
            Name = name,
            FirstEpg = new EpgEvent
            {
                StartTime = start,
                TimeTotal = total,
                TimeElapsed = elapsed,
                Percentage = percent,
                Description = text,
                Info1 = info1,
                Info2 = info2,
            },
            SecondEpg = new EpgEvent { StartTime = "22:00", Description = "Tagesschau" },
        };

        List<ChannelItem> items = new()
        {
            new(Make("1", "Das Erste HD", "20:15", "45", "33", "73", "Tatort: Der Fall Reemtsma", "Krimireihe", "Deutschland 2024")),
            new(Make("2", "ZDFinfo HD", "21:00", "45", "22", "49", "Aufgeklärt - Spektakuläre Kriminalfälle", "Dokureihe", "Deutschland 2020")),
            new(Make("3", "arte HD", "20:50", "55", "1", "2", "Größte Städte der Welt", "Dokumentation", "Frankreich 2023")),
        };

        _suppressBouquetChange = true;
        BouquetCombo.ItemsSource = new List<Bouquet> { new() { Number = "1", Name = "Favoriten" } };
        BouquetCombo.SelectedIndex = 0;
        _suppressBouquetChange = false;

        ChannelList.ItemsSource = items;
        ChannelList.SelectedIndex = 0;
        SetEmptyHint(null);
        SetStatus(Localization.T(100), true);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point point);

    public MainWindow()
    {
        InitializeComponent();

        ApplyLocalization();
        ApplyTheme();
        PositionWindow();

        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();

        _fadeTimer.Interval = TimeSpan.FromMilliseconds(200);
        _fadeTimer.Tick += (_, _) => UpdateFadeState();
        _fadeTimer.Start();

        CreateTray();

        SetEmptyHint(Localization.T(15));

        Loaded += async (_, _) => await InitializeAsync();
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void ApplyLocalization()
    {
        SetButton(StreamButton, "\uE768", Localization.T(207));
        SetButton(StopButton, "\uE71A", Localization.T(4));
        SetButton(VideoButton, "\uE714", Localization.T(30));
        SetButton(RefreshButton, "\uE72C", Localization.T(304));
        SetButton(TimerButton, "\uE823", Localization.T(9));
        SetButton(SettingsButton, "\uE713", Localization.T(0));
        SetButton(ProfilesButton, "\uE716", Localization.T(93));

        FullScreenButton.Content = "\uE740";
        FullScreenButton.ToolTip = Localization.T(6);
        ExitButton.Content = "\uE8BB";
        ExitButton.ToolTip = Localization.T(305);

        if (ChannelList.ItemsSource is IEnumerable<ChannelItem> items)
        {
            ChannelList.ItemsSource = items.ToList();
        }
    }

    /// <summary>Setzt Inhalt eines Werkzeugleisten-Buttons: Icon + Text.</summary>
    private static void SetButton(Button button, string glyph, string text)
    {
        StackPanel panel = new() { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
        });

        button.Content = panel;
        button.ToolTip = text;
    }

    private void ApplyTheme()
    {
        // Setzt alle "Brush.*"-Ressourcen; die Oberfläche folgt per DynamicResource.
        ThemeManager.Apply(_session.Theme);
        Opacity = 1.0;
    }

    private void PositionWindow()
    {
        Rect area = SystemParameters.WorkArea;
        Height = area.Height;
        Top = area.Top;
        Left = _session.Settings.RightEdge ? area.Right - Width : area.Left;
    }

    private void UpdateClock()
    {
        TimeText.Text = DateTime.Now.ToString("HH:mm:ss");
    }

    private void CreateTray()
    {
        try
        {
            _tray = new Forms.NotifyIcon
            {
                Text = "TC.TV",
                Visible = true,
                Icon = new System.Drawing.Icon(AppPaths.Temp("sTV.ico")),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            // Ohne Icon trotzdem weiterarbeiten.
            _tray = new Forms.NotifyIcon { Text = "TC.TV", Visible = true };
        }

        Forms.ContextMenuStrip menu = new();
        menu.Items.Add(Localization.T(8), null, (_, _) => TogglePin());
        menu.Items.Add(Localization.T(30), null, (_, _) => OpenVideoWindow());
        menu.Items.Add(Localization.T(0), null, (_, _) => OpenSettings());
        menu.Items.Add(Localization.T(93), null, (_, _) => OpenProfiles());
        menu.Items.Add(Localization.T(1), null, (_, _) => ToggleFullScreen());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Localization.T(305), null, (_, _) => ExitApp());

        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                TogglePin();
            }
        };
    }

    private void TogglePin()
    {
        _fadePinned = !_fadePinned;
        if (_fadePinned)
        {
            ShowWindow();
        }
    }

    /// <summary>Ein-/Ausblenden anhand der Mausposition (Fade-in/Fade-out).</summary>
    private void UpdateFadeState()
    {
        if (_fadePinned)
        {
            return;
        }

        Rect area = SystemParameters.WorkArea;
        Point mouse = GetMousePositionDip();
        bool nearEdge = _session.Settings.RightEdge
            ? mouse.X >= area.Right - 3
            : mouse.X <= area.Left + 3;

        if (nearEdge)
        {
            ShowWindow();
            return;
        }

        if (IsVisible && (mouse.X < area.Right - Width - 40 || mouse.X > area.Right + 40 || mouse.Y < area.Top - 40 || mouse.Y > area.Bottom + 40))
        {
            HideWindow();
        }
    }

    private Point GetMousePositionDip()
    {
        if (!GetCursorPos(out Win32Point point))
        {
            return new Point(0, 0);
        }

        PresentationSource? source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
        {
            return source.CompositionTarget.TransformFromDevice.Transform(new Point(point.X, point.Y));
        }

        return new Point(point.X, point.Y);
    }

    private void ShowWindow()
    {
        if (!IsVisible)
        {
            Show();
        }

        BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(220)));
    }

    private void HideWindow()
    {
        if (_session.Settings.FadeInDelayMs > 0)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(_session.Settings.FadeInDelayMs);
                if (!_fadePinned)
                {
                    HideNow();
                }
            });
            return;
        }

        HideNow();
    }

    private void HideNow()
    {
        DoubleAnimation animation = new(0.0, TimeSpan.FromMilliseconds(220));
        animation.Completed += (_, _) =>
        {
            if (!_fadePinned && Opacity < 0.05)
            {
                Hide();
            }
        };
        BeginAnimation(OpacityProperty, animation);
    }

    private async Task InitializeAsync()
    {
        await ReloadAsync();
    }

    /// <summary>Bouquets und Sender neu laden (entspricht _1ag/_1af des Originals).</summary>
    private async Task ReloadAsync()
    {
        SetStatus(Localization.T(15));
        try
        {
            List<Bouquet> bouquets = await _session.Client.GetBouquetsAsync(_cts.Token);

            _suppressBouquetChange = true;
            BouquetCombo.ItemsSource = bouquets;
            if (bouquets.Count > 0)
            {
                BouquetCombo.SelectedIndex = 0;
            }

            _suppressBouquetChange = false;

            if (bouquets.Count == 0)
            {
                ChannelList.ItemsSource = Array.Empty<ChannelItem>();
                _channels.Clear();
                _selected = null;
                UpdateDetails();
                SetEmptyHint(Localization.T(17));
                SetStatus(Localization.T(17), false);
                return;
            }

            SetEmptyHint(null);
            await LoadChannelsAsync(bouquets[0].Number);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task LoadChannelsAsync(string bouquetNumber)
    {
        SetStatus(Localization.T(15));
        List<Channel> channels = await _session.Client.GetBouquetAsync(bouquetNumber, _cts.Token);
        _channels = channels;

        List<ChannelItem> items = channels.Select(c => new ChannelItem(c)).ToList();
        ChannelList.ItemsSource = items;

        if (items.Count > 0)
        {
            int activeIndex = channels.FindIndex(c => c.IsActiveChannel is "1" or "true");
            ChannelList.SelectedIndex = activeIndex >= 0 ? activeIndex : 0;
        }

        SetStatus(channels.Count > 0 ? Localization.T(100) : Localization.T(99), channels.Count > 0);
        SetEmptyHint(channels.Count == 0 ? Localization.T(17) : null);
    }

    private void SetStatus(string text, bool? connected = null)
    {
        StatusText.Text = text;
        string key = connected switch
        {
            true => "Brush.Success",
            false => "Brush.Danger",
            _ => "Brush.Muted",
        };
        StatusDot.SetResourceReference(System.Windows.Shapes.Ellipse.FillProperty, key);
    }

    private void OnBouquetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressBouquetChange || BouquetCombo.SelectedItem is not Bouquet bouquet)
        {
            return;
        }

        _ = LoadChannelsAsync(bouquet.Number);
    }

    private void OnChannelChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = (ChannelList.SelectedItem as ChannelItem)?.Channel;
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        if (_selected is null)
        {
            DetailCard.Visibility = Visibility.Collapsed;
            return;
        }

        EpgEvent first = _selected.FirstEpg;
        DetailTitle.Text = _selected.Name;
        DetailInfo.Text = first.IsEmpty
            ? Localization.T(2)
            : (first.StartTime + "   " + first.TimeTotal + " " + Localization.T(29) + Environment.NewLine +
               first.Header + Environment.NewLine + first.Description).Trim();

        EpgEvent next = _selected.SecondEpg;
        DetailNext.Text = next.IsEmpty
            ? string.Empty
            : (next.StartTime + "   " + next.Description).Trim();
        DetailSeparator.Visibility = next.IsEmpty ? Visibility.Collapsed : Visibility.Visible;

        DetailCard.Visibility = Visibility.Visible;
    }

    /// <summary>Blendet den Hinweis im leeren Senderbereich ein bzw. aus.</summary>
    private void SetEmptyHint(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            EmptyHint.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyHintText.Text = text;
        EmptyHint.Visibility = Visibility.Visible;
    }

    private void OnChannelDoubleClick(object sender, MouseButtonEventArgs e) => _ = StreamSelectedAsync();

    private async void OnStream(object sender, RoutedEventArgs e) => await StreamSelectedAsync();

    /// <summary>Startet den Stream des gewählten Senders in VLC (entspricht _1aq).</summary>
    private async Task StreamSelectedAsync()
    {
        Channel? channel = _selected;
        if (channel is null || string.IsNullOrEmpty(channel.Id))
        {
            return;
        }

        if (!_session.IsVlcAvailable)
        {
            MessageBox.Show(Localization.T(102), "TC.TV", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Nur eine aktuell laufende Aufnahme auf einem anderen Transponder
        // verhindert das Streaming (Original: Abbruch mit "Aufnahme läuft!").
        if (await IsBlockedByRunningRecordingAsync(channel))
        {
            MessageBox.Show(Localization.T(54), Localization.T(75), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_session.Profile.ForceSwitch)
        {
            VlcLauncher.StopPlayback(_session.VlcOptions);
        }

        try
        {
            await _session.Client.ZapToAsync(channel.Id, _cts.Token);
            await Task.Delay(500);

            string arguments;
            if (_session.Profile.ReciProxy)
            {
                await _session.Client.BuildLiveUrlAsync(_cts.Token);
                await Task.Delay(ReciProxyDelayMs());
                arguments = _session.VlcOptions.BuildOptionString() + " " + _session.Client.StreamBaseUrl + "/ReciProxy";
            }
            else
            {
                arguments = _session.VlcOptions.BuildOptionString() + " " + _session.Client.StreamBaseUrl + "/id=" + channel.Id;
            }

            if (VlcLauncher.Play(_session.VlcPath, arguments))
            {
                SetStatus(Localization.T(299));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private int ReciProxyDelayMs()
        => int.TryParse(_session.Profile.ReciProxyDelay, out int seconds) ? Math.Max(0, seconds) * 1000 : 0;

    /// <summary>
    /// Prüft, ob der Stream des gewählten Senders wegen einer laufenden Aufnahme
    /// nicht möglich ist. Entspricht Funktion _1aq des Originals ($kv = laufende
    /// Aufnahmen, $ks = aktueller Transponder, $kw = Multituner-Box).
    ///
    /// Wichtig: Es blockiert nur eine AKTUELL laufende Aufnahme (Boxzeit zwischen
    /// Start und Ende), nicht ein bloß geplanter Timer.
    /// </summary>
    private async Task<bool> IsBlockedByRunningRecordingAsync(Channel channel)
    {
        // Multituner-Boxen können gleichzeitig aufnehmen und streamen.
        if (_session.Profile.MultiTuner)
        {
            return false;
        }

        List<RunningRecording> running = await _session.Client.GetRunningRecordingsAsync(_cts.Token);
        if (running.Count == 0)
        {
            return false;
        }

        // Der gewählte Sender wird selbst aufgenommen -> kein Konflikt.
        foreach (RunningRecording recording in running)
        {
            if (RecordingServiceClient.ChannelIdsMatch(recording.ChannelId, channel.Id))
            {
                return false;
            }
        }

        // Sender liegt auf dem aktuell empfangenen Transponder -> kein Konflikt.
        string? transponder = await _session.Client.GetCurrentTpChannelsAsync(_cts.Token);
        return !RecordingServiceClient.TransponderContains(transponder, channel.Id);
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        VlcLauncher.StopPlayback(_session.VlcOptions);
        SetStatus(Localization.T(4));
    }

    private void OnRefreshEpg(object sender, RoutedEventArgs e) => _ = ReloadAsync();

    private async void OnTimer(object sender, RoutedEventArgs e)
    {
        if (_selected is null || string.IsNullOrEmpty(_selected.Id))
        {
            return;
        }

        string? minutes = PromptWindow.Ask(this, Localization.T(9), Localization.T(29), "60");
        if (minutes is null || !int.TryParse(minutes, out int value) || value <= 0)
        {
            return;
        }

        try
        {
            DateTime start = DateTime.Now;
            await _session.Client.CreateTimerAsync(
                start.ToString("yyyy-MM-ddTHH:mm:ss"),
                start.AddMinutes(value).ToString("yyyy-MM-ddTHH:mm:ss"),
                _selected.Id,
                "0",
                _cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnVideo(object sender, RoutedEventArgs e) => OpenVideoWindow();

    private void OnSettings(object sender, RoutedEventArgs e) => OpenSettings();

    private void OnProfiles(object sender, RoutedEventArgs e) => OpenProfiles();

    private void OnToggleFullScreen(object sender, RoutedEventArgs e) => ToggleFullScreen();

    private void ToggleFullScreen()
    {
        _compact = !_compact;
        Width = _compact ? 680 : 480;
        PositionWindow();
    }

    private void OpenVideoWindow()
    {
        VideoWindow window = new();
        if (IsVisible)
        {
            window.Owner = this;
        }

        window.ShowDialog();
    }

    private void OpenSettings()
    {
        SettingsWindow window = new() { Owner = this };
        if (window.ShowDialog() == true)
        {
            _session.ReloadSettings();
            ApplyLocalization();
            ApplyTheme();
            PositionWindow();
        }
    }

    private void OpenProfiles()
    {
        ProfilesWindow window = new() { Owner = this };
        if (window.ShowDialog() == true)
        {
            _session.ReloadProfile();
            _ = ReloadAsync();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
        else if (e.Key == Key.S)
        {
            VlcLauncher.StopPlayback(_session.VlcOptions);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HideWindow();
            e.Handled = true;
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exiting || AllowClose)
        {
            return;
        }

        // Das Fenster wird nur versteckt; beendet wird über das Tray-Menü.
        e.Cancel = true;
        HideWindow();
    }

    private void OnExit(object sender, RoutedEventArgs e) => ExitApp();

    private void ExitApp()
    {
        _exiting = true;
        _tray?.Dispose();
        _cts.Cancel();
        if (Application.Current is App app)
        {
            app.ExitApplication();
        }
    }
}
