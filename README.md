# ham_qso_spots

Server degli spot di HamQSO (F24): raccoglie gli spot da nodi DX cluster, HamQTH, DX Summit, POTA, SOTA e WWFF, toglie i doppioni, li salva in Postgres (7 giorni) e li pubblica su MQTT (Mosquitto) per le app. Calcola anche gli avvisi.

- .NET 11, architettura pulita, SOLID.
- Ospitato su Coolify, dominio `spot.hamqso.app`.
- Nota del backlog: `../Backlog/Funzionalità/F24 Flusso spot centralizzato su Supabase Realtime.md`.

## Come gira

```
Fonti (4 nodi telnet, HamQTH, DX Summit, POTA, SOTA, WWFF)
  → IngestSpots: arricchimento, dedup (call + kHz + 10 min), Postgres dx_spots (7 giorni)
  → Mosquitto: un messaggio salvato per topic, con la lista intera
      spots/dx/<banda>/<cw|phone|digital|other>   (50 spot DX in tutto)
      spots/pota, spots/sota, spots/wwff          (tutti gli spot attivi)
  → Supabase: edge function alert_spots (avvisi e push) per gli spot nuovi
```

- `GET /health` (porta 8080, pubblica): stato di ogni fonte, senza dettagli degli errori.
- `POST /mqtt/user|superuser|acl` (porta 8081, solo rete Docker): backend HTTP di `mosquitto-go-auth`. Le app si collegano con il token di accesso Supabase come password; solo l'utente `ingestor` pubblica.
- Nodi telnet e intervalli delle fonti: `appsettings.json` (`Telnet`, `Feeds`).

## Sviluppo

```
dotnet test                 # serve Docker per Postgres e Mosquitto (Testcontainers)
dotnet user-secrets set "Supabase:SecretKey" "sb_secret_..." --project src/HamQso.Spots.Host
```

## Deploy su Coolify

`compose.yaml` (server, Postgres 17, Mosquitto con go-auth). Variabili da impostare in Coolify:

| Variabile | Cosa |
|---|---|
| `SUPABASE_SECRET_KEY` | chiave secret del progetto Supabase (per `alert_spots`) |
| `MQTT_INGESTOR_PASSWORD` | password dell'utente `ingestor` del broker |
| `POSTGRES_PASSWORD` | password del Postgres dell'archivio |

Dominio `spot.hamqso.app`: `/health` → server:8080; MQTT su WebSocket → mosquitto:9001 (TLS di Traefik).
