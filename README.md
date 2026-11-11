# Warzone: Sunmeadow Valley

A browser RTS built with ASP.NET Core, SignalR, TypeScript and PixiJS. Play entirely on the world map: manage buildings from the bottom ribbon, capture oases for production, exchange goods through physical trade caravans and reinforce villages or oases. Combat, sieges, conquest, vassalage and match resolution remain server-authoritative. The interface supports Arabic (default, RTL) and English; translations live in `frontend/src/locales/ar.json`.

## Run

Requires .NET SDK 10 and Node.js 22.12+ (or 20.19+) with npm.

```powershell
npm --prefix frontend ci
npm --prefix frontend run build
dotnet run --project backend/Warzone.csproj --urls http://localhost:5080
```

Open **http://localhost:5080**. The host serves the built client and SignalR from the same origin. Rebuild the frontend after changes. `/health` reports phase 5. Stop your previous host before rebuilding its executable; for an independent development build, use `dotnet build --artifacts-path <temporary-directory>`.

Choose **Begin your adventure** to start with seven AI neighbors. **Play with friends** opens room creation and joining; ready each human player, then start the match. Use six bots for two humans. Separate browser profiles/private sessions create different guests; tabs in one profile share identity.

Gameplay fills the viewport without page scrolling. The world map is the only scene. Select your village to manage its nine buildings in the bottom ribbon, with visible levels and separate **+** upgrade buttons. **Buildings** returns to the main village; selecting another owned province changes the ribbon to that province. Navigation sits above the compact ribbon, which reserves space to its right for desktop panels without expanding when a panel opens. Desktop panels scroll beside the ribbon; mobile panels end above it. The resource bar is flush with the top of the screen. Live updates preserve existing controls and scroll position. Selecting an oasis replaces the ribbon with its wall, starting at level zero; use its **+** to build or upgrade. The oasis panel keeps all four stocks and village bonuses pinned together. Selecting a building opens its detailed panel, including troop recruitment, hospital, defenses and market. Resource counters open production details. Upgrades complete immediately when affordable; recruitment retains its training time.

Select a village, army or resource oasis on the world map to inspect it and issue orders. **Armies** manages existing expeditions or raises a mixed formation. Selecting an existing force opens editable counts for every unit type, initially selecting its full composition. Sending a reduced formation splits off that detachment while the remainder keeps its orders and a proportional share of food and loot. Keep a wagon at home for supply. Choose **Return home** or **Hold position** after an attack; this order persists on the server and can be changed for a deployed army. A victorious siege still waits for the city's fate decision before following that order. Every participant receives a unique server-assigned flag color shared by all viewers and retained by their villages, armies, caravans and oases.

Ongoing attacks, village/oasis battles and incoming reinforcements appear as persistent event cards, independently of the selected panel. Concurrent cards run from right to left in start order; mobile uses a compact horizontal row. Cards derive from authoritative live state and disappear when the event ends, while completed battles and reinforcement arrivals remain in Reports. The drawer uses the available desktop height with the building shelf beside it; smaller screens retain a thin themed scrollbar. Oasis capture, blockade and reinforcement actions use muted red, gold and green accents.

## Chat, alliances and resource shipments

A translucent realm chat sits at the lower-left edge, directly above the bottom controls. Players can exchange messages of up to 300 characters; the latest 100 messages are included in snapshots and survive reconnects. Selecting any village shows its commander's public pairwise alliances. Allied armies show their attack routes, allied reinforcements use green routes, and incoming attacks on allied villages or armies appear in red. Human players can send independent alliance requests to multiple other humans, cancel outgoing requests, and accept or decline incoming requests from the village or Kingdoms panel. Accepting an alliance posts a system message naming both commanders in the shared chat. Alliances are diplomatic relationships and do not by themselves disable combat.

Every village panel includes direct resource shipment controls. Choose another surviving source province and whole amounts of wood, iron, stone and wheat. Actual wagons carry the shipment, consume wheat provisions, deliver the cargo on arrival and then return home. The source is debited at departure; recipients receive nothing before arrival. Shipments can be intercepted after protection and wait outside blockaded villages, following the existing physical convoy rules.

## Production and campaign awareness

- Production starts at 240 per minute per resource, multiplied by realm speed, tribe, biome and `1 + accumulatedOasisBonus`. The displayed percentage compares net output to that base. Wheat subtracts home troops and reserved market wagons. A village blockade reduces gross wheat by 50%, without an additional hidden starvation drain. Guards in their own oases consume local wheat first; other deployed troops consume carried food. Purchases and healing costs are separate from production.

## Recruitment progression

Barracks, Stable and Siege Workshop upgrades add **2.5 percentage points of base training speed per level above level 1**. For example, level 5 provides +10% speed: an 8-second baseline recruit takes 8 / 1.10 seconds, before tribe and training-tier modifiers. Tribe time bonuses and the separate +5% training-tier speed bonus are applied once; Main Hall keeps its existing +8% per-level governor training bonus. Upgrading does not reschedule already-paid training orders.

| Facility | Level 1 | Level 2 | Level 3 | Level 5 | Level 8 |
| --- | --- | --- | --- | --- | --- |
| Barracks | Infantry | Archer | Spearman | Swordsman | Crossbowman |
| Stable | Scout, Wagon | — | Light Cavalry | Cavalry | Heavy Cavalry |
| Siege Workshop | Ram | — | Catapult | Siege Tower | — |

The server validates manual recruitment, repeat recruitment and training-tier upgrades against these requirements. Demolishing a facility below the requirement disables that unit's automatic queue. Locked units remain visible with their required level. `TrainingRules.cs` provides the requirements and speed formula to catalog and economy snapshots; deterministic progression checks cover all five tribes through level 20.

An owned oasis's **Transfer oasis allegiance** action sends an occupation mission from another village. The army's home village becomes the linked village after occupation; that village receives the production bonus and collection deliveries. The panel shows the current linked village and explains this, and only offers transfer when the player has multiple surviving villages.

## Biomes

Each new match generates four contiguous regions with shared irregular borders, using a central plain surrounded by woodland, highlands and desert. Seeded rotation, varied region centers and multiple scales of border curvature keep layouts different without dividing the world into rectangular quadrants. Villages receive their position's actual server-side production bonus: woodland +30% wood; highlands +25% iron and +20% stone; grassland +25% wheat; desert +15% wheat and stone. These multiply tribe and realm-speed bonuses. A biome badge and map selection show the bonuses; nomad relocation immediately recalculates them.

Terrain textures blend across biome edges, with muted forest clusters, connected mountain ridges, grass cover and low desert dunes. Use the map's biome-boundary button to see the exact production regions. Resource oases have exclusive ground rings and framed resource symbols; hovering highlights the ring and clicking the artwork, symbol or visible name opens the oasis. At wider zoom levels small labels and ground details recede while resource symbols remain visible. Decorative trees, rocks and ridges open terrain information, never occupation actions. Nearby settlements connect through a spanning road network with river crossings aligned to its bridges. Terrain blending is baked once per landscape rebuild rather than filtered every frame.

## Completed stages

| Stage | Implemented |
| --- | --- |
| 1. Foundation | Shared upgrade/queue interfaces, unit inheritance, composable passives, five tribes, 70 data-driven units, map entity hierarchy. |
| 2. Multiplayer simulation | Guest identity, 8-16 participant rooms, host settings, readiness, reconnect, private snapshots, authoritative fixed ticks, economy, immediate upgrades, training, auto queues, three bot personalities. |
| 3. World and logistics | Illustrated landscape, free movement, mixed formations, food, morale, oasis ownership, reinforcements, market and supply caravans, escorts, configurable routes and interceptions. |
| 4. Combat and siege | Automatic five-second combat rounds, multiple armies per engagement, composition counters, smithy bonuses, ordinary recall, wounded transit and healing, automatic fortification attacks and relief armies. |
| 5. Politics and resolution | Governor conquest and multiple provinces, gradual demolition, nomad relocation, central castle, scoreboards, elimination/spectating, winners/ties and frozen final results. |

## Compact structure

Feature-related types are grouped together. Generated builds and dependencies are ignored.

```text
backend/
  Warzone.csproj
  Program.cs       Hosting, guest authentication, same-origin checks, SVG assets
  Units.cs         Unit hierarchy, definitions, passive skills
  World.cs         Villages, armies, convoys, map entities
  GameCatalog.cs   Tribes, balance registry, 70 unit definitions and legacy SVG endpoints
  Economy.cs       Resources, immediate upgrades, training queues, hospitals
  Logistics.cs     Movement, supply, world snapshots, AI expeditions
  Combat.cs        Battles, directives, casualties, siege rounds and defenses
  Endgame.cs       Conquest, castle control and scoring
  Intelligence.cs  Production details, kingdom muster and private campaign reports
  Biomes.cs        Organic seeded regions and authoritative resource bonuses
  Oases.cs         Oasis ownership, production, collection, walls and reinforcement defense
  OasisSieges.cs   Grouped oasis blockades, live wall reports, relief and local guard food
  MultiArmyCombat.cs Joining armies, shared rounds, proportional casualties and recall
  UnitBalance.cs   Authoritative unit-counter multipliers, also exposed in the catalog
  Market.cs        Offers, reserved goods/wagons and delayed trade delivery
  Transfers.cs     Direct physical resource shipments between villages
  Social.cs        Shared chat, alliance requests and public alliances
  Rooms.cs         Lobby, match state, command receipts, economy AI
  GameServer.cs    Room registry, synchronization, lifecycle, fixed-step loop
  GameHub.cs       Authenticated SignalR API and rate filter
frontend/
  package.json, package-lock.json, tsconfig.json
  index.html       World map, building ribbon, HUD, contextual drawer and event journal
  src/main.ts      Connection, contextual building/army panels and live timers
  src/locale.ts    Language preference, RTL and localized interface/event text
  src/locales/ar.json Arabic translations
  src/units.ts     Shared atlas portraits, delayed stat tooltips and provision estimates
  src/art.ts       Cached illustrated building, terrain, oasis and ground atlas frames
  src/settlements.ts Three visual growth stages with tribe-specific cottages
  public/art/      Generated transparent atlases and exact generation prompts
  src/landscape.ts Biome scenery, mountains, forests, oases and river
  src/map.ts       Pixi illustration, moving entities, siege/battle/castle visuals
  src/oasis-panel.ts Oasis storage, transport, pinned bonuses and siege orders
  src/alerts.ts    Important event inbox and persistent read state
  src/market.ts    Trade offers and visible caravan journeys
  src/social.css   Translucent chat, shipment forms and alliance controls
  src/command-deck.css Building ribbon and responsive map controls
  src/intelligence.ts Production, troop breakdown and paginated campaign reports
  src/style.css    Forest glass HUD, gold accents and fixed-viewport layouts
  src/gameplay-v3.css Direct choice cards, building activity and sprite presentation
tests/
  Warzone.Tests.csproj
  Program.cs       Foundation, server, economy and logistics checks
  WarfareChecks.cs Combat, siege, politics and complete-match simulations
  OasisEconomyChecks.cs Physical collection, walls, return interception and alerts
  OasisSiegeChecks.cs Blockades, live reports, relief, suspended bonuses and local food
  MultiArmyChecks.cs Grouped field/city/oasis combat, late relief, recall and composition counters
  GameplayChecks.cs Instant upgrades, trade conservation, oases and reinforcements
  SocialChecks.cs  Resource shipments, chat replay, alliance requests and announcements
  signalr.mjs      Actual WebSocket integration with independent guests
README.md, .gitignore
```

## Match and economy

- Rooms contain 8-16 humans plus bots in total, last 30-60 minutes, and offer a 0/5/10 minute protection shield. Resource production is configurable from 0.5x to 5x. Changing settings resets readiness.
- Every human must be connected and ready to start. Foundation lasts up to minute ten; expansion follows; the final fifteen minutes take precedence in shorter matches.
- Each village starts with 1500 of each resource, 20 infantry, two wagons and nine buildings. Settlement artwork grows at main hall levels 1/6/12.
- Production per second is `4 * resourceSpeed * tribeMultiplier * biomeMultiplier * (1 + oasisBonus)`; wheat upkeep is deducted. Storage per resource is `2000 + warehouseLevel * 2000`. Paid trade deliveries remain available even if storage fills while caravans travel; normal production pauses above capacity.
- Buildings are MainHall, Warehouse, Barracks, Stable, SiegeWorkshop, Smithy, Hospital, Wall and Market. Upgrades pay once and complete immediately, up to level 20. There are no resource fields or construction queues.
- Manual training batches contain 1-100 units; automatic training repeats batches of five. Facility levels and training tiers reduce training time. Training uses separate facility lanes and a fair repeating queue. Main hall levels determine automatic training slots; Romans gain an extra slot. The population cap includes home, deployed, wounded and pending troops.
- Germans train faster/cheaper; Greeks have stronger walls and hospitals and fast cavalry; Mongols can relocate and have swift wagons; Persians produce 20% more resources. Each tribe has fourteen distinct unit types (70 total), including spearmen, swordsmen, crossbowmen, light cavalry and heavy cavalry. Training prices in `backend/units.json` emphasize wood/iron for Romans, stone/wheat for Germans, wood/stone for Greeks, wood/wheat for Mongols and iron/stone for Persians. Each role retains its overall cost; German infantry and Greek, Mongol and Persian scouts are deliberate light-unit exceptions. These are recruitment costs, separate from wheat upkeep.
- Bots use the same commands and resources as humans. Their priorities vary between economy, defense and aggression; they upgrade buildings, recruit, occupy oases, fight and compete for the castle.

## Unit composition

Hover, focus or tap the **?** on a unit portrait to see its attack/defense stats, health, speed, carrying capacity, wheat upkeep and actual matchup multipliers. Barracks train infantry, archers, spearmen, swordsmen and crossbowmen. Stables train cavalry, light/heavy cavalry, scouts and wagons. Mixed formations travel at their slowest unit's speed; omitting slow wagons allows a mounted force to use its cavalry speed.

| Unit | Main advantage | Main weakness |
| --- | --- | --- |
| Spearman | Strong attack/defense against mounted troops | Swordsmen and archers |
| Swordsman | Defeats spearmen and general infantry | Heavy cavalry |
| Archer | Defeats spearmen at low cost | Light and heavy cavalry |
| Crossbowman | Armor penetration against heavy cavalry | Light cavalry and archers; low health |
| Light cavalry | Fast raids against archers, crossbows and wagons | Spears and heavy cavalry |
| Heavy cavalry | Durable shock troops against infantry | Spears and crossbows; higher cost/upkeep |

Regular cavalry now moves at 18 world units/second for Romans/Germans, 22 for Greeks and 26 for Mongols; Persian elephants move at 12. Light cavalry moves at 23, with Persian 25, Greek 27 and Mongol 30 variants. Heavy cavalry moves at 15 (Mongol 20). Spearmen defeating equal-sized cavalry armies while retaining most troops is covered by deterministic combat tests. These are initial balance values for further multiplayer playtesting.

## Market and caravans

Each market level allows one open offer per village. Posting specifies one resource/quantity offered for another resource/quantity requested. The server reserves the actual goods and enough actual wagons, including carrying space for travel provisions. Reserved wagons cannot join armies or other offers and still consume food at home. Canceling returns both goods and wagons.

Another player accepts using an owned village. Both sides must have the required goods, wagons and round-trip wheat provisions, and neither village may be blockaded. Acceptance is atomic and launches two physical caravans. Goods arrive only when each caravan reaches its recipient; wagons become available after returning home. Caravans are visible and interceptible after protection ends. Delivery waits at a blockaded recipient; an invalid recipient causes a return with cargo. A lost village cancels its outstanding offers. Trade cannot create resources or reuse an accepted offer.

## Movement, supply and combat

The expanded world spans coordinates 30-1970, with its center at 1000:1000. Starting settlements lie on a radius of 760, up from 350, so neighboring villages are over twice as far apart. Travel and supply use these actual distances. Drag/touch to pan, scroll or use buttons to zoom, click to select; keyboard arrows and Home also work. Decorative rivers are traversable. There is no terrain pathfinding or fog of war.

Maps target 24 resource oases for eight villages and 16 for sixteen villages, preserving at least 300 world units from starting villages and 260 from another oasis. Projected clearances are at least 220 and 185 respectively; placement never relaxes those limits to add more sites. All four specialties appear. Decorative scenery stays outside oasis clearings; resource symbols and ownership rings distinguish selectable sites. Occupying an undefended oasis takes fifteen seconds after arrival and grants ownership to the army’s home village. Capture itself has no one-time resource payout. Each oasis adds +50% production for its specialty and +25% for each other resource. Bonuses add: two wood oases give +100% wood and +50% iron, stone and wheat to their owning village. Losing an oasis removes its bonus immediately.

Owned oases produce all four resources: 240/minute for their specialty and 120/minute for each other resource, multiplied by realm resource speed. Each resource has its own 15000 capacity. Automatic transport is enabled on capture and starts at 1000 total stored resources. The owning village must provide enough actual wagons to carry the whole current stock plus travel provisions; otherwise collection waits without debiting resources or troops. Wagons travel empty to the oasis, pick up goods, and return to deposit them. One collection caravan per oasis can be in transit, including the return leg. Owners can pause automatic transport or send a manual collection below the threshold. Goods are not credited before arrival, and collected cargo is retained if village storage fills in transit. A lost, reassigned or blockaded oasis turns outbound wagons home without collecting. Loaded wagons already returning can still deliver.

Owners can build and instantly upgrade oasis walls from their bottom building ribbon to level 20 using the linked village's resources. Each level adds 150 durability. Infantry damages walls slowly; rams, catapults and siege towers accelerate breaches. Occupation waits for the wall and defending forces to fall, then takes fifteen seconds. Wall upgrades are blocked while a hostile occupation army approaches. Hostile capture resets wall level and durability to zero. Walls and durability are public; stored resources and transport settings are private to the owner.

Choose **Besiege oasis** to blockade instead of capture. The blockade starts on arrival and suspends collection and all four village bonuses, while local resource production continues. A blockade holds without damaging the wall; switching to capture resumes wall damage. Ongoing wall attacks and blockades have clickable map markers that open the live report with durability, rounds, committed troops and losses. Withdrawal, defeat or capture ends the siege; surviving ownership bonuses resume immediately. Arriving reinforcements can fight the besieger to lift the blockade.

Up to sixteen armies can be deployed per player. A formation contains up to 10,000 total units, including wagons. Troops and initial provisions leave their source atomically. Movement uses the slowest unit, morale and retreat modifiers. Dispatch directly to an oasis to occupy it, or issue CaptureOasis to an existing army. Neutral occupation is available during protection; taking another player’s oasis requires protection to expire.

**Camp here** sends a new or split formation to a map position. On arrival it pitches tents and exposes a public 90-unit perimeter to every player. After protection, hostile armies and any kind of caravan inside that perimeter automatically join the camp's field engagement, including armies and caravans arriving after fighting has started. Allied players are excluded from this automatic aggression. The camp does not chase targets outside its perimeter and remains pitched after a victory; moving, redirecting or recalling the army removes it.

Each wagon adds 5000 carrying capacity; soldier backpacks also carry food and loot. Initial provisions always cover a conservative round trip plus 180 seconds of combat, targeting at least ten minutes when resources and capacity permit. The panel previews the loaded wheat and food duration. Unfunded journeys are rejected before troops or resources leave. Automatic convoys cannot start in the first minute and wait until remaining provisions approach the replenishment travel threshold. Guards reinforcing their own oasis consume its wheat first, falling back to carried food if stocks run out. They request no village supply convoys while stationed there; outstanding supply convoys return home. Other armies consume carried food, fatigue with movement, and lose four morale points per second without food. Zero morale routes them home. Automatic supply calculates cargo from upkeep and travel time; actual source wagons and escorts must be available. Only one convoy per army is active, including its return. Supply sources can be changed to any owned province. A besieged source cannot launch supply; returning supply caravans wait for its blockade to end.

Reinforcements can defend an owned village or oasis, including another player’s site. Select the target and send a new formation or a deployed army. Reinforcements remain controlled by their sender. Own-oasis guards eat local wheat; allied guards retain their sender's supply arrangements. At arrival they guard the site, engage hostile occupiers or approaching besiegers, and can relieve an active siege. After a victory they resume their defensive assignment when its ownership still matches. The sender can recall or redirect them; a change in supported ownership routes them home. Occupying armies may remain to guard their new oasis or return home according to their post-attack order.

All enemy convoy types—supply, trade and oasis collection—can be intercepted on both outward and return legs after protection ends. Being empty or returning does not grant immunity. Army contact and convoy interception create visible battles. Automatic rounds occur every five seconds, normally resolving after 30-120 seconds. Combat is automatic: unit matchups, enemy composition, morale, hit points and smithy levels determine outcomes. Morale has a nonlinear effect on strength. Allied armies nearby and arriving reinforcements join the same encounter. Casualties are distributed across the participating armies; using **Return home** during an active battle or siege withdraws that army with a 20% loss of its remaining force, without ending combat for the other armies. Ordinary recalls outside combat have no penalty. Stable map slots spread forces around their target instead of stacking their artwork. Stronger surviving escorts can defeat an interceptor. A captured convoy loses its wagons and cargo; its surviving escorts return home and the supplied army loses 25 morale.

Human losses use a deterministic repeating distribution of six wounded, three dead and one fled per ten casualties. Vehicles and siege engines are destroyed outright. Wounded travel to their home hospital after a minimum sixty-second delay plus distance, then recover gradually using iron and wheat. Hospital level and Greek bonuses accelerate care. The scoreboard reports cumulative wounded admissions; the village displays patients still awaiting recovery. Surviving troops, unused food and carried loot are credited once on return.

## Sieges and defeated cities

A besieging army forms a visible ring. Select its objective **before dispatch**; server validation applies equally to new and existing formations. Joining armies must share the commander's current objective. Combat rounds run every five seconds; the assault phase normally lasts 60–180 seconds.

| Objective | Requirement and result |
| --- | --- |
| Raid | Defeat the defense, take capacity-limited physical loot and follow the chosen return/hold order. Buildings and ownership remain. |
| Conquest | Include the existing Governor recruited in the Main Hall. After defeating the defense, each five-second persuasion round removes 5 loyalty per surviving governor (up to three). At zero loyalty, ownership transfers and one governor is consumed. |
| Raze | Include catapults; no Governor is required. After defeating the defense, approximately five minutes of rounds remove actual building levels, leaving the Main Hall last. The village disappears from the map only when every building reaches zero. Loss or withdrawal of the required force stops the operation. |
| Blockade | Surround the village without automatically assaulting its garrison. Incoming oasis and trade cargo waits outside; relief armies fight the besiegers before entering. Gross wheat production is halved until the blockade is lifted. |

Live and archived reports retain the objective, phase, persuasion/demolition progress, remaining building levels and per-army losses. There is no post-victory fate menu or instant raze command.

Conquest evacuates wounded to another surviving city. Deployed armies and supply lines based in a lost province rebase to a remaining province. Losing the final city eliminates a player, releases their vassals, removes remaining field forces and permits spectating. Failing to choose a fate releases the defeated city without silently choosing for the player.


Mongol relocation costs 300 wood and 300 wheat, has a 120-second cooldown, and requires spacing from other cities and the castle. Existing return/supply routes follow the moved village.

## Castle, scoring and results

Crown Keep appears at the map center during the last fifteen minutes. A supplied, unbroken army needs thirty seconds of uncontested presence to claim it. Rival occupants contest it and automatically engage. An occupied, uncontested keep earns its controller ten points per second; an empty keep earns none.

Score is `floor(castleSeconds * 10) + settlementLevels * 30 + sum(floor(cityResourceTotal / 200)) + enemyDead * 5 + floor(homeTroops / 10)`. Surviving kingdoms rank before eliminated kingdoms. The match ends at its timer or when only one kingdom retains a settlement. Highest scoring survivors win, and exact ties share victory. Results contain every participant and freeze with the rest of the simulation.

## Authority and networking

The server owns prices, identity, ownership, timers, movement, combat and victory. A monotonic 250 ms fixed step retains elapsed-time debt; a catch-up batch processes up to five simulated seconds. Full snapshots are sent once per second and the renderer interpolates positions. Clients cannot supply elapsed time or casualty results.

A single process lock serializes state operations; networking occurs outside it. Limits are 64 rooms, 1024 connections and four connections per guest. Signed, HTTP-only, SameSite=Strict guest cookies last eight hours and use Secure over HTTPS. Session creation is limited to 30 requests/minute/IP; hub invocations to 20/second/connection, with an 8 KB message limit. Cross-origin browser requests are rejected. Requests have stable IDs: exact replay returns the first result, changed payloads conflict, and structurally identical formation dictionaries replay correctly. Receipts are retained up to 4096 commands/player/match without eviction.

Positions, settlement politics, battle state and rankings are public. Enemy formation details, non-engaged army food/morale, supply cargo, economy and queues are private. Battle morale is public in engagement reports. Each guest receives its selected owned province and private road notices. Snapshot revisions prevent rollback from delayed delivery.

`Resume` restores a guest session after reconnect. Disconnecting keeps the seat and simulation running. Leaving is allowed outside an active match. Rooms with no connected humans for ten minutes are cleaned up. A simulation error faults its room and is logged.

### SignalR API

Endpoint: `/game`; enum strings are English and properties are camelCase.

| Method | Arguments |
| --- | --- |
| ListRooms / Resume | None |
| CreateRoom | playerName, tribe, roomName, settings |
| JoinRoom | roomId, playerName, tribe |
| ConfigureRoom | settings (host/lobby only) |
| SetReady | boolean |
| StartMatch / LeaveRoom | None |
| SendCommand | Command object; returns requestId, accepted, code, message |
| Snapshot event | Full private room snapshot |

Every command includes a unique `requestId`, `kind` and `targetId`.

| Commands | Additional fields / target |
| --- | --- |
| Upgrade, Train, SetAutoQueue | Owned building/training target; quantity for training, enabled for automatic troop training |
| SelectVillage | Owned village |
| DispatchArmy | Owned village; formation dictionary (or unitId + quantity), wagons, escorts, enabled, x/y; camp=true pitches at x/y on arrival; optional oasis destinationId and oasisMode (Capture/Blockade), enemy village destinationId and objective (Raid/Conquest/Raze/Blockade), or reinforcement=true with village/oasis destinationId, owned sourceVillageId, afterAttack |
| MoveArmy, CampArmy, RecallArmy, CaptureOasis, BesiegeOasis, Reinforce, InterceptConvoy | Owned army; x/y or destinationId as appropriate; optional formation selects a detachment and leaves the remainder in place; optional afterAttack for occupations/interceptions |
| ConfigureSupply | Owned army; sourceVillageId, wagons (0 = automatic), escorts, enabled |
| AttackArmy, BeginSiege | Owned army; enemy destinationId, optional formation detachment and afterAttack; BeginSiege also accepts objective (Raid/Conquest/Raze/Blockade). Required specialists must be in the selected formation. |
| SetAfterAttack | Owned army; afterAttack is Hold or Return |
| RelocateVillage | Owned nomad village; relocation x/y |
| SetOasisTransport | Owned oasis; enabled |
| CollectOasis, UpgradeOasisWall | Owned oasis |
| CaptureCastle | Owned army |
| PostMarketOffer | Owned village; offerResource, offerAmount, askResource, askAmount |
| AcceptMarketOffer | Owned buying village; destinationId = offer ID |
| CancelMarketOffer | targetId = owned offer ID |
| SendResources | Owned source village; destinationId = recipient village; shipment contains wood, iron, stone and wheat amounts |
| SendChat | Empty targetId; message contains 1-300 visible characters |
| ProposeAlliance | targetId = another human player |
| RespondAlliance | targetId = incoming request ID; enabled accepts or declines |
| CancelAllianceRequest | targetId = own outgoing request ID |

Room snapshots add shared chat, public alliances, viewer-specific alliance requests and a stable `flagColor` for every participant. World snapshots include randomized biome regions, armies, convoys, oasis ownership/specialties/defenders/walls, private four-resource stocks and transport state, public oasisSieges, market offers, notices, incoming threats, private muster/reports and warfare (settlements, battles, sieges, castle and rankings). Armies expose their afterAttack order, defensive assignment, public home tribe and public `campRadius` when encamped. Trade convoys expose kind and recipient; cargo is private to the two parties. Economy snapshots include ownedOases, additive oasisBonuses, reservedTradeWagons and a production array with grossPerMinute, upkeepPerMinute, netPerMinute, percent, blockaded and storageFull. Muster separates home/deployed/escort counts per unit and food demand. Reports include original participants, committed units (including zero-loss units), per-type casualty arrays, both sides' captured and lost resources, cargo/capacity snapshots, status and timeline. Every report includes an army list with owner, side, participation state and per-type losses. Oasis siege reports combine all besieging armies and defenders, carrying current wall level, durability, mode and blockade state; related field fighting and occupation use parentReportId so one site does not fill the report list with duplicate entries. Notices contain kind, optional targetId and endsAt, retaining the most recent 40 per owner. Important alerts are retained separately (40 per recipient) so routine events do not displace them. Threats are private to the threatened player and contain armyId, ownerId, targetId and estimated arrivesAt. Finished room snapshots add a result.

## Visuals

Sunmeadow Valley uses five generated transparent PNG atlases (`frontend/public/art/units-{romans,germans,greeks,mongols,persians}-v2.png`), each with fourteen units in a 4-by-4 grid; the last two cells are empty. `unit-art.ts` shares the same frame order between UI portraits and Pixi map sprites. Army formations display one figure per ten units of each type, rounding partial groups up: 100 infantry, 20 cavalry and 10 catapults produce ten infantry, two riders and one catapult. There is no 32-figure cap. Wagons and convoy escorts use the same ratio. Public `MapFigures` counts show every army's unit types at this visual granularity; exact enemy formations and provisions remain private. Casualties shrink the formation. Large armies use wider rows and more space around battles. Occupied oases fly their owner's village flag color, which stays stable through conquest and village removal. Persistent paired swords swing, clash with sparks and recoil between the centers of the active opposing groups, including multi-army battles; their click targets follow their movement and the effect disappears when battle ends. Reduced-motion preferences show static crossed swords. Selection and hit areas cover the enlarged party. Convoys use their tribe's wagon. There are no decorative soldier patrols around villages or fake troops around siege rings. Chimney smoke, marching motion and dust add movement. Active Raze sieges in the Demolishing phase add animated flames, embers and black smoke; effects persist between snapshots and disappear when demolition stops. Reduced-motion preferences disable ambient animation and keep a static fire/smoke indicator. Real building upgrades, unit unlocks, completed recruitment and oasis occupation trigger brief achievement feedback. Recruitment progress uses server start and completion times. Generation prompts are recorded in `frontend/public/art/units-v2-prompts.json`. Roman iron armor, lightly equipped Germanic raiders, Greek bronze hoplites, Mongol steppe clothing and Persian scales/robes define the tribes beyond palette changes. The previous v1 sheets are retained; generation used the built-in image_gen tool. The backend's legacy SVG URLs remain available for compatibility.

The map uses shared painted grass, rock and sand textures, irregular blended biome borders, grouped forests, ridges, dunes, roads and bridges. Oases use four illustrated specialties, exclusive clearings, resource badges and ownership rings. Tribal buildings use detailed atlas art in the ribbon and three settlement growth stages on the map. Building panels preserve recruitment, defenses, hospital and province controls. There is no separate village scene. The market has its own stall illustration. Ambient scenery respects reduced-motion preferences. Mobile keeps the map visible behind compact panels and a horizontally scrollable building ribbon. Unit portraits expose a delayed tooltip with combat, movement, carrying, food and passive details.

## Validation

```powershell
dotnet build backend/Warzone.csproj
dotnet run --project tests/Warzone.Tests.csproj
npm --prefix frontend run build
```

For actual WebSocket integration, start a development instance on port 5087, then run:

```powershell
npm --prefix frontend run test:integration
```

`WARZONE_TEST_URL` overrides the test server address. Integration creates isolated rooms and disconnects its guests afterward; normal idle cleanup removes the rooms.

The deterministic runner includes checks covering resources, immediate upgrades, training queues, replay, authorization, private snapshots, movement and supply conservation, combat, staged city objectives, blockade deliveries, castle control and frozen match results. New checks cover reservation/cancellation/acceptance of market offers, two delayed caravans, returned wagons, exact paid deliveries above storage capacity, additive oasis bonuses, hostile ownership transfer, protection, reinforcement control and defense before siege. Checks also cover oasis production, the automatic threshold, enough real wagons for a full load, delayed pickup/delivery, return-leg interception, wall payment/damage/destruction and private targeted alerts. Blockade checks cover deferred arrival effects, suspended/restored bonuses, blocked pickup, live wall reports, mode switching, relief casualties and local guard food. Placement checks cover reproducible oases, increased world/projected spacing and biome coverage. The runner also simulates full 30-minute matches with 8 and 16 participants.

Live SignalR checks cover multiple guests, privacy, replay, reconnect, immediate upgrades, training, movement, provisions, formation/biome snapshots, room isolation, rate limits, artwork, automatic combat, per-army reports and recall. Browser checks passed for the map-only scene, all nine building cards, level/upgrade actions, market reservation and two-caravan acceptance, oasis occupation, reinforcement orders, Arabic/English and mobile layouts. Oasis browser checks also exercise actual occupation, wall building, transport toggles and automatic caravan departure, persistent alert read state, and a centered compact ribbon on desktop and mobile.

The latest browser regression verifies initial army positions before the first animation frame for multiple villages, the level-zero oasis wall ribbon and upgrade button, four-resource stocks with pinned bonuses, the actual blockade order, opening the live report from its map marker, changing wall durability, restored bonuses after withdrawal and mobile report layout with no page errors.

## Operational limits

The multi-army browser checks exercise both besiegers in one live report, separate encirclement slots, reinforcement dispatch during an active blockade, persistent live controls, alert grouping/expiry, training tooltips with eight stat icons, and complete button access at 1440×1000, 1280×720, 390×844 and 390×640. Separate bounds checks confirm the report actions also fit horizontally. Deterministic checks include equal-sized composition matchups, late army joins, proportional losses, partial withdrawals and shared city/oasis sieges.

This is a playable local multiplayer prototype. Rooms and progress are in memory and are lost on server restart; there are no registered accounts, database, matchmaking service or deployment configuration. Guest identity depends on the browser cookie. Balance values are centralized and tested for consistency, but competitive balance still needs human multiplayer playtesting. Bots use deterministic heuristics. Graphics combine detailed generated building/terrain/oasis atlases, painted ground textures, scalable unit portraits and lightweight ambient effects. Production hosting, persistence, horizontal scaling and a larger art/audio pack are separate work.

Map presentation rules are verified with `npm --prefix frontend run test:visuals` (army growth, composition, casualty updates, privacy and demolition eligibility).
