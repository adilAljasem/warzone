namespace Warzone;

public enum ResourceType { Wood, Iron, Stone, Wheat }
public enum BuildingType { MainHall, Warehouse, Barracks, Stable, SiegeWorkshop, Smithy, Hospital, Wall, Market }

public readonly record struct Resources
{
    public long Wood { get; }
    public long Iron { get; }
    public long Stone { get; }
    public long Wheat { get; }

    [System.Text.Json.Serialization.JsonConstructor]
    public Resources(long wood, long iron, long stone, long wheat)
    {
        if (wood < 0 || iron < 0 || stone < 0 || wheat < 0)
            throw new ArgumentOutOfRangeException(nameof(wood), "Resources cannot be negative.");
        (Wood, Iron, Stone, Wheat) = (wood, iron, stone, wheat);
    }

    public bool Covers(Resources cost) => Wood >= cost.Wood && Iron >= cost.Iron
        && Stone >= cost.Stone && Wheat >= cost.Wheat;
    [System.Text.Json.Serialization.JsonIgnore]
    public long Total => Wood + Iron + Stone + Wheat;
    public static Resources operator +(Resources a, Resources b) => new(a.Wood + b.Wood, a.Iron + b.Iron, a.Stone + b.Stone, a.Wheat + b.Wheat);
    public static Resources operator -(Resources a, Resources b) => new(a.Wood - b.Wood, a.Iron - b.Iron, a.Stone - b.Stone, a.Wheat - b.Wheat);
    public Resources Fraction(double fraction) => new((long)(Wood * fraction), (long)(Iron * fraction), (long)(Stone * fraction), (long)(Wheat * fraction));
}

public sealed record UpgradeStep(int TargetLevel, Resources Cost);

public interface IUpgradable
{
    int Level { get; }
    int MaxLevel { get; }
    UpgradeStep? NextUpgrade { get; }
}

public interface IAutoQueueable
{
    Guid QueueId { get; }
    bool AutoQueueEnabled { get; }
}

// The room's authoritative service owns resource debit, timers and slot limits.
// Read-only interfaces prevent clients/bots from completing work directly.
public abstract class UpgradeableTarget : IUpgradable
{
    private readonly IReadOnlyList<UpgradeStep> upgrades;
    public Guid QueueId { get; } = Guid.NewGuid();
    public int Level { get; private set; } = 1;
    public int MaxLevel => upgrades.Count + 1;
    public UpgradeStep? NextUpgrade => Level > 0 && Level < MaxLevel ? upgrades[Level - 1] : null;

    protected UpgradeableTarget(IEnumerable<UpgradeStep> upgrades)
    {
        var steps = upgrades.ToArray();
        for (var i = 0; i < steps.Length; i++)
            if (steps[i].TargetLevel != i + 2)
                throw new ArgumentException("Upgrade levels must be sequential from 2.", nameof(upgrades));
        this.upgrades = Array.AsReadOnly(steps);
    }


    internal void CompleteUpgrade(int expectedLevel)
    {
        if (Level != expectedLevel || NextUpgrade is null)
            throw new InvalidOperationException("Stale or unavailable upgrade.");
        Level++;
    }
    internal void DamageLevel(bool ruin = false) => Level = ruin ? 0 : Math.Max(1, Level - 1);
}

public sealed class Building(BuildingType type, IEnumerable<UpgradeStep> upgrades) : UpgradeableTarget(upgrades)
{
    public BuildingType Type { get; } = Enum.IsDefined(type) ? type : throw new ArgumentOutOfRangeException(nameof(type));
}

// Level denotes the training tier, not the number of soldiers produced.
// Repeating troop production and training-tier upgrades use separate commands.
public sealed class UnitTraining : UpgradeableTarget, IAutoQueueable
{
    public bool AutoQueueEnabled { get; private set; }
    internal void SetAutoQueue(bool enabled) => AutoQueueEnabled = enabled;
    public UnitDefinition Unit { get; }
    public int BatchSize { get; }

    public UnitTraining(UnitDefinition unit, int batchSize, IEnumerable<UpgradeStep> upgrades) : base(upgrades)
    {
        unit.Validate();
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
        Unit = unit;
        BatchSize = batchSize;
    }
}

public sealed class GameRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public enum CommandKind { Upgrade, Train, SetAutoQueue, DispatchArmy, MoveArmy, CampArmy, RecallArmy, ConfigureSupply, CaptureOasis, InterceptConvoy,
    AttackArmy, BeginSiege, CaptureCastle, RelocateVillage, SelectVillage, SetAfterAttack, Reinforce, PostMarketOffer, AcceptMarketOffer, CancelMarketOffer, SwapMarket, SetOasisTransport, CollectOasis, UpgradeOasisWall, UpgradeOasisLevel, BesiegeOasis, BurnOasis, Scout,
    SendResources, SendChat, ProposeAlliance, RespondAlliance, CancelAllianceRequest, MergeArmies, DisbandTroops }
public enum OasisMode { Capture, Blockade, Burn }
public enum AfterAttack { Hold, Return }
public enum AttackObjective { Raid, Conquest, Raze, Blockade }
public sealed record GameCommand(Guid RequestId, CommandKind Kind, Guid TargetId, int Quantity = 1, bool Enabled = true,
    double X = 0, double Y = 0, string? UnitId = null, int Wagons = 1, int Escorts = 0, Guid? DestinationId = null, Guid? SourceVillageId = null,
    AttackObjective Objective = AttackObjective.Raid,
    IReadOnlyDictionary<string, int>? Formation = null, AfterAttack AfterAttack = AfterAttack.Hold,
    ResourceType OfferResource = ResourceType.Wood, long OfferAmount = 0, ResourceType AskResource = ResourceType.Iron, long AskAmount = 0, bool Reinforcement = false, OasisMode OasisMode = OasisMode.Capture,
    Resources Shipment = default, string? Message = null, bool Camp = false, bool LoadedOnly = true, Guid[]? TargetIds = null);
public sealed record CommandResult(Guid RequestId, bool Accepted, string Code, string Message);
public sealed record TrainingOrder(Guid TargetId, string Lane, int Quantity, double CompletesAt, double StartedAt);
public sealed record TargetSnapshot(Guid Id, string Name, string Kind, int Level, int MaxLevel, bool Auto,
    Resources? UpgradeCost, Resources? TrainingCost, double? TrainingSeconds, int AutoBatchSize, string? IconUrl = null,
    string? UnitId = null, string? TrainingBuilding = null, int RequiredBuildingLevel = 1,
    bool Unlocked = true, double TrainingSpeedBonusPercent = 0);
public sealed record WoundedGroupSnapshot(Guid? ArmyId, int Total, IReadOnlyDictionary<string, int> Units);
public sealed record EconomySnapshot(Guid VillageId, Resources Resources, long Capacity, int AutoSlots, int UsedAutoSlots,
    bool FoodShortage, TargetSnapshot[] Targets, TrainingOrder[] Jobs, IReadOnlyDictionary<string, int> Garrison, int Wounded, bool Besieged, ProductionSnapshot[] Production, BiomeType Biome, int OwnedOases, double[] OasisBonuses, int ReservedTradeWagons, WoundedGroupSnapshot[]? WoundedGroups = null);
internal sealed record WoundedBatch(string UnitId, int Count, double ReadyAt, Guid? ArmyId = null);

// Mutations run under the game server lock. All time is authoritative match time.
public sealed class VillageEconomy
{
    internal Action<string, string, double, double?>? OnEvent { get; set; }
    private static string TargetName(UpgradeableTarget target) => target switch { Building b => b.Type.ToString(), UnitTraining t => t.Unit.DisplayName, _ => "Target" };
    private readonly decimal[] stock = [1500, 1500, 1500, 1500];
    private readonly List<TrainingOrder> jobs = [];
    private readonly Dictionary<string, int> garrison = [];
    private readonly UpgradeableTarget[] targets;
    private readonly TribeBonuses bonuses;
    private readonly List<Guid> autoOrder = [];
    private int awayTroops;
    private readonly List<WoundedBatch> wounded = [];
    private double healingProgress;
    internal double BotResourceMultiplier { get; private set; } = 1.0;
    internal double BotTrainingSpeedMultiplier { get; private set; } = 1.0;
    internal double BotBuildCostMultiplier { get; private set; } = 1.0;
    internal long BotCapacityMultiplier { get; private set; } = 1L;
    internal void SetHardBotBonuses(bool enabled)
    {
        BotResourceMultiplier = enabled ? 2.0 : 1.0;
        BotTrainingSpeedMultiplier = 1.0;
        BotBuildCostMultiplier = enabled ? 0.55 : 1.0;
        BotCapacityMultiplier = enabled ? 2L : 1L;
    }
    public BiomeType Biome { get; internal set; }
    public bool Besieged { get; internal set; }
    public bool Ruined { get; private set; }
    public double RelocateAfter { get; internal set; }
    public Position? RelocateTarget { get; internal set; }
    public bool Relocating => RelocateTarget is not null;
    public double RaidedUntil { get; internal set; }
    public int Population => garrison.Values.Sum() + awayTroops + wounded.Sum(x => x.Count) + jobs.Sum(x => x.Quantity);
    public Village Village { get; }
    public IReadOnlyList<UnitTraining> Training { get; }
    public long Capacity => (2000L + BuildingLevel(BuildingType.Warehouse) * 2000L) * BotCapacityMultiplier;
    public double[] OasisBonuses { get; } = new double[4];
    public int OwnedOases { get; internal set; }
    public int ReservedTradeWagons { get; internal set; }
    private double HomeUpkeep => garrison.Sum(x => GameCatalog.Units[x.Key].Stats.WheatUpkeep * x.Value)
        + ReservedTradeWagons * Training.Single(t => t.Unit.Role == UnitRole.Wagon).Unit.Stats.WheatUpkeep;
    public int AutoSlots => GameCatalog.Tribes[Village.Tribe].StartingAutoQueueSlots + (Village.Level - 1) / 5;
    public Resources Resources => new((long)stock[0], (long)stock[1], (long)stock[2], (long)stock[3]);
    public bool FoodShortage { get; private set; }

    // Differentiated material mix for each building upgrade, ending uniform 180-costs and creating varied resource demand.
    private static readonly IReadOnlyDictionary<BuildingType, Resources> BuildingRate = new Dictionary<BuildingType, Resources>
    {
        [BuildingType.MainHall] = new(90, 80, 85, 60),
        [BuildingType.Warehouse] = new(110, 65, 90, 50),
        [BuildingType.Barracks] = new(100, 90, 65, 60),
        [BuildingType.Stable] = new(90, 75, 60, 90),
        [BuildingType.SiegeWorkshop] = new(115, 95, 55, 50),
        [BuildingType.Smithy] = new(65, 130, 75, 45),
        [BuildingType.Hospital] = new(75, 55, 95, 90),
        [BuildingType.Wall] = new(50, 55, 160, 50),
        [BuildingType.Market] = new(105, 60, 70, 80)
    };

    public VillageEconomy(Guid ownerId, TribeType tribe, Position position)
    {
        static UpgradeStep[] Track(Resources rate, int maxLevel = 30) => Enumerable.Range(2, maxLevel - 1)
            .Select(level => new UpgradeStep(level, new Resources(level * rate.Wood, level * rate.Iron, level * rate.Stone, level * rate.Wheat))).ToArray();
        static UpgradeStep[] UnitTrack(Resources unitCost, int maxLevel = 20)
        {
            var total = Math.Max(1, unitCost.Total);
            return Enumerable.Range(2, maxLevel - 1).Select(level =>
            {
                var budget = level * 210L;
                return new UpgradeStep(level, new Resources(
                    budget * unitCost.Wood / total,
                    budget * unitCost.Iron / total,
                    budget * unitCost.Stone / total,
                    budget * unitCost.Wheat / total));
            }).ToArray();
        }
        Village = new(ownerId, position, tribe,
            Enum.GetValues<BuildingType>().Select(type => new Building(type, Track(BuildingRate[type]))));
        Training = Array.AsReadOnly(GameCatalog.Units.Values.Where(x => x.TribeType == tribe)
            .OrderBy(x => x.Role).Select(x => new UnitTraining(x, 5, UnitTrack(x.TrainingCost))).ToArray());
        targets = Village.Buildings.Cast<UpgradeableTarget>().Concat(Training).ToArray();
        bonuses = GameCatalog.Tribes[tribe].Bonuses;
        garrison.Add(Training.Single(x => x.Unit.Role == UnitRole.Infantry).Unit.UnitId, 20);
        garrison.Add(Training.Single(x => x.Unit.Role == UnitRole.Wagon).Unit.UnitId, 2);
    }

    public int BuildingLevel(BuildingType type) => Village.Buildings.Single(x => x.Type == type).Level;
    public bool ContainsTarget(Guid id) => targets.Any(x => x.QueueId == id);
    private UpgradeableTarget Find(Guid id) => targets.FirstOrDefault(x => x.QueueId == id)
        ?? throw new GameRuleException("not_owned", "This target does not belong to your village.");
    private static BuildingType Facility(UnitTraining target) => target.Unit.TrainingBuilding;
    private bool Unlocked(UnitTraining target) => BuildingLevel(Facility(target)) >= target.Unit.RequiredBuildingLevel;
    private void RequireUnlocked(UnitTraining target)
    {
        if (!Unlocked(target)) throw new GameRuleException("unit_locked",
            $"Requires {Facility(target)} level {target.Unit.RequiredBuildingLevel}.");
    }
    private static Resources Scale(Resources value, double multiplier) => new(
        (long)Math.Ceiling(value.Wood * multiplier), (long)Math.Ceiling(value.Iron * multiplier),
        (long)Math.Ceiling(value.Stone * multiplier), (long)Math.Ceiling(value.Wheat * multiplier));
    private Resources TrainingCost(UnitTraining target, int count) => Scale(target.Unit.TrainingCost, bonuses.TrainingCostMultiplier * count);
    private Resources? EffectiveUpgradeCost(UpgradeableTarget target)
    {
        if (target.NextUpgrade is not { } step) return null;
        var multiplier = (target is Building ? bonuses.BuildingCostMultiplier : 1.0) * BotBuildCostMultiplier;
        return multiplier < 1.0 ? Scale(step.Cost, multiplier) : step.Cost;
    }
    private double TrainingSeconds(UnitTraining target, int count) => Math.Max(.25,
        target.Unit.TrainingDuration.TotalSeconds * count * bonuses.TrainingTimeMultiplier
        / ((1 + TrainingRules.SpeedBonusPercent(Facility(target), BuildingLevel(Facility(target))) / 100)
            * (1 + .05 * (target.Level - 1)) * BotTrainingSpeedMultiplier));
    internal void Pay(Resources cost)
    {
        if (!Resources.Covers(cost)) throw new GameRuleException("resources", "Not enough resources.");
        stock[0] -= cost.Wood; stock[1] -= cost.Iron; stock[2] -= cost.Stone; stock[3] -= cost.Wheat;
    }

    internal void Apply(GameCommand command, double now)
    {
        if (Ruined) throw new GameRuleException("ruins", "This settlement is in ruins.");
        var target = Find(command.TargetId);
        switch (command.Kind)
        {
            case CommandKind.SetAutoQueue when target is UnitTraining repeating:
                if (command.Enabled) RequireUnlocked(repeating);
                if (command.Enabled && !repeating.AutoQueueEnabled && Training.Count(x => x.AutoQueueEnabled) >= AutoSlots)
                    throw new GameRuleException("auto_slots", "All automatic training slots are in use.");
                if (command.Enabled && !repeating.AutoQueueEnabled) autoOrder.Add(target.QueueId);
                if (!command.Enabled) autoOrder.Remove(target.QueueId);
                repeating.SetAutoQueue(command.Enabled);
                break;
            case CommandKind.Upgrade:
                StartUpgrade(target, now);
                break;
            case CommandKind.Train when target is UnitTraining training:
                StartTraining(training, command.Quantity, now);
                break;
            default:
                throw new GameRuleException("command", "Invalid command or target type.");
        }
    }

    private void StartUpgrade(UpgradeableTarget target, double now)
    {
        if (target is Building && Besieged) throw new GameRuleException("siege", "Buildings cannot be upgraded while the village is under siege.");
        if (target is UnitTraining training) RequireUnlocked(training);
        var cost = EffectiveUpgradeCost(target) ?? throw new GameRuleException("max_level", "Maximum level reached.");
        if (target is Building building && building.Type != BuildingType.MainHall && building.Level >= BuildingLevel(BuildingType.MainHall))
            throw new GameRuleException("main_hall", "Upgrade the Main Hall first. Other buildings cannot exceed the Main Hall level.");
        Pay(cost);
        target.CompleteUpgrade(target.Level);
        OnEvent?.Invoke("build", $"{TargetName(target)} reached level {target.Level}.", now, null);
    }

    private void StartTraining(UnitTraining target, int quantity, double now)
    {
        if (quantity is < 1 or > 100) throw new GameRuleException("quantity", "Train between 1 and 100 units per batch.");
        var lane = Facility(target).ToString();
        if(BuildingLevel(Facility(target))==0)throw new GameRuleException("building", "This training building has been destroyed.");
        RequireUnlocked(target);
        if (jobs.Count(x => x.Lane == lane) >= 12)
            throw new GameRuleException("busy", "This training facility or target is busy.");
        if (Population + quantity > 100000)
            throw new GameRuleException("troop_limit", "The village troop limit has been reached.");
        Pay(TrainingCost(target, quantity));
        var startAt = jobs.Where(x => x.Lane == lane).Select(x => x.CompletesAt).DefaultIfEmpty(now).Max();
        startAt = Math.Max(now, startAt);
        var completesAt = startAt + TrainingSeconds(target, quantity);
        jobs.Add(new(target.QueueId, lane, quantity, completesAt, startAt));
        OnEvent?.Invoke("train", $"Training {quantity} {target.Unit.DisplayName}.", now, jobs[^1].CompletesAt);
    }

    internal TroopStack[] Depart(IReadOnlyDictionary<string, int> requested, long wheat)
    {
        if (requested.Count == 0 || requested.Any(x => x.Value <= 0 || garrison.GetValueOrDefault(x.Key) < x.Value))
            throw new GameRuleException("troops", "Not enough available troops or wagons in this village.");
        var stacks = requested.Select(x => new TroopStack(BaseUnit.Create(GameCatalog.Units[x.Key]), x.Value)).ToArray();
        Pay(new Resources(0, 0, 0, wheat));
        foreach (var (id, count) in requested) garrison[id] -= count;
        awayTroops += requested.Values.Sum();
        return stacks;
    }
    internal int Disband(IReadOnlyDictionary<string, int> requested)
    {
        if (requested.Count == 0 || requested.Any(x => x.Value <= 0 || garrison.GetValueOrDefault(x.Key) < x.Value))
            throw new GameRuleException("troops", "Not enough available troops or wagons in this village.");
        var total = 0;
        foreach (var (id, count) in requested)
        {
            garrison[id] -= count;
            if (garrison[id] <= 0) garrison.Remove(id);
            total += count;
        }
        return total;
    }
    internal int Available(string unitId) => garrison.GetValueOrDefault(unitId);
    internal void Arrive(IEnumerable<TroopStack> troops, double wheat)
    {
        foreach (var stack in troops)
        {
            garrison[stack.Unit.UnitId] = garrison.GetValueOrDefault(stack.Unit.UnitId) + stack.Count;
            awayTroops -= stack.Count;
        }
        stock[3] += Math.Min(Math.Max(0, Capacity - stock[3]), (decimal)wheat);
    }
    internal void LoseDeployed(int count) => awayTroops -= count;
    internal void Rebase(VillageEconomy destination, int count) { awayTroops -= count; destination.awayTroops += count; }
    internal TroopStack[] Garrison() => garrison.Where(x => x.Value > 0).Select(x => new TroopStack(BaseUnit.Create(GameCatalog.Units[x.Key]), x.Value)).ToArray();
    internal void ReplaceGarrison(IEnumerable<TroopStack> stacks)
    {
        garrison.Clear(); foreach (var stack in stacks) garrison[stack.Unit.UnitId] = stack.Count;
    }
    internal bool ReceiveGift(IEnumerable<TroopStack> stacks)
    {
        if (Population + stacks.Sum(x => x.Count) > 100000) return false;
        foreach (var stack in stacks) garrison[stack.Unit.UnitId] = garrison.GetValueOrDefault(stack.Unit.UnitId) + stack.Count;
        return true;
    }
    internal void AddResources(Resources amount)
    {
        stock[0] += Math.Min(Math.Max(0, Capacity - stock[0]), amount.Wood); stock[1] += Math.Min(Math.Max(0, Capacity - stock[1]), amount.Iron);
        stock[2] += Math.Min(Math.Max(0, Capacity - stock[2]), amount.Stone); stock[3] += Math.Min(Math.Max(0, Capacity - stock[3]), amount.Wheat);
    }
    // Delivered cargo and cancelled escrow retain their full value even if storage filled in transit.
    // Production pauses above capacity until the excess is spent.
    internal void ReceiveCargo(Resources amount)
    { stock[0] += amount.Wood; stock[1] += amount.Iron; stock[2] += amount.Stone; stock[3] += amount.Wheat; }
    internal double DrainWheat(double amount) { var taken = Math.Min(stock[3], (decimal)Math.Max(0, amount)); stock[3] -= taken; return (double)taken; }
    internal Resources Loot(double fraction, double capacity)
    {
        var available = Resources.Fraction(Math.Min(.9, fraction));
        var result = available.Fraction(Math.Min(1, Math.Max(0, capacity) / Math.Max(1, available.Total)));
        Pay(result); return result;
    }
    internal void AdmitWounded(string unitId, int count, double readyAt, Guid? armyId = null)
    {
        if (count > 0) wounded.Add(new(unitId, count, readyAt, armyId));
    }
    internal int WoundedForArmy(Guid armyId) => wounded.Where(x => x.ArmyId == armyId).Sum(x => x.Count);
    internal Dictionary<string, int> KillArmyWounded(Guid armyId)
    {
        var killed = new Dictionary<string, int>();
        for (var i = wounded.Count - 1; i >= 0; i--)
        {
            if (wounded[i].ArmyId != armyId) continue;
            killed[wounded[i].UnitId] = killed.GetValueOrDefault(wounded[i].UnitId) + wounded[i].Count;
            wounded.RemoveAt(i);
        }
        return killed;
    }
    internal List<WoundedBatch> ExtractArmyWounded(Guid armyId)
    {
        var extracted = new List<WoundedBatch>();
        for (var i = wounded.Count - 1; i >= 0; i--)
        {
            if (wounded[i].ArmyId != armyId) continue;
            extracted.Add(wounded[i]);
            wounded.RemoveAt(i);
        }
        extracted.Reverse();
        return extracted;
    }
    internal void AddWoundedBatches(IEnumerable<WoundedBatch> batches)
    {
        foreach (var batch in batches)
            if (batch.Count > 0) wounded.Add(batch);
    }
    internal void DamageBuilding(BuildingType type, bool ruin = false)
    {
        var building = Village.Buildings.Single(x => x.Type == type);
        building.DamageLevel(ruin);
        if(building.Level==0)
        {
            jobs.RemoveAll(j=>j.Lane==type.ToString());
            foreach(var training in Training.Where(t=>Facility(t)==type))training.SetAutoQueue(false);
        }
        foreach (var training in Training.Where(t => Facility(t) == type && !Unlocked(t)))
            training.SetAutoQueue(false);
        foreach (var target in Training.Where(x => x.AutoQueueEnabled).Skip(AutoSlots)) target.SetAutoQueue(false);
    }
    internal Resources Raze()
    {
        var value = Village.Buildings.Sum(x => (long)(x.Level * (x.Level + 1) / 2) * 45);
        var stored = Resources;
        Ruined = true; Besieged = false; RaidedUntil = 0;
        foreach (var building in Village.Buildings) DamageBuilding(building.Type, true); jobs.Clear(); garrison.Clear(); wounded.Clear();
        foreach (var target in Training) target.SetAutoQueue(false);
        Array.Clear(stock);
        return stored + new Resources(value / 2, value / 2, value / 2, value / 4);
    }
    internal void ChangeOwner(Guid owner, VillageEconomy? evacueeHome)
    {
        if (evacueeHome is not null) evacueeHome.wounded.AddRange(wounded);
        wounded.Clear(); jobs.Clear(); Besieged = false;
        foreach (var target in Training) target.SetAutoQueue(false);
        Village.TransferTo(owner);
    }

    private static int BaseProductionPerMinute(ResourceType resource) => resource == ResourceType.Wheat ? 360 : 240;
    private double MainHallProductionMultiplier => 1.0 + (BuildingLevel(BuildingType.MainHall) / 5) * 0.5;

    internal void Advance(double seconds, double now, double resourceSpeed)
    {
        if (Ruined) return;
        var upkeep = HomeUpkeep / 60;
        var hallMult = MainHallProductionMultiplier;
        for (var i = 0; i < stock.Length; i++)
        {
            var resource = (ResourceType)i;
            var rate = BaseProductionPerMinute(resource) / 60d * resourceSpeed * hallMult * BotResourceMultiplier * bonuses.ResourceMultiplier * Biomes.Multiplier(Biome, resource) * (1 + OasisBonuses[i]) * (Besieged && resource == ResourceType.Wheat ? .5 : 1) - (resource == ResourceType.Wheat ? upkeep : 0);
            stock[i] = Math.Clamp(stock[i] + (decimal)(rate * seconds), 0, Math.Max(Capacity, stock[i]));
        }
        FoodShortage = stock[3] == 0 && upkeep > 0;
        healingProgress = Math.Min(10, healingProgress + seconds * BuildingLevel(BuildingType.Hospital) * bonuses.HealingMultiplier / 10);
        while (healingProgress >= 1 && Resources.Covers(new(0, 1, 0, 1)))
        {
            var idx = wounded.FindIndex(x => x.ReadyAt <= now);
            if (idx < 0) break;
            var patient = wounded[idx];
            Pay(new(0, 1, 0, 1)); healingProgress--;
            garrison[patient.UnitId] = garrison.GetValueOrDefault(patient.UnitId) + 1;
            if (patient.Count > 1) wounded[idx] = patient with { Count = patient.Count - 1 };
            else wounded.RemoveAt(idx);
        }
        foreach (var job in jobs.Where(x => x.CompletesAt <= now + 1e-9).ToArray())
        {
            var training = (UnitTraining)Find(job.TargetId);
            garrison[training.Unit.UnitId] = garrison.GetValueOrDefault(training.Unit.UnitId) + job.Quantity;
            jobs.Remove(job);
            OnEvent?.Invoke("train", $"{job.Quantity} {TargetName(training)} ready for duty.", now, null);
        }
        autoOrder.RemoveAll(id => !((UnitTraining)Find(id)).AutoQueueEnabled);
        foreach (var id in autoOrder.ToArray())
        {
            var training = (UnitTraining)Find(id);
            if (jobs.Any(x => x.Lane == Facility(training).ToString())) continue;
            try { StartTraining(training, training.BatchSize, now); autoOrder.Remove(id); autoOrder.Add(id); }
            catch (GameRuleException) { /* Retry when the facility, supplies or population permits. */ }
        }
    }

    public ProductionSnapshot[] Production(double resourceSpeed = 1) => Enum.GetValues<ResourceType>().Select(resource =>
    {
        var normal = BaseProductionPerMinute(resource);
        var gross = Ruined ? 0 : normal * resourceSpeed * MainHallProductionMultiplier * BotResourceMultiplier * bonuses.ResourceMultiplier * Biomes.Multiplier(Biome, resource) * (1 + OasisBonuses[(int)resource]) * (Besieged && resource == ResourceType.Wheat ? .5 : 1);
        var upkeep = resource == ResourceType.Wheat && !Ruined ? HomeUpkeep : 0;
        var net = gross - upkeep;
        return new ProductionSnapshot(resource.ToString(), gross, upkeep, net, net / normal * 100, Besieged && resource == ResourceType.Wheat, stock[(int)resource] >= Capacity);
    }).ToArray();

    public EconomySnapshot Snapshot(double resourceSpeed = 1) => new(Village.Id, Resources, Capacity, AutoSlots, Training.Count(x => x.AutoQueueEnabled),
        FoodShortage, targets.Select(target =>
        {
            var training = target as UnitTraining;
            var name = target switch { Building b => b.Type.ToString(), UnitTraining t => t.Unit.DisplayName, _ => "Unknown" };
            return new TargetSnapshot(target.QueueId, name, target is Building ? "Building" : "Training",
                target.Level, target.MaxLevel, training?.AutoQueueEnabled ?? false, EffectiveUpgradeCost(target),
                training is null ? null : TrainingCost(training, 1), training is null ? null : TrainingSeconds(training, 1), training?.BatchSize ?? 0, training?.Unit.IconUrl,
                training?.Unit.UnitId, training is null ? null : Facility(training).ToString(), training?.Unit.RequiredBuildingLevel ?? 1,
                training is null || Unlocked(training),
                target is Building facility && facility.Type is BuildingType.Barracks or BuildingType.Stable or BuildingType.SiegeWorkshop or BuildingType.MainHall
                    ? TrainingRules.SpeedBonusPercent(facility.Type, facility.Level) : 0);
        }).ToArray(), jobs.ToArray(), new Dictionary<string, int>(garrison), wounded.Sum(x => x.Count), Besieged, Production(resourceSpeed), Biome, OwnedOases, (double[])OasisBonuses.Clone(), ReservedTradeWagons,
        wounded.GroupBy(x => x.ArmyId)
            .Select(g => new WoundedGroupSnapshot(
                g.Key,
                g.Sum(x => x.Count),
                g.GroupBy(x => x.UnitId).ToDictionary(u => u.Key, u => u.Sum(x => x.Count))))
            .OrderBy(g => g.ArmyId is null ? 1 : 0)
            .ToArray());
}
