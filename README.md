<img src="docs/logo.png" alt="OpenVoiceSpeak-Logo" width="96">

# OpenVoiceSpeak

Selbst hostbarer Voice-Chat in der Art von Mumble oder TeamSpeak, bewusst einfach gehalten:

- ein Server mit mehreren Channels
- ein **serverweites** Rechtesystem aus Gruppen und Rechten, ohne Rechte pro Channel
- **Channel-Linking**: Normale Sprache bleibt im eigenen Channel. Mit der Link-PTT-Taste sprichst du zusätzlich in alle direkt verlinkten Channels.
- Text-Chat serverweit, im eigenen Channel und privat zwischen zwei Personen. Jede Art hat ein eigenes Recht: Gäste dürfen im Channel und privat schreiben, serverweit nur Moderatoren und Admins. Der Server speichert keinen Verlauf und schreibt den Inhalt privater Nachrichten in kein Log.
- Identität per Schlüsselpaar: keine Accounts, keine Passwörter
- Sprache als Opus über verschlüsseltes UDP (AES-GCM), Steuerung über TLS

Der Server läuft im Docker-Container auf Linux (amd64 und arm64). Der Client läuft unter Windows.

Für Nutzer gibt es eine eigene Seite mit Download und Anleitung: https://rezondes.github.io/OpenVoiceSpeak/ (Quelle in `website/`, ausgeliefert nach jedem Release).

## Server mit Docker

Voraussetzungen: Linux-Server (amd64 oder arm64) mit Docker samt Compose-Plugin
(z. B. `curl -fsSL https://get.docker.com | sh`). Das fertige Image liegt als Package in der GitHub Container Registry
(`ghcr.io/rezondes/openvoicespeak-server`, Tags `latest` und die Version, siehe "Client (Windows)"). Du brauchst nur die Compose-Datei:

```bash
mkdir openvoicespeak && cd openvoicespeak
curl -fsSLO https://raw.githubusercontent.com/Rezondes/OpenVoiceSpeak/main/docker-compose.yml
docker compose up -d
docker compose logs
```

Der Ordnername ist der Compose-Projektname und damit Teil des Volume-Namens. Behalte ihn, sonst beginnt der Server mit leeren Daten.

Im Log stehen zwei wichtige Zeilen:

- `Admin-Token: ...`: Damit wird der erste Nutzer zum Admin (im Client: "Admin-Token einlösen"). Solange es keinen Admin gibt, erzeugt jeder Start (auch ein automatischer Neustart) ein neues Token. Das Token steht nur in der Konsolenausgabe, nicht in den Logdateien.
- `Zertifikat-Fingerprint: ...`: Der Client zeigt diesen Fingerprint bei der ersten Verbindung an. Vergleiche beide, bevor du dem Server vertraust.

### Ports und Firewall

Der Server braucht **einen Port für TCP und UDP** (Standard 7000):

```bash
ufw allow 7000/tcp
ufw allow 7000/udp
```

Wenn UDP hinter Docker-NAT Probleme macht, kannst du in `docker-compose.yml` `network_mode: host` einkommentieren und die `ports:` entfernen.

### Umgebungsvariablen

Du setzt sie in `docker-compose.yml` unter `environment:`, dort stehen alle als Kommentar. Übernommen werden Änderungen mit `docker compose up -d`. Ein ungültiger Wert beendet den Server mit einer Fehlermeldung in `docker compose logs`.

| Variable | Standard | Bedeutung |
|---|---|---|
| `OVS_PORT` | 7000 | Port für TCP und UDP. Die Port-Mappings in der Compose-Datei mit anpassen. |
| `OVS_DATA_DIR` | `/data` im Container, sonst `./data` | Speicherort für Daten, Zertifikat und optional `server-config.json` |
| `OVS_MAX_USERS` | 50 | Maximale Zahl gleichzeitiger Nutzer |
| `OVS_SERVER_NAME` | `OpenVoiceSpeak Server` | Nur Startwert beim allerersten Start. Später im Client unter Verwaltung, Server ändern. |
| `OVS_PASSWORD` | leer | Nur Startwert beim allerersten Start, wie oben |
| `OVS_LOG_DAYS` | 30 | Wie viele Tage Logdateien aufbewahrt werden. `0` löscht nie. |
| `OVS_LOG_ROTATE_DAILY` | `true` | `true`: nach 00:00:00 beginnen neue Logdateien. `false`: die Dateien laufen bis zum nächsten Start weiter. |
| `OVS_AUTO_RESTART` | `false` | Täglicher automatischer Neustart an (`true`) oder aus (`false`). Auch `1`/`0` und `an`/`aus` gehen. |
| `OVS_AUTO_RESTART_TIME` | `04:00:00` | Uhrzeit des Neustarts im Format `hh:mm:ss` |
| `TZ` | `UTC` im Container | Zeitzone für Neustart-Uhrzeit, Tageswechsel der Logs und Zeitstempel, z. B. `Europe/Berlin` (in der Compose-Datei gesetzt) |

Umgebungsvariablen haben Vorrang vor `server-config.json`
(`{"port":7000,"maxUsers":50,"logDays":30,"logRotateDaily":true,"autoRestart":false,"autoRestartTime":"04:00:00"}`).

### Automatischer Neustart

Mit `OVS_AUTO_RESTART=true` startet der Server jeden Tag zur eingestellten Uhrzeit neu, ohne dass der Prozess oder Container endet.
Die Uhrzeit gilt in der Zeitzone aus `TZ`. Die Compose-Datei setzt `Europe/Berlin`, ohne `TZ` gilt UTC.
Beim Neustart liest der Server Konfiguration, Daten und Zertifikat neu ein und beginnt neue Logdateien.

Verbundene Clients bekommen die Meldung "Der Server startet neu" und verbinden sich nach wenigen Sekunden selbst wieder über "Verbinden ...". Ein automatisches Wiederverbinden gibt es nicht.

### Logs

Der Server beginnt bei jedem Start neue Logdateien im Datenverzeichnis, benannt nach Datum und Uhrzeit des Starts (z. B. `2026-09-27_17-29-22.log`). Server-Log und Channel-Logs eines Laufs tragen denselben Namen. Läuft der Server über Mitternacht, beginnt eine neue Datei (abschaltbar mit `OVS_LOG_ROTATE_DAILY=false`).

- `logs/server/<Start>.log`: Start, Stopp, Verbindungen, Ablehnungen, Kicks, Bans, Gruppen- und Einstellungsänderungen. Dieselben Zeilen stehen auch in `docker compose logs`.
- `logs/channels/<Channel-ID>/<Start>.log`: Betreten, Verlassen, Verschieben, Links und Änderungen eines Channels. Der Ordner ist nach der ID benannt, damit ein umbenannter Channel seine Historie behält. Jede Zeile nennt den aktuellen Channelnamen.

```bash
# die letzten 50 Zeilen des neuesten Server-Logs (die Namen sortieren chronologisch)
docker run --rm -v openvoicespeak_ovs-data:/data alpine sh -c 'tail -n 50 "$(ls /data/logs/server/*.log | tail -n 1)"'
```

### Daten, Backup, Update

Alles Dauerhafte liegt im Volume `ovs-data`. Docker Compose stellt den Projektnamen voran, also den
Ordnernamen (`openvoicespeak_ovs-data` bei einem Ordner namens `openvoicespeak`, siehe `docker volume ls`):

- `server-data.json`: Channels, Links, Gruppen, Nutzer, Bans
- `cert.pfx`: Serverzertifikat. Geht es verloren, bekommen alle Clients eine Warnung.
- `server-icon.png`: Server-Logo, falls eines hochgeladen wurde
- `logs/`: Server- und Channel-Logs (siehe oben)

```bash
# Backup
docker run --rm -v openvoicespeak_ovs-data:/data -v "$PWD":/backup alpine tar czf /backup/ovs-data.tgz -C /data .
# Restore
docker run --rm -v openvoicespeak_ovs-data:/data -v "$PWD":/backup alpine tar xzf /backup/ovs-data.tgz -C /data
# Update: neues Image holen und neu starten, Daten und Zertifikat bleiben im Volume
docker compose pull && docker compose up -d
```

Der Container läuft als Nutzer `app` (UID 1654). Nutzt du statt des benannten Volumes einen Bind-Mount, muss dieser Nutzer darauf schreiben dürfen:

```bash
sudo chown -R 1654:1654 ./ovs-data
```

### Image selbst bauen

Aus einem Checkout des Repositorys baut die Zusatzdatei `docker-compose.build.yml` das Image lokal, statt es zu laden:

```bash
docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build
```

Multi-Arch-Image:

```bash
docker buildx build --platform linux/amd64,linux/arm64 -t openvoicespeak/server:dev .
```

## Client (Windows)

Die fertige `OVS.Client.exe` liegt unter [Releases](https://github.com/Rezondes/OpenVoiceSpeak/releases). Sie läuft ohne installiertes .NET. Jeder Push auf `main` baut sie nach grünen Tests neu (`.github/workflows/release.yml`). Die Version ist eine Build-Kennung aus Datum und Uhrzeit in UTC, z. B. `280926.0a1k`, lokale Builds heissen `dev.<...>`. Die Einstellungen zeigen sie unter "Über", der Server schreibt sie beim Start ins Log (`OVS.Server --version` gibt sie aus). Die exe ist nicht signiert, Windows SmartScreen fragt deshalb beim ersten Start nach.

**Updates:** Veröffentlichte Versionen fragen beim Start bei GitHub nach einer neueren Version (abschaltbar unter Einstellungen, Über, dort auch "Nach Updates suchen"). Ist eine da, fragt der Client, ob er sie installieren soll. Er lädt dann die neue exe, prüft sie gegen die mitveröffentlichte SHA-256-Datei, ersetzt sich selbst und startet neu. Dabei geht nur eine lesende Anfrage an `api.github.com` und der Download von GitHub raus, keine Nutzerdaten. Lokale Builds (`dev.<...>`) suchen nie nach Updates.

Selbst bauen:

```bash
dotnet publish src/OVS.Client -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish/client
```

Heraus kommt eine einzelne `publish/client/OVS.Client.exe`.

**Bedienung:**
- Ohne Verbindung stehen deine Lesezeichen links in der Seitenleiste. Ein Klick verbindet sofort, fehlt ein Passwort oder stimmt das gespeicherte nicht, fragt der Client danach. Per Rechtsklick: Verbinden, Bearbeiten, Löschen.
- "Verbinden ..." auf dem Startbildschirm fragt Adresse, Port, Nickname und optional das Serverpasswort ab. Mit "Als Lesezeichen speichern" steht der Server beim nächsten Mal in der Seitenleiste bereit. Mit "Passwort speichern" merkt sich das Lesezeichen auch das Serverpasswort, verschlüsselt für deinen Windows-Benutzer und erst nach einer erfolgreichen Verbindung.
- Links stehen Server, Channels und Nutzer, unten dein eigener Name mit Mikrofon, Ton aus und Einstellungen.
- Tasten legst du unter Einstellungen, Tasten mit "Tastenaktion hinzufügen" fest: Push-to-Talk, Link-PTT, Push-to-Mute, Mikrofon an/aus oder Ton an/aus, auch als Kombination mit Strg, Umschalt oder Alt. Eine Aktion darf auf mehreren Tasten liegen (z. B. Push-to-Mute auf Maustaste 4 und 5), eine Taste aber nur eine Aktion haben. Ein neues Profil hat keine Belegung, bis dahin steht unter deinem Namen "Keine PTT-Taste belegt". Profile aus älteren Versionen behalten Maustaste 4 und 5. Die Tasten wirken auch, während ein Spiel im Vordergrund ist.
- In den Einstellungen wählst du ausserdem Push-to-Talk oder Sprachaktivierung, die Geräte und das Design (wie Windows, hell oder dunkel). Bei Sprachaktivierung wirkt die PTT-Taste nicht, Link-PTT schon. Regler und Modus wirken sofort, "Verwerfen" stellt die gespeicherten Werte wieder her.
- "Selbsttest starten" unter Übertragung schaltet dich stumm und taub (auch für den Server) und spielt deine Stimme so zurück, wie andere sie hören: durch den Codec, mit Verstärkung und Lautstärke, bei Sprachaktivierung nur über der Schwelle, bei Push-to-Talk nur mit Taste. Mit Kopfhörern testen. Beim Beenden, Schliessen der Einstellungen oder Trennen kommt dein vorheriger Zustand zurück.
- Die Stimmen der anderen werden beim Empfang verdoppelt (+6 dB), der Regler "Lautstärke" in den Einstellungen (0 bis 200 %, Standard 100 %) regelt das Ganze. Ein weicher Begrenzer verhindert Übersteuern, auch wenn mehrere gleichzeitig sprechen. Mikrofone, die sich als Stereo melden, aber nur auf einem Kanal Signal liefern, werden mit vollem Pegel übernommen.
- Die Lautstärke einzelner Personen stellst du per Rechtsklick auf ihren Namen ein (0 bis 200 %, 0 % macht sie nur für dich stumm). Die Einstellung hängt an der Person, gilt also auf jedem Server und nach einem Neustart. Ein Lautsprecher-Icon im Channel-Baum zeigt, bei wem du sie geändert hast.
- Kurze Töne begleiten Mikrofon an/aus, Ton an/aus, Verbinden, Trennen, Channelwechsel, wenn jemand deinen Channel betritt oder verlässt, Server-Mute, Verschieben und Privatnachrichten. Lautstärke und "Alle Sounds aus" findest du in den Einstellungen unter Sounds. Dort lässt sich auch jeder Sound einzeln leiser stellen, stumm schalten oder durch eine eigene WAV- oder MP3-Datei bis 5 Sekunden ersetzen.
- Doppelklick auf einen Channel betritt ihn.
- Mit dem Recht "Channels bearbeiten" ziehst du Channels mit der Maus an eine neue Position, oder du nimmst "Nach oben" bzw. "Nach unten" im Kontextmenü.
- Ein Channel lässt sich beim Anlegen oder unter "Bearbeiten ..." stumm schalten: Dort wird niemand gehört, auch nicht per Link-PTT. Sprache aus verlinkten Channels ist dort hörbar.
- Ebenso begrenzt "Maximale Nutzer" einen Channel (0 = unbegrenzt, der Standard-Channel bleibt immer unbegrenzt). Die Seitenleiste zeigt dann z. B. "3/5". Wer das Recht "Volle Channel betreten" hat, kommt trotzdem hinein und darf andere hineinverschieben.
- Per Rechtsklick auf Channels und Nutzer erreichst du Bearbeiten, Verlinken, Verschieben, Kicken und Bannen. Du siehst nur, wozu du berechtigt bist.
- Ein grüner Ring um das Profilbild bedeutet: jemand spricht. Ein violetter Ring mit Link-Symbol bedeutet: jemand spricht über einen Link.
- Unter "Verwaltung ...", Links siehst du alle Channel-Links als Matrix. Wähle mehrere Channels aus und verlinke sie mit einem Klick jeder mit jedem. Änderungen gelten erst nach "Übernehmen".
- In der Verwaltung ordnest du Gruppen per Maus oder mit den Pfeilen unter der Liste. Die Reihenfolge gilt überall, wo Gruppen erscheinen, und ändert keine Rechte.
- Rechts oben stehen Ping, "Verwaltung ..." (mit den nötigen Rechten: Gruppen, Nutzer, Bans, Servereinstellungen und Server-Logo) und "Trennen". Darunter liegt der Chat mit den Tabs "Allgemein" (serverweite Nachrichten, Willkommensnachricht, Warnungen und Fehler) und dem aktuellen Channel. Enter sendet, Umschalt+Enter macht eine neue Zeile. Der Channel-Tab beginnt bei jedem Channelwechsel leer, Tabs im Hintergrund zeigen die Zahl ungelesener Nachrichten. Per Rechtsklick auf einen Nutzer, "Privatnachricht", öffnest du einen privaten Tab "@Nickname". Eingehende private Nachrichten öffnen ihn im Hintergrund. Private Tabs lassen sich schliessen und behalten ihren Verlauf, solange du verbunden bist. Ist der Partner offline, ist die Eingabe gesperrt.

Ein Server kann ein eigenes Logo haben: Unter "Verwaltung ...", Server lädst du ein PNG oder JPG hoch (quadratisch, höchstens 3 MB, per Dateiauswahl oder durch Ziehen auf das Vorschaufeld). Der Client verkleinert es auf 256 x 256 Pixel. Alle verbundenen Clients sehen es sofort in der Seitenleiste, die Lesezeichen-Kacheln zeigen das zuletzt gesehene Logo.

Deine Identität, Einstellungen und vertrauten Server liegen in `%APPDATA%\OpenVoiceSpeak`. Sichere `identity.key`: Diese Datei ist dein Account auf allen Servern.

Der Client schreibt alles, was er tut, in eine neue Datei pro Start (und nach Mitternacht): `%APPDATA%\OpenVoiceSpeak\logs\client-<Datum>_<Uhrzeit>.log` (bei `--profile` im dortigen Ordner `logs`). Dazu gehören Verbindungen, Zertifikatsentscheidungen, Änderungen vom Server, eigene Anfragen, Senden und Einstellungen. Passwörter und das Admin-Token stehen nie darin. Dateien, die älter als 30 Tage sind, werden gelöscht.

### Kommandozeile

| Option | Wirkung |
|---|---|
| `--profile <ordner>` | Eigener Ordner für Identität und Einstellungen, z. B. für eine zweite Instanz |
| `--no-audio` | Ohne Mikrofon und Lautsprecher |
| `--audio-debug` | Schreibt zusätzlich PTT-Tastenwechsel und gesendete Frames pro Sekunde ins Client-Log |
| `--debug-api <port>` | Aktiviert die lokale Debug-API (siehe unten) |

### Debug-API

Mit der Debug-API lässt sich der Client ohne Maus und Tastatur steuern und prüfen.

- Sie lauscht nur auf `localhost`.
- Jede Anfrage braucht den Header `X-OVS-Debug: 1`. Dadurch können Webseiten im Browser sie nicht nutzen.
- Alle Endpunkte sind in `src/OVS.Client/Debug/DebugApi.cs` beschrieben.

```bash
OVS.Client.exe --profile ./anna --debug-api 7011 --no-audio
curl -H "X-OVS-Debug: 1" -H "Content-Type: application/json" -X POST localhost:7011/connect -d '{"host":"127.0.0.1","port":7000,"nickname":"anna"}'
curl -H "X-OVS-Debug: 1" -H "Content-Type: application/json" -X POST localhost:7011/tone -d '{"hz":440}'
curl -H "X-OVS-Debug: 1" -H "Content-Type: application/json" -X POST localhost:7011/linkptt -d '{"down":true}'
curl -H "X-OVS-Debug: 1" localhost:7011/state
```

Die wichtigsten Endpunkte:

- `/tone` ersetzt das Mikrofon durch einen Testton.
- `/ptt` und `/linkptt` halten die Tasten softwareseitig gedrückt, `/key` löst jede Tastenaktion aus (z. B. `{"action":"ToggleMute"}` oder `{"action":"PushToMute","down":true}`).
- `/chat` schreibt eine Nachricht (`{"target":"channel","text":"Hallo"}`, privat mit `"target":"private","to":"bert"`), `/state` zeigt unter `server.chat` die letzten empfangenen.
- `/server-icon` lädt ein Logo hoch (`{"path":"C:\bild.png"}`), ohne `path` wird es entfernt.
- `/state` liefert den kompletten Oberflächenzustand, darunter wer spricht (auch über Link) und empfangene Frames pro Sprecher.

## Entwicklung

```bash
dotnet build
dotnet test
```

Die Tests laufen mit In-Process-Servern auf zufälligen lokalen Ports. Sie umfassen Protokoll, Rechte, Voice-Kryptografie, Routing und Relay über echtes UDP, die Audio-Pipeline, die ViewModels, Server-, Channel- und Client-Logs sowie den automatischen Neustart. Dazu kommen Ende-zu-Ende-Tests mit zwei echten Clients, gesteuert über die Debug-API.

Server ohne Docker starten, z. B. zum Testen unter Windows (Daten landen in `./data`, Einstellungen wie oben als Umgebungsvariablen):

```bash
dotnet run --project src/OVS.Server
```

Projektaufbau:

- `src/OVS.Shared`: Protokoll, Identität, Voice-Paketformat
- `src/OVS.Server`: Server
- `src/OVS.Client`: Avalonia-Client
- `tests/OVS.Tests`: Tests

Der Umsetzungsplan steht in `PLAN.md`.
