#!/usr/bin/env bash
# The tests of the VOACAP service with the real voacapl, in Docker (voacapl is not on macOS).
#   voacap/test-engine.sh
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
docker build --progress=plain --target engine-test -f "$root/voacap/Dockerfile" "$root"
