# Manual play client: plan and progress

A person plays NetRts like a normal RTS, as **just another API player**: register (or paste an API
key), long-poll `GET /matches/{id}/state` (fogged), and send orders with `POST /matches/{id}/commands`.
Same fog, same tick, same rules and rate limits as a bot. The play view never reads `/spectate`.

This file is the working plan. Each step is one commit; the box is ticked in that commit.

## Decisions (from the brief, refined after reading the code)

- **Rendering RCL `src/NetRts.Web.Rendering`** (`Microsoft.NET.Sdk.Razor`, no components). Its
  `wwwroot` holds what both sites share: `mapview.js`, `util.js`, `lore.js`, `theme.js`, `3d/**`,
  `vendor/three/**`, the Snow College "S" images, and a new `netrts.css` with the colour tokens the
  renderer reads through `cssVar()` (`--p0..15`, `--terrain-*`, `--ore`, `--fog`, …, in both colour
  schemes and both brands) plus the classes the 3D view creates (`.map3d-canvas`, `.map3d-label*`).
  Served at `_content/NetRts.Web.Rendering/…`.
- **The server keeps** `index.html`, `app.js`, `spectate.js` (live view and replay viewer),
  `account.js`, `league.js`, `styles.css` (minus the moved blocks) and `trailer-poster.jpg`. Its
  pages import the renderer with relative URLs (`./_content/NetRts.Web.Rendering/mapview.js`), so a
  module such as `lore.js` still has one URL and one instance whichever file imports it.
- **Play client `play/NetRts.PlayClient`** (Blazor WebAssembly, standalone, its own
  `play/NetRts.Play.sln`), also listed in `NetRts.sln` so a root `dotnet build` / `dotnet test`
  covers it. References `NetRts.Protocol`, `NetRts.Web.Rendering` and `NetRts.Bots`:
  `NetRtsClient` is a thin `HttpClient` wrapper with no server-only dependencies, so it works in
  WASM and the client long-polls with exactly the code bots use.
- **Tests `play/NetRts.PlayClient.Tests`** (xUnit v3 on Microsoft.Testing.Platform, like the other
  test projects): the state adapter, the order queue and the click → `CommandRequest` mapping.
- **The renderer is not rewritten.** The play client wraps `MapView` from a small JS module
  (`play/NetRts.PlayClient/wwwroot/js/playview.js`) that adds what a player needs on a second canvas
  stacked over the map: selection rings, the drag box, the build ghost, order markers. It reads
  `MapView`'s camera (`tileSize()`, `offset()`) and its interpolated unit positions (`anim`,
  `interp()`), so `mapview.js` itself stays as it is and the spectator site can't change. Selection,
  orders and the HUD live in C#; the JS module only reports pointer gestures in tile coordinates.
- **Adapter in C#** (`GameStateDto` → the spectator-frame shape `MapView.setState` draws), so it is
  unit-testable. My units and buildings, visible enemies and deposits pass through. Remembered enemy
  buildings are added as buildings flagged `remembered`; they sit under the fog layer, so they show
  dimmed where they were last seen. Fog comes from a single player entry `{slot: you.slot,
  visibility}` with `MapView.setVision(you.slot)`.
- **Tick loop = `BotLoop`.** `GetStateAsync(waitForTick: tick + 1, sinceTick: tick)`. Orders go into
  a local queue; at most one `POST /commands` per tick, sent as soon as something is queued (so
  latency stays low and the request rate stays at ≤ 2 per tick, well inside 30/s even at 100 ms
  ticks). Per-command errors from `CommandResult` and `CommandFailed` events show in the HUD.
- **Server URL** from `wwwroot/appsettings.json` (`"NetRts": { "Server": "http://localhost:5080" }`),
  overridable in the lobby and remembered in `localStorage` with the API key.
- **2D only for play.** The 3D view needs an import map for three.js, and the page already has
  Blazor's own `<script type="importmap">` placeholder, which a published app needs to find its
  fingerprinted `dotnet.js` (found in step g; the dev server hides it). RTS control is a 2D job
  anyway. The 3D files still move to the RCL for later use.

## Steps

- [x] **0. Plan.** Read CLAUDE.md, docs, the server's `wwwroot`, Protocol DTOs, `NetRtsClient` /
  `BotLoop`, tests. Write this file.
- [x] **a. Rendering RCL.** Create `src/NetRts.Web.Rendering`, `git mv` the shared files, split the
  tokens and 3D-label rules out of `styles.css` into `netrts.css`, update the server's import paths
  and import map, add one `ProjectReference`. Also one line in `Program.cs`:
  `builder.WebHost.UseStaticWebAssets()`, which makes `_content/…` available when the server is run
  from its build output outside the Development environment (a published app has the files copied
  into `wwwroot` and the call is a no-op). Without it, `ASPNETCORE_ENVIRONMENT=Production dotnet run`
  would serve the site without its renderer, which works today.
  *Verify:* build, tests; start the server and in a browser compare the home page, a live match and
  a replay before/after: no failed requests, no console errors, and the computed style of every
  element on the spectate page identical in both brands and colour schemes.
- [x] **b. Scaffold the Blazor client** and `play/NetRts.Play.sln`; add the projects to `NetRts.sln`.
  Smoke test: a page that spectates a match through the shared `MapView` (allowed here: nobody is
  playing). *Verify:* `dotnet run --project play/NetRts.PlayClient` renders a house-bot exhibition.
- [x] **c. Lobby.** Server URL, register or paste a key (localStorage), house-bot list
  (`GET /bots`), create vs a house bot, create an open match (shows id/name and a ready-to-paste
  BotRunner command), join a waiting match, map/settings options. *Verify:* create vs `sitter` in the
  browser; BotRunner joins an open match by name.
- [x] **d. State adapter + tick loop (read-only play view).** `StateAdapter`, `GameSession`
  (long-poll loop, cancellation, reconnect), HUD (ore/storage, income, units, upgrades, production
  and research progress, event log). Remove the step-b spectate page so no play path reads
  `/spectate`. *Verify:* adapter tests; the fogged map renders and advances in the browser.
- [x] **e. Selection and orders.** Click / drag-box / shift-add / double-click-type selection,
  smart right-click (deposit → Gather with workers, enemy → Attack, else Move), Stop, attack-move,
  the order queue and command submission, per-command errors. *Verify:* queue and mapping tests;
  end-to-end: select a worker, right-click a tile, the Move is accepted and the unit walks.
- [x] **f. Build, produce, research, hotkeys, control groups, minimap.** Build menu with a
  valid/invalid ghost (rock, deposit, known building, visible enemy unit, prerequisites, ore),
  production and research buttons with costs from `GET /rules`, rally (right-click with a building
  selected), Ctrl+1–9 / 1–9, camera keys and edge/drag pan, a minimap (click to look, right-click to
  order). *Verify:* tests for placement validity and hotkey → command mapping; build a Barracks and
  train a Soldier in the browser.
- [x] **g. Speed, end screen, polish.** Pause / step / speed when `speedAdjustable`, surrender, the
  end screen (result, score breakdown, link to the server's replay viewer `/#/match/{id}`).
- [x] **h. Docs.** `docs/play-client.md` (running each part alone or together, controls, playing
  your own bot), sections in README.md and getting-started.md, `docs/architecture.md`.
- [x] **i. (Optional) AppHost.** Add the client as a separate resource with the server URL wired in.

## Risks

- **Static web assets outside Development.** Covered by `UseStaticWebAssets()` (step a). Published
  output (Docker image, Azure) gets the files under `wwwroot/_content` either way.
- **CSS order.** Moving rules into a file loaded before `styles.css` can change the cascade only
  where two rules of equal specificity set the same property. Tokens are custom properties on
  `:root` that nothing else sets; checked by diffing computed styles in step a.
- **Trimming / warnings as errors in WASM.** Reflection-based `System.Text.Json` in `NetRtsClient`
  meets the trimmer on publish. Use `TrimMode=partial` (only framework assemblies are trimmed) if
  the analyzer or a published build complains.
- **One client, one browser tab per player.** API keys live in `localStorage`, shared by every tab
  of the same origin; two people on one machine need two browser profiles (or the key field).
- **Rate limits.** 30 requests/s per player per match: one long-poll and at most one POST per tick
  is ≤ 20/s at the fastest 100 ms tick. Speed changes are rare.
- **CORS preflights.** The `Authorization` header makes the browser send an `OPTIONS` before every
  long-poll (the preflight cache is per URL, and each poll's URL differs). The server's CORS
  middleware answers them before authentication and the rate limiter, so they cost a round trip
  but no budget.
- **Polling while paused.** A long-poll on a paused match returns after 30 s with the same tick;
  the loop just polls again.

## Needs decision

No server change turned out to be needed. Open questions for you:

1. **Deploy the play client to Azure?** The AppHost adds it in run mode only (`aspire run` /
   `dotnet run --project src/NetRts.AppHost`). Publishing it would mean another App Service site
   (or a static host) plus a server address baked into its `appsettings.json`, so it is your call.
   Alternatively the server could serve it under `/play`, but that changes the server.
2. **A `speedAdjustable` field on `GameStateDto`?** The client reads it from `GET /matches/{id}`
   once the match starts, which works. Adding it to the state (add-only) would save that call;
   not needed.
3. **`MapView` hooks.** The play client draws its own layer on a second canvas and reads
   `MapView`'s camera and interpolation fields (`tileSize()`, `offset()`, `anim`, `interp()`,
   `cam`, `zoom`). If `mapview.js` is refactored, keep those, or give it a small public API for
   them. (Nothing changed in `mapview.js` for the play client.)

## Progress log

- Step 0: plan written.
- Step a: done. `netrts.css` holds lines 5–137 (the three `:root` palettes) and 814–880 (the Snow
  College palette) of the old `styles.css` verbatim, plus the `.map3d-canvas` / `.map3d-label`
  rules; `index.html` loads it just before `styles.css`. `promo/` and README paths follow the move.
  *Verified:* on a paused live match, the spectate page with the 3D view open, the home page and a
  replay, the computed style of every element (and its `::before`/`::after`) with the new
  stylesheets equals that with the old `styles.css` swapped in on the same page, in Snow/classic ×
  auto/light/dark: 0 differences (225–239 elements per page). No failed requests; three.js and its
  addons load from `_content`; the 3D view renders. `dotnet publish` puts the library under
  `wwwroot/_content/NetRts.Web.Rendering`, and both the published app and the build output run in
  `Production` serve it. Build clean, 127 tests pass.
- Step b: done. `play/NetRts.PlayClient` (Blazor WebAssembly, `TrimMode=partial`, port 5090) with
  `play/NetRts.Play.sln` (client + Protocol, Bots, Rendering) and an entry in `NetRts.sln`.
  `ClientSettings` keeps server URL / API key / name in localStorage; `ServerConnection` hands out a
  `NetRtsClient` for them. The layout loads the shared `theme.js` once its toggle buttons exist (it
  binds them on import). No import map: the play view is 2D, and Blazor's own import-map
  placeholder is left out. *Verified:* `/watch/{id}` draws a live exhibition through
  `playview.js` → `MapView` (map via `NetRtsClient.GetMapAsync`, frames via interop), the theme
  toggle cycles, no failed requests. Debug and Release builds clean, `dotnet publish` of the client
  clean, 127 tests pass.
- Step c: done. The lobby (`Pages/Home.razor`) connects to the server (house bots prove it's up),
  registers a player or takes a pasted key (validated with `GET /players/me`), creates a match
  against a house bot or an open match (seats, optional name, speed, map size, tick limit, seed),
  joins a waiting match from the list or by id/name, and lists your own unfinished matches to
  resume. `/play/{id}` is for now a waiting room that shows the id, name and a ready-to-paste
  BotRunner command, and polls the summary until the match starts. New test project
  `play/NetRts.PlayClient.Tests` (in both solutions). *Verified in the browser:* registered
  `human-tester`; opened `lab-test-1`, ran the displayed BotRunner command (`--join lab-test-1`)
  and the match started; played vs `sitter` (starts at once); a BotRunner `--create` match showed
  up under "Waiting for players" and Join seated us. 137 tests pass.
- Step d: done. `StateAdapter` turns `GameStateDto` into the spectator-frame shape (`RenderState`):
  units and deposits as they are, visible buildings plus remembered ones flagged `remembered`
  (drawn under the fog, so dimmed), one player entry with `you.slot` and `visibility`.
  `GameSession` is `BotLoop` for a person: rules, `WaitForStartAsync`, map, then
  `state?waitForTick=N+1&sinceTick=N`; it keeps the event log (newest first, 200), retries network
  errors from the same tick, stops on 401/403/404, and fetches the result at the end. `OrderQueue`
  + `GameSession.OrderAsync`: one `POST /commands` per tick, sent at once if this tick's request
  hasn't gone, otherwise with the next state; `CommandResult` rejections and `CommandFailed`
  events (matched by `commandId`) become `OrderOutcome`s with the order's label. HUD: resource bar,
  base (construction, production queue, research), orders and events, scores. Labels come from
  `lore.js` through `playview.js` (`loreTables()`), so the HUD uses the same brand vocabulary as the
  map. The step-b spectate page is gone. *Verified:* adapter tests (incl. the JSON names `mapview.js`
  reads), session tests on a fake server (whole match, one request per tick with batching, rejected
  orders, dropped long-poll, no orders after the end, no `spectate` requests); in the browser vs
  `sitter` at 500 ms the fogged map shows only our corner and advances, the HUD updates, and the
  network log is one long-poll per tick and nothing else.
- Step e: done. `playview.js` adds an input canvas over the map: left click / drag box /
  double-click / right-click / middle-drag pan / wheel zoom / arrow-key scroll, hit-testing units
  at the positions `MapView` is drawing them (its interpolation), and reports tiles and unit ids
  to `PlayController` ([JSInvokable]). It draws selection rings, rally lines, the drag box and
  order markers each frame; `MapView` is untouched. C#: `Selection` (click, shift-add/remove, box,
  double-click or ctrl+click = all of that type, own building, inspect enemies/deposits, pruned
  every tick), `OrderBuilder.Smart` (enemy → `Attack` by id; remembered building → attack-move to
  its tile, since the API refuses ids it can't see; deposit → workers `Gather`, others `Move`;
  own construction site → workers `Build` to help; building selected → `Rally`; else `Move`),
  `AttackMove` (A, then click) and `Stop` (S). `DepositMemory` keeps deposits seen earlier so
  workers can be sent into the fog, and forgets ones seen gone. Orders that belong together go in
  one request. *Verified:* 37 client tests; in the browser vs `sitter`: clicked worker #66,
  right-clicked (10, 11) → `POST /commands` 200 `{"accepted":1,"commandId":12}` and the worker
  walked there; drag-boxed 5 workers → right-click a deposit → "5 Workers: mine grubs #17"
  accepted; `A` then click → attack-move accepted, cursor and hint shown in targeting mode.
- Step f: done. `CommandCard` builds the buttons from `GET /rules` (costs, prerequisites, what each
  building trains, the next tier of each upgrade) and says why one is unavailable (prerequisite,
  ore, queue full, under construction, already researching here or elsewhere). Hotkeys: with
  workers B/C/D/T/G build; with a building W/S/A/C train (Shift = five) or, at a TechLab,
  W/A/M/H research the next Weapons/Armor/Mobility/Harvesting tier; A/S attack-move/stop for units;
  Esc cancels or deselects; Space looks at the selection (or the HQ); Ctrl+1–9 set a group,
  Shift+1–9 add to it, 1–9 recall, twice to look. `PlayController.Budget` subtracts what this
  tick's orders cost, since the API checks every command against the ore you have *now*.
  `Placement` checks rock, known deposits, buildings (visible and remembered), visible enemy units,
  prerequisites and the budget, allows helping your own site of the same type, and sends the ghost
  a per-tile mask; the HUD shows why the tile under the pointer is red. JS: build ghost, edge-of-map
  scrolling, minimap (terrain, deposits, buildings, units, fog, camera rectangle; click/drag to
  look, right-click to order), camera starts on your HQ. Tests now reference the engine so the
  card is checked against the real `GameRules`. *Verified:* 61 client tests; in the browser vs
  `sitter` at 500 ms: B with a worker showed the ghost (red over the HQ with "There's a building
  there"), placed a Housing (Barracks), five workers right-clicked onto the site helped finish it,
  S at the Housing trained a Soldier, right-click set the rally point and the Soldier walked to it,
  Ctrl+1 / 1 recalled the building; minimap and camera as expected in a screenshot.
- Note for testing: rebuilding the client while `dotnet run` serves it leaves the dev server with
  stale fingerprints (stuck on "Loading…"); restart it after a rebuild.
- Step g: done. `MatchControls` (`POST /speed`, `/step`; `NetRtsClient` doesn't wrap them).
  `GameStateDto` has no `speedAdjustable`, so the screen reads it from `GET /matches/{id}` (public
  metadata) once the match has started. Pause/Resume (also P), Step while paused, speed choice;
  the page applies each speed response at once, since a paused long-poll only returns after 30 s.
  Surrender with a confirm. End screen: Victory/Defeat/Draw, how and when, the result table from
  `GET /result`, a link to the spectator site's replay viewer, back to the lobby, or hide it to
  look at the map. A controls cheat sheet in the sidebar. Event and error messages are retold in the
  brand's words by the shared `lore.badgerize` (through interop, cached). Fixes: the play page is
  now a thin route over `GameScreen @key=MatchId`, so going straight from one match to another
  starts a fresh session (it used to keep the old one); the Blazor import-map placeholder is back
  (without it the *published* client can't start). *Verified:* 63 client tests; in the browser vs
  `rusher`: Pause held the tick at 21, Step moved it to 22, P resumed, 0.25 s sped it up, the
  80-tick limit showed "Defeat — Honey Badger won on score at the time limit" with scores and a
  replay link; vs `sitter`, Surrender → confirm → "won by surrender". The Release publish, served as
  static files, registered a player, started a match, rendered and sent an accepted Move.
- Step h: done. `docs/play-client.md` (what it is, running each part alone or together, the lobby,
  playing your own bot, controls, ticks and orders, how it fits together); a "play it yourself"
  section, a docs row and project rows in README.md; a pointer box and a "where next" line in
  getting-started.md; a "two front ends, one renderer" section and diagram in architecture.md; a
  "beyond the spec" note in requirements-coverage.md; the new projects in CLAUDE.md.
- Step i: done. `NetRts.AppHost` references the client and adds it as resource `play` in run mode
  only; the publish (Azure) model is unchanged. The client keeps its default server address,
  which is the server's fixed port 5080 under Aspire too (the WASM app can't read Aspire's
  environment variables). *Verified:* `dotnet run --project src/NetRts.AppHost` with Docker served
  both 5080 and 5090 through Aspire's proxy; the lobby connected (and explained that the SQLite
  test key is unknown to the PostgreSQL database). (The AppHost's own `http` launch profile fails
  without `ASPIRE_ALLOW_UNSECURED_TRANSPORT`; that was already so and is untouched.)

## Status

All steps are done. Everything builds warning-free, `dotnet test` passes (190 tests: 127
existing, 63 new), and the spectator site is unchanged (computed-style diff in step a).
What a next round could add: the 3D view in the play client (needs merging three.js into Blazor's
import map), selecting several buildings at once, waypoint queues (the API has none: a later
order replaces the earlier one), sounds, and a Playwright test that drives the browser in CI.

## After the plan

- Idle worker indicator: an *Idle n* button in the top bar, lit (and pulsing, unless reduced motion
  is on) while any of your workers is `Idle`. Clicking it, or pressing **I**, selects the next idle
  worker in id order and moves the camera to it; repeat to cycle. *Verified:* two controller
  tests; in the browser it showed 5 at the start, two clicks selected workers #72 and #73 and
  moved the camera, and it went dark and disabled once every worker was mining.
