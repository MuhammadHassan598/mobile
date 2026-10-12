# EmpireSim — starter project (working title)

A .NET MAUI Blazor Hybrid starter for the 17th-century grand strategy game.
The game simulation lives in pure C# (`EmpireSim.Core`) so it can be tested
without a device; the MAUI app (`EmpireSim`) is the Blazor Hybrid shell.

**UI direction (2026-10-08):** the app opens with an ornate nation-select screen
for new users: dark navy header (EmpireSim + settings/help), a CIVILIZATIONS
rail with 41 tappable emblem buttons, advisor portrait + speech bubble, a red
banner with the nation's emblem, a parchment world map with an emblem pin on
the capital, a stats card (minerals: wood/stone/iron/copper/lead, historical
1600 population, military strength, income/day, state religion, capital city),
a green START button, and a painted city backdrop. Then a main menu in the
competitor's style: wooden resource bar, date + crest medallion + speed
controls, and a parchment tile grid (Command staff, Troops, Diplomacy,
Tribute, Trade, Production, Assemblies, Laws, Religion, Statistics,
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
  war / peace negotiation, border attrition while at war, AI war declarations
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
- [x] **Diplomatic actions (done, all 14 + Ask Attack and Annex)** — the country panel has three pages
  (⚔️ hostile · 🏛️ treaties & cooperation · 🎁 relations & expansion). Real mechanics, all saved with the game:
  Embassy, Non-Aggression Pact (enforced at war declaration, tribute, spy ops, AI war and marches),
  Defensive Alliance (allies march on whoever attacks you), Trade Agreement (cheaper imports, richer
  exports), Send Troops (temporary loan), Call to Arms and Ask Attack (the friend's own army joins your
  war; Ask Attack needs relations 70+ but no alliance), Give Army, Send a Gift, Improve Relations, Ask for
  Aid, Present a Colony, Missionary Work (uses the existing religions), **Research Contract** (the partner
  shares half its daily research for a year), **Support Sovereignty** (a two-year independence guarantee:
  you may not attack the country while it lasts; it does not stop a victor annexing a country it has beaten).
  **Victory spoils** (`VictoryService`): when the player wins a battle the country is no longer annexed
  automatically; a banner (on every page, and behind the 🏰 Annex button) offers three choices, each with
  its own flow — **Annex** (the whole country becomes yours), **Take resources** (30% of its treasury and
  stocks; it survives, resentful, relations 20) or **Nothing** (white peace; it is grateful, relations 50).
  Undecided for 30 days it is let go. While the choice waits the beaten country fights, invades and pleads
  no more. AI winners settle their wars by what the war was fought for (see *A living AI world*).
  Annexation protection is one shared check (`TreatyService.AnnexationBlock`): an Assembly
  `annexation_ban` policy (a sovereignty guarantee is not a block: it binds the guarantor, not the victor) stops invasions from being launched and makes the
  Annex choice unavailable (the other two still work). Military → Allied Assistance now moves troops OUT of
  the ally's army (10%, −2 relations, 30-day cooldown) instead of copying them.
  **Research** (`/research`, 🔬 tile): the player banks 1 point a day + 1 per 10 million people (+ contract
  income) and spends it on 3 technologies (battle strength, trade income, production output). AI
  countries never research, so nothing changes for them.
  Rules live in `GameEngine.Diplomacy.cs`, `TreatyService.cs` and `ResearchService.cs`; numbers in
  `Balance.cs`; 20 new SimTests sections (28–47), including victory spoils (45).
- [x] **Movement Report (done)** — the main menu's 🧭 Movements tile opens `/movements`: an **Events** tab
  (every movement that concerns **your country** — gold, goods, troops, marches, colonies, envoys, missionaries, spies,
  treaties, war and peace — with date, from ➜ to and a Done / Under way / Refused status; saved with the
  game, newest 1,500 kept, world news dropped first) and a **Mission location** tab (what is on the road or posted abroad right now:
  marching armies, troop loans, shipments, the colony expedition, spy networks, missionaries, embassies,
  with progress and days left). The report lists only what involves your chosen country: your own movements, wars and marches that touch your allies, and the fall of a friend or an enemy. Filter by the other country; group *By state* or *By date*. Every movement site calls
  `GameState.LogMovement`, which also writes the usual event-log line. Read side: `MovementReport.cs`.
- [x] **World news (done)** — its own section, apart from the Movement Report: the main menu's 📰 World news tile opens `/worldnews`, which lists what other countries do among themselves (everything that does not involve you or your allies), in two halves — **⚔ Military** (wars and declarations, marches and battles, soldiers lent, and plunder taken in war) and **🌐 Other** (alliances and treaties, gold, goods, colonies, missions, diplomacy). Filter by country; group *By state* or *By date*. It reads the same saved movement records as the Movement Report, so a record is in exactly one of the two. Read side: `WorldNewsReport.cs`.
- [x] **A living AI world (done)** — AI countries are independent of the player and of each other.
  *Economy:* only the country you pick starts short of food (the 30/15/10% shortage challenge); every other
  country's mills cover its needs and are topped up as it grows (`ConsumptionService.EnsureSupply`), so AI
  countries no longer starve; they also rebuild their armies from their treasury toward a target that grows
  with their population. *Diplomacy and war* (`AiWorldService`): AI countries hold opinions of each other
  (faith warms, neighbours quarrel), form defensive alliances with every friend in reach (**no limit** on
  how many), call their allies in (defenders' allies usually join), send armies marching through the normal
  march/battle system, and make peace (worn out, a long stalemate, or a victory's terms; afterwards regard is
  neutral so they do not bounce straight back to war).
  *Any country may go to war with any other in reach* — the stronger on the weaker, the weaker on the
  stronger, an AI country on the player — **there is no rule about who is stronger**. A ruler weighs
  *motives* (`AiWorldService.Motives`): **land** (a near, populous country is a prize), **grudge** (regard
  below zero), **fear** (a near neighbour with an army to match its own, unless it is a friend) and **duty**
  (the country is at war with one of its allies), against its **chances** (`Edge`/`Confidence`: its army
  plus the share of its allies' armies that would join, against the garrison it would meet, by the same rule
  battles are decided by). Appetite = motives × confidence; the keenest target is picked and the war
  declared at a steady pace (`AiWarPressure`). A weaker country is held back by poor odds, never barred, and
  allies tip the odds. The reason is shown in the Movement Report ("declared war on X — to humble a hated
  rival"). Treaties (alliance, non-aggression pact, guarantee) and a war already on still forbid attacks.
  *What a war ends in* follows from what it was **for** (`WarAim`, the heaviest motive when it was declared,
  stored on the war): a war for **land** ends in **annexation** the first time the aggressor wins a battle; a
  war of **grudge** takes the loser's resources and ends in peace; a war of **fear** is a white peace once the
  menace is beaten back. A defender that wins, or an ally with no war of its own, judges by its motives at
  that moment. Annexation depends on nothing but the war — **no relative-power percentage, no per-winner
  cooldown, no world-wide spacing, no forced plunder when a conquest is not possible**: if the Assembly
  forbids annexing (a real game rule) the battle is won and the war goes on. The world
  wakes up on 01-01-1601 (a calm first year), runs on its own random stream (so the rest of the simulation is
  unchanged), and what concerns your country appears in your Movement Report (the rest of the world's news is in its
  separate World news section). A country's panel lists its allies and who it is at war with. Typical result over 5 years:
  2–7 of 40 countries eliminated, 25–30 wars (mostly ending in tribute or a white peace), ~39 alliances, no
  shortages; over 15 years the map settles into several rival powers. A completely passive player keeps their
  country for 5 years in about four worlds out of five (measured 49 of 60; it was about two in three before annexed countries stopped adding their people to their conqueror's own totals). All numbers are in `Balance.cs` (`Ai*`); the only
  remaining limit on an AI country's own wars is `AiMaxWars` (one war it started itself at a time).
- [x] **Trade screen (done)** — products are sorted into three groups everywhere (Buy, Sell and Stock): **⚔ Military** (equipment), **🍞 Food** (food and everyday goods: clothing, wool, fur, horses, perfume...) and **⛏ Minerals** (wood, stone and the metals; `TradeCatalog.Groups`). The Sell list shows **every** product with the stock you hold (none dimmed), and the checks happen in the Sell popup: no stock, more than your stock, no price, or a buyer who cannot afford it each show a message and keep SELL disabled. Both popups have a **MAX** button (the most the seller will part with / your gold allows when buying; your stock / the buyer's gold allows when selling) and ±100 / ±1K / ±10K steps. The Buy popup now shows the price actually charged, with the import-law and trade-agreement discounts (`GameEngine.BuyPricePer1000`, `MaxBuyQuantity`, `MaxSellQuantity`).
- [x] **Inbox (done)** — the main menu's ✉️ Inbox tile (with an unread badge) opens `/inbox`, a new screen (there was no inbox before; the old notifications were transient dashboard warnings and an event log no page showed) with three tabs, each with its own unread count: **🕵️ Intelligence** (spy networks and missions, caught spies, army movements of enemies and of countries your spies watch, reports on foreign strength), **⚔️ Military** (wars declared on you or your allies, battles, invasions, peace, victories, calls to arms, allied help, army movements, reinforcements, the fall of an ally or enemy) and **📜 Other** (treaties and answers to proposals, aid and tribute, trade deliveries, Assembly, colonies, shortages, army upkeep, production, ruler rating, religion, research). Each message has a title, date, one-line summary and its country's emblem or event icon; unread ones are highlighted, opening one marks it read and shows its details, and 🗑 deletes every message after a confirmation (only the inbox is cleared; nothing that happened is undone). Messages are filed by the **type of event** that produced them (`InboxTopic` → `InboxCategory`, never by text) and are posted where the events really happen — mostly through `GameState.LogMovement(..., inbox: topic)`, so a message exists only for what concerns your country and only once; a shortage that goes on is one message kept up to date while unread. Saved with the game (`GameState.Inbox`, newest 500 kept). Nothing is invented to fill it. Rules in `InboxService.cs` / `InboxMessage.cs`; screen `Inbox.razor`.
- [x] **Production build button (done)** — the 📐 build button on every production row is always active (it used to grey out whenever you could not afford one mill). The build popup now does the checking: it says **how many of the chosen mill you can build right now and what limits it** (gold, wood, stone, iron or the build queue), has a **MAX** button and ±1 / ±10 steps (the old 100 cap is gone; the build queue is the real limit), and lists exactly what is missing for the number chosen, keeping Build disabled until it is possible (`GameEngine.MaxBuildProduction`).
- [x] **Trade needs an embassy (done)** — you can only buy from or sell to a country where you have set up an embassy (Diplomacy). The Trade country lists (Buy and Sell) show two groups: **🏛 Embassy — you can trade** (selectable; still blocked for a country at war, or when buying one with no stock) and **🚫 No embassy — set one up first** (shown greyed out with a "No embassy" tag, not selectable), with a shortcut button to Diplomacy. Sell Everything only sells to embassy countries. The rule is in the engine (`GameEngine.TradeBlock` / `HasTradeEmbassy`; BuyProduct, SellProduct and the MAX helpers all refuse without an embassy), so the screen and the game agree. Cancelling an embassy stops new trade with that country but goods already on the road are still delivered.
- [x] **The diplomatic web of 1600 (done)** — whichever country you choose starts with the embassies, friendships and quarrels it really had (`HistoricalDiplomacy`: ~100 historical ties between the 41 countries, each with a rating, an embassy flag and the reason; e.g. the Ottomans keep embassies in France, Venice, England and Crimea and are cold to the Habsburgs, Spain and Persia; England has the Dutch, Morocco and Russia and is hostile to Spain and the Pope; Korea has Ming and hates Japan). Pairs history says nothing about start neutral, a touch warmer between one faith (56) and cooler between different ones (48). A country with fewer than two embassies from the table gets two in its warmest acceptable neighbours within reach; an island far from everyone gets none. The Diplomacy list marks every country where you have an embassy (🏛), which is what lets you trade with it. Relations settle back to their *historical* level (`Nation.BaselineRelation`) and not to neutral, so a year of peace does not make the Habsburgs forget. **Old bad blood does not by itself start a war**: toward the player, an AI country's war grudge counts only what has soured below its historical level (a fresh insult, threat or war), so the passive-player guard still holds; wars on the player still come from land, fear and duty. AI-to-AI starting regard is unchanged. Saves from before this load as indifferent, as before.
- [x] **War update (done, build 73)** — five fixes to how a war starts, moves and ends, in `GameEngine.Attack.cs`, `MapGeography.cs`, `PeaceService.cs` and the shared popups `AttackDialog.razor` / `PeaceDialog.razor` / `PeaceBanner.razor`.
  - **Attack declares war when the troops are sent.** Attack (Diplomacy, and the Map's Attack button) opens a confirmation popup: the target, your **home army**, a slider / ± / 25-50-75-ALL control for how many to send, **sending and left-at-home** counts, the **estimated arrival** (days and date), and a warning about leaving home vulnerable (it names an enemy that would outnumber what is left and the counter-invasion it could answer with; red below the 500-man home guard). **Send Troops** validates everything first (`GameEngine.ConfirmAttack`): a treaty (pact, alliance, a guarantee you gave), an Assembly annexation ban, the 30-day cooldown on declaring war again, a won battle still awaiting its decision, an unknown country, or a troop count that is zero, above your home army or below the 500-soldier minimum refuses the attack *with the reason* — and then **no war is declared and no soldier leaves**. If war is already on, confirming only sends another army. Declaration and march are one action.
  - **March time comes from the distance between the capitals.** The fixed 14 days is gone: days = straight-line distance between the two capitals' map coordinates ÷ **one movement speed** (`Balance.MarchSpeedPerDay` = 30 map units a day), rounded up, never under a day, never capped (a border neighbour still takes its capital-to-capital time; a far country takes proportionally longer). A march stores its **start date and arrival date** (`MarchingArmy.StartDate/ArrivalDate`) and the daily update goes by the arrival date, so saving and loading changes nothing. If a capital has no coordinates the fallback is the centre of its territory polygons, else `Balance.MarchFallbackDays` (20); the event log names the countries that need geographical data and `MapGeography.MissingCapitals` lists them all. AI countries use exactly the same route function.
  - **Enemy counter-invasions are real marches.** An AI country that decides to press its attack takes half its home army out of that army *at once* (so the soldiers cannot be sent twice), marches them toward your capital for as long as the distance takes, and the battle is fought **on arrival** against the troops you actually have at home then (reinforcements that reach home first count). You get a report in the inbox / Movement Report when it sets out and another when it arrives; nothing resolves on the day it is launched. The existing decision about when it attacks is unchanged.
  - **A sovereignty guarantee no longer blocks annexation after a victory.** `TreatyService.AnnexationBlock` is now the Assembly's `annexation_ban` only. The guarantee stays in the diplomatic system: it still stops the guarantor attacking its ward, adds trust and warms relations — it just never silently overrides the winner's post-victory choice (player or AI). Annexing (`Warfare.AnnexNation`) is idempotent (a country is annexed once) and puts every asset in exactly one place: the winner takes the **treasury, the army's equipment, the crown domain, the lands on the map and the colonies** (with the people and buildings they added) at once; the **people, taxed workforce and tax rates, stocks of food, goods and minerals, farms, mines, mills, workshops and building queue stay** as the annexed territory's own local economy (see *Annexed States* below). Its army, fleet, reserves, officers and recruiting are gone; shipments still on the road follow the sale to the winner (one that would run from the winner to itself is settled on the spot); armies marching on the fallen country come home once.
  - **An annexed country is a territory under its master, and the map shows it.** It records `AnnexedById` / `AnnexedDate` (every country it had annexed is re-pointed to the new master), leaves the lists of independent countries (Diplomacy, Espionage, Assembly, Religion, Military allies; `GameState.LiveOtherNations()`), and the engine refuses to deal with it as a country (`FindNation` no longer finds it: no war, gifts, tribute, treaties, spying or talks). Its crown domain becomes the master's (`Nation.CrownDomains`: crown income scales with the domains held; the income statement shows "Crown Domains (x2)"). On the **map** its polygons belong to the winner's realm: same colour, the winner's name, tapping them selects the winner, the edge between the annexed lands and the realm is a faint dashed province line where their outlines share points (the engraved border runs round the whole realm), the old capital stays as a small city label, the card says what a country includes and has a *Manage annexed states* button, and the map redraws by itself when an annexation happens while it is open (`Nation.AnnexedPolygonCount`, `map.js`).
  - **Negotiations replace the 10-gold Sue for Peace** (`PeaceService`). Either side proposes terms: **gold**, **goods** (any tradable product), a **colony** (the only separable territory), **annexation** of the other country, and **peace** — each with an explicit giver and receiver. A proposal moves nothing and ends no war. The receiver may **accept** (every term applied once; the war ends if peace is among them), **reject** (the war goes on, nothing moves) or **counter** (a new offer one round later; four rounds at most, the last is accept-or-reject). Terms are validated when proposed *and* again on acceptance: a giver must own what it gives and never more gold/goods than it holds, a colony must be the giver's, an annexation obeys the Assembly ban and stands alone, the player cannot offer its own country; if anything fails no term applies. The AI answers a day later by how the war stands for it (`PeaceService.LeverageOf`): armies at home and on the road, battles won and lost, soldiers lost (`WarFront`, recorded as battles resolve), whether its army can go on fighting, the age of the war, the stakes (liquid wealth), its war aim and its regard — the losing side concedes more, the winning side demands more, and a per-offer mood means neither always says yes or no; it accepts, **counters** with terms it would itself take (refusing to give land, taking back some of what it was to pay, asking gold of the player), or rejects. A country that has had enough, or has the upper hand, also **puts terms to the player on its own** (an AI offer lapses after 30 days unanswered and does not end the war). Proposals, counteroffers, rejections and the final agreement are in the inbox (⚔️ Military: *Peace talks*, *Peace agreement*), and the negotiation state (offers, rounds, war records) is saved. There is **no fee for opening talks**: the offer is the whole price. 🕊️ Peace talks sits in a country's Diplomacy panel and on the Map; pending offers wait in a banner on every page. Talks are for wars with the player; AI-to-AI wars still end by exhaustion or a winner's spoils. Not modelled: occupation (countries are atomic, there are no provinces).
- [x] **Command Staff (done, build 74)** — its own screen (`/commandstaff`, reached from Command Control — the main menu's 🎖️ tile; that tile used to open Army Management by mistake), separate from **Army Management** (`/military`: soldiers, reserves, recruitment, mercenaries, equipment, warships, upkeep — untouched apart from showing the staff's discounts in its cost and upkeep lines). Six appointments in two groups — **Military leadership**: ⚓ Fleet Commander, 🪖 Land Forces Commander, 🎖️ Commander-in-Chief; **Civil administration**: 🪙 Chief Tribute Collector, ⚖️ Head of Trade, 🏗️ Head of Construction. Each card shows the post's icon and duty, the current appointee (skill tier, wage, since when) with the effects he really has, a three-candidate shortlist (capable / distinguished / renowned: dearer to appoint and to keep, stronger effects: ×1.0 / ×1.35 / ×1.7), Appoint / Replace buttons (the fee is paid once, nothing is refunded for the old holder), and a Dismiss button with a confirmation tap. The commander model already existed in the engine (`Nation.Commanders`, `HireCommander`) but its screen had been dropped from the Military page in build 30, so none of it was reachable; the rules now live in `CommandStaffService` and every effect is computed from the country's own appointments, so **the player and the AI countries are treated alike** — AI countries fill their own vacant posts one at a time (capable candidate, only posts they have something to run: army, fleet, taxes; they have no trade or construction of their own) when their treasury is ten times the fee. Effects (capable holder; each is read by the rule it changes): **Land Commander** −15% land upkeep, −10% recruiting cost, +5% battle strength of its armies (the existing `BattleStrengthMult` hook, attackers); **Fleet Commander** −15% naval upkeep, −10% warship cost (naval battles are not modelled, so no fighting bonus — the card says so); **Commander-in-Chief** −5% upkeep of every force (stacks with the two above instead of repeating them) and −25% war attrition (his old +5% tax moved to the tribute collector); **Chief Tribute Collector** +5% tax revenue and +25% tribute demanded; **Head of Trade** −5% price of what you buy, +5% revenue of what you sell, +10% trade-pact income; **Head of Construction** −15% construction time (nearest day, never under one; the Production screen shows the shortened days). Wages join the army's maintenance bill. One holder per post and one post per person; appointments are saved with the country (old saves' commanders load as capable). Death, ageing and loss of eligibility are not modelled (the game has no such systems). Candidate names are generated placeholders drawn from each country's culture, not historical office-holders.
- [x] **Annexed States (done, build 75)** — *Command Control → Annexed States → a territory → Annexed State Operations* (`/command`, `/annexed`, `/annexed/{id}`; the main menu's 🎖️ tile is now **Command control**, which opens a hub with Command Staff and Annexed States; Army Management stays the Troops tile). The smallest architecture that lets a territory live without bringing a country back: the annexed `Nation` object is kept as the territory's **local record** (people, taxed workforce and tax rates, stocks, buildings, building queue, shortages), still `IsEliminated` — so every AI loop, treaty, war and diplomatic rule skips it — and `SimulationService.AdvanceTerritory` runs it daily by the very same formulas as any economy (`AdvanceEconomy`, split out of `AdvanceNation` without changing it): raw materials, workshops, construction, births, tax, mills, consumption, shortages. The **master collects its tax** (with the master's own tax multipliers: edicts, stance, tribute collector) and any gold its mines dig, is told in the inbox what it finishes building and what runs short ("Italy: Farm completed", "Shortage in Italy"), and the territory itself never holds gold. **Counted once**: the master's own `Population` is unchanged; the realm total is `GameState.TotalPopulation` (top bar, Statistics, Population), the realm income is own + territories' (top bar, income statement "Annexed states"); **Assembly**: an annexed territory has no vote and its people add to its master's voting power by the existing rule (one per 500,000, `AssemblyService.VotingPower(nation, allNations)`, `GameState.VotingPowerOf`). The **list** shows each territory's flag and name, population, tax and other income, buildings, production, stocks, shortages and status; an empty state when there are none. The **operations screen** (four tabs, mobile-first) has: *Economy & Tax* (revenue, approval, safe threshold, six tax sliders with a **preview by the existing taxation rules before applying**: revenue change, approval target, extra shortage deaths), *Population* (births, deaths, net, groups, realm total, the Assembly votes it adds), *Production* (farms, mines, sawmills, workshops, mills and their output, and per item the need, output, stock, cover and shortage) and *Administration* (build production buildings or a farm/mine/sawmill/workshop with a **quote before building**: gold from the master's treasury, wood/stone/iron from the territory's own stockpile, time shortened by the master's head of construction; the queue; and **send stock to the national stockpile**, once, at once). Every operation is checked against `AnnexedStateService.Authority`: only the master of an annexed territory can manage it (another country's territory, an independent country or an unknown id are refused with the reason). **AI masters** govern their territories through the same daily simulation and keep their people fed as they do their own (`EnsureSupply`). Save/load keeps it all; a territory annexed in an older save (emptied then) reads as *absorbed*. Rules in `AnnexedStateService.cs`, `GameEngine.Territories.cs`.
- [ ] Tune everything in `Balance.cs`.
