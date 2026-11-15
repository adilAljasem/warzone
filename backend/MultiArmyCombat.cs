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
    private bool Allied(Army a, Army b) => a.OwnerId == b.OwnerId || CampAllied(a.OwnerId, b.OwnerId)
        || a.DefendingSiteId is not null && a.DefendingSiteId == b.DefendingSiteId && a.SupportedOwnerId == b.SupportedOwnerId;
    private bool SideAlliedWith(IEnumerable<BattleMember> side, Guid actor) =>
        side.Any(m => !m.Withdrawn && !m.Army.Routing && Count(m.Army) > 0 && (m.Army.OwnerId == actor || CampAllied(m.Army.OwnerId, actor)));
    private bool SideAlliedWith(IEnumerable<BattleMember> side, Army army) =>
        side.Any(m => !m.Withdrawn && !m.Army.Routing && Count(m.Army) > 0 && Allied(m.Army, army));
    private bool CanAttackIntoActiveBattle(Guid actor, Army target)
    {
        var battle = battles.Find(b => b.Status == "Active" && b.ActiveContains(target));
        if (battle is null) return true;
        var targetIsAttacker = battle.Attackers.Any(m => !m.Withdrawn && m.Army == target);
        var targetSide = targetIsAttacker ? battle.Attackers : battle.Defenders;
        var opposingSide = targetIsAttacker ? battle.Defenders : battle.Attackers;
        return !SideAlliedWith(targetSide, actor) && SideAlliedWith(opposingSide, actor);
    }
    private bool CanAttackIntoActiveBattle(Army attacker, Army target)
    {
        var battle = battles.Find(b => b.Status == "Active" && b.ActiveContains(target));
        if (battle is null) return true;
        var targetIsAttacker = battle.Attackers.Any(m => !m.Withdrawn && m.Army == target);
        var targetSide = targetIsAttacker ? battle.Attackers : battle.Defenders;
        var opposingSide = targetIsAttacker ? battle.Defenders : battle.Attackers;
        return !SideAlliedWith(targetSide, attacker) && SideAlliedWith(opposingSide, attacker);
    }
    private static Army[] Fighting(IEnumerable<BattleMember> members) => members.Where(m=>!m.Withdrawn && !m.Army.Routing && Count(m.Army)>0).Select(m=>m.Army).ToArray();
    private void JoinBattle(BattleState battle, Army army, bool attacking, double now)
    {
        if(battle.Contains(army)||army.Routing||Fighters(army)==0||battles.Any(b=>b.Status=="Active"&&b.ActiveContains(army)))return;
        var side=attacking?battle.Attackers:battle.Defenders;
        var opposingSide=attacking?battle.Defenders:battle.Attackers;
        if(!SideAlliedWith(side,army)||SideAlliedWith(opposingSide,army))return;
        side.Add(new(army));army.Order="Battle";
        AddReportArmy(reports.Single(r=>r.Id==battle.Id),army,attacking);
        ReportEvent(battle.Id,now,"Another army joined the battle.");
    }
    private void JoinNearbyForces(BattleState battle,double now)
    {
        foreach(var army in armies.Where(a=>!a.Routing&&a.Order is "Idle" or "Guarding" or "Reinforcing" or "Occupying" or "Blockading" or "Capturing" or "Attacking" or "Approaching" or "Sieging").ToArray())
        {
            if(battle.Contains(army))continue;
            var attack=SideAlliedWith(battle.Attackers,army);
            var defense=SideAlliedWith(battle.Defenders,army);
            if(attack==defense)continue;
            var side=attack?battle.Attackers:battle.Defenders;
            if(side.Any(m=>!m.Withdrawn&&Distance(army.Position,m.Army.Position)<=45))JoinBattle(battle,army,attack,now);
        }
    }
    private readonly record struct TierSlot(Army Army, int StackIndex, TroopStack Stack);
    private sealed record SideRoundPlan(Dictionary<Army, int[]> LossByArmy, List<((Guid ArmyId, string UnitId) Key, double NextDamage)> PoolUpdates);

    private static bool HasFrontline(Army[] side) =>
        side.Any(a => a.Troops.Any(t => t.Unit is not (SupplyWagon or SiegeEngine)));

    private static TierSlot[] ActiveTierSlots(Army[] side, bool rearOnly = false)
    {
        var takeRear = rearOnly || !HasFrontline(side);
        var list = new List<TierSlot>();
        foreach (var a in side)
            for (var i = 0; i < a.Troops.Count; i++)
            {
                var t = a.Troops[i];
                var isRear = t.Unit is SupplyWagon or SiegeEngine;
                if (isRear == takeRear) list.Add(new(a, i, t));
            }
        return list.ToArray();
    }

    // Scales round tempo gently for larger battles and accelerates prolonged stalemates so fights always finish naturally.
    private static double RoundPaceFactor(int totalFighters, int round)
    {
        var sizeBoost = 1.0 + Math.Min(0.6, Math.Max(0, totalFighters - 300) / 1500.0);
        var roundBoost = round <= 10 ? 1.0 : 1.0 + Math.Min(2.0, (round - 10) * 0.12);
        return 0.25 * sizeBoost * roundBoost;
    }

    private double ComputeTierSlotDamage(TierSlot target, int tierTotal, TierSlot[] activeOpponents, bool targetsAttacking, double damageMultiplier, double paceFactor)
    {
        if (tierTotal <= 0) return 0;
        var share = target.Stack.Count / (double)tierTotal;
        var targetMounted = UnitBalance.Mounted(target.Stack.Unit);
        var incoming = activeOpponents.Sum(enemy =>
        {
            var enemyMounted = UnitBalance.Mounted(enemy.Stack.Unit);
            var oppStat = !targetsAttacking
                ? enemy.Stack.Unit.Attack
                : (targetMounted ? enemy.Stack.Unit.DefenseCavalry : enemy.Stack.Unit.DefenseInfantry);
            var oppMatchup = UnitBalance.Matchup(enemy.Stack.Unit, target.Stack.Unit.Definition.Role, !targetsAttacking);
            var targetDef = enemyMounted ? target.Stack.Unit.DefenseCavalry : target.Stack.Unit.DefenseInfantry;
            var mitigation = 100.0 / (100.0 + Math.Max(0, targetDef));
            return enemy.Stack.Count * oppStat * oppMatchup * ForceModifier(enemy.Army) * mitigation;
        });
        return share * incoming * damageMultiplier * paceFactor;
    }

    private SideRoundPlan PlanSideRoundDamage(Army[] targets, Army[] opponents, bool targetsAttacking, double damageMultiplier, double paceFactor,
        Dictionary<(Guid ArmyId, string UnitId), double> damagePool, Func<Army, Guid> poolKeySelector)
    {
        var lossByArmy = targets.ToDictionary(a => a, a => new int[a.Troops.Count]);
        var poolUpdates = new List<((Guid ArmyId, string UnitId) Key, double NextDamage)>();
        if (targets.Sum(Count) == 0 || opponents.Sum(Count) == 0)
            return new(lossByArmy, poolUpdates);

        var activeOpponents = ActiveTierSlots(opponents);
        if (activeOpponents.Length == 0) return new(lossByArmy, poolUpdates);

        var hasFront = HasFrontline(targets);
        var front = ActiveTierSlots(targets, rearOnly: false);
        var frontTotal = front.Sum(x => x.Stack.Count);
        var (frontKilled, totalFrontDamage, unspentFrontDamage) = PlanTierSlots(front, frontTotal, activeOpponents, targetsAttacking, damageMultiplier, paceFactor, damagePool, poolKeySelector, lossByArmy, poolUpdates);

        if (hasFront && frontTotal > 0 && frontKilled >= frontTotal && unspentFrontDamage > 0 && totalFrontDamage > 0)
        {
            var rear = ActiveTierSlots(targets, rearOnly: true);
            var rearTotal = rear.Sum(x => x.Stack.Count);
            if (rearTotal > 0)
            {
                var spilloverFraction = Math.Clamp(unspentFrontDamage / totalFrontDamage, 0, 1);
                PlanTierSlots(rear, rearTotal, activeOpponents, targetsAttacking, damageMultiplier * spilloverFraction, paceFactor, damagePool, poolKeySelector, lossByArmy, poolUpdates);
            }
        }
        return new(lossByArmy, poolUpdates);
    }

    private (int Killed, double TotalDamage, double UnspentDamage) PlanTierSlots(TierSlot[] tier, int tierTotal, TierSlot[] activeOpponents,
        bool targetsAttacking, double damageMultiplier, double paceFactor,
        Dictionary<(Guid ArmyId, string UnitId), double> damagePool, Func<Army, Guid> poolKeySelector,
        Dictionary<Army, int[]> lossByArmy, List<((Guid ArmyId, string UnitId) Key, double NextDamage)> poolUpdates)
    {
        if (tierTotal == 0) return (0, 0, 0);
        var killedTotal = 0;
        var damageTotal = 0d;
        var unspentTotal = 0d;
        var rawDamages = tier.Select(slot => ComputeTierSlotDamage(slot, tierTotal, activeOpponents, targetsAttacking, damageMultiplier, paceFactor)).ToArray();
        // Keep side-level incoming damage balanced while splitting mixed-unit casualties by 1 / (10 + roleStat):
        // Attackers distribute losses by Attack (weaker Attack dies faster); defenders by weighted Defense vs enemy mix.
        var tierDamage = rawDamages.Sum();
        var totalOppCount = activeOpponents.Sum(e => e.Stack.Count);
        var oppCavShare = totalOppCount > 0
            ? activeOpponents.Where(e => UnitBalance.Mounted(e.Stack.Unit)).Sum(e => e.Stack.Count) / (double)totalOppCount
            : 0d;
        var oppInfShare = 1.0 - oppCavShare;
        var vulnScores = tier.Select(slot =>
        {
            var roleStat = targetsAttacking
                ? slot.Stack.Unit.Attack
                : oppInfShare * slot.Stack.Unit.DefenseInfantry + oppCavShare * slot.Stack.Unit.DefenseCavalry;
            return slot.Stack.Count * (1.0 / (10.0 + Math.Max(0, roleStat)));
        }).ToArray();
        var vulnTotal = vulnScores.Sum();
        var slotDamages = vulnTotal > 0 ? vulnScores.Select(v => tierDamage * (v / vulnTotal)).ToArray() : rawDamages;
        for (var i = 0; i < tier.Length; i++)
        {
            var slot = tier[i];
            var dmg = slotDamages[i];
            damageTotal += dmg;
            var key = (poolKeySelector(slot.Army), slot.Stack.Unit.UnitId);
            var pool = damagePool.GetValueOrDefault(key) + dmg;
            var hp = Math.Max(1, slot.Stack.Unit.Definition.Stats.HitPoints);
            var kills = Math.Min(slot.Stack.Count, (int)Math.Floor(pool / hp));
            killedTotal += kills;
            lossByArmy[slot.Army][slot.StackIndex] += kills;
            var leftover = Math.Max(0, pool - kills * hp);
            if (kills >= slot.Stack.Count)
            {
                unspentTotal += leftover;
                poolUpdates.Add((key, 0));
            }
            else
            {
                poolUpdates.Add((key, leftover));
            }
        }
        return (killedTotal, damageTotal, unspentTotal);
    }

    private int CommitSideRoundPlan(Army[] targets, SideRoundPlan plan, Dictionary<(Guid ArmyId, string UnitId), double> damagePool, double now, Guid enemyOwner, bool deployed)
    {
        foreach (var (key, next) in plan.PoolUpdates)
        {
            if (next <= 0) damagePool.Remove(key);
            else damagePool[key] = next;
        }
        var appliedTotal = 0;
        foreach (var army in targets)
        {
            var stackLosses = plan.LossByArmy[army];
            var armyTotalLost = stackLosses.Sum();
            if (armyTotalLost == 0) continue;
            appliedTotal += armyTotalLost;
            var wagonsBefore = RoleCount(army, UnitRole.Wagon);
            var surviving = new List<TroopStack>();
            for (var i = 0; i < army.Troops.Count; i++)
            {
                var stack = army.Troops[i];
                var lost = stackLosses[i];
                RegisterLoss(army, stack.Unit, lost, now, enemyOwner, deployed);
                if (stack.Count > lost) surviving.Add(new(stack.Unit, stack.Count - lost));
            }
            FinalizeArmyDamage(army, surviving, armyTotalLost, wagonsBefore);
        }
        return appliedTotal;
    }

    private void AdvanceBattle(BattleState battle, double now)
    {
        JoinNearbyForces(battle, now);
        var attackers = Fighting(battle.Attackers); var defenders = Fighting(battle.Defenders);
        if (attackers.Length == 0 || defenders.Length == 0) { EndBattle(battle, attackers.Length == 0 ? battle.Defender : battle.Attacker, now); return; }
        if (now < battle.NextRound) return;
        battle.NextRound = now + 5; battle.Round++;
        foreach (var member in battle.ConvoyMembers.Where(m => !m.Withdrawn)) { member.Army.Wheat = member.Convoy!.WheatCargo; }

        var pace = RoundPaceFactor(attackers.Sum(Count) + defenders.Sum(Count), battle.Round);
        var attPlan = PlanSideRoundDamage(attackers, defenders, true, 1, pace, battle.DamagePool, a => a.Id);
        var defPlan = PlanSideRoundDamage(defenders, attackers, false, 1, pace, battle.DamagePool, a => a.Id);
        CommitSideRoundPlan(attackers, attPlan, battle.DamagePool, now, defenders[0].OwnerId, true);
        CommitSideRoundPlan(defenders, defPlan, battle.DamagePool, now, attackers[0].OwnerId, true);

        foreach (var member in battle.ConvoyMembers) { var carrier = member.Convoy!; carrier.Troops = member.Army.Troops; carrier.WheatCargo = Math.Min(carrier.WheatCargo, carrier.WheatCapacity); }
        ReportEvent(battle.Id, now, $"Round {battle.Round}: attackers {attackers.Sum(Count)} remaining; defenders {defenders.Sum(Count)} remaining.");
        attackers = Fighting(battle.Attackers); defenders = Fighting(battle.Defenders);
        if (attackers.Length == 0 || defenders.Length == 0)
            EndBattle(battle, attackers.Length == 0 ? battle.Defender : battle.Attacker, now);
    }
    private void ApplyRetreatLoss(Army army,double now)
    {
        var battle=battles.FirstOrDefault(b=>b.Status=="Active"&&b.ActiveContains(army));
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
    private void WithdrawFromSieges(Army army, double now, Guid? formerTargetId = null, string? formerOrder = null)
    {
        foreach (var siege in sieges.Where(s => s.Status == "Active" && s.Attackers.Contains(army)).ToArray())
        {
            if (siege.Attackers.Count(a => a != army && armies.Contains(a) && Fighters(a) > 0 && !a.Routing && a.Order is "Sieging" or "Battle") == 0)
                EndSiege(siege, "Withdrawn", now);
            else
                siege.Attackers.Remove(army);
        }

        if (formerOrder == "Burning" && formerTargetId is { } burningOasisId
            && oases.Find(o => o.Id == burningOasisId) is { } burnedOasis
            && !armies.Any(a => a != army && !a.Routing && a.Order == "Burning" && a.TargetId == burningOasisId))
        {
            burnedOasis.BurningSeconds = 0;
        }

        foreach (var siege in oasisSieges.Where(s => s.Status == "Active" && s.Attackers.Contains(army)).ToArray())
        {
            if (!armies.Any(a => a != army && !a.Routing && a.Order == "Burning" && a.TargetId == siege.Oasis.Id))
                siege.Oasis.BurningSeconds = 0;
            if (!siege.Attackers.Any(a => a != army && ValidOasisArmy(siege, a)))
                EndOasisSiege(siege, "Withdrawn", now, siege.Defender);
            else
                siege.Attackers.Remove(army);
        }
        if (oasisSieges.RemoveAll(s => s.Status != "Active") > 0)
            RefreshOasisBonuses();
    }
    private void WithdrawFromCombat(Army army,double now)
    {
        foreach(var battle in battles.Where(b=>b.Status=="Active"&&b.ActiveContains(army)).ToArray())
        {
            battle.Members.Single(m=>m.Army==army).Withdrawn=true;
            ReportEvent(battle.Id,now,"An army was recalled from the battle.");
            if(Fighting(battle.Attackers).Length==0||Fighting(battle.Defenders).Length==0)EndBattle(battle,Fighting(battle.Attackers).Length==0?battle.Defender:battle.Attacker,now);
        }
        WithdrawFromSieges(army, now, army.TargetId, army.Order);
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
        foreach(var force in won){if(convoyArmies.Contains(force))continue;if(Count(force)==0){armies.Remove(force);continue;}ResumeBattleWinner(battle,force);}
        if(survivors.Length>0)foreach(var owner in won.Select(a=>a.OwnerId).Distinct())Note(owner,now,"Victory in the field. Survivors follow their orders.");
        foreach(var owner in lost.Select(a=>a.OwnerId).Distinct())Note(owner,now,"The engagement is lost. Survivors are withdrawing; wounded are heading to hospital.");
    }
}
