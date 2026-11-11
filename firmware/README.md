# DeskBuddy firmware (ESP32)

A tiny desk companion with opinions: animated robot eyes on a 0.91" 128x32 OLED, three
buttons, and a Bluetooth link to the DeskBuddy PC app, which does all the thinking and
pushes values, moods and nudges to the device.

Protocol: [`../docs/protocol.md`](../docs/protocol.md)

## Hardware

| Part                        | ESP32 pin | Notes                                  |
|-----------------------------|-----------|----------------------------------------|
| OLED SSD1306 128x32 `SDA`   | GPIO 21   | I2C, 400 kHz                           |
| OLED SSD1306 128x32 `SCL`   | GPIO 22   |                                        |
| OLED `VCC` / `GND`          | 3V3 / GND |                                        |
| LEFT button                 | GPIO 25   | other leg to GND (internal pull-up)    |
| RIGHT button                | GPIO 26   | other leg to GND                       |
| SELECT button (top)         | GPIO 27   | other leg to GND                       |

Any classic ESP32 with Bluetooth Classic works (ESP32-WROOM / `esp32dev`). The ESP32-S2/S3/C3
have no Bluetooth Classic, so they can only use the USB serial link. All pins and timings
live in [`src/config.h`](src/config.h).

### Alternative screen: 1602 LCD (handy for development)

A standard 16×2 character LCD with a PCF8574 I2C backpack works instead of the OLED. Pick
**Screen type → LCD 1602** in the PC app (*Device → Screens*) and set its address (0x27 for PCF8574
backpacks, 0x3F for PCF8574A). Before it has been configured, the buddy uses whichever screen it finds
at boot (OLED preferred), and it reports what it found so the PC app can suggest the address.

| Part              | ESP32 pin | Notes                                                        |
|-------------------|-----------|--------------------------------------------------------------|
| LCD backpack `SDA`| GPIO 21   | same bus as the OLED                                         |
| LCD backpack `SCL`| GPIO 22   |                                                              |
| LCD `VCC` / `GND` | 5V / GND  | most 1602 modules need 5 V; adjust the contrast pot on the back |

On the LCD the eyes are 2×2 characters each, drawn with custom glyphs from the same eye shapes as
the OLED face. They blink, glance around (moving across columns) and change shape with the mood.
Apps use bars, sparklines and scrolling text, and urgent nudges flash the backlight. 5 V backpacks
pull SDA/SCL up to 5 V; most ESP32 boards tolerate this, but a level shifter (or removing the
backpack's pull-ups) is the safe option.

### Optional: a second screen (one per eye)

Choose **1 or 2 screens** in the PC app (*Device → Screens*); the buddy stores the choice in flash.
With two screens each display shows one big eye. On app screens the PC app picks the layout:
the app on one screen while the other eye *watches it*, or the app on both screens.

| Part                          | ESP32 pin | Notes                                              |
|-------------------------------|-----------|----------------------------------------------------|
| 2nd OLED `SDA`                | GPIO 18   | second I2C bus (`Wire1`)                           |
| 2nd OLED `SCL`                | GPIO 19   |                                                    |
| 2nd OLED `VCC` / `GND`        | 3V3 / GND |                                                    |

Most 0.91" modules are fixed at address `0x3C`, so two of them can't share a bus — hence the second
bus. If your second module has an address jumper set to `0x3D`, you can put it on GPIO 21/22 instead:
set `DISP2_SAME_BUS 1` and `DISP2_I2C_ADDR 0x3D` in `src/config.h`. Screens mounted the wrong way round
or upside down are fixed in the PC app (swap / flip), no rewiring needed. The buddy reports whether it
found the second screen at boot, and the PC app warns you if it didn't.

## Build and flash

```bash
# PlatformIO CLI (or open the folder in VS Code with the PlatformIO extension)
cd firmware
pio run                 # build
pio run -t upload       # flash over USB
pio device monitor      # watch the JSON lines (115200 baud)
```

The `huge_app.csv` partition table is used because the Bluetooth Classic stack is large.

## Connecting to the PC

The buddy speaks the same JSON-lines protocol over **Bluetooth SPP** (device name
`DeskBuddy`) and over the **USB serial** port, so you can develop over USB and switch to
Bluetooth later. It always answers on the link it last heard from.

1. Pair with `DeskBuddy` from the OS Bluetooth settings (no PIN).
2. Find the serial port it created:
   - **Windows:** Bluetooth settings -> *More Bluetooth options* -> *COM Ports*. Use the
     **Outgoing** COM port for `DeskBuddy 'ESP32SPP'`.
   - **Linux:** `sudo rfcomm bind 0 <MAC>` (find the MAC with `bluetoothctl devices`),
     then use `/dev/rfcomm0`. Add yourself to the `dialout` group if needed.
   - **macOS:** `/dev/cu.DeskBuddy` appears after pairing.
3. Select that port in the DeskBuddy PC app.

If no message arrives from the PC for 30 s the buddy decides it is alone: it gets sleepy
and blinks a small Bluetooth "no PC" sign in the corner.

## Controls

| Where            | Button               | What happens                                                    |
|------------------|----------------------|-----------------------------------------------------------------|
| Any screen       | LEFT / RIGHT         | Previous / next screen (slides). Face is screen 0, apps follow. |
| Any screen       | long LEFT / RIGHT    | Jump home to the face.                                          |
| Face             | SELECT               | Play a random gag.                                              |
| App screen       | SELECT               | Tells the PC "open this" (e.g. it opens the PR list).           |
| Any screen       | long SELECT          | Tells the PC "refresh now".                                     |
| Speech bubble    | any                  | Dismiss it.                                                     |
| Takeover (nudge) | SELECT               | "I'm on it" -> `ack` to the PC, buddy winks.                    |
| Takeover (nudge) | LEFT / RIGHT         | "Later" -> `snooze` to the PC.                                  |

Every press is reported to the PC as a `btn` event (takeover presses are reported as
`ack` / `snooze` instead).

## Personality

The PC decides the mood from how many things are waiting on you and how long they have
been waiting. Each mood has its own eyes and idle habits:

| Mood       | Look                                    | Habits                                                       |
|------------|-----------------------------------------|--------------------------------------------------------------|
| `happy`    | `^ ^` cheeks                            | little hops, winks, sparkles                                 |
| `curious`  | one eye bigger than the other           | quick glances around, swaps brows, "?"                       |
| `antsy`    | worried outer lids                      | keeps glancing at *you*, foot-tapping shuffle, "!!"          |
| `grumpy`   | angry inner lids, squinting             | long judgemental side-eye, slow blinks, huffs                |
| `meltdown` | wide eyes                               | shaking, sweat drop, flashes of X eyes, "!!"                 |
| sleepy     | heavy lids (no PC connected)            | floating "z"s, nodding off and snapping awake                |

A small badge in the top-right corner shows how many items are pending.

**Gags** (SELECT on the face): heart eyes, dizzy spirals, wink, laughing `> <` with
"ha ha", surprised double-take and "boop!" squish.

**Speech bubbles**: when the PC sends a value with a `quip` (or a `say`), a mini face pops
up with a scrolling speech bubble for ~4.5 s, and the big face does a double-take afterwards.

**Takeovers**: an `alert` takes over the whole screen: a mini face bounces and stares at you,
the message scrolls, and every 3 s the buddy "knocks" (shakes the screen). Level-2 alerts
also flash the screen. Several alerts can be queued; the highest level (then the newest)
is shown first.

## App visuals

The PC defines the app screens; each one picks a visual:

| `vis`     | Shows                                                                 |
|-----------|-----------------------------------------------------------------------|
| `gauge`   | semicircle gauge with a sweeping needle, big value + unit, small text |
| `bar`     | big value, horizontal progress bar                                    |
| `counter` | big number that drops in with a bounce and a floating `+1!`           |
| `text`    | scrolling text                                                        |
| `spark`   | sparkline of the last 32 values                                       |
| `status`  | OK / warning / error icon plus scrolling text                         |

Values tween smoothly, and level-2 (critical) values blink.

## Source layout

| File                 | Purpose                                                       |
|----------------------|---------------------------------------------------------------|
| `src/config.h`       | pins, timings, limits                                         |
| `src/Link.*`         | JSON-lines over Bluetooth SPP + USB serial, heartbeat         |
| `src/Buttons.*`      | debounced short/long presses                                  |
| `src/Face.*`         | procedural eyes, moods, gags, mini faces                      |
| `src/Apps.*`         | app store + the six visuals, marquee helper                   |
| `src/main.cpp`       | screens, slide transitions, toasts, takeovers, message handling |
