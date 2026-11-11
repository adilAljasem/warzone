using Microsoft.Extensions.Logging.Abstractions;
using Warzone;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
void Reject(Action action, string code)
{
    try { action(); }
    catch (GameRuleException error) when (error.Code == code) { checks++; return; }
    throw new Exception($"Expected rule rejection: {code}");
}
GameRoom NewRoom(TribeType tribe = TribeType.Romans, int minutes = 30, int bots = 7)
{
    var id = Guid.NewGuid();
    return new(id, "Host", tribe, "Test", new(DurationMinutes: minutes, BotCount: bots), DateTimeOffset.UtcNow);
}
GameRoom Running(TribeType tribe = TribeType.Romans, bool passive = false)
{
    var result = NewRoom(tribe, bots: passive ? 0 : 7);
    result.Configure(result.HostId, new(DurationMinutes: 30, ShieldMinutes: 10, BotCount: passive ? 0 : 7));
    if (passive) for (var i = 0; i < 7; i++) { var id = Guid.NewGuid(); result.Join(id, "Guest", TribeType.Romans); result.SetReady(id, true); }
    result.SetReady(result.HostId, true);
    result.Start(result.HostId, _ => true);
    return result;
}
EconomySnapshot Economy(GameRoom room) => room.Snapshot(room.HostId, _ => true).Economy!;
CommandResult Command(GameRoom room, CommandKind kind, Guid target, int quantity = 1, bool enabled = true)
    => room.Execute(room.HostId, new(Guid.NewGuid(), kind, target, quantity, enabled));
void Advance(GameRoom room, int seconds) { for (var i = 0; i < seconds * 4; i++) room.Advance(.25); }

GameCatalog.Validate();
Reject(() => new RoomSettings(Capacity: 7).Validate(), "settings");
Reject(() => new RoomSettings(DurationMinutes: 61).Validate(), "settings");
Reject(() => new RoomSettings(ShieldMinutes: 3).Validate(), "settings");
Reject(() => new RoomSettings(ResourceSpeed: double.NaN).Validate(), "settings");
Reject(() => new RoomSettings(BotCount: 8).Validate(), "settings");
var lobby = NewRoom();
Reject(() => lobby.Start(Guid.NewGuid(), _ => true), "host_only");
Reject(() => lobby.Start(lobby.HostId, _ => true), "not_ready");
Reject(() => lobby.Join(Guid.NewGuid(), "Extra", TribeType.Greeks), "full");
lobby.SetReady(lobby.HostId, true);
lobby.Configure(lobby.HostId, new(BotCount: 6));
Check(!lobby.Snapshot(lobby.HostId, _ => true).Players[0].Ready, "Settings must reset readiness");
lobby.SetReady(lobby.HostId, true);
Reject(() => lobby.Start(lobby.HostId, _ => true), "minimum_players");
var guest = Guid.NewGuid();
lobby.Join(guest, "Guest", TribeType.Greeks);
lobby.SetReady(guest, true);
Reject(() => lobby.Start(lobby.HostId, id => id != guest), "not_ready");
lobby.Start(lobby.HostId, _ => true);
Check(lobby.Snapshot(guest, _ => true).Players.Length == 8, "Eight participants");
Reject(() => lobby.Configure(lobby.HostId, new()), "lobby_only");
Reject(() => lobby.Join(Guid.NewGuid(), "Late", TribeType.Greeks), "lobby_only");
Reject(() => lobby.Leave(guest), "running");
var target = Economy(lobby).Targets.First(x => x.Kind == "Building");
var foreign = lobby.Execute(guest, new(Guid.NewGuid(), CommandKind.Upgrade, target.Id));
Check(!foreign.Accepted && foreign.Code == "not_owned", "Reject another player's target");
Check(Economy(lobby).Jobs.Length == 0 && Economy(lobby).Resources.Wood == 1500, "Rejected command has no effects");
var request = new GameCommand(Guid.NewGuid(), CommandKind.Upgrade, target.Id);
Check(lobby.Execute(lobby.HostId, request).Accepted, "Accept owned upgrade");
var paid = Economy(lobby).Resources;
Check(paid.Wood == 1500 - target.UpgradeCost!.Value.Wood, "Charge exact upgrade cost");
Check(lobby.Execute(lobby.HostId, request).Accepted && Economy(lobby).Resources == paid && Economy(lobby).Jobs.Length == 0 && Economy(lobby).Targets.Single(t=>t.Id==target.Id).Level==2, "Duplicate is idempotent");
Check(lobby.Execute(lobby.HostId, request with { Kind = CommandKind.Train }).Code == "request_conflict", "Request ID cannot change payload");
Check(Command(lobby, CommandKind.Upgrade, Economy(lobby).Targets.Last(x=>x.Kind=="Building").Id).Accepted, "Different buildings upgrade immediately without a construction lane");
Check(Economy(lobby).Targets.Single(x=>x.Id==target.Id).Level==2&&Economy(lobby).Jobs.Length==0,"Upgrade is visible before the next tick");

var trainingRoom = Running(TribeType.Germans);
var training = Economy(trainingRoom).Targets.First(x => x.Kind == "Training");
Check(training.TrainingCost!.Value.Wood == 20 && Math.Abs(training.TrainingSeconds!.Value - 3.2) < .001, "German training bonuses applied once");
Check(Command(trainingRoom, CommandKind.Train, training.Id, 0).Code == "quantity", "Reject zero batch");
Check(Command(trainingRoom, CommandKind.Train, training.Id, 101).Code == "quantity", "Reject oversized batch");
Check(Command(trainingRoom, CommandKind.Train, training.Id, 100).Code == "resources", "Insufficient funds rejected");
Check(Command(trainingRoom, CommandKind.Train, training.Id, 2).Accepted, "Start batch");
Check(Economy(trainingRoom).Garrison.Values.Sum() == 22, "No instant troops");
var archer = Economy(trainingRoom).Targets.Where(x => x.Kind == "Training").Skip(1).First();
Check(Command(trainingRoom, CommandKind.Upgrade, Economy(trainingRoom).Targets.Single(t=>t.Name=="Barracks").Id).Code == "main_hall", "Buildings cannot exceed Main Hall level");
Check(Command(trainingRoom, CommandKind.Upgrade, Economy(trainingRoom).Targets.Single(t=>t.Name=="MainHall").Id).Accepted, "Upgrade Main Hall to level two first");
Check(Command(trainingRoom, CommandKind.Upgrade, Economy(trainingRoom).Targets.Single(t=>t.Name=="Barracks").Id).Accepted, "Unlock archers at barracks level two");
Check(Command(trainingRoom, CommandKind.Train, archer.Id).Accepted, "Shared barracks lane queues additional batch in waiting queue");
Check(Economy(trainingRoom).Jobs.Length == 2 && Economy(trainingRoom).Jobs[1].StartedAt >= Economy(trainingRoom).Jobs[0].CompletesAt, "Queued batch starts after active batch finishes");
Check(Economy(trainingRoom).Targets.Single(x => x.UnitId == "germans-scout").TrainingBuilding == "Barracks", "Foot scouts train in the Barracks");
var wagon = Economy(trainingRoom).Targets.Single(x => x.UnitId == "germans-wagon");
Check(Command(trainingRoom, CommandKind.Train, wagon.Id).Accepted, "Independent stable lane");
Advance(trainingRoom, 13);
Check(Economy(trainingRoom).Garrison.Values.Sum() == 26, "Training completion adds all queued batches sequentially");

var autoRoom = Running();
for(var level=2;level<=5;level++) Check(Command(autoRoom, CommandKind.Upgrade, Economy(autoRoom).Targets.Single(t=>t.Name=="MainHall").Id).Accepted, "Raise Main Hall cap for auto lane checks");
Check(Command(autoRoom, CommandKind.Upgrade, Economy(autoRoom).Targets.Single(t=>t.Name=="Barracks").Id).Accepted, "Unlock archers for auto lane checks");
Advance(autoRoom, 320);
for(var level=2;level<=5;level++) Check(Command(autoRoom, CommandKind.Upgrade, Economy(autoRoom).Targets.Single(t=>t.Name=="Stable").Id).Accepted, "Unlock cavalry for auto lane checks");
Advance(autoRoom, 210);
var autoTargets = Economy(autoRoom).Targets.Where(t=>t.Kind=="Training").Take(5).ToArray();
foreach (var item in autoTargets.Take(4)) Check(Command(autoRoom, CommandKind.SetAutoQueue, item.Id).Accepted, "Roman auto slot");
Check(Command(autoRoom, CommandKind.SetAutoQueue, autoTargets[4].Id).Code == "auto_slots", "Enforce auto limit");
Check(Economy(autoRoom).Jobs.Length == 0, "Auto does not bypass simulation clock");
autoRoom.Advance(.25);
Check(Economy(autoRoom).Jobs.Length == 2, "Automatic training respects the barracks and stable lanes");
var active = Economy(autoRoom).Jobs[0].TargetId;
foreach (var item in autoTargets.Take(4)) Command(autoRoom, CommandKind.SetAutoQueue, item.Id, enabled: false);
Advance(autoRoom, 120);
Check(Economy(autoRoom).Garrison.Values.Sum() > 22 && Economy(autoRoom).Jobs.Length == 0, "Disabling auto preserves paid work but stops repeats");
var autoTraining = Economy(autoRoom).Targets.First(x => x.Kind == "Training");
Command(autoRoom, CommandKind.SetAutoQueue, autoTraining.Id);
Advance(autoRoom, 90);
Check(Economy(autoRoom).Garrison.Values.Sum() >= 30, "Auto training repeats batches");

var normal = Running(TribeType.Greeks);
var persian = Running(TribeType.Persians);
Advance(normal, 1); Advance(persian, 1);
Check(Economy(normal).Resources.Wood == (long)(1500 + 4 * Biomes.Multiplier(Economy(normal).Biome, ResourceType.Wood)) && Economy(persian).Resources.Wood == (long)(1500 + 4.8 * Biomes.Multiplier(Economy(persian).Biome, ResourceType.Wood)), "Fractional production rounds only at snapshot");
Advance(normal, 4); Advance(persian, 4);
Check(Economy(normal).Resources.Wood == (long)(1500 + 20 * Biomes.Multiplier(Economy(normal).Biome, ResourceType.Wood)) && Economy(persian).Resources.Wood == (long)(1500 + 24 * Biomes.Multiplier(Economy(persian).Biome, ResourceType.Wood)), "Persian production bonus preserves fractional accrual");
Check(Economy(normal).AutoSlots == 3, "Non-Roman base slots");
Advance(normal, 596);
Check(normal.Snapshot(normal.HostId, _ => true).ShieldRemainingSeconds == 0, "Shield expires");
Check(normal.Snapshot(normal.HostId, _ => true).Phase == "Expansion", "Expansion starts at ten minutes");
Advance(normal, 299);
Check(normal.Snapshot(normal.HostId, _ => true).Phase == "Finale", "Final phase starts with fifteen minutes left");
Advance(normal, 900);
Check(normal.Status == RoomStatus.Finished && normal.ElapsedSeconds == 1800, "Match ends at configured duration");
var ended = Economy(normal).Resources;
Check(Command(normal, CommandKind.Upgrade, Economy(normal).Targets[0].Id).Code == "not_running", "No spending after finish");
Advance(normal, 10);
Check(Economy(normal).Resources == ended && normal.ElapsedSeconds == 1800, "Finished simulation freezes");
Check(ended.Wood <= Economy(normal).Capacity, "Storage cap enforced");
var bot = normal.Snapshot(normal.HostId, _ => true).Players.First(x => x.IsBot);
var botEconomy = normal.Snapshot(bot.Id, _ => true).Economy!;
Check(botEconomy.Targets.Count(x => x.Kind == "Building" && x.Level > 1) >= 4, "Bots develop through command interface across multiple buildings");
Check(botEconomy.Targets.Any(x => x.Kind == "Training" && x.RequiredBuildingLevel > 1 && x.Unlocked), "Bots upgrade military facilities to unlock higher-tier units");
Check(!botEconomy.Targets.Any(x => x.Auto && (x.UnitId!.EndsWith("-wagon") || x.UnitId.EndsWith("-scout"))), "Bots reserve auto-queue slots for combat units");

var manualTime = new ManualTime();
var server = new GameServer(manualTime, NullLogger<GameServer>.Instance);
var user = Guid.NewGuid();
server.Connect("first", user);
var roomView = server.Create(user, "Host", TribeType.Romans, "Server", new());
server.Ready(user, true);
server.Start(user);
var serverTarget = server.Resume(user)!.Economy!.Targets[0];
var concurrentRequest = new GameCommand(Guid.NewGuid(), CommandKind.Upgrade, serverTarget.Id);
Parallel.For(0, 20, _ => server.Execute(user, concurrentRequest));
Check(server.Resume(user)!.Economy!.Targets.Single(t=>t.Id==serverTarget.Id).Level == 2, "Concurrent duplicate commands apply one upgrade");
Check(server.Resume(user)!.Economy!.Resources.Wood == 1500 - serverTarget.UpgradeCost!.Value.Wood, "Concurrent debit is atomic");
server.Disconnect("first");
server.Tick(.25);
Check(!server.Resume(user)!.Players.First(x => x.Id == user).Online, "Disconnect updates presence");
server.Connect("second", user);
Check(server.Resume(user)!.Id == roomView.Id && server.Resume(user)!.Economy!.Targets.Single(t => t.Id == serverTarget.Id).Level == 2, "Reconnect restores existing state");
Check(server.Resume(Guid.NewGuid()) is null, "Unknown identity cannot resume another player");
var otherUser = Guid.NewGuid();
server.Connect("other", otherUser);
var otherRoom = server.Create(otherUser, "Other", TribeType.Greeks, "Other", new());
Check(otherRoom.Id != roomView.Id && server.Deliveries().Length == 2, "Rooms are isolated");
Check(server.Deliveries().All(x => x.Connections.Length == 1 && x.Snapshot.You == (x.Connections[0] == "second" ? user : otherUser)), "Private snapshots route to their owner");
server.Disconnect("second");
manualTime.Advance(TimeSpan.FromMinutes(11));
server.Tick(.25);
Check(server.Resume(user) is null && server.Resume(otherUser) is not null, "Cleanup expires fully disconnected rooms only");
server.Ready(otherUser, true);
server.Start(otherUser);
Check(server.Resume(otherUser)!.Status == RoomStatus.Running, "Other room is running");
server.Leave(otherUser);
Check(server.Resume(otherUser) is null, "Leaving a running match via GameServer clears active membership so player returns to main menu");
var freshRoom = server.Create(otherUser, "Other", TribeType.Greeks, "Fresh", new());
Check(freshRoom.Id != otherRoom.Id && server.Resume(otherUser)!.Id == freshRoom.Id, "Player can immediately start a new match after leaving");
for (var i = 0; i < 20; i++) server.CheckRate("other");
Reject(() => server.CheckRate("other"), "rate_limit");
manualTime.Advance(TimeSpan.FromSeconds(1));
server.CheckRate("other");
checks++;
var transfer = NewRoom(bots: 6);
var successor = Guid.NewGuid();
transfer.Join(successor, "Successor", TribeType.Mongols);
transfer.Leave(transfer.HostId);
Check(transfer.HostId == successor, "Host transfers on explicit lobby departure");
var expeditionRoom = Running(passive: true);
var homeId = Economy(expeditionRoom).VillageId;
var expeditionHost = expeditionRoom.HostId;
WorldSnapshot World(GameRoom room) => room.Snapshot(room.HostId, _ => true).World!;
ArmySnapshot OwnArmy(GameRoom room) => World(room).Armies.First(x => x.OwnerId == room.HostId);
var dispatch = new GameCommand(Guid.NewGuid(), CommandKind.DispatchArmy, homeId, 10, true, 2450, 1500, "romans-infantry", 1, 2);
var initialFood = Economy(expeditionRoom).Resources.Wheat;
Check(expeditionRoom.Execute(expeditionHost, dispatch with { RequestId = Guid.NewGuid(), X = LogisticsWorld.MapMaximum + 1 }).Code == "bounds", "Reject outside-map destination");
Check(expeditionRoom.Execute(expeditionHost, dispatch with { RequestId = Guid.NewGuid(), UnitId = "greeks-infantry" }).Code == "unit", "Reject foreign tribe unit");
Check(Economy(expeditionRoom).Resources.Wheat == initialFood, "Rejected expedition does not charge food");
Check(expeditionRoom.Execute(expeditionHost, dispatch).Accepted, "Dispatch with troops and loaded wagon");
Check(Economy(expeditionRoom).Garrison.Values.Sum() == 11, "Dispatched troops leave the garrison");
var armyId = OwnArmy(expeditionRoom).Id;
Check(expeditionRoom.Execute(expeditionHost, dispatch).Accepted && World(expeditionRoom).Armies.Length == 1, "Dispatch replay cannot clone armies");
var starting = OwnArmy(expeditionRoom).Position;
var loadedFood = OwnArmy(expeditionRoom).Wheat;
expeditionRoom.Advance(.25);
var next = OwnArmy(expeditionRoom).Position;
Check(LogisticsWorld.Distance(starting, next) > 0 && LogisticsWorld.Distance(starting, next) <= 2.501, "Server movement obeys unit speed");
Check(OwnArmy(expeditionRoom).Wheat < loadedFood, "Marching consumes loaded wheat");
Check(expeditionRoom.Execute(expeditionHost, new(Guid.NewGuid(), CommandKind.ConfigureSupply, armyId, SourceVillageId: Guid.NewGuid())).Code == "not_owned", "Reject foreign supply village");
var enemyId = expeditionRoom.Snapshot(expeditionHost, _ => true).Players.First(x => x.Id != expeditionHost).Id;
Check(expeditionRoom.Execute(enemyId, new(Guid.NewGuid(), CommandKind.MoveArmy, armyId, X: 500, Y: 500)).Code == "not_owned", "Reject enemy army orders");
Advance(expeditionRoom, 20);
Check(!World(expeditionRoom).Convoys.Any(x => x.OwnerId == expeditionHost), "A provisioned army does not trigger an immediate convoy");
Check(Economy(expeditionRoom).Garrison.Values.Sum() + World(expeditionRoom).Armies.Where(x => x.OwnerId == expeditionHost).Sum(x => x.Soldiers + x.Wagons)
    + World(expeditionRoom).Convoys.Where(x => x.OwnerId == expeditionHost).Sum(x => x.Escorts + x.Wagons) == 22, "Army and escort reservations conserve troops");
Check(expeditionRoom.Snapshot(enemyId, _ => true).World!.Armies.First(x => x.Id == armyId).Wheat is null, "Enemy supply quantities stay private");
Advance(expeditionRoom, 490);
Check(World(expeditionRoom).Notices.Any(x => x.Text.StartsWith("Supply delivered")), "Convoy reaches moving army and delivers food");
Check(expeditionRoom.Execute(expeditionHost, new(Guid.NewGuid(), CommandKind.RecallArmy, armyId)).Accepted, "Recall army");
Advance(expeditionRoom, 70);
Check(!World(expeditionRoom).Armies.Any(x => x.OwnerId == expeditionHost) && !World(expeditionRoom).Convoys.Any(x => x.OwnerId == expeditionHost), "Army and convoy complete their return");
Check(Economy(expeditionRoom).Garrison.Values.Sum() == 22, "All troops and wagons restored exactly once");

var raidRoom = Running(passive: true);
var raidHome = raidRoom.Snapshot(raidRoom.HostId, _ => true).Players.Single(p => p.Id == raidRoom.HostId).Position!;
var oasis = World(raidRoom).Oases.OrderBy(c => LogisticsWorld.Distance(raidHome.Value, c.Position)).First();
var raidCommand = dispatch with { RequestId = Guid.NewGuid(), TargetId = Economy(raidRoom).VillageId, Enabled = false, DestinationId = oasis.Id, X = oasis.Position.X, Y = oasis.Position.Y };
Check(raidRoom.Execute(raidRoom.HostId, raidCommand).Accepted, "Launch neutral raid while player protection is active");
for (var step = 0; step < 4000 && OwnArmy(raidRoom).Order != "Guarding"; step++) raidRoom.Advance(.25);
Check(OwnArmy(raidRoom).Loot!.Value.Total == 0 && World(raidRoom).Oases.First(x => x.Id == oasis.Id).OwnerId == raidRoom.HostId, "Occupation transfers oasis ownership without gathering cargo");
Check(OwnArmy(raidRoom).Order == "Guarding", "Occupying army guards its captured oasis");
Check(raidRoom.Execute(raidRoom.HostId, new(Guid.NewGuid(), CommandKind.InterceptConvoy, OwnArmy(raidRoom).Id, DestinationId: Guid.NewGuid())).Code == "protected", "Shield rejects hostile convoy orders");

var hungryRoom = Running(passive: true);
Check(hungryRoom.Execute(hungryRoom.HostId, dispatch with { RequestId = Guid.NewGuid(), TargetId = Economy(hungryRoom).VillageId, Enabled = false, X = 50, Y = 50 }).Accepted, "Launch without automatic supply");
var hungerWait = OwnArmy(hungryRoom).Wheat!.Value / (11d / 60) + 30;
Advance(hungryRoom, (int)Math.Ceiling(hungerWait));
Check(OwnArmy(hungryRoom).Morale == 0 && OwnArmy(hungryRoom).Order == "Returning", "Starvation collapses morale and forces retreat");
Check(hungryRoom.Execute(hungryRoom.HostId, new(Guid.NewGuid(), CommandKind.MoveArmy, OwnArmy(hungryRoom).Id, X: 500, Y: 500)).Code == "routing", "Cannot cancel a routed army's retreat");
Advance(hungryRoom, 450);
Check(!World(hungryRoom).Armies.Any(x => x.OwnerId == hungryRoom.HostId) && Economy(hungryRoom).Garrison.Values.Sum() == 22, "Routed troops return safely without duplication");
void CheckInterception(int escorts, bool wins)
{
    var room = NewRoom(bots: 0);
    room.Configure(room.HostId, new(DurationMinutes: 30, ShieldMinutes: 0, BotCount: 0));
    var raider = Guid.NewGuid();
    room.Join(raider, "Raider", TribeType.Romans);
    for (var i = 0; i < 6; i++) { var id = Guid.NewGuid(); room.Join(id, "Guest", TribeType.Romans); room.SetReady(id, true); }
    room.SetReady(room.HostId, true); room.SetReady(raider, true); room.Start(room.HostId, _ => true);
    var raiderHome = room.Snapshot(raider, _ => true).Economy!.VillageId;
    Check(room.Execute(raider, dispatch with { RequestId = Guid.NewGuid(), TargetId = raiderHome, Enabled = true, Escorts = 0, X = 2580, Y = 1500 }).Accepted, "Position an interception force");
    Advance(room, 130);
    var raiderArmy = room.Snapshot(raider, _ => true).World!.Armies.Single(x => x.OwnerId == raider);
    Check(room.Execute(room.HostId, dispatch with { RequestId = Guid.NewGuid(), TargetId = Economy(room).VillageId, Quantity = 8, Escorts = escorts, X = 2450, Y = 1500 }).Accepted, "Dispatch supply-dependent target");
    for (var i = 0; i < 2200 && !World(room).Convoys.Any(x => x.OwnerId == room.HostId); i++) room.Advance(.25);
    var convoy = World(room).Convoys.First(x => x.OwnerId == room.HostId);
    Check(room.Execute(raider, new(Guid.NewGuid(), CommandKind.InterceptConvoy, raiderArmy.Id, DestinationId: convoy.Id)).Accepted, "Accept interception after shield expiry");
    Advance(room, 80);
    var publicWorld = World(room);
    Check((wins ? publicWorld.Alerts.Any(x => x.Kind == "convoy-loss") : publicWorld.Notices.Any(x => x.Text.Contains("protected a supply convoy"))), wins ? "Interception destroys unprotected wagons" : "Strong escorts repel interception");
    var total = Economy(room).Garrison.Values.Sum() + publicWorld.Armies.Where(x => x.OwnerId == room.HostId).Sum(x => x.Soldiers + x.Wagons)
        + publicWorld.Convoys.Where(x => x.OwnerId == room.HostId).Sum(x => x.Escorts + x.Wagons);
    var losses = publicWorld.Warfare.Rankings.Single(x => x.PlayerId == room.HostId);
    total += Economy(room).Wounded + losses.Dead + losses.Fled;
    Check(total == (wins ? 21 : 22), "Interception conserves living, wounded and permanently lost troops");
    if (wins) Check(OwnArmy(room).Morale < 90, "Broken supply convoy immediately reduces target army morale");
}
CheckInterception(2, true);
CheckInterception(0, true);
checks += WarfareChecks.Run();
checks += BiomeLogisticsChecks.Run();
checks += GameplayChecks.Run();
checks += OasisChecks.Run();
checks += OasisEconomyChecks.Run();
checks += OasisSiegeChecks.Run();
checks += MultiArmyChecks.Run();
checks += CityObjectiveChecks.Run();
checks += TrainingProgressionChecks.Run();
checks += SocialChecks.Run();
checks += CampChecks.Run();
checks += TribalCostChecks.Run();
Console.WriteLine($"Passed {checks} deterministic game checks.");

sealed class ManualTime : TimeProvider
{
    private DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private long timestamp;
    public override DateTimeOffset GetUtcNow() => now;
    public override long GetTimestamp() => timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public void Advance(TimeSpan duration) { now += duration; timestamp += duration.Ticks; }
}

