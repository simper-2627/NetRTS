# Contributing to Badger Brawl

## Ground rules

- **Game behaviour lives in `NetRts.Engine` and must stay deterministic.** No clocks, no
  `System.Random`, no iteration over unordered collections, explicit tie-breaks. If you add state,
  include it in `StateHash()` so the replay test keeps you honest.
- **Balance numbers live in `src/NetRts.Engine/rules.json`** and reach bots through
  `GET /api/v1/rules`. Update `docs/bot-guide.md` when you change them.
- **The wire contract lives in `NetRts.Protocol`.** Changing a field name breaks every bot in every
  language; add fields rather than renaming, and bump the API version for breaking changes.
- **The server stays thin.** Anything that touches a `GameSimulation` goes through `MatchHost` so it
  happens under the match lock.

## Changing the rules

Every unit, building and upgrade is defined in `src/NetRts.Engine/rules.json`. The build copies it
next to the server, which reads it at startup. Edit it, restart (`dotnet run --project src/NetRts.Server`),
and the startup log prints `Rules <version> from <path>`. A file with mistakes stops the server with
every problem listed: unknown names, misspelt stats, a `maxHp` of 0, and so on.

- **`units`**: the name is the type bots use. Stats are `cost`, `buildTicks`, `maxHp`, `damage`, `armor`,
  `range`, `attackCooldown`, `vision`, `speed`, plus optional `producedAt` (the building that trains it),
  `minRange`, `shotTicks` (siege), `isWorker` (gathers and builds; needs `carryCapacity` and
  `gatherPerTick`) and `serumBoost`.
- **`buildings`**: `cost`, `buildWork`, `maxHp`, `armor`, `vision`, plus optional `damage`, `range`,
  `attackCooldown` (it fires at enemy units), `storageBonus`, `isDropOff`, `canResearch`, `requires`,
  `requiresUpgrade` and `isHeadquarters` (lose them all and you're out). A building trains the units
  that name it as `producedAt`.
- **`upgrades`**: `cost`, `researchTicks`, optional `requires` and `effect`, and the stats it sets:
  `"units": { "Soldier": { "damage": 15, "maxHp": 75 } }`, likewise `"buildings"`. Values replace the
  current ones, for units and buildings already on the field as well as new ones. HP keeps its share
  of max HP, and if two upgrades set the same stat, the later research wins. Only number stats can
  be set.
- **`economy`** and **`serum`**: starting ore, the starting building (it must be a headquarters) and units,
  the production queue limit, and how long and how strong the super-soldier serum is.

Names are matched case-insensitively, but references inside the file must spell a name exactly as
it is declared. The house bots, achievements and spectator site know the classic names (Worker,
Barracks, ...), so a type you add plays fine, but those only count it where they go by its flags
rather than its name.

## Tests

Write the test first where you can.

- Rules and mechanics → `tests/NetRts.Engine.Tests`. Use `TestGames.New()` and the internal hooks in
  `GameSimulation.Testing.cs` to stage exact scenarios.
- API behaviour → `tests/NetRts.Server.Tests` (real HTTP pipeline, isolated SQLite file, manual
  ticks via `MatchHost.Advance`).
- If you change the house bots or balance, run `FullMatchTests` and make sure matches still end
  decisively.

```bash
dotnet build
dotnet test
```

## Pull requests

1. Branch from `main`.
2. `dotnet build` must be warning-free (warnings are errors) and `dotnet test` green.
3. Describe user-visible changes (API, rules, balance) in the PR.
