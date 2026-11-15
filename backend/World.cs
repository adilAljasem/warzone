namespace Warzone;

public readonly record struct Position
{
    public double X { get; }
    public double Y { get; }
    public Position(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(x), "Coordinates must be finite.");
        (X, Y) = (x, y);
    }
}

public abstract class MapEntity
{
    public Guid Id { get; } = Guid.NewGuid();
    public Guid OwnerId { get; private set; }
    public Position Position { get; private set; }
    protected MapEntity(Guid ownerId, Position position)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("An owner is required.", nameof(ownerId));
        OwnerId = ownerId;
        Position = position;
    }

    // Called only by authoritative room systems, never via client-owned state.
    internal void Relocate(Position position) => Position = position;
    internal void TransferTo(Guid ownerId)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("An owner is required.", nameof(ownerId));
        OwnerId = ownerId;
    }
}

public enum SettlementStage { Village, Town, City }

public sealed class Village : MapEntity
{
    public TribeType Tribe { get; }
    public IReadOnlyList<Building> Buildings { get; }
    public int Level => Buildings.Single(x => x.Type == BuildingType.MainHall).Level;
    public SettlementStage Stage => Level <= 5 ? SettlementStage.Village : Level <= 10 ? SettlementStage.Town : SettlementStage.City;

    public Village(Guid ownerId, Position position, TribeType tribe,
        IEnumerable<Building> buildings) : base(ownerId, position)
    {
        if (!Enum.IsDefined(tribe)) throw new ArgumentOutOfRangeException(nameof(tribe));
        var buildingList = buildings.ToArray();
        if (buildingList.Length != Enum.GetValues<BuildingType>().Length || buildingList.Select(x => x.Type).Distinct().Count() != Enum.GetValues<BuildingType>().Length)
            throw new ArgumentException("A village needs exactly one of each building type.", nameof(buildings));
        Tribe = tribe;
        Buildings = Array.AsReadOnly(buildingList);
    }
}

public sealed class Army : MapEntity
{
    public Guid HomeVillageId { get; internal set; }
    public Guid SupplyVillageId { get; internal set; }
    public IReadOnlyList<TroopStack> Troops { get; internal set; }
    public double StarvationTimer { get; internal set; }
    public bool StarvingNotified { get; internal set; }
    public bool AutoSupplyEnabled { get; private set; } = true;
    public Position Destination { get; internal set; }
    public double Wheat { get; internal set; }
    public double Capacity => Troops.Sum(x => (double)x.Count * x.Unit.CarryCapacity);
    public double Upkeep => Troops.Sum(x => x.Count * x.Unit.WheatUpkeep) / 60;
    public double Speed => Troops.Count == 0 ? 0 : Troops.Min(x => x.Unit.Speed);
    public AfterAttack AfterAttack { get; internal set; }
    public OasisMode OasisMode { get; internal set; }
    public AttackObjective Objective { get; internal set; }
    public Resources Loot { get; internal set; }
    public double TotalWheat => Wheat + Loot.Wheat;
    public double LootSpace => Math.Max(0, Troops.Sum(x => (double)x.Count * x.Unit.CarryCapacity) - Wheat - Loot.Total);
    public string Order { get; internal set; } = "Marching";
    public Guid? TargetId { get; internal set; }
    public Guid? MergeGroupId { get; internal set; }
    internal Guid? MergeVillageId { get; set; }
    public Guid? DefendingSiteId { get; internal set; }
    public Guid? SupportedOwnerId { get; internal set; }
    public double? CampRadius { get; internal set; }
    // A camp that only raids loaded caravans lets empty ones pass; hostile armies are always engaged.
    public bool CampLoadedOnly { get; internal set; } = true;
    public double ActionSeconds { get; internal set; }
    public double AttackOrderedAt { get; internal set; }
    public double NextSupplyAt { get; internal set; }
    public double NextLootCollectAt { get; internal set; }
    public int SupplyWagons { get; internal set; }
    public int Escorts { get; internal set; }
    public int WoundedCount { get; internal set; }
    public int DeadCount { get; internal set; }
    public int FledCount { get; internal set; }
    public bool Routing { get; internal set; }
    public string SupplyStatus { get; internal set; } = "Ready";
    internal void SetSupply(bool enabled) => AutoSupplyEnabled = enabled;

    public Army(Guid ownerId, Position position, Guid homeVillageId, IEnumerable<TroopStack> troops) : base(ownerId, position)
    {
        if (homeVillageId == Guid.Empty) throw new ArgumentException("Home village is required.", nameof(homeVillageId));
        var stacks = troops.ToArray();
        if (stacks.Length == 0 || stacks.Any(x => x is null)) throw new ArgumentException("An army needs troops.", nameof(troops));
        HomeVillageId = homeVillageId;
        SupplyVillageId = homeVillageId;
        Troops = Array.AsReadOnly(stacks);
        Destination = position;
    }
}

public enum ConvoyKind { Supply, Trade, Oasis, Shipment }

public sealed class Convoy : MapEntity
{
    public Guid SourceVillageId { get; internal set; }
    public Guid? TargetArmyId { get; }
    public ConvoyKind Kind { get; internal set; }
    public IReadOnlyList<TroopStack> Troops { get; internal set; }
    public double WheatCargo { get; internal set; }
    public bool Returning { get; internal set; }
    public bool InBattle { get; internal set; }
    public Guid? RecipientId { get; internal set; }
    public Guid? TargetVillageId { get; internal set; }
    public Guid? OasisId { get; internal set; }
    public Resources Cargo { get; internal set; }
    public double Speed => Troops.Count == 0 ? 6 : Troops.Min(x => x.Unit.Speed);
    // Escort rations are not loot: only trade goods or wheat on its way to an army count as a load.
    public bool Loaded => Cargo.Total > 0 || Kind == ConvoyKind.Supply && !Returning && WheatCargo > 0;
    public long WheatCapacity => Troops.Where(x => x.Unit is SupplyWagon)
        .Sum(x => checked((long)x.Count * x.Unit.CarryCapacity));

    public Convoy(Guid ownerId, Position position, Guid sourceVillageId, Guid? targetArmyId,
        IEnumerable<TroopStack> troops, long wheatCargo) : base(ownerId, position)
    {
        if (sourceVillageId == Guid.Empty || targetArmyId == Guid.Empty)
            throw new ArgumentException("Supply endpoints are required.");
        var stacks = troops.ToArray();
        if (stacks.Any(x => x is null) || !stacks.Any(x => x.Unit is SupplyWagon))
            throw new ArgumentException("A convoy needs at least one supply wagon.", nameof(troops));
        Troops = Array.AsReadOnly(stacks);
        if (wheatCargo < 0 || wheatCargo > WheatCapacity) throw new ArgumentOutOfRangeException(nameof(wheatCargo));
        SourceVillageId = sourceVillageId;
        TargetArmyId = targetArmyId;
        WheatCargo = wheatCargo;
    }
}
