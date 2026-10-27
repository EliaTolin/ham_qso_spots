#!/usr/bin/env bash
# All tests with line coverage (needs Docker for Postgres and Mosquitto).
# Fails when the total is under 90% or an assembly under 85%.
set -euo pipefail
cd "$(dirname "$0")/.."
out=$(mktemp -d)
dotnet test --coverage --coverage-output-format cobertura --results-directory "$out"
python3 scripts/coverage_report.py "$out"/merged/*.cobertura.xml "$@"
