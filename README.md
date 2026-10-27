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

## Collegarsi al broker a mano (MQTT Explorer)

```
scripts/mqtt-token.sh --copy
```

Stampa (e copia) un token di accesso Supabase, valido circa un'ora, di un utente anonimo di sviluppo sempre uguale (sessione in `~/.config/hamqso/mqtt-dev-session.json`). In MQTT Explorer: `ws://` con Encryption (tls), host `spot.hamqso.app`, porta 443, basepath `mqtt`, username l'id che lo script stampa, password il token; in ADVANCED togliere `#` e `$SYS/#` e aggiungere `spots/#`.

## Test end-to-end (produzione)

`tests/HamQso.Spots.EndToEnd.Tests` prova il sistema pubblicato come lo vede un'app, senza l'app: `/health`, broker su `wss://spot.hamqso.app/mqtt` con un token Supabase vero (utente anonimo di prova), pubblicazione rifiutata, token falsi rifiutati, `alert_spots` che crea l'evento d'avviso solo per l'avviso di prova. Crea un utente anonimo Pro senza dispositivi e a fine test cancella tutto (utente, avvisi, eventi, `processed_spots`, statistiche). Non è nella soluzione: va lanciato a mano.

```
SUPABASE_PUBLISHABLE_KEY=sb_publishable_... SUPABASE_SECRET_KEY=sb_secret_... dotnet test tests/HamQso.Spots.EndToEnd.Tests
```

## Deploy su Coolify

`compose.yaml` (server, Postgres 17, Mosquitto con go-auth). Variabili da impostare in Coolify:

| Variabile | Cosa |
|---|---|
| `SUPABASE_SECRET_KEY` | chiave secret del progetto Supabase (per `alert_spots`) |
| `MQTT_INGESTOR_PASSWORD` | password dell'utente `ingestor` del broker |
| `POSTGRES_PASSWORD` | password del Postgres dell'archivio |

Dominio `spot.hamqso.app`: `/health` → server:8080; `wss://spot.hamqso.app/mqtt` → mosquitto:9001 (TLS di Traefik). Mosquitto ha la configurazione dentro la sua immagine (`deploy/mosquitto/Dockerfile`): Coolify non monta i file del repo (B21). Applicazione Coolify: `HamQSO_Spots` nel progetto HamQSO, deploy automatico a ogni push su `main`.
