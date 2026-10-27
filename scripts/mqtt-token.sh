#!/usr/bin/env bash
# Supabase access token to log in to the spots broker by hand (MQTT Explorer,
# mosquitto_sub, ...): the broker takes it as password.
#
#   scripts/mqtt-token.sh            print a fresh token
#   scripts/mqtt-token.sh --copy     also copy it to the clipboard (macOS)
#   scripts/mqtt-token.sh --reset    forget the saved session (next run makes a new user)
#
# Always the same anonymous user: it is created on the first run, then its
# session is renewed with the refresh token, so no new user piles up in Auth.
# The session is kept outside the repo, in $SESSION (mode 600).
# Needs curl, python3 and the publishable key: SUPABASE_PUBLISHABLE_KEY, or
# the Supabase CLI logged in (the key is read from the project).
set -euo pipefail

PROJECT_REF="mpyvdejoqfgsupiqtkcp"
SUPABASE_URL="https://${PROJECT_REF}.supabase.co"
SESSION="${HOME}/.config/hamqso/mqtt-dev-session.json"

copy=false
for arg in "$@"; do
  case "$arg" in
    --copy) copy=true ;;
    --reset) rm -f "$SESSION"; echo "Saved session removed: the next run creates a new anonymous user." >&2; exit 0 ;;
    -h|--help) sed -n '2,13p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $arg (see --help)" >&2; exit 2 ;;
  esac
done

key="${SUPABASE_PUBLISHABLE_KEY:-}"
if [[ -z "$key" ]]; then
  # From a temporary folder: the CLI leaves a supabase/.temp cache where it runs.
  key=$(cd "$(mktemp -d)" && supabase projects api-keys --project-ref "$PROJECT_REF" -o json 2>/dev/null \
    | python3 -c "import json,sys; print(next(k['api_key'] for k in json.load(sys.stdin) if k.get('type')=='publishable'))") \
    || { echo "No publishable key: set SUPABASE_PUBLISHABLE_KEY or log in with the Supabase CLI." >&2; exit 1; }
fi

# Prints the access token of an Auth response and saves its session; fails on an error response.
save_session() {
  python3 -c '
import json, os, sys
d = json.load(sys.stdin)
if "access_token" not in d or "refresh_token" not in d:
    sys.exit("Supabase Auth: " + (d.get("msg") or d.get("error_description") or d.get("message") or str(d)))
path = sys.argv[1]
os.makedirs(os.path.dirname(path), mode=0o700, exist_ok=True)
fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
with os.fdopen(fd, "w") as f:
    json.dump({"user_id": d["user"]["id"], "refresh_token": d["refresh_token"]}, f)
print(d["access_token"])
' "$SESSION"
}

auth() {
  curl -sS -m 20 -X POST "${SUPABASE_URL}/auth/v1/$1" -H "apikey: ${key}" -H "Content-Type: application/json" -d "$2"
}

token=""
if [[ -f "$SESSION" ]]; then
  refresh=$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['refresh_token'])" "$SESSION")
  body=$(python3 -c "import json,sys; print(json.dumps({'refresh_token': sys.argv[1]}))" "$refresh")
  # An expired or revoked session: fall back to a new user.
  token=$(auth "token?grant_type=refresh_token" "$body" | save_session 2>/dev/null) || token=""
fi
if [[ -z "$token" ]]; then
  token=$(auth "signup" '{}' | save_session)
  echo "New anonymous user created (saved in $SESSION)." >&2
fi

user_id=$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['user_id'])" "$SESSION")
cat >&2 <<EOF

MQTT Explorer:  ws:// + Encryption (tls) on, host spot.hamqso.app, port 443, basepath mqtt
                username ${user_id}, password = the token below (valid about 1 hour)
                ADVANCED: remove "#" and "\$SYS/#", add "spots/#"

EOF
echo "$token"
if $copy; then
  printf '%s' "$token" | pbcopy
  echo "(copied to the clipboard)" >&2
fi
