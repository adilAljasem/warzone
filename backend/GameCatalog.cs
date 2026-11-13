using System.Collections.Frozen;
using System.Text.Json;

namespace Warzone;

// Centralized data-driven balance settings loaded from units.json.
// All tribe/unit content lives in units.json; editing unit stats or matchups never requires code changes.
public static class GameCatalog
{
    public static IReadOnlyDictionary<TribeType, TribeDefinition> Tribes { get; } = new[]
    {
        new TribeDefinition(TribeType.Romans, "Romans", "/assets/tribes/romans.svg", new(ExtraAutoQueueSlots: 1)),
        new TribeDefinition(TribeType.Germans, "Germans", "/assets/tribes/germans.svg",
            new(TrainingCostMultiplier: .8, TrainingTimeMultiplier: .7, LootMultiplier: 1.2)),
        new TribeDefinition(TribeType.Greeks, "Greeks", "/assets/tribes/greeks.svg", new(WallMultiplier: 2, HealingMultiplier: 2)),
        new TribeDefinition(TribeType.Mongols, "Mongols / Nomads", "/assets/tribes/mongols.svg", new(CanRelocate: true)),
        new TribeDefinition(TribeType.Persians, "Persians / Babylonians", "/assets/tribes/persians.svg",
            new(TrainingTimeMultiplier: .5, ResourceMultiplier: 1.2))
    }.ToFrozenDictionary(x => x.Type);

    private sealed record JsonCost(long Wood, long Iron, long Stone, long Wheat);
    private sealed record JsonStats(double Attack, double DefenseInfantry, double DefenseCavalry,
        double Speed, int CarryCapacity, double WheatUpkeep, double HitPoints);
    private sealed record JsonMatchupEntry(Dictionary<string, double>? Attack, Dictionary<string, double>? Defense);
    private sealed record JsonUnitEntry(string UnitId, string Tribe, string Role, string DisplayName,
        string? TrainingBuilding, int? RequiredBuildingLevel, double TrainingSeconds, JsonCost Cost, JsonStats Stats,
        Dictionary<string, double>? AttackMatchups = null, Dictionary<string, double>? DefenseMatchups = null);
    private sealed record JsonCatalogFile(Dictionary<string, JsonMatchupEntry>? Matchups, JsonUnitEntry[] Units);

    public static IReadOnlyDictionary<string, UnitDefinition> Units { get; } = BuildUnits();

    private static string ResolveUnitsJsonPath()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "units.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "units.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "backend", "units.json"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "units.json")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "backend", "units.json"))
        ];
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Could not find units.json catalog file.");
    }

    private static IReadOnlyDictionary<UnitRole, double> ParseMatchups(Dictionary<string, double>? raw)
    {
        var dict = new Dictionary<UnitRole, double>();
        if (raw is null) return dict;
        foreach (var (key, value) in raw)
            if (Enum.TryParse<UnitRole>(key, true, out var role))
                dict[role] = value;
        return dict;
    }

    private static IReadOnlyDictionary<string, UnitDefinition> BuildUnits()
    {
        var path = ResolveUnitsJsonPath();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var catalog = JsonSerializer.Deserialize<JsonCatalogFile>(File.ReadAllText(path), options)
            ?? throw new InvalidOperationException("Invalid units.json file.");
        var units = new List<UnitDefinition>();
        foreach (var entry in catalog.Units)
        {
            var tribe = Enum.Parse<TribeType>(entry.Tribe, true);
            var role = Enum.Parse<UnitRole>(entry.Role, true);
            var facility = !string.IsNullOrWhiteSpace(entry.TrainingBuilding)
                ? Enum.Parse<BuildingType>(entry.TrainingBuilding, true)
                : TrainingRules.Facility(role);
            var requiredLevel = entry.RequiredBuildingLevel ?? TrainingRules.RequiredLevel(role);
            var roleMatchup = catalog.Matchups is not null && catalog.Matchups.TryGetValue(role.ToString(), out var m) ? m : null;
            var attackMatchups = ParseMatchups(entry.AttackMatchups ?? roleMatchup?.Attack);
            var defenseMatchups = ParseMatchups(entry.DefenseMatchups ?? roleMatchup?.Defense);
            var displayMatchups = role == UnitRole.Spearman && defenseMatchups.Count > 0 ? defenseMatchups : attackMatchups;
            var stats = new UnitStats(
                entry.Stats.Attack,
                entry.Stats.DefenseInfantry, entry.Stats.DefenseCavalry,
                entry.Stats.Speed, entry.Stats.CarryCapacity,
                entry.Stats.WheatUpkeep, entry.Stats.HitPoints);
            var cost = new Resources(entry.Cost.Wood, entry.Cost.Iron, entry.Cost.Stone, entry.Cost.Wheat);
            var id = entry.UnitId.Trim().ToLowerInvariant();
            units.Add(new(id, tribe, role, entry.DisplayName, $"/assets/units/{id}/icon.svg",
                $"/assets/units/{id}/map.svg", stats, cost, TimeSpan.FromSeconds(entry.TrainingSeconds),
                displayMatchups, facility, requiredLevel, attackMatchups, defenseMatchups));
        }
        return units.ToFrozenDictionary(x => x.UnitId, StringComparer.Ordinal);
    }

    public static string UnitSprite(UnitDefinition unit)
    {
        var color = unit.TribeType switch { TribeType.Romans => "#c85f53", TribeType.Germans => "#65935b", TribeType.Greeks => "#528eb1", TribeType.Mongols => "#d29c44", _ => "#9b78b2" };
        var helm = unit.TribeType switch
        {
            TribeType.Romans => "<path d='M39 25V12h8v13' fill='#b8473c'/>",
            TribeType.Greeks => "<path d='M34 22Q40 2 52 15L48 24' fill='#dbb552'/>",
            TribeType.Mongols => "<path d='m32 28 11-18 11 18Z' fill='#8e6a46'/>",
            TribeType.Persians => "<ellipse cx='43' cy='25' rx='13' ry='9' fill='#eadbb9'/>",
            _ => "<path d='m34 23-8-7 3 13m23-6 8-7-3 13' fill='#e9d8ab'/>"
        };
        var person = $"<path d='M36 57v19m14-19v19' stroke='#65563f' stroke-width='7'/><rect x='30' y='37' width='26' height='27' rx='7' fill='{color}'/><circle cx='43' cy='30' r='11' fill='#efc493'/><path d='M31 28a12 12 0 0 1 24 0Z' fill='#c8d5d6'/>{helm}";
        person += $"<path d='M30 44 34 38l3 16-7 2Zm26 0-4-6-3 16 7 2Z' fill='#9eaaa8'/><path d='M31 58h24v5H31Z' fill='#72543c'/><rect x='41' y='58' width='5' height='5' rx='1' fill='#e4bd64'/><ellipse cx='39' cy='31' rx='1.5' ry='2' fill='#283d3a'/><ellipse cx='47' cy='31' rx='1.5' ry='2' fill='#283d3a'/><path d='M40 36q3 3 6 0' fill='none' stroke='#a86d51' stroke-width='1.2'/><path d='M30 27q12-17 26 0' fill='none' stroke='#edf2da' stroke-width='2'/>";
        var art = unit.Role switch
        {
            UnitRole.Wagon => $"<circle cx='27' cy='71' r='11' fill='#755538'/><circle cx='68' cy='71' r='11' fill='#755538'/><rect x='16' y='39' width='65' height='31' rx='4' fill='#b8864a'/><path d='M20 43V32Q48 8 77 32v11Z' fill='#f7e6b4'/><path d='M48 20v24' stroke='{color}' stroke-width='10'/>",
            UnitRole.Catapult => $"<path d='M17 70h64M29 68l20-31 20 31' stroke='#947045' stroke-width='8' fill='none'/><path d='m25 29 46 29' stroke='#bd934e' stroke-width='7'/><circle cx='25' cy='23' r='10' fill='#aab2aa'/><circle cx='25' cy='76' r='8' fill='#715536'/><circle cx='73' cy='76' r='8' fill='#715536'/><path d='M48 41v17' stroke='{color}' stroke-width='8'/>",
            UnitRole.Ram => $"<path d='M19 73 32 32h31l16 41' fill='none' stroke='#947045' stroke-width='7'/><path d='m17 37 30-18 34 18Z' fill='{color}'/><path d='M12 56h68' stroke='#bb975e' stroke-width='14'/><rect x='74' y='47' width='13' height='19' rx='3' fill='#c3cbc6'/><circle cx='25' cy='77' r='7' fill='#715536'/><circle cx='72' cy='77' r='7' fill='#715536'/>",
            UnitRole.Cavalry when unit.TribeType == TribeType.Persians => $"<path d='M17 68V49q0-19 22-19h28q19 0 19 23v24h-8V54h-8v25H59V65H35v14H23V65Z' fill='#9baca8'/><ellipse cx='65' cy='43' rx='13' ry='17' fill='#b5c2b8'/><path d='M69 55q12 9 18-1' fill='none' stroke='#f8ebca' stroke-width='4'/><rect x='27' y='33' width='29' height='21' fill='{color}'/><circle cx='42' cy='22' r='8' fill='#efc493'/>",
            UnitRole.Cavalry or UnitRole.LightCavalry or UnitRole.HeavyCavalry => $"<path d='M19 64h45l12-25-16-9-8 21H24Z' fill='#aa774b'/><path d='M24 63v17m35-17v17' stroke='#795539' stroke-width='6'/><g transform='translate(12,-2) scale(.65)'>{person}</g><path d='M75 21v35' stroke='#b1c6c8' stroke-width='4'/>",
            UnitRole.Swordsman => $"{person}<path d='M66 72V19l5-9 5 9-6 53Z' fill='#d8e5e0'/><path d='M59 64h20' stroke='#d7b356' stroke-width='4'/><path d='M12 44h21v25H12Z' fill='#95aaa6'/>",
            UnitRole.Scout when unit.TrainingBuilding == BuildingType.Stable => $"<path d='M19 64h45l12-25-16-9-8 21H24Z' fill='#b68456'/><path d='M24 63v17m35-17v17' stroke='#795539' stroke-width='6'/><g transform='translate(12,-2) scale(.65)'>{person}</g><path d='M55 29 77 19' stroke='#655e49' stroke-width='6'/><circle cx='78' cy='18' r='4.5' fill='#c9dfdc'/>",
            UnitRole.Scout => $"{person}<path d='M55 43 78 32' stroke='#655e49' stroke-width='8'/><circle cx='79' cy='31' r='5' fill='#c9dfdc'/>",
            UnitRole.Governor => $"{person}<path d='M30 23 27 11l10 6 6-11 7 11 10-6-5 12Z' fill='#e7c163'/><path d='M65 75V29m-5 0h10' stroke='#d3b25d' stroke-width='4'/>",
            _ => $"{person}<path d='M68 77V19' stroke='#9b7844' stroke-width='3'/><path d='m63 20 5-13 5 13Z' fill='#d4e3df'/><path d='m22 43 12 5v17l-12 10-10-10V48Z' fill='#e6c16e'/>"
        };
        if(unit.Role==UnitRole.Spearman)art+="<path d='M80 79V9m-5 6 5-13 5 13Z' fill='#dce9de' stroke='#af9468' stroke-width='2'/><rect x='12' y='42' width='24' height='31' rx='5' fill='#667e75' stroke='#d6c28b' stroke-width='3'/>";
        if(unit.Role==UnitRole.LightCavalry)art+="<path d='m18 51 33 5 9-8' fill='none' stroke='#e1bd76' stroke-width='3'/><path d='m57 23 26-9' stroke='#d2ba81' stroke-width='2'/>";
        if(unit.Role==UnitRole.HeavyCavalry)art+="<path d='m22 52 34-3 13-13 8 5-13 23H22Z' fill='#a0b2b5' stroke='#607b7e' stroke-width='2'/><path d='M28 53v10m8-11v11m9-12v12' stroke='#d7e1d3' stroke-width='2'/>";
        return $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 96 96'><title>{System.Net.WebUtility.HtmlEncode(unit.DisplayName)}</title><ellipse cx='48' cy='82' rx='36' ry='8' fill='#6a8b55' opacity='.2'/>{art}</svg>";
    }

    public static string TribeSheet(TribeType tribe)
    {
        var units = Units.Values.Where(u => u.TribeType == tribe).OrderBy(u => u.Role).ToArray();
        var cells = units.Select((u, i) => $"<svg x='{i % 3 * 96}' y='{i / 3 * 96}' width='96' height='96' viewBox='0 0 96 96'>{UnitSprite(u)}</svg>");
        return $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 288 {Math.Ceiling(units.Length / 3d) * 96}'>{string.Join("", cells)}</svg>";
    }

    public static void Validate()
    {
        if (Tribes.Count != 5) throw new InvalidOperationException("Exactly five tribes are required.");
        foreach (var unit in Units.Values)
        {
            unit.Validate();
            if (!Tribes.ContainsKey(unit.TribeType)) throw new InvalidOperationException("Unknown tribe.");
        }
        UnitRole[] sharedRoles = [UnitRole.Infantry, UnitRole.Scout, UnitRole.Wagon, UnitRole.Catapult, UnitRole.Ram, UnitRole.Governor, UnitRole.Spearman, UnitRole.Swordsman, UnitRole.HeavyCavalry];
        foreach (var tribe in Tribes.Keys)
        {
            var roles = Units.Values.Where(x => x.TribeType == tribe).Select(x => x.Role).ToHashSet();
            var mount = tribe == TribeType.Persians ? UnitRole.Cavalry : UnitRole.LightCavalry;
            if (roles.Count != 10 || !sharedRoles.All(roles.Contains) || !roles.Contains(mount))
                throw new InvalidOperationException("Incomplete unit roles.");
        }
    }
}
