using System.Windows;
using System.Windows.Controls;
using TCTV.Core;
using Localization = TCTV.Core.Localization;

namespace TCTV.Views;

/// <summary>Ein Eintrag des Video-Browsers (Ordner oder Videodatei der Box).</summary>
public sealed class VideoEntry
{
    public VideoEntry(FileEntry entry)
    {
        Entry = entry;
    }

    public FileEntry Entry { get; }

    public string Name => Entry.Name;

    /// <summary>Absoluter Pfad auf der Box.</summary>
    public string FullPath => Entry.FullPath;

    public bool IsDirectory => Entry.IsDirectory;

    public string Display => IsDirectory ? "[ " + Name + " ]" : Name;

    public string SizeText => IsDirectory ? string.Empty : FormatSize(Entry.Size);

    public override string ToString() => Display;

    internal static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return string.Empty;
        }

        string[] units = { "B", "kB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString(unit == 0 ? "0" : "0.#", System.Globalization.CultureInfo.CurrentCulture) + " " + units[unit];
    }

    /// <summary>
    /// Vergleich für die Anzeige: Ordner zuerst, danach alphabetisch nach Name
    /// (ohne Beachtung von Groß-/Kleinschreibung).
    /// </summary>
    internal static int Compare(VideoEntry x, VideoEntry y)
    {
        if (x.IsDirectory != y.IsDirectory)
        {
            return x.IsDirectory ? -1 : 1;
        }

        return StringComparer.CurrentCultureIgnoreCase.Compare(x.Name, y.Name);
    }
}

/// <summary>
/// Video-Browser: listet Aufnahmen und Videos der Box und spielt sie per HTTP vom
/// Dateiserver der Box ab (kein FTP). Je nach Profil über die alte API
/// (/control/getdir, /control/getmovies, /control/file) oder die neue
/// (/api/v1/storage/files, /api/v1/storage/file).
/// </summary>
public partial class VideoWindow : Window
{
    private static readonly string[] VideoExtensions =
    {
        ".mkv", ".ts", ".avi", ".divx", ".mpg", ".mp4", ".mpeg",
    };

    private readonly AppSession _session = App.Session;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>"recordingdir" oder "moviedir".</summary>
    private string _root = "recordingdir";

    private List<string> _roots = new();

    /// <summary>Aktueller absoluter Pfad; leer = Wurzelauswahl.</summary>
    private string _path = string.Empty;
    private int _infoLoadVersion;

    public VideoWindow()
    {
        InitializeComponent();
        ApplyLocalization();
        RecursiveBox.IsChecked = _session.Settings.RecursiveVideoDisplay;
        Loaded += async (_, _) => await LoadRootsAsync();
    }

    private void ApplyLocalization()
    {
        Title = Localization.T("video");
        RecordingsButton.Content = Localization.T("recordings");
        MoviesButton.Content = Localization.T("videos");
        UpButton.Content = Localization.T("back");
        RefreshButton.Content = Localization.T("refresh_epg");
        PlayButton.Content = Localization.T("play");
        CloseButton.Content = Localization.T("back");
        RecursiveBox.Content = Localization.T("incl_subfolders");
        RecursiveBox.ToolTip = Localization.T("recursive_video_tooltip");
    }

    /// <summary>Lädt die Wurzelverzeichnisse (/control/getdir).</summary>
    private async Task LoadRootsAsync()
    {
        PathText.Text = Localization.T(_root == "moviedir" ? "videos" : "recordings");
        _roots = await _session.Client.GetVideoRootsAsync(_root, _cts.Token);
        _path = string.Empty;

        if (_roots.Count == 0)
        {
            SetEntries(new List<VideoEntry>());
            return;
        }

        if (_roots.Count == 1)
        {
            _path = _roots[0];
            await LoadPathAsync();
            return;
        }

        // Mehrere Wurzelverzeichnisse zur Auswahl anbieten.
        List<VideoEntry> entries = _roots
            .Select(root => new VideoEntry(new FileEntry { Name = root, FullPath = root, IsDirectory = true }))
            .ToList();
        SetEntries(entries);
    }

    /// <summary>Lädt den Inhalt des aktuellen Verzeichnisses.</summary>
    private async Task LoadPathAsync()
    {
        PathText.Text = _path;

        if (RecursiveBox.IsChecked == true)
        {
            // Alle Videos inkl. Unterordner auf einmal (/control/getmovies).
            List<FileEntry> movies = await _session.Client.GetMoviesAsync(_path, true, _cts.Token);
            SetEntries(movies.Where(m => IsVideo(m.Name)).Select(m => new VideoEntry(m)).ToList());
            return;
        }

        List<FileEntry> list = await _session.Client.GetFileListAsync(_path, _cts.Token);
        List<VideoEntry> entries = new();
        foreach (FileEntry entry in list)
        {
            if (entry.IsDirectory || IsVideo(entry.Name))
            {
                entries.Add(new VideoEntry(entry));
            }
        }

        SetEntries(entries);
    }

    private static bool IsVideo(string name)
        => VideoExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    private void SetEntries(List<VideoEntry> entries)
    {
        entries.Sort(VideoEntry.Compare);
        EntryList.ItemsSource = entries;
        if (entries.Count > 0)
        {
            EntryList.SelectedIndex = 0;
        }
    }

    private async void OnRecordings(object sender, RoutedEventArgs e)
    {
        _root = "recordingdir";
        await LoadRootsAsync();
    }

    private async void OnMovies(object sender, RoutedEventArgs e)
    {
        _root = "moviedir";
        await LoadRootsAsync();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_path))
        {
            await LoadRootsAsync();
        }
        else
        {
            await LoadPathAsync();
        }
    }

    private async void OnUp(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_path))
        {
            Close();
            return;
        }

        string current = _path.TrimEnd('/');

        // Sind wir in einem Wurzelverzeichnis, zurueck zur Wurzelauswahl.
        if (_roots.Any(r => string.Equals(r.TrimEnd('/'), current, StringComparison.Ordinal)))
        {
            await LoadRootsAsync();
            return;
        }

        int slash = current.LastIndexOf('/');
        if (slash <= 0)
        {
            await LoadRootsAsync();
            return;
        }

        _path = current[..slash];
        await LoadPathAsync();
    }

    private async void OnEntryActivated(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (EntryList.SelectedItem is not VideoEntry entry)
        {
            return;
        }

        if (entry.IsDirectory)
        {
            _path = entry.FullPath;
            await LoadPathAsync();
            return;
        }

        PlayEntry(entry);
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (EntryList.SelectedItem is VideoEntry { IsDirectory: false } entry)
        {
            PlayEntry(entry);
        }
    }

    /// <summary>Spielt eine Aufnahme per HTTP vom Dateiserver der Box ab.</summary>
    private async void PlayEntry(VideoEntry entry)
    {
        if (!_session.IsVlcAvailable)
        {
            MessageBox.Show(Localization.T("set_vlc_manually"), "TC.TV", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Die neue API (/api/v1/storage/file) verlangt einen kurzlebigen Medien-Token
        // in der Adresse; die alte API liefert die Datei direkt.
        string url = await _session.Client.GetFileUrlAsync(entry.FullPath, _cts.Token);
        string arguments = _session.VlcOptions.BuildOptionString(includeOneInstance: false) + " --one-instance " + url;
        VlcLauncher.Play(_session.VlcPath, arguments);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>Zeigt die Beschreibung der markierten Aufnahme in der Detailkarte.</summary>
    private void OnEntrySelected(object sender, SelectionChangedEventArgs e)
    {
        if (EntryList.SelectedItem is not VideoEntry entry || entry.IsDirectory)
        {
            DetailCard.Visibility = Visibility.Collapsed;
            return;
        }

        // Bis die Metadaten da sind, erst einmal den Dateinamen zeigen.
        DetailTitle.Text = entry.Name;
        DetailInfo.Text = string.Empty;
        DetailDescription.Text = string.Empty;
        DetailCard.Visibility = Visibility.Visible;

        _ = LoadRecordingInfoAsync(entry);
    }

    /// <summary>Lädt die .xml-Metadaten der Aufnahme (Titel, Sender, Beschreibung).</summary>
    private async Task LoadRecordingInfoAsync(VideoEntry entry)
    {
        int version = ++_infoLoadVersion;
        try
        {
            // Kurz warten, damit schnelles Durchschalten nicht jede Aufnahme lädt.
            await Task.Delay(200, _cts.Token);
            if (version != _infoLoadVersion)
            {
                return;
            }

            RecordingInfo? info = await _session.Client.GetRecordingInfoAsync(entry.FullPath, _cts.Token);
            if (version != _infoLoadVersion)
            {
                return;
            }

            if (info is null || !info.HasContent)
            {
                DetailTitle.Text = entry.Name;
                DetailInfo.Text = string.Empty;
                DetailDescription.Text = string.Empty;
                return;
            }

            DetailTitle.Text = info.Title.Length == 0 ? entry.Name : info.Title;

            string line = info.ChannelName;
            if (info.Info1.Length > 0)
            {
                line = line.Length == 0 ? info.Info1 : line + "   ·   " + info.Info1;
            }

            DetailInfo.Text = line;
            DetailDescription.Text = info.Description;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
