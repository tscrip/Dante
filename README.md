# DANTE

### **D**esk **A**ssistant for **N**otifications, **T**asks & **E**fficiency

> "What's that on your desk?" "Oh, that's DANTE."

A little desk companion with a personality: an ESP32-S3 with one or two 0.91" 128×32 OLEDs and four buttons that
sits on your desk, pulls faces, and nudges you when things are waiting on you. All the thinking happens
on your PC, which pushes simple values to DANTE over Bluetooth LE (or USB).

```
┌──────────────── PC (Windows / macOS / Linux) ────────────────┐   Bluetooth LE (NUS) / USB
│  Dante.Server  (.NET 10, ASP.NET Core + Blazor)              │  ◄──── JSON lines ────►  ┌────────────┐
│   • Config UI in your browser: http://127.0.0.1:5280         │                          │  ESP32-S3  │
│   • Data sources: Claude Code usage, Claude Code "waiting",  │                          │  ◉ ◉  face │
│     Azure DevOps PRs, HTTP JSON, command, REST push          │                          │  ◄ ● ►     │
│   • Engine: levels, quips, mood, take-over nudges            │                          └────────────┘
└──────────────────────────────────────────────────────────────┘
```

| Folder | What |
|---|---|
| [`firmware/`](firmware/README.md) | PlatformIO / Arduino firmware: animated face, moods, gags, app screens, nudges. Wiring + flashing. |
| [`pc/`](pc) | .NET 10 solution: `Dante.Core` (engine, sources, protocol), `Dante.Server` (web config UI + REST API), tests. |
| [`docs/protocol.md`](docs/protocol.md) | The wire protocol between the two. |

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

## How it behaves

* **Screen type** — a 0.91" 128×32 OLED, or a 1602 (16×2) or 2004 (20×4) character LCD
  (PCF8574 I2C); chosen on the *Device* page (or *Automatic*) and switched live.
* **One or two screens** — use a single display for both eyes, or give each eye its own display.
  With two, app screens either show the app on one display while the other eye watches it, or on both.
  Pick it (plus swap/upside-down fixes for mounting) on the *Device* page; DANTE remembers it.

* **Face screen** — procedural robot eyes that blink, glance around and react. Press **SELECT** (top) for a
  random gag: heart eyes, dizzy, wink, laughing `> <`, surprised, "boop!".
* **Mood** — depends on how many things are waiting on you and how long the oldest has waited:
  `happy → curious → antsy → grumpy → meltdown`. Thresholds are set on the *Mood & personality* page.
  With no PC it gets sleepy ("no PC").
* **Apps** — **LEFT/RIGHT** browse app screens. Each app has a name, a visual (gauge, bar, counter, text,
  sparkline, status) and a data source that runs on the PC. **SELECT** on an app opens its link on the PC
  (e.g. the oldest PR); long-press **SELECT** refreshes everything.
* **Funny deliveries** — when a value changes the PC picks a line in DANTE's personality
  (Cheerful, Sarcastic, Dramatic, or your own lines) and DANTE pops it up in a speech bubble.
* **Magic button** — a fourth button on top runs something on the PC: mute/unmute the mic or
  sound, play/pause, next/previous, volume, open a URL/app, or run any command. Press and long press
  are set separately on the *Magic button* page; DANTE confirms ("Mic muted").
* **Take-overs** — an app can take over the face and nudge you ("Claude Code is waiting on you!") after a
  grace period. **SELECT** = "I'm on it" (re-nudges later if still true), **LEFT/RIGHT** = snooze.

## The example apps (created on first run)

| App | Visual | Source | Behaviour |
|---|---|---|---|
| Claude 5h | Gauge | `claude-code-usage` — subscription window utilisation (what `/usage` shows) | warn 70 %, crit 90 %; SELECT opens the usage page |
| Claude Code | Status | `claude-code-waiting` — fed instantly by Claude Code hooks | counts toward mood; takes over after 20 s; re-nudges every 5 min |
| PRs for me | Counter | `azure-devops-prs` — active PRs where you haven't voted (or your PRs waiting on you) | counts toward mood; SELECT opens the oldest PR. Disabled until you set org + PAT |

## Quick start

1. **Flash DANTE** — see [firmware/README.md](firmware/README.md) (`pio run -t upload`).
2. **Run the PC app** (needs the [.NET 10 SDK](https://dotnet.microsoft.com/download)):
   ```bash
   cd pc
   dotnet run --project src/Dante.Server
   ```
   Your browser opens <http://127.0.0.1:5280>. On **Device**, choose **Bluetooth LE** (no pairing;
   *Scan* finds `DANTE`) or **USB cable** and its port. Bluetooth LE works on Windows and Linux
   (BlueZ); on macOS use USB for now.
3. **Screens** (same page): Automatic, OLED (one or two), LCD 1602 or LCD 2004.
4. **Claude Code page → Install hooks** so DANTE knows when a session is waiting for you.
5. **Azure DevOps**: set `DANTE_ADO_PAT` (PAT with *Code: Read*) in your environment, then edit
   *PRs for me* → organisation URL → enable.

Publish a self-contained build for any OS with e.g.
`dotnet publish src/Dante.Server -c Release -r win-x64 --self-contained` (or `osx-arm64`, `linux-x64`).
Windows builds automatically use the `net10.0-windows10.0.19041.0` target, which is needed for Bluetooth LE.
Config lives in `%APPDATA%\Dante\config.json` (Windows), `~/.config/Dante/config.json` (Linux, macOS);
override with `DANTE_CONFIG`.

## Feeding your own apps

* **Push** — any script can update an app whose source is *push*:
  ```bash
  curl -X POST http://127.0.0.1:5280/api/apps/my_app/value -H "Content-Type: application/json" \
       -d '{"value": 2, "text": "two builds red", "pending": [{"key": "b1", "title": "Nightly", "since": "2026-11-11T08:00:00Z", "url": "https://ci/b1"}]}'
  ```
  `pending` items are what drive the mood and "waiting" take-overs.
* **HTTP JSON** — poll a URL and pick fields with a dotted path (`data.items[0].count`, `$count:items`).
* **Command** — run any program; it prints `3`, `3 some text`, or `{"value":3,"text":"..."}`.
* **New source type** — implement `IDataSource` in `Dante.Core` and register it in `Program.cs`;
  the UI renders its settings automatically.
* `POST /api/say {"text":"..."}` makes DANTE say something; `GET /api/state` returns everything.

## Notes

* The server listens on `127.0.0.1` only and rejects non-localhost `Host` headers.
* Secrets in source settings can be written as `env:VAR_NAME` so they stay out of `config.json`.
* The Claude Code usage source uses the same (undocumented) OAuth usage endpoint Claude Code itself
  calls; if it changes, point that app at the *push* or *command* source instead.

## Coming from DeskBuddy or HANK?

This project was called DeskBuddy, then briefly HANK. Upgrading keeps everything:

* **PC app:** on first start it copies your old config (`Hank/config.json`, or else `DeskBuddy/config.json`)
  to the new `Dante` folder. The old `HANK_*` and `DESKBUDDY_*` environment variables still work
  alongside the new `DANTE_*` names (e.g. `DESKBUDDY_ADO_PAT`).
* **Bluetooth:** a saved device name of "DeskBuddy" or "HANK" becomes "DANTE", and scanning also finds
  devices still running older firmware.
* **Firmware:** after reflashing, the device advertises as `DANTE` and keeps the screen settings it had saved.

## Development

```bash
cd pc && dotnet test        # engine, mood, nudges, quips, protocol, sources, hook installer + end-to-end with a fake DANTE
cd firmware && pio run      # build firmware
```
