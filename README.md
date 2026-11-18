# DANTE

### **D**esk **A**ssistant for **N**otifications, **T**asks & **E**fficiency

> "What's that on your desk?" "Oh, that's DANTE."

A little desk companion with a personality: an ESP32-S3 with one screen (a 0.91" OLED, a character LCD or a 3.5" colour TFT) and four buttons that
sits on your desk, pulls faces, and nudges you when things are waiting on you. All the thinking happens
on your PC, which pushes simple values to DANTE over Bluetooth LE (or USB).

```
┌──────────────── PC (Windows / macOS / Linux) ────────────────┐   Bluetooth LE (NUS) / USB
│  Dante.Server  (.NET 10, ASP.NET Core + Blazor)              │  ◄──── JSON lines ────►  ┌────────────┐
│   • Config UI in your browser: http://127.0.0.1:5280         │                          │  ESP32-S3  │
│   • Features: Claude Code, Azure DevOps PRs, Pomodoro,       │                          │  ◉ ◉  face │
│     calendar reminders, HTTP JSON, command, REST push        │                          │  ◄ ● ►     │
│   • Engine: levels, quips, mood, take-over nudges            │                          └────────────┘
└──────────────────────────────────────────────────────────────┘
```

| Folder | What |
|---|---|
| [`firmware/`](firmware/README.md) | PlatformIO / Arduino firmware: animated face, moods, gags, widget screens, nudges. Wiring + flashing. |
| [`pc/`](pc) | .NET 10 solution: `Dante.Core` (the kernel: engine, device link, protocol, config), `Dante.Server` (web UI, REST API and every feature as a vertical slice under `Features/`), tests. |
| [`docs/research/custom-plugins.md`](docs/research/custom-plugins.md) | Research: user-defined plugins (call an API, transform the response with a script) from the UI. |
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

* **One screen** — a 0.91" 128×32 OLED, a 1602 (16×2) or 2004 (20×4) character LCD (PCF8574 I2C), or a
  3.5" 480×320 colour TFT (ST7796, SPI); chosen on the *Device* page (or *Automatic*) and switched live.
  On the TFT the eyes fill the screen and change colour with the mood, with a status bar on top.

* **Face screen** — procedural robot eyes that blink, glance around and react. Press **SELECT** (top) for a
  random gag: heart eyes, dizzy, wink, laughing `> <`, surprised, "boop!".
* **Looks** — dress DANTE up on the *Mood & personality* page: **Classic**, **Gentleman** (curly mustache,
  calm half-lidded eyes, slow blinks), **Nerd** (glasses, glances up while "thinking"), **Grumpy Uncle**
  (heavy eyebrows that make every mood sterner, bristly mustache) or **Party** (big eyes, a party hat, happy
  hops and confetti). Each changes the eyes, the little habits and the accessories, on the OLED and the TFT.
* **Mood** — depends on how many things are waiting on you and how long the oldest has waited:
  `happy → curious → antsy → grumpy → meltdown`. Thresholds are set on the *Mood & personality* page.
  With no PC it gets sleepy ("no PC").
* **Plugins and widgets** — a **plugin** adds something to DANTE (Claude Code, Azure DevOps, a calendar, a
  Pomodoro timer...) and has one page in the PC app (*Plugins* in the sidebar) with its settings. A
  **widget** is a screen on DANTE made by a plugin; one plugin can offer several (Claude Code: *5-hour usage*
  and *Sessions waiting on me*). Each widget has a visual (gauge, bar, counter, text, sparkline, status,
  timer) and an **Active** switch: only active widgets run and show up, so you see just the ones you want.
  **LEFT/RIGHT** browse the active widgets; **SELECT** on one opens its link on the PC (e.g. the oldest PR);
  long-press **SELECT** refreshes everything.
* **Funny deliveries** — when a value changes the PC picks a line in DANTE's personality
  (Cheerful, Sarcastic, Dramatic, or your own lines) and DANTE pops it up in a speech bubble.
* **Magic button** — a fourth button on top runs something on the PC: mute/unmute the mic or
  sound, play/pause, next/previous, volume, open a URL/app, or run any command. Press and long press
  are set separately on the *Magic button* page; DANTE confirms ("Mic muted").
* **Take-overs** — a widget can take over the face and nudge you ("Claude Code is waiting on you!") after a
  grace period. **SELECT** = "I'm on it" (re-nudges later if still true), **LEFT/RIGHT** = snooze.
* **Pomodoro** — a focus / break timer widget. Go to it and press **MAGIC** to start or pause (hold to
  reset); DANTE counts down by itself and tells you when a session ends (**SELECT** starts the next one).
  Lengths, sessions before a long break and auto-start are on the *Pomodoro* plugin page.
* **Calendar reminders** — DANTE nudges you before meetings, at the minutes you choose (e.g. 10 and 1),
  from Outlook / Microsoft 365. **SELECT** on the reminder opens the Teams / Zoom link. Optional
  "Next meeting" countdown widget. Set up on the *Calendar* plugin page (needs a free Entra app
  registration, steps on the page).

## Plugins

| Plugin | Widgets it offers | Shared settings / page |
|---|---|---|
| Claude Code | *Subscription usage* (gauge: 5-hour window, week, or Opus week) · *Sessions waiting on me* (status, nudges) | credentials file / token override; hook installer, sessions waiting right now |
| Azure DevOps | *Pull requests* (to review, yours waiting on you, or both; one widget per filter or project) | organisation URL, PAT |
| Calendar | *Next meeting* (countdown) | Outlook sign-in, reminder minutes and filters, coming-up list |
| Pomodoro | *Timer* | lengths, long break, auto-start, end-of-session nudge; live timer with start / skip / reset |
| Web API (JSON) | *JSON value* (one per URL) | – |
| Command | *Command output* (one per command) | – |
| REST push | *Pushed value* (one per thing you push) | – |

## The example widgets (created on first run)

| Widget | Plugin → type | Visual | Behaviour |
|---|---|---|---|
| Claude 5h | Claude Code → Subscription usage | Gauge | warn 70 %, crit 90 %; SELECT opens the usage page |
| Claude Code | Claude Code → Sessions waiting on me | Status | counts toward mood; takes over after 20 s; re-nudges every 5 min |
| PRs for me | Azure DevOps → Pull requests | Counter | counts toward mood; SELECT opens the oldest PR. Off until you set the org + PAT |

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
3. **Screen** (same page): Automatic, OLED, LCD 1602, LCD 2004 or TFT 3.5". Automatic can't detect the TFT
   (it's on SPI): it uses it only when no I2C screen answers, so pick it explicitly.
4. **Plugins → Claude Code → Install hooks** so DANTE knows when a session is waiting for you.
5. **Plugins → Azure DevOps**: organisation URL and a PAT (*Code: Read*; `env:DANTE_ADO_PAT` reads it from
   an environment variable), then switch the *PRs for me* widget on.
6. Optional: **Plugins → Pomodoro** → add the *Timer* widget; **Plugins → Calendar** → client ID, sign in,
   enable reminders.
7. **Widgets**: switch widgets on and off and set their order on DANTE.

Publish a self-contained build for any OS with e.g.
`dotnet publish src/Dante.Server -c Release -r win-x64 --self-contained` (or `osx-arm64`, `linux-x64`).
Windows builds automatically use the `net10.0-windows10.0.19041.0` target, which is needed for Bluetooth LE.
Config lives in `%APPDATA%\Dante\config.json` (Windows), `~/.config/Dante/config.json` (Linux, macOS);
override with `DANTE_CONFIG`.

## Develop without hardware: the simulator

The **DANTE simulator** is the real firmware compiled for your PC: same face, moods, gags, widgets,
countdowns and nudges as the board, drawn by the same code (u8g2 for the OLED and LCD, LovyanGFX for
the 3.5" TFT), so what you see is what the hardware will do.

1. Build it once (needs a C/C++ compiler: gcc on Linux, Xcode command line tools on macOS,
   MinGW-w64 on Windows):
   ```bash
   cd firmware
   pio run -e simulator
   ```
2. In the PC app open **Simulator**, pick the screen (3.5" TFT, 0.91" OLED, 1602 or 2004 LCD) and
   click **Start simulator**. The app runs the program itself.
3. Click DANTE's buttons on screen (hold for a long press) or use the keyboard:
   ←/→ = LEFT/RIGHT, Enter/Space = SELECT, M = MAGIC.

Everything else (widgets, plugins, Claude Code hooks, Pomodoro, the Magic button) works against the
simulator exactly as with a real DANTE. After changing firmware code, rebuild and press
**Power-cycle**. Switch back to Bluetooth or USB on the *Device* page.

## Feeding your own widgets

* **REST push** — any script can update a *Pushed value* widget:
  ```bash
  curl -X POST http://127.0.0.1:5280/api/widgets/my_widget/value -H "Content-Type: application/json" \
       -d '{"value": 2, "text": "two builds red", "pending": [{"key": "b1", "title": "Nightly", "since": "2026-11-11T08:00:00Z", "url": "https://ci/b1"}]}'
  ```
  `pending` items are what drive the mood and "waiting" take-overs. (The old `/api/apps/...` URL still works.)
* **Web API (JSON)** — poll a URL and pick fields with a dotted path (`data.items[0].count`, `$count:items`).
* **Command** — run any program; it prints `3`, `3 some text`, or `{"value":3,"text":"..."}`.
* **Your own plugin** — see below; the UI renders its settings automatically.
* `POST /api/say {"text":"..."}` makes DANTE say something; `GET /api/state` returns everything.

## Adding a plugin (vertical slices)

Everything a plugin needs lives in its own folder, `pc/src/Dante.Server/Features/<Name>/`: its settings,
widget types, page panel, REST endpoints and background services. One class per folder implements
`IFeature` and registers the rest; features are discovered by reflection, so adding one touches no shared
file (not `Program.cs`, not the sidebar, which lists every `IPlugin` under *Plugins*):

```csharp
// The plugin: its page at /plugins/weather, with a settings form built from Fields.
public sealed class WeatherPlugin : IPlugin
{
    public string Id => "weather";
    public string Name => "Weather";
    public string Description => "Today's forecast for your city.";
    public IReadOnlyList<SettingField> Fields { get; } = [new("apiKey", "API key", SettingKind.Secret)];
}

// A widget type: what one screen on DANTE shows. A plugin can have several.
public sealed class TemperatureWidget(PluginSettings settings, WeatherPlugin plugin) : IWidgetType
{
    public string Type => "weather-temperature";
    public string PluginId => "weather";
    public string DisplayName => "Temperature";
    public string Description => "The temperature outside now.";
    public IReadOnlyList<SettingField> Fields { get; } = [new("city", "City")];   // per widget

    public WidgetDefinition CreateDefault() => new() { Name = "Outside", Type = Type, Visual = VisualKind.Counter, Unit = "°C" };

    public async Task<WidgetReading> ReadAsync(WidgetDefinition widget, CancellationToken ct)
    {
        var key = settings.Get(plugin, "apiKey");        // shared, from the plugin page
        var city = widget.Get(this, "city");              // this widget's own
        return new WidgetReading { Value = 21, Text = city };
    }
}

public sealed class WeatherFeature : IFeature
{
    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton<WeatherPlugin>();
        services.AddSingleton<IPlugin>(sp => sp.GetRequiredService<WeatherPlugin>());
        services.AddSingleton<IWidgetType, TemperatureWidget>();
    }
}
```

A plugin that needs more than a form (a sign-in, live status) sets `IPlugin.ConfigComponent` to a Razor
component, shown on its page above its widgets. The kernel (`Dante.Core`) offers the extension points:

| Need | Use |
|---|---|
| A screen on DANTE | `IWidgetType` (value, text, pending items, or a `TimerReading` countdown) |
| Shared plugin settings | `IPlugin.Fields`, read with `PluginSettings.Get(plugin, key)` |
| React to DANTE's buttons | `IDeviceEventHandler`, registered with `DanteEngine.AddHandler(handler, order)` |
| Nudge the human yourself | `DeviceAlerts.Raise/Clear/Say`; answers arrive as `Acknowledged` / `Snoozed` |
| Anything else to store | your own class, stored with `config.SetFeature(key, value)`, read with `config.Feature<T>(key)` |

The Pomodoro and Calendar plugins are worked examples of all of these.

## Notes

* The server listens on `127.0.0.1` only and rejects non-localhost `Host` headers.
* Secret settings (plugin or widget) can be written as `env:VAR_NAME` so they stay out of `config.json`.
* The Claude Code usage source uses the same (undocumented) OAuth usage endpoint Claude Code itself
  calls; if it changes, use a *REST push* or *Command* widget instead.

## Coming from DeskBuddy or HANK?

This project was called DeskBuddy, then briefly HANK. Upgrading keeps everything:

* **PC app:** on first start it copies your old config (`Hank/config.json`, or else `DeskBuddy/config.json`)
  to the new `Dante` folder. The old `HANK_*` and `DESKBUDDY_*` environment variables still work
  alongside the new `DANTE_*` names (e.g. `DESKBUDDY_ADO_PAT`).
* **Bluetooth:** a saved device name of "DeskBuddy" or "HANK" becomes "DANTE", and scanning also finds
  devices still running older firmware.
* **Firmware:** after reflashing, the device advertises as `DANTE` and keeps the screen settings it had saved.

## Upgrading to 3.0 (one screen)

DANTE 3.0 always drives exactly one screen. The two-screen mode (one eye per OLED or LCD) is gone; a
config with two screens keeps working with the first one, and its "upside down" setting carries over.
The MAGIC button's settings moved inside `config.json` (from `magic` to `features.magic`); this happens
by itself on first start. Reflash the firmware too: older firmware ignores the TFT and the timer screen.

"Apps" are now **widgets**, made by **plugins**. `config.json` is converted on first start (`apps` →
`widgets`, `enabled` → `active`, `sourceType` → `type`), and settings that several widgets shared (the Azure
DevOps organisation and PAT, the Claude token) move to their plugin page. The old pages (`/claude-code`,
`/calendar`, `/pomodoro`) are now under *Plugins*.

## Development

```bash
cd pc && dotnet test        # engine, slices (Pomodoro, calendar, magic...), protocol, sources + end-to-end with a fake DANTE
cd firmware && pio run      # build firmware
```
