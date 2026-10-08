# EmpireSim — starter project (working title)

A .NET MAUI Blazor Hybrid starter for the 17th-century grand strategy game.
The game simulation lives in pure C# (`EmpireSim.Core`) so it can be tested
without a device; the MAUI app (`EmpireSim`) is the Blazor Hybrid shell.

**UI direction (2026-10-08):** the app opens with an ornate nation-select screen
(advisor, crest, country dropdown, parchment map, nation stats) for new users,
then a main menu in the competitor's style: wooden resource bar, date + crest
medallion + speed controls, and a parchment tile grid (Command staff, Troops,
Diplomacy, Tribute, Trade, Production, Assemblies, Laws, Religion, Statistics,
Population, Events). Tiles route to the existing system pages.

## Structure

```
empire-sim/
├── EmpireSim.sln
├── src/
│   ├── EmpireSim.Core/          # Game engine — UI agnostic, fully testable
│   │   ├── Models/              # GameState, Nation, Province, GameSpeed
│   │   └── Services/
│   │       ├── Balance.cs           # ALL tunable numbers live here
│   │       ├── GameClock.cs         # Daily tick timer (pause/normal/fast/very fast)
│   │       ├── SimulationService.cs # Daily economy: food, taxes, upkeep, payday, growth
│   │       ├── SaveService.cs       # JSON save/load (SQLite can replace this later)
│   │       └── GameEngine.cs        # Facade the UI talks to (singleton)
│   └── EmpireSim/               # MAUI Blazor Hybrid app (Android/iOS/Mac/Windows)
│       ├── MauiProgram.cs           # DI wiring
│       ├── Components/Pages/Dashboard.razor  # Date, speed controls, stocks, warnings, log
│       └── wwwroot/                 # index.html, css/app.css, js/map.js (map milestone)
└── tests/
    └── SimTests/                # Headless engine verification (console)
```

## Design rules baked in

- Clock starts **01-01-1600**, advances **one day per tick**; speed is player-controlled
  (paused / normal 1 day·s⁻¹ / fast 5 days·s⁻¹ / very fast 20 days·s⁻¹).
- Every day recalculates: food production − consumption, taxes, army upkeep accrual,
  population growth (surplus) or starvation (deficit).
- **Army maintenance payday every 180 days.** Miss it → 14-day grace period with a
  critical warning → unpaid soldiers desert (10%).
The MAUI app targets **.NET 10** (`net10.0-*`); the engine and tests stay on
.NET 9 — a .NET 10 app may reference a .NET 9 library. Match your installed SDK
or retarget.

- **Nation select first.** Every launch starts on a "Choose your empire" screen;
  Ottoman Empire, Persia, Mughal Empire, or Kazakh Khanate. The dashboard "New"
  button returns here.
- **Fixed top bar** (sticky): nation, date, clock-speed controls, treasury
  (Gold · Silver), soldiers, food, provinces — always visible. The dashboard is
  a menu grid (Map / Economy / Military / Diplomacy / Espionage / Laws);
  tapping 🏠 in the top bar returns to it.
- **Invasions march.** Launching an invasion commits the force immediately and
  the army travels for distance-based days (4–30); the Military page's
  CAMPAIGNS section shows % travelled with a progress bar. The battle resolves
  on arrival; if peace is made meanwhile the army marches home.
- **No monthly popups.** Only critical warnings interrupt; everything else is
  pulled from the dashboard by the player.

## Run it (Windows)

1. Install **Visual Studio 2022** with the **.NET MAUI** workload
   (or: `dotnet workload install maui` with the .NET 9 SDK).
2. Open `EmpireSim.sln`.
3. Set the Android emulator (or Windows Machine) as the target and press F5.
4. Dashboard shows the date, speed controls, treasury/food/population/soldiers,
   warnings and the chronicle log. Save / Load / New Game buttons included.

> The MAUI app itself can only be compiled on Windows or macOS
> (MAUI workloads are not supported on Linux). The engine below was
> verified on Linux instead.

## Verify the engine (any OS with .NET 9 SDK)

```bash
dotnet run --project tests/SimTests
```

Runs 13 checks headlessly: new-game state, a 200-day simulation crossing the
first 6-month payday, save/load round-trip, and the missed-payday →
grace-warning → desertion path. All must print PASS.

## What's done / what's next

- [x] **World map of 1600 (done)** — stylised 2200x1150 Eastern Hemisphere with
  92 provinces across 41 nations of 1600 — the 16 great powers plus minor
  states (Denmark-Norway, Crimean Khanate, Ethiopia, Thailand, Korea, Bukhara,
  Holy Roman Empire, Scotland, Genoa, Papal States, Italy, Croatia, Vietnam,
  Burma, Ahom Kingdom, Northern Yuan, Nepal, Kongo, Jianzhou Jurchens,
  Cambodia, Laos, Malaysia, UAE, Micronesia, Easter Island) — plus neutral
  territories and 3 uncharted frontier regions; hegemony needs 20 provinces; canvas rendering with
  drag-pan, wheel/pinch zoom and tap-to-select via JS interop (`wwwroot/js/map.js`); `Map.razor` page with a
  province info card; top nav (Dashboard | Map). Map data verified by SimTests.
- [x] **Economy milestone (done)** — resources Wood / Iron / Goods; buildings
  Farm / Mine / Sawmill / Workshop with costs and build times; construction
  queue per province (costs paid upfront, progress bars); production chains
  (mine → iron, sawmill → wood, workshop: 2 wood + 1 iron → 1 goods);
  `Economy.razor` page with stocks, sell-goods trade action, per-province
  build buttons; 14 new SimTests covering construction, chains and trade.
- [x] **Military milestone (done)** — 4 land unit types (Musketeer, Pikeman,
  Cavalry, Cannon) with per-type costs/upkeep + Warships; instant recruitment
  (+100/+1000, cannons and warships cost iron/wood); 3 commander roles
  (Commander-in-Chief, Land, Fleet) with hire costs, daily wages and upkeep/
  tax bonuses; army maintenance UI with accrued upkeep, payday countdown,
  grace status and Pay-now; desertion drains unit stacks. 19 new SimTests.
- [x] **Diplomacy/espionage milestone (done)** — per-nation relations
  (-100..+100) with drift, gifts, tribute demands (paid when your army is
  1.5x theirs, war risk when refused), trade pacts (daily income), declare
  war / sue for peace, border attrition while at war, AI war declarations
  and AI peace offers as critical warnings; spy networks with strength
  growth, steal treasury / sabotage buildings / incite revolt operations
  with discovery risk; `Diplomacy.razor` and `Espionage.razor` pages.
  24 new SimTests (seeded RNG for determinism).
- [x] **Period currency (done)** — Ottoman Akçe (silver) and Sultani (gold),
  1 Sultani = 100 Akçe. Split treasury into silver/gold purses with free
  exchange on the Economy page and automatic gold→silver conversion when
  paying; wealth displays as "52 Sultani · 30 Akçe". 11 new SimTests.
- [x] **Real warfare (done)** — invasions with commit-size choice, battle
  resolution (unit strengths, garrison fraction, home advantage, casualties),
  province capture, nation elimination, and AI invasions against the player
  with critical warnings. 9 new SimTests.
- [x] **Laws & religion (done)** — 4 toggleable edicts (War Taxes, Grain Dole,
  Military Drills, Merchant Charters) with real trade-offs wired into taxes,
  growth, upkeep, battle strength and trade; 3 religious stances
  (Pragmatic/Devout/Tolerant); `Laws.razor` page. 10 new SimTests.
- [x] **Colonisation (done)** — 3 uncharted frontier regions on the map;
  colony expeditions (2000 silver, 3000 food, 5 warships, 30 days) found new
  provinces; one expedition at a time. 8 new SimTests.
- [x] **Balance & victory pass (done)** — hegemony victory at 20 provinces,
  defeat on losing everything (or an AI reaching hegemony), dashboard
  banners; 5-year autoplay validation (a passive player survives).
  12 new SimTests.
- [ ] Tune everything in `Balance.cs`.
