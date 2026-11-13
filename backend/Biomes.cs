namespace Warzone;

public enum BiomeType { Meadow, Woodland, Highlands, Grassland, Desert }
public sealed record BiomeRegion(int Id, BiomeType Kind, Position Center, Position[] Boundary, double Wood, double Iron, double Stone, double Wheat);
public static class Biomes
{
    public static double Multiplier(BiomeType biome, ResourceType resource) => (biome, resource) switch
    {
        (BiomeType.Woodland, ResourceType.Wood) => 1.30,
        (BiomeType.Highlands, ResourceType.Iron) => 1.25,
        (BiomeType.Highlands, ResourceType.Stone) => 1.20,
        (BiomeType.Grassland, ResourceType.Wheat) => 1.25,
        (BiomeType.Desert, ResourceType.Wheat) => 1.15,
        (BiomeType.Desert, ResourceType.Stone) => 1.15,
        _ => 1
    };
    public static BiomeRegion[] Generate(int seed)
    {
        var random = new Random(seed);
        var turn = random.Next(4);
        Position Rotate(double x, double y)
        {
            for (var i = 0; i < turn; i++) (x, y) = (4500 - y, x);
            return new(x, y);
        }
        // An open central plain, with three surrounding regions instead of four rectangular quadrants.
        Position[] centers = [Rotate(540 + random.Next(-180, 181), 1912 + random.Next(-360, 361)),
            Rotate(2880 + random.Next(-270, 271), 652 + random.Next(-225, 226)),
            Rotate(3375 + random.Next(-315, 316), 3668 + random.Next(-225, 226)),
            Rotate(1755 + random.Next(-158, 159), 2430 + random.Next(-158, 159))];
        BiomeType[] kinds = [BiomeType.Desert, BiomeType.Highlands, BiomeType.Woodland, BiomeType.Grassland];
        var edges = new Dictionary<string, Position[]>();
        Position Rounded(Position p) => new(Math.Round(p.X, 5), Math.Round(p.Y, 5));
        Position[] NaturalEdge(Position from, Position to)
        {
            from = Rounded(from); to = Rounded(to);
            if (from.X == to.X && from.X is 45 or 4455 || from.Y == to.Y && from.Y is 45 or 4455) return [from, to];
            var reverse = from.X > to.X || from.X == to.X && from.Y > to.Y;
            var a = reverse ? to : from; var b = reverse ? from : to;
            var key = $"{a.X:R},{a.Y:R}:{b.X:R},{b.Y:R}";
            if (!edges.TryGetValue(key, out var points))
            {
                var dx = b.X - a.X; var dy = b.Y - a.Y; var length = Math.Sqrt(dx * dx + dy * dy);
                var phase = random.NextDouble() * Math.PI * 2; var amplitude = Math.Min(155, length * .10);
                var steps = Math.Max(32, (int)Math.Ceiling(length / 36));
                points = Enumerable.Range(0, steps + 1).Select(i => {
                    var t = i / (double)steps;
                    // Shared endpoints and a smooth envelope keep neighboring regions watertight.
                    // Broad bends plus smaller variations avoid a single, regular wave per border.
                    var envelope = Math.Pow(Math.Sin(t * Math.PI), 2);
                    var offset = envelope * (Math.Sin(t * Math.PI * 3 + phase)
                        + .30 * Math.Sin(t * Math.PI * 7 + phase * 1.7)
                        + .12 * Math.Sin(t * Math.PI * 13 + phase * .6)) * amplitude;
                    return new Position(a.X + dx * t - dy / length * offset, a.Y + dy * t + dx / length * offset);
                }).ToArray();
                edges.Add(key, points);
            }
            return reverse ? points.Reverse().ToArray() : points;
        }
        var regions = new List<BiomeRegion>();
        for (var i = 0; i < centers.Length; i++)
        {
            var polygon = new List<Position> { new(45,45), new(4455,45), new(4455,4455), new(45,4455) };
            for (var j = 0; j < centers.Length; j++)
            {
                if (i == j) continue;
                var a = 2 * (centers[j].X - centers[i].X); var b = 2 * (centers[j].Y - centers[i].Y);
                var c = centers[j].X * centers[j].X + centers[j].Y * centers[j].Y - centers[i].X * centers[i].X - centers[i].Y * centers[i].Y;
                var clipped = new List<Position>();
                for (var k = 0; k < polygon.Count; k++)
                {
                    var start = polygon[k]; var finish = polygon[(k + 1) % polygon.Count];
                    var d1 = a * start.X + b * start.Y - c; var d2 = a * finish.X + b * finish.Y - c;
                    if (d1 <= 0) clipped.Add(start);
                    if ((d1 <= 0) != (d2 <= 0)) { var t = d1 / (d1 - d2); clipped.Add(new(start.X + (finish.X - start.X) * t, start.Y + (finish.Y - start.Y) * t)); }
                }
                polygon = clipped;
            }
            var boundary = polygon.SelectMany((p, k) => NaturalEdge(p, polygon[(k + 1) % polygon.Count]).SkipLast(1)).ToArray();
            regions.Add(new(i, kinds[i], centers[i], boundary, Multiplier(kinds[i], ResourceType.Wood), Multiplier(kinds[i], ResourceType.Iron),
                Multiplier(kinds[i], ResourceType.Stone), Multiplier(kinds[i], ResourceType.Wheat)));
        }
        return regions.ToArray();
    }

    public static BiomeType At(BiomeRegion[] regions, Position point)
    {
        foreach (var region in regions)
        {
            var inside = false; var p = region.Boundary;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                if ((p[i].Y > point.Y) != (p[j].Y > point.Y) && point.X < (p[j].X - p[i].X) * (point.Y - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X) inside = !inside;
            if (inside) return region.Kind;
        }
        return regions.OrderBy(r => LogisticsWorld.Distance(r.Center, point)).FirstOrDefault()?.Kind ?? BiomeType.Meadow;
    }
}
