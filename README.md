# ham_qso_spots

The spots server of HamQSO. It collects DX cluster and activity (POTA, SOTA, WWFF) spots from every source once, cleans and merges them, keeps 7 days of them in Postgres, and hands them to the apps: the news live over MQTT, the starting list over REST. It also triggers the HamQSO alerts within seconds.

```
DX cluster nodes (telnet), HamQTH, DX Summit, POTA, SOTA, WWFF
        │  one connection / one request per interval for all of HamQSO
        ▼
  ingestion: enrich → merge duplicates → save (Postgres, seq) ─┬─► MQTT news      wss://spot.hamqso.app/mqtt
                                                               ├─► REST lists     https://spot.hamqso.app/v1/spots
                                                               └─► alerts         Supabase edge function alert_spots
```

Production: `https://spot.hamqso.app` (Coolify, application `HamQSO_Spots`). Health: `https://spot.hamqso.app/health`.

## Documentation

| Document | For |
|---|---|
| [docs/protocol.md](docs/protocol.md) | App and client developers: MQTT topics, REST API, spot fields, the client algorithm, errors |
| [docs/architecture.md](docs/architecture.md) | How the server works inside: sources, merging, enrichment, seq, alerts, access control, design decisions |
| [docs/operations.md](docs/operations.md) | Running it: deployment, configuration, secrets, monitoring, what to do when an alarm fires |
| [docs/development.md](docs/development.md) | Working on it: build, tests, coverage, end-to-end and load tests, adding a source |

## Quick start

```
dotnet test                                  # all tests (Docker needed for Postgres and Mosquitto)
scripts/coverage.sh                          # tests + line coverage, fails under 90% total / 85% per assembly
scripts/mqtt-token.sh --copy                 # a Supabase token to look at the broker with MQTT Explorer
```

.NET 11 (SDK pinned in `global.json`), clean architecture (`Domain`, `Application`, `Infrastructure`, `Host`). Backlog note (Italian): `../Backlog/Funzionalità/F24 Flusso spot centralizzato su Supabase Realtime.md`.
