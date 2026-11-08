#!/usr/bin/env python3
"""F71 decision 30: the CSVs for the app with the approved model, read from forecast_model_cases.json.

  python3 export_model.py <tool export folder> <forecast_model_cases.json> <output folder>

The tool exports VOACAP's probability (p_theory) with the daily SSN. Here, on the bands of the model
(40 m, 30 m), p_theory becomes the corrected probability, the test gets expected (sum), the typical
expected (k x expected), the margin on the typical expected (only when >= 3) and the surprises
(heard with p < 0.3). Other bands: no comparison (comparable = 0), p_theory and those columns empty.
The model's rules are applied exactly as the fixture says; its cases are checked first.
"""
import csv
import json
import math
import os
import sys

src, fixture_path, out = sys.argv[1], sys.argv[2], sys.argv[3]
model = json.load(open(fixture_path))
bands = model["bands"]


def logit(p):
    q = min(max(p, 0.001), 0.999)
    return math.log(q / (1 - q))


def corrected(band, p, night, summer):
    c = bands[band]
    z = c["a"] + c["b"] * logit(p) + c["night"] * (1 if night else 0) + c["summer"] * (1 if summer else 0)
    return 1 / (1 + math.exp(-max(-30, min(30, z))))


for case in model["cases"]["correction"]:
    assert abs(corrected(case["band"], case["p_voacap"], case["night"], case["summer"]) - case["p_corrected"]) < 1e-6, case


def lon_of(locator):
    """Centre of a 6-character locator (the node's: the same position the tool uses)."""
    l = locator.upper()
    return -180 + (ord(l[0]) - 65) * 20 + int(l[2]) * 2 + (ord(l[4]) - 65) * (5 / 60) + 2.5 / 60


def is_night(started_at, locator):
    hour = int(started_at[11:13]) + int(started_at[14:16]) / 60 + int(started_at[17:19]) / 3600
    local = ((hour + lon_of(locator) / 15) % 24 + 24) % 24
    return not (6 <= local < 18)


def is_summer(started_at):
    return 5 <= int(started_at[5:7]) <= 8


def convert(skimmers_in, skimmers_out):
    """Rewrites a per-skimmer file; returns, per test, (band, expected, heard, surprises) of the comparable tests."""
    per_test = {}
    with open(skimmers_in) as f, open(skimmers_out, "w", newline="") as g:
        r = csv.DictReader(f)
        w = csv.DictWriter(g, fieldnames=r.fieldnames + ["comparable"])
        w.writeheader()
        for row in r:
            band = row["band"]
            if band in bands:
                p = corrected(band, float(row["p_theory"]), is_night(row["started_at"], row["locator"]), is_summer(row["started_at"]))
                row["p_theory"] = f"{p:.6f}"
                row["comparable"] = 1
                t = per_test.setdefault(row["test_id"], [band, 0.0, 0, 0, row["started_at"], row["locator"]])
                t[1] += p
                t[2] += int(row["heard"])
                t[3] += 1 if int(row["heard"]) and p < 0.3 else 0
            else:
                row["p_theory"] = ""
                row["comparable"] = 0
            w.writerow(row)
    return per_test


os.makedirs(out, exist_ok=True)
tests = convert(os.path.join(src, "calibration_skimmers.csv"), os.path.join(out, "calibration_skimmers.csv"))
convert(os.path.join(src, "calibration_history_skimmers.csv"), os.path.join(out, "calibration_history_skimmers.csv"))

checked = 0
with open(os.path.join(src, "calibration_tests.csv")) as f, open(os.path.join(out, "calibration_tests.csv"), "w", newline="") as g:
    r = csv.DictReader(f)
    names = [n for n in r.fieldnames if n != "margin"] + ["expected_typical", "margin", "comparable"]
    w = csv.DictWriter(g, fieldnames=names)
    w.writeheader()
    for row in r:
        t = tests.get(row["test"])
        if t:
            band, expected, heard, surprises, started, locator = t
            # The tool's day/night (from the station's position) and the fixture's rule must agree.
            assert (row["daylight"] == "night") == is_night(started, locator), row
            checked += 1
            typical = bands[band]["k"] * expected
            row.update({"expected_theory": f"{expected:.4f}", "expected_typical": f"{typical:.4f}",
                        "margin": f"{(heard - typical) / typical:.4f}" if typical >= 3 else "",
                        "surprises": surprises, "comparable": 1})
        else:
            row.pop("margin", None)
            row.update({"expected_theory": "", "expected_typical": "", "margin": "", "surprises": "", "comparable": 0})
        w.writerow(row)
print(f"{checked} comparable tests, day/night checked against the tool")
