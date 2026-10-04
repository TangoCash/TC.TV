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

    public VideoWindow()
    {
        InitializeComponent();
        ApplyLocalization();
        RecursiveBox.IsChecked = _session.Settings.RecursiveVideoDisplay;
        Loaded += async (_, _) => await LoadRootsAsync();
    }

    private void ApplyLocalization()
    {
        Title = Localization.T(30);
        RecordingsButton.Content = "Aufnahmen";
        MoviesButton.Content = "Videos";
        UpButton.Content = Localization.T(240);
        RefreshButton.Content = Localization.T(304);
        PlayButton.Content = Localization.T(242);
        CloseButton.Content = Localization.T(240);
        RecursiveBox.Content = Localization.T(320);
        RecursiveBox.ToolTip = Localization.T(36);
    }

    /// <summary>Lädt die Wurzelverzeichnisse (/control/getdir).</summary>
    private async Task LoadRootsAsync()
    {
        PathText.Text = _root == "moviedir" ? "Videos" : "Aufnahmen";
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
            MessageBox.Show(Localization.T(102), "TC.TV", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Die neue API (/api/v1/storage/file) verlangt einen kurzlebigen Medien-Token
        // in der Adresse; die alte API liefert die Datei direkt.
        string url = await _session.Client.GetFileUrlAsync(entry.FullPath, _cts.Token);
        string arguments = _session.VlcOptions.BuildOptionString(includeOneInstance: false) + " --one-instance " + url;
        VlcLauncher.Play(_session.VlcPath, arguments);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
