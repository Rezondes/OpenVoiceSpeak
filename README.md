# OpenVoiceSpeak

Selbst hostbarer Voice-Chat in der Art von Mumble oder TeamSpeak, bewusst einfach gehalten:

- ein Server mit mehreren Channels
- ein **serverweites** Rechtesystem aus Gruppen und Rechten, ohne Rechte pro Channel
- **Channel-Linking**: Normale Sprache bleibt im eigenen Channel. Mit der Link-PTT-Taste sprichst du zusätzlich in alle direkt verlinkten Channels.
- Identität per Schlüsselpaar: keine Accounts, keine Passwörter
- Sprache als Opus über verschlüsseltes UDP (AES-GCM), Steuerung über TLS

Der Server läuft im Docker-Container auf Linux (amd64 und arm64). Der Client läuft unter Windows.

## Server mit Docker

Voraussetzungen: Linux-Server (amd64 oder arm64) mit Git und Docker samt Compose-Plugin
(z. B. `curl -fsSL https://get.docker.com | sh`).

```bash
git clone <URL dieses Repositorys> openvoicespeak
cd openvoicespeak
docker compose up -d --build
docker compose logs
```

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
- `logs/`: Server- und Channel-Logs (siehe oben)

```bash
# Backup
docker run --rm -v openvoicespeak_ovs-data:/data -v "$PWD":/backup alpine tar czf /backup/ovs-data.tgz -C /data .
# Restore
docker run --rm -v openvoicespeak_ovs-data:/data -v "$PWD":/backup alpine tar xzf /backup/ovs-data.tgz -C /data
# Update (das Image wird lokal gebaut, es gibt keine Registry)
git pull && docker compose up -d --build
```

Der Container läuft als Nutzer `app` (UID 1654). Nutzt du statt des benannten Volumes einen Bind-Mount, muss dieser Nutzer darauf schreiben dürfen:

```bash
sudo chown -R 1654:1654 ./ovs-data
```

### Multi-Arch-Image bauen

```bash
docker buildx build --platform linux/amd64,linux/arm64 -t openvoicespeak/server:dev .
```

## Client (Windows)

```bash
dotnet publish src/OVS.Client -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/client
```

Heraus kommt eine einzelne `publish/client/OVS.Client.exe`, die ohne installiertes .NET läuft.

**Bedienung:**
- Der Startbildschirm zeigt "Verbinden ..." und deine Lesezeichen. Der Dialog fragt Adresse, Port, Nickname und optional das Serverpasswort ab. Mit "Als Lesezeichen speichern" steht der Server beim nächsten Mal als Kachel bereit.
- Links stehen Server, Channels und Nutzer, unten dein eigener Name mit Mikrofon, Ton aus und Einstellungen.
- Push-to-Talk liegt auf Maustaste 4, Link-PTT auf Maustaste 5. Beides lässt sich unter Einstellungen ändern, dort auch Sprachaktivierung, Geräte und das Design (wie Windows, hell oder dunkel).
- Doppelklick auf einen Channel betritt ihn.
- Per Rechtsklick auf Channels und Nutzer erreichst du Bearbeiten, Verlinken, Verschieben, Kicken und Bannen. Du siehst nur, wozu du berechtigt bist.
- Ein grüner Ring um das Profilbild bedeutet: jemand spricht. Ein violetter Ring mit Link-Symbol bedeutet: jemand spricht über einen Link.
- Rechts oben stehen Ping, "Verwaltung ..." (mit den nötigen Rechten: Gruppen, Nutzer, Bans, Servereinstellungen) und "Trennen". Darunter sammelt "Aktivität" Willkommensnachricht, Warnungen und Fehler.

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
- `/ptt` und `/linkptt` halten die Tasten softwareseitig gedrückt.
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
