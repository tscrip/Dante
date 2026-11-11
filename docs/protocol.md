# DeskBuddy wire protocol (v1)

Transport: **Bluetooth LE**, Nordic UART Service: service `6E400001-B5A3-F393-E0A9-E50E24DCCA9E`,
the PC writes to RX `6E400002-…`, the buddy notifies on TX `6E400003-…` (lines may be split across
writes/notifications in any way). Or **USB serial** (115200 baud; on the ESP32-S3 both the native
USB port and the UART port). The firmware accepts the same protocol on every link and answers on the
link it last heard from. (The ESP32-S3 has no Bluetooth Classic, so there is no SPP.)

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
| `cfg`   | `bright` (0-255), `display` (`oled`\|`lcd1602`), `lcdAddr` (I2C address, e.g. 39 = 0x27), `screens` (1\|2), `layout` (`watch`\|`mirror`), `content` (`left`\|`right`), `swap` (bool), `flip1` (bool), `flip2` (bool) | Device settings; every field optional. `display` picks the screen type (`lcd1602` = 16x2 character LCD on a PCF8574 backpack; always one screen, `bright` 0 turns its backlight off). Display settings are stored in flash. `screens`: 1 = both eyes on one display, 2 = one eye per display. With 2 screens, app screens use `layout`: `watch` = the app on the `content` side while the other eye watches it, `mirror` = the app on both. `swap` exchanges left/right; `flip1`/`flip2` rotate a physical display 180°. |

## Device → PC

| `t`      | Fields | Meaning |
|----------|--------|---------|
| `hello`  | `fw`, `name`, `display`, `screens`, `disp2`, `oled`, `lcd` | Firmware version, device name, screen type in use, screens in use, and what answered at boot: a second OLED (`disp2`), the first OLED (`oled`), and the address of an LCD backpack (`lcd`, 0 = none). PC replies by sending `apps`, every `val`, `mood`, active `alert`s. |
| `pong`   | –      | Heartbeat reply. |
| `btn`    | `b` ∈ `sel`,`left`,`right`; `long` (bool); `scr` (app id or `face`) | Button press. Short `sel` on an app = "open it" (PC opens configured URL); long `sel` = "refresh now". |
| `ack`    | `id`   | User pressed SELECT on a takeover ("I'm on it"). |
| `snooze` | `id`   | User pressed LEFT/RIGHT on a takeover ("later"). |

## Example session

```
PC  {"t":"hello"}
DEV {"t":"hello","fw":"1.2.0","name":"DeskBuddy","display":"oled","screens":1,"disp2":true,"oled":true,"lcd":0}
PC  {"t":"cfg","bright":200,"display":"oled","lcdAddr":39,"screens":2,"layout":"watch","content":"right","swap":false,"flip1":false,"flip2":false}
PC  {"t":"apps","apps":[{"id":"cc_usage","name":"Claude 5h","vis":"gauge","min":0,"max":100,"unit":"%"},{"id":"prs","name":"PRs","vis":"counter","min":0,"max":20,"unit":""}]}
PC  {"t":"val","id":"cc_usage","v":63,"txt":"resets 2h10m","lvl":0}
PC  {"t":"val","id":"prs","v":3,"txt":"oldest 4h","lvl":1,"quip":"3 PRs! They multiply when you look away."}
PC  {"t":"mood","m":"antsy","n":4,"age":240}
PC  {"t":"alert","id":"cc_wait","title":"Claude Code","txt":"DeskBuddy repo is waiting on you","lvl":1}
DEV {"t":"ack","id":"cc_wait"}
```
