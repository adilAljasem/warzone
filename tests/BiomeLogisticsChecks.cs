using System.Text.Json;
using System.Xml.Linq;
using Warzone;

internal static class BiomeLogisticsChecks
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        var regions = Biomes.Generate(731);
        Check(JsonSerializer.Serialize(regions) == JsonSerializer.Serialize(Biomes.Generate(731)), "A terrain seed reproduces the same regions");
        Check(JsonSerializer.Serialize(regions) != JsonSerializer.Serialize(Biomes.Generate(732)), "New seeds change biome distribution");
        Check(regions.Select(r => r.Kind).Distinct().Count() == 4, "Every world contains four productive biomes");
        // Multi-frequency borders must still partition the playable area without gaps or overlaps.
        bool Contains(Position[] polygon, Position point)
        {
            var inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)
                    && point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X)
                    inside = !inside;
            return inside;
        }
        for (var seed = 0; seed < 24; seed++)
        {
            var generated = Biomes.Generate(seed);
            var samples = from x in Enumerable.Range(0, 41) from y in Enumerable.Range(0, 41) select new Position(42 + x * 70, 43 + y * 70);
            Check(samples.All(p => generated.Count(r => Contains(r.Boundary, p)) == 1), "Organic biome boundaries cover every sampled point exactly once");
            Check(generated.All(r => Contains(r.Boundary, r.Center)), "Organic borders preserve their region centers");
        }
        foreach (var region in regions)
        {
            Check(Biomes.At(regions, region.Center) == region.Kind, "Region centers match the server biome query");
            var city = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, region.Center);
            var world = new LogisticsWorld([city], 731);
            var before = city.Resources;
            world.AdvanceEconomies(10, 10, 1);
            var output = city.Snapshot().Production;
            Check(city.Resources.Wood - before.Wood == (long)(40 * region.Wood)
                && city.Resources.Iron - before.Iron == (long)(40 * region.Iron)
                && city.Resources.Stone - before.Stone == (long)(40 * region.Stone), "Biome bonuses change actual material accrual");
            Check(output.Single(p => p.Resource == "Wheat").GrossPerMinute == 240 * region.Wheat,
                "Wheat biome bonus is reflected before troop upkeep");
            Check(world.Snapshot(city.Village.OwnerId).Warfare.Settlements.Single().Biome == region.Kind,
                "Public settlement and private production agree on biome");
        }
        var nomad = new VillageEconomy(Guid.NewGuid(), TribeType.Mongols, regions[0].Center);
        var nomadWorld = new LogisticsWorld([nomad], 731);
        nomadWorld.Apply(nomad.Village.OwnerId, new(Guid.NewGuid(), CommandKind.RelocateVillage, nomad.Village.Id,
            X: regions[1].Center.X, Y: regions[1].Center.Y), 0, 0);
        Check(nomad.Biome == regions[1].Kind && nomad.Snapshot().Production.Single(p => p.Resource == "Iron").GrossPerMinute == 240 * regions[1].Iron,
            "Relocating updates the production bonus immediately");

        var home = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(2650, 1500));
        var visualHome = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(800, 800));
        var visualWorld = new LogisticsWorld([visualHome]);
        var visualFormation = new Dictionary<string, int> { ["romans-infantry"] = 20, ["romans-cavalry"] = 31, ["romans-catapult"] = 10, ["romans-ram"] = 1, ["romans-wagon"] = 11 };
        foreach (var (unit, count) in visualFormation) visualHome.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units[unit]), count)]);
        visualWorld.Apply(visualHome.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, visualHome.Village.Id,
            Formation: visualFormation, Wagons: 0, X: 900, Y: 900), 0, 0);
        var ownVisual = visualWorld.Snapshot(visualHome.Village.OwnerId).Armies.Single();
        var enemyVisual = visualWorld.Snapshot(Guid.NewGuid()).Armies.Single();
        foreach (var (unit, figures) in new Dictionary<string, int> { ["romans-infantry"] = 2, ["romans-cavalry"] = 4, ["romans-catapult"] = 1, ["romans-ram"] = 1, ["romans-wagon"] = 2 })
            Check(ownVisual.MapFigures![unit] == figures && enemyVisual.MapFigures![unit] == figures, "Map shows one figure per ten units of each type for both owners and opponents");
        Check(enemyVisual.Formation is null && enemyVisual.Wheat is null && enemyVisual.Morale is null && ownVisual.Formation!["romans-cavalry"] == 31,
            "Public visual groups preserve private exact formations and supply information");
        var logistics = new LogisticsWorld([home]);
        var oasis = logistics.Snapshot(home.Village.OwnerId).Oases.OrderBy(c => LogisticsWorld.Distance(home.Village.Position, c.Position)).First();
        var initial = home.Resources;
        logistics.Apply(home.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
            Formation: new Dictionary<string, int>() { ["romans-infantry"] = 10, ["romans-wagon"] = 1 }, Wagons: 0,
            X: oasis.Position.X, Y: oasis.Position.Y, DestinationId: oasis.Id, AfterAttack: AfterAttack.Return), 0, 0);
        var army = logistics.Snapshot(home.Village.OwnerId).Armies.Single();
        Check(army.Formation!["romans-wagon"] == 1 && home.Available("romans-wagon") == 1, "Unified formation reserves each wagon exactly once");
        Check(army.Wheat >= 110 && initial.Wheat - home.Resources.Wheat == army.Wheat, "Departure loads at least ten minutes of food and debits it exactly once");
        Check(army.Capacity > 1000, "Soldier backpacks contribute to army cargo space");
        for (var i = 1; i <= 80; i++) logistics.Advance(.25, i * .25, 0, 1800);
        Check(logistics.Snapshot(home.Village.OwnerId).Convoys.Length == 0, "Fresh provisions prevent an immediate supply convoy");
        for (var i = 81; i <= 8000 && logistics.Snapshot(home.Village.OwnerId).Armies.Length > 0; i++) logistics.Advance(.25, i * .25, 0, 1800);
        var raid = logistics.Snapshot(home.Village.OwnerId).Reports.Single();
        Check(logistics.Snapshot(home.Village.OwnerId).Armies.Length == 0, "Collect and return completes without another command");
        Check(raid.AttackerLoot.Total == 0, "Occupation grants production rather than carried loot");
        Check(home.Resources.Wood == initial.Wood + raid.AttackerLoot.Wood
            && home.Resources.Iron == initial.Iron + raid.AttackerLoot.Iron
            && home.Resources.Stone == initial.Stone + raid.AttackerLoot.Stone, "Reported materials arrive home exactly once");
        Check(home.Available("romans-infantry") == 20 && home.Available("romans-wagon") == 2, "Raid returns the original formation intact");
        Check(raid.AttackerUnits.Single(u => u.UnitId == "romans-infantry") is { Sent: 10, Dead: 0 }
            && raid.AttackerUnits.Single(u => u.UnitId == "romans-wagon") is { Sent: 1, Destroyed: 0 }, "Reports retain units that suffered no losses");
        Check(raid.AttackerLoad.Loaded > raid.AttackerLoot.Total && raid.AttackerLoad.Loaded <= raid.AttackerLoad.Capacity,
            "Report cargo includes remaining food without exceeding capacity");
        Check(home.OwnedOases==1, "The capturing village retains the production bonus after its army returns");

        var poor = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(2650, 1500));
        poor.Pay(new(0, 0, 0, 1499));
        var poorWorld = new LogisticsWorld([poor]);
        try { poorWorld.Apply(poor.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, poor.Village.Id,
            Formation: new Dictionary<string, int>() { ["romans-infantry"] = 10 }, Wagons: 0, X: 50, Y: 50), 0, 0); throw new Exception("Unfunded march accepted"); }
        catch (GameRuleException e) when (e.Code == "resources") { checks++; }
        Check(poor.Resources.Wheat == 1 && poor.Available("romans-infantry") == 20 && poorWorld.Snapshot(poor.Village.OwnerId).Armies.Length == 0,
            "Insufficient provisions reject atomically");
        foreach (var tribe in Enum.GetValues<TribeType>())
        {
            var svg = XDocument.Parse(GameCatalog.TribeSheet(tribe));
            Check(svg.Root!.Elements().Count() == 14 && svg.Descendants().Count(e => e.Name.LocalName == "title") == 14,
                "Every tribe atlas contains fourteen valid unit illustrations");
        }
        return checks;
    }
}


