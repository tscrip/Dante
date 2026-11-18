# DANTE wire protocol (v1, firmware 3.5)

Transport: **Bluetooth LE**, Nordic UART Service: service `6E400001-B5A3-F393-E0A9-E50E24DCCA9E`,
the PC writes to RX `6E400002-…`, DANTE notifies on TX `6E400003-…` (lines may be split across
writes/notifications in any way). Or **USB serial** (115200 baud; on the ESP32-S3 both the native
USB port and the UART port). The firmware accepts the same protocol on every link and answers on the
link it last heard from. (The ESP32-S3 has no Bluetooth Classic, so there is no SPP.)

Framing: one compact JSON object per line, terminated by `\n`. Max line length 1024 bytes.
Every message has a `t` (type) field. Unknown types/fields are ignored by both sides.

The PC app calls the screens *widgets*; on the wire they are still `apps`.

Limits on the device: max 12 apps, `id` ≤ 15 chars, `name` ≤ 19 chars, `unit` ≤ 5 chars,
`txt` ≤ 47 chars, `quip`/`title` ≤ 63 chars. Longer strings are truncated by the device.

## PC → Device

| `t`     | Fields | Meaning |
|---------|--------|---------|
| `hello` | –      | PC (re)connected. Device answers with its own `hello`. |
| `ping`  | –      | Heartbeat (PC sends every ~10 s). Device answers `pong`. If no message arrives for 30 s DANTE considers itself alone (sleepy + "no PC" icon). |
| `diag`  | –      | "Check wiring": DANTE answers with a `diag` (what it found at boot plus a live scan of the I2C bus). |
| `apps`  | `apps`: array of `{id, name, vis, min, max, unit}` (+ `l1`, `l2` for `spark2`) | Replaces the list of app screens. `vis` ∈ `gauge`, `bar`, `counter`, `text`, `spark`, `spark2`, `status`, `timer`. `spark2` is a dual sparkline: two history graphs, one per line, labelled `l1` / `l2` (up to 4 characters, e.g. `CPU`, `MEM`); both use `min`..`max` (firmware 3.4+). Screen order = array order; the face is always screen 0. |
| `val`   | `id`, `v` (number, optional), `v2` (number, optional: the bottom line of a `spark2`), `txt` (optional), `lvl` (0 ok, 1 warn, 2 crit), `quip` (optional), `tot` + `run` (countdowns) | New value for an app. If `quip` is present DANTE "delivers" it: a speech-bubble toast with a reaction face. **Countdowns** (`timer` apps) carry `v` = seconds left, `tot` = seconds in the whole countdown (for the progress bar) and `run` = whether it is ticking; DANTE counts down by itself from the moment the message arrives, so the PC only sends a `val` when something changes (start, pause, a new session). A `timer` app's `val` without `v` means "no countdown right now". |
| `mood`  | `m` ∈ `happy`,`curious`,`antsy`,`grumpy`,`meltdown`; `n` pending count; `age` oldest wait in minutes | Personality baseline for the face. |
| `alert` | `id`, `title`, `txt`, `lvl` (1 nudge, 2 urgent) | Take over the screen and nudge the human until acknowledged or cleared. Apps raise these through their take-over rules; features such as calendar reminders (`id` `calendar`) and the Pomodoro timer (`id` `pomodoro`) raise their own. |
| `clear` | `id`   | Remove the takeover for that id. |
| `say`   | `txt`  | Show a toast speech bubble (no value attached). |
| `cfg`   | `bright` (0-255), `display` (`auto`\|`oled`\|`tft`), `flip` (bool), `accent` (0xRRGGBB as a number), `look` (0-4) | Device settings; every field optional; stored in flash. `accent` is the TFT's accent colour (title, happy eyes, highlights). `look` is DANTE's style: 0 Classic, 1 Gentleman, 2 Nerd, 3 Grumpy Uncle, 4 Party. DANTE always drives **one** screen. `display` picks it: `oled` = 0.91" SSD1306 128x32 (I2C), `tft` = 3.5" 480x320 ST7796 colour TFT (SPI), `auto` = the OLED if it answers on I2C, otherwise the TFT (which can't be detected). `bright` is the OLED contrast or the TFT backlight (0 = off). `flip` rotates the screen 180°. (Older firmware also took `lcd1602` / `lcd2004` with `lcdSize` and `lcdAddr` for character LCDs, and `screens`, `layout`, `content`, `swap`, `flip1`, `flip2` for two-screen mode; 3.2 ignores them, treats an LCD as `auto`, and reads `flip1` as `flip` when `flip` is missing.) |

## Device → PC

| `t`      | Fields | Meaning |
|----------|--------|---------|
| `hello`  | `fw`, `name`, `display`, `oled` | Firmware version, device name, screen type in use (`oled`/`tft`), and whether an OLED answered on the I2C bus at boot (`oled`). PC replies by sending `cfg`, `apps`, every `val`, `mood`, active `alert`s. |
| `pong`   | –      | Heartbeat reply. |
| `diag`   | `boot`, `scan` | `boot` = the startup report line; `scan` = live I2C scan, e.g. `I2C scan now: bus 1 (SDA=8 SCL=9): 0x3C (OLED)`. The TFT is on SPI and never shows up in the scan. Typing `?` in a serial monitor prints the same two lines as plain text. |
| `btn`    | `b` ∈ `sel`,`left`,`right`,`magic`; `long` (bool); `scr` (app id or `face`) | Button press. `sel` acts on the screen in `scr`: short on an app = "open it" (PC opens configured URL), long = "refresh now"; on a countdown screen (the Pomodoro timer) press = start / pause and long press = reset. `magic` always runs the action configured in the PC app (mute, play/pause, ...), whatever the screen. The PC answers with a `say` describing the result. MAGIC presses are sent even during a takeover and never acknowledge it. |
| `ack`    | `id`   | User pressed SELECT on a takeover ("I'm on it"). For a meeting reminder the PC opens the meeting's join link; for a finished Pomodoro session it starts the next one. |
| `snooze` | `id`   | User pressed LEFT/RIGHT on a takeover ("later"). |

## Example session

```
PC  {"t":"hello"}
DEV {"t":"hello","fw":"3.2.0","name":"DANTE","display":"tft","oled":false}
PC  {"t":"cfg","bright":200,"display":"tft","flip":false,"accent":6220500,"look":0}
PC  {"t":"apps","apps":[{"id":"cc_usage","name":"Claude 5h","vis":"gauge","min":0,"max":100,"unit":"%"},{"id":"pomodoro","name":"Pomodoro","vis":"timer","min":0,"max":1500,"unit":""}]}
PC  {"t":"val","id":"cc_usage","v":63,"txt":"resets 2h10m","lvl":0}
PC  {"t":"val","id":"pomodoro","lvl":0,"v":1500,"tot":1500,"run":false,"txt":"Focus 1/4 - SEL: start"}
PC  {"t":"mood","m":"antsy","n":4,"age":240}
DEV {"t":"btn","b":"sel","long":false,"scr":"pomodoro"}
PC  {"t":"say","txt":"Focus 1/4: 25:00. Go!"}
PC  {"t":"val","id":"pomodoro","lvl":0,"v":1500,"tot":1500,"run":true,"txt":"Focus 1/4"}
PC  {"t":"alert","id":"calendar","title":"Meeting","txt":"Standup in 10 min","lvl":1}
DEV {"t":"ack","id":"calendar"}
```

## Simulator only

The [simulator](../firmware/README.md#simulator) speaks the protocol above on stdin/stdout, plus:

| Direction | `t` | Fields | Meaning |
|---|---|---|---|
| PC → sim | `sim.btn` | `b` ∈ `left`,`right`,`sel`,`magic`; `down` (bool) | Press or release a button. Changes are applied at least debounce-time apart, so a quick click never gets lost. |
| sim → PC | `sim.frame` | `fw`, `kind`, plus per kind: **`oled`** `w`, `h`, `pixels` (base64 SSD1306 buffer: 4 pages × 128 columns, LSB on top); **`tft`** `w`, `h`, `light` (backlight 0-255), `rle` (base64 run-length RGB565: little-endian uint16 pairs of run length and colour) | What the screen shows, sent whenever it changes (at most ~25 per second, 20 for the TFT). |
