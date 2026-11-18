# CLAUDE.md — working on DANTE

DANTE is a desk companion: an ESP32-S3 with one screen and four buttons that pulls faces and nudges its
owner when things are waiting on them. All the thinking happens in a PC app; the device just draws.

**Who you are probably helping:** someone who enjoys this project but is *not* a .NET or embedded
programmer. They will ask for things in plain language ("show my GitHub notifications on DANTE", "make
the eyes purple when I have a meeting"). Your job is to turn that into working, tested code that follows
the standards below, and to explain what you did in plain language. Read "Working with a non-developer"
before you start.

## The two halves

| Folder | What | Language | Build / test |
|---|---|---|---|
| `pc/` | The PC app: web UI at http://127.0.0.1:5280, the engine, every plugin | C# / .NET 10, ASP.NET Core + Blazor Server | `cd pc && dotnet test` |
| `firmware/` | The device: face, widget screens, buttons, the screen driver | C++ (Arduino on ESP32-S3), PlatformIO | `cd firmware && pio run` |
| `docs/protocol.md` | The JSON-lines protocol between them | — | — |
| `case/` | 3D-printable case (OpenSCAD) | OpenSCAD | `openscad -o case/case.stl case/dante_case.scad` |

Almost everything people want (new data on DANTE, new nudges, new settings) is a **plugin in `pc/`**.
Only touch `firmware/` when the device itself has to draw or do something new (a new visual, a new
button behaviour); see "Firmware standards". New plugins never need firmware changes.

## Words we use

| Word | Means | In code |
|---|---|---|
| **Plugin** | Something that adds functionality to DANTE (Claude Code, Calendar, Pomodoro...). Has exactly one page in the PC app, `/plugins/<id>`. | `IPlugin` |
| **Widget** | One screen on DANTE, made from one of a plugin's widget types. Has an **Active** switch. | `WidgetDefinition` (in `config.json` under `widgets`) |
| **Widget type** | A kind of widget a plugin offers ("Subscription usage", "Countdown to a date"). A plugin can offer several. | `IWidgetType` |
| **Visual** | How a widget is drawn: gauge, bar, counter, text, sparkline, status, timer. | `VisualKind` |
| **Nudge** / takeover | DANTE takes over its screen until the human answers (SELECT = "I'm on it", LEFT/RIGHT = "later"). | `TakeoverRule`, `DeviceAlerts` |
| **Feature** / slice | A folder under `pc/src/Dante.Server/Features/`. Every plugin is one; so are the Device and Mood pages. | `IFeature` |

Use **plugin** and **widget** in everything the user sees (UI text, docs, messages). On the wire the
protocol still says `apps`; never show that word to the user.

## How the PC app is built (vertical slices)

```
pc/src/Dante.Core/          the kernel: shared by everything, knows no plugin
  Config/                   config.json model (DanteConfig, WidgetDefinition), ConfigStore (load/save/migrate)
  Device/                   link to DANTE (BLE / USB), protocol messages, DeviceAlerts (nudges), button events
  Engine/DanteEngine.cs     polls active widgets, levels, quips, mood, takeovers; sends everything to DANTE
  Plugins/Plugins.cs        IPlugin, PluginRegistry, PluginSettings
  Widgets/IWidgetType.cs    IWidgetType, WidgetReading, SettingField, TimerReading
pc/src/Dante.Server/        the web app
  Program.cs                kernel registration + AddFeatures()/UseFeatures(); you should not need to edit it
  Features/<Name>/          one folder per feature/plugin: everything it needs, nothing it doesn't
  Features/IFeature.cs      the slice entry point; found by reflection
  Components/               layout, shared UI (LiveComponent, SettingsForm)
pc/tests/Dante.Tests/       xUnit tests for everything above
```

Rules of the architecture:

1. **A plugin lives entirely in its own folder** `pc/src/Dante.Server/Features/<Name>/`. Adding one must
   not require editing `Program.cs`, the sidebar, the kernel or another plugin's folder.
2. **The kernel (`Dante.Core`) never references a plugin.** If a plugin needs something the kernel
   doesn't offer, add a small, general extension point to the kernel (and a test), not plugin-specific code.
3. **Slices don't reference each other.** Shared helpers go in the kernel (e.g. `JsonPath`) or in
   `Features/` root files used by several slices (e.g. `PluginMigration.cs`).
4. **Settings:** shared plugin settings are `IPlugin.Fields` (stored by `PluginSettings`, read with
   `PluginSettings.Get(plugin, key, widget)`); per-widget settings are `IWidgetType.Fields` (read with
   `widget.Get(this, key)`). Anything richer is a settings class stored with `config.SetFeature(key, value)`
   and read with `config.Feature<T>(key)` (see `Pomodoro/PomodoroSettings.cs`).

Extension points a plugin can use:

| Need | Use | Example |
|---|---|---|
| A screen on DANTE | `IWidgetType` | `DaysUntil/DaysUntilWidget.cs` |
| Shared settings with a form | `IPlugin.Fields` + `PluginSettings` | `AzureDevOps/` |
| A richer plugin page (sign-in, live status, buttons) | `IPlugin.ConfigComponent` = a Razor component | `Calendar/CalendarPanel.razor` |
| React to DANTE's buttons | `IDeviceEventHandler`, registered in `IFeature.Start` with `DanteEngine.AddHandler(handler, order)` | `Pomodoro/PomodoroService.cs` |
| Nudge the human yourself | `DeviceAlerts.Raise / Clear / Say`; answers arrive as `Acknowledged` / `Snoozed` | `Calendar/CalendarService.cs` |
| Something running in the background | a `BackgroundService` registered in `AddServices` | `Pomodoro/PomodoroService.cs` |
| A countdown on DANTE | return a `TimerReading` and use `VisualKind.Timer` | `Pomodoro/PomodoroSource.cs` |
| Push updates straight away (not at the next poll) | `DanteEngine.PollWidgetType(type)` / `PollNow(id)` | `ClaudeCode/ClaudeCodeFeature.cs` |
| A REST endpoint | `IFeature.MapEndpoints` | `Push/PushFeature.cs` |

## Adding a plugin (the recipe)

Follow these steps in order. `Features/DaysUntil/` is the reference: it is small and commented; copy it.

1. **Understand the idea** (see "Working with a non-developer"): what should DANTE show, how often does it
   change, does it need an account / API key, should it nudge, is there one of it or several?
2. **Pick names.** Plugin id and widget type id: lower-case with dashes (`github`, `github-notifications`).
   Folder and classes: PascalCase (`Features/GitHub/`, `GitHubPlugin`, `GitHubNotificationsWidget`,
   `GitHubFeature`). Ids are stored in `config.json`: never rename them once released.
3. **Create the folder** `pc/src/Dante.Server/Features/<Name>/` with:
   - `<Name>Plugin.cs` — `IPlugin`: `Id`, `Name`, `Description` (one or two friendly sentences),
     `Fields` for shared settings (accounts, tokens, a city), `Order` (sidebar position; 10–90).
   - `<Something>Widget.cs` — one `IWidgetType` per kind of screen: `Type`, `PluginId`, `DisplayName`,
     `Description`, per-widget `Fields`, `CreateDefault()` (pick the visual, unit, min/max, poll interval,
     levels, nudge rule), and `ReadAsync`.
   - `<Name>Feature.cs` — `IFeature` that registers the plugin and widget types in `AddServices`
     (register the plugin as itself *and* as `IPlugin` if widget types need to read its settings, as in
     `AzureDevOps/AzureDevOpsFeature.cs`).
   - Optional: a `BackgroundService`, a Razor panel (`ConfigComponent`), a device event handler.
4. **Write `ReadAsync`.** Return `new WidgetReading { Value = ..., Text = ... }`:
   - `Value`: the number (drives gauges, bars, counters, levels and nudges "when above/below").
   - `Text`: at most 47 characters reach DANTE (longer text scrolls); keep it short and human.
   - `Pending`: the list of things waiting on the human (each with a key, title, since-when and optional
     URL). This drives DANTE's mood and "something is waiting" nudges.
   - `WidgetReading.Fail("...")` for problems: plain words saying what to fix ("Set your API key on the
     Weather page."). Never throw for expected problems; never put a secret in the message.
   - Use `IHttpClientFactory` for HTTP (`http.CreateClient(nameof(MyWidget))`), honour the
     `CancellationToken`, and inject `TimeProvider` instead of using `DateTime.Now`.
5. **Write tests** in `pc/tests/Dante.Tests/<Name>Tests.cs` (copy `DaysUntilTests.cs`): the reading for
   normal data, an empty/edge case, and a bad setting. Parse API responses in a `static` method and test
   it with a JSON sample, so tests never call the internet (see `GraphParsingTests` in `CalendarTests.cs`).
6. **Run** `cd pc && dotnet test` until everything passes (the `PluginTests` there also check that every
   widget type belongs to a registered plugin, and that the app's services wire up).
7. **Try it for real:** `cd pc && dotnet run --project src/Dante.Server`, open http://127.0.0.1:5280,
   find the plugin under *Plugins*, fill in its settings, add a widget, press **Test** in the widget editor.
8. **Document it:** add a row to the *Plugins* table in `README.md`.
9. **Tell the user** in plain language what you built, where to find it in the app, what they need to
   provide (an API key, an account), and anything you couldn't check.

Things a plugin must get right:

- **Secrets** (API keys, tokens, passwords) are `SettingKind.Secret` fields; tell users they can write
  `env:VAR_NAME` to keep them out of `config.json`. Never log them, show them back, or put them in errors.
- **Be polite to APIs:** poll no faster than the data changes (most things: 60–300 s), cache when possible.
- **Never block:** everything is `async`; no `.Result` / `.Wait()`.
- **Things that exist once** (a timer, "next meeting") set `AllowMultiple => false`.
- **Device limits:** widget names ≤ 19 characters, text ≤ 47, nudge title/text ≤ 63, at most 12 widget
  screens. The protocol layer trims longer strings, but write for these sizes.

## C# standards (pc/)

- **.NET 10, C# latest.** `Nullable` and `ImplicitUsings` are on. File-scoped namespaces matching the
  folder (`Dante.Server.Features.DaysUntil`). Primary constructors for dependency injection.
- **Naming:** PascalCase types/members, `_camelCase` private fields, `camelCase` locals, `I` prefix for
  interfaces, `Async` suffix on async methods, `const string PluginId` / `TypeName` for stored ids.
- **Style:** small sealed classes; `sealed record` for data; collection expressions (`[]`); `switch`
  expressions; early returns; no regions. Prefer clear code to clever code.
- **Comments:** XML `<summary>` on public types and members, written for a person who isn't a .NET expert:
  say what it is for and why, not how. Comment the non-obvious "why" inline. User-facing text is plain,
  friendly English (no jargon, no exception names).
- **Dependencies:** use what's in the solution first (ASP.NET Core, System.Text.Json, IHttpClientFactory,
  MSAL for Microsoft sign-in). Only add a NuGet package when it saves real work; say so in your summary.
- **Code examples and helper scripts are C#/.NET or shell.** Do not write Python for this project (the
  owner's preference), including throw-away scripts.
- **Config changes must not break existing users:** if you rename or move something in `config.json`,
  add a migration (`ConfigStore.MigrateJson` for the kernel, `PluginMigration.MoveToPlugin` for plugin
  settings) and a test that loads an old config.
- **Tests:** xUnit. Every plugin and every bug fix gets tests. No real network, Bluetooth or device in
  tests: use the fakes in the test project (`FakeDante`, `ManualTime`, `RecordingOpener`).
- **UI (Razor):** use the existing CSS classes in `wwwroot/app.css` (`panel`, `grid`, `fields`, `field`,
  `check`, `btn`, `btn primary`, `notice`, `muted`, `small`, `help`) and `SettingsForm` for settings.
  Pages that show live state inherit `LiveComponent`.

## Firmware standards (firmware/)

Only change the firmware when the device must draw or do something new. Plugins never need it.

- **Board:** ESP32-S3 with octal PSRAM (`R8` modules). Arduino framework, PlatformIO env `esp32-s3`.
- **Screens:** exactly one: the 0.91" SSD1306 128×32 OLED (I2C, GPIO 8/9) or the 3.5" ST7796 480×320 TFT
  (SPI, GPIO 12/11/10/13/14/21, via LovyanGFX). All pins and timings live in `src/config.h`.
- **Drawing:** the face and widget screens draw on a logical 128×32 canvas through `Gfx` (`src/Gfx.h`):
  1:1 on the OLED, scaled up in colour on the TFT (`TftGfx`). New visuals go in `src/Apps.cpp`
  (`draw*` functions) using `Gfx` calls only, so they work on both screens.
- **Protocol:** any new message or field goes in `docs/protocol.md`, in `pc/src/Dante.Core/Device/DeviceProtocol.cs`
  (with a test) and in `src/main.cpp` (`onMessage`). Unknown fields must be ignored, so old and new
  versions keep working together.
- **Memory:** fixed-size buffers and the limits in `config.h` (no unbounded `String` growth); keep
  `loop()` non-blocking (no long `delay()`).
- **Style:** C++17, 2-space indent, namespaces per module (`Apps::`, `Face::`, `Displays::`), file-local
  helpers in an anonymous namespace, comments that explain the why.
- **Version:** bump `FW_VERSION` in `src/config.h` when the firmware changes.
- **Check:** `cd firmware && pio run` must build without new warnings from `src/`. You can't run it on
  the hardware: say so, and tell the user exactly how to flash it and what to look for.

## Working with a non-developer

- **Start by understanding, not coding.** If the request leaves real choices open, ask one or two short
  questions with concrete options ("Count all unread notifications, or only the ones that mention you?").
  Don't ask about things you can decide sensibly yourself; decide and mention it.
- **Explain in plain language.** No unexplained jargon. Say where things are *in the app* ("Plugins →
  Weather, then Add widget"), not only in the code.
- **Do the whole job:** code, tests, a run of the app if you can, README row. Don't leave TODOs.
- **Tell them what they must provide** (an API key, an account, an app registration) and exactly where to
  get it and where to paste it. Recommend `env:VAR_NAME` for secrets.
- **Be honest about what you couldn't check** (a real API you had no key for, the device itself).
- **Keep the fun.** DANTE has a personality: friendly wording, a good default visual, maybe a nudge
  message with character ("{item} is waiting on you!").
- **Commands they can copy** go in their own code block, one command per block.

## Everyday commands

```bash
cd pc && dotnet test                                   # all PC tests
cd pc && dotnet run --project src/Dante.Server        # run the app: http://127.0.0.1:5280
cd firmware && pio run                                 # build the firmware
cd firmware && pio run -t upload                       # flash it (USB)
cd firmware && pio device monitor                      # watch DANTE's messages; type ? for a status line
```

Config lives in `%APPDATA%\Dante\config.json` (Windows) or `~/.config/Dante/config.json` (Linux, macOS);
set `DANTE_CONFIG` (or `Dante:ConfigPath`) to use another file, e.g. a throw-away one while testing.

## Definition of done

- [ ] `cd pc && dotnet test` passes (and `cd firmware && pio run` builds, if the firmware changed).
- [ ] New code follows the architecture rules: plugin in its own folder, no kernel/plugin cross references.
- [ ] Tests cover the new behaviour; no network or hardware in tests.
- [ ] User-facing text uses *plugin* / *widget* and plain language.
- [ ] Existing `config.json` files still load (migration + test if anything moved).
- [ ] `README.md` (and `docs/protocol.md` / `firmware/README.md` if relevant) updated.
- [ ] The summary to the user says what changed, how to try it, and what wasn't verified.
