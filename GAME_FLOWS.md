# EmpireSim — Complete Game Flow & Mechanics Guide

This guide explains **how every flow, feature, and system works** in EmpireSim at a clear, high-level conceptual level. It serves as the primary reference for understanding player actions, game systems, and how mechanics interact across the entire game.

---

## 1. Game Concept & Setting

EmpireSim is a 17th-century grand strategy simulation starting on **January 1, 1600**. Players take the throne of one of more than 40 historical empires (such as the **Ottoman Empire**, **Safavid Persia**, **France**, **England**, or the **Habsburg Monarchy**) and guide their civilization through domestic governance, economic development, military campaigns, diplomacy, and global affairs.

### The Core Gameplay Loop

```
           ┌──────────────────────────────────────────────┐
           │              MANAGE THE REALM                │
           │  • Set Taxes      • Construct Buildings     │
           │  • Craft Arms     • Appoint Advisors         │
           └──────────────────────┬───────────────────────┘
                                  │
                                  ▼
           ┌──────────────────────────────────────────────┐
           │            ADVANCE DAILY TIME                │
           │  • Collect Taxes  • Feed Population         │
           │  • Pay Wages      • Progress Construction   │
           └──────────────────────┬───────────────────────┘
                                  │
                                  ▼
           ┌──────────────────────────────────────────────┐
           │            INTERACT WITH THE WORLD           │
           │  • Trade Goods    • Sign Treaties           │
           │  • Send Spies     • Found Colonies          │
           │  • Vote in League • Wage War & Annex Lands  │
           └──────────────────────────────────────────────┘
```

---

## 2. Starting a Game & The Main Interface

### Choosing a Civilization
1. **Civilization Selection Screen**: At launch, the player chooses an empire from the civilization rail.
2. **Historical Starting Snapshot**: Each nation starts with accurate historical parameters:
   - **Capital City** (e.g., Constantinople for the Ottomans, Isfahan for Persia, Paris for France).
   - **State Religion** (Islam, Christianity, Buddhism, etc.).
   - **Treasury & Resources** (Gold, Wood, Stone, Iron, Copper, Lead).
   - **Standing Army & Fleet** (Infantry, Cavalry, Artillery, Warships).
   - **Pre-existing Embassies & Alliances** (e.g., the Ottoman Empire begins with historical embassies in Venice, France, and England, and a defensive alliance with the Crimean Khanate).

### The Top Resource Bar
The top bar is permanently visible on every screen and displays real-time imperial statistics:
- **Home Button (`🏠`)**: Returns to the main empire dashboard at any time.
- **Imperial Date**: Displays the current calendar day (starts `01-01-1600`).
- **Clock Speed Controls**:
  - `⏸` **Pause**: Halts the passage of time.
  - `▶` **Normal**: Advances at 1 day per second.
  - `⏩` **Fast**: Advances at 5 days per second.
  - `⏭` **Very Fast**: Advances at 20 days per second.
- **Treasury (Gold)**: Liquid currency used for construction, wages, troop training, and foreign gifts.
- **Crown Domains / Assembly Weight**: Imperial authority and voting power in the International Assembly.
- **Population**: Total living subjects residing across your imperial lands.

### The Main Dashboard
The dashboard features the nation's **central circular emblem** showcasing the selected country's **official flag** encased in a gold border. Around it, intuitive menu tiles grant immediate access to every branch of imperial administration.

---

## 3. Detailed Game Flows & Systems

---

### Flow 1: Command Staff & Imperial Cabinet
The ruler cannot govern alone. The player can appoint ministers to **6 specialized government posts** to boost imperial efficiency.

* **The 6 Cabinet Posts**:
  1. **Commander-in-Chief**: Boosts overall military morale and combat effectiveness.
  2. **Land Commander**: Reduces the gold and time required to recruit land armies.
  3. **Fleet Commander**: Reduces the cost and build time of warships.
  4. **Chief Tribute Collector**: Extracts higher tribute revenue from subjugated or intimidated foreign states.
  5. **Head of Trade**: Increases the profit earned when exporting domestic goods abroad.
  6. **Head of Construction**: Reduces the gold cost and duration of domestic building projects.

* **Advisor Tiers & Hiring**:
  - Each post can be staffed at **Tier 1 (Novice)**, **Tier 2 (Competent)**, or **Tier 3 (Master)**.
  - Higher tiers grant stronger bonuses but demand higher daily gold salaries.
* **Dismissal & Replacement**:
  - The player can dismiss any advisor at any time to cut daily salary costs or replace them with a higher-tier candidate.

---

### Flow 2: Domestic Economy, Production & Infrastructure
Wealth stems from imperial production. The economy converts land and labor into raw commodities and manufactured items.

* **Basic Buildings**:
  - **Farms**: Grow food crops (Wheat, Bread, Milk, Meat) to feed the population and avoid starvation.
  - **Mines**: Extract Iron, Stone, Copper, and Lead from the earth.
  - **Sawmills**: Fell timber to produce Wood for buildings and warships.
  - **Workshops**: Combine raw materials (Wood + Iron) into commercial goods.
* **Construction Queue**:
  - Ordering a new building expends gold and materials, placing the project into a timed queue.
  - Daily ticks advance the construction until the building finishes and enters active production.
* **Selling Surplus**:
  - If the empire has excess raw materials or manufactured goods in stock, the player can sell them directly on the open domestic market for instant treasury gold.

---

### Flow 3: Armory & Military Crafting
Elite military units require proper weapons and armor before they can be trained.

* **Crafting Military Gear**:
  - Using iron and metals, blacksmiths craft specific weapons and equipment:
    - **Helmets & Armor** (for defensive infantry).
    - **Daggers & Pikes** (for pikemen and vanguard troops).
    - **Shotguns & Arquebuses** (for musketeers and ranged units).
    - **Ship Parts** (for expanding the imperial war fleet).
* **Inventory Management**:
  - Crafted equipment is stored safely in national armories and does not spoil or get consumed by citizens, remaining ready until military recruitment calls for it.

---

### Flow 4: Military Recruitment, Reserves, Mercenaries & Upkeep
An empire's borders must be defended with infantry, cavalry, artillery, and naval fleets.

```
       ┌─────────────────────────── MILITARY SYSTEM ───────────────────────────┐
       │                                                                       │
       │   [Standing Army] ◄── [Reserve Mobilization] ◄── [Emergency Conscription]
       │          ▲                                                            │
       │          ├── [Mercenary Contracts] (Swiss Guard, Landsknecht, Cossacks) │
       │          └── [Allied Call to Arms] (Crimean Tartars, Friendly Nations)  │
       │                                                                       │
       │   Every 180 Days: Imperial Payday ──► Pay Gold (or soldiers desert)    │
       └───────────────────────────────────────────────────────────────────────┘
```

* **Recruiting Regular Troops**:
  - Players queue recruits (Infantry, Cavalry, Musketeers, Artillery). Recruitment consumes gold, manpower, and armory equipment.
* **Naval Fleets & Dockyard Construction**:
  - Building warships requires timber, ship parts, and gold. The appointed **Fleet Commander** reduces build costs and accelerates dockyard completion, while providing a naval battle combat multiplier (+15% per tier).
* **Naval Strategic Deployments (The 3 Sovereign Imperial Orders)**:
  - Rulers can deploy their available warships into 3 strategic fleet missions directly from the Military Naval screen:
    1. **Naval Blockade (Minimum 5 Warships)**: Dispatched against hostile states at war. Enforces an economic siege that drains enemy treasury gold daily (-1 gold/warship/day), cuts off their maritime trade contracts, and steadily accumulates peace leverage (+15 peace score/day) to force enemy capitulation. Blockaded rivals may sortie their own fleet in a desperate bid to break the blockade.
    2. **Trade Patrol / Convoy Escort (Minimum 3 Warships)**: Stationed along imperial maritime shipping lanes to protect domestic merchant convoys and secure the realm's sea lanes.
    3. **State Privateering & Letters of Marque (Minimum 3 Warships)**: Dispatched against rival nations during peace or war. Privateer squadrons raid foreign commercial shipping (25% daily chance), capturing up to 8 gold per warship directly into your imperial treasury while bleeding enemy coffers.
  - **Live Movement Tracking & Instant Recall**: All deployed naval squadrons are tracked live in Movement Reports. Deployed squadrons can be recalled back to port at any moment with a single royal decree.
* **High-Seas Naval Battles & Prize Warships**:
  - Clashes at sea factor fleet numbers, commander skill multipliers, and tactical variance. Victorious admirals sink enemy vessels and have a 25% chance to board and capture surviving enemy ships as **Prize Warships**, adding them directly into the imperial navy.
* **Reserves & Instant Mobilization**:
  - Citizens can be trained into military reserves during peacetime. When a surprise war erupts, reserves can be mobilized immediately into the standing army without waiting for lengthy training.
* **Emergency Conscription**:
  - In desperate times, the ruler can declare **Limited**, **National**, or **Total Conscription**. This instantly drafts thousands of emergency militia into the ranks, but lowers civilian happiness and workforce productivity.
* **Mercenary Contracts**:
  - When imperial manpower is low, rulers can hire famous mercenary companies (e.g. *Swiss Guard*, *Landsknechts*, *Cossacks*, *Artillery Companies*). Mercenaries join immediately for upfront gold plus ongoing maintenance.
* **Allied Assistance**:
  - Rulers can call upon defensive treaty partners (such as the Crimean Khanate for the Ottomans) to dispatch volunteer auxiliary armies during wartime.
* **Army Maintenance & Payday**:
  - Troops require daily upkeep. **Every 180 days**, the army holds a formal payday. If the treasury lacks funds, a **14-day grace period** begins. If wages remain unpaid, soldiers desert.

---

### Flow 5: Taxation & Population Classes
The treasury is funded through taxes levied upon 6 distinct segments of society.

* **The 6 Population Classes**:
  1. **Peasants**: Farmers and agricultural laborers.
  2. **Craftsmen**: Artisans, builders, and workshop laborers.
  3. **Soldiers**: Regular troops and military garrison families.
  4. **Merchants**: Traders, shopkeepers, and commercial shippers.
  5. **Spies**: Covert agents working within the empire.
  6. **Saboteurs**: Specialized covert operators.
* **Setting Tax Rates**:
  - Players adjust tax sliders independently for each class (from very low to extortionate).
* **Tax Approval & Happiness**:
  - High taxation generates massive daily revenue but lowers public approval.
  - Low taxation boosts approval and encourages population growth.
  - Excessive tax burden over long periods causes civil unrest, riots, and lower worker output.

---

### Flow 6: International Assembly (Global League)
The world's major empires belong to an international diplomatic forum to debate and vote on global policies.

* **Voting Power**:
  - Each nation has a voting weight based on its prestige, population, and military strength (the Ottoman Empire commands a heavyweight block of 60 votes).
* **Submitting Proposals**:
  - Any member can propose global resolutions, such as:
    - **Global War Ban**: Forbids member nations from declaring offensive wars for a period.
    - **Weapons Export Embargo**: Prohibits the international sale of military firearms and armor.
    - **Annexation Ban**: Prevents winners of wars from completely wiping out and annexing defeated states.
* **Voting Procedure**:
  - When the player submits a proposal, their own votes are registered **FOR** automatically.
  - Foreign nations vote based on their self-interest and relations with the proposer. If passed, the resolution becomes binding international law.

---

### Flow 7: Imperial Laws & Reforms
Imperial edicts establish long-term national bonuses and trade-offs.

* **Law Categories**:
  - Laws fall into distinct categories: *Economic*, *Military*, *Religious*, and *Administrative*.
  - Examples: *Peacetime Economy* (boosts farm/mine output, slows troop training), *Civilian Production* (increases consumer goods, lowers weapon production), *War Mobilization* (speeds recruitment, increases maintenance costs).
* **Enactment & Replacement**:
  - Enacting a new law in a category automatically and cleanly replaces any conflicting older law in that same category.
* **Repealing Laws**:
  - If national priorities shift, any active law can be repealed to return to baseline state policy.

---

### Flow 8: State Religion & Missionary Work
Faith guides societal cohesion and foreign influence.

* **State Religion**:
  - Each empire has an official religion (e.g., Islam for the Ottomans and Persia; Catholicism/Protestantism for European states).
* **Religious Conversion**:
  - A ruler can formally initiate a national faith conversion. This process is expensive, requires substantial gold, and takes time. The player can monitor or cancel the conversion if circumstances change.
* **Missionary Expeditions**:
  - Rulers can dispatch missionary delegations to foreign countries.
  - Over time, missionaries build religious influence within the target realm, warming diplomatic relations and softening foreign cultural resistance.

---

### Flow 9: Colonization & New World Expansion
Empires can fund expeditions to colonize unclaimed coastal regions across Africa, Asia, and the Americas.

```
   [Select Region] ──► [Quote Voyage] ──► [Dispatch Expedition]
         (Swahili Coast)       (Gold & Days)         (Track in Movements)
                                                            │
                                                            ▼
   [Colony Founded] ◄── [Arrive on Coast] ◄── [Journey at Sea/Overland]
         │
         ├── Generates Daily Tax Revenue
         └── Houses Growing Population
```

1. **Quoting a Region**: The player inspects available frontier territories (e.g., Swahili Coast, Horn of Africa, Aden Coast). The game calculates expedition costs and transit travel days based on distance.
2. **Dispatching Settlers**: Funding the mission dispatches settlers. The mission appears as an active voyage in the **Movement Report**.
3. **Founding the Colony**: When transit days conclude, settlers land and found the settlement.
4. **Colonial Administration**: Established colonies acquire a resident population, develop local extraction, and pay daily tax revenue into the imperial treasury.

---

### Flow 10: Diplomacy, Treaties & Alliances
Foreign policy manages relations with neighbors to secure borders or prepare for expansion.

* **Embassies (The Core Prerequisite)**:
  - Establishing an embassy in a foreign capital is required before two nations can trade or sign advanced treaties.
  - Historical starting embassies are pre-established on day one (e.g. Ottoman embassies in Venice and France).
  - New embassies can be proposed to any nation once relations are warm ($\ge 30$).
* **Non-Aggression Pacts (NAP)**:
  - Binds both nations to avoid attacking or aggressively extorting one another for a multi-year term.
* **Defensive Alliances**:
  - Pledges mutual military protection. If an ally is attacked, the player is summoned to assist, and vice versa.
* **Diplomatic Gifts**:
  - Sending gold gifts immediately warms a foreign ruler's diplomatic opinion of your empire.
* **Demanding Tribute**:
  - Strong empires can demand recurring tribute from weaker neighbors.
  - If the player's army significantly outmatches the neighbor ($> 1.5\times$), the neighbor complies and pays gold.
  - If the neighbor refuses, relations plummet and war may erupt.
  - *Safety Rule*: Non-aggression pacts protect nations from tribute extortion.

---

### Flow 11: Espionage & Covert Operations
When diplomacy fails, underground intelligence networks and imperial counter-intelligence protect the state and achieve strategic goals.

* **Offensive Foreign Infiltration**:
  - Spies are dispatched to establish an underground spy network in a target nation (e.g. Poland, Austria).
  - **Network Strength**: Over time, the network gathers intel, increasing operational strength (up to 100).
  - **Covert Missions**:
    - **Theft**: Steals a portion of the target country's gold treasury or material stockpiles.
    - **Sabotage**: Destroys enemy production buildings or ruins ongoing construction projects.
    - **Incite Revolt**: Stirs civil unrest and rebellion among the enemy's population, forcing them to fight domestic rebels instead of guarding their borders.

* **Homeland Defense & Palace Security (Counter-Intelligence)**:
  - Rulers can assign up to 5 **Imperial Guards** to domestic counter-intelligence directly from the Espionage screen.
  - **Guard Upkeep**: Each counter-espionage guard costs 1.0 gold/day in royal wages.
  - **Catch Chance**: Base interception chance is 10%, scaling by +16% per assigned guard (up to 90% catch chance with 5 guards).
  - **Interception of Hostile Agents**: Hostile or at-war rival empires will attempt covert theft, sabotage, or unrest in your lands. When intercepted:
    - The hostile attack is completely foiled, protecting imperial stockpiles and buildings.
    - The enemy operative is captured and cast into the **Imperial Dungeons**.
    - If counter-intelligence fails to catch the agent, foreign spies will inflict gold theft or structural sabotage.

* **Sovereign Decrees for Captured Spies**:
  As ruler, you are presented with three direct imperial choices for every foreign spy held in your dungeons:
  1. **Execute Traitor**:
     - The operative is executed on palace grounds.
     - **Imperial Reward**: +5 Ruler Rating, and establishes a 90-day deterrence period that significantly reduces enemy espionage attempts.
  2. **Ransom for Gold**:
     - The spy is returned to their home realm in exchange for a heavy royal ransom.
     - **Imperial Reward**: +1,000 Gold deposited immediately into the imperial treasury.
  3. **Parade Treason (Justified Casus Belli)**:
     - Publicly displays foreign treachery to the citizenry and foreign ambassadors.
     - **Imperial Reward**: Grants a **Justified Casus Belli** against the offending nation (providing +5 Tax Approval upon war declaration and bypassing diplomatic warmongering penalties) plus an immediate +5 Public Tax Approval bonus.

---

### Flow 12: International Trade & Commerce
No empire produces every single good. Trade balances resource deficits and exports surpluses.

```
   [Establish Embassy] ──► [Browse Partner Goods] ──► [Execute Purchase]
                                                            │
                                                            ▼
   [Delivered into Stores] ◄── [Merchant Route Transit] ◄── [Pay Gold Upfront]
```

* **Embassy Requirement**:
  - Trade can only take place with countries where your empire maintains an active embassy.
* **Buying Imports**:
  - The player can purchase food, minerals, or military equipment from trading partners. Gold is paid upfront, and a merchant contract enters transit.
* **Delivery Transit**:
  - Merchant ships or caravans travel for several days based on physical distance between the capitals. Upon arrival, the cargo is deposited directly into imperial stockpiles.
* **Selling Exports**:
  - Selling surplus commodities (e.g. Wood, Iron, Textiles) to friendly states generates instant gold revenue once delivered.

---

### Flow 13: Warfare, Marches, Battles & Annexation
War is the ultimate instrument of imperial expansion.

* **Declaring War**:
  - Wars can be declared against any rival (unless forbidden by an Assembly War Ban or an active Non-Aggression Pact).
* **Planning the Campaign & March Delay**:
  - The player selects the invading army size. Armies do not teleport; they march across the map, taking days to reach the front lines (tracked under Movement Reports).
* **Battle Resolution**:
  - Combat evaluates commander skill tiers, troop counts, equipment quality, and army morale. The victor inflicts casualties and captures the battlefield.
* **Peace Negotiations & Warship Demands**:
  - If a war stalls or the enemy is overwhelmed, either side can propose peace terms, white peace, or border truces.
  - **Naval Leverage**: Active naval blockades wear down enemy resolve, shifting the diplomatic balance in your favor.
  - **Surrender of Warships**: Victorious rulers can demand the surrender of enemy warships as an explicit peace term, transferring hostile ships directly into your naval dockyards upon signing.
* **Decisive Conquest, Fleet Absorption & Annexation**:
  - If an enemy's armies and defenses are completely crushed, the player can choose **Total Annexation**.
  - The defeated country ceases to exist independently. Its provinces, civilian population, treasury, and mines are permanently incorporated into the player's realm.
  - **Fleet Capture**: Rather than being scuttled or vanishing, all surviving enemy warships are captured and incorporated into the victor's imperial navy.
* **Administering Annexed Territories**:
  - Conquered lands continue operating their farms and mines, paying all tax revenues directly into your imperial treasury.

---

### Flow 14: Technology & Research
Scholars and military theorists develop cutting-edge 17th-century advancements.

* **The Tech Tree**:
  - Technologies span military tactics, agricultural techniques, and industrial tools (e.g. *Drill Manuals*, *Crop Rotation*, *Modern Fortifications*, *Central Banking*).
* **Active Research**:
  - Selecting a technology assigns daily intellectual progress toward its completion.
* **Permanent Perks**:
  - Once completed, technologies grant permanent imperial bonuses (e.g. $+10\%$ infantry combat power, $+15\%$ sawmill yield, or reduced corruption).

---

### Flow 15: Movements, Inbox & World News
The communication nerve center of the empire keeps the ruler informed.

* **Movement Reports**:
  - Live tracking of all ongoing journeys: marching armies, merchant trade convoys, colonist fleets, and diplomatic envoys. Shows origins, destinations, and remaining travel days.
* **The Imperial Inbox**:
  - Urgent reports are delivered to a categorized inbox (*Military*, *Diplomacy*, *Trade*, *Production*).
  - Unread notification badges highlight critical events (enemy declarations of war, resource shortages, completed ships, alliance calls).
* **World News (International Chronicle)**:
  - Major events taking place exclusively between foreign powers (e.g., Spain declaring war on France, or Russia signing a treaty with Poland) are routed into a separate World News ledger so the player can monitor global power balances without cluttering their personal inbox.

---

### Flow 16: Saving, Loading & Session Persistence
The entire state of the world persists cleanly between sessions.

* **Saving (`SaveAsync`)**:
  - Captures complete game state: exact gold treasury, stockpiles, troop regiments, calendar date, active construction projects, cabinet advisors, diplomatic treaties, ongoing merchant contracts, colonies, and annexed provinces.
* **Loading (`LoadAsync`)**:
  - Restores all data structures completely with 100% round-trip fidelity, allowing players to resume their empire right where they left off.

---

## 4. End-to-End Player Journey: A Practical Example

To see how these flows connect during actual gameplay, here is a typical campaign walkthrough as the **Ottoman Empire**:

```
[Day 1: Jan 1600]
  1. Review realm in Constantinople: 8,000 standing troops, healthy treasury, embassies in France & Venice.
  2. Appoint cabinet: Land Commander (cheaper recruitment) & Head of Construction (faster building).
  3. Inspect domestic economy: Order 2 new Sawmills and 2 Farms to boost timber and food output.

[Month 1 - 3: Spring 1600]
  4. Balance the budget: Adjust peasant and merchant tax sliders to secure positive daily cash flow.
  5. Craft weapons: Direct blacksmiths to forge 100 Helmets and Pikes in the Armory.
  6. Diplomacy: Send a gold gift to France to cement friendship; propose an embassy to Persia.

[Month 4 - 6: Summer 1600]
  7. Trade: Open trade with France; import 100 Wheat to bolster stores; export surplus timber to Venice.
  8. Research: Order scholars to begin researching "Drill Manuals" for military discipline.
  9. International Assembly: Vote against an arms embargo to keep imperial trade open.

[Month 7 - 12: Autumn & Winter 1600]
  10. Payday: Accrued military wages are paid successfully on the 180-day payday; soldiers remain loyal.
  11. Colonization: Fund an expedition to the Swahili Coast; colonists arrive after 82 days and found a colony.
  12. Espionage: Infiltrate a rival nation (Austria) to build spy network strength.

[Year 2: 1601]
  13. Crisis & War: Austria mobilizes near the border. Declare war, mobilize reserve forces, and recruit Janissaries.
  14. Campaign: March imperial army to Vienna. Win decisive battle.
  15. Conquest: Annex Habsburg lands. Incorporate their mines, farms, and population into the Ottoman realm.
  16. Save Game: Save imperial progress to disk and continue the campaign anytime.
```

---

*Document created for EmpireSim. Keep this file updated as new gameplay mechanics or expansions are introduced.*
