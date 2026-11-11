using Warzone;

internal static class OasisChecks
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        foreach (var count in new[] { 8, 16 })
        {
            var cities = Enumerable.Range(0, count).Select(i => new VillageEconomy(Guid.NewGuid(), TribeType.Romans,
                new(1500 + Math.Cos(i * Math.PI * 2 / count) * 1150, 1500 + Math.Sin(i * Math.PI * 2 / count) * 1150))).ToArray();
            for (var seed = 1; seed <= 8; seed++)
            {
                var world = new LogisticsWorld(cities, seed);
                var regions = world.Snapshot(cities[0].Village.OwnerId).Regions;
                bool Contains(BiomeRegion region, Position point)
                {
                    var inside = false; var p = region.Boundary;
                    for (int a = 0, b = p.Length - 1; a < p.Length; b = a++)
                        if ((p[a].Y > point.Y) != (p[b].Y > point.Y) && point.X < (p[b].X - p[a].X) * (point.Y - p[a].Y) / (p[b].Y - p[a].Y) + p[a].X) inside = !inside;
                    return inside;
                }
                Check(Enumerable.Range(0, 20).SelectMany(x => Enumerable.Range(0, 20).Select(y => new Position(57 + x * 145, 61 + y * 145)))
                    .All(p => regions.Count(r => Contains(r, p)) == 1), "Natural biome borders cover the world without gaps or overlaps");
                var sites = world.Snapshot(cities[0].Village.OwnerId).Oases;
                Check(sites.Length == Math.Clamp(32-count,16,24), "Spacious worlds place the full population-adjusted set of oases");
                Check(sites.Select(c => c.Resource).Distinct().Count() == 4, "All four resource specialties are available");
                Check(sites.All(c => cities.All(v => LogisticsWorld.Distance(v.Village.Position, c.Position) >= 380)), "Oases do not appear at settlement gates");
                Check(sites.All(c => sites.All(o => o.Id == c.Id || LogisticsWorld.Distance(o.Position, c.Position) >= 330)), "Oases have distinct selectable clearings");
                Check(sites.All(c => cities.All(v => double.Hypot(v.Village.Position.X-c.Position.X,(v.Village.Position.Y-c.Position.Y)*.65) >= 275)), "Projected village and oasis artwork stays separated");
                Check(sites.All(c => sites.All(o => o.Id == c.Id || double.Hypot(o.Position.X-c.Position.X,(o.Position.Y-c.Position.Y)*.65) >= 235)), "Projected oasis artwork stays separated");
                Check(sites.All(c => c.Position.X is >= 120 and <= 2880 && c.Position.Y is >= 120 and <= 2880), "All oases are inside playable bounds");
                Check(sites.All(c=>c.OwnerId is null && c.VillageId is null && c.Defenders==0), "Oases begin neutral and unguarded");
                var repeated = new LogisticsWorld(cities, seed).Snapshot(cities[0].Village.OwnerId).Oases;
                Check(sites.Select(c => (c.Position, c.Resource)).SequenceEqual(repeated.Select(c => (c.Position, c.Resource))), "Terrain seed reproduces oasis positions and types");
                var changed = new LogisticsWorld(cities, seed + 1).Snapshot(cities[0].Village.OwnerId).Oases;
                Check(!sites.Select(c => c.Position).SequenceEqual(changed.Select(c => c.Position)), "New worlds scatter oases differently");
            }
        }
        return checks;
    }
}

