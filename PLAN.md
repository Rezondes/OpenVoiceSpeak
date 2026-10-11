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
>
> **Packages 93 and 94 (channel locks):** the request was split at the user's request into the group lock (93) and the password lock with its bypass right (94). 94 needs 93.
>
> **Packages 95 to 98:** 95 logo, 96 drag and drop, 97 and 98 the 400 ms waiting rule (split at the user's request into the shared mechanism with the admin page, and all other places). 98 needs 97; 95, 96 and 97 can run in parallel.
>
> **Packages 99 to 109 (animated display):** the request "more animations, away from the plain style" was cut into one package per area. Rendering stays on Skia (Impeller was considered and declined). 99 adds the setting "Vereinfachte Darstellung" (today's look) / "Animierte Darstellung" and must come first; 100, 101, 102, 104, 105 and 108 can then run in parallel; 103 needs 101 and 102, 106 needs 101 and 104, 107 needs 101; 109 (every action animated) comes last.
>
> **Packages 110 to 115:** one package per point of the request, in its order (no reordering needed). 111 (separators) needs 110 (duplicate names), because every separator carries the same empty name; 113 (sidebar width) and 111 both touch `MainWindow.FitSidebar`, so 113 should come after 111 or be merged carefully. All others can run in parallel.
>
> **Packages 119 to 125:** one package per point of the request, in its order, except that the two key bugs share one cause: key bindings edited on the settings page only reach the key poller on Save. 121 (self test) fixes that, so 125 (push-to-mute) needs 121. 122 (many users) and 124 (idle CPU and RAM) are cut by cost: 122 is what every server message and every speaking user costs in the UI, 124 is what runs while nothing happens. 123 (voice filters) and 124 both change `CapturePipeline`, so 124 comes after 123. 119, 120, 121, 122 and 123 can run in parallel. (Packages 116 to 118, the website, were planned in a separate file; their assumptions are A124 to A127.)
>
> **Packages 126 to 142 (voice like Fluxer):** the request "plan every difference between Fluxer and OVS so that OVS voice comes as close to Fluxer as possible" followed an analysis of Fluxer (the `fluxerapp/fluxer` web and desktop app with its LiveKit setup, and `fluxerapp/flutter_client`). Fluxer runs the LiveKit SFU over WebRTC; OVS keeps its own UDP stack (A134), so each package rebuilds one thing Fluxer gets from WebRTC, LiveKit or its own client code. The order follows the dependencies, not the order of the findings: 126 (a lost UDP Hello leaves a session without voice, a bug found on the way) comes first; 127 (louder senders) adds the settings migration that 128 and 129 use; 133 (real connection stats) provides the measurements 134, 135 and 136 need; 139 (auto-reconnect) and 141 (voice over TCP) build on 126; 142 (music mode) comes last. 126, 127, 130, 131, 132, 137 and 138 can start at once. Fluxer features outside voice transmission are not planned (A152).

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
| 93 | Channels locked to groups | A channel can be restricted to one or more groups, so only users with at least one of these groups (and admins) can join it, shown with a lock icon behind the channel name. | - |
| 94 | Channels locked with a password | A channel can additionally or alternatively be protected by a password, which users with the new right "Passwort-Lock umgehen" and admins do not need to enter. | 93 |
| 95 | The same logo at every size | The exe icon, the window and taskbar icon and the title bar show the same logo with the sound wave as the start screen, generated from `logo.svg` so they can never drift apart. | - |
| 96 | One drag and drop behaviour everywhere | Every reorder drag in the app shows a lifted, slightly tilted preview of the dragged item and a placeholder gap of the item's height where it will land. | - |
| 97 | Waiting states, shared mechanism and admin page | A shared pending mechanism shows a visible reaction within 400 ms of every action that waits, and the administration page (lists, log search, backups) uses it. | - |
| 98 | Waiting states everywhere else | Every other action that waits for the server shows a visible reaction within 400 ms. | 97 |
| 99 | Display mode setting and motion switch | Users choose between "Vereinfachte Darstellung" and "Animierte Darstellung" in the settings, and the whole client follows that choice live. | - |
| 100 | Hover, press and focus effects | In the animated display every interactive control reacts to hover, press and keyboard focus with a smooth effect. | 99 |
| 101 | Items appear animated | In the animated display every new item (user, channel, bookmark, admin row) fades and slides into its list. | 99 |
| 102 | Items leave animated | In the animated display every removed item collapses and fades out of its list before it disappears. | 99 |
| 103 | Items move animated | In the animated display items that change position or channel glide to their new place. | 101, 102 |
| 104 | Page and tab transitions | In the animated display switching pages and tabs cross-fades and slides. | 99 |
| 105 | Dialogs, popups and drawer | In the animated display dialogs, menus, dropdowns, tooltips and the compact sidebar drawer open and close animated. | 99 |
| 106 | Connect and disconnect | In the animated display connecting builds the server view up step by step, and disconnecting folds it away. | 101, 104 |
| 107 | Chat animations | In the animated display chat messages, notices and the send state animate. | 101 |
| 108 | Live status animations | In the animated display speaking, mute, deafen, slots, busy and level states animate continuously and smoothly. | 99 |
| 109 | Every action animated | A walk-through test proves that every user action has its animation in the animated display, none in the simplified one, and that load stays bounded. | 100 to 108 |
| 110 | Duplicate channel names | Several channels may carry the same name, and the client still recognises its own newly created channel. | - |
| 111 | Separator channels | Admins can put separators into the channel list: a full-width line that nobody can join, link or chat in. | 110 |
| 112 | No empty channel tooltip | Hovering a channel without description and without links shows no tooltip at all. | - |
| 113 | Sidebar fits its footer | After connecting, the fitted sidebar is wide enough for the own nickname, the talk hint and the footer buttons. | - |
| 114 | Ask before losing unsaved changes | Leaving a page or admin tab with unsaved changes asks whether to save and leave, leave without saving, or keep editing. | - |
| 115 | Tooltips fade in once | In the animated display a tooltip fades in once and stays, without blinking a second time. | - |
| 119 | Groups from the user's context menu | Moderators with the right to assign groups give and take a user's groups with a right click on the user. | - |
| 120 | Move users by drag and drop and menu | Users with the move right drag a user onto a channel, or pick the channel straight from the user's context menu, to move them. | - |
| 121 | Key bindings apply at once, self test with push-to-talk | Keys set on the settings page work at once, so the self test in push-to-talk mode plays back while the key is held. | - |
| 122 | Fast with many users | A server with hundreds of users and many speakers stays smooth in both displays, because a server message only updates what it changes. | - |
| 123 | Voice filters | Noise suppression and automatic gain clean the microphone signal before it is sent, each switchable in the settings. | - |
| 124 | Low CPU and RAM while idle | While nothing happens the client uses almost no CPU, opens the audio devices only when needed and allocates nothing per audio frame. | 123 |
| 125 | Push-to-mute works like mute | Holding the push-to-mute key mutes exactly like the mute button, with icon, sound and server state, and releasing it restores the previous state. | 121 |
| 126 | UDP Hello is retried | A session whose first UDP Hello is lost still gets voice, and one whose UDP mapping breaks later gets it back. | - |
| 127 | Louder senders | Every microphone arrives at a normal speech level, because automatic gain is on for everyone and also raises very quiet voices. | - |
| 128 | Fluxer's volume curve | The volume sliders follow Fluxer's curve, so 200 % is four times as loud and the lower half of the slider is fine-grained. | 127 |
| 129 | Automatic voice gate | Voice activation adapts its threshold to the room by itself and no longer flickers at the end of words. | 127 |
| 130 | Push-to-talk release delay | Releasing the talk key a moment early no longer cuts off the last syllable. | - |
| 131 | Talk feedback | Users are told when they talk while muted or deafened, and can hear their talk key act. | - |
| 132 | Bitrate per channel | Each channel has its own voice quality (16 to 128 kbps, default 64), and senders encode with their channel's bitrate. | - |
| 133 | Real connection stats | Users see their real voice round trip, packet loss and jitter in a signal icon and a details panel. | 126 |
| 134 | Adaptive jitter buffer | The jitter buffer starts each talk spurt with a cushion that fits the measured jitter instead of a fixed 40 ms. | 133 |
| 135 | Adaptive loss protection | The sender's FEC and bitrate follow the loss its packets actually suffer. | 132, 133 |
| 136 | Redundant frames | Packets on a lossy path carry a copy of the previous frame, so a lost frame is played back in full quality. | 135 |
| 137 | Audio device changes | Unplugging, plugging in or switching the Windows default device during a call keeps voice running without a trip to the settings. | - |
| 138 | Clean resampling | Microphones that do not run at 48 kHz are converted without aliasing. | - |
| 139 | Auto-reconnect | A lost connection is restored by itself, back into the same channel with the same mute state. | 126 |
| 140 | Echo cancellation | Users on speakers can use voice activation without the others hearing themselves. | 127 |
| 141 | Voice over TCP | Voice works on networks that block UDP, over the existing TLS connection. | 126 |
| 142 | Music mode | A sender can switch to a music mode that sends stereo without voice filters. | 132, 140 |

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
- **A106 Moving into a group-locked channel.** Allowed only if the mover could join the channel by group themselves; the moved user's groups are not checked (user's decision). Admins are always allowed.
- **A107 Moving into a password-locked channel.** Nobody can move another user into a password-locked channel, except Admin-group members, who are exempt from all locks.
- **A108 Lock changes and visibility.** Adding, changing or removing a lock, or a user losing a group, never moves anybody out of the channel; locks only apply to later joins and moves. Locked channels stay fully visible (name, description, users, lock icon with tooltip), and channel links route voice as before.
- **A109 Lock details.** Any groups can be chosen for a group lock; deleting a group removes it from lock lists, and a lock left with no groups means "admins only". The default channel can have neither lock (like the user limit). Channel passwords are stored as salted PBKDF2 like the server password, never sent to clients, and remembered in the client only in memory for the current connection. "Admin" means membership in the Admin group.
- **A110 Admins move anyone anywhere.** Members of the Admin group ignore every permission check, as before: they can join any channel and move any user into any channel, whether it has a group lock, a password lock, a user limit, or all of them at once. The moved user's groups, the password and the limit are not checked in that case.
- **A111 Logo.** One design at every size, with the sound wave everywhere (the user's choice); small sizes get thicker wave strokes. The icon files are generated from `logo.svg` by a checked-in tool.
- **A112 Drag and drop rules (apply to every reorder drag).** Preview lifted into an overlay: scale 1.03, shadow +8 px, tilt 3 degrees; placeholder gap with the dragged item's height at the target, which the item takes after the drop without jumping. OS file drags (logo drop zone) only get a hover highlight, because their preview belongs to the operating system.
- **A113 Waiting rule.** Every action that waits shows a visible reaction within 400 ms (spinner, loading state, pending marker or progress). The busy UI appears after 150 ms if the action is not done, so fast actions do not flicker; no answer within 10 s ends in a visible, retryable error.
- **A114 Display mode.** The setting "Darstellung" offers "Animierte Darstellung" (default for everyone, also for settings files without the key) and "Vereinfachte Darstellung" (today's look, pixel for pixel). It applies live. The Windows setting "Show animations" does not override it; the drag from Package 96 keeps its own behaviour.
- **A115 One place for animated styles.** All new animated styles live in `Styles/Motion.axaml`, scoped under the window class `animated` (set by `Motion.Apply`, like `Responsive.Apply`). Code-driven animations ask `Motion.IsAnimated` / `Motion.Duration(...)`, which is zero in the simplified display.
- **A116 Motion tokens.** `Motion.Fast` 120 ms (hover, press), `Motion.Normal` 220 ms (appear, leave, tabs, dialogs), `Motion.Slow` 320 ms (pages, connect, move); easing `CubicEaseOut`, "pop" effects a light `BackEaseOut`. Lists stagger by 30 ms, at most 8 items per batch.
- **A117 Animations never change state.** Every action ends in the same final state in both displays; leaving items are invisible to commands, counts and the debug API; first fills and rebuilds do not replay enter animations; looping animations run only while their state is active.
- **A118 Scope.** Every area of the client animates (hover, lists, moves, pages, tabs, dialogs, popups, drawer, connect, chat, live status); client-only, no server or protocol change.
- **A119 Duplicate names (Package 110).** Only channel names may repeat (case and spelling do not matter any more). Group names and nicknames stay unique.
- **A120 Separators (Package 111).** A separator is a channel of kind `Separator`, created in the existing create dialog by choosing "Trenner" as the type. It has no name, description, limit, lock or link, can be moved (drag, up, down) and deleted like a channel, and has no "Bearbeiten". It is never the default channel. The protocol version goes up by one.
- **A121 Sidebar footer (Package 113).** The fitted width also covers the footer: avatar, nickname, the wider of the talk hint and the nickname (as built: the "Sendet" texts trim while talking, the width never jumps because sending does not refit) and the three buttons. The window's own upper bound (`Responsive.SidebarMax`) still wins; below it texts trim as today. The header (server name) keeps trimming.
- **A122 Unsaved changes (Package 114).** Places with a manual save: the settings page, the group editor, the server settings tab and the link matrix. Leaving means: changing page (menu, settings cog, Esc, the X of the settings page), switching admin tab, selecting another group, disconnecting, connecting to another server, closing the window. The buttons "Abbrechen" (settings) and "Verwerfen" (links) discard without asking, because they say so. "Speichern und verlassen" that the server refuses stays on the page with its error.
- **A123 Tooltip fade (Package 115).** Only the animated display changes; the simplified display keeps the Fluent tooltip as it is.
- **A128 Groups in the context menu (Package 119).** A submenu "Gruppen" on a user row lists every group with a check mark, with the same rules as the administration's user list: shown when I have `GroupsAssign` and the user is weaker than me (`CanBeModeratedByMe`), never on myself; a group is enabled when `AssignableByMe`; the last admin cannot lose the Admin group. A click applies at once (no save button), like the toggles in the administration. No protocol change: `AssignGroup` / `UnassignGroup` by fingerprint.
- **A129 Moving users (Package 120).** "Verschieben" in the context menu becomes a submenu listing the channels directly (the current channel and separators left out, locked or full channels shown but refused by the server as today) instead of the channel picker dialog. Dragging a user row onto a channel row moves that user; dragging my own row joins that channel. The drag follows A112 (lifted preview), the target channel row is highlighted instead of a gap, Esc cancels. No protocol change: `MoveUser` and `JoinChannel`.
- **A130 Live key bindings (Package 121).** While the settings page is open, the key poller uses the bindings shown on the page; closing without saving restores the saved ones, like the audio live preview.
- **A131 Voice filters (Package 123).** Noise suppression with RNNoise (BSD-3, native library shipped with the client) and an own automatic gain control (no dependency). Defaults: noise suppression on, automatic gain off, also for settings files without the keys. Both act on the sent signal, before voice activation and the level meter, and apply live. No echo cancellation (users with speakers instead of a headset keep using push-to-talk). Replaces A18 in part.
- **A132 Audio only when needed (Package 124).** Microphone and output device open when connecting or starting the self test, and close on disconnect; the settings page opens the microphone only while it is shown (level meter). Windows then shows the microphone as in use only while connected.
- **A133 Push-to-mute (Package 125).** Holding the push-to-mute key does the same as pressing the mute button, and releasing it undoes exactly that: if I was already muted, holding and releasing changes nothing. Single modifier keys (Shift, Ctrl, Alt alone) can be bound to any action.
- **A134 Own voice stack stays (Packages 126 to 142).** OVS does not switch to WebRTC or LiveKit; only ideas and values are taken from Fluxer. Where a package names a Fluxer value, it is the value found in Fluxer's code (October 2026).
- **A135 Settings migration (Packages 127 to 129).** `ClientSettings` gets a `SettingsVersion` (a file without it counts as 0). `Load` runs every missing step once, in order, and the file is saved with the new version. Step 1 (127): `AutoGain` on. Step 2 (128): per-user volumes and the output volume above 100 % go back to 100 %. Step 3 (129): a `VadThresholdDb` still at the old default -40 turns the automatic threshold on, any other value stays as a manual threshold. A new file starts at the newest version with the new defaults.
- **A136 Louder senders (Package 127).** Automatic gain is on by default, also for existing users once (A135 step 1). With noise suppression running, a frame counts as speech when RNNoise rates it at least 0.5 (the return value of `rnnoise_process_frame`, ignored today); the -45 dBFS floor stays only for noise suppression off or missing. The gain may reach +30 dB instead of +18 dB, target and peak limit stay (-20 dBFS, -3 dBFS). The input gain's hard clip at ±1 becomes a soft clip above -3 dBFS. The fixed voice boost ×2 (A51) stays for now; if the manual check of 127 and 128 finds the result too loud, a follow-up lowers it.
- **A137 Volume curve (Package 128).** Fluxer's curve for the per-user volume and the output volume: up to 100 % the gain is `10^((v - 100) / 100 * 1.2)`, above it `4^((v - 100) / 100)`, so 200 % is ×4 and 50 % is about ×0.25. Per-user and output gain multiply and are capped at ×12 together (the voice boost and the limiter come after). The sliders still show 0 to 200 %. Stored values above 100 % go back to 100 % once (A135 step 2); values up to 100 % keep their number and follow the new curve, so they become somewhat quieter than before, like in Fluxer.
- **A138 Voice gate (Package 129).** Fluxer's gate: noise floor = 10th percentile of the frame levels of the last 2 s times 1.5, automatic threshold = 3 × the floor, clamped to -110 to -28 dBFS; before 0.3 s of learning the manual threshold applies. Hysteresis: the hold only starts counting once the level is below 0.7 × the threshold (about -3 dB); the hold is 9 frames (180 ms) instead of today's 15 (300 ms). The 20 ms pre-roll stays and covers Fluxer's 10 ms lookahead. A switch "Automatisch" on the settings page; the manual slider stays in dBFS (-60 to -10); the level meter shows the threshold in use.
- **A139 Release delay (Package 130).** A setting "Loslassverzögerung", 0 to 2000 ms, default 20 ms (Fluxer's default). It applies to push-to-talk and link push-to-talk, also in the self test, never to push-to-mute or mute. Pressing again within the delay just continues.
- **A140 Talk feedback (Package 131).** The existing voice hint line shows "Du bist stummgeschaltet" (or "taub geschaltet", "vom Server stummgeschaltet") while a talk key is held, or voice activation hears voice, while I am self-muted, deafened or server-muted, and for 2 s after. Two new sounds "Sprechtaste gedrückt" and "Sprechtaste losgelassen" exist in the sound settings and are off by default, so nothing changes for anyone who does not turn them on. Fluxer only warns for a key pressed while deafened; the rest goes beyond it.
- **A141 Bitrate per channel (Package 132).** `ChannelInfo.Bitrate` in kbps, default 64, allowed 16 to 128, set in the create and edit dialog by whoever may edit the channel today. A stored channel without a value loads as 64, so existing channels move from 32 to 64 kbps. Separators have none. A sender encodes with its own channel's bitrate, also for link push-to-talk. The protocol version goes up by one.
- **A142 Connection stats (Package 133).** The UDP ping goes every 2 s instead of every 5 s (Fluxer refreshes its stats every 2 s) and carries the client's send time, which the server echoes. Both directions report loss inside the ping: the server tells the client how many of its frames it missed (uplink loss), the client tells the server the loss it sees from the server (downlink loss, used by 136). The client also measures jitter (RFC 3550 style, from arrival times against 20 ms steps), jitter buffer delay and concealed or FEC-rebuilt frames. A signal icon next to the ping text rates the voice round trip above the user's own baseline (median of the last 30 samples): below 70 ms green with 4 bars, below 160 yellow 3, below 300 orange 2, else red 1; UDP unreachable shows it crossed out. Clicking it opens "Verbindungsdetails" with round trip, uplink and downlink loss, jitter, buffer delay, concealed frames and the bitrate in use. The protocol version goes up by one.
- **A143 Adaptive jitter buffer (Package 134).** The start cushion of each talk spurt is `clamp(ceil(max(2 × jitter, peak lateness) / 20 ms) + 1, 2, 10)` frames from the speaker's measured jitter and the peak lateness of its frames (as built in Package 134, so network stalls raise it too); both estimates are halved after a silence of more than a second. The buffer only shrinks at a talk spurt boundary (today's rebuffer) and through the existing cap of 10 frames, never by dropping frames in the middle of speech, because OVS has no time stretching.
- **A144 Adaptive loss protection (Package 135).** The encoder's `PacketLossPercent` follows the reported uplink loss (`clamp(round(1.5 × loss), 5, 30)`, updated every 2 s). Above 10 % loss for 5 s the bitrate halves, down to at most 16 kbps; after 10 s below 2 % it goes up one step, never above the channel's bitrate. This is the part of WebRTC's congestion control that matters for voice; nothing changes on the server.
- **A145 Redundant frames (Package 136).** Like Fluxer's RED, but only where it helps, since Fluxer's "always on" would double every stream through a self-hosted server. The voice payload may carry the previous frame after the current one. The sender adds it while its uplink loss is at least 2 %. The server keeps each speaker's last frame and adds it for each recipient whose reported downlink loss is at least 2 %, so one listener on bad Wi-Fi does not cost everyone. The receiver uses the copy for a lost frame before falling back to FEC or concealment. The protocol version goes up by one.
- **A146 Device changes (Package 137).** The engine listens to Windows device notifications (`MMDeviceEnumerator.RegisterEndpointNotificationCallback`) and to `RecordingStopped` / `PlaybackStopped` with an error. 800 ms after the last change (Fluxer's debounce) it reopens what is open: the chosen device if present, else the default with one notice; a chosen device that comes back is taken again. "Standard" follows a change of the Windows default device.
- **A147 Resampling (Package 138).** NAudio's `WdlResampler` (already shipped in `NAudio.Core`) replaces the linear resampler for capture rates other than 48 kHz; equal rates pass through as today.
- **A148 Auto-reconnect (Package 139).** It starts on `ConnectionLost`, `ServerShutdown` and `ServerRestart` (the restart added in the review of Package 139), never on `Kicked`, `Banned`, `ReplacedByNewConnection`, `VersionMismatch`, a wrong password or my own disconnect. Attempts after 1, 2, 4, 8 and 16 s (5 attempts, about 31 s, Fluxer's 1 s base and 30 s window), then today's disconnected notice. Meanwhile the status reads "Verbindung verloren, neuer Versuch in N s" with "Abbrechen", and no "Getrennt" sound plays. On success the client joins the last channel again (with the password it used for it, kept in memory only), restores self mute and deafen, and posts one notice; server mute comes from the server (Package 85). The server needs no change: a new connection replaces the half-open old session (`ServerState.cs:208-225`). Each attempt carries a generation number, and the result of an outdated one is discarded.
- **A149 Echo cancellation (Package 140).** WebRTC's audio processing module (AEC3) from the freedesktop `webrtc-audio-processing` project (BSD-3), built once for win-x64 with a small C wrapper DLL (its API is C++) and checked in, loaded like RNNoise (missing means off with one log line). Order on the sent signal: echo cancellation, noise suppression, automatic gain, voice gate. The far-end reference is the mixed output frame after the limiter, as two 10 ms halves, with a delay hint from the capture and output buffer sizes. Only its echo canceller is used; RNNoise and the own automatic gain stay. A switch "Echounterdrückung", default on (Fluxer's voice mode), also for existing users. Replaces the echo part of A18 and A131. If the library cannot be built and loaded, the package stops after its first step and reports.
- **A150 Voice over TCP (Package 141).** When UDP is still not reachable 5 s after connecting despite the Hello retries (126), or pings go unanswered for 15 s, voice goes over the TLS connection: new messages `VoiceUp(Seq, Target, Opus)` (client to server) and `VoiceDown(SessionId, Seq, Target, Opus)` (server to client), the Opus bytes as base64 in the JSON. The server relays to each recipient on that recipient's path (UDP or TCP), with the same routing and rights. The control channel limits (Package 86) get a separate voice budget (60 per second, burst 10, like UDP). UDP is tried again every 30 s and voice switches back when it answers. The ping text shows ", Sprache über TCP". The protocol version goes up by one.
- **A151 Music mode (Package 142).** A setting "Übertragungsart": "Sprache" (today) or "Musik". Music turns echo cancellation, noise suppression and automatic gain off, captures in stereo without the downmix (a mono device stays mono), and encodes with Opus `AUDIO` and signal type music, in stereo when the channel's bitrate is at least 64 kbps (Fluxer's rule). In-band FEC is off in music mode, as Opus carries it only in its speech coder and would leave the music coder for it; lost music frames are covered by the redundant copies (Package 136) and the decoder's concealment. A stereo device whose signal comes on one input only (an instrument on a two-input interface) plays on both sides. Push-to-talk and voice activation work as before. Receivers decode and mix in stereo and open the output in stereo; a mono speaker plays on both sides. No protocol change: an Opus packet carries its channel count.
- **A152 Not planned (Packages 126 to 142).** Fluxer features outside voice transmission or without a use in a single self-hosted server: several voice servers and regions, join confirmation through LiveKit webhooks, end-to-end encryption (Fluxer's room key is known to its own gateway; the OVS server decrypts as well, so nothing would change for users), entrance sounds, soundboard, AFK channel, DM calls, video and screen share, further noise suppression models (DeepFilterNet, GTCRN), native libopus (see Package 124). Speaking indicators stay packet based: OVS only sends while someone speaks, so they already match Fluxer's level-based rings.
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
| 31 | `ServerLogWriterTests > "Chat_Logged_PrivateWithoutContent"` | `ChatTests > "Logs_ServerAndChannel_PrivateWithoutContent"` | Der Test braucht drei verbundene Clients, die Hilfen dafür liegen in `ChatTests`. |
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

1. `SmokeTests.cs > "VersionAndPort_Defaults_AreStable"` deckt AC2 und AC5 ab.
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
5. `"Read_UnknownTypeOrInvalidJson_Throws"`, `"Read_ZeroLength_Throws"` (AC5)
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
2. `HandshakeTests.cs > "ServerCertificate_LoadOrCreate_Twice_SameThumbprint"` (AC12)
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
   - `ServerTuningTests > "OverLimitConnection_ClosedBeforeTls"` (AC11)
     (Hinweis zur Referenz: Seit f598a4e wird eine Verbindung über dem Limit pro IP schon vor TLS geschlossen, statt `Rejected(TooManyConnections)` zu bekommen; der Test prüft das Schliessen.)
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
4. `"AdminGroup_NotEditableNotDeletable"`, `"GuestGroup_NotDeletable_OtherGroupDeletable"` (AC5)
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
   - `"SetSelfState_BroadcastsUserUpdated_DeafImpliesMute"` (AC12)
   - `"Restart_KeepsCreatedChannels"` (AC13)
3. `ChannelCommandTests.cs`. Zwei Clients: A mit vorbelegter Admin-Gruppe, B als Gast.
   - `"Join_BroadcastsUserUpdated"` (AC5)
   - `"Create_AsAdmin_BroadcastsAndPersists"`, `[Theory] "Create_InvalidName_Error"` (AC6)
     (Hinweis zur Referenz: Seit de0bc0a dürfen mehrere Channels denselben Namen tragen, `NameTaken` entfällt; der Teil "eindeutig" ist durch keinen Test mehr gedeckt.)
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

1. `AdminCommandTests.cs`:
   - `"NoAdmin_TokenGenerated_AdminExists_NoToken"` (AC1)
   - `"AdminToken_Redeem_Once_Valid_Twice_Invalid"` (AC2, AC3)
2. `AdminCommandTests.cs`:
   - `"Redeem_BroadcastsNewPermissions_ThenTokenInvalid"` (AC2)
   - `"CreateGroup_AsAdmin_BroadcastsGroupsChanged"`, `"UpdateGroup_ChangesOnlineMemberPermissions"` (AC4)
   - `"CreateGroup_GrantingUnownedPermission_PermissionDenied"` (AC4, Eskalation)
   - `[Theory] "ProtectedGroups_Errors"` (AC5)
   - `"DeleteGroup_RemovedFromUsers"` (AC6)
   - `"Assign_OfflineUser_Persisted_AndListed"`, `"Unassign_LastAdmin_Error"` (AC7)
   - `"ListUsers_WithoutGroupsAssign_PermissionDenied"`, `"Assign_OfflineUser_Persisted_AndListed"`, `"Unassign_OfflineUser_And_ListShowsNickname"` (AC8)
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
   - `"KickSession_TargetDisconnectedOthersUserLeft"` (AC1)
   - `"BanSession_PersistsAndDisconnects_ReconnectRejected"` (AC2)
   - `"BanSession_PersistsAndDisconnects_ReconnectRejected"`, `"IpBan_OtherIdentitySameIp_Rejected"` (AC3)
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
   - `"Unlink_Broadcasts_MissingIsNoOp"` (AC4)
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
   - `"SeqCounter_CountsFromZero_OverflowThrows"` (AC7)
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
2. `VoiceRoutingTests.cs > "RateLimiter_Over60PerSecond_Dropped"` mit `ManualTimeProvider` (AC8)
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

1. `ReceivePathTests.cs > "EncodeDecode_Sine_PreservesEnergy"` (AC1)
2. `ReceivePathTests.cs`:
   - `"OutOfOrder_WithinDelay_PlayedInOrder"` (AC2)
   - `"BelowStartDelay_PullReturnsNull"` (AC3)
   - `"Gap_ProducesPlcFrame_ThenContinues"` (AC4)
   - `"LatePacket_Dropped"` (AC5)
   - `"SpeakerPause_KeepsDecoder"` (AC6)
   - `"Overflow_CappedAt10"` (AC7)
3. `ReceivePathTests.cs`:
   - `"Mixer_SumsAndClamps"` (AC8)
   - `"Mixer_ActiveSpeakers_ReportViaLink"` (AC9)

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

1. `SendPathTests.cs > [Theory] "ChooseTarget_EachRuleRow_ReturnsExpectedTarget"` mit einer Zeile pro Regel sowie den Kombinationen PTT+Link und VAD+Link (AC1 bis AC6)
2. `SendPathTests.cs`:
   - `"Vad_SilenceInactive_LoudActive_Hangover9Frames"` (AC7)
     (Hinweis zur Referenz: Der Test prüft 9 Frames Nachlauf statt 15; geändert mit Package 129 (8ee9b6c).)
3. `SendPathTests.cs > "FrameChunker_IrregularInput_ExactFrames_NoLoss"` (AC8)
4. Manuelle Checks für AC9 und AC10 mit einem Debug-Schalter `--audio-debug` in `Program.cs` des Clients.

Testbefehl: `dotnet test --filter "FullyQualifiedName~SendPathTests.ChooseTarget|FullyQualifiedName~SendPathTests.Vad_|FullyQualifiedName~FrameChunker"`

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

1. `ClientNetTests.cs > "KnownServers_Check_Cases"` für Known, Unknown und Mismatch (Grundlage für AC1 und AC2)
2. `ClientNetTests.cs`, gegen `TestServer`:
   - `"FirstConnect_AsksAndStores_SecondConnectSilent"` (AC1)
   - `"ChangedCert_Mismatch_Refused_NoHelloSent"`: zweiter `TestServer` mit neuem Zertifikat unter derselben Adresse (AC2)
   - `"Connect_MirrorShowsSelfInLobby_AndSecondClient"` (AC5)
   - `"ServerKick_RaisesDisconnectedWithReason"` (AC7)
3. `IdentityStore`: `"Identity_LoadOrCreate_Twice_SameFingerprint"` in `ClientNetTests.cs` (AC3)
4. `StateMirrorTests.cs > [Theory] "Apply_EachDelta"` (AC4)
5. `ClientNetTests.cs > "Voice_SendFromA_ReceivedByB_Identical"` (AC6)

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

1. `ServerViewModelTests.cs`:
   - `"Build_SortsChannelsAndGroupsUsers"` (AC1)
   - `"UserUpdated_MovesUser_KeepsInstance"` (AC2)
   - `"Speaking_ClearsAfter300ms"` (AC3)
   - `"SpeakingViaLink_Flagged"` (AC4)
   - `"LinkedChannels_ShowPartners"` (AC5)
   - `"JoinCommand_SendsJoinChannel"` (AC6)
   - `"Deafen_SendsSelfState_AndRestoresMute"` (AC7)
2. `SettingsTests.cs > "FullSettings_RoundTrip_IncludingBookmarks"` (AC8)
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

1. `SettingsTests.cs`:
   - `"FullSettings_RoundTrip_IncludingBookmarks"`, `"Missing_GivesDefaults"` (AC1)
   - `"Corrupt_DefaultsAndBackup"` (AC2)
2. `SettingsTests.cs`:
   - `"KeyList_SameKeyTwoActions_BlocksSave"` (AC3)
   - `"Clamp_OutOfRange"` (AC4)
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

1. `ServerViewModelTests.cs`:
   - `[Theory] "ChannelMenu_VisibilityByPermission"` (AC1)
   - `[Theory] "UserMenu_VisibilityByPermissionAndRank"` (AC2)
2. `AdminViewModelTests.cs`:
   - `ServerViewModelTests > "LinkCandidates_ExcludesSelfAndLinked"` (AC3)
   - `GroupListViewModelTests > "Groups_ActorHoldsEveryRightOfTheGroupButNotAll_OnlyTheHeldRightsToggleTheRowShowsTheGroupsRightsAndItCanBeDeleted"`, `"Groups_ActorHoldsAllRights_AdminGroupReadOnlyWithDisabledTogglesAndNeitherProtectedGroupDeletable"` (AC4)
   - `"AssignGroup_SendsRequest_DisablesHigherGroups"` (AC5)
   - `ServerViewModelTests > "Ban_SendsDurationAndIpFlag"` (AC6)
   - `[Theory] "Tabs_VisibleByPermission"` (AC7)
   - `ServerViewModelTests > "RedeemToken_HiddenForAdmin"` (AC8)
3. `SettingsTests.cs > "ErrorTexts_EveryCodeHasText"`: Schleife über alle Codes (AC9)

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

`DebugApiTests.cs`: `"MissingHeader_Forbidden"` (AC1), `"Devices_ViewModelWithoutAudio_ListsTheHardwaresDevices"`, `"UnknownChannel_IsBadRequest"`, `"FullFlow_Linking_PttAndLinkPtt"` (AC2 bis AC4), `"DeafenedClient_HearsNothing_UntilUndeafened"` (AC4), `"AdminActions_ThroughApi"` (AC5). AC6 ist durch die zwei getrennten Profile in jedem dieser Tests abgedeckt. Der Hinweis ohne `SpeakLinked` (AC4) auch ohne Netz: `MainViewModelTests > "Tick_LinkKeyHeldWithoutLinkRight_ShowsTheNoLinkRightHint"`, `MainViewModelTests > "Tick_LinkKeyUpWithoutLinkRight_ShowsNothing"`, `VoiceHintsTests > "Evaluate_NoLinkRightWithLinkKeyDown_ShowsTheNoLinkRightHint"`, `VoiceHintsTests > "Evaluate_NoLinkRightWithoutLinkKeyDown_ShowsNothing"`, `VoiceHintsTests > "Evaluate_LinkRightWithLinkKeyDown_ShowsNothing"` (AC4).

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

`ServerLogWriterTests.cs`, mit `ManualTimeProvider` und temporärem Datenverzeichnis:
1. `"ServerLine_GoesToConsoleAndFileOfThisStart"`, `"EachStart_OwnFile_SharedByServerAndChannelLogs"`, `"DayChange_StartsNewFile_NamedAfterThatMoment"` (AC1)
2. `"ConnectDisconnectRejected_Logged"`, `"AdminActions_LoggedWithActor"` für Gruppen, Zuweisung, Einstellungen, Kick, Ban, Unban und Server-Mute (AC2)
3. `"JoinMoveLeave_LoggedInChannelLogs"` (Beitritt, Verschieben im alten und neuen Channel, Trennen), `"Link_LoggedInBothChannels_EditDeleteLogged"` (AC3)
4. `"TokenAndPassword_NeverInFiles"` (AC4)
5. `"OldFiles_DeletedAfterRetention"`, `"RetentionZero_KeepsAll"` und in `ServerConfigTests` `"Load_InvalidLogDays_Throws"` (AC5)
6. `ServerLogWriterTests > "WriteFailure_DoesNotThrow_ReportsOnce"`, dazu `"ReaderLockingTheFile_LineIsNotLost"`: Ein Leser, der die Datei sperrt, kostet keine Zeile (AC6)
7. `"RenamedChannel_SameFolderAfterRestart"` (AC7)

Testbefehl: `dotnet test --filter "FullyQualifiedName~ServerLogWriterTests"`

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
1. `ProtocolLogTextTests > "Describe_ServerMessage_InWordsWithNamesFromTheMirror"` für jede Delta-Art, mit Namen aus dem `StateMirror` (AC1)
2. `ClientLogTests > "Connect_Disconnect_Logged"` über `MainViewModel` gegen `TestServer` (AC1)
3. `ClientLogTests > "OwnActions_Logged"` über `ServerViewModel`, denselben Weg, den auch die Debug-API nimmt (AC1). Dazu `ProtocolLogTextTests > "Describe_Request_UsesNames_NeverSecrets"`.
4. `ClientLogTests > "EachStart_OwnFile_OldFilesDeleted"` mit `ManualTimeProvider`, auch über Mitternacht (AC2)
5. `"PasswordAndToken_NeverLogged"` (AC3)
6. `"AudioDebug_WritesToClientLog"` (AC4), ersetzt `MainViewModelTests.AudioDebugLog_RecordsKeysAndFrameRate`
7. `"WriteFailure_DoesNotThrow"` (AC5)

Testbefehl: `dotnet test --filter "FullyQualifiedName~ClientLogTests|FullyQualifiedName~ProtocolLogTextTests"`

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

1. `RestartScheduleTests > "FindNext_TodayOrTomorrow_InLocalTime"` für vor, nach, genau zur Uhrzeit und knapp davor (AC1)
2. `ServerHostTests > "AutoRestart_TellsClients_NewRunServes_WithOwnLogFiles"`: echter Lauf mit Neustart in 5 s, Client bekommt `ServerRestart`, ein neuer Client kommt in den neuen Lauf, zwei Logdateien (AC1, AC2, AC3)
3. `ServerConfigTests > "Load_AutoRestart_OffByDefault_TimeDefaultsTo4am"`, `"Load_AutoRestartAndLogRotation_FromFile_EnvWins"`, `"Load_LogRotateDaily_OnByDefault"`, `"Load_InvalidRestartOrRotation_Throws"` (AC1, AC4)
4. `ServerLogWriterTests > "RotateDailyOff_FileLastsUntilRestart_OldFilesStillDeleted"` (AC5)
5. `ServerHostTests > "ShuttingDown_HandshakeRejectedWithTheReason"`, Regression aus Test 2, der vereinzelt einen Client im alten Lauf fand (AC6)

Testbefehl: `dotnet test --filter "FullyQualifiedName~ServerHostTests|FullyQualifiedName~ServerConfigTests|FullyQualifiedName~ServerLogWriterTests"`

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

1. `SendPathTests > "ChooseTarget_EachRuleRow_ReturnsExpectedTarget"`, neue Zeile `{ VoiceActivation, ptt: true, link: false, vad: false, ..., expected: null }` als Reproduktion, muss vor dem Fix rot sein (AC1)
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
2. `UiSmokeTests > "Overlay_EscCancels_EnterConfirms_EscClosesPage"`: Bestätigen-Dialog, Esc liefert `false`, Enter `true` (AC3)
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
3. `SettingsTests > "Load_NoFile_DefaultPttOnly"` (AC1), `"Load_OldFileWithPttKeys_KeepsThem"` (AC2), `"KeyList_SameKeyTwoActions_BlocksSave"` (AC6)
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
6. `ProtocolLogTextTests > "Describe_Request_UsesNames_NeverSecrets"` um die neuen Nachrichten erweitert
7. `MainViewModelTests > "ServerIcon_LoadedOnce_ThenFromCache_ShownOnTile"`: echter Server mit Logo, der erste Verbindungsaufbau lädt es, der zweite nimmt es aus dem Cache, die Kachel zeigt es (AC3, AC6), `ServerIconTrackerTests > "Receive_LogoOfTheServersHash_ShowsItCachesItAndRefreshesTheTiles"` und `ServerIconWiringTests > "Apply_LogoOfTheServersHash_ShowsItOnTheServerAndOnTheBookmarkTile"` (AC3, AC6), `ServerIconTrackerTests > "Sync_CachedLogoHasTheServersHash_ShowsItWithoutAsking"` (AC6), `ServerIconTrackerTests > "Sync_ServerHasNoLogo_RemovesTheCachedLogoShowsNoneAndAsksNothing"` (AC4); die Regeln (aus dem Cache oder beim Server holen, Wiederholung nach einer Ablehnung, Ankunft prüfen und ablegen) stehen in den übrigen Fakten von `ServerIconTrackerTests`, die Verdrahtung im Hauptmodell in `ServerIconWiringTests`

**Umsetzungsnotizen:**
- Die Tests der Oberfläche rendern jetzt mit Skia statt der Zeichen-Attrappe, damit Bilder wirklich dekodiert und skaliert werden.
- `ServerIconCache` fängt Plattenfehler selbst ab: Der Cache ist nur eine Abkürzung und darf nie das Verbinden verhindern. Gefunden hat das der Test: `File.Delete` wirft, wenn der Ordner `server-icons` noch fehlt.
- Das Logo gilt sofort nach der Auswahl, unabhängig von "Servereinstellungen speichern".

Testbefehl: `dotnet test --filter "FullyQualifiedName~ServerIconTests|FullyQualifiedName~IconImportTests|FullyQualifiedName~UiSmokeTests|FullyQualifiedName~ClientLogTests|FullyQualifiedName~ProtocolLogTextTests"`

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
2. `ChatTests > "NoRight_PermissionDenied_NobodyGetsIt"` als Theory je Ziel (AC2)
3. `ChatTests > "Invalid_EmptyTooLongOfflineSelf"` (AC3), `"RateLimit_SixthInFiveSeconds"` mit `ManualTimeProvider` (AC4), `"ServerMuted_CanWrite"` (AC5)
4. `PermissionRulesTests > "DefaultGroups_HaveDocumentedPermissions"` (um die Chat-Rechte erweitert) und `DataStoreTests > "LoadVersion1_GuestGetsChatRights_Once"` (AC6)
5. `ChatTests > "Logs_ServerAndChannel_PrivateWithoutContent"` (AC7, beim Chat statt in `ServerLogWriterTests`, weil es drei verbundene Clients braucht)
6. `DebugApiTests > "Chat_RoundTrip"` zwischen zwei echten Clients (AC8), `ProtocolLogTextTests` für `ChatMessage` und `SendChat`

Testbefehl: `dotnet test --filter "FullyQualifiedName~ChatTests|FullyQualifiedName~PermissionRulesTests|FullyQualifiedName~DataStoreTests|FullyQualifiedName~ServerLogWriterTests|FullyQualifiedName~DebugApiTests|FullyQualifiedName~ClientLogTests|FullyQualifiedName~ProtocolLogTextTests"`

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
5. `SendPathTests > "ChannelMuted_SendsNothing"` (AC5)
6. `ServerViewModelTests > "ChannelMuted_EditSendsFlag_ViewModelShowsIt"`, `MainViewModelTests > "ChannelMuted_HintAndNoSending"` (AC5), `VoiceHintsTests > "Evaluate_ChannelMuted_ShowsTheMutedChannelHint"` (AC5), `EngineSyncTests > "SyncConnection_OnlyChannelMutedSet_TurnsOnlyTheEnginesChannelMutedOn"` (AC5)
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

1. `ChannelCommandTests > "Join_Full_ChannelFull_AdminStillEnters"` (AC2)
2. `ChannelCommandTests > "Move_IntoFull_NeedsRightOfMover"` (AC3)
3. `ChannelCommandTests > "Edit_DefaultChannelLimit_Invalid"`, `"Edit_LimitOutOfRange_Invalid"` (AC1)
4. `ChannelCommandTests > "Edit_LimitBelowCount_NobodyRemoved_PersistedInSnapshot"` (AC4, AC6)
5. `PermissionRulesTests`: `All` enthält `ChannelJoinFull` (AC5)
6. `ServerViewModelTests > "SlotText_LimitedAndUnlimited"`, `"SlotText_UserJoins_ChangeRaised"`, `"MaxUsers_LimitChanges_ChangeRaised"`, `ChannelSlotsTests > "ToString_LimitedOrUnlimited_CountOfLimitOrCountAlone"` (AC5)
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
4. `ServerViewModelTests > "DropOnChannel_SendsOrderWithSourceBeforeOrAfterTarget"`: die Einfüge-Logik liegt im ViewModel und ist ohne Maus testbar (AC1)
5. `UiSmokeTests`: Kontextmenü enthält beide Einträge, ohne Recht ist Ziehen aus (AC1, AC2); `ProtocolLogTextTests`, `SmokeTests` (AC5)
6. `UiSmokeTests > "ChannelTree_DragRaidAboveLobby_SendsOrder"`: Ziehen mit echten Mausereignissen im Headless-Fenster, kurzer Klick zieht nicht. Offen bleibt nur der Blick auf die Einfügemarke am echten Bildschirm.

Testbefehl: `dotnet test --filter "FullyQualifiedName~ChannelCommandTests|FullyQualifiedName~ServerViewModelTests|FullyQualifiedName~UiSmokeTests|FullyQualifiedName~ClientLogTests|FullyQualifiedName~ProtocolLogTextTests|FullyQualifiedName~SmokeTests"`

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

1. `AdminCommandTests > "ReorderGroups_PersistsAndBroadcasts_RankUnchanged"`, `"ReorderGroups_IncompleteOrWithoutRight_Rejected"` (AC2, AC3)
2. `GroupListViewModelTests > "MoveGroupDown_GroupSelected_SendsTheFullOrderAndShowsItAtOnce"`, `"MoveGroupUp_GroupSelected_SendsTheFullOrder"`, `"MoveGroupAsync_GroupDroppedBeforeTheFirst_SendsTheFullOrderWithItFirst"` `"MoveGroupAsync_UnsavedGroup_SendsNothing"` und `"MoveGroupCommands_DraftSelected_CannotExecute"` (AC1)
3. `ServerViewModelTests > "GroupNames_FollowGroupOrder"` (AC3)
4. `AdminCommandTests > "ReorderGroups_PersistsAndBroadcasts_RankUnchanged"`: Moderator kann Admin weiterhin nicht kicken (AC4)
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
5. `UiSmokeTests`: Tab "Links" in beiden Designs; `ProtocolLogTextTests`, `SmokeTests` (AC5)

Testbefehl: `dotnet test --filter "FullyQualifiedName~LinkCommandTests|FullyQualifiedName~AdminViewModelTests|FullyQualifiedName~UiSmokeTests|FullyQualifiedName~ClientLogTests|FullyQualifiedName~ProtocolLogTextTests|FullyQualifiedName~SmokeTests"`

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
3. `MainViewModelTests > "Connect_SavePassword_OnlyAfterSuccess"` gegen `TestServer` mit Passwort, einmal falsch, einmal richtig (AC2), `BookmarkListTests > "Remember_PasswordNotYetConfirmed_KeepsWhatTheBookmarkHadAndLogsNothing"`, `"Remember_PasswordConfirmedAndAsked_StoresItProtectedAndNeverLogsIt"` und `"Remember_PasswordConfirmedButNotAsked_DropsTheStoredOne"` (AC2)
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
2. `MainViewModelTests > "Bookmark_WrongSavedPassword_AsksAndSavesNew"` und `"Bookmark_AskPassword_Cancel_StaysDisconnected"` (AC3), die Verbindung ohne Fenster: `ConnectionViewModelTests > "ConnectBookmarkAsync_WrongPasswordAndUserCancels_AsksOnceStopsAfterOneAttemptAndKeepsTheRefusedStatus"` und `"ConnectBookmarkAsync_WrongPasswordAndNoAskPasswordDialog_StopsAfterOneAttempt"` und `"ConnectBookmarkAsync_WrongPasswordAndUserAnswers_TriesAgainWithTheAnswerAndItsSaveChoice"` (AC3)
3. `MainViewModelTests > "Bookmark_Edited_PersistsToTheProfileFolder"` (AC4), `BookmarkListTests > "EditAsync_Answered_ReplacesInPlaceSavesLogsAndRefreshesTiles"`, `"EditAsync_PasswordNotKept_StoresNone"`, `"DeleteAsync_Confirmed_RemovesSavesAndLogs"` und `"DeleteAsync_Declined_KeepsIt"` (AC4)
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
5. `MainViewModelTests > "TalkHint_NoPttBinding_UntilBound"` (AC6)
   (Hinweis zur Referenz: Der Test prüft den Hinweis mit den Tastennamen der Liste, "PTT: Maustaste 4, Maustaste 5", nicht "Maus 4"; sein Name nennt nur den Fall ohne Taste.)
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

1. `BuildInfoTests > "Format_CiBuild_DayMonthYearAndStamp"`, `"Format_LocalBuild_Dev"`, `"Stamp_MidnightAndLastSecond_UtcNotLocal"` (AC1)
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

1. `UpdateTests` mit einem Fake-`HttpMessageHandler`: `"NewerRelease_Offered"`, `"SameCommit_OlderRelease_MissingAsset_NotOffered"`, `"DevBuild_NeverAsks"`, `"HttpErrorOrNoNetwork_NoUpdate_WithReason"` (AC1, AC5)
2. `UpdateTests` in einem Temp-Ordner: `"HashMismatch_NothingReplaced"`, `"Success_NewInPlace_OldRenamed_Restarted"`, `"CleanupOld_WaitsForLockedFile_ThenDeletes"` (AC3, AC4)
3. `MainViewModelTests > "StartupCheck_Prompt_LaterDoesNothing"` mit Fake-Checker, `UpdateFlowTests > "CheckAsync_OfferDeclined_SaysAvailableAndInstallsNothing"`, `"CheckAtStartAsync_SettingOn_Checks"`, `"CheckAtStartAsync_SettingOff_DoesNotCheck"` (AC2), `"CheckAsync_NoLookUp_SaysLocalBuildAndLogsNothing"`, `"Use_LocalBuildChecker_CheckAsyncSaysLocalBuildAndSendsNoRequest"` (AC1), `"CheckAsync_LookUpFails_SaysCheckFailedAndLogsTheReason"` (AC5)
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
2. `LocalizationTests > "Language_SystemGermanOrEnglish_ChoiceWins"` als Theory: `de-DE`, `de-AT` ergeben Deutsch, `en-US`, `fr-FR` ergeben Englisch, eine feste Wahl überstimmt (AC2)
3. `LocalizationTests > "ErrorTexts_English"`: mit englischer Kultur liefert `ErrorTexts.For(Codes.PermissionDenied)` den englischen Text (AC4)
4. `LocalizationTests > "ServerDetail_HiddenExceptKickAndBan"` (AC5)
5. `SettingsTests > "Language_RoundTrip_ShownInSettings"` und `UiSmokeTests`: Auswahl "Sprache" auf der Einstellungsseite (AC3)
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
4. `SoundPlayerTests > "Play_Deafened_OnlyOwnMicAndSoundTonesPlay"` (AC4)
5. `SettingsTests > "Sounds_GlobalSettings_RoundTrip"` und `UiSmokeTests`: Abschnitt "Sounds" (AC5)
6. `DebugApiTests > "Sounds_ListedInState"` mit zwei echten Clients (AC3, AC6)
7. Manueller Check (offen, braucht Ohren): Töne klingen angenehm und sind im Spiel hörbar, nicht zu laut

Testbefehl: `dotnet test --filter "FullyQualifiedName~SoundTests|FullyQualifiedName~SoundPlayerTests|FullyQualifiedName~ServerViewModelTests|FullyQualifiedName~SettingsTests|FullyQualifiedName~DebugApiTests|FullyQualifiedName~UiSmokeTests"`

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

1. `SoundImportTests > "Wav_UpToFiveSeconds_Accepted_CopiedAsMono48k"`, `"TooLong_NotAudio_WrongType_Missing_Rejected"` mit selbst erzeugten WAV-Dateien, `MainViewModelTests > "OpenSettings_SoundImported_LandsInTheProfileFolder"` (AC2, AC3)
2. `SoundImportTests > "CustomSound_MissingFile_FallsBackToDefault_LoggedOnce"`, `MainViewModelTests > "OpenSettings_SoundWithMissingFilePreviewed_EngineFallsBackAndLogsTheMissingFile"` (AC2, AC6)
3. `SoundPlayerTests > "Volume_PerSoundTimesGlobal_MutedSilent"` und `"Play_MutedSound_NeitherRenderedNorQueuedNorRecorded"` (AC5)
4. `SettingsTests > "Sounds_PerEvent_RoundTrip_ResetDeletesCopy"` (AC4, AC5)
5. `UiSmokeTests`: eine Zeile je Sound mit allen Bedienelementen, beide Designs (AC1)
6. Manueller Check (offen, braucht Ohren): eine echte MP3-Datei auswählen und hören

Testbefehl: `dotnet test --filter "FullyQualifiedName~SoundImportTests|FullyQualifiedName~SoundTests|FullyQualifiedName~SoundPlayerTests|FullyQualifiedName~SettingsTests|FullyQualifiedName~UiSmokeTests"`

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
6. `SampleConversionTests > "ReadMono_StereoSignalOnOneChannel_KeepsFullLevel"` (AC5)
   - Gegeben: Float-Stereo, links Sinus 0,5, rechts 0 (und umgekehrt)
   - Erwartet: Mono-Spitze 0,5 statt 0,25
7. `SampleConversionTests > "ReadMono_FloatStereo_Averages"` bleibt grün: gleiche oder ähnlich laute Kanäle werden weiter gemittelt (AC5)
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
4. `MainViewModelTests > "UserVolume_ChangesMixer_SavesByFingerprint"` (AC2, AC3), `EngineSyncTests > "SyncGains_SavedVolumes_GiveOnlyTheSpeakersWithASavedVolumeTheirGain"` and `"SyncGains_VolumeSavedForOneFingerprint_GoesToThatSessionOnly"`, `EngineSyncWiringTests > "ShowServer_VolumesAppliedBefore_TableUsesThem"` and `"ShowServer_AnotherServerAfterOne_BuildsTheGainTableAgain"` (AC2, AC3)
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
   - Gegeben: `FakeAudioHardware.Listing`, das bis zur Freigabe blockiert, gespeichertes Eingabegerät "mic-2"
   - Erwartet: `OpenSettings` kehrt in unter 100 ms zurück, `Page` ist Settings, das Eingabefeld zeigt den Platzhalter, kein `DeviceHint`. Nach der Freigabe enthält `Inputs` die Geräte, "mic-2" ist ausgewählt.
2. `SettingsTests > "Devices_LoadedLater_HintOnlyIfReallyMissing_KeepsUserChoice"` (AC2, AC3)
   - Gegeben: `SettingsViewModel` ohne Liste, der Nutzer wählt "Standard", danach `ShowDevices` ohne das gespeicherte Gerät
   - Erwartet: Auswahl "Standard" bleibt. Ohne Nutzerauswahl erscheint der Hinweis "Gerät fehlt" erst nach `ShowDevices`.
3. `MainViewModelTests > "OpenSettings_DoesNotWaitForDeviceList"` (AC4), `DeviceListTests > "Refresh_BothDirectionsListed_ShowsThemOnceOnTheUiThreadAndRemembersThem"`, `"Refresh_ListingWaits_ReturnsBeforeTheListingIsDone"` and `"Refresh_NewerLoadStarted_DropsTheOlderList"`, `EngineSyncWiringTests > "OpenSettings_ListLoadedAtStart_ShowsInputsAndOutputsAtOnce"` (AC4)
   - Gegeben: erstes Öffnen mit Liste A geladen, zweites Öffnen mit blockierendem `FakeAudioHardware.Listing`
   - Erwartet: Liste A ist sofort da, nach der Freigabe die neue Liste
4. Bestehende Tests zu `SettingsViewModel.Resolve` bleiben grün, `Open` nutzt dieselbe Entscheidung über die ID (AC5), `EngineSyncTests > "Configure_SavedDeviceMissing_ReturnsTheFallbackWarning"` and `"ConfigureAsync_SavedDeviceMissing_ReturnsTheFallbackWarning"`, `EngineSyncWiringTests > "ApplySettings_SavedDeviceMissing_ShowsTheFallbackAsWarningNotice"` and `"ApplySettingsAsync_SavedDeviceMissing_ShowsTheFallbackAsWarningNotice"` (AC5)
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
4. `SettingsTests > "Sliders_ApplyLive"` (AC4)
   (Hinweis zur Referenz: Dieser Test prüft nur die sofortige Vorschau; das Zurückstellen beim Verwerfen prüft Test 5, siehe die Abweichungstabelle zu Package 53.)
   - Gegeben: `SettingsViewModel` mit `LivePreview`, Verstärkung und Schwelle ändern, danach verwerfen
   - Erwartet: `LivePreview` bekam die neuen Werte sofort, beim Verwerfen die gespeicherten
5. `MainViewModelTests > "SelfTest_MutesAndDeafens_RestoresPreviousState"` (AC2, AC4, AC5)
   - Gegeben: echter Testserver, verbunden, nicht stumm. Einstellungen öffnen, Selbsttest starten
   - Erwartet: der Server meldet stumm und taub. Nach "Selbsttest beenden" wieder nicht stumm und nicht taub. Zweiter Durchlauf: Start, dann Einstellungen schliessen, ebenso zurück. Dritter: Start, dann trennen, der Test ist aus.
   - Dieselben Regeln einzeln, ohne Server und Fenster: `SelfTestTests > "SetAsync_OnWithAServer_MutesAndDeafensAtTheServer"`, `"SetAsync_OffAfterOnWithAServer_PutsTheStateFromBeforeBackNotTheOneNow"` `"AbandonAsync_SelfTestRunning_PutsNothingBackAtTheServer"` (AC2, AC5) und `"AbandonAsync_SelfTestRunning_SwitchesTheEngineOff"` (AC5); die Verdrahtung: `SelfTestWiringTests > "ToggleSelfTest_ToggledAgain_EngineAndPageStop"`, `"ToggleSelfTest_ServerShown_MutesAndDeafensAtTheServer"`, `"DisconnectAsync_SelfTestRunning_EngineStopsPlayingBack"` (AC2, AC5), `"ToggleSelfTest_ToggledAgainOnAServerMutedBefore_ServerGetsThatStateBack"`, `"ClosePage_SelfTestRunning_ServerGetsTheStateFromBefore"` und `"DisconnectAsync_SelfTestRunning_SendsNoStateToTheServerThatIsGoing"` (AC5); nach einem Verbindungsabbruch: `ReconnectTests > "Reconnect_SelfTestRunningOnAMutedUser_BringsBackTheMuteFromBeforeIt"` (AC5)
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
4. `ChannelDialogViewModelTests > "Create_EmptyDefaults"`, `"Edit_Prefilled"`, `"Default_SlotsLocked"`, `"NameRequired_SlotsBounded_Trimmed"` (AC2 bis AC4)
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
2. `ReceivePathTests > "Mixer_Volume200_FourTimes_Limited"` (AC3)
   (Hinweis zur Referenz: Seit Package 128 (8ef64f1, Kurve der Lautstärkeregler) ergeben 200 % das Vierfache statt das Doppelte; der Test prüft das.)
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
3. `SoundPlayerTests > "Play_Deafened_OnlyOwnMicAndSoundTonesPlay"` um `LinkVoice` ergänzt (AC3)
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
4. `SoundTests > "Defaults_EveryEvent_ShortAudibleDistinct"`: `OwnLinkVoice` gleich `LinkVoice`, sonst weiter alle verschieden. `SoundPlayerTests > "Play_Deafened_OnlyOwnMicAndSoundTonesPlay"` um `OwnLinkVoice` ergänzt (AC3, AC4)
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
3. `MainViewModelTests > "Update_Accepted_ShowsProgress_UntilFailure"`, `UpdateFlowTests > "CheckAsync_OfferAcceptedAndInstalled_ShowsEveryReportThenClearsTheCardAndSaysStarting"`, `"CheckAsync_InstallFails_ReportsTheFailureOnEveryChannelAndClearsTheCard"`, `"CheckAsync_InstallThrows_ClearsTheCardAndLetsTheErrorThrough"`, `"IsUpdating_ProgressSet_IsTrueAndTellsTheBindings"`, `"IsUpdating_ProgressCleared_IsFalseAndTellsTheBindings"` (AC2, AC4)
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
7. Bestehender Test `UpdateTests > "Success_NewInPlace_OldRenamed_Restarted"` bleibt grün
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
4. Bestehende `ChatViewModelTests` und `UiSmokeTests > "Chat_FollowsNewLines_UnlessScrolledUp"` bleiben grün
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
5. `RestartScheduleTests > "AutoRestartChanged_ScheduleFollows"` (AC3)
   - Gegeben: `ManualTimeProvider`, Neustart aus, dann per Einstellung an für in 10 Minuten
   - Erwartet: nach 10 Minuten Zeitvorschub startet ein neuer Lauf. Ausschalten davor verhindert ihn.
6. `ServerLogWriterTests > "RetentionChangedAtRuntime_NextCleanupUsesIt"` (AC3)
7. `ServerSettingsViewModelTests > "Save_EveryLimitEdited_SendsTheEditedLimitsWithNameAndWelcomeTextAndNoPassword"` (AC1), bestehende `ServerSettingsViewModelTests > [Theory] "Save_PasswordFieldsSet_SendsTheTypedPasswordTheEmptyOneToRemoveItOrNone"` bleibt grün
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

1. `ModerationTests > "BanKnownUser_Offline_BlocksNextLogin"` (AC1): Gast verbindet, trennt, Moderator bannt per Fingerabdruck, Gast bekommt `Banned`
2. `ModerationTests > "BanKnownUser_OfflineWithIp_UsesLastIp"` (AC1)
3. `ModerationTests > "BanKnownUser_StrongerOrSelf_Denied"` (AC2)
4. `ModerationTests > "BanKnownUser_Online_Disconnected"` (AC2)
5. `ModerationTests > "DeleteKnownUser_RemovesRecordAndBans_RejoinsAsNew"` (AC4)
   - Gegeben: Nutzer in Gruppe Moderator mit Statistiken und einem abgelaufenen und einem aktiven Ban
   - Erwartet: kein `UserRecord`, keine Bans mit dem Fingerabdruck, erneuter Login als Gast mit neuem `FirstSeen`
6. `ModerationTests > "DeleteKnownUser_Online_KickedWithMessage"` (AC4)
7. `ModerationTests > "DeleteKnownUser_SelfLastAdminStronger_Denied"` (AC5)
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
6. `SoundPlayerTests > "Play_Deafened_OnlyOwnMicAndSoundTonesPlay"` bleibt grün (AC5)

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
7. `BackupCardViewModelTests > "Apply_TwoBackups_ShowsThemInTheServersOrderWithSizeAndHashDetails"`, `"Apply_EmptyListAfterTwoBackups_HasNoBackups"`, `"NewBackup_Executed_SendsCreateBackup"`, `"DeleteAndRestore_AnsweredNo_AskForTheBackupsTitleAndSendNothing"`, `"DeleteAndRestore_AnsweredYes_SendDeleteThenRestoreOfThatBackup"`, `AdminViewModelTests > [Theory] "RequestListsAsync_ByRights_AsksForTheBackupListOnlyWithBackupsManage"` (AC2, AC3)
8. `UiSmokeTests`, `LocalizationTests`, `SettingsTests > "ErrorTexts_EveryCodeHasText"` (AC6)

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
5. `BackupCardViewModelTests > "DownloadBackupAsync_BackupChosen_SendsTheDownloadRequestAndSavesItsArchive"`, `AdminTransferTests > [Theory] "Download_ChunksAnswered_FileInPlaceWithProgress"` (AC1, AC3)
6. `BackupCardViewModelTests > "UploadBackupAsync_RestoreNotWanted_SendsTheArchiveAsksNothingAndRestoresNothing"`, `[Theory] "UploadBackupAsync_RestoreWanted_AsksOnceAboutTheStoredBackupAndRestoresItOnlyWhenAnsweredYes"`, `"UploadBackupAsync_TwoUploads_SendEachArchiveUnderItsOwnUploadId"`, `"UploadBackupAsync_ServerRefusesTheArchive_AsksNothingRestoresNothingAndEndsTheTransfer"`, `AdminTransferTests > "UploadBackupAsync_ChunksAcknowledged_ReturnsTheStoredBackupWithProgress"`, `[Theory] "UploadBackupAsync_FileSize_RefusedOnlyOverTheLimit"`, `AnswerRoutingTests > "AdminPage_UploadStored_TheFollowingListShowsTheBackup"` (AC2, AC3)
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
6. `GroupListViewModelTests > "Commands_ViewRightOnly_NoCommandCanExecuteAndTheSelectedRowIsReadOnlyWithDisabledToggles"`, `"Commands_CreateRightOnly_NewGroupAllowedAndSavedGroupNotSavable"`, `"Commands_CreateRightOnlyDraftAdded_DraftSavableAndDeletable"`, `"Commands_ManageRightOnly_SavedGroupSavableAndMovableButNeitherNewNorDeletable"` und `"Commands_DeleteRightAndTheModeratorsRights_ModeratorDeletableButNotSavable"` (AC4): Neu, Speichern, Sortieren, Löschen, Nutzer-Checkboxen, Entbannen einzeln gesperrt
7. `GroupListViewModelTests > [Theory] "Toggles_FirstRowInTheCulture_OneUniqueLabelPerSingleRightWithTheNewRightsNamed"`, `LocalizationTests` (AC1, AC7): Checkbox-Namen aus `Perm_*` in beiden Sprachen

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
4. Bestehende `UiSmokeTests` zu Einstellungen (`UiSmokeTests > "Settings_SectionOrder"`, `UiSmokeTests > "Settings_PttHint_WithSetKeyButton"` usw.) bleiben grün

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
4. Bestehende `UiSmokeTests > "GroupList_DragAdminAboveGuest_SendsOrder"`, `LinkMatrix_*` und `AdminViewModelTests` bleiben grün

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

1. `ModerationTests > "BanSession_Created_StoresCreatedAtCreatorDuration"` (AC1)
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
6. `LogsViewModelTests > "DownloadAsync_TwoFilesSelected_SavesTheirZip"`, `AdminTransferTests > [Theory] "Download_ChunksAnswered_FileInPlaceWithProgress"`, `[Theory] "Download_ErrorForASecondChunk_OldFileKeptWithoutNotice"` (AC4)
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

- [x] AC1: A request with a null value in a non-nullable field, an unknown enum value or a malformed list is rejected with `InvalidValue`, and the connection stays open. No handler throws for any such input.
- [x] AC2: Unexpected exceptions inside a handler are logged (German server log line with the request type) and answered with `InvalidValue`. Only protocol violations on the frame level still close the connection.
- [x] AC3: Names (nickname, channel, group, server) reject control characters, Unicode format characters (category Cf: zero-width, bidi controls) and U+2028/2029, and are trimmed. Nickname uniqueness compares NFKC-normalized, case-insensitive names against online users AND the stored `LastNickname` of other users (so an offline admin's name cannot be taken by a new identity).
- [x] AC4: Multi-line texts (welcome text, channel description) allow `\n` but reject other C0/C1 control characters and bidi controls. Chat text allows `\n` and rejects the same set.
- [x] AC5: `EditChannel.Order` is ignored (order only changes through `ReorderChannels`) or validated to 0..channel count; `CreateChannel` never overflows. The server password is limited to 1..128 characters when set.
- [x] AC6: `SendChat` with a target outside the enum is rejected with `InvalidValue` and not counted against the rate limit.

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

- [x] AC1: `CanActOn` requires the target's rights to be a strict subset of the actor's rights. Two users with equal rights (for example two admins, or two moderators) cannot kick, ban, mute, move, regroup or delete each other.
- [x] AC2: No moderation action targets oneself on the server (`PermissionDenied`): Kick, Ban, BanUser, SetServerMute, MoveUser of another session id equal to one's own, AssignGroup/UnassignGroup on one's own fingerprint, DeleteUser. Moving oneself stays possible through `JoinChannel`.
- [x] AC3: Ban and BanUser can never hit the last member of the Admin group (`LastAdmin`), the same as DeleteUser and UnassignGroup.
- [x] AC4: AssignGroup and UnassignGroup require the actor to be able to act on the target (AC1) in addition to the existing group-subset rule.
- [x] AC5: Unban requires that the actor could ban that user now (rank rule against the banned user's stored rights) and replies only with an acknowledgement; the full `BanList` is sent only if the actor has `BansView`.
- [x] AC6: The client shows and enables exactly the actions the server would allow (same helper), so hidden buttons and server answers never disagree.

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

- [x] AC1: Setting or lifting a server mute is stored on the user's record and saved.
- [x] AC2: On login a stored mute is applied before the Welcome, so the user never sends voice in between; the other clients see the user as server-muted.
- [x] AC3: The user overview shows "vom Server stummgeschaltet" and, with `UserMute`, allows lifting it for offline users too (rank rule from Package 84 applies).
- [x] AC4: Deleting the user's data (Package 72) removes the mute.

### Tests (TDD)

1. `ModerationTests > "ServerMute_Persists_AcrossReconnect"` (AC1, AC2)
2. `ModerationTests > "ServerMute_NoVoiceBeforeWelcome"` (AC2): voice packets right after Hello are not relayed
3. `AdminViewModelTests > "UserCard_ShowsServerMute_LiftOffline"` (AC3)
4. `ModerationTests > "DeleteKnownUser_MutedUser_RemovesMute"` (AC4)

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
- `tests/OVS.Tests/Server/ServerTuningTests.cs` (new)

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

1. `ServerTuningTests > "RequestFlood_RateLimited_ThenDisconnected"` (AC1)
2. `ServerTuningTests > "ServerIcon_CachedAndThrottled"` (AC2)
3. `ServerTuningTests > "AdminTokenGuessing_Throttled_Logged"` (AC2)
4. `ServerTuningTests > "NoOpJoinAndSelfState_NoBroadcast"` (AC3)
5. `ServerTuningTests > "NonReadingClient_DisconnectedAndSlotFreed"` (AC4): a raw TLS client that never reads, flooding icon requests; the server stays under the byte cap and the slot is free within the timeout
6. `ServerTuningTests > "LongRequestId_Rejected"` (AC5)
7. `ServerTuningTests > "OverLimitConnection_ClosedBeforeTls"` (AC6)

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

- [x] AC1: The server accepts a UDP endpoint only if its address equals the session's control-connection address (IPv4 exact, IPv6 same /64). Other packets for that session are dropped and counted.
- [x] AC2: The UDP path looks up sessions without taking the global lock, and a per-source-address pre-filter drops more than 200 unauthenticated packets per second from one address before decryption.
- [x] AC3: The per-IP connection limit and IP bans group IPv6 addresses by /64 (the same helper as password throttling). Existing IPv6 bans keep matching their exact address and also their /64.

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
6. `BackupCardViewModelTests > "CanUploadRestore_AdminGroupMember_True"`, `"CanUploadRestore_AllRightsButNotInTheAdminGroup_False"`, `"Restore_BackupsManageWithoutTheAdminGroup_RowCannotRestoreAndSendsNothing"`, `"Restore_AdminGroupMember_RowCanRestoreAndTheRestoreIsSent"`, `"ConfirmDownloadAsync_WarningAnsweredNoThenYes_ReturnsEachAnswerAfterAskingEachTime"`, `"ConfirmDownloadAsync_NoWarningDialogRegistered_ReturnsFalse"`, `"UploadBackupAsync_OutsideTheAdminGroup_ReturnsWithoutSending"`, `AdminViewModelTests > [Theory] "ServerTab_OneCardRight_ShowsTheTabAndOnlyThatCard"`, `"OnServerStateChanged_AdminGroupLeft_BackupRowsLoseRestoreAndUploadRestore"` (AC2, AC5)
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

- [x] AC1: Console and file receive the same escaped line: CR, LF, U+2028/2029, other control characters and ESC (ANSI sequences) are shown escaped.
- [x] AC2: A chat message containing a newline followed by a fake timestamp produces exactly one log line on the console and in the file.
- [x] AC3: The search log line is written only when the search is actually run.

### Tests (TDD)

1. `ServerLogWriterTests > "ConsoleLine_EscapedLikeFile"` (AC1)
2. `ServerLogWriterTests > "ChatWithFakeLine_OneLineOnly"` (AC2)
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

- [x] AC1: New and changed passwords are stored as PBKDF2-SHA256 with a random 16-byte salt and at least 100,000 iterations, in a self-describing format (for example `pbkdf2$<iterations>$<salt>$<hash>`).
- [x] AC2: Verification stays constant-time and works for both formats; a successful login with an old SHA-256 hash replaces it with the new format and saves.
- [x] AC3: No password (empty) keeps meaning "no password".

### Tests (TDD)

1. `DataStoreTests > "StoredPassword_Pbkdf2_SaltedUnique"` (AC1): the same password twice gives different stored values that both verify
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

- [x] AC1: Users with `GroupsView` receive groups with permissions as today; everyone else receives groups with id, name and order only.
- [x] AC2: Other users' `UserInfo` no longer carries their permission bits. Instead each recipient gets per-target flags it needs: `CanBeModeratedByMe` (Package 84 rule) and `CanSpeakLinked` for the link indicator, plus the group ids for display. The own user still receives the full own permissions. (Implemented without `CanSpeakLinked`: the link indicator comes from the voice packet's via-link bit, the client never read others' `SpeakLinked`. Groups carry `AssignableByMe` and the user list `CanBeModeratedByMe`, so `UsersView` + `GroupsAssign` works without `GroupsView`.)
- [x] AC3: Flags are recomputed and sent when the recipient's or the target's rights change (the existing `RecomputePermissions` path).
- [x] AC4: The client's context menus, admin cards and indicators behave exactly as before for every combination covered by the existing tests.
- [x] AC5: Fingerprints stay in `UserInfo` (needed for private chat and user volume); this is documented as public data.

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

---

## Package 93: Channels locked to groups

**Goal:** A channel can be restricted to one or more groups, so only users with at least one of these groups (and admins) can join it, shown with a lock icon behind the channel name.

**Dependencies:** none

**Affected files:**
- `src/OVS.Server/Data/ServerData.cs` (change): `ChannelRecord.AllowedGroupIds` and `GroupLocked`
- `src/OVS.Server/Commands/ChannelCommands.cs` (change): `OnCreateChannel`, `OnEditChannel`, `OnJoinChannel`, `OnMoveUser`
- `src/OVS.Server/Commands/AdminCommands.cs` (change): deleting a group removes it from channel locks
- `src/OVS.Shared/Protocol/Messages.cs`, `Codes.cs` (change): `ChannelInfo`, `CreateChannel`, `EditChannel` with the group list; code `ChannelLocked`
- `src/OVS.Client/ViewModels/ChannelDialogViewModel.cs`, `src/OVS.Client/Views/ChannelDialog.axaml` (change): group selection
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (change): `ChannelViewModel.IsGroupLocked`, lock tooltip, join availability
- `src/OVS.Client/Views/MainWindow.axaml`, `SidebarWidth.cs` (change): lock icon behind the name, width fitting counts it
- `src/OVS.Client/ErrorTexts.cs`, `Localization/Strings.resx`, `Strings.en.resx` (change)
- `README.md` (change)
- `tests/OVS.Tests/Server/ChannelLockTests.cs` (new), `tests/OVS.Tests/Client/ServerViewModelTests.cs`, `ChannelDialogTests` (where the dialog view model is tested), `UiSmokeTests.cs`, `ResponsiveTests.cs`, `SidebarWidthTests.cs` (change)

### Context

`ChannelRecord` (ServerData.cs) has `Name`, `Description`, `Order`, `IsMuted`, `MaxUsers`. The only join restriction today is the user limit: `OnJoinChannel` and `OnMoveUser` refuse a full channel unless the actor has `ChannelJoinFull` (`IsFull`, ChannelCommands.cs). The default channel cannot get a limit (server check on `data.DefaultChannelId`, client `ChannelDialogViewModel.CanLimitUsers` for `ChannelDialogMode.EditDefault`). Admin-group members hold `Permission.All` and are exempt from restrictions. Since Package 92, every client receives group ids and names (without permission bits), so a client can show group names in a tooltip. The channel row shows home, mute and link icons (Packages 66, 67); `Icon.LockClosed` exists. Channel links route voice independent of who may join.

### Acceptance Criteria

- [x] AC1: Creating or editing a channel (rights `ChannelCreate`/`ChannelEdit`) can set a list of allowed groups (any existing groups, several at once). An empty list means no group lock. The server rejects unknown group ids (`InvalidValue`) and any group lock on the default channel (`InvalidValue`).
- [x] AC2: Joining a group-locked channel requires membership in at least one listed group; otherwise `ChannelLocked`. No right bypasses the group lock. Members of the Admin group can always join.
- [x] AC3: Moving another user into a group-locked channel is allowed only if the mover could join it by group themselves (A106); the moved user's groups are not checked. Admins can always move.
- [x] AC4: Adding, changing or removing a group lock does not move anybody out of the channel, and losing a group does not either; the lock only applies to later joins and moves.
- [x] AC5: Deleting a group removes it from every channel's list. If a list becomes empty that way, the channel stays locked and only admins can join (shown as "nur Admins" in the tooltip), until someone edits the lock.
- [x] AC6: Everyone sees locked channels as before (name, description, users, links unchanged). A lock icon stands behind the name next to the home and link icons; its tooltip names the allowed groups. The sidebar width fitting from Package 67 counts the icon.
- [x] AC7: The client offers "Beitreten" and double-click join only when the own groups allow it (or the user is an admin); a refused join still shows the server's error text. The channel dialog shows a checkbox list of groups under "Nur für Gruppen", disabled with a hint for the default channel.
- [x] AC8: Texts in German and English, README updated, the dialog and the icon fit from 360 px (A96).

### Tests (TDD)

1. `ChannelLockTests > "CreateEdit_GroupLock_Validated_NotOnDefault"` (AC1)
2. `ChannelLockTests > "Join_RequiresOneListedGroup_AdminAlways"` (AC2)
   - Given: channel locked to Moderator and Raid; users with Guest only, with Raid, with Moderator; an admin
   - Expected: Guest gets `ChannelLocked`, the others join
3. `ChannelLockTests > "Move_OnlyIfMoverQualifies_TargetNotChecked"` (AC3)
4. `ChannelLockTests > "LockChange_NobodyMovedOut"` (AC4)
5. `ChannelLockTests > "GroupDeleted_RemovedFromLocks_EmptyMeansAdminsOnly"` (AC5)
6. `ServerViewModelTests > "LockedChannel_IconTooltipAndJoinAvailability"` (AC6, AC7)
7. `SidebarWidthTests` extended with the lock icon (AC6)
8. Channel dialog view model test: group list, disabled for the default channel (AC7)
9. `ResponsiveTests.Dialogs_FitAt360` for the channel dialog with many groups, `LocalizationTests`, `SettingsTests > "ErrorTexts_EveryCodeHasText"` (AC8)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 to 5 (red): fields (optional, old data loads without a version bump), protocol, one server helper `CanEnterByGroup(session, channel)` used by join and move.
2. Tests 6 to 8 (red): client view models, icon, width constants, dialog.
3. Test 9, texts, README, headless screenshot of the sidebar and dialog at 360 and 1100 px.

### Out of Scope

- Password lock and its bypass right (Package 94)
- Hiding locked channels or their users (A108)

---

## Package 94: Channels locked with a password

**Goal:** A channel can additionally or alternatively be protected by a password, which users with the new right "Passwort-Lock umgehen" and admins do not need to enter.

**Dependencies:** Package 93 (shares the lock icon, dialog section and join checks)

**Affected files:**
- `src/OVS.Shared/Permissions/Permission.cs` (change): `ChannelPasswordBypass = 1 << 26`, `All = (1 << 27) - 1`
- `src/OVS.Server/Data/ServerData.cs` (change): `ChannelRecord.PasswordHash`
- `src/OVS.Server/Commands/ChannelCommands.cs` (change): password on create/edit, check on join, move refusal
- `src/OVS.Shared/Protocol/Messages.cs`, `Codes.cs` (change): `JoinChannel.Password`, `ChannelInfo.HasPassword`, `CreateChannel`/`EditChannel.Password`; codes `ChannelPasswordRequired`, `WrongChannelPassword`
- `src/OVS.Client/ViewModels/ChannelDialogViewModel.cs`, `src/OVS.Client/Views/ChannelDialog.axaml` (change): password field with "remove password"
- `src/OVS.Client/ViewModels/ServerViewModel.cs`, `src/OVS.Client/Views/SimpleDialogs.cs`, `src/OVS.Client/App.axaml.cs` (change): password prompt on join, remembered for the session
- `src/OVS.Client/ErrorTexts.cs`, `Localization/Strings.resx`, `Strings.en.resx` (change): texts and `Perm_ChannelPasswordBypass`
- `README.md` (change)
- `tests/OVS.Tests/Server/ChannelLockTests.cs`, `tests/OVS.Tests/Client/ServerViewModelTests.cs`, `AdminViewModelTests.cs` (group editor lists the new right), `ResponsiveTests.cs` (change)

### Context

After Package 93 channels can be group-locked. The server password is stored as salted PBKDF2 since Package 91 (`ServerSettings.Hash`/`Verify` in ServerData.cs, about 9 ms per hash, constant-time verification, 1..128 characters per Package 83). Server-password guessing is throttled per address. The client already has an in-window password prompt for the server password (`SimpleDialogs.AskPassword`) and the "set / remove password" pattern in the admin server settings (`UpdateServerSettings.Password`: null unchanged, "" remove). Permission bits are used up to `BackupsManage = 1 << 25`.

### Acceptance Criteria

- [x] AC1: Creating or editing a channel can set, change or remove a password (null = unchanged, empty = remove, 1..128 characters). It is stored as a salted PBKDF2 hash like the server password and never sent to clients; clients only see `HasPassword`. The default channel cannot get a password (`InvalidValue`).
- [x] AC2: Joining a password-locked channel requires the correct password (`ChannelPasswordRequired` without, `WrongChannelPassword` with a wrong one). Users with the new right "Passwort-Lock umgehen" (`ChannelPasswordBypass`) and Admin-group members join without it.
- [x] AC3: The bypass right does not bypass the group lock: a channel with both locks needs a listed group AND (the password or the bypass right). Admins are exempt from both.
- [x] AC4: Nobody can move another user into a password-locked channel (`ChannelPasswordRequired`), except Admin-group members (A107).
- [x] AC10: Admin-group members can move any user into any channel regardless of group lock, password lock and user limit, also all three at once (A110).
- [x] AC5: Wrong channel passwords are throttled: after 5 wrong attempts per session and channel within 5 minutes, further attempts get `RateLimited` for 5 minutes. Attempts are logged without the password.
- [x] AC6: Adding or changing a password does not move anybody out of the channel (A108 via Package 93 AC4).
- [x] AC7: The new right appears in the group editor; only Admin has it on new and existing servers (through `All`), other groups get nothing new.
- [x] AC8: Client: joining a password-locked channel without the bypass right asks for the password in an in-window dialog; a correct password is remembered in memory for this connection and not stored on disk. The lock icon's tooltip says "Passwort" (and the groups, if also group-locked). The channel dialog has a password field with "Passwort entfernen" when one is set.
- [x] AC9: Texts in German and English, README rights table and channel section updated; the prompt and dialog fit from 360 px.

### Tests (TDD)

1. `ChannelLockTests > "Password_SetChangeRemove_HashedNeverSent_NotOnDefault"` (AC1)
2. `ChannelLockTests > "Join_PasswordRequired_WrongRefused_BypassAndAdminJoin"` (AC2)
3. `ChannelLockTests > "BothLocks_BypassSkipsOnlyPassword"` (AC3)
4. `ChannelLockTests > "Move_IntoPasswordChannel_RefusedExceptAdmin"` (AC4)
4a. `ChannelLockTests > "Admin_MovesGuestIntoFullGroupAndPasswordLockedChannel"` (AC10)
   - Given: a channel locked to a group the guest lacks, with a password and a user limit that is reached; an admin who is not in the lock's groups
   - Expected: the admin moves the guest in; a moderator with `UserMove` and `ChannelJoinFull` is refused for the same move
5. `ChannelLockTests > "WrongPasswords_Throttled_NotLogged"` (AC5)
6. `ChannelLockTests > "PasswordChange_NobodyMovedOut"` (AC6)
7. `PermissionRulesTests`, `DataStoreTests` (only Admin gets the new bit), `GroupListViewModelTests > [Theory] "Toggles_FirstRowInTheCulture_OneUniqueLabelPerSingleRightWithTheNewRightsNamed"` (AC7)
8. `ServerViewModelTests > "JoinPasswordChannel_PromptsOnce_RememberedForSession"` (AC8)
9. `ResponsiveTests.Dialogs_FitAt360` with the prompt, `LocalizationTests`, `SettingsTests > "ErrorTexts_EveryCodeHasText"` (AC9)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 to 6 (red): field, protocol, checks in the shared join helper from 93, throttle per session and channel.
2. Test 7 (red): permission bit, labels.
3. Tests 8 and 9 (red): prompt via a new `Dialogs.AskChannelPassword`, per-connection memory in `ServerViewModel`, dialog field, texts, README; headless screenshot at 360 and 1100 px.

### Out of Scope

- Storing channel passwords on disk in the client
- A right that bypasses the group lock (explicitly not wanted)

---

## Package 95: The same logo at every size

**Goal:** The exe icon, the window and taskbar icon and the title bar show the same logo with the sound wave as the start screen, generated from `logo.svg` so they can never drift apart.

**Dependencies:** none

**Affected files:**
- `tools/render-icon/` (new): small console program or script that renders `src/OVS.Client/Assets/logo.svg` to all ICO sizes
- `src/OVS.Client/Assets/ovs.ico` (regenerated), `docs/logo.png` (regenerated)
- `src/OVS.Client/Views/TitleBar.axaml` (change): `LogoMark` without `ShowWave="False"`
- `src/OVS.Client/Views/LogoMark.axaml`, `LogoMark.axaml.cs` (change): thicker wave strokes below a size threshold
- `src/OVS.Client/Assets/logo.svg`, `website/public/logo.svg` (change): header comment
- `tests/OVS.Tests/Client/LogoTests.cs`, `UiSmokeTests.cs` (change)
- `README.md` (change): how to regenerate the icon

### Context

There is one design: a headset with a three-bar sound wave, white on a #2F6FEB rounded square (`Assets/logo.svg`, drawn in XAML by `Views/LogoMark.axaml` with identical path data). `ovs.ico` (exe icon via `ApplicationIcon` in `OVS.Client.csproj`, window icon via `MainWindow.axaml`) has frames 16, 24, 32, 48, 64 and 256 px; the 16 and 24 px frames were rendered once by hand without the wave (Package 28). The title bar uses `LogoMark` at 18 px with `ShowWave="False"`. That is why the taskbar, Explorer and title bar look different from the big logo on the start screen. There is no generator script.

### Acceptance Criteria

- [x] AC1: Every ICO frame (16, 24, 32, 48, 64, 256 px) shows the headset with the wave, rendered from `logo.svg` by the new tool; at 16 and 24 px the wave strokes are thickened just enough to stay visible (one pixel or more at 16 px).
- [x] AC2: The title bar shows the logo with the wave; below 32 px `LogoMark` uses the same thicker wave strokes as the small icon frames.
- [x] AC3: `docs/logo.png` equals the 256 px frame.
- [x] AC4: Running the tool twice gives byte-identical files; the README says how to run it after changing `logo.svg`.
- [ ] AC5 (manual): exe in Explorer, taskbar, window title bar and start screen show the same design at 100 % and 150 % display scaling. (open: needs a manual look on a real Windows desktop)

### Tests (TDD)

1. `LogoTests > "IcoFrames_ShowWave"` (AC1): decode each frame, the pixels in the wave area (known coordinates from the SVG, scaled) contain white pixels
2. `LogoTests > "IcoMatchesSvgRender"` (AC1, AC4): rendering `logo.svg` with the tool code at 64 px equals the 64 px frame within a small tolerance
3. `UiSmokeTests` (AC2): the title bar `LogoMark` has the wave visible
4. `LogoTests > "DocsLogo_Equals256Frame"` (AC3)
5. Manual check AC5

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 and 4 (red).
2. Tool: render the SVG paths with SkiaSharp (already available through Avalonia) or by rendering `LogoMark` headless, write a PNG-compressed ICO; thicker wave below 32 px.
3. Regenerate `ovs.ico` and `docs/logo.png`, tests green; title bar and `LogoMark` small-size strokes, test 3.
4. README, manual check.

### Out of Scope

- A new logo design

---

## Package 96: One drag and drop behaviour everywhere

**Goal:** Every reorder drag in the app shows a lifted, slightly tilted preview of the dragged item and a placeholder gap of the item's height where it will land.

**Dependencies:** none

**Affected files:**
- `src/OVS.Client/Views/ReorderDrag.cs` (new): shared drag controller (threshold, capture, preview, placeholder, target index)
- `src/OVS.Client/Views/MainWindow.axaml`, `MainWindow.axaml.cs` (change): channel tree uses it
- `src/OVS.Client/Views/AdminView.axaml`, `AdminView.axaml.cs` (change): group list uses it; logo drop zone highlight
- `src/OVS.Client/Styles/Controls.axaml` (change): preview and placeholder styles, `dropLine` removed
- `tests/OVS.Tests/Client/ReorderDragTests.cs` (new), `UiSmokeTests.cs` (change)

### Context

Two reorder drags exist, copy-pasted: the channel tree (`MainWindow.axaml.cs` around lines 74-131) and the group list (`AdminView.axaml.cs` around lines 34-90). Both start after a 6 px move, pick the target by comparing Y with the realized containers, and show only a 2 px accent line (`Border.dropLine`, bound to `IsDropAbove`/`IsDropBelow`); the dragged item stays in place unchanged, there is no pointer capture, no preview and no gap. The server-logo field in the admin Server tab accepts a file drop (OS drag, no hover highlight). Tests: `ChannelTree_DragRaidAboveLobby_SendsOrder`, `GroupList_DragAdminAboveGuest_SendsOrder`.

### Acceptance Criteria

- [x] AC1: While dragging, a preview of the dragged item follows the pointer in an overlay layer: scale 1.03, a shadow 8 px stronger (larger blur and offset) than the item's normal elevation, rotated 3°, fully opaque; the original item is dimmed in its slot.
- [x] AC2: Instead of the line, a placeholder gap with exactly the dragged item's height opens at the target position; the other items move aside. After the drop the item sits where the placeholder was, with the same height, so nothing jumps.
- [x] AC3: Esc or releasing outside the list cancels: preview and placeholder disappear, the order is unchanged and nothing is sent.
- [x] AC4: Both the channel tree and the group list use the shared controller; the old `dropLine` code is removed; the existing reorder requests (`MoveChannelAsync`, `MoveGroupAsync`) are sent exactly as before.
- [x] AC5: The logo drop zone highlights while a file is dragged over it (the OS drag preview itself is outside the app's control).
- [x] AC6: With the Windows setting for reduced animations the preview is not tilted and items jump instead of animating; scale and shadow stay. (the tests replace the Windows query `SPI_GETCLIENTAREAANIMATION`; the real setting is only checked by hand)

### Tests (TDD)

1. `ReorderDragTests > "Preview_Scale103_Tilt3_ShadowPlus8"` (AC1): start a drag headless, the preview control has `ScaleTransform` 1.03, `RotateTransform` 3 and the shadow values
2. `ReorderDragTests > "Placeholder_HasDraggedItemHeight_AtTarget"` (AC2): a channel with users (tall item) dragged above another; the gap's height equals the dragged container's height
3. `ReorderDragTests > "Drop_ItemTakesPlaceholderSlot_NoJump"` (AC2)
4. `ReorderDragTests > "EscOrOutside_Cancels_NothingSent"` (AC3)
5. existing `UiSmokeTests > "ChannelTree_DragRaidAboveLobby_SendsOrder"` and `UiSmokeTests > "GroupList_DragAdminAboveGuest_SendsOrder"` stay green (AC4)
6. `UiSmokeTests > "LogoDropZone_HighlightsOnDragOver"` (AC5)
7. `ReorderDragTests > "ReducedMotion_KeepsTilt_GapDoesNotSlide"` (AC6)
   (Note on the reference: since b2b7493 the preview keeps its tilt with reduced motion and only the gap does not slide; the test checks that, not "not tilted".)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 to 4 (red): `ReorderDrag` controller on an `ItemsControl`, preview in an overlay layer, placeholder by a margin on the container at the target index.
2. Move both lists onto it, remove the duplicated code and `dropLine`; tests 5.
3. Logo drop highlight, reduced motion; tests 6 and 7; headless screenshot of a drag in progress at 1100 px.

### Out of Scope

- Dragging users between channels (not a feature today)
- Drag and drop on touch screens

---

## Package 97: Waiting states, shared mechanism and admin page

**Goal:** A shared pending mechanism shows a visible reaction within 400 ms of every action that waits, and the administration page (lists, log search, backups) uses it.

**Dependencies:** none

**Affected files:**
- `src/OVS.Client/ViewModels/Pending.cs` (new): tracks pending requests, flips `IsBusy` after a short delay, times out
- `src/OVS.Client/Styles/Controls.axaml`, `src/OVS.Client/Views/Spinner.axaml` (new): small spinner and busy styles for buttons, rows and list areas (done as `Views/BusySpinner.axaml` with its own animation style: Avalonia already has a `Spinner` control)
- `src/OVS.Client/ViewModels/AdminViewModel.cs`, `LogsViewModel.cs`, `ListPages.cs` (change): loading state per list, search, backup create and restore
- `src/OVS.Client/Views/AdminView.axaml` (change)
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change)
- `tests/OVS.Tests/Client/PendingTests.cs` (new), `AdminViewModelTests.cs`, `LogsViewModelTests.cs`, `ResponsiveTests.cs` (change)

### Context

There is no shared busy component. Only connecting (`IsConnecting`, indeterminate bar) and file transfers (`IsTransferring`, percent bar) show progress. On the admin page, `RequestListsAsync` loads users, bans, backups and logs with no loading state (lists are just empty until they arrive); `ListRefresh` tracks `inFlight` internally but does not expose it. Log search (`LogsViewModel`) is fire-and-forget without feedback. Creating a backup shows nothing until the list changes; restoring shows nothing until the server disconnects the client.

### Acceptance Criteria

- [x] AC1: `Pending` marks an action busy when it starts; its UI (spinner on the triggering button or area, button disabled) becomes visible after 150 ms at the latest if the action is not done by then, so it is visible within 400 ms and fast actions do not flicker. When the answer (or an error) arrives, the busy state ends.
- [x] AC2: If no answer arrives within 10 s, the busy state ends with a visible error ("Keine Antwort vom Server") and the action can be retried.
- [x] AC3: Every admin list (users, bans, backups, logs) shows a loading state (spinner in the list area plus "Wird geladen ...") while its first page is on the way, and keeps showing the old content with a small spinner during a refresh.
- [x] AC4: Log search shows a spinner and "Suche läuft ..." until results arrive; the search button is disabled meanwhile.
- [x] AC5: "Backup anlegen" shows a spinner on the button until the new list arrives; "Wiederherstellen" shows a full-card "Wird wiederhergestellt ..." state until the disconnect.
- [x] AC6: Busy states fit at 360 px (A96); texts in German and English.

### Tests (TDD)

1. `PendingViewModelTests > "BusyVisibleWithin150ms_NotForFastActions"` (AC1): with a manual clock, an action answered after 100 ms never shows busy, one answered after 1 s shows busy from 150 ms on
2. `PendingViewModelTests > "NoAnswer_TimesOutWithError_Retryable"` (AC2)
3. `AdminViewModelTests > "Lists_ShowLoadingUntilFirstPage_RefreshKeepsContent"` (AC3)
4. `LogsViewModelTests > "Search_BusyUntilResult"` (AC4)
5. `BackupCardViewModelTests > "NewBackup_ClickedTwice_SendsOnceShowsBusyAfterTheDelayAndEndsWithTheNewList"`, `"Restore_ConfirmedThenRefused_StaysBusyThroughTheSafetyBackupAndEndsWithTheErrorText"` (AC5)
6. `ResponsiveTests.Admin_EveryTabFits` with busy states, `LocalizationTests` (AC6)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 and 2 (red): `Pending` with the dispatcher and an injectable time provider; match answers by request id (as `ServerViewModel.SendAsync` returns it).
2. Spinner control and styles.
3. Tests 3 to 5 (red): admin page and logs.
4. Test 6, texts, headless screenshots of the loading states.

### Out of Scope

- The other places (Package 98)

---

## Package 98: Waiting states everywhere else

**Goal:** Every other action that waits for the server shows a visible reaction within 400 ms.

**Dependencies:** Package 97 (shared `Pending` mechanism and spinner)

**Affected files:**
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (change): join channel, redeem token, reorder, moderation actions, link apply
- `src/OVS.Client/ViewModels/ChatViewModel.cs`, `src/OVS.Client/Views/ChatView.axaml` (change): pending chat messages
- `src/OVS.Client/ViewModels/ChannelDialogViewModel.cs`, `src/OVS.Client/Views/SimpleDialogs.cs`, `OverlayHost.cs` (change): dialogs stay open with a busy button until the server confirms
- `src/OVS.Client/ViewModels/AdminViewModel.cs` (change): group save, logo upload, user and ban actions
- `src/OVS.Client/ViewModels/SettingsViewModel.cs`, `MainViewModel.cs` (change): settings save while audio devices restart, update check
- `src/OVS.Client/Views/MainWindow.axaml`, `AdminView.axaml`, `SettingsView.axaml` (change)
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change)
- `tests/OVS.Tests/Client/ServerViewModelTests.cs`, `ChatViewModelTests.cs`, `AdminViewModelTests.cs`, `SettingsTests.cs`, `UiSmokeTests.cs`, `ResponsiveTests.cs` (change)

### Context

These actions send a request and show nothing until the server's state change arrives: join channel (`ServerViewModel`, `pendingJoin`), chat send (draft cleared at once, no pending message), channel create/edit dialogs, group save (a new group disappears and reappears), redeem token, logo upload, ban/kick/move/delete/mute, link matrix apply, channel and group reorder. Settings save closes synchronously while audio devices restart; the update check only changes a text.

### Acceptance Criteria

- [x] AC1: Joining a channel marks the target row with a small spinner until the own channel changes (or an error arrives); a second join request meanwhile is ignored.
- [x] AC2: A sent chat message appears at once in the history, greyed with a small clock, and turns normal when the server echo arrives; on error it shows "nicht gesendet" with a retry button and the text stays.
- [x] AC3: Dialogs that send (channel create/edit, ban, redeem token) keep their card open with a busy primary button until the server confirms, then close; errors show inside the dialog.
- [x] AC4: Group save, logo upload, user card actions (ban, unban, delete, lift mute, group toggles), link apply and reorder show a spinner on the triggering control until confirmed; reorder keeps the new order visible (from Package 96) and reverts with an error if refused. (A user card shows one spinner for whichever of its actions runs, beside its buttons, which are disabled meanwhile.)
- [x] AC5: Settings "Speichern" shows a busy button while audio devices restart and closes afterwards; the update check shows a spinner next to its text.
- [x] AC6: Every waiting action in the client has a visible reaction within 400 ms; a UI test walks through all of them with a fake server that answers after 1 s and asserts a visible busy indicator after 400 ms each.
- [x] AC7: Texts in German and English, everything fits at 360 px.

### Tests (TDD)

1. `ServerViewModelTests > "JoinChannel_RowBusyUntilMoved_SecondJoinIgnored"` (AC1)
2. `ChatViewModelTests > "SentMessage_PendingUntilEcho_ErrorRetry"` (AC2)
3. `UiSmokeTests > "SendingDialogs_StayOpenBusyUntilConfirmed"`, `DialogButtonsTests > [Theory] "ShowAsync_SendingDialog_SendsTheAnswerAndClosesWhenDone"`, `DialogFrameTests > "ShowAsync_SubmitRefusedThenConfirmed_ShowsTheErrorThenCloses"` (AC3)
4. `AdminViewModelTests > "CardActions_BusyUntilConfirmed"`, `ServerViewModelTests > "Reorder_RevertsOnError"` (AC4)
5. `SettingsTests > "Save_BusyWhileDevicesRestart"` (AC5), `EngineSyncTests > "ConfigureAsync_DeviceOpenWaits_ReturnsBeforeTheRestartIsDone"` and `"ConfigureAsync_NoDevices_AppliesAtOnceOnTheCallingThreadWithoutWarning"`, `MainViewModelCompositionTests > "ApplySettingsAsync_GivenHardware_RestartsDevicesOffTheCallingThread"`, `EngineSyncWiringTests > "ApplySettingsAsync_NoDevices_CompletesOnTheCallingThreadAndEngineTakesTheMode"` (AC5)
6. `UiSmokeTests > "EveryWaitingAction_VisibleWithin400ms"` (AC6)
7. `ResponsiveTests`, `LocalizationTests` (AC7)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 and 2 (red): join and chat.
2. Tests 3 and 4 (red): dialogs and admin actions.
3. Test 5 (red): settings and update check.
4. Test 6 as the catch-all, test 7, headless screenshots.

### Out of Scope

- Server-side changes (where an action has no explicit answer, the confirmation is the matching state change or error)

---

## Package 99: Display mode setting and motion switch

**Goal:** Users choose between "Vereinfachte Darstellung" and "Animierte Darstellung" in the settings, and the whole client follows that choice live.

**Dependencies:** none

**Affected files:**
- `src/OVS.Client/Settings/ClientSettings.cs` (change): `enum DisplayMode { Animated, Simplified }`, property `Display` (default `Animated`)
- `src/OVS.Client/Views/Motion.cs` (new):
  - `Motion.IsAnimated`
  - `Motion.Apply(Window, DisplayMode)` sets or clears the window class `animated`
  - the duration and easing tokens
  - `Motion.Duration(normal)`, which returns zero in simplified mode, for code-driven animations
- `src/OVS.Client/Styles/Motion.axaml` (new, still empty except for a header comment); `App.axaml` (change) includes it after `Controls.axaml`
- `src/OVS.Client/App.axaml.cs` (change): apply the mode at start and on `Settings` changes or live preview
- `src/OVS.Client/ViewModels/SettingsViewModel.cs`, `Views/SettingsView.axaml` (change): a "Darstellung" choice under "Design", with a live preview
- `src/OVS.Client/Styles/Controls.axaml` (change): the header comment points to `Motion.axaml` for the animated display
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change)
- `tests/OVS.Tests/Client/SettingsTests.cs`, `MotionTests.cs` (new), `UiSmokeTests.cs`, `LocalizationTests.cs`, `ResponsiveTests.cs` (change)

### Context

There's no display mode today. `Theme`, opacity and blur already show how an appearance setting flows: `ClientSettings` → `SettingsViewModel` (`LivePreview`, `ToSettings`) → `MainViewModel.Settings` and `Appearance` → `App.axaml.cs`. `Responsive.Apply` shows the window-class pattern that styles react to.

### Acceptance Criteria

- [x] AC1: `ClientSettings.Display` defaults to `Animated`. A settings file without the key loads as `Animated`. Saving and loading keeps `Simplified`. (found in Package 104: the saved look only counted after the next save; the main view model now takes it from the loaded settings at start, which also fixes the background opacity and blur of Package 61)
- [x] AC2: The settings section "DARSTELLUNG" has the choice "Darstellung" with "Animierte Darstellung" and "Vereinfachte Darstellung" (English: "Animated display" and "Simplified display"), with a one-line hint for each. Changing it applies at once (live preview); "Abbrechen" reverts it.
- [x] AC3: In animated mode the main window has the class `animated` and `Motion.IsAnimated` is true. In simplified mode neither is set, and `Motion.Duration(x)` returns `TimeSpan.Zero`. (applied by `MainWindow` when `MainViewModel.Appearance` changes, not in `App.axaml.cs`, so headless tests see it too; leaving the settings any way without saving now also ends the preview)
- [x] AC4: In simplified mode the client renders exactly as before this package. Headless frames of the home page, the connected server view, the settings and the admin page at 1100 px equal baselines captured before the change. (done as a comparison inside the test: each page in the simplified display against the same page rendered without `Motion.axaml`; checked-in PNGs would break on the new settings choice itself. The test also asserts that every selector in `Motion.axaml` starts with `Window.animated`.)
- [x] AC5: The texts exist in German and English, and the choice fits at 360 px.

### Tests (TDD)

1. `SettingsTests > "Display_DefaultsToAnimated_MissingKeyIsAnimated_SimplifiedRoundTrips"` (AC1)
   - Given: a fresh `ClientSettings`, a JSON file without `Display`, and a settings object with `Simplified` that is saved and loaded again.
   - Expected: `Animated`, `Animated`, `Simplified`.
2. `MainViewModelTests > "Display_LivePreviewAndCancelRevert"` (AC2)
   - Given: the settings page switches to Simplified.
   - Expected: `LivePreview` gets `Simplified`; after cancelling, the applied mode is `Animated` again.
3. `MotionTests > "Apply_SetsWindowClassAndDurations"`, `"Apply_TwoWindows_EachControlFollowsItsOwnWindow"` (AC3)
4. `UiSmokeTests > "Simplified_LooksLikeBefore"` (AC4)
   - Given: baselines in `tests/OVS.Tests/Client/Baselines/`, captured once from the current `main` before any change.
   - Expected: pixel-equal frames.
5. `LocalizationTests`, `ResponsiveTests.Settings_FitAt360_480_600_1100` (AC5)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Capture the baselines (test 4) on the unchanged code and commit them with the test.
2. Tests 1 and 2 (red): the enum, the property, the settings choice and the live preview.
3. Test 3 (red): `Motion.cs`, `Motion.axaml`, and the apply call in `App.axaml.cs`.
4. Test 5, then the texts.

### Out of Scope

- Any actual animation (Packages 100 to 108)
- Changing `ReorderDrag` (Package 96 stays as it is)

---

## Package 100: Hover, press and focus effects

**Goal:** In the animated display every interactive control reacts to hover, press and keyboard focus with a smooth effect.

**Dependencies:** Package 99

**Affected files:**
- `src/OVS.Client/Styles/Motion.axaml` (change)
- `tests/OVS.Tests/Client/MotionStyleTests.cs` (new)

### Context

`Controls.axaml` styles rows (`Border.row`, with a 100 ms background brush), rings, chips, button variants, icons, the sidebar, bookmarks, admin cards and settings controls. Hover today only changes a background colour. Avalonia transitions (`TransformOperationsTransition`, `DoubleTransition`, `BoxShadowsTransition`, `BrushTransition`) can be set as style setters under `Window.animated`.

### Acceptance Criteria

- [x] AC1: Buttons (all variants), icon buttons and chips:
  - hover: lift by 1 px with a soft shadow and a brighter background, over `Motion.Fast`
  - press: scale to 0.96 and spring back with `BackEaseOut`
  - (the title-bar window buttons only fade their colour, link-like `Button.hint` keeps its underline; the only chip, the ping, lifts on hover)
- [x] AC2: Rows (channels, users, bookmarks, admin list rows, sound rows):
  - hover: a background fade plus an accent bar of 3 px that grows in from the left
  - the current channel's accent bar stays visible
  - (the bar is an inset box shadow, so no row template changed; admin list rows are the `ListBoxItem`s of the group, log file and search hit lists, a selected one keeps the bar; the dense log lines get no bar; user, ban and backup entries are cards and get their effects in Package 101 and 102; sound rows rest on the hover colour and light up one step further)
- [x] AC3: Keyboard focus shows an animated focus ring (fade plus 2 px grow) on every focusable control; it never appears for pointer focus. (a `FocusAdornerTemplate` set on every control in the animated display; Avalonia shows the adorner only for keyboard focus)
- [x] AC4: Inputs, combo boxes, sliders, check boxes and toggle switches:
  - the border colour fades on hover and focus
  - check marks and toggle knobs animate their change
  - slider thumbs grow on hover
  - (the frame of `NumericUpDown` fades through its `ButtonSpinner`; no view uses a `ToggleSwitch` today, its knob only grows on hover beyond Fluent's own movement)
  - tabs (administration, chat) lift their label on hover; menu and dropdown entries fade their background
- [x] AC5: Icons in icon buttons turn or nudge where it carries meaning:
  - the settings cog turns 30° on hover
  - the close X turns 90°
  - the arrows nudge in their direction
  - (the icons carry the classes `spin`, `turn`, `nudgeUp`, `nudgeDown`, `nudgeRight`, and `sync` for the reload arrows, which turn 180°)
- [x] AC6: In simplified mode none of these effects exist (`Simplified_LooksLikeBefore` stays green, and no new transitions are set on the controls).

### Tests (TDD)

1. `MotionStyleTests > "Button_Animated_HasHoverAndPressTransitions_SimplifiedHasNone"` (AC1, AC6): a headless window in each mode; read `Transitions` and the `:pressed` `RenderTransform` of a button.
2. `MotionStyleTests > "Row_HoverAccentBarGrows"` (AC2): hover a row, tick the render timer and assert the width of the accent bar over time (0 → 3).
3. `MotionStyleTests > "FocusRing_KeyboardOnly"` (AC3)
4. `MotionStyleTests > "Inputs_HaveStateTransitions"` (AC4)
5. `MotionStyleTests > "IconButtons_TurnOnHover"` (AC5)
6. `UiSmokeTests.Simplified_LooksLikeBefore` (AC6)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 and 6 (red): the button styles in `Motion.axaml`.
2. Tests 2 to 5 (red): rows, focus, inputs and icons.
3. Headless screenshots of hover and press at 1100 px, in light and dark.

### Out of Scope

- Lists changing (Packages 101 to 103)

---

## Package 101: Items appear animated

**Goal:** In the animated display every new item (user, channel, bookmark, admin row) fades and slides into its list.

**Dependencies:** Package 99

**Affected files:**
- `src/OVS.Client/Views/ItemMotion.cs` (new): attached property `ItemMotion.Enter="True"` for an `ItemsControl`. When a container is prepared for an item added after the first layout, it plays the enter animation: opacity 0 → 1, translate Y 6 → 0, height grows from 0, over `Motion.Normal`, with a stagger of 30 ms per item and at most 8 items per batch.
- `src/OVS.Client/Views/MainWindow.axaml`, `AdminView.axaml`, `SettingsView.axaml` (change): set it on the channel tree, the users per channel, bookmarks, admin lists (users, bans, groups, backups, logs, links) and sound rows
- `src/OVS.Client/Styles/Motion.axaml` (change): a "new" highlight flash for joined users and created channels
- `tests/OVS.Tests/Client/ItemMotionTests.cs` (new)

### Context

`ServerViewModel.Sync` inserts new instances; `AdminViewModel` uses `Clear` plus `Add`. A view-side enter animation reacts to `ContainerPrepared`. The first fill of a list must not animate item by item: a list that loads shows as one fade, and a rebuild with `Clear` plus `Add` must not replay rows that were already there.

### Acceptance Criteria

- [x] AC1: A user who joins my view of a channel slides in under the channel; the row briefly flashes in the accent colour (once, 600 ms).
- [x] AC2: A newly created channel slides in at its position; the rows below it make room smoothly instead of jumping.
- [x] AC3: Bookmarks, admin rows and sound rows that are added slide in the same way. (rebuilt lists (bookmarks, users, bans, backups, groups, log files, link rows) name a stable key with `IMotionKey`; the key bindings in the settings slide in too)
- [x] AC4: The first fill of a list, and an admin rebuild with `Clear` plus `Add` where the item keys are unchanged, do not replay the enter animation for existing items; the whole list fades in once on the first fill. (also: an empty administration list that gets its first page from the server fades in as a whole, and entries that come while a list is hidden, such as the channel tree in the closed drawer, do not slide in when it shows; the channel tree's own first fill just shows, because a moved channel gets a new user list and connecting is Package 106)
- [x] AC5: With more than 8 items added at once, only the first 8 are staggered; the rest appear with the last batch, so a full list never takes longer than 8 × 30 ms + `Motion.Normal`.
- [x] AC6: In simplified mode items appear at once, as today.

### Tests (TDD)

1. `ItemMotionTests > "NewUser_SlidesInAndFlashes"` (AC1): a headless channel tree; add a user to the mirror and tick the timer; the container's opacity and translate go from (0, 6) to (1, 0).
2. `ItemMotionTests > "NewChannel_SlidesIn_RowsBelowMoveSmoothly"` (AC2)
3. `ItemMotionTests > "AdminRowAdded_SlidesIn"` (AC3)
4. `ItemMotionTests > "FirstFillAndRebuild_NoReplay"` (AC4)
5. `ItemMotionTests > "ManyItems_StaggerCapped"` (AC5)
6. `ItemMotionTests > "Simplified_AppearsAtOnce"` (AC6)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 and 6 (red): `ItemMotion` on the user lists.
2. Tests 2 to 5 (red): channels, admin lists, first fill, rebuild keys (by the row's id) and the stagger cap.
3. Wire it into all lists; take headless screenshots mid-animation.

### Out of Scope

- Removal (Package 102); chat entries (Package 107)

---

## Package 102: Items leave animated

**Goal:** In the animated display every removed item collapses and fades out of its list before it disappears.

**Dependencies:** Package 99

**Affected files:**
- `src/OVS.Client/ViewModels/CollectionSync.cs` (new): `ServerViewModel.Sync` moves here as a shared `CollectionSync.Sync(target, desired, leave)`.
  - With a leave delay, a removed item gets `ILeaving.IsLeaving = true` and stays in the collection until the delay has passed, then it is removed.
  - An item that comes back meanwhile is revived (same instance, `IsLeaving = false`).
  - The delay uses a `TimeProvider` and the UI `post`, like `Pending`.
- `src/OVS.Client/ViewModels/ServerViewModel.cs`, `AdminViewModel.cs`, `MainViewModel.cs` (bookmarks) (change): use it; `ChannelViewModel` and `UserViewModel` implement `ILeaving`
- `src/OVS.Client/Styles/Motion.axaml` (change): `:is(Control).leaving` collapses the height to 0, fades the opacity to 0 and moves the item 8 px to the left, over `Motion.Normal`; leaving rows are not hit-testable
- `src/OVS.Client/Debug/DebugApi.cs` (change): leaving items are not reported
- `tests/OVS.Tests/Client/CollectionSyncTests.cs` (new), `ServerViewModelTests.cs`, `UiSmokeTests.cs` (change)

### Context

`Sync` removes rows from the end at once (`ServerViewModel.cs:290`), so there's nothing left to animate. Everything that reads `Channels` or `Users` (commands such as `MoveUp` and `MoveDown`, `PickChannel` targets, `SlotText`, `DebugApi`, tests) must ignore leaving items.

### Acceptance Criteria

- [x] AC1: In animated mode, a user who leaves my view of a channel (disconnect, switch away, kick) collapses and fades out over `Motion.Normal` and is removed after that. (as built: leaving is kept per list in `CollectionSync` (`IsLeaving(list, item)`, `Live()`, the event `LeavingChanged`) instead of an `ILeaving` flag on the item, because a user who switches channels is the same view model leaving one list and entering another; the fold is played from code by `ItemMotion`, like the appear animation, not by a `.leaving` style. The delay sits in a `LeaveTimer` per connection and one for the bookmarks; it is zero unless the window sets `Motion.Normal`, so view models without a view behave as before)
- [x] AC2: A deleted channel collapses with its users; the rows below move up smoothly.
- [x] AC3: Removed bookmarks and admin rows (unban, delete user, delete backup, remove group) leave the same way.
- [x] AC4: An item that comes back while leaving is the same instance again (no double row, no flicker).
- [x] AC5: Leaving items are excluded from commands, counts (`SlotText`), move targets, `DebugApi` output and selection; they cannot be clicked.
- [x] AC6: In simplified mode items are removed at once, as today; all existing `ServerViewModelTests` and `UiSmokeTests` stay green in both modes.

### Tests (TDD)

1. `CollectionSyncTests > "Removed_MarkedLeaving_RemovedAfterDelay"` (AC1): use a fake `TimeProvider`.
2. `CollectionSyncTests > "ReturnsWhileLeaving_SameInstanceRevived"` (AC4)
3. `CollectionSyncTests > "NoDelay_RemovedAtOnce"` (AC6)
4. `CollectionSyncTests > "Moves_IgnoreLeavingItems"`: the order is correct when the desired items and leaving items are mixed.
5. `ServerViewModelTests > "LeavingUser_NotCountedNotTarget_LeavingChannelSkipped"`, `DebugApiTests > "LeavingUser_NotInState"` (AC5)
6. `ItemMotionTests > "DeletedChannel_CollapsesThenGone"` (AC2), `"AdminRowRemoved_Leaves_AndComesBackWhole"` (AC3), `BookmarkListTests > "Refresh_RemovedBookmarkWithLeaveDelay_StaysLeavingThenFoldsAway"` (AC3)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 to 4 (red): `CollectionSync`; move `ServerViewModel.Sync` into it without changing behaviour (the old tests stay green).
2. Test 5 (red): the exclusions in `ServerViewModel` and `DebugApi`.
3. Test 6 (red): the styles and the admin lists and bookmarks.

### Out of Scope

- Animating the move to another channel itself (Package 103)

---

## Package 103: Items move animated

**Goal:** In the animated display items that change position or channel glide to their new place.

**Dependencies:** Packages 101 and 102

**Affected files:**
- `src/OVS.Client/Views/ItemMotion.cs` (change): attached `ItemMotion.Flip="True"`. Before a collection change it records the positions of the realized containers; after the layout it sets a `TranslateTransform` from the old offset and animates it to 0 over `Motion.Slow` (the FLIP technique).
- `src/OVS.Client/Views/FlyGhost.cs` (new): a snapshot of a row flies in the overlay layer from its old to its new screen position. It reuses `ReorderDrag.Snapshot`, which becomes `internal static`.
- `src/OVS.Client/Views/MainWindow.axaml`, `MainWindow.axaml.cs`, `AdminView.axaml` (change)
- `tests/OVS.Tests/Client/ItemMotionTests.cs` (change), `ReorderDragTests.cs` (stays green)

### Context

Reordering by another admin, or a move confirmed by the server, arrives as a `Sync` `Move` and the rows jump. A user who switches channel is removed from one `Users` list and inserted into another. Package 96's drag already has its own placeholder; after the drop the server order arrives and must not cause a second jump.

### Acceptance Criteria

- [x] AC1: A channel reorder from the server glides: every moved row slides from its old to its new position over `Motion.Slow`. (as built: Avalonia gives a moved entry a new row at once, so a `Flip` list notes every row's layout position after each layout pass and compares against that; users within a channel glide too)
- [x] AC2: A user who switches channel (themselves, moved by someone, or by a link) flies as a ghost from the old row to the new one. The old slot collapses (Package 102) and the new slot opens (Package 101); at the end the real row flashes once.
- [x] AC3: My own switch also gives the current-channel highlight a sliding move to the new channel.
- [x] AC4: Group reorder in Administration glides the same way.
- [x] AC5: After a drag drop (Package 96) the confirming server order causes no second jump or glide.
- [x] AC6: If the old or new position is scrolled out of view, the item only appears or leaves (no ghost across the screen edge).
- [x] AC7: In simplified mode rows jump as today.

### Tests (TDD)

1. `ItemMotionTests > "ServerReorder_RowsGlideFromOldOffset"`, `PlaceTrackerTests > "Move_FlipWithoutEnter_MovedRowsGlideFromTheirOldPlaces"` (AC1): the `TranslateTransform` right after the change equals the old offset and is 0 after the duration.
2. `ItemMotionTests > "UserSwitch_GhostFliesOldToNew_LandsOnTheRow"` (AC2): the overlay contains a ghost whose start and end positions match both rows.
3. `ItemMotionTests > "OwnSwitch_CurrentHighlightSlides"` (AC3)
4. `ItemMotionTests > "GroupReorder_Glides"` (AC4)
5. `ReorderDragTests > "DropThenServerOrder_NoSecondMove"`, `PlaceTrackerTests > "Hold_MoveRightAfter_NoRowGlides"`, `"Hold_MoveAfterTheHoldEnded_RowsGlide"` (AC5)
6. `ItemMotionTests > "OffscreenEnd_NoGhost"` (AC6)
7. `ItemMotionTests > "Simplified_Jumps"` (AC7)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1, 4 and 7 (red): FLIP on the channel tree and the group list.
2. Tests 2, 3 and 6 (red): `FlyGhost` with the shared snapshot.
3. Test 5 (red); headless screenshots mid-flight.

### Out of Scope

- Dragging users between channels (still not a feature)

---

## Package 104: Page and tab transitions

**Goal:** In the animated display switching pages and tabs cross-fades and slides.

**Dependencies:** Package 99

**Affected files:**
- `src/OVS.Client/Views/MainWindow.axaml` (change): the Home, Settings and Admin panels become one `TransitioningContentControl` with a page selector. Instances are kept, so the state isn't lost.
- `src/OVS.Client/Views/PageMotion.cs` (new): a transition that is a slide of 24 px plus a fade over `Motion.Slow`. Settings and Admin enter from the right; going back to Home reverses it. In simplified mode it is `null` (instant).
- `src/OVS.Client/Views/AdminView.axaml`, `ChatView.axaml`, `SettingsView.axaml` (change): admin tab content and chat tabs cross-fade; the tab underline slides to the selected tab; settings sections reveal on scroll (a fade-up as they come into view)
- `tests/OVS.Tests/Client/PageMotionTests.cs` (new), `UiSmokeTests.cs`, `ResponsiveTests.cs` (change)

### Context

`MainViewModel.Page` switches panels by `IsVisible` (`MainWindow.axaml` lines 321 to 414), and `IsVisible = false` cannot fade out. Pages keep their view models (`SettingsPage`, `AdminPage`). The admin `TabControl` and the chat tabs switch content at once.

### Acceptance Criteria

- [x] AC1: Opening Settings or Administration slides and fades the page in from the right while the previous page fades out; closing goes the reverse way. The duration is `Motion.Slow`. (as built: the pages stay the three panels they were; just before the switch a picture of the leaving page is taken (`PropertyChanging`) and slides away in the overlay layer while the new page comes in, so no page loses state and the simplified display is untouched; the tab mark flies like the highlight of Package 103; the picture is taken at the first change of the page or its model, so it shows the page as it was, and lies in its own layer right above the pages, clipped to the main area, below the drawer and the dialogs)
- [x] AC2: Page state is kept across transitions (scroll position, inputs, the selected admin tab), exactly as today.
- [x] AC3: Switching admin tabs cross-fades the content and slides the selection indicator; switching chat tabs does the same. (as built: the new content fades and slides in from the side of the new tab while the old one goes at once; the mark flies only when both tabs are in view of the scrolling chat tab strip and takes the new tab's width on the way)
- [x] AC4: Settings sections fade up once as they first scroll into view.
- [x] AC5: Fast double switches (Settings → Home → Admin within 100 ms) end on the right page with no leftover half-faded page.
- [x] AC6: In simplified mode everything switches instantly, as today; `ResponsiveTests` stay green in both modes. (the layout checks measure layout positions, not where a running animation draws a control; the simplified display adds nothing to layout)

### Tests (TDD)

1. `PageMotionTests > "OpenSettings_SlidesInFromRight_CloseReverses"` (AC1)
2. `PageMotionTests > "PageSwitch_KeepsState"` (AC2)
3. `PageMotionTests > "AdminAndChatTabs_FadeIn_MarkFlies"` (AC3)
4. `PageMotionTests > "SettingsSections_FadeUpOnce"` (AC4)
5. `PageMotionTests > "RapidSwitch_EndsClean"` (AC5)
6. `PageMotionTests > "Simplified_Instant"`, `"Reveal_SwitchToSimplified_WaitingSectionShowsAtOnce"`, `ResponsiveTests` (AC6)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1, 2, 5 and 6 (red): the pages in `MainWindow`.
2. Tests 3 and 4 (red): tabs and sections.
3. Headless screenshots mid-transition at 1100 px and 360 px.

### Out of Scope

- The switch between home and the connected server view (Package 106)

---

## Package 105: Dialogs, popups and drawer

**Goal:** In the animated display dialogs, menus, dropdowns, tooltips and the compact sidebar drawer open and close animated.

**Dependencies:** Package 99

**Affected files:**
- `src/OVS.Client/Views/OverlayHost.cs` (change):
  - the scrim fades in
  - the card scales from 0.94 to 1 with a fade over `Motion.Normal` (`BackEaseOut`)
  - closing reverses it, and the content is released after the animation
  - a queued dialog starts after the previous one has closed
  - a refused input shakes the card (3 × 6 px)
- `src/OVS.Client/Styles/Motion.axaml` (change): context menus, flyouts, combo box dropdowns and tooltips fade and slide 4 px from their anchor
- `src/OVS.Client/Views/MainWindow.axaml.cs`, `SidebarWidth.cs` (change): the compact drawer slides in from the left over `Motion.Normal` with its scrim; closing slides out
- `tests/OVS.Tests/Client/OverlayMotionTests.cs` (new), `UiSmokeTests.cs` (change)

### Context

`OverlayHost` toggles `IsVisible` and serializes dialogs with a semaphore. Error states inside dialogs come from Package 98, which keeps dialogs open on errors. Popups are the Fluent defaults. The drawer appears below `Responsive.CompactBelow`.

### Acceptance Criteria

- [x] AC1: Every dialog opens with a scrim fade plus a card pop, and closes in reverse; focus and the Enter and Esc handling work as before, already during the animation. (closing counts at once: the answer, keys and clicks; only the picture plays back)
- [x] AC2: A second dialog waiting its turn starts only after the first has finished closing; there are never two cards at once. (a dialog asked for while the last one folds away ends that fold at once instead of waiting)
- [x] AC3: A refused input in a dialog (server error, wrong password) shakes the card once.
- [x] AC4: Context menus, flyouts, dropdowns and tooltips fade and slide from their anchor. (one handler on every popup; they close at once, as Avalonia takes popups away)
- [x] AC5: In compact width the drawer slides in and out with its scrim; a click beside it closes it, animated. (closed counts at once for keys and clicks; the test lives in `OverlayMotionTests`, and the layout tests wait for the slide)
- [x] AC6: In simplified mode everything appears instantly; all existing dialog tests stay green in both modes.

### Tests (TDD)

1. `OverlayMotionTests > "Open_ScrimFadesCardPops_CloseReverses"` (AC1)
2. `OverlayMotionTests > "Queued_StartsWhenTheLastHasGone_NeverTwoCards"` (AC2)
3. `OverlayMotionTests > "Refused_ShakesCard"` (AC3)
4. `OverlayMotionTests > "Popups_FadeAndSlide"` (AC4)
5. `OverlayMotionTests > "Drawer_SlidesInAndOut"` (AC5)
6. `OverlayMotionTests > "Simplified_Instant"` plus the existing dialog tests (AC6)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1, 2 and 6 (red): `OverlayHost`.
2. Tests 3 to 5 (red): the shake, popups and the drawer.
3. Headless screenshots of a dialog mid-pop.

### Out of Scope

- Dialog content and its texts

---

## Package 106: Connect and disconnect

**Goal:** In the animated display connecting builds the server view up step by step, and disconnecting folds it away.

**Dependencies:** Packages 101 and 104

**Affected files:**
- `src/OVS.Client/Views/MainWindow.axaml`, `MainWindow.axaml.cs` (change)
- `src/OVS.Client/Styles/Motion.axaml` (change)
- `tests/OVS.Tests/Client/ConnectMotionTests.cs` (new)

### Context

The home panel shows the `LogoMark` and an indeterminate `ProgressBar` while connecting (`MainWindow.axaml:326` and `:329`). `IsConnected` switches the sidebar header, the channel tree and the main area by `IsVisible` (lines 63, 97, 173 and 346).

### Acceptance Criteria

- [x] AC1: While connecting, the logo's sound wave pulses and the progress bar is replaced by a slim animated bar; a failed connect shakes the home card once and shows the error with a fade. (the whole logo breathes; the bar is the Fluent indeterminate bar made 3 px high; a refused or cancelled connect counts as failed too, its reason is always shown)
- [x] AC2: On connect: (as built: home and the bookmarks leave as pictures (the mechanism of Package 104, now in any direction) while the chat comes up from below; the tree builds up once it is laid out visible, only the top-level tree, not a moved channel's user list)
  - the home content fades out
  - the sidebar header and the server name fade in
  - the channel tree builds up channel by channel (the stagger from Package 101, capped)
  - the chat area slides up from below
  - the whole sequence takes at most 700 ms
- [x] AC3: On disconnect (by the user, kicked, or lost): the tree and chat fold away (fade plus collapse), the home page returns with the reason; with a lost connection the server name grey-pulses while reconnecting. (the client has no automatic reconnect, so there is no reconnecting state to pulse; the server folds away downwards as a picture and home shows the reason)
- [x] AC4: Switching directly between two servers (bookmark click while connected) plays disconnect then connect without a blank frame.
- [x] AC5: In simplified mode everything switches instantly, as today.

### Tests (TDD)

1. `ConnectMotionTests > "Connecting_PulsesAndFailShakes"` (AC1)
2. `ConnectMotionTests > "Connect_SequenceWithin700ms"` (AC2): a fake server; tick the timer and assert the order and the end state at 700 ms.
3. `ConnectMotionTests > "Disconnect_FoldsAwayHomeWithReason"` (AC3)
4. `ConnectMotionTests > "ServerSwitch_NoBlankFrame"` (AC4)
5. `ConnectMotionTests > "Simplified_Instant"` (AC5)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 2 and 5 (red): the connect sequence.
2. Tests 1, 3 and 4 (red).
3. Headless screenshots of the sequence.

### Out of Scope

- The connection logic itself (unchanged)

---

## Package 107: Chat animations

**Goal:** In the animated display chat messages, notices and the send state animate.

**Dependencies:** Package 101

**Affected files:**
- `src/OVS.Client/Views/ChatView.axaml`, `ChatView.axaml.cs` (change)
- `src/OVS.Client/Styles/Motion.axaml` (change)
- `tests/OVS.Tests/Client/ChatMotionTests.cs` (new)

### Context

`ChatView.axaml` lists `Selected.Entries` in an `ItemsControl` (`chatHistory`). Pending messages are greyed with a clock until the echo arrives (Package 98). Notices (`NoticeKind`) appear in "Allgemein". The view scrolls to the end on new entries.

### Acceptance Criteria

- [x] AC1: A new message slides up and fades in. My own messages come from the composer side; other people's messages come from the left.
- [x] AC2: Scrolling to the newest message is smooth (`Motion.Normal`) instead of jumping; if the user has scrolled up, nothing scrolls and a "new messages" pill bounces in. (the pill exists only in the animated display; scrolling during a glide stops the glide)
- [x] AC3: A pending message turns from grey to normal with a fade, and the clock morphs into a check that fades out; "nicht gesendet" shakes once. (as built: the clock goes and a check appears in its place, then fades)
- [x] AC4: Warning and error notices slide in with a short colour pulse on their left edge.
- [x] AC5: An unread badge on a chat tab pops in, and its count ticks.
- [x] AC6: Loading the history of a tab does not animate every entry (one fade for the whole history).
- [x] AC7: In simplified mode the chat behaves as today.

### Tests (TDD)

1. `ChatMotionTests > "NewMessage_SlidesIn_OwnFromComposer"` (AC1)
2. `ChatMotionTests > "ScrolledUp_NoAutoScroll_PillAppears"` (AC2)
3. `ChatMotionTests > "Pending_FadesToSent_WithACheck"`, `"NotSent_Shakes"` (AC3)
4. `ChatMotionTests > "Notice_SlidesInWithPulse"` (AC4)
5. `ChatMotionTests > "UnreadBadge_PopsIn"` (AC5)
6. `ChatMotionTests > "HistoryLoad_OneFade_NotLineByLine"` (AC6)
7. `ChatMotionTests > "Simplified_AsToday"` (AC7)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1, 6 and 7 (red).
2. Tests 2 to 5 (red).
3. Texts for the pill in German and English; take headless screenshots.

### Out of Scope

- New chat features (reactions, typing indicators)

---

## Package 108: Live status animations

**Goal:** In the animated display speaking, mute, deafen, slots, busy and level states animate continuously and smoothly.

**Dependencies:** Package 99

**Affected files:**
- `src/OVS.Client/Styles/Motion.axaml` (change)
- `src/OVS.Client/Views/BusySpinner.axaml` (change): a smoother spinner in animated mode
- `src/OVS.Client/Views/MainWindow.axaml`, `SettingsView.axaml` (change)
- `tests/OVS.Tests/Client/LiveMotionTests.cs` (new)

### Context

`Border.ring.speaking` fades its border in 120 ms (`Controls.axaml:200`). Mute and deafen show `PathIcon`s by `IsVisible`. `SlotText` is plain text. The input level meter in the settings follows `InputLevelDb`. The busy flags come from `Pending`.

### Acceptance Criteria

- [x] AC1: A speaking user's ring glows and breathes softly (a scale of 1.0 to 1.06 with an outer glow, a 900 ms loop) while speaking, and fades out after the speaking stops. Speech heard through a link uses the link colour. (as built: the glow is a box shadow in the speaking or link colour; glow and ring colour fade out in 220 ms, the breathing stops with the speaking)
- [x] AC2: The mute, deafen and server-mute icons pop in or out (scale plus fade) instead of switching; my own mute button in the bottom bar flips its icon with a short rotation. (as built: `LiveMotion.Pop` on the state icons of a user row; a hidden icon is gone at once and a copy of it pops out in the overlay layer, only where the icon could be seen (not in a closed drawer, a row scrolled away or under an open dialog); the deafen button turns its icon in as well; no pop while a row is first built)
- [x] AC3: The slot counter (`3/10`) ticks to the new number; a full channel pulses its counter once. (as built: `LiveMotion.Tick`, a higher number comes up from below, a lower one down from above; the pulse plays when the channel becomes full)
- [x] AC4: The input level meter moves smoothly (fast attack, slower release, about 80 ms and 300 ms) instead of stepping; the push-to-talk indicator pulses while sending. (as built: `LiveMotion.Level` follows the level frame by frame and arrives within about 80 ms rising and 300 ms falling, the frames stop once it has arrived; a new model (the settings opened again) or a meter nobody sees shows the level at once; the indicator is the "Sendet ..." line in the bottom bar)
- [x] AC5: Busy spinners rotate smoothly, and busy buttons show a shimmer; when the busy state ends, they cross-fade back. (as built: the arc keeps turning and grows and shrinks; a button holding a showing spinner gets the class `busy` and a light band passes over it (an opacity mask); the spinner fades in, and the button's content fades back when it goes)
- [x] AC6: Idle CPU stays flat: when nobody speaks and nothing is busy, no looping animation runs (the loops start and stop with their states). (as built: every loop is a style that stops matching with its state; a busy spinner turns only while it and all its parents are visible, in both displays; animations started from code ask for their frames (`Motion.Play`), since without a loop running nothing else would draw them; the test watches every visual of an idle connected view, after the administration was opened and closed, for a second)
- [x] AC7: In simplified mode all of this looks as today.

### Tests (TDD)

1. `LiveMotionTests > "Speaking_RingBreathesWhileSpeaking_StopsAfter"` (AC1)
2. `LiveMotionTests > "MuteIcons_PopInOut"` (AC2)
3. `LiveMotionTests > "SlotCounter_TicksAndPulsesWhenFull"`, `SlotCounterTests > "Slots_NumbersChange_TickAndPulseOnlyWhenItGetsFullThenRest"`, `"Slots_UserCountChanges_HigherComesFromBelowLowerFromAbove"`, `"Slots_FirstNumbers_StartNoAnimation"`, `"Slots_CounterInNoWindow_StartNoAnimation"`, `ChannelSlotsTests > "IsFull_AnyNumbers_AsTheSharedCapacityRule"`, `ChannelCapacityTests > "IsFull_LimitAndOthersInside_FullWhenNoFreeSlot"` (AC3)
4. `LiveMotionTests > "LevelMeter_AttackRelease"` (AC4)
5. `LiveMotionTests > "Busy_ShimmerCrossFadesBack"` (AC5)
6. `LiveMotionTests > "Idle_NoRunningLoops"` (AC6): count the running animations on an idle connected view = 0.
7. `LiveMotionTests > "Simplified_AsToday"`, `SlotCounterTests > "Slots_Simplified_StartNoAnimation"` (AC7)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1, 6 and 7 (red): the speaking ring.
2. Tests 2 to 5 (red).
3. Headless screenshots of a speaking row.

### Out of Scope

- Audio-related logic (only the display changes)

---

## Package 109: Every action animated

**Goal:** A walk-through test proves that every user action has its animation in the animated display, none in the simplified one, and that load stays bounded.

**Dependencies:** Packages 100 to 108

**Affected files:**
- `tests/OVS.Tests/Client/MotionCoverageTests.cs` (new)
- `src/OVS.Client/Views/Motion.cs` (change): a test hook that counts the animations started per action
- any view or style file where the walk-through finds a gap

### Context

Packages 100 to 108 each cover one area. The user asked for every action in the app to animate, so one catch-all check is needed, similar to `EveryWaitingAction_VisibleWithin400ms` from Package 98.

### Acceptance Criteria

- [x] AC1: A list of every user action in the client exists in the test, including:
  - connect, disconnect, join channel, switch channel
  - create, edit, delete and move a channel
  - create, delete and reorder groups
  - ban, unban, kick, move and mute a user
  - send a chat message, switch tabs
  - open and close Settings and Administration
  - open and close every dialog, menu and dropdown
  - toggle mute and deafen
  - add and remove bookmarks
  - create and restore backups, upload a logo, link channels
  - every settings control

  (as built: `MotionCoverageTests.Steps` walks 135 actions in one window, the same walk in both displays: home (the connect, server password, certificate and update offer dialogs, a failed connect, bookmarks with their menu and dialogs), every settings control (check boxes and the radio button toggle and are set back, drop-downs open and close, sliders and a button are hovered; rows of a list count once per kind), the key binding dialog, connect, channel and user menus and every dialog they open (each opened and closed, also the channel password), join and switch, create, rename, mute, limit, move, link, unlink and delete a channel, join, move, mute, kick and ban a user, own mute and deafen, chat send and tab switch, the administration: every tab and its 7 drop-downs, new and dropped group, created, chosen, edited (a right toggled), saved, reordered and deleted group, user list with a stored mute lifted and a user deleted (with its question), ban and unban, the link matrix (select a channel, change a link, discard, change again, apply), the server settings' check boxes and save, logo upload, backup created, the restore, download warning and delete questions and a deleted backup, log search, a log file opened and its page turned; the drawer at 360 px, disconnect. Buttons whose effect is the click (save, apply, discard, search) are clicked with the mouse. What the server answers is applied as its message (`server.Apply`); the dialogs are the real ones in the overlay (`SimpleDialogs.For`), the server password, certificate, update offer and channel password dialogs are opened directly (their triggers need a real connection, an update check or a channel lock the admin bypasses). Not drivable headless, listed in the test with the reason: picking a file (OS picker; what follows the pick is driven), saving a backup or log (OS save dialog; the warning before it is driven), a restore to its end (the server restarts; the question is driven, the dropped connection is "disconnect"), dragging (Package 96 keeps its own behaviour; the drop's result is "move a channel" and "reorder groups"), speaking and the level meter (audio devices, `LiveMotionTests`), the window's own buttons)
- [x] AC2: In animated mode, each action in the list starts at least one animation (counted by the hook) and ends in the same final state as in simplified mode. (as built: the hook is `Motion.Started` / `Motion.Running` / `Motion.ResetStarted`, counted in `Motion.Play` and `FlyGhost.Fly`, one increment and one continuation per animation; effects only a style plays (a hover lift, a check mark, a slider thumb, a clicked button's face) count when the control or a part of it passes through a value between start and end (three or more distinct looks over the frames, a jump has two), or, since late frames on a loaded machine can miss the middle of a 120 ms transition, when its look changed and the change ran through a transition of the animated display (`Motion.Fast` or `Motion.Normal` long; Fluent's own are shorter and the simplified display has none of them); Fluent's own press tilt of a button is not watched. One idle step watches every visual of the connected window and must count nothing: no stray animation makes the other steps pass (it found Fluent's indeterminate connect bar still looping after a failed connect; it is indeterminate only while connecting now). Each step waits until no animation from code runs, no list row enters or leaves and the end state has held (at least 160 ms in the animated display for the waiting states and transitions, a few frames in the simplified one); both walks take about 55 s, the floor being the animations themselves (about 120 animated steps of 220 to 320 ms). The end state compared: page, connection, status, dialog and card, drawer, bookmarks, every channel with its users and marks, own mute and deafen, chat tab and lines, the settings being edited, the administration's tab, groups with their rights, users with their mutes, bans, backups, logo, link matrix changes and selection, server settings, log hits, open file, page and lines, and that nothing of a movement is left (overlay layer empty, no page picture, no list row entering, leaving or faded). Gaps the walk found and closed: closing a menu, dropdown or tooltip (a picture of the popup fades out in the overlay layer, `Motion.Fast`; it is closed at once as before; no picture when a dialog is open or opens with the close, the overlay layer lies above the dialogs), linking and unlinking (the link icon pops), renaming a channel (`LiveMotion.Fade` on the name), muting a channel and its lock (the icons pop), dropping an unsaved new group (it folds away like a deleted one; `AdminViewModelTests.UnsavedGroup_Dropped_FoldsAway`), uploading a logo (a busy spinner fades in also when the area around it is shown for a wait, e.g. the logo's busy cover or a user card's action; not when it is built or rebuilt (A117), so a tab's content or a row that is made again does not replay it; an area that holds a running wait and is shown again, a page or the drawer, fades its spinner in with it, which is the same showing), a log file's page (its lines fade in; a line keeps its key while the filter changes); the fake servers of the tests need the main view model's `Post`, otherwise leaving rows go off the UI thread)
- [x] AC3: In simplified mode none of the new animations start for any action, and `Simplified_LooksLikeBefore` stays green. (as built: the same walk counts 0 started and no in-between looks for every action)
- [x] AC4: Load: 50 users joining and 50 leaving within one second ends in a correct tree within `Motion.Slow` + 8 × 30 ms after the last change; no animation is queued beyond that. (as built: 50 joins and 50 leaves spread over 800 ms in the crowded server; measured 250 to 510 ms on an idle machine from the last change until the tree is right, every row whole and `Motion.Running` back to where it was; the bound is 560 ms plus a margin of 300 ms and four times the longest gap between two frames seen meanwhile, since an animation can only end on a frame and a loaded machine (the suite, a game beside it) delivers them late; the storm's own length is the test machine's and not asserted; afterwards nothing starts any more. The test found that counters ticking many times stacked their animations, which then ended one per frame seconds later: a counter's or icon's new tick or pop now ends its last one (`LiveMotion.Replace`). On a loaded machine it also found rows that left while they still slid in: their entry ran on in the removed row and ended about a second later; a row that starts leaving or is reused now ends its entry at once (`ItemMotion.EndEntry`))
- [ ] AC5: A manual check (outside the tests, a 2 minute session at 1100 px and at 360 px) feels smooth, with no stutter on a mid-range laptop; findings go into the package notes. (open: needs a real machine and a person; to be done by the user, not by the automated run)

### Tests (TDD)

1. `MotionCoverageTests > "EveryAction_AnimatesInAnimatedMode"` (AC1, AC2)
2. `MotionCoverageTests > "EveryAction_NoNewAnimationInSimplifiedMode"` (AC3)
3. `MotionCoverageTests > "JoinLeaveStorm_EndsBounded"` (AC4)
4. Manual check (AC5)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Test 1 (red): fill the gaps it finds.
2. Tests 2 and 3 (red).
3. The manual check; record the result.

### Out of Scope

- New animations beyond closing gaps

---

## Package 110: Duplicate channel names

**Goal:** Several channels may carry the same name, and the client still recognises its own newly created channel.

**Dependencies:** none

**Affected files:**
- `src/OVS.Server/Commands/ChannelCommands.cs` (change): `ValidateChannel` no longer refuses a name that another channel has
- `src/OVS.Shared/Protocol/Messages.cs` (change): `ChannelAdded(ChannelInfo Channel, string? RequestId = null)`; the creator's copy carries its request's id (as built: the existing `RequestId` instead of a new tag)
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (change): `CreateChannel` confirms on the `ChannelAdded` that answers its own request id instead of the name
- `src/OVS.Client/Debug/DebugApi.cs` (change): a channel key that matches several names is answered with an error naming the ids, instead of taking the first
- `tests/OVS.Tests/Server/ChannelCommandTests.cs`, `tests/OVS.Tests/Client/ServerViewModelTests.cs`, `tests/OVS.Tests/Client/DebugApiTests.cs` (change)

### Context

`ChannelCommands.ValidateChannel` (line 301) refuses a name another channel has (case-insensitive) with `NameTaken`, for create and edit. Internally every channel is identified by its `Guid` (data, links, logs, voice routing), so nothing on the server needs unique names. Two client places rely on them: `ServerViewModel.CreateChannel` (line 475) recognises its new channel by `a.Channel.Name == e.Name`, and `DebugApi` (line 340) resolves a name to the first match. `ChannelCommandTests` has `[InlineData("lobby", Codes.NameTaken)]` (line 85). Groups keep their own uniqueness check in `AdminCommands.cs:273` (A119).

### Acceptance Criteria

- [x] AC1: Creating a channel named like an existing one (also in other case) succeeds; both exist with their own ids.
- [x] AC2: Renaming a channel to the name of another channel succeeds.
- [x] AC3: The creator's dialog closes on its own new channel, not on a channel of the same name that someone else adds meanwhile. (as built: no new tag; the creator's copy of `ChannelAdded` carries the request's existing `RequestId`, the others get it without, and `CreateChannel` confirms when its `Pending` answers that id)
- [x] AC4: Group names stay unique (`NameTaken` as before).
- [x] AC5: The debug API refuses an ambiguous channel name with a message listing the matching ids; an id still works.

### Tests (TDD)

1. `ChannelCommandTests > "Create_SameNameAsExisting_Succeeds"` (AC1)
   - Given: a server with "Lobby"; create "lobby".
   - Expected: `ChannelAdded`, two channels with different ids.
2. `ChannelCommandTests > "Edit_ToNameOfOther_Succeeds"` (AC2)
3. `ServerViewModelTests > "SendingDialogs_WaitForTheConfirmation_RowActionsSpin"` (AC3, as built: folded into the existing test)
   - Given: a pending create of "Neu"; the server first sends someone else's `ChannelAdded` "Neu", then the own one with the request id.
   - Expected: still pending after the first, confirmed after the second.
4. `AdminCommandTests` existing `" gast "` → `NameTaken` stays green (AC4)
5. `DebugApiTests > "AmbiguousChannelName_Refused"` (AC5)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 and 2 (red): drop the duplicate check in `ValidateChannel`; the old `NameTaken` row in `ChannelCommandTests` changes to the new expectation.
2. Test 3 (red): the request id on the creator's `ChannelAdded` (the others get it without).
3. Test 5 (red): the debug API.

### Out of Scope

- Separators (Package 111)
- Duplicate group names (A119)

---

## Package 111: Separator channels

**Goal:** Admins can put separators into the channel list: a full-width line that nobody can join, link or chat in.

**Dependencies:** Package 110 (every separator has the same empty name)

**Affected files:**
- `src/OVS.Shared/Protocol/Messages.cs` (change): `enum ChannelKind { Voice, Separator }`; `ChannelInfo` and `CreateChannel` get `ChannelKind Kind = ChannelKind.Voice`
- `src/OVS.Shared/Protocol/ProtocolInfo.cs` (change): `Version` up by one, with the comment (as built: 10 was deployed on 2026-09-30, so 11)
- `src/OVS.Shared/Protocol/Codes.cs` (change): `NotJoinable`
- `src/OVS.Server/Data/ServerData.cs` (change): `ChannelRecord.Kind` (older files load as `Voice`, no new data version)
- `src/OVS.Server/Commands/ChannelCommands.cs` (change):
  - create a separator: name and description empty; no limit, lock or password; no channel log file
  - refuse `EditChannel` on a separator, and `JoinChannel` or `MoveUser` into one
- `src/OVS.Server/Commands/LinkCommands.cs` (change): links touching a separator are refused with `InvalidLink`
- `src/OVS.Server/Data/BackupStore.cs` (change): validation accepts a separator with an empty name, and refuses one that is the default channel or linked
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (change):
  - `ChannelViewModel.IsSeparator`; a separator has no join, edit, link, users or slot text
  - move targets (`PickChannel`) skip separators
- `src/OVS.Client/ViewModels/ChannelDialogViewModel.cs`, `src/OVS.Client/Views/SimpleDialogs.cs` (change): in create mode a type choice "Sprach-Channel" / "Trenner"; with "Trenner" every other field hides
- `src/OVS.Client/ViewModels/LinkMatrixViewModel.cs` (change): separators are no rows
- `src/OVS.Client/Views/MainWindow.axaml`, `MainWindow.axaml.cs` (change):
  - the separator row: a full-width line, no icon left, no count right
  - its context menu has only "Channel erstellen", "Nach oben", "Nach unten" and "Löschen"
  - a double click does nothing
  - `FitSidebar` skips separator rows
- `src/OVS.Client/Debug/DebugApi.cs` (change): channels report their kind
- `src/OVS.Client/ErrorTexts.cs`, `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change)
- Tests (change):
  - `tests/OVS.Tests/Server/`: `ChannelCommandTests.cs`, `LinkCommandTests.cs`, `BackupTests.cs`, `DataStoreTests.cs`
  - `tests/OVS.Tests/Client/`: `ServerViewModelTests.cs`, `ChannelDialogViewModelTests.cs`, `UiSmokeTests.cs`, `DebugApiTests.cs`, `LocalizationTests.cs`

### Context

There is one kind of channel today:
- `ChannelInfo` (Messages.cs:121) and `ChannelRecord` (ServerData.cs:99) describe it.
- `ChannelCommands` handles join, create, edit, move and delete.
- The client shows each channel through the `ChannelViewModel` template in `MainWindow.axaml` (lines 177 to 236): speaker icon left, slot count right, the name, small icons and a context menu.

`ControlServer` refuses any other protocol version (`ControlServer.cs:162`), so client and server move to the new version together. Separators go through the same order, reorder (`ReorderChannels`, Package 96 drag), delete and animation paths as channels.

### Acceptance Criteria

- [x] AC1: The create dialog offers the type "Sprach-Channel" (the default) or "Trenner". A separator is created with one click on "Erstellen", needs `ChannelCreate`, and lands at the end like a channel.
- [x] AC2: In the channel tree a separator is a single horizontal line across the row: no icon left, no number right, no name, no tooltip, no users below.
- [x] AC3: Nobody can join a separator, not even admins:
  - there is no "Beitreten", and a double click does nothing
  - the server answers `JoinChannel` or `MoveUser` to it with `NotJoinable`
- [x] AC4: A separator can be dragged, moved up and down, and deleted with the usual rights. It cannot be:
  - edited (`EditChannel` gives `InvalidValue`)
  - linked (`InvalidLink`)
  - a move target
- [x] AC5: (as built: `DataStoreTests > "OldFile_ChannelsLoadAsVoice_SeparatorRoundTrips"`; backup variants `separator-default`, `separator-linked`, `separator-named` in `BackupTests`) A server data file or a backup without `Kind` loads every channel as a voice channel. A backup with a linked separator, or a separator as the default channel, is refused (`InvalidBackup`).
- [x] AC6: The fitted sidebar width ignores separators; the texts exist in German and English.

### Tests (TDD)

1. `ChannelCommandTests > "CreateSeparator_EmptyNameNoOptions_BroadcastsKind"` (AC1)
2. `ChannelCommandTests > "Separator_JoinMoveEdit_Refused"` (AC3, AC4)
3. `LinkCommandTests > "LinkToSeparator_InvalidLink"` (AC4)
4. `DataStoreTests > "OldFile_ChannelsLoadAsVoice_SeparatorRoundTrips"` and `BackupTests > [Theory] "Restore_InvalidContent_RejectedServerStillStarts"` (AC5)
5. `ChannelDialogViewModelTests > "Create_Separator_HidesFieldsAndSendsKind"` (AC1)
6. `ServerViewModelTests > "Separator_NoJoinNoEditNoLink_NotAMoveTarget"` (AC3, AC4)
7. `UiSmokeTests > "Separator_IsALine_NoIconNoCount"` (AC2)
   - Given: a headless channel tree with one separator.
   - Expected: its row has no `channelIcon` and no `channelCount`, and one line element spans the row width.
8. (as built) the double click runs `JoinCommand`, so `ServerViewModelTests > "Separator_NoJoinNoEditNoLink_NotAMoveTarget"` covers it (no join sent, no notice); reorder and delete of a separator are covered on the server in `ChannelCommandTests > "Separator_JoinMoveEdit_Refused"` (AC3, AC4)
9. `UiSmokeTests > "Separator_IsALine_NoIconNoCount"` also checks that the fitted width ignores the separator; `LocalizationTests` (AC6)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 to 4 (red): the protocol, the data, the server rules, the backup validation and the protocol version.
2. Tests 5 and 6 (red): the dialog and the view model.
3. Tests 7 to 9 (red): the row template, the double click, the sidebar fit and the texts. Take headless screenshots of a tree with separators in light and dark.

### Out of Scope

- Separators with a text label (the request asks for a plain line)
- Collapsible sections

---

## Package 112: No empty channel tooltip

**Goal:** Hovering a channel without description and without links shows no tooltip at all.

**Dependencies:** none

**Affected files:**
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (change): `ChannelViewModel.Tooltip` returns `null` instead of an empty text
- `tests/OVS.Tests/Client/ServerViewModelTests.cs`, `UiSmokeTests.cs` (change)

### Context

The channel row binds `ToolTip.Tip="{Binding Tooltip}"` (`MainWindow.axaml:180`). `ChannelViewModel.Tooltip` (`ServerViewModel.cs:623`) returns `Description` when the channel is not linked, and that is `""` without a description. Avalonia shows a tooltip for an empty string (an empty box); only `null` shows none.

### Acceptance Criteria

- [x] AC1: A channel with neither description nor links has `Tooltip == null`, and hovering its row opens no tooltip.
- [x] AC2: A channel with a description, with links, or with both shows the same tooltip text as today.
- [x] AC3: A description of only spaces counts as none. (as built, found in review: a user row in no known group had the same empty tooltip; `UserViewModel.GroupTip` is null then)

### Tests (TDD)

1. `ServerViewModelTests > "Tooltip_NoDescriptionNoLinks_IsNull"` (AC1, AC3): the reproduction, red first
2. `ServerViewModelTests > "Tooltip_DescriptionOrLinks_Unchanged"` (AC2)
3. `UiSmokeTests > "ChannelRow_NoDescription_NoTooltipOpens"` (AC1): hover the row headless; `ToolTip.GetIsOpen` stays false (as built: the row has no tip, and opening it as hovering does after its delay keeps it closed)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 and 3 (red), then the one-line change in `Tooltip`.
2. Test 2 stays green.

### Out of Scope

- Tooltips elsewhere (Package 115 handles their animation)

---

## Package 113: Sidebar fits its footer

**Goal:** After connecting, the fitted sidebar is wide enough for the own nickname, the talk hint and the footer buttons.

**Dependencies:** none (if Package 111 also runs, do 111 first: both change `FitSidebar`)

**Affected files:**
- `src/OVS.Client/Views/SidebarWidth.cs` (change): `For(rows, footer)` takes the footer's need as a second lower bound
- `src/OVS.Client/Views/MainWindow.axaml` (change): names for the footer parts (`SelfFooter`, `SelfTexts`, `FooterButtons`)
- `src/OVS.Client/Views/MainWindow.axaml.cs` (change): `FitSidebar` measures the footer and refits when the nickname or the talk hint changes. The footer's need is:
  - its padding, the avatar ring and the text margins
  - the widest untrimmed text among the nickname, the current talk hint (`TalkHint`, including the warning hint button), `Transmit_Channel` and `Transmit_Linked`
  - the buttons' desired width
- `tests/OVS.Tests/Client/SidebarWidthTests.cs`, `ResponsiveTests.cs` (change)

### Context

`FitSidebar` (`MainWindow.axaml.cs`) sets the fitted width from the channel rows only (`SidebarWidth.For`, minimum 240). The footer (`MainWindow.axaml` lines 70 to 118) holds the avatar, the nickname and the talk hint beside three icon buttons. The talk hint reads "Sprachaktivierung", "Push-to-Talk: ..." or, while sending, "Sendet an eigenen Channel und Links". With short channel names the fitted width is narrower than the footer, so the nickname and the hint trim. `ApplyResponsive` keeps the fitted width as the column's minimum, bounded by `Responsive.SidebarMax` (A121).

### Acceptance Criteria

- [x] AC1: Connected at a 1100 px window width, with short channel names and a long nickname, the nickname and the talk hint are not trimmed after the fit.
- [x] AC2: While sending (the "Sendet ..." texts) the sidebar width does not change. (as built: the two "Sendet" texts are not part of the fit, "Sendet an eigenen Channel und Links" alone made every sidebar about 386 px wide; they show only while talking and may trim, and sending never refits)
- [x] AC3: Changing the transmit mode in the settings refits the sidebar so the new hint fits. The modes are voice activation, push-to-talk with a key, and push-to-talk without a key.
- [x] AC4: When the window is too narrow for that width, `SidebarMax` still wins and the texts trim as today; the compact drawer (below 700 px) is unchanged.
- [x] AC5: The sidebar cannot be dragged narrower than the footer needs (it is the column's minimum, like the channel rows).

### Tests (TDD)

1. `SidebarWidthTests > "For_FooterWiderThanRows_UsesFooter"` (AC1)
2. `ResponsiveTests > "Connected_LongNickname_FooterNotTrimmed"` (AC1)
   - Given: a headless connected window at 1100 px with short channel names and a long nickname.
   - Expected: the desired widths of the nickname and hint text blocks fit their bounds.
3. `ResponsiveTests > "Sending_WidthStays"` (AC2)
4. `ResponsiveTests > "ModeChange_Refits"` (AC3)
5. `ResponsiveTests > "NarrowWindow_MaxStillWins"` (AC4); the existing drawer tests stay green
6. `ResponsiveTests > "Drag_NotBelowFooter"` (AC5)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Test 1 (red): the second bound in `SidebarWidth.For`.
2. Tests 2 to 4 (red): the footer measurement in `FitSidebar`, and the refit on `TalkHint` and nickname changes.
3. Tests 5 and 6.

### Out of Scope

- The sidebar header (the server name keeps trimming, A121)

---

## Package 114: Ask before losing unsaved changes

**Goal:** Leaving a page or admin tab with unsaved changes asks whether to save and leave, leave without saving, or keep editing.

**Dependencies:** none

**Affected files:**
- `src/OVS.Client/ViewModels/SettingsViewModel.cs` (change): `HasChanges`, true when the edited settings differ from the saved ones (compared as their saved JSON)
- `src/OVS.Client/ViewModels/AdminViewModel.cs` (change):
  - `HasChanges` for the selected group: its name or rights differ from the server's, or it is a new draft
  - `HasChanges` for the server settings tab: name, welcome text, password fields or limits differ from the loaded ones
  - `SaveCurrentAsync` per place, which returns whether the server took it
- `src/OVS.Client/ViewModels/LinkMatrixViewModel.cs` (change): `HasPending` counts as unsaved
- `src/OVS.Client/ViewModels/MainViewModel.cs` (change): one guard, `ConfirmLeaveAsync()`, before every way out: `OpenSettings`, `OpenAdminAsync`, `ClosePage`, `DisconnectAsync`, connecting elsewhere and closing the window
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (change): the dialog hook `AskLeave` in the dialogs record, beside `Confirm`
- `src/OVS.Client/Views/SimpleDialogs.cs` (change): the modal with three buttons, "Speichern und verlassen", "Verlassen ohne Speichern" and "Weiter bearbeiten" (Esc keeps editing)
- `src/OVS.Client/Views/AdminView.axaml.cs` (change): a tab switch or another group selection with unsaved changes asks first, and on "Weiter bearbeiten" goes back to the old tab or group
- `src/OVS.Client/Views/MainWindow.axaml.cs` (change): the window's `Closing` asks (cancel, ask, close again)
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change)
- `tests/OVS.Tests/Client/MainViewModelTests.cs`, `SettingsTests.cs`, `AdminViewModelTests.cs`, `UiSmokeTests.cs`, `LocalizationTests.cs`, `ResponsiveTests.cs` (change)

### Context

Nothing tracks unsaved changes today:
- **Settings page:** it edits a copy (`SettingsViewModel`, live preview) that `CloseSettings` throws away (`MainViewModel.cs:663`). Esc, the X and opening the administration all close it silently.
- **Administration:** the group editor (`SaveGroup`), the server settings (`SaveServerSettings`) and the link matrix (`Apply`, `HasPending`) need a manual save (A122). Switching tab, group or page drops the edits.

Dialogs live in the main window's overlay (`SimpleDialogs`, A20). `Pending` (Package 97) tells whether a save went through.

### Acceptance Criteria

- [x] AC1: With unsaved changes in one of the four places, every way out listed in A122 shows the modal; without changes nothing is asked.
- [x] AC2: "Weiter bearbeiten" (and Esc) stays exactly where one was: the same page, tab and group, with the edits untouched; the window stays open.
- [x] AC3: "Verlassen ohne Speichern" discards and carries out the action. On the settings page the live preview reverts as today.
- [x] AC4: "Speichern und verlassen" saves and carries out the action once the server confirms. A refused or unanswered save stays on the page with its error shown (Package 98) and does not leave.
- [x] AC5: "Abbrechen" on the settings page and "Verwerfen" in the link matrix discard without asking.
- [x] AC6: The texts exist in German and English, and the modal fits at 360 px.

### Tests (TDD)

(as built: all of them in `tests/OVS.Tests/Client/ViewModels/LeaveGuardTests.cs`, the administration's saving places also in `tests/OVS.Tests/Client/ViewModels/AdminPlaceTests.cs`, with `ResponsiveTests.Dialogs_FitAt360` for the modal; the X of the settings page got its own `LeaveCommand`, "Abbrechen" keeps `CancelCommand`; with three answers the dialog stacks its buttons in full width, the default one first, because the card is at most 460 px wide)

(as built, from three review rounds: the settings page compares with what it showed when it opened, not with the file, since the sliders do not round-trip exactly; the server tab and an edited group follow the server in the fields the user did not touch, so a save never undoes another admin; edited group rows survive server updates, which used to drop them silently; a device list arriving with the page open no longer crashes the change check)

1. `LeaveGuardTests > "Settings_HasChanges_FalseUntouched_TrueAfterEdit_FalseAfterRevert"` (AC1)
2. `LeaveGuardTests > "Admin_HasChanges_GroupDraftServerSettingsLinks"`, `AdminPlaceTests > "HasAnyChanges_OnlyLinksChangedThenDiscarded_TrueThenFalse"` (AC1)
3. `LeaveGuardTests > "LeaveWithChanges_Asks_KeepEditingStays"`, `"ConnectAsync_AdminHasChangesAndUserStays_AsksOnceAndKeepsTheShownServer"`, `"SettingsCloseButton_UnsavedChanges_AsksFirst"` (AC1, AC2)
   - Given: a fake `AskLeave` that answers each choice in turn.
   - Expected: correct behaviour for a page change, a disconnect and a window close.
4. `LeaveGuardTests > "LeaveWithoutSaving_Discards"` and `"SaveAndLeave_LeavesAfterConfirm_StaysOnRefusal"`, `DialogFrameTests > "ShowAsync_ThirdAnswer_ThreeButtonsDefaultFirstAndItsValueReturned"`, `DialogButtonsTests > [Theory] "ShowAsync_EnterPressed_AcceptsOnlyWhereThatIsSafe"`, `AdminPlaceTests > "SaveAsync_NoChanges_TrueWithoutSending"` and `"Places_SavingOrder_GroupsLinksServer"`, `GroupListViewModelTests > "Place_GroupSaveOnItsWay_SavingWaitsForItAndSendsNoSecondOne"`, `"Place_OtherGroupEdited_SavingSendsItAndSucceedsOnceConfirmed"`, `"Place_TwoGroupsEdited_SendsTheSecondOnlyOnceTheFirstIsConfirmedAndSucceedsAfterBoth"`, `"Place_SecondGroupRefused_FailsSelectsItAndItKeepsItsEdit"`, `"Place_FirstGroupRefused_FailsAndTheSecondIsNeverSentAndBothKeepTheirEdits"`, `"Place_FirstGroupGetsNoAnswer_FailsAfterTheTimeoutAndTheSecondIsNeverSent"`, `"Place_SavedAgainAfterATimeout_OneAnswerConfirmsOnlyTheFirstGroup"`, `"Place_SecondGroupNameBlank_FailsSelectsItWithTheNameErrorAfterTheFirstWasSaved"`, `"Place_RightsLostWhileTheFirstGroupIsOnItsWay_TheRebuiltSecondGroupIsNotSentStale"`, `"Place_GroupRebuiltWhileOnItsWayThenRefused_FailsAndSelectsTheRowThatIsInTheList"`, `"Place_DraftAndEditedSavedGroup_SendsTheUpdateThenTheCreateAndSucceedsAfterBoth"`, `"SaveGroup_OtherGroupEditedToo_SendsOnlyTheSelectedGroup"`, `LinkMatrixViewModelTests > "Place_SaveOnItsWay_SavingWaitsForItAndSendsNoSecondOne"`, `"Place_LinkMarked_HasChangesAndSavingSendsItAndWaitsForTheServer"`, `"PlaceDiscard_LinkMarked_LinkBackAndNoChanges"`, `ServerSettingsViewModelTests > "Place_NameEdited_HasChangesAndSavingSendsTheSettingsAndWaitsForTheServer"` and `"Place_NameBlank_HasChangesButSavingFailsWithoutSending"` (AC3, AC4)
5. `LeaveGuardTests > "AdminTabSwitch_WithChanges_AsksAndStays"` and `"OtherGroup_WithChanges_AsksAndStays"`, `AdminPlaceTests > "Places_EveryPlace_CarriedByExactlyOneTab"` and `"SavingTabs_DataContextSet_CarryTheirOwnPlace"` (AC1, AC2)
6. `LeaveGuardTests > "CancelAndDiscardButtons_DoNotAsk"` (AC5)
7. `LocalizationTests` and `ResponsiveTests > [Theory] "Dialogs_FitAt360"` (AC6)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 and 2 (red): `HasChanges` in the three view models.
2. Tests 3, 4 and 6 (red): the guard in `MainViewModel` and the dialog hook.
3. Test 5 (red): tab and group switches in `AdminView`, and the window's `Closing`.
4. Test 7: the texts and the modal.

### Out of Scope

- Dialogs (they have their own Abbrechen)
- Changes that save at once (user groups, bans, the server icon)

---

## Package 115: Tooltips fade in once

**Goal:** In the animated display a tooltip fades in once and stays, without blinking a second time.

**Dependencies:** none

**Affected files:**
- `src/OVS.Client/Views/Motion.cs` (change): the popup fade (the `Popup.IsOpenProperty` handler)
- `src/OVS.Client/Styles/Motion.axaml` (change, if the cause is the theme's own transition)
- `tests/OVS.Tests/Client/OverlayMotionTests.cs` (change)

### Context

Package 105 fades every popup in from `Motion.cs`: opacity 0 to 1 and a 4 px slide over `Motion.Normal`, with `FillMode.Backward`. The Fluent `ToolTip` theme has its own opacity transition when it opens.

The likely cause of the second blink is that both run on the tooltip: when the code animation ends, its value is removed and the theme's transition shows once more. The first test must reproduce the blink before anything is changed. If the cause turns out to be a different one (for example a tooltip that closes and opens again), the fix follows that cause.

### Acceptance Criteria

- [x] AC1: In the animated display a tooltip's opacity only rises from opening until it is fully shown, then stays at 1 until it closes (no dip, no second fade).
- [x] AC2: The slide from the anchor (Package 105) stays.
- [x] AC3: Context menus, flyouts and dropdowns keep their open animation, without a blink either.
- [x] AC4: The simplified display is unchanged (`Simplified_LooksLikeBefore` stays green).

### Tests (TDD)

(as built: the reproduction showed the tooltip fading to full, dropping to 0.02 at frame 15 and fading in again: the Fluent tooltip fades in by its own theme, and when the code fade of Package 105 ended the theme fade ran once more. For a tooltip the code animation now only slides; menus, flyouts and dropdowns keep both)

1. `OverlayMotionTests > "ToolTip_FadesInOnce_NoSecondBlink"` (AC1): the reproduction, red first
   - Given: a tooltip opened headless in the animated display.
   - Expected: sampled every frame for 700 ms, the effective opacity of the tooltip and its content never drops after rising.
2. `OverlayMotionTests > "Popups_FadeAndSlide"` stays green (AC2)
3. `OverlayMotionTests > "ContextMenu_FadesInOnce"` (AC3)
4. `UiSmokeTests.Simplified_LooksLikeBefore` (AC4)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Test 1 (red): find the cause from the sampled values.
2. Fix it at the cause. Most likely, in the animated display either the code fade replaces the tooltip theme's own transition, or the code does not animate opacity where the theme already does.
3. Tests 2 to 4.

### Out of Scope

- Tooltip timing (the delay before showing)

---

## Package 119: Groups from the user's context menu

**Goal:** Moderators with the right to assign groups give and take a user's groups with a right click on the user.

**Dependencies:** none

**Affected files:**
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (change):
  - `UserViewModel` keeps `GroupIds` and `CanBeModeratedByMe` from `Update` and offers `Groups`, a list of `GroupToggle` like the administration's
  - `ServerViewModel.SetUserGroupAsync(fingerprint, sessionId, groupId, assigned)` sends `AssignGroup` / `UnassignGroup` with `SendConfirmed`, confirmed by the `UserUpdated` whose `GroupIds` match
- `src/OVS.Client/ViewModels/AdminViewModel.cs` (change): `GroupToggle` and the last-admin guard move to a place both can use, without changing behaviour
- `src/OVS.Client/Views/MainWindow.axaml` (change): the submenu "Gruppen" in the user row's `ContextMenu` (lines 245 to 281), items `ToggleType="CheckBox"`
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change)
- `tests/OVS.Tests/Client/ServerViewModelTests.cs`, `UiSmokeTests.cs`, `LocalizationTests.cs` (change)

### Context

Groups are assigned today only in the administration's user list: `AdminViewModel.RebuildUsers` (lines 503 to 525) builds one `GroupToggle` per group, enabled when `g.AssignableByMe && user.CanBeModeratedByMe`, and `ToggleUserGroupAsync` (lines 852 to 862) sends `AssignGroup` / `UnassignGroup` (`Messages.cs:185-186`, by fingerprint). The server checks `GroupsAssign`, `CanAssign` (never a group stronger than me) and `CanModerate` (`AdminCommands.cs:134-160`), refuses removing the last admin (`Codes.LastAdmin`) and answers a change only with `UserUpdated` for online users.

The user row's context menu (`MainWindow.axaml:245-281`) has volume, message, move, server mute, kick and ban, each enabled by a `Can*` flag from `UserViewModel.Update` (`ServerViewModel.cs:816`). `UserViewModel` keeps only `GroupNames` and `GroupTip`, not the ids. The client has no checkable menu item yet; Avalonia 11 supports `MenuItem ToggleType="CheckBox"`.

### Acceptance Criteria

- [x] AC1: Right-clicking a user who is weaker than me, while I have `GroupsAssign`, shows a submenu "Gruppen" listing every group of the server with a check mark for the user's groups. On myself, on stronger or equal users, or without the right, the submenu is not there.
- [x] AC2: A group I may not assign (`AssignableByMe` false) is shown greyed out; the Admin group of the last admin cannot be unticked (same rule as the administration).
- [x] AC3: Clicking a group assigns or removes it at once; the check mark and the user's group tooltip follow the server's `UserUpdated`. While waiting, the row shows its busy state (A113); a refusal shows the server's error and the check mark goes back.
- [x] AC4: A change made elsewhere (the administration, another admin) shows in the submenu the next time it opens.
- [x] AC5: The texts exist in German and English, and the menu fits at 360 px.

### Tests (TDD)

1. `ServerViewModelTests > "UserGroups_OnlyForWeakerUsersWithAssignRight"` (AC1, AC2)
   - Given: a fake server where I have `GroupsAssign`, a weaker user, a stronger user and myself, with groups of which one is not assignable by me.
   - Expected: `Groups` is empty for myself and the stronger user; for the weaker user it lists all groups, ticked by `GroupIds`, the not assignable one disabled.
2. `ServerViewModelTests > "UserGroupToggle_SendsAssign_ConfirmedByUserUpdated"` (AC3)
   - Given: the weaker user, toggling a group on and then off.
   - Expected: `AssignGroup(fp, id)` then `UnassignGroup(fp, id)` are sent; the pending ends with the matching `UserUpdated`; an `Error` for the request reverts the tick and shows the error.
3. `ServerViewModelTests > "LastAdmin_CannotBeUnticked"` (AC2)
4. `UiSmokeTests > "UserContextMenu_GroupsSubmenu"` (AC1, AC4): a headless right click on a user row opens the menu with the submenu and its check marks.
5. `LocalizationTests`, `ResponsiveTests > "UserContextMenu_Fits360"` (AC5)

(as built: `GroupToggle` stays where it is, it is already public and both use it, so nothing moved; the method is `ServerViewModel.SetUserGroup` returning the `Pending` like `Move` / `ServerMute`. The administration has no extra last-admin guard on its toggles: an Admin-group member has every right, so the server never judges one weaker than me, and the rank rule alone keeps the last admin's Admin group; test 3 checks that row has no groups. The submenu reuses the existing text `Ui_Groups` ("Gruppen" / "Groups"), so no new strings. A plain `ClickEvent` does not tick a checkable `MenuItem` in Avalonia 11.3, only the pointer click does, before it runs the command; the smoke test therefore clicks with the mouse.)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 and 3 (red): `GroupIds`, `CanBeModeratedByMe` and `Groups` on `UserViewModel`, reusing `GroupToggle` and the last-admin guard from the administration.
2. Test 2 (red): `SetUserGroupAsync` with `SendConfirmed`.
3. Test 4 (red): the submenu in `MainWindow.axaml`.
4. Test 5: the texts.

### Out of Scope

- Offline users (only the administration lists them)
- Creating or editing groups

---

## Package 120: Move users by drag and drop and menu

**Goal:** Users with the move right drag a user onto a channel, or pick the channel straight from the user's context menu, to move them.

**Dependencies:** none

**Affected files:**
- `src/OVS.Client/Views/UserDrag.cs` (new): a small drag helper on the channel tree for user rows. It reuses from `ReorderDrag` the threshold, pointer capture, Esc, `Snapshot` and the preview styles (`dragPreview`), and highlights the channel row under the pointer instead of opening a gap.
- `src/OVS.Client/Views/MainWindow.axaml.cs` (change): attach it to `ChannelItems` beside the channel `ReorderDrag` (lines 25 to 26)
- `src/OVS.Client/Views/MainWindow.axaml` (change): "Verschieben" becomes a submenu with the channels (line 268); a style for the highlighted drop target
- `src/OVS.Client/Styles/Controls.axaml`, `Styles/Motion.axaml` (change): the drop target highlight
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (change): `UserViewModel.MoveTargets` (the channels for the submenu), `CanDrag` (my row, or `CanMove`), `ServerViewModel.DropUser(user, channel)` (move, or join for myself)
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change)
- `tests/OVS.Tests/Client/UserDragTests.cs` (new), `ServerViewModelTests.cs`, `UiSmokeTests.cs` (change)

### Context

Moving exists today only as a dialog: `UserViewModel.Move` (`ServerViewModel.cs:851-857`) opens `Dialogs.PickChannel` with every other channel and calls `owner.Move(SessionId, channelId)`, which sends `MoveUser` and waits for the `UserUpdated` (`ServerViewModel.cs:530-531`). The server checks `UserMove`, rank (never myself, only weaker users), separators, locks and the limit (`ChannelCommands.cs:290-320`); moving into the same channel is a silent no-op, so the client must not offer it.

Channel reordering uses `ReorderDrag` (Package 96), built for gaps inside one list; it deliberately ignores presses on nested user rows (`ReorderDrag.cs:66`), so a user drag can live beside it on the same tree. After a move, `ItemMotion` flies the row's picture from the old to the new place (`Fly="True"`); after a drop the row is already where the pointer is, so like the channel reorder the drop holds that flight (`ItemMotion.Hold`).

### Acceptance Criteria

- [x] AC1: "Verschieben" in the user's context menu is a submenu listing the channels (current channel and separators left out); clicking one moves the user. The dialog is no longer used for this.
- [x] AC2: Dragging a user row I may move (`CanMove`) more than the threshold lifts a preview (A112) that follows the pointer; the channel row under the pointer is highlighted when it is a valid target (not the user's own channel, not a separator).
- [x] AC3: Dropping on a highlighted channel moves the user there (`MoveUser`); the row lands without flying a second time. Dropping elsewhere, or Esc, cancels and nothing is sent.
- [x] AC4: Dragging my own row and dropping it on a channel joins that channel (`JoinChannel`, with its password prompt for a password lock as today).
- [x] AC5: A refused move (full, locked, rank) shows the server's error as with the menu; the user stays where they were.
- [x] AC6: Rows of users I may not move do not start a drag; a channel drag (Package 96) still works and is not started by a press on a user row.
- [x] AC7: The texts exist in German and English; the submenu fits at 360 px.

### Tests (TDD)

(as built: `ItemMotion.Hold` only holds a change of order for one dispatcher round, but a moved user only flies once the server confirms, so the drop holds the flight with a new `ItemMotion.Ground(user, until)`: that entry does not fly while the drop waits for the server; refused or confirmed, the next move flies again. The whole channel block (its row and its users) is the drop target, the channel row lights up (`Border.row.dropTarget`). `ReorderDrag.Lift` and `Follow` are shared with `UserDrag`. Without the dialog the moved user's row spins until the server shows the move (Package 98), and `Dialog_MoveTo` is gone; "Verschieben" / "Move to" lost the dots of a dialog. `DropUser_OwnRowJoins_InvalidTargetsSendNothing` (ServerViewModelTests) covers the password prompt of AC4 and the invalid targets; `UiSmokeTests > "UserContextMenu_MoveSubmenu"` clicks the submenu for real. `UserDrag` needs no `ReducedMotion`: nothing makes room, so there is no gap to animate, and the tilt is a static pose. Review: a drag ends when the button is no longer held or the row has left the tree (the user left, a sync made a new row), and `DropUser` ignores a user no longer on the server; `Ground` counts per entry, so overlapping drops of the same user do not let each other fly)

1. `ServerViewModelTests > "MoveTargets_OtherChannelsWithoutSeparators"` (AC1)
2. `UserDragTests > "DragUserOntoChannel_HighlightsAndMoves"` (AC2, AC3)
   - Given: a headless main window with a fake server, a weaker user in the Lobby, me with `UserMove`.
   - Expected: past the threshold a preview is in the overlay; over the Raid row it is highlighted; on release `MoveUser(user, Raid)` is sent and no flight ghost appears.
3. `UserDragTests > "DropOutsideOrEsc_SendsNothing"` (AC3)
4. `UserDragTests > "OwnRow_DropJoinsChannel"` (AC4)
5. `UserDragTests > "NoMoveRight_NoDrag_ChannelDragUnchanged"` (AC6), with `ReorderDragTests` staying green
6. `ServerViewModelTests > "RefusedMove_ShowsError"` (AC5)
7. `LocalizationTests`, `ResponsiveTests` (AC7)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Test 1 (red): `MoveTargets` and the submenu.
2. Tests 2 to 5 (red): `UserDrag` with the shared pieces of `ReorderDrag` (threshold, capture, preview, `Snapshot`, `ReducedMotion`), the highlight style and `DropUser`.
3. Test 6 and 7.

### Out of Scope

- Dragging several users at once
- Dragging users into the administration or between servers

---

## Package 121: Key bindings apply at once, self test with push-to-talk

**Goal:** Keys set on the settings page work at once, so the self test in push-to-talk mode plays back while the key is held.

**Dependencies:** none

**Affected files:**
- `src/OVS.Client/ViewModels/SettingsViewModel.cs` (change): a change of the key bindings (add, edit, remove, the "Taste festlegen" banner) triggers `Preview()`, like the audio settings
- `src/OVS.Client/ViewModels/MainViewModel.cs` (change): `LivePreview` also sets `Keys.Bindings`; `CloseSettings` without saving restores `Settings.KeyBindings`, as it restores the audio with `ApplyLive(Settings)`
- `src/OVS.Client/Views/SettingsView.axaml` (change, only if test 3 shows it): the self test button must not toggle when the push-to-talk key is Space or Enter
- `tests/OVS.Tests/Client/MainViewModelTests.cs`, `SendPathTests.cs`, `SettingsTests.cs` (change)

### Context

The self test itself is fine: with `Audio.SelfTest` set, `AudioEngine.Decide` (`AudioEngine.cs:267-280`) takes `keys.PttDown` and loops the frame back. The bug is in the keys: `Keys.Bindings` is written in exactly one place, `MainViewModel.Prepare` (`MainViewModel.cs:209`), which runs only on Save. The settings page edits its own list (`SettingsViewModel.cs:66, 241-268`), and `LivePreview` (`MainViewModel.cs:629-633`) pushes mode, gain, volume and threshold live, but not the keys.

So switching to push-to-talk on the page works at once, but a key set on the page does nothing until Save, and Save closes the page, which ends the self test. A profile without a saved push-to-talk key, or one that was in voice activation before, is silent in the self test. The tests did not see it: `SelfTest_PlaysOwnVoice_SendsNothing_OthersSilent` runs in voice activation, `SelfTest_FollowsMode_PushToTalkWithoutKeySilent` only checks silence, and `keys.Simulate` bypasses the bindings (`KeyPoller.cs:118`).

A second possible cause is unproven: with Space or Enter as the push-to-talk key, the focused self test button may be clicked by the key and stop the test. Test 3 checks it before anything is changed.

### Acceptance Criteria

- [x] AC1: A key binding added, changed or removed on the settings page is in `Keys.Bindings` at once, before Save.
- [x] AC2: Closing the settings without saving restores the saved bindings; saving keeps the new ones.
- [x] AC3: In push-to-talk mode, with the self test running and a key set on the page, holding the key plays the own voice back and releasing it stops it.
- [x] AC4: A push-to-talk key that is also a key of the focused self test button (Space, Enter) does not stop the self test.

### Tests (TDD)

(as built: AC4 was real. A focused Button clicks on Space (on release) and Enter (on press), so a push-to-talk key on Space stopped the self test; the self test button now swallows Space and Enter while one of them is bound on the page, unbound they still click it. The seam for test 2 is an optional `KeyPoller(Func<int, bool>? keyState)`, the keyboard by default, given before the poll thread starts. The view test lives in `SettingsTests` as an `[AvaloniaFact]` and checks Space and Enter one at a time, since two clicks cancel out)

1. `MainViewModelTests > "SettingsPage_KeyBindingLive_RevertedOnCancel"` (AC1, AC2): the reproduction, red first
   - Given: saved settings in voice activation without a push-to-talk key; the settings page open; push-to-talk chosen and a key added through a stubbed `EditKeyBinding`.
   - Expected: `vm.Keys.Bindings` contains the new key at once; after leaving without saving it is gone again; after saving it stays.
2. `SendPathTests > "SelfTest_PushToTalk_KeyHeld_PlaysBack"` (AC3): a tone engine in push-to-talk mode with `SelfTest = true`; the binding resolved from a simulated key state (not `Simulate(ptt:)`), so the bindings are part of the test.
3. `SettingsTests > "SelfTest_SpaceAsPttKey_DoesNotToggleButton"` (AC4): headless, the self test button focused, Space pressed and released.

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Test 1 (red): bindings in `LivePreview` and the revert in `CloseSettings`.
2. Test 2: if `KeyPoller` cannot be fed a raw key state in tests, add a small seam for the polled state (not for the resolved actions).
3. Test 3: only if it is red, keep the self test button from reacting to the push-to-talk key.

### Out of Scope

- Push-to-mute's behaviour (Package 125, which builds on the live bindings)

---

## Package 122: Fast with many users

**Goal:** A server with hundreds of users and many speakers stays smooth in both displays, because a server message only updates what it changes.

**Dependencies:** none

**Affected files:**
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (change): `ApplyMessage` updates only the affected users and channels instead of `Rebuild` for every message; `RefreshSpeaking` touches only users whose state changed
- `src/OVS.Client/ViewModels/CollectionSync.cs` (change): no `IndexOf` inside the loop (linear instead of quadratic)
- `src/OVS.Client/ViewModels/MainViewModel.cs` (change): `SyncAudioFlags` and the server icon hash run only when their inputs change, not on every `StateChanged`
- `src/OVS.Client/Audio/AudioEngine.cs` (change): `SpeakersChanged` at most every 100 ms, not up to 50 times per second
- `src/OVS.Client/Views/MainWindow.axaml.cs` (change): `FitSidebar` only when a channel name, a count or the sidebar's content width can change, not after every message
- `src/OVS.Client/Views/ItemMotion.cs` (change): the FLIP place tracking (`WatchPlaces`, `LayoutUpdated` per list) records places only around a change, not on every layout pass
- `src/OVS.Client/Views/BusySpinner.axaml.cs` (change): no tree walk for every visibility change of every control
- `src/OVS.Client/Styles/Motion.axaml` (change): the speaking ring breathes without animating a blurred shadow (the glow stays still, only a light layer pulses)
- `tests/OVS.Tests/TestSupport/FakeServers.cs` (change): `FakeServers.Large` with 40 channels and 400 users
- `tests/OVS.Tests/Client/PerformanceTests.cs` (new)

### Context

Every state message rebuilds the whole tree: `ServerViewModel.ApplyMessage` calls `Rebuild` (`ServerViewModel.cs:234, 271-312`), which filters all users per channel (channels × users), updates every `UserViewModel` (with `string.Join` for the group names), syncs every list with an `IndexOf` per item, and raises `StateChanged`. Each `StateChanged` then:
- rebuilds the speaker gain table (`MainViewModel.SyncAudioFlags`)
- re-hashes the server icon (`TrackServerIcon.Sync`)
- refits the sidebar, which walks every channel container with all its user rows and measures every name (`MainWindow.FitSidebar`)

Speaking adds load: `AudioEngine.Mix` raises `SpeakersChanged` whenever the set changes (up to 50 times per second) and every 100 ms while anyone talks, and each goes over all users. In the animated display, every speaking user's ring breathes forever with a blurred shadow (`Motion.axaml:291-323`), and every user list subscribes `LayoutUpdated` (`ItemMotion.cs:339-356`), so during joins and leaves every frame walks every list. The channel tree is not virtualized (`MainWindow.axaml:174-317`); with the fixes above it does not need to be for a few hundred users, and virtualizing would collide with the motion code.

The tests only know `FakeServers.Crowded` (8 channels, 20 users). Wall-clock limits are fragile on CI (see the timing work after Package 118), so the tests count work instead of timing it.

### Acceptance Criteria

- [x] AC1: A `UserUpdated` for one user (mute, deafen, rename) updates only that user's view model; a move updates only the two channels involved; no other `UserViewModel.Update` runs.
- [x] AC2: A message that changes no channel name or count does not refit the sidebar; one that does refits it once per frame at most.
- [x] AC3: The speaker gain table and the icon hash are rebuilt only when users join or leave, a user volume changes, or the icon changes.
- [x] AC4: With 400 users and 20 speakers, the UI gets at most 10 speaking updates per second, and each touches only users whose speaking state changed.
- [x] AC5: Without joins, leaves or moves, no list's place tracking runs on a layout pass.
- [ ] AC6: In the animated display, a speaking ring animates no blurred shadow; the look stays close to Package 108 (manual check against its screenshots).
- [x] AC7: All existing motion tests stay green in both displays, including `JoinLeaveStorm_EndsBounded` on `FakeServers.Large`.
- [ ] AC8: Manual check: a `FakeServers.Large`-sized server (debug tool or a local server with test clients) at 1100 px, 20 speakers, animated display. CPU of the client before and after is recorded in the package notes.

### Tests (TDD)

(as built: `ServerViewModel.ApplyMessage` updates a `UserJoined` (also of a session already shown, which is moved), `UserLeft` or `UserUpdated` of another user (or of myself with the same channel, rights and groups) incrementally: that user's view model, its move targets, and the lists of the channels it leaves or comes into (only when its channel or name changed); everything else still runs `Rebuild`, which now groups the users by channel once instead of filtering them per channel. `StateChanged` stays an `Action`; the new `ServerViewModel.LastChange` (a `[Flags] StateChange`: `Members`, `Names`, `Sidebar`, `Tree`) says what it was about. `MainViewModel` rebuilds the gain table on `Members`, a volume change or a new server; the logo is only checked when the server's hash changes; `ChatViewModel` syncs private tabs on `Members`/`Names` and the channel tab and rights on `Tree`; `MainWindow` refits on `Sidebar` (also set when my own name below the tree changes) and coalesces queued fits into one. Test counters are internal, the client now has `InternalsVisibleTo` for `OVS.Tests` (in the csproj): `ServerViewModel.UserUpdates`, `SpeakingTouches`, `MainViewModel.GainTableBuilds`, `IconHashes`, `MainWindow.SidebarFits`, `ItemMotion.TracksPlaces(list)`; `LastChange` and `StateChange` are public like `Rebuild`. The throttle is `SpeakerReports` in `AudioEngine.cs` (at most every 5 mixer frames, a change waits for the next slot, its frame count capped); test 4 drives it frame by frame and `RefreshSpeaking` on a `ManualTimeProvider`, instead of a mixer fed with opus. `RefreshSpeaking` only looks at the users shown speaking, myself and those reported since, and drops expired reports. FLIP place tracking subscribes `LayoutUpdated` only after a change of the list, of a list inside it or of a row starting or stopping to leave, and unsubscribes once the change's effects are over (`Motion.Slow` plus the stagger) and the places stay the same or no layout pass came for 100 ms (not while a move still waits for its layout pass; leaving clears a pending move). Starting to track, it first notes where the laid-out rows sit now, so a row that grew meanwhile does not make the others glide from stale places (`ItemMotionTests.ReflowWhileIdle_ThenReorder_OnlyMovedRowsGlide`). `CollectionSync.Sync` works from sets of the shown entries and their keys, places leaving entries in one pass, and without a leave delay removes gone entries first instead of moving them to the end step by step. `BusySpinner` keeps the attached spinners that are visible themselves and only checks those on a visibility change. The speaking ring keeps its glow still; the avatar inside breathes (scale 1 to 1.06, inside the ring's 1 px padding), so `LiveMotionTests` now measures the avatar's scale; whether that looks close enough to Package 108 is still to be checked by eye, so AC6 stays open. Added after review: `PerformanceTests.RandomMessages_IncrementalMatchesRebuild` (fixed seed, 50 runs of 30 messages about others and myself on `Large`, each compared with a twin server that rebuilds, over every public state of the channels and users), `UserJoined_KnownSession_ShownOnce`, and `CollectionSyncTests` for pure reorders, leaving entries with moves and revived entries; `Tree()` in the tests compares all of it. `JoinLeaveStorm_EndsBounded` is a theory over `Crowded` and `Large`; its storm users start at id 1000, clear of `Large`'s users. AC8 (CPU before and after on a large server) is a manual check and not done yet.)

1. `PerformanceTests > "UserUpdated_UpdatesOnlyThatUser"` (AC1): count `UserViewModel.Update` calls through a test counter on `FakeServers.Large`.
2. `PerformanceTests > "MuteChange_DoesNotRefitSidebar_RenameDoes"` (AC2)
3. `PerformanceTests > "StateChanged_GainTableAndIconHashOnlyWhenNeeded"` (AC3), `ServerIconTrackerTests > "Sync_FirstCall_ChecksOnce"`, `"Sync_SameHashAsLastTime_ChecksNothingMore"` and `"Sync_HashChanged_ChecksAgain"` (AC3), `EngineSyncTests > "SyncConnection_NoServerYet_BuildsNoGainTable"`, `"SyncConnection_SameServerAsTheLastTable_BuildsNoNewTable"`, `"SyncConnection_OtherServerThanTheLastTable_BuildsTheTableOfTheOtherOne"` and `"SyncGains_Twice_CountsTwoBuilds"`, `EngineSyncWiringTests > "ShowServer_UserWithSavedVolume_BuildsTheGainTableOnceGivesThemTheirGainAndOthersOne"` and `"ShowServer_AnotherServerAfterOne_BuildsTheGainTableAgain"` (AC3)
4. `PerformanceTests > "ManySpeakers_SpeakingUpdatesThrottledAndScoped"`, `PlaybackLoopTests > "Run_VoiceClearedAfterItsReport_RunsOnUntilTheSilenceIsReported"` (AC4): a mixer fed with 20 speakers on a manual clock.
5. `PerformanceTests > "NoMove_NoPlaceTracking"`, `PlaceTrackerTests > "Change_FlipListInsideAFlipList_OuterListTracksPlaces"`, `EntryTrackerTests > "Add_EnterWithoutFlip_NotesNoPlaces"`, `"Change_EnterListInsideAFlipList_OuterListTracksPlaces"` (AC5)
6. `MotionStyleTests > "SpeakingRing_NoAnimatedShadow"` (AC6)
7. `MotionCoverageTests.JoinLeaveStorm_EndsBounded` on `FakeServers.Large`, and the whole suite (AC7)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. `FakeServers.Large` and test 1 (red): incremental `ApplyMessage`; keep `Rebuild` for the first fill and for messages that change the channel list.
2. Tests 2 and 3 (red): the `StateChanged` subscribers react to what changed (an argument on the event, or separate events).
3. Test 4 (red): throttle `SpeakersChanged`; `RefreshSpeaking` by changed users.
4. Tests 5 and 6 (red): place tracking around changes, the ring without an animated shadow, `BusySpinner` without the global walk.
5. Test 7, then the manual check.

### Out of Scope

- Virtualizing the channel tree (not needed for a few hundred users; revisit only if the manual check still stutters)
- The audio threads and idle cost (Package 124)

---

## Package 123: Voice filters

**Goal:** Noise suppression and automatic gain clean the microphone signal before it is sent, each switchable in the settings.

**Dependencies:** none

**Affected files:**
- `src/OVS.Client/Audio/NoiseSuppressor.cs` (new): RNNoise through P/Invoke (`rnnoise_create`, `rnnoise_process_frame`, `rnnoise_destroy`), two 480-sample halves per 960-sample frame, samples scaled to and from the 16-bit range RNNoise expects
- `src/OVS.Client/Audio/AutoGain.cs` (new): a slow automatic gain toward a target level (about -20 dBFS), raising at most +18 dB, never above -3 dBFS peak, frozen while there is no voice so noise is not pumped up
- `src/OVS.Client/Audio/CapturePipeline.cs` (change): in `Feed` after the input gain (line 87) and before `Vad.Process` (line 88): noise suppression, then automatic gain
- `src/OVS.Client/Audio/AudioEngine.cs` (change): `ApplyLive` and `StartCapture` pass the two switches
- `src/OVS.Client/Settings/ClientSettings.cs` (change): `NoiseSuppression` (default true), `AutoGain` (default false)
- `src/OVS.Client/ViewModels/SettingsViewModel.cs`, `Views/SettingsView.axaml` (change): a "Sprachfilter" card after the volume card with two switches and a one-line hint each, with live preview
- `src/OVS.Client/OVS.Client.csproj` (change): the native `rnnoise.dll` (win-x64) shipped with the client (from a maintained NuGet package or built from xiph/rnnoise and checked in)
- `THIRD-PARTY-NOTICES.md` (change): RNNoise, BSD-3-Clause
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change)
- `tests/OVS.Tests/Client/VoiceFilterTests.cs` (new), `SettingsTests.cs`, `SendPathTests.cs`, `LocalizationTests.cs` (change)

### Context

The send path is: WASAPI capture → `CapturePipeline.ToMono` → resample to 48 kHz → 960-sample frames (20 ms) → input gain with a hard clip (`CapturePipeline.cs:87`) → `VoiceActivityDetector` (energy only, 300 ms hangover) → Opus (`OpusCodec.cs`, VOIP, 32 kbps, FEC on). There is no noise suppression, no automatic gain and no echo cancellation (A18), and no DSP package; only Concentus and NAudio.

RNNoise works on 48 kHz mono 10 ms frames, so it fits the format exactly. Putting the filters before the voice activation means the activation, the level meter and the self test all see the cleaned signal, so keyboard and fan noise no longer open voice activation. `ApplyLive` already pushes gain and threshold into the pipeline under its lock without restarting the device; the switches use the same path.

### Acceptance Criteria

- [x] AC1: With noise suppression on, steady fan-like noise (pink noise at -35 dBFS) leaves the pipeline at least 10 dB quieter; with it off the signal is unchanged.
- [x] AC2: With noise suppression on, voice activation at its default threshold no longer opens on that noise alone.
- [x] AC3: With automatic gain on, a quiet voice-like signal (-40 dBFS) rises toward -20 dBFS within 3 s and never exceeds -3 dBFS; a loud signal is not raised; during silence the gain does not grow.
- [x] AC4: Both switches apply live on the settings page (and in the self test) without restarting the device; Cancel reverts them.
- [x] AC5: Defaults: noise suppression on, automatic gain off, also for settings files without the keys; saved and loaded values round-trip.
- [x] AC6: The filters cost less than 1 ms per 20 ms frame on the dev machine (measured once, recorded in the notes), and nothing is allocated per frame.
- [ ] AC7: The published single-file client contains and loads the native library; a missing library turns noise suppression off with one log line instead of crashing.
- [x] AC8: The texts exist in German and English, the card fits at 360 px, and `THIRD-PARTY-NOTICES.md` lists RNNoise.
- [ ] AC9: Manual check: in the self test with a fan or typing near the microphone, the noise is gone and the voice stays clear.

### Tests (TDD)

1. `VoiceFilterTests > "NoiseSuppression_LowersSteadyNoise_OffIsUnchanged"` (AC1)
2. `VoiceFilterTests > "NoiseSuppression_VadStaysClosedOnNoise"` (AC2): `CapturePipeline.FromTone`-style synthetic noise through the whole pipeline.
3. `VoiceFilterTests > "AutoGain_RaisesQuiet_CapsLoud_HoldsInSilence"` (AC3)
4. `SendPathTests > "ApplyLive_SwitchesFilters_WithoutRestart"` (AC4)
5. `SettingsTests > "VoiceFilters_DefaultsAndRoundTrip"` and `"VoiceFilters_LivePreviewAndCancel"` (AC4, AC5)
6. `VoiceFilterTests > "Filters_AllocateNothingPerFrame"` (AC6): `GC.GetAllocatedBytesForCurrentThread` around 100 frames.
7. `VoiceFilterTests > "MissingNativeLibrary_CaptureRunsWithoutSuppressor"`, `NativeCaptureFiltersTests > "AttachTo_LibrariesMissing_NoFiltersAndOneLogLineEach"` (AC7)
8. `LocalizationTests`, `ResponsiveTests > "Settings_FitAt360_480_600_1100"` (AC8)

(as built: RNNoise comes from the NuGet package `YellowDogMan.RRNoise.NET` 0.1.9 (MIT wrapper, maintained by Yellow Dog Man Studios; its `runtimes/win-x64/native/rnnoise.dll` is built from their fork of xiph/rnnoise, BSD-3, exports the xiph API; sha256 of the shipped win-x64 DLL: `12e19bf7a18d13e092a5fbe5a7c5b2081f5e7b56f6d77aeab5837335f44ceedf`), referenced with `IncludeAssets="native"`, so only the native library ships and the own `NoiseSuppressor` calls it. It loads the library and resolves the three exports once per process with `NativeLibrary` and function pointers instead of `DllImport`, all before the first `rnnoise_create`; any failure to load or resolve gives null instead of an exception on the capture thread, `AudioEngine` then logs one line per engine ("Rauschunterdrückung aus: ...") and sends unfiltered; tests name a missing library through `AudioEngine.NoiseLibrary`. A disposed suppressor passes frames unchanged; it has no finalizer because the pipeline owns it and frees it under its lock. The suppressor is created with every capture start, also while switched off, so the switch acts at once. RNNoise adds about 10 ms of latency (its overlapping 10 ms window). `rnnoise.dll` imports `VCRUNTIME140.dll`; checked: none of the other native parts of the client (Skia, HarfBuzz, ANGLE, the .NET runtime) do, so on a Windows without the Visual C++ runtime it does not load and noise suppression is off with the log line. The package has no win-arm64 build either; there the same logged fallback applies. `AutoGain` raises only (0 to +18 dB toward -20 dBFS), 0.15 dB per frame up and 1 dB down, ramped across the frame, limited so no sample goes above -3 dBFS (a signal already that loud is left alone, the gain never goes below 1), and frozen below -45 dBFS so room noise is not pulled up while noise suppression is off; switched on again live it starts over at 0 dB. AC1/AC2 are worded for pink noise because RNNoise hardly touches white noise: measured, white noise at -35 dBFS comes out at about -37 dBFS (about 2 dB) and RNNoise rates it as voice, while pink noise at -35 dBFS comes out about 15 dB quieter (about -50 dBFS) and no longer opens voice activation. AC6: 0.21 ms per 20 ms frame for both filters together (Release, dev machine, 1000 frames after warm-up, measured once); test 6 checks the two filters only, the rest of `Feed` still allocates (Package 124). AC7: the published single-file exe contains `rnnoise.dll` (found in the bundle) and the missing-library fallback is tested; that the published exe loads it is not proven without starting the GUI, so AC7 stays open for a manual check. `CapturePipeline.Dispose` no longer joins a tone thread that never started, so a pipeline fed by hand can be disposed. The "Abbrechen" revert is the existing `ApplyLive(Settings)` in `MainViewModel.CloseSettings`; `ApplyLive_SwitchesFilters_WithoutRestart` checks that applying the saved settings switches the running capture back.)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Choose the RNNoise binary: a maintained NuGet package with a win-x64 `rnnoise.dll`, or build xiph/rnnoise once and check the DLL in; check its license and that single-file publish carries it (`IncludeNativeLibrariesForSelfExtract` is already set).
2. Tests 1, 2, 6 and 7 (red): `NoiseSuppressor` and its place in `Feed`.
3. Test 3 (red): `AutoGain`.
4. Tests 4 and 5 (red): settings, live preview, the card.
5. Test 8, the notices, the timing measurement and the manual check.

### Out of Scope

- Echo cancellation (A131)
- Voice effects (pitch, robot) and filters on what others send

---

## Package 124: Low CPU and RAM while idle

**Goal:** While nothing happens the client uses almost no CPU, opens the audio devices only when needed and allocates nothing per audio frame.

**Dependencies:** Package 123 (both change `CapturePipeline`)

**Affected files:**
- `src/OVS.Client/ViewModels/MainViewModel.cs` (change): `Audio.Configure` no longer runs at start (`MainViewModel.cs:132, 215`); the devices open on connect, the self test and while the settings page shows the level meter, and close again (A132)
- `src/OVS.Client/Audio/AudioEngine.cs` (change): the playback loop waits on a signal while no speaker, no sound and no self test is active instead of mixing silence every 5 ms (`AudioEngine.cs:304-334`)
- `src/OVS.Client/Audio/Mixer.cs`, `JitterBuffer.cs`, `OpusCodec.cs`, `SendPath.cs`, `CapturePipeline.cs` (change): reused buffers instead of new arrays per frame; a speaker's decoder and jitter buffer survive pauses between talk spurts (today they are dropped after 500 ms and recreated)
- `src/OVS.Client/Audio/OpusCodec.cs` (change): encoder complexity set explicitly (measured choice, likely 5 to 8 instead of the default 10) and DTX on, so silence in push-to-talk and voice activation costs nothing
- `src/OVS.Client/Input/KeyPoller.cs`, `Input/KeyBindings.cs` (change): one poll allocates nothing (no LINQ per poll)
- `src/OVS.Client/Logging/ClientLog.cs` or the call site in `MainViewModel.cs:261-264` (change): the per-message log line is written off the UI thread through a queue, with one open file instead of open and close per line
- `src/OVS.Client/OVS.Client.csproj`, `.github/workflows/release.yml` (change, only what the measurement keeps): `PublishReadyToRun` for faster start and less JIT; `ConcurrentGarbageCollection=false` and `TieredCompilation` settings only if they lower idle CPU or RAM measurably
- `tests/OVS.Tests/Client/IdleCostTests.cs` (new), `SendPathTests.cs`, `ReceivePathTests.cs`, `MainViewModelTests.cs` (change)

### Context

Why the client can use more than Discord while idle, from the code:
- **Audio devices are open from the start.** The constructor configures the audio (`MainViewModel.cs:132` → `Audio.Configure` at :215), so the microphone runs (about 50 callbacks per second) and the playback thread mixes silence about 50 times per second (`Mix` with `Mixer.Tick`), even when not connected. Discord opens its voice engine only in a voice channel.
- **Allocations per audio frame:**
  - capture allocates in `ToMono`, the resampler (even at equal rates) and the chunker
  - the mixer allocates a buffer and two lists per tick
  - the decoder allocates per frame and is recreated for every talk spurt
  
  That keeps the garbage collector busy.
- **The key poller allocates on every 10 ms poll** (LINQ in `KeyActions.Resolve` and `KeyStateTracker.Update`).
- **Every server message is logged on the UI thread**, opening and closing the log file per line.
- **Opus runs as managed code** (Concentus) at the default complexity. Discord's voice engine is native (WebRTC).
- **RAM:** a .NET single-file app carries the runtime, the JIT and Skia. Without ReadyToRun every method is compiled at start.

  Discord spreads itself over several processes, so a fair comparison in the Task Manager adds up all of Discord's processes.

The UI itself is already idle: no looping animation runs without its state (A117, `MotionCoverageTests` checks it).

### Acceptance Criteria

- [x] AC1: After start and while not connected (settings closed), no capture or output device is open and no playback thread runs; connecting or starting the self test opens them, disconnecting closes them. The settings page's level meter opens the microphone while it is shown.
- [x] AC2: Connected with nobody speaking and no sound playing, the playback loop does not mix; it wakes within one frame when a voice packet or a sound arrives (no cut first syllable).
- [ ] AC3: Capture per frame while not sending, a mixer tick with speakers, a decode and a key poll each allocate 0 bytes in steady state.
- [x] AC4: A speaker who pauses and talks again keeps the same decoder and jitter buffer (no new one per talk spurt).
- [x] AC5: Logging a server message does not touch the disk on the UI thread; log lines keep their order and none are lost on exit.
- [x] AC6: Opus encoder complexity is set explicitly and DTX deliberately off; the sent quality stays at 32 kbps voice (the existing send and receive tests stay green).
- [ ] AC7: Manual check, recorded in the package notes: idle connected CPU and working set of the client before and after (Task Manager, 2 minutes, nobody talking), and the same for Discord (all its processes added up) on the same machine; publish settings are kept only if they improve these numbers or start time.

### Tests (TDD)

1. `MainViewModelTests > "Devices_WhileNeeded_OpenAndCloseAgain"` (AC1): a fake device factory counts open and close through start, connect, self test, settings page and disconnect. `EngineSyncTests > "SyncConnection_OnlyConnectedSet_TurnsOnlyTheEnginesConnectedOn"`, `EngineSyncWiringTests > "ShowServer_NotMutedNotDeafened_EngineIsConnectedUnmutedWithTheSpeakRight"` and `"ShowServer_NoServerAfterOne_EngineIsNotConnectedMutedWithoutSpeakRight"` (AC1)
2. `ReceivePathTests > "Playback_IdleDoesNotMix_WakesOnVoice"`, `PlaybackLoopTests > "Kick_LoopWaitingWhileNeeded_WakesItToPlayTheTone"`, `PlaybackLoopTests > "Wake_NoLongerNeeded_ClosesTheSpeakerAndEnds"`, `PlaybackLoopTests > "Kick_NothingQueuedNorNeeded_OpensTheSpeakerThenClosesItAndEnds"` (AC2): count `Mix` calls over 500 ms idle (0), then one voice packet is mixed within one frame.
3. `IdleCostTests > "SteadyState_AllocatesNothing"` (AC3): `GC.GetAllocatedBytesForCurrentThread` around 100 capture frames, mixer ticks, decodes and key polls after warm-up.
4. `ReceivePathTests > "SpeakerPause_KeepsDecoder"` (AC4); a speaker who left loses the decoder, the self test's voice and everyone still there keep theirs: `EngineSyncTests > "SyncMembers_SpeakerWhoLeft_LosesTheirDecoder"`, `"SyncMembers_SpeakerStillThere_KeepsTheirDecoder"`, `"SyncMembers_SelfTestVoice_KeepsItsDecoderThoughNoUserHasItsId"` and `"ClearSpeakers_SpeakersPresent_DropsEveryDecoder"`, `EngineSyncWiringTests > "StateChanged_UserLeft_DropsTheirDecoder"`, `"StateChanged_AnotherUserLeft_KeepsTheDecoderOfThoseStillThere"`, `"StateChanged_NobodyCameOrWent_DropsNoDecoder"` and `"DisconnectAsync_SpeakersPresent_DropsEveryDecoder"` (AC4)
5. `MainViewModelTests > "MessageLog_OffUiThread_InOrder"` (AC5)
6. `SendPathTests > "Encoder_ComplexitySet_DtxOff"` (AC6)

Test command: `dotnet test tests/OVS.Tests`

(as built: the engine takes an `IAudioDeviceFactory` (WASAPI by default; `MainViewModel` passes a test's fake through). The microphone follows `Connected`, `SelfTest` and the new `LevelMeter` (set while the settings page shows) and opens and closes on a pool thread, so the UI never waits for a device; a failure arrives as a notice via `AudioEngine.Warning`. The playback thread and the speaker run while connected or in the self test, otherwise only until queued tones played (a preview or the "Getrennt" tone opens the speaker and closes it after the last frames played); `Configure` reopens what is open, so a device switched while connected applies at once. The loop sleeps on an `AutoResetEvent` while no speaker is busy (frames waiting or concealing), no tone plays, no self test runs and no speaker report is due; a voice packet, the loopback, a tone or a flag change wakes it, and the own 20 ms clock restarts so it does not catch up on the pause. A speaker's jitter buffer and decoder survive pauses (`Mixer.IdleTicksUntilRemoved` 3000 mixed frames, a minute) and are dropped when the user leaves (`Mixer.RemoveLeft` on a member change). Capture (`ToMono` into a kept buffer, the resampler's reused output and pass-through at equal rates, `FrameChunker.Take` with one frame buffer), the mixer's frame and speaker list, the decoder's PCM buffer and the key poll (`KeyActionSet` bits instead of sets, no LINQ) allocate nothing; a speaker report copies the list at most every 100 ms. AC3 is not ticked: Concentus allocates inside every Opus call (measured about 28 KB per decode and 100 KB per encode), so `IdleCostTests` checks that a decode and a mixer tick allocate exactly what Concentus does and nothing more, and capture while not sending and a key poll allocate 0 bytes; only native libopus would remove the rest (out of scope). Opus complexity 5, measured with a scratch benchmark on a 30 s speech-like signal (harmonics with a gliding pitch, syllable envelope, noise bursts) at 32 kbps with FEC: encode time per 20 ms frame 0.10 ms at 2 to 3, 0.17 ms at 4 to 5, 0.23 ms at 6 to 7, 0.28 ms at 8 to 10; log spectral distance 7.74, 7.81, 7.66 and 7.71 dB, so no measurable loss anywhere. 5 is the lowest value that keeps the voice coder's interpolated spectral envelopes and warped noise shaping (from 4) and the music coder's pitch pre-filter (from 5), which a synthetic signal cannot judge. DTX is deliberately off (`Encoder_ComplexitySet_DtxOff`): push-to-talk and voice activation already stop sending in pauses, so it would only act within a held key, and there Opus sends a comfort-noise frame about every 400 ms, which a receiver plays as a lone frame plus concealment, a faint pulse; it also takes a steady tone (the test tone) for noise after a second and stops sending it. Concentus throws when asked to decode a 1-byte frame (what other Opus senders send with DTX), so `VoiceDecoder` conceals such a packet, and any broken one, instead of throwing on the playback thread (`OneBytePacket_Concealed`). The client log queues lines (stamped at the call) and a pool thread writes each batch with one opening of the file via a new `LogFiles.Append` overload; the file is not held open, so readers that lock it against writing keep working. `Flush` runs on dispose, at exit and in the crash handler; tests that read the log at once call it; an unexpected error costs one batch, never the log. After review: `Dispose` sets `running` under the device lock and a microphone open still queued then does nothing (`AudioEngine_DisposedWhileMicOpening_StaysClosed`); live values from the settings page apply to a microphone that opens later (`AudioEngine_LiveSettingsBeforeMicOpen_Applied`), until the next `Configure`; the speaker closes 40 ms after its buffer emptied, so the end of the last tone is not clipped; device errors on the background threads become notices; `IdleCostTests` needs one clean window out of three, as the runtime may allocate on the thread now and then. Not measured: AC7 needs the client started with a window, which was not done here, so the publish settings stay unchanged and the Task Manager check is left. Changed besides: `Mixer_Silence500ms_SpeakerRemoved` became `SpeakerPause_KeepsDecoder`, and `Mixer_LoudInput_ClampedToUnit` allows float rounding at the threshold like the other limiter tests.)

### Steps

1. Measure first (AC7 "before"), so every change can be judged.
2. Tests 1 and 2 (red): devices on demand, the idle playback loop.
3. Tests 3 and 4 (red): buffer reuse and decoders kept per speaker.
4. Tests 5 and 6 (red): the log queue, the encoder settings.
5. Publish settings one at a time with the measurement; keep only what helps.
6. AC7 "after".

### Out of Scope

- The UI's cost per message and per speaker (Package 122)
- Replacing Concentus with native libopus (only if the measurement shows Opus is the main cost; a separate package then)

---

## Package 125: Push-to-mute works like mute

**Goal:** Holding the push-to-mute key mutes exactly like the mute button, with icon, sound and server state, and releasing it restores the previous state.

**Dependencies:** Package 121 (bindings set on the settings page work at once)

**Affected files:**
- `src/OVS.Client/ViewModels/MainViewModel.cs` (change): subscribe `Keys.Changed` for `PushToMute`; on press, if not muted, mute like `ToggleMute` (`ServerViewModel.cs:557-565`: `SelfMuted`, the `MicOff` sound, `SetSelfState`); on release undo exactly that
- `src/OVS.Client/ViewModels/ServerViewModel.cs` (change): a `HoldMute(bool held)` beside `ToggleMute` that remembers whether it muted
- `src/OVS.Client/Input/KeyPoller.cs` (change): a single modifier key (Shift, Ctrl, Alt, left or right) can be captured as a binding (today `IsModifier` skips them, `KeyPoller.cs:78, 107`): captured on release when no other key was pressed meanwhile
- `src/OVS.Client/Input/KeyBindings.cs` (change, if needed): a chord that is only a modifier
- `tests/OVS.Tests/Client/MainViewModelTests.cs`, `KeyBindingTests.cs` (change)

### Context

Push-to-mute is registered with the poller like every other action (`KeyBindings.cs:7, 37, 54-64`) and is read only by the audio engine (`keys.MuteHeld` in `AudioEngine.cs:282`), which stops sending while it is held. Nothing else reacts: `Keys.Changed` has no subscriber outside the audio debug log, so the mute icon, the sounds and the server state stay as they are. In push-to-talk mode it only matters while push-to-talk is held too, so it looks dead. A key set for it on the settings page did nothing until Save (fixed by Package 121). Shift, Ctrl or Alt alone, common choices for such a key, cannot be bound at all.

The mute button (`ToggleMute`, `ServerViewModel.cs:557-565`) sets `SelfMuted`, plays `MicOff` / `MicOn` and sends `SetSelfState`; the server broadcasts it, so others see the muted icon.

### Acceptance Criteria

- [x] AC1: Holding the push-to-mute key while unmuted: the own mute icon shows, the `MicOff` sound plays, the server gets `SetSelfState(Muted: true)` and nothing is sent. Releasing it: unmuted again, `MicOn`, `SetSelfState(Muted: false)`.
- [x] AC2: Holding it while already muted (or deafened) changes nothing, and releasing it does not unmute.
- [x] AC3: Pressing the mute button while holding the key makes that choice stick: releasing the key does not undo it.
- [x] AC4: Not connected, holding the key only stops sending (the self test included); nothing is sent to a server.
- [x] AC5: Shift, Ctrl or Alt alone can be bound to push-to-mute (and any other action) on the settings page and is resolved like any other key.

### Tests (TDD)

1. `MainViewModelTests > "PushToMuteHeld_MutesLikeButton_ReleaseRestores"` (AC1): a fake server collects `SetSelfState`; the binding resolved from a simulated key state.
2. `MainViewModelTests > "PushToMute_AlreadyMuted_NoChange"` (AC2)
3. `MainViewModelTests > "MuteButtonWhileHeld_Sticks"` (AC3)
4. `MainViewModelTests > "PushToMute_NotConnected_OnlyStopsSending"` (AC4)
5. `KeyBindingTests > "Capture_SingleModifier_OnRelease"` and `"Resolve_SingleModifierChord"` (AC5)

Test command: `dotnet test tests/OVS.Tests`

(as built: `MainViewModel` takes an optional `keyState` (passed to `KeyPoller`); the `MainViewModelTests` fixture uses a new `TestSupport/FakeKeys`, so a real key pressed during the run no longer counts. Test 2 is a theory (muted, deafened). The "nothing happened" checks of tests 2 and 3 wait for a later press of a bound deafen key: key events reach the UI thread in order. Test 1 checks the server state, tone and requests but not `Audio.SelfMuted`, which the fake server (not connected via `ConnectAsync`) does not sync; that nothing is sent is the engine's `MuteHeld` (`SendPathTests.PushToMute_Held_SendsNothing`). Test 4 and `Resolve_SingleModifierChord` passed at once: the engine already stopped on `MuteHeld` and `Resolve` already handles a chord whose key is a modifier; they stay as guards. Capture stores the side key (`0xA0`-`0xA5`, e.g. "Umschalt links") without modifiers; two modifiers pressed together count as neither until all are up. A single-modifier binding is also active while that modifier is part of another chord (Shift+F1 holds Left Shift). Deafening while the key holds the mute remembers "muted" for the undeafen, like any mute. AltGr cannot be bound alone: Windows reports it as Left Ctrl plus Right Alt, two modifiers together. Added after review: `MuteButtonWhileHeld_TalkKeyTapped_Sticks` (only the push-to-mute key's own edges count, as `Changed` also fires for the talk keys) and `PushToMute_HeldBeforeConnecting_MutesOnConnect` (a new server applies the held key at once).)

### Steps

1. Tests 1 to 4 (red): `HoldMute` and the `Keys.Changed` subscription.
2. Test 5 (red): modifier-only capture and resolve.

### Out of Scope

- A push-to-deafen action
---

## Package 126: UDP Hello is retried

**Goal:** A session whose first UDP Hello is lost still gets voice, and one whose UDP mapping breaks later gets it back.

**Dependencies:** none

**Affected files:**
- `src/OVS.Client/Net/VoiceClient.cs` (change): `Start` sends one `Hello` (line 40), afterwards only pings. The ping timer sends `Hello` instead of `Ping` while not `Reachable`, first every 500 ms for 5 s, then at the normal ping interval; after 15 s without any answer `Reachable` goes back to false and Hellos start again
- `src/OVS.Client/ViewModels/MainViewModel.cs` (change): `LogUdpReachability` also logs "UDP wieder erreichbar" when UDP recovers after having been lost
- `tests/OVS.Tests/Client/ClientNetTests.cs`, `MainViewModelTests.cs` (change), `tests/OVS.Tests/TestSupport/` (a lossy UDP relay, new)

### Context

`VoiceClient.Start` sends exactly one `Hello`. The server binds the session's UDP endpoint only on a `Hello`, or on any packet once an endpoint is bound (`UdpVoiceServer.cs:110`), and `SendAsync` returns early without an endpoint (line 146). If that one Hello is lost (a Wi-Fi hiccup, a NAT that drops the first packet of a new mapping), every later ping is accepted but binds nothing and gets no answer: the session has no voice at all, and the client only logs "UDP nicht erreichbar" after 5 s (`MainViewModel.cs:559-570`). A NAT mapping that changes later already recovers (a bound endpoint is updated by every authenticated packet); only the unbound case is stuck. The fix stays on the client: the server already binds on `Hello`.

### Acceptance Criteria

- [x] AC1: With the first Hello dropped, the session is reachable and voice flows in both directions within 1 s.
- [x] AC2: With every packet dropped for 20 s and then delivered again, `Reachable` turns false after 15 s and true again within one ping interval of the network coming back.
- [x] AC3: Once reachable, the client sends only pings (no Hello flood), at the normal interval.
- [x] AC4: The client log shows "UDP nicht erreichbar" once and "UDP wieder erreichbar" once for such an outage.

### Tests (TDD)

Reproduction first: test 1 must be red before anything changes.

1. `ClientNetTests > "FirstHelloLost_StillGetsVoice"` (AC1): a test server behind a lossy UDP relay that drops the first client packet; expected: `Reachable` within 1 s, and a voice frame from a second client arrives.
2. `ClientNetTests > "UdpOutage_Recovers"` (AC2): the relay drops everything for 20 s, then passes; `VoiceClient` takes a `TimeProvider` (default `TimeProvider.System`), driven by `ManualTimeProvider`.
3. `ClientNetTests > "Reachable_SendsOnlyPings"` (AC3): count Hello and Ping packets at the relay.
4. `MainViewModelTests > "UdpOutage_LoggedOnceEachWay"` (AC4); the log by itself on a fake UDP server: `VoiceLinkTests > "LogReachability_ServerAnswersAskedTwice_LogsNowReachableOnce"`, `"LogReachability_AnswersStopForFifteenSeconds_LogsNotReachableAfterNowReachable"` and `"LogReachability_ServerAnswersAgainAfterAnOutage_LogsReachableAgain"` (AC4)

(as built: `VoiceClient` takes an optional `TimeProvider` and runs one timer every 500 ms in both states instead of the 5 s ping timer; each tick decides: unreachable sends a Hello on every tick for 5 s after start or after a loss, then one per ping interval (5 s); reachable sends a ping once 5 s passed since the last packet, and on every tick while an answer is overdue by more than one ping interval, so two lost pings in a row (an idle listener on lossy Wi-Fi) are asked again instead of counting as a loss (`TwoLostPings_StayReachable`, added after review); 15 s after the last answer `Reachable` turns false and the fast Hellos start again. Any authenticated packet from the server (ping answer or voice) counts as an answer. The 500 ms tick costs two timer callbacks per second while idle, which does nothing measurable next to Package 124's numbers; switching the period with the state was not worth the extra code. `TestSupport/LossyUdpRelay` sits between one client and the server, drops the next N client packets or everything both ways, and counts the client's packets by type (also dropped ones) and the server packets passed back. All three `ClientNetTests` drive the client's clock with `ManualTimeProvider` and the network in real time, so "within 1 s" (AC1) is one 500 ms retry in client time, and AC2 is checked at 14.5 s (still reachable), 15 s (lost), 20 s (still lost, network back) and after one more ping interval (reachable). `Reachable_SendsOnlyPings` waits for each ping's answer before moving the clock on, as real time would; without that, the answers all arrived after the clock had passed 15 s and the client counted the line as lost; likewise the outage tests wait until the relay has seen (and dropped) the outage's packets before letting packets through again. All four were red before the change (test 1 timed out waiting for `Reachable`). `MainViewModel` gets two internal test hooks, `VoiceRelay` (UDP goes there instead of the server) and `VoiceTime`. The log: a loss after UDP worked logs the existing "UDP nicht erreichbar: Sprache kommt nicht an (Firewall oder NAT?)" at the next UI tick (no 5 s wait, `Reachable` already waited 15 s), and the recovery logs "UDP wieder erreichbar"; a connection that was unreachable from the start and then reaches the server still logs "UDP erreichbar", as before. The server is unchanged. In the first full run `ChannelLockTests.LockChange_NobodyMovedOut` and `BackupTests.UploadAndRestore_OnlyAdminGroup_CheckedPerChunk` failed once under load (server tests, untouched); both passed alone and in a second full run.)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Test 1 (red), then the Hello retry in `VoiceClient`.
2. Tests 2 and 3 (red): the "lost again" detection and the interval switch.
3. Test 4 (red): the log line.

### Out of Scope

- Voice when UDP stays blocked (Package 141)
- Measuring the round trip and loss (Package 133)

---

## Package 127: Louder senders

**Goal:** Every microphone arrives at a normal speech level, because automatic gain is on for everyone and also raises very quiet voices.

**Dependencies:** none

**Affected files:**
- `src/OVS.Client/Settings/ClientSettings.cs` (change): `AutoGain` default true; `SettingsVersion` and migration step 1 in `Load` (A135)
- `src/OVS.Client/Audio/NoiseSuppressor.cs` (change): `Process` returns the frame's voice probability (the lower of the two halves' `rnnoise_process_frame` results), or null when disposed
- `src/OVS.Client/Audio/AutoGain.cs` (change): `Process(frame, voiceProbability)`: with a probability, speech means `>= 0.5`; without one the `VoiceFloorDb` (-45) rule stays; `MaxGainDb` 18 → 30
- `src/OVS.Client/Audio/CapturePipeline.cs` (change): `Feed` passes the probability from the suppressor to the gain (lines 115-116); the hard clip of the input gain (line 113) becomes a soft clip above -3 dBFS
- `src/OVS.Client/Views/SettingsView.axaml`, `Localization/Strings.resx`, `Strings.en.resx` (change): the automatic gain hint calls it recommended
- `tests/OVS.Tests/Client/VoiceFilterTests.cs`, `SettingsTests.cs`, `SendPathTests.cs` (change)

### Context

Testers had to set others to 200 %, and one still found someone too quiet. Fluxer has no such complaint because its browser's WebRTC gain control is on by default (`fluxer_app/src/features/voice/state/VoiceSettings.ts:434`, `autoGainControl = true`) and decides what is speech with a voice detector, so a quiet microphone reaches a normal level before it is sent. In OVS automatic gain is off by default (A131, `ClientSettings.cs:88`). Even when on, it never raises speech below -45 dBFS (`AutoGain.cs:18`), exactly the quiet senders behind the complaint, and it stops at +18 dB. The -45 dBFS floor exists so room noise is not pulled up while noise suppression is off; with RNNoise running (default on), RNNoise's own voice probability is a better speech test. Settings files write every key, so existing users have `"AutoGain": false` stored and a new default alone would not reach them (A135).

### Acceptance Criteria

- [x] AC1: A new settings file and a file without `SettingsVersion` (even with `"AutoGain": false`) load with automatic gain on and `SettingsVersion` set; a file already migrated keeps the user's choice.
- [x] AC2: With noise suppression on, a voice-like signal at -50 dBFS rises to -20 dBFS ±3 dB within 5 s; with it off, the same signal is not raised (floor rule), while -40 dBFS still rises as today.
- [x] AC3: With noise suppression on, pink noise at -50 dBFS (rated as no speech) is not raised.
- [x] AC4: The gain never exceeds +30 dB, and no sample exceeds -3 dBFS after it.
- [x] AC5: An input gain of 200 % on a signal peaking at 0.8 is soft-clipped: no sample above 1, samples below -3 dBFS unchanged.
- [x] AC6: Nothing is allocated per frame (`Filters_AllocateNothingPerFrame` stays green).
- [ ] AC7: Manual check, recorded in the package notes: a tester who was too quiet before is understandable at 100 % on the other side, and their level meter shows the raised level.

### Tests (TDD)

1. `SettingsTests > "Migration_TurnsAutoGainOnOnce"` (AC1): three files (none, old with `false`, migrated with `false`).
2. `VoiceFilterTests > "AutoGain_RaisesVeryQuietVoice_WithSuppression"` (AC2): a voice-like synthetic signal (harmonics with gliding pitch and syllable envelope, as in the Package 124 benchmark) at -50 dBFS through `NoiseSuppressor` and `AutoGain`, and the same without suppression.
3. `VoiceFilterTests > "AutoGain_IgnoresNoiseRatedAsNoise"` (AC3)
4. `VoiceFilterTests > "AutoGain_CapsAt30dB_AndPeak"` (AC4)
5. `SendPathTests > "InputGain_SoftClips"` (AC5)

(as built: `ClientSettings.Migrations` is an array of steps (`Action<ClientSettings>`), `CurrentVersion` is its length, so Packages 128 and 129 each add one line. `SettingsVersion` defaults to `CurrentVersion` for a new object (new profile, damaged file replaced by defaults) and is set to 0 in `IJsonOnDeserializing.OnDeserializing`, so a file without the key counts as 0. `Load` runs the missing steps after `MigrateKeys` and `Clamp` and saves the file only when a step ran; a failed save (read-only profile) is ignored and the steps run again next time with the same result; a file from a newer client (higher version) is left alone, a negative version counts as 0. `Program.cs` loads the settings once before `MainViewModel` does (for the language), so the migration runs and saves there. `NoiseSuppressor.Process` returns the lower rating of the two 10 ms halves; `AutoGain.Process(frame, voiceProbability = null)` uses `>= 0.5` when given and the -45 dBFS floor otherwise; `CapturePipeline.Feed` passes the rating only while noise suppression is on; a frame shorter than 10 ms gets no rating (null) rather than 1. `SettingsViewModel.ToSettings` carries the loaded `SettingsVersion` over, so saving the settings page does not set a newer client's file back (`ToSettings_KeepsSettingsVersion`), and the debug API's `/settings` keeps the current version when the body has none. The input gain moved into `CapturePipeline.ApplyGain(frame, gain)`: up to 100 % the plain multiplication with the clamp at ±1 as before (bit-transparent at 100 %); above 100 % samples below -3 dBFS are only multiplied and those above it become `knee + (1 - knee) * tanh((|x| - knee) / (1 - knee))` (same slope at the knee, flattening toward 1, which only rounding reaches). RNNoise rated the synthetic voice as speech, so no recorded sample was needed: the voice (eight harmonics of a pitch gliding 100 to 180 Hz, 200 ms syllables with 50 ms ramps and 50 ms gaps, syllable frames at -50 dBFS RMS) got a rating of at least 0.5 in 203 of 250 frames (median 1.0); pink noise at -50 dBFS was rated 0.13 at most, so the gain stayed at 0 dB. That the rating overrides the floor is checked with a fixed rating of 0.1 on pink noise at -40 dBFS (0 dB, while the floor rule alone raises it by more than 10 dB). Louder steady pink noise needed a second guard: at -42, -40 and -35 dBFS RNNoise rated 15 to 216 of 250 frames at least 0.5 after 1 to 4 s (two seeds), and at -40 dBFS the gain reached +9 dB, where the old floor rule held still. So `NoiseSuppressor.Process` returns 0 for a frame it cut by more than `NoiseCutDb` = 4 dB (energy before and after, summed in the loops it already has, nothing allocated). Measured cut of the speech-rated frames: the synthetic voice at -50, -40 and -30 dBFS median -0.1 dB, 90 % at most 3.8 dB (max 12.6, at the syllable ends); pink noise at -40 dBFS at least 4.1 dB (median 35 to 37), at -35 dBFS at least 4.9 dB (median 9), at -30 dBFS at least 8.2 dB. 6 dB, as first suggested, would have let pink noise at -35 dBFS through (frames cut by 4.9 to 6 dB). With the guard, pink noise at -50, -40 and -35 dBFS (two seeds) keeps the gain at exactly 0 dB. The guard leaves 183 of the voice's 250 frames as speech instead of 203, which at 0.15 dB per frame gave only +27.4 dB after 5 s (-24.5 dBFS, outside AC2), so the rise rate went from 0.15 to 0.2 dB per frame: +30 dB within 3 s of speech; the -50 dBFS voice now ends at -21.5 dBFS. A quiet voice in a loud room, which RNNoise also cuts by more than 4 dB, is not raised either; the manual check (AC7) should listen for both. AC4: a -70 dBFS sine rated as speech stops at exactly +30 dB, and louder words at that gain (-30, -15, -9 dBFS) peak at -3 dBFS at most; a word already peaking above -3 dBFS is left alone, as before (the gain never attenuates). AC6: `Filters_AllocateNothingPerFrame` now also runs `ApplyGain` and passes the rating, still 0 bytes. The settings page needed no layout change: only the hint text says "Empfohlen" / "Recommended". Existing tests that assumed automatic gain off by default or the +18 dB cap (`VoiceFilters_DefaultsAndRoundTrip`, `VoiceFilters_LivePreviewAndCancel`, `ApplyLive_SwitchesFilters_WithoutRestart`, `AutoGain_RaisesQuiet_CapsLoud_HoldsInSilence`, `AutoGain_SwitchedOnAgain_StartsOver`) were adjusted: -40 dBFS now rises to -20 dBFS (+20 dB) instead of stopping at -22 dBFS. AC7 (manual check with a tester) is not done: it needs people on a real server and is left open for the user.)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Test 1 (red): `SettingsVersion`, the migration framework and step 1.
2. Tests 2 and 3 (red): the probability from `NoiseSuppressor` into `AutoGain`. If RNNoise rates the synthetic voice below 0.5, use a short recorded speech sample with a free license in `tests/OVS.Tests/TestData` instead and note its source.
3. Tests 4 and 5 (red): the new cap and the soft clip.
4. Manual check AC7.

### Out of Scope

- The receiving side's volume curve (Package 128)
- Changing the fixed voice boost ×2 (A136)

---

## Package 128: Fluxer's volume curve

**Goal:** The volume sliders follow Fluxer's curve, so 200 % is four times as loud and the lower half of the slider is fine-grained.

**Dependencies:** Package 127 (settings migration)

**Affected files:**
- `src/OVS.Client/Audio/VolumeCurve.cs` (new): `Gain(float sliderValue)` per A137 and `Combine(user, output)` with the ×12 cap
- `src/OVS.Client/Audio/Mixer.cs` (change): `SetSpeakerGains` and `Volume` take slider values; `Tick` applies `Combine(speaker, output)` × `DefaultVoiceBoost` (lines 71-76)
- `src/OVS.Client/Audio/AudioEngine.cs`, `ViewModels/MainViewModel.cs` (change, if needed): pass slider values unchanged
- `src/OVS.Client/Settings/ClientSettings.cs` (change): migration step 2 (A135)
- `tests/OVS.Tests/Client/ReceivePathTests.cs`, `SettingsTests.cs` (change), `VolumeCurveTests.cs` (new)

### Context

The mixer multiplies linear values: per-user volume 0 to 2, output volume 0 to 2, voice boost 2 (`Mixer.cs:71-76`), so 200 % is only +6 dB per slider. Fluxer maps 100 to 200 % exponentially to ×1 to ×4 and 0 to 100 % logarithmically, multiplies user and output gain and caps the total at ×12 (`fluxer_app/src/features/voice/utils/VoiceVolumeUtils.ts`, `state/ParticipantVolume.ts:205-208`). The user chose to reset stored values above 100 % to 100 % once, as Fluxer did when it changed its curve (`recalibrateStoredVoiceVolumes`).

### Acceptance Criteria

- [x] AC1: The curve gives ×1 at 100 %, ×4 at 200 %, ×2 at 150 %, about ×0.25 at 50 %, 0 at 0 %, and rises monotonically.
- [x] AC2: Per-user 200 % and output 200 % give ×12 (capped), not ×16.
- [x] AC3: A frame mixed with per-user 200 % has four times the amplitude of the same frame at 100 % (below the limiter's threshold).
- [x] AC4: Migration step 2 resets per-user volumes and the output volume above 100 % to 100 % once; values up to 100 % are kept; a migrated file is not touched again.
- [x] AC5: The sliders still show 0 to 200 %, and the stored values are the slider values.

### Tests (TDD)

1. `VolumeCurveTests > "Gain_KnownPoints_Monotonic"` (AC1)
2. `VolumeCurveTests > "Combine_CapsAtTwelve"` (AC2)
3. `ReceivePathTests > "Mixer_UserVolume200_FourTimes"` (AC3); existing mixer tests that assume a linear ×2 are adjusted.
4. `SettingsTests > "Migration_ResetsVolumesAbove100Once"` (AC4)
5. `SettingsTests > "Volume_SliderValueStored"` (AC5)

(as built: `VolumeCurve.Gain(volume)` takes the stored slider value (1 = 100 %, 0 to 2), the unit the settings, `SettingsViewModel` and the per-user slider already use, so nothing between the sliders and the mixer changed: `AudioEngine` still sets `Mixer.Volume` to `OutputVolume` and `MainViewModel.SyncGains` still passes `VolumeFor` per session. Up to 100 % it is `10^((v - 1) * 1.2)`, above it `4^(v - 1)`, and 0 gives exactly 0 (the formula alone would leave -24 dB at 0 %). `Combine(user, output)` multiplies both and caps at `MaxGain` = 12. `Mixer.Tick` now applies `Combine(speaker, Volume) * DefaultVoiceBoost` per speaker in the one loop it already had, instead of the speaker gain in the loop and boost times volume over the sum; the self test's own voice has no per-speaker entry and gets the output volume as before; sounds are mixed after `Tick` and stay outside the curve. Migration step 2 is `ClientSettings.ResetVolumesAbove100` (output volume and every per-user volume above 1 set to 1; a per-user entry at 1 is removed, as `SetVolume` does for 100 %). Older mixer tests on the linear meaning were adjusted: `Mixer_Volume_ScalesBoostedVoice` (50 % is now -6 dB against the decoded frame, not 0 dB), `Mixer_Volume200_DoublesVoice_Limited` renamed `Mixer_Volume200_FourTimes_Limited` (+12 dB), `Mixer_SpeakerGain_OnlyThatSpeaker` (150 % for twice). The new tests failed to build before `VolumeCurve` existed. The debug API and the sliders needed no change. AC5 is checked on the stored values and the settings page's 0 to 200 % clamp; the per-user slider already clamps to 0 to 200 % (`ServerViewModel`).)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 and 2 (red): `VolumeCurve`.
2. Test 3 (red): the mixer uses it; adjust older mixer tests.
3. Tests 4 and 5 (red): migration step 2.

### Out of Scope

- The input gain (stays linear 0 to 200 %, as in Fluxer)
- Lowering the voice boost (A136)

---

## Package 129: Automatic voice gate

**Goal:** Voice activation adapts its threshold to the room by itself and no longer flickers at the end of words.

**Dependencies:** Package 127 (settings migration; the gate sees the signal after automatic gain)

**Affected files:**
- `src/OVS.Client/Audio/SendPath.cs` (change): `VoiceActivityDetector` (lines 27-60) gets a noise floor tracker (2 s of frame levels, 10th percentile), the automatic threshold, the 0.7 hysteresis and a hold of 9 frames (A138); properties `Automatic` and `ThresholdInUseDb`
- `src/OVS.Client/Settings/ClientSettings.cs` (change): `VadAutomatic` (default true), migration step 3
- `src/OVS.Client/Audio/AudioEngine.cs` (change): passes the switch live like `VadThresholdDb`
- `src/OVS.Client/ViewModels/SettingsViewModel.cs`, `Views/SettingsView.axaml` (change): a switch "Automatisch" above the threshold slider (slider disabled while on), the level meter marks the threshold in use
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change)
- `tests/OVS.Tests/Client/SendPathTests.cs`, `SettingsTests.cs` (change)

### Context

Voice activation is an energy gate with a fixed threshold (default -40 dBFS, range -60 to -10) and a 300 ms hangover (`SendPath.cs:29`), without hysteresis: speech hovering around the threshold at the end of a word opens and closes the gate repeatedly, and users must tune the threshold to their room. Fluxer's gate (`fluxer_app/src/features/voice/utils/voiceGateProcessor.worklet.js`) learns the noise floor continuously, sets the threshold at three times it, and only starts its 180 ms hold below 0.7 × the threshold. The pre-roll frame in `CapturePipeline.Feed` already does what Fluxer's 10 ms lookahead does.

### Acceptance Criteria

- [x] AC1: In automatic mode with steady noise at -55 dBFS, the threshold settles within 2 s at 3 × (1.5 × the floor) of that noise (about -42 dBFS ±2 dB), and the noise alone does not open the gate.
- [x] AC2: When the noise rises to -45 dBFS, the threshold follows within 2 s; speech 15 dB above the noise opens the gate in both cases.
- [x] AC3: A level that drops below the threshold but stays above 0.7 × it does not start the hold; the gate closes 180 ms after the level fell below 0.7 × the threshold.
- [x] AC4: In the first 0.3 s the manual threshold applies.
- [x] AC5: Migration step 3: a stored -40 turns automatic on, any other stored value stays manual; new files are automatic.
- [x] AC6: The switch applies live, the slider is disabled while automatic, and the meter shows the threshold in use.

### Tests (TDD)

1. `SendPathTests > "Vad_Hysteresis_Hold180ms"` (AC3): frame sequences at chosen levels; the existing hangover tests change from 15 to 9 frames.
2. `SendPathTests > "VadAuto_SettlesOnNoise_StaysClosed"` (AC1)
3. `SendPathTests > "VadAuto_FollowsRisingNoise_OpensOnSpeech"` (AC2)
4. `SendPathTests > "VadAuto_ManualDuringLearning"` (AC4)
5. `SettingsTests > "Migration_VadAutoOnlyForDefault"` (AC5)
6. `SettingsTests > "VadAuto_LiveAndSliderDisabled"` (AC6)

(as built: `VoiceActivityDetector` always tracks the floor, also in manual mode, so switching to automatic live uses what it has learned: a ring of the last 100 frame levels in dB (`FloorFrames`), copied into a second array of the same size and sorted with `Span.Sort` per frame (at most 100 floats, nothing allocated; a full sort instead of the planned partial sort, marked with a `ponytail:` comment, Fluxer's histogram would be the upgrade), the 10th percentile is element `count / 10`. In dB the rule is `p10 + 20 log10(4.5)` (about +13.1 dB), clamped to -60 to -28 dBFS. The lower bound deviates from A138 (Fluxer's -110): with noise suppression on (the default) RNNoise leaves near-silence between words, so the 10th percentile can sit near -120 dBFS and a threshold near -107 dBFS would let breaths, RNNoise leftovers and clicks open the gate; the automatic gain (Package 127) holds speech near -20 dBFS, so -60 never blocks speech. Steady room noise above about -73 dBFS gives a threshold above -60 and is not affected; the hysteresis is `20 log10(0.7)` (about -3.1 dB). `ThresholdInUseDb` is the manual `ThresholdDb` while `Automatic` is off or fewer than `LearningFrames` = 15 frames (0.3 s) were seen, so frames 0 to 14 use the manual one and frame 15 the learned one. The hold: a level at or above the threshold opens and sets the hold to `HangoverFrames` = 9; while open, a level between 0.7 × and the threshold sets it to 9 again (Fluxer resets its silence counter the same way); below 0.7 × it counts down, so the gate closes on the tenth quiet frame. The detector's own default stays manual (`Automatic` false), the engine passes `ClientSettings.VadAutomatic` (default true) in `StartCapture` and `ApplyLive`. Migration step 3 is `VadAutomatic = VadThresholdDb == DefaultVadThresholdDb` (exactly -40, the untouched default); the debug API's `/settings` applies the same rule to a body without `vadAutomatic`, so a script that sets a threshold keeps it. The settings page: a check box "Automatisch" / "Automatic" with a one-line hint above the threshold row; the slider and the dBFS text bind to `MeterThresholdDb` (the reported threshold in use while automatic, else the manual one; its setter only writes the manual threshold) and the slider is disabled through `IsManualThreshold`; `IsAboveThreshold` compares with it. The threshold in use reaches the page with the level: `AudioEngine.InputThresholdDb` (the capture's `ThresholdInUseDb`, else the manual one) is read in `MainViewModel.OnInputLevel` on the capture thread and set on `SettingsViewModel.ThresholdInUseDb` with the level; the page starts with the manual value. Below -60 dBFS the slider's thumb sits at its left end. The page still fits at 360 px. Tests: AC1 `VadAuto_SettlesOnNoise_StaysClosed` (white noise at -55 dBFS, 100 frames: threshold -44 to -40, never open), AC2 `VadAuto_FollowsRisingNoise_OpensOnSpeech` (-40 opens on -55 noise; after 100 frames of -45 noise the threshold is -34 to -30, the noise stays closed for 50 more frames, -30 opens), AC3 `Vad_Hysteresis_Hold180ms` (manual -40: -42 does not open, keeps an open gate open for 30 frames, -44 holds 9 frames and closes on the tenth) and the renamed `Vad_SilenceInactive_LoudActive_Hangover9Frames`, `VadAuto_NearSilentFloor_IgnoresQuietClicks` (a floor at -115 dBFS gives exactly -60, a 60 ms burst at -70 stays closed, -50 opens), `VadAuto_ContinuousSpeech_StaysOpen` (3 s of 200 ms syllables between -30 and -18 dBFS with 40 ms or 20 ms gaps of -55 dBFS noise: open in every frame; with 20 ms gaps under 10 % of the frames the floor is the syllables' ramps and the threshold stops at -28, the hysteresis and the hold keep it open), AC4 `VadAuto_ManualDuringLearning` (manual -60 for frames 0 to 14, learned at frame 15, back to manual when switched off), AC5 `Migration_VadAutoOnlyForDefault`, AC6 `VadAuto_LiveAndSliderDisabled` (rendered settings page: slider disabled and at the reported -47 while automatic, enabled and draggable when off, the preview carries the switch; the engine switches the running capture without a restart and reports its threshold), `MainViewModelTests.SettingsPage_ShowsThresholdInUse` (the steady test tone sets the page's threshold in use to -28 dBFS through `OnInputLevel`), `DebugApiTests.Settings_WithoutVadAutomatic_ManualUnlessDefault` (the debug API rule), and `ResponsiveTests.Settings_FitAt360_480_600_1100` now also switches to voice activation and checks that the check box, its hint, the meter and the slider fit. `Filters_AllocateNothingPerFrame` now also runs an automatic detector (warm-up 20 frames, past the learning, since the first sort initializes the sort helper once). Older tests that rely on a fixed threshold above a steady test tone set `VadAutomatic = false` (`PttKey_BelowThreshold_SendsOnlyInPttMode`, `SelfTest_FollowsMode_PushToTalkWithoutKeySilent`, `LevelMeter_MarksThreshold`): in automatic mode the tone itself would set the threshold, at most -28 dBFS, below the tone.)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Test 1 (red): hysteresis and the shorter hold, for the manual mode too.
2. Tests 2 to 4 (red): the floor tracker (a ring of 100 frame levels and a partial sort, nothing allocated per frame).
3. Tests 5 and 6 (red): setting, migration, settings page.

### Out of Scope

- Using RNNoise's voice probability for the gate (Fluxer's gate is energy only)
- Push-to-talk behaviour (Package 130)

---

## Package 130: Push-to-talk release delay

**Goal:** Releasing the talk key a moment early no longer cuts off the last syllable.

**Dependencies:** none

**Affected files:**
- `src/OVS.Client/Input/KeyPoller.cs` or `src/OVS.Client/Audio/AudioEngine.cs` (change): the push-to-talk and link push-to-talk state the engine sees stays true for the delay after release, at one place, so the engine, the self test and the "Sendet" text agree
- `src/OVS.Client/Settings/ClientSettings.cs` (change): `PttReleaseDelayMs` (default 20, clamped 0 to 2000)
- `src/OVS.Client/ViewModels/SettingsViewModel.cs`, `Views/SettingsView.axaml` (change): a slider "Loslassverzögerung" under the key bindings, shown in push-to-talk mode, live
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change)
- `tests/OVS.Tests/Client/SendPathTests.cs`, `SettingsTests.cs` (change)

### Context

`TransmitController` (`SendPath.cs:14-24`) sends in push-to-talk mode exactly while the key is down, polled every 10 ms (`KeyPoller.cs:7`). People release the key as the last word ends, so its tail is cut. Fluxer has a release delay of 20 to 2000 ms, default 20 ms (`fluxer_app/src/features/input/state/InputKeybind.ts:265-267`). Voice activation has its own hold (Package 129) and is not affected.

### Acceptance Criteria

- [x] AC1: With a delay of 300 ms, frames are still sent for 300 ms (15 frames) after the key is released, then sending stops.
- [x] AC2: Pressing the key again within the delay continues without a gap and without a second pre-roll frame.
- [x] AC3: Link push-to-talk has the same delay with its link target; push-to-mute and mute act at once.
- [x] AC4: The setting defaults to 20 ms, is clamped to 0 to 2000, saved, and applies live.

### Tests (TDD)

1. `SendPathTests > "PttRelease_KeepsSendingForDelay"` (AC1): `FakeKeys` and `ManualTimeProvider`.
2. `SendPathTests > "PttRepressWithinDelay_NoGap"` (AC2)
3. `SendPathTests > "LinkPttRelease_Delayed_MuteImmediate"` (AC3)
4. `SettingsTests > "PttReleaseDelay_DefaultClampLive"` (AC4)

(as built: the one place is `KeyPoller`. Its loop stores the time stamp (from an injectable `TimeProvider`, the system clock by default) when push-to-talk or link push-to-talk goes up, before it publishes the key state, so a reader never sees the key up without it. `PttDown` and `LinkPttDown` are true while the key is down or less than `ReleaseDelay` has passed since that release; `IsDown(action)` stays the bare key, and `MuteHeld` (push-to-mute) has no delay. Everything that decides about sending already reads `PttDown` and `LinkPttDown`: the engine's `Decide` per frame (sending and the self test), so `TransmitChanged`, the "Sendet" text and the own speaking ring follow it, as do the "no link right" hint and the debug API's `/state`. Pressing again within the delay keeps `PttDown` true throughout, so the capture pipeline never stops sending and adds no second pre-roll frame. Self mute, server mute, a muted channel and push-to-mute go into `Decide` as before and cut at once. The engine sets `KeyPoller.ReleaseDelay` from `ClientSettings.PttReleaseDelayMs` in `Configure` and `ApplyLive`, so the settings page's live preview, saving and cancelling reach it without a new path; the poller's own default is 0. `Changed` still fires on the key's edges, not when the delay ends; `AudioDebugLog` now logs the bare keys through `IsDown`. `ClientSettings.PttReleaseDelayMs` is an int, default 20, clamped to 0 to 2000 in `Clamp`; no migration step is needed (a file without it loads 20, `SettingsVersion` is unchanged and `ToSettings` keeps carrying it). The settings page has the slider "Loslassverzögerung" / "Release delay" (0 to 2000, value shown as "N ms") with a one-line hint at the end of the keys card, visible in push-to-talk mode and also with voice activation while a link push-to-talk key is bound (`ShowReleaseDelay`, since the delay acts on link push-to-talk in both modes), live through `Preview` like the other sliders; `LocalizationTests.Xaml_NoHardcodedText` now allows the unit format "{}{0:0} ms" like "dBFS". The page is visible at 360 px in its default push-to-talk mode, so `ResponsiveTests.Settings_FitAt360_480_600_1100` covers it unchanged. `AudioEngine.Decide` became internal: the send path tests feed frames by hand into a never started `CapturePipeline` whose decision is the engine's, 20 ms apart on a `ManualTimeProvider`, with keys from `FakeKeys` through real bindings, so frames are counted exactly: AC1 `PttRelease_KeepsSendingForDelay` (300 ms: 15 frames after the release, then none; 0 ms set live stops at once), AC2 `PttRepressWithinDelay_NoGap` (one frame per frame after pressing again, and 15 more after the second release), AC3 `LinkPttRelease_Delayed_MuteImmediate` (link target for the 15 delayed frames; self mute and push-to-mute within the delay stop the next frame), `SelfTest_PttRelease_PlaysBackForDelay` (the self test plays back the 15 delayed frames and nothing counts as sending), AC4 `PttReleaseDelay_DefaultClampLive` (default, clamp on load, saved, rendered slider live and clamped, hidden with voice activation until a link push-to-talk key is bound, the engine passes it to the poller in `Configure` and `ApplyLive`). The new tests failed to build before the setting, `ReleaseDelay` and the internal `Decide` existed.)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 to 3 (red): the delayed key state at one place.
2. Test 4 (red): setting and slider.

### Out of Scope

- Sounds and hints around the talk key (Package 131)

---

## Package 131: Talk feedback

**Goal:** Users are told when they talk while muted or deafened, and can hear their talk key act.

**Dependencies:** none

**Affected files:**
- `src/OVS.Client/ViewModels/MainViewModel.cs` (change): `Tick` sets `VoiceHint` (lines 551-556) to the muted texts when a talk key is held, or voice activation reports voice, while self-muted, deafened or server-muted, and keeps it 2 s (A140)
- `src/OVS.Client/Audio/AudioEngine.cs` (change): exposes "voice detected" while muted (the VAD runs anyway; the engine only does not send)
- `src/OVS.Client/Audio/Sounds.cs` (change): `SoundEvent.TalkKeyDown`, `TalkKeyUp` with short soft tones
- `src/OVS.Client/Settings/ClientSettings.cs` (change): both sounds muted by default in `SoundFor`
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change)
- `tests/OVS.Tests/Client/MainViewModelTests.cs`, `SoundTests.cs`, `LocalizationTests.cs` (change)

### Context

Neither OVS nor Fluxer tells users they are talking into a muted microphone, except Fluxer's modal for a talk key pressed while deafened. OVS already has a voice hint line for a muted channel and a missing link right (`MainViewModel.cs:551-556`), the natural place for it. Fluxer ships push-to-talk on and off sounds (`fluxer_app/src/media/sounds`); OVS's sound list (`Sounds.cs:4-14`) has none. They are off by default so that nobody's client suddenly beeps (A140).

### Acceptance Criteria

- [x] AC1: Holding push-to-talk while self-muted shows "Du bist stummgeschaltet" in the hint line; it disappears 2 s after release.
- [x] AC2: The same for deafened ("taub geschaltet") and server-muted ("vom Server stummgeschaltet").
- [x] AC3: In voice activation mode, speaking above the threshold while self-muted shows the hint.
- [x] AC4: Not muted, no hint appears; the existing hints keep their priority.
- [x] AC5: The two talk key sounds exist in the sound settings, are off by default, and when turned on play on press and release of push-to-talk and link push-to-talk.
- [x] AC6: The texts exist in German and English.

### Tests (TDD)

1. `MainViewModelTests > "TalkWhileMuted_ShowsHint_2s"` (AC1)
2. `MainViewModelTests > "TalkWhileDeafenedOrServerMuted_ShowsHint"` (AC2), a theory.
3. `MainViewModelTests > "VoiceWhileMuted_ShowsHint"` (AC3)
4. `MainViewModelTests > "NotMuted_NoHint"` (AC4)
   - the rules without a window: `VoiceHintsTests > "Evaluate_SelfMutedAndTalkKeyHeld_ShowsTheSelfMutedHint"`, `VoiceHintsTests > "Evaluate_TalkEndedJustUnderTwoSecondsAgo_StillShowsTheHint"`, `VoiceHintsTests > "Evaluate_TalkEndedTwoSecondsAgo_ShowsNothing"`, `VoiceHintsTests > "Evaluate_TalkGoesOnForFiveSeconds_KeepsShowingTheHint"`, `VoiceHintsTests > "Evaluate_MutedAgainAfterBeingUnmuted_DoesNotBringBackTheOldAttempt"` (AC1); `VoiceHintsTests > "Evaluate_ServerMutedAndTalkKeyHeld_ShowsTheServerMutedHint"`, `VoiceHintsTests > "Evaluate_DeafenedAndTalkKeyHeld_ShowsTheDeafenedHint"`, `VoiceHintsTests > "Evaluate_ServerMutedAndDeafenedAndSelfMutedAndTalkKeyHeld_ShowsTheServerMutedHint"`, `VoiceHintsTests > "Evaluate_DeafenedAndSelfMutedAndTalkKeyHeld_ShowsTheDeafenedHint"`, `VoiceHintsTests > "Evaluate_ServerMutedWhilePushToMuteIsHeld_ShowsTheServerMutedHint"` (AC2); `VoiceHintsTests > "Evaluate_MutedAndVoiceDetectedInVoiceActivation_ShowsTheSelfMutedHint"`, `VoiceHintsTests > "Evaluate_MutedAndVoiceDetectedInPushToTalk_ShowsNothing"`, `VoiceHintsTests > "Evaluate_MutedInVoiceActivationWithoutVoice_ShowsNothing"` (AC3); `VoiceHintsTests > "Evaluate_NotMutedAndTalkKeyHeld_ShowsNothing"`, `VoiceHintsTests > "Evaluate_SelfMutedWhilePushToMuteIsHeld_ShowsNothing"`, `VoiceHintsTests > "Evaluate_DeafenedWhilePushToMuteIsHeld_ShowsTheDeafenedHint"`, `VoiceHintsTests > "Evaluate_MutedAfterTalkingUnmuted_ShowsNothing"`, `VoiceHintsTests > "Evaluate_ChannelMutedWithEveryOtherReasonToo_ShowsTheMutedChannelHint"`, `VoiceHintsTests > "Evaluate_NoLinkRightWithLinkKeyDownAndSelfMutedTalking_ShowsTheNoLinkRightHint"` (AC4); `MainViewModelTests > "Tick_InNoChannelSelfMutedAndTalkKeyHeld_ShowsTheSelfMutedHint"` (AC4)
5. `SoundTests > "TalkKeySounds_OffByDefault_PlayWhenOn"` (AC5)
6. `LocalizationTests` (AC6)

(as built: the hint is one more arm at the end of the `VoiceHint` switch in `MainViewModel.Tick`, after the muted channel and the missing link right, so those keep their priority. `MutedTalkHint` picks the reason: server-muted (`Mirror.Self.ServerMuted`) first, since unmuting oneself would not help, then deafened, then self-muted; self-muted while push-to-mute is held shows nothing, as that is muting on purpose (Package 125 sets `SelfMuted` while it is held). An attempt to talk is `AudioEngine.TalkKeyHeld` (the bare keys through `KeyPoller.IsDown`, without the Package 130 release delay: link push-to-talk always, push-to-talk only in push-to-talk mode, as `TransmitController` counts them) or, in voice activation mode only, `AudioEngine.VoiceDetected` (the gate's result of the last frame, stored in `Decide`, which the capture calls for every frame also while muted). Each tick with an attempt while muted stores the time; the hint shows while less than 2 s have passed (`MutedHintHold`) and the reason still applies, so unmuting removes it at once. The time comes from `MainViewModel.Clock` (internal, the system clock by default). The tones `SoundEvent.TalkKeyDown` and `TalkKeyUp` ("Sprechtaste gedrückt" / "Talk key pressed", "Sprechtaste losgelassen" / "Talk key released") are short soft double blips (75 ms, half level like the link tone), rising for the press and falling for the release; they get rows on the sound page through the enum like every sound. Off by default is `ClientSettings.DefaultSound(sound)` (muted for these two), used by `SoundFor`, by `SoundSetting.IsDefaultFor(sound)` (replaces `IsDefault`, so the page stores a talk tone only when turned on and every other sound as before) and by the row's reset, which turns them off again. The engine plays them from its own `KeyPoller.Changed` handler on the edges of `TalkKeyHeld` (holding both talk keys is one press), while connected or in the self test; `PlaySound` applies the mute, the volume and the deafened rule (A46) as for any tone. The hint texts: "Du bist stummgeschaltet: niemand hört dich.", "Du bist taub geschaltet: ...", "Du bist vom Server stummgeschaltet: ..." and English. Tests: AC1 `TalkWhileMuted_ShowsHint_2s` (manual clock: nothing without a key, shown while held for 5 s, still at 1.9 s after the release, gone at 2.1 s), AC2 `TalkWhileDeafenedOrServerMuted_ShowsHint` (theory, with link push-to-talk), AC3 `VoiceWhileMuted_ShowsHint` (voice in push-to-talk mode shows nothing, in voice activation it does), AC4 `NotMuted_NoHint` (no hint unmuted, none while push-to-mute is held, the muted channel text wins over the muted hint), AC5 `SoundTests.TalkKeySounds_OffByDefault_PlayWhenOn` (rows and labels, off and not stored by default, stored when on, reset turns off, no tone by default, press and release of push-to-talk and link push-to-talk play down and up when on, nothing while not connected), AC6 `LocalizationTests.ErrorTexts_English` and `Resources_SameKeys_NoneEmpty_SamePlaceholders`. The new tests failed to build before the strings, the sound events and `Clock` existed.)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 to 4 (red): the hint.
2. Test 5 (red): the sounds.
3. Test 6.

### Out of Scope

- A modal dialog (the hint line is enough and does not interrupt)

---

## Package 132: Bitrate per channel

**Goal:** Each channel has its own voice quality (16 to 128 kbps, default 64), and senders encode with their channel's bitrate.

**Dependencies:** none

**Affected files:**
- `src/OVS.Shared/Protocol/Messages.cs` (change): `ChannelInfo`, `CreateChannel`, `EditChannel` get `int Bitrate = 64` (lines 122, 149, 153)
- `src/OVS.Shared/Protocol/ProtocolInfo.cs` (change): version 12
- `src/OVS.Server/Commands/ChannelCommands.cs`, `src/OVS.Server/Data/ServerData.cs` (change): validate 16 to 128 (refused like other invalid channel fields), store, load a missing value as 64; separators keep none
- `src/OVS.Server/RequestShape.cs` (change, if it lists channel fields)
- `src/OVS.Client/Audio/OpusCodec.cs` (change): `VoiceEncoder` bitrate settable (fixed 32 000 today)
- `src/OVS.Client/Audio/AudioEngine.cs`, `ViewModels/MainViewModel.cs` (change): the encoder follows the current channel's bitrate (join, move, channel edited); the self test uses 64
- `src/OVS.Client/ViewModels/ChannelDialogViewModel.cs`, `Views/ChannelDialog.axaml` (change): a "Qualität" field (kbps) in create and edit
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change)
- `tests/OVS.Tests/Server/ChannelCommandTests.cs`, `DataStoreTests.cs`, `tests/OVS.Tests/Client/ChannelDialogViewModelTests.cs`, `SendPathTests.cs` (change)

### Context

Every sender encodes at a fixed 32 kbps (`OpusCodec.cs`, `VoiceEncoder`). Fluxer sets the bitrate per channel: default 64 kbps, 8 to 96 kbps, more with paid guild features (`packages/constants/src/LimitConstants.ts`, `GuildConstants.ts`). The user chose a 64 kbps default and 16 to 128 kbps. 128 kbps is 320 bytes per 20 ms frame, well inside the 1400-byte packet limit (`VoicePacket.cs:19`).

### Acceptance Criteria

- [x] AC1: A channel created without a bitrate has 64; one created or edited with 16 to 128 keeps it; 15 or 129 is refused with the usual error.
- [x] AC2: A data file from before (channels without the field) loads with 64 for every voice channel; the value survives a restart.
- [x] AC3: All clients receive the bitrate in `ChannelInfo`; the dialog shows and edits it for users who may edit the channel.
- [x] AC4: A sender in a 96 kbps channel produces frames whose average size matches about 96 kbps (±15 %) on a steady test signal; after moving to a 32 kbps channel, about 32 kbps.
- [x] AC5: Link push-to-talk uses the sender's own channel's bitrate.
- [x] AC6: The protocol version is 12.

### Tests (TDD)

1. `ChannelCommandTests > "Bitrate_DefaultRangeRefused"` (AC1)
2. `DataStoreTests > "Channel_WithoutBitrate_Loads64"` (AC2)
3. `ChannelDialogViewModelTests > "Bitrate_ShownAndEdited"` (AC3)
4. `SendPathTests > "Encoder_FollowsChannelBitrate"` (AC4, AC5), `EngineSyncTests > "SyncConnection_ChannelBitrate_SetsTheEnginesChannelBitrate"`, `"SyncConnection_NoChannelBitrate_SetsTheDefaultBitrate"`, `"SyncConnection_BitrateAboveTheMaximum_SetsTheMaximum"` and `"SyncConnection_BitrateBelowTheMinimum_SetsTheMinimum"`, `MainViewModelTests > "Bitrate_FollowsCurrentChannel"` (AC4)
5. `StateSyncTests > "ChannelInfo_CarriesBitrate"` (AC3, AC6)

(as built: `ProtocolInfo` has `DefaultBitrate = 64`, `MinBitrate = 16`, `MaxBitrate = 128` and version 12. `ChannelInfo`, `CreateChannel` and `EditChannel` end with `int Bitrate = ProtocolInfo.DefaultBitrate`, so every existing construction keeps compiling and means 64; `RequestShape` needs nothing (an int is never null). `ChannelRecord.Bitrate` defaults to 64, so a data file without it loads every channel at 64 without a new data version; `ServerState.Info` sends 0 for a separator, whatever its record holds, and a new separator is stored with 0. `ValidateBitrate` refuses anything outside 16 to 128 with `InvalidValue` ("Qualität: 16 bis 128 kbps.") on create and on edit, also for the default channel, which has its own bitrate like any channel; a separator request with a bitrate other than the default is refused like one with other options. The channel log names it ("Qualität 96 kbps" on create when not 64, and on edit when it changed), and so does the client log line of an edit. `BackupStore.Validate` refuses a voice channel outside the range ("Channel-Name oder -Qualität ungültig") and ignores a separator's value, since separators stored before the package load as 64. On the client, `ChannelViewModel.Bitrate` follows `ChannelInfo`, `ChannelEdit` carries it into and out of the dialog, and `ServerViewModel.CreateChannel`/`EditChannel` send it. `MainViewModel.SyncAudioFlags` (run on every state change, so on join, move and an edit of the own channel) sets `AudioEngine.ChannelBitrate` to the current channel's, 64 while in none. `VoiceEncoder` starts at 64 kbps instead of the fixed 32 and has `BitrateKbps`; `CapturePipeline.Bitrate` (a `Func<int>` like `DecideTarget`) is asked for every frame sent and the encoder takes a change right before encoding, on the capture thread under the pipeline's lock, so no encode races a change. The engine wires it to `EncoderBitrate`: the channel's, or 64 in the self test. Link push-to-talk uses it unchanged, since it is the sender's own channel. The channel dialog has "Qualität (kbps, 16 bis 128)" / "Quality (kbps, 16 to 128)" with a one-line hint, a number field (step 8) below the user limit, clamped like it; an emptied field keeps the channel's value. The debug API's `/state` lists each channel's bitrate. Tests: AC1 `ChannelCommandTests.Bitrate_DefaultRangeRefused` (64 by default, 16 and 128 on create, 96 on edit in the snapshot of a later client and in the channel log, 15, 129 and 0 refused, a separator has 0 and one asking for 96 is refused), AC2 `DataStoreTests.Channel_WithoutBitrate_Loads64` (a stored 96 round-trips, the field removed loads 64, the backup check refuses 200), AC2, AC3 and AC6 `StateSyncTests.ChannelInfo_CarriesBitrate` (version 12, another client and its `StateMirror` get 96, still 96 after a server restart), AC3 `ChannelDialogViewModelTests.Bitrate_ShownAndEdited` (64 for a new channel, the channel's own when editing, also the default channel's, clamped to 16 and 128, a separator sends none) and `Bitrate_FromChannelIntoDialog_SentWithEdit` (the channel's 24 goes into the dialog, the edited 128 into `EditChannel` and `CreateChannel`), `UiSmokeTests` (the rendered dialog shows the value), AC4 and AC5 `SendPathTests.Encoder_FollowsChannelBitrate` (uniform noise at -20 dBFS through the `TalkRig`, the average of 80 frames within 15 % of 96 kbps, after the change to 32 while talking of 32, the same with link push-to-talk and its link target, 64 in the self test with nothing sent, and a real capture asks the engine), `MainViewModelTests.Bitrate_FollowsCurrentChannel` (64 in the Lobby, unchanged by an edit of another channel, 96 after moving into it, 24 after it is edited, 64 after disconnecting). `SendPathTests.Encoder_ComplexitySet_DtxOff` now expects a new encoder at 64 000 and `SmokeTests` version 12. The new tests failed to build before the fields, `ChannelBitrate` and the dialog's `Bitrate` existed.)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1, 2 and 5 (red): protocol and server.
2. Test 4 (red): the encoder follows the channel.
3. Test 3 (red): the dialog.

### Out of Scope

- Lowering the bitrate on loss (Package 135)
- Stereo (Package 142)

---

## Package 133: Real connection stats

**Goal:** Users see their real voice round trip, packet loss and jitter in a signal icon and a details panel.

**Dependencies:** Package 126 (the ping timer it changes)

**Affected files:**
- `src/OVS.Shared/Voice/VoicePacket.cs` (change): a `PingPayload`: the sender's time in ms (8 bytes) and the loss figure for the other side (frames expected and missed in the last 5 s, 2 × 4 bytes)
- `src/OVS.Shared/Protocol/ProtocolInfo.cs` (change): version + 1
- `src/OVS.Server/Voice/UdpVoiceServer.cs`, `src/OVS.Server/Session.cs` (change): per sender, count received and missed frame numbers (gaps in `frameSeq`) per 5 s window; echo the client's time and put that uplink loss into the ping answer; keep the client's reported downlink loss on the session (used by 136)
- `src/OVS.Client/Net/VoiceClient.cs` (change): ping every 2 s with the time; `RoundTrip`, `UplinkLoss`; reports its own downlink loss
- `src/OVS.Client/Audio/JitterBuffer.cs` (change): per speaker received, lost, FEC-rebuilt and concealed frames, an interarrival jitter estimate (RFC 3550: arrival time against 20 ms × frame number) and the buffer delay
- `src/OVS.Client/Audio/Mixer.cs`, `AudioEngine.cs` (change): aggregate over speakers for the panel and for the reported downlink loss
- `src/OVS.Client/ViewModels/ConnectionStats.cs` (new): baseline (median of the last 30 round trips) and rating per A142
- `src/OVS.Client/ViewModels/MainViewModel.cs`, `Views/MainWindow.axaml` (change): the signal icon next to the ping text (`MainViewModel.cs:547-549`) and the "Verbindungsdetails" popup on click
- `src/OVS.Client/Debug/DebugApi.cs` (change): the same figures
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change)
- `tests/OVS.Tests/Voice/VoicePacketTests.cs`, `Server/VoiceRelayTests.cs`, `Client/ReceivePathTests.cs`, `Client/ClientNetTests.cs`, `Client/MainViewModelTests.cs` (change), `Client/ConnectionStatsTests.cs` (new)

### Context

OVS shows only the control connection's TCP ping (`ClientConnection.cs:142`, `MainViewModel.cs:547-549`) and whether UDP answers at all (`VoiceClient.Reachable`). Loss and jitter are measured nowhere, so neither users nor Packages 134 to 136 know how the voice path is doing. Fluxer reads WebRTC stats every 2 s (loss, jitter, jitter buffer delay, concealed samples, round trip; `VoiceEngineV2AppStatsHostAdapter.ts`) and rates the round trip against the user's own baseline (`VoiceLatencySignal.ts`), so someone far away is not permanently red. The frame number in the voice payload (`RelayPayload`, `VoicePacket.cs:142-161`) is per sender and gapless while talking, so gaps are losses.

### Acceptance Criteria

- [x] AC1: Through a relay that delays every packet by 50 ms each way, the measured voice round trip is 100 ms ±10 ms.
- [x] AC2: Through a relay that drops 10 % of client-to-server voice packets, the client's uplink loss reads 10 % ±3 % after 5 s; dropped server-to-client packets show as downlink loss on the client and on the server's session.
- [x] AC3: With arrival times jittered uniformly by ±15 ms, the jitter estimate is within 50 % of the expected value; without jitter it is below 2 ms.
- [x] AC4: The rating gives green / yellow / orange / red at 69 / 159 / 299 / 300 ms above the baseline, and a client with a steady 250 ms round trip is green.
- [x] AC5: The icon shows the rating, crossed out while UDP is unreachable; clicking it shows round trip, uplink and downlink loss, jitter, buffer delay, concealed frames and the bitrate, updated every 2 s.
- [x] AC6: Talk pauses do not count as loss.
- [x] AC7: The protocol version is raised.

### Tests (TDD)

1. `VoicePacketTests > "PingPayload_RoundTrip"` (AC7)
2. `ClientNetTests > "VoiceRoundTrip_Measured"` (AC1): a delaying relay in `TestSupport` (from 126).
3. `VoiceRelayTests > "UplinkLoss_ReportedInPing"` and `ClientNetTests > "DownlinkLoss_ReportedBothSides"` (AC2)
4. `ReceivePathTests > "Jitter_Estimated"` (AC3) and `"TalkPause_NotLoss"` (AC6)
5. `ConnectionStatsViewModelTests > "Rating_RelativeToBaseline"` (AC4)
6. `MainViewModelTests > "SignalIcon_AndDetails"`, `DialogButtonsTests > [Theory] "ShowAsync_EachDialog_ItsButtonsInOrderAndRedOnlyWhenRisky"` (AC5); the 2 s refresh and the crossing out one by one: `ConnectionStatsViewModelTests > "Refresh_FirstCall_ShowsTheRoundTripBothLossFiguresAndTheMixerAndEncoderFigures"`, `"Refresh_UdpUnreachable_CrossesTheIconOut"`, `"Refresh_JustBeforeTheInterval_KeepsTheFiguresAndAsksForNoNewOnes"`, `"Refresh_AfterTheInterval_ReadsAndShowsTheNewFigures"` and `"Refresh_UdpReachableAgainWithinTheInterval_ClearsTheCrossingAtOnce"`; the card stays blank without a voice connection and the mixer's figures reach it: `MainViewModelCompositionTests > "Tick_NoVoiceConnection_StatsStayBlank"`, `MainViewModelTests > "Tick_MixerHasJitter_StatsShowTheMixersJitter"` and `ReconnectTests > "Reconnect_SelfTestFramesDuringTheWait_StatsCountFromTheNewConnection"` (AC5); the answers are taken once: `ConnectionStatsViewModelTests > "Refresh_NoNewAnswerSinceTheLast_KeepsTheBaseline"` and `"Reset_AfterAnAnswer_CountsTheSameAnswerCountAsNewAgain"` (AC4); the figures the card reads from the voice connection: `VoiceLinkTests > "ReadFigures_ServerAnswered_ReturnsTheRoundTripAndTheLossSeen"` (AC5)

(as built: `ProtocolInfo.Version` is 13. `PingPayload` (`VoicePacket.cs`) is the plaintext of Hello and Ping both ways, 16 bytes: the sender's time in ms and a `LossReport` (frames expected and missed, with `Percent`, 0 when nothing was expected). `LossWindow` counts received frames and the gaps in the frame numbers per stream (one per speaker) and 5 s window, and `Report` gives the last complete window, or the current one until the first is complete. A talk pause leaves no gap, as the sender's frame number stands still. After review: a stream silent for more than 1 s (`FreshAfter`) or jumping ahead by more than 50 frames (`MaxGap`) starts afresh without counting the gap, because the server relays only what routing allows (not while deafened, other channels only by link) while the speaker's numbers go on; a real outage longer than that goes uncounted (marked `ponytail:`). Each stream keeps a bitmap of its last 64 frames, so only a late frame whose gap was counted in this window takes one back (duplicates, frames before the stream's start and gaps of the last window take nothing back), and the counts are wide and clamped, so the share never exceeds 100 %. Server: each `Session` has an `Uplink` window, counted for every voice frame the UDP loop accepts (before routing, so also a sender alone or server-muted), and `DownlinkLoss` (packed into one long, so another thread never reads half of it), set from the client's Hello or ping; the answer echoes the time with the uplink report. An empty Hello or ping is still answered, empty, as before, so nothing extra is allocated on the voice path. Client: `VoiceClient` pings every 2 s instead of 5 s (Package 126's other timings stay; its tests now expect 17 overdue pings in 10 s and one ping per 2 s), Hello and ping carry the time since the client started and its downlink report; the receive loop counts every frame relayed to it per speaker in a downlink window (network level; while deafened the server relays nothing, and the fresh start keeps those frames out of the loss) and takes `RoundTrip` (with `RoundTripSample`, the value and the number of answers so far) and `UplinkLoss` from each ping answer, all under the state lock the timer uses. The downlink loss is counted in `VoiceClient` rather than aggregated from the jitter buffers (the plan's `AudioEngine` aggregation), because the buffers count at playback and a deafened client has none; `AudioEngine` needed no change. `JitterBuffer.Push` takes the arrival time (now by default, `Mixer.Push` stamps it before taking the lock) and keeps `Received`, `Lost` (gaps skipped while playing, frames dropped when the buffer is full, and frames missing between two spurts, only when the next spurt follows within 1 s and 50 frames, the same rule as `LossWindow`), `Rebuilt` (from the next frame's FEC), `Concealed` (lost without a following frame, and underruns inside a spurt; the two underruns at a spurt's end do not count), `JitterMs` (RFC 3550: |D| of transit = arrival - 20 ms x frame number, J += (|D| - J) / 16; a sample only while playing, so the first frame of a spurt, also after a short pause such as voice activation starting again, starts anew) and `DelayMs`; nothing is allocated per frame (`IdleCostTests` unchanged). `Mixer.Stats()` sums the counts, also of speakers already removed, and takes the worst jitter and delay of the speakers kept. `ConnectionStats` (new view model) keeps the last 30 round trips (the main view model passes a round trip only when `RoundTripSample`'s count changed, so a missing answer adds no copy), rates the newest against their median (`Rate`: below 70 ms above it 4 green bars, below 160 3 yellow, below 300 2 orange, else 1 red), and formats the figures; frame counts start at the connection. `MainViewModel.Tick` sets `Stats.Unreachable` every UI tick and the figures every 2 s on `Clock`; `ShowConnectionDetailsCommand` opens `Dialogs.ConnectionDetails`. In the window the ping chip's Wi-Fi icon became `SignalButton`: four bars (lit by `Ui.AtLeast`, coloured by the classes `bars1` to `bars4`, new brushes `Ovs.SignalFair` and `Ovs.SignalPoor` in both themes) and a red diagonal line while UDP is unreachable, with the rating as tooltip; it stays visible at 360 px, where the ping text hides. `SimpleDialogs.ConnectionDetails` is an overlay card bound to the stats (so it follows the 2 s refresh while open) with round trip and usual round trip, loss to and from the server, jitter, buffer, concealed and FEC-rebuilt frames, bitrate (the channel's, `AudioEngine.ChannelBitrate`) and a one-line hint, with only a close button (the existing `Ui_Close`; `Show` got `withCancel`). The debug API's `/state` has a `Connection` block with the same figures. Tests: AC7 `VoicePacketTests.PingPayload_RoundTrip`, `LossWindow_CountsGapsPerStream` and `LossWindow_FreshStarts_TakesBackOnlyCountedGaps` (2 s of silence and a jump of 100 frames, one beyond the cap and one to 2^32 - 1 give 0 missed; duplicates and frames before the start take nothing back; a late frame after the window rolled neither), `SmokeTests` (version 13; `StateSyncTests.ChannelInfo_CarriesBitrate` from 132 now asks for at least 12); AC1 `ClientNetTests.VoiceRoundTrip_Measured` (`LossyUdpRelay` got `Delay` on a `TimeProvider`, so 50 ms each way is exact on a manual clock: 100 ms, then 130 ms for a slower answer, one sample per answer); AC2 and AC6 `VoiceRelayTests.UplinkLoss_ReportedInPing` (every tenth frame missing: 10 of 100 echoed with the time, the reported downlink kept on the session, after a 4 s pause 10 of 10 with none missed, an empty ping answered empty) and `ClientNetTests.DownlinkLoss_ReportedBothSides` (the relay drops every tenth voice packet each way, `DropUpVoicePercent`/`DropDownVoicePercent`; server and clients on one manual clock, 20 ms per frame: uplink, downlink at the client and on the server's session all 7 to 13 %; after 10 s of silence and clean talking all report 0 missed) and `ClientNetTests.Deafened_FramesKeptAway_NotDownlinkLoss` (b talks on for 2 s while a is deafened: no downlink loss at a or in its report to the server); AC3 and AC6 `ReceivePathTests.Jitter_Estimated` (±15 ms uniform: within 5 to 15 ms of the expected 10; none: below 2), `TalkPause_NotLoss` (2 s pause: 0 lost, 0 concealed, jitter below 2 ms; then a lost frame rebuilt by FEC and a late one concealed once), `ShortPause_NoJitterSample` (160 ms pause: jitter stays below 0.5 ms), `SpurtGap_CountedOnlyWhenClose` (2 frames missing across a 200 ms pause count, a 2 s silence or a jump of 870 frames does not), `Overflow_CappedAt10` (5 frames dropped count as lost) and `Mixer_Stats_SumOverSpeakers`; AC4 `ConnectionStatsTests.Rating_RelativeToBaseline` (69/159/299/300 ms above the baseline, a steady 250 ms green, the baseline following a lasting change) and `Counts_SinceReset`; AC5 `MainViewModelTests.SignalIcon_AndDetails` (4 bars after the Hello's answer, the card gets the stats, a bitrate change shows after 2 s and not at 1.9 s, crossed out with no bars 16 s into an outage with no further baseline samples on the refreshes meanwhile, and back afterwards, reset on disconnect), `ResponsiveTests.Narrow_HeaderIconOnly_NothingClipped` (the icon inside at 360 px, the cross shown, 4 green bars lit) and `Dialogs_FitAt360` with the new card in both languages. All new tests failed to build before the types and members existed; the review's tests were written with their fixes and pin cases the first version got wrong (the gap while deafened, a short pause as a jitter sample, a stale round trip added again). `DownlinkLoss_ReportedBothSides` waits every 5 frames until everything sent has arrived and moves the shared clock in single steps around pauses: otherwise many frames or pings reach the server at one instant of its clock and its voice rate limit (60/s, burst 10) drops some, which reads as loss. Not checked by hand: how the icon and card look in the running client.)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Test 1 (red): the ping payload and the version.
2. Tests 2 and 3 (red): server counting and echo, client measuring.
3. Test 4 (red): jitter and counters in the jitter buffer.
4. Tests 5 and 6 (red): rating, icon and popup.

### Out of Scope

- Acting on the figures (Packages 134 to 136)
- Server-side statistics pages

---

## Package 134: Adaptive jitter buffer

**Goal:** The jitter buffer starts each talk spurt with a cushion that fits the measured jitter instead of a fixed 40 ms.

**Dependencies:** Package 133 (jitter estimate per speaker)

**Affected files:**
- `src/OVS.Client/Audio/JitterBuffer.cs` (change): `StartDelay` (line 10) becomes the target from A143, computed when playback (re)starts in `Pull`
- `tests/OVS.Tests/Client/ReceivePathTests.cs` (change)

### Context

The buffer waits for 2 frames (40 ms) before playing and caps at 10 (`JitterBuffer.cs:10-11`). On a calm network 40 ms is more than needed; on Wi-Fi with 30 ms jitter it underruns, and every underrun is a concealed frame. WebRTC (Fluxer) adapts its buffer to the measured jitter. OVS has no time stretching, so the cushion only changes where that is free: at the start of a talk spurt (A143).

### Acceptance Criteria

- [x] AC1: Without jitter the cushion is 2 frames, as today.
- [x] AC2: With ±30 ms jitter the cushion grows to 4 frames at the next talk spurt, and the concealed frames over 10 s are fewer than with a fixed 2.
- [x] AC3: When the jitter calms down, the next talk spurt starts with a smaller cushion again; never above 10 frames.
- [x] AC4: Within a talk spurt no frame is dropped to shrink the buffer.

### Tests (TDD)

1. `ReceivePathTests > "Cushion_NoJitter_Two"` (AC1)
2. `ReceivePathTests > "Cushion_GrowsWithJitter_FewerConcealed"` (AC2): a scripted arrival schedule against a fixed-2 run.
3. `ReceivePathTests > "Cushion_ShrinksNextSpurt_Capped"` (AC3)
4. `ReceivePathTests > "NoDropInsideSpurt"` (AC4)

(as built: `JitterBuffer.StartDelay` stays as the least cushion (2 frames) and is still what the other tests refer to; the new `Cushion` property is A143's `clamp(ceil(2 × JitterMs / 20) + 1, StartDelay, MaxFrames)`, read in `Pull` where a non-playing buffer decides whether to start (wait until that many frames are here, or a lone frame for that many ticks, as before with 2). Once playing it changes nothing, so a buffer that started with 4 frames keeps them when the jitter calms; it shrinks only at the next rebuffer or through the cap of 10, never by dropping. Nothing is allocated per frame. After review, two changes beyond A143 (only `JitterBuffer.cs` and its tests changed, as listed under "Affected files"; the restart point in `Pull` is unchanged): (1) a network stall of 60 to 150 ms drains a 2 frame buffer and its frames then come in a burst while it rebuffers, where Package 133 takes no jitter sample (the first frame after a gap looks the same as after a talk pause, as the payload carries no send time), and even sampled, RFC 3550's 1/16 smoothing would fall back below 1 frame within a second. So the cushion is `clamp(ceil(max(2 × JitterMs, LateMs) / 20) + 1, 2, 10)`: the new `LateMs` is the peak lateness, the highest transit (arrival - 20 ms × frame number) since the buffer last ran empty minus this frame's transit, sampled once a spurt has played and also while rebuffering; the burst shows the stall, as its newest frames are early against the first by as much as that was late, while a talk pause only raises the transit (the frame number stood still) and the buffer is empty then, so it starts anew (`ShortPause_NoJitterSample` and the other Package 133 tests unchanged). The highest transit fades by 1 ms per frame, so a lasting drop of the latency fades out, and a jump of 200 ms or more starts anew, as for the jitter. `LateMs` fades by 0.997 per frame (half-life about 4.6 s), so stalls every second hold it up. A burst of N frames pushed at once gives a cushion of N, so they still play at once (the existing tests that push several frames at the same instant stay green; the first spurt is not measured, as before it the buffer has never played). `JitterMs` is still only sampled while playing. (2) A frame more than `LossWindow.FreshAfter` (1 s) after the last halves `JitterMs` and `LateMs`, so one noisy spurt end does not hold back the next spurt fully. Tests drive whole spurts through a scripted arrival schedule (`Spurt`: 500 frames, arrival times as `Push`'s `arrivalMs`, a tick every 20 ms, 500 ms of silence after) with the "±30 ms" jitter as arrivals 0, 30, 60, 30 ms late in turn (|D| 30 each, so the estimate approaches 30 ms from below and gives 4 frames): AC1 `Cushion_NoJitter_Two` (a lone frame plays after 2 ticks), AC2 `Cushion_GrowsWithJitter_FewerConcealed` (the first spurt starts with the fixed 2 and conceals 126 frames in 10 s, the next starts with 4 and conceals none), AC3 `Cushion_ShrinksNextSpurt_Capped` (4, then 2 after a calm spurt, then every other frame 190 ms late gives 10 and a lone frame waits 10 ticks), AC4 `NoDropInsideSpurt` (a spurt starting with 4 that turns calm still holds 3 frames after each pull 5 s in, no frame lost or concealed, while the cushion for the next spurt is back to 2); review: `Cushion_GrowsWithStalls_NoGapsNextSpurt` (frames sent 500 to 600 ms into every second all come at 600 ms: the first spurt with 2 frames goes silent for 2 ticks at its first stall while `JitterMs` stays below 5 ms, the cushion becomes 6, and the next spurt plays 10 s of such stalls without a gap) and `Cushion_LongSilence_Halved` (1.5 s after a jittery spurt `JitterMs` and `LateMs` are halved, the cushion 3 instead of 4, and a lone frame waits 3 ticks). The tests failed before `Cushion` existed and, with it only a property, AC2 (126 vs 126), AC3 and AC4 failed until `Pull` used it; the stall test fails with the cushion from the jitter alone. The review's fixes were written before their tests (then checked red by taking `LateMs` out of the cushion). Not checked by hand: how it sounds on a real jittery network.)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 to 4 (red), then the target computation at the restart point in `Pull`.

### Out of Scope

- Time stretching (speeding up or slowing down playback)

---

## Package 135: Adaptive loss protection

**Goal:** The sender's FEC and bitrate follow the loss its packets actually suffer.

**Dependencies:** Package 132 (channel bitrate as the ceiling), Package 133 (uplink loss)

**Affected files:**
- `src/OVS.Client/Audio/LossAdapter.cs` (new): from the uplink loss every 2 s, the loss hint and the bitrate step per A144
- `src/OVS.Client/Audio/OpusCodec.cs` (change): `PacketLossPercent` settable (fixed 10 today)
- `src/OVS.Client/Audio/AudioEngine.cs`, `ViewModels/MainViewModel.cs` (change): feed the adapter from `VoiceClient.UplinkLoss`, apply to the encoder under the capture lock
- `src/OVS.Client/ViewModels/ConnectionStats.cs` (change): the panel shows the bitrate in use
- `tests/OVS.Tests/Client/LossAdapterTests.cs` (new), `SendPathTests.cs` (change)

### Context

Opus adds in-band FEC in proportion to the expected loss, and OVS tells it a fixed 10 % (`OpusCodec.cs`, `VoiceEncoder`). On a clean line that wastes bits, on a bad one it protects too little. WebRTC (Fluxer) feeds the measured loss into Opus and lowers the bitrate under congestion. With 133 the sender knows its uplink loss.

### Acceptance Criteria

- [x] AC1: 0 % loss gives a loss hint of 5, 10 % gives 15, 40 % gives 30.
- [x] AC2: Loss above 10 % for 5 s halves the bitrate (64 → 32 → 16, never below 16); 10 s below 2 % doubles it again, never above the channel's bitrate.
- [x] AC3: Moving to another channel resets the bitrate to that channel's value.
- [x] AC4: The encoder actually uses the values (read back through `VoiceEncoder.Opus`).

### Tests (TDD)

1. `LossAdapterTests > "LossHint_FromLoss"` (AC1)
2. `LossAdapterTests > "Bitrate_StepsDownAndUp_WithinBounds"` (AC2), with `ManualTimeProvider`; the voice link hands it the uplink figure once per new answer, and a disconnect ends the streak: `VoiceLinkTests > "FeedLoss_NewAnswerSeenOnTwoTicks_ReportsItsUplinkFigureOnce"` and `EngineSyncWiringTests > "DisconnectAsync_LossStreakRunning_EndsItSoTheNextFigureStartsANewOneAndNoStepDownComes"` (AC2)
3. `SendPathTests > "ChannelChange_ResetsAdaptedBitrate"` (AC3), `EngineSyncTests > "SyncConnection_ChannelNotSeenBefore_ResetsTheLossAdapterToTheChannelsBitrate"`, `"SyncConnection_BitrateAboveTheMaximumInANewChannel_ResetsTheLossAdapterToTheMaximum"`, `"SyncConnection_SameChannelAgain_KeepsTheLoweredBitrate"` and `"SyncConnection_ChannelLeftAfterOne_ResetsTheLossAdapter"`, `MainViewModelTests > "Bitrate_Adapted_ResetOnMove"` (AC3)
4. `SendPathTests > "Encoder_UsesAdaptedValues"` (AC4)

(as built: `LossAdapter` (new, `Audio/LossAdapter.cs`) is pure logic on a `TimeProvider`: `Report(LossReport)` sets `LossHint` to `HintFor(loss) = clamp(round(1.5 × loss), 5, 30)` (rounded half away from zero) and keeps two streaks, one for figures above 10 % and one for figures below 2 %; once a streak is 5 s (10 s) old, `BitrateKbps` halves to at least 16 (doubles to at most the channel's) and that streak starts again, so each further step needs the full time again. A figure in between ends both streaks. A figure with nothing expected (no voice in the server's window, a talk pause) ends both streaks and leaves the hint, so it is neither loss nor recovery. After review: more than 5 s (2.5 ping intervals) without any figure (an outage, a reconnect) also ends both streaks before the new figure counts, so one figure after a gap never steps; `EndStreaks()` does the same for a new connection (`MainViewModel.ResetStats`, as a reconnect into the same channel keeps its id). `UplinkLoss` is the server's last complete 5 s window (`LossWindow`), so the same figure comes back in 2 to 3 ping answers and a loss shows in it only once its window completes; in practice a step down comes about 10 to 17 s after the loss begins and a step up about 15 to 22 s after it ends, not 5 s and 10 s. Before the first figure the hint is today's 10. `Reset(kbps)` (a join or move) sets the ceiling and the bitrate and ends the streaks; the hint stays, as it describes the network, not the channel. `SetCeiling(kbps)` (an edit of the own channel): a bitrate not lowered follows the new value, a lowered one stays lowered and at most the new value. `AudioEngine` owns the adapter as `Loss` (on a new optional `time` constructor parameter; `internal set` so a test can swap in one on its own clock): a new `ChannelBitrate` calls `SetCeiling` (the same value set again by every state change changes nothing), and `MainViewModel.SyncAudioFlags` calls `Reset` when the current channel's id changes, so a move, also to a channel with the same bitrate, starts at the channel's bitrate. `EncoderBitrate` is the adapted bitrate and the new `EncoderLossHint` the adapted hint; the self test, which sends nothing, keeps 64 kbps and the initial hint 10. `MainViewModel.UpdateStats` (every 2 s) passes `VoiceClient.UplinkLoss` to `Report` only when a new ping answer arrived (the `RoundTripSample` count changed), so a stale figure is not counted again while answers stay away. `VoiceEncoder.PacketLossPercent` is settable (starts at `LossAdapter.InitialHint`); `CapturePipeline.LossHint` is a `Func<int>` asked per sent frame like `Bitrate`, and the encoder takes a change right before encoding on the capture thread under the pipeline's lock; nothing is allocated per frame. The "Verbindungsdetails" card shows `EncoderBitrate` (the bitrate in use) instead of the channel's; the change is in the main view model's call of `ConnectionStats.Update`, `ConnectionStats.cs` itself needed none. `MainViewModel.Voice` (internal) lets a test wait for ping answers. Tests: AC1 `LossAdapterTests.LossHint_FromLoss` (0, 3, 10, 20, 40 % give 5, 5, 15, 30, 30; 10 before the first figure; an empty figure keeps the hint), AC2 `LossAdapterTests.Bitrate_StepsDownAndUp_WithinBounds` (figures every 2 s on a manual clock: 64 → 32 after 6 s of 15 %, 16 after another 6 s, never below 16; 5 % keeps it; 32 after 10 s of 1 %, 64 after another 10 s, never above 64; empty figures between lossy or clean ones break the streak; a 96 channel goes 48 and back to 96, a 24 channel 16 and back to 24) and `Gap_EndsStreaks` (lowered, a clean figure, 20 s without figures, a clean figure: unchanged, and no step 6 s into the new streak; the same for loss; after `EndStreaks` the full 5 s again), AC2 and AC3 `LossAdapterTests.Edit_SetsCeiling_KeepsLowered` (64 follows an edit to 96; lowered to 48 it stays at an edit to 128, becomes 32 at an edit to 32 and never goes above it), AC3 `SendPathTests.ChannelChange_ResetsAdaptedBitrate` (96 adapted to 48 stays 48 when 96 is set again and when edited to 128, 32 when edited to 32; a move resets to 64, encoded at 64 000, adapted to 32, a move to another 64 kbps channel encodes at 64 000 again) and `MainViewModelTests.Bitrate_Adapted_ResetOnMove` (adapted to 32 in the Lobby, stays over an edit of another channel, 64 after moving into another 64 kbps channel; red without the id check), AC4 `SendPathTests.Encoder_UsesAdaptedValues` (read back through `VoiceEncoder.Opus`: 10 % and 64 000 at first, unchanged until the next frame, then 21 % and 32 000 after 6 s of 14 %, 5 % and 64 000 after 10 s clean; 10 % in the self test, 30 % again after it; a real capture asks the engine), and the production wiring in `MainViewModelTests.SignalIcon_AndDetails` (the test's server runs on its own manual clock, moved 20 ms per frame and 200 ms before each ping, so its uplink window never rolls and the result does not depend on how fast the machine is; the relay drops every fifth voice frame to the server, 40 frames sent through the connected client, four ping answers 2 s apart on the client's clock: the hint above 10, the bitrate 48 in a 96 kbps channel and the card shows "48 kbps"; red without the line in `UpdateStats`). The `TalkRig` of `SendPathTests` exposes its pipeline, wires `LossHint` as `StartCapture` does and gives the engine its manual clock. The new tests failed to build before `LossAdapter`, `AudioEngine.Loss`, `CapturePipeline.LossHint` and `Encoder` existed; the review's adapter tests were checked red by taking the gap check and the ceiling rule out. Not checked by hand: how it sounds on a really lossy line.)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 and 2 (red): `LossAdapter` as pure logic.
2. Tests 3 and 4 (red): the wiring.

### Out of Scope

- Redundant frames (Package 136)
- Downlink congestion (the server does not transcode)

---

## Package 136: Redundant frames

**Goal:** Packets on a lossy path carry a copy of the previous frame, so a lost frame is played back in full quality.

**Dependencies:** Package 135 (the sender's loss handling; the reported downlink loss on the server comes from 133)

**Affected files:**
- `src/OVS.Shared/Voice/VoicePacket.cs` (change): `RelayPayload` becomes `[frameSeq 4][length of current 2][current opus][previous opus, optional]`; `TryParse` returns the optional copy; the size limit covers both
- `src/OVS.Shared/Protocol/ProtocolInfo.cs` (change): version + 1
- `src/OVS.Client/Net/VoiceClient.cs`, `src/OVS.Client/Audio/CapturePipeline.cs` (change): the sender keeps its last encoded frame and appends it while its uplink loss is ≥ 2 %
- `src/OVS.Server/Voice/UdpVoiceServer.cs`, `src/OVS.Server/Session.cs` (change): keep each speaker's last frame; strip the sender's copy and build the payload per recipient, adding the copy for recipients reporting ≥ 2 % downlink loss (A145); `CountVoiceFrame` unchanged
- `src/OVS.Client/Audio/JitterBuffer.cs` (change): a copy is stored under `frameSeq - 1` when that frame is missing; a lost frame uses the copy before FEC and concealment
- `tests/OVS.Tests/Voice/VoicePacketTests.cs`, `Server/VoiceRelayTests.cs`, `Client/ReceivePathTests.cs`, `Client/SendPathTests.cs` (change)

### Context

Opus FEC rebuilds a lost frame only as a coarse copy, and only from the next packet. Fluxer turns RED on for every microphone (`fluxer_app/src/features/voice/utils/AudioPublishOptions.ts:23-38`): each packet repeats the previous frame in full. Always on would double every stream through a self-hosted server, so OVS adds the copy only on legs that lose packets (A145): the sender's uplink, and per recipient on the server. The server already relays the plaintext and re-encrypts per recipient (`UdpVoiceServer.cs:118-123`), so a payload per recipient costs no extra crypto.

### Acceptance Criteria

- [x] AC1: A payload with and without a copy round-trips; an oversize payload is refused.
- [x] AC2: The sender adds the copy only while its uplink loss is ≥ 2 %.
- [x] AC3: The server adds a copy only for recipients reporting ≥ 2 % downlink loss; the others get single frames, even from a sender that sends copies.
- [x] AC4: With 10 % random loss on a recipient's leg and copies on, the recipient's concealed frames over 10 s drop by at least 80 % compared to copies off.
- [x] AC5: A copy never replaces a frame that arrived itself, and nothing plays twice.
- [x] AC6: The protocol version is raised.

### Tests (TDD)

1. `VoicePacketTests > "RelayPayload_WithCopy_RoundTrip_SizeLimit"` (AC1, AC6)
2. `ReceivePathTests > "LostFrame_PlayedFromCopy_FewerConcealed"` (AC4)
3. `ReceivePathTests > "Copy_NeverReplacesOrDuplicates"` (AC5)
4. `SendPathTests > "Sender_AddsCopyOnlyOnLoss"` (AC2)
5. `VoiceRelayTests > "Server_AddsCopyPerLossyRecipient"` (AC3)

(as built: `ProtocolInfo.Version` is 14. `RelayPayload` (`VoicePacket.cs`) is the voice plaintext in both directions, `[frameSeq 4 BE][length of current 2 BE][current opus][previous opus, optional]`; `TryRead` takes it apart into ranges without copying (the server's side) and refuses a payload shorter than 6 bytes or longer than `MaxSize` (1374, what a 1400 byte packet leaves after header and tag), an empty current frame, a current frame or copy above `VoiceHeader.MaxOpusSize` and a length field beyond the payload; `TryParse` (the client's side) returns the frame and the copy or null. `Build` throws for an empty or oversize current frame and leaves out a copy that would not fit the packet. `RelayPayload.CopyFromLossPercent` (2) is the threshold for both legs. Sender: `VoiceClient.SendVoice` keeps the encoder's array of the last frame (no copy, nothing extra allocated) and appends it while `sendCopies` is set, which each ping answer with frames expected sets from the uplink figure (at least 2 %); an answer with nothing expected (a talk pause) keeps the decision. This needed no change in `CapturePipeline` (the plan listed it): `VoiceClient` serializes sends under its own lock and knows the uplink figure itself. Server: `Session.DownlinkLoss`'s setter also stores the time and, for a figure with frames expected, whether it is at least 2 %; `WantsCopies` is that decision while the last report is at most 10 s old (`CopiesStaleAfter`, five ping intervals), so a listener whose pings stopped, or who never reported, gets single frames. `Session.LastFrame` keeps the sender's newest frame as a slice of its decrypted plaintext (only advanced by a higher frame number). `UdpVoiceServer.RelayAsync` builds at most three payloads per packet and seals each once per recipient as before: with the copy (the sender's own, else the kept frame when it is exactly the one before; the plaintext itself when the sender sent a copy), single (the plaintext itself when the sender sent none), and, beyond the plan, the sender's copy as a frame of its own when the server never got that frame (a loss on the uplink): listeners without copies get it before the frame, those with copies get it inside it, so the sender's copy helps every listener. `Uplink` still counts only the frame itself (a copy is no frame received, so the loss figure and with it the sender's decision stay put), and `CountVoiceFrame` is unchanged. Client: the copy travels as a fifth argument of `VoiceClient.VoiceReceived` through `AudioEngine.OnVoice` and `Mixer.Push` (optional `copy` parameters) into `JitterBuffer.Push`, which keeps it in a small dictionary under `frameSeq - 1` only while that frame is missing and still to play (not before playback reaches it, never for one played already); a frame that arrives itself removes its copy, and `Pull` takes the frame itself before a copy, so a copy never replaces a frame that arrived, and frames behind `next` are dropped as before, so nothing plays twice. Copies playback has moved past are dropped at a spurt start and after an overflow, without allocating. A lost frame plays from its copy before FEC and concealment, and the FEC rebuild also reads the next frame from a copy. A frame played from a copy counts in a new counter `Recovered` (neither `Lost` nor `Rebuilt`, as it plays in full); `ReceiveStats` got `Recovered` as an optional last field and the details card's concealed line reads "{0} ({1} per FEC ersetzt, {2} aus Kopien gespielt)" / "{0} ({1} rebuilt by FEC, {2} played from copies)". The AC4 measure is `Concealed + Rebuilt` (every frame the decoder had to make up, as a coarse FEC rebuild is not full quality either), not `Concealed` alone; `Concealed` alone barely changes, since a single loss was already rebuilt by FEC and only two losses in a row leave a gap. Tests: AC1 and AC6 `VoicePacketTests.RelayPayload_WithCopy_RoundTrip_SizeLimit` (version 14, with and without copy through both parse methods, the largest frame alone and a copy left out by `Build`, a payload of exactly `MaxSize` sealed, each malformed form refused) and `SmokeTests` (version 14); AC2 `SendPathTests.Sender_AddsCopyOnlyOnLoss` (a `VoiceClient` against a stand-in UDP server on loopback that answers pings by hand, the client on a manual clock so its own timer never pings: no copy before a figure and at 1 %, the previous frame at 2 %, kept over an empty figure, gone at 1 %); AC3 `VoiceRelayTests.Server_AddsCopyPerLossyRecipient` (server on a manual clock, 20 ms per frame: y reports 10 %, z 1 %, w nothing; y gets the kept frame, then the sender's copy, z and w single frames throughout; a frame lost on the uplink reaches z and w on its own from the sender's copy and y inside the next packet; the uplink window still counts it missed; an empty report from y keeps its copies; 11 s later y's report is stale and it gets a single frame); AC4 `ReceivePathTests.LostFrame_PlayedFromCopy_FewerConcealed` (500 frames of one stream, 10 % lost with a fixed seed, one push and pull per tick: without copies every loss is `Lost`, with copies `Lost + Recovered` is the number lost and frames made up drop from 46 to 6, 87 %; a mixer sums `Recovered`); AC5 `ReceivePathTests.Copy_NeverReplacesOrDuplicates` (the pulled audio equals a reference decoder fed the frames that arrived and the one copy for the frame that never did, a late frame after its copy played and a copy for a played frame change nothing, a duplicate plays once and is counted once; after review also a lost onset played from its copy, in a fresh buffer and after a rebuffer). The tests failed to build before the four-out `TryParse`, `Recovered` and the `copy` parameters existed; with the server's `WantsCopies` forced off, the client's copy decision never set, or the copies ignored, AC2, AC3 and AC4 fail. `ClientNetTests` only got the fifth lambda argument. After review: (1) a copy travels under the routing of the frame that carries it, so it must have had the same: `VoiceClient` keeps the target and send time with its last frame and appends it only to the same target, and `Session.TrackFrame(frame, target, opus, relayed)` replaces `LastFrame`: it keeps the newest frame that had recipients (not one sent while muted or into a muted channel) with its effective target and arrival time, and the highest frame number that arrived at all. The frame before goes along (kept, or the sender's copy) only when the last relayed frame had the same target and arrived at most `RelayPayload.CopyMaxAge` ago, and only when it is exactly that frame or never arrived (`Unseen`); otherwise a sender's copy is stripped, so a frame that arrived but was not relayed (muted, another target) never travels later. (2) The recovered extra packet takes a token of the sender's rate limit (`Limiter.TryTake()`, once per packet), and only a frame newer than anything that arrived is recovered. (3) Frame numbers go on across talk pauses, so the frame before a spurt's first is the end of the last spurt: `CopyMaxAge` is 100 ms (five frames; the review suggested about 60 ms, 100 leaves room for jitter around a frame lost on the way in, as its neighbours are 40 ms apart), on the client since the last send and on the server since the last relayed frame. (4) `JitterBuffer.Pull` starts a spurt at a copy of exactly the first frame's predecessor when that is not played yet, so a lost onset plays in full (counted `Recovered`; a gap before it still counts `Lost`). (5) A recovered frame arriving late itself is not relayed again (`Session.RecoveredFrame`), and `JitterBuffer.Push` ignores a frame already buffered, so a duplicate is neither stored again nor counted in `Received` (a late frame after its slot played still counts, as in Package 133). Tests: `SendPathTests.Sender_AddsCopyOnlyOnLoss` also checks no copy across a change to and from the link target and none after 150 ms without sending; `VoiceRelayTests.Server_AddsCopyPerLossyRecipient` now proves staleness with a kept frame that would otherwise go along; new `VoiceRelayTests.Server_Copies_SameRoute_RecentOnly_Charged` (manual clock; x speaks in the lobby with y lossy and z, the linked channel Other has c lossy and d): a lobby-only frame lost on the way in and carried by a linked frame reaches no one, alone or as copy, while the next linked frame carries its copy; a frame sent while self-muted is never relayed later; the end of a spurt 2 s before is no copy; a recovered frame arriving late is not relayed again; a sender skipping every other number with copies gets 10 packets out of its burst of 10, not 19 (each packet awaited, and the server's handling of the late frame and of the muted frame confirmed by a ping before going on, since a packet handled after the clock moved would take a refilled token). `ReceivePathTests.Copy_NeverReplacesOrDuplicates` covers (4) and (5). Each fix was checked red by taking it out (target and age on both sides, the token, the late duplicate, the strip, the onset, the duplicate count). Second review: "same route" also means the same channel, as a move while talking changes the listeners with the same target byte: `ServerState.VoiceRecipients` also returns the sender's channel (read under its lock), `TrackFrame` keeps it with the last relayed frame and requires it for both the kept copy and the recovery; `Server_Copies_SameRoute_RecentOnly_Charged` moves x to Other between frame 13 (lobby) and 14 (with 13 as copy: none for c, nothing extra for d) and back before frame 16 (with the lost 15 as copy: no copy for y, no packet of its own for z), red without the channel check. A frame counter starting again within one session (not reachable, a reconnect makes a new session) would only turn copies off (noted in `TrackFrame`). Not checked by hand: how it sounds on a really lossy line.)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Test 1 (red): the payload format.
2. Tests 2 and 3 (red): the receiver.
3. Tests 4 and 5 (red): sender and server.

### Out of Scope

- More than one previous frame

---

## Package 137: Audio device changes

**Goal:** Unplugging, plugging in or switching the Windows default device during a call keeps voice running without a trip to the settings.

**Dependencies:** none

**Affected files:**
- `src/OVS.Client/Audio/DeviceWatcher.cs` (new): the `IMMNotificationClient` implementation (A146)
- `src/OVS.Client/Audio/AudioEngine.cs` (change): `IAudioDeviceFactory` (lines 60-67) gets a change event so tests can raise changes; on a change or a device error, reopen what is open after 800 ms of quiet, through the existing reopen path of `Configure`
- `src/OVS.Client/Audio/CapturePipeline.cs` (change): `RecordingStopped` with an exception reported to the engine (playback likewise in the engine)
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change): "Mikrofon getrennt, Standardgerät wird verwendet" and the output counterpart
- `tests/OVS.Tests/Client/SendPathTests.cs`, `ReceivePathTests.cs` (change)

### Context

Devices are opened by saved id with a fallback to the default (`AudioEngine.cs:40-56`), and only when opening. Nothing listens for changes: unplugging a headset mid-call leaves silence until the user saves the settings, and "Standard" keeps the device that was the default at opening time. Fluxer reacts to device changes with an 800 ms debounce and restarts the track (`flutter_client/lib/features/voice/utils/voice_audio_route_recovery.dart`; the web app through the browser's `devicechange`).

### Acceptance Criteria

- [x] AC1: When the chosen microphone disappears while connected, capture reopens on the default within 1 s with one notice; when it comes back, capture moves back to it.
- [x] AC2: The same for the output device.
- [x] AC3: With "Standard" chosen, a change of the Windows default device moves capture or output to the new default.
- [x] AC4: Several changes within 800 ms cause one reopen.
- [x] AC5: While no device is open (not connected, no self test, no level meter), a change opens nothing.
- [ ] AC6: Manual check: unplug and replug a USB headset during a call; voice continues without touching the settings.

### Tests (TDD)

1. `SendPathTests > "ChosenMicRemoved_FallsBack_Returns"` (AC1): the fake factory raises changes and counts opens. `DeviceLifecycleTests > "DeviceChange_ChosenMicBack_ReopensItOnceQuiet"`, `"DeviceChange_ChosenMicRemoved_DefaultOpensWithTheLostNotice"`, `"DeviceChange_ChosenMicGoneAgainAfterReturning_LostNoticeAgain"`, `"DeviceChange_SameDevicesStillCurrent_ReopensNothing"` (AC1)
2. `ReceivePathTests > "ChosenOutputRemoved_FallsBack_Returns"` (AC2)
3. `SendPathTests > "DefaultChanged_Follows"` (AC3)
4. `SendPathTests > "Changes_Debounced"` (AC4), with `ManualTimeProvider`. `DeviceLifecycleTests > "DeviceChanges_KeepComing_OneReopenOnceTheLastIsQuiet"` (AC4)
5. `SendPathTests > "Closed_ChangeOpensNothing"` (AC5)

(as built: `IAudioDeviceFactory` got `Resolve(deviceId, flow)` (the endpoint id that would open now: the chosen device if active, else the console default's id, null if there is none), `event Action? Changed` (a device added, removed, enabled or disabled, or a new default) and `event Action<DataFlow, Exception>? Failed` (an open stream stopped with an error), both raised on any thread. The real `WasapiDevices` registers a new `DeviceWatcher` (`DeviceWatcher.cs`, an `IMMNotificationClient` on its own `MMDeviceEnumerator`) when the first `Changed` handler subscribes and unregisters and disposes it when the last one leaves; the engine subscribes in its constructor and leaves in `Dispose`, so Windows notifies exactly for the engine's lifetime. The watcher has no thread of its own, reacts to state changes, added, removed and the console role's default change (what `WasapiCapture` and `WasapiOut` open as the default; the other roles would repeat it), ignores property changes (volume and the like, frequent), and returns null where there is no audio service. `CapturePipeline.FromDevice` takes an optional `failed` callback for `RecordingStopped` with an exception, `WasapiDevices.OpenOutput` does the same for `PlaybackStopped` (a stop of our own carries none); both raise `Failed` with their direction. The engine's handlers only rearm an `ITimer` from its `TimeProvider` (`AudioEngine.DeviceSettle`, 800 ms; a stream error is first marked on its direction), so a COM thread never waits and nothing Avalonia is touched there. When it fires (a pool thread; the test thread with `ManualTimeProvider`) `ReopenAfterChange` takes the device lock, returns after `Dispose`, and decides per direction in use (the microphone while wanted and no test tone runs, the speaker while open or the playback thread runs): each direction remembers the id it resolved when it last opened (`Endpoint`); it reopens only when `Resolve` now gives another id (the chosen device went or came back, "Standard" got a new default), or its stream broke, or it is wanted but not open (an earlier open failed). So the many unrelated notifications (a monitor's audio sleeping, a Bluetooth profile switch, a dock, a virtual device, or opening a Bluetooth hands-free microphone itself) leave a running capture (with its noise suppression, encoder, automatic gain and learned voice gate) and speaker alone, and a change to one direction does not touch the other. A reopen closes and opens only that direction, under the device lock like `Configure`, the pool-thread opens of Package 124, the playback thread's own open and end, and `Dispose`; the live values of the settings page stay. Backoff: failures in a row on the same id are counted (an open that threw, a stream error within 10 s of opening; one later than that counts as a new first one); at 3 that device is not tried again, with one notice ("Mikrofon konnte nicht geöffnet werden: ..." or the speaker's) for a broken stream (a failed open has given its notice each time), until another id would open or the settings are saved. A failure of a reopen is reported in its direction's words. The notice for a missing device: the factory still warns with `Audio_DeviceFallback`; `Report` turns it into "Mikrofon getrennt, Standardgerät wird verwendet." / "Microphone disconnected, using the default device." (`Audio_MicLost`) or "Lautsprecher getrennt, Standardgerät wird verwendet." / "Speaker disconnected, using the default device." (`Audio_SpeakerLost`) on a reopen after a change, once until the device is back (per direction, remembered across every open; the test tone leaves the microphone's state alone), and leaves other opens as before. It goes out through `AudioEngine.Warning`. Tests: the fake factory moved from `MainViewModelTests` to `TestSupport/FakeAudioDevices.cs` and learned plugged-in devices, defaults, `Resolve`, the fallback warning, `RaiseChanged` and `RaiseFailed`; `SendPathTests.DeviceRig` is an engine on a manual clock with it. AC1 `SendPathTests.ChosenMicRemoved_FallsBack_Returns` (nothing 1 ms before the 800 ms, the default with `Audio_MicLost` at 800 ms, a further change reopens nothing and gives no second notice, back on the headset once it returns, a notice again when it goes again); AC2 `ReceivePathTests.ChosenOutputRemoved_FallsBack_Returns` (the same for the speaker); AC3 `SendPathTests.DefaultChanged_Follows` ("Standard": an unrelated change keeps both open with the open counts unchanged, a new microphone default reopens only the microphone, a new speaker default only the speaker, no notice); AC4 `SendPathTests.Changes_Debounced` (new defaults, then changes 500 ms apart for 2.5 s and two at once: one reopen of each, 800 ms after the last); AC5 `SendPathTests.Closed_ChangeOpensNothing` (before connecting and after disconnecting a change opens nothing; after `Dispose` the factory has no subscriber and a pending change opens nothing); the stream errors `SendPathTests.StreamError_Reopens_BacksOff` (a speaker error reopens only the speaker on the same device, microphone errors right after opening reopen it twice and the third gives up with one notice, further changes and 30 s reopen nothing, a new default tries again). `SendPathTests.DeviceWatcher_RegistersAndUnregisters` starts and disposes the real watcher three times and only requires that nothing throws (it registered on this machine; without an audio service `Start` gives null). The tests failed to build before `DeviceSettle` and the strings existed; with the "unrelated change" check or the backoff taken out, AC1, AC2, AC3 and the stream error test fail. AC6 is not ticked: it needs a real USB headset unplugged and plugged in during a call, which was not done here.)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 to 5 (red) against the fake factory, then the engine logic.
2. `DeviceWatcher` with the real notifications, then AC6.

### Out of Scope

- Refreshing the device list on the settings page while it is open (the list loads when the page opens)

---

## Package 138: Clean resampling

**Goal:** Microphones that do not run at 48 kHz are converted without aliasing.

**Dependencies:** none

**Affected files:**
- `src/OVS.Client/Audio/SendPath.cs` (change): `LinearResampler` (lines 88-111) replaced by a wrapper around NAudio's `WdlResampler` with a reused output buffer; 48 kHz passes through
- `src/OVS.Client/Audio/CapturePipeline.cs` (change, the type name only)
- `tests/OVS.Tests/Client/SendPathTests.cs` (change)

### Context

Capture converts other rates to 48 kHz by linear interpolation without a low-pass filter, which folds high frequencies back into the voice band (a 44.1 kHz headset turns sibilants into audible artefacts). Fluxer gets the browser's resampler. NAudio, already a dependency, contains `WdlResampler`, a windowed-sinc resampler (A147).

### Acceptance Criteria

- [x] AC1: A 1 kHz sine at 44.1 kHz comes out at 48 kHz as 1 kHz with the same level ±0.5 dB.
- [x] AC2: A 20 kHz sine at 44.1 kHz leaves at least 40 dB less energy below 4 kHz than the linear resampler does.
- [x] AC3: The number of output samples matches the rate ratio over 10 s (no drift), and 48 kHz input passes unchanged.
- [x] AC4: Nothing is allocated per frame.

### Tests (TDD)

1. `SendPathTests > "Resampler_KeepsTone"` (AC1)
2. `SendPathTests > "Resampler_NoAliasing"` (AC2)
3. `SendPathTests > "Resampler_LengthAndPassThrough"` (AC3)
4. `SendPathTests > "Resampler_AllocatesNothing"` (AC4)

(as built: `LinearResampler` is gone; `SincResampler` in `SendPath.cs` wraps NAudio's `WdlResampler` in sinc mode (64 taps) and keeps the shape of the old type (constructor with the two rates, `Process` returning a span into a reused buffer, the input itself at equal rates), so `CapturePipeline` only changed the type name; it still makes a new one when the capture rate changes (a device reopened at another rate by Package 137), dropping under 1 ms of queued input. Two findings in WDL shaped it: `ResamplePrepare` sizes its input buffer with `Array.Resize` to what each call needs, so chunks of changing size allocated about 2 KB per call; asked for a fixed number of output samples (output driven, not WDL's input driven feed mode) the buffer always has the same size, so the wrapper queues the input and takes 48 output samples (1 ms) at a time while the queue holds enough. And WDL interpolates linearly in its coefficient table: at its default of 32 steps that error left the folded 20 kHz tone only 27 dB below the linear resampler's, at 256 steps (a 65 KB table built once) it is 71 dB below. Latency: about 36 input samples (half the sinc plus WDL's 4 of look-ahead, 0.8 ms at 44.1 kHz) plus up to 1 ms in the queue, so under 2 ms from 44.1 kHz up and about 3, 4 and 5 ms at 16, 11 and 8 kHz. `SoundImport` used the old resampler too and now uses this one; it appends 10 ms of silence, at least 128 samples, so the end of the sound comes out, and cuts the result to the exact converted length. Tests: AC1 `Resampler_KeepsTone` (1 kHz fitted by correlation over whole periods: level within ±0.5 dB, the rest under 0.1 % of it); AC2 `Resampler_NoAliasing` (energy below 4 kHz in a Blackman-Harris windowed DFT, against a plain linear interpolation kept in the test as the reference; a second case downsamples a 30 kHz tone from 96 kHz and counts everything below 24 kHz, all of it folded back: linear keeps it at the level of a 1 kHz tone, the sinc at about -45 dB, the ceiling of 64 taps whose transition band reaches past 24 kHz; the test asks for 40 dB in both cases); AC3 `Resampler_LengthAndPassThrough` (10 s fed in chunks of 441, 440, 1000, 3, 448 and 1 samples give 480000 samples less at most 2 ms held back; 48 kHz comes back as the same memory); AC4 `Resampler_AllocatesNothing` (chunks of 441, 440 and 448 samples, one clean window of 100 as in `IdleCostTests`, whose 44.1 kHz capture check now runs through it too). The old `Resampler_44100To48000_KeepsDuration` and `Resampler_SameRate_PassesThrough` are folded into AC3. With the linear resampler under the new name only AC2 failed (both 15.9 dB); the first WDL wrapper failed AC2 and AC4 and `SoundImportTests.Wav_UpToFiveSeconds_Accepted_CopiedAsMono48k` (the held back end).)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 to 4 (red), then the wrapper.

### Out of Scope

- The output side (the mixer produces 48 kHz and WASAPI's shared mode converts)

---

## Package 139: Auto-reconnect

**Goal:** A lost connection is restored by itself, back into the same channel with the same mute state.

**Dependencies:** Package 126 (UDP comes back reliably on the new session)

**Affected files:**
- `src/OVS.Client/ViewModels/Reconnector.cs` (new): the attempt schedule and generation counter (A148), driven by a `TimeProvider`
- `src/OVS.Client/ViewModels/MainViewModel.cs` (change): `OnDisconnected` (line 463) starts the reconnector for `ConnectionLost` and `ServerShutdown`; the last `ConnectChoice` (with its password, in memory), the last channel and its password, and self mute and deafen are kept; after success the channel is joined and the state restored; "Abbrechen" stops it; no `Disconnected` sound while retrying (`DisconnectAsync`, line 449)
- `src/OVS.Client/Views/MainWindow.axaml` (change): the status with countdown and "Abbrechen"
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change)
- `tests/OVS.Tests/Client/MainViewModelTests.cs` (change), `ReconnectorTests.cs` (new)

### Context

A lost connection ends the session (`MainViewModel.cs:463-470`); auto-reconnect was out of scope until now and the user asked for it. Fluxer retries up to 5 times with exponential backoff within a 30 s window, never after a deliberate disconnect, and discards superseded attempts with a generation counter (`fluxer_app/.../VoiceEngineV2AppReconnectPolicy.ts`). The OVS server already replaces an old session of the same identity (`ServerState.cs:208-225`) and keeps server mute across reconnects (Package 85), so only the client changes.

### Acceptance Criteria

- [x] AC1: After `ConnectionLost`, attempts run after 1, 2, 4, 8 and 16 s; the first success ends the series; after the fifth failure the usual disconnected notice shows.
- [x] AC2: After a successful reconnect the user is in the channel they were in (also a password channel), with the same self mute and deafen, and one notice "Verbindung wiederhergestellt" shows.
- [x] AC3: `Kicked`, `Banned`, `ReplacedByNewConnection`, `VersionMismatch`, a wrong password and the user's own disconnect never start reconnecting.
- [x] AC4: "Abbrechen" stops the series at once; connecting to another server meanwhile also stops it, and a late result of an old attempt is ignored.
- [x] AC5: While retrying the status shows the countdown, no `Disconnected` sound plays, and the administration page is closed as on a disconnect.
- [x] AC6: If the last channel no longer exists or can no longer be joined, the user stays in the default channel with one notice.

### Tests (TDD)

1. `ReconnectorTests > "Schedule_1_2_4_8_16_ThenGivesUp"` (AC1), `ManualTimeProvider`.
2. `ReconnectorTests > "OutdatedAttempt_Ignored"` (AC4)
3. `ReconnectTests > "ConnectionLost_Reconnects_RestoresChannelAndMute"` (AC2): `TestServer` drops the connection; `"Reconnect_MutedWithOrWithoutDeafen_StaysMutedWhenUndeafening"`, `"Reconnect_PushToMuteHeld_NotKept"`, `"Reconnect_ChosenMute_HeldKeyReleased_StaysMuted"` and `"Reconnect_ServerHasWelcomeText_AddsOnlyTheReconnectedNotice"`.
4. `ReconnectTests > "Reconnect_DeliberateEndOfTheSession_StartsNoSeries"` (AC3), a theory over the reasons and `ConnectionViewModelTests > "Reconnect_ShownServerGoneBeforeTheDrop_NoSeries"` (AC3).
5. `ReconnectTests > "Reconnect_CancelledOrConnectedElsewhere_EndsTheSeriesAndDropsTheOldAttempt"` (AC4), `ConnectionViewModelTests > "CancelReconnect_NotReconnecting_DoesNothing"` and `"OnDisconnected_OldConnectionReportsLate_IsIgnored"` (AC4)
6. `ReconnectTests > "Reconnect_AttemptsRunning_StatusCountsDownWithoutToneUntilTheFifthFails"` (AC5)
7. `ReconnectTests > "Reconnect_ChannelGone_StaysInDefault"` (AC6)
8. `ReconnectTests > "Reconnect_RefusedWithANotNowCode_SchedulesTheNextAttempt"` (AC1), `"Reconnect_ServerRestarts_ConnectsAgainLikeAfterAShutdown"` and `"Reconnect_UnexpectedFailure_CountsAsFailedAttempt"` (AC1), `ConnectionViewModelTests > "Reconnect_AttachFailsBeforeOwningTheConnection_ClosesItAndContinues"`, `ConnectionViewModelTests > "Reconnect_AttachFailsAfterOwningTheConnection_TearsItDownAndContinues"` (AC1)
9. `ReconnectTests > "Reconnect_TofuRejected_EndsTheSeriesWithStatusNoticeAndTone"` (AC3), `ConnectionViewModelTests > "ConnectAsync_CertificateRefused_StatusSaysTofuRejected"` (AC3)
10. `ReconnectTests > "Reconnect_AttemptAfterCancel_IsDroppedAndDisposed"` (AC4)
11. `ReconnectTests > "Reconnect_AfterADrop_ReusesTheChoiceOfTheConnectWithoutCancellation"` (AC2)
12. `ReconnectTests > "Reconnect_AttemptFailingAfterCancel_IsOnlyLogged"` (AC4)

(as built: `Reconnector` (`ViewModels/Reconnector.cs`) takes a `TimeProvider` and the UI post; `Start(attempt, gaveUp)` plans the attempts 1, 2, 4, 8 and 16 s apart (`Delays`), each counted from the end of the one before (so at 1, 3, 7, 15 and 31 s when they fail at once), hands each attempt its generation, ends the series on the first success and calls `gaveUp` after the fifth failure; `Stop` and a new `Start` raise the generation, so a result that comes back later is ignored (`IsCurrent(generation)`), and `NextAttemptAt` is the countdown's target (null while an attempt runs). `MainViewModel.ConnectAsync` is split into `OpenAsync` (status, log line, handshake) and `Attach` (the session as before), which also keeps the choice in memory as `lastChoice` (without the bookmark flag, the password never written anywhere). `OnDisconnected` starts a series for `ConnectionLost`, `ServerShutdown` and, added in the review (A148 updated), `ServerRestart` (not for `Restoring`); it keeps the channel I was in, its password from `ServerViewModel.PasswordFor` (the in-memory channel passwords of Package 94), my self mute and deafen (the state from before a running self test, Package 53; a mute that only comes from a held push-to-mute key, `ServerViewModel.MutedByHold`, is not kept, so a key let go during the outage leaves the microphone on and one still held mutes again through `HoldMute` and unmutes on release) and whether undeafening unmutes (`MutedBeforeDeafen`, so muted-then-deafened stays muted after undeafening), and tears the session down like a disconnect but without the "Getrennt" tone (`DisconnectAsync(status, sound: false)`), which also closes the administration page and ends the self test. The start screen shows `Status` "Verbindung verloren, neuer Versuch in N s" / "Connection lost, retrying in N s" (`Status_Reconnecting`, N rounded up, refreshed by `Tick`; during an attempt its "Verbinde mit ..." stays) and a button "Abbrechen" (`CancelReconnect`, visible while `IsReconnecting`). An attempt that connects while still current is attached like a fresh connect (the "Verbunden" tone plays, the welcome text is not posted again), then `ServerViewModel.RejoinAsync` sends the join with the kept password and never a dialog (the "Channel betreten" tone follows as for an own join), `ServerViewModel.RestoreSelfStateAsync(muted, deafened, mutedBeforeDeafen)` restores them in one `SetSelfState` when mute or deafen was on (keeping a mute of a key held right now), and one notice "Verbindung wiederhergestellt" / "Connection restored" (`Status_Reconnected`) is posted; server mute comes from the server (Package 85). A channel that no longer exists gives the notice "Der Channel "{0}" ist nicht mehr da, du bist im Standard-Channel." (`Status_ReconnectChannelGone`) without a join; one that refuses the join leaves me in the default channel with the server's error as the one notice (for example "Falsches Channel-Passwort."). An attempt the server refuses (a `Rejected` other than `ServerShutdown`, `ServerRestart` and `RateLimited`, which retry: e.g. a new server password, a ban, another protocol version) or a refused certificate ends the series at once with the disconnected notice, the "Getrennt" tone and the refusal as the status; network failures and, after the review, any unexpected exception (logged, a connection already opened disposed, a half attached session torn down) count as a failed attempt and go on to the next. After the fifth failure the disconnected notice, status and tone come as before Package 139; "Abbrechen" gives the notice and status without the tone. `lastChoice` (with the server password) is cleared on the own disconnect and whenever a series ends without a session (`EndSeries`). Connecting anywhere, the own disconnect and closing the app go through `DisconnectAsync()`, which stops the series; an outdated attempt that still gets a connection disposes it ("Wiederverbinden: veralteter Versuch verworfen" in the log) and never touches the current session. Tests: AC1 `ReconnectorTests.Schedule_1_2_4_8_16_ThenGivesUp` (manual clock: attempts at 1, 3, 7, 15 and 31 s, given up after the fifth, nothing after; a second series ends at its second attempt) and `MainViewModelTests.Reconnecting_StatusNoSound` (the five refused attempts against a stopped server, then the notice and tone); AC2 `MainViewModelTests.ConnectionLost_Reconnects_RestoresChannelAndMute` (the server aborts the session; back in the password channel without a dialog, muted and deafened at the server, a new session, exactly one new notice, no password in the log); AC3 `MainViewModelTests.NoReconnect_ForDeliberateReasons` (Kicked, Banned, ReplacedByNewConnection and VersionMismatch sent as `Disconnected`, the own disconnect, and a server that comes back with another password refusing the attempt with WrongPassword; no series or no further attempt, no session after 60 s); AC4 `ReconnectorTests.OutdatedAttempt_Ignored` and `MainViewModelTests.Reconnect_Cancel_AndOtherServer` ("Abbrechen", then an attempt held at the certificate question of a server back with a new certificate while the user connects elsewhere: the late attempt is dropped and its server sees no session); AC5 `MainViewModelTests.Reconnecting_StatusNoSound` (the administration page closes, the countdown counts 1, 2, 4, 8 and 16 s down, no "Getrennt" tone and no notice while retrying, no ping text) and `ResponsiveTests.Reconnecting_StatusAndCancelFit360` (de and en at 360 px); AC6 `MainViewModelTests.Reconnect_ChannelGone_StaysInDefault` (the channel deleted, or its password changed, by an admin while I am away: default channel, two notices in all). Review tests: `MainViewModelTests.Reconnect_KeepsMutedUnderDeafen` (muted alone, and muted then deafened: after the reconnect and undeafening still muted, at the server too), `Reconnect_PushToMuteHeld_NotKept` (push-to-mute held when the connection drops, let go during the outage or after the reconnect: unmuted, at the server too), `ServerRestart_Reconnects` (`TestServer.RestartAsync(samePort: true)`), `Reconnect_UnexpectedFailure_CountsAsFailedAttempt` (the certificate question of a server back with a new certificate throws once: the countdown to the second attempt shows and that one connects) and the tone in the WrongPassword case of `NoReconnect_ForDeliberateReasons`; with the hold mute kept, the plain `SetSelfStateAsync`, `ServerRestart` left out or only network failures caught, these fail (the muted-only case passes either way, it guards the common path). Re-review: `RestoreSelfStateAsync` clears the hold flag when it restores a mute or deafen, so a chosen mute with push-to-mute held through the reconnect stays after the release (`Reconnect_ChosenMute_HeldKeyReleased_StaysMuted`, muted and muted plus deafened, red without it); `lastChoice` is also cleared when a disconnect starts no series; `Attach` takes ownership of the connection and the `VoiceClient` before wiring `Audio.Send` and starting it, so a failure after that is torn down with both by `DisconnectAsync`. The retry on a `Rejected` with `ServerShutdown`, `ServerRestart` or `RateLimited` has no test of its own (a test server does not refuse handshakes that way on demand). `MainViewModelTests.Notices_HaveKinds_ErrorAndDisconnect` stops the server and now cancels the series to get its disconnected notice. `TestServer.StartAsync` got an optional fixed `port` so a server can come back where the client reconnects. The tests failed to build before `Reconnector` and the strings existed; with the tone kept, the rejoin skipped or `Kicked` added to the reasons, AC2, AC3 and AC5 fail. Not checked by hand: a real network drop, such as Wi-Fi going away during a call.)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 and 2 (red): `Reconnector` as pure logic.
2. Tests 3 and 4 (red): the wiring in `MainViewModel`.
3. Tests 5 to 7 (red): cancel, status, edge cases.

### Out of Scope

- Restoring a session after an app restart (Fluxer keeps one for 5 minutes)
- Server-side state caches (not needed, see Context)

---

## Package 140: Echo cancellation

**Goal:** Users on speakers can use voice activation without the others hearing themselves.

**Dependencies:** Package 127 (the same filter chain in `CapturePipeline.Feed`)

**Affected files:**
- `native/ovs_aec/` (new): the C wrapper around `webrtc::AudioProcessing` (create, set delay, process render 10 ms, process capture 10 ms, destroy) and its build script; the built `ovs_aec.dll` (win-x64) checked in, its sha256 in the notes
- `src/OVS.Client/Audio/EchoCanceller.cs` (new): loader and calls like `NoiseSuppressor` (null when missing, one log line)
- `src/OVS.Client/Audio/AudioEngine.cs` (change): the mixed frame after the limiter goes to the canceller as the far-end reference; the delay hint from the capture and output buffer sizes
- `src/OVS.Client/Audio/CapturePipeline.cs` (change): echo cancellation first in `Feed`, before noise suppression
- `src/OVS.Client/Settings/ClientSettings.cs`, `ViewModels/SettingsViewModel.cs`, `Views/SettingsView.axaml` (change): `EchoCancellation` (default on, also for existing files) in the "Sprachfilter" card, live
- `src/OVS.Client/OVS.Client.csproj`, `THIRD-PARTY-NOTICES.md` (change)
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change)
- `tests/OVS.Tests/Client/VoiceFilterTests.cs`, `SettingsTests.cs` (change)

### Context

OVS has no echo cancellation (A18, A131): someone on speakers with voice activation sends the others' voices back to them. Fluxer has it on by default through WebRTC in the browser (`VoiceProcessingProfile.ts`, voice mode), and its desktop app already wraps the same WebRTC audio processing library natively (`fluxer_desktop/native/audio-apm`, not active yet). The freedesktop project packages that library standalone under BSD-3; its API is C++, so a small C wrapper is needed for P/Invoke (A149). The far end is easy to tap: the mixer produces exactly what goes to the speaker (`Mixer.Tick`).

### Acceptance Criteria

- [x] AC1 (spike): the wrapper DLL builds for win-x64, loads from the published single-file client and processes a frame; otherwise the package stops here and the findings go into the notes.
- [x] AC2: A speech-like far-end signal played back and fed into the capture with 60 ms delay at -6 dB is reduced by at least 25 dB in the sent signal after 2 s of adaptation, while a near-end voice at the same time stays within 3 dB.
- [x] AC3: Without a far-end signal the capture passes with less than 1 dB change.
- [x] AC4: The switch applies live and defaults on (a file without the key loads it on); a missing DLL turns it off with one log line.
- [x] AC5: All filters together stay below 2 ms per 20 ms frame on the dev machine (measured once, in the notes).
- [ ] AC6: Manual check: two clients, one on laptop speakers with voice activation; the other side hears no echo of itself.

### Tests (TDD)

1. `VoiceFilterTests > "EchoCanceller_Loads"` (AC1)
2. `VoiceFilterTests > "EchoCanceller_RemovesFarEnd_KeepsNearEnd"`, `PlaybackLoopTests > "MixForSpeaker_CaptureHasCanceller_FedOnlyWhileOnInVoiceMode"` (AC2)
3. `VoiceFilterTests > "EchoCanceller_NoFarEnd_Unchanged"` (AC3)
4. `SettingsTests > "EchoCancellation_DefaultLiveMissingLib"`, `NativeCaptureFiltersTests > "AttachTo_LibrariesMissing_NoFiltersAndOneLogLineEach"` (AC4)

(as built: the spike first stopped because the dev machine had no C++ toolchain for Windows (no Visual Studio or Build Tools, no MinGW, no zig; the Android NDK clang lacks a Windows C++ library). On the coordinator's go-ahead it was retried with zig's clang from PyPI, installed per user (`pip install --user ziglang==0.16.0 meson==1.12.1 ninja==1.13.2`), and that worked, so the DLL is built with zig, not MSVC: `native/ovs_aec/build.ps1` clones `webrtc-audio-processing` at tag `v2.1` (abseil-cpp `20240722.0` through its meson wrap), builds static libraries with meson for `x86_64-windows-gnu` (cross file `native/ovs_aec/zig-x86_64-windows-gnu.ini`; it sets `NTDDI_VERSION` to Windows 10 because zig's MinGW headers lack `windows.globalization.h` that abseil's time zone code includes at the Windows 11 level) and links `native/ovs_aec/ovs_aec.cpp` (`ovs_aec_create`, `ovs_aec_set_delay`, `ovs_aec_render`, `ovs_aec_capture`, `ovs_aec_destroy`, 48 kHz mono float, 10 ms) with them into one stripped `ovs_aec.dll`; libc++ and the MinGW runtime are linked statically. Visual Studio 2022 Build Tools were installed later during the package; the zig DLL was kept because it already loaded and passed, needs no VCRUNTIME, and rebuilds without Visual Studio. The build is reproducible: a rebuild from a wiped build folder gives the same file, and the script checks that the checkout is at `v2.1` (commit `846fe90a`), that zig, meson and ninja have the pinned versions, and that the built DLL has the expected sha256 kept in the script before it replaces the checked-in one. The wrapper lets no C++ exception cross into .NET (create catches everything and returns null, the other exports are `noexcept`) and every export does nothing on a null handle. Checked in as `src/OVS.Client/runtimes/win-x64/native/ovs_aec.dll` (1 275 392 bytes, sha256 `09055b6a599b67a6c5f88103fce50ff159644668912ff8335287266e5abcae2f`), copied next to the client by a `None` item, bundled and extracted by the single-file publish like `rnnoise.dll`. It imports only Windows DLLs: `KERNEL32.dll`, `WINMM.dll` and the UCRT API sets `api-ms-win-crt-*` (convert, heap, locale, math, multibyte, private, runtime, stdio, string, time), no VCRUNTIME or libc++ DLL (llvm-readobj). It also exports WebRTC's own C++ symbols (the library marks them for export), which nothing uses. AC1: the published single-file client (`dotnet publish` as in `release.yml`), started with `--no-audio --debug-api` and a test tone, had `ovs_aec.dll` and `rnnoise.dll` loaded from its extraction folder and logged no fallback line; `EchoCanceller_Loads` checks loading and a frame. `EchoCanceller` follows `NoiseSuppressor` (NativeLibrary and function pointers, null with the reason when missing; `AudioEngine` logs "Echounterdrückung aus: ..." once per engine, tests name a missing library through `AudioEngine.EchoLibrary`). It is created with every capture start, also while switched off, and owned and disposed by the pipeline like the suppressor; device reopens (Package 137) start a new one that adapts again. Order in `Feed`: echo cancellation first, before the input gain (whose soft clip above 100 % would make the echo path nonlinear), then noise suppression, automatic gain, voice gate. Only the echo canceller is enabled; its forced high-pass filter is off (`enforce_high_pass_filtering = false`) because it took 1.4 dB off the test voice without any echo (AC3). Threads: `Render` runs on the playback thread, `Process` on the capture thread under the pipeline's lock; the library locks its render and capture sides itself, `Render` and `Dispose` share a small lock of their own, and the stream delay is set right before each capture call on the capture thread as the library requires. Nothing is allocated per frame (`Filters_AllocateNothingPerFrame` now includes it). The far end is the final frame written to the speaker buffer: the mixer's frame after the limiter plus the tones (they reach the speaker too), and only when a speaker is open and the switch is on (`AudioEngine.MixForSpeaker`, tested by `EchoCanceller_FedFromSpeakerMix_OnlyWhenOn`). The delay hint `AudioEngine.EchoDelayMs` is 90 ms (40 ms playback buffer, 30 ms speaker device buffer, 20 ms microphone buffer); AEC3's delay estimator finds the real delay itself, the hint is mainly the alignment it starts from, at first and after the estimator resets (the tests use the 90 ms hint on a 60 ms path). Self test: echo cancellation stays on there; the reference is the own voice played back, so its echo through speakers is removed (no feedback loop) while the live voice, which does not match the delayed reference, passes; with a headset nothing changes. Music mode (Package 142) will switch it off with the other filters. The setting `EchoCancellation` defaults to true, and a file without the key loads true from the property default, so no migration step is needed. Measured (AC2, test signals): echo at -26 dBFS reduced by 34.2 dB after 2 s, near-end voice during double talk -2.0 dB; AC3: -0.47 dB without a far end. AC5: all filters together (echo render and capture, RNNoise, automatic gain, voice gate) 0.30 ms per 20 ms frame (Release, dev machine, 1000 frames after warm-up, measured once). The licenses of WebRTC (BSD-3 and its patent grant, with the Ooura FFT, `modules/third_party/fft`, PFFFT, the public domain sqrt and its RNNoise copy for `rnn_vad`, also named in the RNNoise section), Abseil (Apache-2.0), the MinGW-w64 runtime (ZPL 2.1, winpthreads MIT) and a note on libc++ (Apache-2.0 with LLVM exception) are in `THIRD-PARTY-NOTICES.md`. AC6 (two clients, laptop speakers, voice activation) needs people and hardware and is open; two things to watch there: the forced high-pass filter is off, so DC offset or low rumble of a microphone reaches the canceller and the sent signal unfiltered (RNNoise takes some of it), and the playback loop writes render frames in bursts (it tops the 40 ms buffer up, then sleeps 5 ms), so the far end does not arrive evenly every 10 ms; AEC3 buffers render frames, but bursts and gaps may show up as a slower start or short echo after pauses.)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Spike (AC1): build the library and the wrapper, load it from a test; stop and report if impossible.
2. Tests 2 and 3 (red): `EchoCanceller` and the reference tap.
3. Test 4 (red): setting and card.
4. Timing (AC5) and the manual check (AC6).

### Out of Scope

- The library's own noise suppression and gain control (RNNoise and the own gain stay)
- Other platforms (the client is Windows only, A3)

---

## Package 141: Voice over TCP

**Goal:** Voice works on networks that block UDP, over the existing TLS connection.

**Dependencies:** Package 126 (reliable "UDP unreachable" detection)

**Affected files:**
- `src/OVS.Shared/Protocol/Messages.cs` (change): `VoiceUp` and `VoiceDown` (A150)
- `src/OVS.Shared/Protocol/ProtocolInfo.cs` (change): version + 1
- `src/OVS.Server/ControlServer.cs`, `src/OVS.Server/Limits.cs`, `src/OVS.Server/Session.cs` (change): accept `VoiceUp` within its own budget (60 per second, burst 10), route it through `ServerState.VoiceRecipients`, mark the session as TCP voice
- `src/OVS.Server/Voice/UdpVoiceServer.cs` (change): one relay method for UDP and TCP senders that sends to each recipient on its own path
- `src/OVS.Client/Net/VoiceClient.cs`, `Net/ClientConnection.cs` (change): a voice transport that switches to TCP per A150 and back when UDP answers again; `VoiceDown` raises `VoiceReceived` like UDP
- `src/OVS.Client/ViewModels/MainViewModel.cs` (change): the ping text ", Sprache über TCP"
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change)
- `tests/OVS.Tests/Server/VoiceRelayTests.cs`, `VoiceRoutingTests.cs`, `ServerTuningTests.cs`, `Client/ClientNetTests.cs` (change)

### Context

Voice is UDP only; a TCP fallback was out of scope until now and the user asked for it. Fluxer gets one from LiveKit (ICE over TCP on port 7881, `deploy/self-hosting/docker-compose.yml`). OVS already holds a TLS connection to every client, so voice can travel there when UDP does not work, at the cost of head-of-line blocking on loss. Routing and rights must stay exactly those of UDP voice (`VoiceRouting.cs`, Package 12), including the per-sender rate limit.

### Acceptance Criteria

- [x] AC1: With all UDP blocked, two clients hear each other within 6 s of connecting, over TCP.
- [x] AC2: A TCP client and a UDP client in the same channel hear each other.
- [x] AC3: Rights and routing are the same as for UDP: a muted sender, a deafened recipient, link targets and the `Speak` right behave identically (the routing tests run for both paths).
- [x] AC4: More voice than the budget over TCP is dropped without disconnecting; other control messages keep their own limits.
- [x] AC5: When UDP becomes reachable again, the client switches back within 30 s; the ping text shows the transport in use.
- [x] AC6: The protocol version is raised.

### Tests (TDD)

1. `VoiceRoutingTests` and `VoiceRelayTests` extended with a TCP variant (AC3)
2. `VoiceRelayTests > "TcpAndUdp_Mixed"` (AC2)
3. `ServerTuningTests > "VoiceUp_OwnBudget"` (AC4)
4. `ClientNetTests > "UdpBlocked_VoiceOverTcp"` (AC1, AC6)
5. `ClientNetTests > "UdpBack_SwitchesBack"` (AC5), `ConnectionViewModelTests > "Tick_VoiceOverTcpInUse_PingTextNamesItUntilUdpAnswersAgain"` (AC5)

(as built: `VoiceUp(Seq, Target, Opus)` and `VoiceDown(SessionId, Seq, Target, Opus)` carry the frame number and the Opus frame as base64; protocol version 15. An empty `VoiceUp` only moves the session's voice to TCP, so a client that only listens gets `VoiceDown` too (the plan named no switch message); the client repeats it with every Hello while on TCP (review), as one can be lost or overtaken by an older UDP ping, and it takes no voice token (it relays nothing, like a `Ping`), so the repeats cannot starve the UDP Hellos that share the budget. It carries the next frame number. The server marks the session `TcpVoice` on a `VoiceUp` and clears it on a UDP ping or voice packet, not on a Hello, which is only the client's probe. The server sends each frame to each recipient on one path only, and the client sends each frame once, so no frame is duplicated; during a switch the server may still use the old path for a moment (the client accepts `VoiceDown` on either path). Stale TCP voice after a switch back is limited (review): the client drops the `VoiceUp` frames still queued when a Hello is answered, and the server ignores a `VoiceUp` whose frame number is not newer than the sender's last UDP frame (`Session.LastUdpFrame`), so it neither plays twice nor moves the session to TCP again; at most the one frame being written can still arrive, and the next UDP ping undoes its switch. Likewise a listen-only client's announce in flight can set `TcpVoice` again after the ping that cleared it; harmless (the client accepts `VoiceDown`) until its next ping clears it. `VoiceUp` is no request: the control read loop hands it (like `Ping`) to `UdpVoiceServer.VoiceUpAsync` through `ServerState.VoiceOverTcp`, which checks it like a UDP payload (target 0 or 1, base64 length before decoding, at most `MaxOpusSize` bytes; anything else dropped quietly) and takes a token of the sender's voice budget `Session.Limiter` (`ServerTuning.VoicePerSecond` 60, `VoiceBurst` 10), the same bucket as its UDP packets and never the request budget. Over the budget a valid frame is dropped quietly like UDP: TCP delivers in bursts after a retransmission stall, so an honest talker exceeds the burst of 10 now and then (second review). Invalid ones (target, size, base64) count toward Package 86's flood rule with their own clock (`Session.RefuseVoice`, sharing `Flooding` with `TakeRequest`): invalid ones without a pause over `FloodPause` for `FloodDisconnectAfter` end the session with `RateLimited` (`ServerState.DisconnectFlood`). UDP refusals do not count (spoofable). A repeated empty switch on a session already on TCP returns before the voice gate. After the way in, UDP and TCP senders share `VoiceAsync` (uplink window of 133, routing through `ServerState.VoiceRecipients`, the speech time of 70) and `RelayAsync` (`TrackFrame` and the copies of 136); a `SemaphoreSlim` in `UdpVoiceServer` now serialises the UDP loop and the `VoiceUp` handlers, as both touch the sessions' voice state. `VoiceRouting.Recipients` accepts a bound UDP endpoint or `TcpVoice`. A TCP listener never gets copies (a reliable leg loses nothing) but, like UDP listeners without copies, a frame recovered from a UDP sender's copy as its own `VoiceDown` first; `VoiceDown` is encoded once per frame for all TCP listeners and queued with `Session.SendVoice`, which drops it (never aborts) while the outbox holds `ServerTuning.MaxQueuedVoice` (15, 300 ms after review, so a slow listener's delay stays bounded) messages or half its bytes, so a slow reader loses voice, not its connection, and holds up nobody. Loss over TCP: the server still counts the frames in the uplink window (so a switch makes no false gaps), the client reports `UplinkLoss` as zero while on TCP, counts `VoiceDown` in its downlink window and sends no copies there; the loss adapter (135) gets no figures on TCP and keeps its bitrate. Client: `VoiceClient` takes the `ClientConnection` and moves to TCP when UDP has not answered 5 s after `Start`, or at once when lost (15 s, Package 126); it goes back as soon as a Hello is answered, and its next ping moves the server back. Deviation: UDP is tried with 126's Hellos every 2 s, not every 30 s, which meets AC5's 30 s and needs no extra timer. `ClientConnection.SendVoice` never waits: a bounded queue of 10 frames (200 ms, more is dropped) written in order by its own loop (not marked single-reader, as `DropPendingVoice` reads too), emptied by `DropPendingVoice` on the switch back; `VoiceDown` is raised by its own event on the receive thread, outside `MessageReceived`. The ping text shows ", Sprache über TCP" / ", voice over TCP" (`Ping_VoiceOverTcp`) instead of the UDP warning; the signal icon stays crossed out while UDP is unreachable. Tests: `VoiceRoutingTests` run routing, rights, deafened and muted cases for both paths, `VoiceRelayTests > Tcp_RoutingLikeUdp`, `Tcp_InvalidVoice_Dropped` and `TcpAndUdp_Mixed` (AC2, AC3), `Tcp_StaleVoiceUp_AfterUdp_Ignored`, `ServerTuningTests > VoiceUp_OwnBudget`, `VoiceUpFlood_Disconnected_TalkingIsNot` (also bursts of 20 every second) and `VoiceDown_SlowListener_DroppedNotAborted` (AC4), `ClientNetTests > UdpBlocked_VoiceOverTcp` (AC1, AC6; also the repeated switch) and `UdpBack_SwitchesBack` (AC5), `MainViewModelTests > VoiceOverTcp_ShownInPingText` (AC5); `VoicePacketTests` no longer pins the version, `SmokeTests` pins 15. Not tested on their own: that `RelayAsync` queues TCP voice through the dropping `Session.SendVoice` (seen in the code; a reliable test needs a listener whose socket buffers fill) and the client's cap of 10 queued frames (racy against its write loop).)

Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Tests 1 and 2 (red): the shared relay method with both paths on the server.
2. Test 3 (red): the budget.
3. Tests 4 and 5 (red): the client transport switch.

### Out of Scope

- Voice over WebSocket or HTTPS on port 443 (proxies that only allow web traffic)

---

## Package 142: Music mode

**Goal:** A sender can switch to a music mode that sends stereo without voice filters.

**Dependencies:** Package 132 (the channel bitrate decides stereo), Package 140 (echo cancellation is one of the filters turned off)

**Affected files:**
- `src/OVS.Client/Audio/OpusCodec.cs` (change): `VoiceEncoder` in music mode: `OPUS_APPLICATION_AUDIO`, `OPUS_SIGNAL_MUSIC`, 2 channels at ≥ 64 kbps; `VoiceDecoder` always decodes to 2 channels
- `src/OVS.Client/Audio/CapturePipeline.cs` (change): `ToMono` (line 136) skipped in music mode (frames of 2 × 960 samples); filters off
- `src/OVS.Client/Audio/SendPath.cs` (change): chunker and resampler handle 2 channels; the VAD measures the louder channel
- `src/OVS.Client/Audio/Mixer.cs`, `JitterBuffer.cs`, `AudioEngine.cs`, `Sounds.cs` (change): mix and play in stereo (output opened as 48 kHz stereo float); sounds and mono speakers play on both sides
- `src/OVS.Client/Settings/ClientSettings.cs`, `ViewModels/SettingsViewModel.cs`, `Views/SettingsView.axaml` (change): "Übertragungsart" Sprache / Musik, live; the filter switches disabled in music mode
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change)
- `tests/OVS.Tests/Client/SendPathTests.cs`, `ReceivePathTests.cs`, `SettingsTests.cs`, `IdleCostTests.cs` (change)

### Context

The whole client is mono (`AudioFormat` in `OpusCodec.cs`, `ToMono` downmixes every capture) and tuned for speech (VOIP application, voice signal type, filters). Fluxer's "studio" mode turns all processing off and allows stereo only from 64 kbps (`VoiceProcessingProfile.ts`, `NoiseSuppressionSelection.ts`, `AudioPublishOptions.ts`). Opus packets carry their channel count, so receivers need no protocol change: a stereo decoder plays mono packets on both sides (A151). This touches the whole audio path, which is why it comes last.

### Acceptance Criteria

- [x] AC1: In music mode in a 64 kbps channel, a stereo test signal (different tones left and right) arrives at a receiver with each tone on its own side (crosstalk below -20 dB).
- [x] AC2: In music mode in a 32 kbps channel the signal is sent in mono.
- [x] AC3: In music mode noise suppression, automatic gain and echo cancellation are not applied, whatever their switches say; back in voice mode they apply again.
- [x] AC4: Mono speakers and the sounds play equally on both sides; the limiter still keeps every sample below its threshold.
- [x] AC5: Push-to-talk and voice activation work in music mode; the self test plays stereo.
- [x] AC6: The setting saves, applies live, and voice mode is the default.
- [x] AC7: Nothing is allocated per frame beyond what the Opus calls allocate (the `IdleCostTests` rule from Package 124).

### Tests (TDD)

1. `ReceivePathTests > "Stereo_MonoSpeakerBothSides_Limited"` (AC4)
2. `SendPathTests > "MusicMode_StereoArrivesSeparated"` (AC1): sender pipeline to a receiver's mixer through the codec.
3. `SendPathTests > "MusicMode_LowBitrate_Mono"` (AC2)
4. `SendPathTests > "MusicMode_FiltersOff"` (AC3)
5. `SendPathTests > "MusicMode_PttAndVad"` (AC5)
6. `SettingsTests > "TransmissionMode_DefaultLive"` (AC6)
7. `IdleCostTests` extended (AC7)

(as built: the setting is `ClientSettings.Transmission` (`Transmission.Voice` or `Music`, the enum next to `TransmitMode` in `SendPath.cs`), default `Voice`; a file without the key loads `Voice` from the property default, so no migration step. The settings page shows "Übertragungsart" / "Transmission type" with the radio buttons "Sprache" / "Musik" ("Speech" / "Music") and a hint at the top of the voice filter card (review: it recommends headphones, "Kopfhörer empfohlen", as music is sent without echo cancellation); while music is chosen the three filter switches are disabled but keep their values for speech. It is previewed live like the filters (`ApplyLive` sets `CapturePipeline.Music` on the running capture, `StartCapture` on a new one) and named in the settings log line. Receive side: `VoiceDecoder` always decodes to 2 channels (`AudioFormat.StereoFrameSamples`, 2 x 960 interleaved), so a mono packet plays on both sides; the jitter buffer works on whole decoded frames, so copies (136), FEC rebuilding and concealment work unchanged for stereo frames. The stereo decoder rounds the two sides of a mono packet on its own (they differ by about 1e-5). The mixer sums stereo frames, the limiter keeps one gain for both sides (the image does not move), `SoundQueue.MixInto` plays the mono tones on both sides, the speaker opens as 48 kHz stereo float, and `LastOutputLevelDb` is the louder side. The echo canceller's far end is the speaker frame downmixed to mono, (L + R) / 2, which is exactly the old reference for mono voices and what the speakers play when a voice-mode listener hears music from others; `MixForSpeaker` skips it while the own capture is in music mode. Send side: the device callback converts with the new `ToStereo` (the first two channels, without Package 50's one-sided rule) when music is on and the device has at least 2 channels, otherwise with `ToMono` as before (a mono device stays mono). `Feed(samples, rate, channels)` reads the music flag once per chunk; a stereo chunk that arrives just after switching back to speech is averaged to mono, and a change of the channel count starts the resampler, the chunkers and the pre-roll anew (the pre-roll is then silence). `SincResampler` and `FrameChunker` take a channel count (WDL resamples interleaved channels). In music mode echo cancellation, noise suppression and automatic gain are skipped; the input gain stays (it is the volume slider). The voice gate measures the louder side (`VoiceActivityDetector.LevelDb(frame, channels)`). Encoders: the voice encoder (mono, VOIP, voice) is unchanged; music has one more, made on first use: a stereo encoder with `OPUS_APPLICATION_AUDIO` and `OPUS_SIGNAL_MUSIC` that sends stereo when the frame is stereo and the bitrate asked for that frame is at least 64 kbps (`VoiceEncoder.StereoFromKbps`), and mono otherwise through Opus' forced channel count (`VoiceEncoder.Mono`, `ForceChannels` 1 or 2), so the same encoder downmixes and a switch across 64 kbps keeps its state; a mono microphone's frame is doubled into a reused stereo buffer for it. The bitrate asked for is the loss adapter's (135), so a halving below 64 kbps sends mono and stepping up again stereo; switching between the voice and the music encoder resets the one taken over. Review: on a stereo device in music mode `ToStereo` applies Package 50's one-sided rule through `StereoInput`, one per opened device, with hysteresis, since a wrong stereo decision only costs 6 dB on one side while a wrong one-sided one discards the other input: it goes one-sided (that side copied to both at full level) only after one side has been at least 6 dB above the other in every chunk with signal for 2.5 s (`StereoInput.OneSidedAfter`), and back to stereo as soon as the weak side, averaged over about 100 ms, rises to within 10 dB of the strong one, e.g. a voice microphone joining a guitar on the other input. Chunks below -70 dBFS on both sides change nothing, so a quiet instrument is still judged. A first version kept the decision for the device's lifetime, which hid a second input added later until a reopen. A stereo source panned hard for less than the window stays stereo. The self test sends as a 64 kbps channel, so it hears stereo. Deviation from A151 as first planned ("FEC stays on"; A151 now says so): the music encoder has in-band FEC off. Opus carries FEC only in its speech coder (SILK), so with FEC on it leaves the music coder (CELT), and SILK's stereo blurs the sides: at 64 kbps a tone on one side came out 2.6 dB below it on the other with a bare Concentus encoder, and about as loud in the pipeline (AC1 failed); with FEC off they are more than 70 dB apart. Also in mono: with the initial loss hint of 10 % Opus would otherwise use SILK for music. A lost music frame comes from the copy (136) or the decoder's concealment; the jitter buffer counts a frame lost before a music coder packet (CELT only, `VoiceDecoder.MayCarryFec` reads the table of contents) as concealed instead of rebuilt; whether a SILK or hybrid packet really carries FEC would need its range coder, so those still count as rebuilt. Packet size: 128 kbps stereo noise frames plus the copy of the previous frame fit one voice packet (`RelayPayload.MaxSize`, 1368 bytes for both). Tests: `ReceivePathTests > Stereo_MonoSpeakerBothSides_Limited` (AC4; also a rebuilt and a concealed frame and the tones), `SendPathTests > MusicMode_StereoArrivesSeparated` (AC1: a 440 Hz and a 1 kHz tone through the pipeline, the codec and a receiving mixer at 64 kbps, the other side's tone below -70 dB; also a stereo frame played from its copy, a lost one concealed and counted so, the 128 kbps packet with its copy, and the 44.1 kHz stereo resampler keeping the sides apart), `MusicMode_LowBitrate_Mono` (AC2: 32 kbps, 64 and a halving back with the same encoder and no gap or dip in the level heard across the switches, a mono microphone at 128 kbps, voice mode at 128 kbps), `MusicMode_OneInputInstrument_BothSides` (review: an instrument on either input, also one at about -65 dBFS, plays on both sides at full level after the window; 4 s of guitar alone and then a second input brings stereo back with both heard on their own sides from 200 ms on, and after 2 s alone at once; a stereo source panned hard for half the window stays stereo), `MusicMode_FiltersOff` (AC3: an echo with all three switches on passes untouched in music mode and is reduced by more than 10 dB 2 s after switching back; the automatic gain alone likewise; the speaker mix feeds the canceller only in voice mode), `MusicMode_PttAndVad` (AC5: push-to-talk with the real bindings, the self test sending stereo packets, the voice gate opening on music on one side only), `SettingsTests > TransmissionMode_DefaultLive` (AC6, with the disabled switches in the rendered page), `IdleCostTests > SteadyState_AllocatesNothing` (AC7: stereo capture at 44.1 kHz 16 bit and at 48 kHz float allocates nothing while not sending; sending in stereo allocates exactly what an own stereo music encoder allocates on the same frames, measured in turns because the runtime's warm-up allocations shift between two loops measured one after the other; the decoder and the mixer tick are now compared with stereo Concentus decoders taken in turns with them; the tick, with two decoders of its own, sometimes came out a few hundred bytes below twice the bare one after other audio tests, so it must be at most that). Existing tests follow the stereo frames (frame lengths, `SoundTests`, the mixer reference), and `MotionCoverageTests` looks for the radio button chosen before within its own group, as the page now has two. The settings page fits at 360 px (`ResponsiveTests`). Not checked: how real music or an instrument sounds through a real stereo interface (no AC asks for it).)
Test command: `dotnet test tests/OVS.Tests`

### Steps

1. Test 1 (red): the receive side in stereo first (it works for everyone; mono senders are unchanged).
2. Tests 2 to 4 (red): the send side in music mode.
3. Tests 5 to 7 (red): modes, setting, allocations.

### Out of Scope

- Stereo for speech (voice mode stays mono, like Fluxer's default)
- Sending system audio or a second input
