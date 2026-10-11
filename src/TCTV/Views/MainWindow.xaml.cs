using System.Globalization;
using System.Net.Http;
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

        string logoPath = App.Session.GetLogoPath(channel);
        if (logoPath.Length > 0 && File.Exists(logoPath))
        {
            LogoPath = logoPath;
            HasLogo = true;
        }
    }

    public Channel Channel { get; }

    /// <summary>Lokaler Pfad des Senderlogos (leer, wenn keines vorliegt).</summary>
    public string LogoPath { get; } = string.Empty;

    /// <summary>true, wenn ein Senderlogo vorliegt.</summary>
    public bool HasLogo { get; }

    /// <summary>true, wenn statt des Logos die Sendernummer angezeigt wird.</summary>
    public bool ShowNumber => !HasLogo;

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

    /// <summary>
    /// Zeitpunkt des letzten erfolgreichen Ladens von Bouquet/Sender/EPG. Grundlage
    /// der gebremsten Auto-Aktualisierung beim Einblenden.
    /// </summary>
    private DateTimeOffset _lastLoadUtc = DateTimeOffset.MinValue;

    /// <summary>Verhindert, dass sich zwei Auto-Aktualisierungen überlagern.</summary>
    private bool _autoRefreshing;

    /// <summary>
    /// Mindestabstand zweier automatischer Aktualisierungen. Das Original lud bei
    /// jedem Ein-/Ausblenden nach; hier wird gebremst, damit hektisches Mausschwenken
    /// am Bildschirmrand die Box nicht mit Anfragen überschüttet.
    /// </summary>
    internal static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Entscheidet, ob beim Einblenden automatisch nachgeladen wird: nur wenn die
    /// Option „EPG-Auto-Aktualisierung“ gesetzt ist und die Daten älter als
    /// <see cref="AutoRefreshInterval"/> sind (oder noch nie geladen wurden).
    /// </summary>
    internal static bool ShouldAutoRefresh(bool enabled, DateTimeOffset lastLoadUtc, DateTimeOffset now)
        => enabled && (lastLoadUtc == DateTimeOffset.MinValue || now - lastLoadUtc >= AutoRefreshInterval);

    /// <summary>
    /// true, wenn der Mauszeiger außerhalb des Fensters liegt (mit 40-px-Rand) und
    /// das Fenster deshalb ausgeblendet werden soll. Berücksichtigt, ob das Fenster
    /// am rechten oder linken Bildschirmrand hängt.
    /// </summary>
    internal static bool ShouldHide(Point mouse, Rect area, double width, bool rightEdge)
    {
        double windowLeft = rightEdge ? area.Right - width : area.Left;
        return mouse.X < windowLeft - 40
            || mouse.X > windowLeft + width + 40
            || mouse.Y < area.Top - 40
            || mouse.Y > area.Bottom + 40;
    }

    /// <summary>
    /// Die Statuszeile im Kopf: der Meldungstext plus der Name des aktiven Profils,
    /// damit auf den ersten Blick klar ist, mit welcher Box man verbunden ist.
    /// Das Original zeigte dort nur „Verbunden!“ bzw. „Nicht verbunden!“
    /// ($ei[100]/$ei[99], S.TV.au3 Z. 3670/3678). Ohne Profilnamen bleibt der Text
    /// unverändert – so sieht die Zeile aus, wenn gar kein Profil geladen ist.
    /// </summary>
    internal static string StatusLine(string text, string? profileName)
        => string.IsNullOrEmpty(profileName) ? text : text + " · " + profileName;

    /// <summary>Erlaubt das Schliessen trotz Tray-Verhalten (Selbsttest/Rendering).</summary>
    internal bool AllowClose { get; set; }

    /// <summary>
    /// Nur für <c>--render</c>: unterdrückt das Laden beim Start, damit die
    /// Beispieldaten (<see cref="LoadDemoChannels"/>) stehen bleiben. Ohne das
    /// überschreibt der erste Netzabruf sie wieder mit „Keine Verbindung zur Box“.
    /// </summary>
    internal bool SkipInitialLoad { get; set; }

    /// <summary>
    /// Nur für den Selbsttest: was im Kopf unter „TC.TV“ steht (Statuszeile und die
    /// Kurzinfo, die als Tooltip daran hängt).
    /// </summary>
    internal (string Text, string Tip) StatusInfoForSelfTest
        => (StatusText.Text, StatusText.ToolTip as string ?? string.Empty);

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
        SetButton(GuideButton, "\uE7F4", Localization.T(335));
        SetButton(RefreshButton, "\uE72C", Localization.T(304));
        SetButton(TimersButton, "\uE823", Localization.T(336));
        SetButton(SettingsButton, "\uE713", Localization.T(0));
        SetButton(ProfilesButton, "\uE716", Localization.T(93));

        FullScreenButton.Content = "\uE740";
        FullScreenButton.ToolTip = Localization.T(6);
        ExitButton.Content = "\uE8BB";
        ExitButton.ToolTip = Localization.T(305);
        StreamMenuItem.Header = Localization.T(334);
        MoreEpgMenuItem.Header = Localization.T(333);

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

        if (IsVisible && ShouldHide(mouse, area, Width, _session.Settings.RightEdge))
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
        bool wasHidden = !IsVisible;
        if (wasHidden)
        {
            Show();
        }

        BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(220)));

        // „EPG-Auto-Aktualisierung“: beim Einblenden des Fensters werden Sender und
        // EPG des aktuellen Bouquets von der Box neu geholt (Original: _1ak/_1al →
        // _1ag()). Gebremst, damit hektisches Mausschwenken am Rand nicht jede
        // Bewegung nachladen lässt.
        if (wasHidden && !_autoRefreshing
            && ShouldAutoRefresh(_session.Settings.FadeInAutoRefresh, _lastLoadUtc, DateTimeOffset.UtcNow))
        {
            _autoRefreshing = true;
            Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await RefreshCurrentBouquetAsync();
                }
                finally
                {
                    _autoRefreshing = false;
                }
            });
        }
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
        // Beim Rendern bleiben die Beispieldaten stehen (siehe SkipInitialLoad).
        if (SkipInitialLoad)
        {
            return;
        }

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
                _lastLoadUtc = DateTimeOffset.UtcNow;
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

    /// <summary>
    /// Sender (mit EPG) eines Bouquets laden. <paramref name="preserveSelection"/>
    /// behält die gerade markierte Zeile bei, wenn es den Sender noch gibt – so
    /// wirkt die Auto-Aktualisierung nicht wie ein Neuladen der Liste.
    /// </summary>
    private async Task LoadChannelsAsync(string bouquetNumber, bool preserveSelection = false)
    {
        string? previousId = preserveSelection ? _selected?.Id : null;

        SetStatus(Localization.T(15));
        List<Channel> channels = await _session.Client.GetBouquetAsync(bouquetNumber, _cts.Token);
        _channels = channels;

        List<ChannelItem> items = channels.Select(c => new ChannelItem(c)).ToList();
        ChannelList.ItemsSource = items;
        _ = RefreshLogosAsync(channels);

        if (items.Count > 0)
        {
            int index = previousId is null
                ? -1
                : channels.FindIndex(c => RecordingServiceClient.ChannelIdsMatch(c.Id, previousId));

            if (index < 0)
            {
                index = channels.FindIndex(c => c.IsActiveChannel is "1" or "true");
            }

            ChannelList.SelectedIndex = index >= 0 ? index : 0;
        }

        SetStatus(channels.Count > 0 ? Localization.T(100) : Localization.T(99), channels.Count > 0);
        SetEmptyHint(channels.Count == 0 ? Localization.T(17) : null);

        // Frische Daten holen heißt: die Auto-Aktualisierung wartet wieder 60 s.
        _lastLoadUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Lädt fehlende Senderlogos im Hintergrund und aktualisiert danach die Liste.</summary>
    private async Task RefreshLogosAsync(List<Channel> channels)
    {
        bool downloaded = false;
        try
        {
            foreach (Channel channel in channels)
            {
                string local = _session.GetLogoPath(channel);
                if (local.Length == 0 || File.Exists(local))
                {
                    continue;
                }

                string? url = _session.Client.BuildLogoUrl(channel);
                if (string.IsNullOrEmpty(url))
                {
                    continue;
                }

                if (await TryDownloadLogoAsync(url, local))
                {
                    downloaded = true;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        if (downloaded)
        {
            // Liste neu aufbauen, damit die Logos erscheinen; Auswahl beibehalten.
            string? selectedId = (ChannelList.SelectedItem as ChannelItem)?.Channel.Id;
            List<ChannelItem> items = channels.Select(c => new ChannelItem(c)).ToList();
            ChannelList.ItemsSource = items;
            int index = selectedId is null ? -1 : items.FindIndex(i => i.Channel.Id == selectedId);
            ChannelList.SelectedIndex = index >= 0 ? index : 0;
        }
    }

    private async Task<bool> TryDownloadLogoAsync(string url, string localPath)
    {
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            byte[] bytes = await client.GetByteArrayAsync(url, _cts.Token);
            // Nur echte PNG-Dateien übernehmen (sonst zeigt das Bild nichts an).
            if (bytes.Length > 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            {
                File.WriteAllBytes(localPath, bytes);
                return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Aktualisiert Sender und EPG des **aktuellen** Bouquets, ohne die Auswahl zu
    /// verwerfen. Wird von der „EPG-Auto-Aktualisierung“ (beim Einblenden) und vom
    /// Knopf „EPG aktualisieren“ verwendet. Wie im Original
    /// (<c>_1af($ge[$i6][0])</c> + <c>_1ag()</c>) wird nur das aktuelle Bouquet
    /// neu geholt, nicht die ganze Bouquetliste.
    /// </summary>
    private async Task RefreshCurrentBouquetAsync()
    {
        if (BouquetCombo.SelectedItem is Bouquet bouquet && bouquet.Number.Length > 0)
        {
            await LoadChannelsAsync(bouquet.Number, preserveSelection: true);
            return;
        }

        await ReloadAsync();
    }

    private void SetStatus(string text, bool? connected = null)
    {
        Profile profile = _session.Profile;
        StatusText.Text = StatusLine(text, profile.Name);

        // Kurzinfo am Statuspunkt: welches Profil, welche Adresse, welche Schnittstelle.
        StatusText.ToolTip = "Profil: " + (profile.Name.Length == 0 ? "-" : profile.Name)
            + "\nBox:    " + profile.Host + ":" + profile.WebPort
            + "\nAPI:    " + BoxApis.DisplayName(profile.ApiVersion);

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

    /// <summary>Wählt beim Rechtsklick den Sender unter dem Cursor aus (für das Kontextmenü).</summary>
    private void OnChannelRightClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(ChannelList, e.OriginalSource as DependencyObject) is ListBoxItem item)
        {
            item.IsSelected = true;
        }
    }

    /// <summary>Zeigt das erweiterte EPG des markierten Senders (Rechtsklick → „Weiteres EPG“).</summary>
    private async void OnMoreEpg(object sender, RoutedEventArgs e)
    {
        Channel? channel = _selected;
        string? bouquet = (BouquetCombo.SelectedItem as Bouquet)?.Number;
        if (channel is null || string.IsNullOrEmpty(channel.Id) || string.IsNullOrEmpty(bouquet))
        {
            return;
        }

        try
        {
            List<EpgEvent> events = await _session.Client.GetChannelEpgAsync(channel.Id, bouquet, _cts.Token);
            EpgWindow window = new(channel.Id, channel.Name, events) { Owner = this };
            window.ShowDialog();
        }
        catch (OperationCanceledException)
        {
        }
    }

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
            // Nur bei "Umschalten erzwingen" die Box wirklich umschalten (zap).
            // Ohne Zwang streamt die Adresse den Sender direkt (id=<id> bzw.
            // GET /api/v1/stream/{id}); ein Zap würde den laufenden Sender der Box
            // ändern, was beim reinen Streamen nicht gewollt ist.
            if (_session.Profile.ForceSwitch)
            {
                await _session.Client.ZapToAsync(channel.Id, _cts.Token);
                await Task.Delay(500);
            }

            string arguments;
            string? liveUrl = _session.Client.ApiVersion == BoxApi.V1
                ? await _session.Client.GetStreamUrlAsync(channel.Id, _cts.Token)
                : null;

            if (liveUrl is not null)
            {
                // Neue API: die Box nennt die fertige Adresse (Port aus ihren Einstellungen).
                arguments = _session.VlcOptions.BuildOptionString() + " " + liveUrl;
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

    private void OnRefreshEpg(object sender, RoutedEventArgs e) => _ = RefreshCurrentBouquetAsync();

    private void OnVideo(object sender, RoutedEventArgs e) => OpenVideoWindow();

    private void OnSettings(object sender, RoutedEventArgs e) => OpenSettings();

    private void OnProfiles(object sender, RoutedEventArgs e) => OpenProfiles();

    private void OnGuide(object sender, RoutedEventArgs e) => OpenGuide();

    private void OnTimers(object sender, RoutedEventArgs e) => OpenTimers();

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

    private void OpenGuide()
    {
        string bouquet = (BouquetCombo.SelectedItem as Bouquet)?.Number ?? string.Empty;
        EpgGuideWindow window = new(bouquet) { Owner = this };
        window.ShowDialog();
    }

    private void OpenTimers()
    {
        TimersWindow window = new() { Owner = this };
        window.ShowDialog();
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
