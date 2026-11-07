#!/usr/bin/env python3
"""Builds src/HamQso.Spots.Domain/Data/dxcc_centers.json (DXCC -> centre of the
entity, the lat/lon of the app's cty.csv, east positive) from the site's
data/dxcc.json, the same file build_dxcc_prefixes.mjs of the backend reads.
F71: the position of a skimmer that is not in the RBN node list.

  python3 scripts/build_dxcc_centers.py [path/to/ham_qso_website/data/dxcc.json]
"""
import json
import sys
from pathlib import Path

root = Path(__file__).resolve().parent.parent
src = Path(sys.argv[1]) if len(sys.argv) > 1 else root / "../ham_qso_website/data/dxcc.json"
out = root / "src/HamQso.Spots.Domain/Data/dxcc_centers.json"

data = json.loads(src.read_text(encoding="utf-8"))
centers = {}
for e in data["entities"]:
    d, lat, lon = e.get("dxcc"), e.get("lat"), e.get("lon")
    if not isinstance(d, int) or d <= 0 or lat is None or lon is None:
        continue
    if not (-90 <= lat <= 90 and -180 <= lon <= 180):
        raise SystemExit(f"dxcc {d}: bad centre {lat},{lon}")
    centers.setdefault(str(d), [round(lat, 2), round(lon, 2)])

meta = data.get("meta", {})
out.write_text(json.dumps({
    "meta": {"source": meta.get("source", "dxcc.json"), "sourceGeneratedAt": meta.get("generatedAt")},
    "centers": dict(sorted(centers.items(), key=lambda kv: int(kv[0]))),
}, separators=(",", ":")) + "\n", encoding="utf-8")
print(f"{len(centers)} entities -> {out}")
