#!/usr/bin/env bash
# F71 phase 5, 160 m: every pass of the tool for 160 m (monthly and daily SSN), each in its own folder under $W.
set -uo pipefail
W="${STABILITY_OUT:?folder for the passes}"
cd "$(dirname "$0")/../../.."
CAL=20260114,20260115,20260116,20260318,20260319,20260320,20260615,20260616,20260617,20261028,20261029,20261030,20261031,20261101,20261102,20261103,20261104,20261105,20261106
WIN=20260114,20260115,20260116; SPR=20260318,20260319,20260320; SUM=20260615,20260616,20260617; AUT=20261028,20261029,20261030,20261031,20261101,20261102,20261103,20261104,20261105,20261106
DEV=20241022,20241023,20241024,20251209,20251210,20251211,20260421,20260422,20260423,20260818,20260819,20260820,20260922,20260923,20260924
FIN=20250210,20250211,20250212,20250512,20250513,20250514,20250714,20250715,20250716,20251117,20251118,20251119
TEN=20241112,20241113,20241114,20250120,20250121,20250122
BANDS=80m,40m,30m,20m,17m,15m,12m,10m
GRID=$(seq 32 2 56 | paste -sd, -)
run() { # name days bands snr [extra args...]
  local name=$1 days=$2 bands=$3 snr=$4; shift 4
  mkdir -p "$W/$name"
  echo "== $name $(date +%T)"
  dotnet run --no-build -c Release --project tools/voacap-calibration/HamQso.Spots.VoacapCalibration -- \
    --days "$days" --bands "$bands" --per-stratum 100000 --noise -135 --snr "$snr" --history-stations 0 \
    --export-path "$W/$name/calibration_tests.csv" "$@" > "$W/$name/out.txt" 2>&1
  grep -E "^Grid:" "$W/$name/out.txt" | sed 's/^Grid: //' > "$W/$name/grid_path.txt"
  grep -E "chosen for the export" "$W/$name/out.txt"
}
BANDS=160m
G=$(seq 30 2 56 | paste -sd, -)
for m in monthly daily; do
  extra=""; [ $m = daily ] && extra="--ssn daily"
  run cal_$m "$CAL" 160m "$G,42" $extra
  best=$(grep -oE "chosen for the export: -135 dBW, [0-9.]+" "$W/cal_$m/out.txt" | grep -oE "[0-9.]+$")
  echo "160m $m best $best"
  run cal42_$m "$CAL" 160m 42 --export -135,42 $extra
  for s in WIN SPR SUM AUT; do run "season_${s}_$m" "${!s}" 160m "$G" $extra; done
  for set in DEV FIN; do
    run "${set}42_$m" "${!set}" 160m 42 --export -135,42 $extra
    run "${set}best_$m" "${!set}" 160m "$best" --export "-135,$best" $extra
  done
done
echo "== done $(date +%T)"
