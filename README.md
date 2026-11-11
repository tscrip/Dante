# DeskBuddy

A little desk companion with a personality: an ESP32 with one or two 0.91" 128×32 OLEDs and three buttons that
sits on your desk, pulls faces, and nudges you when things are waiting on you. All the thinking happens
on your PC, which pushes simple values to the buddy over Bluetooth.

```
┌──────────────── PC (Windows / macOS / Linux) ────────────────┐         Bluetooth SPP
│  DeskBuddy.Server  (.NET 10, ASP.NET Core + Blazor)          │  ◄──── JSON lines ────►  ┌────────────┐
│   • Config UI in your browser: http://127.0.0.1:5280         │                          │  ESP32     │
│   • Data sources: Claude Code usage, Claude Code "waiting",  │                          │  ◉ ◉  face │
│     Azure DevOps PRs, HTTP JSON, command, REST push          │                          │  ◄ ● ►     │
│   • Engine: levels, quips, mood, take-over nudges            │                          └────────────┘
└──────────────────────────────────────────────────────────────┘
```

| Folder | What |
|---|---|
| [`firmware/`](firmware/README.md) | PlatformIO / Arduino firmware: animated face, moods, gags, app screens, nudges. Wiring + flashing. |
| [`pc/`](pc) | .NET 10 solution: `DeskBuddy.Core` (engine, sources, protocol), `DeskBuddy.Server` (web config UI + REST API), tests. |
| [`docs/protocol.md`](docs/protocol.md) | The wire protocol between the two. |

## How it behaves

* **One or two screens** — use a single display for both eyes, or give each eye its own display.
  With two, app screens either show the app on one display while the other eye watches it, or on both.
  Pick it (plus swap/upside-down fixes for mounting) on the *Device* page; the buddy remembers it.

* **Face screen** — procedural robot eyes that blink, glance around and react. Press **SELECT** (top) for a
  random gag: heart eyes, dizzy, wink, laughing `> <`, surprised, "boop!".
* **Mood** — depends on how many things are waiting on you and how long the oldest has waited:
  `happy → curious → antsy → grumpy → meltdown`. Thresholds are set on the *Mood & personality* page.
  With no PC it gets sleepy ("no PC").
* **Apps** — **LEFT/RIGHT** browse app screens. Each app has a name, a visual (gauge, bar, counter, text,
  sparkline, status) and a data source that runs on the PC. **SELECT** on an app opens its link on the PC
  (e.g. the oldest PR); long-press **SELECT** refreshes everything.
* **Funny deliveries** — when a value changes the PC picks a line in the buddy's personality
  (Cheerful, Sarcastic, Dramatic, or your own lines) and the buddy pops it up in a speech bubble.
* **Take-overs** — an app can take over the face and nudge you ("Claude Code is waiting on you!") after a
  grace period. **SELECT** = "I'm on it" (re-nudges later if still true), **LEFT/RIGHT** = snooze.

## The example apps (created on first run)

| App | Visual | Source | Behaviour |
|---|---|---|---|
| Claude 5h | Gauge | `claude-code-usage` — subscription window utilisation (what `/usage` shows) | warn 70 %, crit 90 %; SELECT opens the usage page |
| Claude Code | Status | `claude-code-waiting` — fed instantly by Claude Code hooks | counts toward mood; takes over after 20 s; re-nudges every 5 min |
| PRs for me | Counter | `azure-devops-prs` — active PRs where you haven't voted (or your PRs waiting on you) | counts toward mood; SELECT opens the oldest PR. Disabled until you set org + PAT |

## Quick start

1. **Flash the buddy** — see [firmware/README.md](firmware/README.md) (`pio run -t upload`).
2. **Pair it** over Bluetooth as `DeskBuddy`.
3. **Run the PC app** (needs the [.NET 10 SDK](https://dotnet.microsoft.com/download)):
   ```bash
   cd pc
   dotnet run --project src/DeskBuddy.Server
   ```
   Your browser opens <http://127.0.0.1:5280>. Pick the port on **Device**
   (Windows: the paired device's *Outgoing* COM port · Linux: `/dev/rfcomm0` · macOS: `/dev/cu.DeskBuddy`).
4. **Claude Code page → Install hooks** so the buddy knows when a session is waiting for you.
5. **Azure DevOps**: set `DESKBUDDY_ADO_PAT` (PAT with *Code: Read*) in your environment, then edit
   *PRs for me* → organisation URL → enable.

Publish a self-contained build for any OS with e.g.
`dotnet publish src/DeskBuddy.Server -c Release -r win-x64 --self-contained` (or `osx-arm64`, `linux-x64`).
Config lives in `%APPDATA%\DeskBuddy\config.json` / `~/.config/DeskBuddy/config.json`
(override with `DESKBUDDY_CONFIG`).

## Feeding your own apps

* **Push** — any script can update an app whose source is *push*:
  ```bash
  curl -X POST http://127.0.0.1:5280/api/apps/my_app/value -H "Content-Type: application/json" \
       -d '{"value": 2, "text": "two builds red", "pending": [{"key": "b1", "title": "Nightly", "since": "2026-11-11T08:00:00Z", "url": "https://ci/b1"}]}'
  ```
  `pending` items are what drive the mood and "waiting" take-overs.
* **HTTP JSON** — poll a URL and pick fields with a dotted path (`data.items[0].count`, `$count:items`).
* **Command** — run any program; it prints `3`, `3 some text`, or `{"value":3,"text":"..."}`.
* **New source type** — implement `IDataSource` in `DeskBuddy.Core` and register it in `Program.cs`;
  the UI renders its settings automatically.
* `POST /api/say {"text":"..."}` makes the buddy say something; `GET /api/state` returns everything.

## Notes

* The server listens on `127.0.0.1` only and rejects non-localhost `Host` headers.
* Secrets in source settings can be written as `env:VAR_NAME` so they stay out of `config.json`.
* The Claude Code usage source uses the same (undocumented) OAuth usage endpoint Claude Code itself
  calls; if it changes, point that app at the *push* or *command* source instead.

## Development

```bash
cd pc && dotnet test        # engine, mood, nudges, quips, protocol, sources, hook installer + end-to-end with a fake buddy
cd firmware && pio run      # build firmware
```
