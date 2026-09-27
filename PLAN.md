# Plan: OpenVoiceSpeak - selbst hostbarer Voice-Server und Client

> **Hinweis zur Reihenfolge:** Die Packages folgen den technischen Abhängigkeiten, nicht den Phasen aus der Besprechung. Grosse Phasen wurden so aufgeteilt, dass jedes Package mit einem grünen, commitfähigen Stand endet. Zuordnung:
>
> | Phase aus der Besprechung | Packages |
> |---|---|
> | Phase 0: Grundgerüst | 1 |
> | Phase 1: Steuerkanal und Handshake | 2, 3, 4 |
> | Docker-Betrieb (früh, damit Pfad- und Rechteprobleme sofort auffallen) | 5, 19 |
> | Phase 2: Channels | 7 |
> | Phase 3: Rechtesystem | 6, 8, 9 |
> | Channel-Linking | 10 (Server), 12 (Routing), 14 (Link-PTT), 16 und 18 (UI) |
> | Phase 4: Sprachübertragung | 11, 12, 13, 14, 15 |
> | Phase 5: Client-UI | 16, 17, 18 |
> | Phase 6: Hosting | 19 |
>
> **Parallel möglich:** Nach Package 1 können 2, 3, 6, 11 und 13 unabhängig voneinander laufen. Nach Package 7 können 8, 9 und 10 parallel laufen.

## Überblick

| # | Package | Ziel in einem Satz | Abhängig von |
|---|---------|--------------------|--------------|
| 1 | Grundgerüst | Die Solution mit Shared-, Server-, Client- und Testprojekt baut, und `dotnet test` läuft grün. | - |
| 2 | Protokoll-Framing | Steuernachrichten werden als längenpräfixiertes JSON sicher gelesen und geschrieben. | 1 |
| 3 | Serverkonfiguration | Der Server bestimmt Port, Datenverzeichnis und Limits aus Umgebungsvariablen, Konfigurationsdatei und Standardwerten. | 1 |
| 4 | TLS-Verbindung und Handshake | Clients authentifizieren sich per TLS und Challenge-Signatur mit ihrem Schlüsselpaar beim Server. | 2, 3 |
| 5 | Docker-Betrieb | Der Server läuft als Container ohne Root-Rechte mit persistentem `/data`-Volume und beendet sich bei SIGTERM sauber. | 4 |
| 6 | Rechtemodell | Serverweite Rechte, Gruppen und die Regeln gegen Rechteausweitung existieren als reine, getestete Logik. | 1 |
| 7 | Serverzustand und Channels | Clients erhalten den Serverzustand live und verwalten Channels rechtegeprüft und persistent. | 4, 6 |
| 8 | Server-Administration | Admins verwalten Gruppen, Zuordnungen und Servereinstellungen über das Protokoll. Der erste Admin entsteht per Admin-Token. | 7 |
| 9 | Moderation | Berechtigte können kicken, bannen und serverseitig stummschalten. Gebannte kommen nicht mehr herein. | 7 |
| 10 | Channel-Linking (Server) | Channels lassen sich rechtegeprüft und dauerhaft paarweise verlinken. | 7 |
| 11 | Voice-Paketformat und Verschlüsselung | Sprachpakete haben ein festes Binärformat, sind per AES-GCM geschützt, und Wiederholungen werden verworfen. | 1 |
| 12 | Voice-Relay | Der Server leitet Sprachpakete per UDP an genau die berechtigten Empfänger weiter, inklusive Link-Übertragungen. | 4, 7, 9, 10, 11 |
| 13 | Client-Empfangspfad Audio | Opus-Pakete mehrerer Sprecher werden zu einem gemischten, lückenlosen PCM-Strom. | 1 |
| 14 | Client-Sendepfad und Sprechmodi | Mikrofonaudio wird je nach PTT, Link-PTT oder Sprachaktivierung mit dem richtigen Ziel kodiert. | 11, 13 |
| 15 | Client-Netzwerkschicht | Der Client verbindet sich per TOFU-geprüftem TLS, spiegelt den Serverzustand und tauscht Sprachpakete aus. | 4, 7, 10, 12 |
| 16 | Client-Hauptfenster | Nutzer verbinden sich über die Oberfläche, sehen Channels, Links und Sprecher und sprechen miteinander. | 13, 14, 15 |
| 17 | Client-Einstellungen | Audiogeräte, Lautstärken, Sprechmodus, Tasten und VAD-Schwelle sind einstellbar und bleiben gespeichert. | 16 |
| 18 | Admin- und Moderations-UI | Berechtigte erledigen Channelverwaltung, Linking, Moderation und Gruppenverwaltung vollständig im Client. | 8, 9, 10, 16 |
| 19 | Hosting-Abschluss | Multi-Arch-Server-Images und ein Windows-Client-Build sind reproduzierbar baubar und dokumentiert. | 5, 12, 18 |
| 20 | Debug-API für den Client | Der Client lässt sich ohne Maus und Tastatur komplett steuern und prüfen. | 16, 17, 18 |
| 21 | Server- und Channel-Logs | Der Server schreibt allgemeine Ereignisse in ein Server-Log und alles Channel-bezogene in ein eigenes Log pro Channel. | 12 |
| 22 | Client-Log | Der Client schreibt alles, was er tut und erlebt, in eine einzige Logdatei im Profil. | 16, 20 |
| 23 | Automatischer Neustart und Log-Tageswechsel | Der Server startet auf Wunsch täglich zu einer einstellbaren Uhrzeit neu, und der Tageswechsel der Logs ist abschaltbar. | 19, 21 |

## Annahmen

Die offenen Fragen aus der Besprechung wurden nicht beantwortet. Deshalb gelten die dort genannten Standardwerte:

- **A1 Rechtesystem:** "Serverübergreifend" bedeutet **serverweit**. Gruppen gelten für alle Channels eines Servers. Es gibt keine Rechte, die über mehrere Server hinweg gelten, und keine Channel-Rechte.
- **A2 Stack:** C# / .NET 10 (SDK 10 ist installiert), Avalonia mit dem MVVM-Template (bringt CommunityToolkit.Mvvm mit), NAudio (WASAPI), Concentus 2.x (Opus in reinem C#), xUnit.
- **A3 Plattformen:** Der Client läuft zunächst nur unter Windows. Der Server läuft unter Linux (amd64, arm64) im Docker-Container und lokal unter Windows.
- **A4 Kein Textchat** im MVP.
- **A5 Links sind nicht transitiv.** Aus A-B und B-C folgt keine Verbindung A-C.
- **A6 Link-PTT schliesst den eigenen Channel ein.**
- **A7 Link-Übertragung nur per Taste.** Sprachaktivierung sendet nie an Links. Ohne das Recht `SpeakLinked` fällt Link-PTT auf den eigenen Channel zurück.
- **A8 Standardport 7000**, TCP (Steuerung) und UDP (Sprache) auf derselben Nummer.
- **A9 Flache Channel-Liste** mit einem Standard-Channel "Lobby", der nicht gelöscht werden kann.
- **A10 Einstellungen:**
  - Port, Datenverzeichnis und Nutzerlimit kommen aus Umgebungsvariablen oder `server-config.json`.
  - Servername, Willkommenstext und Passwort sind zur Laufzeit änderbar und liegen in `server-data.json`. `OVS_SERVER_NAME` und `OVS_PASSWORD` sind nur Startwerte, wenn die Daten-Datei neu angelegt wird.
- **A11 Rangordnung:** Kicken, Bannen, Verschieben und Stummschalten geht nur bei Nutzern, deren Rechte eine Teilmenge der eigenen sind. Ein Moderator kann also keinen Admin kicken.
- **A12 Gleiche Identität doppelt verbunden:** Die neue Verbindung ersetzt die alte.
- **A13 PTT-Erkennung:** Die Tasten werden per Polling mit `GetAsyncKeyState` im 10-ms-Takt abgefragt. `RegisterHotKey`, wie in der Besprechung genannt, liefert kein Loslassen-Ereignis und taugt deshalb nicht für PTT. Standardtasten: Maustaste X1 für PTT, X2 für Link-PTT.
- **A14 Server-Mute** gilt nur für die aktuelle Session und wird nicht gespeichert.
- **A15 Keine Veröffentlichung** in einer Container-Registry. Das Image wird nur lokal gebaut.
- **A16 Client-Identitätsschlüssel** liegt unverschlüsselt in `%APPDATA%\OpenVoiceSpeak\identity.key`, wie bei Mumble. Schutz per DPAPI kommt später.
- **A17 Serverzustand** ist durch einen globalen Lock geschützt. Das reicht für kleine und mittlere Server.
- **A18 Keine Echo- oder Rauschunterdrückung** im MVP.
- **A19 Projektzustand:**
  - Das Projektverzeichnis enthält noch keinen Code und ist kein Git-Repo. Es liegen dort nur Tool-Artefakte (`.claude-flow/`, `.swarm/`, `ruvector.db`), die in die `.gitignore` kommen.
  - Alle Pfade in diesem Plan sind neu anzulegen.
  - Der Testbefehl `dotnet test` (im Projekt-Root) existiert ab Package 1. Gefilterte Läufe nutzen `dotnet test --filter "FullyQualifiedName~<Teil>"`.

### Projektstruktur (Zielbild)

```
OpenVoiceSpeak.slnx
Directory.Build.props
Dockerfile, .dockerignore, docker-compose.yml, README.md
src/OVS.Shared/   Protocol/, Identity/, Voice/, Permissions/
src/OVS.Server/   Program.cs, ServerConfig.cs, ControlServer.cs, Session.cs, ServerState.cs,
                  Tls/, Data/, Permissions/, Commands/, Voice/
src/OVS.Client/   App.axaml, Views/, ViewModels/, Net/, Audio/, Input/, Settings/
tests/OVS.Tests/  TestSupport/, Protocol/, Shared/, Server/, Voice/, Client/
```

**Testkonventionen:**
- Ein xUnit-Projekt. Der Ordner spiegelt den Bereich wider, die Dateien heissen `<Klasse>Tests.cs`, die Methoden `Methode_Situation_Erwartung`.
- Netzwerktests laufen gegen einen In-Process-Server auf `127.0.0.1` mit Port 0 (`TestSupport/TestServer.cs`).
- Zeitabhängige Logik bekommt `TimeProvider` (Standardbibliothek) injiziert. In den Tests wird er durch `TestSupport/ManualTimeProvider.cs` ersetzt.

## Umsetzungsstand (27.09.2026)

Alle 23 Packages sind umgesetzt. Die Tests laufen mit `dotnet test` grün, der Build hat 0 Warnungen. Zwei Acceptance Criteria sind noch offen, weil sie ein Headset bzw. einen Blick auf den Bildschirm brauchen: Package 16 AC9 und Package 17 AC7 (siehe Tabelle der manuellen Checks).

### Bewusste Abweichungen vom Plantext

| Package | Plan | Umsetzung | Grund |
|---|---|---|---|
| 2 | Frame-Obergrenze 64 KiB | 1 MiB | Snapshot, Nutzer- und Bannlisten passen sonst ab einigen hundert Einträgen nicht in einen Frame. Der Speicherschutz bleibt. |
| 2 | `Messages.cs` zuerst nur mit Ping, Pong und Error | alle Nachrichtentypen ab Package 2 | Spätere Packages ergänzen nur die Handler, so baut jeder Commit. |
| 3 | MaxUsers >= 1 | 1 bis 100.000 | Tippfehler mit riesigen Werten fallen sofort auf. |
| 4 | `Welcome { SessionId, VoiceKey, ServerName }` | `Welcome { SessionId, VoiceKey, Snapshot }` | Package 7 ersetzt den Namen durch den Snapshot, der den Namen enthält. |
| 11, 12 | Voice-Klartext von Client an Server: nur Opus | `[FrameSeq][Opus]` in beide Richtungen | Die Paket-Seq ist der GCM-Nonce und wird auch von Pings verbraucht. Als Sprecher-Seq erzeugte jeder Ping eine Lücke und 20 ms mehr Latenz. |
| 12 | nur Ping wird beantwortet | Hello und Ping werden mit Ping beantwortet | Daran erkennt der Client, ob UDP durchkommt (Hinweis "UDP nicht erreichbar"). |
| 13 | AC3: Wiedergabe erst ab 3 Frames | ab 2 Frames oder nach 40 ms Wartezeit (Puffer von 3 auf 2 Frames gesenkt, damit die Latenz unter 150 ms bleibt) | Kurze Äusserungen mit 1 bis 2 Frames würden sonst nie abgespielt. |
| 13 | AC6: Der JitterBuffer beendet den Stream nach 25 fehlenden Frames. | Der JitterBuffer puffert nach 2 verschleierten Frames neu, der Mixer entfernt den Sprecher nach 500 ms. | Die Sender-Seq läuft in Sprechpausen nicht weiter. Nach Plan wäre jede Pause wie Paketverlust mit wachsender Latenz behandelt worden. |
| 14 | `WdlResamplingSampleProvider` | eigener `LinearResampler` | Arbeitet direkt auf jedem Aufnahmepuffer ohne Pull-Kette, für Sprache ausreichend, getestet. |
| 14 | KeyPoller-Ereignisse `Pressed` und `Released` | ein Ereignis `Changed` plus `PttDown` und `LinkPttDown` | Beide Tasten werden im selben Polling-Durchlauf gelesen. |
| 15 | `StateMirror`-Ereignis `Changed` | `Apply` liefert `bool` | Der Aufrufer (`ServerViewModel`) baut direkt neu auf. |
| 16, 18 | Dialoge als XAML (Connect, Tofu, ChannelEdit, Link, Ban, RedeemToken) | in Code gebaut (`Views/SimpleDialogs.cs`) | kleine Formulare, weniger Dateien |
| 16 | `MainViewModel` mit Channel-Baum | `MainViewModel` (Verbindung, Audio) plus `ServerViewModel` (Baum, Befehle) | Trennung von Verbindung und Serverzustand. Tests in `MainViewModelTests` und `ServerViewModelTests`. |
| 17 | Pegelmesser mit Markierung der Schwelle | Pegel und Schwellen-Slider auf derselben Skala direkt untereinander, dazu die Anzeige "über/unter der Schwelle" | Der Slider-Knopf ist die Markierung. |
| alle | eigene Dateien für DataStore, Group, VoiceCrypto, ReplayWindow, RateLimiter, AudioFormat, TransmitController, VoiceActivityDetector, FrameChunker, IdentityStore, KnownServers, AudioDevices | jeweils in der thematisch passenden Datei zusammengefasst, z. B. `DataStore` in `Data/ServerData.cs` | weniger Kleinstdateien. Die Tests sind entsprechend zusammengefasst. |
| Tests | Netzwerktests auf Port 0 | Zufallsport zwischen 20000 und 45000 mit Wiederholung | TCP und UDP brauchen dieselbe Portnummer, und Windows reserviert für UDP Teile des dynamischen Bereichs. |
| 1 | keine `nuget.config` | `nuget.config` nur mit nuget.org | Die globale NuGet-Konfiguration des Entwicklungsrechners verweist auf einen fehlenden Ordner. Mit der Datei baut das Projekt überall gleich. |

### Ergebnisse der manuellen Checks (27.09.2026, Windows 11, Docker Desktop 29.2.1)

| Package, AC | Ergebnis |
|---|---|
| 1, AC1, AC3, AC4 | Build mit 0 Warnungen. Die Referenzen sind wie geplant. `bin/`, `obj/` und die Tool-Artefakte werden ignoriert. |
| 3, AC4 | `OVS_PORT=abc` ergibt eine Meldung und Exit-Code 1. Ebenso: Datenverzeichnis ist eine Datei, Port belegt. |
| 4, AC14 | Das Log zeigt `Listening on 0.0.0.0:7000 (TCP und UDP)` und `Zertifikat-Fingerprint: ...`. |
| 5, AC2 bis AC6 | `compose up` startet, das Log zeigt `Listening on 0.0.0.0:7000`. Der Container läuft als User 1654 (`app`). Der Fingerprint ist nach `down` und `up` gleich. `stop` dauert 1,3 s mit Exit 0. Mit `OVS_PORT=7100` lauscht der Server auf 7100 (arm64-Image). |
| 14, AC9 | Die globale PTT-Taste (F24 per `keybd_event`) wird erkannt: `PTT gedrückt` und `PTT losgelassen` in `audio-debug.log`. |
| 14, AC10 | Das echte Mikrofon (WASAPI, Event-Modus mit 20 ms) liefert 49 bis 50 Frames/s bei gedrückter PTT und 0 ohne PTT. |
| 16, AC9 | **Offen: Test mit Headset.** Ohne Headset geprüft mit zwei echten Clients über den Docker-Server und Testton: Normales PTT erreicht den gelinkten Channel nicht, Link-PTT schon. Gemessen von PTT bis zum ersten empfangenen Frame: 34 bis 62 ms. Mit Jitter-Puffer (2 Frames, 40 ms), Wiedergabepuffer (bis 40 ms) und WASAPI-Ausgabe (30 ms) ergibt sich rechnerisch eine Gesamtlatenz von etwa 135 bis 170 ms. Ob das spürbar unter 150 ms liegt, zeigt nur der Headset-Test. |
| 17, AC6 | Das Ausgabegerät wurde während eines Gesprächs dreimal gewechselt (VG245, Elgato Music, Standard). Die Verbindung blieb bestehen, der Empfang lief weiter. |
| 17, AC7 | **Offen: Sichtprüfung des Dialogs.** Die Pegelmessung mit Testton ist per Test belegt (-13,5 dBFS erwartet). Einen Bildschirmzugriff auf die App gab es nicht. |
| 19, AC1, AC2 | Mit `buildx` für amd64 und arm64 gebaut. Das arm64-Image meldet `aarch64` und startet. |
| 19, AC3 | Die veröffentlichte `.exe` startet ohne .NET im `PATH` und ohne `DOTNET_ROOT`, die Debug-API antwortet. Self-contained, eine Datei, 99 MB. |
| 19, AC5 | Frischer Linux-Host (Alpine 3.24 per Docker-in-Docker), eingerichtet nur nach der README: Der Server startet, der TOFU-Fingerprint entspricht dem Log, das Admin-Token wirkt, Sprache kommt über UDP durch zwei NAT-Ebenen (99 Frames), das Backup enthält `server-data.json` und `cert.pfx`, das Update behält den Fingerprint. Dieser Durchlauf deckte zwei `.gitignore`-Fehler auf: `data/` und `[Dd]ebug/` ignorierten Quellordner. Beide sind behoben. |

---

## Package 1: Grundgerüst

**Ziel:** Die Solution mit `OVS.Shared`, `OVS.Server`, `OVS.Client` und `OVS.Tests` baut ohne Warnungen, und `dotnet test` läuft grün.

**Abhängigkeiten:** keine

**Betroffene Dateien:**
- `OpenVoiceSpeak.slnx` (neu)
- `Directory.Build.props` (neu)
- `.gitignore` (neu)
- `src/OVS.Shared/OVS.Shared.csproj` (neu)
- `src/OVS.Shared/Protocol/ProtocolInfo.cs` (neu)
- `src/OVS.Server/OVS.Server.csproj`, `src/OVS.Server/Program.cs` (neu)
- `src/OVS.Client/*` (neu, aus dem Avalonia-MVVM-Template)
- `tests/OVS.Tests/OVS.Tests.csproj`, `tests/OVS.Tests/SmokeTests.cs` (neu)

### Kontext

Im Verzeichnis gibt es noch keinen Code und kein Git-Repo, nur Tool-Artefakte. .NET SDK 10 ist installiert, die Avalonia-Templates sind es noch nicht. Die Klasse `ProtocolInfo` hält die zwei Konstanten, auf die sich alle späteren Packages beziehen: Protokollversion und Standardport.

### Acceptance Criteria

- [x] AC1: `dotnet build` im Root endet ohne Fehler und ohne Warnungen. `Directory.Build.props` setzt `Nullable=enable`, `ImplicitUsings=enable` und `TreatWarningsAsErrors=true`.
- [x] AC2: `dotnet test` führt mindestens einen Test aus, und alle sind grün.
- [x] AC3: Die Projektreferenzen sind: Server -> Shared, Client -> Shared, Tests -> Shared, Server und Client.
- [x] AC4: `bin/`, `obj/`, `data/`, `.claude-flow/`, `.swarm/` und `ruvector.db` erscheinen nicht in `git status`.
- [x] AC5: `ProtocolInfo.Version == 1` und `ProtocolInfo.DefaultPort == 7000`.

### Tests (TDD)

Reihenfolge: Test schreiben -> rot -> minimal implementieren -> grün -> refactoren.

1. `SmokeTests.cs > "ProtocolInfo_Defaults_AreStable"` deckt AC2 und AC5 ab.
   - Gegeben: nichts
   - Erwartet: `Version == 1`, `DefaultPort == 7000`
2. Manuelle Checks:
   - AC1: Ausgabe von `dotnet build` prüfen.
   - AC3: `dotnet list tests/OVS.Tests reference` ausführen.
   - AC4: `git status` nach einem Build prüfen.

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. `git init`, dann `dotnet new gitignore` und die Tool-Artefakte sowie `data/` ergänzen.
2. `dotnet new sln -n OpenVoiceSpeak` (erzeugt unter .NET 10 eine `.slnx`).
3. Projekte anlegen:
   - `dotnet new classlib -o src/OVS.Shared`
   - `dotnet new console -o src/OVS.Server`
   - `dotnet new xunit -o tests/OVS.Tests`
4. `dotnet new install Avalonia.Templates`, dann `dotnet new avalonia.mvvm -o src/OVS.Client -n OVS.Client`.
5. Alle Projekte mit `dotnet sln add` aufnehmen und die Referenzen nach AC3 setzen. `Directory.Build.props` anlegen.
6. `SmokeTests.cs` schreiben. Das ergibt rot, weil `ProtocolInfo` nicht kompiliert.
7. `ProtocolInfo` anlegen (grün).
8. Template-Warnungen beheben, bis AC1 erfüllt ist.

### Out of Scope

- Docker (Package 5), CI, Protokollinhalte (ab Package 2)

---

## Package 2: Protokoll-Framing

**Ziel:** Steuernachrichten werden über beliebige Streams als längenpräfixiertes JSON gelesen und geschrieben, und fehlerhafte oder zu grosse Frames werden abgelehnt.

**Abhängigkeiten:** Package 1

**Betroffene Dateien:**
- `src/OVS.Shared/Protocol/FrameCodec.cs` (neu): `FrameReader`, `FrameWriter`, `ProtocolException`
- `src/OVS.Shared/Protocol/Messages.cs` (neu): Basistyp `Message`, zunächst `Ping`, `Pong`, `Error`
- `tests/OVS.Tests/Protocol/FrameCodecTests.cs` (neu)
- `tests/OVS.Tests/TestSupport/TrickleStream.cs` (neu): liefert pro Read nur 1 Byte

### Kontext

Der Steuerkanal ist ein Stream, ab Package 4 ein `SslStream`.

- **Frame-Format:** 4 Byte Länge (uint32, Big Endian), danach UTF-8-JSON.
- **Obergrenze:** 64 KiB. Sie schützt vor Speicherangriffen.
- **Nachrichtentypen:** `record`s mit `[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]` und `[JsonDerivedType]` aus `System.Text.Json`. Spätere Packages ergänzen nur neue Typen.
- **Nebenläufigkeit:** `FrameWriter` serialisiert gleichzeitige Schreibzugriffe per `SemaphoreSlim`, weil der Server aus mehreren Threads an dieselbe Verbindung sendet.

### Acceptance Criteria

- [x] AC1: Eine geschriebene Nachricht wird mit gleichem Typ und gleichen Feldern wieder gelesen.
- [x] AC2: Ein Frame mit Längenangabe > 64 KiB führt zu `ProtocolException`, ohne dass ein Puffer dieser Grösse angelegt wird.
- [x] AC3: Frames, die in 1-Byte-Stücken ankommen, werden korrekt zusammengesetzt.
- [x] AC4: Endet der Stream mitten im Frame, gibt es `EndOfStreamException`. Endet er sauber zwischen zwei Frames, liefert `ReadAsync` den Wert `null`.
- [x] AC5: Unbekannter `type`, ungültiges JSON oder Länge 0 führen zu `ProtocolException`.
- [x] AC6: Das Schreiben einer Nachricht, die serialisiert grösser als 64 KiB wäre, wirft `ProtocolException`, bevor irgendetwas gesendet wird.
- [x] AC7: 100 gleichzeitige `WriteAsync`-Aufrufe erzeugen 100 vollständige, einzeln lesbare Frames.

### Tests (TDD)

1. `FrameCodecTests.cs > "WriteThenRead_Ping_RoundTrips"` (AC1)
   - Gegeben: `Ping` in einen `MemoryStream` geschrieben
   - Erwartet: gelesen wird ein `Ping`
2. `"Read_LengthAboveLimit_ThrowsWithoutAllocating"` (AC2)
   - Gegeben: Stream mit Header `0x00100001` und ohne Payload
   - Erwartet: `ProtocolException` (ohne Payload würde ein Leseversuch hängen oder `EndOfStream` werfen)
3. `"Read_TrickledBytes_AssemblesFrame"` (AC3)
   - Gegeben: `TrickleStream` mit einem gültigen `Error`-Frame
   - Erwartet: gleiche Nachricht
4. `"Read_EndInsideFrame_ThrowsEndOfStream"` und `"Read_EndBetweenFrames_ReturnsNull"` (AC4)
5. `"Read_UnknownType_Throws"`, `"Read_InvalidJson_Throws"`, `"Read_ZeroLength_Throws"` (AC5)
6. `"Write_OversizedMessage_ThrowsAndWritesNothing"` (AC6)
   - Gegeben: `Error` mit 70.000 Zeichen Text
   - Erwartet: Exception, Stream-Länge bleibt 0
7. `"Write_Concurrent100_AllFramesIntact"` (AC7)

Testbefehl: `dotnet test --filter "FullyQualifiedName~OVS.Tests.Protocol"`

### Umsetzungsschritte

1. Tests 1 bis 7 schreiben (rot).
2. `Message`-Basis und die Typen `Ping`, `Pong`, `Error { RequestId?, Code, Detail? }` anlegen.
3. `FrameWriter.WriteAsync` implementieren: serialisieren, Grösse prüfen, Header und Payload unter Semaphore schreiben.
4. `FrameReader.ReadAsync` implementieren: exakt 4 Byte lesen, Länge prüfen, exakt n Byte lesen, deserialisieren. Alle `JsonException`s werden in `ProtocolException` übersetzt.
5. Refactoring: gemeinsame `JsonSerializerOptions` als statische Instanz.

### Out of Scope

- TLS und Handshake-Nachrichten (Package 4)
- Fachliche Nachrichten (ab Package 7)

---

## Package 3: Serverkonfiguration

**Ziel:** Der Server ermittelt Port, Datenverzeichnis, Nutzerlimit und die Startwerte für Name und Passwort aus Umgebungsvariablen, optionaler Konfigurationsdatei und Standardwerten.

**Abhängigkeiten:** Package 1

**Betroffene Dateien:**
- `src/OVS.Server/ServerConfig.cs` (neu): `ServerConfig`, `ConfigException`
- `src/OVS.Server/Program.cs` (ändern): Config laden, bei Fehler Meldung auf stderr und Exit-Code 1
- `tests/OVS.Tests/Server/ServerConfigTests.cs` (neu)

### Kontext

**Vorrang:** Umgebungsvariable vor `<DataDir>/server-config.json` vor Standardwert. Das Datenverzeichnis selbst kommt nur aus `OVS_DATA_DIR` (Standard `./data`), weil die Konfigurationsdatei darin liegt.

| Variable | Standard | Gültig |
|---|---|---|
| `OVS_PORT` | 7000 | 1-65535 |
| `OVS_DATA_DIR` | `./data` | beschreibbares Verzeichnis |
| `OVS_MAX_USERS` | 50 | >= 1 |
| `OVS_SERVER_NAME` | `OpenVoiceSpeak Server` | 1-64 Zeichen (nur Startwert, siehe A10) |
| `OVS_PASSWORD` | leer | beliebig (nur Startwert) |

`ServerConfig.Load(Func<string, string?> getEnv)` bekommt die Umgebung als Funktion übergeben. So kommen die Tests ohne echte Umgebungsvariablen aus.

### Acceptance Criteria

- [x] AC1: Ohne Umgebungsvariablen und ohne Datei gelten die Standardwerte aus der Tabelle.
- [x] AC2: Werte aus `server-config.json` werden übernommen.
- [x] AC3: Umgebungsvariablen überschreiben Dateiwerte.
- [x] AC4: Ungültige Werte (Port `0`, `70000`, `abc`, MaxUsers `0`) führen zu `ConfigException`, deren Text den Variablennamen enthält. `Program` gibt die Meldung auf stderr aus und endet mit Exit-Code 1.
- [x] AC5: Ein fehlendes Datenverzeichnis wird angelegt.
- [x] AC6: Ungültiges JSON in `server-config.json` führt zu `ConfigException` mit dem Dateipfad. Es wird nicht stillschweigend auf Standardwerte zurückgefallen.

### Tests (TDD)

1. `ServerConfigTests.cs > "Load_NoEnvNoFile_UsesDefaults"` (AC1)
2. `"Load_FileValues_AreUsed"` (AC2)
   - Gegeben: temporäres Verzeichnis mit `{"port":7100,"maxUsers":10}`
   - Erwartet: Port 7100, MaxUsers 10
3. `"Load_EnvOverridesFile"` (AC3)
   - Gegeben: Datei mit Port 7100, Umgebung mit `OVS_PORT=7200`
   - Erwartet: 7200
4. `[Theory] "Load_InvalidValue_ThrowsNamingVariable"` mit den Werten aus AC4 (AC4)
5. `"Load_MissingDataDir_IsCreated"` (AC5)
6. `"Load_CorruptConfigFile_ThrowsNamingFile"` (AC6)
7. Manueller Check zu AC4: `$env:OVS_PORT="abc"; dotnet run --project src/OVS.Server; $LASTEXITCODE` ergibt `1`.

Testbefehl: `dotnet test --filter "FullyQualifiedName~ServerConfigTests"`

### Umsetzungsschritte

1. Tests schreiben (rot).
2. `ServerConfig` als `record` mit `Load` implementieren: DataDir bestimmen und anlegen, Datei optional lesen, dann Umgebung darüberlegen, dann validieren.
3. In `Program.cs` einbinden.

### Out of Scope

- Zur Laufzeit änderbare Einstellungen (Package 8)
- Docker-Umgebung (Package 5)

---

## Package 4: TLS-Verbindung und Handshake

**Ziel:** Clients bauen eine TLS-Verbindung auf und authentifizieren sich per Challenge-Signatur mit ihrem Schlüsselpaar, und der Server vergibt dafür eine Session.

**Abhängigkeiten:** Package 2, Package 3

**Betroffene Dateien:**
- `src/OVS.Shared/Identity/ClientIdentity.cs` (neu): Schlüssel erzeugen, laden, speichern, `Fingerprint`, `Sign`, statisches `Verify`
- `src/OVS.Shared/Protocol/Messages.cs` (ändern): `ClientHello`, `Challenge`, `ClientProof`, `Welcome`, `Rejected`, `Disconnected`
- `src/OVS.Server/Tls/ServerCertificate.cs` (neu): `LoadOrCreate(dataDir)`
- `src/OVS.Server/ControlServer.cs` (neu): Listener, TLS, Handshake, Session-Registry, Limits, Ping-Timeout
- `src/OVS.Server/Session.cs` (neu)
- `src/OVS.Server/Program.cs` (ändern): Server starten, Zertifikat-Fingerprint loggen
- `tests/OVS.Tests/TestSupport/TestServer.cs`, `TestClient.cs` (neu)
- `tests/OVS.Tests/Shared/ClientIdentityTests.cs`, `Server/ServerCertificateTests.cs`, `Server/HandshakeTests.cs` (neu)

### Kontext

**Identität:**
- Schlüsselpaar: ECDsa P-256 aus der Standardbibliothek.
- Fingerprint: SHA-256 über den `SubjectPublicKeyInfo` des öffentlichen Schlüssels, als Hex-String in Kleinbuchstaben. Er ist die Nutzer-ID auf allen Servern.

**Zertifikat:**
- Selbstsigniert (ECDsa P-256, 20 Jahre gültig), gespeichert als `<DataDir>/cert.pfx`.
- Unter Windows muss das Zertifikat nach dem Erzeugen einmal als PFX exportiert und mit `X509CertificateLoader.LoadPkcs12` neu geladen werden, sonst kann `SslStream` den flüchtigen Schlüssel nicht verwenden.

**Handshake:**
1. TLS-Aufbau.
2. Client sendet `ClientHello { ProtocolVersion, Nickname, PublicKey (Base64-SPKI), Password? }`.
3. Server sendet `Challenge { Nonce (32 Byte) }`.
4. Client sendet `ClientProof { Signature }` über `Nonce + SHA-256(Server-Zertifikat)`. Diese Kanalbindung verhindert, dass ein Angreifer die Challenge an einen anderen Server durchreicht.
5. Server sendet entweder `Welcome { SessionId, VoiceKey (32 Byte, Base64), ServerName }` oder `Rejected { Code, Detail? }` und schliesst die Verbindung.

**Rejected-Codes:** `VersionMismatch`, `BadSignature`, `WrongPassword`, `ServerFull`, `NicknameInvalid`, `NicknameTaken`, `TooManyConnections`, `Timeout`.

**Grenzwerte:**
- Handshake-Timeout: 10 s.
- Idle-Timeout: 15 s ohne Frame. Der Client pingt alle 5 s.
- Die Timeouts werden für Tests per Konstruktor verkürzt.
- Höchstens 5 Verbindungen pro IP (Konstante).

**Sonstiges:**
- Passwortvergleich über SHA-256 beider Seiten mit `CryptographicOperations.FixedTimeEquals`.
- `SessionId`: fortlaufender `uint`, nie 0.
- `VoiceKey`: 32 Byte aus `RandomNumberGenerator`.

### Acceptance Criteria

- [x] AC1: Ein gültiger Handshake liefert `Welcome` mit `SessionId > 0` und einem 32-Byte-Schlüssel.
- [x] AC2: Eine falsche Signatur führt zu `Rejected(BadSignature)`, danach wird die Verbindung geschlossen.
- [x] AC3: Eine Signatur mit dem Hash eines anderen Zertifikats führt zu `Rejected(BadSignature)`.
- [x] AC4: Eine abweichende `ProtocolVersion` führt zu `Rejected(VersionMismatch)`.
- [x] AC5: Ist ein Passwort gesetzt, führt ein falsches oder fehlendes Passwort zu `Rejected(WrongPassword)`. Ohne gesetztes Passwort wird jedes akzeptiert.
- [x] AC6: Ist `MaxUsers` erreicht, folgt `Rejected(ServerFull)`.
- [x] AC7: Nickname nach dem Trimmen 1 bis 32 Zeichen, keine Steuerzeichen, sonst `NicknameInvalid`. Ist der Nickname online bereits vergeben (Gross- und Kleinschreibung egal), folgt `NicknameTaken`. Ausnahme: dieselbe Identität.
- [x] AC8: Verbindet sich dieselbe Identität ein zweites Mal, bekommt die alte Session `Disconnected(ReplacedByNewConnection)`, die neue bekommt `Welcome`.
- [x] AC9: Ohne abgeschlossenen Handshake wird die Verbindung nach dem Handshake-Timeout geschlossen.
- [x] AC10: Ohne Ping innerhalb des Idle-Timeouts wird die Session entfernt.
- [x] AC11: Die 6. gleichzeitige Verbindung von derselben IP bekommt `Rejected(TooManyConnections)`.
- [x] AC12: `ServerCertificate.LoadOrCreate` liefert beim zweiten Aufruf auf demselben Verzeichnis denselben Thumbprint.
- [x] AC13: Eine gespeicherte und neu geladene `ClientIdentity` hat denselben Fingerprint. `Verify` akzeptiert eigene Signaturen und lehnt veränderte Daten ab.
- [x] AC14: Beim Start loggt der Server `Listening on <ip>:<port>` und `Zertifikat-Fingerprint: <sha256>`.

### Tests (TDD)

1. `ClientIdentityTests.cs` (AC13):
   - `"SaveLoad_KeepsFingerprint"`
   - `"Verify_OwnSignature_True"`
   - `"Verify_TamperedData_False"`
2. `ServerCertificateTests.cs > "LoadOrCreate_Twice_SameThumbprint"` (AC12)
3. `HandshakeTests.cs`, jeweils gegen `TestServer` auf Port 0:
   - `"ValidHandshake_ReceivesWelcome"` (AC1)
   - `"WrongSignature_RejectedBadSignature"` (AC2)
   - `"SignatureBoundToOtherCert_RejectedBadSignature"` (AC3)
   - `"OldProtocolVersion_RejectedVersionMismatch"` (AC4)
   - `[Theory] "Password_Cases"`: gesetzt und richtig / gesetzt und falsch / gesetzt und fehlend / nicht gesetzt (AC5)
   - `"MaxUsersReached_RejectedServerFull"` mit MaxUsers = 1 (AC6)
   - `[Theory] "InvalidNickname_Rejected"` mit `""`, `"   "`, 33 Zeichen, `"a\u0007b"` sowie `"DuplicateNickname_RejectedNicknameTaken"` (AC7)
   - `"SameIdentityTwice_OldSessionReplaced"` (AC8)
   - `"NoHandshake_ClosedAfterTimeout"` mit Timeout 200 ms (AC9)
   - `"NoPing_SessionRemovedAfterIdleTimeout"` mit Timeout 300 ms (AC10)
   - `"SixthConnectionSameIp_Rejected"` (AC11)
4. Manueller Check zu AC14: `dotnet run --project src/OVS.Server` und die Logausgabe ansehen.

Testbefehl: `dotnet test --filter "FullyQualifiedName~Handshake|FullyQualifiedName~ClientIdentity|FullyQualifiedName~ServerCertificate"`

### Umsetzungsschritte

1. `ClientIdentityTests` schreiben (rot), dann `ClientIdentity` implementieren (grün).
2. `ServerCertificateTests` schreiben (rot), dann `ServerCertificate` implementieren (grün).
3. `TestServer` und `TestClient` als Hilfen anlegen. `TestClient` kann den Handshake gezielt fehlerhaft ausführen.
4. `HandshakeTests` einzeln rot schreiben und `ControlServer` Schritt für Schritt grün machen. Reihenfolge: Welcome, Signatur, Version, Passwort, Limits, Nickname, Ersetzen, Timeouts.
5. `Program.cs`: Server starten und die Log-Zeilen aus AC14 ausgeben.

### Out of Scope

- Serverzustand im `Welcome` (Package 7)
- UDP (Package 12)
- Bans (Package 9)
- TOFU auf Client-Seite (Package 15)

---

## Package 5: Docker-Betrieb

**Ziel:** Der Server läuft als Container ohne Root-Rechte, hält alle Daten im Volume `/data` und fährt bei SIGTERM sauber herunter.

**Abhängigkeiten:** Package 4

**Betroffene Dateien:**
- `Dockerfile`, `.dockerignore`, `docker-compose.yml` (neu)
- `src/OVS.Server/Program.cs` (ändern): SIGTERM und SIGINT behandeln
- `src/OVS.Server/ControlServer.cs` (ändern): `StopAsync`
- `tests/OVS.Tests/Server/ShutdownTests.cs` (neu)

### Kontext

**Image:**
- Multi-Stage-Build. Build-Stage `mcr.microsoft.com/dotnet/sdk:10.0` mit `dotnet publish src/OVS.Server -c Release --self-contained -p:PublishSingleFile=true`.
- Runtime-Stage `mcr.microsoft.com/dotnet/runtime-deps:10.0`.
- `RUN mkdir /data && chown $APP_UID /data`, danach `USER $APP_UID`. Ein benanntes Volume übernimmt beim ersten Einbinden diese Besitzrechte.
- `ENV OVS_DATA_DIR=/data`, `VOLUME /data`, `EXPOSE 7000/tcp 7000/udp`.
- Multi-Arch folgt erst in Package 19.

**Herunterfahren:** `PosixSignalRegistration.Create(PosixSignal.SIGTERM, ...)` aus der Standardbibliothek löst `ControlServer.StopAsync` aus. Das sendet `Disconnected(ServerShutdown)` an alle, schliesst die Verbindungen und stoppt den Listener. Danach endet der Prozess mit Exit-Code 0.

**Compose:** entspricht der Besprechung. Service `ovs-server`, Ports 7000 für TCP und UDP, benanntes Volume `ovs-data`, `restart: unless-stopped`.

### Acceptance Criteria

- [x] AC1: `ControlServer.StopAsync` sendet allen verbundenen Clients `Disconnected(ServerShutdown)` und schliesst alle Verbindungen innerhalb von 2 s.
- [x] AC2: `docker compose up -d --build` startet den Container, und `docker compose logs` zeigt `Listening on 0.0.0.0:7000`.
- [x] AC3: `docker inspect --format '{{.Config.User}}' <container>` ist nicht leer und nicht `root` bzw. `0`.
- [x] AC4: Nach `docker compose down` und `docker compose up -d` loggt der Server denselben Zertifikat-Fingerprint wie vorher.
- [x] AC5: `docker compose stop` endet in unter 10 s (ohne Kill), und der Exit-Code ist 0 (`docker inspect --format '{{.State.ExitCode}}'`).
- [x] AC6: Mit `OVS_PORT=7100` in der Compose-Datei (und angepasstem Port-Mapping) lauscht der Server auf 7100.

### Tests (TDD)

1. `ShutdownTests.cs > "StopAsync_ConnectedClients_ReceiveShutdownAndAreClosed"` (AC1)
   - Gegeben: `TestServer` mit 2 verbundenen `TestClient`s
   - Erwartet: beide lesen `Disconnected(ServerShutdown)`, danach liefert `ReadAsync` `null`, Gesamtdauer unter 2 s
2. Manuelle Checks AC2 bis AC6 mit genau den Befehlen aus den ACs. Das Ergebnis wird als Notiz im PR bzw. Commit festgehalten.

Testbefehl: `dotnet test --filter "FullyQualifiedName~ShutdownTests"`, danach `docker compose up -d --build`

### Umsetzungsschritte

1. `ShutdownTests` schreiben (rot), `StopAsync` implementieren (grün).
2. Signal-Registrierung in `Program.cs` einbauen.
3. `.dockerignore` (`bin/`, `obj/`, `.git/`, `data/`, Tool-Artefakte) und `Dockerfile` schreiben.
4. `docker-compose.yml` schreiben.
5. Die manuellen Checks AC2 bis AC6 durchgehen.

### Out of Scope

- Multi-Arch-Build und README (Package 19)
- UDP-Nutzung (Package 12). Der Port ist aber bereits freigegeben.
- Veröffentlichung in einer Registry (A15)

---

## Package 6: Rechtemodell

**Ziel:** Serverweite Rechte, Gruppen und alle Regeln gegen Rechteausweitung existieren als reine, vollständig getestete Logik ohne Netzwerk und Speicherung.

**Abhängigkeiten:** Package 1

**Betroffene Dateien:**
- `src/OVS.Shared/Permissions/Permission.cs` (neu): `[Flags] enum Permission`. Liegt in Shared, weil der Client damit die UI steuert.
- `src/OVS.Server/Permissions/Group.cs` (neu)
- `src/OVS.Server/Permissions/PermissionRules.cs` (neu)
- `tests/OVS.Tests/Server/PermissionRulesTests.cs` (neu)

### Kontext

**Rechte:** `Speak`, `SpeakLinked`, `ChannelCreate`, `ChannelEdit`, `ChannelDelete`, `ChannelLink`, `UserMove`, `UserMute`, `UserKick`, `UserBan`, `GroupsManage`, `GroupsAssign`, `ServerConfig`, dazu `All` als Kombination aller Flags.

`Group(Guid Id, string Name, Permission Permissions)`

**Standardgruppen:** `Gast` und `Admin` haben feste, bekannte GUIDs.

| Gruppe | Rechte |
|---|---|
| Gast (Standard) | Speak |
| Moderator | Speak, SpeakLinked, ChannelLink, UserMove, UserMute, UserKick, UserBan |
| Admin | All (immer, auch wenn später neue Flags dazukommen) |

**Regeln in `PermissionRules`:**
- `Effective(groupIds, groups)`: bitweises OR über alle Gruppen, unbekannte IDs werden ignoriert. Gehört die Admin-Gruppe dazu, ist das Ergebnis `All`.
- `CanSaveGroup(actor, existing?, newPerms)`: `GroupsManage`, `newPerms ⊆ actor` und bei Bearbeitung `existing ⊆ actor`. Admin ist nie bearbeitbar.
- `CanDeleteGroup(actor, group)`: `GroupsManage`, `group ⊆ actor`, nicht Gast und nicht Admin.
- `CanAssign(actor, group)` für Zuweisen und Entfernen: `GroupsAssign` und `group ⊆ actor`.
- `CanActOn(actor, target)`: `target ⊆ actor` (A11).
- `WouldRemoveLastAdmin(users, fingerprint, groupId)`: wahr, wenn Admin entfernt werden soll und der Nutzer der einzige Admin ist.

### Acceptance Criteria

- [x] AC1: `Effective` vereinigt die Rechte aller Gruppen eines Nutzers. Unbekannte Gruppen-IDs ändern nichts. Ein Admin hat `All`.
- [x] AC2: Die Standardgruppen haben genau die Rechte aus der Tabelle.
- [x] AC3: Ohne `GroupsManage` ist jedes `CanSaveGroup`/`CanDeleteGroup` falsch.
- [x] AC4: Ein Nutzer kann keiner Gruppe Rechte geben, die er selbst nicht hat. Er kann auch keine Gruppe bearbeiten, die mehr kann als er.
- [x] AC5: Admin ist nicht bearbeitbar und nicht löschbar. Gast ist nicht löschbar.
- [x] AC6: Zuweisen und Entfernen geht nur mit `GroupsAssign` und nur für Gruppen, die eine Teilmenge der eigenen Rechte sind.
- [x] AC7: `CanActOn`: Moderator gegen Gast ist wahr, Moderator gegen Admin ist falsch, Admin gegen Admin ist wahr.
- [x] AC8: `WouldRemoveLastAdmin` ist beim einzigen Admin wahr, bei zwei Admins falsch, und bei anderen Gruppen immer falsch.

### Tests (TDD)

1. `PermissionRulesTests.cs > [Theory] "Effective_Cases"` (AC1)
   - Fälle: keine Gruppe, Gast, Gast + Moderator, unbekannte ID, Admin
2. `"DefaultGroups_HaveDocumentedPermissions"` (AC2)
3. `[Theory] "CanSaveGroup_Cases"` (AC3, AC4). Tabelle aus Akteur-Rechten, alten Rechten, neuen Rechten und erwartetem Ergebnis. Enthält den Eskalationsfall: Akteur hat `GroupsManage` ohne `UserBan` und versucht, einer Gruppe `UserBan` zu geben -> falsch.
4. `"AdminGroup_NotEditableNotDeletable"`, `"GuestGroup_NotDeletable"` (AC5)
5. `[Theory] "CanAssign_Cases"` (AC6)
6. `[Theory] "CanActOn_Cases"` (AC7)
7. `[Theory] "WouldRemoveLastAdmin_Cases"` (AC8)

Testbefehl: `dotnet test --filter "FullyQualifiedName~PermissionRulesTests"`

### Umsetzungsschritte

1. Enum und `Group` anlegen (nur so viel, dass die Tests kompilieren).
2. Tests schreiben (rot).
3. `PermissionRules` als statische Klasse implementieren (grün).
4. Refactoring: eine Hilfsfunktion `IsSubset(a, b) => (a & ~b) == 0`.

### Out of Scope

- Protokollbefehle (Package 8, 9)
- Speicherung der Gruppen (Package 7)

---

## Package 7: Serverzustand und Channels

**Ziel:** Clients erhalten beim Verbinden den vollständigen Serverzustand, sehen Änderungen live und verwalten Channels rechtegeprüft, wobei alles Dauerhafte atomar gespeichert wird.

**Abhängigkeiten:** Package 4, Package 6

**Betroffene Dateien:**
- `src/OVS.Server/Data/ServerData.cs` (neu): `Channels`, `DefaultChannelId`, `Groups`, `Users` (`Fingerprint`, `LastNickname`, `GroupIds`, `FirstSeen`), `Settings` (`Name`, `WelcomeText`, `PasswordHash?`)
- `src/OVS.Server/Data/DataStore.cs` (neu): `Load` und `Save` für `<DataDir>/server-data.json`
- `src/OVS.Server/ServerState.cs` (neu): Live-Zustand, globaler Lock (A17), Broadcast
- `src/OVS.Server/Commands/ChannelCommands.cs` (neu)
- `src/OVS.Server/ControlServer.cs` (ändern): Nachrichten nach dem Handshake an `ServerState` weiterreichen. Name und Passwort kommen ab jetzt aus `ServerData.Settings`.
- `src/OVS.Shared/Protocol/Messages.cs` (ändern):
  - Basistyp `Request { RequestId? }`
  - Snapshot: `ServerSnapshot`, `ChannelInfo`, `UserInfo { SessionId, Fingerprint, Nickname, ChannelId, SelfMuted, SelfDeafened, Permissions }`
  - Anfragen: `JoinChannel`, `CreateChannel`, `EditChannel`, `DeleteChannel`, `MoveUser`, `SetSelfState`
  - Deltas: `ChannelAdded`, `ChannelUpdated`, `ChannelRemoved`, `UserJoined`, `UserUpdated`, `UserLeft`
  - `Welcome` wird um `Snapshot` erweitert.
- `tests/OVS.Tests/Server/DataStoreTests.cs`, `ChannelCommandTests.cs`, `StateSyncTests.cs` (neu)
- `tests/OVS.Tests/TestSupport/TestServer.cs` (ändern): Möglichkeit, `ServerData` vorzubelegen, z. B. einen Nutzer mit Gruppen

### Kontext

**Speichern:** Jede dauerhafte Änderung wird sofort geschrieben. Zuerst `server-data.json.tmp` schreiben und flushen, dann `File.Move(..., overwrite: true)`.

**Beschädigte Datei:** Ist `server-data.json` beim Start beschädigt, bricht der Start ab, und die Datei wird **nicht** überschrieben. Das schützt vor Datenverlust.

**Neue Nutzer:** Ein neuer Fingerprint wird beim ersten `Welcome` mit der Gruppe Gast gespeichert und landet im Standard-Channel.

**Fehler:** Anfragen ohne Recht bekommen `Error { RequestId, Code = PermissionDenied }`, und zwar nur an den Anfragenden. Erfolg erkennt der Client am Delta.

### Acceptance Criteria

- [x] AC1: Fehlt die Daten-Datei, entstehen die Standardwerte: Channel "Lobby" als Standard, die Gruppen aus Package 6 und Settings aus `ServerConfig`. Die Datei wird angelegt.
- [x] AC2: Nach `Save` existiert keine `.tmp`-Datei mehr, und `Load` liefert gleiche Daten.
- [x] AC3: Eine beschädigte `server-data.json` führt beim Laden zu einer Exception, und der Dateiinhalt bleibt byte-gleich.
- [x] AC4: `Welcome` enthält den Snapshot mit allen Channels, allen Online-Nutzern und den eigenen Rechten. Ein neuer Nutzer ist in "Lobby" und als Gast gespeichert.
- [x] AC5: `JoinChannel` erzeugt `UserUpdated` bei allen Clients, auch beim Anfragenden.
- [x] AC6: `CreateChannel` braucht `ChannelCreate`. Der Name ist getrimmt 1 bis 64 Zeichen und eindeutig ohne Rücksicht auf Gross- und Kleinschreibung, sonst `Error(InvalidName)` bzw. `Error(NameTaken)`. Erfolg erzeugt `ChannelAdded` bei allen und wird gespeichert.
- [x] AC7: `EditChannel` (Name, Beschreibung bis 500 Zeichen, Sortierung) braucht `ChannelEdit`. Erfolg erzeugt `ChannelUpdated`.
- [x] AC8: `DeleteChannel` braucht `ChannelDelete`. Der Standard-Channel ergibt `Error(CannotDeleteDefault)`. Nutzer im gelöschten Channel werden in den Standard-Channel verschoben (`UserUpdated`), danach folgt `ChannelRemoved`.
- [x] AC9: `MoveUser` braucht `UserMove` und `CanActOn`. Eine unbekannte Session ergibt `Error(NotFound)`.
- [x] AC10: Fehlende Rechte ergeben `Error(PermissionDenied)` mit derselben `RequestId`, nur an den Anfragenden. Der Zustand bleibt unverändert, und niemand bekommt ein Delta.
- [x] AC11: Trennt ein Client die Verbindung, bekommen alle anderen `UserLeft`.
- [x] AC12: `SetSelfState(muted, deafened)` braucht kein Recht und erzeugt `UserUpdated` bei allen. Deafened schliesst Muted ein.
- [x] AC13: Nach einem Neustart (neuer `TestServer` auf demselben Datenverzeichnis) sind angelegte Channels vorhanden.

### Tests (TDD)

1. `DataStoreTests.cs`:
   - `"Load_MissingFile_CreatesDefaults"` (AC1)
   - `"Save_ThenLoad_RoundTrips_NoTempLeft"` (AC2)
   - `"Load_CorruptFile_ThrowsAndLeavesFileUntouched"` (AC3)
2. `StateSyncTests.cs`:
   - `"Welcome_ContainsSnapshotAndNewUserInLobbyAsGuest"` (AC4)
   - `"Disconnect_OthersReceiveUserLeft"` (AC11)
   - `"SetSelfState_BroadcastsUserUpdated"` (AC12)
   - `"Restart_KeepsCreatedChannels"` (AC13)
3. `ChannelCommandTests.cs`. Zwei Clients: A mit vorbelegter Admin-Gruppe, B als Gast.
   - `"Join_BroadcastsUserUpdated"` (AC5)
   - `"Create_AsAdmin_BroadcastsAndPersists"`, `[Theory] "Create_InvalidOrDuplicateName_Error"` (AC6)
   - `"Edit_AsAdmin_BroadcastsChannelUpdated"` (AC7)
   - `"Delete_MovesUsersToDefaultThenRemoves"`, `"Delete_DefaultChannel_Error"` (AC8)
   - `"Move_ModeratorMovesGuest_Ok"`, `"Move_ModeratorMovesAdmin_PermissionDenied"`, `"Move_UnknownSession_NotFound"` (AC9)
   - `[Theory] "GuestCommands_PermissionDenied_NoBroadcast"` für Create, Edit, Delete und Move (AC10)

Testbefehl: `dotnet test --filter "FullyQualifiedName~DataStoreTests|FullyQualifiedName~StateSyncTests|FullyQualifiedName~ChannelCommandTests"`

### Umsetzungsschritte

1. `DataStoreTests` schreiben (rot), dann `ServerData` und `DataStore` implementieren (grün).
2. `StateSyncTests` für AC4 schreiben (rot). `ServerState` und die Snapshot-Nachrichten anlegen und `Welcome` erweitern (grün).
3. Die übrigen Tests einzeln rot schreiben und `ChannelCommands` implementieren. Jeder Handler beginnt mit der Rechteprüfung.
4. Refactoring: eine gemeinsame Hilfsfunktion `Require(session, Permission, requestId)`, die bei fehlendem Recht das `Error` sendet und `false` liefert.

### Out of Scope

- Gruppenverwaltung (Package 8)
- Moderation und Server-Mute (Package 9)
- Links (Package 10)
- Channel-Passwörter und Hierarchie

---

## Package 8: Server-Administration

**Ziel:** Admins verwalten Gruppen, Gruppenzuordnungen und Servereinstellungen vollständig über das Protokoll, und der erste Admin entsteht über ein einmaliges Admin-Token.

**Abhängigkeiten:** Package 7

**Betroffene Dateien:**
- `src/OVS.Server/AdminToken.cs` (neu)
- `src/OVS.Server/Commands/AdminCommands.cs` (neu)
- `src/OVS.Server/Program.cs` (ändern): Token loggen
- `src/OVS.Shared/Protocol/Messages.cs` (ändern):
  - `GroupInfo`
  - Anfragen: `CreateGroup`, `UpdateGroup`, `DeleteGroup`, `AssignGroup`, `UnassignGroup`, `ListUsers`, `RedeemAdminToken`, `UpdateServerSettings`
  - Antworten und Deltas: `UserList`, `GroupsChanged`, `ServerSettingsChanged`
  - Der Snapshot wird um `Groups` und `Settings { Name, WelcomeText, HasPassword }` erweitert.
- `tests/OVS.Tests/Server/AdminTokenTests.cs`, `AdminCommandTests.cs` (neu)

### Kontext

**Admin-Token:**
- Solange kein Nutzer in der Admin-Gruppe ist, erzeugt jeder Serverstart ein neues Token mit 128 Bit Zufall und loggt es als `Admin-Token: <token>` (siehe Besprechung: übersteht Log-Rotation).
- Das Token ist nur im Speicher und einmal einlösbar.
- Der Vergleich erfolgt in konstanter Zeit.

**Gruppenzuordnung:** Das Zuweisen funktioniert auch für Offline-Nutzer, adressiert über den Fingerprint. Dafür liefert `ListUsers` alle bekannten Nutzer.

**Rechteprüfung:** Alle Regeln kommen aus `PermissionRules` (Package 6).

### Acceptance Criteria

- [x] AC1: Ohne Admin wird beim Start ein Token erzeugt und geloggt. Mit Admin wird kein Token erzeugt.
- [x] AC2: Ein korrektes Token macht den Nutzer zum Admin. Das wird gespeichert, alle bekommen `UserUpdated` mit den neuen Rechten, und das Token ist danach ungültig. Ein zweites Einlösen ergibt `Error(InvalidToken)`.
- [x] AC3: Ein falsches Token ergibt `Error(InvalidToken)` ohne Zustandsänderung.
- [x] AC4: `CreateGroup` und `UpdateGroup` folgen `CanSaveGroup`. Name 1 bis 32 Zeichen und eindeutig. Erfolg erzeugt `GroupsChanged` bei allen, und Online-Mitglieder bekommen `UserUpdated` mit den neu berechneten Rechten.
- [x] AC5: Die Admin-Gruppe bearbeiten ergibt `Error(ProtectedGroup)`. Admin oder Gast löschen ergibt `Error(ProtectedGroup)`.
- [x] AC6: `DeleteGroup` entfernt die Gruppe auch aus allen Nutzer-Zuordnungen.
- [x] AC7: `AssignGroup` und `UnassignGroup` folgen `CanAssign` und funktionieren auch für Offline-Nutzer. Den letzten Admin entfernen ergibt `Error(LastAdmin)`.
- [x] AC8: `ListUsers` braucht `GroupsAssign` und liefert alle bekannten Nutzer mit Fingerprint, letztem Nickname und Gruppen.
- [x] AC9: `UpdateServerSettings` braucht `ServerConfig`. Name 1 bis 64 Zeichen, Willkommenstext bis 500 Zeichen, Passwort optional (leer = keins, gespeichert als SHA-256-Hash). Alle bekommen `ServerSettingsChanged` ohne Passwort. Ein neues Passwort gilt ab dem nächsten Handshake.
- [x] AC10: Alle Änderungen überstehen einen Neustart.

### Tests (TDD)

1. `AdminTokenTests.cs`:
   - `"NoAdmin_TokenGenerated"`, `"AdminExists_NoToken"` (AC1)
   - `"Redeem_Once_Valid_Twice_Invalid"` (AC2, AC3)
2. `AdminCommandTests.cs`:
   - `"Redeem_BroadcastsNewPermissions"` (AC2)
   - `"CreateGroup_AsAdmin_BroadcastsGroupsChanged"`, `"UpdateGroup_ChangesOnlineMemberPermissions"` (AC4)
   - `"CreateGroup_ModeratorGrantsUserBanWithoutOwning_PermissionDenied"` (AC4, Eskalation)
   - `[Theory] "ProtectedGroups_Errors"` (AC5)
   - `"DeleteGroup_RemovedFromUsers"` (AC6)
   - `"Assign_OfflineUser_Persisted"`, `"Unassign_LastAdmin_Error"` (AC7)
   - `"ListUsers_WithoutGroupsAssign_PermissionDenied"`, `"ListUsers_ReturnsKnownUsers"` (AC8)
   - `"UpdateSettings_NewPasswordAppliesToNextHandshake"`, `[Theory] "UpdateSettings_InvalidValues_Error"` (AC9)
   - `"AdminChanges_SurviveRestart"` (AC10)

Testbefehl: `dotnet test --filter "FullyQualifiedName~AdminTokenTests|FullyQualifiedName~AdminCommandTests"`

### Umsetzungsschritte

1. `AdminTokenTests` schreiben (rot) und `AdminToken` implementieren (grün). In `Program.cs` einbinden.
2. Die Tests für `AdminCommandTests` einzeln rot schreiben. Handler in `AdminCommands` implementieren und dabei ausschliesslich `PermissionRules` verwenden.
3. Neuberechnung der Rechte nach Gruppenänderungen zentral in `ServerState.RecomputePermissions()` bündeln.

### Out of Scope

- UI (Package 18)
- Audit-Log

---

## Package 9: Moderation

**Ziel:** Berechtigte können Nutzer kicken, bannen und serverseitig stummschalten, und gebannte Identitäten oder IP-Adressen werden beim Handshake abgewiesen.

**Abhängigkeiten:** Package 7

**Betroffene Dateien:**
- `src/OVS.Server/Commands/ModerationCommands.cs` (neu)
- `src/OVS.Server/Data/ServerData.cs` (ändern): `Bans` (`Id`, `Fingerprint`, `Ip?`, `Reason`, `CreatedBy`, `ExpiresAt?`)
- `src/OVS.Server/ControlServer.cs` (ändern): Ban-Prüfung im Handshake
- `src/OVS.Server/ServerState.cs` (ändern): `TimeProvider` injizieren, `ServerMuted` pro Session
- `src/OVS.Shared/Protocol/Messages.cs` (ändern):
  - Anfragen: `Kick`, `Ban`, `Unban`, `ListBans`, `SetServerMute`
  - Antwort: `BanList`
  - `UserInfo.ServerMuted`
  - Neue Codes: `Rejected(Banned)`, `Disconnected(Kicked | Banned)`
- `tests/OVS.Tests/Server/ModerationTests.cs` (neu)
- `tests/OVS.Tests/TestSupport/ManualTimeProvider.cs` (neu)

### Kontext

- Die Ban-Prüfung läuft nach der Signaturprüfung, also mit bekanntem Fingerprint.
- Ein IP-Ban sperrt alle Identitäten von dieser Adresse.
- Abgelaufene Bans werden ignoriert und beim nächsten Speichern entfernt.
- Server-Mute gilt nur für die Session (A14). Das Verwerfen von Sprache folgt in Package 12.

### Acceptance Criteria

- [x] AC1: `Kick` braucht `UserKick` und `CanActOn`. Das Ziel bekommt `Disconnected(Kicked, Reason)` und wird getrennt. Die anderen bekommen `UserLeft`.
- [x] AC2: `Ban { SessionId, Reason, DurationMinutes?, IncludeIp }` braucht `UserBan` und `CanActOn`. Der Ban wird gespeichert, und das Ziel bekommt `Disconnected(Banned)`.
- [x] AC3: Ein gebannter Fingerprint bekommt beim Handshake `Rejected(Banned)` mit Grund und Ablaufzeit. Bei einem IP-Ban wird auch eine andere Identität von derselben IP abgewiesen.
- [x] AC4: Nach Ablauf (Zeit per `ManualTimeProvider` vorgestellt) ist der Ban wirkungslos.
- [x] AC5: `ListBans` und `Unban` brauchen `UserBan`. Nach `Unban` ist der Handshake wieder erfolgreich.
- [x] AC6: `SetServerMute { SessionId, Muted }` braucht `UserMute` und `CanActOn`. Alle bekommen `UserUpdated` mit `ServerMuted`.
- [x] AC7: Ein Moderator, der einen Admin kicken, bannen oder muten will, bekommt `Error(PermissionDenied)`.
- [x] AC8: Bans überstehen einen Neustart.

### Tests (TDD)

1. `ModerationTests.cs`:
   - `"Kick_TargetDisconnectedOthersUserLeft"` (AC1)
   - `"Ban_PersistsAndDisconnects"` (AC2)
   - `"BannedFingerprint_HandshakeRejected"`, `"IpBan_OtherIdentitySameIp_Rejected"` (AC3)
   - `"ExpiredBan_HandshakeSucceeds"` (AC4)
   - `"Unban_AllowsReconnect"`, `"ListBans_AsGuest_PermissionDenied"` (AC5)
   - `"ServerMute_BroadcastsUserUpdated"` (AC6)
   - `[Theory] "ModeratorVsAdmin_PermissionDenied"` für Kick, Ban und Mute (AC7)
   - `"Bans_SurviveRestart"` (AC8)

Testbefehl: `dotnet test --filter "FullyQualifiedName~ModerationTests"`

### Umsetzungsschritte

1. `ManualTimeProvider` anlegen: `TimeProvider` mit überschriebenem `GetUtcNow` und einer `Advance`-Methode.
2. Tests einzeln rot schreiben und `ModerationCommands` sowie die Ban-Prüfung umsetzen.

### Out of Scope

- Voice-Verwerfen bei Server-Mute (Package 12)
- IP-Bereiche
- UI (Package 18)

---

## Package 10: Channel-Linking (Server)

**Ziel:** Channels lassen sich rechtegeprüft und dauerhaft paarweise verlinken, und jeder Client kennt stets die aktuellen Links.

**Abhängigkeiten:** Package 7

**Betroffene Dateien:**
- `src/OVS.Server/Data/ServerData.cs` (ändern): `Links` als `List<ChannelLink(Guid A, Guid B)>`, normalisiert mit `A < B`
- `src/OVS.Server/Commands/LinkCommands.cs` (neu)
- `src/OVS.Server/ServerState.cs` (ändern): `LinkedChannels(channelId)`
- `src/OVS.Server/Commands/ChannelCommands.cs` (ändern): Beim Löschen eines Channels seine Links aufräumen
- `src/OVS.Shared/Protocol/Messages.cs` (ändern): `LinkChannels`, `UnlinkChannels`, `ChannelsLinked`, `ChannelsUnlinked`, `ServerSnapshot.Links`
- `tests/OVS.Tests/Server/LinkCommandTests.cs` (neu)

### Kontext

- Links sind ungerichtete Paare. Nur direkte Nachbarn zählen (A5).
- `LinkedChannels` ist die einzige Stelle, die das Voice-Routing in Package 12 abfragt.

### Acceptance Criteria

- [x] AC1: `LinkChannels { A, B }` braucht `ChannelLink`. Alle bekommen `ChannelsLinked`, und der Link wird gespeichert.
- [x] AC2: `A == B` ergibt `Error(InvalidLink)`. Ein unbekannter Channel ergibt `Error(NotFound)`.
- [x] AC3: Existiert der Link bereits (egal in welcher Reihenfolge), passiert nichts: kein Delta, kein Fehler.
- [x] AC4: `UnlinkChannels` erzeugt `ChannelsUnlinked`. Ein nicht existierender Link führt zu keiner Aktion.
- [x] AC5: Wird ein Channel gelöscht, gehen `ChannelsUnlinked` für jeden seiner Links vor `ChannelRemoved` raus.
- [x] AC6: Mit den Links A-B und B-C ergibt `LinkedChannels(A)` die Menge `{B}`, `LinkedChannels(B)` die Menge `{A, C}`, und ein Channel ohne Links ergibt `{}`.
- [x] AC7: Der Snapshot enthält alle Links, und Links überstehen einen Neustart.
- [x] AC8: Ohne `ChannelLink` ergibt Link oder Unlink `Error(PermissionDenied)`.

### Tests (TDD)

1. `LinkCommandTests.cs`:
   - `"Link_AsModerator_BroadcastsAndPersists"` (AC1)
   - `"Link_SameChannel_InvalidLink"`, `"Link_UnknownChannel_NotFound"` (AC2)
   - `"Link_ExistingReversed_NoOp"` (AC3)
   - `"Unlink_Broadcasts"`, `"Unlink_Missing_NoOp"` (AC4)
   - `"DeleteChannel_UnlinksBeforeRemove"`: die Reihenfolge der empfangenen Nachrichten wird geprüft (AC5)
   - `"LinkedChannels_OnlyDirectNeighbours"` (AC6)
   - `"Snapshot_ContainsLinks_AfterRestart"` (AC7)
   - `"Link_AsGuest_PermissionDenied"` (AC8)

Testbefehl: `dotnet test --filter "FullyQualifiedName~LinkCommandTests"`

### Umsetzungsschritte

1. Tests einzeln rot schreiben.
2. Datenmodell, `LinkCommands`, `LinkedChannels` und das Aufräumen beim Löschen implementieren.

### Out of Scope

- Voice-Routing (Package 12)
- UI (Package 16, 18)
- Transitive Links (A5)

---

## Package 11: Voice-Paketformat und Verschlüsselung

**Ziel:** Sprachpakete haben ein festes Binärformat, sind per AES-GCM verschlüsselt und authentifiziert, und wiederholte Pakete werden erkannt.

**Abhängigkeiten:** Package 1

**Betroffene Dateien:**
- `src/OVS.Shared/Voice/VoicePacket.cs` (neu): Header lesen und schreiben, Grössengrenzen
- `src/OVS.Shared/Voice/VoiceCrypto.cs` (neu): `Seal` und `TryOpen` mit `AesGcm`, `SeqCounter`
- `src/OVS.Shared/Voice/ReplayWindow.cs` (neu)
- `tests/OVS.Tests/Voice/VoicePacketTests.cs`, `ReplayWindowTests.cs` (neu)

### Kontext

**Paketaufbau:**

```
Byte 0      Typ: 0 = Hello, 1 = Voice, 2 = Ping
Byte 1-4    SessionId (uint32 BE)
              Client an Server: Absender
              Server an Client: Sprecher (bei Hello und Ping: Empfänger)
Byte 5-8    Seq (uint32 BE): Zähler der jeweiligen Richtung, dient als Nonce
Byte 9      Target: 0 = eigener Channel, 1 = eigener und gelinkte Channels
Byte 10..   Ciphertext
letzte 16   GCM-Tag
```

**Verschlüsselung:**
- Die Header-Bytes 0 bis 9 sind die AAD. Das Target kann also nicht gefälscht werden.
- Nonce (12 Byte): `[Richtung (0 = Client an Server, 1 = Server an Client)][7 Nullbytes][Seq]`.
- Der Schlüssel ist der `VoiceKey` der Session. In jeder Richtung ist Seq ein eigener Zähler, dadurch wird kein Nonce doppelt verwendet.

**Klartext:**
- Client an Server: nur die Opus-Daten.
- Server an Client: `[SpeakerSeq uint32 BE][Opus]`. Die Sequenz des Sprechers braucht der Jitter-Buffer.
- Hello und Ping haben einen leeren Klartext, es bleibt nur der Tag.

**Grenzen:**
- Maximal 1400 Byte pro Paket, Opus maximal 1275 Byte.
- `SeqCounter` wirft beim Überlauf. Das passiert bei 50 Paketen pro Sekunde erst nach über 2 Jahren. Der Aufrufer baut dann eine neue Verbindung auf.

### Acceptance Criteria

- [x] AC1: Pakete in beiden Richtungen überstehen `Seal` und `TryOpen` unverändert, inklusive `SpeakerSeq` bei Server an Client.
- [x] AC2: Jedes einzelne geänderte Byte, egal ob im Header, im Ciphertext oder im Tag, lässt `TryOpen` `false` liefern. Es wird keine Exception geworfen.
- [x] AC3: Mit falschem Schlüssel liefert `TryOpen` `false`.
- [x] AC4: Pakete kürzer als 26 Byte (Header + Tag) oder länger als 1400 Byte werden abgelehnt, ohne dass Krypto ausgeführt wird.
- [x] AC5: Ein Paket von Client an Server lässt sich nicht als Paket von Server an Client öffnen (andere Richtung im Nonce).
- [x] AC6: `ReplayWindow` (64 Pakete):
  - neue Seq wird akzeptiert
  - Duplikat wird abgelehnt
  - bis zu 63 zurück wird einmalig akzeptiert
  - älter als 64 wird abgelehnt
  - ein grosser Sprung nach vorn verschiebt das Fenster
- [x] AC7: `SeqCounter` wirft nach `uint.MaxValue` eine `OverflowException`.

### Tests (TDD)

1. `VoicePacketTests.cs`:
   - `"SealOpen_ClientToServer_RoundTrips"`, `"SealOpen_ServerToClient_RoundTripsSpeakerSeq"` (AC1)
   - `"TryOpen_AnyByteFlipped_False"`: Schleife über alle Byte-Positionen (AC2)
   - `"TryOpen_WrongKey_False"` (AC3)
   - `[Theory] "TryOpen_BadLength_False"` mit 0, 25 und 1401 Byte (AC4)
   - `"TryOpen_WrongDirection_False"` (AC5)
   - `"SeqCounter_Overflow_Throws"` (AC7)
2. `ReplayWindowTests.cs`, ein Test je Punkt aus AC6 (AC6)

Testbefehl: `dotnet test --filter "FullyQualifiedName~OVS.Tests.Voice"`

### Umsetzungsschritte

1. Tests schreiben (rot).
2. `VoicePacket` (Header), `VoiceCrypto` und `ReplayWindow` implementieren (grün). `ReplayWindow` als Bitmaske über ein `ulong`.

### Out of Scope

- UDP-Sockets (Package 12, 15)
- Opus (Package 13)

---

## Package 12: Voice-Relay

**Ziel:** Der Server nimmt Sprachpakete per UDP entgegen und leitet sie an genau die Nutzer weiter, die sie laut Channel, Links und Rechten hören dürfen.

**Abhängigkeiten:** Package 4, 7, 9, 10, 11

**Betroffene Dateien:**
- `src/OVS.Server/Voice/VoiceRouting.cs` (neu): reine Funktion `Recipients(state, senderSessionId, target)`
- `src/OVS.Server/Voice/UdpVoiceServer.cs` (neu)
- `src/OVS.Server/Voice/RateLimiter.cs` (neu)
- `src/OVS.Server/Session.cs` (ändern): `UdpEndpoint`, `ReplayWindow`, `SeqCounter` für die Richtung Server an Client
- `src/OVS.Server/Program.cs` (ändern): UDP auf demselben Port starten
- `tests/OVS.Tests/Server/VoiceRoutingTests.cs`, `RateLimiterTests.cs`, `VoiceRelayTests.cs` (neu)

### Kontext

**Routing-Regeln:**
1. Hat der Sender kein `Speak`, ist er `ServerMuted` oder `SelfMuted`, gibt es keine Empfänger.
2. Die Channels sind `{Channel des Senders}`. Bei `target == 1` und dem Recht `SpeakLinked` kommen `LinkedChannels(Channel des Senders)` dazu. Sonst bleibt es beim eigenen Channel (A7).
3. Empfänger sind alle Sessions in diesen Channels, ausser dem Sender, ausser `SelfDeafened` und nur solche mit gebundenem UDP-Endpunkt.

**Relay-Ablauf:**
1. Header parsen und die Session über die `SessionId` finden. Unbekannt: stilles Verwerfen.
2. `TryOpen` mit dem Schlüssel der Session. Fehlschlag: stilles Verwerfen.
3. Replay-Prüfung, dann Rate-Limit (Token-Bucket: 60 Pakete/s, Burst 10).
4. Hello oder Ping: Endpunkt auf die Absenderadresse setzen. Bei Ping mit einem Ping in Richtung Client antworten.
5. Voice: für jeden Empfänger mit dessen Schlüssel und Zähler neu versiegeln. Sprecher-ID und Target werden übernommen, `SpeakerSeq` ist die Seq des Senders.

**Sicherheit:** Auf ungültige Pakete antwortet der Server nie. So kann er nicht als Verstärker für Angriffe missbraucht werden.

### Acceptance Criteria

- [x] AC1: Target 0 erreicht nur die anderen im eigenen Channel, auch wenn dieser gelinkt ist.
- [x] AC2: Target 1 mit `SpeakLinked` erreicht den eigenen Channel und alle direkt gelinkten Channels, aber keine transitiven.
- [x] AC3: Target 1 ohne `SpeakLinked` verhält sich wie Target 0.
- [x] AC4: Ohne `Speak`, mit `ServerMuted` oder mit `SelfMuted` erreicht das Paket niemanden.
- [x] AC5: Deafened-Empfänger und der Sender selbst sind nie Empfänger.
- [x] AC6: Ende-zu-Ende über UDP-Loopback: Pakete von A erreichen B im selben Channel mit Sprecher-ID A, dem gesendeten Target und korrektem `SpeakerSeq`. C in einem anderen, nicht gelinkten Channel empfängt innerhalb von 500 ms nichts.
- [x] AC7: Ungültiger Tag, unbekannte Session oder Replay werden verworfen, und der Absender bekommt keine Antwort.
- [x] AC8: Mehr als 60 Pakete/s (nach Burst) werden verworfen.
- [x] AC9: Der Endpunkt wird erst nach einem gültigen Hello gesetzt und wechselt bei einem gültigen Paket von einer neuen Adresse (NAT-Rebinding).

### Tests (TDD)

1. `VoiceRoutingTests.cs`, rein und ohne Netzwerk. Aufbau: Channels L, A, B, C mit den Links A-B und B-C.
   - `"Target0_LinkedChannel_OnlyOwnChannel"` (AC1)
   - `"Target1_WithSpeakLinked_OwnPlusDirectLinks_NotTransitive"`: Sender in A erreicht A und B, nicht C (AC2)
   - `"Target1_WithoutSpeakLinked_OwnChannelOnly"` (AC3)
   - `[Theory] "SenderCannotSpeak_NoRecipients"` für fehlendes Speak, ServerMuted und SelfMuted (AC4)
   - `"DeafenedAndSender_Excluded"`, `"UnboundEndpoint_Excluded"` (AC5)
2. `RateLimiterTests.cs > "Over60PerSecond_Dropped"` mit `ManualTimeProvider` (AC8)
3. `VoiceRelayTests.cs`, gegen `TestServer` mit echten UDP-Sockets:
   - `"EndToEnd_SameChannel_Received_OtherChannel_Not"` (AC6)
   - `[Theory] "InvalidPackets_DroppedNoResponse"` für manipulierten Tag, unbekannte Session und Replay (AC7)
   - `"Endpoint_BoundOnlyAfterHello_UpdatesOnRebind"` (AC9)

Testbefehl: `dotnet test --filter "FullyQualifiedName~VoiceRouting|FullyQualifiedName~RateLimiter|FullyQualifiedName~VoiceRelay"`

### Umsetzungsschritte

1. `VoiceRoutingTests` schreiben (rot) und `VoiceRouting` implementieren (grün).
2. `RateLimiterTests` schreiben (rot) und `RateLimiter` implementieren (grün).
3. `VoiceRelayTests` einzeln rot schreiben und `UdpVoiceServer` implementieren.
4. In `Program.cs` und `TestServer` einbinden.

### Out of Scope

- Sprache über TCP als Fallback
- Jitter-Behandlung (Client, Package 13)

---

## Package 13: Client-Empfangspfad Audio

**Ziel:** Der Client wandelt Opus-Pakete mehrerer Sprecher in einen gemischten, lückenlosen PCM-Strom aus 20-ms-Frames um.

**Abhängigkeiten:** Package 1

**Betroffene Dateien:**
- `src/OVS.Client/OVS.Client.csproj` (ändern): NuGet `Concentus`
- `src/OVS.Client/Audio/AudioFormat.cs` (neu): 48 kHz, mono, 960 Samples pro Frame
- `src/OVS.Client/Audio/OpusCodec.cs` (neu): Encoder (VOIP, 32 kbps) und Decoder inklusive PLC
- `src/OVS.Client/Audio/JitterBuffer.cs` (neu)
- `src/OVS.Client/Audio/Mixer.cs` (neu)
- `tests/OVS.Tests/Client/OpusCodecTests.cs`, `JitterBufferTests.cs`, `MixerTests.cs` (neu)

### Kontext

**JitterBuffer (pro Sprecher):**
- `Push(speakerSeq, opus, viaLink)` nimmt Pakete an. `Pull()` wird alle 20 ms aufgerufen.
- Die Wiedergabe startet erst, wenn 2 Frames (40 ms) gepuffert sind. (Ursprünglich 3 Frames, am 27.09.2026 für geringere Latenz auf 2 gesenkt.)
- Fehlt ein Frame, erzeugt der Opus-Decoder mit PLC einen Ersatz (Decode mit `null`).
- Nach 25 fehlenden Frames in Folge (500 ms) gilt der Stream als beendet, und der Puffer wird zurückgesetzt.
- Verspätete Pakete (Seq kleiner als die nächste erwartete) werden verworfen.
- Mehr als 10 gepufferte Frames: die ältesten werden verworfen. Das begrenzt die Latenz.

**Mixer:**
- Hält einen JitterBuffer und einen Decoder pro Sprecher.
- `Tick()` liefert einen Frame mit der Summe aller Sprecher, auf [-1, 1] begrenzt, und die Liste der aktiven Sprecher `ActiveSpeaker { SessionId, ViaLink }` für die Sprechanzeige.
- Alle Klassen sind ohne NAudio testbar.

### Acceptance Criteria

- [x] AC1: Ein kodierter und wieder dekodierter 440-Hz-Sinus ergibt 960 Samples, deren RMS mindestens 50 % des Eingangs erreicht.
- [x] AC2: Pakete, die innerhalb der Puffertiefe vertauscht ankommen (1, 3, 2), werden in der Reihenfolge 1, 2, 3 ausgegeben.
- [x] AC3: Solange weniger als 2 Frames gepuffert sind, liefert `Pull()` nichts.
- [x] AC4: Eine Lücke (1, 2, 4) ergibt an Position 3 einen PLC-Frame mit 960 Samples statt eines Sprungs.
- [x] AC5: Ein verspätetes Paket wird verworfen und nicht abgespielt.
- [x] AC6: 500 ms ohne Pakete machen den Sprecher inaktiv, und er verschwindet aus den aktiven Sprechern.
- [x] AC7: Der Puffer hält nie mehr als 10 Frames.
- [x] AC8: Mixer: 0.4 + 0.4 ergibt 0.8. 0.8 + 0.8 wird auf 1.0 begrenzt. Ohne Sprecher gibt es Stille (960 Nullen).
- [x] AC9: `ActiveSpeaker.ViaLink` entspricht dem Target des zuletzt empfangenen Pakets.

### Tests (TDD)

1. `OpusCodecTests.cs > "EncodeDecode_Sine_PreservesEnergy"` (AC1)
2. `JitterBufferTests.cs`:
   - `"OutOfOrder_WithinDelay_PlayedInOrder"` (AC2)
   - `"BelowStartDelay_PullReturnsNull"` (AC3)
   - `"Gap_ProducesPlcFrame"` (AC4)
   - `"LatePacket_Dropped"` (AC5)
   - `"Silence500ms_StreamEnds"` (AC6)
   - `"Overflow_CappedAt10"` (AC7)
3. `MixerTests.cs`:
   - `[Theory] "Mix_SumsAndClamps"` (AC8)
   - `"ActiveSpeakers_ReportViaLink"` (AC9)

Testbefehl: `dotnet test --filter "FullyQualifiedName~OpusCodecTests|FullyQualifiedName~JitterBufferTests|FullyQualifiedName~MixerTests"`

### Umsetzungsschritte

1. Concentus einbinden und `OpusCodecTests` schreiben (rot), dann `OpusCodec` implementieren (grün).
2. `JitterBufferTests` einzeln rot schreiben und `JitterBuffer` implementieren.
3. `MixerTests` schreiben (rot) und `Mixer` implementieren (grün).

### Out of Scope

- Ausgabe über NAudio (Package 16)
- Netzwerk (Package 15)
- Lautstärke pro Nutzer

---

## Package 14: Client-Sendepfad und Sprechmodi

**Ziel:** Mikrofonaudio wird in 20-ms-Opus-Frames kodiert und nur dann mit dem richtigen Ziel ausgegeben, wenn PTT, Link-PTT oder die Sprachaktivierung es verlangen.

**Abhängigkeiten:** Package 11, Package 13

**Betroffene Dateien:**
- `src/OVS.Client/OVS.Client.csproj` (ändern): NuGet `NAudio`
- `src/OVS.Client/Audio/TransmitController.cs` (neu): reine Entscheidungslogik
- `src/OVS.Client/Audio/VoiceActivityDetector.cs` (neu)
- `src/OVS.Client/Audio/FrameChunker.cs` (neu): beliebige Puffergrössen in Blöcke zu 960 Samples zerlegen
- `src/OVS.Client/Audio/CapturePipeline.cs` (neu): WASAPI-Aufnahme, Resampling auf 48 kHz mono, Chunker, Controller, Encoder, Callback `(opus, target)`
- `src/OVS.Client/Input/KeyPoller.cs` (neu): `GetAsyncKeyState` per P/Invoke, 10-ms-Takt, Ereignisse `Pressed`/`Released`
- `tests/OVS.Tests/Client/TransmitControllerTests.cs`, `VoiceActivityDetectorTests.cs`, `FrameChunkerTests.cs` (neu)

### Kontext

`TransmitController.Decide(mode, pttDown, linkPttDown, vadActive, selfMuted, hasSpeakLinked)` liefert das Ziel oder `null`. Die Regeln in Prioritätsreihenfolge:

1. `selfMuted`: `null`
2. `linkPttDown` und `hasSpeakLinked`: Target 1
3. `linkPttDown` ohne das Recht: Target 0. Die UI zeigt dazu einen Hinweis (Package 16).
4. `pttDown`: Target 0
5. `mode == VoiceActivation` und `vadActive`: Target 0. Sprachaktivierung sendet nie an Links (A7).
6. sonst: `null`

**VAD:** RMS in dBFS. Aktiv ab der Schwelle (Standard -40 dBFS), danach 15 Frames (300 ms) Nachlaufzeit.

**Standardtasten:** X1 (`VK_XBUTTON1`) für PTT, X2 (`VK_XBUTTON2`) für Link-PTT (A13).

### Acceptance Criteria

- [x] AC1 bis AC6: Je eine Regel aus der Tabelle im Kontext liefert das angegebene Ergebnis, einschliesslich der Priorität. Zum Beispiel ergibt PTT und Link-PTT gleichzeitig mit Recht Target 1, und Link-PTT im VAD-Modus ergibt Target 1.
- [x] AC7: VAD: Stille ist inaktiv, ein lauter Frame macht sie aktiv, sie bleibt 15 Frames lang aktiv und ist danach inaktiv.
- [x] AC8: Der `FrameChunker` gibt bei Eingangsblöcken mit 441, 1000 und 3 Samples nur vollständige 960er-Frames aus. Kein Sample geht verloren oder wird doppelt ausgegeben.
- [x] AC9 (manuell): `KeyPoller` meldet `Pressed` und `Released` für X1, auch wenn ein anderes Fenster den Fokus hat. Das wird per Debug-Log geprüft.
- [x] AC10 (manuell): `CapturePipeline` mit dem Standardmikrofon erzeugt etwa 50 Frames pro Sekunde, solange PTT gedrückt ist, und keine ohne PTT. Das wird per Log-Zähler geprüft.

### Tests (TDD)

1. `TransmitControllerTests.cs > [Theory] "Decide_Cases"` mit einer Zeile pro Regel sowie den Kombinationen PTT+Link und VAD+Link (AC1 bis AC6)
2. `VoiceActivityDetectorTests.cs`:
   - `"Silence_Inactive"`
   - `"Loud_Active"`
   - `"Hangover15Frames_ThenInactive"` (AC7)
3. `FrameChunkerTests.cs > "IrregularInput_ExactFrames_NoLoss"` (AC8)
4. Manuelle Checks für AC9 und AC10 mit einem Debug-Schalter `--audio-debug` in `Program.cs` des Clients.

Testbefehl: `dotnet test --filter "FullyQualifiedName~TransmitController|FullyQualifiedName~VoiceActivityDetector|FullyQualifiedName~FrameChunker"`

### Umsetzungsschritte

1. Die drei Test-Dateien schreiben (rot).
2. `TransmitController`, `VoiceActivityDetector` und `FrameChunker` implementieren (grün).
3. `KeyPoller` und `CapturePipeline` als dünne Schicht über Windows und NAudio bauen. Resampling mit `WdlResamplingSampleProvider`.
4. Die manuellen Checks durchgehen.

### Out of Scope

- Geräteauswahl und Tastenbelegung in der UI (Package 17)
- Versand ins Netz (Package 15, 16)

---

## Package 15: Client-Netzwerkschicht

**Ziel:** Der Client verbindet sich über TOFU-geprüftes TLS, hält einen Spiegel des Serverzustands aktuell und tauscht verschlüsselte Sprachpakete per UDP aus.

**Abhängigkeiten:** Package 4, 7, 10, 12

**Betroffene Dateien:**
- `src/OVS.Client/Net/IdentityStore.cs` (neu): Identität unter `%APPDATA%\OpenVoiceSpeak\identity.key` laden oder anlegen. Das Verzeichnis ist für Tests überschreibbar.
- `src/OVS.Client/Net/KnownServers.cs` (neu): `known_servers.json` mit einem Eintrag `host:port` -> Fingerprint
- `src/OVS.Client/Net/ClientConnection.cs` (neu): TLS mit TOFU, Handshake, Ping alle 5 s, Senden, Empfangsschleife
- `src/OVS.Client/Net/StateMirror.cs` (neu): Snapshot und Deltas anwenden, Ereignis `Changed`
- `src/OVS.Client/Net/VoiceClient.cs` (neu): UDP, Hello, Ping alle 5 s (hält das NAT offen), `Send(opus, target)`, Empfang mit Callback `(speakerId, speakerSeq, target, opus)`
- `tests/OVS.Tests/Client/KnownServersTests.cs`, `StateMirrorTests.cs`, `ClientConnectionTests.cs`, `VoiceClientTests.cs` (neu)

### Kontext

**TOFU:** `KnownServers.Check(host, port, fingerprint)` liefert `Known`, `Unknown` oder `Mismatch`. `ClientConnection.ConnectAsync` erhält eine Rückfrage-Funktion `Func<TofuPrompt, Task<bool>>`, die UI dazu kommt in Package 16.
- `Unknown`: nachfragen, bei Zustimmung speichern.
- `Mismatch`: nachfragen mit deutlicher Warnung. Die Vorgabe ist Ablehnen.

**Rest:** Kein automatischer Neuaufbau der Verbindung.

### Acceptance Criteria

- [x] AC1: Beim ersten Verbinden wird wegen `Unknown` nachgefragt. Nach Zustimmung ist der Server gespeichert, und beim zweiten Verbinden wird nicht mehr gefragt.
- [x] AC2: Bei geändertem Zertifikat wird mit `Mismatch` nachgefragt. Bei Ablehnung wird die Verbindung abgebrochen, bevor ein `ClientHello` gesendet wird.
- [x] AC3: Die Identität wird einmal erzeugt und danach wiederverwendet. Zwei Ladevorgänge ergeben denselben Fingerprint.
- [x] AC4: `StateMirror` bildet Snapshot und jedes Delta aus den Packages 7 bis 10 korrekt ab.
- [x] AC5: Ende-zu-Ende: Nach dem Verbinden zeigt der Spiegel den eigenen Nutzer in "Lobby". Verbindet sich ein zweiter Client, erscheint er im Spiegel des ersten.
- [x] AC6: Ende-zu-Ende Voice: `Send` von A kommt bei B mit identischen Opus-Bytes, Sprecher-ID A und dem gesendeten Target an.
- [x] AC7: `Disconnected(reason)` vom Server löst das Ereignis `Disconnected` mit Grund aus, und der Verbindungszustand ist danach `Disconnected`.

### Tests (TDD)

1. `KnownServersTests.cs > [Theory] "Check_Cases"` für Known, Unknown und Mismatch (Grundlage für AC1 und AC2)
2. `ClientConnectionTests.cs`, gegen `TestServer`:
   - `"FirstConnect_AsksAndStores_SecondConnectSilent"` (AC1)
   - `"ChangedCert_Mismatch_Refused_NoHelloSent"`: zweiter `TestServer` mit neuem Zertifikat unter derselben Adresse (AC2)
   - `"Connect_MirrorShowsSelfInLobby_AndSecondClient"` (AC5)
   - `"ServerKick_RaisesDisconnectedWithReason"` (AC7)
3. `IdentityStore`: `"LoadOrCreate_Twice_SameFingerprint"` in `ClientConnectionTests.cs` (AC3)
4. `StateMirrorTests.cs > [Theory] "Apply_EachDelta"` (AC4)
5. `VoiceClientTests.cs > "SendFromA_ReceivedByB_Identical"` (AC6)

Testbefehl: `dotnet test --filter "FullyQualifiedName~OVS.Tests.Client"`

### Umsetzungsschritte

1. `KnownServersTests` und `StateMirrorTests` schreiben (rot), dann `KnownServers`, `StateMirror` und `IdentityStore` implementieren (grün).
2. `ClientConnectionTests` einzeln rot schreiben und `ClientConnection` implementieren.
3. `VoiceClientTests` schreiben (rot) und `VoiceClient` implementieren (grün).

### Out of Scope

- UI (Package 16)
- Automatischer Neuaufbau der Verbindung
- DPAPI (A16)

---

## Package 16: Client-Hauptfenster

**Ziel:** Nutzer verbinden sich über die Oberfläche mit einem Server, sehen Channels, Links und Sprecher und sprechen mit anderen Nutzern.

**Abhängigkeiten:** Package 13, 14, 15

**Betroffene Dateien:**
- `src/OVS.Client/Views/MainWindow.axaml`, `ConnectDialog.axaml`, `TofuDialog.axaml` (neu bzw. ändern)
- `src/OVS.Client/ViewModels/MainViewModel.cs`, `ChannelViewModel.cs`, `UserViewModel.cs`, `ConnectViewModel.cs` (neu)
- `src/OVS.Client/Settings/ClientSettings.cs` (neu): `%APPDATA%\OpenVoiceSpeak\settings.json`, zunächst nur Lesezeichen
- `src/OVS.Client/Audio/AudioEngine.cs` (neu): `WasapiOut` mit `BufferedWaveProvider`, 20-ms-Takt aus `Mixer.Tick()`, `CapturePipeline` wird an `VoiceClient.Send` angeschlossen
- `tests/OVS.Tests/Client/MainViewModelTests.cs`, `ClientSettingsTests.cs` (neu)

### Kontext

**Oberfläche:**
- Verbindungsdialog mit Adresse, Port (Standard 7000), Nickname und Passwort, dazu Lesezeichen.
- Channel-Liste mit den Nutzern unter ihrem Channel. Gelinkte Channels bekommen ein Symbol, der Tooltip nennt die verlinkten Channels.
- Sprechanzeige: grün für normal, andere Farbe für eine Link-Übertragung.
- Doppelklick betritt einen Channel.
- Buttons für Mute und Deafen, Statusleiste mit Verbindung und Ping.
- Nach dem Verbinden wird der Willkommenstext angezeigt, bei einer Trennung der Grund.
- Link-PTT ohne `SpeakLinked` zeigt einen Hinweis.

**Testbarkeit:** Das ViewModel bekommt eine Sende-Funktion `Func<Request, Task>` statt eines Interfaces. So reicht in den Tests ein Lambda.

### Acceptance Criteria

- [x] AC1: `MainViewModel` sortiert die Channels nach `Order`, dann nach Name. Die Nutzer stehen unter ihrem Channel.
- [x] AC2: `UserUpdated` mit einem neuen Channel verschiebt den Nutzer im ViewModel.
- [x] AC3: Ein aktiver Sprecher hat `IsSpeaking = true`. 300 ms nach dem letzten Frame ist er wieder `false` (geprüft mit `ManualTimeProvider`).
- [x] AC4: Ein Sprecher mit `ViaLink` hat `IsSpeakingViaLink = true` und wird anders eingefärbt.
- [x] AC5: Gelinkte Channels haben `IsLinked = true`, und `LinkedNames` enthält die Namen der Partner.
- [x] AC6: Doppelklick bzw. `JoinCommand` sendet `JoinChannel` mit der richtigen ID.
- [x] AC7: Deafen sendet `SetSelfState(muted: true, deafened: true)`, und die Wiedergabe ist stumm.
- [x] AC8: Lesezeichen werden in `settings.json` gespeichert und beim Start geladen.
- [ ] AC9 (manuell, Ende-zu-Ende): Server in Docker, zwei Client-Instanzen (zwei PCs oder ein PC mit zwei Audiogeräten). **Offen, siehe Umsetzungsstand.**
  - PTT: der andere im selben Channel hört mich.
  - Normales PTT: im gelinkten Channel hört mich niemand.
  - Link-PTT: im gelinkten Channel werde ich gehört.
  - Die Latenz liegt spürbar unter 150 ms.

### Tests (TDD)

1. `MainViewModelTests.cs`:
   - `"Build_SortsChannelsAndGroupsUsers"` (AC1)
   - `"UserUpdated_MovesUser"` (AC2)
   - `"Speaking_ClearsAfter300ms"` (AC3)
   - `"SpeakingViaLink_Flagged"` (AC4)
   - `"LinkedChannels_ShowPartners"` (AC5)
   - `"JoinCommand_SendsJoinChannel"` (AC6)
   - `"Deafen_SendsSelfStateAndMutesOutput"` (AC7)
2. `ClientSettingsTests.cs > "Bookmarks_RoundTrip"` (AC8)
3. Manueller Check für AC9 nach dem Szenario aus dem AC.

Testbefehl: `dotnet test --filter "FullyQualifiedName~MainViewModelTests|FullyQualifiedName~ClientSettingsTests"`, danach `dotnet run --project src/OVS.Client`

### Umsetzungsschritte

1. ViewModel-Tests einzeln rot schreiben und die ViewModels implementieren.
2. `ClientSettings` testgetrieben umsetzen.
3. Die Views in XAML an die ViewModels binden.
4. `AudioEngine` verdrahten: `VoiceClient`-Empfang in den `Mixer`, von dort an `WasapiOut`. `CapturePipeline` an `VoiceClient.Send`. `KeyPoller` an den `TransmitController`.
5. Den manuellen Ende-zu-Ende-Check durchführen.

### Out of Scope

- Einstellungsdialog (Package 17)
- Verwaltung und Moderation (Package 18)
- Tray-Icon, automatischer Neuaufbau der Verbindung

---

## Package 17: Client-Einstellungen

**Ziel:** Nutzer stellen Audiogeräte, Lautstärken, Sprechmodus, PTT- und Link-PTT-Taste sowie die VAD-Schwelle ein, und die Einstellungen bleiben über Neustarts erhalten.

**Abhängigkeiten:** Package 16

**Betroffene Dateien:**
- `src/OVS.Client/Views/SettingsDialog.axaml` (neu)
- `src/OVS.Client/ViewModels/SettingsViewModel.cs` (neu)
- `src/OVS.Client/Settings/ClientSettings.cs` (ändern)
- `src/OVS.Client/Audio/AudioDevices.cs` (neu): Geräteliste über `MMDeviceEnumerator`
- `src/OVS.Client/Audio/AudioEngine.cs` (ändern): Gerätewechsel im laufenden Betrieb
- `src/OVS.Client/Input/KeyPoller.cs` (ändern): Modus "nächste gedrückte Taste erfassen"
- `tests/OVS.Tests/Client/SettingsViewModelTests.cs` (neu)
- `tests/OVS.Tests/Client/ClientSettingsTests.cs` (ändern)

### Kontext

**Einstellungen:**

| Einstellung | Wertebereich / Standard |
|---|---|
| Eingabegerät, Ausgabegerät | `null` = Standardgerät |
| Mikrofonverstärkung | 0 bis 200 % |
| Ausgabelautstärke | 0 bis 100 % |
| Sprechmodus | PTT oder VAD |
| PTT-Taste, Link-PTT-Taste | Standard X1 und X2 |
| VAD-Schwelle | -60 bis -10 dBFS |

Dazu ein Pegelmesser, der live das Mikrofon anzeigt.

### Acceptance Criteria

- [x] AC1: Die Einstellungen überstehen Speichern und Laden unverändert. Eine fehlende Datei ergibt die Standardwerte.
- [x] AC2: Eine beschädigte Datei ergibt die Standardwerte. Die alte Datei wird als `settings.json.bak` gesichert und nicht einfach überschrieben.
- [x] AC3: Sind PTT und Link-PTT dieselbe Taste, zeigt das ViewModel einen Fehler, und Speichern ist deaktiviert.
- [x] AC4: Werte ausserhalb des Bereichs werden auf die Grenzen gesetzt.
- [x] AC5: Ist ein gespeichertes Gerät nicht mehr vorhanden, wird das Standardgerät verwendet, und ein Hinweis erscheint.
- [x] AC6 (manuell): Ein Wechsel des Ausgabegeräts während eines Gesprächs verlegt die Wiedergabe ohne neue Verbindung.
- [ ] AC7 (manuell): Der Pegelmesser bewegt sich, und die Markierung der VAD-Schwelle ist sichtbar. **Offen, siehe Umsetzungsstand.**

### Tests (TDD)

1. `ClientSettingsTests.cs`:
   - `"FullSettings_RoundTrip"`, `"Missing_Defaults"` (AC1)
   - `"Corrupt_DefaultsAndBackup"` (AC2)
2. `SettingsViewModelTests.cs`:
   - `"SamePttKeys_ErrorAndSaveDisabled"` (AC3)
   - `[Theory] "OutOfRange_Clamped"` (AC4)
   - `"MissingDevice_FallbackWithHint"`: Geräteliste als Parameter übergeben (AC5)
3. Manuelle Checks für AC6 und AC7.

Testbefehl: `dotnet test --filter "FullyQualifiedName~ClientSettingsTests|FullyQualifiedName~SettingsViewModelTests"`

### Umsetzungsschritte

1. Tests rot schreiben, dann `ClientSettings` und `SettingsViewModel` grün machen.
2. `AudioDevices`, den Dialog, die Tastenerfassung und den Gerätewechsel verdrahten.
3. Die manuellen Checks durchführen.

### Out of Scope

- Lautstärke pro Nutzer
- Rauschunterdrückung (A18)

---

## Package 18: Admin- und Moderations-UI

**Ziel:** Nutzer mit den passenden Rechten erledigen Channelverwaltung, Linking, Moderation und Gruppenverwaltung vollständig im Client.

**Abhängigkeiten:** Package 8, 9, 10, 16

**Betroffene Dateien:**
- `src/OVS.Client/ViewModels/ChannelViewModel.cs`, `UserViewModel.cs` (ändern): Sichtbarkeit der Menüpunkte
- `src/OVS.Client/Views/ChannelEditDialog.axaml`, `LinkDialog.axaml`, `BanDialog.axaml`, `RedeemTokenDialog.axaml`, `AdminDialog.axaml` (neu). Der Admin-Dialog hat die Tabs Gruppen, Nutzer, Bans und Server.
- `src/OVS.Client/ViewModels/AdminViewModel.cs` (neu)
- `src/OVS.Client/ErrorTexts.cs` (neu): deutsche Texte für jeden Fehlercode
- `src/OVS.Shared/Permissions/Permission.cs` (ändern): `IsSubset` und `CanActOn` hierher verschieben, damit Client und Server dieselbe Regel nutzen
- `tests/OVS.Tests/Client/MenuPermissionTests.cs`, `AdminViewModelTests.cs`, `ErrorTextsTests.cs` (neu)

### Kontext

- Die Sichtbarkeit richtet sich nach `UserInfo.Permissions` des eigenen Nutzers.
- Für Aktionen gegen andere Nutzer spiegelt der Client die Regel `CanActOn`. Der Server prüft trotzdem immer selbst.

### Acceptance Criteria

- [x] AC1: Das Channel-Kontextmenü zeigt Anlegen, Bearbeiten, Löschen, Verlinken und Entlinken nur mit `ChannelCreate`, `ChannelEdit`, `ChannelDelete` bzw. `ChannelLink`.
- [x] AC2: Das Nutzer-Kontextmenü zeigt Verschieben, Stummschalten, Kicken und Bannen nur mit dem jeweiligen Recht und nur, wenn die Rechte des Ziels eine Teilmenge der eigenen sind.
- [x] AC3: Der Link-Dialog bietet nur Channels an, die nicht der eigene sind und noch nicht gelinkt sind. Bestätigen sendet `LinkChannels`.
- [x] AC4: Im Gruppen-Editor sind Rechte, die man selbst nicht hat, deaktiviert. Die Admin-Gruppe ist schreibgeschützt. Für Admin und Gast ist Löschen deaktiviert.
- [x] AC5: Der Nutzer-Tab zeigt das Ergebnis von `ListUsers`. Zuweisen und Entfernen senden die passenden Anfragen. Gruppen, die über die eigenen Rechte hinausgehen, sind deaktiviert.
- [x] AC6: Der Bans-Tab zeigt die Liste und erlaubt Entbannen. Der Ban-Dialog bietet einen Grund, die Dauer (1 h, 1 Tag, 7 Tage, dauerhaft) und die Option "IP einschliessen".
- [x] AC7: Der Server-Tab (Name, Willkommenstext, Passwort setzen oder entfernen) ist nur mit `ServerConfig` sichtbar und sendet `UpdateServerSettings`.
- [x] AC8: "Admin-Token einlösen" ist verfügbar, solange man kein Admin ist. `InvalidToken` wird als Meldung angezeigt.
- [x] AC9: Jeder Fehlercode aus den Packages 4 bis 10 hat einen deutschen Text.

### Tests (TDD)

1. `MenuPermissionTests.cs`:
   - `[Theory] "ChannelMenu_VisibilityByPermission"` (AC1)
   - `[Theory] "UserMenu_VisibilityByPermissionAndRank"` (AC2)
2. `AdminViewModelTests.cs`:
   - `"LinkDialog_ExcludesSelfAndLinked"` (AC3)
   - `"GroupEditor_DisablesUnownedPermissions"`, `"AdminGroup_ReadOnly"` (AC4)
   - `"AssignGroup_SendsRequest_DisablesHigherGroups"` (AC5)
   - `"Ban_SendsDurationAndIpFlag"` (AC6)
   - `"ServerTab_HiddenWithoutServerConfig"` (AC7)
   - `"RedeemToken_HiddenForAdmin"` (AC8)
3. `ErrorTextsTests.cs > "EveryErrorCode_HasText"`: Schleife über alle Codes (AC9)

Testbefehl: `dotnet test --filter "FullyQualifiedName~MenuPermissionTests|FullyQualifiedName~AdminViewModelTests|FullyQualifiedName~ErrorTextsTests"`

### Umsetzungsschritte

1. `IsSubset` und `CanActOn` nach Shared verschieben. Die bestehenden Tests aus Package 6 müssen grün bleiben.
2. Menü-Tests rot schreiben und die Sichtbarkeitslogik grün machen.
3. Die AdminViewModel-Tests einzeln rot schreiben und das ViewModel implementieren.
4. `ErrorTexts` testgetrieben umsetzen.
5. Die Dialoge in XAML bauen.

### Out of Scope

- Audit-Log
- Kanalbezogene Rechte (A1)

---

## Package 19: Hosting-Abschluss

**Ziel:** Server-Images für amd64 und arm64 sowie ein eigenständiger Windows-Client lassen sich mit dokumentierten Befehlen reproduzierbar bauen und betreiben.

**Abhängigkeiten:** Package 5, 12, 18

**Betroffene Dateien:**
- `Dockerfile` (ändern): Multi-Arch
- `docker-compose.yml` (ändern): auskommentierte Alternative mit `network_mode: host`
- `README.md` (neu)

### Kontext

**Multi-Arch nach dem offiziellen .NET-Muster:**
- `FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build`
- `ARG TARGETARCH`
- `dotnet publish -a $TARGETARCH`

Die Runtime-Stage wird pro Plattform gezogen.

**Client:** `dotnet publish src/OVS.Client -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`

### Acceptance Criteria

- [x] AC1: `docker buildx build --platform linux/amd64,linux/arm64 -t openvoicespeak/server:dev .` läuft ohne Fehler durch.
- [x] AC2: Das arm64-Image startet (auf einem arm64-Host oder per QEMU mit `docker run --platform linux/arm64 ...`) und loggt `Listening on 0.0.0.0:7000`.
- [x] AC3: Der Client-Publish ergibt eine einzelne `.exe`, die auf einem Windows-PC ohne installiertes .NET startet.
- [x] AC4: Die README beschreibt:
  - Schnellstart mit Compose
  - Ports für TCP und UDP samt Firewall-Beispiel (`ufw allow 7000/tcp` und `ufw allow 7000/udp`)
  - wie man das Admin-Token aus `docker compose logs` liest
  - wie man den Zertifikat-Fingerprint prüft
  - Backup und Restore des Volumes (Beispiel mit `docker run --rm -v ovs-data:/data -v "$PWD":/backup alpine tar czf /backup/ovs-data.tgz -C /data .`)
  - den Update-Ablauf (`docker compose pull && docker compose up -d`)
  - `chown 1654` bei Bind-Mounts
  - die Alternative mit Host-Netzwerk
  - eine Tabelle aller Umgebungsvariablen
- [x] AC5 (manuell): Auf einer frischen Linux-VM führt das Befolgen der README (und nur der README) zu einem laufenden Server, mit dem sich der Client verbinden kann.

### Tests (TDD)

In diesem Package gibt es keine neue Logik, deshalb keine neuen Unit-Tests. Die Prüfungen:
1. `dotnet test`: alle bestehenden Tests sind grün (Regressionsschutz).
2. AC1 bis AC3 mit den Befehlen aus den ACs.
3. AC4 per Checkliste gegen die README.
4. AC5 als manueller Durchlauf.

Testbefehl: `dotnet test`, danach die Befehle aus AC1 bis AC3

### Umsetzungsschritte

1. Das Dockerfile auf Multi-Arch umstellen und AC1 und AC2 prüfen.
2. Den Client-Publish prüfen (AC3).
3. Die README schreiben (AC4).
4. Den Durchlauf auf der VM machen (AC5).

### Out of Scope

- Veröffentlichung in einer Registry (A15)
- CI-Pipeline
- Installer und Code-Signing
- Clients für Linux und macOS (A3)

---

## Package 20: Debug-API für den Client

**Ziel:** Der Client lässt sich ohne Maus und Tastatur komplett steuern und prüfen, lokal und abgesichert gegen Zugriffe aus dem Browser.

**Abhängigkeiten:** Package 16, 17, 18

**Betroffene Dateien:**
- `src/OVS.Client/Debug/DebugApi.cs` (neu): HTTP-API auf `localhost`, Header `X-OVS-Debug`
- `src/OVS.Client/Debug/AudioDebugLog.cs` (neu): `--audio-debug` aus Package 14
- `src/OVS.Client/Program.cs` (ändern): `--profile`, `--debug-api <port>`, `--no-audio`, `--audio-debug`
- `src/OVS.Client/Audio/AudioEngine.cs`, `CapturePipeline.cs` (ändern): Testton statt Mikrofon, Betrieb ohne Geräte, Statistik
- `src/OVS.Client/Input/KeyPoller.cs` (ändern): simulierte PTT-Tasten
- `tests/OVS.Tests/TestSupport/TestDispatcher.cs`, `tests/OVS.Tests/Client/DebugApiTests.cs` (neu)

### Kontext

Die manuellen Ende-zu-Ende-Checks brauchen sonst zwei Rechner, Headsets und echte Tastendrücke. Die Debug-API ersetzt Maus und Tastatur. Der Testton ersetzt das Mikrofon. `--no-audio` erlaubt Clients ohne Audiogeräte, deren Mixer trotzdem im 20-ms-Takt läuft. Endpunkte: `GET /state`, `GET /devices`, `POST /connect`, `/disconnect`, `/join`, `/ptt`, `/linkptt`, `/mute`, `/deafen`, `/tone`, `/redeem`, `/create-channel`, `/delete-channel`, `/link`, `/unlink`, `/move`, `/kick`, `/ban`, `/server-mute`, `/request` (beliebige Protokollanfrage), `/settings`.

### Acceptance Criteria

- [x] AC1: Die API ist nur mit `--debug-api` aktiv, lauscht nur auf `localhost` und lehnt Anfragen ohne Header `X-OVS-Debug` mit 403 ab.
- [x] AC2: Zwei Clients ohne Audiogeräte verbinden sich über die API mit einem echten Server, und `/state` spiegelt Channels, Nutzer, Sprechanzeige, Links und Audio-Statistik.
- [x] AC3: Mit Testton und simulierter PTT kommt Sprache beim anderen Client an. Normales PTT bleibt im eigenen Channel, Link-PTT erreicht gelinkte Channels, und die Sprechanzeige bleibt an, solange gesprochen wird.
- [x] AC4: Ohne `SpeakLinked` bleibt Link-PTT im eigenen Channel, und der Hinweis erscheint. Stummgeschaltete Clients senden nichts, taub geschaltete empfangen nichts.
- [x] AC5: Verwaltungsaktionen (Admin-Token, Gruppen über `/request`, Server-Mute, Kick) wirken, und der Status des gekickten Clients nennt den Grund.
- [x] AC6: `--profile` trennt Identität und Einstellungen, sodass mehrere Instanzen auf einem Rechner laufen.

### Tests (TDD)

`DebugApiTests.cs`: `MissingHeader_Forbidden` (AC1), `Devices_AreListed`, `UnknownChannel_IsBadRequest`, `FullFlow_Linking_PttAndLinkPtt` (AC2 bis AC4), `DeafenedClient_HearsNothing_UntilUndeafened` (AC4), `AdminActions_ThroughApi` (AC5). AC6 ist durch die zwei getrennten Profile in jedem dieser Tests abgedeckt.

Testbefehl: `dotnet test --filter "FullyQualifiedName~DebugApiTests"`

### Out of Scope

- Fernzugriff von anderen Rechnern
- Steuerung von Fenstern und Dialogen (die API spricht die ViewModels an)

---

## Package 21: Server- und Channel-Logs

**Ziel:** Der Server schreibt allgemeine Ereignisse in ein Server-Log und alles, was einen bestimmten Channel betrifft, in ein eigenes Log dieses Channels, mit einer neuen Datei pro Serverstart, beides dauerhaft im Datenverzeichnis.

**Abhängigkeiten:** Package 12

**Betroffene Dateien:**
- `src/OVS.Server/Logging/ServerLogs.cs` (neu): Server- und Channel-Log, Konsole
- `src/OVS.Shared/Logging/LogFiles.cs` (neu): eine Datei pro Start, Tageswechsel, Aufbewahrung, Schreibfehler ohne Absturz. Liegt in Shared, weil Package 22 sie wiederverwendet.
- `src/OVS.Client/Net/ClientConnection.cs`, `tests/OVS.Tests/TestSupport/TestClient.cs` (ändern): sauberes TLS-Ende beim Trennen, damit der Server "vom Client beendet" statt "Verbindung abgebrochen" protokolliert
- `src/OVS.Server/ServerState.cs`, `Commands/*.cs`, `ControlServer.cs` (ändern): Ereignisse protokollieren
- `src/OVS.Server/ServerConfig.cs` (ändern): `OVS_LOG_DAYS`
- `src/OVS.Server/Program.cs` (ändern): Start, Stopp und Startfehler ins Server-Log
- `tests/OVS.Tests/Server/ServerLogsTests.cs` (neu), `tests/OVS.Tests/TestSupport/TestServer.cs` (ändern)
- `README.md` (ändern): Ablage der Logs, Aufbewahrung, neue Umgebungsvariable

### Kontext

Bisher schreibt der Server nur wenige Zeilen auf die Konsole (`docker compose logs`) und nichts in Dateien. Channel-Änderungen, Channel-Wechsel, Links, Gruppen und Einstellungen werden gar nicht protokolliert.

**Ablage** im Volume `/data`, übersteht also Updates:
- `<DataDir>/logs/server/2026-09-27_17-29-22.log`
- `<DataDir>/logs/channels/<Channel-ID>/2026-09-27_17-29-22.log`. Die ID bleibt beim Umbenennen gleich, jede Zeile nennt den aktuellen Channel-Namen. Das Log eines gelöschten Channels bleibt erhalten.

**Dateiname:** Datum und Uhrzeit des Serverstarts. Jeder Start beginnt neue Dateien, damit bei einem Problem nur die Zeilen dieses Laufs durchsucht werden müssen. Server-Log und Channel-Logs eines Laufs tragen denselben Namen. Läuft der Server über Mitternacht, beginnt eine neue Datei mit dem Zeitpunkt des Wechsels im Namen, damit ein monatelang laufender Server keine endlose Datei schreibt.

**Zeilenformat:** `2026-09-27 17:29:22.810 Text` in lokaler Serverzeit, wie auf der Konsole.

**Server-Log (allgemein):**
- Start und Stopp, Startfehler, Listening und Fingerprint
- Verbinden (Nickname, Fingerprint-Anfang, IP), Trennen mit Grund (normal, Timeout, gekickt, gebannt, ersetzt), abgelehnter Handshake mit Grund und IP
- Admin-Token eingelöst, Gruppen angelegt, geändert und gelöscht, Gruppen zugewiesen und entfernt, Servereinstellungen geändert
- Kick, Ban (mit Dauer und IP-Option), Unban, Server-Mute an und aus
- Anlegen und Löschen eines Channels zusätzlich als Übersichtszeile

**Channel-Log (pro Channel):**
- Channel angelegt, geändert (vorher und nachher), gelöscht
- Nutzer betritt den Channel: Beitritt, beim Verbinden im Standard-Channel, durch Verschieben mit Akteur
- Nutzer verlässt den Channel: Wechsel, Trennen, Kick, Bann, weil der Channel gelöscht wurde
- Link gesetzt und entfernt, in den Logs beider beteiligten Channels

**Sicherheit:** Das Admin-Token erscheint nur auf der Konsole, nie in einer Datei. Die Datei vermerkt nur, dass ein Token erzeugt wurde. Passwörter werden nie protokolliert, nur "Passwort gesetzt" oder "Passwort entfernt".

**Aufbewahrung:** `OVS_LOG_DAYS` (Standard 30, `0` = unbegrenzt). Dateien, deren Startdatum älter ist, werden beim Start und beim Tageswechsel gelöscht.

### Acceptance Criteria

- [x] AC1: Jede Server-Log-Zeile erscheint auf der Konsole und in `logs/server/<Start>.log`. Jeder Serverstart beginnt eine neue Datei, die Channel-Logs desselben Laufs tragen denselben Namen. Nach Mitternacht beginnt eine neue Datei.
- [x] AC2: Jedes genannte allgemeine Ereignis erzeugt genau eine Zeile im Server-Log, mit Akteur und Ziel, wo es sie gibt.
- [x] AC3: Jedes genannte Channel-Ereignis steht im Log des betroffenen Channels. Verschieben steht im Log des alten und des neuen Channels, ein Link in den Logs beider Channels.
- [x] AC4: Das Admin-Token und Passwörter stehen in keiner Logdatei.
- [x] AC5: Dateien, die älter als `OVS_LOG_DAYS` Tage sind, werden beim Start und beim Tageswechsel gelöscht. Bei `0` wird nichts gelöscht. Ungültige Werte ergeben einen Konfigurationsfehler mit Exit-Code 1.
- [x] AC6: Ein Schreibfehler im Log, etwa bei voller Platte, bringt den Server nicht zum Absturz. Er meldet ihn einmal auf der Konsole.
- [x] AC7: Nach Neustart und Umbenennen schreibt ein Channel weiter in denselben Ordner.

### Tests (TDD)

`ServerLogsTests.cs`, mit `ManualTimeProvider` und temporärem Datenverzeichnis:
1. `"ServerLine_GoesToConsoleAndFileOfThisStart"`, `"EachStart_OwnFile_SharedByServerAndChannelLogs"`, `"DayChange_StartsNewFile_NamedAfterThatMoment"` (AC1)
2. `"ConnectDisconnectRejected_Logged"`, `"AdminActions_LoggedWithActor"` für Gruppen, Zuweisung, Einstellungen, Kick, Ban, Unban und Server-Mute (AC2)
3. `"JoinMoveLeave_LoggedInChannelLogs"` (Beitritt, Verschieben im alten und neuen Channel, Trennen), `"Link_LoggedInBothChannels_EditDeleteLogged"` (AC3)
4. `"TokenAndPassword_NeverInFiles"` (AC4)
5. `"OldFiles_DeletedAfterRetention"`, `"RetentionZero_KeepsAll"` und in `ServerConfigTests` `"Load_InvalidLogDays_Throws"` (AC5)
6. `"WriteFailure_DoesNotThrow_ReportsOnce"`, dazu `"ReaderLockingTheFile_LineIsNotLost"`: Ein Leser, der die Datei sperrt, kostet keine Zeile (AC6)
7. `"RenamedChannel_SameFolderAfterRestart"` (AC7)

Testbefehl: `dotnet test --filter "FullyQualifiedName~ServerLogsTests"`

### Out of Scope

- Anzeige oder Download der Logs im Client
- Sprachaktivität (wer wann spricht) im Log
- Versand der Logs an externe Systeme

---

## Package 22: Client-Log

**Ziel:** Der Client schreibt alles, was er tut und erlebt, in eine einzige Logdatei im Profil.

**Abhängigkeiten:** Package 16, 20

**Betroffene Dateien:**
- `src/OVS.Client/Logging/ClientLog.cs` (neu): eine Datei pro Start, lesbare Beschreibung von Protokollnachrichten und eigenen Anfragen
- `src/OVS.Shared/Logging/LogFiles.cs` (aus Package 21, unverändert): Dateien pro Start und Aufbewahrung
- `src/OVS.Client/ViewModels/MainViewModel.cs` (ändern): Ereignisse protokollieren. Nachrichten und Anfragen werden dort beim Empfangen bzw. Senden abgegriffen, `ServerViewModel.cs` bleibt unverändert.
- Geräte, Warnungen, Sendebeginn und -ende kommen über die vorhandenen Events und Rückgabewerte von `AudioEngine` an, die Datei bleibt unverändert.
- `src/OVS.Client/Debug/AudioDebugLog.cs` (ändern): `--audio-debug` schreibt in dieselbe Datei
- `src/OVS.Client/App.axaml.cs` (ändern): Start und Ende
- `tests/OVS.Tests/Client/ClientLogTests.cs` (neu)
- `README.md` (ändern): Ablage des Client-Logs

### Kontext

Der Client zeigt heute nur einzelne Meldungen im Fenster, und `--audio-debug` schreibt eine eigene Datei. Künftig schreibt der Client ein einziges allgemeines Log, mit einer neuen Datei pro Start: `<Profil>/logs/client-2026-09-27_18-54-43.log`, standardmässig also unter `%APPDATA%\OpenVoiceSpeak\logs\`. So lassen sich bei einem Problem die Zeilen anderer Starts direkt ausschliessen. Läuft der Client über Mitternacht, beginnt eine neue Datei. Dateien, deren Startdatum älter als 30 Tage ist, werden beim Start und beim Tageswechsel gelöscht.

**Protokolliert wird:**
- Start und Ende mit Version und Kommandozeilen-Optionen
- Einstellungen geladen, gespeichert und geändert (Geräte, Tasten, Modus), Warnungen zu Geräten und beschädigten Dateien
- Verbindungsversuche (Adresse, Port, Nickname), TOFU-Entscheidung mit Fingerprint, verbunden, abgelehnt mit Grund, getrennt mit Grund, UDP erreichbar oder nicht
- alles, was vom Server kommt: Nutzer verbunden, getrennt, verschoben, Channels angelegt, geändert, gelöscht, Links, Gruppen, Servereinstellungen, Fehlermeldungen
- eigene Aktionen: Channel-Wechsel, Mute, Deafen, alle Verwaltungsanfragen
- Senden beginnt und endet, mit Ziel Channel oder Links
- mit `--audio-debug` zusätzlich PTT-Tastenwechsel und Frames pro Sekunde

**Nie protokolliert:** Serverpasswort, Admin-Token, Identitätsschlüssel.

### Acceptance Criteria

- [x] AC1: Jedes genannte Ereignis erzeugt eine lesbare Zeile mit Zeitstempel im Client-Log. Protokollnachrichten erscheinen mit Namen statt IDs.
- [x] AC2: Jeder Start hat eine eigene Datei mit Datum und Uhrzeit im Namen. Dateien, die älter als 30 Tage sind, werden beim Start und beim Tageswechsel gelöscht.
- [x] AC3: Serverpasswort und Admin-Token stehen nie in der Datei.
- [x] AC4: `--audio-debug` schreibt in das Client-Log, eine eigene `audio-debug.log` gibt es nicht mehr.
- [x] AC5: Ein Schreibfehler im Log bringt den Client nicht zum Absturz.
- [x] AC6: Die Meldungsliste im Fenster zeigt weiterhin nur Fehler, Willkommenstext, Trennungen und Warnungen.

### Tests (TDD)

`ClientLogTests.cs`:
1. `[Theory] "Describe_Message"` für jede Delta-Art, mit Namen aus dem `StateMirror` (AC1)
2. `"Connect_Disconnect_Logged"` über `MainViewModel` gegen `TestServer` (AC1)
3. `"OwnActions_Logged"` über `ServerViewModel`, denselben Weg, den auch die Debug-API nimmt (AC1). Dazu `"Describe_Request_UsesNames_NeverSecrets"`.
4. `"EachStart_OwnFile_OldFilesDeleted"` mit `ManualTimeProvider`, auch über Mitternacht (AC2)
5. `"PasswordAndToken_NeverLogged"` (AC3)
6. `"AudioDebug_WritesToClientLog"` (AC4), ersetzt `MainViewModelTests.AudioDebugLog_RecordsKeysAndFrameRate`
7. `"WriteFailure_DoesNotThrow"` (AC5)

Testbefehl: `dotnet test --filter "FullyQualifiedName~ClientLogTests"`

### Out of Scope

- Anzeige des Logs im Client
- Mehrere Logdateien oder Log-Level

---

## Package 23: Automatischer Neustart und Log-Tageswechsel

**Ziel:** Ein Server, der Tage oder Monate läuft, startet auf Wunsch täglich zu einer einstellbaren Uhrzeit neu, und ob nach Mitternacht neue Logdateien beginnen, ist einstellbar.

**Abhängigkeiten:** Package 19, 21

**Betroffene Dateien:**
- `src/OVS.Server/ServerHost.cs` (neu): ein Serverlauf, die Schleife über Läufe, Berechnung des nächsten Neustarts
- `src/OVS.Server/Program.cs` (ändern): nur noch Signale und Aufruf von `ServerHost`
- `src/OVS.Server/ServerConfig.cs` (ändern): `OVS_AUTO_RESTART`, `OVS_AUTO_RESTART_TIME`, `OVS_LOG_ROTATE_DAILY`
- `src/OVS.Server/ControlServer.cs`, `ServerState.cs` (ändern): Abschied mit Grund Neustart, keine neuen Nutzer mehr während des Herunterfahrens
- `src/OVS.Shared/Logging/LogFiles.cs`, `src/OVS.Server/Logging/ServerLogs.cs` (ändern): Tageswechsel abschaltbar
- `src/OVS.Shared/Protocol/Codes.cs`, `src/OVS.Client/ErrorTexts.cs` (ändern): Code `ServerRestart` mit Text für den Nutzer
- `docker-compose.yml`, `README.md` (ändern): neue Einstellungen, Zeitzone `TZ`
- `tests/OVS.Tests/Server/ServerHostTests.cs` (neu), `ServerConfigTests.cs`, `ServerLogsTests.cs` (ändern)

### Kontext

Bisher läuft ein Serverprozess, bis er gestoppt wird, und nach Mitternacht beginnen immer neue Logdateien.

**Neustart:** im selben Prozess, nicht durch Beenden. So funktioniert er auch ohne Docker und ohne dessen Restart-Policy. Zur eingestellten Uhrzeit endet der Lauf wie beim Herunterfahren. Clients bekommen `Disconnected(ServerRestart)`, "Der Server startet neu. Verbinde dich in ein paar Sekunden erneut." Danach beginnt ein neuer Lauf: Konfiguration, Daten und Zertifikat werden neu gelesen, die Logs beginnen neue Dateien. Kann der neue Lauf nicht starten (z. B. ungültige Konfiguration), endet der Prozess mit Exit-Code 1.

**Uhrzeit:** in der lokalen Zeit des Servers, im Container über `TZ` (die Compose-Datei setzt `Europe/Berlin`, sonst gilt UTC). Liegt die Uhrzeit weniger als eine Sekunde in der Zukunft, gilt der nächste Tag, damit ein etwas zu früh feuernder Timer nicht zweimal neu startet.

**Log-Tageswechsel:** `OVS_LOG_ROTATE_DAILY` (Standard `true`, bisheriges Verhalten). Bei `false` laufen die Dateien eines Laufs bis zum nächsten Start weiter. Alte Dateien werden trotzdem beim Tageswechsel gelöscht, die gerade beschriebenen nie.

| Einstellung | Standard | Werte |
|---|---|---|
| `OVS_AUTO_RESTART` / `autoRestart` | `false` | `true`/`false`, `1`/`0`, `an`/`aus`, `on`/`off` |
| `OVS_AUTO_RESTART_TIME` / `autoRestartTime` | `04:00:00` | `hh:mm:ss` |
| `OVS_LOG_ROTATE_DAILY` / `logRotateDaily` | `true` | wie oben |

### Acceptance Criteria

- [x] AC1: Ohne Einstellung startet der Server nie von selbst neu. Mit `OVS_AUTO_RESTART=true` startet er täglich um `OVS_AUTO_RESTART_TIME` (Standard 04:00:00) neu, ohne dass der Prozess endet.
- [x] AC2: Verbundene Clients erfahren den Neustart als eigenen Grund. Nach dem Neustart können sie sich wieder verbinden.
- [x] AC3: Jeder Lauf hat eigene Logdateien. Das Server-Log des alten Laufs endet mit dem Neustart, das des neuen beginnt mit "startet (automatischer Neustart)".
- [x] AC4: Ungültige Werte (`ja`, `25:00:00`, `4:00`) ergeben einen Konfigurationsfehler mit Exit-Code 1.
- [x] AC5: Mit `OVS_LOG_ROTATE_DAILY=false` bleibt die Datei eines Laufs über Mitternacht bestehen. Alte Dateien werden weiter gelöscht, die laufende nie.
- [x] AC6: Wer sich während des Herunterfahrens verbindet, wird mit dem Grund (Neustart oder Herunterfahren) abgelehnt, statt im alten Lauf zu landen.
- [x] AC7 (manuell, Docker): Der Container mit `TZ=Europe/Berlin` zeigt Ortszeit, startet zur eingestellten Sekunde neu, bleibt dabei laufen (RestartCount 0) und schreibt zwei Logdateien. Geprüft am 2026-09-27.

### Tests (TDD)

1. `ServerHostTests > "NextRestart_TodayOrTomorrow_InLocalTime"` für vor, nach, genau zur Uhrzeit und knapp davor (AC1)
2. `ServerHostTests > "AutoRestart_TellsClients_NewRunServes_WithOwnLogFiles"`: echter Lauf mit Neustart in 5 s, Client bekommt `ServerRestart`, ein neuer Client kommt in den neuen Lauf, zwei Logdateien (AC1, AC2, AC3)
3. `ServerConfigTests > "Load_AutoRestart_OffByDefault_TimeDefaultsTo4am"`, `"Load_AutoRestartAndLogRotation_FromFile_EnvWins"`, `"Load_LogRotateDaily_OnByDefault"`, `"Load_InvalidRestartOrRotation_Throws"` (AC1, AC4)
4. `ServerLogsTests > "RotateDailyOff_FileLastsUntilRestart_OldFilesStillDeleted"` (AC5)
5. `ServerHostTests > "ShuttingDown_HandshakeRejectedWithTheReason"`, Regression aus Test 2, der vereinzelt einen Client im alten Lauf fand (AC6)

Testbefehl: `dotnet test --filter "FullyQualifiedName~ServerHostTests|FullyQualifiedName~ServerConfigTests|FullyQualifiedName~ServerLogsTests"`

### Out of Scope

- Automatisches Wiederverbinden im Client
- Neustart per Befehl aus dem Client
- Den Tageswechsel des Client-Logs abschalten (der Client beginnt weiter nach Mitternacht eine neue Datei)
