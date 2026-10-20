# TC.TV – WPF-Client für Neutrino-Receiver

**TC.TV** ist ein halbtransparentes Einblendfenster am Bildschirmrand (Windows, WPF / .NET 8)
für Neutrino-basierte Receiver: Sender- und EPG-Liste, VLC-Streaming, Video-Browser,
Timer, Profile und automatische Updates.

Es werden zwei Box-Schnittstellen unterstützt:

- **Alte API** `/control/` (Standard, auch für ältere Neutrino-Images)
- **Neue API** `/api/v1/` (NI-Image, JSON + Login)

---

## Funktionen

- **Hauptfenster** am linken/rechten Bildschirmrand (Topmost, ohne Taskleiste) mit
  Bouquet-Auswahl, Senderliste, EPG-Zeile, Fortschrittsbalken und Detailbereich.
- **EPG**: laufende und folgende Sendung im Hauptfenster; „Weiteres EPG“ (Rechtsklick)
  zeigt alle kommenden Sendungen eines Senders inkl. ausführlicher Beschreibung.
- **Radio**: Radio-Bouquets werden korrekt angezeigt (TV-/Radio-Hälfte der neuen API,
  `mode=radio`-Fallback) und direkt über den Streaming-Port gestreamt.
- **Video-Browser**: Aufnahmen und Videos per HTTP; die Beschreibung einer Aufnahme wird
  aus der `.xml`-Metadatendatei neben der `.ts` gelesen.
- **Timer**, **Programmübersicht/-suche**, **Senderlogos** (mit Downloader),
  **Profil-Export/-Import**.
- **VLC-Streaming** für Sender und Aufnahmen (portabler VLC wird erkannt).
- **Automatische Updateprüfung** gegen die GitHub-Releases (lädt die passende EXE-Variante).
- **Theming** (Standard „Dark orange“), Tray-Icon, Hotkeys, EPG-Auto-Aktualisierung.

---

## Schnellstart

```powershell
dotnet build TCTV.Net.sln
dotnet run --project src/TCTV/TCTV.csproj
```

Beim ersten Start wird ein Profil (`Box100.conf3`) mit der Box-IP angelegt – im
Profilfenster anpassen (Host, Webport, Streamingport, API-Version).

### Einzeldatei-EXE bauen

```powershell
# EINE Datei, ~1 MB, braucht .NET-Desktop-Runtime 8+ (rollForward: Major)
powershell -File tools/publish-singlefile.ps1                     # dist\TC.TV.exe (x64)

# EINE Datei, ~70 MB, self-contained (kein .NET nötig)
powershell -File tools/publish-singlefile.ps1 -Variant Standalone  # dist\standalone\TC.TV.exe
```

| Variante | Ergebnis | Voraussetzung auf dem Zielrechner |
|---|---|---|
| SingleFile | `dist\TC.TV.exe` (x64) | .NET-Desktop-Runtime 8+ |
| Standalone | `dist\standalone\TC.TV.exe` (x64) | keine (.NET eingebettet) |

---

## Datenablage

Benutzerdaten liegen im **Benutzerprofil**, nicht neben der EXE:

```
%APPDATA%\TC.TV\TC.TV_Workdir\
├── TC.TV.ini         # Einstellungen
├── Box100.conf3      # Empfängerprofile
├── Logos\            # Senderlogos
├── Lang\             # zusätzliche .lang-Sprachdateien
├── VLCPortable\      # optionaler portabler VLC
└── Temp\             # Arbeitsdateien
```

Portabel/Test: `TC.TV.exe --datadir <Ordner>` oder Umgebungsvariable `TCTV_DATADIR`.

---

## Box-Schnittstellen

Im Profil wählbar (`Profildaten\API-Version`): **„Alte API (/control/)“** (Standard) oder
**„Neue API (/api/v1/)“**.

### Alte API `/control/`

XML-/plain-Antworten, Basisauthentifikation. Verwendet u. a. `getbouquets`,
`getbouquet?epg=true&format=xml`, `getdir`, `getmovies`, `file?action=list`,
`timer`, `zapto`, `currenttpchannels`. Dateien liefert der Webserver direkt
(`http://<host>:<Webport>/<absoluter Pfad>`).

### Neue API `/api/v1/` (NI-Image)

JSON, kein Basic. Lesen erlaubt die Box im eigenen Netz; Schreiben braucht
`POST /api/v1/login` (Sitzungs-Cookie + `X-CSRF-Token`). Dateien gehen über
`/api/v1/storage/file` mit einem Medien-Token (`POST /api/v1/token/media`).

Wichtigste Endpunkte:

| Endpunkt | Zweck |
|---|---|
| `/api/v1/bouquets` | Bouquets (mit `tv_count`/`radio_count`) |
| `/api/v1/channels?bouquet=&mode=tv\|radio` | Sender eines Bouquets (blättert über `next_cursor`) |
| `/api/v1/epg/grid?bouquet=&from=&to=` | Sendungen eines Bouquets |
| `/api/v1/epg/event?id=&start=` | ausführliche Beschreibung einer Sendung |
| `/api/v1/timers`, `/api/v1/recordings` | Timer, laufende Aufnahmen |
| `/api/v1/storage/files`, `/api/v1/storage/file` | Dateiliste und Dateiabruf |
| `/api/v1/zap`, `/api/v1/stream/{id}` | Umschalten, Live-Adresse |

Jedes Bouquet hat eine Fernseh- und eine Radio-Hälfte; `/api/v1/channels` liefert ohne
`mode` nur das Fernsehen. TC.TV lädt bei leerer Fernsehliste automatisch `mode=radio` nach,
damit reine Radio-Bouquets nicht „offline“ erscheinen.

---

## Oberfläche & Design

- Eigenes **Style-System** (`src/TCTV/Styles/Controls.xaml`): Buttons, Eingaben, Listen,
  Karten, Fortschrittsbalken, ScrollBar; Icons über Segoe MDL2 Assets.
- **Dynamisches Theming**: alle Fenster binden per `DynamicResource` – ein
  Theme-Wechsel färbt die Anwendung sofort um.
- Standard **„Dark orange“** (Farben von neutrino-images.de):

  | Rolle | Wert |
  |---|---|
  | Hintergrund / Fläche / Rahmen | `#212121` / `#424242` / `#616161` |
  | Primärtext / Sekundärtext | `#E0E0E0` / `#9E9E9E` |
  | Akzent | `#FD6D13` |

  Weitere Themes: „Dark grün“ (`2`), „Hell“ (`4`), „TC.TV Overlay“ (`1`).

---

## Projektstruktur

| Pfad | Inhalt |
|---|---|
| `src/TCTV/TCTV.csproj` | WPF-Projekt (`net8.0-windows`) → `TC.TV.exe` |
| `src/TCTV/Core/` | Box-Schnittstellen (alt/neu), Konfiguration, VLC, Theming, Localization, Modelle |
| `src/TCTV/Views/` | Haupt-, Einstellungs-, Profil-, Video-, EPG-, Timer-, Hilfe-Fenster |
| `src/TCTV/Styles/Controls.xaml` | Style-System |
| `src/TCTV/Languages/` | Sprachdateien (`deutsch.lang`, `english.lang`, …) |
| `tools/publish-singlefile.ps1` | baut die Einzeldatei-EXE und prüft sie |

---

## Entwickler-Werkzeuge

| Aufruf | Wirkung |
|---|---|
| `TC.TV.exe --selftest` | baut alle Fenster unsichtbar auf und schreibt `selftest.log` (Exit 1 bei Fehlern) |
| `TC.TV.exe --render [Ordner]` | speichert PNG-Abbildungen aller Fenster |
| `TC.TV.exe --datadir <Ordner>` | verlegt die Benutzerdaten (portabel/Test) |
| `powershell -File tools/render-themes.ps1` | rendert alle Fenster in allen Themes |

---

## Hinweise

- INI und Box-Antworten werden als **UTF-8** gelesen (Rückfall auf Latin-1 bei
  ungültigem UTF-8); geschrieben wird UTF-8 ohne BOM.
- `Profildaten\API-Version` (`alt`/`neu`): fehlt der Schlüssel, gilt die alte API.
- **Neue Sprache hinzufügen**: eine Datei `<name>.lang` in den Ordner `Lang\`
  der Datenablage legen (je Zeile `schlüssel=wert`, `\n` = Zeilenumbruch). Sie
  erscheint danach in der Sprachauswahl; deutsch/english sind eingebettet.