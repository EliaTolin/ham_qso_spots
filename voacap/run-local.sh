#!/usr/bin/env bash
# The VOACAP service on this machine, in Docker: http://127.0.0.1:18090 with a random token (printed).
#   voacap/run-local.sh            start (the token is in voacap/.local-token, git-ignored)
#   voacap/run-local.sh down       stop
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
name=hamqso-voacap-local
if [[ "${1:-up}" == down ]]; then docker rm -f "$name" >/dev/null 2>&1 || true; exit 0; fi
token_file="$root/voacap/.local-token"
[[ -s "$token_file" ]] || openssl rand -hex 24 > "$token_file"
docker build -q -t hamqso-voacap -f "$root/voacap/Dockerfile" "$root" >/dev/null
docker rm -f "$name" >/dev/null 2>&1 || true
docker run -d --name "$name" -p 127.0.0.1:18090:8080 -e VOACAP_TOKEN="$(cat "$token_file")" hamqso-voacap >/dev/null
echo "VOACAP on http://127.0.0.1:18090 (token: $(cat "$token_file"))"
