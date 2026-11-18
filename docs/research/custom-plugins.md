# Research: custom plugins defined in the UI

**Question:** can a user build a plugin in the DANTE web UI that calls an API and then uses a configurable
script to turn the response into a reading?

**Short answer:** yes. Build it as two modes on one plugin type:

- **Map** (no code): a dotted or JSONPath expression for each output field. This is the existing
  `HttpJsonSource` idea, made more capable.
- **Script**: a JavaScript `transform(res)` function run in **Jint** with hard limits.

Both modes produce the same `SourceReading`. JSONata is a close second choice for the expression mode,
but I would not ship it first; section 2 explains why. Do not use Roslyn C# scripting or NLua for
plugins that people share.

Versions and dates below were checked against the NuGet API on 2026-11-16. Section 6 lists what I could
not verify.

---

## 1. What exists today

- `IDataSource` (`pc/src/Dante.Core/Sources/IDataSource.cs`) has a fixed `Type`, a static list of
  `SettingField`s that the app editor renders, and one method, `ReadAsync(AppDefinition, CancellationToken)`.
  That method returns `SourceReading { Value, Text, Pending, Error }`.
- `HttpJsonSource` (being moved to `Features/HttpJson/`) sends a GET with one optional header. It picks
  `value` and `text` with a small dotted-path helper (`a.b[0].c`, `$count:items`). It cannot filter,
  build `pending[]`, format text, use other HTTP methods, or send a body.
- Secrets are written as `env:NAME` and resolved by `SourceSettingsExtensions.ResolveSecret`. Settings
  are stored in plain text in `config.json`.
- `DanteEngine.PollAsync` already wraps each read in a 60 s timeout and turns exceptions into
  `SourceReading.Fail`.
- `DanteConfig.Features` is a dictionary of JSON objects, one per feature slice. A plugin library can
  live there without changing the kernel.
- The device truncates `txt` to 47 characters and `title` to 63, and shows at most 12 apps
  (`docs/protocol.md`).

**Trust boundary today:** the `command` source already runs any program the config names. So anyone who
can edit the config already has code execution as the user. Plugins add a new threat only through
**sharing**: someone imports a plugin written by someone else. The sandbox design is aimed at that case.

---

## 2. Candidate transform languages

Sample input used by all the examples below. It is a typical "list of things" API response:

```json
{ "items": [
  { "id": 101, "state": "open",   "title": "Fix login", "created_at": "2026-11-10T09:00:00Z", "html_url": "https://x/101" },
  { "id": 102, "state": "closed", "title": "Docs",      "created_at": "2026-11-11T09:00:00Z", "html_url": "https://x/102" } ] }
```

Target output: `{ value: 1, text: "1 open", pending: [{ key: "101", title: "Fix login", since: "...", url: "..." }] }`.

### Comparison

| Option | NuGet package (latest) | Status | Licence | Builds `{value,text,pending[]}`? | Sandbox story |
|---|---|---|---|---|---|
| Built-in dotted path | none (in repo) | ours | ours | Value and text only | Nothing to sandbox |
| JSONPath (RFC 9535) | `JsonPath.Net` 3.1.0 (13 Nov 2026), targets net10.0 | Very active | MIT source; the NuGet binary comes with an "Open Source Maintenance Fee" EULA (applies only to revenue ≥ US$10k) | No: it only selects nodes | Safe: no loops or user functions |
| JSONPath | `JsonCons.JsonPath` 1.1.0 (Aug 2021) | Stale | MIT (per repo) | No | Safe |
| JMESPath | `JmesPath.Net` 1.1.0 (Mar 2026), jmespath-community | Maintained, slow cadence | Apache-2.0 | Mostly: it can project objects, but it cannot concatenate a number into a string | Safe: not Turing-complete, so cost grows with input size |
| JSONata | `Jsonata.Net.Native` 3.0.0 (Feb 2026) | Active, one maintainer | MIT | Yes, cleanly | Partial: has user-defined recursive functions; I found no documented evaluation timeout or recursion limit |
| JavaScript | `Jint` 4.18.0 (13 Nov 2026), targets net10.0 | Very active | BSD-2-Clause | Yes | Good: no CLR access by default; options for timeout, statement count, memory, recursion and cancellation |
| Lua | `MoonSharp` 2.0.0 (2016) / 3.0.0-beta.1 (Jul 2026) | Was dormant, now a v3 beta | BSD-3 (per README) | Yes | Moderate: core-module presets can drop `io`/`os`; there is no timeout, so you have to auto-yield coroutines yourself |
| Lua | `NLua` 1.7.9 (May 2026) | Active | MIT | Yes | Poor: it runs native Lua through P/Invoke, so you must strip libraries by hand and a crash in native code takes the process down |
| C# | `Microsoft.CodeAnalysis.CSharp.Scripting` 5.12.0 (Nov 2026) | Very active | MIT | Yes | **None.** See below |
| Templates | `Scriban` 7.6.1 (Nov 2026); `Fluid.Core` (3.0 in beta, beta.9 Nov 2026) | Active | BSD-2 / MIT | Text only | Good: loop and recursion limits, no .NET access by default |

### The same transform in each language

**JSONPath (`JsonPath.Net`).** It selects; C# has to do the shaping.

```csharp
var open = JsonPath.Parse("$.items[?@.state == 'open']").Evaluate(JsonNode.Parse(body)).Matches;
var reading = new SourceReading { Value = open.Count, Text = $"{open.Count} open" };
```

**JMESPath (`JmesPath.Net`).** Note that `"1 open"` cannot be built here, so the text has to come from a
template.

```text
{ value: length(items[?state=='open']),
  pending: items[?state=='open'].{key: to_string(id), title: title, since: created_at, url: html_url} }
```

**JSONata (`Jsonata.Net.Native`).** The outer `[...]` matters: without it, JSONata collapses a
one-element array into a single object.

```text
( $open := items[state = "open"];
  { "value": $count($open), "text": $count($open) & " open",
    "pending": [ $open.{ "key": $string(id), "title": title, "since": created_at, "url": html_url } ] } )
```

**JavaScript (Jint).**

```js
function transform(res) {
  const open = res.body.items.filter(i => i.state === "open");
  return { value: open.length, text: `${open.length} open`,
           pending: open.map(i => ({ key: String(i.id), title: i.title, since: i.created_at, url: i.html_url })) };
}
```

**Lua (MoonSharp).** The JSON is passed in with `JsonTable.JsonToTable`.

```lua
local open = {}
for _, i in ipairs(res.body.items) do
  if i.state == "open" then open[#open+1] = { key = tostring(i.id), title = i.title, since = i.created_at, url = i.html_url } end
end
return { value = #open, text = #open .. " open", pending = open }
```

**C# via Roslyn.** It works, and that is the problem:

```csharp
await CSharpScript.EvaluateAsync("System.IO.File.Delete(@\"C:\\Users\\me\\.ssh\\id_ed25519\")"); // compiles and runs
```

Modern .NET has no Code Access Security, and AppDomain sandboxing is gone. `AssemblyLoadContext` is not
a security boundary. Blocking APIs with an allowlist or analyzer is easy to get around with reflection,
`dynamic`, or `Type.GetType`. The only safe way to host Roslyn scripts is a separate low-privilege
process or container, and that is far too heavy for a desk gadget. **Verdict: no**, except perhaps as a
"trusted local scripts" mode that can never be imported. Even then, the `command` source already covers
that need.

**Scriban/Liquid.** Useful only for formatting `text`, for example `{{ value }} open · oldest: {{ pending[0].title }}`.
JavaScript and JSONata already format strings, so a third language adds little. Skip it for now.

### Why Jint for the script mode

- **Hard limits that are actually implemented.** I inspected the 4.18.0 binary and it has
  `LimitMemory` and `LimitRecursion`. The `TimeoutInterval`, `MaxStatements` and `CancellationToken`
  options are long-standing. Hitting a limit throws a catchable exception, not a `StackOverflowException`.
  That matters because .NET cannot catch a stack overflow, and one would kill the whole server.
- **No CLR access unless you opt in** with `AllowClr`. You can keep all .NET objects out of the engine
  by passing the response in as a JSON string and reading the result back with `JSON.stringify`.
- **Most users already know JavaScript.** Monaco or CodeMirror give good editing and syntax checking
  for free.
- **Very active.** Two releases in November 2026, and it targets net10.0.
- **Caveat 1.** The current README describes a hardened `Options.ForUntrustedCode(UntrustedCodeLimits…)`
  profile. That profile is **not in 4.18.0**: I checked the shipped DLL. Use the individual options until
  it ships.
- **Caveat 2.** The Jint README says plainly that it is "not an operating-system security boundary",
  and that limits are cooperative. That is acceptable here because the script gets no host functions at
  all: it receives data and returns data.

**Why not JSONata first.** It is the most elegant expression language for this job. But a shared plugin
can define a recursive function, and I could not confirm that `Jsonata.Net.Native` limits evaluation
depth or time. A `MaxDepth` member exists, but I could not tell whether it applies to the parser or to
evaluation. Runaway recursion there means a stack overflow, which crashes the process. If a later spike
shows that evaluation honours a depth limit and a cancellation token, JSONata is a good choice for a
third "expression" mode.

---

## 3. Recommended design

### 3.1 The plugin definition, also the share format

Plugins are stored under `DanteConfig.Features["customPlugins"]` and exported as standalone `.dante-plugin.json` files:

```json
{
  "format": "dante-plugin/1",
  "id": "github-my-prs", "name": "My open PRs", "version": "1.2.0",
  "description": "Open PRs assigned to me",
  "inputs": [ { "key": "repo", "label": "Repository", "kind": "Text", "default": "octo/app" },
              { "key": "token", "label": "GitHub token", "kind": "Secret" } ],
  "request": {
    "method": "GET",
    "url": "https://api.github.com/repos/{{repo}}/pulls?state=open",
    "headers": { "Accept": "application/vnd.github+json", "Authorization": "Bearer {{secret:token}}" },
    "body": null,
    "timeoutSeconds": 10
  },
  "allowedHosts": [ "api.github.com" ],
  "pollSeconds": 120,
  "transform": { "mode": "map", "value": "$count:$", "text": "{{value}} open", "pendingPath": "$",
                 "pending": { "key": "id", "title": "title", "since": "created_at", "url": "html_url" } },
  "fixtures": [ { "name": "two PRs", "status": 200, "body": "[...]",
                  "expect": { "value": 2 } } ]
}
```

- **`inputs`** become ordinary `SettingField`s for each app. Two apps can use the same plugin with
  different repositories. Secret inputs hold only `env:NAME` (or, later, a secret-store reference).
  A raw secret is never stored in the plugin itself.
- **`transform.mode`** is either `"map"` or `"script"`. In script mode the transform is
  `{ "mode": "script", "language": "js", "code": "function transform(res) {...}" }`.
- **`{{...}}` placeholders** in the URL, headers and body are filled from inputs. The URL is
  URL-encoded. `{{secret:x}}` is allowed **only in headers and body, never in the URL**, because URLs
  end up in logs.
- **Versioning.** `format` versions the schema; `version` is the author's semver. On import, the same
  `id` with a higher version is offered as an update with a diff view. The previous definition is kept
  as `previous` so the user can roll back one step.

### 3.2 How it fits `IDataSource`

There should be **one virtual source per saved plugin** (`Type = "plugin:github-my-prs"`), not a single
generic `custom-plugin` type. That way each plugin appears in the source dropdown next to the built-in
ones, and its `inputs` become that source's static `Settings`. This needs one small change to the kernel:

```csharp
public interface IDataSourceProvider { IEnumerable<IDataSource> Sources { get; } event Action? Changed; }

// SourceRegistry gains: IEnumerable<IDataSourceProvider> providers; Find() checks the static map first,
// then the providers. The CustomPluginProvider rebuilds its list on ConfigStore.Changed.
```

`CustomPluginSource.ReadAsync` then runs these steps: build the request, send it through the
**guarded HTTP client** (section 4), apply the transform (map or Jint), and validate and clamp the
result (`text` to 47 characters, `title` to 63, at most 20 pending items, `since` parsed as ISO-8601 or
defaulting to now). Errors come back as `SourceReading.Fail`.

A minimal Jint host, with no CLR objects inside the engine:

```csharp
public static SourceReading RunJs(string code, string responseJson, CancellationToken ct)
{
    using var engine = new Engine(o => o
        .Strict()
        .TimeoutInterval(TimeSpan.FromMilliseconds(500))
        .MaxStatements(200_000)
        .LimitMemory(16_000_000)
        .LimitRecursion(64)
        .CancellationToken(ct));
    engine.Execute(code);
    engine.SetValue("__res", responseJson);                       // a plain string, not a CLR object
    var json = engine.Evaluate("JSON.stringify(transform(JSON.parse(__res)))").AsString();
    return ReadingMapper.FromJson(json);                         // validates, clamps, maps to SourceReading
}
```

Run this through `Task.Run`, because Jint is synchronous and should not run on a Blazor circuit thread.
Use a new `Engine` for every run. Engines are cheap, and sharing one between plugins would leak state.

### 3.3 The editor UI

A **Plugins** page under `Features/CustomPlugins/` with three panes:

1. **Request.** Method, URL, headers, body and inputs. A **Run request** button sends the real call
   once and shows the status, timing, size and the body, pretty-printed and truncated. The response can
   be saved as a **sample** or as a fixture.
2. **Transform.** Toggle between Map and Script. Map mode has a field per output, plus a JSON tree of
   the sample where clicking a node fills in its path. Script mode has a code editor (CodeMirror 6 or
   Monaco from a CDN) with a `transform(res)` stub. **Run transform** re-runs only the transform
   against the saved sample, so editing the script does not call the API again.
3. **Result.** The raw `{value, text, pending}` and any errors with line numbers. Below it, the
   existing **`WidgetPreview`/`OledPreview` components** show what DANTE would display.

**Import** accepts a pasted JSON blob or an uploaded file. Before saving, it shows the hosts the plugin
will call, the secrets it wants, and its code. **Export** writes the definition with `env:` references
kept and any literal secret values blanked. **Test fixtures** run on save and from a "Test" button, and
a failing fixture shows a warning rather than blocking the save.

---

## 4. Security and safety

**What a malicious shared plugin could try, and the defence for each:**

| Threat | Defence |
|---|---|
| Send your token to an attacker's host | Bind each secret to the plugin's `allowedHosts`. `{{secret:x}}` resolves only when the request host is on that list. Import shows the hosts prominently. Redirects are followed manually (`AllowAutoRedirect = false`) and re-checked against the list, and secrets are dropped on any cross-host hop. |
| Ask for an `env:` variable it should not see, such as `env:AWS_SECRET_ACCESS_KEY` | `env:` names are chosen by the **user** in input values, never by the plugin's request template. On import, all secret inputs start empty. |
| SSRF against localhost or the LAN: DANTE's own `/api/say` and push endpoints, the router admin page, a local Docker API | DANTE runs as the user on that user's machine. Block loopback, link-local (`169.254/16`, `fe80::/10`) and private ranges **by default**, using a `SocketsHttpHandler.ConnectCallback` that checks the *resolved* IP. That also defeats DNS rebinding. Add a per-plugin "allow local network" switch that is off on import. |
| Infinite loop, memory bomb, or deep recursion in the script | Jint limits: 500 ms, 200k statements, 16 MB, depth 64. A fresh engine per run. The engine's 60 s outer timeout stays as a backstop. |
| Huge or slow response | `HttpCompletionOption.ResponseHeadersRead`, a stream capped at 1 MB (configurable up to 5 MB), a request timeout of 10 s (maximum 30 s), and JSON parsed with `MaxDepth = 64`. |
| Hammering an API, or getting the user banned | Minimum poll interval for plugins of 30 s. Back off on 429 and 5xx and honour `Retry-After`. "Run request" in the editor is rate-limited to once every 2 s. |
| Secrets showing up in the UI or logs | Never render resolved secrets. Redact every resolved secret value from error messages, response previews and logs, because some APIs echo headers back. Log the plugin id and status, not URLs with query strings. The request pane shows `{{secret:token}}`, never its value. |
| Script reaching the file system or network | Jint has no CLR access (`AllowClr` is never called), no modules and no `fetch`. The script only transforms data; the HTTP call is made by the host. |
| Misleading text or links on the device | Clamp lengths. Allow only `http`/`https` in `url`, because pressing SELECT opens it on the PC through `OnSelectUrl`. |

The Blazor UI itself stays bound to localhost (`AllowedHosts: localhost;127.0.0.1`). That protects the
editor, but not the outbound calls, which is why the SSRF guard is needed.

---

## 5. Phased plan

| Phase | Scope | Rough effort |
|---|---|---|
| **0. Spike** | Check the Jint limits against hostile scripts (a `while(true)` loop, a 100 MB string, deep recursion). Prototype the guarded `HttpClient`. | 0.5–1 day |
| **1. MVP** | Plugin model in `Features["customPlugins"]`; `IDataSourceProvider` plus `CustomPluginSource`; Map mode, reusing the dotted path and adding `pendingPath`; Script mode with Jint; guarded HTTP client (size, timeout, allowed hosts, no private IPs); editor with Run request, Run transform and preview; JSON import and export | 4–6 days |
| **2. Polish** | Code editor with syntax highlighting; click-to-path JSON tree; fixtures with expected values; version diff on import; rollback; redaction tests | 2–3 days |
| **3. Extras** | Optional JSONata expression mode, if the spike shows it can be bounded; more auth helpers such as OAuth2 client-credentials token caching; paging (follow `next` links, with a cap); a small "starter gallery" of built-in plugin templates (GitHub, Home Assistant, weather); OS secret store via DPAPI or libsecret | 3–5 days |
| **Later** | Push or webhook plugins (combine with `PushSource`); a plugin that chains two requests; a shared gallery URL | open |

---

## 6. Things I could not verify

- **Jint's hardened `ForUntrustedCode` profile.** It is in the README on `main` but absent from the
  4.18.0 DLL, so it is presumably in the next release. The individual option names in the sample are
  standard Jint 3/4 API. I confirmed `LimitMemory` and `LimitRecursion` in the binary; the rest I did not
  compile-test.
- **`Jsonata.Net.Native` evaluation limits.** I could not tell whether the `MaxDepth` and
  `CancellationToken` usage applies to evaluation or only to parsing.
- **The JSONPath sample's exact API.** Confirmed from the 3.1.0 XML docs (`JsonPath.Parse`,
  `Evaluate(JsonNode)`, `PathResult.Matches`) but not compiled. `docs.json-everything.net` was not
  reachable from this session.
- **The exact monthly amount** of the json-everything maintenance fee. It does not apply to a personal
  project under US$10k revenue.
- **The current state of the MoonSharp 3.0 beta** (sandbox presets, any added instruction limits).
- **Licences** for `JsonCons.JsonPath` (assumed MIT) and `NLua` (MIT): taken from the package
  licence URLs, not read in full.

## 7. Open questions for you

1. Is **sharing plugins** with other people a real goal? If plugins are only ever "mine", the SSRF and
   secret-binding work in Phase 1 can be simplified.
2. Should plugins be allowed to call **localhost/LAN services** (Home Assistant, a NAS) by default? I
   recommend off by default, with a per-plugin switch.
3. **Where secrets live:** keep `env:NAME` only, or add an OS secret store (DPAPI on Windows, libsecret
   on Linux, Keychain on macOS) in Phase 3?
4. **Script language:** is JavaScript acceptable, or do you specifically want Lua or JSONata?
5. Should plugins live inside `config.json`, or as separate files in `%AppData%/Dante/plugins/` that
   you can drop in or keep under git?
6. Should the existing `http-json` source eventually become a built-in "map-mode plugin", so there is
   one HTTP code path?
