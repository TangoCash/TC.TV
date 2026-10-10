# TC.TV – WPF-Client für Neutrino-Receiver

**TC.TV** ist eine **.NET-8-WPF-Anwendung**: ein halbtransparentes
Einblendfenster am Bildschirmrand mit Sender-/EPG-Liste, Video-Browser, Timern,
Profilen und VLC-Streaming für Neutrino-basierte Receiver.

Es werden zwei Box-Schnittstellen unterstützt: die klassische `/control/`-API
und die neue REST-API `/api/v1/` des NI-Images (siehe „Box-Schnittstelle“).


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
| **SingleFile** (Standard) | `dist\TC.TV.exe` | 971.857 B (x64) / 943.697 B (x86) | .NET-Desktop-Runtime (WPF) **8.0 oder neuer** |
| **Standalone** | `dist\standalone\TC.TV.exe` bzw. `dist\standalone\x86\` | 72,0 MB (x64) / 66,4 MB (x86) | **kein .NET** |

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
  läuft sie (133/133 Prüfungen OK, `Laufzeit: .NET 8.0.27` aus der EXE).
  Der erste Start entpackt sich kurz nach `%TEMP%\.net` (Single-File-Standard).


### Benutzerdaten liegen im Profil, nicht neben der EXE

Beim ersten Start entsteht **im Benutzerprofil** (nicht im Programmordner):

```
%APPDATA%\TC.TV\TC.TV_Workdir\
├── TC.TV.ini             # Allgemeine Einstellungen
├── Box100.conf3          # Empfängerprofil(e)
├── Logos/                # heruntergeladene Senderlogos (ShortId + ".png")
├── VLCPortable/          # optionaler portabler VLC
└── Temp/                 # eingebettete Ressourcen (sTV.ico, deutsch/english.bmp, …)
```

* Der Programmordner bleibt dadurch sauber und kann schreibgeschützt sein
  (`Program Files`, Netzlaufwerk, USB-Stick).
* **Übernahme:** Liegt noch ein Datenbestand einer früheren Fassung vor, wird er
  beim ersten Start einmalig ins Profil kopiert (`Temp` ausgenommen). Vorhandene
  Daten im Profil werden **nie** überschrieben – der Vorgang steht im Kopf von
  `selftest.log`.
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
| `src/TCTV/Assets/sTV.ico` | Anwendungs-Icon als EXE-Icon |
| `src/TCTV/Generated/` | erzeugte Sprach- und Ressourcentabellen (im Projekt enthalten) |
| `src/TCTV/Core/` | Konfiguration, Box-Schnittstellen (alt/neu), VLC, Themes, Localization, Modelle |
| `src/TCTV/Styles/Controls.xaml` | Style-System (Buttons, Eingaben, Listen, Karten, ScrollBar) |
| `src/TCTV/Views/` | Haupt-, Einstellungs-, Profil-, Video- und Eingabefenster |
| `src/TCTV/Properties/PublishProfiles/` | `SingleFile-*` (eine Datei, `rollForward: Major`) und `Standalone-*` (self-contained) |
| `tools/publish-singlefile.ps1` | Baut die Einzeldatei-EXE (bzw. die self-contained Variante) und prüft das Ergebnis |
| `tools/render-themes.ps1` | Rendert alle Fenster in allen vier Themes (eigener `--datadir`) |

### Generierte Dateien

`src/TCTV/Generated/` enthält zwei erzeugte Tabellen:

* `LocalizationTable.cs` – die komplette **Sprach-Tabelle** (Deutsch/Englisch,
  alle 324 Zeilen inkl. `@LF`-Umbrüchen).
* `EmbeddedResources.cs` – die **7 eingebetteten Ressourcen**
  (`sTV.ico`, `deutsch.bmp`, `english.bmp`, `keinBild.jpg`, `refresh.png`,
  `movie1.ico`, `folder1.ico`) als Base64 inkl. LZNT1-Kennzeichnung.

`ResourceProvider` dekodiert die Ressourcen (Base64 + LZNT1) zur Laufzeit.


---

## Funktionsumfang

### Umgesetzt

**Kern / Konfiguration**
* `IniFile` zum Lesen/Schreiben/Löschen von INI-Werten:
  case-insensitive Abschnitts-/Schlüsselnamen, Standardwerte, Anlegen bei Bedarf,
  Erhalt von Reihenfolge/Kommentaren.
* `AppSettings` mit den Abschnitten `Benutzerdaten`, `Optionen`, `VLC Fenster`
  und `Programmdaten` (inkl. der überlieferten Schlüsselnamen wie
  `Traspondersender markieren`, `Fenstergröße_fest`).
* `ProfileStore`/`Profile`: Profile als `*.conf3` im Arbeitsverzeichnis,
  aktives Profil in `Benutzerdaten\Profil`, Defaults `root`/`ni`/`80`.
* `Localization` (DE/EN) über die extrahierte Tabelle.
* `Theme` mit Farbtabellen. Das Standarddesign ist
  **Dark orange** mit den Farben von neutrino-images.de; „1“ = dunkler, fast
  transparenter Rahmen (`0x252525`, 50 % Deckkraft).

**Box-Schnittstelle** (`BoxApiClient`, `LegacyControlApi`, `NeutrinoV1Api`,
`RecordingServiceClient`)

Im Profil wählbar (`Profildaten\API-Version`, „Alte…/Neue…“):

* **Alte API `/control/`** (Standard) – `LegacyControlApi`, folgende Endpunkte:
  `/control/info`, `/control/getmode`, `/control/gettime?rawtime`,
  `/control/standby`, `/control/zapto`, `/control/getbouquets`,
  `/control/getbouquet?…&epg=true&format=xml`, `/control/getdir`,
  `/control/getmovies`, `/control/file?action=list`, `/control/timer`
  (`?format=xml`, `?format=id`, `?action=new`), `/control/currenttpchannels`.
  Basisauthentifikation über `NetworkCredential`
  (`Web-Authentifikation`); XML-Parsing von
  `<channel>`/`<firstEPG>`/`<secondEPG>`, `<dirs>`, `<movies>`, `<filelist>` und
  der Timer-Liste. Die Datei-Endpunkte liefern **absolute Pfade** der Box
  (`dir`/`path`/`fullname`), die direkt weiterverwendet werden.
* **Neue API `/api/v1/`** – `NeutrinoV1Api`, die REST-Schnittstelle des
  NI-Images (<https://github.com/neutrino-images/ni-neutrino>, `src/httpd/ep/*`).
  Gegen JSON, mit den Eigenheiten dieser Schnittstelle:
  * **Anmeldung**: sie kennt keine Basisauthentifikation. Lesen darf jeder im
    eigenen Netz (`lan_read`), alles andere braucht eine Sitzung
    (`POST /api/v1/login` mit `user`/`password`; die Antwort liefert `csrf`,
    `csrf_header` und `expires_in`). Das Programm meldet sich selbst an, merkt
    sich den zweiten Token und schickt ihn als `X-CSRF-Token` mit – ohne ihn
    werden POST/PUT/PATCH/DELETE mit `403` abgelehnt.
  * **Endpunkte**: `/api/v1/system/info`, `/api/v1/system/standby`,
    `/api/v1/channels` (geblättert über `next_cursor`, `bouquet=<nr>`),
    `/api/v1/channels/current`, `/api/v1/bouquets`,
    `/api/v1/epg/grid?bouquet=&from=&to=`, `/api/v1/timers` (GET/POST),
    `/api/v1/recordings`, `/api/v1/settings/recording`,
    `/api/v1/storage/files?path=`, `/api/v1/storage/file?path=&token=`,
    `/api/v1/token/media`, `/api/v1/zap`, `/api/v1/stream/{id}`.
  * **Umsetzung der Unterschiede** zur alten API: Senderliste und EPG kommen aus
    zwei Aufrufen (die Sendungen des Bouquets werden über `epg/grid` geholt und
    auf die Sender verteilt – erste = laufende bzw. nächste, zweite = folgende);
    Timer werden in **Unix-Sekunden** angelegt (`kind`); der Zap (`POST /api/v1/zap`)
    sendet `wake:false`, damit eine Box im Standby beim Streamen nicht geweckt wird
    (die Live-Adresse liefert `GET /api/v1/stream/{id}`); die Sender des laufenden
    Transponders werden aus
    Satellitenposition/Transponderschlüssel/Transportstrom der Senderliste
    berechnet (die neue API kennt kein `currenttpchannels`); Dateien gehen über
    `/api/v1/storage/file` und werden mit einem kurzlebigen Medien-Token aus
    `POST /api/v1/token/media` in der Adresse abgespielt; als Live-Adresse dient
    `http://<host>:<StreamingPort>/id=<id>` (von `/api/v1/stream/{id}` geliefert,
    mit Rückfall auf den Streaming-Port des Profils).

**GUI (WPF)**
* `MainWindow`: Einblendfenster am rechten/linken Rand, Topmost, ohne Taskleiste,
  mit Transparenz aus dem Theme; Kopfzeile mit Uhr und Verbindungsstatus;
  Bouquet-Auswahl; Senderliste mit EPG-Zeile; Detailbereich (Startzeit, Dauer,
  Info1/Info2, Beschreibung, Folgesendung); Werkzeugleiste
  (Stream, Stop, Video, EPG aktualisieren, Timer, Einstellungen, Profile,
  Vollbild-Hotkey, Beenden).
* **Kopfzeile nennt das Profil**: unter dem Titel steht die Statusmeldung mit dem
  Namen des aktiven Profils, z. B. „Verbunden! · Box100“ bzw.
  „Keine Verbindung zur Box · Box100“ – so ist auf einen Blick klar, mit welcher
  Box man verbunden ist (`MainWindow.SetStatus` + `StatusLine`). Der Statuspunkt
  hängt mit der Maus an einem Tooltip, der Profil, Adresse und Schnittstelle
  nennt (z. B. „Profil: Box100 / Box: 192.168.178.222:80 / API: Alte API
  (/control/)“).
* **Fade-in/Fade-out** per Mausposition an der Bildschirmkante, konfigurierbare
  Verzögerung (`fade-in-Verzögerung in Millisekunden`), Pinnen per Tray-Klick.
* **EPG-Auto-Aktualisierung** (`Optionen\fade-in-Auto-Aktualisierung`): ist sie
  gesetzt, holt TC.TV beim **Einblenden** des Fensters EPG und Sender des
  aktuellen Bouquets neu von der Box – man muss also nicht „EPG aktualisieren“
  drücken. Dabei ist es **gebremst**: nachgeladen wird höchstens
  alle 60 Sekunden (`MainWindow.AutoRefreshInterval`), damit hektisches
  Mausschwenken am Rand die Box nicht mit Anfragen überschüttet. Das Fenster
  springt dabei nicht um: es bleibt beim gewählten Bouquet und beim markierten
  Sender. Der Tooltip der Option stammt aus Sprachzeile 51 („EPG wird direkt
  nach Einblendung des Fensters aktualisiert.“).
* **Tray-Icon** mit Menü.
* Hotkeys: `f` = Fenster verbreitern (680 px), `s` = VLC-Stop,
  `Esc` = ausblenden.
* `SettingsWindow`, `ProfilesWindow`, `VideoWindow`, `PromptWindow`.
* **Modernes Design**: eigenes Style-System, dynamisches Theming, Icon-Buttons –
  siehe Abschnitt [Oberfläche & Design](#oberfläche--design).

**VLC-Integration** (`VlcOptions`, `VlcLauncher`)
* Kommandozeilenoptionen
  (`--video-on-top`, `--qt-notification=0`, `--no-qt-video-autoresize`,
  `--qt-start-minimized`, `--no-video-title-show`, `--qt-minimal-view`,
  `--no-qt-name-in-title`, `--deinterlace=-1`, `--fullscreen`, `--one-instance`).
* Pfaderkennung: INI → `VLCPortable\VLCPortable.exe` → `Program Files`
  → `Program Files (x86)` → Registry `HKLM\Software\VideoLAN\VLC` (32/64-bit).
* Live-Stream: `<host>:<StreamingPort>/id=<channelId>`. Bei der neuen API wird
  die Adresse von `/api/v1/stream/{id}` übernommen.
* **Aufnahme-/Video-Wiedergabe per HTTP** (kein FTP):
  `--one-instance http://<host>:<WebserverPort>/<absoluter Pfad>`
  (Leerzeichen → `%20`). Der Pfad stammt aus `/control/getdir`,
  `/control/getmovies` bzw. `/control/file?action=list` – bei der neuen API aus
  `/api/v1/storage/files`, abgespielt über `/api/v1/storage/file` mit
  Medien-Token.
* Stoppen beim Zap: Taste `s` an das VLC-Fenster bzw. Prozess beenden
  (`VLC erhalten beim Zap`, `Beenden`).

### Getestet

* Build ohne Warnungen (`dotnet build TCTV.Net.sln`).
* Start der Anwendung; Anlegen von `TC.TV.ini`, `Box100.conf3`, `Logos/`, `Temp/`.
* Byte-genaue Prüfung aller 7 entpackten Ressourcen (LZNT1).
* VLC-Erkennung über `Program Files (x86)`.
* `--selftest`: alle fünf Fenster (Main, Settings, Profiles, Video, Prompt)
  werden fehlerfrei aufgebaut (Exit-Code 0).
* `--selftest`: **133 Prüfungen** mit synthetischen Antworten
  (Timer-XML ohne/mit Eintrag, `format=id`, ID-Vergleich, Transponder,
  `getdir`/`getmovies`/`file`-Listen, HTTP-Wiedergabe-URL, UTF-8-Umlaute,
  Latin-1-Rückfall, INI-Rundlauf, Fortschrittsberechnung, Themenauswahl,
  Datenpfade, Profilfenster) – alle OK, Exit-Code 0.
* `--selftest` prüft die **neue API** mit 43 weiteren Fällen: Auswahl im Profil
  (`API-Version` alt/neu inkl. Rundlauf durch die `.conf3` und der Auswahl im
  Profilfenster), Senderliste
  (`/channels` mit `next_cursor`, Sendernummern, Logo-Kurz-ID, Transponder je
  Sender), Bouquets, EPG-Raster (`epg/grid` → erste/zweite Sendung, Startzeit,
  Fortschritt und gelaufene Minuten), Timer (`kind`, Unix-Sekunden, Laufzeit),
  laufende Aufnahmen, Dateilisten, Aufnahmeverzeichnis aus `/settings`,
  Zeitumrechnung sowie leere/defekte Antworten.
* **EPG-Auto-Aktualisierung live geprüft** (echtes Fenster, simulierte Box,
  Maus per `Cursor.Position` an den Bildschirmrand): mit gesetzter Option stiegen
  die Anfragen an die Box nach dem Aus-/Einblenden mit frischen Daten **nicht**
  (2 → 2, gebremst), nach über 60 Sekunden wurden sie **nachgeladen** (2 → 3),
  und unmittelbar danach wieder nicht (3 → 3); mit abgeschalteter Option blieb es
  durchgehend bei 2. Das Protokoll der Box belegt zudem, dass beim Nachladen nur
  das **aktuelle** Bouquet geholt wird (`/control/getbouquet`, nicht die
  Bouquetliste) – die Auswahl des Benutzers bleibt also stehen.
  Dazu 11 Selbsttest-Prüfungen (Standard aus, Grenze bei genau 60 s, noch nie
  geladen, Beschriftung + Tooltip aus den Sprachzeilen 50/51 im Einstellungsfenster).
* **Kopfzeile geprüft**: die Statuszeile trägt den Profilnamen
  (`StatusLine("Verbunden!", "Box100")` → „Verbunden! · Box100“, leerer
  Profilname lässt die Meldung unverändert), das aufgebaute Hauptfenster zeigt
  „Verbunden! · Box100“ aus seiner Beispielansicht, und der Tooltip nennt
  Adresse und Schnittstelle (`:80`, „Alte API (/control/)“) – 5 Prüfungen.
  Zusätzlich als PNG in allen vier Themes gesichtet.
* **Live gegen eine simulierte Box geprüft** (`HttpListener` mit den Routen der
  neuen API und einem Protokoll aller Anfragen): Anmeldung (`POST /api/v1/login`),
  Sitzungs-Cookie, zweiter Token als `X-CSRF-Token` bei allen ändernden Aufrufen,
  Senderliste in zwei Seiten, EPG-Zuordnung, Medien-Token in der Datei-Adresse,
  Zap (`{"channel_id":…,"wake":false}`), Timer als Unix-Sekunden – alles aus dem
  echten Client (`RecordingServiceClient`) heraus, inklusive Berechnung der
  aktuellen Transpondersender.
* Profilfenster **live** geprüft (UI-Automation mit echtem Mausklick auf
  „Profile“ → „Speichern“): der Klick kommt an, die `.conf3` wird neu
  geschrieben, `Benutzerdaten\Profil` gesetzt, `ShowDialog()` liefert `true`
  und das Fenster ist danach geschlossen.
* `--render`: alle Fenster zusätzlich als PNG geprüft – in allen Themes
  (Dark orange = Standard, Dark grün, Hell, TC.TV Overlay). Für alle vier
  Themes auf einmal: `powershell -File tools\render-themes.ps1`
  (schreibt je Theme einen Ordner und setzt vorher `Theme=` in der INI).
  Die Beispieldaten des Hauptfensters bleiben dabei stehen: ohne das
  überschrieb der erste Netzabruf sie wieder mit „Keine Verbindung zur Box“
  (`MainWindow.SkipInitialLoad`).
* `--selftest`: 12 Design-Prüfungen (Dark orange als Standard inkl. der exakten
  NI-Farbwerte `#212121`/`#424242`/`#616161`/`#E0E0E0`/`#9E9E9E`/`#FD6D13`,
  sowie die übrigen Themes) – alle OK.

### Einzeldatei-EXE und Datenablage – geprüft

* `tools/publish-singlefile.ps1` liefert **genau eine Datei** `dist\TC.TV.exe`
  (971.857 Bytes) – die `runtimeconfig.json` ist mit eingebettet.
* Lauffähig geprüft: `TC.TV.exe --selftest` **aus dem Publish-Ordner** → 133/133 OK,
  Exit-Code 0.
* **Aufwärtskompatibilität nachgemessen** (Umgebung mit *nur* .NET 10: eigene
  `DOTNET_ROOT`-Wurzel mit `host`- und `shared`-Junctions auf echte 10.0.8-Frameworks):

  | Auslieferung | nur .NET 10 vorhanden | kein .NET auffindbar |
  |---|---|---|
  | `TC.TV.exe` (SingleFile, 1 Datei) | **OK – `Laufzeit: .NET 10.0.8`** | – |
  | `TC.TV.exe` (Standalone, 1 Datei) | OK – `Laufzeit: .NET 8.0.27` | **OK – 133/133 in 2 s** |

* `COREHOST_TRACE=1` belegt, dass der Host `rollForward` **aus dem Bundle** anwendet:
  `'Roll forward' enabled with version_compatibility_range [major]` und
  `framework:'Microsoft.WindowsDesktop.App', lowest requested version='8.0.0', found version='10.0.8'`.
  *(Ein früherer Messwert `version_compatibility_range=minor` stammte von einer
  veralteten EXE-Kopie von vor dem `RollForward`-Eintrag – daher die damals falsche
  Annahme, das Bundle werde nicht ausgewertet.)*
* Kontrolle mit `DOTNET_ROLL_FORWARD=Disable`: Start schlägt fehl → die Umgebung
  hat wirklich kein 8.x, der Erfolg oben kommt also vom RollForward.
* Mit vorhandenem 8.x läuft die App wie bisher auf **.NET 8.0.31** (8.x wird bevorzugt).
* Standalone-Variante: `dist\standalone\TC.TV.exe` (72,0 MB) in einen leeren Ordner
  kopiert und mit `DOTNET_ROOT` auf einen **leeren** Ordner plus
  `DOTNET_MULTILEVEL_LOOKUP=0` gestartet – also ohne irgendeine auffindbare
  .NET-Laufzeit: **ExitCode 0, 133/133 OK in 2 s**. x86: 66,4 MB, ebenfalls eine Datei.
* EXE-Icon geprüft: `ExtractAssociatedIcon` liefert das Anwendungssymbol
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
Neutrino-Box oder einer echten Box mit NI-Image (keine im
Testsystem vorhanden). Die Endpunkte, XML-/JSON-Strukturen und Kommandozeilen
wurden aus den NI-Firmware-Quellen abgeleitet; für die
neue API zusätzlich gegen eine simulierte Box durchgespielt (siehe oben).

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

Aufnahmen werden nicht mehr über FTP (`ftp://root:…@box/pfad`) abgespielt; die
Profile enthalten keine FTP-Zugangsdaten.

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


### Neue API `/api/v1/` – Abgleich mit dem NI-Image

Die neue Schnittstelle wurde gegen die Quellen des Images abgeglichen:
<https://github.com/neutrino-images/ni-neutrino> (`src/httpd/routes.cpp`,
`src/httpd/ep/*.cpp`, `src/httpd/server.cpp`, `src/httpd/auth.cpp`,
`src/coreapi/streaming.cpp`). Umgesetzt sind genau die Routen, die das Programm
braucht:

| Route | Antwort der Box | Umsetzung hier |
|---|---|---|
| `POST /api/v1/login` | `{"authenticated":true,"level":"system","user":…,"csrf":…,"csrf_header":…,"expires_in":…}` + `Set-Cookie: ni-web-session=…` | eigene Anmeldung, Cookie und `X-CSRF-Token` automatisch |
| `GET /api/v1/system/info` | `{"box":{…},"api":{…}}` | Verbindungstest |
| `GET /api/v1/system/standby` | `{"on":true\|false}` | Standby-Erkennung |
| `POST /api/v1/system/standby` | `{"on":true\|false}` im Body, Systemrechte | Standby schalten (CEC kennt die neue API nicht) |
| `GET /api/v1/channels?bouquet=&limit=&cursor=` | `{"items":[…],"next_cursor":"…"}` | blättert über `next_cursor` (Grenzen: 500/Seite) |
| `GET /api/v1/channels/current` | `{"id":…,"name":…,"kind":"tv\|radio\|webtv\|webradio"}` | Modus-Erkennung, aktueller Sender/Transponder |
| `GET /api/v1/bouquets` | `{"items":[{"id":…,"name":…,"hidden":…,"locked":…,"tv_count":…}]}` | Bouquet-Auswahl (Nummer = `id`) |
| `GET /api/v1/epg/grid?bouquet=&from=&to=&cursor=` | `{"items":[{"channel_id":…,"events":[{"id","title","description","start","duration"}]}],"next_cursor":…}` | Sendungen je Sender; daraus erste (laufende bzw. nächste) und zweite |
| `GET /api/v1/timers` | `{"items":[{"id","kind","channel_id","start","stop","title","state",…}]}` | Timerliste (Sendernamen aus der Senderliste) |
| `POST /api/v1/timers` | `{"id":…}` (201), `kind` = `record` | Timer anlegen, Zeiten als **Unix-Sekunden** |
| `GET /api/v1/recordings` | `{"items":[{"id","channel_id","title","start","timeshift",…}]}` | laufende Aufnahmen |
| `GET /api/v1/settings/recording` | `{"items":[{"id":"network_nfs_recordingdir","value":…}]}` | Aufnahmeverzeichnis |
| `GET /api/v1/storage/files?path=` | `{"items":[{"id":"Name","kind":"dir\|regular\|…","size":…}]}` | Verzeichnislisten (Systemrechte) |
| `GET /api/v1/storage/file?path=&token=` | Bytes der Datei | Wiedergabe der Aufnahme |
| `POST /api/v1/token/media` | `{"token":…,"scope":"media","expires_in":…}` | Medien-Token für die Adresse (nur diese Route nimmt eines an) |
| `POST /api/v1/zap` | 202, Body `{"channel_id":…,"wake":…}` | Umschalten |
| `GET /api/v1/stream/{id}` | `{"url":…,"name":…,"port":…}` | Live-Adresse |

Eigenschaften der Schnittstelle, die den Aufbau bestimmen:

* **Basic wird nur auf der alten Oberfläche akzeptiert.** `src/httpd/auth.h`
  sagt das ausdrücklich („Basic is accepted only on the legacy surface under
  `/control/`“), deshalb meldet sich das Programm an und benutzt Sitzung +
  CSRF-Token.
* **Schreiben braucht eine Sitzung**, Lesen erlaubt die Box jedem im eigenen
  Netz (`lan_read`); `/api/v1/storage/*` und `/api/v1/token/media` verlangen
  mehr als `read`.
* **Rechteprüfung vor dem Body**: der Router entscheidet anhand des Pfades,
  bevor der Body gelesen wird – ein abgelehnter Aufruf kommt sofort zurück.
* **Der Router verwirft `?token=`**, bevor er die Parameter prüft; ein Token
  in der Adresse ist also kein Hindernis für die Routen-Prüfung.
* **`/api/v1/epg/grid`** ist auf 100 Zeilen je Seite begrenzt
  (`MAX_GRID_CHANNELS`) und endet mit fehlendem `next_cursor`; `limit` der
  Senderliste ist bei 500 gedeckelt (`kMaxPage`). Beides wird beim Blättern
  berücksichtigt.
* Als Live-Adresse wird `http://<host>:<StreamingPort>/id=<id>` benutzt
  (`addressPrefix` in `src/coreapi/streaming.cpp` baut genau dieses Format, mit
  dem `streaming_port` der Box).


### Behobener Fehler: dauerhafte Meldung „Aufnahme läuft!“

Ursprünglich wurde für die Warnung `GetTimersAsync()` (XML) ausgewertet und nur
gezählt, ob **überhaupt** Timer existieren. Da `format=xml` das Element `timer`
zweimal verwendet (Wurzel **und** Eintrag), lieferte die Abfrage selbst bei
**null** Timern ein Ergebnis → die Meldung erschien bei **jedem** Streamstart.

Korrekt umgesetzt ist jetzt folgende Logik:

1. **Multituner-Box** (`Profile.MultiTuner`) → keine Warnung.
2. **Aktuell laufende Aufnahmen** bestimmen: Timer aus `/control/timer?format=id`
   gegen die Boxzeit (`/control/gettime?rawtime`) prüfen – nur Timer mit
   `Start ≤ jetzt < Ende` zählen. Geplante oder beendete Timer blockieren nicht.
   Bei der neuen API kommen die laufenden Aufnahmen aus `/api/v1/recordings`
   (die Box meldet sie selbst) und die Uhrzeit von der lokalen Uhr.
3. Wird **derselbe** Sender aufgenommen (`ChannelIdsMatch`, tolerant gegenüber
   führenden Nullen) → keine Warnung.
4. Liegt der Sender auf dem **aktuellen Transponder** (`/control/currenttpchannels`;
   bei der neuen API aus Satellitenposition/Transponderschlüssel/Transportstrom
   der Senderliste berechnet) → keine Warnung.
5. Nur wenn all das nicht zutrifft, erscheint die Meldung
   („Aufnahme läuft!“ / „Aktuell Streaming nur auf gleichem Transponder möglich.“)
   und der Stream wird **nicht** gestartet.

Zusätzlich parst `ParseTimersXml` nur noch `<timer>`-Elemente **mit** `<id>`
(damit der Wurzelknoten nicht als Geister-Timer zählt) und liest Start/Ende aus
`<alarm>/<stop>` → `<normal><digits>` (Unix-Sekunden).

Der `--selftest` prüft diese Logik mit synthetischen Antworten
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

Ursache: `GetTextAsync` dekodierte die Antwort mit **Latin-1**. Die Box liefert
aber **UTF-8** – `yhook.cpp`
(`outValue` → `convert_UTF8_To_UTF8_XML`) und der Header
`Content-Type: …; charset=UTF-8` belegen das. UTF-8-Bytes `C3 A4` („ä“) wurden
dadurch zu „Ã¤“.

Lösung: `Core/TextEncoding.cs` dekodiert **UTF-8** und fällt nur dann auf
Latin-1 zurück, wenn die Antwort kein gültiges UTF-8 ist (`DecoderFallbackException`).
`RecordingServiceClient.DecodeText` und `IniFile` nutzen dieselbe Routine – so
bleiben auch Umlaute in INI-Schlüsseln erhalten, selbst wenn die Datei noch in
ANSI vorliegt.

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
  Wert.
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
* Weitere Themes: „Dark grün“ (`2`), „Hell“ (`4`),
  „TC.TV Overlay“ (`1`, dunkler Rahmen `0x252525`, stark transparent).
  Leer/`3` = Dark orange (Standard).

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

**Profilfenster** – **„Speichern“** schließt das Fenster und
übernimmt das bearbeitete Profil als aktuelles: die Werte gehen in die
`.conf3`-Datei, der Name wird als `Benutzerdaten\Profil` in die INI geschrieben
und das Hauptfenster lädt Profil und Senderliste neu
(`ProfilesWindow.ApplyAndActivate()`).
„Profil wechseln“ löst denselben Ablauf aus, „Zurück“ schließt ohne zu speichern.
Neu im Profilfenster ist die Auswahl **API** (`Profildaten\API-Version`):
„Alte API (/control/)“ (Standard) oder
„Neue API (/api/v1/)“ für das NI-Image – siehe „Box-Schnittstelle“ oben.

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

---

## Hinweise

* In der Profildatei steht zusätzlich `Profildaten\API-Version` (`alt`/`neu`);
  fehlt der Schlüssel, gilt die alte API – Profile aus früheren
  Programmversionen laufen also unverändert.
* INI und Box-Antworten werden als **UTF-8** gelesen – die Box liefert UTF-8
  (`charset=UTF-8`, `convert_UTF8_To_UTF8_XML`). Ist der Inhalt kein gültiges
  UTF-8 (etwa eine in ANSI geschriebene INI), wird automatisch auf Latin-1
  zurückgefallen, damit Umlaute erhalten bleiben. Geschrieben wird UTF-8 ohne BOM.
