namespace Warzone;

public sealed record ArmySnapshot(Guid Id, Guid OwnerId, Guid HomeVillageId, Position Position, Position Destination,
    string Order, int Soldiers, int Wagons, double Speed, double? Wheat, double Capacity,
    bool AutoSupply, int SupplyWagons, int Escorts, string? SupplyStatus, Guid? TargetId, double ActionSeconds,
    Guid SupplyVillageId, Resources? Loot, IReadOnlyDictionary<string, int>? Formation, AfterAttack AfterAttack, TribeType Tribe, Guid? DefendingSiteId, Guid? SupportedOwnerId,
    IReadOnlyDictionary<string, int>? MapFigures = null, double? CampRadius = null, int Wounded = 0, int Dead = 0, int Fled = 0, double? ScoutedUntil = null, AttackObjective? Objective = null);
public sealed record ConvoySnapshot(Guid Id, Guid OwnerId, Guid? TargetArmyId, Position Position, Position Destination,
    bool Returning, int Wagons, int Escorts, double? Wheat, double Speed, ConvoyKind Kind, bool InBattle, Resources? Cargo, Guid? RecipientId);
public sealed record WorldNotice(long Id, double At, string Text, string Kind = "info", double? EndsAt = null, Guid? TargetId = null);
public sealed record IncomingThreat(Guid ArmyId, Guid OwnerId, Guid TargetId, double ArrivesAt);
public sealed record WorldSnapshot(ArmySnapshot[] Armies, ConvoySnapshot[] Convoys, OasisSnapshot[] Oases, WorldNotice[] Notices, WarfareSnapshot Warfare, IncomingThreat[] Threats, MusterSnapshot Muster, CombatReport[] Reports, BiomeRegion[] Regions, MarketOfferSnapshot[] MarketOffers, WorldNotice[] Alerts, OasisSiegeSnapshot[] OasisSieges, IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, int>>? VillageGarrisons = null);

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
    public LogisticsWorld(IEnumerable<VillageEconomy> villages, int? terrainSeed = null, double castleUnlockSeconds = 1800)
    {
        this.castleUnlockSeconds = Math.Max(0, castleUnlockSeconds);
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
            throw new GameRuleException("bounds", $"Choose a destination inside the map ({MapMinimum}-{MapMaximum}).");
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
        var available = army.Troops.ToDictionary(t => t.Unit.UnitId, t => t.Count);
        if (formation.Count == 0 || formation.Any(x => x.Value <= 0 || x.Value > available.GetValueOrDefault(x.Key)))
            throw new GameRuleException("troops", "Choose units from this army; the remainder keeps its current orders.");
        var selected = army.Troops.Select(t => (t.Unit, Count: formation.GetValueOrDefault(t.Unit.UnitId))).Where(t => t.Count > 0).Select(t => new TroopStack(t.Unit, t.Count)).ToArray();
        var remainder = army.Troops.Select(t => (t.Unit, Count: t.Count - formation.GetValueOrDefault(t.Unit.UnitId))).Where(t => t.Count > 0).Select(t => new TroopStack(t.Unit, t.Count)).ToArray();
        if (remainder.Length == 0) return army;
        if (Engaged(army) || army.Order is "Battle" or "Sieging") throw new GameRuleException("engaged", "Finish the engagement or retreat before splitting this army.");
        if (armies.Count(x => x.OwnerId == army.OwnerId) >= 16) throw new GameRuleException("army_limit", "Recall an army before splitting this force. Limit: sixteen.");
        var selCap = selected.Sum(t => (double)t.Count * t.Unit.CarryCapacity);
        var remCap = remainder.Sum(t => (double)t.Count * t.Unit.CarryCapacity);
        if (army.Capacity > 0 && (selCap <= 0 || remCap <= 0))
            throw new GameRuleException("food_capacity", "Include carrying units in each split force so neither runs out of provisions.");
        var capRatio = army.Capacity > 0 ? selCap / army.Capacity : selected.Sum(t => t.Count) / (double)Count(army);
        var selUpkeep = selected.Sum(t => t.Count * t.Unit.WheatUpkeep) / 60;
        var upkeepWheat = army.Upkeep > 0 ? army.Wheat * (selUpkeep / army.Upkeep) : army.Wheat * capRatio;
        var minWheat = Math.Max(0, army.Wheat - remCap);
        var maxWheat = Math.Min(army.Wheat, selCap);
        var wheat = minWheat <= maxWheat ? Math.Clamp(upkeepWheat, minWheat, maxWheat) : army.Wheat * capRatio;
        var loot = new Resources((long)(army.Loot.Wood * capRatio), (long)(army.Loot.Iron * capRatio), (long)(army.Loot.Stone * capRatio), (long)(army.Loot.Wheat * capRatio));
        var leftBehind = new Army(army.OwnerId, army.Position, army.HomeVillageId, remainder)
        {
            Wheat = army.Wheat - wheat, Loot = army.Loot - loot, Destination = army.Destination, Order = army.Order,
            TargetId = army.TargetId, DefendingSiteId = army.DefendingSiteId, SupportedOwnerId = army.SupportedOwnerId,
            CampRadius = army.CampRadius, CampLoadedOnly = army.CampLoadedOnly, ActionSeconds = army.ActionSeconds, AttackOrderedAt = army.AttackOrderedAt,
            AfterAttack = army.AfterAttack, OasisMode = army.OasisMode, Objective = army.Objective,
            SupplyVillageId = army.SupplyVillageId, NextSupplyAt = now, SupplyWagons = army.SupplyWagons,
            Escorts = army.Escorts, SupplyStatus = army.SupplyStatus, StarvationTimer = army.StarvationTimer
        };
        leftBehind.SetSupply(army.AutoSupplyEnabled);
        armies.Add(leftBehind);
        foreach (var os in oasisSieges.Where(s => s.Status == "Active"))
        {
            var idx = os.Attackers.IndexOf(army);
            if (idx >= 0) os.Attackers[idx] = leftBehind;
        }
        army.Troops = Array.AsReadOnly(selected); army.Wheat = wheat; army.Loot = loot; army.CampRadius = null;
        army.DefendingSiteId = null; army.SupportedOwnerId = null; army.TargetId = null; army.ActionSeconds = 0; army.AttackOrderedAt = 0;
        army.NextSupplyAt = now;
        return army;
    }
    private static string? TryUnitId(VillageEconomy economy, UnitRole role) => economy.Training.FirstOrDefault(x => x.Unit.Role == role)?.Unit.UnitId;
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
        if (command.Kind == CommandKind.DisbandTroops) { ApplyDisbandTroops(actor, command, now); return; }
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
            var castleTarget = !command.Reinforcement && cityTarget is null && command.DestinationId == castle.Id ? castle : null;
            if (castleTarget is not null && !castleTarget.Active)
                throw new GameRuleException("castle", "The central castle opens at 30 minutes.");
            var target = !command.Reinforcement && cityTarget is null && castleTarget is null && command.DestinationId is { } oasisId ? oases.FirstOrDefault(x => x.Id == oasisId)
                ?? throw new GameRuleException("target", "Oasis not found.") : null;
            var destination = command.Reinforcement ? DefenseSite(command.DestinationId ?? Guid.Empty).Position
                : cityTarget?.Village.Position ?? castleTarget?.Position ?? target?.Position ?? Point(command.X, command.Y);
            if (command.Camp && (cityTarget is not null || castleTarget is not null || target is not null || command.Reinforcement))
                throw new GameRuleException("target", "Choose an open map position for the camp.");
            if (command.Camp && !requested.Keys.Any(id => GameCatalog.Units[id].Role is not (UnitRole.Wagon or UnitRole.Governor)))
                throw new GameRuleException("troops", "Fighting troops are required to establish a camp.");
            if (target is not null && target.BurnedUntil > now)
                throw new GameRuleException("burned", "This oasis is burned and in ruins.");
            if (target is not null && target.OwnerId != actor && target.OwnerId is not null && now < shieldUntil)
                throw new GameRuleException("protected", "Wait until the protection shield expires.");
            if (command.OasisMode==OasisMode.Blockade&&(target?.OwnerId is null||target.OwnerId==actor))
                throw new GameRuleException("target","Choose another player's oasis to blockade.");
            if (command.OasisMode==OasisMode.Burn&&target is null)
                throw new GameRuleException("target","Choose a valid oasis to burn.");
            if ((target is not null || castleTarget is not null || command.Reinforcement) && !requested.Keys.Any(id => GameCatalog.Units[id].Role is not (UnitRole.Wagon or UnitRole.Governor)))
                throw new GameRuleException("troops", castleTarget is not null ? "Fighting troops are required to claim the castle." : "Fighting troops are required.");
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
                Wheat = food, Destination = target?.Position ?? destination, TargetId = cityTarget?.Village.Id ?? castleTarget?.Id ?? target?.Id, AfterAttack = command.AfterAttack,
                Order = command.Camp ? "Camping" : cityTarget is not null ? "Approaching" : castleTarget is not null ? "Capturing" : target is not null ? "Occupying" : Distance(home.Village.Position, destination) < .01 ? "Idle" : "Marching",
                CampLoadedOnly = command.LoadedOnly, Objective=command.Objective, OasisMode=command.OasisMode, SupplyWagons = 0, Escorts = command.Escorts,
                SupplyVillageId = command.SourceVillageId ?? home.Village.Id, NextSupplyAt = now + 60,
                AttackOrderedAt = !command.Reinforcement && (cityTarget is not null || castleTarget is not null || target is not null) ? now : 0
            };
            army.SetSupply(command.Enabled);
            armies.Add(army);
            if (cityTarget is not null) { UpdateWarDestination(army); Note(cityTarget.Village.OwnerId, now, "An enemy army is marching on your village.", "war", targetId:cityTarget.Village.Id); }
            if (target is not null) WarnOasisAttack(army, target, now);
            if (command.Reinforcement) OrderReinforcement(army, command.DestinationId!.Value, now);
            Note(actor, now, Distance(home.Village.Position, destination) < .01 && cityTarget is null && castleTarget is null && target is null && !command.Camp && !command.Reinforcement
                ? "A new field army has been formed outside the village."
                : "Your expedition has set off. Keep its supply road open.");
            return;
        }
        if (command.Kind == CommandKind.MergeArmies)
        {
            ApplyMergeArmies(actor, command, now);
            return;
        }
        var selected = Owned(actor, command.TargetId);
        home = villages[selected.HomeVillageId];
        if ((selected.Order == "Battle" || InActiveBattle(selected)) && command.Kind is not (CommandKind.ConfigureSupply or CommandKind.RecallArmy))
            throw new GameRuleException("engaged", "This army is in combat. Recall it or send another formation.");
        if (selected.Order == "Sieging" && command.Kind is not (CommandKind.ConfigureSupply or CommandKind.RecallArmy or CommandKind.MoveArmy or CommandKind.CampArmy or CommandKind.Reinforce or CommandKind.CaptureOasis or CommandKind.BesiegeOasis or CommandKind.BurnOasis))
            throw new GameRuleException("engaged", "This army is in combat. Recall it or send another formation.");
        var prevTargetId = selected.TargetId;
        var prevOrder = selected.Order;
        switch (command.Kind)
        {
            case CommandKind.Reinforce:
                OrderReinforcement(selected, command.DestinationId ?? Guid.Empty, now, command.Formation);
                WithdrawFromSieges(selected, now, prevTargetId, prevOrder);
                break;
            case CommandKind.MoveArmy:
                var destination = Point(command.X, command.Y);
                selected = Detach(selected, command.Formation, now);
                selected.Destination = destination; selected.DefendingSiteId = null; selected.SupportedOwnerId = null; selected.CampRadius = null;
                selected.Order = Distance(selected.Position, destination) < .01 ? "Idle" : "Marching"; selected.TargetId = null; selected.ActionSeconds = 0; selected.AttackOrderedAt = 0;
                WithdrawFromSieges(selected, now, prevTargetId, prevOrder);
                break;
            case CommandKind.CampArmy:
                var campSite = Point(command.X, command.Y);
                if (OrderFighters(selected, command.Formation) == 0) throw new GameRuleException("troops", "Fighting troops are required to establish a camp.");
                selected = Detach(selected, command.Formation, now);
                selected.Destination = campSite; selected.DefendingSiteId = null; selected.SupportedOwnerId = null; selected.CampRadius = null;
                selected.Order = "Camping"; selected.TargetId = null; selected.ActionSeconds = 0; selected.AttackOrderedAt = 0;
                selected.CampLoadedOnly = command.LoadedOnly;
                selected.AfterAttack = command.AfterAttack;
                WithdrawFromSieges(selected, now, prevTargetId, prevOrder);
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
            case CommandKind.BurnOasis:
                var oasis = oases.FirstOrDefault(x => x.Id == command.DestinationId) ?? throw new GameRuleException("target", "Oasis not found.");
                if (oasis.BurnedUntil > now) throw new GameRuleException("burned", "This oasis is burned and in ruins.");
                if (OrderFighters(selected, command.Formation) == 0) throw new GameRuleException("troops", "Fighting troops are required.");
                if (oasis.OwnerId is not null && oasis.OwnerId != actor && now < shieldUntil) throw new GameRuleException("protected", "Wait until the protection shield expires.");
                if(command.Kind==CommandKind.BesiegeOasis&&(oasis.OwnerId is null||oasis.OwnerId==actor))throw new GameRuleException("target","Choose another player's oasis to blockade.");
                selected = Detach(selected, command.Formation, now);
                selected.OasisMode=command.Kind==CommandKind.BesiegeOasis?OasisMode.Blockade:command.Kind==CommandKind.BurnOasis?OasisMode.Burn:OasisMode.Capture;
                selected.TargetId = oasis.Id; selected.DefendingSiteId = null; selected.SupportedOwnerId = null; selected.Destination = oasis.Position; selected.Order = "Occupying"; selected.ActionSeconds = 0; selected.CampRadius = null;
                selected.AttackOrderedAt = now;
                selected.AfterAttack = command.AfterAttack;
                if (prevTargetId != oasis.Id)
                    WithdrawFromSieges(selected, now, prevTargetId, prevOrder);
                else if (prevOrder == "Burning" && selected.OasisMode != OasisMode.Burn
                    && !armies.Any(a => a != selected && !a.Routing && a.Order == "Burning" && a.TargetId == oasis.Id))
                    oasis.BurningSeconds = 0;
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
                WithdrawFromSieges(selected, now, prevTargetId, prevOrder);
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
            var distance = engaged || blockedReturn ? 0 : Move(army, army.Destination, army.Speed * seconds * (army.Routing ? 1.25 : 1));
            var demand = army.Upkeep * seconds;
            var unmet = FeedArmy(army, demand);
            var hungrySeconds = army.Upkeep > 0 ? unmet / army.Upkeep : 0;
            if (hungrySeconds > 0)
            {
                army.StarvationTimer += hungrySeconds;
                if (!army.StarvingNotified)
                {
                    army.StarvingNotified = true;
                    Note(army.OwnerId, now, "An army ran out of food; 2 soldiers every 10 seconds are heading to the hospital.");
                }
                while (army.StarvationTimer >= 10 && army.Troops.Any(t => t.Unit is not SupplyWagon))
                {
                    army.StarvationTimer -= 10;
                    StarveArmyTroops(army, 2, now);
                }
                if (!army.Troops.Any(t => t.Unit is not SupplyWagon))
                {
                    if (Count(army) == 0) { armies.Remove(army); continue; }
                    if (army.Order != "Returning" && army.Order != "Battle") Route(army);
                }
            }
            else
            {
                army.StarvationTimer = 0;
                army.StarvingNotified = false;
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
        AdvanceMerges(now);
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
                var returnTravel = Distance(convoy.Position, ConvoyCityDestination(convoy, home)) / convoy.Speed;
                var returnUpkeep = convoy.Troops.Sum(x => x.Count * x.Unit.WheatUpkeep) / 60;
                var returnProvisions = Math.Ceiling(returnUpkeep * (returnTravel + 45));
                var availableToGive = Math.Max(0, convoy.WheatCargo - returnProvisions);

                var spaceForFood = Math.Max(0, target.Capacity - target.Wheat - target.Loot.Total);
                var delivered = Math.Min(availableToGive, spaceForFood);
                if (delivered > 0)
                {
                    target.Wheat += delivered;
                    convoy.WheatCargo -= delivered;
                }

                // Reserve enough wheat on the army as provisions before loading excess resources for the village.
                var keepFoodTarget = DesiredArmyProvisions(target, returnTravel);
                if (target.Wheat < keepFoodTarget && target.Loot.Wheat > 0)
                {
                    var reserveWheat = Math.Min(target.Loot.Wheat, (long)Math.Ceiling(keepFoodTarget - target.Wheat));
                    target.Loot -= new Resources(0, 0, 0, reserveWheat);
                    target.Wheat += reserveWheat;
                }

                var convoyCapacity = convoy.Troops.Sum(x => (double)x.Count * x.Unit.CarryCapacity);
                var convoySpace = Math.Max(0, convoyCapacity - convoy.WheatCargo - convoy.Cargo.Total);
                if (target.Loot.Total > 0 && convoySpace > 0)
                {
                    var lootFraction = Math.Min(1.0, convoySpace / (double)target.Loot.Total);
                    var lootTaken = convoySpace >= target.Loot.Total ? target.Loot : target.Loot.Fraction(lootFraction);
                    target.Loot -= lootTaken;
                    convoy.Cargo += lootTaken;
                    var fromCamp = target.CampRadius is not null || target.Order is "Camped" or "Camping";
                    Note(convoy.OwnerId, now, fromCamp
                        ? $"Collection wagon gathered {ResourceText(lootTaken)} from your camp and is returning to the village."
                        : $"Collection wagon gathered {ResourceText(lootTaken)} from your army and is returning to the village.", "camp-loot", targetId: target.Id);
                }
                else if (delivered > 0)
                {
                    Note(convoy.OwnerId, now, $"Supply delivered: {Math.Floor(delivered)} wheat. The convoy is returning.");
                }

                convoy.Returning = true;
            }
            else
            {
                if (home.Besieged) continue;
                if (convoy.Cargo.Total > 0)
                {
                    home.ReceiveCargo(convoy.Cargo);
                    Note(convoy.OwnerId, now, $"Convoy safely returned with camp cargo: {ResourceText(convoy.Cargo)}.", "camp-loot-delivered", targetId: home.Village.Id);
                    convoy.Cargo = new();
                }
                home.Arrive(convoy.Troops, convoy.WheatCargo);
                convoys.Remove(convoy);
            }
        }
    }
    private static double DesiredArmyProvisions(Army army, double travelSeconds) =>
        Math.Min(army.Capacity, Math.Ceiling(army.Upkeep * Math.Max(300, 2 * travelSeconds + 180)));

    private void Supply(Army army, double now)
    {
        if(OasisFoodSource(army) is not null){army.SupplyStatus="Supplied by oasis wheat";return;}
        if (army.Order == "Returning") { army.SupplyStatus = "Returning home"; return; }
        if (convoys.Any(x => x.TargetArmyId == army.Id && !x.Returning)) { army.SupplyStatus = "Convoy in transit"; return; }
        if (now < army.NextSupplyAt) return;
        var home = villages[army.SupplyVillageId];
        if (home.Besieged) { army.SupplyStatus = "Source village blockaded"; return; }
        if (home.Relocating) { army.SupplyStatus = "Village relocating"; return; }
        var wagonId = UnitId(home, UnitRole.Wagon);
        var escortId = UnitId(home, UnitRole.Infantry);
        var speed = GameCatalog.Units[wagonId].Stats.Speed;
        if (army.Escorts > 0) speed = Math.Min(speed, GameCatalog.Units[escortId].Stats.Speed);
        var travel = Distance(home.Village.Position, army.Position) / speed;

        var desiredFood = DesiredArmyProvisions(army, travel);
        var reserveFromLootWheat = army.Wheat < desiredFood ? Math.Min(army.Loot.Wheat, (long)Math.Ceiling(desiredFood - army.Wheat)) : 0L;
        var collectibleLoot = army.Loot - new Resources(0, 0, 0, reserveFromLootWheat);
        var isCamp = army.CampRadius is not null || army.Order is "Camped" or "Camping";
        var campFull = isCamp && army.Loot.Total > 0 && (army.LootSpace <= 50 || (army.Capacity > 0 && (army.TotalWheat + army.Loot.Wood + army.Loot.Iron + army.Loot.Stone) >= army.Capacity * 0.9));
        var overThreshold = army.Order != "Battle" && army.Loot.Total >= 2000 && collectibleLoot.Total > 0;
        var lootFull = overThreshold || campFull;
        var needsFood = army.TotalWheat <= army.Upkeep * (travel + 100);

        if (!army.AutoSupplyEnabled && !lootFull) { army.SupplyStatus = "Manual supply"; return; }
        if (!needsFood && !lootFull) { army.SupplyStatus = "Well supplied"; return; }

        var wagonCapacity = GameCatalog.Units[wagonId].Stats.CarryCapacity;
        var foodWagons = Math.Clamp((int)Math.Ceiling(desiredFood / wagonCapacity), 1, 20);
        var lootWagons = lootFull ? (int)Math.Max(1, Math.Ceiling(Math.Max(1, collectibleLoot.Total) / (double)wagonCapacity)) : 0;
        var wagonsNeeded = army.SupplyWagons == 0 ? Math.Max(foodWagons, lootWagons) : Math.Max(army.SupplyWagons, lootWagons);

        var availWagons = home.Available(wagonId);
        if (availWagons < 1) { army.SupplyStatus = "Waiting for wagons"; army.NextSupplyAt = now + 5; return; }
        if (army.Escorts > 0 && home.Available(escortId) < army.Escorts)
        { army.SupplyStatus = "Waiting for escorts"; army.NextSupplyAt = now + 5; return; }

        var count = Math.Min(wagonsNeeded, availWagons);
        var escortUpkeep = army.Escorts * GameCatalog.Units[escortId].Stats.WheatUpkeep / 60;
        var wagonUpkeep = GameCatalog.Units[wagonId].Stats.WheatUpkeep / 60;
        var convoyTripProvisions = (long)Math.Ceiling((escortUpkeep + count * wagonUpkeep) * (2 * travel + 60));

        var wheatToDeliver = needsFood ? (long)Math.Floor(Math.Min(desiredFood, Math.Max(0.0, Math.Min(count * wagonCapacity - convoyTripProvisions, (double)(home.Resources.Wheat - convoyTripProvisions))))) : 0L;
        var cargo = Math.Min(convoyTripProvisions + wheatToDeliver, home.Resources.Wheat);

        if (cargo < 1) { army.SupplyStatus = "Waiting for wheat"; army.NextSupplyAt = now + 5; return; }

        var requested = new Dictionary<string, int> { [wagonId] = count };
        if (army.Escorts > 0) requested[escortId] = army.Escorts;
        var troops = home.Depart(requested, cargo);
        convoys.Add(new(army.OwnerId, home.Village.Position, home.Village.Id, army.Id, troops, cargo));
        army.NextSupplyAt = now + 10; army.SupplyStatus = "Convoy in transit";
        if (lootFull && !needsFood)
            Note(army.OwnerId, now, $"A collection wagon departed to retrieve loot from your camp ({count} wagon(s)).", "camp-loot", targetId: army.Id);
        else
            Note(army.OwnerId, now, $"A supply convoy departed with {count} wagon(s) and {army.Escorts} escorts.");
    }
    private static readonly UnitRole[] BotCombatRoles =
    [
        UnitRole.HeavyCavalry, UnitRole.Swordsman, UnitRole.Cavalry,
        UnitRole.Spearman, UnitRole.LightCavalry, UnitRole.Infantry
    ];
    private static int HomeCombatTroops(VillageEconomy home) =>
        BotCombatRoles.Sum(role => TryUnitId(home, role) is { } id ? home.Available(id) : 0);

    private readonly Dictionary<Guid, double> botNextTradeAt = [];
    private readonly Dictionary<Guid, double> botNextScoutAt = [];
    private static long ResourceOf(Resources r, ResourceType t) => t switch
    {
        ResourceType.Wood => r.Wood, ResourceType.Iron => r.Iron,
        ResourceType.Stone => r.Stone, ResourceType.Wheat => r.Wheat, _ => 0
    };
    private static readonly UnitRole[] BotCavalryRoles =
    [
        UnitRole.HeavyCavalry, UnitRole.Cavalry, UnitRole.LightCavalry
    ];
    private static int ActiveFieldArmyCount(Army[] ownArmies) =>
        ownArmies.Count(a => !IsScoutOnly(a) && a.Order != "Guarding");

    private Dictionary<string, int>? FinalizeBotFormation(VillageEconomy home, Position destination, Dictionary<string, int> formation)
    {
        var wagonId = UnitId(home, UnitRole.Wagon);
        var availWagons = home.Available(wagonId);
        var maxWagons = Math.Min(availWagons, Math.Clamp((formation.Values.Sum() + 49) / 50, 8, 80));
        if (availWagons > 0)
            formation[wagonId] = Math.Clamp((formation.Values.Sum() + 19) / 20, 1, Math.Min(5, maxWagons));
        var upkeep = formation.Sum(x => x.Value * GameCatalog.Units[x.Key].Stats.WheatUpkeep) / 60;
        var slowest = formation.Min(x => GameCatalog.Units[x.Key].Stats.Speed);
        var carry = formation.Sum(x => (double)x.Value * GameCatalog.Units[x.Key].Stats.CarryCapacity);
        var trip = Distance(home.Village.Position, destination) / Math.Max(.1, slowest * .8);
        var required = (long)Math.Ceiling(upkeep * (trip * 2 + 180));
        while (required > carry && availWagons > 0 && formation.GetValueOrDefault(wagonId) < maxWagons)
        {
            formation[wagonId] = formation.GetValueOrDefault(wagonId) + 1;
            upkeep = formation.Sum(x => x.Value * GameCatalog.Units[x.Key].Stats.WheatUpkeep) / 60;
            carry = formation.Sum(x => (double)x.Value * GameCatalog.Units[x.Key].Stats.CarryCapacity);
            required = (long)Math.Ceiling(upkeep * (trip * 2 + 180));
        }
        return required <= carry && home.Resources.Wheat >= required ? formation : null;
    }
    private Dictionary<string, int>? BuildFastCavalryFormation(VillageEconomy home, Position destination, int maxSoldiers, int reserveAtHome = 4, int minSoldiers = 8)
    {
        var availableCav = BotCavalryRoles.Sum(role => TryUnitId(home, role) is { } id ? home.Available(id) : 0);
        var budget = Math.Min(Math.Min(maxSoldiers, 9500), Math.Max(0, availableCav - reserveAtHome));
        if (budget < minSoldiers) return null;
        var formation = new Dictionary<string, int>();
        var remaining = budget;
        foreach (var role in BotCavalryRoles)
        {
            if (TryUnitId(home, role) is not { } id) continue;
            var count = Math.Min(remaining, home.Available(id));
            if (count > 0) { formation[id] = count; remaining -= count; }
            if (remaining <= 0) break;
        }
        return formation.Values.Sum() >= minSoldiers ? FinalizeBotFormation(home, destination, formation) : null;
    }
    private Dictionary<string, int>? BuildBotFormation(VillageEconomy home, Position destination, int maxSoldiers, bool includeSiege, bool includeGovernor, int reserveAtHome = 0, BotDifficulty difficulty = BotDifficulty.Normal)
    {
        var availableTotal = HomeCombatTroops(home);
        var budget = Math.Min(Math.Min(maxSoldiers, 9500), Math.Max(0, availableTotal - reserveAtHome));
        var floor = difficulty == BotDifficulty.Hard && warTime >= protectionUntil && maxSoldiers >= 28 ? 28 : 8;
        if (budget < floor) return null;
        var minFallback = budget >= 24 ? Math.Max(24, floor) : floor;

        Dictionary<string, int>? TryBuild(int currentBudget, bool withSiege, bool withGovernor)
        {
            var formation = new Dictionary<string, int>();
            var remaining = currentBudget;
            foreach (var role in BotCombatRoles)
            {
                if (TryUnitId(home, role) is not { } id) continue;
                var count = Math.Min(remaining, home.Available(id));
                if (count > 0) { formation[id] = count; remaining -= count; }
                if (remaining <= 0) break;
            }
            if (formation.Values.Sum() < 8) return null;
            if (withSiege)
            {
                var maxEngines = difficulty == BotDifficulty.Hard ? 10 : 3;
                foreach (var role in new[] { UnitRole.Ram, UnitRole.Catapult })
                {
                    if (TryUnitId(home, role) is not { } id) continue;
                    if (home.Available(id) > 0) formation[id] = Math.Min(maxEngines, home.Available(id));
                }
            }
            if (withGovernor)
            {
                var govId = UnitId(home, UnitRole.Governor);
                if (home.Available(govId) > 0) formation[govId] = 1;
            }
            return FinalizeBotFormation(home, destination, formation);
        }

        while (budget >= minFallback)
        {
            var finalized = TryBuild(budget, includeSiege, includeGovernor)
                ?? (includeSiege && (availableTotal >= 140 || home.FoodShortage) ? TryBuild(budget, false, includeGovernor) : null);
            if (finalized is not null) return finalized;
            if (budget <= minFallback) break;
            budget = Math.Min(budget - 1, Math.Max(minFallback, (int)(budget * 0.72)));
        }

        // If wheat/wagons are too scarce for heavy or large formations, try a smaller low-upkeep expedition down to 10 soldiers.
        var lightBudget = Math.Min(Math.Min(maxSoldiers, 160), Math.Max(0, availableTotal - reserveAtHome));
        var lightFloor = Math.Min(minFallback, 10);
        while (lightBudget >= lightFloor)
        {
            var form = new Dictionary<string, int>();
            var rem = lightBudget;
            foreach (var role in BotCombatRoles.Reverse())
            {
                if (TryUnitId(home, role) is not { } id) continue;
                var cnt = Math.Min(rem, home.Available(id));
                if (cnt > 0) { form[id] = cnt; rem -= cnt; }
                if (rem <= 0) break;
            }
            if (form.Values.Sum() >= lightFloor && FinalizeBotFormation(home, destination, form) is { } lightOk)
                return lightOk;
            if (lightBudget <= lightFloor) break;
            lightBudget = Math.Min(lightBudget - 1, Math.Max(lightFloor, (int)(lightBudget * 0.65)));
        }
        return null;
    }

    private ResourceOasis? BestOasisForBot(Guid actor, VillageEconomy home, Position from, int armyFighters,
        Guid? focusEnemyId = null, VillageEconomy? focusCity = null, BotDifficulty difficulty = BotDifficulty.Normal)
    {
        var ownTargeted = armies.Where(a => a.OwnerId == actor && a.Order is "Occupying" or "Blockading" or "Burning" && a.TargetId is not null)
            .Select(a => a.TargetId!.Value).ToHashSet();
        var alliedClaimingNeutral = armies.Where(a => a.OwnerId != actor && CampAllied(actor, a.OwnerId)
                && a.Order == "Occupying" && a.OasisMode == OasisMode.Capture && a.TargetId is not null)
            .Select(a => a.TargetId!.Value).ToHashSet();
        var alliedBurningUndefended = armies.Where(a => a.OwnerId != actor && CampAllied(actor, a.OwnerId)
                && a.Order is "Burning" or "Blockading" && a.TargetId is not null)
            .Select(a => a.TargetId!.Value).ToHashSet();
        var wheatLow = home.FoodShortage || home.Resources.Wheat < 500;
        return oases.Where(o => o.OwnerId != actor && (o.OwnerId is null || !CampAllied(actor, o.OwnerId.Value))
                && !ownTargeted.Contains(o.Id)
                && (o.OwnerId is not null || !alliedClaimingNeutral.Contains(o.Id))
                && (o.OwnerId is null || !alliedBurningUndefended.Contains(o.Id) || Guards(o.Id, actor).Any())
                && (o.OwnerId is null || warTime >= protectionUntil))
            .Where(o => o.BurnedUntil <= warTime)
            .Where(o => o.OwnerId is null
                || armyFighters >= Math.Max(10, Guards(o.Id, actor).Sum(Fighters) + o.WallLevel * 6 + 4)
                || (difficulty == BotDifficulty.Hard && armyFighters >= 10
                    && ((o.OwnerId is { } oid && humanOwners.Contains(oid))
                        || (focusEnemyId == o.OwnerId && !Guards(o.Id, actor).Any()))))
            .OrderBy(o =>
            {
                var dist = Distance(from, o.Position);
                var unclaimedBonus = o.OwnerId is null ? (difficulty == BotDifficulty.Hard && warTime < protectionUntil ? -1200 : -600) : 0;
                var levelBonus = o.Level * -120;
                var wheatBonus = wheatLow && o.Resource == ResourceType.Wheat ? -220 : 0;
                var humanBonus = difficulty == BotDifficulty.Hard && o.OwnerId is { } hId && humanOwners.Contains(hId) ? -1400 : 0;
                var focusBonus = focusEnemyId is { } fe && o.OwnerId == fe ? (difficulty == BotDifficulty.Hard ? -950 : -600) : 0;
                var nearFocusBonus = o.OwnerId is not null && focusCity is not null && Distance(o.Position, focusCity.Village.Position) < 1200
                    ? (difficulty == BotDifficulty.Hard ? -450 : -250) : 0;
                var defensePenalty = o.OwnerId is null ? 0 : (Guards(o.Id, actor).Sum(Fighters) + o.WallLevel * 6) * 8;
                return dist + unclaimedBonus + levelBonus + wheatBonus + humanBonus + focusBonus + nearFocusBonus + defensePenalty;
            })
            .FirstOrDefault();
    }

    private void BotManageTradeAndAid(Guid actor, VillageEconomy home, Army[] ownArmies, IGameCommands commands, BotDifficulty difficulty)
    {
        if (home.Besieged || home.BuildingLevel(BuildingType.Market) < 1) return;

        const int MaxBotTradeConvoys = 3;
        var activeTrades = convoys.Count(c => c.Kind is ConvoyKind.Trade or ConvoyKind.Shipment && c.OwnerId == actor);
        var ownOffer = marketOffers.FirstOrDefault(o => o.VillageId == home.Village.Id);
        if (ownOffer is not null)
        {
            var askStock = ResourceOf(home.Resources, ownOffer.AskResource);
            if (activeTrades >= MaxBotTradeConvoys || warTime - ownOffer.CreatedAt > 90 || askStock >= home.Capacity * 0.6 || (home.Available(UnitId(home, UnitRole.Wagon)) == 0 && ownArmies.Length > 0))
            {
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.CancelMarketOffer, ownOffer.Id));
                botNextTradeAt[actor] = warTime + 30;
                return;
            }
        }

        // At most three active trade or shipment convoys per bot at a time, plus cooldown, to prevent caravan spam.
        if (activeTrades >= MaxBotTradeConvoys) return;
        if (warTime < botNextTradeAt.GetValueOrDefault(actor)) return;

        var availWagons = home.Available(UnitId(home, UnitRole.Wagon));
        var reserveWagons = ownArmies.Length > 0 ? 1 : 0;
        if (availWagons <= reserveWagons) return;

        var hasAllies = economies.Keys.Any(id => id != actor && CampAllied(actor, id) && Primary(id) is not null);
        var stocks = new[]
        {
            (Type: ResourceType.Wood, Value: home.Resources.Wood),
            (Type: ResourceType.Iron, Value: home.Resources.Iron),
            (Type: ResourceType.Stone, Value: home.Resources.Stone),
            (Type: ResourceType.Wheat, Value: home.Resources.Wheat)
        };
        var scarce = stocks.MinBy(x => x.Value);
        var surplus = stocks.MaxBy(x => x.Value);

        var openDeal = marketOffers.Where(o => o.OwnerId != actor
                && (!hasAllies || CampAllied(actor, o.OwnerId))
                && (warTime < protectionUntil || CampAllied(actor, o.OwnerId) || (o.OfferAmount >= 1200 && o.OfferAmount >= o.AskAmount))
                && o.OfferAmount >= (CampAllied(actor, o.OwnerId) ? 500L : 1200L)
                && o.OfferAmount >= (long)(o.AskAmount * 0.85)
                && home.Resources.Wheat >= (o.OfferResource == ResourceType.Wheat ? 5 : 200)
                && convoys.Count(c => c.Kind is ConvoyKind.Trade or ConvoyKind.Shipment && c.OwnerId == o.OwnerId) < MaxBotTradeConvoys
                && villages.TryGetValue(o.VillageId, out var seller) && !seller.Ruined && !seller.Besieged && seller.Resources.Wheat >= 100)
            .Where(o =>
            {
                var wantStock = stocks.Single(s => s.Type == o.OfferResource).Value;
                var giveStock = stocks.Single(s => s.Type == o.AskResource).Value;
                var neededWagons = (int)Math.Ceiling(o.AskAmount / 4800.0);
                return wantStock < Math.Max(1200L, (long)(home.Capacity * 0.40))
                    && wantStock + o.OfferAmount <= home.Capacity
                    && giveStock >= o.AskAmount + Math.Max(700L, (long)(home.Capacity * 0.22))
                    && giveStock > wantStock + 600
                    && availWagons - reserveWagons >= Math.Max(1, neededWagons);
            })
            .OrderBy(o => CampAllied(actor, o.OwnerId) ? 0 : 1)
            .ThenBy(o => o.OfferResource == scarce.Type ? 0 : 1)
            .ThenByDescending(o => o.OfferAmount)
            .FirstOrDefault();

        if (openDeal is not null)
        {
            if (ownOffer is not null)
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.CancelMarketOffer, ownOffer.Id));
            if (commands.Execute(actor, new(Guid.NewGuid(), CommandKind.AcceptMarketOffer, home.Village.Id, DestinationId: openDeal.Id)).Accepted)
                botNextTradeAt[actor] = warTime + 45;
            return;
        }

        if (ownOffer is null)
        {
            var reserve = Math.Max(600L, (long)(home.Capacity * 0.20));
            var minBulk = Math.Clamp((long)(home.Capacity * 0.25), 1200L, 3500L);
            var maxUsableWagons = Math.Clamp(availWagons - reserveWagons, 1, 3);
            var maxCarry = maxUsableWagons * 4800L;
            var minPostWheat = scarce.Type == ResourceType.Wheat ? 20L : 300L;
            if (scarce.Value < Math.Max(600L, (long)(home.Capacity * 0.22)) && surplus.Value >= reserve + minBulk && scarce.Type != surplus.Type && home.Resources.Wheat >= minPostWheat)
            {
                var offerAmount = (Math.Min(surplus.Value - reserve, maxCarry) / 100) * 100;
                var maxRoom = Math.Max(0L, home.Capacity - scarce.Value - 200);
                var askAmount = (Math.Min((long)(offerAmount * 0.9), maxRoom) / 100) * 100;
                if (offerAmount >= minBulk && askAmount >= 1000 && (surplus.Type != ResourceType.Wheat || home.Resources.Wheat >= offerAmount + 300))
                {
                    if (commands.Execute(actor, new(Guid.NewGuid(), CommandKind.PostMarketOffer, home.Village.Id,
                        OfferResource: surplus.Type, OfferAmount: offerAmount, AskResource: scarce.Type, AskAmount: askAmount)).Accepted)
                    {
                        botNextTradeAt[actor] = warTime + 45;
                        return;
                    }
                }
            }
        }

        // Rare bulk wheat emergency shipment to a starving ally
        if (difficulty >= BotDifficulty.Normal && hasAllies && availWagons >= 2 + reserveWagons && !home.FoodShortage
            && home.Resources.Wheat >= Math.Max(3200L, (long)(home.Capacity * 0.50)))
        {
            var starvingAlly = villages.Values
                .Where(v => !v.Ruined && !v.Besieged && v.Village.OwnerId != actor && CampAllied(actor, v.Village.OwnerId)
                    && (v.FoodShortage || v.Resources.Wheat < 200) && v.Resources.Wheat + 1500 <= v.Capacity)
                .OrderBy(v => v.Resources.Wheat)
                .FirstOrDefault();
            if (starvingAlly is not null)
            {
                var keepWheat = Math.Max(1800L, (long)(home.Capacity * 0.30));
                var sendWheat = Math.Clamp(((home.Resources.Wheat - keepWheat) / 100) * 100, 1500L, Math.Min(4500L, starvingAlly.Capacity - starvingAlly.Resources.Wheat));
                if (sendWheat >= 1500 && commands.Execute(actor, new(Guid.NewGuid(), CommandKind.SendResources, home.Village.Id,
                    DestinationId: starvingAlly.Village.Id, Shipment: new(0, 0, 0, sendWheat))).Accepted)
                {
                    botNextTradeAt[actor] = warTime + 90;
                }
            }
        }
    }

    private bool TryBotMergeArmies(Guid actor, VillageEconomy home, Army[] ownArmies, IGameCommands commands, BotDifficulty difficulty, int maxArmies)
    {
        if (ownArmies.Length < 2) return false;

        // 1. Consolidate duplicate Guarding detachments stationed at the same village or oasis
        var duplicateGuard = ownArmies
            .Where(a => a.Order == "Guarding" && a.DefendingSiteId is not null && !Engaged(a) && !a.Routing && !SiteUnderThreat(a.DefendingSiteId))
            .GroupBy(a => a.DefendingSiteId!.Value)
            .FirstOrDefault(g => g.Count() >= 2);
        if (duplicateGuard is not null)
        {
            var groupIds = duplicateGuard.Select(a => a.Id).ToArray();
            if (commands.Execute(actor, new(Guid.NewGuid(), CommandKind.MergeArmies, groupIds[0], TargetIds: groupIds)).Accepted)
                return true;
        }

        // 2. Consolidate fragmented or nearby Idle field armies into a stronger cohesive force instead of suiciding small squads
        if (home.Besieged || SiteUnderThreat(home.Village.Id)
            || sieges.Any(s => s.Status == "Active" && CampAllied(actor, s.DefenderOwner)))
            return false;
        var maxRange = difficulty == BotDifficulty.Hard ? 1350.0 : difficulty == BotDifficulty.Normal ? 550.0 : 250.0;
        var idles = ownArmies
            .Where(a => a.Order == "Idle" && !Engaged(a) && !a.Routing && !IsScoutOnly(a) && Fighters(a) > 0)
            .ToArray();
        if (idles.Length < 2) return false;

        Army? bestA = null;
        Army? bestB = null;
        var bestDist = double.MaxValue;
        for (var i = 0; i < idles.Length; i++)
        {
            for (var j = i + 1; j < idles.Length; j++)
            {
                var a = idles[i];
                var b = idles[j];
                var dist = Distance(a.Position, b.Position);
                if (dist > maxRange) continue;
                var shouldCombine = Fighters(a) < 26 || Fighters(b) < 26
                    || ownArmies.Length >= maxArmies - 1
                    || (difficulty == BotDifficulty.Hard && Math.Min(Fighters(a), Fighters(b)) < 36);
                if (shouldCombine && dist < bestDist)
                {
                    bestDist = dist;
                    bestA = a;
                    bestB = b;
                }
            }
        }
        if (bestA is not null && bestB is not null)
            return commands.Execute(actor, new(Guid.NewGuid(), CommandKind.MergeArmies, bestA.Id, TargetIds: [bestA.Id, bestB.Id])).Accepted;

        return false;
    }

    private void TryBotScout(Guid actor, VillageEconomy home, Army[] ownArmies, IGameCommands commands, BotDifficulty difficulty)
    {
        if (difficulty == BotDifficulty.Easy || (difficulty != BotDifficulty.Hard && warTime < protectionUntil * 0.4)) return;
        if (warTime < botNextScoutAt.GetValueOrDefault(actor)) return;
        if (ownArmies.Any(a => a.Order == "Scouting")) return;

        var existingScoutArmy = ownArmies.FirstOrDefault(a => a.Order == "Idle" && !Engaged(a) && !a.Routing && IsScoutOnly(a) && Fighters(a) >= 1);
        Guid? scoutTargetId = null;

        var maxScoutDist = difficulty == BotDifficulty.Hard ? double.MaxValue : 800.0;
        var nearbyUnscoutedEnemy = armies
            .Where(a => a.OwnerId != actor && !CampAllied(actor, a.OwnerId) && !a.Routing && !IsScoutOnly(a)
                && (difficulty == BotDifficulty.Hard || Distance(a.Position, home.Village.Position) <= maxScoutDist)
                && !IsScoutedBy(actor, a.Id))
            .OrderByDescending(a => difficulty == BotDifficulty.Hard && humanOwners.Contains(a.OwnerId) ? 1 : 0)
            .ThenBy(a => Distance(a.Position, home.Village.Position))
            .FirstOrDefault();
        if (nearbyUnscoutedEnemy is not null)
        {
            scoutTargetId = nearbyUnscoutedEnemy.Id;
        }
        else if (difficulty == BotDifficulty.Hard && convoys.FirstOrDefault(c => c.OwnerId != actor && !CampAllied(actor, c.OwnerId) && humanOwners.Contains(c.OwnerId)) is { } humanConvoy)
        {
            var convoyOwnerCity = villages.Values.FirstOrDefault(v => !v.Ruined && v.Village.OwnerId == humanConvoy.OwnerId);
            if (convoyOwnerCity is not null && !IsScoutedBy(actor, convoyOwnerCity.Village.Id))
                scoutTargetId = convoyOwnerCity.Village.Id;
        }
        if (scoutTargetId is null)
        {
            var focus = ResolveTeamFocusTarget(actor, home);
            if (focus?.City is { } fc && !IsScoutedBy(actor, fc.Village.Id))
                scoutTargetId = fc.Village.Id;
        }
        if (scoutTargetId is not { } targetId) return;

        var scoutInterval = difficulty == BotDifficulty.Hard ? 40 : 60;
        if (existingScoutArmy is not null)
        {
            if (commands.Execute(actor, new(Guid.NewGuid(), CommandKind.Scout, existingScoutArmy.Id, DestinationId: targetId)).Accepted)
                botNextScoutAt[actor] = warTime + scoutInterval;
            return;
        }

        if (ownArmies.Count(a => a.Order != "Guarding") >= 12 || TryUnitId(home, UnitRole.Scout) is not { } scoutId) return;
        var sendCount = Math.Min(3, home.Available(scoutId));
        var minScouts = difficulty == BotDifficulty.Hard ? 1 : 2;
        if (sendCount < minScouts || home.Resources.Wheat < 200) return;

        var formation = new Dictionary<string, int> { [scoutId] = sendCount };
        if (commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
            sendCount, false, home.Village.Position.X, home.Village.Position.Y, scoutId, 0, 0, Formation: formation)).Accepted)
        {
            var spawned = armies.LastOrDefault(a => a.OwnerId == actor && a.Order == "Idle" && IsScoutOnly(a));
            if (spawned is not null && commands.Execute(actor, new(Guid.NewGuid(), CommandKind.Scout, spawned.Id, DestinationId: targetId)).Accepted)
                botNextScoutAt[actor] = warTime + scoutInterval;
        }
    }

    internal void BotAct(Guid actor, IGameCommands commands, BotPersonality personality = BotPersonality.Economic, BotDifficulty difficulty = BotDifficulty.Normal)
    {
        var home = Primary(actor);
        if (home is null) return;
        foreach (var extra in villages.Values.Where(v => !v.Ruined && v.Village.OwnerId == actor && v != home))
        {
            BotController.ActEconomy(commands, actor, extra.Snapshot(), personality, difficulty, () => extra.Snapshot());
            // Project surplus resources and troops from secondary conquered settlements toward capital/front
            if (difficulty != BotDifficulty.Easy && !extra.Besieged && extra.Resources.Total > extra.Capacity * 0.58
                && extra.Available(UnitId(extra, UnitRole.Wagon)) >= 2 && convoys.Count(c => c.OwnerId == actor && c.Kind is ConvoyKind.Trade or ConvoyKind.Shipment) < 3)
            {
                var ship = new Resources(Math.Max(0, extra.Resources.Wood - 600), Math.Max(0, extra.Resources.Iron - 600),
                    Math.Max(0, extra.Resources.Stone - 600), Math.Max(0, extra.Resources.Wheat - 900));
                if (ship.Total >= 1200 && home.Resources.Total + ship.Total <= home.Capacity * 1.1)
                    commands.Execute(actor, new(Guid.NewGuid(), CommandKind.SendResources, extra.Village.Id, DestinationId: home.Village.Id, Shipment: ship));
            }
            if (difficulty != BotDifficulty.Easy && warTime >= protectionUntil && HomeCombatTroops(extra) >= (difficulty == BotDifficulty.Hard ? 36 : 20)
                && armies.Count(x => x.OwnerId == actor && !IsScoutOnly(x) && x.Order != "Guarding") < (difficulty == BotDifficulty.Hard ? 5 : 8))
            {
                var reinforcement = BuildBotFormation(extra, home.Village.Position, difficulty == BotDifficulty.Hard ? Math.Max(55, (int)(HomeCombatTroops(extra) * 0.7)) : 35, false, false, 8, difficulty);
                if (reinforcement is not null)
                    commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DispatchArmy, extra.Village.Id,
                        reinforcement.Values.Sum(), true, home.Village.Position.X, home.Village.Position.Y,
                        UnitId(extra, UnitRole.Infantry), 0, 2, home.Village.Id, Reinforcement: true, Formation: reinforcement));
            }
        }
        var economy = home.Snapshot();
        var wheatNet = economy.Production.FirstOrDefault(p => p.Resource == "Wheat")?.NetPerMinute ?? 0;
        var homeTroops = HomeCombatTroops(home);
        var ownArmies = armies.Where(x => x.OwnerId == actor).ToArray();
        var desiredWagons = difficulty == BotDifficulty.Hard
            ? Math.Clamp(4 + home.OwnedOases * 2 + ownArmies.Length, 4, 14)
            : Math.Clamp(3 + home.OwnedOases * 2 + ownArmies.Length, 3, 9);
        var maxSiegeEngines = difficulty == BotDifficulty.Hard ? 8 : personality == BotPersonality.Aggressive ? 3 : 2;
        bool LaneHasRoom(string lane) => difficulty == BotDifficulty.Hard
            ? economy.Jobs.Count(x => x.Lane == lane) < 4
            : !economy.Jobs.Any(x => x.Lane == lane);

        void QueueSpecial(UnitRole role, int count)
        {
            if (role != UnitRole.Wagon && (home.FoodShortage || (homeTroops >= 200 && wheatNet < 0))) return;
            if (TryUnitId(home, role) is not { } unitId) return;
            var target = economy.Targets.FirstOrDefault(x => x.UnitId == unitId && x.Unlocked);
            var wheatBuffer = difficulty == BotDifficulty.Hard ? (homeTroops >= 140 ? Math.Max(120L, Math.Min(3500L, homeTroops * 3L)) : 120L) : 0L;
            if (target?.TrainingCost is { } unitCost
                && economy.Resources.Covers(new(unitCost.Wood * count, unitCost.Iron * count, unitCost.Stone * count, unitCost.Wheat * count))
                && economy.Resources.Wheat >= unitCost.Wheat * count + wheatBuffer)
            {
                if (commands.Execute(actor, new(Guid.NewGuid(), CommandKind.Train, target.Id, Quantity: count)).Accepted)
                    economy = home.Snapshot();
            }
        }

        if (difficulty == BotDifficulty.Hard)
        {
            if ((warTime > protectionUntil * .2 || homeTroops >= 14) && LaneHasRoom("SiegeWorkshop"))
            {
                if (home.BuildingLevel(BuildingType.SiegeWorkshop) >= 3 && home.Available(UnitId(home, UnitRole.Catapult)) < maxSiegeEngines)
                    QueueSpecial(UnitRole.Catapult, 1);
                else if (home.Available(UnitId(home, UnitRole.Ram)) < 2)
                    QueueSpecial(UnitRole.Ram, 1);
            }
            if (home.BuildingLevel(BuildingType.MainHall) >= 5 && home.Available(UnitId(home, UnitRole.Governor)) < 2
                && homeTroops >= 20 && LaneHasRoom("MainHall"))
                QueueSpecial(UnitRole.Governor, 1);
            if (home.Available(UnitId(home, UnitRole.Wagon)) + home.ReservedTradeWagons < desiredWagons && LaneHasRoom("Stable"))
                QueueSpecial(UnitRole.Wagon, 2);
            if (homeTroops >= 6 && TryUnitId(home, UnitRole.Scout) is { } scoutUnitId
                && home.Available(scoutUnitId) < 5
                && economy.Targets.FirstOrDefault(x => x.UnitId == scoutUnitId && x.Unlocked) is { } scoutTarget
                && scoutTarget.TrainingBuilding is { } scoutLane && LaneHasRoom(scoutLane))
                QueueSpecial(UnitRole.Scout, 2);
        }
        else
        {
            UnitRole? neededRole = null;
            var batch = 1;
            if (home.Available(UnitId(home, UnitRole.Wagon)) + home.ReservedTradeWagons < desiredWagons && LaneHasRoom("Stable"))
            { neededRole = UnitRole.Wagon; batch = 2; }
            else if (warTime > protectionUntil * .5 && homeTroops >= 14 && LaneHasRoom("SiegeWorkshop"))
            {
                if (home.BuildingLevel(BuildingType.SiegeWorkshop) >= 3 && home.Available(UnitId(home, UnitRole.Catapult)) < maxSiegeEngines)
                    neededRole = UnitId(home, UnitRole.Catapult) is not null ? UnitRole.Catapult : null;
                else if (home.Available(UnitId(home, UnitRole.Ram)) < maxSiegeEngines)
                    neededRole = UnitRole.Ram;
            }
            else if (personality != BotPersonality.Defensive && warTime > protectionUntil * .5
                && home.Available(UnitId(home, UnitRole.Governor)) == 0 && homeTroops >= 35 && LaneHasRoom("MainHall"))
                neededRole = UnitRole.Governor;
            else if (difficulty >= BotDifficulty.Normal && warTime > protectionUntil * .25 && homeTroops >= 6
                && TryUnitId(home, UnitRole.Scout) is { } scoutUnitId
                && home.Available(scoutUnitId) < 2
                && economy.Targets.FirstOrDefault(x => x.UnitId == scoutUnitId && x.Unlocked) is { } scoutTarget
                && scoutTarget.TrainingBuilding is { } scoutLane
                && LaneHasRoom(scoutLane))
            { neededRole = UnitRole.Scout; batch = 1; }

            if (neededRole is { } needed)
                QueueSpecial(needed, batch);
        }

        var oasisWheatFloor = difficulty == BotDifficulty.Hard && homeTroops >= 140 ? Math.Max(600L, Math.Min(4000L, homeTroops * 3L)) : 600L;
        if (!home.Besieged && home.Resources.Wood > 800 && home.Resources.Stone > 800 && home.Resources.Iron > 600 && home.Resources.Wheat > oasisWheatFloor)
        {
            var maxLvl = difficulty == BotDifficulty.Hard ? 5 : personality == BotPersonality.Economic ? 4 : 3;
            var upgradeTarget = oases.FirstOrDefault(o => o.OwnerId == actor && o.VillageId == home.Village.Id && !OasisUnderAttack(o)
                && o.BurnedUntil <= warTime && o.Level < maxLvl && o.LevelUpgradeCost is { } lc && home.Resources.Covers(lc)
                && home.Resources.Wheat >= lc.Wheat + (difficulty == BotDifficulty.Hard && homeTroops >= 140 ? oasisWheatFloor - 600L : 0L));
            if (upgradeTarget is not null)
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.UpgradeOasisLevel, upgradeTarget.Id));
        }
        if (!home.Besieged && home.Resources.Wood > 1100 && home.Resources.Stone > 1100 && home.Resources.Iron > 800 && home.Resources.Wheat >= (difficulty == BotDifficulty.Hard && homeTroops >= 140 ? oasisWheatFloor : 0L))
        {
            var maxWall = difficulty == BotDifficulty.Hard ? 4 : personality == BotPersonality.Defensive ? 4 : personality == BotPersonality.Economic ? 3 : 2;
            var fortify = oases.FirstOrDefault(o => o.OwnerId == actor && o.VillageId == home.Village.Id && !OasisUnderAttack(o)
                && o.WallLevel < maxWall && o.WallUpgradeCost is { } c && home.Resources.Covers(c)
                && home.Resources.Wheat >= c.Wheat + (difficulty == BotDifficulty.Hard && homeTroops >= 140 ? oasisWheatFloor - 600L : 0L));
            if (fortify is not null)
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.UpgradeOasisWall, fortify.Id));
        }

        BotManageTradeAndAid(actor, home, ownArmies, commands, difficulty);
        TryBotScout(actor, home, ownArmies, commands, difficulty);

        var fieldCount = ActiveFieldArmyCount(ownArmies);
        var maxArmies = difficulty == BotDifficulty.Hard ? (warTime < protectionUntil ? 3 : 6) : warTime < protectionUntil ? 4 : personality == BotPersonality.Aggressive ? 5 : 4;
        if (TryBotMergeArmies(actor, home, ownArmies, commands, difficulty, maxArmies)) return;

        bool TryRelieveWheatDeadlock()
        {
            var currentHome = HomeCombatTroops(home);
            var currentNet = home.Snapshot().Production.FirstOrDefault(p => p.Resource == "Wheat")?.NetPerMinute ?? 0;
            if (home.Besieged || currentHome < 120) return false;
            if (!home.FoodShortage && !(currentNet < 60 && home.Resources.Wheat < 500)) return false;

            // Trim excess upkeep (including idle surplus siege engines and higher-upkeep stacks first) to reach +240 wheat/min so remaining armies can quickly afford to march.
            var upkeepToCut = Math.Max(60d, 240d - currentNet);
            var cutFormation = new Dictionary<string, int>();
            var remainingUpkeep = upkeepToCut;
            foreach (var role in new[] { UnitRole.Ram, UnitRole.Catapult })
            {
                if (TryUnitId(home, role) is not { } unitId) continue;
                var excess = Math.Max(0, home.Available(unitId) - 2);
                if (excess <= 0) continue;
                var unitUpkeep = Math.Max(1.0, GameCatalog.Units[unitId].Stats.WheatUpkeep);
                var count = Math.Min(excess, (int)Math.Ceiling(remainingUpkeep / unitUpkeep));
                if (count > 0) { cutFormation[unitId] = count; remainingUpkeep -= count * unitUpkeep; }
                if (remainingUpkeep <= 0) break;
            }
            var remainingAllowedCut = Math.Max(20, currentHome - 60);
            foreach (var role in BotCombatRoles)
            {
                if (remainingUpkeep <= 0 || remainingAllowedCut <= 0) break;
                if (TryUnitId(home, role) is not { } unitId) continue;
                var avail = home.Available(unitId);
                if (avail <= 0) continue;
                var unitUpkeep = Math.Max(0.5, GameCatalog.Units[unitId].Stats.WheatUpkeep);
                var wanted = (int)Math.Ceiling(remainingUpkeep / unitUpkeep);
                var count = Math.Min(avail, Math.Min(wanted, remainingAllowedCut));
                if (count > 0)
                {
                    cutFormation[unitId] = count;
                    remainingUpkeep -= count * unitUpkeep;
                    remainingAllowedCut -= count;
                }
            }
            if (cutFormation.Count == 0) return false;
            return commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DisbandTroops, home.Village.Id, Formation: cutFormation)).Accepted;
        }

        if (homeTroops >= 200 && home.FoodShortage && TryRelieveWheatDeadlock()) return;

        var dispatchedOrActed = WarBotAct(actor, commands, personality, difficulty);
        if (HomeCombatTroops(home) >= homeTroops && TryRelieveWheatDeadlock()) return;
        if (dispatchedOrActed) return;

        var weakCutoff = difficulty == BotDifficulty.Hard && fieldCount >= 2 ? 28 : 16;
        var weakArmy = ownArmies.FirstOrDefault(a => (a.Order == "Idle" || (difficulty != BotDifficulty.Hard && a.Order == "Guarding")) && !Engaged(a) && !a.Routing && !SiteUnderThreat(a.DefendingSiteId)
            && warTime > protectionUntil && Fighters(a) < weakCutoff && homeTroops >= 24);
        if (weakArmy is not null)
        { commands.Execute(actor, new(Guid.NewGuid(), CommandKind.RecallArmy, weakArmy.Id)); return; }

        var availableArmy = ownArmies.FirstOrDefault(a => IsReassignableArmy(actor, a, personality));

        var burningOasis = oases.FirstOrDefault(o => o.OwnerId is { } owner && CampAllied(actor, owner)
            && (o.BurningSeconds > 0 || armies.Any(a => a.TargetId == o.Id && a.Order == "Burning" && !CampAllied(actor, a.OwnerId))));
        if (burningOasis is not null)
        {
            var arsonist = armies.FirstOrDefault(a => a.TargetId == burningOasis.Id && !CampAllied(actor, a.OwnerId)
                && a.Order is "Burning" or "Occupying" && CanAttackIntoActiveBattle(actor, a));
            if (arsonist is not null && availableArmy is not null && Fighters(availableArmy) >= 6)
            {
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.AttackArmy, availableArmy.Id, DestinationId: arsonist.Id));
                return;
            }
        }

        var focus = ResolveTeamFocusTarget(actor, home);
        var candidateFighters = availableArmy is not null ? Fighters(availableArmy) : Math.Max(0, homeTroops - (warTime >= protectionUntil ? 8 : 0));
        var oasis = BestOasisForBot(actor, home, availableArmy?.Position ?? home.Village.Position, candidateFighters, focus?.EnemyId, focus?.City, difficulty);
        if (oasis is not null)
        {
            var oasisMode = OasisMode.Capture;
            if (oasis.OwnerId is not null && warTime >= protectionUntil)
            {
                var armyPower = availableArmy is not null ? Fighters(availableArmy) : candidateFighters;
                var canBreak = armyPower >= Math.Max(10, Guards(oasis.Id, actor).Sum(Fighters) + oasis.WallLevel * 6 + 4);
                var belongsToFocus = focus?.EnemyId is { } fe && oasis.OwnerId == fe;
                var nearFocus = focus?.City is { } fc && Distance(oasis.Position, fc.Village.Position) < 1200;
                var distFromHome = Distance(oasis.Position, home.Village.Position);
                // Burning nearby oases starves the bot itself; capture near home/allies and only burn distant enemy oases.
                var veryFarFromFriendly = distFromHome >= 1800
                    && !villages.Values.Any(v => !v.Ruined
                        && ((v.Village.OwnerId == actor && Distance(oasis.Position, v.Village.Position) < 1800)
                            || (v.Village.OwnerId != actor && CampAllied(actor, v.Village.OwnerId) && Distance(oasis.Position, v.Village.Position) < 1400)));
                var enemyCity = villages.Values.FirstOrDefault(v => !v.Ruined && v.Village.OwnerId == oasis.OwnerId);
                var inEnemyTerritory = enemyCity is null || distFromHome > Distance(oasis.Position, enemyCity.Village.Position) + 250;
                var isHumanOasis = oasis.OwnerId is { } oOwner && humanOwners.Contains(oOwner);
                var canBurnHere = (isHumanOasis || veryFarFromFriendly || (difficulty == BotDifficulty.Hard && distFromHome >= 550
                    && !villages.Values.Any(v => !v.Ruined && CampAllied(actor, v.Village.OwnerId) && Distance(oasis.Position, v.Village.Position) < 550))) && (isHumanOasis || inEnemyTerritory);
                var shouldBurn = canBurnHere
                    && (difficulty == BotDifficulty.Hard
                        || (personality == BotPersonality.Aggressive && (belongsToFocus || nearFocus || oasis.Level >= 1))
                        || (belongsToFocus && home.OwnedOases >= 2));
                oasisMode = !canBreak && difficulty == BotDifficulty.Hard ? OasisMode.Blockade : shouldBurn ? OasisMode.Burn : OasisMode.Capture;
            }
            var minExistingOasisFighters = oasis.OwnerId is null ? 1 : oasisMode == OasisMode.Blockade ? 10 : 16;
            if (availableArmy is not null && Fighters(availableArmy) >= minExistingOasisFighters)
            {
                var cmdKind = oasisMode == OasisMode.Burn ? CommandKind.BurnOasis : oasisMode == OasisMode.Blockade ? CommandKind.BesiegeOasis : CommandKind.CaptureOasis;
                commands.Execute(actor, new(Guid.NewGuid(), cmdKind, availableArmy.Id, DestinationId: oasis.Id));
                return;
            }
            var reserve = warTime < protectionUntil ? 0 : personality == BotPersonality.Defensive ? 10 : 4;
            var minTroops = oasis.OwnerId is null
                ? (warTime < protectionUntil ? 8 : difficulty == BotDifficulty.Hard ? 28 + reserve : 10)
                : (warTime < protectionUntil ? 12 : difficulty == BotDifficulty.Hard ? 28 + reserve : oasisMode == OasisMode.Blockade ? 12 : 18);
            if (fieldCount < maxArmies && ownArmies.Count(a => a.Order != "Guarding") < 12 && homeTroops >= minTroops)
            {
                var maxDispatch = oasis.OwnerId is null
                    ? (warTime < protectionUntil ? 16 : difficulty == BotDifficulty.Hard ? Math.Clamp((int)(homeTroops * 0.4), 34, 90) : 16)
                    : (warTime < protectionUntil ? 20 : difficulty == BotDifficulty.Hard ? Math.Max(75, (int)(homeTroops * 0.55)) : 60);
                var needSiegeForOasis = oasis.OwnerId is not null && oasis.WallLevel > 0 && warTime >= protectionUntil;
                var formation = BuildBotFormation(home, oasis.Position, maxDispatch, needSiegeForOasis, oasisMode == OasisMode.Capture && personality == BotPersonality.Aggressive && warTime >= protectionUntil, reserve, difficulty);
                if (formation is not null)
                {
                    commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
                        formation.Values.Sum(), true, oasis.Position.X, oasis.Position.Y, UnitId(home, UnitRole.Infantry), 0, 2, oasis.Id,
                        OasisMode: oasisMode, Formation: formation));
                    return;
                }
            }
        }

        // Bot establishing a strategic forward camp near target enemy supply roads
        TryBotSupplyCamps(actor, home, ownArmies, availableArmy, focus?.City, commands, personality, difficulty, maxArmies);
    }

    private bool TryBotSupplyCamps(Guid actor, VillageEconomy home, Army[] ownArmies, Army? availableArmy,
        VillageEconomy? focusCity, IGameCommands commands, BotPersonality personality, BotDifficulty difficulty, int maxArmies)
    {
        if (warTime < protectionUntil) return false;
        var maxCamps = difficulty == BotDifficulty.Hard ? 2 : 1;
        var existingCamps = ownArmies.Count(a => a.CampRadius is not null || a.Order == "Camping");
        if (existingCamps >= maxCamps) return false;

        var rivalCity = focusCity
            ?? villages.Values.Where(v => v.Village.OwnerId != actor && !CampAllied(actor, v.Village.OwnerId) && !v.Ruined)
                .OrderByDescending(v => difficulty == BotDifficulty.Hard && humanOwners.Contains(v.Village.OwnerId) ? 1 : 0)
                .ThenBy(v => Distance(v.Village.Position, home.Village.Position))
                .FirstOrDefault();
        if (rivalCity is null) return false;

        var enemyOasis = oases.Where(o => o.OwnerId == rivalCity.Village.OwnerId && o.BurnedUntil <= warTime)
            .OrderBy(o => Distance(o.Position, rivalCity.Village.Position))
            .Skip(existingCamps)
            .FirstOrDefault();
        var enemyFieldArmy = armies.FirstOrDefault(a => a.OwnerId == rivalCity.Village.OwnerId && !a.Routing && !IsScoutOnly(a)
            && Distance(a.Position, rivalCity.Village.Position) > 150);
        var anchor = existingCamps == 0
            ? (enemyOasis?.Position ?? enemyFieldArmy?.Position ?? home.Village.Position)
            : (enemyFieldArmy?.Position ?? enemyOasis?.Position ?? new Position(MapCenter, MapCenter));
        var gap = Math.Max(1, Distance(rivalCity.Village.Position, anchor));
        var offset = Math.Clamp(gap * 0.42, 130, 190);
        var campX = rivalCity.Village.Position.X + (anchor.X - rivalCity.Village.Position.X) * offset / gap;
        var campY = rivalCity.Village.Position.Y + (anchor.Y - rivalCity.Village.Position.Y) * offset / gap;
        var campPos = new Position(Math.Clamp(campX, MapMinimum + 100, MapMaximum - 100), Math.Clamp(campY, MapMinimum + 100, MapMaximum - 100));
        if (villages.Values.Any(v => !v.Ruined && CampAllied(actor, v.Village.OwnerId) && Distance(v.Village.Position, campPos) < 130))
            return false;
        if (difficulty == BotDifficulty.Hard && armies.Any(a => CampAllied(actor, a.OwnerId) && (a.CampRadius is not null || a.Order == "Camping") && Distance(a.Position, campPos) < 150))
            return false;

        var loadedOnly = difficulty != BotDifficulty.Hard;
        var minIdleForCamp = difficulty == BotDifficulty.Hard ? 28 : 16;
        if (availableArmy is not null && availableArmy.Order == "Idle" && Fighters(availableArmy) >= minIdleForCamp)
        {
            return commands.Execute(actor, new(Guid.NewGuid(), CommandKind.CampArmy, availableArmy.Id, X: campPos.X, Y: campPos.Y, LoadedOnly: loadedOnly)).Accepted;
        }
        var homeTroops = HomeCombatTroops(home);
        var minHomeForCamp = difficulty == BotDifficulty.Hard ? 42 : personality == BotPersonality.Aggressive ? 22 : 30;
        if (ActiveFieldArmyCount(ownArmies) < maxArmies && ownArmies.Count(a => a.Order != "Guarding") < 12 && homeTroops >= minHomeForCamp && home.Available(UnitId(home, UnitRole.Wagon)) >= 1)
        {
            var campFormation = BuildBotFormation(home, campPos, difficulty == BotDifficulty.Hard ? 48 : personality == BotPersonality.Aggressive ? 24 : 18, false, false, 8, difficulty);
            if (campFormation is not null)
            {
                return commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
                    campFormation.Values.Sum(), true, campPos.X, campPos.Y, UnitId(home, UnitRole.Infantry), 0, 2,
                    Camp: true, LoadedOnly: loadedOnly, Formation: campFormation)).Accepted;
            }
        }
        return false;
    }
    private static bool IsScoutOnly(Army army) =>
        army.Order == "Scouting" || (army.Troops.Any(t => t.Unit.Definition.Role == UnitRole.Scout)
            && army.Troops.Where(t => t.Unit is not SupplyWagon).All(t => t.Unit.Definition.Role == UnitRole.Scout));

    private bool CanInspect(Guid viewer, Guid ownerId) => ownerId == viewer || CampAllied(viewer, ownerId);

    public WorldSnapshot Snapshot(Guid viewer, bool omniscient = false) => new(
        armies.Where(x => (omniscient || !(x.Order == "Guarding" && x.DefendingSiteId is { } siteId && (oases.Any(o => o.Id == siteId) || villages.ContainsKey(siteId)) && x.OwnerId != viewer && x.SupportedOwnerId != viewer))
                && (omniscient || CanInspect(viewer, x.OwnerId) || !IsScoutOnly(x)))
            .Select(x => {
                var allied = omniscient || CanInspect(viewer, x.OwnerId);
                return new ArmySnapshot(x.Id, x.OwnerId, x.HomeVillageId, x.Position, x.Destination, x.Order,
                x.Troops.Where(t => t.Unit is not SupplyWagon && (allied || t.Unit.Definition.Role != UnitRole.Scout)).Sum(t => t.Count), WagonCount(x.Troops), x.Speed,
                allied ? x.TotalWheat : null, x.Capacity,
                x.AutoSupplyEnabled, allied ? x.SupplyWagons : 0, allied ? x.Escorts : 0,
                allied ? x.SupplyStatus : null, x.TargetId, x.ActionSeconds, x.SupplyVillageId,
                allied ? x.Loot : null, allied || x.SupportedOwnerId == viewer || IsScoutedBy(viewer, x.Id) ? x.Troops.ToDictionary(t => t.Unit.UnitId, t => t.Count) : null, x.AfterAttack, villages[x.HomeVillageId].Village.Tribe, x.DefendingSiteId, x.SupportedOwnerId,
                x.Troops.Where(t => t.Count > 0 && (allied || t.Unit.Definition.Role != UnitRole.Scout)).ToDictionary(t => t.Unit.UnitId, t => (int)Math.Ceiling(t.Count / 10.0)), x.CampRadius,
                allied ? x.WoundedCount : 0, allied ? x.DeadCount : 0, allied ? x.FledCount : 0,
                !allied && TryScoutExpiry(viewer, x.Id, out var scoutUntil) ? scoutUntil : null,
                allied ? x.Objective : null);
            }).ToArray(),
        convoys.Select(x => {
            var alliedConvoy = omniscient || CanInspect(viewer, x.OwnerId) || (x.RecipientId is { } rec && CanInspect(viewer, rec));
            return new ConvoySnapshot(x.Id, x.OwnerId, x.TargetArmyId, x.Position,
                ConvoyDestination(x),
                x.Returning, WagonCount(x.Troops), x.Troops.Where(t => t.Unit is not SupplyWagon).Sum(t => t.Count),
                alliedConvoy ? x.WheatCargo : null, x.Speed, x.Kind, x.InBattle, alliedConvoy ? x.Cargo : null, x.RecipientId);
        }).ToArray(),
        oases.Select(x => OasisView(x, viewer, omniscient)).ToArray(),
        notices.Where(x => omniscient || x.Owner == viewer).Select(x => x.Notice).ToArray(), WarfareSnapshot(viewer),
        armies.Where(x => !omniscient && x.OwnerId != viewer && (x.Order == "Approaching" && villages.GetValueOrDefault(x.TargetId ?? Guid.Empty)?.Village.OwnerId == viewer
            || x.Order == "Attacking" && armies.Any(a => a.Id == x.TargetId && a.OwnerId == viewer)))
            .Select(x => new IncomingThreat(x.Id, x.OwnerId, x.TargetId!.Value, warTime + Math.Max(0, Distance(x.Position, x.Destination) - (x.Order == "Approaching" ? 2 : 16))
                / Math.Max(.1, x.Speed))).ToArray(), Muster(viewer),
        reports.Where(r => omniscient || r.Attacker == viewer || r.Defender == viewer || r.Forces.Any(f => f.Army.OwnerId == viewer)).OrderByDescending(r => r.Status == "Active").ThenByDescending(r => r.UpdatedAt).Take(Math.Max(40, reports.Count(r => r.Status == "Active"))).Select(r => r.Snapshot(omniscient ? null : viewer)).ToArray(), regions, MarketSnapshot(), importantAlerts.Where(a => omniscient || a.Owner == viewer).Select(a => a.Notice).ToArray(), OasisSiegeViews(),
        villages.Values.Where(v => (omniscient || v.Village.OwnerId == viewer) && !v.Ruined).ToDictionary(v => v.Village.Id, v => (IReadOnlyDictionary<string, int>)v.Garrison().ToDictionary(t => t.Unit.UnitId, t => t.Count)));
    private void CompleteAttack(Army army)
    {
        army.DefendingSiteId = null; army.SupportedOwnerId = null; army.AttackOrderedAt = 0; army.CampRadius = null;
        army.TargetId = null;
        army.Order = army.AfterAttack == AfterAttack.Return ? "Returning" : "Idle";
        army.Destination = army.AfterAttack == AfterAttack.Return ? villages[army.HomeVillageId].Village.Position : army.Position;
    }

    private void ApplyDisbandTroops(Guid actor, GameCommand command, double now)
    {
        var requested = command.Formation is null
            ? new Dictionary<string, int> { [command.UnitId ?? ""] = command.Quantity }
            : new Dictionary<string, int>(command.Formation);
        if (requested.Count is < 1 or > 70 || requested.Any(x => x.Value is < 1 or > 100000 || !GameCatalog.Units.ContainsKey(x.Key)))
            throw new GameRuleException("unit", "Choose valid troop counts to disband.");

        if (villages.TryGetValue(command.TargetId, out var village))
        {
            if (village.Village.OwnerId != actor || village.Ruined)
                throw new GameRuleException("not_owned", "Disband troops from your own active village.");
            if (village.Besieged)
                throw new GameRuleException("siege", "Cannot disband garrison while the village is under siege.");
            var count = village.Disband(requested);
            Note(actor, now, $"Disbanded {count} garrison troops to reduce wheat upkeep.");
            return;
        }

        var army = Owned(actor, command.TargetId);
        if (Engaged(army) || oasisSieges.Any(s => s.Status == "Active" && s.Attackers.Contains(army)) || army.Order is "Battle" or "Sieging" or "Returning")
            throw new GameRuleException("engaged", "Cannot disband troops from an army that is engaged in combat or returning.");

        var stackCounts = army.Troops.ToDictionary(t => t.Unit.UnitId, t => t.Count);
        foreach (var (id, qty) in requested)
        {
            if (stackCounts.GetValueOrDefault(id) < qty)
                throw new GameRuleException("troops", "Not enough available troops of that type in this army.");
        }

        var surviving = new List<TroopStack>();
        foreach (var t in army.Troops)
        {
            var cut = requested.GetValueOrDefault(t.Unit.UnitId);
            if (t.Count > cut) surviving.Add(new(t.Unit, t.Count - cut));
        }

        var disbanded = requested.Values.Sum();
        var wagonsBefore = RoleCount(army, UnitRole.Wagon);
        villages[army.HomeVillageId].LoseDeployed(disbanded);
        FinalizeArmyDamage(army, surviving, disbanded, wagonsBefore);

        if (surviving.Count == 0)
        {
            if (army.Order == "Burning") WithdrawFromSieges(army, now, army.TargetId, army.Order);
            armies.Remove(army);
        }
        else if (Fighters(army) == 0 && army.Order is "Burning" or "Occupying" or "Blockading" or "Capturing")
        {
            WithdrawFromSieges(army, now, army.TargetId, army.Order);
            Route(army);
        }

        Note(actor, now, surviving.Count == 0
            ? $"Disbanded all {disbanded} units from the army."
            : $"Disbanded {disbanded} units from the army.");
    }

    private void ApplyMergeArmies(Guid actor, GameCommand command, double now)
    {
        if (villages.TryGetValue(command.TargetId, out var destVillage) && destVillage.Village.OwnerId == actor && !destVillage.Ruined)
        {
            var memberIds = (command.TargetIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();
            if (memberIds.Length == 0)
                throw new GameRuleException("target", "Select at least one field army to merge into the village garrison.");
            var candidates = memberIds.Select(id => Owned(actor, id)).ToArray();
            foreach (var candidate in candidates)
            {
                if (Engaged(candidate) || candidate.Order is "Battle" or "Sieging" or "Returning")
                    throw new GameRuleException("engaged", "Cannot merge an army that is engaged in combat or already returning.");
            }
            var vGroupId = Guid.NewGuid();
            foreach (var candidate in candidates)
            {
                candidate.MergeGroupId = vGroupId;
                candidate.MergeVillageId = destVillage.Village.Id;
                candidate.Order = "Merging";
                candidate.TargetId = destVillage.Village.Id;
                candidate.DefendingSiteId = null;
                candidate.SupportedOwnerId = null;
                candidate.CampRadius = null;
                candidate.ActionSeconds = 0;
                candidate.AttackOrderedAt = 0;
                candidate.Destination = destVillage.Village.Position;
            }
            AdvanceMerges(now);
            var stillMarching = candidates.Any(c => armies.Contains(c));
            Note(actor, now, stillMarching
                ? "Your armies are marching to merge into the village garrison."
                : "Your armies have merged into the village garrison.");
            return;
        }

        var orderedIds = new List<Guid> { command.TargetId };
        if (command.TargetIds is not null)
        {
            foreach (var id in command.TargetIds)
                if (id != Guid.Empty && !orderedIds.Contains(id)) orderedIds.Add(id);
        }
        var selectedArmies = orderedIds.Select(id => Owned(actor, id)).ToArray();
        if (selectedArmies.Length < 2)
            throw new GameRuleException("target", "Select at least two armies to merge.");
        foreach (var candidate in selectedArmies)
        {
            if (Engaged(candidate) || candidate.Order is "Battle" or "Sieging" or "Returning")
                throw new GameRuleException("engaged", "Cannot merge an army that is engaged in combat or returning.");
        }

        var guardedSites = selectedArmies
            .Where(a => a.Order == "Guarding" && a.DefendingSiteId is not null)
            .Select(a => a.DefendingSiteId!.Value)
            .Distinct()
            .ToArray();
        Position meetingPoint;
        Guid? anchorSiteId = null;
        if (guardedSites.Length == 1)
        {
            try
            {
                meetingPoint = DefenseSite(guardedSites[0]).Position;
                anchorSiteId = guardedSites[0];
            }
            catch (GameRuleException)
            {
                meetingPoint = Point(
                    selectedArmies.Average(a => a.Position.X),
                    selectedArmies.Average(a => a.Position.Y));
            }
        }
        else
        {
            meetingPoint = Point(
                selectedArmies.Average(a => a.Position.X),
                selectedArmies.Average(a => a.Position.Y));
        }

        var groupId = Guid.NewGuid();
        foreach (var candidate in selectedArmies)
        {
            candidate.MergeGroupId = groupId;
            candidate.MergeVillageId = null;
            if (anchorSiteId is { } siteId && candidate.Order == "Guarding" && candidate.DefendingSiteId == siteId)
            {
                candidate.Destination = meetingPoint;
                continue;
            }
            candidate.Order = "Merging";
            candidate.Destination = meetingPoint;
            candidate.TargetId = null;
            candidate.DefendingSiteId = null;
            candidate.SupportedOwnerId = null;
            candidate.CampRadius = null;
            candidate.ActionSeconds = 0;
            candidate.AttackOrderedAt = 0;
        }

        var beforeCount = armies.Count;
        AdvanceMerges(now);
        if (armies.Count < beforeCount && selectedArmies.Count(a => armies.Contains(a)) <= 1)
            return;
        Note(actor, now, "Your armies are marching toward each other to merge.");
    }

    private void AdvanceMerges(double now)
    {
        foreach (var army in armies)
        {
            if (army.Order is not ("Merging" or "Guarding"))
            {
                army.MergeGroupId = null;
                army.MergeVillageId = null;
            }
        }

        foreach (var army in armies.Where(a => a.Order == "Merging" && a.MergeVillageId is not null).ToArray())
        {
            var destId = army.MergeVillageId!.Value;
            if (!villages.TryGetValue(destId, out var destVillage) || destVillage.Ruined || destVillage.Village.OwnerId != army.OwnerId)
            {
                army.MergeGroupId = null;
                army.MergeVillageId = null;
                army.Order = "Idle";
                army.TargetId = null;
                army.Destination = army.Position;
                continue;
            }
            army.Destination = destVillage.Village.Position;
            if (!destVillage.Besieged && Distance(army.Position, destVillage.Village.Position) <= 8)
            {
                if (army.HomeVillageId != destVillage.Village.Id)
                    villages[army.HomeVillageId].Rebase(destVillage, Count(army));
                destVillage.Arrive(army.Troops, army.Wheat);
                destVillage.AddResources(army.Loot);
                armies.Remove(army);
                Note(army.OwnerId, now, "Your expedition merged into the village garrison.");
            }
        }

        var fieldGroups = armies
            .Where(a => a.MergeGroupId is not null && a.MergeVillageId is null && a.Order is "Merging" or "Guarding")
            .GroupBy(a => a.MergeGroupId!.Value)
            .ToArray();

        foreach (var group in fieldGroups)
        {
            var members = group.ToList();
            var mergedAny = false;
            var keepMerging = true;
            while (keepMerging && members.Count > 1)
            {
                keepMerging = false;
                for (var i = 0; i < members.Count && !keepMerging; i++)
                {
                    for (var j = i + 1; j < members.Count; j++)
                    {
                        var a = members[i];
                        var b = members[j];
                        var sameSite = a.DefendingSiteId is not null && a.DefendingSiteId == b.DefendingSiteId;
                        if (Distance(a.Position, b.Position) <= 8 || (sameSite && Distance(a.Position, b.Position) <= 16))
                        {
                            var survivor = (b.Order == "Guarding" && a.Order != "Guarding")
                                || (a.Order == b.Order && Count(b) > Count(a)) ? b : a;
                            var absorbed = survivor == a ? b : a;
                            AbsorbIntoArmy(survivor, absorbed);
                            members.Remove(absorbed);
                            armies.Remove(absorbed);
                            mergedAny = true;
                            keepMerging = true;
                            break;
                        }
                    }
                }
            }

            if (members.Count == 1)
            {
                var sole = members[0];
                sole.MergeGroupId = null;
                sole.MergeVillageId = null;
                if (sole.DefendingSiteId is { } siteId)
                {
                    try
                    {
                        var site = DefenseSite(siteId);
                        sole.Order = "Guarding";
                        sole.TargetId = siteId;
                        sole.Destination = site.Position;
                    }
                    catch (GameRuleException)
                    {
                        sole.Order = "Idle";
                        sole.TargetId = null;
                        sole.DefendingSiteId = null;
                        sole.SupportedOwnerId = null;
                        sole.Destination = sole.Position;
                    }
                }
                else
                {
                    sole.Order = "Idle";
                    sole.TargetId = null;
                    sole.Destination = sole.Position;
                }
                if (mergedAny)
                    Note(sole.OwnerId, now, "Armies merged into a single force.");
            }
        }
    }

    private void StarveArmyTroops(Army army, int targetLosses, double now)
    {
        if (targetLosses <= 0) return;
        var home = villages[army.HomeVillageId];
        var stats = statistics[army.OwnerId];
        var ordered = army.Troops.OrderBy(t => t.Unit is SupplyWagon ? 2 : t.Unit is SiegeEngine ? 1 : 0).ToArray();
        var remaining = targetLosses;
        var survivors = new List<TroopStack>();
        foreach (var stack in ordered)
        {
            var lost = stack.Unit is SupplyWagon ? 0 : Math.Min(remaining, stack.Count);
            if (lost > 0)
            {
                remaining -= lost;
                home.LoseDeployed(lost);
                stats.Wounded += lost;
                army.WoundedCount += lost;
                home.AdmitWounded(stack.Unit.UnitId, lost, now + 60 + Distance(army.Position, home.Village.Position) / stack.Unit.Speed);
            }
            if (stack.Count > lost) survivors.Add(new(stack.Unit, stack.Count - lost));
        }
        army.Troops = survivors.AsReadOnly();
        army.Wheat = Math.Min(army.Wheat, army.Capacity);
        var remainingCargo = Math.Max(0, army.Troops.Sum(x => (double)x.Count * x.Unit.CarryCapacity) - army.Wheat);
        if (army.Loot.Total > remainingCargo) army.Loot = army.Loot.Fraction(remainingCargo / Math.Max(1, army.Loot.Total));
    }

    private void AbsorbIntoArmy(Army survivor, Army absorbed)
    {
        var absCount = Count(absorbed);
        if (absorbed.HomeVillageId != survivor.HomeVillageId)
            villages[absorbed.HomeVillageId].Rebase(villages[survivor.HomeVillageId], absCount);

        var combined = new Dictionary<string, (BaseUnit Unit, int Count)>();
        foreach (var stack in survivor.Troops.Concat(absorbed.Troops))
        {
            combined[stack.Unit.UnitId] = combined.TryGetValue(stack.Unit.UnitId, out var existing)
                ? (existing.Unit, existing.Count + stack.Count)
                : (stack.Unit, stack.Count);
        }

        survivor.Troops = Array.AsReadOnly(combined.Values.Select(x => new TroopStack(x.Unit, x.Count)).ToArray());
        survivor.Wheat = Math.Min(survivor.Capacity, survivor.Wheat + absorbed.Wheat);
        survivor.Loot = survivor.Loot + absorbed.Loot;
        survivor.StarvationTimer = Math.Max(survivor.StarvationTimer, absorbed.StarvationTimer);
        survivor.WoundedCount += absorbed.WoundedCount;
        survivor.DeadCount += absorbed.DeadCount;
        survivor.FledCount += absorbed.FledCount;
        if (absorbed.AutoSupplyEnabled) survivor.SetSupply(true);
        survivor.SupplyWagons = Math.Clamp(Math.Max(survivor.SupplyWagons, absorbed.SupplyWagons), 0, 20);
        survivor.Escorts = Math.Clamp(Math.Max(survivor.Escorts, absorbed.Escorts), 0, 100);
        if (survivor.DefendingSiteId is null && absorbed.DefendingSiteId is not null)
        {
            survivor.DefendingSiteId = absorbed.DefendingSiteId;
            survivor.SupportedOwnerId = absorbed.SupportedOwnerId;
        }
    }
}
