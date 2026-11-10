#!/usr/bin/env bash
# F71 phase 5, enlarged calibration (rule in the F71 note): calibration days, each season alone, the new final days.
#   STABILITY_OUT=<folder> CAL_DAYS=<yyyyMMdd,...> FINAL_DAYS=<yyyyMMdd,...> run-big.sh
set -uo pipefail
W="${STABILITY_OUT:?folder for the passes}"
cd "$(dirname "$0")/../../.."
BANDS=160m,80m,40m,30m,20m,17m,15m,12m,10m
GRID=$(seq 30 2 56 | paste -sd, -)
run() { # name days snr [extra args...]
  local name=$1 days=$2 snr=$3; shift 3
  mkdir -p "$W/$name"
  echo "== $name $(date +%T)"
  dotnet run --no-build -c Release --project tools/voacap-calibration/HamQso.Spots.VoacapCalibration -- \
    --days "$days" --bands "$BANDS" --per-stratum 100000 --noise -135 --snr "$snr" --history-stations 0 --ssn daily \
    --export-path "$W/$name/calibration_tests.csv" "$@" > "$W/$name/out.txt" 2>&1
  grep -E "^Grid:" "$W/$name/out.txt" | sed 's/^Grid: //' > "$W/$name/grid_path.txt"
  grep -E "tests of" "$W/$name/out.txt"
}
season_days() { # winter|spring|summer|autumn
  python3 -c "
import sys
m={'winter':(12,1,2),'spring':(3,4,5),'summer':(6,7,8),'autumn':(9,10,11)}['$1']
print(','.join(d for d in '$CAL_DAYS'.split(',') if int(d[4:6]) in m))"
}
run cal "$CAL_DAYS" 42 --export -135,42
for s in winter spring summer autumn; do run "season_$s" "$(season_days $s)" "$GRID"; done
echo "== calibration done $(date +%T); the final days are run only by: FINAL=1"
if [ "${FINAL:-0}" = 1 ]; then run final "$FINAL_DAYS" 42 --export -135,42; fi
echo "== done $(date +%T)"
