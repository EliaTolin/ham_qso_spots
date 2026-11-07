# voacap-calibration

F71 phase 5: how far VOACAP's forecast is from what the Reverse Beacon Network really heard, for a grid of settings (man-made noise, required SNR), so that the forecast of the coverage test is tuned before release (plan F71, "Il pezzo più a rischio").

**Data.** The RBN history (https://www.reversebeacon.net/raw_data/, "freely available for study and analysis"; RBN asks only to share the ideas with the community): one zip a day, one CSV of about 500,000 spots. It is downloaded once into `~/Library/Caches/hamqso-f71-calibration/` (outside the repo, `HAMQSO_CALIBRATION_CACHE` to change it) with the User-Agent `HamQSO-dev`, read from the zip without extracting it, and treated as untrusted: every field is checked (`HistoryLine`), anything odd is left out. The RBN node list (for the skimmers' positions) is downloaded once a day into the same folder.

**Simulated tests.** A station whose locator is known because it is also an RBN node (its 6-character locator) **starts** calling CQ in CW on a band after at least 15 minutes of silence there (not in the first 15 minutes of a day: the silence before is not known); the test lasts **3 minutes** from its first spot, like a real test. The skimmers listening are found with the server's own rule (`SkimmerActivity`: a spot on the band from 10 minutes before the start to the end of the test), placed with the server's own rule (`LocateSkimmer`), the station itself excluded. Heard = the listening skimmers that spotted it in the 3 minutes.

**Sample.** At most `--per-stratum` tests (default 60) for each band (default 40, 20, 15, 10 m) and day or night at the station (6–18 local solar time), taken in turn from each station so a few busy stations do not fill it; repeatable with `--seed`.

**Forecast.** For each test the local VOACAP service is asked the probability of every listening skimmer with the reference station of the plan (dipole at 10 m, 100 W, isotropic receiver), the SSN of the month (NOAA SWPC's own observed monthly number, `observed_swpc_ssn`, public domain; never SILSO's `ssn`; NOAA's prediction for a month not over yet) and every pair of settings of the grid (`--noise`, default −155…−130 dBW; `--snr`, default 24…49 dB-Hz).

**Score** (per pair of settings, for all tests, per band, by day and by night): Brier score of every skimmer (lower = better probabilities), expected (sum of probabilities) against heard, and the app's margin on the tests with at least 3 expected: 10th, 50th (median) and 90th percentile, and how many are above +5%, near, below −5%. The best of a group: the lowest Brier among the pairs whose median margin is within ±10%.

**Outputs.**
- The grid, `calibration-<time>.csv` in the cache folder: `group,noise_dbw,required_snr_dbhz,tests,with_margin,expected,heard,brier,p10_margin,median_margin,p90_margin,above,near,below` (group: `all`, a band, `day`, `night`).
- The simulated tests with the chosen settings (`--export noise,snr`, else the best for all), for the app's distribution of the titles: `~/Library/Caches/hamqso-f71-fixtures/calibration_tests.csv` (`--export-path` to change it). No callsign or position in it. Columns:

| Column | Meaning |
|---|---|
| `test` | 1, 2, … |
| `date_utc`, `utc_hour`, `month` | when the test started |
| `band` | `40m`, `20m`, `15m`, `10m` |
| `daylight` | `day` or `night` at the station (6–18 local solar time) |
| `ssn` | the SSN of the theory used |
| `listening` | skimmers listening (with a position) |
| `expected_theory` | sum of the theory's probabilities over the listening skimmers (the app's `expected_theory`) |
| `heard` | skimmers that heard the station in the 3 minutes (always at least 1, see the limits) |
| `margin` | `(heard − expected) / expected`, empty when expected is under 3 (the app's rule) |
| `farthest_km` | the farthest skimmer that heard (great circle from the node's locator) |
| `continents` | continents of the skimmers that heard |
| `surprises` | skimmers that heard with a theory below 30% (the app's rule) |

- Next to it, `calibration_skimmers.csv`: one row per listening skimmer per test, for the app's own title rules (record and growth need the skimmers and the station's history). Columns: `test_id` (the `test` above), `callsign` (the base call of the station calling CQ: public RBN data), `started_at` (ISO UTC), `band`, `locator` (the station's node locator, 6 characters), `power_w` (always empty: unknown; the theory is the reference station, a dipole at 10 m with 100 W), `skimmer`, `dxcc`, `continent`, `lat`, `lon` (the server's position rule), `p_theory` (VOACAP with the chosen settings), `p_sky` (always empty: no ionosonde in the history), `heard` (1/0), `snr_db` and `after_s` (dB of its first spot in the test and seconds from the start; empty when not heard). The sample is stratified, so each station has only some of its tests: its history for records and growth is partial. For that, `calibration_history_skimmers.csv` (same columns, `test_id` from 100001) has **every** test of the `--history-stations` stations (default 8) with most tests in the days read. In both files the first skimmer that heard always has `after_s` 0, because the test starts at the first spot (the real start of the CQ is not in the history).

```
voacap/run-local.sh                                   # the VOACAP service on 127.0.0.1:18090 (token in voacap/.local-token)
dotnet run -c Release --project tools/voacap-calibration/HamQso.Spots.VoacapCalibration -- \
  --days 20260114,20260115,… [--bands 40m,20m,15m,10m] [--per-stratum 60] \
  [--noise -155,-150,…] [--snr 24,29,…] [--export -135,49] [--export-band 40m=-135/54,15m=-135/39] \
  [--history-stations 8] [--seed 71] [--parallel 6]
```

`--export-band` gives a band its own pair for the exports and the "with chosen" scores (each pair must be in the grid). The grid of 36 pairs on 500 tests takes about an hour; a few pairs about a minute. `VOACAP_URL` and `VOACAP_TOKEN` point it elsewhere.

**Limits of the method.**
- The stations' real power and antennas are unknown: the margin is exactly that difference, on average.
- Only stations that are also RBN nodes, and only tests heard by at least one skimmer (a CQ nobody heard is not in the RBN history), so the "heard" side is a little optimistic.
- A skimmer reports a station at most once in a while.

## First run (07/11/2026, 10-minute slots, superseded by the phase 5 run below)

3 days (4–6 November 2026), 661 events of 54 stations, 200 used, SSN 83:

| noise dBW | required SNR dB-Hz | Brier | median margin | above / near / below |
|---|---|---|---|---|
| −150 | 24 (the plan's starting values) | 0.258 | −64% | 19 / 15 / 162 |
| −145 | 34 | 0.192 | −38% | 68 / 4 / 122 |
| −140 | 34 | 0.179 | −22% | 74 / 7 / 107 |
| −140 | 39 | 0.167 | −2% | 85 / 9 / 89 |
| −136 | 34 | 0.170 | −8% | 81 / 9 / 94 |
| −136 | 39 | 0.162 | +22% | 93 / 3 / 78 |

With the starting values VOACAP promises far more than the RBN hears: almost every test would be "below the forecast". Around −140 dBW and 39 dB-Hz a 100 W dipole is on average where the forecast says, with better probabilities. To confirm on more days and seasons before fixing `_shared/coverage/forecast-settings.ts`.

## Phase 5 run (07/11/2026)

19 days (14–16 January, 18–20 March, 15–17 June, 28 October – 6 November 2026), 1,448 simulated tests of 110 stations, 500 used (40 m 150, 20 m 150, 15 m 115, 10 m 85), noise −155…−120 dBW × required SNR 24…64 dB-Hz.

- The minimum is a ridge: what matters is almost only required SNR minus noise (−135/49, −130/44, −125/39 give the same Brier). Noise is kept at −135 dBW (VOACAP's "residential" is −136) and the required SNR is tuned per band.
- The reliability of the deck (90%) does not change VOACAP's probabilities (REL), only the LUF: nothing to tune there.

| Band | Required SNR (noise −135 dBW) | Tests (with margin) | Brier | Margin P10 / median / P90 |
|---|---|---|---|---|
| 40 m | 54 dB-Hz | 150 (85) | 0.118 | −93% / +5% / +543% |
| 20 m | 49 dB-Hz | 150 (50) | 0.108 | −91% / +1% / +998% |
| 15 m | 39 dB-Hz | 115 (54) | 0.112 | −83% / −4% / +270% |
| 10 m | 39 dB-Hz | 85 (12) | 0.083 | −88% / −64% / +312% |
| All | | 500 (201) | 0.111 | −92% / −1% / +649% |

The plan's starting values (−150 dBW, 24 dB-Hz): Brier 0.251, median margin −84%. One pair for every band (−135/49): Brier 0.112, median +6%, but 15 m at +224%. 10 m needs more days with the band open.
