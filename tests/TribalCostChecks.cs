using Warzone;

internal static class TribalCostChecks
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        foreach (var tribe in Enum.GetValues<TribeType>())
        {
            var units = GameCatalog.Units.Values.Where(u => u.TribeType == tribe).ToArray();
            foreach (var unit in units)
            {
                Check(unit.TrainingCost.Total > 0, $"{unit.UnitId} has valid training cost");
            }
        }
        var germans = new VillageEconomy(Guid.NewGuid(), TribeType.Germans, new(500, 500));
        var spearman = germans.Snapshot().Targets.Single(t => t.UnitId == "germans-spearman");
        Check(spearman.TrainingCost == new Resources(8, 0, 24, 36), "German training discounts apply to the new stone/wheat cost");
        var persians = new VillageEconomy(Guid.NewGuid(), TribeType.Persians, new(500, 500));
        var buildingCosts = persians.Village.Buildings.Select(b => b.NextUpgrade!.Cost).ToArray();
        Check(buildingCosts.Distinct().Count() == buildingCosts.Length, "Each building upgrade has its own material mix");
        var romanEco = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500));
        var greekEco = new VillageEconomy(Guid.NewGuid(), TribeType.Greeks, new(500, 500));
        var romanSnap = romanEco.Snapshot();
        var greekSnap = greekEco.Snapshot();
        foreach (var bType in Enum.GetValues<BuildingType>())
        {
            var rCost = romanSnap.Targets.Single(t => t.Kind == "Building" && t.Name == bType.ToString()).UpgradeCost!.Value;
            var gCost = greekSnap.Targets.Single(t => t.Kind == "Building" && t.Name == bType.ToString()).UpgradeCost!.Value;
            Check(rCost.Wood == (long)Math.Ceiling(gCost.Wood * .5) && rCost.Iron == (long)Math.Ceiling(gCost.Iron * .5)
                && rCost.Stone == (long)Math.Ceiling(gCost.Stone * .5) && rCost.Wheat == (long)Math.Ceiling(gCost.Wheat * .5),
                $"Romans pay 50% building upgrade cost for {bType}");
        }
        var romanMainHall = romanSnap.Targets.Single(t => t.Kind == "Building" && t.Name == "MainHall");
        var romanBefore = romanEco.Resources;
        romanEco.Apply(new(Guid.NewGuid(), CommandKind.Upgrade, romanMainHall.Id), 0);
        Check(romanEco.Resources == romanBefore - romanMainHall.UpgradeCost!.Value, "Romans debit exact 50% discounted building upgrade cost");
        var romanTier = romanEco.Training.Single(t => t.Unit.UnitId == "romans-infantry").NextUpgrade!.Cost;
        Check(romanTier.Wood + romanTier.Iron > romanTier.Stone + romanTier.Wheat, "Training tiers follow the tribe's signature resources");
        var infantry = persians.Snapshot().Targets.Single(t => t.UnitId == "persians-infantry");
        var heavyCavalry = persians.Snapshot().Targets.Single(t => t.UnitId == "persians-heavycavalry");
        var persianTime = GameCatalog.Tribes[TribeType.Persians].Bonuses.TrainingTimeMultiplier;
        Check(persianTime < 1 && Math.Abs(infantry.TrainingSeconds!.Value - 4 * persianTime) < .001 && Math.Abs(heavyCavalry.TrainingSeconds!.Value - 10 * persianTime) < .001,
            "Persians apply their faster training multiplier");
        persians.Apply(new(Guid.NewGuid(), CommandKind.Train, infantry.Id), 0);
        Check(persians.Resources == new Resources(1500, 1500, 1500, 1500) - infantry.TrainingCost!.Value && infantry.TrainingCost.Value.Iron > 0 && infantry.TrainingCost.Value.Stone > 0, "Persian recruitment debits the tribe-specific iron/stone price");
        return checks;
    }
}
