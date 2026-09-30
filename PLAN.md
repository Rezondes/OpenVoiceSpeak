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
> **Packages 50 und 51:** In der Anfrage stand die Lautstärke je Nutzer zuerst. Sie kommt als 51 nach der Grundverstärkung (50), weil beide im selben Mixer wirken und 51 den Begrenzer aus 50 braucht.
>
> **Parallel möglich:** Nach Package 1 können 2, 3, 6, 11 und 13 unabhängig voneinander laufen. Nach Package 7 können 8, 9 und 10 parallel laufen.
>
> **Packages 65 bis 75:** Die Reihenfolge weicht von der Anfrage ab, kleine UI-Punkte zuerst, dann Server und Verwaltung, Backup zuletzt. Zuordnung der Anfrage: Backup -> 74 und 75, Link-Icon und Breite -> 67, stummer Channel -> 66, Slots -> 69, Nutzerübersicht -> 71 und 72, Nutzerdaten -> 70, Gruppen-Ton -> 73, "Verwaltung ..." -> 65, Seitenleiste zu breit -> 68. 65, 66, 69 und 73 können parallel beginnen. Package 76 (einzelne Rechte) kam nachträglich dazu und wird vor 71 und 72 umgesetzt. Es kann ebenfalls sofort beginnen. Package 68 wurde nachträglich vom Bugfix "Seitenleiste zu breit" zum responsiven Grundgerüst erweitert. Die Packages 77 bis 79 machen darauf aufbauend Einstellungen, Verwaltung sowie Chat und Dialoge responsiv und können nach 68 parallel laufen.
>
> **Packages 80 to 82 (written in English from here on, at the user's request):** 80 is the ban overview, 81 and 82 are the log viewer split into viewing/searching and downloading. 80 and 81 can start in parallel, 82 needs 81.
>
> **Packages 83 to 92 (security audit):** the request "check that every permission is enforced on the server" was audited first (two read-only passes over all requests, the voice path, handshake, backups and logs). The voice path and sender identity are sound; the findings were split by theme into ten packages at the user's request. 92 needs 84, 87 needs 86; all others can run in parallel. Recommended order by severity: 86, 89, 85, 84, 87, 83, 88, 90, 91, 92.

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
| 24 | Moderne Oberfläche | Der Client sieht aus und bedient sich wie eine aktuelle Voice-App, hell und dunkel. | 16, 17, 18 |
| 25 | Sprachaktivierung ohne PTT-Taste | Im Modus Sprachaktivierung schaltet die PTT-Taste das Mikrofon nicht mehr frei. | 14 |
| 26 | Alles in einem Fenster | Einstellungen, Verwaltung und alle Dialoge erscheinen im Hauptfenster statt in eigenen Fenstern. | 24 |
| 27 | Eigener Fensterrahmen | Das Hauptfenster hat eine eigene Titelleiste im App-Design statt des Windows-Rahmens. | 26 |
| 28 | App-Logo | OpenVoiceSpeak hat ein eigenes Logo für Programmdatei, Taskleiste, Titelleiste und Startbildschirm. | 27 |
| 29 | Tastenbelegungen | Beliebige Aktionen lassen sich global auf Tasten oder Tastenkombinationen legen, neue Profile starten ohne Belegung. | 25, 26 |
| 30 | Server-Logo | Admins laden ein eigenes quadratisches Server-Logo hoch, das alle Clients statt des Buchstabens sehen. | 26 |
| 31 | Chat-Grundlage | Der Server vermittelt serverweite, Channel- und Privatnachrichten und prüft dafür drei neue Rechte. | 8, 21 |
| 32 | Chat-Oberfläche | Statt der Aktivität zeigt der Hauptbereich einen Chat mit den Tabs Allgemein und aktueller Channel. | 26, 31 |
| 33 | Privatchats | Zwei Nutzer schreiben sich in einem eigenen Tab privat. | 32 |
| 34 | Stummer Channel | In einem Channel mit der Option "Stumm" wird niemand gehört, auch nicht per Link-PTT. | 12, 18 |
| 35 | Slot-Begrenzung | Channels lassen sich auf eine Höchstzahl an Nutzern begrenzen, die nur ein neues Recht umgeht. | 7, 18 |
| 36 | Channels sortieren | Berechtigte ordnen Channels per Drag and Drop oder Kontextmenü neu, und alle sehen die neue Reihenfolge sofort. | 18, 26 |
| 37 | Gruppen sortieren | Berechtigte ordnen Gruppen in der Verwaltung neu, und alle Gruppenlisten folgen dieser Reihenfolge. | 18, 26 |
| 38 | Links-Übersicht | Berechtigte verlinken mehrere Channels auf einmal über eine Matrix in der Verwaltung. | 10, 26 |
| 39 | Passwort speichern | Serverpasswörter lassen sich verschlüsselt mit dem Lesezeichen speichern. | 26 |
| 40 | Lesezeichen in der Seitenleiste | Ohne Verbindung zeigt die Seitenleiste die Lesezeichen, und ein Klick verbindet direkt. | 39 |
| 41 | Tastenliste | Tastenbelegungen sind eine frei erweiterbare Liste, in der eine Aktion auf mehreren Tasten liegen kann. | 29 |
| 42 | Client-Release per GitHub Actions | Jeder Push auf `main` veröffentlicht nach grünen Tests die Client-exe als versioniertes GitHub-Release. | 19 |
| 43 | Update-Prüfung im Client | Der Client erkennt neue Releases und installiert sie auf Wunsch selbst. | 42 |
| 44 | Server-Image in der GitHub Container Registry | Jedes Release stellt das Server-Image für amd64 und arm64 als öffentliches Package bereit, und Compose nutzt es direkt. | 42 |
| 45 | Lokalisierung: Grundlage | Der Client hat eine Sprachwahl (Wie Windows, Deutsch, English), und alle Texte aus dem C#-Code gibt es auf Deutsch und Englisch. | 26 |
| 46 | Lokalisierung: Oberfläche | Alle XAML-Views zeigen ihre Texte in der gewählten Sprache, kein fest verdrahteter Text bleibt übrig. | 45 |
| 47 | Sounds | Der Client spielt bei Mikrofon, Ton, Verbindung, Channel und Privatnachricht kurze Töne, mit Gesamtlautstärke und "Alle Sounds aus". | 46 |
| 48 | Sounds anpassen | Jeder Sound hat eine eigene Lautstärke, lässt sich stumm schalten und durch eine eigene Datei ersetzen. | 47 |
| 49 | Website | Eine zweisprachige Seite auf GitHub Pages stellt die App für Nutzer vor und wird mit jedem Release neu ausgeliefert. | 42, 46 |
| 50 | Stimmen lauter | Andere Stimmen kommen beim Zuhörer standardmässig doppelt so laut an, ohne bei lauten Stellen zu verzerren. | 13, 17 |
| 51 | Lautstärke je Nutzer | Jeder kann die Lautstärke einzelner anderer Nutzer zwischen 0 und 200 % einstellen, und die Einstellung bleibt je Person erhalten. | 50 |
| 52 | Einstellungen ohne Einfrieren | Einstellungen öffnen, Speichern und Programmstart blockieren die Oberfläche nicht mehr durch das Auflisten der Audiogeräte. | 17 |
| 53 | Selbsttest | Ein Button in den Einstellungen schaltet einen stumm und taub und spielt die eigene Stimme so zurück, wie andere sie hören. | 50, 52 |
| 54 | Ein Channel-Dialog | Anlegen und Bearbeiten eines Channels nutzen denselben Dialog mit allen Optionen, leer beim Anlegen und vorbelegt beim Bearbeiten. | 34, 35 |
| 55 | Lautstärke bis 200 % | Der Regler "Lautstärke" geht von 0 bis 200 %, Standard 100 %. | 50 |
| 56 | Töne für Chat-Nachrichten | Nachrichten in "Allgemein" und im Channel-Chat haben je einen eigenen Sound, standardmässig den Ton der Privatnachricht. | 48 |
| 57 | Ton für Sprache über Link | Beginnt jemand aus einem anderen Channel per Link-PTT zu sprechen, hört man einen kurzen eigenen Ton. | 48, 56 |
| 58 | Reihenfolge der Einstellungen | Die Einstellungen zeigen Geräte, Lautstärke, Übertragung, Tasten, Sounds, Darstellung und Über in dieser Reihenfolge. | - |
| 59 | Push-to-Talk-Taste von Anfang an | Ein neues Profil hat eine passende PTT-Taste, und die Einstellungen weisen auf eine fehlende PTT-Taste hin. | 41, 58 |
| 60 | Ton für die eigene Link-PTT | Beginnt man selbst über Link zu sprechen, hört man einen eigenen, einzeln anpassbaren Ton, standardmässig den aus Package 57. | 57 |
| 61 | Durchsichtiger Hintergrund | Die grossen Hintergrundflächen des Fensters lassen sich von 0 bis 100 % Deckkraft einstellen, auf Wunsch weichgezeichnet, während Text und Bedienelemente lesbar bleiben. | 53, 58 |
| 62 | Update-Fortschritt | Während ein Update geladen, geprüft und gestartet wird, liegt über dem ganzen Fenster eine Karte mit Fortschrittsbalken, Prozent und Megabyte. | - |
| 63 | Neustart nach Update | Nach einem Update startet die neue Version von selbst, die alte `.exe.old` verschwindet, und jeder Fehler auf dem Weg steht im Log. | - |
| 64 | Eigene Nachrichten rechts | Eigene Chatnachrichten stehen rechts als Blase ohne Avatar, die anderer links, Hinweise weiter über die volle Breite. | - |
| 65 | Verwaltungs-Button ohne Auslassungspunkte | Der Button in der Kopfzeile heisst nur noch "Verwaltung" bzw. "Administration". | - |
| 66 | Stummer Channel mit eigenem Icon | Ein stummer Channel zeigt vorne ein Stumm-Icon statt des Lautsprechers und hinter dem Namen kein zusätzliches Stumm-Icon mehr. | - |
| 67 | Link-Icon und passende Breite der Seitenleiste | Verlinkte Channels zeigen nur noch ein Link-Icon hinter dem Namen (Partner im Tooltip), und beim Betreten eines Servers ist die Seitenleiste breit genug für Namen und Icons. | 66 |
| 68 | Responsives Grundgerüst | Das Hauptfenster passt sich jeder Breite ab 360 px an: Die Kopfzeile wird nie abgeschnitten, und unter 700 px wird die Seitenleiste zu einer einblendbaren Leiste. | 67 |
| 69 | Servereinstellungen in der Verwaltung | Nutzerlimit, Log-Aufbewahrung, tägliche Log-Datei und automatischer Neustart lassen sich unter "Verwaltung -> Server" ändern und wirken sofort, Umgebungsvariablen gelten nur noch beim ersten Start. | - |
| 70 | Nutzerstatistiken auf dem Server | Der Server merkt sich pro Nutzer ersten und letzten Login, Anzahl Logins, Online-Zeit, letzte IP, frühere Nicknames, Sprechzeit und Anzahl Chatnachrichten und liefert sie mit der Nutzerliste aus. | 69 |
| 71 | Nutzerübersicht mit Details, Suche und Filter | Unter "Verwaltung -> Nutzer" sieht man zu jedem bekannten Nutzer alle gespeicherten Daten und kann die Liste durchsuchen und filtern. | 70, 76 |
| 72 | Nutzer offline bannen, entbannen und löschen | In der Nutzerübersicht lassen sich Nutzer auch offline bannen und entbannen sowie nach einer Rückfrage mit allen Daten löschen. | 71, 76 |
| 73 | Töne bei Gruppenänderung | Wer einem Nutzer eine Gruppe gibt oder nimmt, und der betroffene Nutzer, falls online, hören je einen eigenen, einzeln einstellbaren Ton, standardmässig denselben. | - |
| 74 | Backups auf dem Server | Unter "Verwaltung -> Server" lassen sich Backups auf dem Server anlegen, auflisten, löschen und wiederherstellen. | 69 |
| 75 | Backup herunterladen und hochladen | Ein Admin kann ein Backup vom Server auf seinen PC laden und ein Backup von seinem PC hochladen und wiederherstellen. | 74 |
| 76 | Einzelne Rechte für die Verwaltung | Nutzerübersicht, Bans-Übersicht, Gruppenübersicht, Gruppen anlegen, Gruppen löschen und Nutzer löschen haben je ein eigenes Recht, und standardmässig sieht nur die Gruppe Admin die Nutzerübersicht. | - |
| 77 | Einstellungen responsiv | Die Einstellungsseite ist auf jeder Breite ab 360 px vollständig bedienbar, ohne abgeschnittene oder überlappende Elemente. | 68 |
| 78 | Verwaltung responsiv | Alle Tabs der Verwaltung sind auf jeder Breite ab 360 px vollständig bedienbar. | 68 |
| 79 | Chat, Startseite und Dialoge responsiv | Chat, Startseite, Update-Karte und alle Dialoge sind auf jeder Breite ab 360 px vollständig bedienbar. | 68 |
| 80 | Ban overview with details, history, search and filter | Under "Verwaltung -> Bans" every ban (active, expired and lifted) is shown with all stored details and can be searched, filtered and sorted like the user overview. | - |
| 81 | Server logs viewable and searchable in the app | Users with the new right "Logs ansehen" can list the server and channel log files under "Verwaltung -> Logs", open them page by page and search all of them. | - |
| 82 | Server logs downloadable | Users with the new right "Logs herunterladen" can save a single log file or a selection of files as a zip on their PC. | 81 |
| 83 | Every request validated on the server | No client request can crash its handler or store malformed text: every field is checked for null, range and allowed characters, and violations are answered with `InvalidValue`. | - |
| 84 | Consistent rank rules for every moderation action | Every action on another user is allowed only on users with strictly fewer rights, never on oneself, and never removes the last admin. | - |
| 85 | Server mute survives reconnects | A user muted by the server stays muted after disconnecting and reconnecting until someone with the right lifts it. | - |
| 86 | Limits on the control channel | No client can exhaust the server's memory, flood other clients with broadcasts or keep a dead connection open, whatever it sends. | - |
| 87 | Identity flood and large lists | Connecting with many fresh key pairs cannot bloat the data file or stall the server, and every list reaches the client even when it is large. | 86 |
| 88 | Network: UDP endpoint and IPv6 | Voice traffic can only be sent to the address of the user's own control connection, and IPv6 users cannot bypass per-IP limits and IP bans by rotating addresses. | - |
| 89 | Backups with their own right, restore only for admins | Backups are managed with the new right "Backups verwalten", uploading and restoring are reserved for members of the Admin group, and a restored archive is validated completely. | - |
| 90 | Log and console hygiene | Nothing a client sends can forge or corrupt lines in the console, the Docker log or the log files. | - |
| 91 | Salted server password hash | The server password is stored as a salted, slow hash, and existing hashes are upgraded without the admin re-entering the password. | - |
| 92 | Clients get only the rights data they need | Without "Gruppen sehen", clients no longer receive other groups' permission bits or other users' full rights; they get only what the UI needs. | 84 |

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
- **A15 Keine Veröffentlichung** in einer Container-Registry. Das Image wird nur lokal gebaut. **Ersetzt durch A41 (Package 44).**
- **A16 Client-Identitätsschlüssel** liegt unverschlüsselt in `%APPDATA%\OpenVoiceSpeak\identity.key`, wie bei Mumble. Schutz per DPAPI kommt später.
- **A17 Serverzustand** ist durch einen globalen Lock geschützt. Das reicht für kleine und mittlere Server.
- **A18 Keine Echo- oder Rauschunterdrückung** im MVP.
- **A19 Projektzustand:**
  - Das Projektverzeichnis enthält noch keinen Code und ist kein Git-Repo. Es liegen dort nur Tool-Artefakte (`.claude-flow/`, `.swarm/`, `ruvector.db`), die in die `.gitignore` kommen.
  - Alle Pfade in diesem Plan sind neu anzulegen.
  - Der Testbefehl `dotnet test` (im Projekt-Root) existiert ab Package 1. Gefilterte Läufe nutzen `dotnet test --filter "FullyQualifiedName~<Teil>"`.

**Annahmen für Package 25 bis 33** (mit dem Nutzer abgestimmt am 27.09.2026). Die Reihenfolge folgt den Abhängigkeiten, nicht der Reihenfolge der Anfrage:

- **A20 Nur ein Fenster.** Der Client öffnet nie ein weiteres Fenster. Einstellungen und Verwaltung sind Seiten im Hauptbereich, kleine Dialoge sind Overlays im Fenster. Einzige Ausnahme ist der Windows-Dateidialog für das Server-Logo, daneben geht Drag-and-drop.
- **A21 Server-Logo.** Der Client prüft die Datei (höchstens 3 MB, PNG oder JPG, genau 1:1, sonst Ablehnung mit Hinweis), verkleinert sie auf 256 x 256 PNG und lädt nur diese Version hoch. So bleibt jede Nachricht unter der Grenze von 1 MiB. Recht: `ServerConfig`. Das Logo lässt sich entfernen.
- **A22 Fensterrahmen.** Eigene Titelleiste mit eigenen Buttons. Ränder zum Grössenändern und Aero Snap bleiben, die Snap-Layouts von Windows 11 beim Hovern über Maximieren entfallen.
- **A23 App-Logo.** Eigener Entwurf: abgerundetes Quadrat in der Akzentfarbe mit stilisiertem Headset und Schallwelle, im kleinen Icon nur das Zeichen. Vor dem Einbau werden zwei bis drei Entwürfe als Bild gezeigt.
- **A24 Sprachaktivierung.** Die PTT-Taste wirkt in diesem Modus nicht. Link-PTT sendet weiter an die Links. Das ändert Regel 4 aus Package 14.
- **A25 Tasten.** Aktionen: Push-to-Talk, Link-PTT, Push-to-Mute, Mikrofon an/aus, Ton an/aus. Einzelne Tasten, Maustasten und Kombinationen mit Strg, Umschalt und Alt, eine Taste pro Aktion, global wirksam. Neue Profile haben keine Belegung, bestehende behalten Maustaste 4 und 5.
- **A26 Chat-Aufteilung.** Drei Packages: Grundlage (Server, Protokoll, Rechte), Oberfläche (Allgemein und Channel), Privatchats. "Allgemein" zeigt serverweite Nachrichten und die bisherigen Systemmeldungen.
- **A27 Chat-Verlauf.** Nur solange verbunden, der Server speichert nichts. Der Channel-Tab zeigt Nachrichten ab dem Betreten und beginnt beim Wechsel neu. Nachrichten gehen nicht an verlinkte Channels. Privat heisst Text zwischen genau zwei Personen, die beide online sind, kein Flüstern per Sprache.
- **A28 Chat-Rechte.** Neu: `ChatServer`, `ChatChannel`, `ChatPrivate`. Gast bekommt Channel und privat, Moderator und Admin alle drei. Bestehende Server geben der Gast-Gruppe einmalig Channel und privat dazu. Vom Server Stummgeschaltete dürfen schreiben.
- **A29 Chat-Grenzen und Logs.** Höchstens 2000 Zeichen, höchstens 5 Nachrichten in 5 Sekunden. Channelnachrichten ins Channel-Log, serverweite ins Server-Log, von privaten nur die Tatsache ohne Inhalt.
- **A30 Protokollversion.** Jedes Package, das das Protokoll erweitert (30, 31), erhöht `ProtocolInfo.Version`. Alte Clients bekommen die klare Meldung zur Versionsabweichung statt unbekannter Nachrichten.

**Annahmen für Package 34 bis 44** (mit dem Nutzer abgestimmt am 28.09.2026). Die Reihenfolge folgt den Abhängigkeiten: erst Server und Protokoll, dann Client, zuletzt Auslieferung. Zuordnung zur Anfrage: Punkt 1 = 34, 8 = 35, 5 = 36, 6 = 37, 7 = 38, 2 = 39, 4 = 40, 3 = 41, 9 = 42 bis 44 (auf Vorschlag in drei Packages geteilt).

- **A31 Stummer Channel.** Wer das Recht `ChannelEdit` hat, schaltet im Bearbeiten-Dialog "Stumm" ein. Dann wird dort **niemand** gehört, auch Admins nicht, und es gibt kein Recht, das das umgeht. Link-PTT aus einem stummen Channel ist ebenfalls gesperrt. Sprache aus verlinkten, nicht stummen Channels hört man im stummen Channel. Neue Channels sind nie stumm.
- **A32 Slot-Begrenzung.** `MaxUsers` pro Channel, 0 = unbegrenzt, sonst 1 bis 999. Neues Recht `ChannelJoinFull` ("Volle Channel betreten", Bit 16). Es gilt für das eigene Betreten und fürs Verschieben: Wer jemanden in einen vollen Channel verschiebt, braucht das Recht selbst. Der Standard-Channel lässt sich nicht begrenzen, weil jeder beim Verbinden und beim Löschen eines Channels dort landet. Wird das Limit unter die aktuelle Zahl gesenkt, muss niemand gehen, nur neu hinein kommt keiner. Admins haben das Recht über `All`, andere Gruppen bekommen es nicht automatisch.
- **A33 Sortieren.** Channels: Drag and Drop im Channel-Baum und "Nach oben" bzw. "Nach unten" im Kontextmenü, Recht `ChannelEdit`, eine Anfrage mit der vollständigen neuen Reihenfolge. Gruppen: Drag and Drop und Pfeil-Buttons in der Gruppenliste der Verwaltung, Recht `GroupsManage`. Die Gruppenreihenfolge ist die Position in `ServerData.Groups` und nur Anzeige, der Rang bleibt die Teilmengenregel (A11). Gast und Admin sind frei verschiebbar.
- **A34 Links-Übersicht.** Neuer Verwaltungstab "Links" mit einer Matrix (Channel x Channel, jedes Feld ein Link) und einer Mehrfachauswahl mit "Alle ausgewählten miteinander verlinken" (jeder mit jedem, weil Links nicht transitiv sind, A5; bei 8 Channels 28 Links) und "Links zwischen den ausgewählten entfernen". Änderungen werden gesammelt, markiert und mit einer einzigen Anfrage übernommen oder verworfen. Recht `ChannelLink`. Das einzelne Verlinken per Kontextmenü bleibt.
- **A35 Passwort speichern.** Das Passwort liegt mit Windows-DPAPI (Bereich aktueller Benutzer) verschlüsselt im Lesezeichen in `settings.json`. Unter einem anderen Benutzer oder PC lässt es sich nicht entschlüsseln und gilt dann als nicht gespeichert. Die Checkbox "Passwort speichern" ist standardmässig aus und nur mit "Als Lesezeichen speichern" wählbar. Gespeichert wird erst nach erfolgreicher Verbindung.
- **A36 Lesezeichen.** Ohne Verbindung zeigt die Seitenleiste statt des Channel-Baums die Lesezeichen, der Hauptbereich nur Logo, Status und "Verbinden ...". Ein Klick verbindet sofort. Fehlt ein nötiges Passwort oder ist das gespeicherte falsch, fragt ein Overlay nach dem Passwort, mit "Passwort speichern". Kontextmenü: Verbinden, Bearbeiten, Löschen. Bearbeiten ändert Name, Adresse, Port, Nickname und das gespeicherte Passwort (ändern oder entfernen).
- **A37 Tastenliste.** Die Liste startet leer. "Tastenaktion hinzufügen" öffnet ein Overlay mit Aktion (Dropdown) und Taste (erfassen). "Ändern" öffnet dasselbe Overlay vorbelegt, "Löschen" entfernt die Zeile. Dieselbe Aktion darf auf mehreren Tasten liegen, dieselbe Taste aber nicht auf zwei verschiedenen Aktionen, sonst blockiert ein Hinweis das Speichern. Das Format in `settings.json` ist schon eine Liste, eine Migration entfällt.
- **A38 Versionierung wie in PersonalEinsatzPlanung.** Keine SemVer, sondern eine Build-Kennung aus einem UTC-Zeitpunkt: `DDMMYY.<Sekunden seit UTC-Mitternacht in Base36, 4 Stellen>`, z. B. `280926.0a1k`. Lokale Builds heissen `dev.<stamp>`. Eingebettet werden nur Rohdaten (Build-Zeit, Commit, CI ja/nein), das Formatieren passiert im Code (`OVS.Shared/BuildInfo.cs`) und ist getestet. Jeder erfolgreiche Push auf `main` erzeugt ein Release mit Tag `deploy-<sha7>`, Titel `OpenVoiceSpeak <Version>` und den Commit-Nachrichten seit dem letzten Release als Notizen. Der Workflow legt die Build-Zeit einmal fest, damit exe und Image dieselbe Version tragen.
- **A39 Öffentliches Repo.** Der Nutzer stellt das Repo vor dem ersten Push dieser Packages auf öffentlich. Update-Prüfung und `docker pull` brauchen dann keine Anmeldung. GitHub legt neue Container-Packages privat an, das Package muss nach dem ersten Push einmal von Hand auf "public" gestellt werden.
- **A40 Update im Client.** Prüfung beim Start (in den Einstellungen abschaltbar, Standard an) und per Button "Nach Updates suchen". Ein Overlay fragt "Version X ist verfügbar. Jetzt installieren?" mit den Release-Notizen. Bei Ja lädt der Client die exe aus dem Release, prüft sie gegen die mitveröffentlichte SHA-256-Datei, benennt sich selbst um, legt die neue exe an seinen Platz, startet sie und beendet sich. Die alte Datei wird beim nächsten Start gelöscht. `dev`-Builds prüfen nie. Die exe ist unsigniert, SmartScreen warnt beim ersten Start einer neuen Version. Angefragt wird nur `api.github.com` und der Download von GitHub, ohne Nutzerdaten.
- **A41 Container-Registry (ersetzt A15).** Image `ghcr.io/rezondes/openvoicespeak-server`, Tags `latest` und die Version aus A38. `docker-compose.yml` nutzt das fertige Image, der lokale Build geht über die zusätzliche `docker-compose.build.yml`.
- **A42 Protokollversion.** Nach A30 erhöhen 34, 35, 36, 37 und 38 `ProtocolInfo.Version` jeweils um eins, in der Reihenfolge der Umsetzung (geplant 4 bis 8).

**Annahmen für Package 45 bis 49** (mit dem Nutzer abgestimmt am 28.09.2026). Reihenfolge: Lokalisierung zuerst, damit die Sound-Einstellungen gleich zweisprachig entstehen, dann Sounds, zuletzt die Website (technisch unabhängig, braucht aber englische Screenshots). Zuordnung zur Anfrage: Punkt 2 = 45 und 46, Punkt 3 = 47 und 48 (jeweils auf Vorschlag geteilt), Punkt 1 = 49.

- **A43 Sprachwahl.** Standard folgt der Windows-Anzeigesprache, alles außer Deutsch fällt auf Englisch zurück. In den Einstellungen: "Wie Windows / Deutsch / English". Ein Wechsel wirkt nach einem Neustart des Clients.
- **A44 Technik der Übersetzung.** Standard-`.resx` mit stark typisierter Klasse: `Strings.resx` (Deutsch, neutrale Sprache) und `Strings.en.resx`. XAML nutzt `{x:Static}`. Ein Test prüft, dass beide Dateien dieselben Schlüssel haben und kein Text leer ist. Die Tests laufen fest mit deutscher Kultur, damit die bestehenden deutschen Erwartungen auf jedem Rechner und in CI gelten.
- **A45 Was deutsch bleibt.** Server-Logs, Client-Log und Debug-API richten sich an Betreiber und Entwickler und bleiben deutsch. Die Detail-Texte, die der Server bei Fehlern mitschickt, zeigt der Client nicht mehr an, sondern seinen eigenen Text je Fehlercode; das Detail landet nur noch im Client-Log. Ausnahme sind Kick und Bann: Deren Detail enthält den Grund, den ein Mensch eingegeben hat, und bleibt sichtbar.
- **A46 Sounds.** Kurze Töne, im Client selbst synthetisch erzeugt (keine Dateien, keine Lizenzfragen). Sie laufen über dieselbe Audioausgabe wie die Sprache. Ereignisse: Mikrofon aus, Mikrofon an, Ton aus, Ton an, Verbunden, Getrennt, du betrittst einen Channel, jemand betritt deinen Channel, jemand verlässt ihn, du wirst vom Server stummgeschaltet, du wirst verschoben, neue Privatnachricht. Bei "Ton aus" spielen nur die Töne für das eigene Mikrofon und den eigenen Ton.
- **A47 Sounds anpassen.** Pro Sound: eigene Datei (WAV oder MP3, höchstens 5 Sekunden, wird ins Profil nach `sounds/` kopiert), Lautstärke, stumm, "Abspielen", "Zurücksetzen". Global: Gesamtlautstärke und "Alle Sounds aus". Die Windows-Dateiauswahl ist wie beim Server-Logo die erlaubte Ausnahme von A20.
- **A48 Website.** Ordner `website/`, React, Vite und TypeScript, Tests mit Vitest. Deutsch und Englisch mit Umschalter, Standard nach Browsersprache. Einseitig: Hero mit Download-Button (direkt die neueste exe über `releases/latest/download/OVS.Client.exe`) und Versionsnummer, Vorteile, für wen, Screenshots, Installation in drei Schritten mit SmartScreen-Hinweis, "Eigenen Server betreiben" mit Link zur README, Footer. Gestaltung mit dem Skill `ui-ux-pro-max`.
- **A49 Auslieferung der Website.** Eigener Job `pages` im Release-Workflow nach dem Release, damit die Seite die ausgelieferte Version zeigt. Adresse `https://rezondes.github.io/OpenVoiceSpeak/`. GitHub Pages ist im Repo bereits auf "GitHub Actions" gestellt. Pull Requests bauen und testen die Seite nur.
- **A51 Verstärkung beim Empfänger.** Alle eingehenden Stimmen bekommen im Mixer den festen Faktor ×2 (+6 dB, `Mixer.DefaultVoiceBoost`), vor dem Regler "Lautstärke". Das wirkt, sobald der Zuhörer aktualisiert, unabhängig von der Version des Sprechers. Die Mikrofonverstärkung beim Sender bleibt unverändert (gespeicherte Profile hätten sonst eine Migration gebraucht). Eine Pegelautomatik je Sprecher ist nicht Teil davon.
- **A52 Begrenzer statt hartem Abschneiden.** Nach Verstärkung und Lautstärke begrenzt ein weicher Begrenzer die Summe auf höchstens 0,95: Die Absenkung greift sofort und geht innerhalb von etwa 200 ms zurück, Übergänge werden innerhalb eines Frames gerampt, damit nichts knackt. Unterhalb der Schwelle bleibt das Signal unverändert.
- **A53 Stereo-Mikrofone.** `CapturePipeline.ToMono` nimmt bei mehreren Kanälen den lautesten Kanal, wenn er mindestens 6 dB (vierfache Energie) lauter ist als der Durchschnitt der übrigen, sonst weiter den Durchschnitt. So kommt ein Mikrofon, das nur auf einem Kanal liefert, nicht mehr mit halbem Pegel an, und echtes Stereo mit gleichen Kanälen bleibt wie bisher.
- **A54 Regler "Lautstärke".** Er bleibt bei 0 bis 100 %. Mehr als heute gibt es über die Grundverstärkung (A51) und je Nutzer (A55).
- **A55 Lautstärke je Nutzer.** Schieberegler 0 bis 200 % in 5er-Schritten direkt im Kontextmenü eines anderen Nutzers, mit Prozentanzeige und dem Eintrag "Auf 100 % zurücksetzen". Beim eigenen Eintrag gibt es ihn nicht. Gespeichert je Fingerprint in `ClientSettings.UserVolumes`, damit gilt die Einstellung auf allen Servern und nach einem Neustart. Gespeichert wird nur, was von 100 % abweicht. Reihenfolge der Faktoren: Nutzer-Lautstärke × Grundverstärkung × Regler "Lautstärke", danach der Begrenzer. 200 % bei einer Person sind also ×4 gegenüber vor Package 50.
- **A56 Anzeige im Channel-Baum.** Weicht die Lautstärke einer Person von 100 % ab, zeigt ein kleines Lautsprecher-Icon mit Tooltip (z. B. "Lautstärke 150 %") das an. Bei 0 % ist es der durchgestrichene Lautsprecher mit dem Tooltip "Für dich stumm". Das Ändern der Lautstärke startet keine Audiogeräte neu, es wirkt sofort im Mixer.
- **A57 Geräteliste im Hintergrund.** Teuer ist nicht das Aufzählen der Geräte (8 ms), sondern `FriendlyName` je Gerät (27 bis 49 ms, gemessen mit 18 Geräten einer Elgato Wave XLR Pro: zusammen etwa 650 ms). Die Liste wird deshalb im Hintergrund geladen, beim Start und bei jedem Öffnen der Einstellungen neu. Die Seite öffnet sofort mit der zuletzt bekannten Liste. Fehlt sie noch, zeigt das Auswahlfeld "Geräte werden geladen ...", und der Hinweis "Gerät fehlt" erscheint erst nach dem Laden.
- **A58 Gerät öffnen ohne Namensliste.** `AudioDevices.Open` holt das gespeicherte Gerät direkt über seine ID (`MMDeviceEnumerator.GetDevice`) und prüft nur, ob es aktiv ist. Fehlt es, gilt wie bisher das Standardgerät mit Hinweis. So blockieren auch "Speichern" und der Start nicht mehr.
- **A59 Selbsttest.** Button "Selbsttest starten" in den Einstellungen, Abschnitt Übertragung. Während des Tests ist man wirklich stumm und taub, bei Verbindung auch beim Server (andere sehen "Ton aus"). Danach kommt der vorherige Zustand zurück. Ohne Verbindung läuft der Test rein lokal.
- **A60 Was man im Selbsttest hört.** Live, mit etwa 60 bis 100 ms Verzögerung, mit dem Hinweis "Mit Kopfhörern testen, sonst gibt es Rückkopplung". Die eigene Stimme läuft durch Opus, die Verstärkung aus Package 50 und den eigenen Regler "Lautstärke", also etwa so, wie andere einen bei Standardeinstellungen hören. Die Übertragung folgt dem Modus: Sprachaktivierung nur über der Schwelle, Push-to-Talk nur mit gedrückter Taste. Andere Stimmen hört man nicht.
- **A61 Regler wirken sofort.** Mikrofonverstärkung, Lautstärke, VAD-Schwelle und Modus wirken auf der Einstellungsseite sofort (ohne die Geräte neu zu starten), nicht erst nach "Speichern". "Verwerfen" stellt die gespeicherten Werte wieder her. Ein anderes Gerät wird erst nach "Speichern" genutzt.
- **A62 Ende des Selbsttests.** Mit "Selbsttest beenden", beim Schliessen der Einstellungen (Speichern oder Verwerfen) und beim Trennen der Verbindung. Kein Zeitlimit.
- **A63 Channel anlegen mit allen Optionen.** `CreateChannel` bekommt `IsMuted` und `MaxUsers` (Standard `false` und 0). Der Server prüft beim Anlegen dieselben Regeln wie beim Bearbeiten (0 bis 999 Plätze). Protokollversion 9: Server und Clients werden gemeinsam aktualisiert, ältere bekommen die bekannte Meldung zur Version.
- **A64 Eigener Channel-Dialog.** `Views/ChannelDialog.axaml` mit `ChannelDialogViewModel` ersetzt `SimpleDialogs.EditChannel`. Anlegen öffnet ihn leer (kein Name, keine Beschreibung, nicht stumm, 0 Plätze = unbegrenzt), Bearbeiten mit den Daten des Channels. Unterschiede nur bei Titel und Button ("Anlegen" bzw. "Speichern"). Beim Standard-Channel bleiben die Plätze gesperrt, mit Hinweis.
- **A65 Lautstärke bis 200 %.** Nur der Regler "Lautstärke" (`ClientSettings.OutputVolume`, jetzt 0 bis 2) wird erweitert. Mit der Grundverstärkung aus Package 50 sind das bis zu ×4, der Begrenzer verhindert Übersteuern. Gesamt-Soundlautstärke und die Regler je Sound bleiben bei 0 bis 100 %.
- **A66 Chat-Töne.** Zwei neue Sounds `ServerMessage` ("Nachricht in Allgemein") und `ChannelMessage` ("Nachricht im Channel"), standardmässig derselbe Ton wie `PrivateMessage`. Jeder hat wie alle Sounds eine eigene Zeile (Datei, Lautstärke, Stumm). Ton nur für Nachrichten anderer, immer, auch wenn der Tab offen ist.
- **A67 Ton für Link-Sprache.** Neuer Sound `LinkVoice` ("Sprache über Link"): ein kurzer, leiser Ton, sobald jemand aus einem anderen Channel per Link-PTT zu sprechen beginnt, erneut nach einer Pause ab etwa 300 ms. Kein Dauerton, kein Ton für die eigene Link-PTT, keiner bei "Ton aus" (A46). Eigene Datei, Lautstärke und Stumm wie alle Sounds.
- **A68 Reihenfolge der Einstellungen.** Geräte, Lautstärke, Übertragung, Tasten, Sounds, Darstellung, Über.
- **A69 Standard-PTT-Taste.** Nur ein neues Profil (keine `settings.json`) bekommt eine PTT-Taste: Maustaste 4, wenn Windows mindestens 5 Maustasten meldet (`GetSystemMetrics(SM_CMOUSEBUTTONS)`), sonst Strg rechts. Bestehende Profile ohne PTT-Taste bleiben so. Package 29 ("neues Profil ohne Belegung") wird damit für die PTT-Taste abgelöst.
- **A70 Hinweis auf fehlende PTT-Taste.** Unter "Übertragung", solange Push-to-Talk gewählt und keine PTT-Taste belegt ist, mit einem Button "Taste festlegen", der den Tasten-Dialog mit Push-to-Talk vorausgewählt öffnet. Der Hinweis unter dem eigenen Namen bleibt.
- **A71 Ton für die eigene Link-PTT.** Neuer Sound `OwnLinkVoice` ("Eigene Sprache über Link"), standardmässig derselbe Ton wie `LinkVoice`, mit eigener Zeile (Datei, Lautstärke, Stumm). Er spielt nur lokal, bei jedem Beginn der eigenen Link-Übertragung, mit derselben 300-ms-Pausenregel wie in Package 57. Nur wenn die Stimme wirklich über Link geht: Recht vorhanden (sonst sendet Link-PTT nur in den eigenen Channel) und der eigene Channel ist mit mindestens einem anderen verlinkt. Kein Ton, wenn nichts gesendet wird (stumm, "Ton aus", stummer Channel, Selbsttest). Das ersetzt "kein Ton für die eigene Link-PTT" aus A67.
- **A72 Machbarkeit durchsichtiger Hintergrund (geprüft).** Wegwerf-Test mit Avalonia 11.3 unter Windows 11 (Build 26200), Fenster ohne Rahmen wie der Client: `Transparent`, `AcrylicBlur` und `Mica` werden gewährt. Klicks auf völlig durchsichtige Stellen (0 %) bleiben im Fenster, weil Avalonia über DirectComposition zeichnet (`WS_EX_NOREDIRECTIONBITMAP`, kein Layered Window). Windows 10 kann `Transparent` und `AcrylicBlur`.
- **A73 Was durchsichtig wird.** Die grossen Flächen: Fensterhintergrund, Titelleiste, Seitenleiste mit Fusszeile, Chat-Bereich und die Seiten (Einstellungen, Verwaltung), also die Pinsel `Ovs.Bg`, `Ovs.Sidebar` und `Ovs.SidebarFooter`. Karten, Eingabefelder, Menüs, Dialoge und Tooltips bleiben deckend. Dialoge bekommen dafür eigene Pinsel (`Ovs.DialogBg`, `Ovs.DialogBar`), weil sie heute `Ovs.Bg` und `Ovs.Sidebar` mitbenutzen. Text, Icons und Buttons bleiben immer voll deckend.
- **A74 Regler und Weichzeichnen.** Unter Darstellung: Regler "Deckkraft des Hintergrunds" 0 bis 100 %, Standard 100 %, und Checkbox "Hintergrund weichzeichnen" (Acrylic, Standard aus). Beide wirken sofort, "Verwerfen" stellt die gespeicherten Werte wieder her (wie A61). Bei 100 % ohne Weichzeichnen bleibt das Fenster ein ganz normales, undurchsichtiges Fenster ohne Transparenzmodus. 0 % ist erlaubt (A72).
- **A75 Update-Fortschritt.** Karte mittig über dem abgedunkelten ganzen Fenster, die alle Eingaben abfängt (Titelleiste ausgenommen): Titel "Update auf {Version}", Balken mit Prozent und "12,3 / 48,0 MB". Schickt GitHub keine Dateigrösse, dreht sich der Balken ohne Prozent. Danach "Wird geprüft ..." und "Die neue Version startet ...". Kein Abbrechen-Knopf. Schlägt etwas fehl, verschwindet die Karte und der Fehler erscheint wie bisher. Die Verbindung und die Sprache laufen während des Downloads weiter.
- **A76 Aufteilung.** Der Punkt "Update" aus der Anfrage wurde in 62 (Anzeige) und 63 (Neustart, Aufräumen) geteilt. Beide berühren `UpdateInstaller`, sind aber unabhängig voneinander umsetzbar.
- **A77 Neustart-Befund.** Log vom 29.09.2026 10:16: "Update auf 290926.0lh6 wird installiert", danach beim alten Client weder "Client beendet" noch ein Fehler. Das Fenster ging laut Nutzer einfach zu, der neue Client kam nicht, und um 10:18 wurde er von Hand gestartet. Der Client hat keinen Handler für unbehandelte Ausnahmen, ein Absturz hinterlässt also keine Spur. Das Muster passt zu einem Absturz im Neustart-Callback in `App.axaml.cs` (dort ist `Process.Start` ungeschützt, und er läuft im `async void`-Handler von `window.Opened`). **Befund (reproduziert mit einer alt datierten CI-exe und dem echten Release):** `FileNotFoundException: System.Diagnostics.Process` im Neustart-Callback. Die Single-File-exe lädt Assemblies erst beim ersten Gebrauch aus ihrer eigenen Datei, und die heisst nach dem Tausch schon `*.old`. Der Absturz beendet den Prozess, bevor der neue gestartet ist. Danach hält Windows die abgestürzte exe noch minutenlang gesperrt (kein sichtbarer Prozess, Löschen gibt "Zugriff verweigert"), deshalb blieb auch `.old` liegen. Nach einem sauberen Ende ist sie sofort löschbar. Fix: Die Startdaten werden beim Programmstart gebaut, solange die exe noch am Platz ist. Unbehandelte Ausnahmen des UI-Threads landen über `AppDomain.UnhandledException` im Log.
- **A78 Aufräumen der alten exe.** Die neue Version bekommt `--after-update <pid>` mit. Sie wartet höchstens 10 Sekunden, bis dieser Prozess weg ist, und löscht dann `OVS.Client.exe.old`, mit ein paar Versuchen (Virenscanner halten frische Dateien kurz fest). Jeder normale Start räumt weiterhin auf wie bisher. Was nicht klappt, wird mit Grund geloggt statt geschluckt.
- **A79 Eigene Nachrichten.** Rechts, als Blase mit Akzent-Hintergrund, ohne Avatar, ohne Namen und ohne "Du", nur mit der Uhrzeit. Die Blase ist höchstens etwa 75 % so breit wie der Verlauf. Nachrichten anderer bleiben links mit Avatar, Name und Zeit, der Text steht in einer neutralen Blase (`Ovs.Surface`). Hinweise, Willkommensnachricht und Marker bleiben unverändert über die volle Breite bzw. mittig. Gilt in allen Tabs (Allgemein, Channel, privat).
- **A80 Aufteilung.** "Nutzerübersicht" wurde in 71 (Anzeige, Suche, Filter) und 72 (Bannen, Entbannen, Löschen) geteilt, "Backup" in 74 (auf dem Server) und 75 (Download und Upload). Die Zeile "9**f" in der Anfrage wurde als Tippfehler ignoriert.
- **A81 Breite der Seitenleiste.** Beim Verbinden wird sie auf den längsten Channel-Namen samt Icons und Nutzerzahl gesetzt, mindestens 240 px. Diese Breite ist die Mindestbreite, begrenzt durch A82. Zieht der Nutzer sie breiter, gilt das bis zum nächsten Verbinden. Die Breite wird nicht gespeichert.
- **A82 Kopfzeile hat Vorrang.** Der Hauptbereich ist nie schmaler als Voice-Icon, Ping, alle sichtbaren Buttons und Innenabstand. Der Channel-Titel darf gekürzt werden oder verschwinden. Reicht das Fenster nicht, gibt die Seitenleiste bis 200 px nach, unter 700 px wird sie einblendbar (A93).
- **A83 Link-Icon.** Kleines `Icon.Link` (Klasse `link`) hinter dem Namen neben dem Home-Icon, Tooltip "Verlinkt mit ..." mit allen Partnern. Der Tooltip der ganzen Zeile bleibt.
- **A84 Stummer Channel.** Vorne `Icon.MicOff` in Warnfarbe statt des Lautsprechers, mit Tooltip "Stummer Channel: niemand wird gehört". Hinter dem Namen kein Stumm-Icon mehr.
- **A85 Einstellungen in der Verwaltung.** Neu änderbar: maximale Nutzer, Log-Aufbewahrung, tägliche Log-Datei, automatischer Neustart mit Uhrzeit. Sie wirken sofort und werden in `server-data.json` gespeichert. Umgebungsvariablen und `server-config.json` liefern dafür nur noch Startwerte (einmalige Übernahme beim Update auf Datenversion 3), wie heute schon Name und Passwort. `OVS_PORT` und `OVS_DATA_DIR` bleiben in der Umgebung, weil sie zum Container gehören (Port-Mapping, Volume).
- **A86 Nutzerdaten.** Zusätzlich zu `FirstSeen`: letzter Login, Anzahl Logins, gesamte Online-Zeit, letzte IP, bis zu 5 frühere Nicknames, Sprechzeit (weitergeleitete Sprache, 20 ms je Paket) und Anzahl Chatnachrichten. Gespeichert beim Login und beim Trennen bzw. Herunterfahren, nicht laufend. Die IP ist ein personenbezogenes Datum und wird nur Nutzern mit Zugriff auf die Nutzerübersicht gezeigt.
- **A87 Nutzerübersicht.** Sichtbar nur mit dem neuen Recht `UsersView` (Package 76), standardmässig nur für Admin. Gruppen ändern nur mit `GroupsAssign`. Suche über Nickname, frühere Nicknames, Fingerabdruck und IP. Filter nach Status und Gruppe, Sortierung nach Name, letztem Login oder Online-Zeit.
- **A88 Nutzer löschen.** Eigenes Recht `UserDelete` (Package 76) und nur bei schwächeren Nutzern, nie sich selbst oder den letzten Admin. Gelöscht werden Nutzerdatensatz, Gruppen, Statistiken und alle Bans mit dem Fingerabdruck. Die Logdateien bleiben und laufen über ihre Aufbewahrungszeit aus. Das Löschen selbst wird geloggt. Ein Online-Nutzer wird mit eigener Meldung getrennt.
- **A89 Gruppen-Töne.** `GroupChanged` für den Betroffenen (bei Änderung der eigenen Gruppen, nicht beim Verbinden) und `GroupChangedByMe` für den Handelnden (wenn die nächste Nutzerliste die Änderung zeigt, auch bei Offline-Nutzern). Standardmässig derselbe neue Ton, einzeln einstellbar wie `LinkVoice`/`OwnLinkVoice`. Bei der eigenen Gruppe nur ein Ton.
- **A90 Backups.** Zip-Archiv `.ovsbackup` mit Manifest, `server-data.json`, `cert.pfx` (damit der Fingerabdruck gleich bleibt) und Logo, ohne Logs. Auf dem Server in `/data/backups/`. Wiederherstellen legt vorher ein Sicherheits-Backup an, trennt alle und startet den Lauf im Prozess neu. Recht `ServerConfig`. Keine automatischen, zeitgesteuerten Backups.
- **A91 Backup-Übertragung.** Über die bestehende Steuerverbindung in Stücken von 512 KB (Nachrichten sind höchstens 1 MiB), Download Stück für Stück auf Anfrage, Upload höchstens 50 MB. Kein HTTP und kein zweiter Port.
- **A92 Einzelne Rechte.** Neu: `UsersView`, `BansView`, `GroupsView`, `GroupsCreate`, `GroupsDelete`, `UserDelete`. Schon vorhanden und weiter genutzt: `UserKick` (kicken), `UserBan` (bannen und entbannen), `GroupsAssign` (Gruppe eines Nutzers ändern), `GroupsManage` (heisst jetzt "Gruppen bearbeiten": Name, Rechte, Reihenfolge). Sehen und Handeln sind getrennt. Standard: Admin alles, Moderator zusätzlich `BansView`. Bestehende Gruppen bekommen beim Update die Sehen-Rechte zu den Rechten, die sie schon haben, damit niemand etwas verliert. `UserDelete` bekommt nur Admin.
- **A93 Responsives System.** Ziel ist, dass der Client später auch auf einem Handy laufen kann. Mindestgrösse des Fensters 360 × 480 px. Zwei Stufen als Klassen am Fenster: `compact` (Fenster unter 700 px) und `narrow` (Hauptbereich unter 560 px). Seiten reagieren per Stil darauf, nicht per Code.
- **A94 Einblendbare Seitenleiste.** Unter 700 px ist die Seitenleiste weg und wird über einen Menü-Button oben links als Überlagerung von links eingeblendet (wie Discord oder Slack mobil). Sie schliesst beim Channel-Wechsel, bei Klick daneben und mit Esc.
- **A95 Schmale Kopfzeile.** Unter `narrow` zeigen Verwaltung, Trennen, Admin-Token und Ping nur ihr Icon, der Text steht im Tooltip und im Namen für Screenreader.
- **A96 Responsive auch für neue UI.** Alle Packages, die Oberfläche hinzufügen oder ändern (65 bis 79), halten sich an A93. Wer nach Package 68 umgesetzt wird, prüft seine neuen Bereiche zusätzlich mit `LayoutAssert.FitsHorizontally` bei 360 px. Wer davor umgesetzt wird, wird von 77 bis 79 mit abgedeckt.
- **A97 Ban details and history.** New per ban: creation time, creator fingerprint, original duration, IP flag, lifted by/at, blocked join attempts with time and IP of the last one. Lifted and expired bans stay as history (status filter, default "active") and are removed after the log retention period (`LogDays`, 0 = forever) or with the user's data. Attempts are saved at most once per minute per ban. Old bans show "unknown" for new fields.
- **A98 Log viewer.** New tab "Logs" in the administration with right `LogsView` (only Admin by default). Server and channel logs, listed newest first with type, channel name, start and size. Opened page by page (1000 lines, last page first) with a line filter. Search runs on the server over all files (plain text, case-insensitive, optional type and period filter), at most 500 hits, stops after 5 s. Files are addressed by listing id only, never by client path. Reading happens outside the state lock.
- **A99 Log download.** Right `LogsDownload` (only Admin by default; needs `LogsView` to see the tab). One file as `.log` or a selection as `.zip` with folders `server/` and `channels/<name>_<id>/`, built from a snapshot in `<DataDir>/logs-export/`, at most 200 MB, transferred in 512 KB chunks like backups.
- **A100 Language.** From Package 80 on, plan texts and commit messages are in English at the user's request. UI texts stay bilingual (German and English resources).
- **A101 Backups.** New right "Backups verwalten" for list, create, download, delete; groups with `ServerConfig` get it once on update. Upload and restore only for members of the Admin group. The certificate stays in backups so a server move keeps its fingerprint; the download warns that the file holds the private key and user data.
- **A102 Rank rule.** Actions on other users need strictly more rights than the target (strict subset). Equal ranks, including two admins, cannot act on each other; nobody can act on themselves; the last Admin-group member is always protected.
- **A103 Limits.** Concrete numbers in Packages 86 to 88 (request budget 20/s burst 40, outbox 8 MB, write timeout 10 s, 64 pending handshakes, 10 new identities per IP per hour, lists paged by 200, 50 backups / 2 GB) are starting values; they live as constants in one place so they can be tuned.
- **A104 Public data.** Fingerprints stay visible to all clients (hashes of public keys, needed for private chat and per-user volume). Permission bits of other users and groups are sent only to `GroupsView` holders (Package 92).
- **A105 Not changed by design.** There is no per-channel join or listen right; anyone can join and hear any channel. Fingerprint bans are avoided by a new key pair; only IP bans help there. Opus payloads are relayed unchecked; clients must tolerate malformed frames.
- **A50 Screenshots.** Echte Bilder des headless gerenderten Clients, je Sprache, einmal erzeugt und in `website/public/screenshots/` eingecheckt. Der Nutzer kann eigene nachreichen, die gleichnamig ersetzt werden.

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

Alle Packages 1 bis 61 sind umgesetzt. Die Tests laufen mit `dotnet test` und `cd website && npm test` grün, der Build hat 0 Warnungen. Offen sind nur manuelle Acceptance Criteria: Package 16 AC9, 17 AC7 und 27 AC4 brauchen ein Headset, einen Blick auf den Bildschirm bzw. echte Fensterbedienung (siehe Tabelle der manuellen Checks). Package 42 AC6, 43 AC6 und 44 AC5 lassen sich erst nach dem Push auf das öffentliche Repo prüfen: erster Workflow-Lauf, ein Update von einem Release auf das nächste, `docker pull` ohne Anmeldung (vorher das Container-Package einmal auf "public" stellen). Package 50 AC6, 51 AC6, 53 AC7, 57 AC5, 60 AC6 und 61 AC6 (echtes Fenster, headless zeichnet auf Schwarz) brauchen einen Test mit echten Clients (53 mit Kopfhörern). Package 49 AC6 ebenso: die Seite unter `https://rezondes.github.io/OpenVoiceSpeak/` mit Download und Lighthouse-Wert.

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
| 31 | `ServerLogsTests > "Chat_Logged_PrivateWithoutContent"` | `ChatTests > "Logs_ServerAndChannel_PrivateWithoutContent"` | Der Test braucht drei verbundene Clients, die Hilfen dafür liegen in `ChatTests`. |
| 32 | AC8 (Scrollverhalten) als manueller Check | `UiSmokeTests > "Chat_FollowsNewLines_UnlessScrolledUp"` | Headless mit Skia lässt sich das Scrollen verlässlich prüfen. |
| 32 | "Allgemein" zeigt Meldungen ab dem Verbinden | "Allgemein" übernimmt beim Verbinden auch die Meldungen davor | So geht z. B. eine Geräte-Warnung vom Start nicht verloren, wie früher in der Aktivität. |
| 49 | `sections/*.tsx`, `styles.css`, Screenshots als PNG | alle Abschnitte in `App.tsx`, Stil in `index.css`, Screenshots als WebP (je Sprache hell und dunkel), Icons aus den Fluent-Pfaden des Clients (`icons.ts`) | eine kleine Seite braucht keine Aufteilung, WebP ist etwa ein Drittel so gross. In der Galerie steht der Privatchat statt einer Wiederholung des Hero-Bilds. |
| 50 | AC3: höchstens 1 % der Proben auf der Grenze | höchstens 5 % | Auch ohne Abschneiden liegen bei einem Sinus die Proben um jeden Scheitel nah an der Grenze, 1 % ist dafür zu knapp. Hartes Abschneiden läge bei etwa 40 %. |
| 51 | Test 4 prüft, dass `Configure` nicht erneut läuft | prüft, dass das `Settings`-Objekt dasselbe bleibt (nur `ApplySettings` ersetzt es und startet die Geräte neu) | `AudioEngine` zählt keine Aufrufe von `Configure`, ein Zähler nur für den Test lohnt sich nicht. |
| 53 | Test 4 `Sliders_ApplyLive_DiscardRestores` | `SettingsTests > "Sliders_ApplyLive"` prüft die Vorschau, das Zurückstellen beim Verwerfen prüft `MainViewModelTests > "SelfTest_MutesAndDeafens_RestoresPreviousState"` (Lautstärke im Mixer) | Das Zurückstellen macht das `MainViewModel`, nicht die Einstellungsseite. |
| 54 | Anbindung in `MainWindow.axaml.cs`, README mit Protokollversion | Anbindung in `App.axaml.cs` (dort entstehen alle Dialoge), README nennt Anlegen mit Optionen | Die `Dialogs` werden in `App.axaml.cs` gesetzt. Die README nennt keine Protokollversion. `SimpleDialogs.Show` ist jetzt `internal`, damit der eigene Dialog denselben Rahmen nutzt. `Dialogs.EditChannel` bekommt keinen Titel mehr, den legt der Modus fest. |
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
| 27, AC4 | **Offen: Aero Snap, Windows 10 und zwei Monitore.** Auf Windows 11 geprüft: Titelleiste per PrintWindow normal und maximiert ohne abgeschnittenen Inhalt, Grössenändern an allen Rändern per `WM_NCHITTEST`. |
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
4. `pttDown`: Target 0. **Geändert in Package 25:** nur noch im PTT-Modus, bei Sprachaktivierung wirkt die PTT-Taste nicht.
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

---

## Package 24: Moderne Oberfläche

**Ziel:** Der Client sieht aus und bedient sich wie eine aktuelle Voice-App, in einer hellen und einer dunklen Variante.

**Abhängigkeiten:** Package 16, 17, 18

**Betroffene Dateien:**
- `src/OVS.Client/Styles/Theme.axaml` (neu): Design-Tokens für hell und dunkel (ThemeDictionaries)
- `src/OVS.Client/Styles/Controls.axaml` (neu): Textrollen, Karten, Zeilen, Avatare, Chips, Button-Varianten
- `src/OVS.Client/Styles/Icons.axaml` (neu): Fluent UI System Icons als Pfaddaten, `THIRD-PARTY-NOTICES.md` (neu) mit der MIT-Lizenz
- `src/OVS.Client/App.axaml`, `App.axaml.cs` (ändern): FluentTheme mit Markenfarbe, Design-Einstellung anwenden
- `src/OVS.Client/Views/MainWindow.axaml`, `SettingsDialog.axaml`, `AdminDialog.axaml`, `SimpleDialogs.cs` (neu gestaltet), `Converters.cs` (neu)
- `src/OVS.Client/ViewModels/MainViewModel.cs`, `ServerViewModel.cs`, `SettingsViewModel.cs`, `Settings/ClientSettings.cs` (ändern)
- `tests/OVS.Tests/Client/UiSmokeTests.cs` (neu), `MainViewModelTests.cs`, `ServerViewModelTests.cs`, `SettingsTests.cs` (ändern), Paket `Avalonia.Headless.XUnit` nur im Testprojekt

### Kontext

Vorher: eine Zeile gleich aussehender Text-Buttons, ein Baum aus reinem Text, Status nur als "(stumm)" in Klammern, Meldungen als graue Liste mit Zeitstempel, ohne Verbindung eine leere Fläche.

Grundlage waren die Empfehlungen des Skills ui-ux-pro-max: Palette "Chat & Messaging" (blaue Primärfarbe, Grün für aktiv), dunkler Modus mit heller Variante, FluentTheme als Basis mit ThemeDictionaries und DynamicResource, Namen für Icon-Buttons (AutomationProperties.Name). Die Stil-Empfehlung der ersten Suche (3D, Landingpage) passte nicht zu einer Desktop-App und wurde verworfen.

**Aufbau:**
- Linke Seitenleiste: Server-Kopf (Initial-Kachel, Name, Verbindungsstatus mit Punkt und Text, Channel anlegen), Channel-Baum, unten das eigene Profil mit Mikrofon, Ton aus und Einstellungen als Icon-Buttons.
- Channels als Zeilen mit Lautsprecher-Icon, Standard-Channel-Icon, Link-Chip und Nutzerzahl, der eigene Channel hervorgehoben.
- Nutzer mit Initial-Avatar (feste Farbe pro Nickname), Sprech-Ring grün, über Link violett plus Link-Icon (nicht nur Farbe), Status als Icons mit Tooltip.
- Rechts: Kopf mit aktuellem Channel, Ping-Chip, Verwaltung, Admin-Token, Trennen (rot). Darunter "Aktivität" mit Icons je Art (Willkommen, Warnung, Fehler).
- Ohne Verbindung: Startbildschirm mit Verbinden, Ladeanzeige während des Verbindens und Lesezeichen als Kacheln, die den Dialog vorbelegt öffnen.
- Dialoge: Titel mit Icon, Beschriftung über jedem Feld, blauer Haupt-Button, roter Button für Bannen, Löschen und geänderte Zertifikate. Einstellungen in Abschnitten, Modus als Radiobuttons, Tasten als Tastenkappen, neue Einstellung "Design".

**Kontrast:** Text mindestens 4.5:1, Ringe und Icons mindestens 3:1, in beiden Varianten nachgerechnet. Das Grün der hellen Variante wurde dafür auf #15803D abgedunkelt.

**Icons:** Fluent UI System Icons (MIT) als Pfaddaten statt eines weiteren Pakets, passend zum Fluent-Stil.

### Acceptance Criteria

- [x] AC1: Hauptfenster, Einstellungen, Verwaltung und alle kleinen Dialoge laden in heller und dunkler Variante ohne Fehler, Farben nur über Tokens.
- [x] AC2: Sprechen ist am Avatar-Ring erkennbar, Sprechen über Link zusätzlich am Link-Icon. Stumm, taub und vom Server stumm erscheinen als Icons.
- [x] AC3: Ohne Verbindung zeigt der Client einen Startbildschirm mit Lesezeichen, während des Verbindens eine Ladeanzeige.
- [x] AC4: Meldungen haben eine Art (Willkommen, Warnung, Fehler) und ein passendes Icon. Die Debug-API liefert sie weiter als Text.
- [x] AC5: Das Design ist wählbar (wie Windows, hell, dunkel) und wird gespeichert.
- [x] AC6: Icon-Buttons haben Tooltip und AutomationProperties.Name, deaktivierte Buttons sind sichtbar abgeschwächt.
- [x] AC7 (manuell): Screenshots beider Varianten aus dem echten Client-Code geprüft (headless mit Skia gerendert), und der echte Client startet unter Windows. Geprüft am 2026-09-27.

### Tests (TDD)

1. `UiSmokeTests > "Windows_LoadAndShowTheirContent"` für hell und dunkel: alle Fenster und kleinen Dialoge, Avalonia-Warnungen und Bindungsfehler lassen den Test scheitern (AC1)
2. `UiSmokeTests > "EveryIconInXaml_Exists"`: ein fehlendes Icon wirft in Avalonia weder eine Ausnahme noch einen Logeintrag, deshalb dieser Abgleich (AC1)
3. `ServerViewModelTests > "CurrentChannelAndSelf_FollowOwnUser"`, `"StatusIcons_MicOffOnlyForAPlainMute"` (AC2)
4. `MainViewModelTests > "StartScreen_BookmarksAndConnectingState"` (AC3), `"Notices_HaveKinds_ErrorAndDisconnect"` (AC4)
5. `SettingsTests > "Theme_DefaultSystem_SavedAndMapped"`, `"PushToTalkRadio_IsTheOppositeOfVoiceActivation"` (AC5)
6. `UiSmokeTests > "Avatar_SameNicknameSameColor_InitialUpperCase"` (AC2)

Testbefehl: `dotnet test --filter "FullyQualifiedName~UiSmokeTests|FullyQualifiedName~MainViewModelTests|FullyQualifiedName~ServerViewModelTests|FullyQualifiedName~SettingsTests"`

### Out of Scope

- Eigene Titelleiste und Mica-Hintergrund
- Animationen über kurze Zustandswechsel hinaus
- Anzeige des Client-Logs in der Oberfläche

---

## Package 25: Sprachaktivierung ohne PTT-Taste

**Ziel:** Im Modus Sprachaktivierung schaltet die PTT-Taste das Mikrofon nicht mehr frei, Link-PTT sendet weiterhin an die Links.

**Abhängigkeiten:** Package 14

**Betroffene Dateien:**
- `src/OVS.Client/Audio/SendPath.cs` (ändern): `TransmitController.Decide`
- `tests/OVS.Tests/Client/SendPathTests.cs` (ändern)

### Kontext

`TransmitController.Decide` prüft `pttDown` vor dem Modus (Regel 4 aus Package 14). Deshalb sendet die PTT-Taste auch bei Sprachaktivierung, sogar unter der Schwelle. Wer Sprachaktivierung wählt, erwartet aber, dass nur die Stimme entscheidet. Link-PTT bleibt erlaubt, weil die Sprachaktivierung nie an Links sendet (A7) und Link-PTT dafür der einzige Weg ist.

Neue Regeln in Prioritätsreihenfolge: stumm, dann Link-PTT, dann im PTT-Modus die PTT-Taste, im Modus Sprachaktivierung die VAD.

### Acceptance Criteria

- [x] AC1: Sprachaktivierung, PTT gedrückt, Pegel unter der Schwelle: es wird nichts gesendet.
- [x] AC2: Sprachaktivierung, PTT gedrückt, Pegel über der Schwelle: gesendet wird wegen der VAD an den eigenen Channel, das Loslassen der Taste ändert nichts.
- [x] AC3: Sprachaktivierung und Link-PTT: gesendet wird an Channel und Links (mit Recht `SpeakLinked`).
- [x] AC4: Im PTT-Modus verhalten sich PTT und Link-PTT wie bisher.

### Tests (TDD)

1. `SendPathTests > "Decide_Cases"`, neue Zeile `{ VoiceActivation, ptt: true, link: false, vad: false, ..., expected: null }` als Reproduktion, muss vor dem Fix rot sein (AC1)
2. Neue Zeilen für AC2 (`vad: true` mit `ptt: true` ergibt Target 0) und AC4 (bestehende PTT-Zeilen bleiben)
3. Bestehende Zeile `{ VoiceActivation, false, true, true, ..., Link }` bleibt grün (AC3)
4. `SendPathTests > "PttKey_BelowThreshold_SendsOnlyInPttMode"`: `AudioEngine` ohne Geräte, Testton (-13.5 dBFS), Schwelle -10 dBFS, `keys.Simulate(ptt: true)`, nach 400 ms hat die Sprachaktivierung 0 Frames gesendet, der PTT-Modus zur Gegenprobe mehr als 0 (AC1, AC4, Ende-zu-Ende im Client)

Testbefehl: `dotnet test --filter "FullyQualifiedName~SendPathTests"`

### Umsetzungsschritte

1. Testzeilen und Engine-Test schreiben, rot sehen.
2. In `Decide` die PTT-Regel auf `mode == TransmitMode.PushToTalk` einschränken.
3. Package 14 im Plan mit einem Hinweis auf die geänderte Regel 4 versehen.

### Out of Scope

- Neue Tastenaktionen wie Push-to-Mute (Package 29)

---

## Package 26: Alles in einem Fenster

**Ziel:** Einstellungen, Verwaltung und alle Dialoge erscheinen im Hauptfenster, der Client öffnet kein weiteres Fenster.

**Abhängigkeiten:** Package 24

**Betroffene Dateien:**
- `src/OVS.Client/Views/SettingsDialog.axaml(.cs)` wird zu `Views/SettingsView.axaml(.cs)` (UserControl)
- `src/OVS.Client/Views/AdminDialog.axaml(.cs)` wird zu `Views/AdminView.axaml(.cs)` (UserControl)
- `src/OVS.Client/Views/OverlayHost.cs` (neu): modale Ebene im Fenster
- `src/OVS.Client/Views/SimpleDialogs.cs` (ändern): baut Inhalte für die Ebene statt Fenster
- `src/OVS.Client/Views/MainWindow.axaml(.cs)` (ändern): Seitenwechsel, Overlay-Ebene
- `src/OVS.Client/ViewModels/MainViewModel.cs` (ändern): aktuelle Seite
- `src/OVS.Client/App.axaml.cs` (ändern): `Dialogs` und `ConfirmTofu` auf die Ebene verdrahten
- `tests/OVS.Tests/Client/UiSmokeTests.cs`, `MainViewModelTests.cs` (ändern)

### Kontext

Heute öffnen `MainWindow.OnSettingsClick` und `OnAdminClick` eigene Fenster per `ShowDialog`, ebenso die acht Dialoge in `SimpleDialogs` (Verbinden, Zertifikat, Bannen, Bestätigen, Channel bearbeiten, Channel wählen, Text, Admin-Token). Das widerspricht A20.

**Seiten:** `MainViewModel.Page` (`Home`, `Settings`, `Admin`). Einstellungen und Verwaltung ersetzen den Hauptbereich rechts, die Seitenleiste bleibt sichtbar. Ein Kopf mit Titel und Schliessen-Button, Esc schliesst ebenfalls.

**Overlay:** `OverlayHost` legt einen abgedunkelten Hintergrund über das ganze Fenster und zeigt darauf eine Karte mit Titel, Inhalt und Button-Leiste (die heutige Gestaltung aus `SimpleDialogs`). Darunter ist nichts klickbar, der Fokus bleibt in der Karte. `SimpleDialogs` behält seine Signaturen (`Task<T?>`), bekommt aber statt `Window owner` den `OverlayHost`. Mehrere Anfragen werden nacheinander gezeigt.

### Acceptance Criteria

- [x] AC1: "Einstellungen" öffnet eine Seite im Hauptbereich. Speichern, Abbrechen, Schliessen und Esc kehren zurück, die Pegelanzeige läuft wie bisher.
- [x] AC2: "Verwaltung ..." öffnet eine Seite im Hauptbereich. Beim Trennen der Verbindung schliesst sie sich.
- [x] AC3: Die acht kleinen Dialoge und die Zertifikatsabfrage beim Verbinden erscheinen als Overlay mit abgedunkeltem Hintergrund. Esc bricht ab, Enter löst den Standard-Button aus, der Fokus steht im ersten Feld, Klicks auf den Hintergrund lösen nichts darunter aus.
- [x] AC4: Bei keinem Ablauf öffnet der Client ein zweites Fenster.
- [x] AC5: Beide Designs, Tastatur (Tab bleibt im Overlay) und Screenreader-Namen funktionieren wie in Package 24.

### Tests (TDD)

1. `UiSmokeTests > "NoSecondWindow_EverOpens"`: ein Klassen-Handler auf `Window.WindowOpenedEvent` zählt Fenster. Gegeben das Hauptfenster, dann Einstellungen, Verwaltung und alle acht Dialoge öffnen und schliessen. Erwartet: genau ein geöffnetes Fenster (AC1 bis AC4). Vor dem Umbau rot.
2. `UiSmokeTests > "Overlay_EscCancels_EnterConfirms"`: Bestätigen-Dialog, Esc liefert `false`, Enter `true` (AC3)
3. `MainViewModelTests > "Pages_OpenAndClose_LevelMeterRuns"`: `OpenSettings` setzt `Page = Settings`, Schliessen setzt `Home`, `OpenAdmin` nur mit `CanAdminister`, Trennen führt zurück auf `Home`, die Pegelanzeige läuft, Speichern übernimmt die Einstellungen (AC1, AC2)
4. Bestehende `UiSmokeTests` nutzen statt `OwnedWindows` die Overlay-Ebene (AC5)

**Umsetzungsnotizen:**
- Esc und Enter behandelt die Overlay-Ebene im Tunnel des Fensters, damit sie auch ankommen, wenn der Fokus woanders liegt. Enter in einem mehrzeiligen Feld bleibt ein Zeilenumbruch, ein fokussierter Button löst sich selbst aus.
- Den ersten Fokus bekommt das erste Eingabefeld, sonst der Button, den Enter auslöst. Bei einem geänderten Zertifikat ist das "Abbrechen".
- Die Radiobuttons für den Sendemodus haben keinen `GroupName` mehr. Mit Gruppenname koppelt Avalonia sie über Views hinweg, zwei Einstellungsseiten auf demselben ViewModel schalteten sich dann endlos gegenseitig um (gefunden beim Rendern der Screenshots).

Testbefehl: `dotnet test --filter "FullyQualifiedName~UiSmokeTests|FullyQualifiedName~MainViewModelTests"`

### Umsetzungsschritte

1. Test 1 schreiben, rot sehen.
2. `OverlayHost` bauen, `SimpleDialogs` darauf umstellen, App-Verdrahtung anpassen.
3. Einstellungen und Verwaltung zu UserControls machen, Seitenwechsel im `MainViewModel`.
4. Alte Fensterklassen löschen, Tests 2 bis 4 grün.

### Out of Scope

- Eigene Titelleiste (Package 27)
- Der Windows-Dateidialog für das Server-Logo (Package 30, A20)

---

## Package 27: Eigener Fensterrahmen

**Ziel:** Das Hauptfenster hat eine eigene Titelleiste im App-Design statt des Windows-Rahmens.

**Abhängigkeiten:** Package 26

**Betroffene Dateien:**
- `src/OVS.Client/Views/TitleBar.axaml(.cs)` (neu)
- `src/OVS.Client/Views/MainWindow.axaml(.cs)` (ändern): `ExtendClientAreaToDecorationsHint`, `ExtendClientAreaChromeHints="NoChrome"`
- `src/OVS.Client/Styles/Controls.axaml` (ändern): Buttons der Titelleiste
- `src/OVS.Client/Styles/Icons.axaml` (ändern): Minimieren, Maximieren, Wiederherstellen, Schliessen aus den Fluent UI System Icons
- `tests/OVS.Tests/Client/UiSmokeTests.cs` (ändern)

### Kontext

Seit Package 24 hat der Inhalt ein eigenes Design, der Rahmen ist aber der von Windows. Avalonia erlaubt, den Inhalt in den Rahmen zu erweitern und den System-Rahmen auszublenden, die Ränder zum Grössenändern bleiben. Die Titelleiste (36 px) zeigt links das Logo (bis Package 28 ein Platzhalter), den Namen "OpenVoiceSpeak" bzw. den Servernamen und rechts die drei Buttons. Maximiert muss der Inhalt den unsichtbaren Rand ausgleichen (`OffScreenMargin`), sonst wird er abgeschnitten.

### Acceptance Criteria

- [x] AC1: Der Windows-Rahmen ist nicht sichtbar, die eigene Titelleiste zeigt Logo, Titel und drei Buttons in beiden Designs.
- [x] AC2: Minimieren, Maximieren bzw. Wiederherstellen (das Icon wechselt) und Schliessen funktionieren. Schliessen färbt sich beim Hovern rot.
- [x] AC3: Ziehen an der Leiste verschiebt das Fenster, Doppelklick maximiert bzw. stellt wieder her.
- [ ] AC4 (manuell): Aero Snap an den Bildschirmrand, Grössenändern an allen Rändern, Mindestgrösse, maximiert auf Windows 10 und 11 ohne abgeschnittenen Inhalt, zwei Monitore mit unterschiedlicher Skalierung.
  - Geprüft am 27.09.2026 auf Windows 11 am echten Client: Ränder und Ecken melden per `WM_NCHITTEST` die Zonen zum Grössenändern, maximiert liegt der Inhalt dank `OffScreenMargin` vollständig im sichtbaren Bereich (per `PrintWindow` aufgenommen).
  - **Offen:** Aero Snap per Ziehen, Windows 10, zwei Monitore mit unterschiedlicher Skalierung. Das Verschieben nutzt die native Schleife von Windows (`BeginMoveDrag`), Snap sollte deshalb funktionieren.
- [x] AC5: Die Buttons haben Tooltip und `AutomationProperties.Name`.

### Tests (TDD)

1. `UiSmokeTests > "TitleBar_ButtonsChangeWindowState_ShowsTitleAndServer"`: Gegeben das Hauptfenster headless. Klick auf Maximieren setzt `WindowState.Maximized` und wechselt das Icon, erneut `Normal`, Minimieren setzt `Minimized` (AC1, AC2)
2. `UiSmokeTests > "Windows_LoadAndShowTheirContent"` prüft zusätzlich, dass die Titelleiste den Servernamen zeigt (AC1)
3. Manueller Check AC4 mit Eintrag in der Tabelle der manuellen Checks

Testbefehl: `dotnet test --filter "FullyQualifiedName~UiSmokeTests"`

### Umsetzungsschritte

1. Test 1 schreiben, rot sehen.
2. `TitleBar` bauen, Hauptfenster erweitern, Buttons und Ziehen verdrahten, Rand bei Maximiert ausgleichen.
3. Manuelle Checks auf Windows ausführen, Screenshots beider Designs prüfen.

### Out of Scope

- Mica- oder Acrylic-Hintergrund
- Snap-Layouts von Windows 11 (A22)

---

## Package 28: App-Logo

**Ziel:** OpenVoiceSpeak hat ein eigenes Logo, das als Programm-, Taskleisten- und Fenstersymbol, in der Titelleiste und auf dem Startbildschirm erscheint.

**Abhängigkeiten:** Package 27

**Betroffene Dateien:**
- `src/OVS.Client/Assets/logo.svg` (neu): Vorlage des Logos
- `src/OVS.Client/Assets/ovs.ico` (neu): 16, 24, 32, 48, 64 und 256 px
- `src/OVS.Client/Styles/Icons.axaml` (ändern): `Logo.Mark` als Pfad für Titelleiste und Startbildschirm
- `src/OVS.Client/OVS.Client.csproj` (ändern): `ApplicationIcon`, `AvaloniaResource`
- `src/OVS.Client/Views/MainWindow.axaml`, `TitleBar.axaml` (ändern)
- `README.md` (ändern): Logo oben
- `tests/OVS.Tests/Client/LogoTests.cs` (neu), `UiSmokeTests.cs` (ändern)

### Kontext

Die Anwendung hat kein Logo. Die exe zeigt das Standard-Icon von .NET, der Startbildschirm ein Headset-Icon, die Titelleiste aus Package 27 einen Platzhalter. Entwurf nach A23. Das Zeichen muss auch mit 16 px erkennbar bleiben und sich in beiden Designs vom Hintergrund abheben (mindestens 3:1).

### Acceptance Criteria

- [x] AC1 (manuell): Zwei bis drei Entwürfe wurden als Bild gezeigt, der Nutzer hat einen gewählt.
  - Umsetzung am 27.09.2026: Drei Entwürfe (A Headset, B Sprechblase, C Funk) mit Avalonia gerendert, je 256, 64, 32 und 16 px auf hellem und dunklem Grund. Weil der Nutzer ohne weitere Rückfragen arbeiten liess, wurde A gewählt: als Voice-App sofort erkennbar und auch mit 16 px noch ein Headset. B wirkt wie ein Text-Messenger, C wie ein Funk- oder Podcast-Symbol.
- [x] AC2: `ovs.ico` enthält die Grössen 16, 24, 32, 48, 64 und 256 px, die exe zeigt es im Explorer (manuell).
- [x] AC3: Fenster und Taskleiste zeigen das Logo, Titelleiste und Startbildschirm zeigen das Zeichen in beiden Designs.
- [x] AC4: Kontrast des Zeichens zum Hintergrund mindestens 3:1 in beiden Designs.

### Tests (TDD)

1. `LogoTests > "Ico_ContainsAllSizes"`: liest den ICO-Header, erwartet die sechs Grössen (AC2)
2. `LogoTests > "Csproj_UsesTheIcon"`: `ApplicationIcon` zeigt auf eine vorhandene Datei (AC2)
3. `UiSmokeTests`: `MainWindow.Icon` ist gesetzt, die Titelleiste enthält das Zeichen (AC3)
4. Kontrast der Logofarben gegen `Ovs.Bg` und `Ovs.Sidebar` beider Designs nachrechnen (AC4, im Package dokumentiert): das Blau #2F6FEB hat etwa 4:1 gegen #111318 und gegen #F4F5F7, das Weiss darauf 4.57:1.

**Umsetzungsnotizen:** `Views/LogoMark.axaml` ist das Logo als View mit derselben Geometrie wie `Assets/logo.svg`. Die ICO-Grössen wurden aus dieser Geometrie gerendert, 16 und 24 px ohne die Schallwelle. Die exe zeigt das Logo (per `ExtractAssociatedIcon` geprüft), `docs/logo.png` steht oben in der README.

Testbefehl: `dotnet test --filter "FullyQualifiedName~LogoTests|FullyQualifiedName~UiSmokeTests"`

### Umsetzungsschritte

1. Entwürfe als SVG erstellen, als PNG rendern und dem Nutzer zeigen.
2. Nach der Wahl: Tests 1 bis 3 schreiben, rot sehen.
3. ICO aus der Vorlage erzeugen, einbinden, Tests grün, Screenshots prüfen.

### Out of Scope

- Logo im Docker-Image oder auf einer Webseite

---

## Package 29: Tastenbelegungen

**Ziel:** Beliebige Aktionen lassen sich global auf Tasten oder Tastenkombinationen legen, und neue Profile starten ohne Belegung.

**Abhängigkeiten:** Package 25, 26

**Betroffene Dateien:**
- `src/OVS.Client/Input/KeyBindings.cs` (neu): `KeyAction`, `KeyChord`, `KeyBinding`, Auswertung
- `src/OVS.Client/Input/KeyPoller.cs` (ändern): fragt alle belegten Tasten und Strg, Umschalt, Alt ab
- `src/OVS.Client/Settings/ClientSettings.cs` (ändern): `KeyBindings` statt `PttKey` und `LinkPttKey`, Übernahme alter Profile
- `src/OVS.Client/Audio/SendPath.cs`, `Audio/AudioEngine.cs` (ändern): Push-to-Mute
- `src/OVS.Client/ViewModels/SettingsViewModel.cs`, `Views/SettingsView.axaml` (ändern): Liste der Aktionen
- `src/OVS.Client/ViewModels/MainViewModel.cs` (ändern): Umschalt-Aktionen, Hinweis ohne PTT-Taste
- `src/OVS.Client/Debug/DebugApi.cs`, `Debug/AudioDebugLog.cs` (ändern)
- `README.md` (ändern): Standardbelegung, Debug-API
- `tests/OVS.Tests/Client/KeyBindingTests.cs` (neu), `SettingsTests.cs`, `SendPathTests.cs`, `MainViewModelTests.cs`, `DebugApiTests.cs` (ändern)

### Kontext

Heute gibt es genau zwei Tasten (`KeyPoller.PttKey`, `LinkPttKey`) mit den Standardwerten Maustaste 4 und 5, und nur einzelne Tasten. Neu nach A25:

| Aktion | Art | Wirkung |
|---|---|---|
| Push-to-Talk | halten | sendet an den eigenen Channel (nur im PTT-Modus, Package 25) |
| Link-PTT | halten | sendet an Channel und Links |
| Push-to-Mute | halten | sendet nichts, solange gedrückt, in beiden Modi, auch über PTT |
| Mikrofon an/aus | drücken | wie der Mikrofon-Button, einmal pro Tastendruck |
| Ton an/aus | drücken | wie der Button "Ton aus", einmal pro Tastendruck |

Eine Kombination gilt, wenn Taste und alle ihre Modifikatoren gedrückt sind. Gibt es für dieselbe Taste mehrere Belegungen, gewinnt die mit den meisten passenden Modifikatoren (Strg+F1 löst nicht zusätzlich F1 aus). Alte `settings.json` mit `pttKey` und `linkPttKey` werden beim Laden in Belegungen übernommen, ohne Datei gibt es keine Belegung.

### Acceptance Criteria

- [x] AC1: Ein neues Profil hat keine Belegung, die Einstellungen zeigen jede Aktion als "Nicht belegt".
- [x] AC2: Ein bestehendes Profil behält PTT auf Maustaste 4 und Link-PTT auf Maustaste 5.
- [x] AC3: Jede der fünf Aktionen lässt sich auf eine Taste, eine Maustaste oder eine Kombination mit Strg, Umschalt, Alt legen und wieder entfernen.
- [x] AC4: Push-to-Mute verhindert das Senden, solange gedrückt, in beiden Modi und auch bei gedrückter PTT-Taste.
- [x] AC5: Mikrofon an/aus und Ton an/aus schalten einmal pro Tastendruck, auch bei gehaltener Taste nur einmal, und melden den Zustand wie die Buttons an den Server.
- [x] AC6: Dieselbe Kombination für zwei Aktionen verhindert das Speichern mit einer Meldung.
- [x] AC7: Strg+F1 auf Aktion A und F1 auf Aktion B: Strg+F1 löst nur A aus.
- [x] AC8: Im PTT-Modus ohne PTT-Belegung steht unter dem eigenen Namen "Keine PTT-Taste belegt", ein Klick öffnet die Einstellungen.
- [x] AC9 (manuell): Die Tasten wirken, während ein anderes Programm im Vordergrund ist.
  - Unverändert `GetAsyncKeyState`, das den globalen Tastenzustand liefert. Genau dieser Mechanismus wurde in Package 16 mit einem anderen Programm im Vordergrund geprüft (F24). Neu ist nur die Auswertung der Belegungen, die die Tests abdecken; ein erneuter Handtest steht aus.
- [x] AC10: Die Debug-API kann jede Aktion simulieren.

### Tests (TDD)

1. `KeyBindingTests > "Resolve_ChordWithMoreModifiersWins"` (AC7), `"Resolve_RequiresAllModifiers_ExtraOnesDoNotHurt"` (AC3): zusätzlich gedrückte Modifikatoren (Umschalt beim Laufen im Spiel) stören eine einfache Taste nicht
2. `KeyBindingTests > "Toggle_FiresOncePerPress_HoldReportsChanges"`: gehaltene Taste über zehn Abfragen löst genau einmal aus (AC5)
3. `SettingsTests > "Load_NoFile_NoBindings"` (AC1), `"Load_OldFileWithPttKeys_KeepsThem"` (AC2), `"Save_DuplicateChord_Blocked"` (AC6)
4. `SendPathTests > "PushToMute_Held_SendsNothing"` in beiden Modi, Ende-zu-Ende mit der Audio-Engine: Push-to-Mute wirkt wie das eigene Stummschalten und schlägt deshalb PTT und Sprachaktivierung (AC4)
5. `MainViewModelTests > "ToggleActions_SyncWithServer"` gegen `TestServer`: simulierte Aktion schaltet stumm, der Server sieht `SelfMuted` (AC5), `"TalkHint_NoPttBinding_UntilBound"` (AC8)
6. `DebugApiTests > "Keys_SimulateEveryAction"` (AC10)

Testbefehl: `dotnet test --filter "FullyQualifiedName~KeyBindingTests|FullyQualifiedName~SettingsTests|FullyQualifiedName~SendPathTests|FullyQualifiedName~MainViewModelTests|FullyQualifiedName~DebugApiTests"`

### Umsetzungsschritte

1. Tests 1 bis 3 schreiben, rot sehen, `KeyBindings` und die Übernahme alter Profile bauen.
2. `KeyPoller` auf Belegungen umstellen (Abfrage aller belegten Tasten, Modifikatoren über `VK_CONTROL`, `VK_SHIFT`, `VK_MENU`).
3. Push-to-Mute im Sendepfad, Umschalt-Aktionen im `MainViewModel`, Tests 4 und 5.
4. Einstellungsseite mit "Belegen" und "Entfernen" je Aktion, Debug-API, README.

### Out of Scope

- Mehrere Tasten pro Aktion
- Eine Aktion zum Wechseln zwischen PTT und Sprachaktivierung (lässt sich später als weitere Zeile ergänzen)

---

## Package 30: Server-Logo

**Ziel:** Admins mit dem Recht "Servereinstellungen ändern" laden ein eigenes quadratisches Logo hoch, das alle Clients statt des Buchstabens sehen.

**Abhängigkeiten:** Package 26

**Betroffene Dateien:**
- `src/OVS.Shared/Protocol/Messages.cs`, `ProtocolInfo.cs` (ändern): `IconHash` in `ServerSettingsInfo`, `SetServerIcon`, `GetServerIcon`, `ServerIcon`, Version erhöhen
- `src/OVS.Server/Data/ServerIconStore.cs` (neu): Prüfung und Ablage in `<DataDir>/server-icon.png`
- `src/OVS.Server/Commands/AdminCommands.cs`, `ServerState.cs` (ändern)
- `src/OVS.Client/Views/IconImport.cs` (neu): Datei prüfen, dekodieren, auf 256 x 256 PNG verkleinern
- `src/OVS.Client/Net/ServerIconCache.cs` (neu): Logos je Host und Port im Profil unter `server-icons/`
- `src/OVS.Client/ViewModels/AdminViewModel.cs`, `ServerViewModel.cs`, `MainViewModel.cs` (ändern)
- `src/OVS.Client/Views/AdminView.axaml`, `MainWindow.axaml` (ändern): Vorschau, Hochladen per Dateidialog und Drag-and-drop, Anzeige in Seitenleiste und Lesezeichen
- `src/OVS.Client/Logging/ClientLog.cs`, `Debug/DebugApi.cs` (ändern)
- `tests/OVS.Tests/Server/ServerIconTests.cs`, `tests/OVS.Tests/Client/IconImportTests.cs` (neu), `UiSmokeTests.cs`, `ClientLogTests.cs` (ändern)

### Kontext

Seitenleiste und Lesezeichen zeigen den ersten Buchstaben des Servernamens. Nach A21 prüft der Client die Datei und lädt eine 256 x 256 PNG hoch. Der Server hat keine Bildbibliothek, er prüft deshalb nur die Bytes: höchstens 512 KB, PNG-Signatur, Breite gleich Höhe laut IHDR, 64 bis 512 px. Das Logo reist nicht im `Welcome` mit, sondern nur sein Hash. Clients holen es per `GetServerIcon`, wenn der Hash nicht zum Cache passt. Änderungen kommen über `ServerSettingsChanged`.

### Acceptance Criteria

- [x] AC1: In der Verwaltung, Tab Server, gibt es eine Vorschau, "Logo wählen ..." (Windows-Dateidialog, A20) und Drag-and-drop auf die Vorschau.
- [x] AC2: Dateien über 3 MB, andere Formate als PNG und JPG und nicht quadratische Bilder werden mit einer verständlichen Meldung abgelehnt, bevor etwas gesendet wird.
- [x] AC3: Ein gültiges Bild erscheint bei allen verbundenen Clients in der Seitenleiste, ohne Neuverbindung.
- [x] AC4: Das Logo übersteht einen Serverneustart und lässt sich entfernen, danach erscheint wieder der Buchstabe.
- [x] AC5: Der Server lehnt ungültige Daten (zu gross, kein PNG, nicht quadratisch) und fehlende Rechte ab und schreibt Änderungen ins Server-Log.
- [x] AC6: Lesezeichen-Kacheln zeigen das zuletzt gesehene Logo des Servers.

### Tests (TDD)

1. `ServerIconTests > "Set_ValidPng_StoredAndBroadcast"`: gültige 256er PNG, ein zweiter Client bekommt `ServerSettingsChanged` mit neuem Hash und kann das Bild holen (AC3)
2. `ServerIconTests > "Set_Invalid_Rejected"` als Theory: zu gross, kein PNG, nicht quadratisch, ohne Recht (AC5)
3. `ServerIconTests > "Icon_SurvivesRestart_AndCanBeRemoved"` (AC4)
4. `IconImportTests > "Import_TooBigWrongFormatNotSquareTooSmall_Rejected"`, `"Import_Jpg1024_Becomes256Png"` und `"Import_SmallSquarePng_KeepsItsSize"`, headless mit Avalonia und Skia (AC2)
5. `UiSmokeTests`: mit Logo zeigt die Seitenleiste ein Bild statt des Buchstabens (AC3), Kachel mit Cache-Datei zeigt es ebenfalls (AC6)
6. `ClientLogTests > "Describe_Request_UsesNames_NeverSecrets"` um die neuen Nachrichten erweitert
7. `MainViewModelTests > "ServerIcon_LoadedOnce_ThenFromCache_ShownOnTile"`: echter Server mit Logo, der erste Verbindungsaufbau lädt es, der zweite nimmt es aus dem Cache, die Kachel zeigt es (AC3, AC6)

**Umsetzungsnotizen:**
- Die Tests der Oberfläche rendern jetzt mit Skia statt der Zeichen-Attrappe, damit Bilder wirklich dekodiert und skaliert werden.
- `ServerIconCache` fängt Plattenfehler selbst ab: Der Cache ist nur eine Abkürzung und darf nie das Verbinden verhindern. Gefunden hat das der Test: `File.Delete` wirft, wenn der Ordner `server-icons` noch fehlt.
- Das Logo gilt sofort nach der Auswahl, unabhängig von "Servereinstellungen speichern".

Testbefehl: `dotnet test --filter "FullyQualifiedName~ServerIconTests|FullyQualifiedName~IconImportTests|FullyQualifiedName~UiSmokeTests|FullyQualifiedName~ClientLogTests"`

### Umsetzungsschritte

1. Servertests schreiben, rot sehen, Protokoll und `ServerIconStore` bauen.
2. Import-Tests schreiben, `IconImport` mit Avalonia-Bitmaps bauen.
3. Abruf, Cache und Anzeige im Client, Verwaltungsseite, Debug-API, Logs.

### Out of Scope

- Zuschneiden im Client
- Animierte Bilder

---

## Package 31: Chat-Grundlage

**Ziel:** Der Server vermittelt serverweite, Channel- und Privatnachrichten und prüft dafür drei neue Rechte.

**Abhängigkeiten:** Package 8, 21

**Betroffene Dateien:**
- `src/OVS.Shared/Permissions/Permission.cs` (ändern): `ChatServer`, `ChatChannel`, `ChatPrivate`, `All` erweitern
- `src/OVS.Shared/Protocol/Messages.cs`, `Codes.cs`, `ProtocolInfo.cs` (ändern): `SendChat`, `ChatMessage`, `ChatTarget`, Code `RateLimited`, Version erhöhen
- `src/OVS.Server/Commands/ChatCommands.cs` (neu)
- `src/OVS.Server/ServerState.cs`, `Session.cs` (ändern): Weiterleitung, eigener Nachrichtenzähler pro Sitzung
- `src/OVS.Server/Permissions/PermissionRules.cs` (ändern): Standardgruppen
- `src/OVS.Server/Data/ServerData.cs`, `DataStore.cs` (ändern): `DataVersion`, einmalige Rechte für Gäste
- `src/OVS.Client/ErrorTexts.cs` (ändern): Rechtenamen und Fehlertext
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (ändern): `SendChatAsync`, Ereignis `ChatReceived`
- `src/OVS.Client/Logging/ClientLog.cs`, `Debug/DebugApi.cs` (ändern): Beschreibung, `/chat` und empfangene Nachrichten in `/state`
- `tests/OVS.Tests/Server/ChatTests.cs` (neu), `PermissionRulesTests.cs`, `DataStoreTests.cs`, `ServerLogsTests.cs`, `Client/DebugApiTests.cs`, `ClientLogTests.cs`, `SettingsTests.cs` (ändern)

### Kontext

Es gibt heute keinen Text-Chat. Die Rechte belegen die Bits 0 bis 12, die neuen werden 13 bis 15. Admin hat über `PermissionRules.Effective` automatisch alle. `ServerData` hat keine Versionsnummer, fehlt sie, gilt Version 1. Beim Laden von Version 1 bekommt die Gast-Gruppe `ChatChannel` und `ChatPrivate` (A28), danach wird Version 2 gespeichert. Die Moderator-Gruppe hat keine feste ID und wird deshalb nicht angepasst.

**Protokoll:** `SendChat(ChatTarget Target, uint? ToSessionId, string Text)`, Antwort und Zustellung als `ChatMessage(Target, FromSessionId, FromNickname, ChannelId, ToSessionId, Text, SentAt)`. Empfänger: serverweit alle, Channel alle im aktuellen Channel des Absenders (nicht verlinkte), privat Empfänger und Absender. Der Absender bekommt seine eigene Nachricht mit Serverzeit zurück.

### Acceptance Criteria

- [x] AC1: Serverweite Nachrichten erreichen alle, Channelnachrichten nur den eigenen Channel, private nur Empfänger und Absender.
- [x] AC2: Ohne das passende Recht antwortet der Server mit `PermissionDenied`, niemand bekommt die Nachricht.
- [x] AC3: Leere Nachrichten und solche über 2000 Zeichen ergeben `InvalidValue`, private an Offline-Nutzer oder an sich selbst `NotFound` bzw. `InvalidValue`.
- [x] AC4: Die sechste Nachricht innerhalb von 5 Sekunden ergibt `RateLimited`.
- [x] AC5: Vom Server Stummgeschaltete können schreiben.
- [x] AC6: Neue Server: Gast hat Channel und privat, Moderator und Admin alle drei. Bestehende Server: Gast bekommt Channel und privat einmalig dazu.
- [x] AC7: Logs nach A29, der Inhalt privater Nachrichten steht in keinem Log.
- [x] AC8: Die Debug-API kann Nachrichten senden und zeigt empfangene in `/state`.

### Tests (TDD)

1. `ChatTests > "Deliver_ServerChannelPrivate"` mit drei `TestClient`s in zwei Channels (AC1)
2. `ChatTests > "NoRight_PermissionDenied"` als Theory je Ziel (AC2)
3. `ChatTests > "Invalid_EmptyTooLongOfflineSelf"` (AC3), `"RateLimit_SixthInFiveSeconds"` mit `ManualTimeProvider` (AC4), `"ServerMuted_CanWrite"` (AC5)
4. `PermissionRulesTests > "DefaultGroups_HaveDocumentedPermissions"` (um die Chat-Rechte erweitert) und `DataStoreTests > "LoadVersion1_GuestGetsChatRights_Once"` (AC6)
5. `ChatTests > "Logs_ServerAndChannel_PrivateWithoutContent"` (AC7, beim Chat statt in `ServerLogsTests`, weil es drei verbundene Clients braucht)
6. `DebugApiTests > "Chat_RoundTrip"` zwischen zwei echten Clients (AC8), `ClientLogTests` für `ChatMessage` und `SendChat`

Testbefehl: `dotnet test --filter "FullyQualifiedName~ChatTests|FullyQualifiedName~PermissionRulesTests|FullyQualifiedName~DataStoreTests|FullyQualifiedName~ServerLogsTests|FullyQualifiedName~DebugApiTests|FullyQualifiedName~ClientLogTests"`

### Umsetzungsschritte

1. Rechte, Standardgruppen und Migration mit Tests.
2. Protokoll und `ChatCommands` mit den Server-Tests.
3. Logs, Client-Anbindung ohne Oberfläche, Debug-API, Client-Log.

### Out of Scope

- Oberfläche (Package 32, 33)
- Verlauf auf dem Server (A27)

---

## Package 32: Chat-Oberfläche

**Ziel:** Statt der Aktivitätsliste zeigt der Hauptbereich einen Chat mit den Tabs "Allgemein" und dem aktuellen Channel.

**Abhängigkeiten:** Package 26, 31

**Betroffene Dateien:**
- `src/OVS.Client/ViewModels/ChatViewModel.cs` (neu): Tabs, Einträge, Entwurf, Senden, Ungelesen
- `src/OVS.Client/Views/ChatView.axaml(.cs)` (neu)
- `src/OVS.Client/ViewModels/MainViewModel.cs`, `ServerViewModel.cs` (ändern): Systemmeldungen und Chat zusammenführen
- `src/OVS.Client/Views/MainWindow.axaml` (ändern): Chat statt Aktivität
- `src/OVS.Client/Styles/Controls.axaml` (ändern)
- `tests/OVS.Tests/Client/ChatViewModelTests.cs` (neu), `UiSmokeTests.cs`, `MainViewModelTests.cs` (ändern)

### Kontext

Der Hauptbereich zeigt seit Package 24 die "Aktivität" (`MainViewModel.Notices`). Nach A26 wird daraus der Tab "Allgemein": serverweite Nachrichten und Systemmeldungen chronologisch gemischt, Systemmeldungen mit ihrem Icon wie bisher. Daneben der Tab des aktuellen Channels. Unten eine Eingabezeile: Enter sendet, Umschalt+Enter macht eine neue Zeile, ab 1800 Zeichen erscheint ein Zähler.

### Acceptance Criteria

- [x] AC1: Es gibt die Tabs "Allgemein" und den aktuellen Channel mit dessen Namen.
- [x] AC2: Systemmeldungen (Willkommen, Warnungen, Fehler, Trennungen) erscheinen in "Allgemein".
- [x] AC3: Enter sendet, Umschalt+Enter bricht um, leere Nachrichten werden nicht gesendet, über 2000 Zeichen ist Senden gesperrt.
- [x] AC4: Ohne das Recht für den Tab ist die Eingabe gesperrt, mit dem Hinweis, dass das Recht fehlt.
- [x] AC5: Der Channel-Tab zeigt Nachrichten ab dem Betreten und beginnt beim Wechsel neu, mit einer Zeile, welchen Channel man betreten hat.
- [x] AC6: Inaktive Tabs zeigen die Zahl ungelesener Nachrichten, beim Öffnen verschwindet sie.
- [x] AC7: Eigene Nachrichten sind erkennbar, Nachrichten zeigen Avatar, Name, Uhrzeit und markierbaren Text.
- [x] AC8: Neue Nachrichten scrollen nach unten, ausser man hat selbst nach oben gescrollt.
- [x] AC9: Fehler des Servers (`RateLimited`, `PermissionDenied`) erscheinen direkt unter der Eingabe.
- [x] AC10: Beide Designs, Tastatur und Screenreader-Namen wie in Package 24.

### Tests (TDD)

1. `ChatViewModelTests > "Tabs_GeneralAndCurrentChannel"` (AC1), `"Notices_AppearInGeneral"` (AC2)
2. `ChatViewModelTests > "Send_EmptyIgnored_TooLongBlocked"` (AC3), `"Composer_DisabledWithoutRight"` (AC4)
3. `ChatViewModelTests > "ChannelTab_ResetsOnSwitch"` (AC5), `"Unread_CountsAndClears"` (AC6)
4. `ChatViewModelTests > "ServerError_ShownAtComposer"` (AC9)
5. `MainViewModelTests > "Chat_EndToEnd"` gegen `TestServer` (AC1, AC3)
6. `UiSmokeTests`: Chat in beiden Designs, eigene und fremde Nachricht sichtbar (AC7, AC10)
7. `UiSmokeTests > "Chat_FollowsNewLines_UnlessScrolledUp"` (AC8, statt des manuellen Checks automatisiert)

Testbefehl: `dotnet test --filter "FullyQualifiedName~ChatViewModelTests|FullyQualifiedName~MainViewModelTests|FullyQualifiedName~UiSmokeTests"`

### Umsetzungsschritte

1. `ChatViewModel` testgetrieben bauen.
2. `ChatView` und Einbau ins Hauptfenster, Systemmeldungen umleiten.
3. Screenshots beider Designs prüfen, manueller Scroll-Check.

### Out of Scope

- Privatchats (Package 33)
- Links, Formatierung, Emojis, Dateien

---

## Package 33: Privatchats

**Ziel:** Zwei Nutzer können sich in einem eigenen Tab privat schreiben.

**Abhängigkeiten:** Package 32

**Betroffene Dateien:**
- `src/OVS.Client/ViewModels/ChatViewModel.cs`, `ServerViewModel.cs` (ändern)
- `src/OVS.Client/Views/ChatView.axaml`, `MainWindow.axaml` (ändern): schliessbare Tabs, Kontextmenü "Privatnachricht"
- `tests/OVS.Tests/Client/ChatViewModelTests.cs`, `MainViewModelTests.cs`, `UiSmokeTests.cs` (ändern)

### Kontext

Server und Protokoll können private Nachrichten seit Package 31. Der Tab gehört zum Fingerprint des Partners, nicht zur Sitzungs-ID, damit er nach einem Neuverbinden des Partners weiter passt. Der Verlauf bleibt im Client, solange man verbunden ist (A27).

### Acceptance Criteria

- [x] AC1: Rechtsklick auf einen anderen Nutzer bietet "Privatnachricht", mit dem Recht `ChatPrivate`. Das öffnet bzw. aktiviert den Tab "@Nickname".
- [x] AC2: Eine eingehende private Nachricht öffnet den Tab im Hintergrund mit Ungelesen-Zähler.
- [x] AC3: Private Tabs lassen sich schliessen, eine neue Nachricht öffnet sie wieder mit dem bisherigen Verlauf.
- [x] AC4: Geht der Partner offline, zeigt der Tab das an und sperrt die Eingabe. Kommt er zurück, ist sie wieder frei.
- [x] AC5: Ändert der Partner seinen Nickname, ändert sich der Tab-Titel.

### Tests (TDD)

1. `ChatViewModelTests > "Private_OpenFromUser_ActivatesTab"` (AC1), `"Private_Incoming_OpensInBackground"` (AC2)
2. `ChatViewModelTests > "Private_CloseAndReopen_KeepsHistory"` (AC3), `"Private_PartnerOfflineAndBack"` (AC4), `"Private_NicknameChange_UpdatesTitle"` (AC5)
3. `MainViewModelTests > "Private_EndToEnd"` mit zwei Clients gegen `TestServer` (AC1, AC2)
4. `UiSmokeTests`: Kontextmenü enthält "Privatnachricht", privater Tab mit Schliessen-Button (AC1, AC3)

Testbefehl: `dotnet test --filter "FullyQualifiedName~ChatViewModelTests|FullyQualifiedName~MainViewModelTests|FullyQualifiedName~UiSmokeTests"`

### Umsetzungsschritte

1. Tests für private Tabs schreiben, rot sehen, `ChatViewModel` erweitern.
2. Kontextmenü, schliessbare Tabs, Ende-zu-Ende-Test.
3. Screenshots beider Designs prüfen.

### Out of Scope

- Flüstern per Sprache
- Nachrichten an Offline-Nutzer

---

## Package 34: Stummer Channel

**Ziel:** In einem Channel mit der Option "Stumm" wird niemand gehört, auch nicht per Link-PTT.

**Abhängigkeiten:** Package 12, 18

**Betroffene Dateien:**
- `src/OVS.Shared/Protocol/Messages.cs`, `ProtocolInfo.cs` (ändern): `ChannelInfo.IsMuted`, `EditChannel.IsMuted`, Version 4
- `src/OVS.Server/Data/ServerData.cs` (ändern): `ChannelRecord.IsMuted`
- `src/OVS.Server/Commands/ChannelCommands.cs`, `ServerState.cs` (ändern): Speichern, Channel-Log, `Info(channel)`
- `src/OVS.Server/Voice/VoiceRouting.cs` (ändern): stumme Channels leiten nichts weiter
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (ändern): `ChannelViewModel.IsMuted`, `EditChannelAsync` mit Flag
- `src/OVS.Client/Views/SimpleDialogs.cs`, `MainWindow.axaml` (ändern): Checkbox im Dialog, Symbol am Channel
- `src/OVS.Client/ViewModels/MainViewModel.cs`, `src/OVS.Client/Audio/SendPath.cs` (ändern): nicht senden, Hinweis
- `src/OVS.Client/Logging/ClientLog.cs` (ändern)
- `tests/OVS.Tests/Server/VoiceRoutingTests.cs`, `ChannelCommandTests.cs`, `VoiceRelayTests.cs`, `tests/OVS.Tests/Client/SendPathTests.cs`, `ServerViewModelTests.cs`, `UiSmokeTests.cs`, `SmokeTests.cs` (ändern)

### Kontext

`VoiceRouting.Recipients` prüft heute nur `Speak`, Server-Mute und Selbst-Mute des Senders, dann den eigenen Channel und bei Link-PTT die verlinkten. Channels haben kein Flag (`ChannelRecord`: Id, Name, Description, Order). Der Bearbeiten-Dialog (`SimpleDialogs.EditChannel`) fragt Name und Beschreibung ab und liefert `ChannelEdit`.

### Acceptance Criteria

- [x] AC1: Der Dialog "Channel bearbeiten" hat die Checkbox "Stummer Channel: niemand wird gehört". Speichern geht nur mit `ChannelEdit`. Neue Channels sind nie stumm.
- [x] AC2: Der Server leitet keine Sprache von Sendern in einem stummen Channel weiter, weder in den Channel noch per Link-PTT in verlinkte, auch nicht von Admins.
- [x] AC3: Sprache aus einem verlinkten, nicht stummen Channel kommt im stummen Channel an.
- [x] AC4: Das Flag wird gespeichert, steht im Snapshot und geht per `ChannelUpdated` an alle. Das Channel-Log vermerkt "stumm geschaltet von X" bzw. "Stummschaltung aufgehoben von X".
- [x] AC5: Der Client zeigt am Channel ein Symbol mit Tooltip. Im stummen Channel sendet er nicht. Bei gedrückter Sprechtaste steht dort "Stummer Channel: niemand hört dich". Der eigene Mikrofonstatus bleibt unverändert.
- [x] AC6: `ProtocolInfo.Version` ist 4.

### Tests (TDD)

1. `VoiceRoutingTests > "Recipients_MutedChannel_NobodyHearsSender"` (AC2)
   - Gegeben: Sender (Admin) in stummem Channel A, Empfänger in A und im verlinkten B, Ziel einmal Channel, einmal Linked
   - Erwartet: leere Empfängerliste in beiden Fällen
2. `VoiceRoutingTests > "Recipients_LinkIntoMutedChannel_Heard"` (AC3): Sender in B (nicht stumm) mit Link-PTT erreicht Empfänger in A.
3. `ChannelCommandTests > "Edit_SetsMuted_PersistsLogsBroadcasts"` und `"Edit_Muted_WithoutRight_Denied"` (AC1, AC4)
4. `VoiceRelayTests > "MutedChannel_NoFramesArrive"` über echtes UDP (AC2)
5. `SendPathTests > "Decide_ChannelMuted_SendsNothing"` (AC5)
6. `ServerViewModelTests > "ChannelMuted_FlagAndHint"` (AC5)
7. `UiSmokeTests`: Dialog zeigt die Checkbox, Channel-Zeile das Symbol, beide Designs (AC1, AC5); `SmokeTests`: Version 4 (AC6)

Testbefehl: `dotnet test --filter "FullyQualifiedName~VoiceRoutingTests|FullyQualifiedName~ChannelCommandTests|FullyQualifiedName~VoiceRelayTests|FullyQualifiedName~SendPathTests|FullyQualifiedName~ServerViewModelTests|FullyQualifiedName~UiSmokeTests|FullyQualifiedName~SmokeTests"`

### Umsetzungsschritte

1. Routing-Tests rot, `Recipients` bekommt die Information, welche Channels stumm sind.
2. Protokoll, Datenmodell und `OnEditChannel` mit Tests.
3. Client: Flag, Dialog, Symbol, Sendesperre und Hinweis.

### Out of Scope

- Ausnahmen per Recht (A31: bewusst keine)
- Stumm beim Anlegen einstellen

---

## Package 35: Slot-Begrenzung

**Ziel:** Channels lassen sich auf eine Höchstzahl an Nutzern begrenzen, die nur ein neues Recht umgeht.

**Abhängigkeiten:** Package 7, 18

**Betroffene Dateien:**
- `src/OVS.Shared/Permissions/Permission.cs` (ändern): `ChannelJoinFull = 1 << 16`, `All = (1 << 17) - 1`
- `src/OVS.Shared/Protocol/Messages.cs`, `Codes.cs`, `ProtocolInfo.cs` (ändern): `ChannelInfo.MaxUsers`, `EditChannel.MaxUsers`, Code `ChannelFull`, Version 5
- `src/OVS.Server/Data/ServerData.cs`, `Commands/ChannelCommands.cs` (ändern): Prüfung in `OnJoinChannel`, `OnMoveUser`, `OnEditChannel`
- `src/OVS.Client/ErrorTexts.cs` (ändern): Fehlertext, Rechtename
- `src/OVS.Client/ViewModels/ServerViewModel.cs`, `Views/SimpleDialogs.cs`, `Views/MainWindow.axaml` (ändern): Eingabefeld, Anzeige "3/5"
- `tests/OVS.Tests/Server/ChannelCommandTests.cs`, `PermissionRulesTests.cs`, `tests/OVS.Tests/Client/ServerViewModelTests.cs`, `UiSmokeTests.cs`, `SmokeTests.cs` (ändern)

### Kontext

`OnJoinChannel` und `OnMoveUser` in `ChannelCommands.cs` prüfen nur, ob der Channel existiert (Move zusätzlich `UserMove` und Rang). Beim Verbinden und beim Löschen eines Channels landen Nutzer im Standard-Channel (`data.DefaultChannelId`). Die Seitenleiste zeigt rechts an jedem Channel die Nutzerzahl.

### Acceptance Criteria

- [x] AC1: Im Dialog "Channel bearbeiten" gibt es "Maximale Nutzer (0 = unbegrenzt)", 0 bis 999. Beim Standard-Channel ist das Feld gesperrt, der Server lehnt dort einen Wert über 0 mit `InvalidValue` ab.
- [x] AC2: Betreten eines vollen Channels ergibt `ChannelFull` ("Der Channel ist voll."), ausser mit `ChannelJoinFull`.
- [x] AC3: Verschieben in einen vollen Channel geht nur, wenn der Verschiebende `ChannelJoinFull` hat.
- [x] AC4: Wird das Limit unter die aktuelle Zahl gesenkt, bleiben alle drin, neu hinein kommt keiner.
- [x] AC5: Die Seitenleiste zeigt bei begrenzten Channels "3/5", bei unbegrenzten wie bisher nur die Zahl. Das neue Recht erscheint in der Gruppenverwaltung als "Volle Channel betreten".
- [x] AC6: Das Limit wird gespeichert und steht im Snapshot. `ProtocolInfo.Version` ist 5.

### Tests (TDD)

1. `ChannelCommandTests > "Join_Full_ChannelFull"`, `"Join_Full_WithRight_Allowed"` (AC2)
2. `ChannelCommandTests > "Move_IntoFull_NeedsRightOfMover"` (AC3)
3. `ChannelCommandTests > "Edit_DefaultChannelLimit_Invalid"`, `"Edit_LimitRange_0To999"` (AC1)
4. `ChannelCommandTests > "Edit_LimitBelowCount_NobodyRemoved"` (AC4), `"Limit_PersistedAndInSnapshot"` (AC6)
5. `PermissionRulesTests`: `All` enthält `ChannelJoinFull` (AC5)
6. `ServerViewModelTests > "SlotText_LimitedAndUnlimited"` (AC5)
7. `UiSmokeTests`: Feld im Dialog, "3/5" im Baum; `SmokeTests`: Version 5

Testbefehl: `dotnet test --filter "FullyQualifiedName~ChannelCommandTests|FullyQualifiedName~PermissionRulesTests|FullyQualifiedName~ServerViewModelTests|FullyQualifiedName~UiSmokeTests|FullyQualifiedName~SmokeTests"`

### Umsetzungsschritte

1. Recht und Code, dann die Server-Tests rot, Prüfungen in Join, Move und Edit.
2. Protokoll und Datenmodell, Snapshot.
3. Client: Dialogfeld, Anzeige, Texte.

### Out of Scope

- Warteschlange für volle Channels
- Limit beim Anlegen einstellen

---

## Package 36: Channels sortieren

**Ziel:** Berechtigte ordnen Channels per Drag and Drop oder Kontextmenü neu, und alle sehen die neue Reihenfolge sofort.

**Abhängigkeiten:** Package 18, 26

**Betroffene Dateien:**
- `src/OVS.Shared/Protocol/Messages.cs`, `ProtocolInfo.cs` (ändern): `ReorderChannels(IReadOnlyList<Guid> ChannelIds)`, Version 6
- `src/OVS.Server/Commands/ChannelCommands.cs`, `ServerState.cs` (ändern): `OnReorderChannels`
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (ändern): `ReorderChannelsAsync`, `MoveUp`/`MoveDown` am `ChannelViewModel`
- `src/OVS.Client/Views/MainWindow.axaml`, `MainWindow.axaml.cs` (ändern): Drag and Drop mit Einfügemarke, Kontextmenü
- `src/OVS.Client/Logging/ClientLog.cs` (ändern)
- `tests/OVS.Tests/Server/ChannelCommandTests.cs`, `tests/OVS.Tests/Client/ServerViewModelTests.cs`, `UiSmokeTests.cs`, `ClientLogTests.cs`, `SmokeTests.cs` (ändern)

### Kontext

Channels haben ein `Order`-Feld, der Baum sortiert nach `Order`, dann nach Name. Ändern lässt sich `Order` bisher nur über `EditChannel` einzeln, die Oberfläche bietet dafür nichts. Gleiche `Order`-Werte kommen vor (neue Channels bekommen Maximum + 1, Seed-Daten gleiche Werte).

### Acceptance Criteria

- [x] AC1: Mit `ChannelEdit` lässt sich ein Channel im Baum auf einen anderen ziehen. Eine Linie zeigt, wo er landet. Ohne das Recht startet kein Ziehen.
- [x] AC2: Das Kontextmenü hat "Nach oben" und "Nach unten", am Anfang bzw. Ende der Liste deaktiviert.
- [x] AC3: Der Server nimmt nur eine vollständige Liste aller Channel-IDs ohne Doppelte an, sonst `InvalidValue`, ohne Recht `PermissionDenied`. Danach sind die `Order`-Werte 0 bis n-1, gespeichert, und jeder geänderte Channel geht per `ChannelUpdated` an alle.
- [x] AC4: Alle Clients zeigen die neue Reihenfolge sofort. Das Server-Log vermerkt die Umsortierung.
- [x] AC5: `ProtocolInfo.Version` ist 6.

### Tests (TDD)

1. `ChannelCommandTests > "Reorder_SetsOrder_PersistsBroadcasts"` (AC3, AC4)
2. `ChannelCommandTests > "Reorder_IncompleteDuplicateUnknown_Invalid"`, `"Reorder_WithoutRight_Denied"` (AC3)
3. `ServerViewModelTests > "MoveUpDown_SendFullOrder_DisabledAtEdges"` (AC2)
4. `ServerViewModelTests > "DropOnChannel_SendsOrderWithSourceBeforeTarget"`: die Einfüge-Logik liegt im ViewModel und ist ohne Maus testbar (AC1)
5. `UiSmokeTests`: Kontextmenü enthält beide Einträge, ohne Recht ist Ziehen aus (AC1, AC2); `ClientLogTests`, `SmokeTests` (AC5)
6. `UiSmokeTests > "ChannelTree_DragRaidAboveLobby_SendsOrder"`: Ziehen mit echten Mausereignissen im Headless-Fenster, kurzer Klick zieht nicht. Offen bleibt nur der Blick auf die Einfügemarke am echten Bildschirm.

Testbefehl: `dotnet test --filter "FullyQualifiedName~ChannelCommandTests|FullyQualifiedName~ServerViewModelTests|FullyQualifiedName~UiSmokeTests|FullyQualifiedName~ClientLogTests|FullyQualifiedName~SmokeTests"`

### Umsetzungsschritte

1. Server-Tests rot, `OnReorderChannels`.
2. ViewModel-Logik (Hoch, Runter, Einfügen vor Ziel) mit Tests.
3. Drag and Drop und Kontextmenü im Baum, manueller Check.

### Out of Scope

- Unter-Channels (A9: flache Liste)

---

## Package 37: Gruppen sortieren

**Ziel:** Berechtigte ordnen Gruppen in der Verwaltung neu, und alle Gruppenlisten folgen dieser Reihenfolge.

**Abhängigkeiten:** Package 18, 26

**Betroffene Dateien:**
- `src/OVS.Shared/Protocol/Messages.cs`, `ProtocolInfo.cs` (ändern): `ReorderGroups(IReadOnlyList<Guid> GroupIds)`, Version 7
- `src/OVS.Server/Commands/AdminCommands.cs`, `ServerState.cs` (ändern): `OnReorderGroups`
- `src/OVS.Client/ViewModels/AdminViewModel.cs`, `Views/AdminView.axaml(.cs)` (ändern): Pfeil-Buttons, Drag and Drop
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (ändern): Gruppennamen am Nutzer in Gruppenreihenfolge
- `src/OVS.Client/Logging/ClientLog.cs` (ändern)
- `tests/OVS.Tests/Server/AdminCommandTests.cs`, `tests/OVS.Tests/Client/AdminViewModelTests.cs`, `ServerViewModelTests.cs`, `UiSmokeTests.cs`, `SmokeTests.cs` (ändern)

### Kontext

`GroupInfo` hat kein Order-Feld. Die Reihenfolge ergibt sich aus `ServerData.Groups` und geht so im Snapshot und in `GroupsChanged` an alle. Der Rang hängt nur an den Rechten (`CanActOn`). `UserViewModel.GroupNames` folgt heute der Reihenfolge der Gruppen-IDs am Nutzer.

### Acceptance Criteria

- [x] AC1: Mit `GroupsManage` hat jede Gruppe in der Verwaltung Pfeile nach oben und unten, und die Liste lässt sich per Drag and Drop umordnen.
- [x] AC2: Der Server nimmt nur eine vollständige Liste aller Gruppen-IDs an (sonst `InvalidValue`, ohne Recht `PermissionDenied`), ordnet `ServerData.Groups` um, speichert und sendet `GroupsChanged`.
- [x] AC3: Gruppenliste in der Verwaltung, Gruppenzuweisung bei Nutzern und der Gruppen-Tooltip am Nutzer folgen der neuen Reihenfolge, auch nach einem Server-Neustart.
- [x] AC4: Rechte und Rang ändern sich durch das Sortieren nicht. `ProtocolInfo.Version` ist 7.

### Tests (TDD)

1. `AdminCommandTests > "ReorderGroups_PersistsAndBroadcasts"`, `"ReorderGroups_IncompleteOrWithoutRight_Rejected"` (AC2, AC3)
2. `AdminViewModelTests > "MoveGroupUpDown_SendsFullOrder"` und `"DropGroup_SendsOrder"` (AC1)
3. `ServerViewModelTests > "GroupNames_FollowGroupOrder"` (AC3)
4. `AdminCommandTests > "ReorderGroups_RankUnchanged"`: Moderator kann Admin weiterhin nicht kicken (AC4)
5. `UiSmokeTests`: Pfeile in der Gruppenliste; `SmokeTests`: Version 7
6. `UiSmokeTests > "GroupList_DragAdminAboveGuest_SendsOrder"`: Ziehen mit echten Mausereignissen im Headless-Fenster

Testbefehl: `dotnet test --filter "FullyQualifiedName~AdminCommandTests|FullyQualifiedName~AdminViewModelTests|FullyQualifiedName~ServerViewModelTests|FullyQualifiedName~UiSmokeTests|FullyQualifiedName~SmokeTests"`

### Umsetzungsschritte

1. Server-Tests rot, `OnReorderGroups`.
2. Verwaltung: Pfeile, Drag and Drop, Tests.
3. Gruppennamen am Nutzer nach Gruppenreihenfolge.

### Out of Scope

- Rang nach Reihenfolge statt nach Rechten

---

## Package 38: Links-Übersicht

**Ziel:** Berechtigte verlinken mehrere Channels auf einmal über eine Matrix in der Verwaltung.

**Abhängigkeiten:** Package 10, 26

**Betroffene Dateien:**
- `src/OVS.Shared/Protocol/Messages.cs`, `ProtocolInfo.cs` (ändern): `SetChannelLinks(IReadOnlyList<LinkInfo> Add, IReadOnlyList<LinkInfo> Remove)`, Version 8
- `src/OVS.Server/Commands/LinkCommands.cs`, `ServerState.cs` (ändern): `OnSetChannelLinks`, alles oder nichts
- `src/OVS.Client/ViewModels/AdminViewModel.cs` (ändern): Links-Matrix, Auswahl, ausstehende Änderungen
- `src/OVS.Client/Views/AdminView.axaml` (ändern): Tab "Links"
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (ändern): `CanAdminister` schliesst `ChannelLink` ein
- `src/OVS.Client/Logging/ClientLog.cs` (ändern)
- `tests/OVS.Tests/Server/LinkCommandTests.cs`, `tests/OVS.Tests/Client/AdminViewModelTests.cs`, `UiSmokeTests.cs`, `ClientLogTests.cs`, `SmokeTests.cs` (ändern)

### Kontext

`LinkChannels` und `UnlinkChannels` verarbeiten je ein Paar, die Oberfläche bietet das nur über das Kontextmenü eines Channels. Links sind ungerichtet und nicht transitiv (A5). Acht Channels vollständig zu verbinden, sind heute 28 Einzelschritte. Die Verwaltung ist nur mit `GroupsManage`, `GroupsAssign`, `UserBan` oder `ServerConfig` erreichbar.

### Acceptance Criteria

- [x] AC1: Mit `ChannelLink` gibt es in der Verwaltung den Tab "Links" mit einer Matrix aller Channels. Ein Häkchen bedeutet Link. Ein Klick ändert beide gespiegelten Felder, die Diagonale ist leer.
- [x] AC2: Über eine Mehrfachauswahl verbindet "Alle ausgewählten miteinander verlinken" jeden mit jedem. "Links zwischen den ausgewählten entfernen" macht das Gegenteil.
- [x] AC3: Änderungen sind bis "Übernehmen" nur vorgemerkt und farblich markiert. "Verwerfen" setzt zurück. "Übernehmen" sendet eine einzige Anfrage.
- [x] AC4: Der Server prüft alles vorab: unbekannter Channel oder Selbst-Link ergibt wie beim Einzel-Link `NotFound` bzw. `InvalidLink` und ändert nichts, derselbe Link in Add und Remove `InvalidValue`. Sonst speichert er einmal, sendet `ChannelsLinked` bzw. `ChannelsUnlinked` je Änderung und schreibt die Channel-Logs wie beim Einzel-Link. Bereits bestehende Links im Add-Teil sind kein Fehler.
- [x] AC5: Ändert ein anderer Admin gleichzeitig Links, zeigt die Matrix den neuen Stand, die eigenen vorgemerkten Änderungen bleiben markiert. `ProtocolInfo.Version` ist 8.

### Tests (TDD)

1. `LinkCommandTests > "SetLinks_AddAndRemove_OneSaveAllBroadcasts"` (AC4)
2. `LinkCommandTests > "SetLinks_UnknownOrSelf_NothingChanged"`, `"SetLinks_WithoutRight_Denied"` (AC4)
3. `AdminViewModelTests > "LinkMatrix_MeshOfEight_Sends28Links"` (AC2, AC3)
   - Gegeben: 8 Channels ohne Links, alle ausgewählt
   - Erwartet: nach "Übernehmen" eine Anfrage mit 28 Paaren, 0 Entfernungen
4. `AdminViewModelTests > "LinkMatrix_ToggleDiscardAndRemoteChange"` (AC1, AC3, AC5)
5. `UiSmokeTests`: Tab "Links" in beiden Designs; `ClientLogTests`, `SmokeTests` (AC5)

Testbefehl: `dotnet test --filter "FullyQualifiedName~LinkCommandTests|FullyQualifiedName~AdminViewModelTests|FullyQualifiedName~UiSmokeTests|FullyQualifiedName~ClientLogTests|FullyQualifiedName~SmokeTests"`

### Umsetzungsschritte

1. Server-Tests rot, `OnSetChannelLinks` mit Vorabprüfung.
2. Matrix-Logik im `AdminViewModel` mit Tests.
3. Tab "Links", Zugang zur Verwaltung mit `ChannelLink`.

### Out of Scope

- Gerichtete oder transitive Links

---

## Package 39: Passwort speichern

**Ziel:** Serverpasswörter lassen sich verschlüsselt mit dem Lesezeichen speichern.

**Abhängigkeiten:** Package 26

**Betroffene Dateien:**
- `src/OVS.Client/OVS.Client.csproj` (ändern): Paket `System.Security.Cryptography.ProtectedData`
- `src/OVS.Client/Settings/ClientSettings.cs` (ändern): `Bookmark.ProtectedPassword`, Schutz und Entschlüsseln
- `src/OVS.Client/Views/SimpleDialogs.cs` (ändern): Checkbox "Passwort speichern", Vorbelegen aus dem Lesezeichen
- `src/OVS.Client/ViewModels/MainViewModel.cs` (ändern): `ConnectChoice.SavePassword`, Speichern nach Erfolg
- `tests/OVS.Tests/Client/SettingsTests.cs`, `MainViewModelTests.cs`, `UiSmokeTests.cs`, `ClientLogTests.cs` (ändern)

### Kontext

`Bookmark(Name, Host, Port, Nickname)` hat kein Passwort. Der Verbinden-Dialog fragt das Passwort jedes Mal ab. Das Client-Log schreibt nur "mit Passwort", nie das Passwort selbst.

### Acceptance Criteria

- [x] AC1: Der Verbinden-Dialog hat "Passwort speichern", standardmässig aus. Wählbar ist es nur mit einem eingegebenen Passwort und "Als Lesezeichen speichern".
- [x] AC2: Das Passwort steht nach erfolgreicher Verbindung DPAPI-verschlüsselt im Lesezeichen. In `settings.json` taucht der Klartext nicht auf. Bei `WrongPassword` wird nichts gespeichert.
- [x] AC3: Ein Lesezeichen mit gespeichertem Passwort füllt das Feld im Dialog vor. Ein nicht entschlüsselbarer Wert (anderer Benutzer, beschädigt) gilt als nicht gespeichert und bricht nichts.
- [x] AC4: Das Passwort erscheint in keinem Log.

### Tests (TDD)

1. `SettingsTests > "Password_Protected_RoundTrip_NoPlainTextInFile"` (AC2)
2. `SettingsTests > "Password_Garbage_TreatedAsNone"` (AC3)
3. `MainViewModelTests > "Connect_SavePassword_OnlyAfterSuccess"` gegen `TestServer` mit Passwort, einmal falsch, einmal richtig (AC2)
4. `UiSmokeTests`: Checkbox, aktiv nur mit Passwort und Lesezeichen, Vorbelegung (AC1, AC3)
5. `ClientLogTests`: das Passwort steht nicht im Log (AC4)

Testbefehl: `dotnet test --filter "FullyQualifiedName~SettingsTests|FullyQualifiedName~MainViewModelTests|FullyQualifiedName~UiSmokeTests|FullyQualifiedName~ClientLogTests"`

### Umsetzungsschritte

1. Schutz und Entschlüsseln mit Tests.
2. Dialog und Speichern nach Erfolg.

### Out of Scope

- Direkt verbinden per Klick auf ein Lesezeichen (Package 40)
- Identitätsschlüssel per DPAPI (A16)

---

## Package 40: Lesezeichen in der Seitenleiste

**Ziel:** Ohne Verbindung zeigt die Seitenleiste die Lesezeichen, und ein Klick verbindet direkt.

**Abhängigkeiten:** Package 39

**Betroffene Dateien:**
- `src/OVS.Client/Views/MainWindow.axaml`, `MainWindow.axaml.cs` (ändern): Lesezeichen in der Seitenleiste, Kontextmenü, Hauptbereich ohne Liste
- `src/OVS.Client/ViewModels/MainViewModel.cs` (ändern): `ConnectBookmarkAsync`, Passwortabfrage bei Bedarf, `EditBookmark`, `DeleteBookmark`
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (ändern): `Dialogs.AskPassword`, `Dialogs.EditBookmark`
- `src/OVS.Client/Views/SimpleDialogs.cs` (ändern): Overlays "Passwort eingeben" und "Lesezeichen bearbeiten"
- `tests/OVS.Tests/Client/MainViewModelTests.cs`, `UiSmokeTests.cs` (ändern)

### Kontext

Ohne Verbindung zeigt die Seitenleiste nur "Nicht verbunden", der Hauptbereich unter "Verbinden ..." die Lesezeichen als Kacheln. Ein Klick darauf öffnet den Verbinden-Dialog vorbelegt (`MainWindow.OnBookmarkClick`). Der Server antwortet auf ein fehlendes oder falsches Passwort mit `WrongPassword`.

### Acceptance Criteria

- [x] AC1: Ohne Verbindung zeigt die Seitenleiste die Lesezeichen mit Logo bzw. Buchstabe, Name und Nickname. Der Hauptbereich zeigt nur Logo, Status und "Verbinden ...".
- [x] AC2: Ein Klick verbindet sofort, mit gespeichertem Passwort, falls vorhanden. Der TOFU-Dialog erscheint wie bisher.
- [x] AC3: Bei `WrongPassword` fragt ein Overlay nach dem Passwort, mit "Passwort speichern". Richtig eingegeben verbindet es und speichert auf Wunsch. Abbrechen lässt den Client getrennt.
- [x] AC4: Das Kontextmenü eines Lesezeichens bietet "Verbinden", "Bearbeiten" und "Löschen". Löschen fragt nach. Bearbeiten ändert Name, Adresse, Port, Nickname und das gespeicherte Passwort (ändern oder entfernen). Alles bleibt nach einem Neustart erhalten.
- [x] AC5: Während einer Verbindung zeigt die Seitenleiste wie bisher die Channels.

### Tests (TDD)

1. `MainViewModelTests > "Bookmark_Connect_UsesSavedPassword"` (AC2)
2. `MainViewModelTests > "Bookmark_WrongSavedPassword_AsksAndSavesNew"` und `"Bookmark_AskPassword_Cancel_StaysDisconnected"` (AC3)
3. `MainViewModelTests > "Bookmark_EditAndDelete_Persist"` (AC4)
4. `UiSmokeTests > "Bookmarks_InSidebar_WhenDisconnected"`: Lesezeichen in der Seitenleiste, keine Kachelliste im Hauptbereich, Kontextmenü mit drei Einträgen, nach dem Verbinden die Channels (AC1, AC4, AC5)

Testbefehl: `dotnet test --filter "FullyQualifiedName~MainViewModelTests|FullyQualifiedName~UiSmokeTests"`

### Umsetzungsschritte

1. Verbinden per Lesezeichen mit Passwortabfrage, Tests gegen `TestServer`.
2. Bearbeiten und Löschen mit Tests.
3. Seitenleiste und Hauptbereich umbauen, Screenshots beider Designs prüfen.

### Out of Scope

- Lesezeichen sortieren

---

## Package 41: Tastenliste

**Ziel:** Tastenbelegungen sind eine frei erweiterbare Liste, in der eine Aktion auf mehreren Tasten liegen kann.

**Abhängigkeiten:** Package 29

**Betroffene Dateien:**
- `src/OVS.Client/ViewModels/SettingsViewModel.cs` (ändern): Liste statt `KeyRows`, Hinzufügen, Ändern, Löschen, Konfliktprüfung
- `src/OVS.Client/Views/SettingsView.axaml(.cs)` (ändern): Liste, leerer Zustand, Button "Tastenaktion hinzufügen"
- `src/OVS.Client/Views/SimpleDialogs.cs` (ändern): Overlay mit Aktion und Taste
- `src/OVS.Client/Input/KeyBindings.cs`, `KeyPoller.cs` (ändern, falls nötig): eine Aktion ist aktiv, solange eine ihrer Tasten gedrückt ist
- `src/OVS.Client/ViewModels/MainViewModel.cs` (ändern): `TalkHint` mit allen PTT-Tasten
- `tests/OVS.Tests/Client/KeyBindingTests.cs`, `SettingsTests.cs`, `MainViewModelTests.cs`, `UiSmokeTests.cs` (ändern)

### Kontext

`ClientSettings.KeyBindings` ist bereits eine Liste von `KeyBinding(Action, Chord)`. Die Oberfläche zeigt aber eine feste Zeile pro Aktion (`KeyBindingRow` für jedes Element von `KeyActions.All`) und damit genau eine Taste pro Aktion. `Conflict` erkennt heute dieselbe Taste auf zwei Zeilen.

### Acceptance Criteria

- [x] AC1: Ohne Belegung zeigt die Einstellungsseite "Noch keine Tastenaktionen" und den Button "Tastenaktion hinzufügen".
- [x] AC2: Der Button öffnet ein Overlay mit Dropdown (Push-to-Talk, Link-PTT, Push-to-Mute, Mikrofon an/aus, Ton an/aus) und "Taste festlegen". Speichern geht erst mit Aktion und Taste. Danach steht die Zeile in der Liste.
- [x] AC3: Jede Zeile hat "Ändern" (dasselbe Overlay vorbelegt) und "Löschen".
- [x] AC4: Dieselbe Aktion darf auf mehreren Tasten liegen, z. B. Push-to-Mute auf Maustaste 4 und 5. Beide wirken, und die Aktion endet erst, wenn keine ihrer Tasten mehr gedrückt ist.
- [x] AC5: Dieselbe Taste auf zwei verschiedenen Aktionen und doppelte Zeilen blockieren das Speichern der Einstellungen mit Hinweis.
- [x] AC6: Bestehende Profile zeigen ihre Belegungen unverändert als Liste. Unter dem eigenen Namen steht bei mehreren PTT-Tasten z. B. "PTT: Maus 4, Maus 5".

### Tests (TDD)

1. `KeyBindingTests > "SameActionTwoKeys_EitherHolds_EndsWhenBothUp"` (AC4)
2. `SettingsTests > "KeyList_StartsEmpty_AddEditRemove"` (AC1 bis AC3)
3. `SettingsTests > "KeyList_SameKeyTwoActions_BlocksSave"` und `"KeyList_SameActionTwoKeys_Saves"` (AC4, AC5)
4. `SettingsTests > "KeyList_ExistingProfile_ShownAsList"` (AC6)
5. `MainViewModelTests > "TalkHint_ListsAllPttKeys"` (AC6)
6. `UiSmokeTests`: leerer Zustand, Overlay öffnet sich im Hauptfenster (A20), Zeile mit "Ändern" und "Löschen" (AC1 bis AC3)

Testbefehl: `dotnet test --filter "FullyQualifiedName~KeyBindingTests|FullyQualifiedName~SettingsTests|FullyQualifiedName~MainViewModelTests|FullyQualifiedName~UiSmokeTests"`

### Umsetzungsschritte

1. Test für mehrere Tasten pro Aktion, rot oder schon grün, dann ggf. `KeyPoller` anpassen.
2. `SettingsViewModel` auf die Liste umbauen, Konfliktprüfung.
3. Overlay und Einstellungsseite, Screenshots beider Designs.

### Out of Scope

- Neue Aktionen

---

## Package 42: Client-Release per GitHub Actions

**Ziel:** Jeder Push auf `main` veröffentlicht nach grünen Tests die Client-exe als versioniertes GitHub-Release.

**Abhängigkeiten:** Package 19

**Betroffene Dateien:**
- `.github/workflows/release.yml` (neu)
- `Directory.Build.props` (ändern): Build-Zeit, Commit und CI-Kennzeichen als `AssemblyMetadata`
- `src/OVS.Shared/BuildInfo.cs` (neu): Rohdaten lesen, Version formatieren wie in PersonalEinsatzPlanung
- `src/OVS.Server/Program.cs` (ändern): `--version`, Version beim Start im Log
- `src/OVS.Client/ViewModels/SettingsViewModel.cs`, `Views/SettingsView.axaml` (ändern): Abschnitt "Über" mit Version, Commit, Build-Zeit
- `README.md` (ändern): Releases, Download
- `tests/OVS.Tests/Shared/BuildInfoTests.cs` (neu), `tests/OVS.Tests/Client/UiSmokeTests.cs` (ändern)

### Kontext

Es gibt keinen Workflow und keine Version, die exe baut man lokal mit `dotnet publish` (README). PersonalEinsatzPlanung (`build/buildDefines.ts`, `src/ui/app/buildInfo.ts`, `.github/workflows/deploy.yml`) bettet nur Rohdaten ein, formatiert daraus `DDMMYY.<stamp>` bzw. `dev.<stamp>` und legt pro Deploy ein Release `deploy-<sha7>` mit den Commit-Nachrichten an (A38). Die Client-Tests brauchen Windows (`GetAsyncKeyState`, NAudio).

### Acceptance Criteria

- [x] AC1: `BuildInfo.Format` liefert für CI-Builds `DDMMYY.<stamp>` und lokal `dev.<stamp>`. Der Stamp sind die Sekunden seit UTC-Mitternacht in Base36 mit genau 4 Stellen (00:00:00 = `0000`, 23:59:59 = `1unz`).
- [x] AC2: Die Build-Zeit kommt aus der Umgebungsvariable `OVS_BUILD_TIME`, sonst aus dem Build-Zeitpunkt. Commit und CI-Kennzeichen kommen aus `GITHUB_SHA` und `GITHUB_ACTIONS`.
- [x] AC3: `OVS.Server --version` gibt die Version aus. Der Server schreibt sie beim Start ins Log, die Einstellungen des Clients zeigen Version, Commit und Build-Zeit.
- [x] AC4: Der Workflow läuft bei Pull Requests und Pushes auf `main`: Tests auf `windows-latest`. Nur bei Pushes auf `main` baut er danach die exe (self-contained, eine Datei, ohne `.pdb`) mit einer einmal festgelegten `OVS_BUILD_TIME`.
- [x] AC5: Danach legt er das Release `deploy-<sha7>` an, Titel `OpenVoiceSpeak <Version>`, Notizen die Commit-Nachrichten seit dem letzten Release, Anhänge `OVS.Client.exe` und `OVS.Client.exe.sha256`. Rote Tests erzeugen kein Release.
- [ ] AC6 (manuell, offen bis zum ersten Push): Nach dem ersten Push läuft der Workflow grün, das Release ist ohne Anmeldung herunterladbar, und die exe zeigt dieselbe Version wie der Release-Titel.

### Tests (TDD)

1. `BuildInfoTests > "Format_CiBuild_DayMonthYearAndStamp"`, `"Format_LocalBuild_Dev"`, `"Stamp_MidnightAndLastSecond"` (AC1)
2. `BuildInfoTests > "FromMetadata_MissingValues_DevWithNow"` (AC2)
3. `UiSmokeTests`: Einstellungen zeigen die Version (AC3)
4. Manueller Check AC6 nach dem Push, Workflow-Syntax vorab mit `actionlint` (falls installiert)

Testbefehl: `dotnet test --filter "FullyQualifiedName~BuildInfoTests|FullyQualifiedName~UiSmokeTests"`

### Umsetzungsschritte

1. `BuildInfo` testgetrieben, dann `Directory.Build.props`.
2. `--version` und Anzeige im Client.
3. Workflow mit den Jobs `test`, `client`, `release`. Die Version für den Titel liefert `OVS.Server --version`, damit das Format nur an einer Stelle steht.

### Out of Scope

- Signieren der exe
- Update-Prüfung (Package 43), Server-Image (Package 44)

---

## Package 43: Update-Prüfung im Client

**Ziel:** Der Client erkennt neue Releases und installiert sie auf Wunsch selbst.

**Abhängigkeiten:** Package 42

**Betroffene Dateien:**
- `src/OVS.Client/Net/Updates.cs` (neu): `UpdateChecker` (neuestes Release über `api.github.com` lesen und mit dem eigenen Build vergleichen) und `UpdateInstaller` (Download, SHA-256-Prüfung, Selbstersetzung, Aufräumen)
- `src/OVS.Client/Settings/ClientSettings.cs` (ändern): `CheckForUpdates` (Standard an)
- `src/OVS.Client/ViewModels/MainViewModel.cs`, `SettingsViewModel.cs`, `Views/SettingsView.axaml`, `Views/SimpleDialogs.cs` (ändern): Prüfung beim Start, Button, Overlay
- `src/OVS.Client/Program.cs` (ändern): alte exe beim Start löschen
- `README.md` (ändern): Update und Hinweis auf die Anfrage an GitHub
- `tests/OVS.Tests/Client/UpdateTests.cs` (neu), `MainViewModelTests.cs`, `UiSmokeTests.cs` (ändern)

### Kontext

Ab Package 42 kennt der Client Commit und Build-Zeit, und jedes Release heisst `deploy-<sha7>` mit der exe und ihrer SHA-256-Datei. Das Repo ist öffentlich (A39), die Releases-API braucht also kein Token. Windows erlaubt, eine laufende exe umzubenennen, nicht aber sie zu überschreiben.

### Acceptance Criteria

- [x] AC1: Ein Update gilt als verfügbar, wenn das neueste Release einen anderen Commit hat, nach der eigenen Build-Zeit veröffentlicht wurde und beide Anhänge hat. `dev`-Builds prüfen nie.
- [x] AC2: Beim Start (wenn eingeschaltet) und per "Nach Updates suchen" fragt ein Overlay "Version X ist verfügbar. Jetzt installieren?" mit den Notizen. "Später" fragt erst beim nächsten Start wieder.
- [x] AC3: Nach "Installieren" lädt der Client die exe, prüft den Hash, ersetzt sich und startet die neue Version. Bei falschem Hash oder Abbruch bleibt die alte exe unverändert, mit Meldung.
- [x] AC4: Eine übrig gebliebene alte exe wird beim nächsten Start gelöscht.
- [x] AC5: Netzwerkfehler und Rate-Limit von GitHub stören nicht: nur ein Eintrag im Client-Log, beim Button zusätzlich eine Meldung.
- [ ] AC6 (manuell, offen bis zu zwei echten Releases): Echte Aktualisierung von einem Release auf das nächste unter Windows, inklusive SmartScreen-Hinweis.

### Tests (TDD)

1. `UpdateCheckerTests` mit einem Fake-`HttpMessageHandler`: `"NewerRelease_Offered"`, `"SameCommit_NotOffered"`, `"OlderRelease_NotOffered"`, `"MissingAsset_NotOffered"`, `"DevBuild_NeverAsks"`, `"HttpError_NoUpdate_Logged"` (AC1, AC5)
2. `UpdateInstallerTests` in einem Temp-Ordner: `"HashMismatch_NothingReplaced"`, `"Success_NewInPlace_OldRenamed"`, `"Cleanup_DeletesOldExe"` (AC3, AC4)
3. `MainViewModelTests > "StartupCheck_Prompt_LaterDoesNothing"` mit Fake-Checker (AC2)
4. `UiSmokeTests`: Overlay und Button in den Einstellungen (AC2)
5. Manueller Check AC6

Testbefehl: `dotnet test --filter "FullyQualifiedName~UpdateCheckerTests|FullyQualifiedName~UpdateInstallerTests|FullyQualifiedName~MainViewModelTests|FullyQualifiedName~UiSmokeTests"`

### Umsetzungsschritte

1. `UpdateChecker` testgetrieben.
2. `UpdateInstaller` testgetrieben, der Neustart hinter einer Schnittstelle, damit Tests nichts starten.
3. Einstellungen, Start-Prüfung, Overlay.

### Out of Scope

- Automatische Updates ohne Nachfrage, Delta-Updates, Update-Kanäle

---

## Package 44: Server-Image in der GitHub Container Registry

**Ziel:** Jedes Release stellt das Server-Image für amd64 und arm64 als öffentliches Package bereit, und Compose nutzt es direkt.

**Abhängigkeiten:** Package 42

**Betroffene Dateien:**
- `.github/workflows/release.yml` (ändern): Job `image` mit QEMU, Buildx, Login bei `ghcr.io`, Push für beide Architekturen
- `Dockerfile` (ändern): `OVS_BUILD_TIME` und Commit als Build-Argumente, Label `org.opencontainers.image.source`
- `docker-compose.yml` (ändern): `image: ghcr.io/rezondes/openvoicespeak-server:latest` statt `build`
- `docker-compose.build.yml` (neu): lokaler Build als Zusatzdatei
- `README.md` (ändern): Installation ohne `git clone`, Update mit `pull`, lokaler Build
- `PLAN.md` (ändern): A15 als ersetzt markieren

### Kontext

Das `Dockerfile` baut bereits für amd64 und arm64 (Cross-Compile über `$BUILDPLATFORM`). Die README beschreibt `git clone` und `docker compose up -d --build` auf dem Server. A15 schloss eine Registry bisher aus.

### Acceptance Criteria

- [x] AC1: Nach grünen Tests auf `main` baut der Workflow das Image für `linux/amd64` und `linux/arm64` und pusht es mit den Tags `latest` und der Version aus Package 42. Das Release entsteht erst, wenn exe und Image fertig sind.
- [x] AC2: `OVS.Server --version` im Image zeigt dieselbe Version wie die exe im selben Release.
- [x] AC3: `docker-compose.yml` zieht das fertige Image. `docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build` baut weiter lokal.
- [x] AC4: Die README beschreibt die Installation nur mit der Compose-Datei und das Update mit `docker compose pull && docker compose up -d`. Daten und Zertifikat bleiben im Volume.
- [ ] AC5 (manuell, offen bis zum ersten Push): Nach dem ersten Push und dem einmaligen Umstellen des Packages auf "public" (A39) klappt `docker pull ghcr.io/rezondes/openvoicespeak-server:latest` ohne Anmeldung auf amd64 und arm64. Der Fingerprint bleibt beim Wechsel vom lokal gebauten Image erhalten.

### Tests (TDD)

Die Änderungen sind Workflow und Konfiguration, automatisiert prüfbar ist wenig:

1. `docker compose config` mit und ohne `docker-compose.build.yml` läuft ohne Fehler, lokal vor dem Commit (AC3)
2. Lokaler Build mit `--build-arg OVS_BUILD_TIME=...`, danach `docker run --rm <image> --version` (AC2)
3. Manueller Check AC1 und AC5 nach dem Push

Testbefehl: `docker compose -f docker-compose.yml -f docker-compose.build.yml config` und `dotnet test` (unverändert grün)

### Umsetzungsschritte

1. `Dockerfile` und Compose-Dateien, lokal prüfen.
2. Job `image` im Workflow, `release` hängt von `client` und `image` ab.
3. README, A15 in PLAN.md als ersetzt markieren.

### Out of Scope

- Docker Hub
- Signieren der Images

---

## Package 45: Lokalisierung: Grundlage

**Ziel:** Der Client hat eine Sprachwahl (Wie Windows, Deutsch, English), und alle Texte aus dem C#-Code gibt es auf Deutsch und Englisch.

**Abhängigkeiten:** Package 26

**Betroffene Dateien:**
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (neu): alle Nutzertexte, Deutsch als neutrale Sprache
- `src/OVS.Client/Localization/Language.cs` (neu): `AppLanguage` (System, German, English), Auflösen der Windows-Sprache, Setzen der UI-Kultur
- `src/OVS.Client/OVS.Client.csproj` (ändern): stark typisierte Ressourcenklasse `Strings`, `NeutralLanguage` de
- `src/OVS.Client/Program.cs` (ändern): Sprache aus den Einstellungen setzen, bevor Avalonia startet
- `src/OVS.Client/Settings/ClientSettings.cs` (ändern): `Language`
- `src/OVS.Client/ViewModels/SettingsViewModel.cs`, `Views/SettingsView.axaml` (ändern): Auswahl "Sprache" mit Hinweis "Wirkt nach einem Neustart"
- `src/OVS.Client/ErrorTexts.cs` (ändern): Fehlertexte und Rechtenamen aus den Ressourcen, Detail-Regel nach A45
- `src/OVS.Client/Input/KeyBindings.cs`, `Views/SimpleDialogs.cs`, `ViewModels/*.cs`, `Views/*.axaml.cs` (ändern): Texte aus den Ressourcen
- `tests/OVS.Tests/Client/LocalizationTests.cs` (neu), `tests/OVS.Tests/TestSupport/` (ändern): Testlauf fest auf Deutsch

### Kontext

Alle Texte sind heute deutsch und fest im Code: rund 300 in C# (`ErrorTexts`, `PermissionLabels`, `KeyActions.Label`, `SimpleDialogs`, Status und Hinweise in `MainViewModel`, `ServerViewModel`, `ChatViewModel`, `AdminViewModel`, `LinkMatrixViewModel`, `SettingsViewModel`) und rund 200 in den XAML-Views (Package 46). Fehler zeigt `ErrorTexts.For(code, detail)` als "Text (Detail)", das Detail kommt vom Server auf Deutsch (z. B. "Der Standard-Channel lässt sich nicht begrenzen."). Kick und Bann schicken den eingegebenen Grund als Detail.

### Acceptance Criteria

- [x] AC1: `Strings.resx` und `Strings.en.resx` haben dieselben Schlüssel, kein Wert ist leer, und die Platzhalter (`{0}`, `{1}`) stimmen je Schlüssel überein.
- [x] AC2: Ohne Einstellung folgt die Sprache Windows: Deutsch bei einer deutschen Anzeigesprache, sonst Englisch. Die Einstellung "Deutsch" bzw. "English" überstimmt das.
- [x] AC3: In den Einstellungen gibt es "Sprache" mit "Wie Windows", "Deutsch", "English" und dem Hinweis, dass der Wechsel nach einem Neustart wirkt. Die Wahl bleibt gespeichert.
- [x] AC4: Fehlertexte, Rechtenamen, Tastenaktionen, alle Dialoge und alle Status- und Hinweistexte aus dem C#-Code erscheinen in der gewählten Sprache.
- [x] AC5: Server-Details erscheinen nicht mehr in der Oberfläche, nur im Client-Log. Kick- und Bann-Grund bleiben sichtbar.
- [x] AC6: Client-Log und Debug-API bleiben deutsch und unverändert.

### Tests (TDD)

1. `LocalizationTests > "Resources_SameKeys_NoneEmpty_SamePlaceholders"` (AC1)
   - Gegeben: beide `.resx` als XML
   - Erwartet: gleiche Schlüsselmenge, keine leeren Werte, gleiche Platzhalter je Schlüssel
2. `LocalizationTests > "Language_System_GermanOrEnglish"` als Theory: `de-DE`, `de-AT` ergeben Deutsch, `en-US`, `fr-FR` ergeben Englisch, eine feste Wahl überstimmt (AC2)
3. `LocalizationTests > "ErrorTexts_English"`: mit englischer Kultur liefert `ErrorTexts.For(Codes.PermissionDenied)` den englischen Text (AC4)
4. `LocalizationTests > "ServerDetail_HiddenExceptKickAndBan"` (AC5)
5. `SettingsTests > "Language_RoundTrip"` und `UiSmokeTests`: Auswahl "Sprache" auf der Einstellungsseite (AC3)
6. Die ganze bestehende Suite läuft weiter grün mit fest deutscher Kultur (AC4, AC6)

Testbefehl: `dotnet test --filter "FullyQualifiedName~LocalizationTests|FullyQualifiedName~SettingsTests|FullyQualifiedName~UiSmokeTests"`, danach `dotnet test`

### Umsetzungsschritte

1. Testlauf auf deutsche Kultur festlegen, `Strings.resx` mit Parity-Test, stark typisierte Klasse.
2. Sprachwahl in Einstellungen und `Program.cs`.
3. Texte aus dem C#-Code Datei für Datei in die Ressourcen, englische Übersetzung dazu, Detail-Regel in `ErrorTexts`.

### Out of Scope

- XAML-Texte (Package 46)
- Server-Logs, Client-Log, Debug-API (A45)
- Sprachwechsel ohne Neustart

---

## Package 46: Lokalisierung: Oberfläche

**Ziel:** Alle XAML-Views zeigen ihre Texte in der gewählten Sprache, kein fest verdrahteter Text bleibt übrig.

**Abhängigkeiten:** Package 45

**Betroffene Dateien:**
- `src/OVS.Client/Views/MainWindow.axaml`, `SettingsView.axaml`, `AdminView.axaml`, `ChatView.axaml`, `TitleBar.axaml` (ändern): `Text`, `Header`, `Content`, `ToolTip.Tip`, `Watermark`, `AutomationProperties.Name` und `StringFormat` aus `Strings`
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (ändern)
- `tests/OVS.Tests/Client/LocalizationTests.cs`, `UiSmokeTests.cs` (ändern)

### Kontext

Rund 200 Texte stehen fest in den XAML-Dateien, darunter auch Screenreader-Namen (`AutomationProperties.Name`) und Formatierungen wie `StringFormat='Mit {0} verbinden'`.

### Acceptance Criteria

- [x] AC1: Keine XAML-Datei enthält mehr einen festen Nutzertext. Erlaubt bleiben nur Bindungen, Ressourcen und eine kurze Ausnahmeliste (z. B. der Produktname "OpenVoiceSpeak").
- [x] AC2: Mit englischer Kultur zeigt das Hauptfenster samt Einstellungen, Verwaltung, Chat und allen Dialogen keinen deutschen Text aus den Ressourcen.
- [x] AC3: Screenreader-Namen und Tooltips sind ebenfalls übersetzt.
- [x] AC4 (manuell, geprüft am 28.09.2026 mit dem Screenshot-Werkzeug): Screenshots in beiden Sprachen und beiden Designs zeigen keine abgeschnittenen oder überlaufenden englischen Texte.

### Tests (TDD)

1. `LocalizationTests > "Xaml_NoHardcodedText"` (AC1)
   - Gegeben: alle `.axaml` unter `src/OVS.Client`
   - Erwartet: kein Attribut `Text`, `Header`, `Content`, `ToolTip.Tip`, `Watermark`, `AutomationProperties.Name` mit einem Wert außerhalb von `{...}` und der Ausnahmeliste
2. `UiSmokeTests > "Windows_English_NoGermanResourceText"` (AC2, AC3)
   - Gegeben: englische Kultur, Hauptfenster mit Fake-Server, alle Seiten und Dialoge wie in `ExercisePagesAndDialogs`
   - Erwartet: kein angezeigter Text, Tooltip oder Automation-Name entspricht einem deutschen Ressourcenwert, der sich vom englischen unterscheidet
3. Manueller Check AC4 mit dem Screenshot-Werkzeug

Testbefehl: `dotnet test --filter "FullyQualifiedName~LocalizationTests|FullyQualifiedName~UiSmokeTests"`

### Umsetzungsschritte

1. `Xaml_NoHardcodedText` rot sehen.
2. View für View auf `{x:Static}` umstellen, bis der Test grün ist.
3. Englischer UI-Test, Screenshots beider Sprachen prüfen.

### Out of Scope

- Rechts-nach-links-Sprachen, weitere Sprachen

---

## Package 47: Sounds

**Ziel:** Der Client spielt bei Mikrofon, Ton, Verbindung, Channel und Privatnachricht kurze Töne, mit Gesamtlautstärke und "Alle Sounds aus".

**Abhängigkeiten:** Package 46

**Betroffene Dateien:**
- `src/OVS.Client/Audio/Sounds.cs` (neu): `SoundEvent`, Synthese der Standardtöne, `SoundQueue` (mischt laufende Töne in einen Ausgabepuffer)
- `src/OVS.Client/Audio/AudioEngine.cs` (ändern): `PlaySound`, Mischen in `PlaybackLoop`, Regel bei "Ton aus"
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (ändern): Ereignis `SoundRequested` aus eingehenden Nachrichten und eigenen Aktionen
- `src/OVS.Client/ViewModels/MainViewModel.cs` (ändern): Verbunden, Getrennt, Weiterleitung an die Audio-Engine
- `src/OVS.Client/Settings/ClientSettings.cs`, `ViewModels/SettingsViewModel.cs`, `Views/SettingsView.axaml` (ändern): `SoundsEnabled`, `SoundVolume`, Abschnitt "Sounds"
- `src/OVS.Client/Localization/Strings*.resx` (ändern), `src/OVS.Client/Debug/DebugApi.cs` (ändern): zuletzt gespielte Töne in `/state`
- `tests/OVS.Tests/Client/SoundTests.cs` (neu), `ServerViewModelTests.cs`, `SettingsTests.cs`, `UiSmokeTests.cs` (ändern)

### Kontext

Die Audio-Engine (`Audio/AudioEngine.cs`) mischt eingehende Sprache in `PlaybackLoop` und gibt sie über WASAPI auf dem gewählten Lautsprecher aus. Töne gibt es nicht. Die Auslöser liegen in `ServerViewModel.Apply` (`UserJoined`, `UserUpdated`, `UserLeft`, `ChatMessage`), in `ToggleMute`/`ToggleDeafen` und in `MainViewModel.ConnectAsync`/`OnDisconnected`.

### Acceptance Criteria

- [x] AC1: Jedes Ereignis aus A46 hat einen eigenen, hörbar unterschiedlichen Standardton von höchstens einer Sekunde, im Client erzeugt.
- [x] AC2: Die Töne kommen aus dem gewählten Lautsprecher, mischen sich mit laufender Sprache und blockieren sie nicht.
- [x] AC3: Die Ereignisse lösen genau einmal aus: eigene Mute- und Ton-Wechsel (Button und Taste), Verbinden, Trennen, eigener Channelwechsel, jemand betritt oder verlässt den eigenen Channel, Server-Mute gegen einen selbst, Verschieben durch jemand anderen, eingehende Privatnachricht (nicht die eigene).
- [x] AC4: Bei "Ton aus" spielen nur die Töne für Mikrofon und Ton.
- [x] AC5: In den Einstellungen gibt es "Sounds" mit Gesamtlautstärke und "Alle Sounds aus". Beides wirkt sofort und bleibt gespeichert.
- [x] AC6: Die Debug-API zeigt die zuletzt gespielten Töne in `/state`.

### Tests (TDD)

1. `SoundTests > "Defaults_EveryEvent_ShortAudibleDistinct"` (AC1)
2. `SoundTests > "Queue_MixesIntoBuffer_WithVolume_Overlaps"` (AC2)
3. `ServerViewModelTests > "SoundEvents_FromMessages"` als Theory je Nachricht, dazu `"SoundEvents_OwnChanges_NotDoubled"` (AC3)
4. `SoundTests > "Deafened_OnlyOwnMicAndSoundTones"` (AC4)
5. `SettingsTests > "Sounds_GlobalSettings_RoundTrip"` und `UiSmokeTests`: Abschnitt "Sounds" (AC5)
6. `DebugApiTests > "Sounds_ListedInState"` mit zwei echten Clients (AC3, AC6)
7. Manueller Check (offen, braucht Ohren): Töne klingen angenehm und sind im Spiel hörbar, nicht zu laut

Testbefehl: `dotnet test --filter "FullyQualifiedName~SoundTests|FullyQualifiedName~ServerViewModelTests|FullyQualifiedName~SettingsTests|FullyQualifiedName~DebugApiTests|FullyQualifiedName~UiSmokeTests"`

### Umsetzungsschritte

1. Synthese und `SoundQueue` testgetrieben.
2. Ereignisse aus `ServerViewModel` und `MainViewModel`, Tests je Nachricht.
3. Einbau in die Audio-Engine, Einstellungen, Debug-API, manueller Hörtest.

### Out of Scope

- Pro-Sound-Einstellungen und eigene Dateien (Package 48)
- Sprachansagen wie bei TeamSpeak

---

## Package 48: Sounds anpassen

**Ziel:** Jeder Sound hat eine eigene Lautstärke, lässt sich stumm schalten und durch eine eigene Datei ersetzen.

**Abhängigkeiten:** Package 47

**Betroffene Dateien:**
- `src/OVS.Client/Audio/SoundImport.cs` (neu): `SoundImport` (WAV oder MP3 prüfen, höchstens 5 Sekunden, als 48 kHz mono ins Profil) und `SoundLibrary` (eigene Datei oder Standardton, Aufräumen nach dem Speichern)
- `src/OVS.Client/Settings/ClientSettings.cs` (ändern): `Sounds` je Ereignis mit Lautstärke, stumm, Dateiname
- `src/OVS.Client/Audio/Sounds.cs`, `AudioEngine.cs` (ändern): eigene Datei statt Standardton, Lautstärke je Sound
- `src/OVS.Client/ViewModels/SettingsViewModel.cs`, `Views/SettingsView.axaml(.cs)` (ändern): eine Zeile je Sound mit Abspielen, Lautstärke, stumm, "Datei wählen ...", "Zurücksetzen"
- `src/OVS.Client/Localization/Strings*.resx` (ändern)
- `tests/OVS.Tests/Client/SoundImportTests.cs` (neu), `SoundTests.cs`, `SettingsTests.cs`, `UiSmokeTests.cs` (ändern)

### Kontext

Nach Package 47 gibt es feste Standardtöne und nur globale Einstellungen. Die Windows-Dateiauswahl gibt es schon für das Server-Logo (`AdminView.OnPickIcon`), die Umrechnung von Audio (`LinearResampler`, `ToMono`) im Aufnahmepfad.

### Acceptance Criteria

- [x] AC1: Jeder Sound hat in den Einstellungen eine Zeile mit Name, "Abspielen", Lautstärke (0 bis 100 %), "Stumm", "Datei wählen ..." und "Zurücksetzen".
- [x] AC2: Eine gewählte WAV- oder MP3-Datei bis 5 Sekunden ersetzt den Standardton. Sie liegt danach als Kopie im Profil unter `sounds/`, das Original darf verschwinden.
- [x] AC3: Zu lange, unlesbare oder fremde Dateien werden mit einem verständlichen Hinweis abgelehnt, der bisherige Ton bleibt.
- [x] AC4: "Zurücksetzen" stellt Standardton und 100 % wieder her. Die Kopie verschwindet beim Speichern der Einstellungen, so bleibt sie bei "Abbrechen" erhalten.
- [x] AC5: Lautstärke und "Stumm" je Sound wirken zusätzlich zur Gesamtlautstärke und zu "Alle Sounds aus". Alles bleibt nach einem Neustart erhalten.
- [x] AC6: Fehlt die kopierte Datei später, spielt der Standardton, und das Client-Log vermerkt es.

### Tests (TDD)

1. `SoundImportTests > "Wav_UpToFiveSeconds_Accepted_Copied"`, `"TooLong_Rejected"`, `"NotAudio_Rejected"` mit selbst erzeugten WAV-Dateien (AC2, AC3)
2. `SoundTests > "CustomSound_ReplacesDefault_MissingFileFallsBack"` (AC2, AC6)
3. `SoundTests > "Volume_PerSoundTimesGlobal_MutedSilent"` (AC5)
4. `SettingsTests > "Sounds_PerEvent_RoundTrip_ResetDeletesCopy"` (AC4, AC5)
5. `UiSmokeTests`: eine Zeile je Sound mit allen Bedienelementen, beide Designs (AC1)
6. Manueller Check (offen, braucht Ohren): eine echte MP3-Datei auswählen und hören

Testbefehl: `dotnet test --filter "FullyQualifiedName~SoundImportTests|FullyQualifiedName~SoundTests|FullyQualifiedName~SettingsTests|FullyQualifiedName~UiSmokeTests"`

### Umsetzungsschritte

1. `SoundImport` testgetrieben.
2. Einstellungen je Sound und Wiedergabe mit Lautstärke und Ersatzdatei.
3. Einstellungsseite, Screenshots beider Designs, manueller Hörtest.

### Out of Scope

- Soundpakete zum Teilen, weitere Formate als WAV und MP3

---

## Package 49: Website

**Ziel:** Eine zweisprachige Seite auf GitHub Pages stellt die App für Nutzer vor und wird mit jedem Release neu ausgeliefert.

**Abhängigkeiten:** Package 42, 46

**Betroffene Dateien:**
- `website/package.json`, `package-lock.json`, `vite.config.ts`, `tsconfig.json`, `index.html` (neu): React, Vite, TypeScript, Vitest, Basis-Pfad `/OpenVoiceSpeak/`
- `website/src/main.tsx`, `App.tsx`, `i18n.ts`, `sections/*.tsx`, `styles.css` (neu): Hero, Vorteile, Für wen, Screenshots, Installation, Eigener Server, Footer
- `website/src/*.test.tsx` (neu)
- `website/public/screenshots/*.png`, `website/public/logo.svg` (neu)
- `.github/workflows/release.yml` (ändern): Website-Tests bei jedem Lauf, Job `pages` nach dem Release
- `.gitignore` (ändern): `website/node_modules`, `website/dist`
- `README.md` (ändern): Link zur Website

### Kontext

Die README richtet sich an Entwickler und Serverbetreiber. Releases heißen `deploy-<sha7>` mit Titel `OpenVoiceSpeak <Version>` (Package 42), die neueste exe liegt stabil unter `https://github.com/Rezondes/OpenVoiceSpeak/releases/latest/download/OVS.Client.exe`. Node 24 und npm 12 sind installiert. GitHub Pages steht im Repo bereits auf "GitHub Actions". Vorbild für den Pages-Job ist `.github/workflows/deploy.yml` in PersonalEinsatzPlanung.

### Acceptance Criteria

- [x] AC1: Die Seite ist ein OnePager mit Hero (Name, Kernaussage, Download-Button, Versionsnummer), Vorteilen, "Für wen", Screenshots, Installation in drei Schritten mit SmartScreen-Hinweis, "Eigenen Server betreiben" mit Link zur README und Footer.
- [x] AC2: Der Download-Button lädt direkt die neueste `OVS.Client.exe`. Die angezeigte Version ist die des Releases, mit dem die Seite gebaut wurde.
- [x] AC3: Deutsch und Englisch mit Umschalter. Standard nach Browsersprache (Deutsch bei `de`, sonst Englisch), die Wahl bleibt im Browser gespeichert. Beide Sprachen haben dieselben Texte.
- [x] AC4: Die Seite funktioniert von 375 px Breite an ohne waagerechtes Scrollen, in hellem und dunklem Design nach Systemeinstellung, mit Tastatur bedienbar und sinnvollen Alt-Texten.
- [x] AC5: Pull Requests bauen und testen die Seite, nach jedem Release auf `main` wird sie neu auf GitHub Pages ausgeliefert.
- [ ] AC6 (manuell, nach dem Push): Die Seite ist unter `https://rezondes.github.io/OpenVoiceSpeak/` erreichbar, der Download funktioniert, Lighthouse zeigt bei Barrierefreiheit mindestens 90.

### Tests (TDD)

1. `website/src/App.test.tsx > "Hero shows version and direct download link"` (AC1, AC2)
2. `website/src/i18n.test.ts > "same keys in German and English"` und `"browser language picks German or English"` (AC3)
3. `website/src/App.test.tsx > "language toggle switches texts and is remembered"` (AC3)
4. `website/src/App.test.tsx > "every section has a heading, every image an alt text"` (AC1, AC4)
5. Manueller Check: Ansicht bei 375 px und am Desktop, hell und dunkel (AC4)
6. Manueller Check AC6 nach dem Push

Testbefehl: `cd website && npm test`, Build: `cd website && npm run build`

### Umsetzungsschritte

1. Vite-Projekt anlegen, i18n mit Tests, Hero mit Version und Download testgetrieben.
2. Übrige Abschnitte, Gestaltung mit `ui-ux-pro-max`, Screenshots mit dem Screenshot-Werkzeug in beiden Sprachen erzeugen.
3. Workflow: Tests der Seite im Job `test`, Job `pages` nach `release` mit der Version als Build-Variable.

### Out of Scope

- Eigene Domain, Analytics, Blog oder Changelog-Seite

---

## Package 50: Stimmen lauter

**Ziel:** Andere Stimmen kommen beim Zuhörer standardmässig doppelt so laut an, ohne bei lauten Stellen zu verzerren.

**Abhängigkeiten:** Package 13, 17

**Betroffene Dateien:**
- `src/OVS.Client/Audio/Mixer.cs` (ändern): `DefaultVoiceBoost = 2f`, Verstärkung vor `Volume`, Begrenzer statt `Math.Clamp`
- `src/OVS.Client/Audio/Mixer.cs` (ändern): neue kleine Klasse `SoftLimiter` in derselben Datei
- `src/OVS.Client/Audio/CapturePipeline.cs` (ändern): `ToMono` nimmt bei einseitigem Signal den lauteren Kanal (A53)
- `tests/OVS.Tests/Client/ReceivePathTests.cs` (ändern)
- `tests/OVS.Tests/Client/SendPathTests.cs` (ändern)
- `README.md` (ändern): Hinweis zur Verstärkung bei den Client-Einstellungen

### Kontext

Im Test mit anderen Leuten waren alle hörbar, aber sehr leise. Die Wiedergabe hat heute genau einen Faktor: `Mixer.Tick()` summiert die dekodierten Frames aller Sprecher, multipliziert mit `Volume` (Regler "Lautstärke", 0 bis 100 %, `ClientSettings.OutputVolume`) und schneidet mit `Math.Clamp` hart auf ±1 ab. Eine Verstärkung gibt es nur beim Sender (`CapturePipeline.Gain`, Regler "Mikrofonverstärkung", Standard 100 %).

Dazu kommt eine wahrscheinliche Ursache auf der Senderseite: `CapturePipeline.ToMono` mittelt die Kanäle. Viele Audio-Interfaces und manche Headsets melden sich als Stereo, liefern aber nur auf einem Kanal Signal. Dann halbiert der Durchschnitt den Pegel (−6 dB).

`AudioEngine.Mix()` misst `LastOutputLevelDb` nach dem Mixer und vor den Tönen, die Debug-API gibt den Wert aus. Damit lässt sich die Änderung auch am echten Gerät nachmessen.

### Acceptance Criteria

- [x] AC1: Ein einzelner leiser Sprecher (Sinus mit Spitze 0,2) kommt bei Lautstärke 100 % mit etwa doppelter Amplitude an (Pegel +6 dB ± 0,5 dB gegenüber vorher).
- [x] AC2: Der Regler "Lautstärke" wirkt weiter linear auf die verstärkte Stimme: 50 % ergibt den Pegel von vor Package 50.
- [x] AC3: Laute Signale (Spitze nach Verstärkung über 1) werden nicht hart abgeschnitten: keine Probe liegt über 0,95, die Kurvenform bleibt rund (höchstens 1 % der Proben eines Frames liegen auf der Grenze), und nach dem Ende der lauten Stelle ist die volle Verstärkung nach spätestens 300 ms zurück.
- [x] AC4: Signale unterhalb der Schwelle bleiben unverändert (Abweichung unter 0,001), der Begrenzer färbt normale Sprache also nicht.
- [x] AC5: Ein Stereo-Mikrofon mit Signal nur auf einem Kanal wird mit vollem Pegel übernommen. Stereo mit gleichen Kanälen und Mono verhalten sich wie bisher.
- [ ] AC6 (manuell): Test mit mindestens zwei echten Clients über den Docker-Server: Die anderen sind bei Standardeinstellungen deutlich lauter als vorher, auch zwei gleichzeitig Sprechende verzerren nicht. Die Debug-API zeigt beim Sprechen einen um etwa 6 dB höheren `LastOutputLevelDb` als eine Version vor Package 50.

### Tests (TDD)

Reihenfolge: Test schreiben -> rot -> minimal implementieren -> grün -> refactoren.

1. `ReceivePathTests > "Mixer_DefaultBoost_DoublesQuietVoice"` (AC1)
   - Gegeben: ein Sprecher mit Sinus 0,2, einmal durch `new Mixer()`, einmal als Referenz direkt dekodiert
   - Erwartet: Pegel des Mixer-Frames = Referenz + 6 dB ± 0,5 dB, Spitze etwa 0,4
2. `ReceivePathTests > "Mixer_Volume_ScalesBoostedVoice"` (AC2)
   - Gegeben: derselbe Sprecher, `Volume = 0.5f`
   - Erwartet: Pegel gleich der Referenz ± 0,5 dB
3. `ReceivePathTests > "Limiter_LoudInput_NoHardClipping_RecoversAfterwards"` (AC3)
   - Gegeben: `SoftLimiter` mit Frames eines Sinus mit Spitze 1,6, danach Frames mit Spitze 0,4
   - Erwartet: alle Proben höchstens 0,95, höchstens 1 % der Proben eines Frames auf 0,95 ± 0,001, spätestens nach 15 leisen Frames (300 ms) wieder Faktor 1
4. `ReceivePathTests > "Limiter_BelowThreshold_Untouched"` (AC4)
   - Gegeben: Sinus mit Spitze 0,5
   - Erwartet: Ausgabe gleich Eingabe (Abweichung unter 0,001)
5. `ReceivePathTests > "Mixer_SumsAndClamps"` und `"Mixer_LoudInput_ClampedToUnit"` anpassen: Summe zweier Sprecher = 2 × einzeln bis zur Schwelle, Grenze 0,95 statt 1
6. `SendPathTests > "ToMono_StereoSignalOnOneChannel_KeepsFullLevel"` (AC5)
   - Gegeben: Float-Stereo, links Sinus 0,5, rechts 0 (und umgekehrt)
   - Erwartet: Mono-Spitze 0,5 statt 0,25
7. `SendPathTests > "ToMono_FloatStereo_Averages"` bleibt grün: gleiche oder ähnlich laute Kanäle werden weiter gemittelt (AC5)
8. Manueller Check AC6 mit zwei Clients und der Debug-API

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Tests 1, 2 schreiben (rot), `Mixer.DefaultVoiceBoost` einführen und in `Tick()` vor `Volume` anwenden.
2. Tests 3, 4 schreiben (rot), `SoftLimiter` mit Schwelle 0,95, sofortiger Absenkung, Rampe innerhalb des Frames und etwa 200 ms Rückkehr umsetzen. `Tick()` nutzt ihn statt `Math.Clamp`. Tests 5 anpassen.
3. Test 6 schreiben (rot), `ToMono` nach A53 umbauen, Test 7 grün halten.
4. README ergänzen (Stimmen werden beim Empfang verstärkt, "Lautstärke" regelt das Ganze).
5. Manueller Check AC6.

### Out of Scope

- Pegelautomatik je Sprecher
- Höherer Standard für die Mikrofonverstärkung beim Sender
- Regler "Lautstärke" über 100 % (A54)
- Lautstärke je Nutzer (Package 51)

---

## Package 51: Lautstärke je Nutzer

**Ziel:** Jeder kann die Lautstärke einzelner anderer Nutzer zwischen 0 und 200 % einstellen, und die Einstellung bleibt je Person erhalten.

**Abhängigkeiten:** Package 50

**Betroffene Dateien:**
- `src/OVS.Client/Audio/Mixer.cs` (ändern): Faktor je Sprecher (`SetSpeakerGains`)
- `src/OVS.Client/Settings/ClientSettings.cs` (ändern): `UserVolumes` (Fingerprint -> Faktor), `VolumeFor`, Begrenzung in `Clamp()`
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (ändern): `UserViewModel.VolumePercent`, `IsVolumeChanged`, `IsLocallyMuted`, `VolumeText`, `ResetVolumeCommand`
- `src/OVS.Client/ViewModels/MainViewModel.cs` (ändern): Nutzer-Lautstärken an den Mixer geben, bei Änderung speichern ohne `ApplySettings`
- `src/OVS.Client/Views/MainWindow.axaml`, `MainWindow.axaml.cs` (ändern): Regler und "Auf 100 % zurücksetzen" im Kontextmenü, Icons im Baum
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (ändern): neue Texte
- `tests/OVS.Tests/Client/ReceivePathTests.cs`, `SettingsTests.cs`, `ServerViewModelTests.cs`, `MainViewModelTests.cs`, `UiSmokeTests.cs` (ändern)
- `README.md` (ändern): Abschnitt Client-Bedienung

### Kontext

Der `Mixer` kennt Sprecher nur über die Session-ID (`Push(speakerId, ...)`), alle werden gleich laut summiert. Personen werden über ihren Fingerprint erkannt (`UserViewModel.Fingerprint`, gleich auf allen Servern). Das Kontextmenü eines Nutzers steht in `MainWindow.axaml` im `DataTemplate` für `vm:UserViewModel` (Privatnachricht, Verschieben, Server-Stumm, Kicken, Bannen). Geänderte Einstellungen laufen heute über `MainViewModel.ApplySettings`, das auch `AudioEngine.Configure` aufruft und damit die Geräte neu startet. Für einen Schieberegler ist das zu schwer, deshalb geht die Nutzer-Lautstärke direkt an den Mixer und wird nur gespeichert.

### Acceptance Criteria

- [x] AC1: Im Kontextmenü eines anderen Nutzers gibt es einen Schieberegler "Lautstärke" von 0 bis 200 % in 5er-Schritten mit Prozentanzeige, Standard 100 %, dazu "Auf 100 % zurücksetzen". Das Menü bleibt beim Ziehen offen, der Regler geht auch mit den Pfeiltasten. Beim eigenen Eintrag gibt es beides nicht.
- [x] AC2: Die Einstellung wirkt sofort und nur auf diese Person: 200 % verdoppelt, 50 % halbiert, 0 % macht sie für mich stumm. Andere Sprecher bleiben unverändert, die Audiogeräte werden nicht neu gestartet. Faktoren nach A55, der Begrenzer aus Package 50 wirkt danach.
- [x] AC3: Die Einstellung hängt am Fingerprint: Sie bleibt nach Neustart, nach erneutem Verbinden (neue Session-ID) und auf einem anderen Server mit derselben Person erhalten. In `settings.json` stehen nur Personen, deren Lautstärke von 100 % abweicht. Werte ausserhalb 0 bis 2 werden beim Laden begrenzt.
- [x] AC4: Im Channel-Baum zeigt ein Icon mit Tooltip, dass die Lautstärke einer Person nicht 100 % ist ("Lautstärke 150 %"), bei 0 % der durchgestrichene Lautsprecher mit "Für dich stumm". Bei 100 % ist kein Icon zu sehen.
- [x] AC5: Alle neuen Texte gibt es auf Deutsch und Englisch.
- [ ] AC6 (manuell): Mit zwei echten Clients eine Person auf 0, 50 und 200 % stellen und hören, dass nur sie leiser bzw. lauter wird.

### Tests (TDD)

Reihenfolge: Test schreiben -> rot -> minimal implementieren -> grün -> refactoren.

1. `ReceivePathTests > "Mixer_SpeakerGain_OnlyThatSpeaker"` (AC2)
   - Gegeben: zwei Sprecher mit gleichem leisen Sinus, `SetSpeakerGains({1: 2f})`, danach `{1: 0f}`
   - Erwartet: Sprecher 1 hat die doppelte Amplitude bzw. ist still, Sprecher 2 unverändert, ohne Eintrag gilt 1
2. `SettingsTests > "UserVolumes_RoundTrip_OnlyNonDefault_Clamped"` (AC3)
   - Gegeben: `UserVolumes` mit 1,5 für A, 1,0 für B, 7 für C, speichern und laden
   - Erwartet: A = 1,5, B fehlt in der Datei, C = 2, `VolumeFor(unbekannt)` = 1
3. `ServerViewModelTests > "UserVolume_ShownPerUser_NotForSelf"` (AC1, AC4)
   - Gegeben: Snapshot mit mir und zwei anderen, Lautstärke für einen auf 150 %, für einen auf 0 %
   - Erwartet: `VolumePercent`, `IsVolumeChanged`, `IsLocallyMuted` und `VolumeText` passend, der eigene Eintrag bietet keinen Regler an
4. `MainViewModelTests > "UserVolume_ChangesMixer_SavesByFingerprint_NoDeviceRestart"` (AC2, AC3)
   - Gegeben: verbundener Fake-Server, `VolumePercent` einer Person auf 200 setzen
   - Erwartet: der Mixer hat für ihre Session-ID Faktor 2, `settings.json` enthält ihren Fingerprint, `Configure` wurde nicht erneut aufgerufen. Nach neuem Snapshot mit anderer Session-ID gilt der Faktor für die neue ID. "Auf 100 % zurücksetzen" entfernt den Eintrag.
5. `UiSmokeTests > "UserContextMenu_VolumeSlider_OnlyForOthers"` (AC1, AC4)
   - Gegeben: Hauptfenster mit Fake-Server
   - Erwartet: Das Kontextmenü eines anderen Nutzers enthält einen `Slider` (0 bis 200, Schritt 5) mit Namen "Lautstärke von anna" und "Auf 100 % zurücksetzen", das eigene nicht. Bei 150 % bzw. 0 % ist das jeweilige Icon sichtbar.
6. `LocalizationTests` bleiben grün (Parität der neuen Schlüssel, kein fest verdrahteter Text) (AC5)
7. Manueller Check AC6

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Test 1 schreiben (rot), Faktor je Sprecher im `Mixer` (vor Grundverstärkung, `Volume` und Begrenzer).
2. Test 2 schreiben (rot), `ClientSettings.UserVolumes`, `VolumeFor`, Begrenzung in `Clamp()`.
3. Test 3 schreiben (rot), Eigenschaften und `ResetVolumeCommand` im `UserViewModel`, Wert aus den Einstellungen beim Aufbau.
4. Test 4 schreiben (rot), `MainViewModel` hält die Zuordnung Session-ID zu Faktor aktuell (bei `StateChanged` und Änderung am Regler), speichert ohne `ApplySettings`.
5. Test 5 schreiben (rot), Kontextmenü mit Regler (`StaysOpenOnClick`) und Icons in `MainWindow.axaml`, neue Texte in beiden `resx`.
6. README ergänzen, manueller Check AC6.

### Out of Scope

- Lautstärke je Nutzer serverseitig oder für andere sichtbar
- Pegelautomatik je Sprecher
- Eine Übersicht aller angepassten Personen in den Einstellungen

---

## Package 52: Einstellungen ohne Einfrieren

**Ziel:** Einstellungen öffnen, Speichern und Programmstart blockieren die Oberfläche nicht mehr durch das Auflisten der Audiogeräte.

**Abhängigkeiten:** Package 17

**Betroffene Dateien:**
- `src/OVS.Client/Audio/AudioEngine.cs` (ändern): `AudioDevices.Open` direkt über die ID (A58)
- `src/OVS.Client/ViewModels/MainViewModel.cs` (ändern): Geräteliste im Hintergrund, `DeviceSource` austauschbar für Tests
- `src/OVS.Client/ViewModels/SettingsViewModel.cs` (ändern): `ShowDevices(inputs, outputs)`, Platzhalter solange geladen wird
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (ändern): "Geräte werden geladen ..."
- `tests/OVS.Tests/Client/MainViewModelTests.cs`, `SettingsTests.cs` (ändern)

### Kontext

`MainViewModel.OpenSettings` ruft `AudioDevices.List` für Eingänge und Ausgänge synchron auf dem UI-Thread auf. Gemessen auf dem Entwicklungsrechner (9 Eingänge, 9 Ausgänge): 260 bis 290 ms plus 360 bis 380 ms, dazu 80 bis 190 ms für Aufbau und Layout der Seite. Das passt zum beobachteten Einfrieren von etwa einer Sekunde. Das Aufzählen selbst dauert 8 ms, teuer ist `FriendlyName` je Gerät.

Dieselbe Funktion steckt in `AudioDevices.Open`, das `AudioEngine.Configure` beim Start und bei jedem "Speichern" für Mikrofon und Lautsprecher aufruft. Auch dort friert die App also jedes Mal etwa 0,6 s ein.

### Acceptance Criteria

- [x] AC1: `OpenSettings` wartet nicht auf die Geräteliste: Die Seite ist sofort offen, auch wenn das Laden noch läuft.
- [x] AC2: Ist die Liste da, zeigen die Auswahlfelder alle Geräte, das gespeicherte ist ausgewählt. Solange sie fehlt, zeigt das Feld "Geräte werden geladen ...", und der Hinweis "Gerät fehlt" erscheint nicht.
- [x] AC3: Fehlt das gespeicherte Gerät nach dem Laden wirklich, erscheint der Hinweis wie bisher. Eine Auswahl, die der Nutzer während des Ladens getroffen hat, bleibt erhalten.
- [x] AC4: Beim zweiten Öffnen ist die Liste sofort da (vom letzten Laden) und wird im Hintergrund aufgefrischt.
- [x] AC5: `AudioDevices.Open` liest keine Gerätenamen mehr, ein fehlendes oder deaktiviertes Gerät führt wie bisher zum Standardgerät mit Hinweis.
- [ ] AC6 (manuell, Messung erledigt, Gefühl am echten Client offen): Gemessen mit 18 Geräten: `OpenSettings` 1 bis 16 ms statt etwa 650 ms, `AudioDevices.Open` 0 bis 3 ms statt 300 bis 380 ms, Speichern samt Gerätestart 48 bis 79 ms. Auf dem Entwicklungsrechner öffnen sich die Einstellungen ohne spürbare Pause, "Speichern" ebenso. Das Messprogramm aus der Analyse zeigt für `OpenSettings` unter 50 ms und für `Configure` ohne Gerätestart unter 50 ms.

### Tests (TDD)

Reihenfolge: Test schreiben -> rot -> minimal implementieren -> grün -> refactoren.

1. `MainViewModelTests > "OpenSettings_DoesNotWaitForDeviceList"` (AC1, AC2) - Reproduktion, muss zuerst rot sein
   - Gegeben: `DeviceSource`, das bis zur Freigabe blockiert, gespeichertes Eingabegerät "mic-2"
   - Erwartet: `OpenSettings` kehrt in unter 100 ms zurück, `Page` ist Settings, das Eingabefeld zeigt den Platzhalter, kein `DeviceHint`. Nach der Freigabe enthält `Inputs` die Geräte, "mic-2" ist ausgewählt.
2. `SettingsTests > "Devices_LoadedLater_HintOnlyIfReallyMissing_KeepsUserChoice"` (AC2, AC3)
   - Gegeben: `SettingsViewModel` ohne Liste, der Nutzer wählt "Standard", danach `ShowDevices` ohne das gespeicherte Gerät
   - Erwartet: Auswahl "Standard" bleibt. Ohne Nutzerauswahl erscheint der Hinweis "Gerät fehlt" erst nach `ShowDevices`.
3. `MainViewModelTests > "OpenSettings_Again_UsesLastListAtOnce"` (AC4)
   - Gegeben: erstes Öffnen mit Liste A geladen, zweites Öffnen mit blockierendem `DeviceSource`
   - Erwartet: Liste A ist sofort da, nach der Freigabe die neue Liste
4. Bestehende Tests zu `AudioDevices.Resolve` bleiben grün, `Open` nutzt dieselbe Entscheidung über die ID (AC5)
5. Manueller Check AC6 mit dem Messprogramm und am echten Client

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Test 1 schreiben (rot). `DeviceSource` in `MainViewModel`, Laden per `Task.Run`, Ergebnis per `post` an die Seite.
2. Test 2 schreiben (rot), `SettingsViewModel.ShowDevices` und Platzhalter.
3. Test 3 schreiben (rot), letzte Liste merken, Laden beim Start anstossen.
4. `AudioDevices.Open` auf `GetDevice(id)` umstellen (A58).
5. Messprogramm erneut laufen lassen, manueller Check AC6.

### Out of Scope

- Schnellerer Aufbau der Einstellungsseite selbst (80 bis 190 ms headless). Nur wenn sie nach der Änderung noch spürbar hängt, wird das gesondert angegangen.
- Automatisches Aktualisieren bei neu angesteckten Geräten, während die Seite offen ist

---

## Package 53: Selbsttest

**Ziel:** Ein Button in den Einstellungen schaltet einen stumm und taub und spielt die eigene Stimme so zurück, wie andere sie hören.

**Abhängigkeiten:** Package 50, 52

**Betroffene Dateien:**
- `src/OVS.Client/Audio/AudioEngine.cs` (ändern): `SelfTest`, eigene Frames in den Mixer statt ins Netz, `ApplyLive(...)` für Regler ohne Geräteneustart
- `src/OVS.Client/ViewModels/SettingsViewModel.cs` (ändern): `IsSelfTesting`, `ToggleSelfTestCommand`, Regler melden Änderungen sofort (`LivePreview`)
- `src/OVS.Client/ViewModels/MainViewModel.cs` (ändern): Test starten und beenden, Zustand merken und wiederherstellen
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (ändern): `SetSelfStateAsync(muted, deafened)` für das gezielte Setzen und Zurücksetzen
- `src/OVS.Client/Views/SettingsView.axaml` (ändern): Button und Kopfhörer-Hinweis beim Pegel
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (ändern)
- `tests/OVS.Tests/Client/SendPathTests.cs`, `MainViewModelTests.cs`, `SettingsTests.cs`, `UiSmokeTests.cs` (ändern)
- `README.md` (ändern)

### Kontext

Die Einstellungen zeigen den Mikrofonpegel (`AudioEngine.InputLevel` -> `SettingsViewModel.InputLevelDb`) und die VAD-Schwelle, aber man hört sich nicht selbst. Regler wirken heute erst nach "Speichern", weil nur `ApplySettings` -> `AudioEngine.Configure` sie übernimmt. `Configure` startet dabei die Geräte neu.

Die Aufnahme läuft immer (`CapturePipeline.Feed` -> `AudioEngine.Decide` -> `OnFrameEncoded` -> `Send`). `Decide` sendet nichts, wenn man stumm oder nicht verbunden ist. Der Mixer (`Mixer.Push`) nimmt Opus-Frames je Sprecher-ID an. Ist man taub, verwirft `OnVoice` fremde Frames, und `Mix()` leert den Frame.

Stumm und taub setzt heute nur `ServerViewModel.ToggleMute` und `ToggleDeafen`, beide senden `SetSelfState` an den Server.

### Acceptance Criteria

- [x] AC1: Die Einstellungen haben im Abschnitt mit dem Pegel einen Button "Selbsttest starten", daneben den Hinweis "Mit Kopfhörern testen, sonst gibt es Rückkopplung". Während des Tests heisst er "Selbsttest beenden".
- [x] AC2: Während des Tests ist man stumm und taub. Bei Verbindung geht das per `SetSelfState` auch an den Server, und nichts wird gesendet. Andere Stimmen hört man nicht.
- [x] AC3: Die eigene Stimme ist live zu hören, durch Opus, die Verstärkung aus Package 50 und den Regler "Lautstärke". Bei Sprachaktivierung nur über der Schwelle, bei Push-to-Talk nur mit gedrückter Taste.
- [x] AC4: Mikrofonverstärkung, Lautstärke, VAD-Schwelle und Modus wirken sofort (auch ohne Test), ohne die Geräte neu zu starten. "Verwerfen" stellt die gespeicherten Werte wieder her, "Speichern" übernimmt sie.
- [x] AC5: Der Test endet mit "Selbsttest beenden", beim Schliessen der Einstellungen und beim Trennen. Danach gilt wieder der Zustand von vorher (stumm, taub oder keins von beiden), auch beim Server.
- [x] AC6: Alle neuen Texte gibt es auf Deutsch und Englisch.
- [ ] AC7 (manuell): Mit Kopfhörern an echtem Mikrofon: Man hört sich mit kurzer Verzögerung, ein zweiter Client hört einen während des Tests nicht und sieht "Ton aus".

### Tests (TDD)

Reihenfolge: Test schreiben -> rot -> minimal implementieren -> grün -> refactoren.

1. `SendPathTests > "SelfTest_PlaysOwnVoice_SendsNothing_OthersSilent"` (AC2, AC3)
   - Gegeben: `AudioEngine` ohne Geräte, Testton 440 Hz, Sprachaktivierung mit niedriger Schwelle, stumm und taub, `SelfTest = true`, `Send` zählt mit
   - Erwartet: innerhalb einer Sekunde `LastOutputLevelDb` über -30 dB, `Send` nie aufgerufen, ein fremder Frame per `OnVoice` bleibt ungehört
2. `SendPathTests > "SelfTest_FollowsMode_PushToTalkWithoutKeySilent"` (AC3)
   - Gegeben: wie oben, aber Push-to-Talk ohne gedrückte Taste, danach mit hoher VAD-Schwelle (-10 dB) bei Sprachaktivierung
   - Erwartet: in beiden Fällen bleibt die Ausgabe still
3. `SendPathTests > "ApplyLive_ChangesGainAndVolume_WithoutRestart"` (AC4)
   - Gegeben: laufende Engine, `ApplyLive` mit Verstärkung 50 %
   - Erwartet: der gemeldete Eingangspegel sinkt um etwa 6 dB, die Aufnahme wurde nicht neu erzeugt
4. `SettingsTests > "Sliders_ApplyLive_DiscardRestores"` (AC4)
   - Gegeben: `SettingsViewModel` mit `LivePreview`, Verstärkung und Schwelle ändern, danach verwerfen
   - Erwartet: `LivePreview` bekam die neuen Werte sofort, beim Verwerfen die gespeicherten
5. `MainViewModelTests > "SelfTest_MutesAndDeafens_RestoresPreviousState"` (AC2, AC5)
   - Gegeben: echter Testserver, verbunden, nicht stumm. Einstellungen öffnen, Selbsttest starten
   - Erwartet: der Server meldet stumm und taub. Nach "Selbsttest beenden" wieder nicht stumm und nicht taub. Zweiter Durchlauf: Start, dann Einstellungen schliessen, ebenso zurück. Dritter: Start, dann trennen, der Test ist aus.
6. `UiSmokeTests > "Settings_SelfTestButton_TogglesText_ShowsHeadphoneHint"` (AC1, AC6)
7. `LocalizationTests` bleiben grün (AC6)
8. Manueller Check AC7

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Tests 1, 2 schreiben (rot). `SelfTest` in `AudioEngine`: `Decide` entscheidet im Test wie unstumm, `OnFrameEncoded` gibt den Frame an den Mixer (eigene Sprecher-ID 0) statt an `Send`, `Mix()` leert im Test den Frame nicht.
2. Test 3 schreiben (rot), `ApplyLive` (Verstärkung, Lautstärke, Schwelle, Modus).
3. Test 4 schreiben (rot), `LivePreview` im `SettingsViewModel`, `MainViewModel` verbindet es mit `ApplyLive` und stellt beim Verwerfen zurück.
4. Test 5 schreiben (rot), `ServerViewModel.SetSelfStateAsync`, Start und Ende im `MainViewModel` mit gemerktem Zustand.
5. Test 6 schreiben (rot), Button und Hinweis in `SettingsView.axaml`, Texte in beiden `resx`.
6. README ergänzen, manueller Check AC7.

### Out of Scope

- Aufnehmen und später abspielen
- Test über den Server (Echo vom Server zurück)
- Anderes Gerät ohne "Speichern" ausprobieren

---

## Package 54: Ein Channel-Dialog

**Ziel:** Anlegen und Bearbeiten eines Channels nutzen denselben Dialog mit allen Optionen, leer beim Anlegen und vorbelegt beim Bearbeiten.

**Abhängigkeiten:** Package 34, 35

**Betroffene Dateien:**
- `src/OVS.Shared/Protocol/Messages.cs` (ändern): `CreateChannel(Name, Description, IsMuted = false, MaxUsers = 0)`
- `src/OVS.Shared/Protocol/ProtocolInfo.cs` (ändern): Version 9
- `src/OVS.Server/Commands/ChannelCommands.cs` (ändern): `OnCreateChannel` übernimmt und prüft beide Werte
- `src/OVS.Client/Views/ChannelDialog.axaml`, `ChannelDialog.axaml.cs` (neu)
- `src/OVS.Client/ViewModels/ChannelDialogViewModel.cs` (neu)
- `src/OVS.Client/Views/SimpleDialogs.cs` (ändern): `EditChannel` entfällt
- `src/OVS.Client/Views/MainWindow.axaml.cs` (ändern): `Dialogs.EditChannel` zeigt den neuen Dialog
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (ändern): `NewChannel` schickt alle Werte, `CreateChannelAsync` mit `ChannelEdit`
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (ändern): Button "Anlegen"
- `tests/OVS.Tests/Server/ChannelCommandTests.cs`, `tests/OVS.Tests/Client/ServerViewModelTests.cs`, `UiSmokeTests.cs`, `tests/OVS.Tests/SmokeTests.cs` (ändern)
- `tests/OVS.Tests/Client/ChannelDialogViewModelTests.cs` (neu)
- `README.md` (ändern): Protokollversion

### Kontext

Anlegen und Bearbeiten nutzen beide `SimpleDialogs.EditChannel`, im Modus `ChannelDialogMode.Create` blendet der Dialog aber "Stummer Channel" und "Maximale Nutzer" aus. Der Grund liegt im Protokoll: `CreateChannel(Name, Description)` kann diese Werte nicht übertragen, nur `EditChannel` hat `IsMuted` und `MaxUsers`. `OnEditChannel` prüft die Plätze (0 bis `ProtocolInfo.MaxChannelUsers`, Standard-Channel unbegrenzt). `OnCreateChannel` legt immer einen nicht stummen, unbegrenzten Channel an. Beide Buttons heissen heute "Speichern".

### Acceptance Criteria

- [x] AC1: "Channel anlegen" und "Channel bearbeiten" zeigen denselben Dialog mit Name, Beschreibung, "Stummer Channel" und "Maximale Nutzer".
- [x] AC2: Beim Anlegen ist der Dialog leer (kein Name, keine Beschreibung, nicht stumm, 0 Plätze), Titel "Channel anlegen", Button "Anlegen".
- [x] AC3: Beim Bearbeiten ist er mit den Daten des Channels vorbelegt, Titel "Channel bearbeiten", Button "Speichern". Beim Standard-Channel sind die Plätze gesperrt, mit Hinweis.
- [x] AC4: Ohne Namen lässt sich der Dialog nicht bestätigen, die Plätze gehen von 0 bis 999.
- [x] AC5: Ein neu angelegter Channel hat beim Server gleich die gewählten Optionen (stumm, Plätze). Der Server lehnt beim Anlegen ungültige Plätze ab wie beim Bearbeiten und protokolliert die Optionen im Channel-Log.
- [x] AC6: Die Protokollversion ist 9. Ein Client mit Version 8 bekommt beim Verbinden die bekannte Meldung zur Version.
- [x] AC7: `SimpleDialogs.EditChannel` gibt es nicht mehr, alle Texte des Dialogs gibt es auf Deutsch und Englisch.

### Tests (TDD)

Reihenfolge: Test schreiben -> rot -> minimal implementieren -> grün -> refactoren.

1. `ChannelCommandTests > "Create_WithMutedAndSlots_ChannelHasThem"` (AC5)
   - Gegeben: Admin legt einen Channel mit `IsMuted = true`, `MaxUsers = 5` an
   - Erwartet: `ChannelAdded` mit beiden Werten, gespeichert in den Serverdaten, Eintrag im Channel-Log
2. `ChannelCommandTests > "Create_InvalidSlots_Rejected"` (AC5)
   - Gegeben: `MaxUsers = -1` und `1000`
   - Erwartet: `InvalidValue`, kein Channel angelegt
3. `SmokeTests` Version auf 9, `HandshakeTests > "OldProtocolVersion_RejectedVersionMismatch"` bleibt grün (AC6)
4. `ChannelDialogViewModelTests > "Create_EmptyDefaults"`, `"Edit_Prefilled"`, `"Default_SlotsLocked"`, `"NameRequired_SlotsBounded"` (AC2 bis AC4)
5. `ServerViewModelTests > "NewChannel_SendsAllOptions"` (AC1, AC5)
   - Gegeben: Dialog-Fake liefert Name, Beschreibung, stumm, 3 Plätze
   - Erwartet: gesendetes `CreateChannel` mit allen vier Werten
6. `UiSmokeTests > "ChannelDialog_SameForCreateAndEdit"` (AC1 bis AC3, AC7): beide öffnen, gleiche Felder sichtbar, Titel und Button je Modus
7. `LocalizationTests` bleiben grün (AC7)

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Tests 1 bis 3 schreiben (rot), `CreateChannel` erweitern, `OnCreateChannel` mit derselben Prüfung wie `OnEditChannel` (gemeinsame Hilfsmethode), Version 9.
2. Test 4 schreiben (rot), `ChannelDialogViewModel`.
3. Test 5 schreiben (rot), `NewChannel` und `CreateChannelAsync` umstellen.
4. Test 6 schreiben (rot), `ChannelDialog.axaml`, Anbindung in `MainWindow.axaml.cs`, `SimpleDialogs.EditChannel` entfernen, Texte.
5. README (Protokollversion) ergänzen, Screenshot-Werkzeug auf den neuen Dialog umstellen und beide Modi ansehen.

### Out of Scope

- Weitere Channel-Optionen (Passwort, Unter-Channels)
- Anlegen eines Channels direkt mit Links

---

## Package 55: Lautstärke bis 200 %

**Ziel:** Der Regler "Lautstärke" geht von 0 bis 200 %, Standard 100 %.

**Abhängigkeiten:** Package 50

**Betroffene Dateien:**
- `src/OVS.Client/Settings/ClientSettings.cs` (ändern): `OutputVolume` 0 bis 2 in `Clamp()`
- `src/OVS.Client/ViewModels/SettingsViewModel.cs` (ändern): `OutputVolumePercent` 0 bis 200
- `src/OVS.Client/Views/SettingsView.axaml` (ändern): Slider `Maximum="200"`
- `tests/OVS.Tests/Client/SettingsTests.cs`, `ReceivePathTests.cs` (ändern)
- `README.md` (ändern)

### Kontext

`ClientSettings.Clamp()` begrenzt `OutputVolume` auf 0 bis 1, `SettingsViewModel.OnOutputVolumePercentChanged` auf 0 bis 100, der Slider in `SettingsView.axaml` hat `Maximum="100"`. Der Mixer multipliziert mit `Mixer.Volume` nach der Grundverstärkung (Package 50), danach begrenzt `SoftLimiter`. A54 hatte den Regler bewusst bei 100 % gelassen, das wird hiermit geändert.

### Acceptance Criteria

- [x] AC1: Der Regler geht von 0 bis 200 %, Standard 100 %, die Prozentanzeige zeigt den Wert.
- [x] AC2: 200 % werden gespeichert und nach einem Neustart wieder angezeigt. Werte über 200 % aus einer Datei werden auf 200 % begrenzt.
- [x] AC3: 200 % verdoppeln die Stimmen gegenüber 100 %, laute Stellen bleiben unter der Grenze des Begrenzers.

### Tests (TDD)

1. `SettingsTests > "OutputVolume_UpTo200_RoundTrip_Clamped"` (AC1, AC2)
   - Gegeben: Regler auf 200, speichern, laden. Ausserdem `OutputVolume = 5` in der Datei
   - Erwartet: 2,0 bzw. begrenzt auf 2,0, Anzeige 200 %. Standard eines neuen Profils 1,0
2. `ReceivePathTests > "Mixer_Volume200_DoublesVoice_Limited"` (AC3)
   - Gegeben: leiser Sinus bei `Volume = 2` gegenüber `Volume = 1`, ausserdem ein lauter Sinus
   - Erwartet: +6 dB ± 0,5 dB, der laute bleibt unter `SoftLimiter.Threshold`
3. Bestehender Test `SettingsTests > "Clamp_OutOfRange"` angepasst (Lautstärke-Grenze 2)

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Tests 1 und 3 (rot), Grenzen in `ClientSettings` und `SettingsViewModel`, Slider-Maximum.
2. Test 2 (sollte ohne Änderung grün sein, sichert das Zusammenspiel mit dem Begrenzer).
3. README ergänzen.

### Out of Scope

- Sound-Regler über 100 % (A65)

---

## Package 56: Töne für Chat-Nachrichten

**Ziel:** Nachrichten in "Allgemein" und im Channel-Chat haben je einen eigenen Sound, standardmässig den Ton der Privatnachricht.

**Abhängigkeiten:** Package 48

**Betroffene Dateien:**
- `src/OVS.Client/Audio/Sounds.cs` (ändern): `SoundEvent.ServerMessage`, `SoundEvent.ChannelMessage`, Muster wie `PrivateMessage`
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (ändern): Ton je Chat-Ziel
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (ändern): `Sound_ServerMessage`, `Sound_ChannelMessage`
- `tests/OVS.Tests/Client/SoundTests.cs`, `ServerViewModelTests.cs`, `SettingsTests.cs` (ändern)
- `README.md` (ändern)

### Kontext

`ServerViewModel.Apply` löst für `ChatMessage` nur bei `ChatTarget.Private` von anderen `SoundRequested(SoundEvent.PrivateMessage)` aus. Die Sound-Zeilen in den Einstellungen entstehen aus `Enum.GetValues<SoundEvent>()` (`SettingsViewModel.SoundRows`), ein neuer Wert bekommt also automatisch eine Zeile mit Datei, Lautstärke und Stumm. `SoundTests > "Defaults_EveryEvent_ShortAudibleDistinct"` verlangt heute, dass alle Standardtöne verschieden sind.

### Acceptance Criteria

- [x] AC1: Eine Nachricht eines anderen in "Allgemein" spielt `ServerMessage`, im eigenen Channel `ChannelMessage`, privat wie bisher `PrivateMessage`. Eigene Nachrichten spielen keinen Ton.
- [x] AC2: Standardmässig klingen alle drei gleich (Ton der Privatnachricht).
- [x] AC3: In den Einstellungen haben "Nachricht in Allgemein" und "Nachricht im Channel" je eine eigene Zeile. Eigene Datei, Lautstärke und Stumm wirken nur auf diesen Sound.
- [x] AC4: Die neuen Texte gibt es auf Deutsch und Englisch.

### Tests (TDD)

1. `ServerViewModelTests > "ChatMessages_SoundPerTarget_NotForOwn"` (AC1)
   - Gegeben: Nachrichten anderer an Server, Channel und privat, dazu eine eigene
   - Erwartet: `ServerMessage`, `ChannelMessage`, `PrivateMessage`, für die eigene nichts
2. `SoundTests > "Defaults_EveryEvent_ShortAudibleDistinct"` angepasst: die drei Chat-Töne sind gleich, alle anderen verschieden (AC2)
3. `SettingsTests > "Sounds_ChatRows_Independent"` (AC3): Stumm für `ServerMessage` lässt `ChannelMessage` und `PrivateMessage` unberührt
4. `LocalizationTests` bleiben grün (AC4)

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Test 1 (rot), neue Werte in `SoundEvent`, Ton je Ziel in `ServerViewModel`.
2. Test 2 anpassen, Muster der Privatnachricht für beide übernehmen.
3. Test 3, Texte in beiden `resx`, README.

### Out of Scope

- Ton nur, wenn der Tab nicht sichtbar ist
- Erwähnungen (@Name) mit eigenem Ton

---

## Package 57: Ton für Sprache über Link

**Ziel:** Beginnt jemand aus einem anderen Channel per Link-PTT zu sprechen, hört man einen kurzen eigenen Ton.

**Abhängigkeiten:** Package 48, 56 (beide ergänzen `SoundEvent`)

**Betroffene Dateien:**
- `src/OVS.Client/Audio/Sounds.cs` (ändern): `SoundEvent.LinkVoice` mit eigenem, leisem Muster
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (ändern): Beginn einer Link-Übertragung in `OnSpeakers` erkennen
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (ändern): `Sound_LinkVoice`
- `tests/OVS.Tests/Client/ServerViewModelTests.cs`, `SoundTests.cs` (ändern)
- `README.md` (ändern)

### Kontext

Der Mixer meldet aktive Sprecher mit `ActiveSpeaker.ViaLink` (gesetzt, wenn das Paket mit `VoiceHeader.TargetLinked` kam). `ServerViewModel.OnSpeakers` merkt sich je Session den letzten Zeitpunkt, `RefreshSpeaking` zeigt das Link-Icon, solange der Zeitpunkt jünger als `SpeakingHold` ist. Ein Ton fehlt, deshalb klingt Link-Sprache wie Sprache im eigenen Channel. `AudioEngine.PlaySound` spielt bei "Ton aus" nur Mikrofon- und Ton-Töne (A46), das gilt auch für den neuen Ton.

### Acceptance Criteria

- [x] AC1: Beginnt ein anderer über Link zu sprechen, wird `LinkVoice` genau einmal ausgelöst, auch wenn er weiterspricht.
- [x] AC2: Nach einer Pause ab 300 ms löst der nächste Beginn den Ton erneut aus, kürzere Lücken nicht.
- [x] AC3: Sprache im eigenen Channel (ohne Link) und die eigene Link-PTT lösen keinen Ton aus. Bei "Ton aus" bleibt er stumm.
- [x] AC4: "Sprache über Link" hat in den Einstellungen eine eigene Zeile, der Standardton ist kurz, leise und von allen anderen verschieden. Texte auf Deutsch und Englisch.
- [ ] AC5 (manuell): Mit zwei Clients in verlinkten Channels ist beim Link-PTT des anderen der Ton zu hören, er stört das Verstehen nicht.

### Tests (TDD)

1. `ServerViewModelTests > "LinkVoice_SoundOncePerTransmission"` (AC1, AC2)
   - Gegeben: `ManualTimeProvider`, wiederholte `OnSpeakers` mit `ViaLink = true` für Session 2 alle 20 ms, dann 200 ms Pause, dann 400 ms Pause
   - Erwartet: ein Ton am Anfang, keiner nach 200 ms, einer nach 400 ms
2. `ServerViewModelTests > "LinkVoice_NotForOwnChannelOrSelf"` (AC3)
   - Gegeben: Sprecher ohne Link, eigene Session mit Link
   - Erwartet: kein `LinkVoice`
3. `SoundTests > "Deafened_OnlyOwnMicAndSoundTones_AllOffNothing"` um `LinkVoice` ergänzt (AC3)
4. `SoundTests > "Defaults_EveryEvent_ShortAudibleDistinct"` deckt den neuen Ton mit ab, dazu Pegel unter dem der Privatnachricht (AC4)
5. `LocalizationTests` bleiben grün (AC4)
6. Manueller Check AC5

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Tests 1 und 2 (rot), Erkennung in `OnSpeakers`: je Session den Zeitpunkt des letzten Link-Frames merken, Ton bei Beginn oder nach mehr als 300 ms Pause.
2. Tests 3 und 4, Muster und `SoundEvent.LinkVoice`, Texte, README.
3. Manueller Check AC5.

### Out of Scope

- Ton für die eigene Link-PTT
- Anzeige, aus welchem Channel gesprochen wird (das Link-Icon im Baum gibt es schon)

---

## Package 58: Reihenfolge der Einstellungen

**Ziel:** Die Einstellungen zeigen Geräte, Lautstärke, Übertragung, Tasten, Sounds, Darstellung und Über in dieser Reihenfolge.

**Abhängigkeiten:** keine

**Betroffene Dateien:**
- `src/OVS.Client/Views/SettingsView.axaml` (ändern): Abschnitte umstellen
- `tests/OVS.Tests/Client/UiSmokeTests.cs` (ändern)

### Kontext

In `SettingsView.axaml` stehen die Abschnitte heute in der Reihenfolge Geräte, Lautstärke, Sounds, Übertragung, Tasten, Darstellung, Über. Sounds sind der längste Abschnitt und schieben Übertragung und Tasten weit nach unten.

### Acceptance Criteria

- [x] AC1: Die Überschriften erscheinen in der Reihenfolge Geräte, Lautstärke, Übertragung, Tasten, Sounds, Darstellung, Über.
- [x] AC2: Alle Bedienelemente der Abschnitte funktionieren wie vorher (bestehende Tests grün).

### Tests (TDD)

1. `UiSmokeTests > "Settings_SectionOrder"` (AC1)
   - Gegeben: Einstellungen geöffnet
   - Erwartet: die Abschnittsüberschriften (`Classes="section"`) in der genannten Reihenfolge, von oben nach unten
2. Alle bestehenden Einstellungstests bleiben grün (AC2)

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Test 1 (rot), Abschnitte in `SettingsView.axaml` verschieben, ohne Inhalte zu ändern.
2. Screenshot der Seite ansehen.

### Out of Scope

- Einklappbare Abschnitte oder Reiter

---

## Package 59: Push-to-Talk-Taste von Anfang an

**Ziel:** Ein neues Profil hat eine passende PTT-Taste, und die Einstellungen weisen auf eine fehlende PTT-Taste hin.

**Abhängigkeiten:** Package 41, 58

**Betroffene Dateien:**
- `src/OVS.Client/Settings/ClientSettings.cs` (ändern): neues Profil mit Standard-PTT-Taste
- `src/OVS.Client/Input/KeyPoller.cs` (ändern): Zahl der Maustasten (`GetSystemMetrics(SM_CMOUSEBUTTONS)`), Konstante für Strg rechts, Name "Strg rechts"
- `src/OVS.Client/Input/KeyBindings.cs` (ändern): `DefaultKeys.PushToTalk(int mouseButtons)`
- `src/OVS.Client/ViewModels/SettingsViewModel.cs` (ändern): `ShowPttHint`, `SetPttKeyCommand`
- `src/OVS.Client/Views/SettingsView.axaml`, `SimpleDialogs.cs` (ändern): Hinweis mit Button, Dialog mit vorausgewählter Aktion
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (ändern)
- `tests/OVS.Tests/Client/SettingsTests.cs`, `KeyBindingTests.cs`, `UiSmokeTests.cs` (ändern)
- `README.md` (ändern)

### Kontext

Seit Package 29 hat ein neues Profil keine Tasten (`SettingsTests > "Load_NoFile_NoBindings"`), gewählt ist aber Push-to-Talk. Wer nichts einstellt, kann nicht sprechen. Den Hinweis "Keine PTT-Taste belegt" gibt es unter dem eigenen Namen (`MainViewModel.HasNoPttBinding`), in den Einstellungen nicht. `ClientSettings.Load` gibt ohne Datei `new ClientSettings()` zurück. Tasten legt `SimpleDialogs.EditKeyBinding` fest (Aktion wählen, Taste drücken), `KeyBindings.Resolve` lässt zusätzliche Modifier zu, also funktioniert auch Strg rechts als einzelne Taste.

### Acceptance Criteria

- [x] AC1: Ein neues Profil (keine `settings.json`) hat Push-to-Talk auf Maustaste 4, wenn Windows mindestens 5 Maustasten meldet, sonst auf Strg rechts. Weitere Tasten sind nicht belegt.
- [x] AC2: Ein bestehendes Profil ohne PTT-Taste bekommt keine Taste untergeschoben.
- [x] AC3: Unter "Übertragung" steht bei Push-to-Talk ohne PTT-Taste ein Hinweis mit dem Button "Taste festlegen". Bei Sprachaktivierung oder mit PTT-Taste ist er weg, auch sofort nach dem Hinzufügen oder Entfernen auf der Seite.
- [x] AC4: "Taste festlegen" öffnet den Tasten-Dialog mit Push-to-Talk vorausgewählt, die neue Taste landet in der Liste.
- [x] AC5: Strg rechts heisst in der Tastenliste "Strg rechts" bzw. "Right Ctrl" und löst Push-to-Talk aus.
- [x] AC6: Texte auf Deutsch und Englisch.

### Tests (TDD)

1. `KeyBindingTests > "DefaultPtt_Mouse4WithSideButtons_ElseRightCtrl"` (AC1)
   - Gegeben: 5 bzw. 7 Maustasten, dann 3
   - Erwartet: Maustaste 4, dann Strg rechts
2. `SettingsTests > "Load_NoFile_DefaultPttOnly"` ersetzt `"Load_NoFile_NoBindings"` (AC1, AC2)
   - Gegeben: keine Datei, dann eine Datei ohne Tasten
   - Erwartet: genau eine PTT-Taste, dann weiterhin keine
3. `SettingsTests > "PttHint_OnlyForPushToTalkWithoutKey"` (AC3)
   - Gegeben: Push-to-Talk ohne PTT-Taste, dann Sprachaktivierung, dann Taste hinzugefügt und wieder entfernt
   - Erwartet: `ShowPttHint` jeweils passend, mit Änderungsmeldung
4. `SettingsTests > "SetPttKey_OpensDialogWithPushToTalk"` (AC4): Dialog-Fake bekommt Push-to-Talk vorausgewählt, seine Taste landet in `KeyBindings`
5. `KeyBindingTests > "RightCtrl_NameAndResolves"` (AC5)
6. `UiSmokeTests`: Hinweis und Button unter "Übertragung" sichtbar (AC3), `LocalizationTests` grün (AC6)

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Test 1 (rot), `DefaultKeys.PushToTalk` und die Abfrage der Maustasten.
2. Test 2 (rot), `ClientSettings.Load` für ein neues Profil.
3. Tests 3 und 4 (rot), Hinweis und Button, Dialog mit vorausgewählter Aktion.
4. Test 5, Name und Konstante für Strg rechts.
5. Test 6, Texte, README (Abschnitt Tasten).

### Out of Scope

- Eine Taste für bestehende Profile nachträglich setzen
- Weitere Standardbelegungen (Link-PTT, Mute)

---

## Package 60: Ton für die eigene Link-PTT

**Ziel:** Beginnt man selbst über Link zu sprechen, hört man einen eigenen, einzeln anpassbaren Ton, standardmässig den aus Package 57.

**Abhängigkeiten:** Package 57

**Betroffene Dateien:**
- `src/OVS.Client/Audio/Sounds.cs` (ändern): `SoundEvent.OwnLinkVoice`, Muster und Pegel wie `LinkVoice`
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (ändern): Beginn der eigenen Link-Übertragung in `SetSelfTransmitting` erkennen
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (ändern): `Sound_OwnLinkVoice`
- `tests/OVS.Tests/Client/ServerViewModelTests.cs`, `SoundTests.cs`, `SettingsTests.cs` (ändern)
- `README.md` (ändern)

### Kontext

Das eigene Senden meldet `AudioEngine.TransmitChanged(target)`, `MainViewModel` gibt es an `ServerViewModel.SetSelfTransmitting(target)` weiter, das "Sendet" und das Link-Icon am eigenen Namen steuert. `target` ist `VoiceHeader.TargetLinked` nur, wenn Link-PTT gedrückt ist und man das Recht `SpeakLinked` hat (`TransmitController.Decide`), sonst `TargetChannel`. Ob der eigene Channel Links hat, zeigt `ChannelViewModel.IsLinked`. `ServerViewModel` hat eine `TimeProvider` (in Tests `ManualTimeProvider`) und das Ereignis `SoundRequested`, das `MainViewModel` an `AudioEngine.PlaySound` hängt. Package 57 hat `LinkVoice` für andere eingeführt und die eigene Link-PTT ausdrücklich ausgenommen (`LinkVoice_NotForOwnChannelOrSelf`). Die Sound-Zeilen entstehen aus `Enum.GetValues<SoundEvent>()`.

### Acceptance Criteria

- [x] AC1: Beginnt die eigene Übertragung über Link (Wechsel auf `TargetLinked`), wird `OwnLinkVoice` einmal ausgelöst, nicht wiederholt, solange sie läuft.
- [x] AC2: Nach einer Pause ab 300 ms löst der nächste Beginn den Ton erneut aus, ein kürzeres Loslassen und Wiederdrücken nicht.
- [x] AC3: Kein Ton, wenn der eigene Channel keine Links hat oder nur im eigenen Channel gesendet wird (`TargetChannel`, z. B. ohne Recht). Kein Ton, wenn nichts gesendet wird (stumm, "Ton aus", stummer Channel, Selbsttest).
- [x] AC4: Standardmässig klingt `OwnLinkVoice` wie `LinkVoice`. In den Einstellungen hat "Eigene Sprache über Link" eine eigene Zeile, Datei, Lautstärke und Stumm wirken nur auf diesen Sound.
- [x] AC5: `LinkVoice` für andere funktioniert unverändert. Texte auf Deutsch und Englisch.
- [ ] AC6 (manuell): Mit zwei Clients in verlinkten Channels hört man beim eigenen Link-PTT den Ton, der andere hört seinen `LinkVoice`, nicht beide doppelt.

### Tests (TDD)

Reihenfolge: Test schreiben -> rot -> minimal implementieren -> grün -> refactoren.

1. `ServerViewModelTests > "OwnLinkVoice_OnStartOfOwnLinkTransmission"` (AC1, AC2)
   - Gegeben: eigener Channel mit einem Link, `ManualTimeProvider`
   - Erwartet: `SetSelfTransmitting(TargetLinked)` löst einmal `OwnLinkVoice` aus, wiederholte Meldungen nicht. Nach `null`, 100 ms und erneut `TargetLinked` kein Ton, nach `null`, 400 ms und `TargetLinked` wieder einer.
2. `ServerViewModelTests > "OwnLinkVoice_NotWithoutLinksOrForChannelOnly"` (AC3)
   - Gegeben: eigener Channel ohne Link mit `TargetLinked`, dann verlinkt mit `TargetChannel`
   - Erwartet: kein Ton
3. `ServerViewModelTests > "LinkVoice_NotForOwnChannelOrSelf"` bleibt grün: `LinkVoice` weiter nie für die eigene Session (AC5)
4. `SoundTests > "Defaults_EveryEvent_ShortAudibleDistinct"`: `OwnLinkVoice` gleich `LinkVoice`, sonst weiter alle verschieden. `"Deafened_OnlyOwnMicAndSoundTones_AllOffNothing"` um `OwnLinkVoice` ergänzt (AC3, AC4)
5. `SettingsTests > "Sounds_LinkRows_Independent"` (AC4): Stumm für `OwnLinkVoice` lässt `LinkVoice` unberührt
6. `LocalizationTests` bleiben grün (AC5)
7. Manueller Check AC6

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Tests 1 und 2 (rot), `SoundEvent.OwnLinkVoice`, Erkennung in `SetSelfTransmitting`: Wechsel auf `TargetLinked` bei verlinktem eigenen Channel, Zeitpunkt des letzten Endes merken, 300-ms-Regel mit `LinkPause`.
2. Tests 4 und 5, Muster und Pegel von `LinkVoice` teilen, Texte in beiden `resx`.
3. README ergänzen, manueller Check AC6.

### Out of Scope

- Ein Ton beim Loslassen der Link-PTT
- Anzeige, welche Channels man gerade erreicht

---

## Package 61: Durchsichtiger Hintergrund

**Ziel:** Die grossen Hintergrundflächen des Fensters lassen sich von 0 bis 100 % Deckkraft einstellen, auf Wunsch weichgezeichnet, während Text und Bedienelemente lesbar bleiben.

**Abhängigkeiten:** Package 53 (sofort wirkende Regler), 58 (Reihenfolge der Einstellungen)

**Betroffene Dateien:**
- `src/OVS.Client/Settings/ClientSettings.cs` (ändern): `BackgroundOpacity` (0 bis 1, Standard 1), `BlurBackground` (Standard `false`)
- `src/OVS.Client/Views/WindowAppearance.cs` (neu): setzt die Deckkraft der Hintergrund-Pinsel in beiden Themes und den `TransparencyLevelHint` des Fensters
- `src/OVS.Client/Styles/Theme.axaml` (ändern): `Ovs.DialogBg`, `Ovs.DialogBar` in hell und dunkel
- `src/OVS.Client/Styles/Controls.axaml`, `src/OVS.Client/Views/SimpleDialogs.cs` (ändern): Dialoge nutzen die eigenen Pinsel
- `src/OVS.Client/ViewModels/SettingsViewModel.cs` (ändern): `BackgroundOpacityPercent`, `BlurBackground`, in `LivePreview` und `ToSettings`
- `src/OVS.Client/ViewModels/MainViewModel.cs` (ändern): `Appearance` für Vorschau, Speichern und Verwerfen
- `src/OVS.Client/App.axaml.cs` (ändern): wendet `Appearance` wie das Theme an
- `src/OVS.Client/Views/SettingsView.axaml` (ändern): Regler und Checkbox unter Darstellung
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (ändern)
- `tests/OVS.Tests/Client/SettingsTests.cs`, `MainViewModelTests.cs`, `UiSmokeTests.cs` (ändern)
- `README.md` (ändern)

### Kontext

Ein Nutzer möchte die App dahinter sehen können. Alle Farben kommen aus `Styles/Theme.axaml` (je ein `ResourceDictionary` für hell und dunkel). Die grossen Flächen nutzen drei Pinsel: `Ovs.Bg` (Fenster, `MainWindow.Background`), `Ovs.Sidebar` (Seitenleiste, Fusszeilen der Seiten) und `Ovs.SidebarFooter` (Titelleiste, eigene Leiste unten links). Dieselben Pinsel nutzt aber auch der Dialog: `Border.dialog` in `Controls.axaml` hat `Ovs.Bg`, die Button-Leiste in `SimpleDialogs.Show` hat `Ovs.Sidebar`. Karten nutzen `Ovs.Surface`, Menüs und Tooltips die Pinsel des Fluent-Themes.

Die Deckkraft eines `SolidColorBrush` (`Opacity`) wirkt nur auf die Flächen, die ihn nutzen. Text (`Ovs.Text`) bleibt deckend. Damit das Fenster wirklich durchsichtig ist, braucht es zusätzlich `TransparencyLevelHint` (`Transparent` bzw. `AcrylicBlur` zum Weichzeichnen). Die Machbarkeit ist geprüft (A72). Das Theme wendet `App.ApplyTheme` an, wenn sich `MainViewModel.Settings` ändert. Die sofort wirkenden Regler laufen seit Package 53 über `SettingsViewModel.LivePreview`, "Verwerfen" stellt über `MainViewModel` zurück.

### Acceptance Criteria

- [x] AC1: Unter Darstellung gibt es den Regler "Deckkraft des Hintergrunds" von 0 bis 100 % (Standard 100 %) und die Checkbox "Hintergrund weichzeichnen" (Standard aus).
- [x] AC2: Unter 100 % haben `Ovs.Bg`, `Ovs.Sidebar` und `Ovs.SidebarFooter` in hellem und dunklem Theme genau diese Deckkraft, und das Fenster läuft im Transparenzmodus (`Transparent`, mit Weichzeichnen `AcrylicBlur`). Bei 100 % ohne Weichzeichnen ist das Fenster normal undurchsichtig (kein Transparenzmodus).
- [x] AC3: Text, Karten, Eingabefelder, Dialoge, Menüs und Tooltips bleiben voll deckend, auch bei 0 %.
- [x] AC4: Regler und Checkbox wirken sofort, "Verwerfen" stellt die gespeicherten Werte wieder her, "Speichern" übernimmt sie, und sie gelten nach einem Neustart.
- [x] AC5: Werte ausserhalb 0 bis 100 % aus der Datei werden begrenzt. Texte auf Deutsch und Englisch.
- [ ] AC6 (manuell): Am echten Fenster unter Windows 11 bei 50 % und 0 %: Die App dahinter ist sichtbar, Text bleibt lesbar, Klicks auf leere Stellen bleiben im Fenster, mit Weichzeichnen ist der Hintergrund verschwommen. Wechsel zwischen hell und dunkel behält die Deckkraft.

### Tests (TDD)

Reihenfolge: Test schreiben -> rot -> minimal implementieren -> grün -> refactoren.

1. `SettingsTests > "BackgroundAppearance_RoundTrip_Clamped"` (AC1, AC5)
   - Gegeben: neues Profil, dann Regler 40 % und Weichzeichnen an, speichern und laden, ausserdem `BackgroundOpacity = 3` und `-1` in der Datei
   - Erwartet: Standard 1,0 und aus. Nach dem Laden 0,4 und an. Begrenzt auf 1 bzw. 0.
2. `SettingsTests > "Sliders_ApplyLive"` erweitert (AC4): Regler und Checkbox lösen `LivePreview` mit den neuen Werten aus
3. `UiSmokeTests > "BackgroundOpacity_OnlyBackgroundsSeeThrough"` (AC2, AC3)
   - Gegeben: Hauptfenster, `WindowAppearance.Apply` mit 50 % ohne und dann mit Weichzeichnen, dann 100 %. Beide Theme-Varianten
   - Erwartet: `Ovs.Bg`, `Ovs.Sidebar`, `Ovs.SidebarFooter` mit `Opacity` 0,5, `Ovs.Text`, `Ovs.Surface`, `Ovs.DialogBg`, `Ovs.DialogBar` mit 1. `TransparencyLevelHint` `Transparent`, dann `AcrylicBlur`, bei 100 % leer. Ein offener Dialog nutzt `Ovs.DialogBg`.
4. `MainViewModelTests > "Appearance_LivePreview_DiscardRestores_SaveKeeps"` (AC4)
   - Gegeben: Einstellungen öffnen, Regler auf 30 %, verwerfen. Dann 60 % und speichern
   - Erwartet: `Appearance` sofort 30 %, nach dem Verwerfen wieder 100 %. Nach dem Speichern 60 % in `Settings` und in der Datei.
5. `LocalizationTests` bleiben grün (AC5)
6. Manueller Check AC6

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Test 1 (rot), Felder und Begrenzung in `ClientSettings`, Werte im `SettingsViewModel`.
2. Test 3 (rot), `Ovs.DialogBg` und `Ovs.DialogBar` im Theme, Dialoge darauf umstellen, `WindowAppearance.Apply`.
3. Tests 2 und 4 (rot), `LivePreview` erweitern, `MainViewModel.Appearance`, Anwendung in `App.axaml.cs` neben dem Theme.
4. Regler und Checkbox in `SettingsView.axaml` (Abschnitt Darstellung), Texte, README.
5. Manueller Check AC6 am echten Fenster, Screenshot bei 50 % ansehen.

### Out of Scope

- Durchsichtige Karten, Menüs und Dialoge
- `Mica` als eigene Option (nur Windows 11, wirkt kaum anders als Acrylic)
- Eine Tastenkombination zum schnellen Umschalten der Deckkraft

---

## Package 62: Update-Fortschritt

**Ziel:** Während ein Update geladen, geprüft und gestartet wird, liegt über dem ganzen Fenster eine Karte mit Fortschrittsbalken, Prozent und Megabyte.

**Abhängigkeiten:** keine

**Betroffene Dateien:**
- `src/OVS.Client/Net/Updates.cs` (ändern): `UpdateProgress` (neu, Record), `InstallAsync` meldet den Fortschritt über `IProgress<UpdateProgress>`
- `src/OVS.Client/ViewModels/MainViewModel.cs` (ändern): `UpdateProgress? UpdateInProgress` samt abgeleiteten Texten, gesetzt in `CheckForUpdatesAsync`
- `src/OVS.Client/Views/MainWindow.axaml` (ändern): eigene Ebene über allem, auch über `OverlayLayer`
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (ändern): "Update auf {0}", "{0} / {1} MB", "Wird geprüft ..."
- `tests/OVS.Tests/Client/UpdateTests.cs`, `MainViewModelTests.cs`, `UiSmokeTests.cs` (ändern)

### Kontext

`MainViewModel.CheckForUpdatesAsync` fragt über `Dialogs.OfferUpdate`, setzt dann nur `Status = Update_Downloading` und ruft `Installer.InstallAsync` auf. `Status` ist aber nur auf dem Startbildschirm zu sehen (`MainWindow.axaml`, `IsHomePage` und `!IsConnected`). Ist man verbunden oder in den Einstellungen, sieht man während des Downloads (der Client ist etwa 50 MB gross) gar nichts. `UpdateInstaller.InstallAsync` lädt mit `GetStreamAsync` und kennt dadurch die Grösse nicht. Mit `GetAsync(..., HttpCompletionOption.ResponseHeadersRead)` steht sie in `Content.Headers.ContentLength`. Dialoge laufen über `OverlayHost` (A20, eine Karte zur Zeit, mit `SemaphoreSlim`). Der Fortschritt soll sie nicht blockieren und bekommt deshalb eine eigene Ebene statt eines Dialogs. Einen unbestimmten `ProgressBar` gibt es schon beim Verbinden.

### Acceptance Criteria

- [x] AC1: `InstallAsync` meldet beim Laden steigende Byte-Zahlen mit der Gesamtgrösse aus `Content-Length` (oder `null` ohne Grösse), danach die Phase "Prüfen" und zuletzt "Starten". Beim letzten Download-Schritt stimmen die geladenen Bytes mit der Dateigrösse überein.
- [x] AC2: Ab der Zusage im Update-Dialog bis zum Neustart oder Fehler liegt über dem ganzen Fenster eine abgedunkelte Fläche mit einer mittigen Karte: Titel mit Version, Balken mit Prozent und "x,x / y,y MB". Ohne Grösse dreht sich der Balken ohne Prozent. Beim Prüfen und Starten stehen die passenden Texte da.
- [x] AC3: Solange die Karte da ist, kommen keine Klicks und Tastendrücke an das Fenster darunter (Titelleiste ausgenommen, damit man das Fenster verschieben und minimieren kann).
- [x] AC4: Schlägt das Update fehl (Prüfsumme, Netz, Datei), verschwindet die Karte, und der Fehler erscheint wie bisher. Das gilt für den Start-Check und für "Nach Updates suchen" in den Einstellungen gleichermassen.
- [x] AC5: Texte auf Deutsch und Englisch.

### Tests (TDD)

Reihenfolge: Test schreiben -> rot -> minimal implementieren -> grün -> refactoren.

1. `UpdateTests > "Install_ReportsProgress_ThenVerify_ThenStart"` (AC1)
   - Gegeben: Fake-Handler liefert 300 kB mit `Content-Length`, passende Prüfsumme, `IProgress` sammelt die Meldungen synchron
   - Erwartet: mehrere Download-Meldungen, Bytes steigend, Gesamtgrösse 300 kB, die letzte mit 300 kB, danach genau einmal "Prüfen", dann "Starten"
2. `UpdateTests > "Install_WithoutLength_ReportsUnknownTotal"` (AC1)
   - Gegeben: Antwort ohne `Content-Length` (gestreamter Inhalt)
   - Erwartet: Download-Meldungen mit Gesamtgrösse `null`, Installation klappt trotzdem
3. `MainViewModelTests > "Update_Accepted_ShowsProgress_UntilFailure"` (AC2, AC4)
   - Gegeben: Fake-Checker mit Angebot, Dialog sagt ja, Installer mit Fake-Handler, dessen Prüfsumme nicht passt
   - Erwartet: während des Downloads `UpdateInProgress` nicht null mit Version und Prozent-Text, danach null, und die Rückgabe ist `Update_Damaged`
4. `UiSmokeTests > "UpdateProgress_CoversWholeWindow_BlocksInput"` (AC2, AC3)
   - Gegeben: Hauptfenster verbunden, `UpdateInProgress` auf 40 % von 50 MB gesetzt
   - Erwartet: die Ebene ist sichtbar und so gross wie der Inhalt unter der Titelleiste, die Karte steht mittig, Text "40 %" und "20,0 / 50,0 MB". Ein Klick auf den Chat-Senden-Knopf kommt nicht an. Mit `Total = null` ist der Balken `IsIndeterminate`.
5. `LocalizationTests` bleiben grün (AC5)
6. Manueller Check: echtes Update von einer Release-Version aus, verbunden mit einem Server. Die Karte ist sofort sichtbar, und die Prozente laufen hoch.

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Tests 1 und 2 (rot). `UpdateProgress(Phase, long Bytes, long? Total)` anlegen. In `InstallAsync` per `ResponseHeadersRead` laden und in einer Schleife mit Puffer kopieren, höchstens etwa alle 100 ms oder je 1 % melden.
2. Test 3 (rot). `UpdateInProgress` im `MainViewModel` mit `Progress<T>` (meldet auf dem UI-Thread), im `finally` zurücksetzen. Abgeleitete Eigenschaften für Titel, Prozent und MB-Text.
3. Test 4 (rot). Ebene in `MainWindow.axaml` über alle Spalten und über `OverlayLayer` legen: Scrim wie beim `OverlayHost`, Karte im Stil `Border.dialog`, `ProgressBar` mit `Value` und `IsIndeterminate`.
4. Texte in beiden Sprachen, `LocalizationTests`.
5. Manueller Check.

### Out of Scope

- Abbrechen des Downloads (A75)
- Neustart und Aufräumen (Package 63)
- Hintergrund-Download ohne Rückfrage

---

## Package 63: Neustart nach Update

**Ziel:** Nach einem Update startet die neue Version von selbst, die alte `.exe.old` verschwindet, und jeder Fehler auf dem Weg steht im Log.

**Abhängigkeiten:** keine (mit Package 62 parallel möglich, beide ändern `Updates.cs`, aber an verschiedenen Stellen)

**Betroffene Dateien:**
- `src/OVS.Client/Program.cs` (ändern): Option `--after-update <pid>`, Absturz-Logging vor dem Start von Avalonia
- `src/OVS.Client/Net/Updates.cs` (ändern): Argumente für den Neustart, `CleanupOldAsync` wartet auf den alten Prozess, versucht es mehrmals und gibt einen Grund zurück
- `src/OVS.Client/App.axaml.cs` (ändern): Neustart-Callback fängt Fehler ab und loggt sie, Aufräum-Ergebnis ins Log
- `src/OVS.Client/Logging/ClientLog.cs` (ändern, falls nötig): Schreiben ohne laufende App
- `tests/OVS.Tests/Client/UpdateTests.cs` (ändern), dazu die Tests von `ClientOptions.Parse` (ändern)

### Kontext

`UpdateInstaller.InstallAsync` benennt die laufende exe in `OVS.Client.exe.old` um, schiebt die neue an ihre Stelle und ruft den `restart`-Callback aus `App.axaml.cs` auf. Der startet die neue exe mit denselben Argumenten (`Process.Start`, ohne `try`) und ruft danach `desktop.Shutdown()` auf. Beim Start ruft `Program.Main` `UpdateInstaller.CleanupOld` auf. Das löscht `.old` und schluckt `IOException` und `UnauthorizedAccessException` stillschweigend.

Im Log vom 29.09. endet der alte Client nach "Update ... wird installiert" ohne "Client beendet", und ein neuer Prozess wurde nicht gestartet (A77). Es gibt keinen Handler für unbehandelte Ausnahmen, ein Absturz ist also unsichtbar. Das Update wird beim Start aus `window.Opened` angestossen, einem `async void`-Handler, in dem eine Ausnahme den Prozess beendet. Zwei Schwachstellen sind unabhängig von der genauen Ursache sicher:

1. Selbst wenn der neue Prozess startet, läuft sein `CleanupOld` in dem Moment, in dem der alte noch beendet wird. Die Datei ist dann noch gesperrt, das Löschen scheitert still, und es gibt keinen zweiten Versuch in dieser Sitzung.
2. Ein Fehler beim Starten des neuen Prozesses wird weder angezeigt noch geloggt.

Der Client wird als Single-File mit `IncludeNativeLibrariesForSelfExtract` veröffentlicht (`.github/workflows/release.yml`).

### Acceptance Criteria

- [x] AC1: Unbehandelte Ausnahmen (AppDomain, Tasks, UI-Dispatcher) landen mit Stacktrace im Client-Log, bevor der Prozess endet.
- [x] AC2: Die Ursache aus A77 ist reproduziert und als Test festgehalten, der vor dem Fix rot ist (oder, falls sie ausserhalb des Codes liegt, z. B. beim Virenscanner, ist sie mit Log-Zeilen belegt und in A77 nachgetragen).
- [x] AC3: Nach einem erfolgreichen Tausch startet die neue exe mit den bisherigen Argumenten plus `--after-update <pid des alten Prozesses>`. Erst danach beendet sich der alte Client, sauber, mit "Client beendet" im Log.
- [x] AC4: Scheitert der Start der neuen exe, bleibt der alte Client offen, zeigt den Fehler (wie `Update_InstallFailed`) und loggt ihn. Die neue exe liegt dann schon an ihrem Platz, der nächste Start von Hand nutzt sie also.
- [x] AC5: Mit `--after-update <pid>` wartet der neue Client höchstens 10 Sekunden auf das Ende dieses Prozesses und löscht dann `.old`, mit bis zu 5 Versuchen im Abstand von 500 ms, ohne den Start des Fensters aufzuhalten. Das Ergebnis steht im Log ("alte Version entfernt" oder der Grund).
- [x] AC6: Ein normaler Start räumt eine liegengebliebene `.old` weiterhin auf, und ein Fehlschlag wird geloggt statt geschluckt.

### Tests (TDD)

Reihenfolge: Test schreiben -> rot -> minimal implementieren -> grün -> refactoren.

1. Reproduktion zuerst (AC2): Absturz-Logging aus Schritt 1 einbauen, dann ein echtes Update von der vorigen Release-Version durchspielen (Release-exe in einen Testordner kopieren und starten, das Update annehmen). Den Stacktrace bzw. die letzten Log-Zeilen festhalten und daraus einen Unit-Test ableiten, der rot ist. Vermutlich ist das `UpdateTests > "RestartFails_ClientStaysOpen_ErrorShown"` (Restart-Callback wirft `Win32Exception`, erwartet: `InstallAsync` bzw. `CheckForUpdatesAsync` liefert einen Fehlertext, keine Ausnahme).
2. `UpdateTests > "Restart_PassesArgs_PlusAfterUpdatePid"` (AC3)
   - Gegeben: Installer mit Fake-Download, bisherige Argumente `--profile X`
   - Erwartet: der Callback bekommt den exe-Pfad und `--profile X --after-update <Environment.ProcessId>`
3. `ClientOptions`-Test `"Parse_AfterUpdate"` (AC5): `--after-update 1234` ergibt `AfterUpdatePid = 1234`, ungültige Werte werden ignoriert
4. `UpdateTests > "CleanupOld_WaitsForLockedFile_ThenDeletes"` (AC5)
   - Gegeben: `.old` mit `FileShare.None` geöffnet, nach 700 ms wieder freigegeben
   - Erwartet: `CleanupOldAsync` liefert "entfernt", und die Datei ist weg
5. `UpdateTests > "CleanupOld_StillLocked_ReportsReason"` (AC5, AC6)
   - Gegeben: `.old` bleibt die ganze Zeit gesperrt
   - Erwartet: Ergebnis mit Grund, keine Ausnahme, Dauer höchstens etwa 2,5 s (5 × 500 ms)
6. `UpdateTests > "CleanupOld_WaitsForProcessExit"` (AC5)
   - Gegeben: ein kurzlebiger Kindprozess (`cmd /c ping -n 2 127.0.0.1`) als pid
   - Erwartet: gelöscht wird erst, nachdem der Prozess beendet ist
7. Bestehender Test `Success_NewInPlace_OldRenamed_Restarted` bleibt grün
8. Manueller Check (AC3, AC5): echtes Update von einer Release-Version. Die neue Version öffnet sich von selbst, im Ordner liegt danach keine `.old` mehr, und das Log des alten Clients endet mit "Client beendet".

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Absturz-Logging in `Program.Main` (`AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`) und in `App` (`Dispatcher.UIThread.UnhandledException`). Dann die Reproduktion, Test 1 aus dem Befund schreiben (rot).
2. Tests 2 und 3 (rot). `ClientOptions.AfterUpdatePid`, Argumente für den Neustart zusammenbauen, das alte `--after-update` dabei nicht doppelt weitergeben.
3. Restart-Callback in `App.axaml.cs`: `Process.Start` in `try`. Bei Fehler loggen und als Fehlertext zurückgeben, nicht `Shutdown`. Test 1 grün.
4. Tests 4 bis 6 (rot). `CleanupOldAsync(exePath, pid?)`: auf den Prozess warten (`Process.GetProcessById` und `WaitForExitAsync` mit 10 s Timeout, `ArgumentException` heisst schon weg), dann löschen mit Wiederholungen, Ergebnis zurückgeben. In `Program.Main` im Hintergrund starten und das Ergebnis loggen.
5. Manueller Check. Den Befund in A77 nachtragen.

### Out of Scope

- Fortschrittsanzeige (Package 62)
- Signierte Releases oder ein eigener Updater-Prozess
- Rückkehr zur alten Version, wenn die neue nicht startet

---

## Package 64: Eigene Nachrichten rechts

**Ziel:** Eigene Chatnachrichten stehen rechts als Blase ohne Avatar, die anderer links, Hinweise weiter über die volle Breite.

**Abhängigkeiten:** keine

**Betroffene Dateien:**
- `src/OVS.Client/Views/ChatView.axaml` (ändern): Vorlage für Nachrichten aufgeteilt in "eigene" und "andere"
- `src/OVS.Client/Styles/Controls.axaml` (ändern): `Border.bubble` und `Border.bubble.own`
- `src/OVS.Client/Styles/Theme.axaml` (ändern, falls nötig): Pinsel für Text auf der Akzent-Blase in hell und dunkel
- `tests/OVS.Tests/Client/UiSmokeTests.cs` (ändern)

### Kontext

`ChatEntry` (`ViewModels/ChatViewModel.cs`) kennt schon `IsOwn`, `IsMessage`, `IsNotice`, `IsWelcome` und `IsMarker`. Eigene Nachrichten werden in `ChatViewModelTests` bereits als `IsOwn` geprüft, im Channel, allgemein und privat. In `ChatView.axaml` hat heute jede Nachricht denselben Aufbau: `DockPanel` mit Avatar links, Name (mit Klasse `own` und dem Zusatz "Du"), Zeit, `SelectableTextBlock`. Hinweise sind `Border.card` über die volle Breite, Marker ein mittiger `TextBlock`. Der Verlauf ist ein `ItemsControl` mit `MaxWidth="900"`. Das ViewModel muss sich nicht ändern, nur Vorlage und Stil.

### Acceptance Criteria

- [x] AC1: Eigene Nachrichten stehen rechtsbündig in einer Blase mit Akzent-Hintergrund, ohne Avatar, ohne Namen und ohne "Du", mit der Uhrzeit unter dem Text. Die Blase ist so breit wie der Text, höchstens aber etwa 75 % des Verlaufs.
- [x] AC2: Nachrichten anderer stehen links mit Avatar, Name und Zeit wie bisher, der Text in einer neutralen Blase, ebenfalls höchstens etwa 75 % breit.
- [x] AC3: Willkommensnachricht und andere Hinweise nutzen weiter die volle Breite, Marker bleiben mittig.
- [x] AC4: Das gilt in den Tabs Allgemein, Channel und privat. Der Text bleibt markierbar, lange Wörter und URLs brechen um, statt über den Rand zu laufen.
- [x] AC5 (manuell): Im hellen und dunklen Theme ist der Text auf beiden Blasen gut lesbar, auch bei durchsichtigem Hintergrund (Package 61).

### Tests (TDD)

Reihenfolge: Test schreiben -> rot -> minimal implementieren -> grün -> refactoren.

1. `UiSmokeTests > "Chat_OwnRight_OthersLeft_NoticesFullWidth"` (AC1, AC2, AC3)
   - Gegeben: Hauptfenster verbunden, im allgemeinen Chat ein Willkommenshinweis, eine Nachricht von "anna" (Session 2) und eine eigene (Session 1), Layout durchgelaufen
   - Erwartet: Die sichtbare Blase der eigenen Nachricht endet am rechten Rand des Verlaufs (Toleranz 1 px) und hat die Klasse `own`, im Eintrag ist kein Avatar sichtbar. Annas Blase beginnt links nach dem Avatar, der Avatar ist sichtbar. Der Hinweis ist so breit wie der Verlauf.
2. `UiSmokeTests > "Chat_LongMessage_BubbleCappedAndWraps"` (AC1, AC4)
   - Gegeben: eine eigene Nachricht mit 600 Zeichen ohne Leerzeichen
   - Erwartet: Die Blase ist höchstens 75 % des Verlaufs breit, und der Text hat mehrere Zeilen (Höhe grösser als eine Zeile)
3. `UiSmokeTests > "Chat_PrivateTab_OwnRight"` (AC4): dasselbe wie Test 1 in einem privaten Tab
4. Bestehende `ChatViewModelTests` und `Chat_FollowsNewLines_UnlessScrolledUp` bleiben grün
5. Manueller Check AC5: Screenshot hell, dunkel und bei 50 % Deckkraft ansehen

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Test 1 (rot). In `ChatView.axaml` die Nachricht in zwei Zweige teilen: `IsVisible` auf eigene bzw. fremde Nachrichten (dafür `IsOtherMessage` im `ChatEntry` oder ein Converter). Der eigene Zweig richtet sich rechts aus, `HorizontalAlignment="Right"`.
2. Stil `Border.bubble` (Padding, Eckenradius, `Ovs.Surface`) und `Border.bubble.own` (Akzentfarbe, passende Textfarbe) in `Controls.axaml`.
3. Test 2 (rot). Maximale Breite der Blase an die Breite des Verlaufs koppeln (z. B. über ein `Grid` mit Spalten `*,3*` bzw. `3*,*`), `TextWrapping="Wrap"`.
4. Test 3, bestehende Tests grün, manueller Check.

### Out of Scope

- Aufeinanderfolgende Nachrichten desselben Absenders zusammenfassen
- Lesebestätigungen, Reaktionen, Bearbeiten
- Änderungen am Chat-Protokoll oder am ViewModel über eine reine Anzeige-Eigenschaft hinaus

---

## Package 65: Verwaltungs-Button ohne Auslassungspunkte

**Ziel:** Der Button in der Kopfzeile heisst nur noch "Verwaltung" bzw. "Administration".

**Abhängigkeiten:** keine

**Betroffene Dateien:**
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (ändern): `Ui_AdminMenu`
- `tests/OVS.Tests/Client/UiSmokeTests.cs` (ändern)

### Kontext

`MainWindow.axaml` zeigt im verbundenen Kopfbereich `Strings.Ui_AdminMenu` = "Verwaltung ..." / "Administration ...". Die Punkte deuteten früher ein eigenes Fenster an. Die Verwaltung ist aber eine Seite im Hauptfenster (A20).

### Acceptance Criteria

- [x] AC1: Der Button zeigt "Verwaltung" (Deutsch) bzw. "Administration" (Englisch), ohne " ...".
- [x] AC2: Der Button "Admin-Token einlösen ..." bleibt unverändert, weil er einen Dialog öffnet.

### Tests (TDD)

1. `UiSmokeTests > "Header_AdminButton_WithoutEllipsis"` (AC1, AC2)
   - Gegeben: Hauptfenster verbunden als Admin, einmal Deutsch, einmal Englisch
   - Erwartet: Button-Text "Verwaltung" bzw. "Administration", kein Text endet in der Kopfzeile auf "Verwaltung ..."
2. `LocalizationTests` bleiben grün

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Test 1 (rot), `Ui_AdminMenu` in beiden resx anpassen, grün.

### Out of Scope

- Andere Menüeinträge mit " ...", die einen Dialog öffnen

---

## Package 66: Stummer Channel mit eigenem Icon

**Ziel:** Ein stummer Channel zeigt vorne ein Stumm-Icon statt des Lautsprechers und hinter dem Namen kein zusätzliches Stumm-Icon mehr.

**Abhängigkeiten:** keine

**Betroffene Dateien:**
- `src/OVS.Client/Views/MainWindow.axaml` (ändern): Channel-Zeile im `ChannelItems`-Template
- `tests/OVS.Tests/Client/UiSmokeTests.cs` (ändern)

### Kontext

In der Channel-Zeile steht vorne immer `PathIcon Classes="muted channelIcon" Data="{StaticResource Icon.Speaker}"`. Hinter dem Namen folgt bei `ChannelViewModel.IsMuted` ein `PathIcon Classes="sm warning" Data="{StaticResource Icon.MicOff}"` mit Tooltip `Dlg_MutedChannel`. Der Stil `.row.current PathIcon.channelIcon` färbt das vordere Icon im aktuellen Channel ein.

### Acceptance Criteria

- [x] AC1: Bei einem stummen Channel ist vorne `Icon.MicOff` in Warnfarbe mit Tooltip "Stummer Channel: niemand wird gehört" zu sehen, der Lautsprecher nicht.
- [x] AC2: Hinter dem Namen erscheint bei stummen Channels kein Stumm-Icon mehr. Home- und Link-Icon bleiben.
- [x] AC3: Nicht stumme Channels zeigen wie bisher den Lautsprecher. Wird ein Channel stumm geschaltet oder freigegeben, wechselt das Icon sofort.

### Tests (TDD)

1. `UiSmokeTests > "MutedChannel_IconReplacesSpeaker"` (AC1, AC2, AC3)
   - Gegeben: `FakeServers.Admin()`, Lobby per `ChannelUpdated` stumm geschaltet, Layout durchgelaufen
   - Erwartet: in Lobbys Zeile ist das erste sichtbare `PathIcon` das Stumm-Icon mit Tooltip `Dlg_MutedChannel`, kein sichtbares Lautsprecher-Icon, nach dem Namen kein Stumm-Icon. In Raids Zeile ist vorne der Lautsprecher. Nach `IsMuted = false` zeigt Lobby wieder den Lautsprecher.

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Test 1 (rot).
2. Vorne zwei `PathIcon` übereinander (Lautsprecher mit `IsVisible="{Binding !IsMuted}"`, Stumm-Icon mit `IsVisible="{Binding IsMuted}"`, beide `channelIcon`), das hintere Stumm-Icon entfernen. Grün.
3. Manueller Blick im hellen und dunklen Theme, auch im aktuellen Channel.

### Out of Scope

- Die Status-Icons der Nutzer unter dem Channel

---

## Package 67: Link-Icon und passende Breite der Seitenleiste

**Ziel:** Verlinkte Channels zeigen nur noch ein Link-Icon hinter dem Namen (Partner im Tooltip), und beim Betreten eines Servers ist die Seitenleiste breit genug für Namen und Icons.

**Abhängigkeiten:** Package 66 (dieselbe Channel-Zeile)

**Betroffene Dateien:**
- `src/OVS.Client/Views/MainWindow.axaml` (ändern): Channel-Zeile, `ColumnDefinitions` der Haupt-`Grid`
- `src/OVS.Client/Views/MainWindow.axaml.cs` (ändern): Breite beim Verbinden berechnen
- `src/OVS.Client/Views/SidebarWidth.cs` (neu): reine Rechenfunktion für die Wunschbreite
- `tests/OVS.Tests/Client/UiSmokeTests.cs` (ändern), `tests/OVS.Tests/Client/SidebarWidthTests.cs` (neu)

### Kontext

Hinter dem Channel-Namen steht heute bei `IsLinked` ein `Border Classes="chip"` mit Link-Icon und `LinkedNames` (max. 110 px, abgeschnitten). Das kostet viel Platz, siehe Screenshot mit "Artillery, FoB, Infan...". Den Tooltip `Ui_LinkedWithFmt` gibt es schon. Home- und Stumm-Icon stehen als kleine `PathIcon Classes="sm ..."` direkt hinter dem Namen. Die Seitenleiste ist eine feste Spalte mit 300 px (`ColumnDefinitions="300,Auto,*"`). Ihre Breite wird weder gespeichert noch aus dem Inhalt abgeleitet.

### Acceptance Criteria

- [x] AC1: Statt des Chips steht hinter dem Namen nur `Icon.Link` (klein, Klasse `link`), in einer Reihe mit dem Home-Icon. Der Tooltip nennt alle verlinkten Channels ("Verlinkt mit Artillery, FoB, ...").
- [x] AC2: Beim Verbinden mit einem Server (und wenn neue Channels dazukommen oder umbenannt werden, solange der Nutzer die Breite nicht selbst gezogen hat) wird die Seitenleiste so breit, dass der längste Channel-Name samt Icon davor, Home- und Link-Icon dahinter und Nutzerzahl ohne Abschneiden passt, mindestens 240 px.
- [x] AC3: Die Wunschbreite gilt als Mindestbreite der Spalte. Sie wird von Package 68 nach oben begrenzt: Reicht das Fenster nicht, werden die Namen mit "..." abgeschnitten.
- [x] AC4: Zieht der Nutzer die Seitenleiste breiter, bleibt seine Breite bis zum nächsten Verbinden.

### Tests (TDD)

1. `SidebarWidthTests > "Fits_LongestName_PlusIcons"` (AC2)
   - Gegeben: Textbreiten 80, 150, 60 px, Icons und Abstände wie in der Zeile
   - Erwartet: Breite = 150 + Summe der festen Anteile. Mit nur kurzen Namen: 240.
2. `UiSmokeTests > "LinkedChannel_IconWithTooltip_NoChip"` (AC1)
   - Gegeben: `FakeServers.Admin()` (Lobby und Raid verlinkt)
   - Erwartet: in Lobbys Zeile kein `Border.chip`, ein sichtbares Link-Icon mit Tooltip "Verlinkt mit Raid"
3. `UiSmokeTests > "Connect_SidebarFitsLongestChannel"` (AC2, AC3)
   - Gegeben: Server mit Channels wie im Screenshot ("Infantry Squad 1", "Sabotage Squad", ...), alle verlinkt, Fenster 1100 px
   - Erwartet: kein `TextBlock.channelName` ist abgeschnitten (`DesiredSize` passt in `Bounds`), die Seitenleiste ist höchstens so breit wie nötig plus 1 px
4. `UiSmokeTests > "UserDraggedWidth_KeptUntilReconnect"` (AC4)

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Test 2 (rot), Chip durch `PathIcon` ersetzen, grün.
2. Test 1 (rot), `SidebarWidth.For(nameWidths, …)`, grün.
3. Tests 3 und 4 (rot). In `MainWindow.axaml.cs` beim Wechsel auf verbunden und bei Änderungen an `Server.Channels` die Namen mit `TextLayout` messen und die Spaltenbreite setzen, solange kein eigener Zug per `GridSplitter.DragCompleted` erfolgt ist. Grün.

### Out of Scope

- Die Breite speichern (A81)
- Obergrenze der Seitenleiste (Package 68)

---

## Package 68: Responsives Grundgerüst

**Ziel:** Das Hauptfenster passt sich jeder Breite ab 360 px an: Die Kopfzeile wird nie abgeschnitten, und unter 700 px wird die Seitenleiste zu einer einblendbaren Leiste.

**Abhängigkeiten:** Package 67 (setzt die Breite der Seitenleiste)

**Betroffene Dateien:**
- `src/OVS.Client/Views/Responsive.cs` (neu): Breitenstufen, setzt die Klassen `compact` und `narrow` am Fenster
- `src/OVS.Client/Views/MainWindow.axaml`, `MainWindow.axaml.cs` (ändern): Mindestgrösse, Seitenleiste als Überlagerung, Menü-Button, Kopfzeile, Obergrenze der Seitenleiste
- `src/OVS.Client/Styles/Controls.axaml` (ändern): Stile für `compact` und `narrow`
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (ändern): "Channels anzeigen"
- `tests/OVS.Tests/Client/ResponsiveTests.cs` (neu), `tests/OVS.Tests/TestSupport/LayoutAssert.cs` (neu), `UiSmokeTests.cs` (ändern)

### Kontext

Das Fenster ist mindestens 760 × 480 px gross (`MainWindow.axaml`). Die Haupt-`Grid` hat `ColumnDefinitions="300,Auto,*"` ohne Grenzen, der `GridSplitter` lässt die Seitenleiste beliebig breit ziehen. Dadurch rutschen im Hauptbereich die Kopfzeilen-Buttons (Ping, Verwaltung, Trennen) aus dem Bild (zweiter Screenshot zu Package 68). Die Seiten (Einstellungen, Verwaltung) haben feste Breiten und brechen nicht um (Screenshots zur Einstellungsseite). Es gibt keine gemeinsame Regel für schmale Breiten. Die Tests rendern das Fenster headless (`UiSmokeTests`, `main.Show()` mit fester `Width`).

### Acceptance Criteria

- [x] AC1: Das Fenster ist mindestens 360 × 480 px gross. Es gibt zwei Stufen, die als Klassen am Fenster hängen, damit jede Seite per Stil darauf reagieren kann:
  - `compact`: Fensterbreite unter 700 px
  - `narrow`: Hauptbereich schmaler als 560 px
  Die Stufen wechseln sofort beim Ändern der Grösse.
- [x] AC2: Ab 700 px stehen Seitenleiste und Hauptbereich nebeneinander. Die Seitenleiste ist höchstens so breit, dass der Hauptbereich die Kopfzeile ohne Titel fasst: Voice-Icon, Ping, alle sichtbaren Buttons, Innenabstand. Das gilt beim Ziehen, beim Verkleinern und wenn Buttons erscheinen oder verschwinden. Reicht der Platz nicht, gibt die Seitenleiste bis 200 px nach.
- [x] AC3: Unter 700 px (`compact`) ist die Seitenleiste ausgeblendet, der Hauptbereich nutzt die ganze Breite.
  - Ein Menü-Button oben links ("Channels anzeigen") blendet sie als Überlagerung von links ein, höchstens Fensterbreite minus 48 px, darunter abgedunkelt.
  - Sie schliesst sich durch einen Klick daneben, durch Esc, beim Betreten eines Channels und beim Öffnen von Einstellungen oder Verwaltung.
  - Wird das Fenster wieder breit, erscheint die Seitenleiste normal.
- [x] AC4: Die Kopfzeile wird nie abgeschnitten. Der Channel-Titel wird gekürzt oder verschwindet. Unter `narrow` zeigen "Verwaltung" und "Trennen" nur noch ihr Icon, mit dem Text als Tooltip und Namen für Screenreader, und die Ping-Anzeige nur noch das Icon mit dem Wert im Tooltip.
- [x] AC5: Ohne Verbindung (Startseite) gelten dieselben Regeln, damit die Breite beim Verbinden nicht springt.
- [x] AC6: Eine Test-Hilfe `LayoutAssert.FitsHorizontally(window)` prüft, dass kein sichtbarer Button, Regler, Eingabefeld, Checkbox oder Text ausserhalb des sichtbaren Bereichs seines Scroll-Containers bzw. des Fensters liegt. Sie wird in den Packages 77 bis 79 für alle Seiten genutzt.

### Tests (TDD)

Bug-Reproduktion zuerst:

1. `ResponsiveTests > "SidebarDraggedWide_HeaderButtonsStayVisible"` (AC2, AC4)
   - Gegeben: Fenster 776 px wie im Screenshot, verbunden, Seitenleiste auf 600 px gesetzt (wie ein Zug am Splitter)
   - Erwartet (vorher rot): Seitenleiste auf die Obergrenze zurückgenommen, Ping, Verwaltung und Trennen vollständig im Fenster
2. `ResponsiveTests > "Classes_FollowWidth"` (AC1): 1100 px keine Klasse, 650 px `compact`, 360 px `compact` und `narrow`, zurück auf 1100 px beide weg
3. `ResponsiveTests > "Compact_SidebarAsOverlay_OpenClose"` (AC3)
   - Gegeben: 360 px, verbunden
   - Erwartet: Seitenleiste unsichtbar, Menü-Button sichtbar. Klick öffnet sie als Überlagerung, höchstens 312 px breit. Doppelklick auf Raid betritt den Channel und schliesst sie. Esc und Klick auf die Abdunklung schliessen sie ebenfalls.
4. `ResponsiveTests > "Narrow_HeaderIconOnly_NothingClipped"` (AC4, AC6): 360 px, Admin und Nicht-Admin (mit "Admin-Token einlösen"), `LayoutAssert.FitsHorizontally`
5. `ResponsiveTests > "WindowShrinks_SidebarGivesWay"` (AC2): Seitenleiste 350 px, Fenster von 1100 auf 760 px, Buttons sichtbar, Seitenleiste mindestens 200 px
6. `ResponsiveTests > "StartScreen_SameRules"` (AC5)
7. `LayoutAssertTests > "DetectsClippedButton"`: ein absichtlich zu breites Panel wird erkannt (die Hilfe selbst ist korrekt)
8. `LocalizationTests` bleiben grün

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Tests 7 und 1 (rot): `LayoutAssert` schreiben, Bug reproduzieren.
2. `Responsive.Attach(window)`: bei `SizeChanged` die Klassen setzen und die Obergrenze der Spalte berechnen. Grün für Test 1, dann Tests 2 und 5.
3. Test 3 (rot): Seitenleisten-Inhalt in ein eigenes `UserControl` ziehen, das je nach `compact` in Spalte 0 oder in einer Überlagerungs-Ebene (wie `OverlayHost`, aber links angedockt) steht. Menü-Button in Kopfzeile und Startseite.
4. Test 4 (rot): Button-Texte per Stil `Window.narrow` ausblenden, Tooltips und `AutomationProperties.Name` setzen. Test 6.
5. Manueller Check: echtes Fenster auf 360, 600, 776 und 1100 px ziehen.

### Out of Scope

- Die einzelnen Seiten (Packages 77 bis 79)
- Touch-Gesten (Wischen zum Öffnen) und eine echte Handy-App
- Speichern der Seitenleistenbreite

---

## Package 69: Servereinstellungen in der Verwaltung

**Ziel:** Nutzerlimit, Log-Aufbewahrung, tägliche Log-Datei und automatischer Neustart lassen sich unter "Verwaltung -> Server" ändern und wirken sofort, Umgebungsvariablen gelten nur noch beim ersten Start.

**Abhängigkeiten:** keine

**Betroffene Dateien:**
- `src/OVS.Server/Data/ServerData.cs` (ändern): `ServerSettings` um `MaxUsers`, `LogDays`, `LogRotateDaily`, `AutoRestart`, `AutoRestartTime`, `DataVersion` 3 mit Migration
- `src/OVS.Server/ServerConfig.cs` (ändern): die Werte heissen jetzt Startwerte
- `src/OVS.Server/ServerState.cs` (ändern): `Admit` liest das Limit aus den Einstellungen, Ereignis `SettingsChanged`
- `src/OVS.Server/ServerHost.cs` (ändern): Wartezeit bis zum Neustart neu berechnen, wenn sich die Einstellung ändert
- `src/OVS.Server/Logging/ServerLogs.cs` (ändern): Aufbewahrung und Tagesdatei zur Laufzeit ändern
- `src/OVS.Server/Commands/AdminCommands.cs` (ändern): `OnUpdateServerSettings`
- `src/OVS.Shared/Protocol/Messages.cs`, `ProtocolInfo.cs` (ändern): `UpdateServerSettings`, `ServerSettingsInfo` erweitert, Protokollversion 10
- `src/OVS.Client/ViewModels/AdminViewModel.cs`, `src/OVS.Client/Views/AdminView.axaml` (ändern): Felder im Server-Tab
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (ändern)
- `README.md`, `docker-compose.yml` (ändern)
- `tests/OVS.Tests/Server/ServerConfigTests.cs`, `DataStoreTests.cs`, `AdminCommandTests.cs`, `ServerHostTests.cs`, `ServerLogsTests.cs`, `tests/OVS.Tests/Client/AdminViewModelTests.cs` (ändern)

### Kontext

`ServerConfig.Load` liest `OVS_MAX_USERS`, `OVS_LOG_DAYS`, `OVS_LOG_ROTATE_DAILY`, `OVS_AUTO_RESTART`, `OVS_AUTO_RESTART_TIME` (Umgebung vor `server-config.json` vor Standard) bei jedem Start neu. Diese Werte leben nur im unveränderlichen `ServerConfig`: `ServerState.Admit` prüft `config.MaxUsers`, `ServerHost.RunOnceAsync` baut `ServerLogs` mit `LogDays`/`LogRotateDaily` und plant den Neustart aus `AutoRestartAt`. Name und Passwort sind schon heute nur Startwerte: `ServerData.CreateDefault` übernimmt sie, danach gilt `server-data.json`. Zur Laufzeit änderbar sind über `UpdateServerSettings` nur Name, Willkommenstext und Passwort (Recht `ServerConfig`).

### Acceptance Criteria

- [x] AC1: Im Server-Tab gibt es "Maximale Nutzer" (1 bis 100000), "Logs aufbewahren (Tage, 0 = unbegrenzt)" (0 bis 3650), "Jeden Tag eine neue Log-Datei", "Automatischer Neustart" mit Uhrzeit. Speichern schickt alle Werte mit Name, Willkommenstext und Passwort.
- [x] AC2: Der Server übernimmt die Werte, speichert sie in `server-data.json` und schickt sie an alle Clients mit dem Recht. Ungültige Werte werden mit `InvalidValue` abgelehnt, nichts wird teilweise übernommen.
- [x] AC3: Die Werte wirken ohne Neustart: das Limit beim nächsten Beitritt (niemand wird rausgeworfen, wenn es unter die aktuelle Zahl sinkt), die Aufbewahrung bei der nächsten Log-Bereinigung, die Tagesdatei ab der nächsten Datei, der Neustart-Zeitplan sofort (an, aus, neue Uhrzeit).
- [x] AC4: Beim ersten Start nach dem Update (Datenversion 2 -> 3) werden die aktuellen Werte aus Umgebung, `server-config.json` oder Standard einmal übernommen. Danach werden `OVS_MAX_USERS`, `OVS_LOG_DAYS`, `OVS_LOG_ROTATE_DAILY`, `OVS_AUTO_RESTART`, `OVS_AUTO_RESTART_TIME` und die gleichnamigen Werte in `server-config.json` ignoriert. Weicht ein gesetzter Wert vom gespeicherten ab, steht beim Start ein Hinweis im Log.
- [x] AC5: `OVS_PORT` und `OVS_DATA_DIR` bleiben Einstellungen der Umgebung (A85).
- [x] AC6: README und `docker-compose.yml` beschreiben die Variablen als Startwerte und verweisen auf die Verwaltung. Texte auf Deutsch und Englisch.

### Tests (TDD)

1. `DataStoreTests > "V2_Migrates_SettingsFromConfig"` (AC4)
   - Gegeben: `server-data.json` der Version 2, Config mit MaxUsers 20, LogDays 7, AutoRestart 03:30
   - Erwartet: Version 3, die Werte stehen in `Settings`. Zweiter Start mit MaxUsers 99 in der Config ändert nichts.
2. `ServerConfigTests > "EnvDiffersFromStored_HintInLog"` (AC4)
3. `AdminCommandTests > "UpdateSettings_Limits_ValidatedAndBroadcast"` (AC2)
   - Gegeben: Admin schickt MaxUsers 0, dann LogDays 5000, dann gültige Werte
   - Erwartet: zweimal `InvalidValue` ohne Änderung, dann `ServerSettingsChanged` mit den neuen Werten, Datei aktualisiert
4. `AdminCommandTests > "MaxUsersLowered_NextJoinRejected_NobodyKicked"` (AC3)
   - Gegeben: 3 Nutzer verbunden, Limit auf 2
   - Erwartet: alle 3 bleiben, ein vierter bekommt `ServerFull`
5. `ServerHostTests > "AutoRestartChanged_ScheduleFollows"` (AC3)
   - Gegeben: `ManualTimeProvider`, Neustart aus, dann per Einstellung an für in 10 Minuten
   - Erwartet: nach 10 Minuten Zeitvorschub startet ein neuer Lauf. Ausschalten davor verhindert ihn.
6. `ServerLogsTests > "RetentionChangedAtRuntime_NextCleanupUsesIt"` (AC3)
7. `AdminViewModelTests > "SaveServerSettings_SendsAllFields"` (AC1), bestehende `SaveServerSettings_PasswordSemantics` bleibt grün
8. `UiSmokeTests`: neue Felder im Server-Tab, `LocalizationTests` (AC6)

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Test 1 (rot): Felder in `ServerSettings`, `Migrate()` auf Version 3 mit den Config-Werten, `CreateDefault` ebenso. Grün.
2. Test 2: Vergleich und Hinweis beim Start in `ServerState`.
3. Tests 3 und 4 (rot): Protokoll erweitern (Version 10), Validierung in `OnUpdateServerSettings`, `Admit` nutzt `data.Settings.MaxUsers`.
4. Tests 5 und 6 (rot): `ServerState.SettingsChanged`, `ServerHost` wartet auf "Zeit erreicht oder Einstellung geändert" und rechnet neu. `ServerLogs` bekommt `Update(logDays, rotateDaily)`.
5. Test 7, UI-Felder, Texte, README und compose.

### Out of Scope

- Port und Datenverzeichnis ändern (A85)
- Einstellungen für einzelne Channels (gibt es schon)

---

## Package 70: Nutzerstatistiken auf dem Server

**Ziel:** Der Server merkt sich pro Nutzer ersten und letzten Login, Anzahl Logins, Online-Zeit, letzte IP, frühere Nicknames, Sprechzeit und Anzahl Chatnachrichten und liefert sie mit der Nutzerliste aus.

**Abhängigkeiten:** Package 69 (beide ändern das Datenformat, 69 kommt zuerst)

**Betroffene Dateien:**
- `src/OVS.Server/Data/ServerData.cs` (ändern): `UserRecord`
- `src/OVS.Server/ServerState.cs` (ändern): `Admit`, `RemoveLocked`, Herunterfahren
- `src/OVS.Server/Session.cs` (ändern): Verbindungsbeginn, gezählte Sprechzeit und Nachrichten der laufenden Sitzung
- `src/OVS.Server/Voice/UdpVoiceServer.cs` (ändern): Sprechzeit zählen
- `src/OVS.Server/Commands/ChatCommands.cs` (ändern): Nachrichten zählen
- `src/OVS.Server/Commands/AdminCommands.cs` (ändern): `OnListUsers`
- `src/OVS.Shared/Protocol/Messages.cs` (ändern): `KnownUserInfo` erweitert
- `tests/OVS.Tests/Server/UserStatsTests.cs` (neu), `DataStoreTests.cs` (ändern)

### Kontext

`UserRecord` hat heute nur `Fingerprint`, `LastNickname`, `GroupIds` und `FirstSeen` (gesetzt beim ersten `Admit`). Die IP steht nur in der flüchtigen `Session`. Das Log schreibt Verbinden und Trennen mit IP, gespeichert wird davon nichts. `ListUsers` (Recht `GroupsAssign`) liefert `KnownUserInfo(Fingerprint, LastNickname, GroupIds)`. Der Server speichert nach jedem Login synchron unter dem globalen Lock. Sprachpakete laufen durch `UdpVoiceServer.HandleAsync` (Fall `PacketType.Voice`, ein Paket = 20 ms), Chatnachrichten durch `ChatCommands`.

### Acceptance Criteria

- [x] AC1: Beim Login werden letzter Login (Zeit), Anzahl Logins (+1) und letzte IP gespeichert. Ein neuer Nickname schiebt den bisherigen in "frühere Nicknames" (höchstens 5, neueste zuerst, ohne Doppelte, ohne den aktuellen).
- [x] AC2: Beim Trennen (auch Kick, Ban, Zeitüberschreitung, Ersetzen) und beim Herunterfahren werden Online-Zeit, Sprechzeit und Chatnachrichten der Sitzung aufaddiert und gespeichert. Zwischen Login und Trennen wird dafür nicht gespeichert.
- [x] AC3: Sprechzeit zählt nur weitergeleitete Sprachpakete (20 ms je Paket), Sprache in einen stummen Channel zählt nicht. Chatnachrichten zählen alle gesendeten Nachrichten (Server, Channel, privat).
- [x] AC4: `KnownUserInfo` enthält alle Werte, dazu ob der Nutzer online ist und seine Session-Id. Für Online-Nutzer enthalten Online-Zeit, Sprechzeit und Nachrichten die laufende Sitzung schon mit.
- [x] AC5: Bestehende Nutzer behalten `FirstSeen`. Die neuen Werte starten leer bzw. bei 0 und werden in der Oberfläche als "unbekannt" gezeigt (Package 71).

### Tests (TDD)

1. `UserStatsTests > "Login_SetsLastLoginCountIp"` (AC1): zweimal verbinden mit `ManualTimeProvider`, Werte in `ServerData` prüfen
2. `UserStatsTests > "NicknameChange_KeepsFiveNewestDistinct"` (AC1)
3. `UserStatsTests > "Disconnect_AddsOnlineTimeSpeechChat"` (AC2, AC3)
   - Gegeben: Login, 90 s Zeitvorschub, 50 Sprachpakete über UDP, 3 Chatnachrichten, Trennen
   - Erwartet: Online-Zeit 90 s, Sprechzeit 1 s, Nachrichten 3, gespeichert
4. `UserStatsTests > "MutedChannel_SpeechNotCounted"` (AC3)
5. `UserStatsTests > "ListUsers_IncludesLiveSession"` (AC4)
6. `UserStatsTests > "Shutdown_PersistsOpenSessions"` (AC2)
7. `DataStoreTests > "OldUsers_KeepFirstSeen_NewFieldsEmpty"` (AC5)

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Tests 1 und 2 (rot), Felder in `UserRecord`, Pflege in `Admit`.
2. Tests 3, 4 und 6 (rot), Zähler in `Session`, Aufaddieren in `RemoveLocked` und beim Herunterfahren.
3. Tests 5 und 7 (rot), `KnownUserInfo` erweitern.

### Out of Scope

- Anzeige in der Verwaltung (Package 71)
- Verlauf einzelner Sitzungen oder Statistiken pro Channel

---

## Package 71: Nutzerübersicht mit Details, Suche und Filter

**Ziel:** Unter "Verwaltung -> Nutzer" sieht man zu jedem bekannten Nutzer alle gespeicherten Daten und kann die Liste durchsuchen und filtern.

**Abhängigkeiten:** Package 70, Package 76

**Betroffene Dateien:**
- `src/OVS.Client/ViewModels/AdminViewModel.cs` (ändern): `KnownUserViewModel`, Suche, Filter, Sortierung
- `src/OVS.Client/Views/AdminView.axaml` (ändern): Tab "Nutzer"
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (ändern)
- `tests/OVS.Tests/Client/AdminViewModelTests.cs`, `UiSmokeTests.cs` (ändern)

### Kontext

Der Tab "Nutzer" (`ShowUsers` = Recht `GroupsAssign`) zeigt je Nutzer eine Karte mit Avatar, Nickname, gekürztem Fingerabdruck und Gruppen-Checkboxen (`GroupToggle`). Es gibt keine Suche, keinen Filter, keinen Online-Status. Die Liste kommt aus `UserList`, sortiert nach `LastNickname`. Ab Package 76 hängt der Tab am Recht `UsersView`, die Checkboxen an `GroupsAssign`.

### Acceptance Criteria

- [x] AC1: Jede Karte zeigt Nickname, Online-Punkt, Gruppen, erster und letzter Login (Datum und Uhrzeit, lokal), Anzahl Logins, Online-Zeit, Sprechzeit, Chatnachrichten, letzte IP, frühere Nicknames, gekürzten Fingerabdruck (voll im Tooltip, kopierbar) und einen aktiven Ban mit Grund und Ablauf. Leere Werte heissen "unbekannt".
- [x] AC2: Ein Suchfeld filtert sofort nach Nickname, früheren Nicknames, Fingerabdruck und IP, ohne Gross- und Kleinschreibung.
- [x] AC3: Filter: Status (alle, online, offline, gebannt) und Gruppe (alle oder eine Gruppe). Sortierung: Name, letzter Login (neueste zuerst), Online-Zeit. Die Trefferzahl steht über der Liste ("12 von 40 Nutzern").
- [x] AC4: Suche, Filter und Details sind für jeden mit `UsersView` nutzbar (Package 76). Ohne `GroupsAssign` sind die Gruppen-Checkboxen sichtbar, aber gesperrt.
- [x] AC5: Die Liste aktualisiert sich, wenn Nutzer kommen und gehen (Online-Status), ohne dass Suche und Filter zurückgesetzt werden.
- [x] AC6: Texte auf Deutsch und Englisch.

### Tests (TDD)

1. `AdminViewModelTests > "UserDetails_FromKnownUserInfo"` (AC1): alle Felder und "unbekannt" für leere Werte
2. `AdminViewModelTests > "Search_MatchesNicknamePreviousFingerprintIp"` (AC2)
3. `AdminViewModelTests > "Filter_StatusGroup_Sort_Count"` (AC3)
4. `AdminViewModelTests > "UsersViewOnly_SearchWorks_TogglesLocked"` (AC4)
5. entfällt, die Rechteprüfung von `ListUsers` testet Package 76
6. `AdminViewModelTests > "UserJoins_ListRefreshed_FilterKept"` (AC5)
7. `UiSmokeTests`: Nutzer-Tab mit Suchfeld und Filtern rendert ohne Binding-Fehler, `LocalizationTests` (AC6)

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Tests 1 bis 3 (rot), `KnownUserViewModel` erweitern, `SearchText`, `StatusFilter`, `GroupFilter`, `SortOrder`, `VisibleUsers`.
2. Test 4.
3. Test 6: bei `UserJoined`/`UserLeft` die Liste neu anfordern (höchstens einmal pro Sekunde).
4. XAML: Suchfeld und zwei `ComboBox`en über der Liste, Karte mit zweispaltigem Detailbereich. Test 7.

### Out of Scope

- Bannen, Entbannen, Löschen (Package 72)
- Export der Liste

---

## Package 72: Nutzer offline bannen, entbannen und löschen

**Ziel:** In der Nutzerübersicht lassen sich Nutzer auch offline bannen und entbannen sowie nach einer Rückfrage mit allen Daten löschen.

**Abhängigkeiten:** Package 71, Package 76

**Betroffene Dateien:**
- `src/OVS.Shared/Protocol/Messages.cs`, `Codes.cs` (ändern): `BanUser`, `DeleteUser`, Code `UserDeleted`
- `src/OVS.Server/Commands/ModerationCommands.cs` (ändern): `OnBanUser`, `OnDeleteUser`
- `src/OVS.Client/ViewModels/AdminViewModel.cs`, `src/OVS.Client/Views/AdminView.axaml` (ändern): Buttons je Karte
- `src/OVS.Client/Views/SimpleDialogs.cs`, `src/OVS.Client/ViewModels/ServerViewModel.cs`, `src/OVS.Client/App.axaml.cs` (ändern): Lösch-Dialog im `Dialogs`-Record
- `src/OVS.Client/ErrorTexts.cs`, `Localization/Strings.resx`, `Strings.en.resx` (ändern)
- `tests/OVS.Tests/Server/ModerationTests.cs`, `tests/OVS.Tests/Client/AdminViewModelTests.cs`, `UiSmokeTests.cs` (ändern)

### Kontext

`Ban(SessionId, Reason, DurationMinutes, IncludeIp)` wirkt nur auf verbundene Nutzer (`FindTarget` sucht in `sessions`). `BanRecord` speichert Fingerabdruck und optional IP, `Admit` prüft beides. `Unban(BanId)` gibt es, im Tab "Bans". Nutzerdaten löschen gibt es nicht. Der Ban-Dialog (`SimpleDialogs.Ban`) liefert Grund, Dauer und "IP mitbannen". `Dialogs.Confirm` zeigt einen roten Bestätigungsdialog.

### Acceptance Criteria

- [x] AC1: Jede Karte hat mit Recht `UserBan` "Bannen" (öffnet den vorhandenen Ban-Dialog) bzw. bei aktivem Ban "Entbannen". Das wirkt auch für Offline-Nutzer. "IP mitbannen" nutzt bei Offline-Nutzern die gespeicherte letzte IP (ohne IP ist die Option gesperrt).
- [x] AC2: Bannen ist nur bei Nutzern erlaubt, deren Rechte (aus ihren Gruppen) eine Teilmenge der eigenen sind, sonst `PermissionDenied`. Man kann sich nicht selbst bannen. Ist der Nutzer online, wird er wie bisher mit Grund getrennt.
- [x] AC3: "Nutzerdaten löschen" öffnet einen eigenen roten Dialog: "Alle Daten von {Name} löschen?" mit Aufzählung (Nutzerdatensatz, Gruppen, Statistiken, Bans) und dem Hinweis, dass der Nutzer danach als neu gilt. Erst "Endgültig löschen" schickt die Anfrage.
- [x] AC4: Der Server entfernt den `UserRecord` und alle `BanRecord`s mit diesem Fingerabdruck und speichert. Ist der Nutzer online, wird er mit Code `UserDeleted` getrennt ("Deine Nutzerdaten wurden auf diesem Server gelöscht."). Verbindet er sich neu, ist er ein neuer Nutzer (Gast, neues `FirstSeen`).
- [x] AC5: Löschen braucht `UserDelete` (Package 76) und dieselbe Rechte-Regel wie Bannen. Sich selbst und den letzten Admin kann man nicht löschen (`LastAdmin`). Das Löschen wird im Server-Log vermerkt. Die Logdateien bleiben (A88).
- [x] AC6: Nach jeder Aktion aktualisieren sich Nutzer- und Bans-Tab. Texte auf Deutsch und Englisch, `ErrorTexts` kennt `UserDeleted`.

### Tests (TDD)

1. `ModerationTests > "BanUser_Offline_BlocksNextLogin"` (AC1): Gast verbindet, trennt, Moderator bannt per Fingerabdruck, Gast bekommt `Banned`
2. `ModerationTests > "BanUser_OfflineWithIp_UsesLastIp"` (AC1)
3. `ModerationTests > "BanUser_StrongerOrSelf_Denied"` (AC2)
4. `ModerationTests > "BanUser_Online_Disconnected"` (AC2)
5. `ModerationTests > "DeleteUser_RemovesRecordAndBans_RejoinsAsNew"` (AC4)
   - Gegeben: Nutzer in Gruppe Moderator mit Statistiken und einem abgelaufenen und einem aktiven Ban
   - Erwartet: kein `UserRecord`, keine Bans mit dem Fingerabdruck, erneuter Login als Gast mit neuem `FirstSeen`
6. `ModerationTests > "DeleteUser_Online_KickedWithMessage"` (AC4)
7. `ModerationTests > "DeleteUser_SelfLastAdminStronger_Denied"` (AC5)
8. `AdminViewModelTests > "DeleteUser_AsksFirst_SendsOnlyAfterConfirm"` (AC3)
9. `AdminViewModelTests > "BanUnbanDeleteButtons_ByStateAndRight"` (AC1, AC5, AC6): Bannen und Entbannen nur mit `UserBan`, Löschen nur mit `UserDelete`
10. `UiSmokeTests`: Lösch-Dialog rendert, `LocalizationTests`, `SettingsTests > "ErrorTexts_EveryCodeHasText"` (AC6)

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Tests 1 bis 4 (rot): `BanUser(Fingerprint, Reason, DurationMinutes, IncludeIp)`, Rechte über die Gruppen des `UserRecord`, bei Online-Nutzern die bestehende Trennlogik.
2. Tests 5 bis 7 (rot): `DeleteUser(Fingerprint)`, `Codes.UserDeleted`.
3. Tests 8 und 9 (rot): Buttons und `Dialogs.ConfirmDeleteUser`.
4. Test 10, Texte.

### Out of Scope

- Logzeilen über den Nutzer entfernen (A88)
- Mehrere Nutzer auf einmal bearbeiten

---

## Package 73: Töne bei Gruppenänderung

**Ziel:** Wer einem Nutzer eine Gruppe gibt oder nimmt, und der betroffene Nutzer, falls online, hören je einen eigenen, einzeln einstellbaren Ton, standardmässig denselben.

**Abhängigkeiten:** keine (nutzt die Nutzerliste, wie sie heute schon ist)

**Betroffene Dateien:**
- `src/OVS.Client/Audio/Sounds.cs` (ändern): `SoundEvent.GroupChanged`, `SoundEvent.GroupChangedByMe`, gemeinsamer Standardton
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (ändern): eigener `UserUpdated` mit anderen `GroupIds`
- `src/OVS.Client/ViewModels/AdminViewModel.cs` (ändern): Ton, wenn die eigene Zuweisung in der nächsten `UserList` angekommen ist
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (ändern): `Sound_GroupChanged`, `Sound_GroupChangedByMe`
- `README.md` (ändern)
- `tests/OVS.Tests/Client/SoundTests.cs`, `ServerViewModelTests.cs`, `AdminViewModelTests.cs`, `SettingsTests.cs` (ändern)

### Kontext

Sounds sind die Werte von `SoundEvent` (Reihenfolge = Reihenfolge in den Einstellungen). Standardtöne kommen aus `SoundSynth.Patterns`, Zeilen der Einstellungen entstehen automatisch aus dem Enum, der Name aus `Sound_<Event>`. Das Paar `LinkVoice`/`OwnLinkVoice` (Packages 57 und 60) ist genau dieses Muster: zwei Einträge, gemeinsamer Ton, die Liste `shared` in `SoundTests.Defaults_EveryEvent_ShortAudibleDistinct`. Gruppenänderungen schickt der Server als `UserUpdated` an alle, aber nur für Online-Nutzer. Der Handelnde bekommt keine Bestätigung. `AdminViewModel` fordert nach `AssignGroup`/`UnassignGroup` die `UserList` neu an.

### Acceptance Criteria

- [x] AC1: Zwei neue Töne, "Eigene Gruppe geändert" und "Gruppe vergeben oder entzogen", mit eigener Zeile (Datei, Lautstärke, Stumm), standardmässig derselbe kurze Ton, der sich von den anderen unterscheidet.
- [x] AC2: Der betroffene Nutzer hört "Eigene Gruppe geändert", wenn sich die Gruppen im eigenen `UserUpdated` ändern (neu dazu oder entfernt), einmal je Änderung, nicht beim Verbinden.
- [x] AC3: Der Handelnde hört "Gruppe vergeben oder entzogen", sobald die nächste Nutzerliste die gewünschte Änderung zeigt, auch bei Offline-Nutzern. Kein Ton bei einem Fehler (z. B. `LastAdmin`).
- [x] AC4: Ändert man die eigene Gruppe, hört man nur einen Ton, "Gruppe vergeben oder entzogen".
- [x] AC5: Wie alle Töne: aus bei "Alle Sounds aus", bei "Ton aus" nicht zu hören.

### Tests (TDD)

1. `SoundTests > "Defaults_EveryEvent_ShortAudibleDistinct"` erweitert (AC1): das neue Paar in `shared`
2. `SettingsTests > "Sounds_GroupRows_Independent"` (AC1)
3. `ServerViewModelTests > "OwnGroupsChanged_SoundOnce_NotOnConnect"` (AC2)
4. `AdminViewModelTests > "AssignGroup_SoundWhenListConfirms_NotOnError"` (AC3)
5. `ServerViewModelTests > "OwnGroupChangedByMe_OnlyOneSound"` (AC4)
6. `SoundTests > "Deafened_OnlyOwnMicAndSoundTones_AllOffNothing"` bleibt grün (AC5)

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Tests 1 und 2 (rot), Enum, Ton, Texte.
2. Tests 3 und 5 (rot), in `ServerViewModel.SoundFor` die Gruppen des eigenen Nutzers vergleichen, unterdrückt, solange eine eigene Zuweisung an sich selbst offen ist.
3. Test 4 (rot), offene Zuweisung im `AdminViewModel` merken und bei passender `UserList` den Ton auslösen, bei `Error` mit derselben RequestId verwerfen.
4. README.

### Out of Scope

- Töne für andere Admin-Aktionen (Kick, Ban)

---

## Package 74: Backups auf dem Server

**Ziel:** Unter "Verwaltung -> Server" lassen sich Backups auf dem Server anlegen, auflisten, löschen und wiederherstellen.

**Abhängigkeiten:** Package 69 (Neustart-Signal im `ServerHost`)

**Betroffene Dateien:**
- `src/OVS.Server/Data/BackupStore.cs` (neu): Archiv anlegen, prüfen, auflisten, löschen, entpacken
- `src/OVS.Server/Commands/BackupCommands.cs` (neu): Anfragen
- `src/OVS.Server/ServerState.cs`, `src/OVS.Server/ServerHost.cs` (ändern): Wiederherstellen trennt alle und startet den Lauf neu
- `src/OVS.Shared/Protocol/Messages.cs`, `Codes.cs` (ändern): `CreateBackup`, `ListBackups`, `BackupList`, `DeleteBackup`, `RestoreBackup`, Codes `Restoring`, `InvalidBackup`
- `src/OVS.Client/ViewModels/AdminViewModel.cs`, `src/OVS.Client/Views/AdminView.axaml` (ändern): Bereich "Backups" im Server-Tab
- `src/OVS.Client/ErrorTexts.cs`, `Localization/Strings.resx`, `Strings.en.resx` (ändern)
- `README.md` (ändern): Docker-Befehle bleiben als Alternative
- `tests/OVS.Tests/Server/BackupTests.cs` (neu), `tests/OVS.Tests/Client/AdminViewModelTests.cs` (ändern)

### Kontext

Backups gehen heute nur per `docker run ... tar` über das Volume (README). Im Datenverzeichnis liegen `server-data.json` (atomar geschrieben von `DataStore`), `cert.pfx`, `server-icon.png`, optional `server-config.json` und `logs/`. Es gibt keinen Datei-Lock, nur den globalen Lock in `ServerState`. `ServerHost.RunAsync` kann einen Lauf beenden und im selben Prozess neu starten (automatischer Neustart). Dabei werden Config, Daten und Zertifikat neu gelesen.

### Acceptance Criteria

- [x] AC1: "Backup anlegen" schreibt `/data/backups/<JJJJ-MM-TT_hh-mm-ss>.ovsbackup`, ein Zip mit `manifest.json` (Formatversion, Datenversion, Serverversion, Zeit), `server-data.json`, `cert.pfx` und, falls vorhanden, `server-icon.png`. Logs kommen nicht mit (A90). Das Archiv entsteht aus einem konsistenten Stand unter dem Lock.
- [x] AC2: Die Liste zeigt je Backup Datum, Grösse und Serverversion, neueste zuerst. Einzelne Backups lassen sich nach Rückfrage löschen.
- [x] AC3: "Wiederherstellen" fragt rot nach ("Alle werden getrennt, der aktuelle Stand wird ersetzt"). Der Server prüft das Archiv (Manifest, lesbare Daten, Datenversion nicht neuer als die eigene), legt zuerst ein Sicherheits-Backup `vor-wiederherstellung_<Zeit>.ovsbackup` an, trennt alle mit Code `Restoring` ("Der Server wird aus einem Backup wiederhergestellt ..."), ersetzt die Dateien und startet den Lauf neu. Ein ungültiges Archiv ändert nichts (`InvalidBackup`).
- [x] AC4: Ältere Archive (niedrigere Datenversion) werden beim Neustart wie gewohnt migriert. Der Zertifikats-Fingerabdruck ist nach dem Wiederherstellen der aus dem Backup.
- [x] AC5: Alle Aktionen brauchen das Recht `ServerConfig` und stehen im Server-Log. Dateinamen aus Anfragen werden gegen die Liste geprüft, Pfade ausserhalb von `backups/` sind unmöglich.
- [x] AC6: Texte auf Deutsch und Englisch, README beschreibt beide Wege.

### Tests (TDD)

1. `BackupTests > "Create_ContainsDataCertIcon_NoLogs"` (AC1)
2. `BackupTests > "List_NewestFirst_Delete"` (AC2)
3. `BackupTests > "Restore_ReplacesState_SafetyBackup_RestartsRun"` (AC3, AC4)
   - Gegeben: Backup mit Channel "Alt", danach Channel "Neu" angelegt, zwei Clients verbunden
   - Erwartet: beide getrennt mit `Restoring`, neuer Lauf hat "Alt" statt "Neu", Sicherheits-Backup enthält "Neu", Fingerabdruck wie im Backup
4. `BackupTests > "Restore_Invalid_NothingChanged"` (AC3): kaputtes Zip, fehlendes Manifest, zu neue Datenversion
5. `BackupTests > "Restore_OlderDataVersion_Migrated"` (AC4)
6. `BackupTests > "PathTraversal_AndMissingRight_Rejected"` (AC5)
7. `AdminViewModelTests > "Backups_ListCreateDeleteRestore_AskFirst"` (AC2, AC3)
8. `UiSmokeTests`, `LocalizationTests`, `ErrorTexts_EveryCodeHasText` (AC6)

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Tests 1, 2, 4 und 6 (rot): `BackupStore` mit `System.IO.Compression.ZipArchive`, Anfragen und Rechte.
2. Tests 3 und 5 (rot): `ServerState.RequestRestore(file)`: Prüfen, Sicherheits-Backup, alle trennen, Signal an `ServerHost`, der den Lauf beendet, die Dateien ersetzt und neu startet.
3. Test 7, UI-Bereich "Backups" mit Liste und Buttons.
4. Test 8, Texte, README.

### Out of Scope

- Zeitgesteuerte automatische Backups und deren Aufbewahrung
- Download und Upload (Package 75)

---

## Package 75: Backup herunterladen und hochladen

**Ziel:** Ein Admin kann ein Backup vom Server auf seinen PC laden und ein Backup von seinem PC hochladen und wiederherstellen.

**Abhängigkeiten:** Package 74

**Betroffene Dateien:**
- `src/OVS.Shared/Protocol/Messages.cs` (ändern): `DownloadBackup`, `BackupChunk`, `UploadBackupChunk`, `BackupUploaded`
- `src/OVS.Server/Commands/BackupCommands.cs` (ändern)
- `src/OVS.Client/ViewModels/AdminViewModel.cs`, `src/OVS.Client/Views/AdminView.axaml`, `AdminView.axaml.cs` (ändern): Speichern- und Öffnen-Dialog, Fortschritt
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (ändern)
- `tests/OVS.Tests/Server/BackupTests.cs`, `tests/OVS.Tests/Client/AdminViewModelTests.cs` (ändern)

### Kontext

Steuernachrichten sind höchstens 1 MiB gross (`FrameReader.MaxFrameSize`). Base64 erlaubt also etwa 750 KB Nutzdaten je Nachricht. Das Server-Logo (bis 512 KB) geht heute in einer Nachricht. Ein Backup ist meist klein (Daten und Zertifikat), mit Logo unter 1 MB, kann aber bei vielen Nutzern wachsen. Die Ausgangs-Queue einer Session fasst 1024 Nachrichten. Datei-Dialoge nutzt der Client schon für Logo und eigene Töne (`StorageProvider`).

### Acceptance Criteria

- [x] AC1: Bei jedem Backup in der Liste gibt es "Herunterladen". Der Client fragt nach dem Speicherort (Vorschlag: Dateiname vom Server) und speichert die Datei. Der Inhalt ist bytegleich zum Archiv auf dem Server.
- [x] AC2: "Backup hochladen ..." wählt eine `.ovsbackup`-Datei. Sie wird hochgeladen, vom Server wie in Package 74 geprüft und unter `backups/` abgelegt, danach erscheint sie in der Liste und kann wiederhergestellt werden. Auf Wunsch direkt mit "Hochladen und wiederherstellen" (gleiche Rückfrage wie in 74).
- [x] AC3: Die Übertragung läuft in Stücken von 512 KB (Base64), mit Fortschritt in Prozent. Dateien über 50 MB lehnt der Server ab. Ein abgebrochener Upload hinterlässt keine Datei.
- [ ] AC4: Während der Übertragung bleiben Sprache und Chat nutzbar. Der Download schickt die Stücke nacheinander (das nächste erst auf Anfrage des Clients), damit die Ausgangs-Queue nicht überläuft.
- [x] AC5: Recht `ServerConfig`. Texte auf Deutsch und Englisch.

### Tests (TDD)

1. `BackupTests > "Download_ChunkedByteIdentical"` (AC1, AC3, AC4): Backup mit 1,5 MB Logo-Platzhalter, Stücke zusammensetzen, Hash vergleichen
2. `BackupTests > "Upload_ValidatedStored_Listed"` (AC2)
3. `BackupTests > "Upload_TooLargeOrAborted_NoFileLeft"` (AC3)
4. `BackupTests > "Upload_InvalidArchive_Rejected"` (AC2)
5. `AdminViewModelTests > "Download_SavesToChosenPath_Progress"` (AC1, AC3)
6. `AdminViewModelTests > "UploadAndRestore_AsksFirst"` (AC2)
7. `LocalizationTests` (AC5)

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Tests 1 bis 4 (rot): Nachrichten mit Übertragungs-Id, Offset und Ende, Zwischendatei `backups/.upload-<id>` bis zur Prüfung.
2. Tests 5 und 6 (rot): Client-Seite mit Datei-Dialogen und Fortschritt.
3. Test 7, Texte.

### Out of Scope

- Übertragung über HTTP oder einen zweiten Port
- Backups zwischen Servern abgleichen

---

## Package 76: Einzelne Rechte für die Verwaltung

**Ziel:** Nutzerübersicht, Bans-Übersicht, Gruppenübersicht, Gruppen anlegen, Gruppen löschen und Nutzer löschen haben je ein eigenes Recht, und standardmässig sieht nur die Gruppe Admin die Nutzerübersicht.

**Abhängigkeiten:** keine (muss vor Package 71 und 72 fertig sein)

**Betroffene Dateien:**
- `src/OVS.Shared/Permissions/Permission.cs` (ändern): `UsersView`, `BansView`, `GroupsView`, `GroupsCreate`, `GroupsDelete`, `UserDelete`, `All` neu berechnet
- `src/OVS.Server/Permissions/PermissionRules.cs` (ändern): `ModeratorPermissions`
- `src/OVS.Server/Data/ServerData.cs` (ändern): Migration der gespeicherten Gruppen (nächste Datenversion)
- `src/OVS.Server/Commands/AdminCommands.cs`, `ModerationCommands.cs` (ändern): geprüfte Rechte je Anfrage
- `src/OVS.Shared/Protocol/ProtocolInfo.cs` (ändern): Protokollversion erhöhen, falls nicht schon in Package 69 geschehen
- `src/OVS.Client/ViewModels/AdminViewModel.cs`, `ServerViewModel.cs` (ändern): Sichtbarkeit der Tabs und Buttons, `CanAdminister`
- `src/OVS.Client/Views/AdminView.axaml` (ändern): Buttons im Gruppen-Tab einzeln gesperrt
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (ändern): `Perm_*`
- `README.md` (ändern): Rechte-Tabelle
- `tests/OVS.Tests/Server/PermissionRulesTests.cs`, `AdminCommandTests.cs`, `ModerationTests.cs`, `DataStoreTests.cs`, `tests/OVS.Tests/Client/AdminViewModelTests.cs` (ändern)

### Kontext

`Permission` ist ein `[Flags]`-Enum mit 17 Bits (bis `ChannelJoinFull = 1 << 16`, `All = (1 << 17) - 1`). Für die Verwaltung gibt es heute:
- `UserKick` (kicken)
- `UserBan` (bannen und entbannen; macht ausserdem den Tab "Bans" sichtbar und erlaubt `ListBans`)
- `GroupsAssign` (Gruppen zuweisen; macht ausserdem den Tab "Nutzer" sichtbar und erlaubt `ListUsers`)
- `GroupsManage` (Gruppen anlegen, bearbeiten, sortieren, löschen; macht ausserdem den Tab "Gruppen" sichtbar)

Sehen und Handeln hängen also am selben Recht. Die Standardgruppe Moderator hat `UserKick` und `UserBan`, aber kein `GroupsAssign`. Die Gruppe Admin bekommt immer `Permission.All` (`PermissionRules.Effective`). Gruppen werden mit ihren Rechten als Text-Enum in `server-data.json` gespeichert (`ProtocolJson.Options`). Die Tab-Sichtbarkeit steht in `AdminViewModel` (`ShowGroups`, `ShowUsers`, `ShowBans`), der Button "Verwaltung" in `ServerViewModel.CanAdminister`. Die Rechte-Checkboxen im Gruppen-Editor entstehen aus dem Enum, ihre Namen aus `Perm_<Name>`.

### Acceptance Criteria

- [x] AC1: Neue Rechte, im Gruppen-Editor einzeln wählbar:
  - "Nutzerübersicht sehen" (`UsersView`)
  - "Bans sehen" (`BansView`)
  - "Gruppen sehen" (`GroupsView`)
  - "Gruppen anlegen" (`GroupsCreate`)
  - "Gruppen löschen" (`GroupsDelete`)
  - "Nutzer löschen" (`UserDelete`)
  Die bestehenden Rechte heissen im Editor "Nutzer kicken" (`UserKick`), "Nutzer bannen und entbannen" (`UserBan`), "Gruppen zuweisen" (`GroupsAssign`) und "Gruppen bearbeiten" (`GroupsManage`: Name, Rechte, Reihenfolge).
- [x] AC2: Sichtbarkeit: Tab "Nutzer" nur mit `UsersView`, Tab "Bans" nur mit `BansView`, Tab "Gruppen" nur mit `GroupsView`. Der Button "Verwaltung" erscheint, sobald irgendein Tab sichtbar wäre.
- [x] AC3: Der Server prüft je Anfrage genau ein Recht:
  - `ListUsers`: `UsersView`
  - `ListBans`: `BansView`
  - `Ban`, `BanUser`, `Unban`: `UserBan`
  - `Kick`: `UserKick`
  - `AssignGroup`, `UnassignGroup`: `GroupsAssign`
  - `CreateGroup`: `GroupsCreate`
  - `UpdateGroup`, `ReorderGroups`: `GroupsManage`
  - `DeleteGroup`: `GroupsDelete`
  - `DeleteUser`: `UserDelete`
  Fehlt es, kommt `PermissionDenied`. Die Regeln gegen Rechteausweitung (nur Teilmengen der eigenen Rechte vergeben, nur schwächere Nutzer bearbeiten) gelten weiter.
- [x] AC4: In den Tabs sind Aktionen ohne das passende Recht gesperrt, aber die Übersicht bleibt lesbar:
  - "Neue Gruppe" nur mit `GroupsCreate`
  - "Speichern" und Sortieren nur mit `GroupsManage`
  - "Löschen" nur mit `GroupsDelete`
  - Gruppen-Checkboxen bei Nutzern nur mit `GroupsAssign`
  - "Entbannen" nur mit `UserBan`
- [x] AC5: Standard auf neuen Servern: Admin hat alles (wie bisher über `All`), Moderator bekommt zusätzlich `BansView`, aber nicht `UsersView`. Die Nutzerübersicht sieht also standardmässig nur Admin. Gast bekommt nichts Neues.
- [x] AC6: Bestehende Server verlieren beim Update keine Möglichkeit. Jede gespeicherte Gruppe bekommt einmalig:
  - mit `GroupsManage`: `GroupsView`, `GroupsCreate`, `GroupsDelete`
  - mit `GroupsAssign`: `UsersView`
  - mit `UserBan`: `BansView`
  `UserDelete` bekommt nur Admin (über `All`). Eine Gruppe, die als Rechte-Text "All" gespeichert ist, hat danach auch alle neuen Rechte.
- [x] AC7: Texte auf Deutsch und Englisch, README-Rechtetabelle aktualisiert.

### Tests (TDD)

1. `PermissionRulesTests > "DefaultGroups_OnlyAdminSeesUsers_ModeratorSeesBans"` (AC5)
2. `DataStoreTests > "Migration_GrantsViewRightsFromOldRights"` (AC6)
   - Gegeben: gespeicherte Gruppen der alten Version mit `GroupsManage`, mit `GroupsAssign`, mit `UserBan` und eine ohne alles davon
   - Erwartet: genau die Zusatzrechte aus AC6, die letzte Gruppe unverändert, `UserDelete` bei keiner
3. `AdminCommandTests > "EachAdminRequest_RequiresItsOwnRight"` (AC3, Theory über alle Anfragen aus AC3)
   - Gegeben: Nutzer in einer Gruppe mit allen Rechten ausser dem geprüften
   - Erwartet: `PermissionDenied`. Mit dem Recht: kein Rechtefehler
4. `AdminCommandTests > "GroupsCreateOnly_CannotEditOrDelete"` (AC3)
5. `AdminViewModelTests > "Tabs_VisibleByPermission"` erweitert (AC2)
   - Gegeben: je nur `UsersView`, `BansView`, `GroupsView`, dann `GroupsAssign` allein
   - Erwartet: jeweils genau der passende Tab. Mit `GroupsAssign` allein keiner, und der Verwaltungs-Button ist ausgeblendet.
6. `AdminViewModelTests > "GroupButtons_ByRight"` (AC4): Neu, Speichern, Sortieren, Löschen, Nutzer-Checkboxen, Entbannen einzeln gesperrt
7. `AdminViewModelTests > "GroupEditor_ListsNewRights"`, `LocalizationTests` (AC1, AC7): Checkbox-Namen aus `Perm_*` in beiden Sprachen

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Tests 1 und 2 (rot):
   - Bits 17 bis 22 anlegen, `All = (1 << 23) - 1`.
   - `ModeratorPermissions` erweitern.
   - Migration in `ServerData.Migrate()` (nächste freie Datenversion nach Package 69 und 70).
2. Tests 3 und 4 (rot): `Require(...)` in allen Handlern auf das Recht aus AC3 umstellen.
3. Tests 5 und 6 (rot):
   - `ShowUsers`, `ShowBans`, `ShowGroups` und `CanAdminister` umstellen.
   - `CanCreateGroup`, `CanEditGroup`, `CanDeleteGroup`, `CanUnban` im `AdminViewModel`, Buttons in `AdminView.axaml` daran binden.
4. Test 7: `Perm_*` für neue und umbenannte Rechte, README.

### Out of Scope

- Rechte für Channels, Links, Server und Chat (unverändert)
- Rechte pro Channel

---

## Package 77: Einstellungen responsiv

**Ziel:** Die Einstellungsseite ist auf jeder Breite ab 360 px vollständig bedienbar, ohne abgeschnittene oder überlappende Elemente.

**Abhängigkeiten:** Package 68

**Betroffene Dateien:**
- `src/OVS.Client/Views/SettingsView.axaml` (ändern): Sound-Zeilen, Geräte, Tasten, Darstellung, Fusszeile
- `src/OVS.Client/Styles/Controls.axaml` (ändern): Stile für `Window.narrow` auf der Einstellungsseite
- `tests/OVS.Tests/Client/ResponsiveTests.cs` (ändern)

### Kontext

Die Seite ist ein `StackPanel` mit `MaxWidth="720"` und `Margin="24,20"`. Eine Sound-Zeile (`Border.soundRow`) ist ein `DockPanel`:
- links Play-Button
- rechts ein waagrechtes `StackPanel` mit Regler (`Width="150"`), Prozent (`Width="44"`), "Stumm", "Datei wählen ..." und Zurücksetzen
- in der Mitte Name und Quelle

Wird der Hauptbereich schmal, verdrängt die rechte Gruppe den Namen und läuft über den Rand (Screenshot 2: Name weg, "Stumm" abgeschnitten, "Datei wählen" unsichtbar). Ähnlich fest sind die Tasten-Zeilen ("Ändern ...", Löschen), die Hinweise mit `MaxWidth` 360 bzw. 420 und die `ComboBox`en mit `MinWidth="200"`.

### Acceptance Criteria

- [ ] AC1: Ab einem Hauptbereich von 560 px sieht die Seite aus wie heute (Screenshot 1).
- [ ] AC2: Darunter (`narrow`) wird jede Sound-Zeile zweizeilig:
  - oben Play-Button, Name und Quelle (volle Breite, Name nie leer)
  - unten Regler (füllt den Platz), Prozent, "Stumm", "Datei wählen" und Zurücksetzen
  Wird es noch enger, bricht die untere Reihe um, statt abzuschneiden.
- [x] AC3: Tasten-Zeilen, Geräteauswahl, Lautstärke, Übertragung und Darstellung stapeln Beschriftung und Bedienelement untereinander. `ComboBox`en und Regler nutzen die volle Breite. Hinweistexte brechen um.
- [x] AC4: Die Fusszeile (Abbrechen, Speichern) bleibt immer sichtbar. Die Seitenabstände schrumpfen unter `narrow` auf 12 px.
- [x] AC5: `LayoutAssert.FitsHorizontally` ist für die ganze, nach unten gescrollte Seite bei 360, 480, 600 und 1100 px grün, in Deutsch und Englisch.

### Tests (TDD)

1. `ResponsiveTests > "Settings_FitAt360_480_600_1100"` (AC2 bis AC5, Theory über Breite und Sprache): jede Sektion in den Blick scrollen und `LayoutAssert.FitsHorizontally` prüfen. Vorher rot, entspricht Screenshot 2.
2. `ResponsiveTests > "Settings_SoundRow_TwoLinesWhenNarrow_NameVisible"` (AC2): bei 360 px liegen Name und Regler in verschiedenen Zeilen, der Name hat Breite > 0
3. `ResponsiveTests > "Settings_Wide_LayoutUnchanged"` (AC1): bei 1100 px Sound-Zeile einzeilig wie bisher
4. Bestehende `UiSmokeTests` zu Einstellungen (`Settings_SectionOrder`, `Settings_PttHint_WithSetKeyButton` usw.) bleiben grün

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Test 1 (rot).
2. Sound-Zeile als `Grid` mit zwei Zeilen und zwei Spalten, die Position des Bedienblocks per Stil `Window.narrow` umschalten (Zeile 1, volle Breite). Untere Reihe als `WrapPanel`.
3. Übrige Sektionen: Paare aus Beschriftung und Bedienelement in eine gemeinsame Vorlage (`DockPanel` breit, `StackPanel` schmal), feste `Width`/`MaxWidth` durch `MinWidth="0"` und Stretch ersetzen.
4. Tests 2 und 3, manueller Blick bei 360 px hell und dunkel.

### Out of Scope

- Neue Einstellungen
- Verwaltung, Chat, Dialoge (Packages 78 und 79)

---

## Package 78: Verwaltung responsiv

**Ziel:** Alle Tabs der Verwaltung sind auf jeder Breite ab 360 px vollständig bedienbar.

**Abhängigkeiten:** Package 68

**Betroffene Dateien:**
- `src/OVS.Client/Views/AdminView.axaml` (ändern): Tabs Gruppen, Nutzer, Bans, Links, Server
- `src/OVS.Client/Styles/Controls.axaml` (ändern)
- `tests/OVS.Tests/Client/ResponsiveTests.cs` (ändern)

### Kontext

Die Tabs sind heute auf breite Fenster ausgelegt:
- **Gruppen:** `Grid ColumnDefinitions="240,*"` mit Liste links und Editor rechts, Rechte in einer zweispaltigen `UniformGrid`
- **Nutzer und Bans:** Karten, die Gruppen-Checkboxen stehen in einem `WrapPanel`
- **Links:** eine Matrix mit festen Breiten (Kopf 228 px, Zeilentitel 192 px, Zellen 34 bis 36 px)
- **Server:** eine Karte mit `MaxWidth="560"`

Die `TabControl`-Kopfzeile hat fünf Tabs nebeneinander.

### Acceptance Criteria

- [x] AC1: Die Tab-Leiste läuft bei Platzmangel waagrecht scrollbar, statt abzuschneiden. Der gewählte Tab ist immer sichtbar.
- [x] AC2: Gruppen unter `narrow`:
  - Liste und Editor stehen untereinander, die Liste höchstens 40 % der Höhe und scrollbar.
  - Die Rechte-Checkboxen stehen einspaltig.
  - Die Buttons (Neu, hoch, runter, Speichern, Löschen) brechen um.
- [x] AC3: Nutzer- und Bans-Karten nutzen die volle Breite, Details brechen um (auch die Felder aus Packages 71 und 72, soweit schon umgesetzt), Aktions-Buttons stehen unter den Details.
- [x] AC4: Die Link-Matrix bleibt eine Matrix, liegt aber in einem waagrecht und senkrecht scrollbaren Bereich. Die Zeilentitel bleiben beim waagrechten Scrollen stehen. Die Buttons (Auswahl verlinken, trennen, Übernehmen, Verwerfen) brechen um.
- [x] AC5: Der Server-Tab nutzt unter `narrow` die volle Breite, alle Felder stehen untereinander.
- [x] AC6: `LayoutAssert.FitsHorizontally` ist für jeden Tab (Link-Matrix: für den Bereich um die Matrix) bei 360, 480, 600 und 1100 px grün, in Deutsch und Englisch, mit 8 Channels und 20 Nutzern.

### Tests (TDD)

1. `ResponsiveTests > "Admin_EveryTabFits"` (AC1 bis AC6, Theory über Tab, Breite und Sprache), vorher rot
2. `ResponsiveTests > "Admin_Groups_StackedWhenNarrow"` (AC2)
3. `ResponsiveTests > "Admin_LinkMatrix_ScrollsTitlesStay"` (AC4)
4. Bestehende `GroupList_DragAdminAboveGuest_SendsOrder`, `LinkMatrix_*` und `AdminViewModelTests` bleiben grün

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Test 1 (rot).
2. Tab-Leiste scrollbar, Gruppen-Tab per Stil umschalten (`Grid` mit Zeilen statt Spalten). Test 2.
3. Link-Matrix in `ScrollViewer` mit fester Titelspalte. Test 3.
4. Nutzer-, Bans- und Server-Tab, Test 1 grün.

### Out of Scope

- Inhalte der Tabs, die Packages 69 bis 76 erst hinzufügen: Diese Packages halten sich selbst an die Regeln aus 68 (A96).

---

## Package 79: Chat, Startseite und Dialoge responsiv

**Ziel:** Chat, Startseite, Update-Karte und alle Dialoge sind auf jeder Breite ab 360 px vollständig bedienbar.

**Abhängigkeiten:** Package 68

**Betroffene Dateien:**
- `src/OVS.Client/Views/ChatView.axaml` (ändern): Tabs, Eingabe
- `src/OVS.Client/Views/OverlayHost.cs`, `SimpleDialogs.cs`, `ChannelDialog.axaml` (ändern): Breite der Dialoge
- `src/OVS.Client/Views/MainWindow.axaml` (ändern): Startseite, Update-Karte (`Width="420"`)
- `tests/OVS.Tests/Client/ResponsiveTests.cs` (ändern)

### Kontext

Die Breiten sind heute fest:
- **Dialoge:** Die Karte im `OverlayHost` ist fest 460 px breit (plus 24 px Rand), die Update-Karte 420 px, `ChannelDialog` enthält ein Feld mit `Width="140"`.
- **Chat:** Die Tab-Titel sind höchstens 220 px breit, die Blasen höchstens 75 % des Verlaufs (Package 64).
- **Startseite:** ein `StackPanel` mit `MaxWidth="460"` und `Margin="32"`.

### Acceptance Criteria

- [x] AC1: Dialoge und Update-Karte sind höchstens so breit wie ihr Standard (460 bzw. 420 px) und mindestens 16 px vom Fensterrand entfernt. Inhalt und Buttons brechen um. Ist ein Dialog höher als das Fenster, scrollt sein Inhalt, die Buttons bleiben sichtbar.
- [x] AC2: Chat unter `narrow`:
  - Die Tab-Leiste scrollt waagrecht.
  - Blasen dürfen 85 % der Breite nutzen.
  - Die Eingabezeile behält den Senden-Button sichtbar.
  - Die Innenabstände schrumpfen auf 12 px.
- [x] AC3: Die Startseite (Logo, Status, Verbinden, Lesezeichen) passt ab 360 px, der Abstand schrumpft auf 16 px.
- [x] AC4: `LayoutAssert.FitsHorizontally` ist für Chat mit langen Nachrichten, Startseite mit drei Lesezeichen, jeden Dialog aus `ExercisePagesAndDialogs` und die Update-Karte bei 360, 480 und 1100 px grün, in Deutsch und Englisch.

### Tests (TDD)

1. `ResponsiveTests > "Dialogs_FitAt360"` (AC1, AC4, Theory über alle `SimpleDialogs` und `ChannelDialog`), vorher rot
2. `ResponsiveTests > "Dialog_TallerThanWindow_ContentScrollsButtonsVisible"` (AC1)
3. `ResponsiveTests > "Chat_Narrow_TabsScroll_SendVisible"` (AC2)
4. `ResponsiveTests > "StartScreen_FitsAt360"` (AC3)
5. Bestehende Chat- und Overlay-Tests bleiben grün

Testbefehl: `dotnet test`

### Umsetzungsschritte

1. Test 1 (rot), Kartenbreite im `OverlayHost` und in der Update-Karte an die Fensterbreite koppeln (`MaxWidth` statt `Width`, Rand 16 px).
2. Test 2, Dialog-Inhalt in `ScrollViewer`.
3. Tests 3 und 4, Chat- und Startseiten-Stile für `narrow`.

### Out of Scope

- Touch-Bedienung, Bildschirmtastatur-Verhalten

---

## Package 80: Ban overview with details, history, search and filter

**Goal:** Under "Verwaltung -> Bans" every ban (active, expired and lifted) is shown with all stored details and can be searched, filtered and sorted like the user overview.

**Dependencies:** none (builds on Packages 71, 72, 76 and 78, which are done)

**Affected files:**
- `src/OVS.Server/Data/ServerData.cs` (change): `BanRecord` gets `CreatedAt`, `CreatedByFingerprint`, `DurationMinutes`, `LiftedAt`, `LiftedBy`, `BlockedAttempts`, `LastAttempt`, `LastAttemptIp`
- `src/OVS.Server/ServerState.cs` (change): `Admit` counts blocked attempts, `Persist` keeps history instead of dropping expired bans, history retention
- `src/OVS.Server/Commands/ModerationCommands.cs` (change): `OnBan`, `OnBanUser` fill the new fields, `OnUnban` marks a ban as lifted instead of removing it, `OnListBans` returns history too
- `src/OVS.Shared/Protocol/Messages.cs` (change): `BanInfo` extended
- `src/OVS.Client/ViewModels/AdminViewModel.cs` (change): `BanViewModel` with details, `BanSearchText`, `BanStatusFilter`, `BanSortOrder`, `VisibleBans`
- `src/OVS.Client/Views/AdminView.axaml` (change): Bans tab as cards with search box, filters and sort, like the Users tab
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change)
- `tests/OVS.Tests/Server/ModerationTests.cs`, `DataStoreTests.cs`, `tests/OVS.Tests/Client/AdminViewModelTests.cs`, `ResponsiveTests.cs`, `UiSmokeTests.cs` (change)

### Context

`BanRecord` (ServerData.cs) stores `Id`, `Fingerprint`, `Nickname`, `Ip?`, `Reason`, `CreatedBy` (nickname) and `ExpiresAt?`. There is no creation time. `Persist()` drops expired bans, and `Unban` deletes the record, so there is no history. `Admit` rejects a banned fingerprint or IP without recording the attempt. `ListBans` (right `BansView`) returns `BanList(BanInfo...)`. The Bans tab shows one text line per ban (`BanViewModel.Text`) with an "Entbannen" button (`UserBan`), without search or filter. The Users tab (Package 71) is the model: search box, `Choice<T>` combo boxes for status and sort, a hit counter ("12 von 40 Nutzern"), cards with a two-column detail area, responsive since Package 78.

### Acceptance Criteria

- [x] AC1: New bans store:
  - creation time and the creator's fingerprint (besides the existing nickname)
  - the original duration (null = permanent) and whether the IP is included
  Existing bans keep working and show "unknown" for the missing values.
- [x] AC2: Lifting a ban (Unban) and expiry no longer delete the record. The ban becomes inactive and keeps:
  - for Unban: who lifted it (nickname) and when
  - for expiry: status "expired"
  `Admit` only blocks on active bans. History entries are removed after the log retention period (`LogDays` from Package 69, 0 = keep forever) and when the user's data is deleted (Package 72).
- [x] AC3: Every rejected join because of an active ban increments that ban's attempt counter and stores time and IP of the last attempt. This is saved at most once per minute per ban, not on every attempt.
- [x] AC4: Each ban card shows:
  - nickname, short fingerprint (full one in the tooltip, copyable)
  - IP if included, reason
  - created by and at, duration and end ("dauerhaft" or date), remaining time for active bans
  - status (active, expired, lifted by X at Y)
  - blocked attempts with the last time and IP
- [x] AC5: A search box filters immediately and case-insensitively by nickname, fingerprint, IP, reason, creator and lifter.
- [x] AC6: Filters:
  - status: active, expired, lifted, all; default is active
  - type: permanent, temporary, with IP, all
  Sorting: newest first (default), ending soonest, most blocked attempts, name. The hit count is shown above the list ("3 von 12 Bans").
- [x] AC7: "Entbannen" stays only on active bans and needs `UserBan`. Without `UserBan` the list is read-only. Search and filter are kept when the list refreshes after an action.
- [x] AC8: The Bans tab fits from 360 px (A96, `LayoutAssert.FitsHorizontally` in `ResponsiveTests.Admin_EveryTabFits`). Texts in German and English.

### Tests (TDD)

1. `ModerationTests > "Ban_StoresCreatedAtCreatorDuration"` (AC1)
   - Given: moderator bans a guest online for 60 minutes with IP, `ManualTimeProvider`
   - Expected: `CreatedAt` = now, `CreatedByFingerprint` = moderator, `DurationMinutes` = 60, IP flag true, in `BanList` too
2. `ModerationTests > "Unban_KeepsHistory_ExpiredToo_AdmitIgnoresInactive"` (AC2)
   - Given: one ban lifted, one expired by advancing the clock
   - Expected: both still in `BanList` with status lifted (by/at) and expired, the guest can join again
3. `ModerationTests > "History_RemovedAfterLogRetention_AndOnUserDelete"` (AC2)
4. `ModerationTests > "BlockedAttempts_Counted_SavedAtMostPerMinute"` (AC3)
   - Given: a banned guest tries 3 times within 10 seconds, then again after 2 minutes
   - Expected: counter 4, last attempt time and IP set, the data file written at most twice for these attempts
5. `DataStoreTests > "OldBans_LoadWithUnknownNewFields"` (AC1)
6. `AdminViewModelTests > "BanCards_ShowAllDetails_UnknownForMissing"` (AC4)
7. `AdminViewModelTests > "BanSearch_MatchesAllTextFields"` (AC5)
8. `AdminViewModelTests > "BanFilter_StatusType_Sort_Count"` (AC6)
9. `AdminViewModelTests > "Unban_OnlyActive_OnlyWithRight_FilterKept"` (AC7)
10. `ResponsiveTests > "Admin_EveryTabFits"` extended with 12 bans of every status, `LocalizationTests` (AC8)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1, 2, 3 and 5 (red): new fields, history instead of deletion, retention in `Persist`.
2. Test 4 (red): attempt counter in `Admit`, throttled saving.
3. `BanInfo` extended (unreleased protocol version 10 stays, only its comment is extended, unless a release happened meanwhile: then bump).
4. Tests 6 to 9 (red): `BanViewModel` and filter logic, copying the patterns of `KnownUserViewModel` and `RebuildUsers`.
5. XAML cards, search and filter bar like the Users tab, test 10, headless screenshot at 360 and 1100 px.

### Out of Scope

- Editing a ban (reason, duration)
- Creating a ban from the Bans tab (done from the user overview or the channel tree)

---

## Package 81: Server logs viewable and searchable in the app

**Goal:** Users with the new right "Logs ansehen" can list the server and channel log files under "Verwaltung -> Logs", open them page by page and search all of them.

**Dependencies:** none

**Affected files:**
- `src/OVS.Shared/Permissions/Permission.cs` (change): `LogsView = 1 << 23`, `All = (1 << 25) - 1` (bit 24 reserved for Package 82)
- `src/OVS.Server/Logging/LogReader.cs` (new): list files, read a page of lines, search
- `src/OVS.Server/Commands/LogCommands.cs` (new): requests
- `src/OVS.Server/ServerState.cs` (change): dispatch
- `src/OVS.Shared/Protocol/Messages.cs` (change): `ListLogs`, `LogList`, `LogFileInfo`, `ReadLog`, `LogPage`, `SearchLogs`, `LogSearchResult`, `LogHit`
- `src/OVS.Client/ViewModels/LogsViewModel.cs` (new), `AdminViewModel.cs` (change): tab visibility, request lists
- `src/OVS.Client/Views/AdminView.axaml` (change): new tab "Logs"
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change): tab, labels, `Perm_LogsView`
- `README.md` (change): rights table
- `tests/OVS.Tests/Server/LogCommandTests.cs` (new), `tests/OVS.Tests/Client/LogsViewModelTests.cs` (new), `ResponsiveTests.cs`, `AdminViewModelTests.cs` (change)

### Context

`ServerLogs` writes `logs/server/<yyyy-MM-dd_HH-mm-ss>.log` and one folder per channel `logs/channels/<channelId>/<start>.log`, through `LogFiles` (one file per start and per day, retention from Package 69). Each line starts with a timestamp `yyyy-MM-dd HH:mm:ss.fff`, channel lines contain `[Channelname]`. The files are open for appending while the server runs (`FileShare.ReadWrite|Delete`). There is no way to read them from the client today, only through the Docker volume. Logs contain IPs, fingerprints and server and channel chat text, so they are sensitive. Messages are limited to 1 MiB (`FrameReader.MaxFrameSize`), and the outbox per session to 1024 messages. Package 75 shows the pull-based chunk pattern (one request per chunk).

### Acceptance Criteria

- [x] AC1: New right "Logs ansehen" (`LogsView`), selectable in the group editor. Only Admin has it by default (through `All`), existing groups get nothing new. The tab "Logs" is visible only with this right, and the server checks it on every log request (`PermissionDenied` otherwise).
- [x] AC2: The file list shows, newest first:
  - type: server or channel with the channel's current name, or the name found in the file for deleted channels
  - start time and size
  Filters: type (all, server, one channel) and period (from, to).
- [x] AC3: Opening a file shows its lines in pages of 1000 lines (the last page first). Controls: "Ältere laden", "Neuere laden", jump to start and end. The currently written file can be refreshed.
- [x] AC4: A filter box inside the opened file shows only matching lines, case-insensitive, with the matches highlighted.
- [x] AC5: Search across all files: query text (case-insensitive, plain text, no regex), optionally limited by the type and period filters. The server returns at most 500 hits (file, line number, line) newest first and says if there were more. Clicking a hit opens the file at that line with the line highlighted.
- [x] AC6: Security:
  - Files are addressed only by an id from the server's own listing; paths from the client are never used.
  - Only `*.log` files below `<DataDir>/logs` are reachable.
  - Pages and search results stay below the message size limit: long lines are cut at 2000 characters with a marker.
  - A search stops after 5 seconds and returns what it found so far.
- [x] AC7: Reading and searching happen outside the global state lock, so voice and chat keep running while a large file is read.
- [x] AC8: The Logs tab fits from 360 px (A96): the file list and viewer stack under `narrow`, and long lines wrap or scroll horizontally inside the viewer. (Done as one after the other under `narrow`: the list with the filters, an opened file on the whole page until it is closed; long lines wrap.) Texts in German and English, README rights table updated.

### Tests (TDD)

1. `LogCommandTests > "ListLogs_ServerAndChannelFiles_NewestFirst_WithNames"` (AC2)
   - Given: server started with two channels and some chat, then a restart (so two server files)
   - Expected: both server files and the channel files, channel names resolved, sizes greater than 0
2. `LogCommandTests > "ReadLog_PagesOf1000_LastPageFirst"` (AC3): a file with 2500 lines, pages 3, 2, 1
3. `LogCommandTests > "SearchLogs_AllFiles_CaseInsensitive_Max500_Truncated"` (AC5, AC6)
4. `LogCommandTests > "Logs_RequireLogsView_UnknownIdRejected_NoPathTraversal"` (AC1, AC6)
5. `LogCommandTests > "LongLine_CutAt2000"` (AC6)
6. `LogCommandTests > "Search_DoesNotHoldStateLock"` (AC7): a chat message is delivered while a long search runs (search on a large generated file, with a test hook that pauses the reader)
7. `LogsViewModelTests > "FileFilter_TypeAndPeriod"` (AC2)
8. `LogsViewModelTests > "OpenFile_PageNavigation_Refresh"` (AC3)
9. `LogsViewModelTests > "LineFilter_HighlightsMatches"` (AC4)
10. `LogsViewModelTests > "SearchHit_OpensFileAtLine"` (AC5)
11. `AdminViewModelTests > "Tabs_VisibleByPermission"` extended with `LogsView`, `PermissionRulesTests` default groups unchanged except Admin (AC1)
12. `ResponsiveTests > "Admin_EveryTabFits"` with the Logs tab, `LocalizationTests` (AC8)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Test 4 and 11 (red): permission bit, `Require` in the new handlers, tab visibility.
2. Tests 1, 2, 3 and 5 (red): `LogReader` (list, page, search) on a directory, independent of `ServerState`; file ids as a stable hash or relative name from the listing, resolved only through the listing.
3. Test 6 (red): handlers read files on a worker task and post the answer to the session, without taking `gate` while reading.
4. Tests 7 to 10 (red): `LogsViewModel`.
5. XAML tab, test 12, headless screenshot at 360 and 1100 px.

### Out of Scope

- Downloading (Package 82)
- Live tail (automatic follow of new lines); a manual refresh is enough
- The client's own log files

---

## Package 82: Server logs downloadable

**Goal:** Users with the new right "Logs herunterladen" can save a single log file or a selection of files as a zip on their PC.

**Dependencies:** Package 81 (file list and file ids)

**Affected files:**
- `src/OVS.Shared/Permissions/Permission.cs` (change): `LogsDownload = 1 << 24`
- `src/OVS.Server/Logging/LogReader.cs` (change): build a zip of selected files
- `src/OVS.Server/Commands/LogCommands.cs` (change): download requests
- `src/OVS.Shared/Protocol/Messages.cs` (change): `PrepareLogDownload`, `LogDownloadReady`, `DownloadLogChunk`, `LogChunk`
- `src/OVS.Client/ViewModels/LogsViewModel.cs` (change), `src/OVS.Client/Views/AdminView.axaml`, `AdminView.axaml.cs` (change): selection, save dialog, progress
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change): `Perm_LogsDownload` and labels
- `README.md` (change)
- `tests/OVS.Tests/Server/LogCommandTests.cs`, `tests/OVS.Tests/Client/LogsViewModelTests.cs`, `ResponsiveTests.cs` (change)

### Context

After Package 81 the client knows the log files by id. Backups (Package 75) already transfer files pull-based in chunks of `ProtocolInfo.BackupChunkBytes` (512 KB) with a `.part` file on the client and a `StorageProvider` save dialog in `AdminView.axaml.cs`. Log files are still being appended to while the server runs.

### Acceptance Criteria

- [x] AC1: New right "Logs herunterladen" (`LogsDownload`), selectable in the group editor. Only Admin has it by default, existing groups get nothing new. Download buttons are only visible with the right, and the server checks it on every download request. Without `LogsView` the tab stays hidden, so downloading also needs the view right.
- [x] AC2: Files in the list can be selected with checkboxes. There are shortcuts "Alle im Zeitraum" and "Keine".
  - "Herunterladen" saves one selected file as `.log` under its original name.
  - Several files are saved as one `.zip` named `ovs-logs_<from>_<to>.zip`, which keeps the folder structure `server/...` and `channels/<channel name>_<id>/...`.
- [x] AC3: The server takes a consistent snapshot: files are copied at the moment of the request, and lines written later are not included. For a zip, the archive is built in a temporary file below `<DataDir>/logs-export/`, deleted after the transfer, when the session ends, and at the next start.
- [x] AC4: The transfer runs in chunks of 512 KB, one request per chunk, with progress in percent. The client writes to `<target>.part` and moves it into place at the end, so a failed transfer leaves no half file. A zip over 200 MB is refused (`LogsTooLarge`), and the user is told to choose a shorter period.
- [x] AC5: Voice and chat keep working during a download (reading and zipping outside the state lock).
- [x] AC6: The selection column and download controls fit from 360 px (A96). Texts in German and English, README updated.

### Tests (TDD)

1. `LogCommandTests > "DownloadSingleFile_ByteIdenticalSnapshot"` (AC2, AC3): content equals the file at request time, lines logged afterwards are not included
2. `LogCommandTests > "DownloadSelection_ZipWithFolders_TempRemoved"` (AC2, AC3)
3. `LogCommandTests > "Download_RequiresLogsDownload_TooLargeRejected"` (AC1, AC4)
4. `LogCommandTests > "Download_ChunkedPullBased_ChatStillFlows"` (AC4, AC5)
5. `LogsViewModelTests > "Selection_AllInPeriod_None_SingleVsZip"` (AC2)
6. `LogsViewModelTests > "Download_PartFileMovedAtEnd_Progress"` (AC4)
7. `ResponsiveTests > "Admin_EveryTabFits"` with the selection and progress, `LocalizationTests` (AC6)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 3 (red): permission bit, checks.
2. Tests 1 and 2 (red): snapshot copy, zip with `System.IO.Compression`, temp folder and cleanup.
3. Test 4 (red): chunked transfer, reusing the chunk pattern from Package 75 (ideally a shared helper instead of a copy).
4. Tests 5 and 6 (red), UI with selection, save dialog, progress; test 7, headless screenshot at 360 and 1100 px.

### Out of Scope

- Automatic or scheduled log export
- Deleting log files from the app (retention is set in Package 69)

---

## Package 83: Every request validated on the server

**Goal:** No client request can crash its handler or store malformed text: every field is checked for null, range and allowed characters, and violations are answered with `InvalidValue`.

**Dependencies:** none

**Affected files:**
- `src/OVS.Shared/Protocol/FrameCodec.cs` (change): `ProtocolJson.Options` with `RespectNullableAnnotations`
- `src/OVS.Server/ServerState.cs` (change): `Handle` catches unexpected exceptions per request and answers `InvalidValue` instead of dropping the connection; `ValidName` and a new `ValidText`
- `src/OVS.Server/Commands/ChannelCommands.cs`, `AdminCommands.cs`, `ChatCommands.cs`, `LinkCommands.cs`, `BackupCommands.cs`, `LogCommands.cs` (change)
- `src/OVS.Server/ServerState.cs` `Admit` (change): nickname rules and uniqueness
- `tests/OVS.Tests/Server/RequestValidationTests.cs` (new), existing command tests (change)

### Context

Audit findings (both passes agree):
- Nullable annotations are not enforced when deserializing. `CreateChannel`/`EditChannel` with `Description = null` (ChannelCommands.cs:41/66), `UploadBackupChunk` with `DataBase64 = null` (BackupCommands.cs:86) and `SetChannelLinks` with a null list element (LinkCommands.cs:39) throw a `NullReferenceException` inside the lock. The sender is disconnected.
- `EditChannel.Order` accepts any int (ChannelCommands.cs:76). `int.MaxValue` makes the next `CreateChannel` overflow `Max + 1` (:42).
- `ValidName` (ServerState.cs:408) rejects only `char.IsControl`. Zero-width characters (U+200B), bidi overrides (U+202E) and U+2028/2029 pass in nicknames, channel, group and server names.
- Nickname uniqueness is checked case-insensitively against online users only (ServerState.cs:178).
- Chat (ChatCommands.cs:22), welcome text (AdminCommands.cs:170) and channel descriptions (ChannelCommands.cs:204) are length-limited only; any control characters reach other clients.
- The server password has no length limit (AdminCommands.cs:188).
- `SendChat` with an out-of-range `ChatTarget` value is treated as private chat and counted but delivered nowhere.

### Acceptance Criteria

- [ ] AC1: A request with a null value in a non-nullable field, an unknown enum value or a malformed list is rejected with `InvalidValue`, and the connection stays open. No handler throws for any such input.
- [ ] AC2: Unexpected exceptions inside a handler are logged (German server log line with the request type) and answered with `InvalidValue`. Only protocol violations on the frame level still close the connection.
- [ ] AC3: Names (nickname, channel, group, server) reject control characters, Unicode format characters (category Cf: zero-width, bidi controls) and U+2028/2029, and are trimmed. Nickname uniqueness compares NFKC-normalized, case-insensitive names against online users AND the stored `LastNickname` of other users (so an offline admin's name cannot be taken by a new identity).
- [ ] AC4: Multi-line texts (welcome text, channel description) allow `\n` but reject other C0/C1 control characters and bidi controls. Chat text allows `\n` and rejects the same set.
- [ ] AC5: `EditChannel.Order` is ignored (order only changes through `ReorderChannels`) or validated to 0..channel count; `CreateChannel` never overflows. The server password is limited to 1..128 characters when set.
- [ ] AC6: `SendChat` with a target outside the enum is rejected with `InvalidValue` and not counted against the rate limit.

### Tests (TDD)

1. `RequestValidationTests > "NullFields_Rejected_ConnectionStays"` (AC1): Theory over CreateChannel/EditChannel with null Description, UploadBackupChunk with null data, SetChannelLinks with a null element, SendChat with null text; expected `InvalidValue` and a following Ping still answered
2. `RequestValidationTests > "HandlerException_AnsweredNotDropped"` (AC2): a test hook throws inside a handler
3. `RequestValidationTests > "Names_RejectFormatAndBidiChars"` (AC3): Theory "Admin​", "‮evil", "a b" for nickname, channel, group, server name
4. `RequestValidationTests > "Nickname_OfflineUserNameTaken_NormalizedCompare"` (AC3)
5. `RequestValidationTests > "MultilineTexts_AllowNewlineRejectOtherControls"` (AC4)
6. `RequestValidationTests > "ChannelOrder_NoOverflow_PasswordLength"` (AC5)
7. `RequestValidationTests > "ChatTargetOutOfRange_Rejected"` (AC6)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 and 2 (red): `RespectNullableAnnotations`, enum validation after deserialization, catch-all per request in `Handle`.
2. Tests 3 to 7 (red): `ValidName`/`ValidText` helpers, use them everywhere text is accepted.
3. Client: mirror the same rules in the dialogs so users get the message before sending (the server remains the authority).

### Out of Scope

- Rate limits (Package 86)

---

## Package 84: Consistent rank rules for every moderation action

**Goal:** Every action on another user (kick, ban, unban, mute, move, group change, delete) is allowed only on users with strictly fewer rights, never on oneself, and never removes the last admin.

**Dependencies:** none

**Affected files:**
- `src/OVS.Shared/Permissions/Permission.cs` (change): `CanActOn` becomes a strict subset
- `src/OVS.Server/Commands/ModerationCommands.cs` (change): `FindTarget`, `OnBan`, `OnBanUser`, `OnUnban`, `OnSetServerMute`, `OnKick`
- `src/OVS.Server/Commands/AdminCommands.cs` (change): `FindAssignment` checks the target's rank
- `src/OVS.Server/Commands/ChannelCommands.cs` (change): `OnMoveUser`
- `src/OVS.Client/ViewModels/ServerViewModel.cs`, `AdminViewModel.cs` (change): same rule for showing and enabling actions
- `tests/OVS.Tests/Server/ModerationTests.cs`, `AdminCommandTests.cs`, `PermissionRulesTests.cs`, `tests/OVS.Tests/Client/AdminViewModelTests.cs`, `ServerViewModelTests.cs` (change)

### Context

`CanActOn(actor, target)` means `target ⊆ actor` (Permission.cs:50), so equal ranks may act on each other. `FindTarget` (ModerationCommands.cs:158-171) does not exclude oneself: a server-muted moderator can unmute themselves, and the last admin can ban themselves (`Ban`/`BanUser` have no last-admin check; `DeleteUser` and `UnassignGroup` do). `AssignGroup`/`UnassignGroup` only check that the group is within the actor's rights, not the target's rank (AdminCommands.cs:118-142, `FindAssignment` :254): a GroupsAssign holder can strip Guest or Moderator from a stronger user. `OnUnban` (ModerationCommands.cs:91-106) lifts any ban regardless of the banned user's or the creator's rank and replies with the full `BanList` without checking `BansView`. The client hides self-actions (ServerViewModel.cs:618-620) but the server allows them.

### Acceptance Criteria

- [ ] AC1: `CanActOn` requires the target's rights to be a strict subset of the actor's rights. Two users with equal rights (for example two admins, or two moderators) cannot kick, ban, mute, move, regroup or delete each other.
- [ ] AC2: No moderation action targets oneself on the server (`PermissionDenied`): Kick, Ban, BanUser, SetServerMute, MoveUser of another session id equal to one's own, AssignGroup/UnassignGroup on one's own fingerprint, DeleteUser. Moving oneself stays possible through `JoinChannel`.
- [ ] AC3: Ban and BanUser can never hit the last member of the Admin group (`LastAdmin`), the same as DeleteUser and UnassignGroup.
- [ ] AC4: AssignGroup and UnassignGroup require the actor to be able to act on the target (AC1) in addition to the existing group-subset rule.
- [ ] AC5: Unban requires that the actor could ban that user now (rank rule against the banned user's stored rights) and replies only with an acknowledgement; the full `BanList` is sent only if the actor has `BansView`.
- [ ] AC6: The client shows and enables exactly the actions the server would allow (same helper), so hidden buttons and server answers never disagree.

### Tests (TDD)

1. `PermissionRulesTests > "CanActOn_StrictSubsetOnly"` (AC1)
2. `ModerationTests > "EqualRanks_CannotActOnEachOther"` (AC1): two moderators, kick/ban/mute/move
3. `ModerationTests > "SelfTargeting_Denied"` (AC2): Theory over every action, including a server-muted moderator unmuting themselves
4. `ModerationTests > "LastAdmin_CannotBeBanned"` (AC3): online and offline ban
5. `AdminCommandTests > "Unassign_FromStrongerUser_Denied"` (AC4)
6. `ModerationTests > "Unban_StrongerBannedUser_Denied_NoBanListWithoutBansView"` (AC5)
7. `AdminViewModelTests`/`ServerViewModelTests > "ActionsMatchServerRule"` (AC6)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Test 1 (red), `CanActOn` strict; fix existing tests that relied on equal ranks.
2. Tests 2 to 6 (red): one server helper `CanModerate(actor, targetRights, targetFingerprint)` used by all handlers.
3. Test 7: client uses the same helper for visibility.

### Out of Scope

- Persisting the server mute (Package 85)

---

## Package 85: Server mute survives reconnects

**Goal:** A user muted by the server stays muted after disconnecting and reconnecting until someone with the right lifts it.

**Dependencies:** none

**Affected files:**
- `src/OVS.Server/Data/ServerData.cs` (change): `UserRecord.ServerMuted`
- `src/OVS.Server/Commands/ModerationCommands.cs` (change): `OnSetServerMute` persists
- `src/OVS.Server/ServerState.cs` (change): `Admit` applies the stored mute
- `src/OVS.Shared/Protocol/Messages.cs` (change): `KnownUserInfo.ServerMuted`
- `src/OVS.Client/ViewModels/AdminViewModel.cs`, `src/OVS.Client/Views/AdminView.axaml` (change): mute state on the user card
- `tests/OVS.Tests/Server/ModerationTests.cs`, `tests/OVS.Tests/Client/AdminViewModelTests.cs` (change)

### Context

`ServerMuted` exists only on the live `Session` (Session.cs:26). `OnSetServerMute` (ModerationCommands.cs:114-120) sets it there, and `Admit` creates every new session unmuted (ServerState.cs:192-197). A muted user just reconnects.

### Acceptance Criteria

- [ ] AC1: Setting or lifting a server mute is stored on the user's record and saved.
- [ ] AC2: On login a stored mute is applied before the Welcome, so the user never sends voice in between; the other clients see the user as server-muted.
- [ ] AC3: The user overview shows "vom Server stummgeschaltet" and, with `UserMute`, allows lifting it for offline users too (rank rule from Package 84 applies).
- [ ] AC4: Deleting the user's data (Package 72) removes the mute.

### Tests (TDD)

1. `ModerationTests > "ServerMute_Persists_AcrossReconnect"` (AC1, AC2)
2. `ModerationTests > "ServerMute_NoVoiceBeforeWelcome"` (AC2): voice packets right after Hello are not relayed
3. `AdminViewModelTests > "UserCard_ShowsServerMute_LiftOffline"` (AC3)
4. `ModerationTests > "DeleteUser_RemovesMute"` (AC4)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1, 2 and 4 (red), field, persistence, `Admit`.
2. Test 3 (red), `KnownUserInfo` and UI; an offline lift uses a new `SetStoredServerMute(Fingerprint, Muted)` request checked like `SetServerMute`.

### Out of Scope

- Temporary mutes with an expiry

---

## Package 86: Limits on the control channel

**Goal:** No client can exhaust the server's memory, flood other clients with broadcasts or keep a dead connection open, whatever it sends.

**Dependencies:** none

**Affected files:**
- `src/OVS.Server/Session.cs` (change): per-session request budget, outbox limited by bytes
- `src/OVS.Server/ControlServer.cs` (change): write timeout, dispose on overflow or timeout, per-IP limit before TLS, global cap on pending handshakes
- `src/OVS.Server/ServerState.cs` (change): budget check in `Handle`, no-op suppression
- `src/OVS.Server/Data/ServerIconStore.cs` (change): cached base64
- `src/OVS.Server/Commands/ChannelCommands.cs`, `AdminCommands.cs`, `BackupCommands.cs`, `LogCommands.cs` (change)
- `tests/OVS.Tests/Server/LimitsTests.cs` (new)

### Context

Audit findings:
- `GetServerIcon` (ServerState.cs:310) needs no right, has no limit and builds a new base64 string of up to ~683 KB on every call (ServerIconStore.cs:24). The outbox is limited to 1024 messages, not bytes (Session.cs:14), and the write loop has no timeout (ControlServer.cs:169-183). A client that stops reading pins about 1.4 GB per connection. A 1 MiB `RequestId` echoed in `Error` is a smaller variant.
- `JoinChannel` and `SetSelfState` need no right, have no limit and broadcast `UserUpdated` to every session; `JoinChannel` also writes two channel log lines, even for the same channel.
- Only chat (5 per 5 s) and log jobs (4 queued) are limited. `ListUsers`, `ListBans`, `CreateBackup`, `RedeemAdminToken`, `SearchLogs`, `PrepareLogDownload` are not.
- A connection over the per-IP limit still costs a full TLS handshake (ControlServer.cs:84-93); there is no global cap on pending handshakes.
- `RequestId` has no length limit.

### Acceptance Criteria

- [x] AC1: Every session has a request budget (token bucket, for example 20 requests per second with a burst of 40, Ping exempt). Exceeding it answers `RateLimited`; sustained abuse (budget exhausted for 10 s) disconnects with a log line.
- [x] AC2: Expensive requests have their own lower limits: `GetServerIcon` at most once per 10 s per session and answered from a cached string; `ListUsers`, `ListBans`, `ListLogs`, `ListBackups` at most 2 per second; `CreateBackup` and `PrepareLogDownload` at most 1 per 10 s; `RedeemAdminToken` at most 5 failures per 10 minutes per IP, failures logged.
- [x] AC3: `JoinChannel` into the current channel and `SetSelfState` without a change do nothing (no broadcast, no log line).
- [x] AC4: The outbox is limited by bytes too (for example 8 MB); exceeding it disconnects the session. Each write has a timeout (for example 10 s); on timeout the connection is closed immediately and its slot released, without waiting for the write loop.
- [x] AC5: `RequestId` longer than 64 characters is rejected (`InvalidValue` with the id cut).
- [x] AC6: Connections over the per-IP limit are closed before TLS. At most 64 handshakes run at the same time server-wide; more are closed right away.

### Tests (TDD)

1. `LimitsTests > "RequestFlood_RateLimited_ThenDisconnected"` (AC1)
2. `LimitsTests > "ServerIcon_CachedAndThrottled"` (AC2)
3. `LimitsTests > "AdminTokenGuessing_Throttled_Logged"` (AC2)
4. `LimitsTests > "NoOpJoinAndSelfState_NoBroadcast"` (AC3)
5. `LimitsTests > "NonReadingClient_DisconnectedAndSlotFreed"` (AC4): a raw TLS client that never reads, flooding icon requests; the server stays under the byte cap and the slot is free within the timeout
6. `LimitsTests > "LongRequestId_Rejected"` (AC5)
7. `LimitsTests > "OverLimitConnection_ClosedBeforeTls"` (AC6)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 5 and 2 first (highest impact): byte cap, write timeout, cached icon.
2. Tests 1, 3, 4 and 6: token bucket in `Session`, per-request costs in `Handle`, no-op checks.
3. Test 7: reorder the accept path in `ControlServer`, global semaphore.

### Out of Scope

- Identity flood and list paging (Package 87)
- UDP limits (Package 88)

---

## Package 87: Identity flood and large lists

**Goal:** Connecting with many fresh key pairs cannot bloat the data file or stall the server, and every list reaches the client even when it is large.

**Dependencies:** Package 86 (uses its budget helpers)

**Affected files:**
- `src/OVS.Server/ServerState.cs` (change): new-identity throttle per IP, debounced persistence
- `src/OVS.Server/Data/ServerData.cs` (change): pruning of unused guest records
- `src/OVS.Server/Commands/AdminCommands.cs`, `ModerationCommands.cs`, `BackupCommands.cs`, `LogCommands.cs` (change): paged lists
- `src/OVS.Shared/Protocol/Messages.cs` (change): page fields on `ListUsers`/`UserList`, `ListBans`/`BanList`, `ListLogs`/`LogList`, `ListBackups`/`BackupList`
- `src/OVS.Client/ViewModels/AdminViewModel.cs`, `LogsViewModel.cs` (change): fetch all pages
- `tests/OVS.Tests/Server/IdentityFloodTests.cs` (new), existing list tests (change)

### Context

Every successful login with a new fingerprint creates a permanent `UserRecord` and rewrites the whole `server-data.json` with fsync under the global lock (ServerState.cs:183-190, Persist on remove :254, ServerData.cs:217-226). On a server without a password a script can create thousands. At about 2000 records the `UserList` JSON exceeds 1 MiB; `FrameWriter` throws and every admin who opens the Users tab is disconnected (the client re-requests automatically). `BanList`, `LogList` and `BackupList` have no paging either.

### Acceptance Criteria

- [x] AC1: At most 10 new identities per IP (IPv6: per /64) per hour are admitted; more get `RateLimited` with a log line. Known fingerprints are never throttled.
- [x] AC2: Saving after login and logout is debounced (at most once per 2 s, and always on shutdown), so a connect loop no longer causes an fsync per connection.
- [x] AC3: Records of users who only ever had the Guest group, have no bans and have not logged in for 90 days are pruned automatically (logged).
- [x] AC4: All four lists are paged (for example 200 entries per page, plus the total). The client fetches all pages and shows the full list; no single message can exceed the frame limit.
- [x] AC5: With 5000 users, 2000 bans and 2000 log files the admin page loads without disconnecting.

### Tests (TDD)

1. `IdentityFloodTests > "NewIdentities_ThrottledPerIp_KnownNotThrottled"` (AC1)
2. `IdentityFloodTests > "ConnectLoop_SavesDebounced"` (AC2)
3. `IdentityFloodTests > "OldGuestRecords_Pruned"` (AC3)
4. `IdentityFloodTests > "LargeLists_Paged_UnderFrameLimit"` (AC4, AC5)
5. `AdminViewModelTests > "UserList_FetchesAllPages"` (AC4)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Test 4 (red) first: paging is the visible failure today.
2. Tests 1 to 3 (red): throttle, debounce, pruning.
3. Test 5: client paging.

### Out of Scope

- Accounts or registration

---

## Package 88: Network: UDP endpoint and IPv6

**Goal:** Voice traffic can only be sent to the address of the user's own control connection, and IPv6 users cannot bypass per-IP limits and IP bans by rotating addresses.

**Dependencies:** none

**Affected files:**
- `src/OVS.Server/Voice/UdpVoiceServer.cs` (change): endpoint check, pre-filter
- `src/OVS.Server/ServerState.cs` (change): lock-free session lookup for UDP, /64 grouping for connection limit and IP bans
- `src/OVS.Server/Commands/ModerationCommands.cs` (change): IP ban stores the /64 for IPv6
- `tests/OVS.Tests/Server/VoiceRelayTests.cs`, `ModerationTests.cs`, `HandshakeTests.cs` (change)

### Context

`UdpVoiceServer.cs:72` sets `UdpEndpoint = from` on any authenticated Hello, without comparing `from.Address` with the control connection's `Session.Ip`. One Hello with a spoofed source address makes the server send every relayed voice packet to a victim (reflection). Unauthenticated UDP packets cost a `FindSession` under the global lock and an AES-GCM attempt, with sequential, guessable session ids. The per-IP connection limit (ServerState.cs:99-107) and IP bans (:158, exact string) use the full IPv6 address, while password throttling already groups by /64.

### Acceptance Criteria

- [ ] AC1: The server accepts a UDP endpoint only if its address equals the session's control-connection address (IPv4 exact, IPv6 same /64). Other packets for that session are dropped and counted.
- [ ] AC2: The UDP path looks up sessions without taking the global lock, and a per-source-address pre-filter drops more than 200 unauthenticated packets per second from one address before decryption.
- [ ] AC3: The per-IP connection limit and IP bans group IPv6 addresses by /64 (the same helper as password throttling). Existing IPv6 bans keep matching their exact address and also their /64.

### Tests (TDD)

1. `VoiceRelayTests > "Hello_FromOtherAddress_EndpointNotChanged"` (AC1)
2. `VoiceRelayTests > "GarbageFlood_DroppedBeforeDecrypt_ControlStaysResponsive"` (AC2)
3. `HandshakeTests > "Ipv6_SameSlash64_SharesConnectionLimit"` (AC3)
4. `ModerationTests > "IpBan_Ipv6_MatchesWholeSlash64"` (AC3)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Test 1 (red), endpoint check.
2. Test 2 (red), `ConcurrentDictionary` for UDP lookups, pre-filter.
3. Tests 3 and 4 (red), shared address-grouping helper.

### Out of Scope

- NAT traversal changes; clients behind carrier-grade NAT keep working because the check uses the control connection's observed address

---

## Package 89: Backups with their own right, restore only for admins

**Goal:** Backups are managed with the new right "Backups verwalten", uploading and restoring are reserved for members of the Admin group, and a restored archive is validated completely.

**Dependencies:** none (Package 84 recommended first for the last-admin rules)

**Affected files:**
- `src/OVS.Shared/Permissions/Permission.cs` (change): `BackupsManage = 1 << 25`, `All = (1 << 26) - 1`
- `src/OVS.Server/Data/ServerData.cs` (change): migration (groups with `ServerConfig` get `BackupsManage` once)
- `src/OVS.Server/Commands/BackupCommands.cs` (change): rights per request
- `src/OVS.Server/Data/BackupStore.cs` (change): full validation, manifest cap, quota, listing cache
- `src/OVS.Client/ViewModels/AdminViewModel.cs`, `src/OVS.Client/Views/AdminView.axaml` (change)
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change): `Perm_BackupsManage`
- `README.md` (change)
- `tests/OVS.Tests/Server/BackupTests.cs`, `DataStoreTests.cs`, `PermissionRulesTests.cs`, `tests/OVS.Tests/Client/AdminViewModelTests.cs` (change)

### Context

All backup requests need `ServerConfig` (BackupCommands.cs). A holder of that right can download an archive with `cert.pfx` (TLS private key, no password, ServerCertificate.cs:20) and the complete `server-data.json` (password hash, every IP), and can upload an edited archive that puts their fingerprint into the Admin group and restore it (BackupStore.cs:178-200 checks structure, versions, default channel, certificate, logo only). `"users": null` or `"settings": null` passes validation and crashes every later start (ServerState.cs:51/53; ServerHost.cs:55 only catches ConfigException/InvalidDataException); `"channels": null` throws inside `Read` and leaves the upload file. `List()` decompresses every manifest up to 64 MB under the lock on every chunk; there is no count or disk quota.

### Acceptance Criteria

- [x] AC1: New right "Backups verwalten" (`BackupsManage`): list, create, download and delete backups. Groups that have `ServerConfig` today get it once on update (so nobody loses a function); new servers: Admin only.
- [x] AC2: Uploading and restoring require membership in the Admin group, checked on the server for every upload chunk and on restore. The buttons are hidden for everyone else.
- [x] AC3: A restored or uploaded archive is fully validated before anything is replaced: every collection present, every name valid under the Package 83 rules, ids unique, references (group ids, channel ids, default channel) consistent, at least one Admin-group member. Any failure, including unexpected exceptions, gives `InvalidBackup` and leaves no temp file. The server can never end up unable to start because of a restored archive.
- [x] AC4: `manifest.json` is limited to 16 KB; the listing is cached and refreshed only when files change. At most 50 backups and 2 GB in total; creating or uploading beyond that gives `BackupQuotaExceeded` with a hint to delete old ones.
- [x] AC5: Downloads keep the certificate (so a server move keeps its fingerprint); the download dialog warns that the file contains the server's private key and user data and must be stored safely.
- [x] AC6: Texts in German and English, README rights table and backup section updated.

### Tests (TDD)

1. `BackupTests > "BackupsManage_Required_ServerConfigAloneNotEnough"` (AC1)
2. `DataStoreTests > "Migration_ServerConfigGroupsGetBackupsManage"` (AC1)
3. `BackupTests > "UploadAndRestore_OnlyAdminGroup_CheckedPerChunk"` (AC2)
4. `BackupTests > "Restore_InvalidContent_RejectedServerStillStarts"` (AC3): Theory null users, null settings, null channels, invalid names, dangling group ids, no admin
5. `BackupTests > "ManifestCap_Quota_ListingCached"` (AC4)
6. `AdminViewModelTests > "BackupButtons_ByRightAndAdminGroup_DownloadWarns"` (AC2, AC5)
7. `LocalizationTests` (AC6)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 to 3 (red): permission bit, migration, per-request checks.
2. Test 4 (red): `BackupStore.Validate(ServerData)` reusing the server's own validation helpers, catch-all to `InvalidDataException`.
3. Test 5 (red): manifest cap, quota, cache.
4. Test 6, UI and texts.

### Out of Scope

- Encrypting the certificate inside backups (decided against, A101)

---

## Package 90: Log and console hygiene

**Goal:** Nothing a client sends can forge or corrupt lines in the console, the Docker log or the log files.

**Dependencies:** none

**Affected files:**
- `src/OVS.Server/Logging/ServerLogs.cs` (change): `OneLine` also for the console
- `src/OVS.Shared/Logging/LogFiles.cs` (change): `OneLine` also escapes ANSI escape sequences
- `src/OVS.Server/Commands/LogCommands.cs` (change): search log line only for accepted searches
- `tests/OVS.Tests/Server/ServerLogsTests.cs`, `LogCommandTests.cs` (change)

### Context

`ServerLogs.Server` (ServerLogs.cs:29-30) writes the raw line to `Console.WriteLine`; only the file path goes through `LogFiles.OneLine` (LogFiles.cs:72, 87-102). Chat text reaches the console (ChatCommands.cs:57), so a guest can inject a fake line such as "Admin-Token: ..." or terminal escape sequences into `docker logs`. `LogCommands.cs:44` logs a search even when it is then refused as `RateLimited` (:104).

### Acceptance Criteria

- [ ] AC1: Console and file receive the same escaped line: CR, LF, U+2028/2029, other control characters and ESC (ANSI sequences) are shown escaped.
- [ ] AC2: A chat message containing a newline followed by a fake timestamp produces exactly one log line on the console and in the file.
- [ ] AC3: The search log line is written only when the search is actually run.

### Tests (TDD)

1. `ServerLogsTests > "ConsoleLine_EscapedLikeFile"` (AC1)
2. `ServerLogsTests > "ChatWithFakeLine_OneLineOnly"` (AC2)
3. `LogCommandTests > "RefusedSearch_NotLogged"` (AC3)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 and 2 (red), apply `OneLine` before both outputs, extend it for ESC.
2. Test 3 (red), move the log line.

### Out of Scope

- Structured (JSON) logging

---

## Package 91: Salted server password hash

**Goal:** The server password is stored as a salted, slow hash, and existing hashes are upgraded without the admin re-entering the password.

**Dependencies:** none

**Affected files:**
- `src/OVS.Server/Data/ServerData.cs` (change): `ServerSettings.PasswordHash` format with algorithm, iterations and salt; `Hash`/`Verify`
- `src/OVS.Server/ServerState.cs` (change): `Admit` verifies and upgrades
- `src/OVS.Server/Commands/AdminCommands.cs` (change): new passwords hashed the new way
- `tests/OVS.Tests/Server/DataStoreTests.cs`, `HandshakeTests.cs` (change)

### Context

`ServerSettings.Hash` stores an unsalted SHA-256 hex string (ServerData.cs:36-44), compared in constant time. The hash is in every backup, so it can be attacked offline with precomputed tables.

### Acceptance Criteria

- [ ] AC1: New and changed passwords are stored as PBKDF2-SHA256 with a random 16-byte salt and at least 100,000 iterations, in a self-describing format (for example `pbkdf2$<iterations>$<salt>$<hash>`).
- [ ] AC2: Verification stays constant-time and works for both formats; a successful login with an old SHA-256 hash replaces it with the new format and saves.
- [ ] AC3: No password (empty) keeps meaning "no password".

### Tests (TDD)

1. `DataStoreTests > "PasswordHash_Pbkdf2_SaltedUnique"` (AC1): the same password twice gives different stored values that both verify
2. `HandshakeTests > "OldSha256Hash_LoginWorks_UpgradedAfter"` (AC2)
3. `HandshakeTests > "EmptyPassword_StillOpenServer"` (AC3)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 to 3 (red), `Rfc2898DeriveBytes.Pbkdf2`, format parsing, upgrade in `Admit`.

### Out of Scope

- User accounts with individual passwords

---

## Package 92: Clients get only the rights data they need

**Goal:** Without "Gruppen sehen", clients no longer receive other groups' permission bits or other users' full rights; they get only what the UI needs.

**Dependencies:** Package 84 (the "can act on" rule sent to clients is the strict rule from 84)

**Affected files:**
- `src/OVS.Shared/Protocol/Messages.cs` (change): `GroupInfo` without permissions for non-viewers, `UserInfo` with per-viewer flags
- `src/OVS.Server/ServerState.cs` (change): per-recipient Welcome and broadcasts (`Info(s)` per viewer)
- `src/OVS.Server/Commands/AdminCommands.cs` (change): `GroupsChanged` per recipient
- `src/OVS.Client/Net/StateMirror.cs`, `src/OVS.Client/ViewModels/ServerViewModel.cs`, `AdminViewModel.cs` (change): use the flags instead of computing from others' permissions
- `tests/OVS.Tests/Server/StateSyncTests.cs`, `tests/OVS.Tests/Client/ServerViewModelTests.cs`, `AdminViewModelTests.cs` (change)

### Context

Every client receives all groups with their permission bitmasks in the Welcome and in every `GroupsChanged` (ServerState.cs:445-452, AdminCommands.cs:38 etc.), and every online user's fingerprint, effective permissions and group ids (`UserInfo`, :430-431), even without `GroupsView`. That makes it trivial to see who can moderate. The client uses others' permissions to decide which actions to show.

### Acceptance Criteria

- [ ] AC1: Users with `GroupsView` receive groups with permissions as today; everyone else receives groups with id, name and order only.
- [ ] AC2: Other users' `UserInfo` no longer carries their permission bits. Instead each recipient gets per-target flags it needs: `CanBeModeratedByMe` (Package 84 rule) and `CanSpeakLinked` for the link indicator, plus the group ids for display. The own user still receives the full own permissions.
- [ ] AC3: Flags are recomputed and sent when the recipient's or the target's rights change (the existing `RecomputePermissions` path).
- [ ] AC4: The client's context menus, admin cards and indicators behave exactly as before for every combination covered by the existing tests.
- [ ] AC5: Fingerprints stay in `UserInfo` (needed for private chat and user volume); this is documented as public data.

### Tests (TDD)

1. `StateSyncTests > "Welcome_WithoutGroupsView_NoPermissionBits"` (AC1, AC2)
2. `StateSyncTests > "Flags_PerRecipient_RecomputedOnRightsChange"` (AC3)
3. existing `ServerViewModelTests` and `AdminViewModelTests` adapted to flags stay green (AC4)
4. `StateSyncTests > "GroupsView_StillSeesFullGroups"` (AC1)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1, 2 and 4 (red): per-recipient serialization in `ServerState` (`Info(s, viewer)`), broadcasts become per-recipient sends.
2. Client adaptation, test 3.
3. Protocol version: bump once for Packages 83 to 92 if a release happened after version 10, otherwise keep 10 and extend the comment.

### Out of Scope

- Hiding fingerprints (they are public keys' hashes, A104)
