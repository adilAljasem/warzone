using Warzone;

internal static class TribalCostChecks
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        var profiles = new Dictionary<TribeType, (ResourceType First, ResourceType Second, string? Exception)>
        {
            [TribeType.Romans] = (ResourceType.Wood, ResourceType.Iron, "romans-archer"),
            [TribeType.Germans] = (ResourceType.Stone, ResourceType.Wheat, "germans-infantry"),
            [TribeType.Greeks] = (ResourceType.Wood, ResourceType.Stone, "greeks-scout"),
            [TribeType.Mongols] = (ResourceType.Wood, ResourceType.Wheat, "mongols-scout"),
            [TribeType.Persians] = (ResourceType.Iron, ResourceType.Stone, "persians-scout")
        };
        long Value(Resources cost, ResourceType resource) => resource switch
        {
            ResourceType.Wood => cost.Wood, ResourceType.Iron => cost.Iron,
            ResourceType.Stone => cost.Stone, _ => cost.Wheat
        };
        foreach (var (tribe, profile) in profiles)
        {
            var units = GameCatalog.Units.Values.Where(u => u.TribeType == tribe).ToArray();
            foreach (var unit in units.Where(u => u.UnitId != profile.Exception))
            {
                var preferred = Value(unit.TrainingCost, profile.First) + Value(unit.TrainingCost, profile.Second);
                Check(preferred > unit.TrainingCost.Total - preferred, $"{unit.UnitId} primarily consumes its tribe's signature resources");
                if (profile.Exception is not null)
                {
                    var roman = GameCatalog.Units[$"romans-{unit.Role.ToString().ToLowerInvariant()}"];
                    Check(unit.TrainingCost.Total == roman.TrainingCost.Total, $"{unit.UnitId} retains the role's overall cost");
                }
            }
            if (profile.Exception is not null)
            {
                var exception = GameCatalog.Units[profile.Exception];
                var preferred = Value(exception.TrainingCost, profile.First) + Value(exception.TrainingCost, profile.Second);
                Check(preferred < exception.TrainingCost.Total - preferred, $"{profile.Exception} is the deliberate light-unit exception");
            }
        }
        var germans = new VillageEconomy(Guid.NewGuid(), TribeType.Germans, new(500, 500));
        var archer = germans.Snapshot().Targets.Single(t => t.UnitId == "germans-archer");
        Check(archer.TrainingCost == new Resources(8, 0, 28, 28), "German training discounts apply to the new stone/wheat cost");
        var persians = new VillageEconomy(Guid.NewGuid(), TribeType.Persians, new(500, 500));
        var buildingCosts = persians.Village.Buildings.Select(b => b.NextUpgrade!.Cost).ToArray();
        Check(buildingCosts.Distinct().Count() == buildingCosts.Length, "Each building upgrade has its own material mix");
        var romanTier = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500)).Training.Single(t => t.Unit.UnitId == "romans-infantry").NextUpgrade!.Cost;
        Check(romanTier.Wood + romanTier.Iron > romanTier.Stone + romanTier.Wheat, "Training tiers follow the tribe's signature resources");
        var infantry = persians.Snapshot().Targets.Single(t => t.UnitId == "persians-infantry");
        persians.Apply(new(Guid.NewGuid(), CommandKind.Train, infantry.Id), 0);
        Check(persians.Resources == new Resources(1500, 1460, 1475, 1480), "Persian recruitment debits the tribe-specific iron/stone price");
        return checks;
    }
}
