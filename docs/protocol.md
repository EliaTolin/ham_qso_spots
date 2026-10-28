# Client protocol

How an app (or any client) gets spots from the spots server, and how a client without telnet sends one ([`POST /v1/dx/spots`](#rest-post-v1dxspots-sending-a-dx-spot)). Two channels, one message shape:

- **REST** `GET https://spot.hamqso.app/v1/spots` gives the starting list, or what changed since the last spot the client saw.
- **MQTT** `wss://spot.hamqso.app/mqtt` pushes the news live, as soon as a spot is saved.

There is no "remove" and no expiry: a spot is shown with its age ("5 min ago") from `spot_time`; hiding old spots is a choice of the client.

All examples below are real answers of the production server (27/10/2026; the POTA `spot_key` is shown in the activation form introduced the same evening).

## Authentication

Only users of the HamQSO apps: every request carries the user's **Supabase access token** (the `access_token` of the Supabase session, anonymous users included). Tokens of the HamQSO project and of the reader projects (`Supabase:ReaderUrls`: HamQRG) are accepted, each checked with the keys of its own project.

| Channel | Where the token goes |
|---|---|
| REST | `Authorization: Bearer <access_token>` |
| MQTT | password of the MQTT connection; the username can be anything (use the user id) |

The token lasts about one hour. The REST API checks it on every request; the broker checks it **only when connecting**, so a connection stays open after the token expires, but a reconnection needs a fresh token (the Supabase client refreshes it on its own).

## The spot

Every spot, in REST answers and MQTT messages, is a JSON object with these fields (snake_case, as the columns of the archive). **Fields without a value are left out**, not sent as `null`: treat every optional field as possibly missing.

| Field | Type | Always | Meaning |
|---|---|---|---|
| `program` | string | yes | `dx`, `pota`, `sota` or `wwff` |
| `seq` | integer | yes | Order of the save on the server: grows at every new **or updated** spot, across all programs (see [seq](#seq)) |
| `spot_key` | string | yes | Identity of the spot within its program. DX: `CALL_kHz_yyyyMMddHHmm` (`SQ8V_3573.0_202610271853`); xOTA: the activation on its band, `CALL_REFERENCE_BAND` (`K4RCY_US-2907_20m`): one row per activation and band, updated by every new spot of it |
| `callsign` | string | yes | The station spotted, uppercase |
| `frequency_khz` | number | yes | Frequency in kHz (`14025`, `21074.5`) |
| `spot_time` | string | yes | ISO 8601 UTC time of the spot (`2026-10-27T18:53:00+00:00`) |
| `feeds` | string[] | yes | The sources that delivered it: telnet node names (`DXFun`, `IZ8MBW-6`…), `HamQTH`, `DXSummit`, `POTA`, `SOTA`, `WWFF` |
| `spotters` | string[] | yes (may be empty) | Who spotted it; an RBN skimmer ends with `-#` (`GM0UDL-#`) |
| `mode` | string | yes | `CW`, `FT8`, `USB`… or `""` when unknown |
| `band` | string | no | `160m` … `23cm`; missing outside the amateur bands |
| `mode_source` | string | no | `reported` (by the source, the comment or a skimmer) or `band_plan` (guessed from the frequency); missing when `mode` is `""` |
| `mode_category` | string | no | `cw`, `phone` or `digital`; missing when unknown |
| `reference` | string | no | xOTA reference: `US-2907` (POTA), `HB/SO-023` (SOTA), `HBFF-0040` (WWFF) |
| `reference_name` | string | no | Name of the park (POTA, WWFF) or of the summit (SOTA) |
| `activator_name` | string | no | Name of the activator (SOTA) |
| `altitude_m` | integer | no | Altitude of the summit in metres (SOTA) |
| `points` | integer | no | Activation points of the summit (SOTA) |
| `spot_count` | integer | yes | How many times the activation was spotted on this band (xOTA, counted by the server); `1` for DX |
| `qrt` | boolean | yes | The activator went QRT (SOTA `QRT` spot, B29): the apps end the whole activation, on every band. A QRT without a frequency has `frequency_khz` `0` and no `band` |
| `lotw`, `eqsl` | boolean | no | The station confirms on LoTW / eQSL (from HamQTH; missing when no source tells) |
| `location` | string | no | POTA location of the park (`US-SC`) |
| `dxcc` | integer | no | ADIF DXCC entity of the station |
| `continent` | string | no | `AF`, `AN`, `AS`, `EU`, `NA`, `OC`, `SA` |
| `latitude`, `longitude` | number | no | Position of the reference (xOTA) |
| `comment` | string | no | Comment of the spotter |
| `spotter_locator` | string | no | Locator of the spotter (some telnet nodes send it) |

A DX spot (several sources merged into one):

```json
{
  "program": "dx",
  "seq": 7936,
  "spot_key": "C91RU_21074.5_202610271852",
  "callsign": "C91RU",
  "frequency_khz": 21074.5,
  "spot_time": "2026-10-27T18:52:00+00:00",
  "feeds": ["IK5PWJ-6", "IZ8MBW-6", "DXFun", "HamQTH"],
  "spotters": ["KC9EI"],
  "band": "15m",
  "mode": "FT8",
  "mode_source": "band_plan",
  "mode_category": "digital",
  "dxcc": 181,
  "continent": "AF"
}
```

A POTA spot:

```json
{
  "program": "pota",
  "seq": 7919,
  "spot_key": "K4RCY_US-2907_20m",
  "callsign": "K4RCY",
  "frequency_khz": 14080,
  "spot_time": "2026-10-27T18:52:38+00:00",
  "feeds": ["POTA"],
  "spotters": ["GM0UDL-#"],
  "band": "20m",
  "mode": "FT4",
  "mode_source": "reported",
  "mode_category": "digital",
  "reference": "US-2907",
  "dxcc": 291,
  "continent": "NA",
  "latitude": 33.6487,
  "longitude": -78.938,
  "comment": "RBN 7 dB via GM0UDL-#",
  "location": "US-SC"
}
```

The same spot comes again, with a **new `seq`**, when it changes. DX: another source reports it (a new entry in `feeds`, maybe a reported mode instead of a band plan guess). xOTA: the activation is spotted again on the same band (new frequency, time, comment). Clients replace a spot they already have by `program` + `spot_key`. A spot identical to the one already saved, or older, never comes again.

## The message

REST answers and MQTT messages have the same shape:

```json
{ "seq": 7941, "full": true, "spots": [ { … }, { … } ] }
```

| Field | Meaning |
|---|---|
| `seq` | REST: the highest seq of the server when the answer was read (all programs). MQTT: the highest seq among the spots of the message |
| `full` | `true`: `spots` is the whole current list for the request, replace yours. `false`: only what changed, merge it into yours |
| `spots` | Spots, see above |

## seq

- One counter for the whole server, given by the database at every save. A spot updated later gets a higher seq than its first one.
- A client filtering by program, band or mode **sees gaps** in the numbers: the missing seqs belong to other spots. Gaps mean nothing.
- Keep the highest seq you have applied; use it as `since_seq` after a disconnection.

## REST: `GET /v1/spots`

```
GET https://spot.hamqso.app/v1/spots?program=dx&band=20m&band=40m&mode=cw&limit=200&since_seq=7900
Authorization: Bearer <access_token>
```

| Parameter | Required | Values |
|---|---|---|
| `program` | yes | `dx`, `pota`, `sota`, `wwff` |
| `band` | no | A band (`160m`, `80m`, `60m`, `40m`, `30m`, `20m`, `17m`, `15m`, `12m`, `10m`, `6m`, `4m`, `2m`, `70cm`, `23cm`) or `other` (no band). Repeat it or separate with commas. None = all |
| `mode` | no | `cw`, `phone`, `digital` or `other` (unknown). Repeat it or separate with commas. None = all |
| `limit` | no | A whole number; default and maximum 200 (above 200 counts as 200, 0 as 1); a negative or non-numeric value is a `400` |
| `since_seq` | no | The highest seq the client has applied |

What comes back:

- **Without `since_seq`**: the newest spots of the filter by `spot_time`, at most `limit`, newest first; `full: true`.
- **With `since_seq`**: the spots of the filter saved after it, oldest seq first; `full: false`. If they are more than `limit`, the answer is the current list instead (as without `since_seq`), with `full: true`.

No compression is used.

A starting list (`?program=dx&limit=2`) gives `{"seq": 7941, "full": true, "spots": [ …2 spots… ]}`; asking `?program=dx&since_seq=7938` right after gave only the DX spot with seq 7941 (`full: false`): 7939 and 7940 were POTA spots.

### Errors

| Status | When | Body |
|---|---|---|
| `401` | No `Authorization: Bearer`, or the token is not a valid Supabase token of HamQSO or of a reader project (expired, forged, other project) | empty |
| `400` | A parameter is wrong | `{"error": "unknown band '11m'"}` (says which one) |

## MQTT: the news

| | |
|---|---|
| URL | `wss://spot.hamqso.app/mqtt` (MQTT over WebSocket, TLS, port 443; path `/mqtt`) |
| Credentials | username: anything; password: the Supabase access token |
| QoS | 1 |
| Retained | no: a client that subscribes gets only what is published after |

Topics:

| Topic | Spots |
|---|---|
| `spots/dx/<band>/<mode>` | DX spots of that band and mode. `<band>`: the bands above or `other`; `<mode>`: `cw`, `phone`, `digital`, `other` |
| `spots/pota`, `spots/sota`, `spots/wwff` | Activity spots of that program, all bands |

Wildcards work: `spots/dx/#` (all DX), `spots/dx/20m/#` (20m, any mode), `spots/dx/+/cw` (CW, any band). Only `spots/…` can be read; nothing can be published by clients.

A message on `spots/dx/80m/digital`:

```json
{
  "seq": 8010,
  "full": false,
  "spots": [
    {
      "program": "dx",
      "seq": 8010,
      "spot_key": "SQ8V_3573.0_202610271853",
      "callsign": "SQ8V",
      "frequency_khz": 3573,
      "spot_time": "2026-10-27T18:53:00+00:00",
      "feeds": ["DA0BCC-7", "IK5PWJ-6", "IZ8MBW-6", "DXFun", "HamQTH", "DXSummit"],
      "spotters": ["SP7ROM"],
      "band": "80m",
      "mode": "FT8",
      "mode_source": "reported",
      "mode_category": "digital",
      "dxcc": 269,
      "continent": "EU",
      "comment": "FT8 +00dB 627Hz"
    }
  ]
}
```

A connection with a missing or invalid token is refused (the broker does not return success); a publish by a client is refused (MQTT 5 `NotAuthorized`); subscribing outside `spots/…` (`#`, `$SYS/#`) is refused.

## The client algorithm

```
on open (and on every reconnection):
  1. connect MQTT with a fresh token, subscribe to the topics you show
     → put incoming messages aside (do not apply them yet)
  2. GET /v1/spots for the same filter
       first time:      no since_seq            → replace your list (full: true)
       after reconnect: since_seq = last applied → merge, or replace if full: true
  3. apply the messages put aside whose spots have seq > the seq of step 2, then apply new messages as they come
  4. keep the highest seq applied

apply a message:  for each spot, insert or replace by (program, spot_key)
show:             newest spot_time first; age from spot_time; trim your list as you like (the server returns at most 200 per answer)
```

Notes:

- Subscribe **before** asking the list: a spot saved between the two would otherwise be lost.
- When the filter changes (other bands or modes), subscribe to the new topics and ask the list for the new filter.
- Reconnect with a growing delay (the broker restarts when the server is deployed).
- If the broker or the API cannot be reached, the client decides its fallback (for example the DX cluster by telnet, as the app did before).

## REST: `POST /v1/dx/spots` (sending a DX spot)

For clients that cannot open telnet (the web app): the server sends the spot to the DX cluster node for the user, as the phone app does by itself (`OnDemandTelnetSender`). Only **signed-in HamQSO** users: users of the reader projects (HamQRG) may only read, and anonymous users (token claim `is_anonymous: true`) must sign in first.

```
POST https://spot.hamqso.app/v1/dx/spots
Authorization: Bearer <access_token>
Content-Type: application/json

{"line": "DX 14025.0 K1ABC CQ CW", "ssid": 7}
```

| Field | Required | Value |
|---|---|---|
| `line` | yes | One DX command, as the app builds it: `DX <kHz with one decimal> <callsign> <text>`; the text may be missing (`DX 14025.0 K1ABC`). Exactly one space between the parts, at most 120 characters, **printable ASCII only** (`0x20`–`0x7E`: no control characters, no newline, no accented letters, no telnet bytes). Nothing else: any other cluster command is refused |
| `ssid` | yes | The SSID of the install, `1`–`99` |

What the server does:

- Logs in to the node (DXFun, `dxfun.com:8000`, as the app) as **`<callsign of the user's profile>-<ssid>`** (`IU4VRB-7`). The callsign is read from the user's Supabase profile (`public.profiles.callsign`, by the token's user id), never from the request, and checked again against the profile pattern (`^[A-Z0-9]{1,8}(/[A-Z0-9]{1,8}){0,2}$`); a callsign that does not match counts as missing (`422`).
- Keeps **one session per user**: the first spot opens it (connection, login, welcome; at most 15 s), the next ones reuse it, and it closes **3 minutes after the last spot**, like the app. A spot with another SSID closes the user's session and opens one with the new login. An empty password is sent if the node asks for one, as the app.
- At most **50 sessions** open at once on the server: a new one beyond that closes the session used least recently that is not sending at that moment; only when all 50 are sending the answer is `503 busy`.
- Once the spot has started (login and send), it finishes even if the client drops the request: the session then closes on its idle time as usual.
- Sends `line`, then collects what the node writes back for 2 seconds: those lines are `replies`. Spots broadcast by the node (including the echo of this one, `DX de IU4VRB-7: …`) are not replies.
- **The confirmation is not in the answer**: as on the phone, the client sees its spot come back in the spot list (MQTT/REST, from the server's telnet sources) when the node broadcasts it. A refusal of the node shows in `replies` (`"Duplicate spot"`, an error text).

Answer `200`:

```json
{"sent": true, "replies": ["Duplicate spot"]}
```

### Errors

| Status | Body | When |
|---|---|---|
| `400` | `{"error": "invalid_line"}` | `line` missing or not one valid DX command; also a body that is not JSON or is over 4 KB |
| `400` | `{"error": "invalid_ssid"}` | `ssid` missing or outside 1–99 |
| `401` | empty | No `Authorization: Bearer`, or not a valid Supabase token |
| `403` | `{"error": "not_allowed"}` | A valid token of a reader project (HamQRG) |
| `403` | `{"error": "account_required"}` | A valid token of an anonymous user: ask the user to sign in |
| `422` | `{"error": "no_callsign"}` | The user's profile has no callsign, or one outside the profile pattern (or the user has no profile): ask the user to set it |
| `429` | `{"error": "rate_limited", "retry_after_s": 42}` + header `Retry-After: 42` | More than **3 spots a minute** or **20 an hour** for this user (sliding windows). Every request with a valid line and SSID counts, also one that then ends in `422` or `502`. There is no limit per callsign: profile callsigns are not verified, so anyone could use up someone else's |
| `502` | `{"error": "node_unavailable"}` | The node cannot be reached, or the login is not over within 15 s, or the connection drops while sending |
| `503` | `{"error": "busy"}` | The server is full: more than **60 spots a minute from all users together**, or 50 sessions open and all sending at that moment. Try again later (no `Retry-After`) |

The order of the checks: token (401), reader or anonymous (403), line and SSID (400), user limit (429), server limit (503), profile (422), sessions (503), node (502). A request refused by the server limit does not count for the user. Every request that gets past the token is logged on the server (user id, login, line, outcome).

## Limits and attribution

- At most 200 spots per answer. No rate limit on the lists yet: be gentle (one list per open or reconnection, not polling). Sending spots is limited (above).
- HamQTH asks to show "Data provided by HamQTH.com" where its data is shown.
- Tested capacity (27/10/2026): up to 500 clients connected at once with news delivered to all within 0.9 s (95th percentile); about 900 is the current limit.
