# Architecture

## Why it exists

Before this server every HamQSO phone opened its own telnet connection to a DX cluster node and polled HamQTH, POTA, SOTA and WWFF on its own (every 3 s before B20). SOTA complained about the traffic. Now the server asks every source once for all of HamQSO, merges and enriches the spots once, and pushes them to the apps; it also feeds the alerts within seconds instead of every minute.

## Components

```
                        ┌─────────────────────────── Coolify (one Docker Compose) ───────────────────────────┐
DX cluster nodes ──────►│ server (.NET 11)                                                                    │
HamQTH, DX Summit ─────►│   sources → IngestSpots → Postgres (dx_spots, 7 days)                               │
POTA, SOTA, WWFF ──────►│                        └→ Mosquitto (MQTT news) ◄── mosquitto-go-auth ── /mqtt/*    │
                        │   GET /v1/spots (lists)  GET /health                                                │
                        └──────────┬──────────────────────────────┬───────────────────────────────────────────┘
                                   │ alert_spots (secret key)     │ wss://spot.hamqso.app/mqtt, https://spot.hamqso.app/v1/spots
                                   ▼                              ▼
                     Supabase: US states, match_alert_spots,    HamQSO apps
                     push (FCM), statistics
```

| Container | Image | Role |
|---|---|---|
| `server` | this repo (`Dockerfile`) | sources, ingestion, archive, publishing, REST API, broker auth backend, health |
| `postgres` | `postgres:17-alpine` | the archive (`dx_spots`), 7 days, volume `postgres-data` |
| `mosquitto` | `deploy/mosquitto/Dockerfile` (`iegomez/mosquitto-go-auth` + config) | MQTT broker for the apps; no persistence |

## Code layout (clean architecture)

| Project | Contains | Depends on |
|---|---|---|
| `HamQso.Spots.Domain` | `Spot`, bands, band plan and mode, DXCC by prefix, duplicate rule, enrichment, topics. Pure functions only | nothing |
| `HamQso.Spots.Application` | Ports (`ISpotSource`, `ISpotStore`, `ISpotReader`, `ISpotPublisher`, `IAlertNotifier`, `ITokenValidator`, `ISourceHealth`), use cases (`IngestSpots`, `GetSpots`), in-memory dedup, broker access rules, health registry | Domain |
| `HamQso.Spots.Infrastructure` | Adapters: telnet and HTTP sources, Postgres store, MQTT publisher, Supabase alerts notifier, JWT validator | Application |
| `HamQso.Spots.Host` | Composition root, background workers, endpoints (`/health`, `/v1/spots`, `/mqtt/*`) | Application, Infrastructure |

## Sources

Every source implements `ISpotSource` and does not know the others; the Host starts all the enabled ones the same way (`SpotSourceFactory`) and restarts a source that fails, 10 s later.

| Source | How | Default interval |
|---|---|---|
| DX cluster nodes | Telnet (DXSpider format), one connection per node, login `N0CALL-87`, `sh/dx 50` backlog after login; a node silent for 5 min is reconnected; exponential backoff up to 2 min. Telnet negotiation is refused | live |
| HamQTH | `https://www.hamqth.com/api/v2/spots?limit=200` (HamQTH asks for at most one request every 15 s) | 15 s |
| DX Summit | `https://www.dxsummit.fi/api/v1/spots?limit=100` | 30 s |
| POTA | `https://api.pota.app/spot/activator` | 30 s |
| SOTA | `https://api-db2.sota.org.uk/api/spots/epoch` every interval; the list `https://api-db2.sota.org.uk/api/spots/50/all/all` only when the epoch differs from the one of the last list read (SOTA's rule). `TEST` spots dropped; a `QRT` spot is kept with `qrt: true` (at 0 kHz when it has no frequency, as SOTAwatch often sends it) and never alerted (B29) | 1 min (the minimum: a shorter interval stops the server at start) |
| WWFF | `https://spots.wwff.co/static/spots.json`; spots with `QRT` in the remarks dropped | 1 min |

Every HTTP request carries the User-Agent `HamQSO/1.0 (IU4VRB; +https://hamqso.app)` (SOTA asks for the app name and a callsign). A feed with an epoch (`IPolledFeed.EpochUrl`, today only SOTA) reports ok when the epoch is unchanged and sends no batch; when the epoch does not answer the list is not asked and the feed reports failing.

Telnet nodes enabled in production: DXFun (EA4RCH-5), IZ8MBW-6, IK5PWJ-6, DA0BCC-7; EA4URE-2 and PI4CC are configured but disabled (spares). Nodes and intervals are configuration, not code.

Parsers are pure functions (`Parse(body, now)`); a malformed row is skipped, the rest of the answer goes on. A telnet `DX de` line carries only `HHMM`: it is taken as today UTC, or yesterday if that is more than 5 minutes in the future.

## Ingestion (`IngestSpots`)

One batch at a time, whatever the source:

1. **Enrich** (`SpotEnrichment`): band from the frequency; DXCC and continent from the callsign prefix (unless the source gives them, as HamQTH does); mode.
2. **Merge duplicates** (`InMemorySpotDeduplicator`, rule in `DuplicateRule`):
   - DX: same callsign, same kHz rounded, at most 10 minutes apart → one spot. Feeds and spotters are joined; a mode reported by a source wins over a band plan guess.
   - xOTA: the same activation on the same band, at any distance in time: the key is `CALL_REFERENCE_BAND` (set by the enrichment). The newer spot wins (frequency, mode, time, comment); an older one changes nothing. Another band is another spot. Before this rule every respot had a new POTA id: the 200 latest POTA spots were 66 activations of the last 7 minutes.
   - Outcome: new, merged (something added or newer), or unchanged (dropped).
   - Memory forgets DX spots after 20 minutes and xOTA spots after 6 hours (as long as Supabase keeps alerted keys in `processed_spots`, so a respot of the same activation never alerts again, and the same activation the next day does). At startup the last 6 hours are reloaded from the archive, so a restart neither republishes nor re-alerts.
3. **Save** new and merged spots in Postgres; every save gets the next `seq` (sequence `dx_spots_seq_counter`), also on update. The save only happens for a newer spot, or the same moment with something changed: an identical or older one (SOTA resends its last 50 spots, POTA the latest of every activation) changes nothing and gets no seq, so it is not published again.
4. **Publish** the saved spots on their topics (`spots/dx/<band>/<mode>`, `spots/<program>`), only after the save: a spot without seq is never published.
5. **Alert**: new spots at most 15 minutes old (a telnet backlog after a reconnection never alerts) go to Supabase.

Steps 3-4 and step 5 run side by side: neither a slow archive nor a silent broker holds the alerts back (bug B21).

### Mode

The DX cluster has no mode field. The mode is, in this order: the one decoded by an RBN skimmer (spotter ending with `-#`, comment like `CW 12 dB`); the one written in the comment as a whole word (CW first, then digital, then phone, as Wavelog); otherwise the band plan of the station's IARU region (FT8/FT4/JS8 calling windows; no mode where a segment is shared). Same rules as the app and the backend: the 57 shared cases of `band_plan_cases.json` (a copy is in the tests) must pass in all three.

`mode_category`: `cw`; `phone` for SSB, USB, LSB, AM, FM, DV, DMR, C4FM, D-STAR, Fusion; `digital` for FT8, FT4, RTTY, PSK, JS8, SSTV, WSPR and the other data modes.

## Archive

Table `dx_spots`, primary key `(program, spot_key)`, with `seq` (from a sequence, so unique and growing; indexed, without a unique constraint), indexes on `seq`, `spot_time`, `(program, spot_time desc)`, `(callsign, spot_time desc)`. The schema (`Postgres/Schema.sql`) is applied at every startup and is idempotent; it upgraded the first production table (without `seq`) in place, and removed the xOTA rows keyed by the program's own id when the activation key came. A worker deletes rows older than 7 days every hour. No backup on purpose: 7 days of spots refill on their own.

Saves come one batch at a time, so a reader never sees a seq whose earlier seqs are not committed yet; the REST lists read the current seq and the rows in one repeatable-read transaction.

## Publishing and lists

- MQTT: QoS 1, not retained, `{"seq", "full": false, "spots"}`; the publisher connects with a 5 s timeout and, after a failure, tries again at most every 10 s (publishes fail fast in between).
- REST `GET /v1/spots` (`GetSpots`): latest 200 of the filter, or the changes since `since_seq`, or the current list if the changes are more than 200. Read from Postgres at every request.

The full contract for clients is in [protocol.md](protocol.md).

## Alerts

`SupabaseAlertNotifier` (official C# client `Supabase.Functions`) posts new spots to the edge function `alert_spots` of `ham_qso_supabase`, with the project secret key. The function (`@supabase/server`, `auth: 'secret:*'`) runs the same pipeline as the old `match_alerts` cron: US states (DX by FCC licence, POTA from the park location, SOTA from the summit), `match_alert_spots` (matching, anti-spam, history), FCM pushes, anonymous statistics. The cron `match-alerts` is disabled since 27/10/2026 and can be turned back on as a fallback ([operations.md](operations.md)).

## Access control

- **Apps** connect to Mosquitto with the Supabase access token as password. `mosquitto-go-auth` (HTTP backend) asks the server: `POST /mqtt/user` (who may connect), `/mqtt/acl` (who may read or write a topic), `/mqtt/superuser` (always no).
- **Rules** (`MqttAccess`): a valid Supabase token may read and subscribe `spots/…`; only the `ingestor` account (password from configuration, compared in constant time) may write; nobody may use other topics; the name `ingestor` never logs in with a token.
- **Tokens** (`SupabaseJwtValidator`): ES256 signature checked with the public keys (JWKS) of the project that issued the token, issuer `https://<project>.supabase.co/auth/v1`, audience `authenticated`, expiry. Accepted projects: HamQSO (`Supabase:Url`) and the readers (`Supabase:ReaderUrls`, today HamQRG `dhmzkhipxvxtbbchvquc`), which may only read like HamQSO users; the alerts stay on HamQSO. A token of any other issuer is refused before any request, so keys are fetched only from the configured projects. Keys cached 10 minutes per project; an unknown key id forces a refresh at most once a minute per project; if Supabase does not answer, the cached keys are kept (tokens signed with them are still accepted, others are refused).
- **Ports**: `8080` is public (Traefik routes `/` to it: `/health`, `/v1/spots`); `8081` only in the Docker network (`/mqtt/*` for Mosquitto). The `/mqtt` endpoints check the local port of the connection, not the `Host` header.
- `/health` is public and shows only the state of each entry, never error messages (those go to the log).

## Health

`GET /health` → `{"status": "Healthy|Degraded|Unhealthy", "sources": {"<name>": "ok|failing|starting", …}}`. Entries: every source, `broker` (publishing to Mosquitto), `alerts` (calls to `alert_spots`), `archive` (saves to Postgres). Degraded when some entry is failing, Unhealthy when all are. An entry is failing when its last report was a failure. The HTTP status is 200 also when Degraded: monitors must look at the body ([operations.md](operations.md)).

## Decisions

| Decision | Why |
|---|---|
| Own server and broker on Coolify, not Supabase Realtime | No cost per message and no 500-connection cap; the ingestor runs anyway |
| News over MQTT + starting list over REST, no retained lists | The first version retained whole lists per topic: a POTA update (~34 KB) to 500 clients was 17 MB and delivery reached 17 s. With news only, 500 clients get them within 0.9 s |
| No remove, no expiry; the client shows the age | As Ham2K PoLo: the spot time is enough, the server knows nothing about "active" |
| xOTA: one row per activation and band | The REST list of 200 covers all the running activations instead of the last few minutes of respots |
| `seq` from Postgres | One order for MQTT and REST; `since_seq` after a disconnection is a plain query |
| No compression | Decided for simplicity |
| Alerts stay in Supabase (`alert_spots`) | Matching, pushes, US states and statistics in one place for the cron, the server and the self-spots |
| .NET 11 | Chosen to try .NET; RC "go-live" in October 2026, final in November 2026 |
