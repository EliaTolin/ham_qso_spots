#!/usr/bin/env bash
# The whole spots server on this machine, for the web app's end-to-end tests (F58):
# the real public sources (DX cluster telnet, HamQTH, DX Summit, POTA, SOTA, WWFF;
# read-only, as production), Postgres and Mosquitto in Docker, the server with dotnet run.
#
#   scripts/run-local.sh          start; Ctrl+C stops the server (the containers stay up)
#   scripts/run-local.sh down     stop and remove the containers and the local archive
#
# Never with the Supabase secret key, even if user secrets have one: alerts and spot
# sending are off, so a local run never alerts real users and never spots on a cluster.
# Tokens are checked with the public keys of the production project, as in production.
# Ports (override with the environment): LOCAL_PUBLIC_PORT, LOCAL_INTERNAL_PORT,
# LOCAL_POSTGRES_PORT, LOCAL_MQTT_PORT, LOCAL_WS_PORT.
set -euo pipefail
cd "$(dirname "$0")/.."

export LOCAL_PUBLIC_PORT="${LOCAL_PUBLIC_PORT:-18080}"
export LOCAL_INTERNAL_PORT="${LOCAL_INTERNAL_PORT:-18081}"
export LOCAL_POSTGRES_PORT="${LOCAL_POSTGRES_PORT:-55432}"
export LOCAL_MQTT_PORT="${LOCAL_MQTT_PORT:-18883}"
export LOCAL_WS_PORT="${LOCAL_WS_PORT:-19001}"
compose=(docker compose -f deploy/local/compose.yaml)

case "${1:-up}" in
  down) "${compose[@]}" down -v; exit 0 ;;
  up) ;;
  -h|--help) sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
  *) echo "Unknown option: $1 (see --help)" >&2; exit 2 ;;
esac

for port in "$LOCAL_PUBLIC_PORT" "$LOCAL_INTERNAL_PORT"; do
  if lsof -nP -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1; then
    echo "Port $port is in use: free it or set LOCAL_PUBLIC_PORT / LOCAL_INTERNAL_PORT." >&2
    exit 1
  fi
done

"${compose[@]}" up -d --build --wait

cat <<URLS

Spots server (local, Development):
  REST    http://127.0.0.1:${LOCAL_PUBLIC_PORT}/v1/spots?program=dx   (Authorization: Bearer <Supabase token>)
  Health  http://127.0.0.1:${LOCAL_PUBLIC_PORT}/health
  MQTT    ws://127.0.0.1:${LOCAL_WS_PORT}/mqtt   (MQTT over WebSocket, no TLS; password: the Supabase token)
  Also    /v1/dx/spots (off: 503 sending_disabled), /v1/proxy/{service}/..., /v1/rbn/{callsign}
  CORS    http://localhost:* and http://127.0.0.1:* (any port)
Stop: Ctrl+C (server), then scripts/run-local.sh down (containers and local archive).

URLS

export ASPNETCORE_ENVIRONMENT=Development
export Server__PublicPort="$LOCAL_PUBLIC_PORT"
export Server__InternalPort="$LOCAL_INTERNAL_PORT"
export ConnectionStrings__Spots="Host=127.0.0.1;Port=${LOCAL_POSTGRES_PORT};Database=spots;Username=spots;Password=spots"
export Mqtt__Host=127.0.0.1
export Mqtt__Port="$LOCAL_MQTT_PORT"
Mqtt__Password="$(openssl rand -hex 16)"
export Mqtt__Password
# Empty on purpose, over any user secret: see FeatureSwitches.
export Supabase__SecretKey=""
exec dotnet run --project src/HamQso.Spots.Host --no-launch-profile -- --Supabase:SecretKey=
