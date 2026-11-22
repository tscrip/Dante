# DANTE

### **D**esk **A**ssistant for **N**otifications, **T**asks & **E**fficiency

> "What's that on your desk?" "Oh, that's DANTE."

DANTE is a little desk companion with a personality. It's an ESP32-S3 board with a screen and four
buttons: it pulls faces, shows you the things you care about (your Claude usage, pull requests, your next
meeting, a Pomodoro timer...), and nudges you when something is waiting on you. All the thinking happens
in an app on your PC, which talks to DANTE over Bluetooth or USB. You add new things to it with
**plugins**, and you don't need to be a programmer to make one: Claude can build them for you (see
[Make your own plugin with Claude](#make-your-own-plugin-with-claude)).

```
┌────────────── Your PC (Windows / macOS / Linux) ──────────────┐   Bluetooth LE or USB
│  DANTE app  (http://127.0.0.1:5280 in your browser)           │  ◄─── small JSON messages ───►  ┌───────────┐
│   • Plugins: Claude Code, Azure DevOps, Calendar, Pomodoro... │                                 │ ESP32-S3  │
│   • Widgets: the screens DANTE shows, switched on and off     │                                 │  ◉   ◉    │
│   • Mood, quips and nudges                                    │                                 │ ◄  ●  ►   │
└───────────────────────────────────────────────────────────────┘                                 └───────────┘
```

## Contents

- [What it does](#what-it-does)
- [What you need](#what-you-need)
- [Wiring](#wiring)
- [Getting started](#getting-started)
- [Using DANTE](#using-dante)
- [Plugins](#plugins)
- [Make your own plugin with Claude](#make-your-own-plugin-with-claude)
- [Feeding DANTE without writing a plugin](#feeding-dante-without-writing-a-plugin)
- [Try it without hardware: the simulator](#try-it-without-hardware-the-simulator)
- [Troubleshooting](#troubleshooting)
- [For developers](#for-developers)
- [Why "DANTE"?](#why-dante)

## What it does

* **A face with moods.** Robot eyes that blink, glance around and react. The mood follows how many things
  are waiting on you and for how long: `happy → curious → antsy → grumpy → meltdown`. With no PC around it
  gets sleepy. On the colour screen the eyes change colour with the mood.
* **Widgets.** Screens you flip through with LEFT/RIGHT: gauges, bars, counters, text, sparklines (one or two), status
  icons and countdowns. Each widget has an **Active** switch, so DANTE only shows what you want right now.
* **Plugins.** Each one adds something: your Claude subscription usage, Claude Code sessions waiting on
  you, pull requests to review, meeting reminders from Outlook, a Pomodoro timer, countdowns to dates,
  any web API, any script. Every plugin has one settings page in the app.
* **Nudges.** When something needs you, DANTE takes over its screen and knocks until you answer:
  **SELECT** = "I'm on it" (and, for a meeting, it opens the Teams/Zoom link), **LEFT/RIGHT** = "later".
* **Quips.** When a number changes, DANTE says something about it in its personality (Cheerful,
  Sarcastic or Dramatic, or your own lines).
* **The MAGIC button.** A fourth button that does something on your PC: mute the microphone,
  play/pause music, open a website, run a command... It does the same thing on every screen, so it's
  always there when you need it (e.g. as a mute button); **SELECT** is the button that acts on what's
  on screen.
* **Looks.** Dress DANTE up: **Classic**, **Gentleman** (curly mustache, calm half-lidded eyes),
  **Nerd** (glasses, glances up while "thinking"), **Grumpy Uncle** (heavy eyebrows, bristly mustache) or
  **Party** (big eyes, a party hat, confetti). On the colour screen you can also pick its accent colour.
* **A simulator.** The real firmware running on your PC, so you can try everything without the hardware
  (see [Try it without hardware](#try-it-without-hardware-the-simulator)).

## What you need

| Part | Notes |
|---|---|
| **ESP32-S3 board with PSRAM** | e.g. an *ESP32-S3-DevKitC-1* with an **N16R8** or **N8R8** module (the `R8` means 8 MB PSRAM, which makes the colour screen smooth). Other S3 boards work if the pins below are free. |
| **A screen**, one of: | |
| • 3.5" colour TFT, ST7796, SPI, 480×320 (recommended) | the common 8-pin modules without touch, e.g. "3.5 inch SPI TFT ST7796" |
| • 0.91" OLED, SSD1306, I2C, 128×32 | small and cheap; monochrome |
| **4 push buttons** | LEFT, RIGHT, SELECT and MAGIC (any momentary buttons) |
| **Jumper wires** and a **USB-C cable** | the cable must carry data, not just power |
| **A PC** | Windows, Linux or macOS. Bluetooth works on Windows and Linux; on macOS use USB. |

**No soldering option:** the yellow **ESP32-3248S035C** board (sold as a "3.5 inch ESP32 display" with
capacitive touch; the model ends in **C**) has the 3.5" screen and a touch panel built in. Taps on the
screen stand in for the four buttons, so there's nothing to wire. Flash it with its own command (see
[Flash DANTE](#2-flash-dante)); details in [`firmware/README.md`](firmware/README.md#the-yellow-board-esp32-3248s035c-touch).

3D-printable cases are in [`case/`](case) (OpenSCAD models):

- **Yellow board:** [`case/cyd35_tilt_case.scad`](case/cyd35_tilt_case.scad) holds the ESP32-3248S035C at a
  60° tilt so the screen faces you. Print the three ready-made parts, all without supports:
  [`cyd35_front.stl`](case/cyd35_front.stl) (face down), [`cyd35_back.stl`](case/cyd35_back.stl) (flat side down)
  and [`cyd35_stand.stl`](case/cyd35_stand.stl) (on its base). Drop the board face-down into the front, screw the
  back on with four **M3×10 flat-head (countersunk) screws**, and stand the case in the stand with the USB port
  on the right. The screws cut their own thread in the plastic (or set `inserts = true` for heat-set inserts). If the picture is
  upside down, tick *Screen is mounted upside down* on the **Device** page. The angle, the USB side and the
  stand's size are settings at the top of the file. For CAD programs (Fusion, Onshape, FreeCAD...) the same three
  parts are there as STEP files (`cyd35_front.step`, `cyd35_back.step`, `cyd35_stand.step`); after changing the
  model, `case/make_step.sh` rebuilds the STL and STEP files.
- **Two small OLEDs:** [`case/dante_case.scad`](case/dante_case.scad).

## Wiring

Everything goes to the ESP32-S3. The GPIO numbers are printed next to the pins on the board (not the pin
positions). All pins are set in [`firmware/src/config.h`](firmware/src/config.h).

**3.5" TFT** (8-pin module). On these modules `SCL` and `SDA` are the SPI clock and data, not I2C.

| TFT pin | ESP32-S3 | |
|---|---|---|
| VCC | 3V3 | |
| GND | GND | |
| SCL | GPIO 12 | SPI clock |
| SDA | GPIO 11 | SPI data |
| RST | GPIO 14 | reset |
| DC | GPIO 13 | data / command |
| CS | GPIO 10 | chip select |
| BL | GPIO 21 | backlight (brightness in the app) |
| SDO (if there is one) | not connected | DANTE never reads from the screen |

**0.91" OLED**

| OLED pin | ESP32-S3 |
|---|---|
| VCC | 3V3 |
| GND | GND |
| SDA | GPIO 8 |
| SCL | GPIO 9 |

**Buttons**: each button goes between the GPIO and GND (no resistors needed).

| Button | ESP32-S3 |
|---|---|
| LEFT | GPIO 4 |
| RIGHT | GPIO 5 |
| SELECT (on top) | GPIO 6 |
| MAGIC (on top) | GPIO 7 |

Soldering tips, screen colour fixes and more detail are in [`firmware/README.md`](firmware/README.md).

## Getting started

### 1. Install the tools (once)

- **[.NET 10 SDK](https://dotnet.microsoft.com/download)**: runs the PC app.
- **[Visual Studio Code](https://code.visualstudio.com/)** with the **PlatformIO IDE** extension: builds
  and flashes the firmware. (If you prefer a terminal: install the PlatformIO Core CLI instead.)
- **Git**, to download the project:

  ```bash
  git clone https://github.com/tscrip/Dante.git
  ```

### 2. Flash DANTE

Plug the board into your PC (the port labelled **USB**). In VS Code open the `firmware` folder and click
the **→ Upload** arrow in the blue status bar, or in a terminal:

```bash
cd firmware
```

```bash
pio run -t upload
```

For the yellow touch board (ESP32-3248S035C) use this instead:

```bash
pio run -e esp32-3248s035c -t upload
```

The first build downloads the ESP32 tools and takes a few minutes. If the upload hangs on
"Connecting...", hold **BOOT**, tap **RST**, let go of **BOOT**, and upload again.

### 3. Run the PC app

```bash
cd pc
```

```bash
dotnet run --project src/Dante.Server
```

Your browser opens **http://127.0.0.1:5280**. Leave the app running while you use DANTE.

### 4. Connect and set up

1. **Device** page: choose **Bluetooth LE** (no pairing; *Scan* finds `DANTE`) or **USB cable** and its
   port. Then pick the **Screen type** (*TFT 3.5"* or *OLED*) and click **Save & send to DANTE**.
2. **Plugins** in the sidebar: open the ones you want and fill in their settings. For example, Claude Code
   → **Install hooks** so DANTE knows when a session is waiting for you.
3. **Widgets**: switch on the widgets you want to see and put them in order.

That's it. DANTE greets you, and LEFT/RIGHT flips through your active widgets.

## Using DANTE

| Where | Button | What happens |
|---|---|---|
| Anywhere | LEFT / RIGHT | Previous / next screen. The face is always first. |
| Anywhere | hold LEFT / RIGHT | Back to the face. |
| Face | SELECT | A random gag: heart eyes, dizzy, wink, laughing, surprised, "boop!". |
| A widget | SELECT | Opens its link on your PC (e.g. the oldest pull request). |
| A timer widget | SELECT / hold SELECT | Start or pause the timer / reset it. |
| Anywhere else | hold SELECT | Refresh everything now. |
| Anywhere | MAGIC / hold MAGIC | Whatever you set on the *Magic button* page (mute mic, play/pause...), on every screen. |
| A nudge | SELECT | "I'm on it." |
| A nudge | LEFT / RIGHT | "Later." |
| A speech bubble | any | Dismiss it. |

The pages of the PC app:

| Page | What it's for |
|---|---|
| **Dashboard** | What DANTE shows right now, its mood, test buttons. |
| **Widgets** | Every widget with its **Active** switch and its order on DANTE; *+ Add widget*. |
| **Plugins** | One page per plugin: its settings, anything special it needs, and its widgets. |
| **Mood & personality** | When DANTE gets antsy or grumpy, how it talks, and its look. |
| **Magic button** | What the MAGIC button does on your PC. |
| **Simulator** | DANTE running on your PC, with clickable buttons (see below). |
| **Device** | Connection, screen type, brightness, "Check wiring", message log. |

## Plugins

| Plugin | Widgets | On its page |
|---|---|---|
| **Claude Code** | *Subscription usage* (gauge: 5-hour window, week or Opus week) · *Sessions waiting on me* (nudges you) | Claude login (found automatically), hook installer, live list of waiting sessions |
| **Azure DevOps** | *Pull requests* (to review, yours waiting on you, or both) | organisation URL, personal access token |
| **Calendar** | *Next meeting* (countdown) | Outlook / Microsoft 365 sign-in, reminder times (e.g. 10 and 1 minutes before), which meetings to skip |
| **Pomodoro** | *Timer* | focus / break lengths, long breaks, auto-start; live timer with start, skip and reset |
| **System performance** | *CPU & memory* (two live graphs on one screen, or just one) | live CPU and memory of this PC; works on Windows, macOS and Linux with nothing to install |
| **Days until** | *Countdown to a date* (one per date) | — (each widget has its event and date) |
| **Web API (JSON)** | *JSON value* (one per URL) | — (each widget has its URL and which field to show) |
| **Command** | *Command output* (one per command) | — (each widget has its program) |
| **REST push** | *Pushed value* (one per thing you push) | — |

Tips:
- **Secrets** (tokens, passwords) can be written as `env:VARIABLE_NAME` in any secret field, so they stay
  in an environment variable instead of the config file.
- **Calendar** needs a free "app registration" in Microsoft Entra; the steps are on its plugin page.
- **Days until** is the example plugin: small, fully commented, and the template for new ones.

## Make your own plugin with Claude

You don't need to know C# or electronics to add things to DANTE. This repository has a
[`CLAUDE.md`](CLAUDE.md) that tells Claude how the project works and the rules to follow, so you can
describe what you want in plain language and Claude builds it, tests it, and tells you how to try it.

**How:**

1. Open the project folder in [Claude Code](https://claude.com/claude-code) (in a terminal: `cd` into
   the `Dante` folder and run `claude`; or use the desktop or web app).
2. Describe what you'd like DANTE to show or do. Say what you know: where the data comes from, what
   should happen when it changes, whether it should nudge you. Claude will ask about anything unclear.
3. When it's done, restart the PC app, open the new plugin under **Plugins**, fill in any settings, and
   add its widget.

**Ideas to try:**

- "Make a plugin that shows how many unread GitHub notifications I have, and nudges me when there are more than 5."
- "Add a weather plugin: the temperature outside in my city as a gauge, from Open-Meteo (no API key)."
- "Show my Home Assistant front door sensor: a status widget that turns red when the door is open."
- "A plugin that counts my open Jira tickets assigned to me, with a link to the board on SELECT."
- "Make DANTE remind me to drink water every hour, as a nudge I can snooze."
- "Add a widget that shows the Bitcoin price as a sparkline."

**Good to know:**

- New plugins live entirely in the PC app; you don't need to reflash DANTE for them.
- You can try a new plugin on the [simulator](#try-it-without-hardware-the-simulator) before touching the
  real DANTE.
- Claude writes tests and runs them. It can't see your DANTE, so it will tell you what to check on the
  device.
- If a plugin needs an account or API key, Claude will tell you where to get it and where to paste it.
- Want to change how DANTE itself looks or behaves (a new kind of screen, different eye colours)? Ask
  too; that changes the firmware, so you'll reflash afterwards ([step 2](#2-flash-dante)).

## Feeding DANTE without writing a plugin

Three built-in plugins let you show your own data straight away:

* **REST push**: any script or tool can send a value to a *Pushed value* widget:

  ```bash
  curl -X POST http://127.0.0.1:5280/api/widgets/my_widget/value -H "Content-Type: application/json" -d '{"value": 2, "text": "two builds red"}'
  ```

  Add `"pending": [{"key": "b1", "title": "Nightly", "since": "2026-11-11T08:00:00Z", "url": "https://ci/b1"}]`
  for things that are waiting on you: they drive the mood and "something is waiting" nudges.
* **Web API (JSON)**: polls a URL and picks a value with a path like `data.items[0].count` (or
  `$count:items` to count a list).
* **Command**: runs any program; it prints `3`, `3 some text`, or `{"value":3,"text":"..."}`.

Also: `POST /api/say {"text":"..."}` makes DANTE say something, and `GET /api/state` returns everything.

## Try it without hardware: the simulator

The **DANTE simulator** is the real firmware compiled for your PC: the same face, moods, looks, widgets,
countdowns and nudges as the board, drawn by the same code (u8g2 for the OLED, LovyanGFX for the 3.5"
TFT). What you see is what the hardware will do. It's also the quickest way to try a new plugin.

1. Build it once. This needs a C/C++ compiler: gcc on Linux, the Xcode command line tools on macOS, or
   MinGW-w64 on Windows.

   ```bash
   cd firmware
   ```

   ```bash
   pio run -e simulator
   ```

2. In the PC app open **Simulator**, pick the hardware (3.5" TFT, 0.91" OLED, or the 3.5" touch screen of the
   yellow board) and click **Start simulator**. The app runs the program itself.
3. Click DANTE's buttons on screen (hold for a long press) or use the keyboard: ←/→ = LEFT/RIGHT,
   Enter/Space = SELECT, M = MAGIC. With the yellow board there are no buttons: click the screen itself
   (click = SELECT; drag left/right to swipe, drag down for the face; right-click = two-finger tap = MAGIC).

Everything else (widgets, plugins, Claude Code hooks, Pomodoro, the Magic button) works with the
simulator exactly as with a real DANTE. After changing firmware code, rebuild and press **Power-cycle**.
Switch back to Bluetooth or USB on the *Device* page.

## Troubleshooting

| Problem | Try |
|---|---|
| The app says DANTE is offline | Device page: check Bluetooth/USB and click **Reconnect**. Only one app can use the USB port: close `pio device monitor`. |
| Bluetooth doesn't find DANTE | On macOS, use USB. Elsewhere, make sure DANTE isn't connected to another PC. |
| TFT stays white or black | Check RST, DC and CS first, and that brightness isn't 0. **Device → Check wiring** lists the pins. |
| TFT colours are wrong | Flip `TFT_INVERT` (black shows as white) or `TFT_BGR` (red/blue swapped) in `firmware/src/config.h`, then reflash. |
| OLED stays dark | **Device → Check wiring** scans the I2C bus and says what it finds. |
| A widget shows an error | Open it (Widgets → its name) and press **Test**: the message says what to fix. |
| Is it running smoothly? | In `pio device monitor` type `?`: the line ends with the screen mode and frames per second. |

More hardware troubleshooting is in [`firmware/README.md`](firmware/README.md).

## For developers

| Folder | What |
|---|---|
| [`pc/`](pc) | .NET 10 solution. `Dante.Core` is the kernel (config, device link, protocol, engine, plugin and widget abstractions); `Dante.Server` is the web app, where every plugin and page is a vertical slice under `Features/<Name>/`; `tests/Dante.Tests` has the xUnit tests. |
| [`firmware/`](firmware) | PlatformIO / Arduino firmware for the ESP32-S3: face, widget visuals, buttons, OLED and TFT drivers. |
| [`docs/protocol.md`](docs/protocol.md) | The JSON-lines protocol between the PC app and DANTE. |
| [`docs/research/custom-plugins.md`](docs/research/custom-plugins.md) | Research on plugins defined in the UI (call an API, transform the response with a script). |
| [`case/`](case) | OpenSCAD case models: the tilted case for the yellow board, and the two-OLED box. |
| [`CLAUDE.md`](CLAUDE.md) | Architecture, standards and the step-by-step recipe for adding a plugin. |

```bash
cd pc && dotnet test        # all PC tests (engine, plugins, protocol, migrations, end-to-end with a fake DANTE)
```

```bash
cd firmware && pio run      # build the firmware
```

Publish a self-contained PC app with e.g.
`dotnet publish src/Dante.Server -c Release -r win-x64 --self-contained` (or `osx-arm64`, `linux-x64`).
Windows builds use the `net10.0-windows10.0.19041.0` target, which Bluetooth LE needs. The config file is
`%APPDATA%\Dante\config.json` (Windows) or `~/.config/Dante/config.json` (Linux, macOS); override it
with `DANTE_CONFIG`. The server listens on `127.0.0.1` only.

### Upgrading from older versions

Your settings come along automatically:

* **Apps → widgets:** `config.json` is converted on first start, and settings that several widgets
  shared (the Azure DevOps organisation and token, the Claude token) move to their plugin page.
* **One screen (3.0) and no character LCDs (3.5):** DANTE drives one screen, the OLED or the TFT. A
  config set to a 1602/2004 LCD or to two screens goes back to *Automatic*, keeping "upside down".
* **DeskBuddy / HANK:** the project had those names before. The PC app copies an old config over on first
  start, old `HANK_*` / `DESKBUDDY_*` environment variables still work, and a saved device name is renamed.

Reflash the firmware when you update the PC app: they are released together.

## Why "DANTE"?

**The acronym.** Every letter is part of the job:

| | | |
|---|---|---|
| **D** | **Desk** | It lives on your desk, in the corner of your eye, not in yet another browser tab. |
| **A** | **Assistant** | Your PC does the work (polling Azure DevOps, watching Claude Code, reading your usage) and DANTE tells you what matters. |
| **N** | **Notifications** | Pull requests, a Claude Code session waiting on you, a usage gauge creeping up, delivered with a quip, or as a nudge that takes over the face. |
| **T** | **Tasks** | It counts what's waiting on you and for how long, and its mood follows: happy, curious, antsy, grumpy, meltdown. |
| **E** | **Efficiency** | Glance instead of context-switching, and get pulled back to the thing that's blocked on *you*. |

**The name.** *Dante* is the medieval Italian short form of *Durante*, from the Latin *durans*:
**"enduring, steadfast"**. That's the job description. DANTE doesn't forget, doesn't get
bored, and keeps nudging, patiently and persistently, until the thing waiting on you is done.

And yes, it's a nod to Dante Alighieri, who walked through all nine circles of the *Inferno*. DANTE
will walk you through yours: the PR queue, the blocked agent, the 90% usage gauge.
