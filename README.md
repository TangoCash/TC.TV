# TC.TV – .NET-Portierung (WPF)

Portierung des ursprünglichen, **dekompilierten AutoIt-Skripts** (6.550 Zeilen,
188 Funktionen, obfuskierte Variablennamen `$0`…`$1b8`) in eine moderne
**.NET-8-WPF-Anwendung**.

Das Original war ein Client für den **DVBViewer Recording Service**: Ein
halbtransparentes Einblendfenster am Bildschirmrand mit Sender-/EPG-Liste,
Video-Browser, Timern, Profilen und VLC-Streaming.

> **Das Originalmaterial ist nicht mehr Teil des Projektordners.** Es wurde auf
> Wunsch ausgelagert und liegt unverändert (mit SHA256-Prüfsummen und
> Wiederherstellungsanleitung) in `C:\tctv-autit-originals\`:
> `App/S.TV.au3`, `App/AutoIt3.exe`, `S.TV.vbs` und `tools/extract.ps1`.
> Die daraus erzeugten Dateien sind im Projekt enthalten (`src/TCTV/Generated/`),
> es wird also nichts zum Bauen oder Ausführen gebraucht.

> **Umbenennung:** Das Projekt hiess bis zur Umstellung auf die
> `/control/`-API **S.TV** und heisst jetzt **TC.TV**. Umbenannt wurden alle
> Bezeichner der Portierung – Assemblename (`TC.TV.exe`), Wurzel-Namespace
> (`TCTV`), Projektordner (`src/TCTV`), Solution (`TCTV.Net.sln`),
> Fenster-/Meldungstitel, Mutex (`Local\TC.TV.Net`), Datenordner
> (`%APPDATA%\TC.TV\TC.TV_Workdir`, `TC.TV.ini`) und die Anzeigenamen in den
> Sprachstrings.
>
> Aus dem Original übernommen (nicht umbenannt, weil es zu dessen Daten gehört):
> der Name der eingebetteten Ressource `sTV.ico` (u. a. `AppPaths.Temp("sTV.ico")`
> und `Assets/sTV.ico`).
> Alte Datenbestände werden erkannt: ein vorhandenes `S.TV_Workdir` samt
> `S.TV.ini` wird beim ersten Start übernommen und die INI auf `TC.TV.ini`
> umgezogen; `STV_DATADIR` gilt weiterhin (neu: `TCTV_DATADIR`).


---

## Schnellstart

```powershell
dotnet build TCTV.Net.sln
dotnet run --project src/TCTV/TCTV.csproj
```

### Einzeldatei-EXE erzeugen – zwei Varianten

```powershell
# Variante 1 (Standard): EINE Datei, ~0,9 MB, braucht .NET 8 oder neuer
powershell -File tools/publish-singlefile.ps1                       # dist\TC.TV.exe (x64)
powershell -File tools/publish-singlefile.ps1 -Architecture x86     # dist\x86\TC.TV.exe

# Variante 2: EINE Datei, ~72 MB, braucht GAR KEIN .NET (Laufzeit eingebettet)
powershell -File tools/publish-singlefile.ps1 -Variant Standalone
powershell -File tools/publish-singlefile.ps1 -Variant Standalone -Architecture x86
```

| Variante | Ergebnis | Größe | Voraussetzung auf dem Zielrechner |
|---|---|---|---|
| **SingleFile** (Standard) | `dist\TC.TV.exe` | 911.953 B (x64) / 883.285 B (x86) | .NET-Desktop-Runtime (WPF) **8.0 oder neuer** |
| **Standalone** | `dist\standalone\TC.TV.exe` | 71,9 MB (x64) / 66,4 MB (x86) | **kein .NET** |

**Es entsteht jeweils genau eine Datei – keine `runtimeconfig.json` daneben.**

* Die `runtimeconfig.json` (samt `rollForward: Major`) ist **in die EXE eingebettet**.
* **`rollForward: Major`** (`TCTV.csproj`) lässt die App auch mit einer *neueren*
  Desktop-Runtime (9, 10, …) starten, wenn kein 8.x vorhanden ist; vorhandenes 8.x
  wird bevorzugt. `DOTNET_ROLL_FORWARD=LatestMajor` erzwingt die neueste.
* Nachgemessen mit `COREHOST_TRACE=1` in einer Umgebung mit **ausschliesslich .NET 10**
  (eigene `DOTNET_ROOT`-Wurzel, nur 10.0.8 per Junction):

  ```
  'Roll forward' enabled with version_compatibility_range [major]
  framework:'Microsoft.WindowsDesktop.App', lowest requested version='8.0.0', found version='10.0.8'
  ```

  → die App startet dort und meldet im Selbsttest `Laufzeit: .NET 10.0.8`.
* Die Standalone-Variante braucht gar keine Laufzeit: in einem leeren Ordner mit
  `DOTNET_ROOT` auf einen leeren Pfad und `DOTNET_MULTILEVEL_LOOKUP=0` gestartet
  läuft sie (74/74 Prüfungen OK, `Laufzeit: .NET 8.0.27` aus der EXE).
  Der erste Start entpackt sich kurz nach `%TEMP%\.net` (Single-File-Standard).


### Benutzerdaten liegen im Profil, nicht neben der EXE

Beim ersten Start entsteht **im Benutzerprofil** (nicht im Programmordner):

```
%APPDATA%\TC.TV\TC.TV_Workdir\
├── TC.TV.ini             # Allgemeine Einstellungen (identische Schlüssel wie im Original)
├── Box100.conf3          # Empfängerprofil(e)
├── Logos/                # heruntergeladene Senderlogos (ShortId + ".png")
├── VLCPortable/          # optionaler portabler VLC
└── Temp/                 # eingebettete Ressourcen (sTV.ico, deutsch/english.bmp, …)
```

* Der Programmordner bleibt dadurch sauber und kann schreibgeschützt sein
  (`Program Files`, Netzlaufwerk, USB-Stick).
* **Übernahme:** Liegt noch ein Datenbestand der früheren Fassung vor, wird er beim
  ersten Start einmalig ins Profil kopiert (`Temp` ausgenommen) – entweder
  `TC.TV_Workdir`/`S.TV_Workdir` neben der EXE **oder** das frühere
  `%APPDATA%\S.TV\S.TV_Workdir`. Eine `S.TV.ini` wird dabei auf `TC.TV.ini`
  umbenannt. Vorhandene Daten im Profil werden **nie** überschrieben – der
  Vorgang steht im Kopf von `selftest.log`.
  im Kopf von `selftest.log`.
* **Verschieben (portabel/Test):** Schalter `--datadir <Ordner>` oder
  Umgebungsvariable `TCTV_DATADIR`; der Schalter hat Vorrang. Beispiel:

```powershell
TC.TV.exe --datadir D:\TC.TV-Daten
```

Zum Testen muss im Profil (`Box100.conf3`) die **IP der Box** gesetzt werden
(Profil-Fenster in der App oder direkt in der Datei).

---

## Projektstruktur

| Pfad | Inhalt |
|---|---|
| `src/TCTV/TCTV.csproj` | WPF-Projekt, `net8.0-windows`, Ergebnis `TC.TV.exe` |
| `src/TCTV/Assets/sTV.ico` | Original-Icon (aus den eingebetteten Ressourcen) als EXE-Icon |
| `src/TCTV/Generated/` | **erzeugt** aus dem Original-AutoIt-Skript (siehe unten) – im Projekt enthalten |
| `src/TCTV/Core/` | Konfiguration, Client, VLC, Themes, Localization, Modelle |
| `src/TCTV/Styles/Controls.xaml` | Style-System (Buttons, Eingaben, Listen, Karten, ScrollBar) |
| `src/TCTV/Views/` | Haupt-, Einstellungs-, Profil-, Video- und Eingabefenster |
| `src/TCTV/Properties/PublishProfiles/` | `SingleFile-*` (eine Datei, `rollForward: Major`) und `Standalone-*` (self-contained) |
| `tools/publish-singlefile.ps1` | Baut die Einzeldatei-EXE (bzw. die self-contained Variante) und prüft das Ergebnis |
| `tools/render-themes.ps1` | Rendert alle Fenster in allen vier Themes (eigener `--datadir`) |

### Generierte Dateien

`src/TCTV/Generated/` enthält zwei aus dem AutoIt-Original erzeugte Tabellen
(Erzeuger: `extract.ps1`, liegt im Archiv `C:\tctv-autit-originals\`):

* `LocalizationTable.cs` – die komplette **Sprach-Tabelle `$ei[324][2]`**
  (Deutsch/Englisch, alle 324 Zeilen inkl. `@LF`-Umbrüchen).
* `EmbeddedResources.cs` – die **7 eingebetteten Ressourcen**
  (`sTV.ico`, `deutsch.bmp`, `english.bmp`, `keinBild.jpg`, `refresh.png`,
  `movie1.ico`, `folder1.ico`) als Base64 inkl. LZNT1-Kennzeichnung.

Neu erzeugen (nur nötig, wenn das Original geändert wird):

```
powershell -File tools/extract.ps1 `
  -Source  ".\App\S.TV.au3" `
  -OutDir  ".\src\TCTV\Generated"
```

> Dafür `App/` und `extract.ps1` vorher aus dem Archiv zurückkopieren – siehe
> `C:\tctv-autit-originals\README.txt`.
> Die Blobs sind **byte-identisch** mit dem Original. `ResourceProvider`
> dekodiert Base64 und entpackt mit `RtlDecompressBuffer`
> (COMPRESSION_FORMAT_LZNT1) – genau wie AutoIts `_1b6`/`_1b7`.
> Geprüft: ICO-/BMP-/GIF-/PNG-Signaturen und Originalgrößen
> (z. B. `sTV.ico` = 175.749 Bytes mit 9 Bildern).


---

## Funktionsumfang

### Umgesetzt

**Kern / Konfiguration**
* `IniFile` mit AutoIt-kompatiblen Semantiken (`IniRead`/`IniWrite`/`IniDelete`):
  case-insensitive Abschnitts-/Schlüsselnamen, Standardwerte, Anlegen bei Bedarf,
  Erhalt von Reihenfolge/Kommentaren.
* `AppSettings` mit **exakt denselben Schlüsseln** und Standardwerten
  (`Benutzerdaten`, `Optionen`, `VLC Fenster`, `Programmdaten`) – inkl. der
  Original-Tippfehler (`Traspondersender markieren`, `Fenstergröße_fest`).
* `ProfileStore`/`Profile`: Profile als `*.conf3` im Arbeitsverzeichnis,
  aktives Profil in `Benutzerdaten\Profil`, Defaults `root`/`coolstream`/`80`/`21`
  wie im Original.
* `Localization` (DE/EN) über die extrahierte Tabelle.
* `Theme` mit den Farbtabellen `$lf[3][11]`. Das Standarddesign ist
  **Dark orange** mit den Farben von neutrino-images.de; „1“ = dunkler, fast
  transparenter Rahmen (`0x252525`, WinSetTrans 50).

**Recording-Service-Client** (`RecordingServiceClient`)
* `/control/info`, `/control/getmode`, `/control/gettime?rawtime`,
  `/control/standby`, `/control/zapto`, `/control/getbouquets`,
  `/control/getbouquet?…&epg=true&format=xml`, `/control/getdir`,
  `/control/getmovies`, `/control/file?action=list`, `/control/timer`
  (`?format=xml`, `?format=id`, `?action=new`), `/control/currenttpchannels`,
  `/control/build_live_url`.
* HTTP-Basisauthentifikation über `NetworkCredential` (`Web-Authentifikation`).
* XML-Parsing von `<channel>`/`<firstEPG>`/`<secondEPG>`, `<dirs>`, `<movies>`,
  `<filelist>` und der Timer-Liste – die Datei-Endpunkte liefern **absolute
  Pfade** der Box (`dir`/`path`/`fullname`), die direkt weiterverwendet werden.

**GUI (WPF)**
* `MainWindow`: Einblendfenster am rechten/linken Rand, Topmost, ohne Taskleiste,
  mit Transparenz aus dem Theme; Kopfzeile mit Uhr und Verbindungsstatus;
  Bouquet-Auswahl; Senderliste mit EPG-Zeile; Detailbereich (Startzeit, Dauer,
  Info1/Info2, Beschreibung, Folgesendung); Werkzeugleiste
  (Stream, Stop, Video, EPG aktualisieren, Timer, Einstellungen, Profile,
  Vollbild-Hotkey, Beenden).
* **Fade-in/Fade-out** per Mausposition an der Bildschirmkante, konfigurierbare
  Verzögerung (`fade-in-Verzögerung in Millisekunden`), Pinnen per Tray-Klick.
* **Tray-Icon** aus der Original-`sTV.ico` mit Menü.
* Hotkeys wie im Original: `f` = Fenster verbreitern (680 px), `s` = VLC-Stop,
  `Esc` = ausblenden.
* `SettingsWindow`, `ProfilesWindow`, `VideoWindow`, `PromptWindow`.
* **Modernes Design**: eigenes Style-System, dynamisches Theming, Icon-Buttons –
  siehe Abschnitt [Oberfläche & Design](#oberfläche--design).

**VLC-Integration** (`VlcOptions`, `VlcLauncher`)
* Kommandozeilenoptionen in der Originalreihenfolge `$l0…$l9`
  (`--video-on-top`, `--qt-notification=0`, `--no-qt-video-autoresize`,
  `--qt-start-minimized`, `--no-video-title-show`, `--qt-minimal-view`,
  `--no-qt-name-in-title`, `--deinterlace=-1`, `--fullscreen`, `--one-instance`).
* Pfaderkennung: INI → `VLCPortable\VLCPortable.exe` → `Program Files`
  → `Program Files (x86)` → Registry `HKLM\Software\VideoLAN\VLC` (32/64-bit).
* Live-Stream: `<host>:<StreamingPort>/id=<channelId>` bzw. `/ReciProxy`
  (mit `build_live_url` und `ReciProxy\Startverzögerung` × 1000 ms).
* **Aufnahme-/Video-Wiedergabe per HTTP** (kein FTP):
  `--one-instance http://<host>:<WebserverPort>/<absoluter Pfad>`
  (Leerzeichen → `%20`). Der Pfad stammt aus `/control/getdir`,
  `/control/getmovies` bzw. `/control/file?action=list`.
* Stoppen beim Zap: Taste `s` an das VLC-Fenster bzw. Prozess beenden
  (`VLC erhalten beim Zap`, `086i`, `Beenden`).

### Nicht übernommen (bewusste Auslassungen)

Das Original enthält 188 Funktionen; ein Teil davon ist **Skin-/Effekt- und
Sonderlogik**, die für die Funktion nicht nötig ist. Nicht portiert wurden:

* GDI+-Sonderzeichen: eigene Bitmap-/Snapshot-Ansicht (`_10a`, `_116`), Logo-
  Skalierung per `AnimateWindow` (`_15t`, `_15k`, `_1b8`).
* ReciProxy-Steuerfenster mit komplexer Fade-/Größenanimation.
* Die CST/Coolstream-Spezialpfade (`/var/tuxbox/config/zapit/bouquets.xml`,
  ubouquets/ubouquets-Merge) und die „Externe Box“-Erkennung per Ping.
* IMDb-/TVspielfilm-Suche (`ShellExecute` auf Such-URLs).
* Reine Kosmetik-Animationen über `GUIRegisterMsg` (WM_NOTIFY/WM_COMMAND).

### Getestet

* Build ohne Warnungen (`dotnet build TCTV.Net.sln`).
* Start der Anwendung; Anlegen von `TC.TV.ini`, `Box100.conf3`, `Logos/`, `Temp/`.
* Byte-genaue Prüfung aller 7 entpackten Ressourcen (LZNT1).
* VLC-Erkennung über `Program Files (x86)`.
* `--selftest`: alle fünf Fenster (Main, Settings, Profiles, Video, Prompt)
  werden fehlerfrei aufgebaut (Exit-Code 0).
* `--selftest`: **74 Prüfungen** mit synthetischen Original-Antworten
  (Timer-XML ohne/mit Eintrag, `format=id`, ID-Vergleich, Transponder,
  `getdir`/`getmovies`/`file`-Listen, HTTP-Wiedergabe-URL, UTF-8-Umlaute,
  Latin-1-Rückfall, INI-Rundlauf, Fortschrittsberechnung, Themenauswahl,
  Datenpfade, Profilfenster) – alle OK, Exit-Code 0.
* Profilfenster **live** geprüft (UI-Automation mit echtem Mausklick auf
  „Profile“ → „Speichern“): der Klick kommt an, die `.conf3` wird neu
  geschrieben, `Benutzerdaten\Profil` gesetzt, `ShowDialog()` liefert `true`
  und das Fenster ist danach geschlossen.
* `--render`: alle Fenster zusätzlich als PNG geprüft – in allen Themes
  (Dark orange = Standard, Dark grün, Hell, TC.TV Overlay). Für alle vier
  Themes auf einmal: `powershell -File tools\render-themes.ps1`
  (schreibt je Theme einen Ordner und setzt vorher `Theme=` in der INI).
* `--selftest`: 12 Design-Prüfungen (Dark orange als Standard inkl. der exakten
  NI-Farbwerte `#212121`/`#424242`/`#616161`/`#E0E0E0`/`#9E9E9E`/`#FD6D13`,
  sowie die übrigen Themes) – alle OK.

### Einzeldatei-EXE und Datenablage – geprüft

* `tools/publish-singlefile.ps1` liefert **genau eine Datei** `dist\TC.TV.exe`
  (911.953 Bytes) – die `runtimeconfig.json` ist mit eingebettet.
* Lauffähig geprüft: `TC.TV.exe --selftest` **aus dem Publish-Ordner** → 74/74 OK,
  Exit-Code 0.
* **Aufwärtskompatibilität nachgemessen** (Umgebung mit *nur* .NET 10: eigene
  `DOTNET_ROOT`-Wurzel mit `host`- und `shared`-Junctions auf echte 10.0.8-Frameworks):

  | Auslieferung | nur .NET 10 vorhanden | kein .NET auffindbar |
  |---|---|---|
  | `TC.TV.exe` (SingleFile, 1 Datei) | **OK – `Laufzeit: .NET 10.0.8`** | – |
  | `TC.TV.exe` (Standalone, 1 Datei) | OK – `Laufzeit: .NET 8.0.27` | **OK – 74/74 in 2 s** |

* `COREHOST_TRACE=1` belegt, dass der Host `rollForward` **aus dem Bundle** anwendet:
  `'Roll forward' enabled with version_compatibility_range [major]` und
  `framework:'Microsoft.WindowsDesktop.App', lowest requested version='8.0.0', found version='10.0.8'`.
  *(Ein früherer Messwert `version_compatibility_range=minor` stammte von einer
  veralteten EXE-Kopie von vor dem `RollForward`-Eintrag – daher die damals falsche
  Annahme, das Bundle werde nicht ausgewertet.)*
* Kontrolle mit `DOTNET_ROLL_FORWARD=Disable`: Start schlägt fehl → die Umgebung
  hat wirklich kein 8.x, der Erfolg oben kommt also vom RollForward.
* Mit vorhandenem 8.x läuft die App wie bisher auf **.NET 8.0.31** (8.x wird bevorzugt).
* Standalone-Variante: `dist\standalone\TC.TV.exe` (71,9 MB) in einen leeren Ordner
  kopiert und mit `DOTNET_ROOT` auf einen **leeren** Ordner plus
  `DOTNET_MULTILEVEL_LOOKUP=0` gestartet – also ohne irgendeine auffindbare
  .NET-Laufzeit: **ExitCode 0, 74/74 OK in 2 s**. x86: 66,4 MB, ebenfalls eine Datei.
* EXE-Icon geprüft: `ExtractAssociatedIcon` liefert das Originalsymbol
  (TV mit Antenne), Icon-Größen 16–256 px.
* Datenablage geprüft:
  * mit `--datadir <Ordner>` entsteht **nichts** neben der EXE und **nichts** in
    `%APPDATA%`;
  * ohne Schalter liegt alles unter `%APPDATA%\TC.TV\TC.TV_Workdir\`
    (INI, `Box100.conf3`, `Logos`, `Temp` mit allen 7 Ressourcen);
  * ein danebenliegendes altes `TC.TV_Workdir` wird übernommen: Marker-Schlüssel
    kopiert, `Temp\junk.txt` **nicht** kopiert, die Quelldateien bleiben
    unverändert (die EXE schreibt dort nicht mehr hinein).

**Nicht** getestet werden konnte die Kommunikation mit einer echten
DVBViewer-Recording-Service-Box (keine im Testsystem vorhanden). Die
Endpunkte, XML-/JSON-Strukturen und Kommandozeilen wurden aus dem Originalcode
abgeleitet.

---

## API-Abgleich mit der Box-Firmware (Neutrino)

Die `/control/`-API wurde gegen die Firmware-Quellen abgeglichen:
<https://github.com/TangoCash/neutrino-tangos> (`src/nhttpd/tuxboxapi/controlapi.cpp`,
Klasse `CControlAPI`). Daraus ergeben sich u. a. folgende, nun exakt umgesetzte Details:

| Endpunkt | Antwort der Box |
|---|---|
| `/control/gettime?rawtime` | Unix-Sekunden + `\n` (`printf("%ld\n", time(NULL))`) |
| `/control/standby` | ohne Parameter `on` bzw. `off` |
| `/control/getmode` | `tv`, `radio`, `avinput`, … |
| `/control/info` | nur `PACKAGE_NAME PACKAGE_VERSION` (Verbindungstest) |
| `/control/currenttpchannels` | je Zeile `<channel_id> <name>` der **aktuellen** Transponder-Ebene |
| `/control/zapto?<channel_id>` | erster Parameter ist die Kanal-ID (ohne führende Nullen) |
| `/control/timer?format=id` | eine Zeile je Timer:<br>`eventID eventType eventRepeat repeatCount announceTime alarmTime stopTime channelID` |
| `/control/timer?format=xml` | `<timer>` als **Wurzel** *und* als Elementname je Timer<br>`<timer><config/><timer_list><timer>…</timer></timer_list></timer>` |
| `/control/getdir?dir=recordingdir\|moviedir\|allmoviedirs` | `<dirs><item><dir>/absoluter Pfad</dir></item>…</dirs>` – `recordingdir` = `network_nfs_recordingdir`, `moviedir` = die `mb_dir_0..7` aus `moviebrowser.conf` |
| `/control/getmovies?dir=<absoluter Pfad>` | `<movies><item><title>Name</title><path>/absoluter/Pfad</path><size>…</size></item>…</movies>` |
| `/control/file?action=list&path=<absoluter Pfad>` | `<filelist><item><name/><type_str>dir\|file\|lnk</type_str><type>4\|8\|10</type><fullname/><size/><time_t/></item>…</filelist>` |
| `http://<host>:<Webport>/<absoluter Pfad>` | statische Datei über `mod_sendfile` (siehe unten) |

Ein `&format=xml` liefert jeweils den XML-Baum; ohne `format` antwortet die Box
im **plain**-Format (`key=value` je Zeile).

### FTP entfernt – Wiedergabe über den Dateiserver der Box

Die Originalfassung spielte Aufnahmen über `ftp://root:…@box/pfad` ab (der
CST/Coolstream-FTP-Server). Das ist ersetzt; die Profile enthalten keine
FTP-Zugangsdaten mehr.

Möglich wird das durch den **nhttpd der Box** (`src/nhttpd/nhttpd.conf.in`,
`src/nhttpd/yconfig.h`, `mod_sendfile.cpp`):

```c
#define Y_CONFIG_FEATUE_SENDFILE_CAN_ACCESS_ALL y   // "every file can be accessed"
#define HTTPD_SENDFILE_ALL  "true"
#define HTTPD_SENDFILE_EXT  "…ts:video/MP2T,mkv:video/x-matroska,avi:video/x-msvideo,mp3:audio/mpeg,ogg:audio/ogg"
```

`CmodSendfile::GetFileName()` prüft in dieser Reihenfolge
`override_directory`, `.gz`, `directory`, `.gz` und – dank des gesetzten Flags –
anschließend den **reinen Pfad**. Damit liefert der Webserver jede Datei unter
ihrem absoluten Pfad aus, inklusive `Range`-Requests (`hh->RangeStart/RangeEnd`)
zum Spulen. Der Video-Browser baut die URL daher direkt aus dem Pfad, den
`/control/getdir`, `/control/getmovies` bzw. `/control/file` zurückgeben.

Die Live-Wiedergabe bleibt unverändert bei `streamts`
(`src/driver/streamts.cpp`): dessen HTTP-Parser akzeptiert ausschließlich
`GET /id=<channel_id>` (Hex) bzw. das alte `<service>:0:…`-Format – Dateien kann
er **nicht** streamen, deshalb der getrennte Weg über den Webserver.


### Behobener Fehler: dauerhafte Meldung „Aufnahme läuft!“

Ursprünglich wurde für die Warnung `GetTimersAsync()` (XML) ausgewertet und nur
gezählt, ob **überhaupt** Timer existieren. Da `format=xml` das Element `timer`
zweimal verwendet (Wurzel **und** Eintrag), lieferte die Abfrage selbst bei
**null** Timern ein Ergebnis → die Meldung erschien bei **jedem** Streamstart.

Korrekt umgesetzt ist jetzt die Original-Logik aus `_1aq`:

1. **Multituner-Box** (`Profile.MultiTuner`) → keine Warnung.
2. **Aktuell laufende Aufnahmen** bestimmen: Timer aus `/control/timer?format=id`
   gegen die Boxzeit (`/control/gettime?rawtime`) prüfen – nur Timer mit
   `Start ≤ jetzt < Ende` zählen. Geplante oder beendete Timer blockieren nicht.
3. Wird **derselbe** Sender aufgenommen (`ChannelIdsMatch`, tolerant gegenüber
   führenden Nullen) → keine Warnung.
4. Liegt der Sender auf dem **aktuellen Transponder** (`/control/currenttpchannels`) → keine Warnung.
5. Nur wenn all das nicht zutrifft, erscheint die Meldung
   `$ei[75]`/`$ei[54]` („Aufnahme läuft!“ / „Aktuell Streaming nur auf gleichem Transponder möglich.“)
   und der Stream wird – wie im Original – **nicht** gestartet.

Zusätzlich parst `ParseTimersXml` nur noch `<timer>`-Elemente **mit** `<id>`
(damit der Wurzelknoten nicht als Geister-Timer zählt) und liest Start/Ende aus
`<alarm>/<stop>` → `<normal><digits>` (Unix-Sekunden).

Der `--selftest` prüft diese Logik mit synthetischen Original-Antworten
(leere Timerliste, ein Timer, `format=id`-Zeilen, ID-Vergleich, Transponder).


### Behobener Fehler: Fortschrittsbalken war nur eine Linie

Symptom: unter der EPG-Zeile war nur eine dünne Linie zu sehen – kein Fortschritt.

Ursache: **im ProgressBar-Template fehlte `PART_Track`**. WPF dimensioniert die
Füllung (`PART_Indicator`) anhand der Breite von `PART_Track`; ohne dieses
Element blieb die Füllung bei Breite 0, sichtbar war nur die Spur.

```xml
<!-- vorher: nur die Spur war sichtbar -->
<Border Background="{TemplateBinding Background}">
    <Border x:Name="PART_Indicator" HorizontalAlignment="Left" ... />
</Border>

<!-- jetzt -->
<Border x:Name="PART_Track" Background="{TemplateBinding Background}" ClipToBounds="True">
    <Border x:Name="PART_Indicator" HorizontalAlignment="Left" ... />
</Border>
```

Zusätzlich wurde die Berechnung belastbar gemacht (`ChannelItem.ComputeProgress`,
Reihenfolge wie die Box liefert):

1. `<percentage>` (0–100, die Box rechnet `100 * (jetzt - Start) / Dauer`),
2. `<timeElapsed>` / `<timeTotal>` (Minuten),
3. `<startTime>` gegen die Uhrzeit (inkl. Sendungen über Mitternacht).

Der Balken wird ausgeblendet, wenn keine Dauer bekannt ist (die Box meldet dann
`percentage = 100`, was sonst einen volllaufenden Balken vortäuschen würde).
`--render` legt eine `ProgressCheck.png` mit 25 %, 50 % und 100 % ab; das
Hauptfenster wird für die Kontrolle mit Beispieldaten gefüllt.


Symptom: EPG-Texte erschienen als „Am 25. MÃ¤rz 1996 wird … entfÃ¼hrt“.

Ursache: `GetTextAsync` dekodierte die Antwort mit **Latin-1**, um AutoIts
`InetGet` nachzubilden. Die Box liefert aber **UTF-8** – `yhook.cpp`
(`outValue` → `convert_UTF8_To_UTF8_XML`) und der Header
`Content-Type: …; charset=UTF-8` belegen das. UTF-8-Bytes `C3 A4` („ä“) wurden
dadurch zu „Ã¤“.

Lösung: `Core/TextEncoding.cs` dekodiert **UTF-8** und fällt nur dann auf
Latin-1 zurück, wenn die Antwort kein gültiges UTF-8 ist (`DecoderFallbackException`).
`RecordingServiceClient.DecodeText` und `IniFile` nutzen dieselbe Routine – so
bleiben auch Umlaute in INI-Schlüsseln erhalten, selbst wenn die Datei noch von
der AutoIt-Fassung (ANSI) stammt.

Der `--selftest` prüft das jetzt mit echten Umlauten: UTF-8-Dekodierung, der
Latin-1-Rückfall, die gesamte Kette XML-Bytes → `ParseBouquet` sowie ein
INI-Rundlauf über „fade-in-Verzögerung in Millisekunden“.


## Oberfläche & Design

Die Oberfläche wurde bewusst **nicht 1:1 nachgebaut**, sondern modern neu
gestaltet – bei identischer Funktionalität und identischen Elementnamen.

**Style-System** (`src/TCTV/Styles/Controls.xaml`)
* Eigene Templates für `Button` (`PrimaryButton`, `GhostButton`, `IconButton`,
  `ModernCombo`-ComboBox, `ModernCheck`-CheckBox, `InputBox`/`InputPasswordBox`,
  `ModernList`/`ModernListItem`, `ThinProgress`, schlanke `ScrollBar`).
* Abgerundete Ecken, Hover-/Pressed-Zustände, Fokusring in der Akzentfarbe,
  Karten (`Card`), Typografie-Styles (`H1`, `H2`, `SectionHeader`, `Muted`,
  `FieldLabel`, `Glyph`).
* Icons über **Segoe MDL2 Assets** (`Font.Icons`) – z. B. Play/Stop/Video/
  Refresh/Timer/Settings/People/FullScreen/Close.

**Dynamisches Theming** (`src/TCTV/Core/ThemeManager.cs`)
* Leitet aus der Farbtabelle ein vollständiges Farbsystem ab:
  `Brush.Window`, `Brush.Shell`, `Brush.Card`, `Brush.Input`, `Brush.Border`,
  `Brush.Text`, `Brush.Muted`, `Brush.Accent`, `Brush.AccentText`,
  `Brush.AccentHover`, `Brush.AccentPressed`, `Brush.Selected`, `Brush.Chip`,
  `Brush.GhostHover`, `Brush.ScrollThumb`, `Brush.Success`, `Brush.Danger`.
* Alle Fenster binden per `DynamicResource` – ein Theme-Wechsel in den
  Einstellungen färbt die komplette Anwendung sofort um.

**Standarddesign: Dark orange (angelehnt an neutrino-images.de)**
* Voreingestellt ist `Benutzerdaten\Theme=3` – auch bei leerem oder unbekanntem
  Wert (also bei einer noch vom Original stammenden INI).
* Die Palette stammt aus dem NI-Forum (`styles/prosilver-ni/theme/custom.css`):

  | Rolle | Wert |
  |---|---|
  | Fensterhintergrund | `#212121` |
  | Flächen/Karten | `#424242` |
  | Rahmen | `#616161` |
  | Primärtext | `#E0E0E0` |
  | Sekundärtext | `#9E9E9E` |
  | Akzent (Buttons, Balken, Logo) | `#FD6D13` |
  | Hervorhebung | `#FFEE58` |
  | Fehler | `#F44336` |

* Bei dunklen Themes stammen Fläche, Rahmen und Sekundärtext direkt aus der
  Farbtabelle, damit die Werte exakt der Vorlage entsprechen. Zusätzlich:
  Auswahlfläche = Fläche + 30 % Akzent, gedrückter Button = Akzent − 25 %.
* Weitere Themes: „Dark grün“ (`2`, Original), „Hell“ (`4`, Original),
  „TC.TV Overlay“ (`1`, dunkler Rahmen `0x252525`, stark transparent).
  Das Original selbst kennt nur `1`, `2`, `3`; der Wert `4` ist neu und
  kollidiert nicht (leer/`3` = Dark orange).

**Hauptfenster**
* Kopfzeile mit Akzent-Badge, Titel, Statuspunkt (grün/rot/grau je nach
  Verbindung) und Uhrzeit-Chip.
* Senderliste als Kartenbereich mit Nummern-Badge, Sendername, Startzeit,
  EPG-Zeile und **Fortschrittsbalken** der laufenden Sendung – er zeigt, wie
  weit die Sendung schon läuft. Er erscheint, sobald die Dauer bekannt ist
  (auch bei 0 %), und wächst mit.
* Leerzustand mit Hinweis („Bearbeitung läuft..“ / „Keine Verbindung zur Box“).
* Detailkarte wird nur bei gewähltem Sender eingeblendet.
* Werkzeugleiste: primäre Aktion „Stream starten“, Ghost-Buttons für
  Stop/Video/EPG/Timer/Einstellungen/Profile, Icon-Buttons für Vollbild und Beenden.

**Dialoge** – Einstellungen, Profile und Video nutzen Karten-Layouts mit
zweispaltigen Formularrastern, Feldlabels über den Eingaben und einer
Buttonleiste; der Eingabedialog (`PromptWindow`) ist ebenfalls neu gestaltet.

**Profilfenster** – wie im Original schließt **„Speichern“** das Fenster und
übernimmt das bearbeitete Profil als aktuelles: die Werte gehen in die
`.conf3`-Datei, der Name wird als `Benutzerdaten\Profil` in die INI geschrieben
und das Hauptfenster lädt Profil und Senderliste neu
(`ProfilesWindow.ApplyAndActivate()`, im Original der Speichern-Zweig mit
`IniWrite($em,"Benutzerdaten","Profil",$nl)` + `_15t`/`GUIDelete`).
„Profil wechseln“ löst denselben Ablauf aus, „Zurück“ schließt ohne zu speichern.

### Entwickler-Werkzeuge

| Aufruf | Wirkung |
|---|---|
| `TC.TV.exe --selftest` | baut alle Fenster unsichtbar auf, schreibt `%APPDATA%\TC.TV\TC.TV_Workdir\selftest.log`; Exit-Code 1 bei Fehlern |
| `TC.TV.exe --render [Ordner]` | speichert PNG-Abbildungen aller Fenster (inkl. Fensterhintergrund) für die Design-Kontrolle; Hauptfenster mit Beispieldaten, zusätzlich `ProgressCheck.png` |
| `TC.TV.exe --datadir <Ordner>` | legt alle Benutzerdaten in `<Ordner>\TC.TV_Workdir` ab (portabel/Test); alternativ Umgebungsvariable `TCTV_DATADIR` |
| `powershell -File tools/publish-singlefile.ps1 [-Variant Standalone]` | baut `dist\TC.TV.exe` (eine Datei, `runtimeconfig.json` eingebettet) bzw. die self-contained Variante in `dist\standalone\` und prüft das Ergebnis |
| `powershell -File tools/render-themes.ps1` | rendert alle Fenster in allen vier Themes (nutzt einen eigenen `--datadir`) |

Die Schalter werden nur ausgewertet, wenn sie übergeben werden; der normale
Start ist davon unberührt. `--datadir` gilt für alle genannten Werkzeuge.


## Abbildung Original → .NET

| AutoIt | .NET |
|---|---|
| `Global $ei[324][2]` | `Generated/LocalizationTable.cs` + `Localization.T(i)` |
| `_1b0`…`_1b5` (Base64 + `RtlDecompressBuffer`) | `ResourceProvider` |
| `IniRead`/`IniWrite` | `IniFile` |
| `$em`, `$ek`, `$ej`, `$el` | `AppPaths` |
| `$fs` (Profil-`.conf3`) | `Profile` / `ProfileStore` |
| `$ie[1000][23]` | `Channel` + `EpgEvent` |
| `InetGet` auf `/control/...` | `RecordingServiceClient` |
| `$l0`…`$l9` + `ShellExecute($kc, …)` | `VlcOptions.BuildOptionString()` + `VlcLauncher` |
| `ShellExecute($kc, "… ftp://user:pass@box/pfad")` | `RecordingServiceClient.BuildHttpFileUrl()` → `http://host:webport/<absoluter Pfad>` |
| `$lf[3][11]` | `Theme` |
| `GUICreate`/`GUICtrlCreate*` | XAML (`MainWindow.xaml`, …) |
| `_1aq` (Stream starten) | `MainWindow.StreamSelectedAsync()` |
| `AnimateWindow`/`WinSetTrans` | WPF-`DoubleAnimation`/`Opacity` |

---

## Hinweise

* Das **Originalmaterial** (`S.TV.au3`, `AutoIt3.exe`, `S.TV.vbs`, `extract.ps1`)
  wurde aus dem Projektordner entfernt und liegt unverändert im Archiv
  `C:\tctv-autit-originals\` (mit SHA256-Prüfsummen und
  `README.txt` zur Wiederherstellung). Zum Bauen und Ausführen von TC.TV wird
  davon nichts benötigt.
* Der Anzeigename in den Sprachstrings wird beim Generieren ersetzt:
  `extract.ps1` schreibt `S.TV` → `-ProductName` (Standard `TC.TV`), damit
  die Tabelle `LocalizationTable.cs` reproduzierbar bleibt.
* Konfigurations- und Profildateien bleiben zum Original kompatibel
  (gleiche Schlüssel = gleiche INI).
* INI und Box-Antworten werden als **UTF-8** gelesen – die Box liefert UTF-8
  (`charset=UTF-8`, `convert_UTF8_To_UTF8_XML`). Ist der Inhalt kein gültiges
  UTF-8 (etwa eine noch von der AutoIt-Fassung in ANSI geschriebene INI), wird
  automatisch auf Latin-1 zurückgefallen, damit Umlaute erhalten bleiben.
  Geschrieben wird UTF-8 ohne BOM.
