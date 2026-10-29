# Architecture

## Why it exists

Before this server every HamQSO phone opened its own telnet connection to a DX cluster node and polled HamQTH, POTA, SOTA and WWFF on its own (every 3 s before B20). SOTA complained about the traffic. Now the server asks every source once for all of HamQSO, merges and enriches the spots once, and pushes them to the apps; it also feeds the alerts within seconds instead of every minute.

## Components

```
                        ┌─────────────────────────── Coolify (one Docker Compose) ───────────────────────────┐
DX cluster nodes ──────►│ server (.NET 11)                                                                    │
HamQTH, DX Summit ─────►│   sources → IngestSpots → Postgres (dx_spots, 7 days)                               │
POTA, SOTA, WWFF ──────►│                        └→ Mosquitto (MQTT news) ◄── mosquitto-go-auth ── /mqtt/*    │
                        │   GET /v1/spots (lists)  POST /v1/dx/spots (send, telnet as the user)  GET /health  │
                        │   /v1/proxy/{service} (web app → QRZ, eQSL, SOTA… without CORS)                     │
                        │   GET /v1/rbn/{call} (SSE, coverage test; one shared RBN telnet)                    │
                        └──────────┬──────────────────────────────┬───────────────────────────────────────────┘
                                   │ alert_spots (secret key)     │ wss://spot.hamqso.app/mqtt, https://spot.hamqso.app/v1/spots
                                   ▼                              ▼
                     Supabase: US states, match_alert_spots,    HamQSO apps
                     push (FCM), statistics
```

| Container | Image | Role |
|---|---|---|
| `server` | this repo (`Dockerfile`) | sources, ingestion, archive, publishing, REST API, spot sending, CORS proxy and RBN stream for the web app, broker auth backend, health |
| `postgres` | `postgres:17-alpine` | the archive (`dx_spots`), 7 days, volume `postgres-data` |
| `mosquitto` | `deploy/mosquitto/Dockerfile` (`iegomez/mosquitto-go-auth` + config) | MQTT broker for the apps; no persistence |

## Code layout (clean architecture)

| Project | Contains | Depends on |
|---|---|---|
| `HamQso.Spots.Domain` | `Spot`, bands, band plan and mode, DXCC by prefix, base callsign (shared cases with the app and the backend), duplicate rule, enrichment, topics, the DX line a user may send and the telnet login (`Spotting/`). Pure functions only | nothing |
| `HamQso.Spots.Application` | Ports (`ISpotSource`, `ISpotStore`, `ISpotReader`, `ISpotPublisher`, `IAlertNotifier`, `ITokenValidator`, `ISourceHealth`, `IProfileReader`, `IClusterSender`, `IUpstreamProxy`, `IRbnFeed`), use cases (`IngestSpots`, `GetSpots`, `SendDxSpot`, `ForwardToUpstream`, `WatchRbn`), in-memory dedup, sliding-window limits (`Limits/`), broker access rules, health registry | Domain |
| `HamQso.Spots.Infrastructure` | Adapters: telnet and HTTP sources, Postgres store, MQTT publisher, Supabase alerts notifier, JWT validator, Supabase profile reader, telnet sender (`Sending/`), proxy allowlist and forwarding (`Proxy/`), shared RBN telnet feed (`Rbn/`) | Application |
| `HamQso.Spots.Host` | Composition root, background workers, CORS, endpoints (`/health`, `/v1/spots`, `/v1/dx/spots`, `/v1/proxy/*`, `/v1/rbn/*`, `/mqtt/*`) | Application, Infrastructure |

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

## Sending spots for the web app

A browser cannot open telnet, so the web app sends its DX spots through the server (`POST /v1/dx/spots`, contract in [protocol.md](protocol.md)); the phones keep their own telnet. `SendDxSpot`:

1. checks the line (`DxSpotLine`: one `DX <kHz.d> <call> <text>` line of printable ASCII, nothing else) and the SSID (1–99);
2. counts the attempt for the user (`SpotRateLimiter`, in memory: a restart forgets the counts): 3 a minute and 20 an hour;
3. reads the callsign of the user's profile (`SupabaseProfileReader`: PostgREST `profiles?id=eq.<sub>&select=callsign` with the secret key, since profiles are private) and checks it again against the profile pattern (`ClusterLogin`);
4. counts the spot against the server budget, 60 a minute for all users: last, so only spots that passed every check use it (failed or refused attempts cannot starve everyone); a refusal here still counts for the user;
5. sends it as `<CALLSIGN>-<ssid>` (`TelnetClusterSender`), logging user, login, line and outcome.

Anonymous users (`is_anonymous` in the token) and reader projects are refused by the endpoint before all this. The telnet session refuses to write any line with a control character or a byte above `0x7E` (last guard against a second command or telnet bytes).

`TelnetClusterSender` mirrors the app's `OnDemandTelnetSender`: one session per user (`ClusterSendSession`, login dialogue in `ClusterSendDialogue`: the prompt or a 4 s fallback, an empty password if asked, ready after 2 s of silence), reused by the next spots of that user, replaced when the login changes (another SSID), closed 3 minutes after the last spot; a login is held by one user at a time (another user gets the generic `503 busy`, which does not reveal that the login is in use, until that session closes when idle); spots of one user go one at a time. At most 50 sessions: a new one beyond them closes the least recently used session that is not sending, and is refused (`busy`) only when all are sending. Once a spot has started it follows its own timeouts (15 s login, 2 s reply window), not the HTTP request, and an open session always gets its idle timer, whatever happened. The node's lines in the 2 s after the command are returned, spots excluded. The confirmation is the echo in the spot list, as on the phone. Node and timings are configuration (`DxSend`), default DXFun as the app.

### Accepted trade-off: the callsign is not verified

The login is the callsign of the user's profile, and nobody checks that the callsign belongs to the user: a signed-in user can write any callsign in the profile and spot with it. This is accepted (Elia, F58): the phone app already logs in to the cluster by telnet with whatever callsign the user typed, as every cluster client does, and DX cluster nodes do not verify callsigns either. The server does not make it worse, and limits what one account can do with it:

- spots only from signed-in accounts (no anonymous users), the callsign checked against the profile pattern, one DX line only;
- 3 spots a minute and 20 an hour per account, 60 a minute for the server, every send logged with the user id (the account behind a misuse can be found and removed);
- one session per account and one account per login at a time.

There is **no limit per callsign** on purpose: since callsigns are not verified, a limit per callsign would let anyone use up a real station's budget by writing its callsign in their profile (griefing). A per-callsign rule can come back only together with callsign verification.

## The RBN stream for the coverage test

The coverage test (Pro) listens to the Reverse Beacon Network for the user's station; the phone opens `telnet.reversebeacon.net:7000` itself, the web app cannot (F58 decision 9). `GET /v1/rbn/{callsign}` streams the matching lines as Server-Sent Events ([protocol.md](protocol.md#stream-get-v1rbncallsign-the-coverage-test)).

- `RbnTelnetFeed`: **one** telnet connection for every listener of the server, logged in with the server's callsign (`Rbn:Callsign`, the same `N0CALL-87` as the cluster sources), opened by the first listener, closed 1 minute after the last one leaves, reopened with exponential backoff (up to 2 min) when it drops or is silent for 2 minutes. Telnet decoding, line splitting and the login prompt are the ones of the cluster sources. Every spot line goes to every listener through its own bounded queue (1000 lines, oldest dropped), so a slow browser never slows the others. RBN sends the whole world, several lines a second: the feed does not filter, the listeners do.
- `WatchRbn`: keeps the lines whose base call equals the asked one (`Callsign.Base`, the app's `baseCallsign`, tested on the shared `base_callsign_cases.json`), at most 2 streams per user.
- The endpoint: signed-in users only (the test needs a callsign; anonymous tokens are free to get), token also in `?access_token=` for `EventSource` (never logged: same rule as the proxy), a `: ping` every 15 s, at most 15 minutes per stream. Logged: user, callsign, number of lines, why it ended.
- Not in `/health`: the connection exists only while someone listens.

## The proxy: nothing kept, nothing logged

On the phone the app talks to QRZ Logbook, eQSL, HRDLog, SOTA (SSO, SOTAwatch, sotadata), hamqsl.com and country-files.com directly; the credentials (QRZ API key, eQSL and HRDLog passwords, SOTA token) stay on the device. Those services send no CORS headers, so in a browser the web app must go through the server (F58 decision 16): `ANY /v1/proxy/{service}/{path}`, contract in [protocol.md](protocol.md#rest-the-proxy-v1proxy).

`ForwardToUpstream` (use case: per-user limit, the log line) → `UpstreamProxy` (allowlist, forwarding) → the service. The rule: **the server passes the request on and keeps nothing**.

- Credentials travel in the query (QRZ `KEY=`, eQSL `password=`), in the body or in `Authorization`. The server never writes them anywhere: no store, no cache, and **no log of path, query, headers or bodies**. The only log line is `Proxy <user> <service> <method> -> <status> in <ms> ms, <up> B up, <down> B down`.
- How the rule is kept: the forwarding `HttpClient` has no loggers (`RemoveAllLoggers`), because its request lines carry the full address with the query; `Microsoft.AspNetCore` logs stay at `Warning` (at `Information` and below the request line, with the query, is logged); errors of the service are logged by their kind (`UpstreamTimeout`), never by their exception message (which may hold the address). A Host test sends secrets through the proxy and fails if any of them is in the log.
- Why: these are the user's accounts on other services. Keeping or logging them would make the server a place to steal them from, and the app's privacy promise ("credentials stay on your device") must stay true on the web: they only pass through.
- Only the listed hosts, in code, not configuration: the proxy cannot be turned into an open relay. The path cannot change the host; redirects are followed only on the same host; only `Content-Type`, `Accept`, `Authorization`, `User-Agent` go out (no cookies: the server has no session with the services); only status, `Content-Type` and body come back.
- Anonymous HamQSO users may use it: the app runs on an anonymous Supabase session until the user signs in, and propagation, SOTA summits and the integrations need no HamQSO account. Its own token travels in `X-HamQSO-Auth` because `Authorization` belongs to the service. Bodies up to 10 MB, 30 s.
- Limits, counted after the token check (`ProxyRateLimiter`, in memory): 120 calls a minute per user, 300 per client address (`AddressKey`: IPv4 by address, an IPv4-mapped IPv6 as its IPv4, native IPv6 by its /64, since one home or phone usually has a whole /64), and a server budget in two parts, 2000 a minute for users with an account and 1000 for anonymous users (`503 busy`), so anonymous traffic cannot starve accounts. The client address is the connection's, or, when the connection comes from a trusted proxy (`ForwardedHeaders:TrustedNetworks`, by default the private ranges: the server's ports are only exposed in the Docker network, where Traefik is), the last `X-Forwarded-For` entry, the one Traefik wrote; earlier entries are the client's word and are ignored. The address only counts for its limit and is never logged.

### Accepted trade-off: anonymous users on the proxy

Anyone can get an anonymous Supabase token, so for the proxy the token proves little: a user limit alone could be dodged with a new anonymous user per call. Requiring an account was refused (the app works without one), so the protection is the address limit (300 a minute, about 2.5 times one user's) and the server budget, split so that anonymous calls (1000 a minute) never use the accounts' share (2000), on top of the fixed allowlist and the size and time limits: at worst someone uses the server to make a few thousand calls a minute to the eight services, with their own credentials and visible to those services as coming from the server.

Remaining trade-offs, accepted (Elia, F58: no more hardening rounds on the proxy):

- Users behind one address (a club, a mobile carrier NAT, a whole IPv6 /64) share its 300 calls a minute.
- Someone with many addresses (many IPv4s or many /64s) gets more, up to the anonymous budget; when it is used up, every anonymous user waits a minute (accounts are not affected), and the same for accounts among themselves.
- An account is easy to make (Apple, Google): a determined abuser can move to the accounts' budget, at the cost of tying the calls to an account that can be removed.
- The limits live in memory: a restart resets them.
- If misuse shows up in the log (many calls of one service), the next step is an account for the heavy services, not more machinery here.

## Access control

- **Apps** connect to Mosquitto with the Supabase access token as password. `mosquitto-go-auth` (HTTP backend) asks the server: `POST /mqtt/user` (who may connect), `/mqtt/acl` (who may read or write a topic), `/mqtt/superuser` (always no).
- **Rules** (`MqttAccess`): a valid Supabase token may read and subscribe `spots/…`; only the `ingestor` account (password from configuration, compared in constant time) may write; nobody may use other topics; the name `ingestor` never logs in with a token.
- **Sending spots**: only tokens of the HamQSO project of signed-in users (`TokenUser.IsReader` and `IsAnonymous` false); the login is always the profile's callsign, never one from the request.
- **Tokens** (`SupabaseJwtValidator`): ES256 signature checked with the public keys (JWKS) of the project that issued the token, issuer `https://<project>.supabase.co/auth/v1`, audience `authenticated`, expiry. Accepted projects: HamQSO (`Supabase:Url`) and the readers (`Supabase:ReaderUrls`, today HamQRG `dhmzkhipxvxtbbchvquc`), which may only read like HamQSO users; the alerts stay on HamQSO. A token of any other issuer is refused before any request, so keys are fetched only from the configured projects. Keys cached 10 minutes per project; an unknown key id forces a refresh at most once a minute per project; if Supabase does not answer, the cached keys are kept (tokens signed with them are still accepted, others are refused).
- **CORS**: the HTTP API answers browsers of the origins in `Cors:Origins` only (see [protocol.md](protocol.md#cors)); no cookies are used.
- **Ports**: `8080` is public (Traefik routes `/` to it: `/health`, `/v1/spots`, `/v1/dx/spots`, `/v1/proxy/*`, `/v1/rbn/*`); `8081` only in the Docker network (`/mqtt/*` for Mosquitto). The `/mqtt` endpoints check the local port of the connection, not the `Host` header.
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
| Web spots through the server, with the profile's callsign | F58 decisions 5 and 13: a browser has no telnet; nobody spots with someone else's callsign; few spots per user; every send logged |
| A pass-through proxy for services without CORS, nothing logged | F58 decision 16: everything of the app works on the web; the user's credentials of other services only pass through |
| RBN for the web over SSE, one shared telnet | F58 decision 9: the coverage test on the web; SSE is plain HTTP (EventSource, CORS, Traefik) and one connection serves everyone, instead of one per user |
