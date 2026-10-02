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
        Check("PromptWindow", () => PromptWindow.CreateForSelfTest());

        RunApiChecks(report);

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
