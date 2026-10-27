# ham_qso_spots

Server degli spot di HamQSO (F24): raccoglie gli spot da nodi DX cluster, HamQTH, DX Summit, POTA, SOTA e WWFF, toglie i doppioni, li salva in Postgres (7 giorni) e li pubblica su MQTT (Mosquitto) per le app. Calcola anche gli avvisi.

- .NET 11, architettura pulita, SOLID.
- Ospitato su Coolify, dominio `spot.hamqso.app`.
- Nota del backlog: `../Backlog/Funzionalità/F24 Flusso spot centralizzato su Supabase Realtime.md`.

## Come gira

```
Fonti (4 nodi telnet, HamQTH, DX Summit, POTA, SOTA, WWFF)
  → IngestSpots: arricchimento, dedup (call + kHz + 10 min)
  → Postgres dx_spots (7 giorni): ogni spot salvato, nuovo o aggiornato, prende un seq crescente
  → Mosquitto: solo le novità, non salvate      spots/dx/<banda>/<cw|phone|digital|other>, spots/pota|sota|wwff
  → Supabase: edge function alert_spots (avvisi e push) per gli spot nuovi
```

### Protocollo per le app

Un solo formato, senza compressione: `{"seq": 1042, "full": false, "spots": [{spot con il suo seq}, …]}` (campi snake_case come le colonne di `dx_spots`). Niente remove né scadenze: l'app mostra "X minuti fa" da `spot_time`.

- **MQTT** `wss://spot.hamqso.app/mqtt` (username l'id utente, password il token di accesso Supabase): le novità di ogni topic, `full: false`.
- **REST** `GET https://spot.hamqso.app/v1/spots` (`Authorization: Bearer <token Supabase>`):
  - `program` (obbligatorio: `dx`, `pota`, `sota`, `wwff`), `band` e `mode` (i segmenti dei topic, ripetibili o separati da virgola), `limit` (max e default 200), `since_seq`;
  - senza `since_seq`: gli ultimi 200 per `spot_time`, `full: true`;
  - con `since_seq`: gli spot salvati dopo, `full: false`; se sono più di 200, la lista attuale con `full: true`.
- **App**: si iscrive a MQTT e tiene da parte le novità → chiede la lista → applica le novità con `seq` maggiore; a ogni riconnessione `since_seq` con l'ultimo seq visto.

- `GET /health` (porta 8080, pubblica): stato di fonti, `broker`, `alerts`, `archive`, senza dettagli degli errori.
- `POST /mqtt/user|superuser|acl` (porta 8081, solo rete Docker): backend HTTP di `mosquitto-go-auth`; solo l'utente `ingestor` pubblica.
- Nodi telnet e intervalli delle fonti: `appsettings.json` (`Telnet`, `Feeds`).

## Sviluppo

```
dotnet test                 # serve Docker per Postgres e Mosquitto (Testcontainers)
scripts/coverage.sh         # test + copertura delle righe: fallisce sotto il 90% totale o l'85% per assembly
dotnet user-secrets set "Supabase:SecretKey" "sb_secret_..." --project src/HamQso.Spots.Host
```

## Collegarsi al broker a mano (MQTT Explorer)

```
scripts/mqtt-token.sh --copy
```

Stampa (e copia) un token di accesso Supabase, valido circa un'ora, di un utente anonimo di sviluppo sempre uguale (sessione in `~/.config/hamqso/mqtt-dev-session.json`). In MQTT Explorer: `ws://` con Encryption (tls), host `spot.hamqso.app`, porta 443, basepath `mqtt`, username l'id che lo script stampa, password il token; in ADVANCED togliere `#` e `$SYS/#` e aggiungere `spots/#`.

## Prova di carico del broker

```
TOKEN=$(scripts/mqtt-token.sh) dotnet run -c Release --project tools/HamQso.Spots.LoadTest -- --steps 100,250,500,1000 --hold 60
```

Simula N app collegate via WebSocket (iscritte a `spots/dx/#` e `spots/pota`), a gradini; misura connessioni, tempo di connessione e ritardo di consegna della stessa novità a tutti. Si ferma da solo se più dell'1% delle connessioni fallisce, se le app collegate calano o se `/health` non è `Healthy`, e chiude tutte le connessioni.

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
