"""Line coverage of the production code from a merged Cobertura file.

Generated code (obj/: regex and logging source generators) and the test and
tool projects are left out. Exit code 1 when the total is under --total or an
assembly under --assembly (percent).
"""
import argparse
import collections
import sys
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument("cobertura")
parser.add_argument("--total", type=float, default=90.0)
parser.add_argument("--assembly", type=float, default=85.0)
args = parser.parse_args()

lines = collections.defaultdict(dict)
for package in ET.parse(args.cobertura).getroot().iter("package"):
    name = package.get("name")
    if not name.startswith("HamQso.Spots.") or name.endswith(".Tests") or "LoadTest" in name:
        continue
    for cls in package.iter("class"):
        path = cls.get("filename")
        if "/obj/" in path:
            continue
        for line in cls.iter("line"):
            key = (name, path, int(line.get("number")))
            lines[name][key] = max(lines[name].get(key, 0), int(line.get("hits")))

failed = False
total_covered = total_lines = 0
for name in sorted(lines):
    hits = lines[name].values()
    covered = sum(1 for h in hits if h > 0)
    total_covered += covered
    total_lines += len(hits)
    pct = 100 * covered / len(hits)
    low = pct < args.assembly
    failed |= low
    print(f"{name:32} {covered:5}/{len(hits):<5} {pct:5.1f}%{'  < ' + str(args.assembly) + '%' if low else ''}")
total = 100 * total_covered / total_lines
failed |= total < args.total
print(f"{'Total':32} {total_covered:5}/{total_lines:<5} {total:5.1f}%{'  < ' + str(args.total) + '%' if total < args.total else ''}")
sys.exit(1 if failed else 0)
