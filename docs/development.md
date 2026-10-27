# Development

## Prerequisites

- .NET SDK `11.0.100-rc.2` (pinned in `global.json`, roll forward to later feature bands).
- Docker running: the Postgres and Mosquitto tests use Testcontainers.
- For the end-to-end tests and the token script: the Supabase CLI logged in (or the keys in the environment).

Rules of the HamQSO repos (`../CLAUDE.md`, Italian): every task in its own git worktree (`../_worktrees/<task>`), a bug gets its failing tests before the fix, clean architecture and no duplicated rules.

## Solution

```
src/
  HamQso.Spots.Domain           pure rules: Spot, bands, band plan, DXCC, duplicates, topics
  HamQso.Spots.Application      ports, IngestSpots, GetSpots, dedup, broker access, health registry
  HamQso.Spots.Infrastructure   telnet and HTTP sources, Postgres, MQTT, Supabase, JWT
  HamQso.Spots.Host             composition root, workers, endpoints
tests/
  HamQso.Spots.*.Tests          one per project (xUnit v3, Microsoft.Testing.Platform)
  HamQso.Spots.EndToEnd.Tests   against production, not in the solution
tools/
  HamQso.Spots.LoadTest         load test of the broker
scripts/
  coverage.sh, coverage_report.py, mqtt-token.sh
deploy/mosquitto/               Mosquitto image (configuration inside)
```

Build settings for every project are in `Directory.Build.props` (nullable, warnings as errors, analyzers `latest-recommended`); package versions in `Directory.Packages.props`.

## Tests

```
dotnet test                 # 245 tests, about 40 s
scripts/coverage.sh         # the same with line coverage; fails under 90% total or 85% per assembly
```

Coverage on 27/10/2026: 98.2% (Domain 100%, Infrastructure 99.3%, Host 95.5%, Application 94.7%); generated code (`obj/`: regex and logging generators) is left out.

| Kind | Where | Notes |
|---|---|---|
| Pure rules | `Domain.Tests` | Band plan: the 57 cases of `Fixtures/band_plan_cases.json`, a copy of `ham_qso_supabase/supabase/functions/_shared/band_plan_cases.json` (keep them identical; the app has the same file) |
| Use cases | `Application.Tests` | Fakes for every port; a fake store gives growing seqs as Postgres does |
| Sources | `Infrastructure.Tests/Sources` | Real answers recorded on 27/10/2026 in `Fixtures/` plus malformed rows; telnet against a fake node on loopback |
| Postgres, Mosquitto | `Infrastructure.Tests/Postgres`, `/Mqtt` | Real containers (`postgres:17-alpine`, `eclipse-mosquitto:2.0`, and the image of `deploy/mosquitto`) |
| Host | `Host.Tests` | The real Host via `WebApplicationFactory` (`TestHost`: no workers, fake archive reader and token check; the local port is chosen with a test header) |

Test names are sentences (`After_10_minutes_it_is_a_new_spot`); the analyzer rule against underscores is off for tests.

## Running it locally

```
dotnet user-secrets set "Supabase:SecretKey" "sb_secret_..." --project src/HamQso.Spots.Host
docker compose --env-file my.env up --build        # my.env: POSTGRES_PASSWORD, MQTT_INGESTOR_PASSWORD, SUPABASE_SECRET_KEY
```

Locally nothing publishes `spot.hamqso.app`; map the ports in a compose override if needed (`server:8080`, `mosquitto:1883/9001`). The real sources are used: the server logs in to the telnet nodes as `N0CALL-87` like production. Alerts go to the real `alert_spots`: use a key of a test project, or expect real alerts.

## End-to-end tests (production)

```
SUPABASE_PUBLISHABLE_KEY=sb_publishable_... SUPABASE_SECRET_KEY=sb_secret_... dotnet test tests/HamQso.Spots.EndToEnd.Tests
```

They test the deployed system as an app sees it: `/health`, the client algorithm (MQTT subscription, REST list, a live piece of news found again with `since_seq`), refused tokens and publishes, `/mqtt/*` not reachable from outside, `alert_spots` matching only the test alert.

What they create and remove: an anonymous Supabase user, Pro for the run (no devices, so no push reaches a phone), an alert on `Q0E2E`; at the end the user (with its alerts and events), the `processed_spots` rows and the +1 in the anonymous statistics.

**Before sending made-up spots to `alert_spots`**: open alerts of real users (no callsign, no entity, no reference, only band or mode) match any spot. The tests use a `Q` prefix (no DXCC entity), 20 m and no reference because on 27/10/2026 the open alerts were only on 40 m, 60 m and 6 m. Check again before changing them:

```sql
select bands, modes, count(*) from alerts
 where enabled and cardinality(callsigns) = 0 and cardinality(dxcc_entities) = 0 and cardinality(refs) = 0
 group by 1, 2;
```

## Load test

```
TOKEN=$(scripts/mqtt-token.sh) dotnet run -c Release --project tools/HamQso.Spots.LoadTest -- --steps 100,250,500,1000 --hold 60
```

Opens N clients over WebSocket (subscribed to `spots/dx/#` and `spots/pota`) in steps, holds each step, measures connections, connection time and how long the same piece of news takes to reach all clients. It stops by itself if more than 1% of the connections fail, the connected clients drop or `/health` is not Healthy, and closes every connection. All clients run on one machine: at high counts the limit may be that machine.

## A token for the broker

```
scripts/mqtt-token.sh --copy
```

Prints (and copies) an access token of a development anonymous user, always the same one (session in `~/.config/hamqso/mqtt-dev-session.json`). MQTT Explorer: protocol `ws://` with Encryption (tls) on, host `spot.hamqso.app`, port 443, basepath `mqtt`, username the id the script prints, password the token; in ADVANCED replace `#` and `$SYS/#` with `spots/#`.

## Adding a source

- **Another DX cluster node**: configuration only, a new entry in `Telnet:Nodes` (check first that it accepts `N0CALL-87`).
- **Another HTTP feed** (RBN aggregate, PSKReporter by HTTP, a new activity program):
  1. record a real answer in `tests/HamQso.Spots.Infrastructure.Tests/Fixtures/`, add a malformed row;
  2. write the parser tests (count, first spot, skipped rows);
  3. implement `IPolledFeed` in `Sources/Http/` (pure `Parse`); a new program also needs a `SpotProgram` value, its topic and the REST `program` value;
  4. add it to `SpotSourceFactory.Feeds` with its default interval and to `Feeds` in `appsettings.json`.
- **A streaming source** (another protocol): implement `ISpotSource` directly, report to `ISourceHealth`, reconnect on your own; register it in `SpotSourceFactory`.

Nothing else changes: merging, archive, publishing, alerts and health work for every source.

## Shared contracts with other repos

| What | Here | Elsewhere |
|---|---|---|
| Band plan cases | `tests/HamQso.Spots.Domain.Tests/Fixtures/band_plan_cases.json` | `ham_qso_supabase/supabase/functions/_shared/`, `ham_qso_flutter/test/fixtures/` |
| DXCC prefixes | `src/HamQso.Spots.Domain/Data/dxcc_prefixes.json` | `ham_qso_supabase/supabase/functions/_shared/data/` |
| Alert payload | `Infrastructure/Alerts/AlertSpotPayload.cs` | `NormalizedSpot` in `ham_qso_supabase/.../match_alerts/types.ts`, parsed by `alert_spots/api/parse-spots.ts` |
| Client protocol | [protocol.md](protocol.md) | the app (`ham_qso_flutter`) |
