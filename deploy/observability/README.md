# Telemetrie-Backends zum Ausprobieren

Der Server exportiert nur, wenn `OTEL_EXPORTER_OTLP_ENDPOINT` gesetzt ist (siehe "Telemetrie (optional)" im Haupt-README). Jeder Ordner hier startet den OpenVoiceSpeak-Server zusammen mit einem Backend, das per OTLP empfängt. Der Server wird aus diesem Checkout gebaut; für ein Release-Image ersetzt du in der Datei `build:` durch `image: ghcr.io/rezondes/openvoicespeak-server:<Version>`.

```bash
cd deploy/observability/grafana-lgtm   # oder aspire-dashboard, jaeger, seq, elastic, opensearch, netdata
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
| `elastic` | Elasticsearch, Kibana und der EDOT Collector | http://localhost:5601 (Observability, APM, Discover) | Traces, Metriken, Logs |
| `opensearch` | OpenSearch, OpenSearch Dashboards und Data Prepper | http://localhost:5601 (Observability, Trace Analytics, Discover) | Traces, Metriken, Logs |
| `netdata` | Netdata | http://localhost:19999 | Metriken, Logs, Traces |

**Elastic und OpenSearch** laufen in diesen Beispielen ohne Sicherheitsfunktionen: kein Passwort, kein TLS. Das ist nur für den lokalen Test gedacht. Bevor du so ein Backend im Netz erreichbar machst, schalte die Sicherheit ein (Elastic: `xpack.security.enabled`, OpenSearch: Security-Plugin mit `OPENSEARCH_INITIAL_ADMIN_PASSWORD`) und gib Kibana bzw. Dashboards und dem Collector bzw. Data Prepper Zugangsdaten. Jedes der beiden braucht etwa 2 GB RAM. Unter Linux muss auf dem Host `vm.max_map_count` mindestens 262144 sein (`sudo sysctl -w vm.max_map_count=262144`, dauerhaft in `/etc/sysctl.conf`); Docker Desktop unter Windows und macOS bringt das mit. Beide Oberflächen nutzen Port 5601, starte also nur eins der beiden Beispiele auf einmal. Data Prepper nimmt jedes Signal auf einem eigenen Port an, daher setzt das OpenSearch-Beispiel zusätzlich `OTEL_EXPORTER_OTLP_TRACES_ENDPOINT`, `_METRICS_ENDPOINT` und `_LOGS_ENDPOINT`.

Was ankommt: ein Span pro Anfrage und pro Handshake (mit dem Fingerabdruck, nie dem Nickname), die Metriken `ovs.*` (Sitzungen, Anfragen, Sprachpakete, Steuerverkehr, Chat-Anzahl und -Größe nur je Channel-ID) samt .NET-Laufzeitmetriken und das Server-Log mit der Trace-ID der Anfrage. Channel-Logs, Chat-Texte und das Admin-Token verlassen den Server nie. Die Fehler-ID, die der Client bei einem unerwarteten Fehler zeigt, ist die Trace-ID: Damit findest du im Backend den Trace und die Logzeile.
