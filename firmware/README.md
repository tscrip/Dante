# DANTE firmware (ESP32-S3)

**D**esk **A**ssistant for **N**otifications, **T**asks & **E**fficiency. See the [main README](../README.md)
for what DANTE is, the parts list and getting started.

The firmware draws DANTE's face and widget screens on one screen (a 0.91" 128×32 OLED or a 3.5" 480×320
colour TFT), reads the four buttons, and talks to the PC app over Bluetooth LE or USB. The PC app does
all the thinking; the device just draws what it is told and reports button presses.

Protocol: [`../docs/protocol.md`](../docs/protocol.md). Pins and timings: [`src/config.h`](src/config.h).

## Hardware

Board: **ESP32-S3**, built for the ESP32-S3-DevKitC-1 with an octal-PSRAM module (`R8`: N8R8 or N16R8).
Any S3 board works if the pins below are free. The S3 has Bluetooth LE but **no Bluetooth Classic**, so
DANTE talks to the PC over BLE (Nordic UART Service) or over USB.

| Part | ESP32-S3 pin | Notes |
|---|---|---|
| LEFT button | GPIO 4 | other leg to GND (internal pull-up) |
| RIGHT button | GPIO 5 | other leg to GND |
| SELECT button (top) | GPIO 6 | other leg to GND |
| MAGIC button (top) | GPIO 7 | other leg to GND; what it does is set in the PC app |

The pins avoid the S3's strapping pins (0, 3, 45, 46), native USB (19/20), UART0 (43/44) and GPIO 26-37,
which the flash and octal PSRAM use.

DANTE drives **one screen**: the 3.5" TFT or the 0.91" OLED. Pick it in the PC app (*Device → Screen
type*). With *Automatic*, DANTE uses the OLED if it answers on I2C, and otherwise the TFT (an SPI screen
can't be detected, so picking *TFT 3.5"* explicitly is better).

### 3.5" colour TFT (ST7796, 480×320, SPI)

The common 8-pin 3.5" SPI modules (ST7796 driver, no touch). The eyes fill the screen and take the colour
of the mood (teal when happy, through yellow and orange, to red in a meltdown), a status bar on top shows
the mood, the link and how many things are waiting, and widget screens, bubbles and nudges are drawn 3.75×
bigger in colour (amber for a warning, red when critical).

| TFT pin (label on the module) | ESP32-S3 pin | Notes |
|---|---|---|
| `VCC` | 3V3 | the logic is 3.3 V |
| `GND` | GND | |
| `SCL` (= SPI clock) | GPIO 12 | the S3's native FSPI pin |
| `SDA` (= SPI data / MOSI) | GPIO 11 | |
| `RST` | GPIO 14 | |
| `DC` | GPIO 13 | data / command |
| `CS` | GPIO 10 | |
| `BL` | GPIO 21 | backlight, dimmed with the PC app's brightness slider |
| `SDO` (if present) | not connected | DANTE never reads from the panel |

On these modules `SCL`/`SDA` are the SPI clock and data, not I2C: don't connect them to GPIO 8/9. The
backlight draws roughly 60–100 mA from 3V3, which DevKit boards handle.

**Panels vary a little:** if black shows as white, flip `TFT_INVERT` in [`src/config.h`](src/config.h);
if red and blue are swapped, flip `TFT_BGR`. With short wires you can raise `TFT_SPI_HZ` from 40 MHz to
80 MHz for even smoother animation (go back if you see garbage).

**Smoothness and PSRAM.** With octal PSRAM (which `platformio.ini` is set up for) DANTE draws each whole
frame in PSRAM, compares it with the previous one, and sends only the part of the screen that changed, by
DMA, at about 30 fps. The eyes move one screen pixel at a time with anti-aliased edges. Without PSRAM it
still works: it draws in strips of 64 rows (61 KB of RAM) and skips strips that didn't change. For a
quad-PSRAM `R2` module change `board_build.arduino.memory_type` in `platformio.ini` to `qio_qspi`.

**Soldering the header.** For a case, fit the pin header from the *back* of the module (plastic strip on
the back, pins pointing backwards) and solder on the front. The glass and its thin ribbon cable are very
close to the pins: keep each joint to 2–3 seconds and the iron off the ribbon. Heat damage there gives a
permanently white screen. A right-angle header, or wires soldered straight into the holes, save depth.

### 0.91" OLED (SSD1306, 128×32, I2C)

| OLED pin | ESP32-S3 pin | Notes |
|---|---|---|
| `VCC` | 3V3 | |
| `GND` | GND | |
| `SDA` | GPIO 8 | I2C, 400 kHz |
| `SCL` | GPIO 9 | |

Modules answer at I2C address `0x3C`, or `0x3D` with the address jumper moved; both are found automatically.

## Build and flash

```bash
cd firmware
```

```bash
pio run -t upload
```

That builds and flashes over USB. Use the board's port labelled **USB** (the **UART** port also works).
`pio run` only builds. Or open the `firmware` folder in VS Code with the PlatformIO extension and click
the **→ Upload** arrow. The first build downloads the ESP32 toolchain and libraries (a few minutes).

If an upload doesn't start, hold **BOOT**, tap **RST**, release **BOOT**, and upload again.

Watch what DANTE says:

```bash
pio device monitor
```

Type `?` and press Enter for a status line (version, what was found, the screen in use, frames per
second). Close the monitor before starting the PC app: only one program can use the USB port.

The environment is `esp32-s3` (board `esp32-s3-devkitc-1`, octal PSRAM, `huge_app.csv` partitions for
the Bluetooth stack). `ARDUINO_USB_CDC_ON_BOOT=1` makes the native **USB** port the main serial port.

## Blank screen?

Easiest: **Device → Check wiring** in the PC app. It asks DANTE what it found at boot and scans its I2C
bus *right now* (no reset needed), and explains the result. Without the PC app, open
`pio device monitor`, type `?` and press Enter. (After **RST** the startup line is often missed, because
the S3's native USB reconnects; `?` prints it again.) For example:

```
DANTE 3.5.0: I2C (SDA=8 SCL=9): OLED none | TFT (SPI, SCK=12 MOSI=11 CS=10 DC=13 RST=14 BL=21) | screen: tft (set by PC app) | last reset: power on | TFT: PSRAM frame buffer, 30 fps, 38 KB per frame sent, SPI 40 MHz
I2C scan now: bus 1 (SDA=8 SCL=9): nothing
```

* **TFT stays white (or black):** the scan can't see SPI screens. Check the six signal wires in the TFT
  table above, that *Screen type* is *TFT 3.5"*, and that brightness isn't 0. White usually means `RST`,
  `CS` or `DC` is wrong or not connected. If another module works with the same wiring, the first one is
  damaged (often by heat while soldering).
* **TFT colours look wrong:** see `TFT_INVERT` / `TFT_BGR` above.
* **`strips (no PSRAM)` on an R8 module:** the PSRAM setting in `platformio.ini` doesn't match the module.
* **`NO MEMORY FOR THE FRAME BUFFER`:** please report it, with the module marking.
* **OLED: nothing found on I2C:** check SDA → GPIO 8, SCL → GPIO 9, power and GND.
* **Power:** if the line ends in `last reset: BROWNOUT`, the supply dipped. Try another USB cable or port.

## Connecting to the PC

DANTE speaks the same JSON-lines protocol over **Bluetooth LE** and over **USB**, and always answers on
the link it last heard from.

* **Bluetooth LE** (default): DANTE advertises as `DANTE` with the Nordic UART Service
  (`6E400001-B5A3-F393-E0A9-E50E24DCCA9E`; the PC writes to `…0002`, DANTE notifies on `…0003`).
  **No pairing**: in the PC app pick *Device → Bluetooth LE* (*Scan* finds it). Works from Windows and
  Linux (BlueZ). Any BLE terminal app (e.g. nRF Connect) can talk to it too, which is handy for debugging.
* **USB**: pick *Device → USB cable* and the port (Windows `COMx`, Linux `/dev/ttyACM0`, macOS
  `/dev/cu.usbmodem…`). On macOS use this: the PC app's Bluetooth LE support covers Windows and Linux.

If no message arrives from the PC for 30 s DANTE decides it is alone: it gets sleepy and shows "no PC".

## Controls

| Where | Button | What happens |
|---|---|---|
| Any screen | LEFT / RIGHT | Previous / next screen. The face is screen 0, the active widgets follow. |
| Any screen | long LEFT / RIGHT | Jump home to the face. |
| Face | SELECT | Play a random gag. |
| Widget screen | SELECT | Acts on what's on screen: the PC opens it (e.g. the PR list). |
| Timer screen | SELECT / long SELECT | Start or pause the countdown / reset it (the Pomodoro timer). |
| Any other screen | long SELECT | Tells the PC "refresh now". |
| Speech bubble | any | Dismiss it. |
| Nudge | SELECT | "I'm on it": `ack` to the PC, DANTE winks. |
| Nudge | LEFT / RIGHT | "Later": `snooze` to the PC. |
| Any screen | MAGIC / long MAGIC | Always runs what the PC app's *Magic button* page says, whatever is on screen; DANTE sparkles and shows the result. |

Every press is reported to the PC as a `btn` event (nudge presses as `ack` / `snooze`).

## Personality

The PC decides the mood from how many things are waiting on you and how long they have been waiting.
Each mood has its own eyes and idle habits (and, on the TFT, its own colour):

| Mood | Look | Habits |
|---|---|---|
| `happy` | `^ ^` cheeks | little hops, winks, sparkles |
| `curious` | one eye bigger than the other | quick glances around, swaps brows, "?" |
| `antsy` | worried outer lids | keeps glancing at *you*, foot-tapping shuffle, "!!" |
| `grumpy` | angry inner lids, squinting | long judgemental side-eye, slow blinks, huffs |
| `meltdown` | wide eyes | shaking, sweat drop, flashes of X eyes, "!!" |
| sleepy | heavy lids (no PC connected) | floating "z"s, nodding off and snapping awake |

**Gags** (SELECT on the face): heart eyes, dizzy spirals, wink, laughing `> <` with "ha ha", surprised
double-take and "boop!" squish.

**Looks** (set on the PC app's *Mood & personality* page): Classic, Gentleman (curly mustache, calm
half-lidded eyes), Nerd (glasses), Grumpy Uncle (heavy eyebrows, bristly mustache) and Party (big eyes,
a party hat, confetti). They change the eyes, the habits and the accessories, on the OLED and the TFT.
On the TFT the accent colour (title, happy eyes, highlights) is also set from the PC app.

**Speech bubbles**: when the PC sends a value with a `quip` (or a `say`), a mini face pops up with a
scrolling speech bubble for about 4.5 s, and the big face does a double-take afterwards.

**Nudges** (`alert`): take over the whole screen. A mini face bounces and stares at you, the message
scrolls, and every 3 s DANTE "knocks" (shakes the screen). Urgent ones also flash. Several can be queued;
the most urgent (then the newest) is shown first.

## Widget visuals

Each widget picks a visual (called `vis` in the protocol):

| `vis` | Shows |
|---|---|
| `gauge` | semicircle gauge with a sweeping needle, big value + unit, small text |
| `bar` | big value, horizontal progress bar |
| `counter` | big number that drops in with a bounce and a floating `+1!` |
| `text` | scrolling text |
| `spark` | sparkline of the last 32 values |
| `spark2` | two sparklines, one per line, each with its label and value (e.g. CPU and memory); on the TFT the top line is in the accent colour |
| `status` | OK / warning / error icon plus scrolling text |
| `timer` | countdown (m:ss, or 2h05) with a progress bar; DANTE ticks it down itself |

Values tween smoothly, and critical values blink.

## Source layout

| File | Purpose |
|---|---|
| `src/config.h` | pins, timings, limits, firmware version |
| `src/main.cpp` | messages from the PC, buttons, screens, slides, bubbles, nudges, the TFT layout |
| `src/Link.*` | JSON lines over BLE + USB (native and UART port), heartbeat |
| `src/BleUart.*` | Bluetooth LE Nordic UART Service (the S3 has no Classic BT) |
| `src/Displays.*` | the one screen (OLED or TFT), settings stored in flash, wiring report |
| `src/Gfx.h` | drawing interface: the 128×32 layouts, drawn 1:1 on the OLED or scaled on the TFT |
| `src/Tft.*` | 3.5" ST7796 TFT (LovyanGFX): colour canvas, PSRAM frame buffer / strips, DMA |
| `src/Buttons.*` | debounced short / long presses |
| `src/Face.*` | procedural eyes, moods, gags, mini faces |
| `src/Apps.*` | widget store and the eight visuals, marquee helper |
| `src/SimMain.cpp` | the simulator's `main()` (empty in the ESP32 build) |
| `simulator/host/` | stand-ins for the Arduino / ESP32 headers, used only by the simulator build |

## Simulator

`pio run -e simulator` builds this firmware as a program for your PC (PlatformIO's `native` platform,
so you need a C/C++ compiler: gcc, the Xcode command line tools, or MinGW-w64 on Windows). The DANTE PC
app starts it from its **Simulator** page; see the
[main README](../README.md#try-it-without-hardware-the-simulator).

How it works:

* `simulator/host/` stands in for the Arduino/ESP32 headers (only the simulator build sees it):
  real-time `millis()`/`delay()`, `Serial` = stdin/stdout, buttons driven by the PC app, plenty of
  "PSRAM", and an I2C bus where the simulated OLED answers, so the firmware's own screen detection runs
  unchanged.
* The OLED is drawn by u8g2 exactly as on the board. The TFT is drawn by the real LovyanGFX into a
  memory-backed panel (`Tft.cpp`, `DANTE_SIMULATOR`), the only firmware code with a simulator branch.
* `src/SimMain.cpp` provides `main()`: it runs `setup()`/`loop()`, passes protocol lines through,
  handles `sim.btn`, and sends the screen as `sim.frame` whenever it changes.
* Choose the screen with `--hw tft|oled`. You can also run it by hand and type protocol lines, e.g.
  `{"t":"hello"}` or `?`.

To change what the device draws (a new visual, a new face behaviour), see "Firmware standards" in
[`../CLAUDE.md`](../CLAUDE.md).
