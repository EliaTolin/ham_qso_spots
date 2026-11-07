# Client protocol

How an app (or any client) gets spots from the spots server, how a client without telnet sends one ([`POST /v1/dx/spots`](#rest-post-v1dxspots-sending-a-dx-spot)), how the web app reaches services that send no CORS headers ([the proxy](#rest-the-proxy-v1proxy)), and how it hears the Reverse Beacon Network for the coverage test ([`GET /v1/rbn`](#stream-get-v1rbncallsign-the-coverage-test), and [who is listening](#rest-get-v1rbnskimmers-who-is-listening-f71)). Two channels, one message shape:

- **REST** `GET https://spot.hamqso.app/v1/spots` gives the starting list, or what changed since the last spot the client saw.
- **MQTT** `wss://spot.hamqso.app/mqtt` pushes the news live, as soon as a spot is saved.

[`GET /v1/spots/on-frequency`](#rest-get-v1spotson-frequency-who-is-on-this-frequency) answers one question: who was spotted on this frequency in the last minutes.

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

## REST: `GET /v1/spots/on-frequency` (who is on this frequency)

F88. The station a frequency written in the log, or the one of the radio, suggests: one spot or none, from every program. Same authentication as `/v1/spots`, anonymous users included.

```
GET https://spot.hamqso.app/v1/spots/on-frequency?khz=14285.0
Authorization: Bearer <access_token>
```

| Parameter | Required | Values |
|---|---|---|
| `khz` | yes | The frequency in kHz, a positive number with `.` as decimal point (`14285`, `14285.5`) |

Answer `200`, the spot in the [same shape](#the-spot) as in the lists, or `null` when there is none. `seq` and `full` are not in this answer:

```json
{ "spot": { "program": "pota", "seq": 7919, "callsign": "K4RCY", "frequency_khz": 14080, … } }
```

```json
{ "spot": null }
```

Which spot (`SpotOnFrequency` in the Domain):

- **Candidates**: DX, POTA, SOTA and WWFF spots with `spot_time` in the last 10 minutes (exactly 10 minutes ago still counts) or up to 5 minutes ahead of the server's clock (exactly 5 still counts, later is left out: clocks differ, the same margin as for telnet lines), a frequency above zero, not QRT.
- **Not QRT**: `qrt` false and no `QRT` as a word in the `comment`, in any program and in any case (`qrt off to next peak` is a QRT, `QRTX` is not). The same rule as the app's `commentSaysQrt`; shared cases in `on_frequency_cases.json`.
- **On the frequency**, by the mode of the spot: AM and FM ±3 kHz, CW ±0.5, phone ±1.5, digital and everything else (no mode too) ±1. A spot of mode FT8, FT4 or JS8 inside a calling window of that mode is compared on the **calling frequency**, ±0.1 kHz: a FT8 spot at 14075.8 answers `khz=14074.0`, not `khz=14075.8`. The same tolerances as the app's `tunedSpot`; shared cases in `on_frequency_cases.json`.
- **The most recent wins**, not the nearest (unlike `tunedSpot`). With the same `spot_time` (DX times have only the minute): the nearest, then the highest `seq`, then `program` and `spot_key` in byte order, so the answer does not depend on the order of the rows.

The 10 minutes count when the request arrives; the client decides how long it shows the answer. The frequency asked is not kept and not logged.

Known limit: every spot is judged alone, on what it says, and only spots near the frequency are read. A QRT spot on another frequency does not end the station here: unlike the lists, where the apps close the whole activation (B29), its older spot on the frequency asked can still be the answer, until it is 10 minutes old. Accepted (Elia, 06/11/2026): no second read of the archive.

### Errors

| Status | When | Body |
|---|---|---|
| `401` | As for `/v1/spots` (checked first) | empty |
| `400` | `khz` missing, not a plain number (`abc`, `14285,5`, `1e5`, `NaN`, repeated) or not positive | `{"error": "khz must be a positive number"}` |

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
      "feeds": ["IK5PWJ-6", "IZ8MBW-6", "DXFun", "HamQTH", "DXSummit"],
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
- A login belongs to **one user at a time**: while another user's session has it (until 3 minutes after that user's last spot), the answer is the generic `503 busy` (the server does not tell that the login is in use).
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
| `503` | `{"error": "sending_disabled"}` | Spot sending is off on this server (a local run without the secret key) |
| `503` | `{"error": "busy"}` | Try again later (no `Retry-After`). The server is full: more than **60 spots a minute from all users together** (only spots that passed every other check count), or 50 sessions open and all sending at that moment; or the login is held by another user. It counts against the user's own limit like any attempt past it |

The order of the checks: token (401), reader or anonymous (403), line and SSID (400), user limit (429), profile (422), server limit (503, checked last so failed or refused attempts never use it), login of another user (503), sessions (503), node (502). Every request that gets past the token is logged on the server (user id, login, line, outcome).

## REST: the proxy (`/v1/proxy`)

QRZ Logbook, eQSL, HRDLog, SOTA and a few other services the app uses send no CORS headers, so a browser cannot call them. The web app calls them **through the server**, which passes the request on and the answer back, only to the services of a fixed list (F58 decision 16):

```
POST https://spot.hamqso.app/v1/proxy/qrz-logbook/api
X-HamQSO-Auth: Bearer <Supabase access_token>
Authorization: ...                         (for the service, if it wants one: passed on untouched)
Content-Type: application/x-www-form-urlencoded

KEY=...&ACTION=INSERT&ADIF=...
```

goes to `POST https://logbook.qrz.com/api` with the same body.

| `{service}` | Goes to |
|---|---|
| `qrz-logbook` | `https://logbook.qrz.com` |
| `hrdlog` | `https://robot.hrdlog.net` |
| `eqsl` | `https://www.eqsl.cc` |
| `sotawatch` | `https://sotawatch.sota.org.uk` |
| `sota-sso` | `https://sso.sota.org.uk` |
| `sotadata` | `https://www.sotadata.org.uk` |
| `hamqsl` | `https://www.hamqsl.com` |
| `country-files` | `https://www.country-files.com` |

- **URL**: `GET|POST|PUT|DELETE /v1/proxy/{service}/{path}?{query}` → `{base}/{path}?{query}`. The path is escaped segment by segment and always stays on the service's host; `.` and `..` segments are refused. The query goes as the client encoded it.
- **Who**: HamQSO users, anonymous ones included (the app's integrations do not need an account). The Supabase token goes in **`X-HamQSO-Auth: Bearer <token>`**, never in `Authorization`, which belongs to the service. Tokens of the reader projects (HamQRG) are refused.
- **What goes to the service**: method, path, query, body, and only the headers `Content-Type`, `Accept`, `Authorization`, `User-Agent`. No cookies, no other header.
- **What comes back**: the service's status, `Content-Type` and body, as they are (also its errors: a `401` of QRZ is a `401` here). No other header of the service (no cookies).
- **Redirects**: followed only on the same host (scheme, host and port), at most 5; 301/302/303 become a `GET` without body, 307/308 repeat the request. A redirect to another host is never followed (`502 upstream_redirect`).
- **Limits**: request and answer body at most 10 MB; the service must answer within 30 s; **120 calls a minute per user**, **300 a minute per client address** (IPv4 by address, IPv6 by its /64; anonymous users are free to create: a new one per call does not get around it), and a server budget of **2000 a minute for users with an account** and **1000 a minute for anonymous users** (so anonymous traffic cannot starve accounts), all counted after the token check.
- **Privacy**: the server keeps nothing and logs only user id, service, method, status, duration and sizes; never credentials, path, query, headers or bodies (see [architecture.md](architecture.md#the-proxy-nothing-kept-nothing-logged)).

### Errors of the proxy itself

Every other answer is the service's own.

| Status | Body | When |
|---|---|---|
| `401` | empty | No `X-HamQSO-Auth: Bearer`, or not a valid Supabase token |
| `403` | `{"error": "not_allowed"}` | A token of a reader project (HamQRG) |
| `404` | `{"error": "unknown_service"}` | `{service}` is not in the list |
| `413` | `{"error": "too_large"}` | The request body is over 10 MB |
| `429` | `{"error": "rate_limited", "retry_after_s": N}` + `Retry-After` | Over 120 calls a minute for the user, or 300 for the client address |
| `503` | `{"error": "busy"}` | The server budget of the caller's kind is used up (accounts or anonymous users): try again later |
| `400` | `{"error": "bad_path"}` | A `.`/`..` segment or a malformed query |
| `502` | `{"error": "upstream_unavailable"}` | The service cannot be reached, or its answer broke off |
| `502` | `{"error": "upstream_too_large"}` | The answer is over 10 MB |
| `502` | `{"error": "upstream_redirect"}` | The service redirects to another host (or more than 5 times) |
| `504` | `{"error": "upstream_timeout"}` | No answer within 30 s |

## Stream: `GET /v1/rbn/{callsign}` (the coverage test)

The coverage test listens to the Reverse Beacon Network for the user's station through the server, as **Server-Sent Events** read with `fetch` (so the token goes in the header, as everywhere; `EventSource` cannot set headers and is not supported). Two versions:

- **v2** (`?v=2`, F71): phone and web. Every event is JSON already read by the server, with the **true position of the skimmer**, plus the state of the RBN connection. Use this one.
- **v1** (no `v`, or `v=1`, F58): the raw RBN line, signed-in users only. Kept unchanged for the web app versions already out.

```
GET https://spot.hamqso.app/v1/rbn/EA8/IU4VRB/P?v=2
Authorization: Bearer <access_token>
Accept: text/event-stream
```

Read the body as a stream of lines: `event: <type>` names the next event (none in v1: every event is a spot), `data: <text>` is its content, `event: end` closes the stream, lines starting with `:` are comments, an empty line ends an event.

- **Who**: v2: every HamQSO user, anonymous ones included (RBN spots are public and the call is in the path). v1: signed-in users only, anonymous users get `403 account_required`. HamQRG users get `403 not_allowed`. The token goes in `Authorization: Bearer` only; a token in the query is not read.
- **`{callsign}`**: the station to hear, slashes included (`EA8/IU4VRB/P`), letters and digits with at least one of each, at most 20 characters, as written (no spaces). The server matches spots **as the app does**: the base call of the spot equals the base call asked (`baseCallsign`, shared cases in `base_callsign_cases.json`), so `IU4VRB`, `IU4VRB/P`, `EA8/IU4VRB` all match `IU4VRB`.
- **Duration**: at most **15 minutes** per stream; then `event: end` / `data: time_limit` and the server closes it (open a new one to go on). Close it yourself when the test ends.
- **Limit**: at most **2 streams at once per user** (`429 too_many_streams`).
- A comment `: ping` every 15 s keeps proxies from closing a quiet stream.

**v2 events** (shared fixture `rbn_sse_v2.txt`, identical in the app's tests):

```
: connected

event: spot
data: {"skimmer":"OK4YL","call":"IU2DEMO","khz":14045.1,"snr_db":17,"wpm":23,"kind":"CQ","band":"20m","rbn_time":"1432Z","received_at":"2026-11-06T14:32:12.418Z","position":{"locator":"JN79FX","lat":49.98,"lon":14.46,"source":"node"},"dxcc":503,"continent":"EU"}

event: status
data: {"feed":"reconnecting"}
```

| Field of `spot` | Type | Meaning |
|---|---|---|
| `skimmer` | string | The skimmer, without the RBN `-#` (`KM3T-2` for `KM3T-2-#`) |
| `call` | string | The station heard, in capitals, as the RBN wrote it (`IU2DEMO/P`) |
| `khz` | number | Frequency |
| `snr_db` | int | Signal to noise |
| `wpm` | int or null | CW speed; null for a speed in bauds (RTTY) |
| `kind` | string | `CQ`, `BEACON`, `NCDXF B`, `DX`… |
| `band` | string or null | `20m` (the server's band edges, the same as the app's); null outside the amateur bands |
| `rbn_time` | string | The time the RBN wrote (`1432Z`, no date) |
| `received_at` | string | When the server received the line (UTC, milliseconds): use this for "time to first spot" |
| `position` | object or null | Where the skimmer is: `locator` (its node's 6-character locator, null for `source: "country"`), `lat`, `lon` (two decimals), `source`: `node` (the centre of the locator of the RBN node list) or `country` (the centre of its DXCC entity, cty.dat). Null when neither is known |
| `dxcc`, `continent` | int, string, or null | The skimmer's DXCC entity (ADIF) and continent, from its call |

`status` carries `feed`: `up`, `reconnecting` or `down` (the same as `feed.state` of `GET /v1/rbn/skimmers`). It is sent when the state changes, and at once after `: connected` if the connection is not up when the stream opens (a stream that says nothing started with the feed up). While it reconnects the stream stays open and simply has no spots.

**v1 events**: one `data:` event per RBN spot line of the station, **the line as the RBN sent it** (trimmed, control characters removed), e.g. `data: DX de OK4YL-#:   7025.10  IU4VRB/P   CW   7 dB  23 WPM  CQ   2055Z`; parse it as the app's `RbnLineParser`. No `status`.

**Behind it**: one RBN telnet connection for all of HamQSO, logged in with the server's own callsign (never the user's), **always open** (F71; it also counts which skimmers are listening), reconnected with backoff if it drops.

| Status | Body | When |
|---|---|---|
| `200` | the event stream | |
| `400` | `{"error": "invalid_callsign"}` | Not a callsign (see above) |
| `400` | `{"error": "invalid_version"}` | `v` is not `1` or `2` |
| `401` | empty | No token, or not a valid Supabase token |
| `403` | `{"error": "account_required"}` / `{"error": "not_allowed"}` | Anonymous user on v1 / reader project |
| `429` | `{"error": "too_many_streams"}` | 2 streams of this user already open |

## REST: `GET /v1/rbn/skimmers` (who is listening, F71)

The skimmers of the Reverse Beacon Network that are listening on a band, each with its true position: the forecast of the coverage test (Supabase `coverage_forecast`) asks it for the skimmers of the test and for the forecast "now".

```
GET https://spot.hamqso.app/v1/rbn/skimmers?band=20m&from=2026-11-06T14:32:05Z&to=2026-11-06T14:33:17Z
Authorization: Bearer <access_token>
```

- **Who**: every HamQSO user, anonymous ones included (`401` without a valid token, `403 not_allowed` for HamQRG).
- **`band`**: required, one of the server's bands (`160m` … `23cm`, lowercase `m`).
- **`from`, `to`**: the start and end of the test, ISO 8601 (UTC with `Z`, or with an offset; without one it is read as UTC). Both or neither. With them the window is **from 10 minutes before `from` to `to`**; without them, the last 10 minutes. The window must end at most 1 minute in the future and start (10 minutes before `from`) at most **48 hours** ago: older activity is not kept.
- **Listening** means at least one spot on that band in the window (counted per minute of arrival at the server: the minute in which the window starts counts whole).

```json
{
  "band": "20m",
  "from": "2026-11-06T14:22:05Z",
  "to": "2026-11-06T14:33:17Z",
  "feed": {"state": "up", "since": "2026-11-06T03:12:44Z"},
  "nodes": {"fetched_at": "2026-11-06T14:05:00Z", "count": 350, "stale": false},
  "skimmers": [
    {"call": "OK4YL", "locator": "JN79FX", "lat": 49.98, "lon": 14.46, "position_source": "node",
     "dxcc": 503, "continent": "EU", "spots": 42, "last_spot_at": "2026-11-06T14:31:40Z"},
    {"call": "Z61DX", "locator": null, "lat": 42.67, "lon": 21.17, "position_source": "country",
     "dxcc": 522, "continent": "EU", "spots": 3, "last_spot_at": "2026-11-06T14:29:02Z"}
  ]
}
```

(Shared fixture `rbn_skimmers_v1.json`, identical in `ham_qso_supabase`.)

| Field | Meaning |
|---|---|
| `from`, `to` | The window used |
| `feed.state`, `feed.since` | `up`, `reconnecting`, `down` (`down` also after 5 minutes of reconnecting), and since when (null: since the server started). Not `up`: the list may be short |
| `nodes.fetched_at`, `count`, `stale` | The last good read of the RBN node list, how many nodes it had, and whether it is stale: older than 2 hours, none yet, or the page changed format. Stale: more skimmers may be at the centre of their country |
| `skimmers[]` | Most spots first, then by call. `call` without `-#`; `locator`, `lat`, `lon`, `position_source` as `position` of the stream v2; `dxcc`, `continent` from the call; `spots` in the window; `last_spot_at` (seconds). A skimmer whose position is not known at all (no node, unknown entity) is left out |

| Status | Body | When |
|---|---|---|
| `200` | the answer above | |
| `400` | `{"error": "invalid_band"}` | `band` missing or not one of the server's |
| `400` | `{"error": "invalid_window"}` | Only one of `from`/`to`, not a time, `from` after `to`, in the future, or older than 48 hours |
| `401` | empty | No token, or not a valid Supabase token |
| `403` | `{"error": "not_allowed"}` | A token of a reader project (HamQRG) |

## REST: `GET /v1/references/catalog` (the offline reference catalog)

F53.3. Every active POTA park, SOTA summit and WWFF area of HamQSO (about 340,000, about 7 MB), for the apps to search, suggest "near you" and name references without the network. The server rebuilds it once a week from `xota_references`; the apps download it once every three months.

- `Authorization: Bearer <Supabase access token>`, anonymous users included. Without a valid token: `401`.
- `200`: the file, `Content-Type: application/gzip`, **no** `Content-Encoding` (the body is the gzip itself; `Content-Length` is its size, known before the body: an app on a metered network can stop there). Headers: `ETag`, `Last-Modified` (when it was built), `X-Reference-Count`.
- `If-None-Match: <ETag>` of the file the app has: `304` without body when the catalog has not changed. The ETag depends on the references only: the same catalog rebuilt a week later keeps it.
- `503` with `Retry-After: 3600` before the server has its first file (first start, or a local run without the Supabase secret key).

The file (gzip of UTF-8 text): a header line `HAMQSO-REFERENCES<TAB>1<TAB><built at, ISO 8601 UTC><TAB><count>`, then one reference per line in code order, ten fields separated by tabs, empty when unknown:

```
ref  program  name  location  dxcc  grid  lat  lon  altitude_m  points
IT-1271  POTA  Torbiere del Lavaze'  Trentino-Alto Adige, Italy  248  JN56NH  46.35541  11.49127
```

Latitude and longitude have at most 5 decimals; a tab or a line break inside a name or a location becomes a space. An app must skip lines with fewer than ten fields and refuse a file whose header is not `HAMQSO-REFERENCES` version 1.

## CORS

The HTTP API (`/v1/spots`, `/v1/spots/on-frequency`, `/v1/references/catalog`, `/v1/dx/spots`, `/v1/proxy`, `/v1/rbn`) answers browsers of the configured origins (`Cors:Origins`: in production **`https://app.hamqso.app`**, the web app; in development `http://localhost:*` and `http://127.0.0.1:*`): methods `GET`, `POST`, `PUT`, `DELETE`, `OPTIONS`; request headers `Authorization`, `Content-Type`, `X-HamQSO-Auth`, `If-None-Match`; exposed response headers `ETag`, `X-Reference-Count` (the catalog); preflight cached 10 minutes; no credentials (cookies) — the tokens travel in headers. Another origin gets no CORS headers, so the browser refuses the answer. MQTT over WebSocket needs no CORS.

## Limits and attribution

- At most 200 spots per answer. No rate limit on the lists or on `/v1/spots/on-frequency` yet: be gentle (one list per open or reconnection, one question per frequency, not polling). Sending spots is limited (above).
- HamQTH asks to show "Data provided by HamQTH.com" where its data is shown.
- RBN data (spots, skimmers, node list): cite "Reverse Beacon Network" with a link to https://www.reversebeacon.net where it is shown (F71).
- Tested capacity (27/10/2026): up to 500 clients connected at once with news delivered to all within 0.9 s (95th percentile); about 900 is the current limit.
