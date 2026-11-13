namespace Warzone;

public sealed record ProductionSnapshot(string Resource, double GrossPerMinute, double UpkeepPerMinute, double NetPerMinute, double Percent, bool Blockaded, bool StorageFull);
public sealed record MusterUnit(string UnitId, int Home, int Deployed, int Escorts, double UpkeepPerMinute);
public sealed record MusterSnapshot(int Total, int Wagons, double UpkeepPerMinute, double HomeUpkeepPerMinute, double FieldUpkeepPerMinute, MusterUnit[] Units);
public sealed record ReportLoss(string UnitId, int Dead, int Wounded, int Fled, int Destroyed);
public sealed record ReportUnit(string UnitId, int Sent, int Dead, int Wounded, int Fled, int Destroyed);
public sealed record ReportLoad(int Wagons, double Capacity, double Loaded);
public sealed record ReportEvent(double At, string Text);
public sealed record ReportArmy(Guid ArmyId, Guid OwnerId, bool Attacking, bool Active, ReportUnit[] Units);
public sealed record CityAttackSnapshot(AttackObjective Objective, string Phase, double Progress, int RemainingBuildingLevels, double Wall, double Gate, double Loyalty);
internal sealed class ReportForce(Army army,bool attacking){public Army Army=army;public bool Attacking=attacking;public Dictionary<string,int> Sent=[];public Dictionary<string,ReportLoss> Losses=[];}
public sealed record CombatReport(Guid Id, string Kind, double StartedAt, double UpdatedAt, Guid Attacker, Guid? Defender,
    Guid? VillageId, Position Position, string Status, Guid? Winner, Resources AttackerLoot, Resources DefenderLoss, Resources DefenderLoot, Resources AttackerResourceLoss,
    ReportLoss[] AttackerLosses, ReportLoss[] DefenderLosses, ReportEvent[] Events, ReportUnit[] AttackerUnits, ReportUnit[] DefenderUnits, ReportLoad AttackerLoad, ReportLoad? DefenderLoad, OasisDefenseSnapshot? OasisDefense, ReportArmy[] Armies, Guid? ParentReportId, CityAttackSnapshot? CityAttack, ReportUnit[] GarrisonUnits);
internal sealed class ReportState(Guid id, string kind, double now, Army attacker, Guid? defender, Guid? villageId, Position position)
{
    public Guid Id = id, Attacker = attacker.OwnerId, ArmyId = attacker.Id;
    public Guid? Defender = defender, VillageId = villageId;
    public string Kind = kind, Status = "Active";
    public Position Position = position;
    public double StartedAt = now, UpdatedAt = now;
    public Guid? Winner;
    public Resources AttackerLoot, DefenderLoss, DefenderLoot, AttackerResourceLoss;
    public Dictionary<string, ReportLoss> AttackerLosses = [], DefenderLosses = [];
    public List<ReportEvent> Events = [];
    public Dictionary<string, int> AttackerSent = [], DefenderSent = [];
    public Dictionary<string, int> GarrisonSent = [];
    public Dictionary<string, ReportLoss> GarrisonLosses = [];
    public ReportLoad AttackerLoad = new(0, 0, 0);
    public ReportLoad? DefenderLoad;
    public OasisDefenseSnapshot? OasisDefense;
    public Guid? ParentReportId;
    public CityAttackSnapshot? CityAttack;
    public List<ReportForce> Forces=[];
    public bool HideDefenderFromAttacker;
    internal static ReportUnit[] Units(Dictionary<string, int> sent, Dictionary<string, ReportLoss> losses) => sent.Keys.Union(losses.Keys).Select(id => {
        var l = losses.GetValueOrDefault(id) ?? new(id, 0, 0, 0, 0);
        return new ReportUnit(id, sent.GetValueOrDefault(id), l.Dead, l.Wounded, l.Fled, l.Destroyed);
    }).ToArray();
    public CombatReport Snapshot(Guid? viewer = null)
    {
        var hideDefender = HideDefenderFromAttacker && viewer == Attacker;
        return new(Id, Kind, StartedAt, UpdatedAt, Attacker, Defender, VillageId, Position, Status, Winner,
            AttackerLoot, hideDefender ? new() : DefenderLoss, hideDefender ? new() : DefenderLoot, AttackerResourceLoss,
            AttackerLosses.Values.ToArray(), hideDefender ? [] : DefenderLosses.Values.ToArray(), Events.ToArray(),
            Units(AttackerSent, AttackerLosses), hideDefender ? [] : Units(DefenderSent, DefenderLosses),
            AttackerLoad, hideDefender ? null : DefenderLoad, OasisDefense,
            Forces.Where(f => !hideDefender || f.Attacking).OrderByDescending(f => f.Attacking).Select(f => new ReportArmy(f.Army.Id, f.Army.OwnerId, f.Attacking, Status == "Active" && !f.Army.Routing && f.Army.Order is "Battle" or "Sieging" or "Occupying" or "Blockading" or "Guarding", Units(f.Sent, f.Losses))).ToArray(),
            ParentReportId, CityAttack, hideDefender ? [] : Units(GarrisonSent, GarrisonLosses));
    }
}

public sealed partial class LogisticsWorld
{
    public const double MapSize = 4500, MapCenter = 2250, MapMinimum = 45, MapMaximum = 4455, SettlementRadius = 1750;
    private readonly List<ReportState> reports = [];
    private ReportState BeginReport(Guid id, string kind, double now, Army army, Guid? defender, Guid? village, Position position)
    {
        var report = new ReportState(id, kind, now, army, defender, village, position);
        if(kind=="Battle")report.ParentReportId=oasisSieges.Find(s=>s.Status=="Active"&&(s.Attackers.Contains(army)||army.DefendingSiteId==s.Oasis.Id))?.Id
            ?? sieges.Find(s=>s.Status=="Active"&&(s.Attackers.Contains(army)||army.DefendingSiteId==s.City.Village.Id))?.Id;
        report.AttackerSent = army.Troops.ToDictionary(t => t.Unit.UnitId, t => t.Count);
        var opponent = battles.Find(b => b.Id == id)?.Defender;
        var defenders = opponent?.Troops ?? (village is { } city ? villages[city].Garrison() : []);
        report.DefenderSent = defenders.ToDictionary(t => t.Unit.UnitId, t => t.Count);
        if(village is not null) report.GarrisonSent = new(report.DefenderSent);
        report.DefenderLoad = opponent is null ? null : Load(opponent);
        AddReportArmy(report,army,true);
        if(opponent is not null)AddReportArmy(report,opponent,false);
        reports.Add(report);
        // Retain at least 40 recent reports per participant without discarding active engagements.
        var expired = reports.Where(r => r.Status != "Active"
            && reports.Count(n => n.StartedAt > r.StartedAt && (n.Attacker == r.Attacker || n.Defender == r.Attacker)) > 40
            && (r.Defender is null || reports.Count(n => n.StartedAt > r.StartedAt && (n.Attacker == r.Defender || n.Defender == r.Defender)) > 40)).Select(r => r.Id).ToHashSet();
        reports.RemoveAll(r => expired.Contains(r.Id));
        return report;
    }
    private static ReportLoad Load(Army army) => new(WagonCount(army.Troops), army.Capacity, army.Wheat + army.Loot.Total);
    private static void TrackCommitted(IEnumerable<TroopStack> troops, Dictionary<string, int> sent, Dictionary<string, ReportLoss> losses)
    {
        foreach (var stack in troops)
        {
            var loss = losses.GetValueOrDefault(stack.Unit.UnitId);
            var committed = stack.Count + (loss is null ? 0 : loss.Dead + loss.Wounded + loss.Fled + loss.Destroyed);
            sent[stack.Unit.UnitId] = Math.Max(sent.GetValueOrDefault(stack.Unit.UnitId), committed);
        }
    }
    private void ReportEvent(Guid id, double now, string text, string? status = null, Guid? winner = null)
    {
        var report = reports.Find(r => r.Id == id); if (report is null) return;
        report.UpdatedAt = now;
        if (sieges.Find(s=>s.Id==id) is { } siege)
            report.CityAttack=new(siege.Objective,siege.Phase,siege.Progress,siege.City.Village.Buildings.Sum(b=>b.Level),siege.Wall,siege.Gate,siege.Loyalty);
        RefreshReportForces(report);
        if (report.VillageId is { } city && villages[city].Village.OwnerId == report.Defender)
            TrackCommitted(villages[city].Garrison(), report.GarrisonSent, report.GarrisonLosses);
        RefreshReportForces(report);
        if (status is not null) { report.Status = status; report.Winner = winner; }
        report.Events.Add(new(now, text));
        if (report.Events.Count > 64) report.Events.RemoveAt(1);
    }
    private void ReportLosses(Army force, BaseUnit unit, int dead, int wounded, int fled, int destroyed, bool deployed)
    {
        foreach(var report in reports.Where(r=>r.Status=="Active"&&(r.Forces.Any(f=>f.Army==force)||!deployed&&r.VillageId==force.HomeVillageId)))
        {
            var participant=report.Forces.Find(f=>f.Army==force);
            var attacking=participant?.Attacking??force.OwnerId==report.Attacker;
            var losses=!deployed?report.GarrisonLosses:attacking?report.AttackerLosses:report.DefenderLosses;
            var sent=!deployed?report.GarrisonSent:attacking?report.AttackerSent:report.DefenderSent;
            var old=losses.GetValueOrDefault(unit.UnitId)??new(unit.UnitId,0,0,0,0);
            if(participant is not null){TrackCommitted(force.Troops,participant.Sent,participant.Losses);var previous=participant.Losses.GetValueOrDefault(unit.UnitId)??new(unit.UnitId,0,0,0,0);participant.Losses[unit.UnitId]=previous with{Dead=previous.Dead+dead,Wounded=previous.Wounded+wounded,Fled=previous.Fled+fled,Destroyed=previous.Destroyed+destroyed};}
            else sent[unit.UnitId]=Math.Max(sent.GetValueOrDefault(unit.UnitId),old.Dead+old.Wounded+old.Fled+old.Destroyed+force.Troops.Where(t=>t.Unit.UnitId==unit.UnitId).Sum(t=>t.Count));
            losses[unit.UnitId]=old with{Dead=old.Dead+dead,Wounded=old.Wounded+wounded,Fled=old.Fled+fled,Destroyed=old.Destroyed+destroyed};
        }
    }
    private void AddReportArmy(ReportState report,Army army,bool attacking)
    {
        if(report.Forces.Any(f=>f.Army==army))return;
        if(report.ParentReportId is { } parentId && reports.Find(r=>r.Id==parentId) is { } parent)
        {
            var parentAttacking = parent.Defender is { } parentDefender
                ? army.OwnerId != parentDefender && army.SupportedOwnerId != parentDefender && !CampAllied(army.OwnerId, parentDefender)
                : army.OwnerId == parent.Attacker || CampAllied(army.OwnerId, parent.Attacker);
            AddReportArmy(parent, army, parentAttacking);
        }
        var force=new ReportForce(army,attacking);
        TrackCommitted(army.Troops,force.Sent,force.Losses);
        // Keep attacking forces grouped above defending forces while preserving join order per side.
        var insertAt=attacking?report.Forces.FindLastIndex(f=>f.Attacking)+1:report.Forces.Count;
        report.Forces.Insert(insertAt,force);
        RefreshReportForces(report);
    }
    private void RefreshReportForces(ReportState report)
    {
        foreach(var force in report.Forces)TrackCommitted(force.Army.Troops,force.Sent,force.Losses);
        foreach(var attacking in new[]{true,false})
        {
            var side=report.Forces.Where(f=>f.Attacking==attacking).ToArray();if(side.Length==0 && (attacking || report.VillageId is null))continue;
            var totals=side.SelectMany(f=>f.Sent).GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.Sum(p=>p.Value));
            var load=new ReportLoad(side.Sum(f=>WagonCount(f.Army.Troops)),side.Sum(f=>f.Army.Capacity),side.Sum(f=>f.Army.Wheat+f.Army.Loot.Total));
            if(attacking){report.AttackerSent=totals;report.AttackerLoad=load;}else{report.DefenderSent=totals;report.DefenderLoad=load;}
            if(!attacking && report.VillageId is not null)
            {
                report.DefenderSent=report.GarrisonSent.Concat(totals).GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.Sum(p=>p.Value));
                report.DefenderLosses=report.GarrisonLosses.Values.Concat(side.SelectMany(f=>f.Losses.Values)).GroupBy(l=>l.UnitId)
                    .ToDictionary(g=>g.Key,g=>new ReportLoss(g.Key,g.Sum(l=>l.Dead),g.Sum(l=>l.Wounded),g.Sum(l=>l.Fled),g.Sum(l=>l.Destroyed)));
            }
        }
    }
    private static string ResourceText(Resources r) => $"{r.Wood} wood, {r.Iron} iron, {r.Stone} stone, {r.Wheat} wheat";
    private static Resources Carried(Army army) => army.Loot + new Resources(0, 0, 0, (long)army.Wheat);
    private static Resources Gained(Resources before, Resources after) => new(Math.Max(0, after.Wood - before.Wood), Math.Max(0, after.Iron - before.Iron), Math.Max(0, after.Stone - before.Stone), Math.Max(0, after.Wheat - before.Wheat));
    public EconomySnapshot? EconomySnapshot(Guid viewer, Guid? selectedVillage, double resourceSpeed)
    {
        var city = Selected(viewer, selectedVillage); if (city is null) return null;
        return city.Snapshot(resourceSpeed);
    }
    private MusterSnapshot Muster(Guid viewer)
    {
        var home = villages.Values.Where(v => v.Village.OwnerId == viewer && !v.Ruined).SelectMany(v => v.Garrison())
            .Concat(marketOffers.Where(o => o.OwnerId == viewer).SelectMany(o => o.Wagons)).ToArray();
        var deployed = armies.Where(a => a.OwnerId == viewer).SelectMany(a => a.Troops).ToArray();
        var escorts = convoys.Where(c => c.OwnerId == viewer).SelectMany(c => c.Troops).ToArray();
        var units = home.Concat(deployed).Concat(escorts).Select(t => t.Unit.UnitId).Distinct().Order().Select(id =>
            new MusterUnit(id, home.Where(t => t.Unit.UnitId == id).Sum(t => t.Count), deployed.Where(t => t.Unit.UnitId == id).Sum(t => t.Count),
                escorts.Where(t => t.Unit.UnitId == id).Sum(t => t.Count), GameCatalog.Units[id].Stats.WheatUpkeep)).ToArray();
        var homeFood = units.Sum(u => u.Home * u.UpkeepPerMinute);
        var fieldFood = units.Sum(u => (u.Deployed + u.Escorts) * u.UpkeepPerMinute);
        return new(units.Sum(u => u.Home + u.Deployed + u.Escorts),
            units.Where(u => GameCatalog.Units[u.UnitId].Role == UnitRole.Wagon).Sum(u => u.Home + u.Deployed + u.Escorts),
            homeFood + fieldFood, homeFood, fieldFood, units);
    }
}
