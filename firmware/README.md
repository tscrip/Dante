# DeskBuddy firmware (ESP32-S3)

A tiny desk companion with opinions: animated robot eyes on a 0.91" 128x32 OLED (or two, or a
1602/2004 LCD), four buttons (incl. a MAGIC button), and a Bluetooth LE link to the DeskBuddy PC app, which does all the thinking and
pushes values, moods and nudges to the device.

Protocol: [`../docs/protocol.md`](../docs/protocol.md)

## Hardware

Board: **ESP32-S3** (built for the ESP32-S3-DevKitC-1; any S3 board works if the pins below
are free). The S3 has Bluetooth LE but **no Bluetooth Classic**, so the buddy talks to the PC
over BLE (Nordic UART Service) or over USB.

| Part                        | ESP32-S3 pin | Notes                                  |
|-----------------------------|--------------|----------------------------------------|
| OLED SSD1306 128x32 `SDA`   | GPIO 8       | I2C, 400 kHz (S3 Arduino default pins) |
| OLED SSD1306 128x32 `SCL`   | GPIO 9       |                                        |
| OLED `VCC` / `GND`          | 3V3 / GND    |                                        |
| LEFT button                 | GPIO 4       | other leg to GND (internal pull-up)    |
| RIGHT button                | GPIO 5       | other leg to GND                       |
| SELECT button (top)         | GPIO 6       | other leg to GND                       |
| MAGIC button (top)          | GPIO 7       | other leg to GND; action set in the PC app |

The pins avoid the S3's strapping pins (0, 3, 45, 46), native USB (19/20), UART0 (43/44) and
GPIO 26-37, which the flash/PSRAM use on most S3 modules (octal-PSRAM "R8" modules included).
All pins and timings live in [`src/config.h`](src/config.h).

### Alternative screen: 1602 (16×2) or 2004 (20×4) LCD

A standard character LCD with a PCF8574 I2C backpack works instead of the OLED: 16×2 ("1602")
or 20×4 ("2004"). Pick **Screen type → LCD 1602** or **LCD 2004** in the PC app (*Device →
Screens*) and set its address (0x27 for PCF8574
backpacks, 0x3F for PCF8574A). Before it has been configured, the buddy uses whichever screen it finds
at boot (OLED preferred), and it reports what it found so the PC app can suggest the address.
Both LCD sizes look the same on the I2C bus, so with *Automatic* the PC app also says which size
to expect (it remembers the last LCD type you picked).

| Part              | ESP32 pin | Notes                                                        |
|-------------------|-----------|--------------------------------------------------------------|
| LCD backpack `SDA`| GPIO 8    | same bus as the OLED                                         |
| LCD backpack `SCL`| GPIO 9    |                                                              |
| LCD `VCC` / `GND` | 5V / GND  | most 1602 modules need 5 V; adjust the contrast pot on the back |

On the LCD the eyes are 2×2 characters each, drawn with custom glyphs from the same eye shapes as
the OLED face. They blink, glance around (moving across columns) and change shape with the mood.
Apps use bars, sparklines and scrolling text, and urgent nudges flash the backlight. On a 20×4 the
face also gets a mouth that follows the mood (`` `--' `` happy, `---` grumpy, `{OO}` meltdown...),
app screens add a line of detail and a `< 2/5 >` page indicator, and messages wrap over two lines. 5 V backpacks
pull SDA/SCL up to 5 V. The ESP32-S3's pins are **not** 5 V tolerant, so use a level shifter
(or remove the backpack's pull-up resistors and rely on 3.3 V pull-ups).

### Optional: a second screen (one per eye)

Choose **1 or 2 screens** in the PC app (*Device → Screens*); the buddy stores the choice in flash.
With two screens each display shows one big eye. On app screens the PC app picks the layout:
the app on one screen while the other eye *watches it*, or the app on both screens.

| Part                          | ESP32 pin | Notes                                              |
|-------------------------------|-----------|----------------------------------------------------|
| 2nd OLED `SDA`                | GPIO 17   | second I2C bus (`Wire1`)                           |
| 2nd OLED `SCL`                | GPIO 18   |                                                    |
| 2nd OLED `VCC` / `GND`        | 3V3 / GND |                                                    |

Most 0.91" modules are fixed at address `0x3C`, so two of them can't share a bus — hence the second
bus. If your second module has an address jumper set to `0x3D`, you can put it on GPIO 8/9 instead:
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

The environment is `esp32-s3` (board `esp32-s3-devkitc-1`). `ARDUINO_USB_CDC_ON_BOOT=1` makes the
S3's native **USB** port the main serial port; the DevKit's **UART** port also speaks the protocol.
If an upload doesn't start, hold **BOOT**, tap **RST**, release **BOOT**, and upload again.
The `huge_app.csv` partition table leaves room for the Bluetooth stack.

## Blank screen?

Open the serial monitor (`pio device monitor`, then press **RST**). The first line says what the
buddy found on the I2C bus and which screen it is driving, e.g.

```
DeskBuddy 2.0.1: I2C SDA=8 SCL=9 | OLED@0x3C not found | LCD 0x27 | screen: lcd1602 (auto)
```

* **Nothing found:** check SDA → GPIO 8, SCL → GPIO 9, power and GND (the old ESP32 build used
  21/22).
* **Found, but the other screen is used:** in the PC app set *Device → Screen type* to
  *Automatic* or the right type and save; the buddy remembers it.
* **1602 found but blank:** turn the contrast potentiometer on the backpack. The LCD needs 5 V,
  and check the I2C level shifting described above.
* **1602 shows a row of solid blocks:** it has power but never got initialised. Usually the wrong
  address or the SDA/SCL wires are swapped.

## Connecting to the PC

The buddy speaks the same JSON-lines protocol over **Bluetooth LE** and over **USB**, and always
answers on the link it last heard from.

* **Bluetooth LE** (default): the buddy advertises as `DeskBuddy` with the Nordic UART Service
  (`6E400001-B5A3-F393-E0A9-E50E24DCCA9E`; the PC writes to `…0002`, the buddy notifies on `…0003`).
  **No pairing**: in the PC app pick *Device → Bluetooth LE* (*Scan* finds it). Works from Windows
  and Linux (BlueZ). Any BLE terminal app (e.g. nRF Connect, "Serial Bluetooth Terminal") can talk
  to it too, which is handy for debugging.
* **USB**: pick *Device → USB cable* and the port (Windows `COMx`, Linux `/dev/ttyACM0`,
  macOS `/dev/cu.usbmodem…`). On macOS use this for now: the PC app's Bluetooth LE support covers
  Windows and Linux.

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
| Anywhere         | MAGIC / long MAGIC   | Runs what the PC app's *Magic button* page says (mute mic, play/pause, ...); the buddy sparkles and shows the result. |

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
| `src/Link.*`         | JSON-lines over BLE + USB (native + UART port), heartbeat     |
| `src/BleUart.*`      | Bluetooth LE Nordic UART Service (S3 has no Classic BT)       |
| `src/Displays.*`     | one/two OLEDs or the LCD, settings stored in flash            |
| `src/Lcd*.*`         | 1602/2004 LCD driver (PCF8574) and the 16x2 / 20x4 layouts    |
| `src/Buttons.*`      | debounced short/long presses                                  |
| `src/Face.*`         | procedural eyes, moods, gags, mini faces                      |
| `src/Apps.*`         | app store + the six visuals, marquee helper                   |
| `src/main.cpp`       | screens, slide transitions, toasts, takeovers, message handling |
