# Operations

## Where it runs

| | |
|---|---|
| Coolify | `https://coolify.auroradigital.it`, project **HamQSO**, environment `production`, application **HamQSO_Spots** (uuid `7o5wrv0wfsljg0sj7bxykspy`), server `localhost` (Oracle VPS `130.61.220.143`) |
| Build | Docker Compose from `compose.yaml` of this repo, via the GitHub App `elia_tolin_github` |
| Deploy | **automatic at every push to `main`** (server and Mosquitto images rebuilt, containers restarted) |
| Domain | `spot.hamqso.app` (TLS by Coolify/Traefik) |

Routing set in Coolify (Domains of the compose services):

| URL | Goes to |
|---|---|
| `https://spot.hamqso.app` | `server:8080` (`/health`, `/v1/spots`, `/v1/dx/spots`) |
| `https://spot.hamqso.app/mqtt` (WebSocket) | `mosquitto:9001` (prefix stripped) |

Port `8081` of the server (`/mqtt/*`, broker auth) and `1883` of Mosquitto (the server publishing) are not exposed.

## Configuration

`src/HamQso.Spots.Host/appsettings.json` holds the defaults; environment variables override any key (`Section__Key`, `Telnet__Nodes__0__Enabled=false`).

| Section | Keys |
|---|---|
| `Server` | `PublicPort` (8080), `InternalPort` (8081) |
| `ConnectionStrings:Spots` | Postgres connection |
| `Telnet` | `Callsign` (`N0CALL-87`; fallback `IU4VRB-1` if nodes refuse it), `Backlog` (50), `IdleTimeout` (5 min), `ConnectTimeout` (10 s), `LoginFallback` (4 s), `BacklogDelay` (2 s), `MaxBackoff` (2 min), `Nodes[]` (`Name`, `Host`, `Port`, `Enabled`) |
| `Feeds:<name>` | `HamQTH`, `DXSummit`, `POTA`, `SOTA`, `WWFF`: `Enabled` (true), `Interval` (SOTA at least `00:01:00`: a shorter one stops the server at start) |
| `Mqtt` | `Host`, `Port` (1883), `ClientId`, `Username` (`ingestor`), `Password` |
| `Supabase` | `Url`, `SecretKey` (alerts, and reading the callsign of a profile for `/v1/dx/spots`), `AlertsFunction` (`alert_spots`), `ReaderUrls` (projects whose users may only read: HamQRG) |
| `DxSend` | The node users spot on through the server: `Host` (`dxfun.com`), `Port` (8000), `LoginTimeout` (15 s), `LoginFallback` (4 s), `WelcomeSilence` (2 s), `ReplyWindow` (2 s), `MaxSessions` (50; beyond it new logins get 503 `busy`), `IdleTimeout` (3 min) |

Options are validated at startup: a missing required value stops the server with a clear error.

## Secrets

| Secret | Production | Local development |
|---|---|---|
| Supabase secret key (`sb_secret_…`, the project's `default` key; alerts and profile reads) | Coolify variable `SUPABASE_SECRET_KEY` | `dotnet user-secrets set "Supabase:SecretKey" … --project src/HamQso.Spots.Host` |
| Broker password of `ingestor` | Coolify variable `MQTT_INGESTOR_PASSWORD` (random) | `Mqtt:Password` in user secrets |
| Postgres password | Coolify variable `POSTGRES_PASSWORD` (random) | compose env file |
| Coolify API token (for scripted changes) | — | `~/.config/hamqso/coolify.env` (mode 600), never in a repo |

Nothing secret is in the repo; `.env` is git-ignored.

### Changing a secret

- **Supabase key** (for example after a rotation in the Supabase dashboard): set the new value in Coolify `SUPABASE_SECRET_KEY`, redeploy. Until then `/health` shows `alerts: failing` and `POST /v1/dx/spots` answers 500 (the profile cannot be read).
- **Broker password**: set `MQTT_INGESTOR_PASSWORD` in Coolify, redeploy. It is the only place: Mosquitto asks the server, which compares it.
- **Postgres password**: changing `POSTGRES_PASSWORD` does **not** change the password of a database already created in the volume. Change it inside Postgres first (`alter user spots password '…'` from the Coolify terminal of the `postgres` container), then the variable, then redeploy.

## Monitoring

Uptime Kuma (Aurora), monitors of type **HTTP(s) - Keyword** on `https://spot.hamqso.app/health`, 60 s, 2 retries, notification AuroraStatus, certificate expiry on (the second and third names are the ones suggested when they were set up; check them in Uptime Kuma):

| Monitor | Keyword | Down means |
|---|---|---|
| HamQSO_Cluster | `"broker":"ok"` | server down, or the apps get no news |
| HamQSO_Avvisi | `"alerts":"ok"` | alerts do not reach Supabase |
| HamQSO_Archivio | `"archive":"ok"` | Postgres down: no news (no seq) and no lists |

`/health` answers 200 also when Degraded, so a plain HTTP monitor would not see these. A monitor on `"status":"Healthy"` is not used: one telnet node reconnecting for a few minutes is normal and would page.

Logs: Coolify → HamQSO_Spots → Logs (per container). Source failures are logged as warnings `Source <name> failing: <reason>`, failed steps as errors `Ingestion step <step> failed`. Every spot sent for a user is logged as `DX spot by user <id> as <login>: <line> -> <outcome>` (an invalid line is written as `<invalid, N chars>`); a node that cannot be reached also as a warning `DX node <host> unavailable for <login>`, a refused new session as `DX sending busy: <max> sessions open, <login> refused` (if it shows often, raise `DxSend__MaxSessions`).

## When an alarm fires

**First, keep the alerts working** if the server cannot be fixed in minutes: turn the old cron back on (it fetches the sources itself, every minute).

```
supabase db query --linked "select cron.alter_job(1, active := true)"    # from ham_qso_supabase
```

Turn it off again when the server is back, or DX alerts arrive twice (the two use different DX keys):

```
supabase db query --linked "select cron.alter_job(1, active := false)"
```

Then, by alarm:

| Alarm | Look at | Usual causes |
|---|---|---|
| HamQSO_Cluster, `/health` unreachable | Coolify: are the containers running? Logs of `server` | Deploy in progress (a minute), build failed (Deployments tab), VPS down |
| HamQSO_Cluster, `broker: failing` | Logs of `mosquitto` | Mosquitto restarting (configuration), wrong `MQTT_INGESTOR_PASSWORD` |
| HamQSO_Avvisi, `alerts: failing` | Supabase dashboard → Edge Functions → `alert_spots` logs | Secret key rotated or wrong, function not deployed, Supabase incident |
| HamQSO_Archivio, `archive: failing` | Logs of `postgres` and `server` | Postgres down, disk full, password changed only in Coolify |
| A source failing for long (`/health` Degraded) | Logs: `Source <name> failing` | Node down or refusing `N0CALL-87`, API changed; the others go on, nothing urgent |
| Web users report "node unavailable" (502) when spotting | Logs: `DX node … unavailable` | DXFun down or slow: point `DxSend__Host`/`DxSend__Port` to another node that accepts user logins, redeploy |

After any fix: redeploy if needed, check `/health`, run the end-to-end tests ([development.md](development.md)).

## Routine

- **Deploy**: merge to `main` and push; Coolify builds and restarts. Clients are disconnected for a few seconds and reconnect.
- **Disable a telnet node**: set `Enabled: false` in `appsettings.json` (commit) or a Coolify variable `Telnet__Nodes__<index>__Enabled=false`.
- **Archive size**: about 11,000 spots a day, 7 days ≈ 80,000 rows ≈ 20 MB; cleaned every hour.
- **Capacity** (load test 27/10/2026): 500 clients at once with news within 0.9 s (95th percentile); about 900 is the limit. Watch CPU and memory in Coolify before the user count gets close.
- **.NET**: 11.0 RC in production; move to the final 11.0 when it ships (November 2026): `global.json`, `Dockerfile` images, package versions.

## Known gaps

- No rate limit on `/v1/spots` (sending spots has one: 3 a minute, 20 an hour per user and per callsign, kept in memory, so a restart resets it).
- `/v1/dx/spots` is not in `/health`: the sending sessions open only when a user spots.
- Container logs are only in the Coolify UI (not reachable by the Coolify API for compose applications).
- Permissions still to ask: DX Summit (no published terms), SOTA commercial licence (`marketing/outreach/04-sota-mt.md`).
