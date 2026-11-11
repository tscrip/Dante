# DeskBuddy wire protocol (v1)

Transport: Bluetooth Classic **SPP** (the ESP32 advertises as `DeskBuddy`) or USB serial
(115200 baud) — the firmware accepts the same protocol on both and answers on the link
it last heard from.

Framing: one compact JSON object per line, terminated by `\n`. Max line length 1024 bytes.
Every message has a `t` (type) field. Unknown types/fields are ignored by both sides.

Limits on the device: max 12 apps, `id` ≤ 15 chars, `name` ≤ 19 chars, `unit` ≤ 5 chars,
`txt` ≤ 47 chars, `quip`/`title` ≤ 63 chars. Longer strings are truncated by the device.

## PC → Device

| `t`     | Fields | Meaning |
|---------|--------|---------|
| `hello` | –      | PC (re)connected. Device answers with its own `hello`. |
| `ping`  | –      | Heartbeat (PC sends every ~10 s). Device answers `pong`. If no message arrives for 30 s the buddy considers itself alone (sleepy + "no PC" icon). |
| `apps`  | `apps`: array of `{id, name, vis, min, max, unit}` | Replaces the list of app screens. `vis` ∈ `gauge`, `bar`, `counter`, `text`, `spark`, `status`. Screen order = array order; the face is always screen 0. |
| `val`   | `id`, `v` (number, optional), `txt` (optional), `lvl` (0 ok, 1 warn, 2 crit), `quip` (optional) | New value for an app. If `quip` is present the buddy "delivers" it: a speech-bubble toast with a reaction face. |
| `mood`  | `m` ∈ `happy`,`curious`,`antsy`,`grumpy`,`meltdown`; `n` pending count; `age` oldest wait in minutes | Personality baseline for the face. |
| `alert` | `id`, `title`, `txt`, `lvl` (1 nudge, 2 urgent) | Take over the screen and nudge the human until acknowledged or cleared. |
| `clear` | `id`   | Remove the takeover for that id. |
| `say`   | `txt`  | Show a toast speech bubble (no value attached). |
| `cfg`   | `bright` (0-255) | Device settings. |

## Device → PC

| `t`      | Fields | Meaning |
|----------|--------|---------|
| `hello`  | `fw`, `name` | Firmware version and device name. PC replies by sending `apps`, every `val`, `mood`, active `alert`s. |
| `pong`   | –      | Heartbeat reply. |
| `btn`    | `b` ∈ `sel`,`left`,`right`; `long` (bool); `scr` (app id or `face`) | Button press. Short `sel` on an app = "open it" (PC opens configured URL); long `sel` = "refresh now". |
| `ack`    | `id`   | User pressed SELECT on a takeover ("I'm on it"). |
| `snooze` | `id`   | User pressed LEFT/RIGHT on a takeover ("later"). |

## Example session

```
PC  {"t":"hello"}
DEV {"t":"hello","fw":"1.0.0","name":"DeskBuddy"}
PC  {"t":"apps","apps":[{"id":"cc_usage","name":"Claude 5h","vis":"gauge","min":0,"max":100,"unit":"%"},{"id":"prs","name":"PRs","vis":"counter","min":0,"max":20,"unit":""}]}
PC  {"t":"val","id":"cc_usage","v":63,"txt":"resets 2h10m","lvl":0}
PC  {"t":"val","id":"prs","v":3,"txt":"oldest 4h","lvl":1,"quip":"3 PRs! They multiply when you look away."}
PC  {"t":"mood","m":"antsy","n":4,"age":240}
PC  {"t":"alert","id":"cc_wait","title":"Claude Code","txt":"DeskBuddy repo is waiting on you","lvl":1}
DEV {"t":"ack","id":"cc_wait"}
```
