# DANTE firmware (ESP32-S3)

**D**esk **A**ssistant for **N**otifications, **T**asks & **E**fficiency. See the [main README](../README.md) for the name.

A tiny desk companion with opinions: animated robot eyes on one screen (a 0.91" 128x32 OLED, a
1602/2004 character LCD, or a 3.5" 480x320 colour TFT), four buttons (incl. a MAGIC button), and a Bluetooth LE link to the DANTE PC app, which does all the thinking and
pushes values, moods and nudges to the device.

Protocol: [`../docs/protocol.md`](../docs/protocol.md)

## Hardware

Board: **ESP32-S3** (built for the ESP32-S3-DevKitC-1; any S3 board works if the pins below
are free). The S3 has Bluetooth LE but **no Bluetooth Classic**, so DANTE talks to the PC
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

DANTE drives **one screen**: the OLED above, a character LCD, or the 3.5" TFT (both below).

### Alternative screen: 1602 (16×2) or 2004 (20×4) LCD

A standard character LCD with a PCF8574 I2C backpack works instead of the OLED: 16×2 ("1602")
or 20×4 ("2004"). Pick **Screen type → LCD 1602** or **LCD 2004** in the PC app (*Device →
Screen*) and set its address (0x27 for PCF8574
backpacks, 0x3F for PCF8574A). Before it has been configured, DANTE uses whichever screen it finds
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

### Alternative screen: 3.5" colour TFT (ST7796, 480×320, SPI)

The common 8-pin 3.5" SPI modules (ST7796 driver, no touch) work as DANTE's screen. The eyes fill the
screen and take the colour of the mood (teal when happy, through yellow and orange, to red in a
meltdown), a status bar on top shows the mood, the link and how many things are waiting, and app
screens, bubbles and nudges are drawn 3.75× bigger in colour (amber for a warning, red when critical).

| TFT pin (label on the module) | ESP32-S3 pin | Notes |
|-------------------------------|--------------|-------|
| `VCC`                         | 3V3          | the logic is 3.3 V |
| `GND`                         | GND          | |
| `SCL` (= SCK)                 | GPIO 12      | SPI clock (the S3's native FSPI pin) |
| `SDA` (= MOSI / SDI)          | GPIO 11      | SPI data |
| `RST`                         | GPIO 14      | |
| `DC` (or `RS`)                | GPIO 13      | data / command |
| `CS`                          | GPIO 10      | |
| `BL` (or `LED`)               | GPIO 21      | backlight, dimmed with the PC app's brightness slider |

Pick **Screen type → TFT 3.5"** in the PC app. An SPI screen can't be detected, so *Automatic* only
uses the TFT when no OLED or LCD answers on I2C; picking it explicitly is better. "Upside down" in the
PC app rotates it 180°. The backlight draws roughly 60–100 mA from 3V3, which DevKit boards handle.

Panels vary a little: if black shows as white, flip `TFT_INVERT` in [`src/config.h`](src/config.h);
if red and blue are swapped, flip `TFT_BGR`. With short wires you can raise `TFT_SPI_HZ` to 80 MHz for
smoother animation.

**PSRAM makes it smooth.** On modules with octal PSRAM (`R8`, e.g. N8R8 or N16R8, which `platformio.ini`
is set up for) DANTE draws each whole frame in PSRAM, compares it with the previous one and sends only the
part of the screen that changed, by DMA, at about 30 fps. On the TFT the eyes also move one screen pixel at
a time with anti-aliased edges, instead of jumping in OLED-sized steps. Without PSRAM it still works: it
draws in strips of 64 rows (61 KB of RAM) and skips strips that didn't change. For a quad-PSRAM `R2`
module, change `board_build.arduino.memory_type` in `platformio.ini` to `qio_qspi`.

To see how it runs, type `?` in `pio device monitor` (or use *Check wiring* in the PC app). The first
line ends with e.g. `TFT: PSRAM frame buffer, 30 fps, 38 KB per frame sent, SPI 40 MHz`. If it says
`strips (no PSRAM)` on an R8 module, the PSRAM setting doesn't match the module.

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

Easiest: **Device → Check wiring** in the PC app. It asks DANTE to scan its I2C bus *right now*
(no reset needed) and explains the result. Without the PC app, open `pio device monitor`, type `?` and
press Enter. (After **RST** the startup line itself is often missed, because the S3's native USB
reconnects; `?` prints it again.) The first line says what DANTE found on the I2C bus and which screen
it is driving, e.g.

```
DANTE 3.0.0: I2C (SDA=8 SCL=9): OLED none, LCD 0x27 | TFT (SPI, SCK=12 MOSI=11 CS=10 DC=13 RST=14 BL=21) | screen: lcd1602 (auto) | last reset: power on
I2C scan now: bus 1 (SDA=8 SCL=9): 0x27 (LCD)
```

* **Power?** A 0.91" OLED draws about 20 mA and the TFT's backlight 60–100 mA, both fine from the
  board's 3V3 pin. If the power ever does dip, the line ends in `last reset: BROWNOUT`.
  Try another USB cable or port then.
* **Nothing found:** check SDA → GPIO 8, SCL → GPIO 9, power and GND (the old ESP32 build used
  21/22).
* **Found, but the other screen is used:** in the PC app set *Device → Screen type* to
  *Automatic* or the right type and save; DANTE remembers it.
* **1602 found but blank:** turn the contrast potentiometer on the backpack. The LCD needs 5 V,
  and check the I2C level shifting described above.
* **1602 shows a row of solid blocks:** it has power but never got initialised. Usually the wrong
  address or the SDA/SCL wires are swapped.
* **TFT stays white (or black):** the scan can't see SPI screens. Check the six signal wires in the
  TFT table (the module's `SCL`/`SDA` are the SPI clock and data, not I2C), that *Screen type* is
  *TFT 3.5"*, and that brightness isn't 0. A white screen usually means `RST`, `CS` or `DC` is off. If
  the `?` line ends in `NO MEMORY FOR THE FRAME BUFFER`, please report it.
* **TFT colours look wrong:** see `TFT_INVERT` / `TFT_BGR` in the TFT section.

## Simulator

`pio run -e simulator` builds this firmware as a program for your PC (PlatformIO's `native`
platform, so you need a C/C++ compiler: gcc, the Xcode command line tools, or MinGW-w64 on Windows).
The DANTE PC app starts it from its **Simulator** page; see the [main README](../README.md#develop-without-hardware-the-simulator).

How it works:

* `simulator/host/` stands in for the Arduino/ESP32 headers (only the simulator build sees it):
  real-time `millis()`/`delay()`, `Serial` = stdin/stdout, buttons driven by the PC app, plenty of
  "PSRAM", and an I2C bus where the simulated screen answers, so the firmware's own screen detection
  runs unchanged.
* The OLED and LCD are drawn by u8g2 exactly as on the board. The TFT is drawn by the real LovyanGFX
  into a memory-backed panel (`Tft.cpp`, `DANTE_SIMULATOR`), the only firmware code with a simulator branch.
* `src/SimMain.cpp` (empty in the ESP32 build) provides `main()`: it runs `setup()`/`loop()`, passes
  protocol lines through, handles `sim.btn`, and sends the screen as `sim.frame` whenever it changes.
* Choose the screen with `--hw tft|oled|lcd`. You can also run it by hand and type protocol lines,
  e.g. `{"t":"hello"}` or `?`.

## Connecting to the PC

DANTE speaks the same JSON-lines protocol over **Bluetooth LE** and over **USB**, and always
answers on the link it last heard from.

* **Bluetooth LE** (default): DANTE advertises as `DANTE` with the Nordic UART Service
  (`6E400001-B5A3-F393-E0A9-E50E24DCCA9E`; the PC writes to `…0002`, DANTE notifies on `…0003`).
  **No pairing**: in the PC app pick *Device → Bluetooth LE* (*Scan* finds it). Works from Windows
  and Linux (BlueZ). Any BLE terminal app (e.g. nRF Connect, "Serial Bluetooth Terminal") can talk
  to it too, which is handy for debugging.
* **USB**: pick *Device → USB cable* and the port (Windows `COMx`, Linux `/dev/ttyACM0`,
  macOS `/dev/cu.usbmodem…`). On macOS use this for now: the PC app's Bluetooth LE support covers
  Windows and Linux.

If no message arrives from the PC for 30 s DANTE decides it is alone: it gets sleepy
and blinks a small Bluetooth "no PC" sign in the corner.

## Controls

| Where            | Button               | What happens                                                    |
|------------------|----------------------|-----------------------------------------------------------------|
| Any screen       | LEFT / RIGHT         | Previous / next screen (slides). Face is screen 0, apps follow. |
| Any screen       | long LEFT / RIGHT    | Jump home to the face.                                          |
| Face             | SELECT               | Play a random gag.                                              |
| App screen       | SELECT               | Acts on what's on screen: the PC opens it (e.g. the PR list).   |
| Timer screen     | SELECT / long SELECT | Start or pause the countdown / reset it (the Pomodoro timer).   |
| Any other screen | long SELECT          | Tells the PC "refresh now".                                     |
| Speech bubble    | any                  | Dismiss it.                                                     |
| Takeover (nudge) | SELECT               | "I'm on it" -> `ack` to the PC, DANTE winks.                     |
| Takeover (nudge) | LEFT / RIGHT         | "Later" -> `snooze` to the PC.                                  |
| Any screen       | MAGIC / long MAGIC   | Always runs what the PC app's *Magic button* page says (mute mic, play/pause, ...), whatever is on screen; DANTE sparkles and shows the result. |

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
the message scrolls, and every 3 s DANTE "knocks" (shakes the screen). Level-2 alerts
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
| `spark2`  | two sparklines, one per line, each with its label and value (e.g. CPU and memory); on the TFT the top line is in the accent colour |
| `status`  | OK / warning / error icon plus scrolling text                         |
| `timer`   | countdown (m:ss, or 2h05) with a progress bar; DANTE ticks it down itself |

Values tween smoothly, and level-2 (critical) values blink.

## Source layout

| File                 | Purpose                                                       |
|----------------------|---------------------------------------------------------------|
| `src/config.h`       | pins, timings, limits                                         |
| `src/Link.*`         | JSON-lines over BLE + USB (native + UART port), heartbeat     |
| `src/BleUart.*`      | Bluetooth LE Nordic UART Service (S3 has no Classic BT)       |
| `src/Displays.*`     | the one screen (OLED, LCD or TFT), settings stored in flash   |
| `src/Gfx.h`          | drawing interface: the OLED layouts, drawn 1:1 or scaled      |
| `src/Tft.*`          | 3.5" ST7796 TFT (LovyanGFX): scaled colour canvas, strips     |
| `src/Lcd*.*`         | 1602/2004 LCD driver (PCF8574) and the 16x2 / 20x4 layouts    |
| `src/Buttons.*`      | debounced short/long presses                                  |
| `src/Face.*`         | procedural eyes, moods, gags, mini faces                      |
| `src/Apps.*`         | app store + the seven visuals, marquee helper                 |
| `src/main.cpp`       | screens, slide transitions, toasts, takeovers, message handling |
