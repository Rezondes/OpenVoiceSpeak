<img src="docs/logo.png" alt="OpenVoiceSpeak-Logo" width="96">

# OpenVoiceSpeak Verwaltungstool (Alpha)

> **Alpha:** Das Verwaltungstool ist eine frühe Testversion. Funktionen, Oberfläche und gespeicherte Daten können sich mit jeder Version noch ändern. Sichere die Server und das Verwaltungstool vor jedem Update und setze es noch nicht ein, wo ein Ausfall weh tut.

Das Verwaltungstool ist eine Web-App, mit der du auf einem Docker-Host bis zu 100 OpenVoiceSpeak-Server anlegst, betreibst, aktualisierst und beobachtest. Server und Client beschreibt `README.md`; hier steht alles über das Verwaltungstool selbst. Die Packages dahinter (M1 bis M34) stehen in `PLAN-MANAGER.md`.

## Was das Verwaltungstool ist

- Eine Web-App (ASP.NET Core, Blazor) in einem eigenen Container, erreichbar auf Port 6999. Die Seiten gibt es auf Deutsch und Englisch, hell und dunkel, pro Nutzer gewählt.
- Es legt jeden Server als eigenen Docker-Container `ovs-<id>` mit eigenem Volume `ovs-<id>-data` an, auf einem Port zwischen 7000 und 7099 (TCP und UDP).
- Es ist auf jedem seiner Server der einzige Server-Admin und verwaltet ihn über eine unsichtbare Dienst-Sitzung ("Serververwaltung"): Einstellungen, Nutzer, Gruppen, Banns, Backups, Logs.
- Jeder Server schickt seine Telemetrie (Traces, Metriken, Logs) an das Verwaltungstool, das sie speichert und als Live-Ansicht, Verlauf und Fehlerliste zeigt. Prometheus, Grafana und Co. brauchst du nicht.
- Daten liegen in `/data` des Containers: `manager.db` (Konten, Server, Einstellungen, Audit-Log), `telemetry.db` (Telemetrie), `keys/` (Schlüssel der Server-Identitäten) und `backups/` (geplante Backups des Verwaltungstools).

## Einrichtung mit Docker Compose

Voraussetzungen: ein Linux-Host (amd64 oder arm64) mit Docker samt Compose-Plugin und der Checkout dieses Repositorys.

```bash
git clone https://github.com/Rezondes/OpenVoiceSpeak.git
cd OpenVoiceSpeak
docker compose -f deploy/manager/docker-compose.yml up -d --build
```

Danach öffnest du `http://<host>:6999` und legst das erste Konto an (siehe "Erster Admin").

Die Compose-Datei `deploy/manager/docker-compose.yml` startet zwei Container:

- **ovs-manager**: das Verwaltungstool, veröffentlicht nur Port 6999. Den Port 4318 (Telemetrie der Server) veröffentlicht sie nie.
- **socket-proxy** (`wollomatic/socket-proxy`): der einzige Weg des Verwaltungstools zu Docker. Der Docker-Socket ist nur lesend eingebunden, der Proxy veröffentlicht keinen Port, liegt nur im internen Netz `docker-proxy` und lässt pro HTTP-Methode genau die Aufrufe durch, die das Verwaltungstool braucht (Container auflisten, anlegen, starten, stoppen, löschen, Images auflisten und holen, Volumes anlegen und löschen, Statistiken). Ein direkt eingebundener Socket wäre root-Zugriff auf den Host; so bleibt ein Fehler im Verwaltungstool auf diese Aufrufe beschränkt. Zusätzlich fasst das Verwaltungstool nur Container mit seinem Label `ovs.managed=true` an.

Der Proxy läuft als Nutzer `nobody` mit der Gruppe des Docker-Sockets, die die Compose-Variable `DOCKER_SOCKET_GID` angibt (Standard 999). Auf einem Linux-Host liefert `stat -c %g /var/run/docker.sock` die richtige Zahl, bei Docker Desktop (Windows, macOS) ist es 0. Setze sie in einer Datei `.env` neben der Compose-Datei (`DOCKER_SOCKET_GID=0`) oder in der Shell. Passt sie nicht, meldet der Proxy "permission denied" und startet immer wieder neu.

Das Netz `ovs-managed` hat einen festen Namen: Das Verwaltungstool und alle seine Server liegen darin. So erreicht es jeden Server als `ovs-<id>`, und die Server schicken ihre Telemetrie an `http://ovs-manager:4318`.

Ab dem ersten Release mit dem Verwaltungstool liegt das fertige Image auch als `ghcr.io/rezondes/openvoicespeak-manager` (Tags `latest` und die Version, gleiche Version wie der Server) in der GitHub Container Registry. Bis dahin baut die Compose-Datei das Image aus dem Checkout (`Dockerfile.manager`).

## Umgebungsvariablen

Alle Variablen sind optional. Setze sie im Abschnitt `environment` von `ovs-manager` in der Compose-Datei.

| Variable | Standard | Bedeutung |
|---|---|---|
| `TZ` | `UTC` | Zeitzone des Verwaltungstools; neue Server übernehmen sie (beim Anlegen änderbar). Die Compose-Datei setzt `Europe/Berlin`. |
| `OVS_MANAGER_DATA_DIR` | `/data` | Ordner von Datenbank, Telemetrie, Schlüsseln und Backups. |
| `OVS_MANAGER_ADMIN_USER` | leer | Name des ersten Admins, der beim Start angelegt wird, solange es kein Konto gibt. Nur zusammen mit dem Passwort. |
| `OVS_MANAGER_ADMIN_PASSWORD` | leer | Passwort dieses Admins (mindestens 12 Zeichen). Gibt es schon ein Konto, werden beide ignoriert (Hinweis im Log). |
| `OVS_MANAGER_DOCKER_HOST` | `tcp://socket-proxy:2375` | Adresse der Docker-API, im Compose-Setup der Socket-Proxy. |
| `OVS_MANAGER_SERVER_IMAGE` | `ghcr.io/rezondes/openvoicespeak-server` | Image der Server ohne Tag; ein selbst gebautes Image funktioniert genauso (z. B. `OVS_MANAGER_SERVER_IMAGE=openvoicespeak/server` in der `.env` neben der Compose-Datei). |
| `OVS_MANAGER_SERVER_HOST` | leer | Ein Host für alle Server statt `ovs-<id>`; nur für ein Verwaltungstool ausserhalb von Docker (Entwicklung). |
| `OVS_MANAGER_PUBLIC_HOST` | leer | Host, den die Clients benutzen, für die Einladungslinks; ohne ihn der Host der aufgerufenen Seite. |
| `OVS_MANAGER_BASE_URL` | leer | Adresse, unter der Nutzer das Verwaltungstool erreichen, z. B. `https://manager.example.org` (ohne Pfad). Links in Mails, Weiterleitungen und das Secure-Flag der Cookies folgen ihr. |
| `OVS_MANAGER_TRUSTED_PROXIES` | leer | Adressen oder Netze (CIDR) der Reverse Proxys, deren `X-Forwarded-*`-Header zählen, durch Komma getrennt. Ohne Eintrag zählen diese Header nie. |
| `OVS_MANAGER_AUDIT_DAYS` | leer (für immer) | Tage, die Audit-Zeilen aufbewahrt werden. |
| `OVS_MANAGER_UPDATE_HEALTH_SECONDS` | `60` | So lange hat ein aktualisierter, übernommener oder geklonter Server Zeit, zu antworten. |
| `OVS_MANAGER_RETENTION_MINUTE_DAYS` | `7` | Tage mit Minutenwerten der Metriken. |
| `OVS_MANAGER_RETENTION_HOUR_DAYS` | `400` | Tage mit Stundenwerten der Metriken. |
| `OVS_MANAGER_RETENTION_LOG_DAYS` | `30` | Tage mit Logzeilen der Server. |
| `OVS_MANAGER_RETENTION_TRACE_DAYS` | `7` | Tage mit Traces der Server. |
| `OVS_MANAGER_TELEMETRY_MAX_MB` | `5000` | Obergrenze der Telemetrie-Datenbank; darüber werden die ältesten Rohdaten zuerst gelöscht. |
| `OVS_MANAGER_BACKUP_PASSWORD` | leer | Passwort der geplanten Backups des Verwaltungstools (mindestens 12 Zeichen). Ohne es bleibt der Zeitplan aus. |
| `SMTP_HOST` | leer | SMTP-Server für Mails. Mit `SMTP_FROM` zusammen ist der Mailversand eingerichtet. |
| `SMTP_PORT` | 25, 587 oder 465 | Port; der Standard folgt `SMTP_TLS` (`none` 25, `starttls` 587, `tls` 465). |
| `SMTP_USER` | leer | Anmeldename am SMTP-Server, leer für keine Anmeldung. |
| `SMTP_PASSWORD` | leer | Passwort dazu. |
| `SMTP_FROM` | leer | Absenderadresse. |
| `SMTP_TLS` | `starttls` | `none`, `starttls` oder `tls`. |

Ein ungültiger Wert (etwa Buchstaben in einer Tageszahl, eine Basis-URL mit Pfad, ein zu kurzes Backup-Passwort) stoppt den Start mit einer Meldung, die die Variable nennt.

Für den mitgelieferten Caddy kommt `OVS_MANAGER_DOMAIN` dazu (siehe "HTTPS und Reverse Proxy"); die liest Compose, nicht das Verwaltungstool.

## Erster Admin

Solange es kein Konto gibt, führt jede Seite nach `/setup`. Dort legst du den Admin mit Name und Passwort (mindestens 12 Zeichen, zweimal eingegeben) an und bist sofort angemeldet; danach antwortet `/setup` mit 404. Alternativ legen `OVS_MANAGER_ADMIN_USER` und `OVS_MANAGER_ADMIN_PASSWORD` den Admin beim Start an.

Auf `/setup` kannst du stattdessen ein Backup eines anderen Verwaltungstools zurückspielen (siehe "Backup des Verwaltungstools").

Angemeldet wird mit Name und Passwort. Fünf Fehlversuche sperren ein Konto 15 Minuten, und die Anmeldung sagt nie, ob es einen Namen gibt.

## Rollen und Zuweisungen

- **Admin**: genau ein Konto, die höchste Instanz. Nur der Admin legt Server und Server-Verwalter an und löscht sie, weist Server zu, sieht alle Server und das ganze Audit-Log.
- **Server-Verwalter**: sieht und bedient nur die Server, die ihm zugewiesen sind, auch in Übersicht, Audit-Log und API. Ein fremder Server antwortet mit 403.

Unter "Konten" legt der Admin einen Verwalter mit Startpasswort an, das der Verwalter bei der ersten Anmeldung ändern muss. Dort sperrt, entsperrt und löscht er Konten, setzt Passwörter zurück und schaltet Zwei-Faktor ab. Die Zuweisung setzt er per Häkchen auf der Seite des Kontos oder auf der Seite des Servers.

Unter "Mein Konto" ändert jeder sein Passwort, seine E-Mail und seine Sprache, sieht seine Sitzungen und beendet die anderen.

Jede Änderung schreibt eine Zeile ins Audit-Log ("Protokoll"): wer, wann, was, welcher Server. Es ist nach Nutzer, Server, Aktion und Tagen filterbar und als CSV exportierbar.

## Server anlegen, starten, stoppen, löschen

**Anlegen** (Admin, "Neuer Server"): Name, Nutzerlimit (1 bis 1000), Passwort, Version (Standard: die des Verwaltungstools; ein selbst gebautes Verwaltungstool nimmt das neueste selbst gebaute Server-Image auf dem Host, z. B. `openvoicespeak/server:local`), Zeitzone, Port und CPU- und RAM-Limits. Der Port kommt aus dem Bereich 7000 bis 7099, also höchstens 100 Server; das Formular schlägt den kleinsten freien vor. Ist der Port auf dem Host schon belegt, wird nichts angelegt. Optional kopiert der neue Server einen bestehenden (siehe "Server übernehmen und klonen").

**Übersicht**: jeder sichtbare Server mit Status, Version, Port, Nutzern online, Fehlern der letzten 24 Stunden und ob ein Update bereitsteht. Der Status kommt alle 5 Sekunden von Docker und einem TLS-Handshake: läuft, startet, gestoppt, abgestürzt (mit Exit-Code), Neustart-Schleife (mehr als 3 Neustarts in 10 Minuten) oder nicht erreichbar.

**Starten, Stoppen, Neustarten** auf der Seite des Servers. Stoppen, Neustarten und Updates kannst du 1, 5 oder 15 Minuten vorher im Server-Chat ankündigen und bis dahin abbrechen. Solange eine Aktion läuft, sind die Knöpfe in allen Browsern aus.

**Löschen** (Admin): nach Eingabe des Servernamens stoppt der Server und liegt 7 Tage im Papierkorb. Dort stellst du ihn wieder her oder löschst ihn endgültig; nach 7 Tagen verschwinden Container, Volume und Identität von selbst. Ein übernommener Server mit Host-Ordner verliert dabei nie seinen Ordner.

## Das Verwaltungstool als Server-Admin

Jeder Server, den das Verwaltungstool anlegt, bekommt eine eigene Identität (ein eigenes Schlüsselpaar pro Server). Ihr Fingerprint steht in `OVS_SERVER_ADMIN_FINGERPRINT`, also ist das Verwaltungstool bei jedem Start der einzige Server-Admin, auch nach Restore, Klonen oder Übernahme. Die privaten Schlüssel liegen verschlüsselt in der Datenbank, die Schlüssel dazu in `/data/keys`.

Über diese Identität hält das Verwaltungstool zu jedem laufenden Server eine Dienst-Sitzung mit dem Namen "Serververwaltung", die in Clients nicht erscheint. Beim ersten Verbinden merkt es sich das Zertifikat; ändert es sich, stoppt die Verbindung mit "Zertifikat geändert", bis der Admin "Neues Zertifikat annehmen" klickt.

Der Besitzer eines Servers bekommt ein Verwalter-Konto mit diesem Server und handelt darüber als Server-Admin. Menschen erhalten auf dem Server höchstens die Gruppe Admin; die gibt ihnen der Verwalter unter "Nutzer".

Ein Server mit einem älteren Protokoll als das Verwaltungstool heisst "veraltet": Starten, Stoppen, Update, Konsolen-Log und Telemetrie gehen, Funktionen über das Protokoll nicht.

## Server verwalten

Auf der Seite eines Servers liegen:

- **Einstellungen**: Name, Willkommensnachricht, Passwort, Nutzerlimit, Log-Aufbewahrung, tägliche Logdatei, täglicher Neustart und Logo (PNG, quadratisch, 64 bis 512 Pixel, höchstens 512 KB).
- **Nutzer**: wer online ist (kicken, bannen, server-stumm schalten, verschieben) und die bekannten Nutzer mit ihren Gruppen (Gruppe geben oder nehmen, löschen).
- **Banns**: alle Banns, aufheben.
- **Gruppen**: anlegen, Rechte ändern, Reihenfolge, löschen. Admin und Server-Admin haben feste Rechte; Gast, Admin und Server-Admin lassen sich nicht löschen.
- **Einladungslink**: `ovs://host:port` und der Web-Link zum Kopieren, daneben der Zertifikat-Fingerprint zum Vergleich beim ersten Verbinden. Das Passwort steht nie im Link.
- **Backups**: anlegen, herunterladen, hochladen, wiederherstellen (alle werden getrennt) und löschen, dazu ein Zeitplan pro Server (aus, täglich oder wöchentlich, die neuesten N behalten). Vor jedem Update entsteht ein markiertes Backup.
- **Log**: das Server-Log lesen (1000 Zeilen pro Seite), durchsuchen und herunterladen. Ohne Dienst-Sitzung zeigt die Seite stattdessen die Konsolen-Ausgabe des Containers live.
- **Nachricht**: ein freier Text in den Server-Chat.

## Versionen und Updates

Das Verwaltungstool liest stündlich die Releases auf GitHub. Einem Server bietet es das neueste Release mit seinem eigenen Protokoll an ("Update auf <Version> verfügbar"); ein neueres Protokoll heisst: zuerst das Verwaltungstool aktualisieren.

Ein **Update** holt das Image, legt ein Backup "vor Update <Version>" an, stoppt und entfernt den Container, legt ihn mit den gleichen Variablen, Limits und Daten und dem neuen Tag neu an, startet ihn und prüft, ob er innerhalb von `OVS_MANAGER_UPDATE_HEALTH_SECONDS` läuft und antwortet. Scheitert die Prüfung, startet die alte Version wieder und das Backup wird zurückgespielt (**Rollback**). Jeder Schritt erscheint live im Dialog und im Audit-Log.

"Alle aktualisieren" (Admin) geht die Server nach Port durch und hält beim ersten Fehler an. Ein **Wartungsfenster** pro Server (ein Wochentag, eine Stunde, in der Zeitzone des Servers) aktualisiert automatisch, wenn dort "Automatisch aktualisieren" an ist; das ist bei jedem Server zunächst aus.

## Telemetrie

Jeder Server schickt per OTLP an `ovs-manager:4318` (nur aus den Docker-Netzen, nur für eigene Server): Traces, Metriken (Sitzungen, Bandbreite, Verbindungen, Sprachpaket-Verluste, Nutzer pro Tag) und Logs. Gespeichert wird in `/data/telemetry.db`:

- Rohwerte werden jede Minute zu Minuten- und Stundenwerten verdichtet und dann gelöscht.
- **Aufbewahrung**: Minutenwerte 7 Tage, Stundenwerte 400 Tage, Logs 30 Tage, Traces 7 Tage, Tagesstatistik für immer (Variablen oben). Erreicht die Datenbank `OVS_MANAGER_TELEMETRY_MAX_MB`, gehen die ältesten Rohdaten zuerst, und die Übersicht zeigt einen Hinweis.
- **Live-Ansicht** auf der Seite des Servers: Nutzer, Bandbreite, Verluste der letzten 15 Minuten, CPU und RAM, alle 5 Sekunden neu.
- **Verlauf** (`/servers/<id>/history`): Verbindungen, Nutzer, Spitzen und Bandbreite pro Tag oder Monat, jedes Diagramm auch als CSV. Oben auf der Übersicht steht ein Dashboard über alle sichtbaren Server.
- **Fehler** (`/servers/<id>/errors`): die Fehler eines Servers gruppiert, mit Stacktrace. Meldet ein Nutzer eine **Fehler-ID**, findet "Fehler suchen" (`/errors`) den Fehler mit den Spans seines Traces.

## Ressourcen und Limits

Unter "Ressourcen" eines Servers stehen CPU und RAM jetzt, die Spitzen der letzten 24 Stunden und die Grösse des Volumes. Docker braucht für die Grössen eine Weile; das Verwaltungstool liest sie deshalb beim Start und danach im Hintergrund, sie sind bis zu 10 Minuten alt. Der Admin setzt CPU-Kerne (0,1 bis 64) und RAM (64 MB bis 256 GB) beim Anlegen oder später; nach einer Rückfrage wird der Container mit den neuen Limits neu angelegt, Updates behalten sie. Die Übersicht zeigt den freien Platz des Datenträgers von `/data` und warnt unter 10 %.

## E-Mail, Passwort vergessen und Benachrichtigungen

Mit `SMTP_HOST` und `SMTP_FROM` verschickt das Verwaltungstool Mails. Unter "E-Mail" (Admin) stehen die Einstellungen und eine Testmail, die den Fehler des SMTP-Servers zeigt.

**Passwort vergessen** (Link auf der Anmeldung): Name oder E-Mail eingeben, die Antwort ist immer gleich. Ein Konto mit E-Mail bekommt einen Link, eine Stunde gültig und nur einmal nutzbar; das Zurücksetzen beendet alle Sitzungen. Ohne SMTP setzt nur der Admin Passwörter zurück.

**Benachrichtigungen** (jeder Nutzer, "Benachrichtigungen"): pro Ereignis und Kanal ein Häkchen, per E-Mail, Webhook (JSON) oder Discord-Webhook (auch Fluxer). Ereignisse: Server abgestürzt, Neustart-Schleife, 5 Minuten nicht erreichbar, voll, zu viele Sprachverluste (Schwelle pro Server, Standard 5 % über 5 Minuten), Update verfügbar, Update gescheitert oder gelungen; für den Admin zusätzlich wenig Platz auf dem Docker-Datenträger und eine neue Version des Verwaltungstools. Jedes Ereignis kommt einmal und einmal "Wieder ok". Ein Versand wird dreimal wiederholt, der letzte Fehler steht an der Regel. Webhooks gehen nur an öffentliche Adressen und folgen keiner Weiterleitung.

## Zwei-Faktor-Anmeldung

Unter "Mein Konto", "Zwei-Faktor-Anmeldung" scannst du einen QR-Code mit einer Authenticator-App (TOTP) und bestätigst mit einem Code; danach siehst du einmal 10 Wiederherstellungscodes, die je einmal gelten. Bei der Anmeldung folgt auf das Passwort der Code oder ein Wiederherstellungscode.

Der Admin schaltet unter "Konten" die "2FA-Pflicht" ein: Wer noch keine Zwei-Faktor-Anmeldung hat, muss sie gleich nach der Anmeldung einrichten. Verliert jemand sein Gerät, schaltet der Admin sie auf der Seite des Kontos ab.

## Server übernehmen und klonen

**Übernehmen** (Admin, "Server übernehmen"): Die Seite listet Container eines OpenVoiceSpeak-Server-Images, die das Verwaltungstool nicht angelegt hat. Nach Wahl eines Ports und einer Rückfrage stoppt der alte Container, ein neuer startet mit den Daten aus dem alten Volume oder Host-Ordner, so wie sie sind (keine Kopie), mit genau demselben Image und derselben Version (auch einem selbst gebauten) und mit Name, Limit, Zeitzone und Passwort aus den alten Variablen. Hat das Image keine erkennbare Version (ein Server von vor dem Verwaltungstool), lehnt die Übernahme ab: Aktualisiere ihn dann einmal von Hand und übernimm ihn danach. Erst nach erfolgreicher Prüfung wird der alte Container entfernt; sonst läuft er wieder. Der bisherige Server-Admin verliert diese Rolle beim ersten Start und behält seine anderen Gruppen.

**Aus Backup anlegen** ("Neuer Server", "Aus Backup anlegen"): Lade die `.ovsbackup`-Datei eines OpenVoiceSpeak-Servers hoch, auch eines von vor dem Verwaltungstool. Die Seite zeigt Version, Datum und Zertifikat-Fingerprint des Backups; erkennt das Verwaltungstool die Version nicht, bricht sie ab, bevor etwas angelegt wird. Du wählst Name und Port, dann entsteht ein neuer Server (ein selbst gebautes Verwaltungstool nimmt das neueste selbst gebaute Server-Image), und das Backup wird darauf eingespielt. Er behält Zertifikat und Fingerprint aus dem Backup, Clients erkennen ihn also wieder; läuft schon ein verwalteter Server mit diesem Zertifikat, warnt die Seite. Wie bei der Übernahme wird das Verwaltungstool Server-Admin, der bisherige Server-Admin wird normaler Nutzer mit seinen anderen Gruppen.

**Klonen** ("Neuer Server", "Aus Server kopieren"): Der neue Server bekommt Kanäle, Links, Gruppen, Einstellungen und Logo eines bestehenden, Nutzer und Banns nur mit Häkchen. Er behält seinen eigenen Port, Namen, sein Zertifikat und seine Identität; Server-Admin ist nur das Verwaltungstool. Das Formular wartet, bis der neue Server erreichbar ist, und kopiert dann über die Backups beider Server.

## REST-API und Tokens

Für Skripte gibt es eine kleine REST-API. Unter "Mein Konto", "API-Tokens" legst du einen Token mit Name und Gültigkeit (1 bis 3650 Tage) an. Er erscheint genau einmal und wird nur als Hash gespeichert; "Widerrufen" macht ihn sofort ungültig. Ein Token darf, was sein Konto darf, auf dessen Servern, und funktioniert nicht, solange das Konto gesperrt ist.

```bash
curl -H "Authorization: Bearer ovs_..." https://manager.example.org/api/servers
curl -X POST -H "Authorization: Bearer ovs_..." https://manager.example.org/api/servers/<id>/restart
```

- `GET /api/servers`: die sichtbaren Server mit Id, Name, Port, Version und Status.
- `POST /api/servers/<id>/start`, `/stop`, `/restart`: 204 oder 409 mit Grund.
- `POST /api/servers/<id>/update` mit optionalem `?version=`: ohne Version das neueste Angebot.
- Ohne gültigen Token 401, ein fremder Server 403. Jeder Aufruf schreibt eine Audit-Zeile mit dem Namen des Tokens.

Die Beschreibung als OpenAPI 3.0 liegt unter `/api/openapi.json`.

## Backup des Verwaltungstools

Ohne die Datenbank verliert das Verwaltungstool Konten, Server und Zuweisungen, ohne die Schlüssel in `/data/keys` die Identitäten und damit die Server-Admin-Rolle auf jedem Server. Beides sichert das Backup des Verwaltungstools; Telemetrie und die Daten der Server gehören nicht dazu (die sichern die Server-Backups).

- **Herunterladen** (Admin, "Backup des Verwaltungstools"): Passwort zweimal eingeben (mindestens 12 Zeichen). Die Datei `ovs-manager-<Zeit>.ovsmb` enthält Datenbank, Schlüssel und ein Manifest, verschlüsselt mit AES-256-GCM. Ohne das Passwort ist sie wertlos; ein verlorenes Passwort lässt sich nicht zurückholen.
- **Zeitplan**: aus, täglich oder wöchentlich nach `/data/backups`, die neuesten N bleiben. Er braucht `OVS_MANAGER_BACKUP_PASSWORD`. Kopiere diese Dateien regelmässig vom Host weg, sonst gehen sie mit dem Host verloren.
- **Wiederherstellen** auf einem neuen Host: das Verwaltungstool frisch einrichten und auf `/setup` "Aus Backup wiederherstellen" wählen, Datei und Passwort angeben. Das geht nur, solange es kein Konto gibt. Ein falsches Passwort, eine beschädigte Datei oder ein Backup einer neueren Version wird abgelehnt und ändert nichts. Danach meldest du dich mit deinem bisherigen Konto an. Liegen die Server-Container samt Volumes auf dem neuen Host, findet das Verwaltungstool sie an ihren Labels wieder.

## HTTPS und Reverse Proxy

Das Verwaltungstool selbst spricht nur HTTP auf Port 6999. Für HTTPS stellst du einen Reverse Proxy davor und setzt dann zwei Variablen:

- `OVS_MANAGER_BASE_URL`: die öffentliche Adresse, z. B. `https://manager.example.org`.
- `OVS_MANAGER_TRUSTED_PROXIES`: die Adresse des Proxys, wie das Verwaltungstool sie sieht (die IP des Proxy-Containers im gemeinsamen Netz oder dessen Netz in CIDR).

Die Live-Seiten laufen über WebSockets (SignalR); der Proxy muss sie durchreichen.

**Nginx Proxy Manager**: einen Proxy Host für die Domain anlegen, Scheme `http`, Forward Hostname `ovs-manager` (wenn NPM im Netz `ovs-managed` hängt) oder die Host-IP, Port `6999`, "Websockets Support" einschalten, unter SSL ein Let's-Encrypt-Zertifikat anfordern und "Force SSL" setzen. In `OVS_MANAGER_TRUSTED_PROXIES` kommt die Adresse des NPM-Containers. Danach Port 6999 nicht mehr nach aussen veröffentlichen.

**Andere Proxys** (nginx, Traefik, HAProxy): an `http://ovs-manager:6999` weiterleiten, WebSockets (`Upgrade`- und `Connection`-Header) durchreichen und `X-Forwarded-For`, `X-Forwarded-Proto` und `X-Forwarded-Host` setzen.

**Mitgelieferter Caddy**: Caddy holt und erneuert das Zertifikat selbst. Die Domain muss auf den Host zeigen, Ports 80 und 443 müssen frei sein:

```bash
OVS_MANAGER_DOMAIN=manager.example.org docker compose -f deploy/manager/docker-compose.yml -f deploy/manager/caddy/docker-compose.yml up -d --build
```

`deploy/manager/caddy/docker-compose.yml` ergänzt die Compose-Datei des Verwaltungstools: Caddy auf 80 und 443 mit `deploy/manager/caddy/Caddyfile`, das Verwaltungstool veröffentlicht dann keinen Port mehr, beide teilen das Netz `edge` (10.255.250.0/29), und das Verwaltungstool vertraut genau der festen Adresse von Caddy. Basis-URL und vertrauenswürdiger Proxy sind dort schon gesetzt.

## Das Verwaltungstool aktualisieren

Ist eine neuere Version erschienen, zeigt das Verwaltungstool oben ein Banner. Mit dem Checkout:

```bash
git pull
docker compose -f deploy/manager/docker-compose.yml up -d --build
```

Mit dem fertigen Image aus der Registry (statt `build` ein `image: ghcr.io/rezondes/openvoicespeak-manager:<version>` in der Compose-Datei):

```bash
docker compose pull ovs-manager && docker compose up -d ovs-manager
```

Für die Entwicklung unter Windows baut `rebuild-manager.ps1` alles aus dem Checkout neu: das Server-Image `openvoicespeak/server:local` (nur bauen, die verwalteten Server laufen davon), das Verwaltungstool samt Socket-Proxy (fehlt `deploy/manager/.env`, legt es sie für Docker Desktop an) und den Client. Mit `-SkipServerImage`, `-SkipManager`, `-SkipClient` und `-NoStart` lässt sich ein Teil auslassen.

Die Datenbank wird beim Start selbst migriert; Daten und Schlüssel bleiben im Volume. Die Server laufen während des Updates weiter. Mach vorher ein Backup des Verwaltungstools.

## Fehlersuche

- **"Docker nicht erreichbar"** auf der Übersicht, `/healthz` antwortet 503: Der Socket-Proxy antwortet nicht. `docker compose -f deploy/manager/docker-compose.yml logs socket-proxy` zeigt meist "permission denied" auf dem Socket; dann stimmt `DOCKER_SOCKET_GID` nicht (siehe "Einrichtung mit Docker Compose"; Docker Desktop braucht 0).
- **Server bleibt "startet" oder "nicht erreichbar"**: Läuft der Container (`docker ps`)? Liegt er im Netz `ovs-managed`? Das Konsolen-Log auf der Log-Seite zeigt die Ausgabe des Servers.
- **"Der Port ist auf dem Host schon belegt"**: Ein anderer Dienst nutzt den Port. Wähle einen anderen aus 7000 bis 7099.
- **"Zertifikat geändert"**: Das Zertifikat des Servers ist neu (etwa nach einem Restore von woanders). Prüfe den Grund und klicke "Neues Zertifikat annehmen".
- **"veraltet (Protokoll N)"**: Der Server spricht ein älteres Protokoll; aktualisiere ihn.
- **"keine Telemetrie"** auf der Live-Ansicht: Der Server hat 2 Minuten nichts geschickt. Er muss im Netz `ovs-managed` liegen und `ovs-manager:4318` erreichen.
- **Live-Seiten aktualisieren sich hinter dem Proxy nicht**: WebSockets sind im Proxy aus.
- **Anmeldung klappt, aber jede Seite führt wieder zur Anmeldung**: Hinter HTTPS fehlt `OVS_MANAGER_BASE_URL` oder `OVS_MANAGER_TRUSTED_PROXIES`, oder der Proxy setzt `X-Forwarded-Proto` nicht.
- **Keine Mails**: Unter "E-Mail" eine Testmail schicken; die Fehlermeldung kommt vom SMTP-Server.
- **Admin-Passwort vergessen und kein SMTP**: Es gibt keinen Weg an der Datenbank vorbei. Spiele ein Backup des Verwaltungstools ein, dessen Passwort du kennst.
- Das Verwaltungstool schreibt seine Logs auf die Konsole: `docker compose -f deploy/manager/docker-compose.yml logs ovs-manager`.
