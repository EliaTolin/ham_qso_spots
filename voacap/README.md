# voacap

The VOACAP service of HamQSO (F71): `voacapl` (VOACAP of NTIA/ITS for Linux, by James Watson HZ1JW; public domain, Watson's parts CC0) behind a small HTTP API. It knows nothing about HamQSO: it runs the circuits it is asked for. The model settings (noise, required SNR, minimum angle) and the mapping of the app's antenna types are in the Supabase edge function `coverage_forecast`, not here.

Production: `https://voacap.hamqso.app` (Coolify, next to the spots server). Citation where its numbers are shown: "VOACAP, NTIA/ITS".

## API

Every call but `/health` needs `Authorization: Bearer <VOACAP_TOKEN>` (401 otherwise). JSON in snake_case; a missing field, a wrong type or a value out of range is `400 {"error": "invalid_request"}`. The shared fixtures `voacap_predict_v1.json` and `voacap_effective_ssn_v1.json` (in `tests/HamQso.Voacap.Tests/Fixtures`, identical in `ham_qso_supabase`) are the contract.

### `POST /v1/predict`

One transmitter, 1 to 4 scenarios, 1 to 600 receivers (`422 {"error": "too_many_receivers"}` above 600). Answers within 10 s (600 receivers and 3 scenarios take well under a second).

```json
{
  "month": 11, "utc_hour": 14, "frequency_mhz": 14.045,
  "settings": {"method": 30, "noise_dbw": -150, "min_takeoff_deg": 3, "required_snr_dbhz": 24},
  "receive_antenna": {"model": "isotropic", "gain_dbi": 0},
  "transmitter": {"lat": 45.6, "lon": 9.2},
  "scenarios": [{"id": "theory", "ssn": 118, "power_w": 100, "antenna": {"model": "dipole", "height_m": 10}}],
  "receivers": [{"id": "OK4YL", "lat": 49.98, "lon": 14.46}]
}
```

```json
{"engine": "voacapl 0.7.6", "elapsed_ms": 27,
 "results": {"theory": [{"id": "OK4YL", "rel": 0.95, "snr_dbhz": 59, "muf_mhz": 14.4, "mode": "1F2"}]}}
```

| Field | Range / meaning |
|---|---|
| `month`, `utc_hour` | 1–12, 0–23 |
| `frequency_mhz` | 1.8–30 |
| `settings` | `method` 30 only; `noise_dbw` −170…−100 (man-made noise at 3 MHz); `min_takeoff_deg` 0.1–40; `required_snr_dbhz` −20…100 |
| `scenarios[].id` | `[a-z0-9_-]{1,20}`, unique; `ssn` 0–300; `power_w` > 0 |
| antennas | `model`: `isotropic`, `dipole`, `vertical`, `yagi`, `loop`; `gain_dbi` −20…30 and `height_m` (0, 300] optional |
| `receivers[].id` | 1–32 characters, unique |
| result `rel` | probability (0–1) that the SNR reaches `required_snr_dbhz` |
| result `snr_dbhz` | median SNR in 1 Hz; null when VOACAP finds no path |
| result `muf_mhz` | the circuit MUF; `mode` the mode at the frequency (`1F2`, `2F2`, `1E`…), null if none |

How the antennas are given to voacapl (ITSA-1 types of voacapl's samples, poor ground):

| Model | voacapl | Defaults |
|---|---|---|
| `isotropic` | its `default/isotrope`; with a gain, a flat gain table (type 11) | 0 dBi |
| `dipole` | horizontal half-wave dipole (type 23) | 10 m, 2.15 dBi |
| `vertical` | quarter-wave monopole on the ground (type 22); `height_m` not used | 2.15 dBi |
| `yagi` | horizontal yagi (type 24): **its pattern has no azimuth**, so it is as if it pointed at every receiver | 12 m, 8.15 dBi |
| `loop` | the dipole pattern (type 23) with the loop's gain | 10 m, 3.15 dBi |

A gain is given to voacapl over a dipole (−2.15 dB). voacapl takes the antennas once per deck, so each scenario is one run of voacapl (run at the same time).

### `POST /v1/effective-ssn`

The SSN with which VOACAP gives the MUF(3000) measured by an ionosonde: a 3000 km east-west circuit centred on it, VOACAP's MUF for SSN 0 to 300 every 10, then every 1 around the best; the nearest wins, on a tie the one nearest `ssn_month`. Two runs of voacapl (`iterations`). The MUF does not grow strictly with the SSN, so a grid, not a bisection. A measured MUF below what VOACAP gives at SSN 0 is SSN 0 (and `predicted_mufd_mhz` says how far it is).

```json
{"month": 11, "utc_hour": 14, "ionosonde": {"lat": 41.9, "lon": 12.5}, "measured_mufd_mhz": 25.2, "ssn_month": 118}
```
```json
{"engine": "voacapl 0.7.6", "ssn_effective": 25, "predicted_mufd_mhz": 25.2, "iterations": 2}
```

### Errors and health

- `503 {"error": "engine_failed", "message": "<what voacapl said, one line>"}`: voacapl did not finish the deck (it exits 0 even then: a run is good only when its output ends), or took more than 8 s.
- `GET /health`: `{"status": "ok", "engine": "voacapl 0.7.6"}`, no token (for Coolify's health check).
- Logged: endpoint, number of scenarios and receivers, time, failures. **Never the positions** (they are where the users are).

## Code

| Project | Contains |
|---|---|
| `src/HamQso.Voacap.Core` | Pure: the request rules, the deck (`VoacapDeck`), the antenna files (`AntennaFiles`), the output reader (`VoacapOutput`), great circles, the SSN search; use cases `Predict`, `EffectiveSsn` on the port `IVoacapEngine` |
| `src/HamQso.Voacap.Host` | `ProcessVoacapEngine` (voacapl as a process: own `--run-dir` per run, antenna files by hash in `itshfbc/antennas/hq/`, stdin closed because voacapl pauses on errors, killed after 8 s, at most `MaxParallel` at once), the endpoints, the options |
| `tests/HamQso.Voacap.Tests` | Everything without voacapl: deck and antenna text, the output recorded from voacapl 0.7.6, rules, use cases with a fake engine, the API against the shared fixtures |
| `tests/HamQso.Voacap.Engine.Tests` | With the real voacapl, in Docker: the contract's request makes physical sense, 600 receivers × 3 scenarios within 10 s, every antenna model, 160 m and midnight, receive gain, effective SSN, a deck voacapl cannot read |

```
cd voacap && dotnet test --project tests/HamQso.Voacap.Tests    # no voacapl needed
voacap/test-engine.sh                                         # the engine tests, in Docker (docker build --target engine-test)
voacap/run-local.sh                                           # the service on http://127.0.0.1:18090, token in voacap/.local-token
voacap/run-local.sh down
```

## Deploy (Coolify)

- New application from this repo, branch `main`: build pack Dockerfile, base directory `/`, Dockerfile `voacap/Dockerfile`, port 8080, domain `https://voacap.hamqso.app`.
- Variable `VOACAP_TOKEN` (random, at least 16 characters: `openssl rand -hex 24`); the same value in the Supabase secret `VOACAP_TOKEN`, with `VOACAP_URL=https://voacap.hamqso.app`.
- DNS: `voacap.hamqso.app` A/AAAA (or CNAME) to the Coolify server, as `spot.hamqso.app`.
- Optional: `Voacap__MaxParallel` (default the CPU count), `Voacap__RunTimeout` (8 s).
- Health: the image has its own `HEALTHCHECK` (`/health`); an Uptime Kuma keyword monitor on `"status":"ok"`.
- The image is the .NET 11 ASP.NET image (Ubuntu 26.04) with the `voacapl` package (0.7.6) and `curl` for the health check; the engine name comes from the package version at build time.

## Calibration

`tools/voacap-calibration` (in the repo root) compares VOACAP's predictions with real RBN history, to fix noise, required SNR and the title thresholds before release (plan F71, phase 5).
