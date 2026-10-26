# Playing by hand: the play client

The play client lets a person play Badger Brawl as an ordinary RTS, with a mouse and keyboard, in
the browser. Use it to test your bot by playing against it, or to let anyone sit down and play a
bot at the competition.

A person here is **just another API player**. The client registers (or uses a pasted API key),
long-polls `GET /api/v1/matches/{id}/state`, which is fogged exactly as your bot's is, and sends
orders with `POST /api/v1/matches/{id}/commands`, at most once per tick. Same fog, same tick, same
rules, same rate limits. It never reads the omniscient `/spectate` feed while you play, so beating
a bot by hand means the bot really lost. The server can't tell a person from a bot, and doesn't
need to.

## Running it

Each part runs on its own. You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet run --project src/NetRts.Server
```

The game server at http://localhost:5080, the same as always.

```bash
dotnet run --project play/NetRts.PlayClient
```

The play client at **http://localhost:5090**. It talks to `http://localhost:5080` by default.
Change that in the lobby (the choice is kept in your browser) or for everyone in
`play/NetRts.PlayClient/wwwroot/appsettings.json` (`"NetRts": { "Server": "…" }`). The server's
CORS policy already lets any page call it.

```bash
dotnet run --project src/NetRts.BotRunner -- --server http://localhost:5080 --name mybot --strategy rusher --join <match>
```

Any number of bots, from other terminals or other computers, exactly as before.

With Docker, `dotnet run --project src/NetRts.AppHost` (Aspire) starts the server against
PostgreSQL *and* the play client, on the same ports. The Azure deployment (`azd up`) doesn't
include the play client.

Point several play clients (one per person, or one per browser profile) at one server for a lab
or the competition floor. The client is a static site after
`dotnet publish play/NetRts.PlayClient -c Release`: host the `wwwroot` folder anywhere that serves
files (it needs `application/wasm` for `.wasm` files and a fallback to `index.html` for deep links).

The client has its own solution, `play/NetRts.Play.sln`, with only what it needs: the client, its
tests, `NetRts.Protocol` and `NetRts.Bots` (for `NetRtsClient`). The map renderer it shares with the
spectator site is linked in from `src/NetRts.Server/wwwroot` by its `.csproj`. The root `NetRts.sln` includes it too, so
`dotnet build` and `dotnet test` at the root cover everything.

## In the lobby

1. **Server.** The address of the game server, then *Connect*.
2. **You.** Register a player name (you get an API key, the same kind a bot uses, kept in this
   browser) or paste the key of a player you already have, such as your bot's: then you play *as*
   your bot.
3. **New match.**
   - Pick a **house bot** (`sitter` mines and nothing else; `rusher`, `economist` and `balanced`
     fight back) and *Play*. The match starts at once.
   - Or pick **Open match**, choose the number of seats and an optional name, and *Open the match*.
     The waiting room shows the match id and name and a BotRunner command to paste. Your own bot
     joins with `POST /api/v1/matches/{id}/join` (or finds the id by name with
     `GET /api/v1/matches/named/{name}`) and plays as usual. The match starts when every seat is
     taken.
   - *Settings*: speed (seconds per tick), map size, tick limit, map seed.
4. **Join a match.** Matches waiting for players are listed, or type an id or name. *Your matches*
   lists ones you are in, so a reloaded page can resume.

## Playing your own bot

```bash
# terminal 1
dotnet run --project src/NetRts.Server
# terminal 2
dotnet run --project play/NetRts.PlayClient
```

In the play client: register, choose **Open match**, name it (say `me-vs-mybot`), and open it.
Then start your bot against that name or id, whatever language it's in. With the C# runner:

```bash
dotnet run --project src/NetRts.BotRunner -- --server http://localhost:5080 --name mybot --strategy balanced --join me-vs-mybot
```

Or the other way round: let your bot create the match (`--create`) and join it from the lobby's
list. Practice matches between two real players count toward the practice ladder like any other.

## Controls

| Input | Does |
|---|---|
| Left-click | Select a unit (the one drawn under the pointer) or one of your buildings; click an enemy or a deposit to inspect it |
| Drag | Box-select your units |
| Shift + click / drag | Add to or remove from the selection |
| Double-click, Ctrl + click | Select all your units of that type |
| Right-click | Smart order: on an enemy, attack it; on an enemy building last seen in the fog, attack-move there; on ore, workers mine (the rest walk); on your own construction site, workers help build; otherwise move. With a building selected: set its rally point |
| **A**, then click | Attack-move: walk there, fighting anything on the way |
| **S** | Stop |
| With workers: **C B D T G** | Place a Command Center, Barracks, Resource Depot, TechLab or Guard Tower. The ghost is green where it may go and red (with the reason) where it may not. Shift-click places several |
| With a building: **W S A C** | Train a Worker, Soldier, Archer or Scout. Shift trains five |
| At a TechLab: **W A M H** | Research the next tier of Weapons, Armor, Mobility or Harvesting |
| **Ctrl + 1–9** | Save the selection as a control group (**Shift + 1–9** adds to it) |
| **1–9** | Recall a group; press twice to look at it |
| Arrow keys, pointer at the map edge, middle-drag | Scroll |
| Wheel, **+** / **−** | Zoom |
| **Space** | Look at the selection (or your HQ) |
| **I**, or the *Idle* button | Select the next idle worker and look at it (press again for the next one) |
| Minimap | Click or drag to look there; right-click to send the selection |
| **P** | Pause or resume (only against house bots) |
| **Esc** | Cancel targeting, then clear the selection |

The command card in the sidebar has the same actions as buttons, with costs, and says why one is
unavailable (a missing prerequisite, not enough ore, a full queue). The client keeps an ore budget
for the orders you give within one tick, because the server checks every command against the ore
you have *now*: five Barracks with 500 ore would all be accepted and four would fail a tick later.

## What you see

- **The map** is the spectator site's own renderer, drawing your fogged view: your units and
  buildings, enemies only while you can see them, enemy buildings you saw earlier dimmed under the
  fog where you last saw them. Units glide between ticks.
- **The top bar**: tick, ore and storage, income per minute, workers, the *Idle* button (lit while any worker has nothing to do), army, the server's command
  queue, orders not sent yet, upgrades. Against house bots: Pause, Step and Speed. *Surrender…*
  asks first.
- **The sidebar**: the minimap, the selection and its command card, your buildings (construction,
  training queues, research), *Orders and events* (orders the server rejected with its reason, and
  your own events: units trained, losses, kills, buildings finished), and the score.
- **The end screen**: who won and how, everyone's score and stats, and *Watch the replay*, which
  opens the spectator site's replay viewer (fine now: the match is over).

## Orders and ticks

Orders queue in the browser and go out in one `POST /commands`, at most once per tick: as soon as
you give the first order of a tick, and with the next tick for anything after that. The server
runs them at the start of the next tick, like a bot's. With the long-poll that's two requests per
tick, well inside the 30 per second a player may make in a match, even at 0.1 s per tick. (Each
request is preceded by a CORS preflight because of the `Authorization` header; the server answers
those before the rate limiter.)

## How it fits together

```text
 play/NetRts.PlayClient (Blazor WebAssembly)
   Pages/Home.razor ............ lobby
   Components/GameScreen.razor . one match: HUD, sidebar, end screen
   Game/GameSession ............ BotLoop for a person: rules, wait for start, map, long-poll /state;
                                 OrderQueue: one POST /commands per tick
   Game/StateAdapter ........... GameStateDto → the frame MapView draws (+ remembered buildings)
   Game/PlayController ......... input → Selection / OrderBuilder / CommandCard / Placement → orders
   wwwroot/js/playview.js ...... MapView + an input canvas: selection rings, drag box, build ghost,
                                 minimap; reports clicks in tile coordinates to PlayController
        │ JS interop
 src/NetRts.Server/wwwroot (the spectator site and replay viewer)
   mapview.js, util.js, lore.js, theme.js, images/snow-s-*.png: linked in by the .csproj and
   served at the same paths here
```

The client is tested in `play/NetRts.PlayClient.Tests`: the state adapter (including the JSON
names `mapview.js` reads), the session loop and order batching against a fake server, selection
and right-click rules, the command card and placement checks against the real `GameRules`, and
the hotkeys.
