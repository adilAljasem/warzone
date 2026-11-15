namespace Warzone;

public sealed record OasisDefenseSnapshot(Guid OasisId, int Level, double Health, double Maximum, bool Blockaded, OasisMode Mode, int Round);
public sealed record OasisSiegeSnapshot(Guid Id, Guid ArmyId, Guid OasisId, Guid AttackerOwner, Guid DefenderOwner,
    Position Position, double Elapsed, OasisMode Mode, double Wall, double WallMaximum, int Round, Guid[] ArmyIds, Guid[] AttackerOwners);
internal sealed class OasisSiegeState(Army army, ResourceOasis oasis, double now)
{
    public Guid Id { get; } = Guid.NewGuid();
    public List<Army> Attackers { get; } = [army];
    public Army Attacker => Attackers.FirstOrDefault(a => a.Troops.Count > 0 && !a.Routing && a.Order is "Occupying" or "Blockading" or "Burning" or "Battle" && a.TargetId == Oasis.Id) ?? Attackers[0];
    public OasisMode Mode => Attackers.Any(a=>a.OasisMode==OasisMode.Capture&&a.Order is "Occupying" or "Battle")?OasisMode.Capture:Attackers.Any(a=>a.OasisMode==OasisMode.Burn&&a.Order is "Occupying" or "Burning" or "Battle")?OasisMode.Burn:OasisMode.Blockade;
    public ResourceOasis Oasis { get; } = oasis;
    public Guid Defender { get; } = oasis.OwnerId!.Value;
    public double Started { get; } = now;
    public double NextRound { get; set; } = now+5;
    public int Round { get; set; }
    public string Status { get; set; } = "Active";
}
public sealed partial class LogisticsWorld
{
    private readonly List<OasisSiegeState> oasisSieges=[];
    private bool ValidOasisArmy(OasisSiegeState s,Army a)=>armies.Contains(a)&&!a.Routing&&Fighters(a)>0&&a.Order is "Occupying" or "Blockading" or "Burning" or "Battle"&&a.TargetId==s.Oasis.Id&&a.OwnerId!=s.Defender&&Distance(a.Position,s.Oasis.Position)<=20;
    private bool ValidOasisSiege(OasisSiegeState s)=>s.Status=="Active"&&s.Oasis.OwnerId==s.Defender&&s.Attackers.Any(a=>ValidOasisArmy(s,a));
    private bool OasisBlockaded(ResourceOasis oasis)=>oasisSieges.Any(s=>s.Oasis==oasis&&ValidOasisSiege(s));
    private void BeginOasisSiege(Army army,ResourceOasis oasis,double now)
    {
        var ongoing=oasisSieges.Find(s=>s.Oasis==oasis&&s.Status=="Active");
        if(ongoing is not null){if(!ongoing.Attackers.Contains(army)){ongoing.Attackers.Add(army);AddReportArmy(reports.Single(r=>r.Id==ongoing.Id),army,true);ReportEvent(ongoing.Id,now,"Another army joined the oasis siege.");}return;}
        var siege=new OasisSiegeState(army,oasis,now);oasisSieges.Add(siege);
        var report=BeginReport(siege.Id,"OasisSiege",now,army,oasis.OwnerId,null,oasis.Position);
        TrackOasisDefenders(siege,report);
        report.OasisDefense=new(oasis.Id,oasis.WallLevel,oasis.WallHealth,oasis.WallMaximum,true,army.OasisMode,0);
        ReportEvent(siege.Id,now,"Oasis surrounded. Resource collection and village production bonuses are suspended.");
        Note(siege.Defender,now,"Your oasis is under siege. Collection and production bonuses are suspended.","oasis-attack",targetId:oasis.Id);
        RefreshOasisBonuses();
    }
    private void TrackOasisDefenders(OasisSiegeState siege,ReportState report)
    {
        foreach(var guard in armies.Where(a=>a.DefendingSiteId==siege.Oasis.Id&&a.SupportedOwnerId==siege.Defender&&Distance(a.Position,siege.Oasis.Position)<=45))AddReportArmy(report,guard,false);
        RefreshReportForces(report);
    }
    private void EndOasisSiege(OasisSiegeState siege,string status,double now,Guid? winner=null)
    {
        if(siege.Status!="Active")return;
        siege.Status=status;
        var report=reports.Find(r=>r.Id==siege.Id);
        if(report is not null) report.OasisDefense=new(siege.Oasis.Id,siege.Oasis.WallLevel,siege.Oasis.WallHealth,siege.Oasis.WallMaximum,false,siege.Mode,siege.Round);
        ReportEvent(siege.Id,now,status=="Captured"?"Oasis captured. Its wall is destroyed.":"The oasis siege has ended. Collection and production bonuses resume.",status,winner);
        if(status!="Captured")Note(siege.Defender,now,"The oasis siege has ended. Collection and production bonuses resume.","reinforcement",targetId:siege.Oasis.Id);
    }
    private void AdvanceOasisSieges(double now)
    {
        foreach(var siege in oasisSieges.Where(s=>s.Status=="Active").ToArray())
        {
            if(!ValidOasisSiege(siege)){EndOasisSiege(siege,siege.Attacker.Routing?"Repelled":"Withdrawn",now,siege.Defender);continue;}
            var report=reports.Find(r=>r.Id==siege.Id);if(report is null)continue;
            TrackOasisDefenders(siege,report);
            if(now>=siege.NextRound){siege.NextRound=now+5;siege.Round++;ReportEvent(siege.Id,now,siege.Mode==OasisMode.Blockade?"Blockade continues. Resource collection and village bonuses remain suspended.":$"Oasis wall durability: {Math.Ceiling(siege.Oasis.WallHealth)} / {siege.Oasis.WallMaximum}.");}
            report.OasisDefense=new(siege.Oasis.Id,siege.Oasis.WallLevel,siege.Oasis.WallHealth,siege.Oasis.WallMaximum,true,siege.Mode,siege.Round);
        }
        oasisSieges.RemoveAll(s=>s.Status!="Active");
        RefreshOasisBonuses();
    }
    private OasisSiegeSnapshot[] OasisSiegeViews()=>oasisSieges.Where(ValidOasisSiege).Select(s=>new OasisSiegeSnapshot(s.Id,s.Attacker.Id,s.Oasis.Id,s.Attacker.OwnerId,s.Defender,s.Oasis.Position,warTime-s.Started,s.Mode,s.Oasis.WallHealth,s.Oasis.WallMaximum,s.Round,s.Attackers.Where(a=>ValidOasisArmy(s,a)).Select(a=>a.Id).ToArray(),s.Attackers.Where(a=>ValidOasisArmy(s,a)).Select(a=>a.OwnerId).Distinct().ToArray())).ToArray();
    private ResourceOasis? OasisFoodSource(Army army)=>army.Order is "Guarding" or "Battle" or "Reinforcing"
        ? oases.Find(o=>o.Id==army.DefendingSiteId&&o.OwnerId==army.OwnerId&&army.SupportedOwnerId==o.OwnerId&&Distance(o.Position,army.Position)<=4):null;
    private double FeedArmy(Army army,double demand)
    {
        if(OasisFoodSource(army) is { } oasis)
        {
            var local=Math.Min(oasis.Stockpile[3],(decimal)demand);oasis.Stockpile[3]-=local;demand-=(double)local;
        }
        var carried=Math.Min(army.Wheat,demand);army.Wheat-=carried;
        return demand-carried;
    }
}
