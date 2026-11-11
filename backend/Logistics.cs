namespace Warzone;

public sealed record ArmySnapshot(Guid Id, Guid OwnerId, Guid HomeVillageId, Position Position, Position Destination,
    string Order, int Soldiers, int Wagons, double Speed, double? Wheat, double Capacity, double? Morale,
    bool AutoSupply, int SupplyWagons, int Escorts, string? SupplyStatus, Guid? TargetId, double ActionSeconds,
    Guid SupplyVillageId, Resources? Loot, IReadOnlyDictionary<string, int>? Formation, AfterAttack AfterAttack, TribeType Tribe, Guid? DefendingSiteId, Guid? SupportedOwnerId,
    IReadOnlyDictionary<string, int>? MapFigures = null, double? CampRadius = null);
public sealed record ConvoySnapshot(Guid Id, Guid OwnerId, Guid? TargetArmyId, Position Position, Position Destination,
    bool Returning, int Wagons, int Escorts, double? Wheat, double Speed, ConvoyKind Kind, bool InBattle, Resources? Cargo, Guid? RecipientId);
public sealed record WorldNotice(long Id, double At, string Text, string Kind = "info", double? EndsAt = null, Guid? TargetId = null);
public sealed record IncomingThreat(Guid ArmyId, Guid OwnerId, Guid TargetId, double ArrivesAt);
public sealed record WorldSnapshot(ArmySnapshot[] Armies, ConvoySnapshot[] Convoys, OasisSnapshot[] Oases, WorldNotice[] Notices, WarfareSnapshot Warfare, IncomingThreat[] Threats, MusterSnapshot Muster, CombatReport[] Reports, BiomeRegion[] Regions, MarketOfferSnapshot[] MarketOffers, WorldNotice[] Alerts, OasisSiegeSnapshot[] OasisSieges);

// All commands and ticks run under the room lock. Movement and logistics never trust client clocks.
public sealed partial class LogisticsWorld
{
    private readonly Dictionary<Guid, VillageEconomy> economies;
    private readonly Dictionary<Guid, VillageEconomy> villages;
    public const double CampRadius = 90;
    private readonly List<Army> armies = [];
    private readonly List<Convoy> convoys = [];
    private readonly List<ResourceOasis> oases = [];
    private readonly List<(Guid Owner, WorldNotice Notice)> notices = [];
    private readonly List<(Guid Owner, WorldNotice Notice)> importantAlerts = [];
    private long noticeId;
    private readonly BiomeRegion[] regions;
    public LogisticsWorld(IEnumerable<VillageEconomy> villages, int? terrainSeed = null)
    {
        economies = villages.ToDictionary(x => x.Village.OwnerId);
        this.villages = economies.Values.ToDictionary(x => x.Village.Id);
        regions = terrainSeed is { } seed ? Biomes.Generate(seed) : [];
        foreach (var city in this.villages.Values) city.Biome = Biomes.At(regions, city.Village.Position);
        InitializeWarfare();
        foreach (var economy in economies.Values)
            economy.OnEvent = (kind, text, at, ends) => Note(economy.Village.OwnerId, at, text, kind, ends);
        oases.AddRange(ResourceOasis.Generate(this.villages.Values.Select(v => v.Village.Position).ToArray(), terrainSeed ?? 731));
    }
    public VillageEconomy? Primary(Guid actor) => villages.Values.FirstOrDefault(x => x.Village.OwnerId == actor && !x.Ruined);
    public VillageEconomy? Selected(Guid actor, Guid? id) => villages.Values.FirstOrDefault(x => x.Village.OwnerId == actor && !x.Ruined && x.Village.Id == id) ?? Primary(actor);
    public VillageEconomy EconomyTarget(Guid actor, Guid target) => villages.Values.FirstOrDefault(x => !x.Ruined && x.Village.OwnerId == actor && (x.Village.Id == target || x.ContainsTarget(target)))
        ?? throw new GameRuleException("not_owned", "This settlement or target does not belong to you.");
    public void AdvanceEconomies(double seconds, double now, double speed)
    { foreach (var village in villages.Values) village.Advance(seconds, now, speed); AdvanceOasisProduction(seconds, speed); }
    private void Note(Guid owner, double now, string text, string kind = "info", double? endsAt = null, Guid? targetId = null)
    {
        if (kind == "info" && (text.Contains("attack", StringComparison.OrdinalIgnoreCase) || text.Contains("siege", StringComparison.OrdinalIgnoreCase) || text.Contains("Battle", StringComparison.OrdinalIgnoreCase))) kind = "war";
        var notice = new WorldNotice(++noticeId, now, text, kind, endsAt, targetId);
        notices.Add((owner, notice));
        if (kind is "oasis-attack" or "oasis-loss" or "reinforcement" or "convoy-attack" or "convoy-loss")
        {
            importantAlerts.Add((owner, notice));
            while (importantAlerts.Count(x => x.Owner == owner) > 40) importantAlerts.RemoveAt(importantAlerts.FindIndex(x => x.Owner == owner));
        }
        while (notices.Count(x => x.Owner == owner) > 40) notices.RemoveAt(notices.FindIndex(x => x.Owner == owner));
    }
    public static double Distance(Position a, Position b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    private static Position Point(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x is < MapMinimum or > MapMaximum || y is < MapMinimum or > MapMaximum)
            throw new GameRuleException("bounds", "Choose a destination inside the map (30-2970).");
        return new(x, y);
    }
    private Army Owned(Guid actor, Guid id) => armies.FirstOrDefault(x => x.Id == id && x.OwnerId == actor)
        ?? throw new GameRuleException("not_owned", "This army does not belong to you.");
    private static IReadOnlyDictionary<string, int> OrderFormation(Army army, IReadOnlyDictionary<string, int>? formation) =>
        formation ?? army.Troops.ToDictionary(t => t.Unit.UnitId, t => t.Count);
    private static int OrderFighters(Army army, IReadOnlyDictionary<string, int>? formation) => OrderFormation(army, formation)
        .Sum(x => GameCatalog.Units.TryGetValue(x.Key, out var unit) && unit.Role is not (UnitRole.Wagon or UnitRole.Governor) ? x.Value : 0);
    private Army Detach(Army army, IReadOnlyDictionary<string, int>? formation, double now)
    {
        if (formation is null) return army;
        if (Engaged(army) || army.Order is "Battle" or "Sieging") throw new GameRuleException("engaged", "Finish the engagement or retreat before splitting this army.");
        if (army.Routing) throw new GameRuleException("routing", "A broken army cannot be split.");
        var available = army.Troops.ToDictionary(t => t.Unit.UnitId, t => t.Count);
        if (formation.Count == 0 || formation.Any(x => x.Value <= 0 || x.Value > available.GetValueOrDefault(x.Key)))
            throw new GameRuleException("troops", "Choose units from this army; the remainder keeps its current orders.");
        var selected = army.Troops.Select(t => (t.Unit, Count: formation.GetValueOrDefault(t.Unit.UnitId))).Where(t => t.Count > 0).Select(t => new TroopStack(t.Unit, t.Count)).ToArray();
        var remainder = army.Troops.Select(t => (t.Unit, Count: t.Count - formation.GetValueOrDefault(t.Unit.UnitId))).Where(t => t.Count > 0).Select(t => new TroopStack(t.Unit, t.Count)).ToArray();
        if (remainder.Length == 0) return army;
        if (armies.Count(x => x.OwnerId == army.OwnerId) >= 16) throw new GameRuleException("army_limit", "Recall an army before splitting this force. Limit: sixteen.");
        var ratio = army.Capacity > 0 ? selected.Sum(t => (double)t.Count * t.Unit.CarryCapacity) / army.Capacity : selected.Sum(t => t.Count) / (double)Count(army);
        var wheat = army.Wheat * ratio;
        var loot = new Resources((long)(army.Loot.Wood * ratio), (long)(army.Loot.Iron * ratio), (long)(army.Loot.Stone * ratio), (long)(army.Loot.Wheat * ratio));
        var leftBehind = new Army(army.OwnerId, army.Position, army.HomeVillageId, remainder)
        {
            Wheat = army.Wheat - wheat, Loot = army.Loot - loot, Destination = army.Destination, Order = army.Order,
            TargetId = army.TargetId, DefendingSiteId = army.DefendingSiteId, SupportedOwnerId = army.SupportedOwnerId,
            CampRadius = army.CampRadius, CampLoadedOnly = army.CampLoadedOnly, ActionSeconds = army.ActionSeconds, AttackOrderedAt = army.AttackOrderedAt,
            AfterAttack = army.AfterAttack, OasisMode = army.OasisMode, Objective = army.Objective,
            SupplyVillageId = army.SupplyVillageId, NextSupplyAt = army.NextSupplyAt, SupplyWagons = army.SupplyWagons,
            Escorts = army.Escorts, SupplyStatus = army.SupplyStatus
        };
        leftBehind.SetSupply(army.AutoSupplyEnabled);
        armies.Add(leftBehind);
        army.Troops = Array.AsReadOnly(selected); army.Wheat = wheat; army.Loot = loot; army.CampRadius = null;
        army.DefendingSiteId = null; army.SupportedOwnerId = null; army.TargetId = null; army.ActionSeconds = 0; army.AttackOrderedAt = 0;
        army.NextSupplyAt = now;
        return army;
    }
    private static string UnitId(VillageEconomy economy, UnitRole role) => economy.Training.Single(x => x.Unit.Role == role).Unit.UnitId;
    private static int WagonCount(IEnumerable<TroopStack> troops) => troops.Where(x => x.Unit is SupplyWagon).Sum(x => x.Count);
    private void ValidateSupply(VillageEconomy home, GameCommand command)
    {
        if (command.SourceVillageId is { } source && EconomyTarget(home.Village.OwnerId, source).Village.Id != source)
            throw new GameRuleException("not_owned", "Choose an owned village as the supply source.");
        if (command.Wagons is < 0 or > 20 || command.Escorts is < 0 or > 100)
            throw new GameRuleException("supply", "Use 0-20 supply wagons (0 calculates automatically) and 0-100 escorts.");
    }
    public void Apply(Guid actor, GameCommand command, double now, double shieldUntil)
    {
        if (!Enum.IsDefined(command.AfterAttack)) throw new GameRuleException("directive", "Unknown post-attack order.");
        if (!Enum.IsDefined(command.OasisMode)) throw new GameRuleException("directive", "Unknown oasis order.");
        if (!Enum.IsDefined(command.Objective)) throw new GameRuleException("objective", "Choose a valid attack objective.");
        if (command.Kind == CommandKind.SetAfterAttack) { Owned(actor, command.TargetId).AfterAttack = command.AfterAttack; return; }
        if (ApplyShipment(actor, command, now)) return;
        if (ApplyMarket(actor, command, now)) return;
        if (ApplyOasis(actor, command, now)) return;
        if (ApplyWarfare(actor, command, now, shieldUntil)) return;
        var home = Primary(actor) ?? throw new GameRuleException("eliminated", "You have no surviving settlements.");
        if (command.Kind == CommandKind.DispatchArmy)
        {
            home = EconomyTarget(actor, command.TargetId);
            if (command.TargetId != home.Village.Id) throw new GameRuleException("not_owned", "Dispatch from your own village.");
            if (armies.Count(x => x.OwnerId == actor) >= 16) throw new GameRuleException("army_limit", "Recall an army before dispatching another. Limit: sixteen.");
            ValidateSupply(home, command);
            if (command.Quantity is < 1 or > 10000)
                throw new GameRuleException("quantity", "Dispatch 1-10000 units.");
            var requested = command.Formation is null
                ? new Dictionary<string, int> { [command.UnitId ?? ""] = command.Quantity }
                : new Dictionary<string, int>(command.Formation);
            if (requested.Count is < 1 or > 70 || requested.Any(x => x.Value is < 1 or > 10000
                || !GameCatalog.Units.TryGetValue(x.Key, out var unit)
                || (unit.TribeType != home.Village.Tribe && home.Available(x.Key) == 0)) || requested.Values.Sum() > 10000)
                throw new GameRuleException("unit", "Choose up to 10000 available units, including wagons.");
            var cityTarget = !command.Reinforcement ? villages.GetValueOrDefault(command.DestinationId ?? Guid.Empty) : null;
            if (cityTarget is not null) ValidateCityAttack(actor, cityTarget, command.Objective, requested, now, shieldUntil);
            var target = !command.Reinforcement && cityTarget is null && command.DestinationId is { } oasisId ? oases.FirstOrDefault(x => x.Id == oasisId)
                ?? throw new GameRuleException("target", "Oasis not found.") : null;
            var destination = command.Reinforcement ? DefenseSite(command.DestinationId ?? Guid.Empty).Position
                : cityTarget?.Village.Position ?? target?.Position ?? Point(command.X, command.Y);
            if (command.Camp && (cityTarget is not null || target is not null || command.Reinforcement))
                throw new GameRuleException("target", "Choose an open map position for the camp.");
            if (command.Camp && !requested.Keys.Any(id => GameCatalog.Units[id].Role is not (UnitRole.Wagon or UnitRole.Governor)))
                throw new GameRuleException("troops", "Fighting troops are required to establish a camp.");
            if (target is not null && target.OwnerId != actor && target.OwnerId is not null && now < shieldUntil)
                throw new GameRuleException("protected", "Wait until the protection shield expires.");
            if (command.OasisMode==OasisMode.Blockade&&(target?.OwnerId is null||target.OwnerId==actor))
                throw new GameRuleException("target","Choose another player's oasis to blockade.");
            if ((target is not null || command.Reinforcement) && !requested.Keys.Any(id => GameCatalog.Units[id].Role is not (UnitRole.Wagon or UnitRole.Governor)))
                throw new GameRuleException("troops", "Fighting troops are required.");
            if (command.Reinforcement)
            {
                var site = DefenseSite(command.DestinationId ?? Guid.Empty);
                if (site.Owner is null) throw new GameRuleException("target", "Capture an unclaimed oasis before reinforcing it.");
                destination = site.Position;
            }
            var wagonId = UnitId(home, UnitRole.Wagon);
            if (!requested.Keys.Any(id => GameCatalog.Units[id].Role == UnitRole.Wagon) && command.Wagons > 0) requested[wagonId] = command.Wagons;
            if (requested.Values.Sum() > 10000) throw new GameRuleException("quantity", "The formation limit includes its wagons.");
            var upkeep = requested.Sum(x => x.Value * GameCatalog.Units[x.Key].Stats.WheatUpkeep) / 60;
            var slowest = requested.Min(x => GameCatalog.Units[x.Key].Stats.Speed);
            var carry = requested.Sum(x => (double)x.Value * GameCatalog.Units[x.Key].Stats.CarryCapacity);
            var trip = Distance(home.Village.Position, target?.Position ?? destination) / Math.Max(.1, slowest * .8);
            // Always cover the round trip and combat; target ten minutes when stores and carrying space allow.
            var required = (long)Math.Ceiling(upkeep * (trip * 2 + 180));
            var food = (long)Math.Min(Math.Ceiling(upkeep * Math.Max(600, trip * 2 + 180)), Math.Min(carry, home.Resources.Wheat));
            if (required > carry) throw new GameRuleException("food_capacity", "Add carrying units or shorten the journey: this formation cannot carry enough provisions.");
            if (food < required) throw new GameRuleException("resources", "Not enough wheat for the round trip and battle. Shorten the march or send fewer units.");
            var troops = home.Depart(requested, food);
            var army = new Army(actor, home.Village.Position, home.Village.Id, troops)
            {
                Wheat = food, Destination = target?.Position ?? destination, TargetId = cityTarget?.Village.Id ?? target?.Id, AfterAttack = command.AfterAttack,
                Order = command.Camp ? "Camping" : cityTarget is not null ? "Approaching" : target is null ? "Marching" : "Occupying", CampLoadedOnly = command.LoadedOnly, Objective=command.Objective, OasisMode=command.OasisMode, SupplyWagons = 0, Escorts = command.Escorts,
                SupplyVillageId = command.SourceVillageId ?? home.Village.Id, NextSupplyAt = now + 60,
                AttackOrderedAt = !command.Reinforcement && (cityTarget is not null || target is not null) ? now : 0
            };
            army.SetSupply(command.Enabled);
            armies.Add(army);
            if (cityTarget is not null) { UpdateWarDestination(army); Note(cityTarget.Village.OwnerId, now, "An enemy army is marching on your village.", "war", targetId:cityTarget.Village.Id); }
            if (target is not null) WarnOasisAttack(army, target, now);
            if (command.Reinforcement) OrderReinforcement(army, command.DestinationId!.Value, now);
            Note(actor, now, "Your expedition has set off. Keep its supply road open.");
            return;
        }
        var selected = Owned(actor, command.TargetId);
        home = villages[selected.HomeVillageId];
        if (selected.Order is "Battle" or "Sieging" && command.Kind is not (CommandKind.ConfigureSupply or CommandKind.RecallArmy))
            throw new GameRuleException("engaged", "This army is in combat. Recall it or send another formation.");
        if (selected.Routing && command.Kind is not CommandKind.ConfigureSupply)
            throw new GameRuleException("routing", "This army has broken and is returning home.");
        switch (command.Kind)
        {
            case CommandKind.Reinforce:
                OrderReinforcement(selected, command.DestinationId ?? Guid.Empty, now, command.Formation);
                break;
            case CommandKind.MoveArmy:
                var destination = Point(command.X, command.Y);
                selected = Detach(selected, command.Formation, now);
                selected.Destination = destination; selected.DefendingSiteId = null; selected.SupportedOwnerId = null; selected.CampRadius = null;
                selected.Order = "Marching"; selected.TargetId = null; selected.ActionSeconds = 0; selected.AttackOrderedAt = 0;
                break;
            case CommandKind.CampArmy:
                var campSite = Point(command.X, command.Y);
                if (OrderFighters(selected, command.Formation) == 0) throw new GameRuleException("troops", "Fighting troops are required to establish a camp.");
                selected = Detach(selected, command.Formation, now);
                selected.Destination = campSite; selected.DefendingSiteId = null; selected.SupportedOwnerId = null; selected.CampRadius = null;
                selected.Order = "Camping"; selected.TargetId = null; selected.ActionSeconds = 0; selected.AttackOrderedAt = 0;
                selected.CampLoadedOnly = command.LoadedOnly;
                break;
            case CommandKind.RecallArmy:
                ApplyRetreatLoss(selected, now);
                WithdrawFromCombat(selected, now);
                selected.Destination = home.Village.Position; selected.DefendingSiteId = null; selected.SupportedOwnerId = null; selected.CampRadius = null;
                selected.Order = "Returning"; selected.TargetId = null; selected.ActionSeconds = 0; selected.AttackOrderedAt = 0;
                break;
            case CommandKind.ConfigureSupply:
                ValidateSupply(home, command);
                selected.SetSupply(command.Enabled); selected.SupplyWagons = command.Wagons; selected.Escorts = command.Escorts;
                selected.SupplyVillageId = command.SourceVillageId ?? selected.HomeVillageId;
                selected.NextSupplyAt = now;
                break;
            case CommandKind.CaptureOasis:
            case CommandKind.BesiegeOasis:
                var oasis = oases.FirstOrDefault(x => x.Id == command.DestinationId) ?? throw new GameRuleException("target", "Oasis not found.");
                if (OrderFighters(selected, command.Formation) == 0) throw new GameRuleException("troops", "Fighting troops are required.");
                if (oasis.OwnerId is not null && oasis.OwnerId != actor && now < shieldUntil) throw new GameRuleException("protected", "Wait until the protection shield expires.");
                if(command.Kind==CommandKind.BesiegeOasis&&(oasis.OwnerId is null||oasis.OwnerId==actor))throw new GameRuleException("target","Choose another player's oasis to blockade.");
                selected = Detach(selected, command.Formation, now);
                selected.OasisMode=command.Kind==CommandKind.BesiegeOasis?OasisMode.Blockade:OasisMode.Capture;
                selected.TargetId = oasis.Id; selected.DefendingSiteId = null; selected.SupportedOwnerId = null; selected.Destination = oasis.Position; selected.Order = "Occupying"; selected.ActionSeconds = 0; selected.CampRadius = null;
                selected.AttackOrderedAt = now;
                selected.AfterAttack = command.AfterAttack;
                WarnOasisAttack(selected, oasis, now);
                break;
            case CommandKind.InterceptConvoy:
                if (now < shieldUntil) throw new GameRuleException("protected", "Player convoys are protected until the room shield expires.");
                var convoy = convoys.FirstOrDefault(x => x.Id == command.DestinationId && x.OwnerId != actor && !x.InBattle)
                    ?? throw new GameRuleException("target", "Select an enemy convoy that is not already in battle.");
                if (OrderFighters(selected, command.Formation) == 0) throw new GameRuleException("troops", "Fighting troops are required.");
                selected = Detach(selected, command.Formation, now);
                selected.TargetId = convoy.Id; selected.DefendingSiteId = null; selected.SupportedOwnerId = null; selected.Order = "Intercepting"; selected.ActionSeconds = 0; selected.CampRadius = null;
                selected.AttackOrderedAt = now;
                selected.AfterAttack = command.AfterAttack;
                Note(convoy.OwnerId, now, "An enemy army is approaching your caravan.", "convoy-attack", targetId: convoy.Id);
                break;
            default: throw new GameRuleException("command", "Unknown map command.");
        }
    }

    private static double Move(MapEntity entity, Position destination, double distance)
    {
        var gap = Distance(entity.Position, destination);
        var step = Math.Min(gap, distance);
        if (gap > 0 && step > 0)
        {
            entity.Relocate(new(entity.Position.X + (destination.X - entity.Position.X) * step / gap,
                entity.Position.Y + (destination.Y - entity.Position.Y) * step / gap));
        }
        return step;
    }

    public void Advance(double seconds, double now, double shieldUntil, double duration = 1800)
    {
        AdvanceOasisSieges(now);
        RefreshOasisBonuses();
        AdvanceOasisTransport(now);
        foreach (var army in armies.ToArray())
        {
            if (!armies.Contains(army)) continue;
            var home = villages[army.HomeVillageId];
            if (army.Order == "Returning") army.Destination = home.Village.Position;
            if (army.Order == "Intercepting")
            {
                var target = convoys.FirstOrDefault(x => x.Id == army.TargetId);
                if (target is null || now < shieldUntil) { army.Order = "Idle"; army.TargetId = null; army.Destination = army.Position; }
                else army.Destination = target.Position;
            }
            UpdateWarDestination(army);
            var engaged = army.Order is "Battle" or "Sieging";
            var blockedReturn = army.Order=="Returning" && home.Besieged && Distance(army.Position,home.Village.Position)<=65;
            var distance = engaged || blockedReturn ? 0 : Move(army, army.Destination, army.Speed * seconds * (army.Routing ? 1.25 : .6 + army.Morale / 250));
            var demand = army.Upkeep * seconds;
            var unmet = FeedArmy(army,demand);
            var hungrySeconds = army.Upkeep > 0 ? unmet / army.Upkeep : 0;
            army.AdjustMorale(hungrySeconds > 0 ? -hungrySeconds * 4 : seconds * .18 - distance * .025);
            if (army.Morale <= 0 && !army.Routing && army.Order != "Battle")
            {
                Route(army);
                Note(army.OwnerId, now, "An army ran out of food and morale. It is retreating home.");
            }
            var arrived = Distance(army.Position, army.Destination) < .01;
            if (arrived && army.Order == "Returning" && !home.Besieged)
            {
                home.Arrive(army.Troops, army.Wheat);
                home.AddResources(army.Loot);
                armies.Remove(army);
                Note(army.OwnerId, now, "Your expedition returned. Troops, wagons and remaining wheat are home.");
                continue;
            }
            if (arrived && army.Order == "Marching") army.Order = "Idle";
            if (arrived && army.Order == "Camping")
            {
                army.Order = "Camped"; army.CampRadius = CampRadius; army.Destination = army.Position;
                Note(army.OwnerId, now, $"Your camp is established. Hostile armies and caravans within {CampRadius:0} map units will be engaged automatically.");
            }
            AdvanceOasisAndReinforcements(army, seconds, now, shieldUntil);
            if (army.Order == "Intercepting" && Distance(army.Position, army.Destination) < 12)
            {
                StartConvoyBattle(army, now);
            }
            else if (army.Order == "Intercepting") army.ActionSeconds = 0;
            Supply(army, now);
        }
        AdvanceWarfare(seconds, now, shieldUntil, duration);
        AdvanceOasisSieges(now);
        foreach (var convoy in convoys.ToArray())
        {
            convoy.WheatCargo = Math.Max(0, convoy.WheatCargo - convoy.Troops.Sum(x => x.Count * x.Unit.WheatUpkeep) / 60 * seconds);
            if (convoy.InBattle) continue;
            if (convoy.Kind == ConvoyKind.Oasis) { AdvanceOasisConvoy(convoy, seconds, now); continue; }
            if (convoy.Kind is ConvoyKind.Trade or ConvoyKind.Shipment) { AdvanceVillageDelivery(convoy, seconds, now); continue; }
            var home = villages[convoy.SourceVillageId];
            var target = armies.FirstOrDefault(x => x.Id == convoy.TargetArmyId);
            if (target is null || target.Order == "Returning" || OasisFoodSource(target) is not null) convoy.Returning = true;
            var destination = convoy.Returning ? ConvoyCityDestination(convoy,home) : target!.Position;
            Move(convoy, destination, convoy.Speed * seconds);
            if (Distance(convoy.Position, destination) > 3) continue;
            if (!convoy.Returning && target is not null)
            {
                var delivered = Math.Min(convoy.WheatCargo, Math.Max(0, target.Capacity - target.Wheat - target.Loot.Total));
                target.Wheat += delivered; convoy.WheatCargo -= delivered; target.AdjustMorale(12);
                convoy.Returning = true;
                Note(convoy.OwnerId, now, $"Supply delivered: {Math.Floor(delivered)} wheat. The convoy is returning.");
            }
            else
            {
                if (home.Besieged) continue;
                home.Arrive(convoy.Troops, convoy.WheatCargo);
                convoys.Remove(convoy);
            }
        }
    }
    private void Supply(Army army, double now)
    {
        if(OasisFoodSource(army) is not null){army.SupplyStatus="Supplied by oasis wheat";return;}
        if (!army.AutoSupplyEnabled) { army.SupplyStatus = "Manual supply"; return; }
        if (army.Order == "Returning") { army.SupplyStatus = "Returning home"; return; }
        if (convoys.Any(x => x.TargetArmyId == army.Id)) { army.SupplyStatus = "Convoy in transit"; return; }
        if (now < army.NextSupplyAt) return;
        var home = villages[army.SupplyVillageId];
        if (home.Besieged) { army.SupplyStatus = "Source village blockaded"; return; }
        var wagonId = UnitId(home, UnitRole.Wagon);
        var escortId = UnitId(home, UnitRole.Infantry);
        var speed = GameCatalog.Units[wagonId].Stats.Speed;
        if (army.Escorts > 0) speed = Math.Min(speed, GameCatalog.Units[escortId].Stats.Speed);
        var travel = Distance(home.Village.Position, army.Position) / speed;
        var desired = Math.Min(army.Capacity, Math.Ceiling(army.Upkeep * (2 * travel + 180)));
        if (army.Wheat > army.Upkeep * (travel + 100)) { army.SupplyStatus = "Well supplied"; return; }
        var escortUpkeep = army.Escorts * GameCatalog.Units[escortId].Stats.WheatUpkeep / 60;
        var wagonUpkeep = GameCatalog.Units[wagonId].Stats.WheatUpkeep / 60;
        desired += Math.Ceiling((escortUpkeep + wagonUpkeep) * (2 * travel + 60));
        var wagonCapacity = GameCatalog.Units[UnitId(home, UnitRole.Wagon)].Stats.CarryCapacity;
        var count = army.SupplyWagons == 0 ? Math.Clamp((int)Math.Ceiling(desired / wagonCapacity), 1, 20) : army.SupplyWagons;
        desired += Math.Ceiling((count - 1) * wagonUpkeep * (2 * travel + 60));
        var cargo = (long)Math.Floor(Math.Min(desired, Math.Min(count * wagonCapacity, home.Resources.Wheat)));
        if (cargo < 1) { army.SupplyStatus = "Waiting for wheat"; army.NextSupplyAt = now + 5; return; }
        if (home.Available(wagonId) < count || home.Available(escortId) < army.Escorts)
        { army.SupplyStatus = "Waiting for wagons or escorts"; army.NextSupplyAt = now + 5; return; }
        var requested = new Dictionary<string, int> { [wagonId] = count };
        if (army.Escorts > 0) requested[escortId] = army.Escorts;
        var troops = home.Depart(requested, cargo);
        convoys.Add(new(army.OwnerId, home.Village.Position, home.Village.Id, army.Id, troops, cargo));
        army.NextSupplyAt = now + 10; army.SupplyStatus = "Convoy in transit";
        Note(army.OwnerId, now, $"A supply convoy departed with {count} wagon(s) and {army.Escorts} escorts.");
    }
    private static readonly UnitRole[] BotCombatRoles =
    [
        UnitRole.HeavyCavalry, UnitRole.Crossbowman, UnitRole.Swordsman, UnitRole.Cavalry,
        UnitRole.Spearman, UnitRole.LightCavalry, UnitRole.Archer, UnitRole.Infantry
    ];
    private static int HomeCombatTroops(VillageEconomy home) =>
        BotCombatRoles.Sum(role => home.Available(UnitId(home, role)));

    private Dictionary<string, int>? BuildBotFormation(VillageEconomy home, Position destination, int maxSoldiers, bool includeSiege, bool includeGovernor, int reserveAtHome = 0)
    {
        var availableTotal = HomeCombatTroops(home);
        var budget = Math.Min(maxSoldiers, Math.Max(0, availableTotal - reserveAtHome));
        if (budget < 8) return null;
        var formation = new Dictionary<string, int>();
        var remaining = budget;
        foreach (var role in BotCombatRoles)
        {
            var id = UnitId(home, role);
            var count = Math.Min(remaining, home.Available(id));
            if (count > 0) { formation[id] = count; remaining -= count; }
            if (remaining <= 0) break;
        }
        if (formation.Values.Sum() < 8) return null;
        if (includeSiege)
            foreach (var role in new[] { UnitRole.Ram, UnitRole.Catapult, UnitRole.SiegeTower })
            {
                var id = UnitId(home, role);
                if (home.Available(id) > 0) formation[id] = Math.Min(3, home.Available(id));
            }
        if (includeGovernor)
        {
            var govId = UnitId(home, UnitRole.Governor);
            if (home.Available(govId) > 0) formation[govId] = 1;
        }
        var wagonId = UnitId(home, UnitRole.Wagon);
        var availWagons = home.Available(wagonId);
        if (availWagons < 1) return null;
        formation[wagonId] = Math.Clamp((formation.Values.Sum() + 19) / 20, 1, Math.Min(5, availWagons));
        var upkeep = formation.Sum(x => x.Value * GameCatalog.Units[x.Key].Stats.WheatUpkeep) / 60;
        var slowest = formation.Min(x => GameCatalog.Units[x.Key].Stats.Speed);
        var carry = formation.Sum(x => (double)x.Value * GameCatalog.Units[x.Key].Stats.CarryCapacity);
        var trip = Distance(home.Village.Position, destination) / Math.Max(.1, slowest * .8);
        var required = (long)Math.Ceiling(upkeep * (trip * 2 + 180));
        while (required > carry && formation[wagonId] < availWagons && formation[wagonId] < 8)
        {
            formation[wagonId]++;
            upkeep = formation.Sum(x => x.Value * GameCatalog.Units[x.Key].Stats.WheatUpkeep) / 60;
            carry = formation.Sum(x => (double)x.Value * GameCatalog.Units[x.Key].Stats.CarryCapacity);
            required = (long)Math.Ceiling(upkeep * (trip * 2 + 180));
        }
        return required <= carry && home.Resources.Wheat >= required ? formation : null;
    }

    private ResourceOasis? BestOasisForBot(Guid actor, VillageEconomy home, Position from, int armyFighters)
    {
        var targeted = armies.Where(a => a.OwnerId == actor && a.Order is "Occupying" or "Blockading" && a.TargetId is not null)
            .Select(a => a.TargetId!.Value).ToHashSet();
        var wheatLow = home.FoodShortage || home.Resources.Wheat < 500;
        return oases.Where(o => o.OwnerId != actor && !targeted.Contains(o.Id) && (o.OwnerId is null || warTime >= protectionUntil))
            .Where(o => o.OwnerId is null || armyFighters >= Math.Max(12, Guards(o.Id, actor).Sum(Fighters) + o.WallLevel * 6 + 4))
            .OrderBy(o =>
            {
                var dist = Distance(from, o.Position);
                var unclaimedBonus = o.OwnerId is null ? -350 : 0;
                var wheatBonus = wheatLow && o.Resource == ResourceType.Wheat ? -220 : 0;
                var defensePenalty = o.OwnerId is null ? 0 : (Guards(o.Id, actor).Sum(Fighters) + o.WallLevel * 6) * 8;
                return dist + unclaimedBonus + wheatBonus + defensePenalty;
            })
            .FirstOrDefault();
    }

    internal void BotAct(Guid actor, IGameCommands commands, BotPersonality personality = BotPersonality.Economic, BotDifficulty difficulty = BotDifficulty.Normal)
    {
        var home = Primary(actor);
        if (home is null) return;
        foreach (var extra in villages.Values.Where(v => !v.Ruined && v.Village.OwnerId == actor && v != home))
            BotController.ActEconomy(commands, actor, extra.Snapshot(), personality, difficulty);
        var economy = home.Snapshot();
        var homeTroops = HomeCombatTroops(home);
        var ownArmies = armies.Where(x => x.OwnerId == actor).ToArray();
        var desiredWagons = Math.Clamp(3 + home.OwnedOases * 2 + ownArmies.Length, 3, 9);
        UnitRole? neededRole = null;
        var batch = 1;
        if (home.Available(UnitId(home, UnitRole.Wagon)) + home.ReservedTradeWagons < desiredWagons && !economy.Jobs.Any(x => x.Lane == "Stable"))
        { neededRole = UnitRole.Wagon; batch = 2; }
        else if (warTime > protectionUntil * .5 && homeTroops >= 20 && !economy.Jobs.Any(x => x.Lane == "SiegeWorkshop"))
        {
            if (home.BuildingLevel(BuildingType.SiegeWorkshop) >= 3 && home.Available(UnitId(home, UnitRole.Catapult)) < (personality == BotPersonality.Aggressive ? 3 : 2))
                neededRole = UnitRole.Catapult;
            else if (home.Available(UnitId(home, UnitRole.Ram)) < (personality == BotPersonality.Aggressive ? 3 : 2))
                neededRole = UnitRole.Ram;
            else if (home.BuildingLevel(BuildingType.SiegeWorkshop) >= 5 && home.Available(UnitId(home, UnitRole.SiegeTower)) < 2)
                neededRole = UnitRole.SiegeTower;
        }
        else if (personality != BotPersonality.Defensive && warTime > protectionUntil * .7 && home.Available(UnitId(home, UnitRole.Governor)) == 0
            && homeTroops >= 35 && !economy.Jobs.Any(x => x.Lane == "MainHall"))
            neededRole = UnitRole.Governor;

        if (neededRole is { } needed)
        {
            var target = economy.Targets.FirstOrDefault(x => x.UnitId == UnitId(home, needed) && x.Unlocked);
            if (target?.TrainingCost is { } unitCost && economy.Resources.Covers(new(unitCost.Wood * batch, unitCost.Iron * batch, unitCost.Stone * batch, unitCost.Wheat * batch)))
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.Train, target.Id, Quantity: batch));
        }

        if (!home.Besieged && home.Resources.Wood > 1100 && home.Resources.Stone > 1100 && home.Resources.Iron > 800)
        {
            var maxWall = personality == BotPersonality.Defensive ? 4 : personality == BotPersonality.Economic ? 3 : 2;
            var fortify = oases.FirstOrDefault(o => o.OwnerId == actor && o.VillageId == home.Village.Id && !OasisUnderAttack(o)
                && o.WallLevel < maxWall && o.WallUpgradeCost is { } c && home.Resources.Covers(c));
            if (fortify is not null)
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.UpgradeOasisWall, fortify.Id));
        }
        if (!home.Besieged && home.BuildingLevel(BuildingType.Market) >= 1 && home.Available(UnitId(home, UnitRole.Wagon)) >= 1)
        {
            var stocks = new[]
            {
                (Type: ResourceType.Wood, Value: home.Resources.Wood),
                (Type: ResourceType.Iron, Value: home.Resources.Iron),
                (Type: ResourceType.Stone, Value: home.Resources.Stone),
                (Type: ResourceType.Wheat, Value: home.Resources.Wheat)
            };
            var openDeal = marketOffers.FirstOrDefault(o => o.OwnerId != actor
                && o.AskAmount <= o.OfferAmount * 2
                && stocks.Single(s => s.Type == o.AskResource).Value >= o.AskAmount + 150
                && home.Resources.Wheat >= 100
                && villages.TryGetValue(o.VillageId, out var seller) && !seller.Ruined && !seller.Besieged && seller.Resources.Wheat >= 50);
            if (openDeal is not null)
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.AcceptMarketOffer, home.Village.Id, DestinationId: openDeal.Id));
            else
            {
                var scarce = stocks.MinBy(x => x.Value);
                var surplus = stocks.MaxBy(x => x.Value);
                if (scarce.Value < 350 && surplus.Value > 1500 && scarce.Type != surplus.Type && !marketOffers.Any(o => o.VillageId == home.Village.Id))
                    commands.Execute(actor, new(Guid.NewGuid(), CommandKind.PostMarketOffer, home.Village.Id,
                        OfferResource: surplus.Type, OfferAmount: 400, AskResource: scarce.Type, AskAmount: 350));
            }
        }

        if (WarBotAct(actor, commands, personality, difficulty)) return;

        var weakArmy = ownArmies.FirstOrDefault(a => a.Order is "Idle" or "Guarding" && !Engaged(a) && !a.Routing
            && (a.Morale < 38 || (warTime > protectionUntil && Fighters(a) < 16 && homeTroops >= 24)));
        if (weakArmy is not null)
        { commands.Execute(actor, new(Guid.NewGuid(), CommandKind.RecallArmy, weakArmy.Id)); return; }

        var availableArmy = ownArmies.FirstOrDefault(a => a.Order == "Idle"
            || (a.Order == "Guarding" && (home.OwnedOases == 0 || ownArmies.Count(x => x.DefendingSiteId == a.DefendingSiteId) > 1 || (warTime >= protectionUntil && Fighters(a) >= 24))));
        var maxArmies = warTime < protectionUntil ? 2 : difficulty == BotDifficulty.Hard ? 3 : personality == BotPersonality.Aggressive ? 3 : 2;
        var candidateFighters = availableArmy is not null ? Fighters(availableArmy) : Math.Max(0, homeTroops - (warTime >= protectionUntil ? 8 : 0));
        var oasis = BestOasisForBot(actor, home, availableArmy?.Position ?? home.Village.Position, candidateFighters);
        if (oasis is null) return;
        if (availableArmy is not null && (oasis.OwnerId is null || Fighters(availableArmy) >= 16))
        {
            commands.Execute(actor, new(Guid.NewGuid(), CommandKind.CaptureOasis, availableArmy.Id, DestinationId: oasis.Id));
            return;
        }
        if (ownArmies.Length < maxArmies && homeTroops >= (warTime < protectionUntil ? 10 : 18) && home.Available(UnitId(home, UnitRole.Wagon)) >= 1)
        {
            var reserve = warTime < protectionUntil ? 0 : personality == BotPersonality.Defensive ? 12 : 6;
            var maxDispatch = warTime < protectionUntil ? 16 : 60;
            var formation = BuildBotFormation(home, oasis.Position, maxDispatch, warTime >= protectionUntil, personality == BotPersonality.Aggressive && warTime >= protectionUntil, reserve);
            if (formation is not null)
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
                    formation.Values.Sum(), true, oasis.Position.X, oasis.Position.Y, UnitId(home, UnitRole.Infantry), 0, 2, oasis.Id, Formation: formation));
        }
    }
    private static bool IsScoutOnly(Army army) =>
        army.Order == "Scouting" || (army.Troops.Any(t => t.Unit.Definition.Role == UnitRole.Scout)
            && army.Troops.Where(t => t.Unit is not SupplyWagon).All(t => t.Unit.Definition.Role == UnitRole.Scout));

    public WorldSnapshot Snapshot(Guid viewer) => new(
        armies.Where(x => !(x.Order == "Guarding" && x.DefendingSiteId is { } siteId && oases.Any(o => o.Id == siteId) && x.OwnerId != viewer && x.SupportedOwnerId != viewer)
                && (x.OwnerId == viewer || !IsScoutOnly(x)))
            .Select(x => new ArmySnapshot(x.Id, x.OwnerId, x.HomeVillageId, x.Position, x.Destination, x.Order,
            x.Troops.Where(t => t.Unit is not SupplyWagon && (x.OwnerId == viewer || t.Unit.Definition.Role != UnitRole.Scout)).Sum(t => t.Count), WagonCount(x.Troops), x.Speed,
            x.OwnerId == viewer ? x.Wheat : null, x.Capacity, x.OwnerId == viewer ? x.Morale : null,
            x.AutoSupplyEnabled, x.OwnerId == viewer ? x.SupplyWagons : 0, x.OwnerId == viewer ? x.Escorts : 0,
            x.OwnerId == viewer ? x.SupplyStatus : null, x.TargetId, x.ActionSeconds, x.SupplyVillageId,
            x.OwnerId == viewer ? x.Loot : null, x.OwnerId == viewer || scoutedArmies.Contains((viewer, x.Id)) ? x.Troops.ToDictionary(t => t.Unit.UnitId, t => t.Count) : null, x.AfterAttack, villages[x.HomeVillageId].Village.Tribe, x.DefendingSiteId, x.SupportedOwnerId,
            x.Troops.Where(t => t.Count > 0 && (x.OwnerId == viewer || t.Unit.Definition.Role != UnitRole.Scout)).ToDictionary(t => t.Unit.UnitId, t => (int)Math.Ceiling(t.Count / 10.0)), x.CampRadius)).ToArray(),
        convoys.Select(x => new ConvoySnapshot(x.Id, x.OwnerId, x.TargetArmyId, x.Position,
            ConvoyDestination(x),
            x.Returning, WagonCount(x.Troops), x.Troops.Where(t => t.Unit is not SupplyWagon).Sum(t => t.Count),
            x.OwnerId == viewer ? x.WheatCargo : null, x.Speed, x.Kind, x.InBattle, x.OwnerId == viewer || x.RecipientId == viewer ? x.Cargo : null, x.RecipientId)).ToArray(),
        oases.Select(x => OasisView(x, viewer)).ToArray(),
        notices.Where(x => x.Owner == viewer).Select(x => x.Notice).ToArray(), WarfareSnapshot(viewer),
        armies.Where(x => x.OwnerId != viewer && (x.Order == "Approaching" && villages.GetValueOrDefault(x.TargetId ?? Guid.Empty)?.Village.OwnerId == viewer
            || x.Order == "Attacking" && armies.Any(a => a.Id == x.TargetId && a.OwnerId == viewer)))
            .Select(x => new IncomingThreat(x.Id, x.OwnerId, x.TargetId!.Value, warTime + Math.Max(0, Distance(x.Position, x.Destination) - (x.Order == "Approaching" ? 2 : 16))
                / Math.Max(.1, x.Speed * (.6 + x.Morale / 250)))).ToArray(), Muster(viewer),
        reports.Where(r => r.Attacker == viewer || r.Defender == viewer || r.Forces.Any(f=>f.Army.OwnerId==viewer)).OrderByDescending(r => r.Status=="Active").ThenByDescending(r => r.UpdatedAt).Take(Math.Max(40,reports.Count(r=>r.Status=="Active"))).Select(r => r.Snapshot(viewer)).ToArray(), regions, MarketSnapshot(), importantAlerts.Where(a => a.Owner == viewer).Select(a => a.Notice).ToArray(), OasisSiegeViews());
    private void CompleteAttack(Army army)
    {
        army.DefendingSiteId = null; army.SupportedOwnerId = null; army.AttackOrderedAt = 0; army.CampRadius = null;
        army.TargetId = null;
        army.Order = army.AfterAttack == AfterAttack.Return ? "Returning" : "Idle";
        army.Destination = army.AfterAttack == AfterAttack.Return ? villages[army.HomeVillageId].Village.Position : army.Position;
    }
}
