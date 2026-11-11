namespace Warzone;

internal sealed class BattleMember(Army army, Convoy? convoy = null)
{
    public Army Army { get; } = army;
    public Convoy? Convoy { get; } = convoy;
    public string Order { get; } = army.Order;
    public Guid? Target { get; } = army.TargetId;
    public bool Withdrawn { get; set; }
}

public sealed partial class LogisticsWorld
{
    private static bool Allied(Army a, Army b) => a.OwnerId == b.OwnerId
        || a.DefendingSiteId is not null && a.DefendingSiteId == b.DefendingSiteId && a.SupportedOwnerId == b.SupportedOwnerId;
    private static Army[] Fighting(IEnumerable<BattleMember> members) => members.Where(m=>!m.Withdrawn && !m.Army.Routing && Count(m.Army)>0 && m.Army.Morale>0).Select(m=>m.Army).ToArray();
    private void JoinBattle(BattleState battle, Army army, bool attacking, double now)
    {
        if(battle.Contains(army)||army.Routing||Fighters(army)==0||battles.Any(b=>b.Status=="Active"&&b.Contains(army)))return;
        var side=attacking?battle.Attackers:battle.Defenders;
        if(!side.Any(m=>Allied(m.Army,army)))return;
        side.Add(new(army));army.Order="Battle";
        AddReportArmy(reports.Single(r=>r.Id==battle.Id),army,attacking);
        ReportEvent(battle.Id,now,"Another army joined the battle.");
    }
    private void JoinNearbyForces(BattleState battle,double now)
    {
        foreach(var army in armies.Where(a=>!a.Routing&&a.Order is "Idle" or "Guarding" or "Reinforcing" or "Occupying" or "Blockading" or "Attacking" or "Approaching" or "Sieging").ToArray())
        {
            if(battle.Contains(army))continue;
            var attack=battle.Attackers.Any(m=>Allied(army,m.Army));
            var defense=battle.Defenders.Any(m=>Allied(army,m.Army));
            if(attack==defense)continue;
            var side=attack?battle.Attackers:battle.Defenders;
            if(side.Any(m=>Distance(army.Position,m.Army.Position)<=45))JoinBattle(battle,army,attack,now);
        }
    }
    private double GroupPower(Army[] own,Army[] enemy,bool attack)
    {
        var total=Math.Max(1,enemy.Sum(Count));
        return own.Sum(a=>enemy.Sum(e=>Power(a,e,attack)*Count(e)/total));
    }
    private int DefenderLoss(Army[] attackers,Army[] defenders)
    {
        var total=defenders.Sum(Count);if(total==0||attackers.Sum(Fighters)==0)return 0;
        var hp=defenders.SelectMany(a=>a.Troops).Sum(t=>t.Count*t.Unit.Definition.Stats.HitPoints)/total;
        var armor=.5+GroupPower(defenders,attackers,false)/total/100;
        return Math.Max(1,(int)Math.Ceiling(GroupPower(attackers,defenders,true)*.12/Math.Max(1,hp*armor)));
    }
    private int AttackerLoss(Army[] attackers,Army[] defenders)
    {
        var total=attackers.Sum(Count);if(total==0||defenders.Sum(Fighters)==0)return 0;
        var hp=attackers.SelectMany(a=>a.Troops).Sum(t=>t.Count*t.Unit.Definition.Stats.HitPoints)/total;
        var offense=.5+GroupPower(attackers,defenders,true)/total/100;
        return Math.Max(1,(int)Math.Ceiling(GroupPower(defenders,attackers,false)*.12/Math.Max(1,hp*offense)));
    }
    private void DamageGroup(Army[] armies,int losses,double now,Guid enemy,bool preserve)
    {
        var total=armies.Sum(Count);var left=Math.Min(losses,Math.Max(0,total-(preserve?1:0)));
        var allocations=armies.Select(a=>(Army:a,Loss:(int)Math.Floor(left*Count(a)/(double)Math.Max(1,total)))).ToArray();
        var extra=left-allocations.Sum(a=>a.Loss);
        foreach(var (army,n) in allocations){var amount=n;if(extra>0&&amount<Count(army)){amount++;extra--;}Damage(army,amount,now,enemy,true);}
    }
    private void AdvanceBattle(BattleState battle,double now)
    {
        JoinNearbyForces(battle,now);
        var attackers=Fighting(battle.Attackers);var defenders=Fighting(battle.Defenders);
        if(attackers.Length==0||defenders.Length==0){EndBattle(battle,attackers.Length==0?battle.Defender:battle.Attacker,now);return;}
        if(now<battle.NextRound)return;
        battle.NextRound=now+5;battle.Round++;
        foreach(var member in battle.ConvoyMembers.Where(m=>!m.Withdrawn)){var convoy=member.Convoy!;member.Army.Wheat=convoy.WheatCargo;member.Army.AdjustMorale(convoy.WheatCargo==0?-20:.9);}
        var attackingLoss=AttackerLoss(attackers,defenders);var defendingLoss=DefenderLoss(attackers,defenders);
        var preserve=now-battle.Started<30;
        DamageGroup(attackers,attackingLoss,now,defenders[0].OwnerId,preserve);
        DamageGroup(defenders,defendingLoss,now,attackers[0].OwnerId,preserve);
        foreach(var member in battle.ConvoyMembers){var carrier=member.Convoy!;carrier.Troops=member.Army.Troops;carrier.WheatCargo=Math.Min(carrier.WheatCargo,carrier.WheatCapacity);}
        ReportEvent(battle.Id,now,$"Round {battle.Round}: attackers {attackers.Sum(Count)} remaining, morale {attackers.Average(a=>a.Morale):0}%; defenders {defenders.Sum(Count)} remaining, morale {defenders.Average(a=>a.Morale):0}%.");
        if(preserve)return;
        attackers=Fighting(battle.Attackers);defenders=Fighting(battle.Defenders);
        if(attackers.Length==0||defenders.Length==0||now-battle.Started>=120)
            EndBattle(battle,attackers.Length==0?battle.Defender:defenders.Length==0?battle.Attacker:GroupPower(attackers,defenders,true)>=GroupPower(defenders,attackers,false)?battle.Attacker:battle.Defender,now);
    }
    private void ApplyRetreatLoss(Army army,double now)
    {
        var battle=battles.FirstOrDefault(b=>b.Status=="Active"&&b.Contains(army));
        var siege=sieges.FirstOrDefault(s=>s.Status=="Active"&&s.Attackers.Contains(army));
        var oasisSiege=oasisSieges.FirstOrDefault(s=>s.Status=="Active"&&s.Attackers.Contains(army));
        if(battle is null&&siege is null&&oasisSiege is null)return;
        var enemy=battle is not null
            ? battle.Attackers.Any(m=>m.Army==army)?battle.Defender.OwnerId:battle.Attacker.OwnerId
            : siege?.DefenderOwner??oasisSiege!.Defender;
        var losses=(int)Math.Ceiling(Count(army)*.2);
        Damage(army,losses,now,enemy,true);
        Note(army.OwnerId,now,$"The army withdrew from combat and lost {losses} units (20% of its remaining force).","war",targetId:army.Id);
    }
    private void WithdrawFromCombat(Army army,double now)
    {
        foreach(var battle in battles.Where(b=>b.Status=="Active"&&b.Contains(army)).ToArray())
        {
            battle.Members.Single(m=>m.Army==army).Withdrawn=true;
            ReportEvent(battle.Id,now,"An army was recalled from the battle.");
            if(Fighting(battle.Attackers).Length==0||Fighting(battle.Defenders).Length==0)EndBattle(battle,Fighting(battle.Attackers).Length==0?battle.Defender:battle.Attacker,now);
        }
        foreach(var siege in sieges.Where(s=>s.Attackers.Contains(army)&&s.Status == "Active").ToArray())
        {
            if(siege.Attackers.Count(a=>a!=army&&!a.Routing&&a.Order is "Sieging" or "Battle")==0)EndSiege(siege,"Withdrawn",now);
        }
    }
    private void EndBattle(BattleState battle,Army winner,double now)
    {
        if(battle.Status!="Active")return;
        var attacking=battle.Attackers.Any(m=>m.Army==winner);
        var won=(attacking?battle.Attackers:battle.Defenders).Where(m=>!m.Withdrawn).Select(m=>m.Army).ToArray();
        var lost=(attacking?battle.Defenders:battle.Attackers).Where(m=>!m.Withdrawn).Select(m=>m.Army).ToArray();
        var survivors=won.Where(a=>Count(a)>0).ToArray();
        var convoyArmies=battle.ConvoyMembers.Select(m=>m.Army).ToHashSet();
        var recipients=survivors.Where(a=>!convoyArmies.Contains(a)).ToArray();
        var loot=new Resources();var captured=new Resources();
        void Distribute(Resources cargo){var remaining=cargo;foreach(var force in recipients){var before=force.Loot;TakeLoot(force,remaining);var took=force.Loot-before;remaining-=took;captured+=took;}}
        foreach(var member in battle.ConvoyMembers)
        {
            var convoy=member.Convoy!;convoy.InBattle=false;
            if(lost.Contains(member.Army))
            {
                var cargo=convoy.Cargo+new Resources(0,0,0,(long)convoy.WheatCargo);loot+=cargo;Distribute(cargo);
                DestroyWagons(member.Army,1,now,winner.OwnerId);convoy.Cargo=new();convoy.WheatCargo=0;convoy.Returning=true;
                armies.Find(a=>a.Id==convoy.TargetArmyId)?.AdjustMorale(-25);
                Note(convoy.OwnerId,now,"Your caravan was intercepted and its cargo was lost.","convoy-loss",targetId:convoy.Id);
            }
            convoy.Troops=member.Army.Troops;if(convoy.Troops.Count==0)convoys.Remove(convoy);
        }
        if(!convoyArmies.Any())foreach(var force in lost){var cargo=force.Loot+new Resources(0,0,0,(long)force.Wheat);loot+=cargo;Distribute(cargo);force.Loot=new();force.Wheat=0;}
        battle.Status="Resolved";battle.Ended=now;battle.Winner=survivors.Length==0?null:survivors[0].OwnerId;
        var report=reports.Find(r=>r.Id==battle.Id);
        if(report is not null){if(attacking){report.AttackerLoot+=captured;report.DefenderLoss+=loot;}else{report.DefenderLoot+=captured;report.AttackerResourceLoss+=loot;}}
        ReportEvent(battle.Id,now,$"Engagement ended. Captured cargo: {ResourceText(captured)}. Survivors follow their withdrawal orders.",battle.Winner is null?"Draw":"Resolved",battle.Winner);
        foreach(var force in lost){if(convoyArmies.Contains(force))continue;if(Count(force)>0)Route(force);else armies.Remove(force);}
        foreach(var force in won){if(convoyArmies.Contains(force))continue;if(Count(force)==0){armies.Remove(force);continue;}if(force.Morale==0)Route(force);else{ResumeBattleWinner(battle,force);force.AdjustMorale(8);}}
        foreach(var owner in won.Select(a=>a.OwnerId).Distinct())Note(owner,now,"Victory in the field. Survivors follow their orders.");
        foreach(var owner in lost.Select(a=>a.OwnerId).Distinct())Note(owner,now,"The engagement is lost. Survivors are withdrawing; wounded are heading to hospital.");
    }
}
