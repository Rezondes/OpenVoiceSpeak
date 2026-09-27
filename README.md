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

- `Admin-Token: ...`: Damit wird der erste Nutzer zum Admin (im Client: "Admin-Token einlösen"). Solange es keinen Admin gibt, erzeugt jeder Start ein neues Token. Das Token steht nur in der Konsolenausgabe, nicht in den Logdateien.
- `Zertifikat-Fingerprint: ...`: Der Client zeigt diesen Fingerprint bei der ersten Verbindung an. Vergleiche beide, bevor du dem Server vertraust.

### Ports und Firewall

Der Server braucht **einen Port für TCP und UDP** (Standard 7000):

```bash
ufw allow 7000/tcp
ufw allow 7000/udp
```

Wenn UDP hinter Docker-NAT Probleme macht, kannst du in `docker-compose.yml` `network_mode: host` einkommentieren und die `ports:` entfernen.

### Umgebungsvariablen

| Variable | Standard | Bedeutung |
|---|---|---|
| `OVS_PORT` | 7000 | Port für TCP und UDP. Die Port-Mappings in der Compose-Datei mit anpassen. |
| `OVS_DATA_DIR` | `/data` (im Container) | Speicherort für Daten, Zertifikat und optional `server-config.json` |
| `OVS_MAX_USERS` | 50 | Maximale Zahl gleichzeitiger Nutzer |
| `OVS_SERVER_NAME` | `OpenVoiceSpeak Server` | Nur Startwert beim allerersten Start. Später im Client unter Verwaltung, Server ändern. |
| `OVS_PASSWORD` | leer | Nur Startwert beim allerersten Start, wie oben |
| `OVS_LOG_DAYS` | 30 | Wie viele Tage Logdateien aufbewahrt werden. `0` löscht nie. |

Umgebungsvariablen haben Vorrang vor `server-config.json` (`{"port":7000,"maxUsers":50,"logDays":30}`).

### Logs

Der Server schreibt tägliche Logdateien in das Datenverzeichnis:

- `logs/server/<Datum>.log`: Start, Stopp, Verbindungen, Ablehnungen, Kicks, Bans, Gruppen- und Einstellungsänderungen. Dieselben Zeilen stehen auch in `docker compose logs`.
- `logs/channels/<Channel-ID>/<Datum>.log`: Betreten, Verlassen, Verschieben, Links und Änderungen eines Channels. Der Ordner ist nach der ID benannt, damit ein umbenannter Channel seine Historie behält. Jede Zeile nennt den aktuellen Channelnamen.

```bash
docker run --rm -v openvoicespeak_ovs-data:/data alpine sh -c 'tail -n 50 /data/logs/server/*.log'
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
- Push-to-Talk liegt auf Maustaste 4, Link-PTT auf Maustaste 5. Beides lässt sich unter Einstellungen ändern, dort auch Sprachaktivierung und Geräte.
- Doppelklick auf einen Channel betritt ihn.
- Per Rechtsklick auf Channels und Nutzer erreichst du Bearbeiten, Verlinken, Verschieben, Kicken und Bannen. Du siehst nur, wozu du berechtigt bist.
- Grün bedeutet: jemand spricht. Blau bedeutet: jemand spricht über einen Link.

Deine Identität, Einstellungen und vertrauten Server liegen in `%APPDATA%\OpenVoiceSpeak`. Sichere `identity.key`: Diese Datei ist dein Account auf allen Servern.

Der Client schreibt alles, was er tut, in ein Log pro Tag: `%APPDATA%\OpenVoiceSpeak\logs\client-<Datum>.log` (bei `--profile` im dortigen Ordner `logs`). Dazu gehören Verbindungen, Zertifikatsentscheidungen, Änderungen vom Server, eigene Anfragen, Senden und Einstellungen. Passwörter und das Admin-Token stehen nie darin. Dateien, die älter als 30 Tage sind, werden gelöscht.

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

Die Tests laufen mit In-Process-Servern auf zufälligen lokalen Ports. Sie umfassen Protokoll, Rechte, Voice-Kryptografie, Routing und Relay über echtes UDP, die Audio-Pipeline und die ViewModels. Dazu kommen Ende-zu-Ende-Tests mit zwei echten Clients, gesteuert über die Debug-API.

Projektaufbau:

- `src/OVS.Shared`: Protokoll, Identität, Voice-Paketformat
- `src/OVS.Server`: Server
- `src/OVS.Client`: Avalonia-Client
- `tests/OVS.Tests`: Tests

Der Umsetzungsplan steht in `PLAN.md`.
