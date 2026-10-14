# NetRts Development Guidelines

Auto-generated from all feature plans. Last updated: 2025-12-10

## Active Technologies

- C# 12 with .NET 10 (LTS) (001-rts-game-engine)

## Project Structure

```text
src/
tests/
```

## Commands

# Add commands for C# 12 with .NET 10 (LTS)

## Code Style

C# 12 with .NET 10 (LTS): Follow standard conventions

## Recent Changes

- 001-rts-game-engine: Added C# 12 with .NET 10 (LTS)

<!-- MANUAL ADDITIONS START -->
## NetRts specifics

RTS game for bot programming competitions: bots play over REST, the server ticks the simulation.

```bash
dotnet build                                   # warnings are errors
dotnet test                                    # Microsoft.Testing.Platform (see global.json)
dotnet test --project tests/NetRts.Engine.Tests
dotnet run --project src/NetRts.Server         # http://localhost:5080 (SQLite), /scalar for API docs
dotnet run --project src/NetRts.AppHost        # Aspire + PostgreSQL (needs Docker)
dotnet run --project src/NetRts.BotRunner -- --server http://localhost:5080 --name x --strategy rusher --vs sitter
dotnet run --project play/NetRts.PlayClient    # http://localhost:5090, play by hand against bots
```

- `src/NetRts.Engine` — deterministic simulation (`GameSimulation` partials: core, Commands, Tick, Views, Testing), `GameRules`, `MapGenerator`, `Pathfinder`. No clocks, `System.Random` or unordered iteration; the replay test checks determinism.
- `src/NetRts.Protocol` — JSON DTOs shared with bots. Add fields; don't rename them.
- `src/NetRts.Server` — ASP.NET Core minimal APIs; `Matches/MatchHost` wraps one simulation behind a lock.
- `src/NetRts.Web.Rendering` — Razor class library of static files (`mapview.js`, `3d/**`, `lore.js`, `util.js`, `theme.js`, `netrts.css`, three.js) served at `_content/NetRts.Web.Rendering/`; shared by the spectator site and the play client. The spectator site must look the same after any change here.
- `play/NetRts.PlayClient` — Blazor WebAssembly manual play client (`dotnet run --project play/NetRts.PlayClient`, http://localhost:5090), its own `play/NetRts.Play.sln`, tests in `play/NetRts.PlayClient.Tests`. It is an ordinary API player: fogged `/state` only, never `/spectate`. See `docs/play-client.md`; plan and progress in `docs/play-client-plan.md`.
- `src/NetRts.Bots` — `NetRtsClient`, `BotLoop`, `PlanBot` + house bot plans.
- `docs/bot-guide.md` is the player-facing rules/API doc — keep it in sync with `GameRules` and the endpoints. `docs/requirements-coverage.md` maps the spec to code.
- `src/NetRts.AppHost` — Aspire: local dev and the Azure deployment (private network, see `docs/deploying.md`).
<!-- MANUAL ADDITIONS END -->
