[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Warzone.Tests")]

namespace Warzone;

public enum TribeType { Romans, Germans, Greeks, Mongols, Persians }
public enum UnitRole { Infantry, Cavalry, Scout, Wagon, Catapult, Ram, Governor, Spearman, Swordsman, LightCavalry, HeavyCavalry }

public sealed record TribeBonuses(
    int ExtraAutoQueueSlots = 0,
    double TrainingCostMultiplier = 1,
    double TrainingTimeMultiplier = 1,
    double LootMultiplier = 1,
    double WallMultiplier = 1,
    double HealingMultiplier = 1,
    bool CanRelocate = false,
    double ResourceMultiplier = 1);

public sealed record TribeDefinition(TribeType Type, string DisplayName, string IconUrl, TribeBonuses Bonuses)
{
    public int StartingAutoQueueSlots => 3 + Bonuses.ExtraAutoQueueSlots;
}

// Speed: map units/second. Upkeep: wheat per soldier/minute. Carry: resource units.
public sealed record UnitStats(
    double Attack,
    double DefenseInfantry, double DefenseCavalry,
    double Speed, int CarryCapacity, double WheatUpkeep, double HitPoints);

public sealed record UnitDefinition(
    string UnitId, TribeType TribeType, UnitRole Role, string DisplayName,
    string IconUrl, string MapSpriteUrl, UnitStats Stats,
    Resources TrainingCost, TimeSpan TrainingDuration, IReadOnlyDictionary<UnitRole, double> Matchups,
    BuildingType? CustomTrainingBuilding = null, int? CustomRequiredLevel = null,
    IReadOnlyDictionary<UnitRole, double>? AttackMatchups = null,
    IReadOnlyDictionary<UnitRole, double>? DefenseMatchups = null)
{
    public BuildingType TrainingBuilding => CustomTrainingBuilding ?? TrainingRules.Facility(Role);
    public int RequiredBuildingLevel => CustomRequiredLevel ?? TrainingRules.RequiredLevel(Role);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(UnitId) || string.IsNullOrWhiteSpace(DisplayName)
            || !Enum.IsDefined(TribeType) || !Enum.IsDefined(Role))
            throw new ArgumentException("Invalid unit identity or category.");
        if (string.IsNullOrWhiteSpace(IconUrl) || !IconUrl.StartsWith("/assets/", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(MapSpriteUrl) || !MapSpriteUrl.StartsWith("/assets/", StringComparison.Ordinal))
            throw new ArgumentException("Sprite paths must be rooted under /assets/.");
        double[] values = [Stats.Attack, Stats.DefenseInfantry,
            Stats.DefenseCavalry, Stats.Speed, Stats.WheatUpkeep, Stats.HitPoints];
        if (values.Any(x => !double.IsFinite(x) || x < 0) || Stats.Speed == 0 || Stats.HitPoints == 0
            || Stats.CarryCapacity < 0 || TrainingDuration <= TimeSpan.Zero)
            throw new ArgumentException("Invalid unit statistics or training duration.");
    }
}

// A unit archetype, shared by troop stacks. No class per tribe or named soldier.
public abstract class BaseUnit
{
    public UnitDefinition Definition { get; }
    public string UnitId => Definition.UnitId;
    public TribeType TribeType => Definition.TribeType;
    public string DisplayName => Definition.DisplayName;
    public string IconUrl => Definition.IconUrl;
    public string MapSpriteUrl => Definition.MapSpriteUrl;
    public double Attack => Definition.Stats.Attack;
    public double DefenseInfantry => Definition.Stats.DefenseInfantry;
    public double DefenseCavalry => Definition.Stats.DefenseCavalry;
    public double Speed => Definition.Stats.Speed;
    public int CarryCapacity => Definition.Stats.CarryCapacity;
    public double WheatUpkeep => Definition.Stats.WheatUpkeep;

    private protected BaseUnit(UnitDefinition definition)
    {
        definition.Validate();
        Definition = definition;
    }

    public static BaseUnit Create(UnitDefinition definition) => definition.Role switch
    {
        UnitRole.Wagon => new SupplyWagon(definition),
        UnitRole.Catapult or UnitRole.Ram => new SiegeEngine(definition),
        _ => new Soldier(definition)
    };
}

public sealed class Soldier : BaseUnit
{
    internal Soldier(UnitDefinition definition) : base(definition) { }
}
public sealed class SupplyWagon : BaseUnit
{
    internal SupplyWagon(UnitDefinition definition) : base(definition) { }
}
public sealed class SiegeEngine : BaseUnit
{
    internal SiegeEngine(UnitDefinition definition) : base(definition) { }
}

public sealed record TroopStack
{
    public BaseUnit Unit { get; }
    public int Count { get; }
    public TroopStack(BaseUnit unit, int count)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        Unit = unit;
        Count = count;
    }
}
