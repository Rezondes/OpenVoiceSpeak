# Telemetrie-Backends zum Ausprobieren

Der Server exportiert nur, wenn `OTEL_EXPORTER_OTLP_ENDPOINT` gesetzt ist (siehe "Telemetrie (optional)" im Haupt-README). Jeder Ordner hier startet den OpenVoiceSpeak-Server zusammen mit einem Backend, das per OTLP empfängt. Der Server wird aus diesem Checkout gebaut; für ein Release-Image ersetzt du in der Datei `build:` durch `image: ghcr.io/rezondes/openvoicespeak-server:<Version>`.

```bash
cd deploy/observability/grafana-lgtm   # oder aspire-dashboard, jaeger, seq
docker compose up -d --build
docker compose down -v                 # beendet alles und löscht die Daten des Beispiels
```

Alle Beispiele belegen Port 7000 (TCP und UDP) wie der normale Server; läuft der schon, stoppe ihn vorher. Die Beispiele sind für den lokalen Test gedacht: Die Oberflächen haben keine Anmeldung.

| Ordner | Backend | Oberfläche | Zeigt |
|---|---|---|---|
| `grafana-lgtm` | Grafana LGTM (Loki, Grafana, Tempo, Prometheus) | http://localhost:3000 (admin / admin) | Traces, Metriken, Logs |
| `aspire-dashboard` | .NET Aspire Dashboard | http://localhost:18888 | Traces, Metriken, Logs (nur im Speicher) |
| `jaeger` | Jaeger v2 | http://localhost:16686 | Traces |
| `seq` | Seq | http://localhost:5380 | Logs, Traces |

Was ankommt: ein Span pro Anfrage und pro Handshake (mit dem Fingerabdruck, nie dem Nickname), die Metriken `ovs.*` (Sitzungen, Anfragen, Sprachpakete, Steuerverkehr, Chat-Anzahl und -Größe nur je Channel-ID) samt .NET-Laufzeitmetriken und das Server-Log mit der Trace-ID der Anfrage. Channel-Logs, Chat-Texte und das Admin-Token verlassen den Server nie. Die Fehler-ID, die der Client bei einem unerwarteten Fehler zeigt, ist die Trace-ID: Damit findest du im Backend den Trace und die Logzeile.
