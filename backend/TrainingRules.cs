namespace Warzone;

// One source of truth for the catalog, command validation and UI snapshots.
public static class TrainingRules
{
    public static BuildingType Facility(UnitRole role) => role switch
    {
        UnitRole.Infantry or UnitRole.Spearman or UnitRole.Swordsman => BuildingType.Barracks,
        UnitRole.Cavalry or UnitRole.LightCavalry or UnitRole.HeavyCavalry or UnitRole.Scout or UnitRole.Wagon => BuildingType.Stable,
        UnitRole.Governor => BuildingType.MainHall,
        _ => BuildingType.SiegeWorkshop
    };

    public static int RequiredLevel(UnitRole role) => role switch
    {
        UnitRole.Spearman or UnitRole.LightCavalry or UnitRole.Catapult => 3,
        UnitRole.Swordsman or UnitRole.Cavalry => 5,
        UnitRole.HeavyCavalry => 8,
        _ => 1
    };

    // +7 percentage points of base throughput per upgrade; duration = base / speed.
    public static double SpeedBonusPercent(BuildingType facility, int level) =>
        Math.Max(0, level - 1) * (facility == BuildingType.MainHall ? 8 : 7);
}
