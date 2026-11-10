#!/usr/bin/env python3
"""F71 phase 5: the stability criteria of decision 24, band by band, on the exports of the tool.

  python3 -I stability.py --cal <dir> --final <dir> --seasons <dir>,<dir>,... --chosen 42

<dir> is an --export-path folder of the tool (calibration_tests.csv, calibration_skimmers.csv, grid_path.txt).

1. Brier skill score > 0 against climatology: a constant probability, the observed frequency of the
   band and day/night on the calibration days, scored on the final days.
2. Brier of the final days at most 0.02 above the calibration days.
3. Median margin of the final days within ±25%.
4. Each season tuned alone: its best required SNR within ±5 dB of the chosen one.
5. At least 30 final tests with a margin.
6. Reliability: probability bands 0-10, 10-30, 30-60, 60-100%; observed frequency in the band or within 0.05
   of it (bands with at least 50 skimmers).
Also: share of final tests "above" with thresholds +15%, +25%, +50%.
"""
import argparse

import numpy as np
import csv
import math
import os
import statistics
from collections import defaultdict

BANDS = ["80m", "40m", "30m", "20m", "17m", "15m", "12m", "10m"]
BINS = [(0.0, 0.1), (0.1, 0.3), (0.3, 0.6), (0.6, 1.0001)]

ap = argparse.ArgumentParser()
ap.add_argument("--cal", required=True)
ap.add_argument("--final", required=True)
ap.add_argument("--seasons", default="")
ap.add_argument("--chosen", type=float, required=True)
ap.add_argument("--bands", default=",".join(BANDS))
ap.add_argument("--fixture", help="write the shared fixture of the approved model (decision 30) to this path, then stop")
ap.add_argument("--fixture-bands", default="40m,30m")
ap.add_argument("--typical", action="store_true",
                help="decision 29: margin on the typical expected, k_b x expected, k_b = median heard/expected of the calibration tests with at least 3 expected")
ap.add_argument("--summer", action="store_true", help="add a May-August term to the recalibration (sporadic E is not in VOACAP)")
ap.add_argument("--recalibrate", action="store_true",
                help="correct VOACAP's probability with a logistic curve per band (logit p, day/night) fitted on the calibration days only")
args = ap.parse_args()
bands = args.bands.split(",")


def load(folder):
    tests = {r["test"]: r for r in csv.DictReader(open(os.path.join(folder, "calibration_tests.csv")))}
    rows, by_test = [], defaultdict(list)
    for r in csv.DictReader(open(os.path.join(folder, "calibration_skimmers.csv"))):
        t = tests[r["test_id"]]
        row = (r["band"], t["daylight"], float(r["p_theory"]), int(r["heard"]), int(r["started_at"][5:7]))
        rows.append(row)
        by_test[r["test_id"]].append(len(rows) - 1)
    return tests, rows, by_test


def logit(p):
    p = min(max(p, 0.001), 0.999)
    return math.log(p / (1 - p))


def fit(rows):
    """Logistic regression h ~ a + b*logit(p) + c*night: Newton's method with step halving (numpy)."""
    X = np.array([features(d, p, m) for _, d, p, _, m in rows])
    y = np.array([r[3] for r in rows], dtype=float)

    def loglik(w):
        z = np.clip(X @ w, -30, 30)
        return float(np.sum(y * z - np.log1p(np.exp(z))))

    w = np.zeros(X.shape[1])
    w[1] = 1.0
    for _ in range(50):
        q = 1 / (1 + np.exp(-np.clip(X @ w, -30, 30)))
        g = X.T @ (y - q)
        H = (X * (q * (1 - q))[:, None]).T @ X + 1e-9 * np.eye(X.shape[1])
        step = np.linalg.solve(H, g)
        t, base = 1.0, loglik(w)
        while t > 1e-4 and loglik(w + t * step) < base:
            t /= 2
        w = w + t * step
        if np.max(np.abs(t * step)) < 1e-7:
            break
    return [float(x) for x in w]


def features(d, p, m):
    f = [1.0, logit(p), 1.0 if d == "night" else 0.0]
    if args.summer:
        f.append(1.0 if 5 <= m <= 8 else 0.0)
    return f


def apply(w, rows):
    return [(b, d, 1 / (1 + math.exp(-max(-30.0, min(30.0, sum(wi * xi for wi, xi in zip(w, features(d, p, m))))))), h, m) for b, d, p, h, m in rows]


SEASON = {1: "winter", 2: "winter", 12: "winter", 3: "spring", 4: "spring", 5: "spring", 6: "summer", 7: "summer", 8: "summer", 9: "autumn", 10: "autumn", 11: "autumn"}


def margins_of(tests, rows, by_test, band, k=1.0):
    """The app's margin of every test of the band, from the (possibly corrected) probabilities, on k x expected (at least 3)."""
    out = []
    for tid, idx in by_test.items():
        if tests[tid]["band"] != band:
            continue
        expected = k * sum(rows[i][2] for i in idx)
        heard = sum(rows[i][3] for i in idx)
        if expected >= 3:
            out.append((heard - expected) / expected)
    return sorted(out)


def typical_factor(tests, rows, by_test, band):
    """k_b: median of heard / expected over the tests with at least 3 expected."""
    ratios = [m + 1 for m in margins_of(tests, rows, by_test, band)]
    return statistics.median(ratios) if ratios else 1.0


def brier(rows):
    return statistics.fmean((r[2] - r[3]) ** 2 for r in rows) if rows else float("nan")


def best_of(grid_rows):
    """The tool's rule (Score.Best): lowest Brier among |median| <= 10%, else nearest margin, never a missing one."""
    with_m = [r for r in grid_rows if r["median_margin"] not in ("", "NaN") and int(r["with_margin"]) > 0]
    near = [r for r in with_m if abs(float(r["median_margin"])) <= 0.10]
    if near:
        return min(near, key=lambda r: float(r["brier"]))
    if with_m:
        return min(with_m, key=lambda r: abs(float(r["median_margin"])))
    return None


def ssn_of_f107(f107):
    """The CCIR relation F = 63.7 + 0.728 R + 0.00089 R^2, solved for R (0 at or below 63.7): SsnByDay.FromF107 of the tool."""
    if f107 <= 63.7:
        return 0.0
    a, b = 0.00089, 0.728
    return (-b + math.sqrt(b * b + 4 * a * (f107 - 63.7))) / (2 * a)


# Same values as the tool's tests (SsnByDayTests): one formula, checked in both places.
assert ssn_of_f107(63.7) == 0 and abs(ssn_of_f107(150) - 105.05) < 0.005


def write_fixture(path, weights, factors):
    import json
    cases = []
    for band in factors:
        w = weights[band]
        for p, night, summer in [(0.0, False, False), (0.05, False, False), (0.3, False, False), (0.5, True, False), (0.83, False, True), (0.95, True, True)]:
            row = (band, "night" if night else "day", p, 0, 6 if summer else 1)
            cases.append({"band": band, "p_voacap": p, "night": night, "summer": summer, "p_corrected": round(apply(w, [row])[0][2], 6)})
    typical = [{"band": b, "expected": e, "typical_expected": round(factors[b] * e, 6), "has_margin": factors[b] * e >= 3}
               for b in factors for e in (2.0, 4.0, 10.0)]
    ssn = [{"f107_observed_sfu": f, "ssn": round(ssn_of_f107(f), 4)} for f in (60.0, 63.7, 70.0, 100.0, 150.0, 200.0, 250.0)]
    doc = {
        "schema": 1,
        "model": "F71 decision 30: VOACAP corrected per band and typical forecast (phase 5, agent A)",
        "generated_by": "ham_qso_spots/tools/voacap-calibration/analysis/stability.py --fixture",
        "voacap": {"noise_dbw": -135, "required_snr_dbhz": 42, "min_takeoff_deg": 3, "method": 30,
                   "reference_station": {"antenna": "dipole", "height_m": 10, "power_w": 100},
                   "receive_antenna": {"model": "isotropic", "gain_dbi": 0},
                   "p_voacap": "the 'rel' of the reference station (theory) for each listening skimmer"},
        "ssn": {
            "source": "observed 10.7 cm solar flux (F10.7obs, local noon at Penticton), not the adjusted one; NOAA SWPC gives it daily",
            "day": "the UTC day of the start of the test; a day without a value takes the nearest day with one within 3 days (the earlier on a tie), else no forecast",
            "formula": "CCIR: F = 63.7 + 0.728 R + 0.00089 R^2, so R = (-0.728 + sqrt(0.728^2 + 4 * 0.00089 * (F - 63.7))) / (2 * 0.00089); R = 0 when F <= 63.7; not rounded",
        },
        "night": "night = not day; day = local SOLAR time of the transmitting station (the user, its longitude) from 06:00 inclusive to 18:00 exclusive, local solar hour = (UTC hours of the start of the test + longitude / 15) modulo 24",
        "summer": "summer = the UTC month of the start of the test is May, June, July or August, in both hemispheres (the RBN data are almost all northern)",
        "correction": "p_corrected = 1 / (1 + exp(-z)), z = a + b * logit(p) + night_coef * (night ? 1 : 0) + summer_coef * (summer ? 1 : 0), logit(p) = ln(q / (1 - q)) with q = p clamped to [0.001, 0.999]; z clamped to [-30, 30]",
        "typical": "expected = sum of p_corrected over the listening skimmers; typical expected = k * expected; margin = (heard - typical) / typical, only when typical >= 3",
        "bands": {b: {"a": weights[b][0], "b": weights[b][1], "night": weights[b][2], "summer": weights[b][3], "k": factors[b]} for b in factors},
        "other_bands": "no comparison with the forecast (decision 30): 160, 80, 20, 17, 15, 12, 10 m",
        "cases": {"correction": cases, "typical": typical, "ssn_from_f107": ssn},
    }
    with open(path, "w") as f:
        json.dump(doc, f, indent=2)
        f.write("\n")
    print(f"fixture: {path}")


cal_tests, cal, cal_by = load(args.cal)
fin_tests, fin, fin_by = load(args.final)
if args.fixture:
    if not (args.recalibrate and args.summer):
        raise SystemExit("--fixture needs --recalibrate --summer (the approved model)")
    fb = args.fixture_bands.split(",")
    weights = {b: fit([r for r in cal if r[0] == b]) for b in fb}
    corrected = [apply(weights[r[0]], [r])[0] if r[0] in weights else r for r in cal]
    write_fixture(args.fixture, weights, {b: typical_factor(cal_tests, corrected, cal_by, b) for b in fb})
    raise SystemExit(0)
if args.recalibrate:
    weights = {b: fit([r for r in cal if r[0] == b]) for b in bands}
    print("recalibration (fitted on the calibration days): " + "; ".join(f"{b} " + " ".join(f"{x:+.2f}" for x in w) for b, w in weights.items()))
    cal = [apply(weights[r[0]], [r])[0] if r[0] in weights else r for r in cal]
    fin = [apply(weights[r[0]], [r])[0] if r[0] in weights else r for r in fin]

freq = defaultdict(lambda: [0, 0])
for b, d, _, h, _ in cal:
    freq[(b, d)][0] += h
    freq[(b, d)][1] += 1
clim = {k: v[0] / v[1] for k, v in freq.items()}

seasons = []
for folder in [s for s in args.seasons.split(",") if s]:
    grid = [r for r in csv.DictReader(open(open(os.path.join(folder, "grid_path.txt")).read().strip()))]
    seasons.append((os.path.basename(folder.rstrip("/")), grid))

print(f"chosen {args.chosen} dB-Hz; calibration {len(cal_tests)} tests, final {len(fin_tests)} tests\n")
print("| Band | 1. BSS | 2. Brier cal → final | 3. median margin | 4. best per season | 5. tests with margin | 6. reliability | Result |")
print("|---|---|---|---|---|---|---|---|")
details = []
for band in bands:
    c = [r for r in cal if r[0] == band]
    f = [r for r in fin if r[0] == band]
    if not f:
        print(f"| {band} | no final data | | | | | | fail |")
        continue
    bm, bc = brier(f), statistics.fmean((clim.get((b, d), 0) - h) ** 2 for b, d, _, h, _ in f)
    bss = 1 - bm / bc if bc > 0 else float("nan")
    gap = bm - brier(c)
    k = typical_factor(cal_tests, cal, cal_by, band) if args.typical else 1.0
    margins = margins_of(fin_tests, fin, fin_by, band, k)
    med = statistics.median(margins) if margins else float("nan")
    best = []
    for name, grid in seasons:
        r = best_of([g for g in grid if g["group"] == band])
        best.append((name, float(r["required_snr_dbhz"]) if r else None, int(r["with_margin"]) if r else 0))
    by_season = defaultdict(lambda: [0.0, 0])
    for r in f:
        by_season[SEASON[r[4]]][0] += r[2]
        by_season[SEASON[r[4]]][1] += r[3]
    season_bias = {k: v[1] / v[0] - 1 for k, v in by_season.items() if v[0] >= 30}
    known = [v for _, v, _ in best if v is not None]
    spread = max(abs(v - args.chosen) for v in known) if known else float("nan")
    rel_rows, rel_ok = [], True
    for lo, hi in BINS:
        x = [(r[2], r[3]) for r in f if lo <= r[2] < hi]
        if not x:
            rel_rows.append(f"{int(lo*100)}-{int(min(hi,1)*100)}%: —")
            continue
        obs = statistics.fmean(h for _, h in x)
        mp = statistics.fmean(p for p, _ in x)
        ok = len(x) < 50 or (lo - 0.05 <= obs < hi + 0.05)
        rel_ok &= ok
        rel_rows.append(f"{int(lo*100)}-{int(min(hi,1)*100)}%: n {len(x)}, p {mp:.2f}, obs {obs:.2f}{'' if ok else ' ✗'}")
    checks = [bss > 0, gap <= 0.02, abs(med) <= 0.25, spread <= 5, len(margins) >= 30, rel_ok]
    above = {t: sum(m >= t for m in margins) / len(margins) if margins else float("nan") for t in (0.15, 0.25, 0.50)}
    print(f"| {band}{f' (k {k:.2f})' if args.typical else ''} | {bss:+.3f} {'✓' if checks[0] else '✗'} | {brier(c):.3f} → {bm:.3f} ({gap:+.3f}) {'✓' if checks[1] else '✗'} "
          f"| {med:+.0%} {'✓' if checks[2] else '✗'} | {', '.join(f'{n} {v:g}' if v is not None else f'{n} —' for n, v, _ in best)} (±{spread:g}) {'✓' if checks[3] else '✗'} "
          f"| {len(margins)} {'✓' if checks[4] else '✗'} | {'✓' if checks[5] else '✗'} | {'**pass**' if all(checks) else 'fail'} |")
    check4b = bool(season_bias) and all(abs(x) <= 0.25 for x in season_bias.values())
    details.append((band, rel_rows, above, all(checks), season_bias, check4b, all(checks[:3] + [check4b] + checks[4:])))

print("\n4'. Bias of the model season by season on the independent days (heard / expected - 1, within ±25%), and the result with 4' instead of 4:")
for band, _, _, _, bias, ok4b, ok_all in details:
    print(f"- {band}: " + ", ".join(f"{k} {v:+.0%}" for k, v in sorted(bias.items())) + f" {'✓' if ok4b else '✗'} → {'**pass**' if ok_all else 'fail'}")
print("\nReliability (final days): probability band, skimmers, mean probability, observed frequency")
for band, rel, *_ in details:
    print(f"- {band}: " + "; ".join(rel))
print("\nShare of final tests 'above the forecast' (of those with a margin), thresholds +15% / +25% / +50%:")
for band, _, above, ok, *_ in details:
    print(f"- {band}{'' if ok else ' (fails)'}: {above[0.15]:.0%} / {above[0.25]:.0%} / {above[0.5]:.0%}")
