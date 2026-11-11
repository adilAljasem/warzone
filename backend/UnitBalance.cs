namespace Warzone;

// Every multiplier is loaded from units.json and sent in the catalog so the unit card describes the actual combat rules.
public static class UnitBalance
{
    public static bool Mounted(UnitRole role) => role is UnitRole.Cavalry or UnitRole.LightCavalry or UnitRole.HeavyCavalry;
    public static bool Mounted(BaseUnit unit) => Mounted(unit.Definition.Role)
        || (unit.Definition.Role == UnitRole.Scout && unit.Definition.TrainingBuilding == BuildingType.Stable);

    public static double Matchup(BaseUnit unit, UnitRole enemy, bool attack = true)
    {
        var table = attack ? (unit.Definition.AttackMatchups ?? unit.Definition.Matchups) : (unit.Definition.DefenseMatchups ?? unit.Definition.Matchups);
        return table.GetValueOrDefault(enemy, 1);
    }
}
