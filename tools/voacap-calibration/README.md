# voacap-calibration

F71 phase 5: how far VOACAP's forecast is from what the Reverse Beacon Network really heard, for a grid of settings (man-made noise, required SNR), so that the forecast of the coverage test is tuned before release (plan F71, "Il pezzo più a rischio").

**Data.** The RBN history (https://www.reversebeacon.net/raw_data/, "freely available for study and analysis"; RBN asks only to share the ideas with the community): one zip a day, one CSV of about 500,000 spots. It is downloaded once into `~/Library/Caches/hamqso-f71-calibration/` (outside the repo, `HAMQSO_CALIBRATION_CACHE` to change it) with the User-Agent `HamQSO-dev`, read from the zip without extracting it, and treated as untrusted: every field is checked (`HistoryLine`), anything odd is left out. The RBN node list (for the skimmers' positions) is downloaded once a day into the same folder.

**Events.** A real CQ in CW of a station whose locator is known because it is also an RBN node (its 6-character locator), in a 10-minute slot on one band; the skimmers listening then are found with the server's own rule (`SkimmerActivity`: a spot on the band from 10 minutes before), placed with the server's own rule (`LocateSkimmer`), the station itself excluded. Heard = the listening skimmers that spotted it in the slot.

**Forecast.** For each event the local VOACAP service is asked the probability of every listening skimmer with the reference station of the plan (dipole at 10 m, 100 W, isotropic receiver) and the settings under test.

**Score** (per pair of settings): Brier score of every skimmer (lower = better probabilities), expected (sum of probabilities) against heard, and the app's margin on the events with at least 3 expected (median, and how many are above +5%, near, below −5%).

```
voacap/run-local.sh                                   # the VOACAP service on 127.0.0.1:18090 (token in voacap/.local-token)
dotnet run --project tools/voacap-calibration/HamQso.Spots.VoacapCalibration -- \
  --days 20261104,20261105,20261106 --ssn 83 --events 200 \
  [--noise -155,-150,-145,-140,-136] [--snr 14,19,24,29,34,39] [--seed 71]
```

`--ssn` is NOAA's predicted monthly SSN for those days (`json/solar-cycle/predicted-solar-cycle.json`): the "theory" of the app. The report is printed and saved as `calibration-<time>.csv` in the cache folder. `VOACAP_URL` and `VOACAP_TOKEN` point it elsewhere.

Limits of the method: the stations' real power and antennas are unknown (the margin is exactly that difference, on average); only stations that are also RBN nodes; a skimmer reports a station at most once in a while, so "heard" is per slot.

## First run (07/11/2026)

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
