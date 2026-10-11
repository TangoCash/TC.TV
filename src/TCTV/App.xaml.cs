using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TCTV.Core;
using TCTV.Views;

namespace TCTV;

public partial class App : Application
{
    private Mutex? _instanceMutex;

    /// <summary>Zentrale Laufzeitdaten (Ersatz der globalen AutoIt-Variablen).</summary>
    public static AppSession Session { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Einzelinstanz - im Original über _yw("S.TV.exe", 1); dort läuft die
        // AutoIt-Fassung, hier die Portierung unter eigenem Namen.
        _instanceMutex = new Mutex(initiallyOwned: true, @"Local\TC.TV.Net", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        // Datenwurzel: %APPDATA%\TC.TV, per --datadir/TCTV_DATADIR verschiebbar.
        string? dataRoot = GetDataRoot(e.Args);
        if (!AppPaths.Initialize(dataRoot))
        {
            MessageBox.Show(
                "Das Arbeitsverzeichnis konnte nicht erstellt werden:" + Environment.NewLine +
                AppPaths.WorkDir + Environment.NewLine + Environment.NewLine +
                "Bitte Schreibrechte in diesem Ordner sicher stellen. Das Programm wird beendet.",
                "TC.TV",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        try
        {
            ResourceProvider.ExtractToTemp();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Die eingebetteten Ressourcen konnten nicht entpackt werden." + Environment.NewLine + ex.Message,
                "TC.TV",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        Session = AppSession.Create();

        // Farbsystem aus dem gewählten Theme ableiten (Brush.*-Ressourcen).
        ThemeManager.Apply(Session.Settings.Theme);

        // Fenster werden bewusst selbst verwaltet (Tray-Betrieb, Selbsttest, Rendering).
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (HasSelfTestArgument(e.Args))
        {
            RunSelfTest();
            return;
        }

        string? renderDirectory = GetRenderDirectory(e.Args);
        if (renderDirectory is not null)
        {
            RenderWindows(renderDirectory);
            return;
        }

        MainWindow window = new();
        MainWindow = window;
        window.Show();

        // Automatische Updateprüfung gegen die GitHub-Releases (abschaltbar
        // über "Updateprüfung" in den Einstellungen).
        if (Session.Settings.UpdateCheck)
        {
            _ = CheckForUpdatesAsync();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Session?.Dispose();

        if (Session?.VlcOptions.CloseVlcWithApp == true)
        {
            VlcLauncher.Shutdown();
        }

        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Beendet das Programm (Menüpunkt "Beenden").</summary>
    public void ExitApplication()
    {
        Shutdown();
    }

    /// <summary>Prüft im Hintergrund auf Updates und meldet eine neue Version.</summary>
    private async Task CheckForUpdatesAsync()
    {
        UpdateInfo? update;
        try
        {
            update = await UpdateChecker.CheckAsync();
        }
        catch (Exception)
        {
            return;
        }

        if (update is not null && update.IsNewer)
        {
            await NotifyUpdateAsync(update, MainWindow);
        }
    }

    /// <summary>Zeigt den Update-Hinweis und führt auf Wunsch Download und Installation aus.</summary>
    public static async Task NotifyUpdateAsync(UpdateInfo update, Window? owner = null)
    {
        if (!AskToDownload(update, owner))
        {
            return;
        }

        ReleaseAsset? asset = UpdateChecker.SelectAsset(update);
        if (asset is null)
        {
            Show(owner, TCTV.Core.Localization.T(330), TCTV.Core.Localization.T(324), MessageBoxButton.OK, MessageBoxImage.Warning);
            OpenReleasePage(update.ReleaseUrl);
            return;
        }

        string tempFile = Path.Combine(Path.GetTempPath(), $"TC.TV-{Guid.NewGuid():N}.exe");
        try
        {
            await UpdateChecker.DownloadAsync(asset, tempFile);
        }
        catch (Exception)
        {
            TryDelete(tempFile);
            Show(owner, TCTV.Core.Localization.T(234), TCTV.Core.Localization.T(233), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            if (AskToInstall(owner))
            {
                InstallAndRestart(tempFile);
            }
            else
            {
                OpenFolder(tempFile);
            }
        }
        catch (Exception)
        {
            OpenFolder(tempFile);
        }
    }

    private static bool AskToDownload(UpdateInfo update, Window? owner)
    {
        string message = string.Format(
            TCTV.Core.Localization.T(325),
            UpdateChecker.Display(update.Version),
            UpdateChecker.Display(UpdateChecker.CurrentVersion));

        if (!string.IsNullOrWhiteSpace(update.Changelog))
        {
            string changelog = update.Changelog.Trim();
            if (changelog.Length > 600)
            {
                changelog = changelog[..600] + "…";
            }

            message += Environment.NewLine + Environment.NewLine + changelog;
        }

        message += Environment.NewLine + Environment.NewLine + TCTV.Core.Localization.T(232);
        return Show(owner, message, TCTV.Core.Localization.T(324), MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes;
    }

    private static bool AskToInstall(Window? owner)
        => Show(owner, TCTV.Core.Localization.T(331), TCTV.Core.Localization.T(324), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private static MessageBoxResult Show(Window? owner, string message, string title, MessageBoxButton buttons, MessageBoxImage icon)
        => owner is null
            ? MessageBox.Show(message, title, buttons, icon)
            : MessageBox.Show(owner, message, title, buttons, icon);

    /// <summary>Ersetzt die laufende EXE nach dem Beenden und startet die neue Version.</summary>
    private static void InstallAndRestart(string downloadedFile)
    {
        string target = Environment.ProcessPath ?? string.Empty;
        if (target.Length == 0)
        {
            OpenFolder(downloadedFile);
            return;
        }

        string script = Path.Combine(Path.GetTempPath(), $"tctv-update-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(script, BuildUpdateScript());

        Process.Start(new ProcessStartInfo("powershell.exe")
        {
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" -ProcessId {Environment.ProcessId} -NewExe \"{downloadedFile}\" -TargetExe \"{target}\"",
            UseShellExecute = true,
        });

        if (Application.Current is App app)
        {
            app.ExitApplication();
        }
    }

    /// <summary>PowerShell-Helfer: wartet auf das Ende der App, ersetzt die EXE und startet neu.</summary>
    private static string BuildUpdateScript() =>
        @"param([int]$ProcessId, [string]$NewExe, [string]$TargetExe)
$ErrorActionPreference = 'SilentlyContinue'
while (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
    Start-Sleep -Milliseconds 500
}
Start-Sleep -Milliseconds 500
$installed = $false
for ($i = 0; $i -lt 20; $i++) {
    try {
        Copy-Item -LiteralPath $NewExe -Destination $TargetExe -Force -ErrorAction Stop
        $installed = $true
        break
    }
    catch {
        Start-Sleep -Milliseconds 500
    }
}
if ($installed) {
    Remove-Item -LiteralPath $NewExe -Force -ErrorAction SilentlyContinue
    Start-Process -FilePath $TargetExe
}
else {
    Start-Process explorer.exe -ArgumentList (Split-Path -Parent $NewExe)
}";

    private static void OpenFolder(string filePath)
    {
        string? folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Öffnet die Release-Seite im Standardbrowser.</summary>
    public static void OpenReleasePage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Browser konnte nicht geöffnet werden – bewusst still.
        }
    }

    /// <summary>Öffnet die IMDb-Suche für eine Sendung.</summary>
    public static void OpenImdb(string title)
        => OpenReleasePage("https://www.imdb.com/find?q=" + Uri.EscapeDataString(title) + "&s=all");

    /// <summary>Öffnet die TheTVDB-Suche für eine Sendung.</summary>
    public static void OpenTheTvdb(string title)
        => OpenReleasePage("https://thetvdb.com/search?query=" + Uri.EscapeDataString(title));

    private static bool HasSelfTestArgument(string[] args)
    {
        foreach (string argument in args)
        {
            if (string.Equals(argument, "--selftest", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Prüft ohne Anzeige, ob alle Fenster (XAML + Styles) fehlerfrei aufgebaut
    /// werden können, und schreibt das Ergebnis in den Datenordner
    /// (%APPDATA%\TC.TV\TC.TV_Workdir\selftest.log).
    /// </summary>
    private void RunSelfTest()
    {
        List<string> report = new();

        report.Add("TC.TV " + typeof(App).Assembly.GetName().Version);
        report.Add("Laufzeit:       " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        report.Add("Programmordner: " + AppPaths.ProgramDir);
        report.Add("Datenordner:    " + AppPaths.DataRoot +
                   (AppPaths.IsCustomDataRoot ? " (verschoben)" : string.Empty));
        if (AppPaths.MigratedFrom is { } migrated)
        {
            report.Add("Übernommen:     " + migrated);
        }

        report.Add(string.Empty);

        void Check(string name, Func<Window> factory)
        {
            try
            {
                Window window = factory();
                report.Add("OK     " + name);
                window.Close();
            }
            catch (Exception ex)
            {
                report.Add("FEHLER " + name + ": " + ex.GetType().Name + " - " + ex.Message);
            }
        }

        Check("MainWindow", () => new MainWindow());
        Check("SettingsWindow", () => new SettingsWindow());
        Check("ProfilesWindow", () => new ProfilesWindow());
        Check("VideoWindow", () => new VideoWindow());
        Check("EpgWindow", () => EpgWindow.CreateForSelfTest());
        Check("TimersWindow", () => TimersWindow.CreateForSelfTest());
        Check("EpgGuideWindow", () => EpgGuideWindow.CreateForSelfTest());
        Check("HelpWindow", () => new HelpWindow());
        Check("PromptWindow", () => PromptWindow.CreateForSelfTest());

        RunApiChecks(report);
        RunUpdateChecks(report);

        File.WriteAllLines(Path.Combine(AppPaths.WorkDir, "selftest.log"), report);
        Shutdown(report.Exists(line => line.StartsWith("FEHLER", StringComparison.Ordinal)) ? 1 : 0);
    }

    /// <summary>Prüft das Parsen der Timer-Antworten der Box (API-Format).</summary>
    private static void RunApiChecks(List<string> report)
    {
        // Antwort ohne Timer: die Box liefert <timer> trotzdem als Wurzelelement.
        const string emptyXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <timer>
            <config><pre_delay>0</pre_delay><post_delay>0</post_delay></config>
            <timer_list></timer_list>
            </timer>
            """;

        // Antwort mit einer Aufnahme.
        const string oneTimerXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <timer>
            <config><pre_delay>0</pre_delay><post_delay>0</post_delay></config>
            <timer_list>
            <timer><type>record</type><id>17</id><state>0</state><type_number>5</type_number>
            <alarm><normal><text>01.01.2024 20:15</text><date>01.01.2024</date><time>20:15</time><digits>1704136500</digits><day>1</day><month>1</month><year>2024</year><hour>20</hour><min>15</min></normal><safety><text>01.01.2024 20:10</text><date>01.01.2024</date><time>20:10</time><digits>1704136200</digits><day>1</day><month>1</month><year>2024</year><hour>20</hour><min>10</min></safety></alarm>
            <announce><normal><text>01.01.2024 20:15</text><date>01.01.2024</date><time>20:15</time><digits>1704136500</digits><day>1</day><month>1</month><year>2024</year><hour>20</hour><min>15</min></normal><safety><text>01.01.2024 20:10</text><date>01.01.2024</date><time>20:10</time><digits>1704136200</digits><day>1</day><month>1</month><year>2024</year><hour>20</hour><min>10</min></safety></announce>
            <stop><normal><text>01.01.2024 21:15</text><date>01.01.2024</date><time>21:15</time><digits>1704140100</digits><day>1</day><month>1</month><year>2024</year><hour>21</hour><min>15</min></normal></stop>
            <start><normal><text>01.01.2024 20:15</text><date>01.01.2024</date><time>20:15</time><digits>1704136500</digits><day>1</day><month>1</month><year>2024</year><hour>20</hour><min>15</min></normal></start>
            <repeat><count>-</count><number>0</number><text>once</text><weekdays></weekdays></repeat>
            <channel_id>361d03f300012b66</channel_id><channel_name>ZDF HD</channel_name><title>Testaufnahme</title>
            <recording_dir>/media/hdd</recording_dir><epg_id>361d03f300012b66</epg_id>
            </timer>
            </timer_list>
            </timer>
            """;

        const string idLines = "17 5 0 0 1704136500 1704136500 1704140100 361d03f300012b66\n"
                             + "18 2 0 0 1704150000 1704150000 1704153600 3f300012b66\n";

        void Expect(string name, bool condition, string detail = "")
            => report.Add((condition ? "OK     " : "FEHLER ") + name + (condition || detail.Length == 0 ? string.Empty : " (" + detail + ")"));

        List<TimerItem> empty = RecordingServiceClient.ParseTimersXml(emptyXml);
        Expect("API: leere Timerliste ohne Phantom-Timer", empty.Count == 0, "Anzahl=" + empty.Count);

        List<TimerItem> timers = RecordingServiceClient.ParseTimersXml(oneTimerXml);
        Expect("API: ein Timer aus XML erkannt", timers.Count == 1, "Anzahl=" + timers.Count);
        if (timers.Count == 1)
        {
            TimerItem timer = timers[0];
            Expect("API: Startzeit (Unix) gelesen", timer.AlarmTime == 1704136500, timer.AlarmTime.ToString());
            Expect("API: Endzeit (Unix) gelesen", timer.StopTime == 1704140100, timer.StopTime.ToString());
            Expect("API: Sender-ID gelesen", timer.ChannelId == "361d03f300012b66", timer.ChannelId);
            Expect("API: Titel gelesen", timer.Title == "Testaufnahme", timer.Title);
            Expect("API: laufende Aufnahme erkannt", timer.IsRunning(1704137000));
            Expect("API: geplanter Timer ist nicht laufend", !timer.IsRunning(1704130000));
            Expect("API: beendeter Timer ist nicht laufend", !timer.IsRunning(1704141000));
        }

        List<TimerItem> lines = RecordingServiceClient.ParseTimerLines(idLines);
        Expect("API: format=id liefert zwei Timer", lines.Count == 2, "Anzahl=" + lines.Count);
        if (lines.Count == 2)
        {
            Expect("API: format=id Startzeit", lines[0].AlarmTime == 1704136500, lines[0].AlarmTime.ToString());
            Expect("API: format=id Endzeit", lines[0].StopTime == 1704140100, lines[0].StopTime.ToString());
            Expect("API: format=id Sender-ID", lines[1].ChannelId == "3f300012b66", lines[1].ChannelId);
        }

        Expect("API: Sender-ID mit/ohne führende Nullen gleich",
            RecordingServiceClient.ChannelIdsMatch("3f300012b66", "03f300012b66"));
        Expect("API: verschiedene Sender-IDs unterscheiden sich",
            !RecordingServiceClient.ChannelIdsMatch("361d03f300012b66", "361d03f300012b67"));
        Expect("API: leere Sender-ID passt nicht", !RecordingServiceClient.ChannelIdsMatch("", "abc"));

        Expect("API: Transponder enthält Sender",
            RecordingServiceClient.TransponderContains("3f300012b66 ZDF HD\n361d03f300012b66 ARD", "361d03f300012b66"));
        Expect("API: Transponder ohne Sender",
            !RecordingServiceClient.TransponderContains("3f300012b66 ZDF HD", "361d03f300012b66"));
        Expect("API: leere Transponderliste", !RecordingServiceClient.TransponderContains(null, "361d03f300012b66"));

        // --- Video-/Dateilisten (Ersatz für FTP) ---
        const string dirsXml = """
            <dirs>
            <item>
            <dir>/mnt/filme</dir>
            </item>
            <item>
            <dir>/media/sda1/movies</dir>
            </item>
            </dirs>
            """;

        const string moviesXml = """
            <movies>
            <item>
            <title>Sample.mkv</title>
            <path>/media/sda1/movies/Sample.mkv</path>
            <size>1136242099</size>
            </item>
            <item>
            <title>Aufnahme 1.ts</title>
            <path>/mnt/filme/Aufnahme 1.ts</path>
            <size>941</size>
            </item>
            </movies>
            """;

        const string fileListXml = """
            <filelist>
            <item>
            <name>2024-01-01</name>
            <type_str>dir</type_str>
            <type>4</type>
            <fullname>/mnt/filme/2024-01-01</fullname>
            </item>
            <item>
            <name>Aufnahme 1.ts</name>
            <type_str>file</type_str>
            <type>8</type>
            <fullname>/mnt/filme/Aufnahme 1.ts</fullname>
            <size>941</size>
            </item>
            </filelist>
            """;

        List<string> roots = RecordingServiceClient.ParseDirectoriesXml(dirsXml);
        Expect("API: getdir liefert zwei absolute Wurzelpfade", roots.Count == 2, "Anzahl=" + roots.Count);
        Expect("API: getdir Pfad korrekt", roots.Count == 2 && roots[0] == "/mnt/filme", roots.Count == 2 ? roots[0] : "-");

        List<FileEntry> movies = RecordingServiceClient.ParseMoviesXml(moviesXml);
        Expect("API: getmovies liefert zwei Videos", movies.Count == 2, "Anzahl=" + movies.Count);
        if (movies.Count == 2)
        {
            Expect("API: getmovies absoluter Pfad", movies[1].FullPath == "/mnt/filme/Aufnahme 1.ts", movies[1].FullPath);
            Expect("API: getmovies Groesse", movies[0].Size == 1136242099, movies[0].Size.ToString());
            Expect("API: getmovies kein Ordner", !movies[0].IsDirectory);
        }

        List<FileEntry> listed = RecordingServiceClient.ParseFileListXml(fileListXml);
        Expect("API: file-Liste liefert zwei Eintraege", listed.Count == 2, "Anzahl=" + listed.Count);
        if (listed.Count == 2)
        {
            Expect("API: file-Liste erkennt Ordner", listed[0].IsDirectory);
            Expect("API: file-Liste absoluter Pfad", listed[1].FullPath == "/mnt/filme/Aufnahme 1.ts", listed[1].FullPath);
            Expect("API: file-Liste erkennt Datei", !listed[1].IsDirectory);
        }

        Expect("API: leere Antwort liefert leere Liste",
            RecordingServiceClient.ParseFileListXml("<filelist></filelist>").Count == 0);

        // --- Zeichensatz: die Box liefert UTF-8 (Umlaute!) ---
        Expect("API: UTF-8-Umlaute werden korrekt dekodiert",
            RecordingServiceClient.DecodeText(System.Text.Encoding.UTF8.GetBytes("März – Entführung")) == "März – Entführung");
        Expect("API: Latin-1-Antwort wird als Fallback gelesen",
            RecordingServiceClient.DecodeText(new byte[] { 0x4D, 0xE4, 0x72, 0x7A }) == "März");
        Expect("API: leere Antwort dekodiert zu leerem Text",
            RecordingServiceClient.DecodeText(Array.Empty<byte>()) == string.Empty);

        const string bouquetXml = """
            <bouquet>
            <channel>
            <number>1</number>
            <id>361d03f300012b66</id>
            <name>ZDFinfo HD</name>
            <firstEPG>
            <description>Am 25. März 1996 wird Tabak-Erbe Jan Philipp Reemtsma entführt</description>
            <info1>Der Fall Reemtsma</info1>
            <startTime>21:00</startTime>
            <timeTotal>45</timeTotal>
            </firstEPG>
            </channel>
            </bouquet>
            """;

        byte[] bouquetBytes = System.Text.Encoding.UTF8.GetBytes(bouquetXml);
        List<Channel> parsed = RecordingServiceClient.ParseBouquet(RecordingServiceClient.DecodeText(bouquetBytes));
        Expect("API: EPG-Umlaute überstehen die ganze Kette",
            parsed.Count == 1 && parsed[0].FirstEpg.Description.Contains("März") && parsed[0].FirstEpg.Description.Contains("entführt"),
            parsed.Count == 1 ? parsed[0].FirstEpg.Description : "kein Sender");

        // Die INI enthält selbst Umlaute in Schlüsselnamen (z. B. "Verzögerung").
        string iniPath = Path.Combine(AppPaths.TempDir, "encoding-test.ini");
        IniFile ini = new(iniPath);
        ini.Write("Optionen", "fade-in-Verzögerung in Millisekunden", "250");
        ini.Write("Profildaten", "Startverzögerung", "3");
        ini.Save();
        IniFile reloaded = new(iniPath);
        Expect("INI: Umlaut-Schlüssel überstehen Speichern/Laden",
            reloaded.Read("Optionen", "fade-in-Verzögerung in Millisekunden", string.Empty) == "250"
            && reloaded.Read("Profildaten", "Startverzögerung", string.Empty) == "3");
        File.Delete(iniPath);

        // --- Fortschrittsbalken der laufenden Sendung ---
        double Near(double value, double expected) => Math.Abs(value - expected) < 0.001 ? 1 : 0;

        Expect("EPG: Fortschritt aus percentage (0..100)",
            Near(ChannelItem.ComputeProgress(new EpgEvent { Percentage = "50", TimeTotal = "45" }, TimeSpan.Zero), 0.5) == 1);
        Expect("EPG: Fortschritt aus timeElapsed/timeTotal",
            Near(ChannelItem.ComputeProgress(new EpgEvent { TimeElapsed = "15", TimeTotal = "45" }, TimeSpan.Zero), 1.0 / 3) == 1);
        Expect("EPG: Fortschritt aus Startzeit gegen Uhrzeit",
            Near(ChannelItem.ComputeProgress(new EpgEvent { StartTime = "21:00", TimeTotal = "60" }, new TimeSpan(21, 30, 0)), 0.5) == 1);
        Expect("EPG: Fortschritt über Mitternacht",
            Near(ChannelItem.ComputeProgress(new EpgEvent { StartTime = "23:50", TimeTotal = "30" }, new TimeSpan(0, 5, 0)), 0.5) == 1);
        Expect("EPG: 120 % werden auf 100 % begrenzt",
            ChannelItem.ComputeProgress(new EpgEvent { Percentage = "120", TimeTotal = "45" }, TimeSpan.Zero) == 1.0);
        Expect("EPG: ohne bekannte Dauer kein Fortschritt",
            ChannelItem.ComputeProgress(new EpgEvent { Percentage = "100", TimeTotal = "0" }, TimeSpan.Zero) == 0);

        Channel channelWithEpg = new()
        {
            Name = "Test",
            FirstEpg = new EpgEvent { Percentage = "0", TimeElapsed = "0", TimeTotal = "45" },
        };
        Expect("EPG: Balken erscheint schon bei 0 %", new ChannelItem(channelWithEpg).HasProgress);
        Expect("EPG: kein Balken ohne Sendungsdauer",
            !new ChannelItem(new Channel { Name = "Test", FirstEpg = new EpgEvent() }).HasProgress);

        // --- Design: Dark orange (neutrino-images) ist das Standardthema ---
        Expect("Theme: leerer Schlüssel ergibt Dark orange",
            Theme.FromIniValue(string.Empty).IniValue == "3");
        Expect("Theme: unbekannter Wert ergibt Dark orange",
            Theme.FromIniValue("99").IniValue == "3");
        Expect("Theme: Standard nutzt die NI-Farben (Wurzelzeile 2)",
            Theme.Default.RowIndex == 2);
        Expect("Theme: NI-Akzent #FD6D13",
            Theme.Default.Color2 == System.Windows.Media.Color.FromRgb(0xFD, 0x6D, 0x13));
        Expect("Theme: NI-Hintergrund #212121",
            Theme.Default.Background == System.Windows.Media.Color.FromRgb(0x21, 0x21, 0x21));
        Expect("Theme: NI-Fläche #424242",
            Theme.Default.Color1 == System.Windows.Media.Color.FromRgb(0x42, 0x42, 0x42));
        Expect("Theme: NI-Rahmen #616161",
            Theme.Default.Color6 == System.Windows.Media.Color.FromRgb(0x61, 0x61, 0x61));
        Expect("Theme: NI-Text #E0E0E0",
            Theme.Default.Color10 == System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0));
        Expect("Theme: NI-Sekundärtext #9E9E9E",
            Theme.Default.Text == System.Windows.Media.Color.FromRgb(0x9E, 0x9E, 0x9E));
        Expect("Theme: '2' bleibt Dark grün",
            Theme.FromIniValue("2").RowIndex == 1);
        Expect("Theme: '4' ist Hell",
            Theme.FromIniValue("4").RowIndex == 0 && !Theme.FromIniValue("4").DarkShell);
        Expect("Theme: '1' bleibt TC.TV-Overlay",
            Theme.FromIniValue("1").DarkShell && Theme.FromIniValue("1").Opacity == 50);

        // --- Pfade: Benutzerdaten liegen im Profil, nicht neben der EXE ---
        Expect("Pfade: Vorgabe ist %APPDATA%\\TC.TV",
            string.Equals(AppPaths.DefaultDataRoot,
                Path.Combine(AppPaths.AppDataDir, "TC.TV"), StringComparison.OrdinalIgnoreCase));
        Expect("Pfade: Datenwurzel liegt nicht neben der EXE",
            !string.Equals(AppPaths.DataRoot, AppPaths.ProgramDir, StringComparison.OrdinalIgnoreCase) &&
            !AppPaths.DataRoot.StartsWith(AppPaths.ProgramDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Expect("Pfade: Arbeitsordner liegt unterhalb der Datenwurzel",
            AppPaths.WorkDir.StartsWith(AppPaths.DataRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Expect("Pfade: Temp/Logos/VLCPortable liegen im Arbeitsordner",
            AppPaths.TempDir.StartsWith(AppPaths.WorkDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            AppPaths.LogosDir.StartsWith(AppPaths.WorkDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            AppPaths.VlcPortableDir.StartsWith(AppPaths.WorkDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Expect("Pfade: INI liegt im Arbeitsordner",
            string.Equals(AppPaths.IniPath, Path.Combine(AppPaths.WorkDir, "TC.TV.ini"), StringComparison.OrdinalIgnoreCase));
        Expect("Pfade: INI und Logos wurden angelegt",
            File.Exists(AppPaths.IniPath) && Directory.Exists(AppPaths.LogosDir));

        // --- Profilfenster: "Speichern" schließt und lädt das Profil neu ---
        try
        {
            ProfilesWindow window = new();
            bool applied = window.ApplyAndActivate();

            Expect("ProfilesWindow: Speichern übernimmt das Profil", applied);
            Expect("ProfilesWindow: übernommenes Profil steht in der INI",
                App.Session.Settings.ProfileFileName.EndsWith(ProfileStore.Extension, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    App.Session.Ini.Read("Benutzerdaten", "Profil", string.Empty),
                    App.Session.Settings.ProfileFileName,
                    StringComparison.OrdinalIgnoreCase));
            Expect("ProfilesWindow: Profildatei existiert",
                File.Exists(App.Session.Profiles.GetPath(
                    App.Session.Settings.ProfileFileName[..^ProfileStore.Extension.Length])));
            Expect("ProfilesWindow: Fenster wurde geschlossen", !window.IsVisible);

            // Das Hauptfenster ruft bei DialogResult == true ReloadProfile() auf.
            App.Session.ReloadProfile();
            Expect("ProfilesWindow: gespeichertes Profil ist danach geladen",
                string.Equals(
                    App.Session.Profile.Name,
                    App.Session.Settings.ProfileFileName[..^ProfileStore.Extension.Length],
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    App.Session.Client.Profile.Host,
                    App.Session.Profile.Host,
                    StringComparison.OrdinalIgnoreCase));

            // Die Auswahl "API" im Fenster muss in der Profildatei ankommen.
            ProfilesWindow apiWindow = new();
            apiWindow.SelectApiForSelfTest(BoxApi.V1);
            bool apiApplied = apiWindow.ApplyAndActivate();
            Profile written = App.Session.Profiles.Load(
                App.Session.Settings.ProfileFileName[..^ProfileStore.Extension.Length]);
            Expect("ProfilesWindow: API-Auswahl \"neue API\" wird gespeichert",
                apiApplied && written.ApiVersion == BoxApi.V1, written.ApiVersion.ToString());

            App.Session.ReloadProfile();
            Expect("ProfilesWindow: danach spricht der Client die neue API",
                App.Session.Client.ApiVersion == BoxApi.V1, App.Session.Client.ApiVersion.ToString());

            ProfilesWindow backWindow = new();
            backWindow.SelectApiForSelfTest(BoxApi.Legacy);
            backWindow.ApplyAndActivate();
            App.Session.ReloadProfile();
            Expect("ProfilesWindow: zurueck auf die alte API",
                App.Session.Client.ApiVersion == BoxApi.Legacy, App.Session.Client.ApiVersion.ToString());
        }
        catch (Exception ex)
        {
            Expect("ProfilesWindow: Speichern ohne Fehler (" + ex.GetType().Name + ": " + ex.Message + ")", false);
        }

        // --- HTTP-Wiedergabe-URL (kein FTP) ---
        Profile profile = new() { Host = "192.168.178.222", WebPort = "80" };
        using (RecordingServiceClient client = new(profile))
        {
            string url = client.BuildHttpFileUrl("/mnt/filme/Aufnahme 1.ts");
            Expect("API: HTTP-URL mit kodierten Leerzeichen",
                url == "http://192.168.178.222:80/mnt/filme/Aufnahme%201.ts", url);
            Expect("API: HTTP-URL ergaenzt fuehrenden Slash",
                client.BuildHttpFileUrl("mnt/filme/x.mkv") == "http://192.168.178.222:80/mnt/filme/x.mkv",
                client.BuildHttpFileUrl("mnt/filme/x.mkv"));
            Expect("API: keine FTP-URL im Client",
                !url.StartsWith("ftp", StringComparison.OrdinalIgnoreCase));
        }

        // --- EPG-Auto-Aktualisierung beim Einblenden (Optionen\fade-in-Auto-Aktualisierung) ---
        Expect("EPG-Auto-Aktualisierung: Standard ist aus", !App.Session.Settings.FadeInAutoRefresh);
        Expect("EPG-Auto-Aktualisierung: Sprachzeile 51 ist der Tooltip-Text",
            TCTV.Core.Localization.T(51).Contains("EPG"), TCTV.Core.Localization.T(51));
        Expect("EPG-Auto-Aktualisierung: Sprachzeile 51 ist nicht die Beschriftung",
            TCTV.Core.Localization.T(51) != TCTV.Core.Localization.T(50));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeSpan interval = TCTV.Views.MainWindow.AutoRefreshInterval;
        Expect("EPG-Auto-Aktualisierung: aus => kein Nachladen",
            !TCTV.Views.MainWindow.ShouldAutoRefresh(false, now.AddMinutes(-5), now));
        Expect("EPG-Auto-Aktualisierung: frische Daten => kein Nachladen",
            !TCTV.Views.MainWindow.ShouldAutoRefresh(true, now.Add(-interval + TimeSpan.FromSeconds(1)), now));
        Expect("EPG-Auto-Aktualisierung: genau " + interval.TotalSeconds + " s => Nachladen",
            TCTV.Views.MainWindow.ShouldAutoRefresh(true, now.Add(-interval), now));
        Expect("EPG-Auto-Aktualisierung: aeltere Daten => Nachladen",
            TCTV.Views.MainWindow.ShouldAutoRefresh(true, now.AddMinutes(-5), now));
        Expect("EPG-Auto-Aktualisierung: noch nie geladen => Nachladen",
            TCTV.Views.MainWindow.ShouldAutoRefresh(true, DateTimeOffset.MinValue, now));
        Expect("EPG-Auto-Aktualisierung: Abstand ist 60 s",
            interval == TimeSpan.FromSeconds(60), interval.ToString());

        // --- Fade-in/out: Ausblenden anhand der Mausposition (rechter/linker Rand) ---
        Rect fadeArea = new(0, 0, 1920, 1080);
        Expect("Fade: rechter Rand – Maus über dem Fenster bleibt sichtbar",
            !TCTV.Views.MainWindow.ShouldHide(new Point(1600, 540), fadeArea, 480, rightEdge: true));
        Expect("Fade: rechter Rand – Maus links vom Fenster blendet aus",
            TCTV.Views.MainWindow.ShouldHide(new Point(1300, 540), fadeArea, 480, rightEdge: true));
        Expect("Fade: linker Rand – Maus über dem Fenster bleibt sichtbar",
            !TCTV.Views.MainWindow.ShouldHide(new Point(240, 540), fadeArea, 480, rightEdge: false));
        Expect("Fade: linker Rand – Maus rechts vom Fenster blendet aus",
            TCTV.Views.MainWindow.ShouldHide(new Point(600, 540), fadeArea, 480, rightEdge: false));
        Expect("Fade: außerhalb in der Höhe blendet aus",
            TCTV.Views.MainWindow.ShouldHide(new Point(1600, 1200), fadeArea, 480, rightEdge: true));

        // --- Video-Browser: alphabetische Sortierung (Ordner zuerst) ---
        VideoEntry dirA = new(new FileEntry { Name = "aaa", IsDirectory = true });
        VideoEntry dirB = new(new FileEntry { Name = "BBB", IsDirectory = true });
        VideoEntry fileA = new(new FileEntry { Name = "aaa.mp4" });
        VideoEntry fileZ = new(new FileEntry { Name = "ZZZ.ts" });

        Expect("Video: Ordner stehen vor Dateien", VideoEntry.Compare(dirB, fileA) < 0);
        Expect("Video: Ordner alphabetisch sortiert", VideoEntry.Compare(dirA, dirB) < 0);
        Expect("Video: Dateien alphabetisch sortiert", VideoEntry.Compare(fileA, fileZ) < 0);
        Expect("Video: gleicher Name (Groß/Klein) gilt als gleich",
            VideoEntry.Compare(dirA, new VideoEntry(new FileEntry { Name = "AAA", IsDirectory = true })) == 0);

        // --- Senderlogos: Adresse ableiten ---
        Profile legacyLogoProfile = new() { Host = "192.168.178.222", WebPort = "80" };
        using (RecordingServiceClient legacyLogoClient = new(legacyLogoProfile))
        {
            Expect("Logo: alte API – Pfad aus dem Logo-Feld",
                legacyLogoClient.BuildLogoUrl(new Channel { Id = "abc", Logo = "/logos/x.png" }) == "http://192.168.178.222:80/logos/x.png");
            Expect("Logo: alte API – ohne Logo-Feld null",
                legacyLogoClient.BuildLogoUrl(new Channel { Id = "abc" }) is null);
        }

        Profile v1LogoProfile = new() { Host = "192.168.178.222", WebPort = "80", ApiVersion = BoxApi.V1 };
        using (RecordingServiceClient v1LogoClient = new(v1LogoProfile))
        {
            Expect("Logo: neue API – Logo-Endpunkt",
                v1LogoClient.BuildLogoUrl(new Channel { Id = "bb88043700016d66" }) == "http://192.168.178.222:80/api/v1/channels/bb88043700016d66/logo");
            Expect("Logo: ohne Sender-ID null",
                v1LogoClient.BuildLogoUrl(new Channel()) is null);
        }

        // --- EPG-Zeit für Aufnahme-Timer (Start/Ende laut EPG) ---
        EpgEvent unixEvent = new() { StartUnix = 1704136500, DurationSeconds = 2700 };
        DateTime? unixStart = unixEvent.StartDateTime();
        DateTime? unixEnd = unixEvent.EndDateTime();
        Expect("EPG-Zeit: Ende = Start + Dauer (Unix)",
            unixStart is not null && unixEnd is not null && unixEnd.Value - unixStart.Value == TimeSpan.FromSeconds(2700));

        EpgEvent textEvent = new() { StartTime = "20:15", TimeTotal = "45" };
        DateTime? textStart = textEvent.StartDateTime();
        DateTime? textEnd = textEvent.EndDateTime();
        Expect("EPG-Zeit: Ende = Start + Dauer (Text)",
            textStart is not null && textEnd is not null && textEnd.Value - textStart.Value == TimeSpan.FromMinutes(45));

        try
        {
            SettingsWindow settingsWindow = new();
            (string label, string tip, bool check) = settingsWindow.AutoRefreshInfoForSelfTest;
            Expect("SettingsWindow: Option ist beschriftet und hat einen Tooltip",
                label == TCTV.Core.Localization.T(50) && tip == TCTV.Core.Localization.T(51), label + " / " + tip);
            Expect("SettingsWindow: Option uebernimmt den Ini-Wert", check == App.Session.Settings.FadeInAutoRefresh);
        }
        catch (Exception ex)
        {
            Expect("SettingsWindow: Option lesbar (" + ex.GetType().Name + ": " + ex.Message + ")", false);
        }

        // --- Kopfzeile: Statusmeldung samt Profilname ---
        Expect("Kopfzeile: Statuszeile nennt das Profil",
            TCTV.Views.MainWindow.StatusLine("Verbunden!", "Box100") == "Verbunden! · Box100",
            TCTV.Views.MainWindow.StatusLine("Verbunden!", "Box100"));
        Expect("Kopfzeile: ohne Profilname bleibt die Meldung unveraendert",
            TCTV.Views.MainWindow.StatusLine("Verbunden!", string.Empty) == "Verbunden!"
            && TCTV.Views.MainWindow.StatusLine("Verbunden!", null) == "Verbunden!");
        Expect("Kopfzeile: auch \"Nicht verbunden\" nennt das Profil",
            TCTV.Views.MainWindow.StatusLine(TCTV.Core.Localization.T(99), "Box100")
                == TCTV.Core.Localization.T(99) + " · Box100");

        try
        {
            MainWindow headerWindow = new() { AllowClose = true };
            headerWindow.LoadDemoChannels();
            (string headerText, string headerTip) = headerWindow.StatusInfoForSelfTest;

            string profileName = App.Session.Profile.Name;
            string expectedHeader = profileName.Length > 0
                ? TCTV.Core.Localization.T(100) + " · " + profileName
                : TCTV.Core.Localization.T(100);
            Expect("Kopfzeile: Fenster zeigt die Meldung mit Profilnamen",
                headerText == expectedHeader, headerText);
            Expect("Kopfzeile: Kurzinfo nennt Box-Adresse und Schnittstelle",
                headerTip.Contains(App.Session.Profile.Host)
                && headerTip.Contains(":" + App.Session.Profile.WebPort)
                && headerTip.Contains(BoxApis.DisplayName(App.Session.Profile.ApiVersion)),
                headerTip.Replace("\n", " | "));

            headerWindow.Close();
        }
        catch (Exception ex)
        {
            Expect("Kopfzeile: Fenster lesbar (" + ex.GetType().Name + ": " + ex.Message + ")", false);
        }

        // --- Neue API (/api/v1/): Auswahl im Profil ---
        Expect("API: ohne Angabe bleibt es bei /control/", BoxApis.Parse(string.Empty) == BoxApi.Legacy);
        Expect("API: \"neu\" wählt /api/v1/", BoxApis.Parse("neu") == BoxApi.V1);
        Expect("API: \"v1\" wählt /api/v1/", BoxApis.Parse("v1") == BoxApi.V1);
        Expect("API: \"alt\" bleibt /control/", BoxApis.Parse("alt") == BoxApi.Legacy);

        string apiProfilePath = Path.Combine(AppPaths.TempDir, "api-version.conf3");
        new Profile { Name = "api-version", Host = "192.168.178.222", ApiVersion = BoxApi.V1 }.Save(apiProfilePath);
        IniFile apiIni = new(apiProfilePath);
        Profile apiReloaded = Profile.Load(apiProfilePath, "api-version");
        Expect("Profil: API-Version wird als \"neu\" gespeichert",
            apiIni.Read("Profildaten", "API-Version", string.Empty) == "neu");
        Expect("Profil: API-Version wird als \"neu\" gelesen", apiReloaded.ApiVersion == BoxApi.V1);

        new Profile { Name = "api-version", Host = "192.168.178.222", ApiVersion = BoxApi.Legacy }.Save(apiProfilePath);
        IniFile legacyIni = new(apiProfilePath);
        Expect("Profil: alte API wird als \"alt\" gespeichert und gelesen",
            legacyIni.Read("Profildaten", "API-Version", string.Empty) == "alt"
            && Profile.Load(apiProfilePath, "api-version").ApiVersion == BoxApi.Legacy);
        File.Delete(apiProfilePath);

        Profile v1Host = new() { Host = "192.168.178.222", WebPort = "80", ApiVersion = BoxApi.V1 };
        using (RecordingServiceClient v1Client = new(v1Host))
        {
            Expect("neue API: Profil nutzt die neue Schnittstelle", v1Client.ApiVersion == BoxApi.V1);
            Expect("neue API: Datei-Adresse zeigt auf /api/v1/storage/file",
                v1Client.BuildHttpFileUrl("/media/hdd/x.ts")
                    == "http://192.168.178.222:80/api/v1/storage/file?path=%2Fmedia%2Fhdd%2Fx.ts",
                v1Client.BuildHttpFileUrl("/media/hdd/x.ts"));
            Expect("neue API: Live-Adresse bleibt am Streaming-Port",
                v1Client.BuildVlcStreamUrl("361d03f300012b66") == "http://192.168.178.222:31339/id=361d03f300012b66",
                v1Client.BuildVlcStreamUrl("361d03f300012b66"));
        }

        // --- Neue API: Senderliste und EPG-Raster ---
        const string v1Channels = """
            {"items":[
            {"id":"3f300012b66","number":1,"name":"Das Erste HD","kind":"tv","scrambled":false,"locked":false,
             "satellite_position":192,"freq_id":1101,"transport_stream_id":1101,"original_network_id":1},
            {"id":"361d03f300012b66","number":2,"name":"ZDF HD","kind":"tv","scrambled":false,"locked":false,
             "satellite_position":192,"freq_id":1102,"transport_stream_id":1102,"original_network_id":1}],
            "next_cursor":"361d03f300012b66"}
            """;

        const string v1Grid = """
            {"items":[
            {"channel_id":"361d03f300012b66","truncated":false,"events":[
            {"id":"5c9","channel_id":"361d03f300012b66","title":"Tagesschau","description":"kurz","start":1704136500,"duration":2700},
            {"id":"5ca","channel_id":"361d03f300012b66","title":"heute journal","description":"","start":1704139200,"duration":1800}]}],
            "next_cursor":"361d03f300012b66"}
            """;

        List<Channel> v1List = NeutrinoV1Api.ParseChannels(v1Channels);
        Expect("neue API: /channels liefert zwei Sender", v1List.Count == 2, "Anzahl=" + v1List.Count);
        if (v1List.Count == 2)
        {
            Expect("neue API: Sendernummer als Text", v1List[1].Number == "2", v1List[1].Number);
            Expect("neue API: Sendername", v1List[1].Name == "ZDF HD", v1List[1].Name);
            Expect("neue API: Logo-Kurz-ID sind die unteren 48 Bit",
                v1List[1].ShortId == "03f300012b66", v1List[1].ShortId);
        }

        Dictionary<string, string> v1Transponder = NeutrinoV1Api.ParseChannelTransponders(v1Channels);
        Expect("neue API: Transponder je Sender erkannt",
            v1Transponder.Count == 2
            && v1Transponder[NeutrinoV1Api.NormalizeChannelId("361d03f300012b66")] == "192:1102:1102:1",
            v1Transponder.Count.ToString());

        Expect("neue API: Sender-IDs werden normalisiert",
            NeutrinoV1Api.NormalizeChannelId("03f300012b66") == NeutrinoV1Api.NormalizeChannelId("3f300012b66"));

        const string v1Bouquets = "{\"items\":[{\"id\":1,\"name\":\"Favoriten\",\"tv_count\":2}]}";
        List<Bouquet> v1BouquetsParsed = NeutrinoV1Api.ParseBouquets(v1Bouquets);
        Expect("neue API: /bouquets liefert das Bouquet",
            v1BouquetsParsed.Count == 1 && v1BouquetsParsed[0].Number == "1" && v1BouquetsParsed[0].Name == "Favoriten",
            v1BouquetsParsed.Count.ToString());

        const long v1Now = 1704137400;   // 15 Minuten nach Beginn der "Tagesschau" (2700 s)
        Dictionary<string, List<EpgEvent>> v1Guide = NeutrinoV1Api.ParseEpgGrid(v1Grid, v1Now);
        List<Channel> v1EpgList = NeutrinoV1Api.ParseChannels(v1Channels);
        NeutrinoV1Api.ApplyEpg(v1EpgList, v1Guide, v1Now);
        Expect("neue API: laufende Sendung wird erkannt",
            v1EpgList[1].FirstEpg.Description == "Tagesschau" && v1EpgList[1].FirstEpg.TotalMinutes == 45,
            v1EpgList[1].FirstEpg.Description);
        Expect("neue API: zweite Sendung ist die folgende",
            v1EpgList[1].SecondEpg.Description == "heute journal", v1EpgList[1].SecondEpg.Description);
        Expect("neue API: Startzeit der Sendung als Uhrzeit",
            v1EpgList[1].FirstEpg.StartTime
                == DateTimeOffset.FromUnixTimeSeconds(1704136500).ToLocalTime()
                    .ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            v1EpgList[1].FirstEpg.StartTime);
        Expect("neue API: Fortschritt aus dem EPG-Raster (33 %)",
            Near(ChannelItem.ComputeProgress(v1EpgList[1].FirstEpg, TimeSpan.Zero), 0.33) == 1,
            v1EpgList[1].FirstEpg.Percentage);
        Expect("neue API: bereits gelaufene Zeit der Sendung",
            v1EpgList[1].FirstEpg.TimeElapsed == "15", v1EpgList[1].FirstEpg.TimeElapsed);
        Expect("neue API: Sender ohne Sendungen bleibt leer", v1EpgList[0].FirstEpg.IsEmpty);

        List<Channel> v1Upcoming = NeutrinoV1Api.ParseChannels(v1Channels);
        NeutrinoV1Api.ApplyEpg(v1Upcoming, v1Guide, 1704136400);   // noch vor der ersten Sendung
        Expect("neue API: ohne laufende Sendung zeigt der Balken die nächste",
            v1Upcoming[1].FirstEpg.Description == "Tagesschau", v1Upcoming[1].FirstEpg.Description);

        // --- Neue API: Timer, laufende Aufnahmen, Dateien, Einstellungen ---
        const string v1Timers = """
            {"items":[{"id":17,"kind":"record","channel_id":"361d03f300012b66","start":1704136500,"stop":1704140100,
            "title":"Testaufnahme","repeat":0,"repeat_count":0,"state":2,"announce":1704136200,
            "epg_id":"5c9","epg_start":1704136500,"standby_on":false,"recording_dir":"/media/hdd"}]}
            """;

        List<TimerItem> v1TimersParsed = NeutrinoV1Api.ParseTimers(v1Timers,
            key => key == NeutrinoV1Api.NormalizeChannelId("361d03f300012b66") ? "ZDF HD" : string.Empty);
        Expect("neue API: /timers liefert einen Timer", v1TimersParsed.Count == 1, "Anzahl=" + v1TimersParsed.Count);
        if (v1TimersParsed.Count == 1)
        {
            Expect("neue API: Timer-Nummer", v1TimersParsed[0].Id == "17", v1TimersParsed[0].Id);
            Expect("neue API: Timer-Art", v1TimersParsed[0].Type == "record", v1TimersParsed[0].Type);
            Expect("neue API: Timer-Zeiten in Unix-Sekunden",
                v1TimersParsed[0].AlarmTime == 1704136500 && v1TimersParsed[0].StopTime == 1704140100);
            Expect("neue API: kompletter Timer laeuft jetzt",
                v1TimersParsed[0].IsRunning(1704137400) && !v1TimersParsed[0].IsRunning(1704140200));
            Expect("neue API: Sendername des Timers",
                v1TimersParsed[0].ChannelName == "ZDF HD", v1TimersParsed[0].ChannelName);
        }

        const string v1Recordings = """
            {"items":[{"id":17,"channel_id":"361d03f300012b66","title":"Testaufnahme","start":1704136500,
            "path":"/media/hdd/Testaufnahme.ts","size":0,"timeshift":false,"started_by":"timer"}]}
            """;

        List<RunningRecording> v1Running = NeutrinoV1Api.ParseRecordings(v1Recordings);
        Expect("neue API: /recordings liefert die laufende Aufnahme",
            v1Running.Count == 1 && v1Running[0].EventId == 17
            && v1Running[0].ChannelId == "361d03f300012b66" && v1Running[0].Start == 1704136500,
            v1Running.Count.ToString());

        const string v1Files = """
            {"items":[{"id":"movies","kind":"dir","attributes_read":true,"size":4096},
            {"id":"Aufnahme 1.ts","kind":"regular","attributes_read":true,"size":1136242099}]}
            """;

        List<FileEntry> v1FileList = NeutrinoV1Api.ParseFiles(v1Files, "/media/hdd");
        Expect("neue API: /storage/files liefert zwei Einträge", v1FileList.Count == 2, "Anzahl=" + v1FileList.Count);
        if (v1FileList.Count == 2)
        {
            Expect("neue API: Ordner erkannt", v1FileList[0].IsDirectory);
            Expect("neue API: absoluter Pfad aus dem Elternordner",
                v1FileList[1].FullPath == "/media/hdd/Aufnahme 1.ts", v1FileList[1].FullPath);
            Expect("neue API: Dateigröße", v1FileList[1].Size == 1136242099, v1FileList[1].Size.ToString());
        }

        Expect("neue API: Aufnahmeverzeichnis aus /settings",
            NeutrinoV1Api.ParseSetting("{\"items\":[{\"id\":\"network_nfs_recordingdir\",\"value\":\"/media/hdd/movie\"}]}",
                "network_nfs_recordingdir") == "/media/hdd/movie");
        Expect("neue API: unbekannte Einstellung ergibt nichts",
            NeutrinoV1Api.ParseSetting("{\"items\":[]}", "network_nfs_recordingdir") is null);

        DateTime localStart = new(2024, 1, 1, 20, 15, 0);
        Expect("neue API: lokale Zeit wird zu Unix-Sekunden umgerechnet",
            NeutrinoV1Api.TryParseLocalUnix("2024-01-01T20:15:00", out long localUnix)
            && localUnix == new DateTimeOffset(localStart).ToUnixTimeSeconds(),
            "Unix=" + localUnix.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Expect("neue API: \"0\" und leere Angaben ergeben keine Zeit",
            !NeutrinoV1Api.TryParseLocalUnix("0", out _) && !NeutrinoV1Api.TryParseLocalUnix(string.Empty, out _));
        Expect("neue API: leere und kaputte Antworten ergeben leere Listen",
            NeutrinoV1Api.ParseChannels("{}").Count == 0
            && NeutrinoV1Api.ParseTimers("kein json").Count == 0
            && NeutrinoV1Api.ParseFiles(string.Empty, "/x").Count == 0
            && NeutrinoV1Api.ParseRecordings("[]").Count == 0
            && NeutrinoV1Api.ParseBouquets("{\"items\":[]}").Count == 0);

        Expect("neue API: Zap weckt die Box nicht (wake:false)",
            NeutrinoV1Api.BuildZapBody("361d03f300012b66") == "{\"channel_id\":\"361d03f300012b66\",\"wake\":false}",
            NeutrinoV1Api.BuildZapBody("361d03f300012b66"));
    }

    /// <summary>Prüft Versionsvergleich und Release-Auswertung der Updateprüfung.</summary>
    private static void RunUpdateChecks(List<string> report)
    {
        void Expect(string name, bool condition, string detail = "")
            => report.Add((condition ? "OK     " : "FEHLER ") + name + (condition || detail.Length == 0 ? string.Empty : " (" + detail + ")"));

        Expect("Update: Tag \"v0.3\" wird gelesen", UpdateChecker.ParseTag("v0.3") == new Version(0, 3, 0, 0));
        Expect("Update: Tag \"v1.2.3\" wird gelesen", UpdateChecker.ParseTag("v1.2.3") == new Version(1, 2, 3, 0));
        Expect("Update: Tag \"1.0\" ohne v wird gelesen", UpdateChecker.ParseTag("1.0") == new Version(1, 0, 0, 0));
        Expect("Update: vierstelliger Tag \"v0.3.1.7\"", UpdateChecker.ParseTag("v0.3.1.7") == new Version(0, 3, 1, 7));
        Expect("Update: ungültige oder leere Tags ergeben 0.0.0.0",
            UpdateChecker.ParseTag("") == new Version(0, 0, 0, 0)
            && UpdateChecker.ParseTag("v") == new Version(0, 0, 0, 0)
            && UpdateChecker.ParseTag("kein-versions-string") == new Version(0, 0, 0, 0));
        Expect("Update: Anzeige kürzt überflüssige Nullen",
            UpdateChecker.Display(new Version(0, 4, 0, 0)) == "0.4"
            && UpdateChecker.Display(new Version(1, 2, 3, 0)) == "1.2.3");

        const string releaseJson = """
            {"tag_name":"v0.4","html_url":"https://github.com/TangoCash/TC.TV/releases/tag/v0.4","body":"Neu","draft":false,"prerelease":false}
            """;

        UpdateInfo? newer = UpdateChecker.ParseRelease(releaseJson, new Version(0, 3, 0, 0));
        Expect("Update: neueres Release wird erkannt",
            newer is not null && newer.IsNewer && newer.Version == new Version(0, 4, 0, 0));

        UpdateInfo? same = UpdateChecker.ParseRelease(releaseJson, new Version(0, 4, 0, 0));
        Expect("Update: gleiche Version ist kein Update", same is not null && !same.IsNewer);

        UpdateInfo? older = UpdateChecker.ParseRelease(releaseJson, new Version(0, 5, 0, 0));
        Expect("Update: ältere Installation wird nicht zurückgestuft", older is not null && !older.IsNewer);

        const string prereleaseJson = """
            {"tag_name":"v0.5","html_url":"https://github.com/TangoCash/TC.TV/releases/tag/v0.5","prerelease":true,"draft":false}
            """;
        Expect("Update: Vorabversionen werden ignoriert",
            UpdateChecker.ParseRelease(prereleaseJson, new Version(0, 3, 0, 0)) is null);

        const string draftJson = """
            {"tag_name":"v0.5","html_url":"https://github.com/TangoCash/TC.TV/releases/tag/v0.5","prerelease":false,"draft":true}
            """;
        Expect("Update: Entwürfe werden ignoriert",
            UpdateChecker.ParseRelease(draftJson, new Version(0, 3, 0, 0)) is null);

        Expect("Update: kaputtes JSON ergibt null",
            UpdateChecker.ParseRelease("kein json", new Version(0, 3, 0, 0)) is null);

        const string assetsJson = """
            {"tag_name":"v0.4","html_url":"https://github.com/TangoCash/TC.TV/releases/tag/v0.4","draft":false,"prerelease":false,
             "assets":[
               {"name":"TC.TV-win-x64.exe","browser_download_url":"https://github.com/TangoCash/TC.TV/releases/download/v0.4/TC.TV-win-x64.exe","size":942161,"digest":"sha256:aaaa"},
               {"name":"TC.TV-win-x86.exe","browser_download_url":"https://github.com/TangoCash/TC.TV/releases/download/v0.4/TC.TV-win-x86.exe","size":943697,"digest":"sha256:bbbb"},
               {"name":"TC.TV-win-x64-standalone.exe","browser_download_url":"https://github.com/TangoCash/TC.TV/releases/download/v0.4/TC.TV-win-x64-standalone.exe","size":71985085,"digest":"sha256:cccc"},
               {"name":"TC.TV-win-x86-standalone.exe","browser_download_url":"https://github.com/TangoCash/TC.TV/releases/download/v0.4/TC.TV-win-x86-standalone.exe","size":66396420,"digest":"sha256:dddd"}
             ]}
            """;

        UpdateInfo? withAssets = UpdateChecker.ParseRelease(assetsJson, new Version(0, 3, 0, 0));
        Expect("Update: Anhänge werden gelesen",
            withAssets is not null && withAssets.Assets.Count == 4, withAssets?.Assets.Count.ToString() ?? "null");

        string? firstSha = withAssets is not null && withAssets.Assets.Count > 0 ? withAssets.Assets[0].Sha256 : null;
        Expect("Update: Prüfsumme ohne sha256:-Präfix", firstSha == "aaaa", firstSha ?? "null");

        Expect("Update: x64 SingleFile wird gewählt",
            withAssets is not null && UpdateChecker.SelectAsset(withAssets, "x64", false)?.Name == "TC.TV-win-x64.exe");
        Expect("Update: x86 SingleFile wird gewählt",
            withAssets is not null && UpdateChecker.SelectAsset(withAssets, "x86", false)?.Name == "TC.TV-win-x86.exe");
        Expect("Update: x64 Standalone wird gewählt",
            withAssets is not null && UpdateChecker.SelectAsset(withAssets, "x64", true)?.Name == "TC.TV-win-x64-standalone.exe");
        Expect("Update: x86 Standalone wird gewählt",
            withAssets is not null && UpdateChecker.SelectAsset(withAssets, "x86", true)?.Name == "TC.TV-win-x86-standalone.exe");
        Expect("Update: unbekannte Architektur ergibt null",
            withAssets is not null && UpdateChecker.SelectAsset(withAssets, "arm64", false) is null);
    }

    /// <summary>
    /// Liest den optionalen Schalter <c>--datadir &lt;Ordner&gt;</c> (portable
    /// Installation bzw. Tests). Ohne Angabe gilt TCTV_DATADIR bzw. %APPDATA%\TC.TV.
    /// </summary>
    private static string? GetDataRoot(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--datadir", StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[i + 1]
                    : string.Empty;
            }
        }

        return null;
    }

    private static string? GetRenderDirectory(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--render", StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[i + 1]
                    : Path.Combine(AppPaths.WorkDir, "render");
            }
        }

        return null;
    }

    /// <summary>
    /// Hilfsfunktion für die Entwicklung: legt alle Fenster an und speichert sie
    /// als PNG (Aufruf: TC.TV.exe --render [Zielordner]).
    /// </summary>
    private void RenderWindows(string directory)
    {
        Directory.CreateDirectory(directory);

        Render(directory, "MainWindow", () => new MainWindow(), 480, 900);
        Render(directory, "SettingsWindow", () => new SettingsWindow(), 540, 0);
        Render(directory, "ProfilesWindow", () => new ProfilesWindow(), 720, 580);
        Render(directory, "VideoWindow", () => new VideoWindow(), 660, 540);
        Render(directory, "EpgWindow", () => EpgWindow.CreateForSelfTest(), 560, 500);
        Render(directory, "TimersWindow", () => TimersWindow.CreateForSelfTest(), 560, 520);
        Render(directory, "EpgGuideWindow", () => EpgGuideWindow.CreateForSelfTest(), 600, 560);
        Render(directory, "HelpWindow", () => new HelpWindow(), 560, 560);
        Render(directory, "PromptWindow", () => PromptWindow.CreateForSelfTest(), 360, 0);
        Render(directory, "ProgressCheck", CreateProgressCheckWindow, 250, 70);

        Shutdown();
    }

    /// <summary>
    /// Nur für <c>--render</c>: zeigt den Fortschrittsbalken-Stil bei 25 %, 50 %
    /// und 100 %, damit die Darstellung geprüft werden kann.
    /// </summary>
    private static Window CreateProgressCheckWindow()
    {
        Window window = new()
        {
            Title = "ProgressCheck",
            Width = 250,
            Height = 70,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Background = Application.Current.FindResource("Brush.Card") as Brush,
        };

        StackPanel panel = new() { Margin = new Thickness(20, 14, 20, 14) };
        foreach (double value in new[] { 0.25, 0.5, 1.0 })
        {
            ProgressBar bar = new()
            {
                Style = Application.Current.FindResource("ThinProgress") as Style,
                Minimum = 0,
                Maximum = 1,
                Value = value,
                Margin = new Thickness(0, 0, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            panel.Children.Add(bar);
        }

        window.Content = panel;
        return window;
    }

    private static void Render(string directory, string name, Func<Window> factory, double width, double height)
    {
        try
        {
            Window window = factory();
            if (window is MainWindow mainWindow)
            {
                mainWindow.AllowClose = true;
                mainWindow.SkipInitialLoad = true;
                mainWindow.LoadDemoChannels();
            }

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            if (width > 0)
            {
                window.Width = width;
            }

            if (height > 0)
            {
                window.Height = height;
            }

            window.Left = 30;
            window.Top = 30;
            window.Topmost = false;
            window.Show();
            window.UpdateLayout();

            if (window.Content is not FrameworkElement content)
            {
                window.Close();
                return;
            }

            // Die Margin des Wurzelelements gehört zum Clientbereich dazu.
            int pixelWidth = (int)Math.Ceiling(content.ActualWidth + content.Margin.Left + content.Margin.Right);
            int pixelHeight = (int)Math.Ceiling(content.ActualHeight + content.Margin.Top + content.Margin.Bottom);
            if (pixelWidth <= 0 || pixelHeight <= 0)
            {
                window.Close();
                return;
            }

            RenderTargetBitmap bitmap = new(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);

            // Der Fensterhintergrund liegt auf dem Window, nicht im Content - für
            // eine originalgetreue Abbildung zuerst als Fläche zeichnen.
            if (window.Background is Brush background)
            {
                DrawingVisual backdrop = new();
                using (DrawingContext context = backdrop.RenderOpen())
                {
                    context.DrawRectangle(background, null, new Rect(0, 0, pixelWidth, pixelHeight));
                }

                bitmap.Render(backdrop);
            }

            bitmap.Render(content);

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using (FileStream stream = File.Create(Path.Combine(directory, name + ".png")))
            {
                encoder.Save(stream);
            }

            window.Close();
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory, name + ".error.txt"), ex.ToString());
        }
    }
}
