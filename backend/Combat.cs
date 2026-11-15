namespace Warzone;

public sealed record BattleSnapshot(Guid Id, Position Position, Guid AttackerId, Guid DefenderId, Guid AttackerOwner,
    Guid DefenderOwner, string DefenderKind, double Elapsed, int Round, int AttackerTroops, int DefenderTroops,
    string Status, Guid? Winner, Guid[] AttackerArmyIds, Guid[] DefenderArmyIds);
public sealed record SiegeSnapshot(Guid Id, Guid ArmyId, Guid VillageId, Guid AttackerOwner, Guid DefenderOwner,
    Position Position, double Elapsed, double Wall, double Gate,
    double Loyalty, string Status, Guid[] ArmyIds, AttackObjective Objective, string Phase, int RemainingBuildingLevels, double Progress);

internal sealed class BattleState(Army attacker, Army defender, double now, Convoy? convoy = null)
{
    public Guid Id { get; } = Guid.NewGuid();
    public Army Attacker { get; } = attacker;
    public Army Defender { get; } = defender;
    public Convoy? Convoy => ConvoyMembers.Select(m => m.Convoy).FirstOrDefault();
    public IEnumerable<BattleMember> ConvoyMembers => Members.Where(m => m.Convoy is not null);
    public double Started { get; } = now;
    public double NextRound { get; set; } = now + 5;
    public int Round { get; set; }
    public string Status { get; set; } = "Active";
    public Guid? Winner { get; set; }
    public List<BattleMember> Attackers { get; } = [new(attacker)];
    public List<BattleMember> Defenders { get; } = [new(defender, convoy)];
    public Dictionary<(Guid ArmyId, string UnitId), double> DamagePool { get; } = [];
    public IEnumerable<BattleMember> Members => Attackers.Concat(Defenders);
    public bool Contains(Army army) => Members.Any(m=>m.Army==army);
    public bool ActiveContains(Army army) => Members.Any(m => !m.Withdrawn && m.Army == army);
    public double Ended { get; set; }
}
internal sealed class SiegeState(Army attacker, VillageEconomy city, double now)
{
    public Guid Id { get; } = Guid.NewGuid();
    public List<Army> Attackers { get; } = [attacker];
    public Army Attacker => Attackers.FirstOrDefault(a=>a.Troops.Count>0&&!a.Routing&&a.Order is "Sieging" or "Battle") ?? Attackers[0];
    public VillageEconomy City { get; } = city;
    public Guid DefenderOwner { get; } = city.Village.OwnerId;
    public double Started { get; set; } = now;
    public double NextRound { get; set; } = now + 5;
    public int Round { get; set; }
    public double Wall { get; set; } = 100;
    public double Gate { get; set; } = 100;
    public double Loyalty { get; set; } = 100;
    public string Status { get; set; } = "Active";
    public Dictionary<(Guid ArmyId, string UnitId), double> DamagePool { get; } = [];
    public double Ended { get; set; }
    public AttackObjective Objective { get; set; } = attacker.Objective;
    public string Phase { get; set; } = attacker.Objective == AttackObjective.Blockade ? "Blockading" : "Fighting";
    public int InitialBuildingLevels { get; set; }
    public double DemolitionCredit { get; set; }
    public double Progress { get; set; }
}

public sealed partial class LogisticsWorld
{
    private readonly List<BattleState> battles = [];
    private readonly List<SiegeState> sieges = [];
    private readonly Dictionary<(Guid Viewer, Guid ArmyId), double> scoutedArmies = [];
    private bool IsScoutedBy(Guid viewer, Guid armyId) => scoutedArmies.TryGetValue((viewer, armyId), out var until) && until > warTime;
    private bool TryScoutExpiry(Guid viewer, Guid armyId, out double until)
    {
        if (scoutedArmies.TryGetValue((viewer, armyId), out until) && until > warTime) return true;
        until = 0;
        return false;
    }
    private static int Count(Army army) => army.Troops.Sum(x => x.Count);
    private static int RoleCount(Army army, UnitRole role) => army.Troops.Where(x => x.Unit.Definition.Role == role).Sum(x => x.Count);
    private static int Fighters(Army army) => army.Troops.Where(x => x.Unit is not SupplyWagon && x.Unit.Definition.Role != UnitRole.Governor).Sum(x => x.Count);
    private bool Engaged(Army army) => battles.Any(x => x.Status == "Active" && x.ActiveContains(army))
        || sieges.Any(x => x.Attackers.Contains(army) && x.Status == "Active");

    private bool ApplyWarfare(Guid actor, GameCommand command, double now, double shieldUntil)
    {
        if (ApplyPolitics(actor, command, now)) return true;
        if (command.Kind == CommandKind.Scout)
        {
            var scoutArmy = Owned(actor, command.TargetId);
            if (scoutArmy.Routing) throw new GameRuleException("routing", "A broken army must return home.");
            if (Engaged(scoutArmy)) throw new GameRuleException("engaged", "Finish the engagement or retreat first.");
            var scoutCount = OrderFormation(scoutArmy, command.Formation).Where(x => GameCatalog.Units.TryGetValue(x.Key, out var unit) && unit.Role == UnitRole.Scout).Sum(x => x.Value);
            if (scoutCount == 0 || OrderFighters(scoutArmy, command.Formation) != scoutCount)
                throw new GameRuleException("scouts", "A scouting mission requires an army composed of scouts.");
            var destId = command.DestinationId ?? Guid.Empty;
            var scoutCity = villages.GetValueOrDefault(destId);
            var scoutOasis = scoutCity is null ? oases.Find(o => o.Id == destId) : null;
            var scoutEnemy = scoutCity is null && scoutOasis is null ? armies.FirstOrDefault(a => a.Id == destId && a.OwnerId != actor) : null;
            if ((scoutCity is null || scoutCity.Ruined || scoutCity.Village.OwnerId == actor) && (scoutOasis is null || scoutOasis.OwnerId == actor) && scoutEnemy is null)
                throw new GameRuleException("target", "Select a village, oasis, or enemy army to scout.");
            scoutArmy = Detach(scoutArmy, command.Formation, now);
            scoutArmy.TargetId = destId; scoutArmy.Order = "Scouting";
            scoutArmy.DefendingSiteId = null; scoutArmy.SupportedOwnerId = null; scoutArmy.CampRadius = null;
            scoutArmy.AfterAttack = AfterAttack.Return; scoutArmy.ActionSeconds = 0;
            UpdateWarDestination(scoutArmy);
            return true;
        }
        if (command.Kind is not (CommandKind.AttackArmy or CommandKind.BeginSiege)) return false;
        if (!Enum.IsDefined(command.Objective)) throw new GameRuleException("directive", "Unknown city order.");
        var army = Owned(actor, command.TargetId);
        if (army.Routing) throw new GameRuleException("routing", "A broken army must return home.");
        if (now < shieldUntil) throw new GameRuleException("protected", "Wait until the protection shield expires.");
        if (Engaged(army)) throw new GameRuleException("engaged", "Finish the engagement or retreat first.");
        if (OrderFighters(army, command.Formation) == 0) throw new GameRuleException("troops", "This army has no fighting troops.");
        if (command.Kind == CommandKind.AttackArmy)
        {
            var target = armies.FirstOrDefault(x => x.Id == command.DestinationId && x.OwnerId != actor)
                ?? throw new GameRuleException("target", "Select an enemy army.");
            if (CampAllied(actor, target.OwnerId))
                throw new GameRuleException("ally", "You cannot attack an allied army.");
            if (!CanAttackIntoActiveBattle(actor, target))
                throw new GameRuleException("battle", "You cannot enter an ongoing battle unless one of the sides is your ally.");
            army = Detach(army, command.Formation, now);
            army.TargetId = target.Id; army.Order = "Attacking"; army.Destination = target.Position;
            army.AttackOrderedAt = now;
            army.DefendingSiteId = null; army.SupportedOwnerId = null; army.CampRadius = null;
            Note(target.OwnerId, now, "An enemy army is approaching your expedition. Its arrival estimate is pinned above the battlefield.", "war");
            if (oases.Find(o => o.Id == target.DefendingSiteId) is { OwnerId: { } supported } site)
                Note(supported, now, "Enemy troops are marching on your oasis.", "oasis-attack", targetId: site.Id);
        }
        else
        {
            var city = villages.Values.FirstOrDefault(x => x.Village.Id == command.DestinationId && x.Village.OwnerId != actor && !x.Ruined)
                ?? throw new GameRuleException("target", "Select an enemy settlement.");
            if (sieges.Any(s => s.City == city && s.Status == "Active" && !CampAllied(s.Attacker.OwnerId, actor))) throw new GameRuleException("siege", "A rival commander is already besieging this city.");
            ValidateCityAttack(actor, city, command.Objective, OrderFormation(army, command.Formation), now, shieldUntil);
            army = Detach(army, command.Formation, now);
            army.Objective = command.Objective;
            army.TargetId = city.Village.Id; army.Order = "Approaching";
            army.AttackOrderedAt = now;
            army.DefendingSiteId = null; army.SupportedOwnerId = null; army.CampRadius = null;
            UpdateWarDestination(army);
            Note(city.Village.OwnerId, now, "An enemy army is marching on your village. Prepare the garrison before it arrives.", "war");
        }
        army.AfterAttack = command.AfterAttack; army.ActionSeconds = 0; return true;
    }
    private void UpdateWarDestination(Army army)
    {
        if (army.Order == "Scouting")
        {
            if (villages.TryGetValue(army.TargetId ?? Guid.Empty, out var scoutCity) && !scoutCity.Ruined && scoutCity.Village.OwnerId != army.OwnerId)
                army.Destination = scoutCity.Village.Position;
            else if (oases.Find(o => o.Id == army.TargetId) is { } scoutOasis && scoutOasis.OwnerId != army.OwnerId)
                army.Destination = scoutOasis.Position;
            else if (armies.FirstOrDefault(x => x.Id == army.TargetId && x.OwnerId != army.OwnerId) is { } scoutEnemy)
                army.Destination = scoutEnemy.Position;
            else CompleteAttack(army);
        }
        if (army.Order == "Attacking")
        {
            var enemy = armies.FirstOrDefault(x => x.Id == army.TargetId && x.OwnerId != army.OwnerId);
            if (enemy is not null) army.Destination = enemy.Position;
            else { army.Order = "Idle"; army.TargetId = null; army.Destination = army.Position; }
        }
        if (army.Order == "Approaching")
        {
            var city = villages.GetValueOrDefault(army.TargetId ?? Guid.Empty);
            if (city is null || city.Ruined || city.Village.OwnerId == army.OwnerId)
            { army.Order = "Idle"; army.TargetId = null; army.Destination = army.Position; return; }
            var angle = Math.Atan2(army.Position.Y - city.Village.Position.Y, army.Position.X - city.Village.Position.X);
            army.Destination = new(city.Village.Position.X + Math.Cos(angle) * 38, city.Village.Position.Y + Math.Sin(angle) * 38);
        }
    }
    private void StartBattle(Army attacker, Army defender, double now, Convoy? convoy = null)
    {
        if(attacker==defender || attacker.OwnerId==defender.OwnerId || CampAllied(attacker.OwnerId, defender.OwnerId) || Fighters(attacker)==0 || attacker.Routing)return;
        var existing=battles.Find(b=>b.Status=="Active"&&b.ActiveContains(defender));
        if(existing is not null){JoinBattle(existing,attacker,!existing.Attackers.Any(m=>!m.Withdrawn&&m.Army==defender),now);return;}
        if(battles.Any(b=>b.Status=="Active"&&b.ActiveContains(attacker)))return;
        var battle=new BattleState(attacker,defender,now,convoy);battles.Add(battle);
        attacker.Order="Battle";defender.Order="Battle";if(convoy is not null)convoy.InBattle=true;
        BeginReport(battle.Id,convoy is null?"Battle":"Interception",now,attacker,defender.OwnerId,null,defender.Position);
        ReportEvent(battle.Id,now,"Battle joined. Army composition determines each combat round.");
        Note(attacker.OwnerId,now,"Battle joined. Open the live report to follow every army.");
        Note(defender.OwnerId,now,convoy is null?"Your army is under attack. Send reinforcements or follow the live report.":"A caravan is under attack. Its escorts are defending it.");
        JoinNearbyForces(battle,now);
    }
    private void StartConvoyBattle(Army army, double now)
    {
        var convoy = convoys.FirstOrDefault(x => x.Id == army.TargetId && !x.InBattle);
        if (convoy is not null) StartConvoyBattle(army, convoy, now);
    }
    private void StartConvoyBattle(Army army, Convoy convoy, double now)
    {
        var defender = new Army(convoy.OwnerId, convoy.Position, convoy.SourceVillageId, convoy.Troops)
        { Wheat = convoy.WheatCargo };
        StartBattle(army, defender, now, convoy);
    }
    private double ForceModifier(Army force)
    {
        var smithy = Math.Max(1, villages[force.HomeVillageId].BuildingLevel(BuildingType.Smithy));
        return 1 + .05 * (smithy - 1);
    }
    private int KillScouts(Army force, int losses, Guid enemy, bool deployed)
    {
        if (losses <= 0) return 0;
        var remaining = losses;
        var result = new List<TroopStack>();
        var home = villages[force.HomeVillageId];
        var stats = statistics[force.OwnerId];
        foreach (var stack in force.Troops)
        {
            var lost = stack.Unit.Definition.Role == UnitRole.Scout ? Math.Min(remaining, stack.Count) : 0;
            if (lost > 0)
            {
                remaining -= lost;
                if (deployed) home.LoseDeployed(lost);
                stats.Dead += lost;
                if (enemy != Guid.Empty && enemy != force.OwnerId && statistics.TryGetValue(enemy, out var killerStats))
                    killerStats.Kills += lost;
                ReportLosses(force, stack.Unit, lost, 0, 0, 0, deployed);
            }
            if (stack.Count > lost) result.Add(new(stack.Unit, stack.Count - lost));
        }
        force.Troops = result.AsReadOnly();
        force.Wheat = Math.Min(force.Wheat, force.Capacity);
        return losses - remaining;
    }
    private void ResolveScout(Army scoutArmy, double now)
    {
        var targetCity = villages.GetValueOrDefault(scoutArmy.TargetId ?? Guid.Empty);
        var targetOasis = targetCity is null ? oases.Find(o => o.Id == scoutArmy.TargetId) : null;
        var targetArmy = targetCity is null && targetOasis is null ? armies.FirstOrDefault(a => a.Id == scoutArmy.TargetId && a.OwnerId != scoutArmy.OwnerId) : null;
        if ((targetCity is null || targetCity.Ruined || targetCity.Village.OwnerId == scoutArmy.OwnerId) && (targetOasis is null || targetOasis.OwnerId == scoutArmy.OwnerId) && targetArmy is null)
        { CompleteAttack(scoutArmy); return; }
        var defenderOwner = targetCity?.Village.OwnerId ?? targetOasis?.OwnerId ?? targetArmy?.OwnerId;
        var targetPosition = targetCity?.Village.Position ?? targetOasis?.Position ?? targetArmy!.Position;
        var defendingArmies = targetCity is not null ? Guards(targetCity.Village.Id, scoutArmy.OwnerId).ToArray()
            : targetOasis is not null ? Guards(targetOasis.Id, scoutArmy.OwnerId).ToArray()
            : [targetArmy!];
        var report = new ReportState(Guid.NewGuid(), "Scout", now, scoutArmy, defenderOwner, targetCity?.Village.Id, targetPosition);
        report.AttackerSent = scoutArmy.Troops.ToDictionary(t => t.Unit.UnitId, t => t.Count);
        AddReportArmy(report, scoutArmy, true);
        if (targetCity is not null) report.GarrisonSent = targetCity.Garrison().ToDictionary(t => t.Unit.UnitId, t => t.Count);
        foreach (var defArmy in defendingArmies) AddReportArmy(report, defArmy, false);
        RefreshReportForces(report);
        reports.Add(report);

        var attackerScouts = Math.Max(1, RoleCount(scoutArmy, UnitRole.Scout));
        var garrisonScouts = targetCity?.Garrison().Where(t => t.Unit.Definition.Role == UnitRole.Scout).Sum(t => t.Count) ?? 0;
        var armyScouts = defendingArmies.Sum(a => RoleCount(a, UnitRole.Scout));
        var defenderScouts = garrisonScouts + armyScouts;

        void DamageDefenderScouts(int count)
        {
            var left = count;
            if (targetCity is not null && garrisonScouts > 0 && left > 0)
            {
                var garrisonForce = new Army(targetCity.Village.OwnerId, targetCity.Village.Position, targetCity.Village.Id, targetCity.Garrison());
                var taken = KillScouts(garrisonForce, Math.Min(left, garrisonScouts), scoutArmy.OwnerId, false);
                targetCity.ReplaceGarrison(garrisonForce.Troops);
                left -= taken;
            }
            foreach (var defArmy in defendingArmies)
            {
                if (left <= 0) break;
                var avail = RoleCount(defArmy, UnitRole.Scout);
                if (avail <= 0) continue;
                left -= KillScouts(defArmy, Math.Min(left, avail), scoutArmy.OwnerId, true);
                if (Count(defArmy) == 0) armies.Remove(defArmy);
            }
        }

        if (defenderScouts > attackerScouts)
        {
            report.HideDefenderFromAttacker = true;
            var defLoss = Math.Clamp((int)Math.Ceiling(attackerScouts * (double)attackerScouts / defenderScouts), 1, Math.Min(attackerScouts, defenderScouts - 1));
            DamageDefenderScouts(defLoss);
            KillScouts(scoutArmy, Count(scoutArmy), defenderOwner ?? scoutArmy.OwnerId, true);
            armies.Remove(scoutArmy);
            ReportEvent(report.Id, now, "Enemy counter-scouts outnumbered your scouts. All your scouts were killed and no intelligence was gathered.", "Intercepted", defenderOwner);
            Note(scoutArmy.OwnerId, now, "Your scouts were intercepted and killed by superior enemy counter-scouts.", "war", targetId: report.Id);
            if (defenderOwner is { } defId) Note(defId, now, "Your scouts intercepted and eliminated enemy spies, suffering minor losses.", "war", targetId: report.Id);
            return;
        }

        if (defenderScouts > 0)
        {
            var defLoss = Math.Max(1, (int)Math.Ceiling(defenderScouts * .6));
            DamageDefenderScouts(defLoss);
            var attLoss = attackerScouts > 1 ? Math.Clamp((int)Math.Floor(defenderScouts * (double)defenderScouts / attackerScouts * .5), 1, attackerScouts - 1) : 0;
            if (attLoss > 0) KillScouts(scoutArmy, attLoss, defenderOwner ?? scoutArmy.OwnerId, true);
        }
        if (targetArmy is not null) scoutedArmies[(scoutArmy.OwnerId, targetArmy.Id)] = now + 60;
        scoutArmy.DefendingSiteId = null; scoutArmy.SupportedOwnerId = null; scoutArmy.TargetId = null;
        scoutArmy.Order = "Returning"; scoutArmy.Destination = villages[scoutArmy.HomeVillageId].Village.Position;
        var summary = report.DefenderSent.Count == 0 ? "No defending troops were found at the target."
            : "Detected forces: " + string.Join(", ", report.DefenderSent.Select(kv => $"{kv.Value} {GameCatalog.Units[kv.Key].DisplayName}")) + ".";
        ReportEvent(report.Id, now, $"Reconnaissance succeeded. {summary}", "Scouted", scoutArmy.OwnerId);
        Note(scoutArmy.OwnerId, now, "Your scouts completed their reconnaissance. Open Reports to inspect the target's forces.", "war", targetId: report.Id);
        if (defenderOwner is { } ownerId && defenderScouts > 0)
            Note(ownerId, now, "Enemy scouts breached your counter-scouts and spied on your forces.", "war", targetId: report.Id);
    }
    private int Damage(Army army, int losses, double now, Guid enemy, bool deployed, bool keepOne = false)
    {
        var before = Count(army); var wagons = RoleCount(army, UnitRole.Wagon);
        losses = Math.Min(Math.Max(0, losses), Math.Max(0, before - (keepOne ? 1 : 0)));
        var remaining = losses; var result = new List<TroopStack>();
        // Split casualties proportionally within the frontline before exposing transport and siege equipment.
        var stacks=army.Troops.OrderBy(t=>t.Unit is SupplyWagon or SiegeEngine?1:0).ToArray();
        foreach(var tier in stacks.GroupBy(t=>t.Unit is SupplyWagon or SiegeEngine))
        {
            var budget=Math.Min(remaining,tier.Sum(t=>t.Count));var left=budget;var total=tier.Sum(t=>t.Count);
            var allocations=tier.Select(t=>(Stack:t,Loss:(int)Math.Floor(budget*t.Count/(double)Math.Max(1,total)))).ToArray();
            left-=allocations.Sum(x=>x.Loss);
            for(var i=0;i<allocations.Length;i++)
            {
                var (stack,lost)=allocations[i];if(left>0&&lost<stack.Count){lost++;left--;}
                remaining-=lost;RegisterLoss(army,stack.Unit,lost,now,enemy,deployed);
                if(stack.Count>lost)result.Add(new(stack.Unit,stack.Count-lost));
            }
        }
        return FinalizeArmyDamage(army, result, losses, wagons);
    }
    private static int FinalizeArmyDamage(Army army, List<TroopStack> surviving, int losses, int wagonsBefore)
    {
        army.Troops = surviving.AsReadOnly();
        army.Wheat = Math.Min(army.Wheat, army.Capacity);
        var remainingCargo = Math.Max(0, army.Troops.Sum(x => (double)x.Count * x.Unit.CarryCapacity) - army.Wheat);
        if (army.Loot.Total > remainingCargo) army.Loot = army.Loot.Fraction(remainingCargo / Math.Max(1, army.Loot.Total));
        return losses;
    }
    private void DestroyWagons(Army army, double fraction, double now, Guid enemy)
    {
        var remaining=(int)Math.Ceiling(RoleCount(army,UnitRole.Wagon)*fraction);var kept=new List<TroopStack>();
        foreach(var t in army.Troops){var lost=t.Unit is SupplyWagon?Math.Min(remaining,t.Count):0;remaining-=lost;RegisterLoss(army,t.Unit,lost,now,enemy,true);if(t.Count>lost)kept.Add(new(t.Unit,t.Count-lost));}
        army.Troops=kept;army.Wheat=Math.Min(army.Wheat,army.Capacity);
    }
    private void Route(Army army)
    {
        army.DefendingSiteId = null; army.SupportedOwnerId = null; army.AttackOrderedAt = 0; army.CampRadius = null;
        army.Routing = true; army.Order = "Returning"; army.TargetId = null; army.Destination = villages[army.HomeVillageId].Village.Position;
    }
    private void ResumeBattleWinner(BattleState battle, Army winner)
    {
        var member=battle.Members.Single(m=>m.Army==winner);
        var order = member.Order;
        var target = member.Target;
        if (winner.CampRadius is not null)
        { winner.Order = "Camped"; winner.TargetId = null; winner.Destination = winner.Position; return; }
        if (winner.DefendingSiteId is { } siteId)
        {
            try
            {
                var site = DefenseSite(siteId);
                if (site.Owner == winner.SupportedOwnerId)
                { winner.Order = "Guarding"; winner.TargetId = siteId; winner.Destination = site.Position; MergeOasisGuards(siteId); return; }
            }
            catch (GameRuleException) { }
            Route(winner); return;
        }
        if (order is "Occupying" or "Blockading" && oases.Find(o => o.Id == target) is { } oasis)
        { winner.Order = order; winner.TargetId = target; winner.Destination = oasis.Position; winner.ActionSeconds = 0; return; }
        if (order is "Approaching" or "Sieging" && villages.GetValueOrDefault(target ?? Guid.Empty) is { Ruined: false } city && city.Village.OwnerId != winner.OwnerId)
        { winner.Order = sieges.Any(s=>s.City==city&&s.Status=="Active"&&s.Attackers.Contains(winner))?"Sieging":"Approaching"; winner.TargetId = target; UpdateWarDestination(winner); return; }
        CompleteAttack(winner);
    }
    private void AdvanceWarfare(double seconds, double now, double shieldUntil, double duration)
    {
        warTime = now; protectionUntil = shieldUntil; matchDuration = duration;
        AdvanceCampBattles(now, shieldUntil);
        foreach (var army in armies.ToArray())
        {
            if (!armies.Contains(army)) continue;
            if (army.Order == "Scouting" && Distance(army.Position, army.Destination) <= 18)
            {
                ResolveScout(army, now);
                continue;
            }
            if (army.Order == "Attacking" && now >= shieldUntil)
            {
                var enemy = armies.FirstOrDefault(x => x.Id == army.TargetId);
                if (enemy is not null && Distance(army.Position, enemy.Position) <= 16)
                {
                    if (!CanAttackIntoActiveBattle(army, enemy))
                    {
                        army.Order = "Idle"; army.TargetId = null; army.Destination = army.Position;
                        Note(army.OwnerId, now, "Your army stopped short because the target is already in an ongoing battle without allies on your side.", "war");
                        continue;
                    }
                    var firstAttacker = enemy.Order == "Attacking" && enemy.TargetId == army.Id && enemy.AttackOrderedAt < army.AttackOrderedAt
                        ? enemy : army;
                    var secondDefender = firstAttacker == army ? enemy : army;
                    StartBattle(firstAttacker, secondDefender, now);
                }
            }
            if (army.Order == "Approaching" && now >= shieldUntil && villages.GetValueOrDefault(army.TargetId ?? Guid.Empty) is { } city
                && Distance(army.Position, city.Village.Position) <= 40)
            {
                var ongoing=sieges.Find(s=>s.City==city&&s.Status=="Active");
                if (ongoing is not null)
                {
                    if (CampAllied(ongoing.Attacker.OwnerId, army.OwnerId) && (ongoing.Objective == army.Objective || ongoing.Objective == AttackObjective.Blockade))
                    {
                        var escalated = ongoing.Objective == AttackObjective.Blockade && army.Objective != AttackObjective.Blockade;
                        if (escalated)
                        {
                            ongoing.Objective = army.Objective;
                            ongoing.Phase = "Fighting";
                            ongoing.Started = now;
                            ongoing.NextRound = now + 5;
                        }
                        if (!ongoing.Attackers.Contains(army)) ongoing.Attackers.Add(army);
                        army.Order = "Sieging"; AddReportArmy(reports.Single(r => r.Id == ongoing.Id), army, true);
                        ReportEvent(ongoing.Id, now, escalated ? "An allied assault force joined and escalated the blockade." : ongoing.Attacker.OwnerId == army.OwnerId ? "Another army joined the siege." : "An allied army joined the siege.");
                    }
                    else { Route(army); Note(army.OwnerId, now, "The target has a different ongoing attack objective. Your army is returning."); }
                    continue;
                }
                var guard = Guards(city.Village.Id, army.OwnerId).FirstOrDefault();
                if (guard is not null) { StartBattle(army, guard, now); continue; }
                if (armies.Any(a => a.DefendingSiteId == city.Village.Id && a.OwnerId != army.OwnerId && a.Order == "Battle")) continue;
                army.Order = "Sieging"; city.Besieged = true;
                var siege = new SiegeState(army, city, now); sieges.Add(siege);
                BeginReport(siege.Id, "Siege", now, army, city.Village.OwnerId, city.Village.Id, city.Village.Position);
                ReportEvent(siege.Id, now, $"Objective: {siege.Objective}. The village is surrounded. External deliveries are blocked and wheat production is reduced by 50%. Relief forces must break the siege.");
                Note(city.Village.OwnerId, now, "Your settlement is under siege. Send reinforcements to lift the siege.");
                Note(army.OwnerId, now, "Siege established. Your soldiers and siege engines attack automatically.");
            }
        }
        foreach(var battle in battles.Where(b=>b.Status=="Active").ToArray()) AdvanceBattle(battle,now);
        battles.RemoveAll(x => x.Status != "Active" && now - x.Ended > 30);
        foreach (var siege in sieges.Where(x => x.Status == "Active").ToArray()) AdvanceSiege(siege, now);
        sieges.RemoveAll(x => x.Status != "Active" && now - x.Ended > 30);
        armies.RemoveAll(x => Count(x) == 0);
        if (scoutedArmies.Count > 0)
            foreach (var key in scoutedArmies.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToArray())
                scoutedArmies.Remove(key);
        AdvancePolitics(seconds, now, duration);
    }
    private void AdvanceSiege(SiegeState siege, double now)
    {
        var attacker = siege.Attacker; var city = siege.City;
        if (!siege.Attackers.Any(a=>armies.Contains(a)&&Fighters(a)>0&&!a.Routing&&a.Order is "Sieging" or "Battle") || city.Ruined || city.Village.OwnerId == attacker.OwnerId)
        { EndSiege(siege, "Withdrawn", now); return; }
        if (siege.Attackers.Any(a=>a.Order=="Battle")) return;
        if (now < siege.NextRound) return;
        siege.NextRound = now + 5; siege.Round++;
        if (siege.Objective == AttackObjective.Blockade)
        {
            ReportEvent(siege.Id, now, "Blockade maintained. External deliveries are stopped; wheat production is halved.");
            return;
        }
        if (siege.Phase is "Persuading" or "Demolishing") { AdvanceCityObjective(siege, now); return; }
        var garrison = city.Garrison();
        var actualDefenders = garrison.Sum(x => x.Count);
        var wallBonus = GameCatalog.Tribes[city.Village.Tribe].Bonuses.WallMultiplier;
        var protection = 1 + city.BuildingLevel(BuildingType.Wall) * .04 * wallBonus * siege.Wall / 100;
        var attackers = siege.Attackers.Where(a => a.Order == "Sieging" && !a.Routing && Fighters(a) > 0).ToArray();
        siege.Gate = Math.Max(0, siege.Gate - attackers.Sum(a => RoleCount(a, UnitRole.Ram) * 12 + Fighters(a) * .03));
        siege.Wall = Math.Max(0, siege.Wall - attackers.Sum(a => RoleCount(a, UnitRole.Catapult) * 5));
        if (actualDefenders > 0)
        {
            var defenders = new Army(city.Village.OwnerId, city.Village.Position, city.Village.Id, garrison);
            var defenderMult = (siege.Gate > 0 ? .82 : 1.15) / protection;
            var attackerMult = 1 + (protection - 1) * .35;
            var pace = RoundPaceFactor(attackers.Sum(Count) + actualDefenders, siege.Round);
            var defPlan = PlanSideRoundDamage([defenders], attackers, false, defenderMult, pace, siege.DamagePool, _ => city.Village.Id);
            var attPlan = PlanSideRoundDamage(attackers, [defenders], true, attackerMult, pace, siege.DamagePool, a => a.Id);
            CommitSideRoundPlan([defenders], defPlan, siege.DamagePool, now, attacker.OwnerId, false);
            CommitSideRoundPlan(attackers, attPlan, siege.DamagePool, now, city.Village.OwnerId, true);
            city.ReplaceGarrison(defenders.Troops);
        }
        ReportEvent(siege.Id, now, $"Round {siege.Round}: gate {siege.Gate:0}%, wall {siege.Wall:0}%, garrison {city.Garrison().Sum(x => x.Count)}.");
        if (attackers.All(a => Count(a) == 0))
        {
            EndSiege(siege, "Defended", now);
            foreach (var force in attackers) { if (Count(force) > 0) Route(force); else armies.Remove(force); }
            return;
        }
        if (city.Garrison().Length == 0)
        {
            if (siege.Objective == AttackObjective.Raid)
            {
                foreach (var force in attackers)
                {
                    var plunder = city.Loot(.25 * GameCatalog.Tribes[villages[force.HomeVillageId].Village.Tribe].Bonuses.LootMultiplier, force.LootSpace);
                    var carried = Carried(force); TakeLoot(force, plunder);
                    var report = reports.Find(r => r.Id == siege.Id);
                    if (report is not null) { report.AttackerLoot += Gained(carried, Carried(force)); report.DefenderLoss += plunder; }
                }
                city.RaidedUntil = Math.Max(city.RaidedUntil, now + 65);
                EndSiege(siege, "Raided", now);
            }
            else
            {
                siege.Phase = siege.Objective == AttackObjective.Conquest ? "Persuading" : "Demolishing";
                siege.InitialBuildingLevels = city.Village.Buildings.Sum(b => b.Level);
                ReportEvent(siege.Id, now, siege.Phase == "Persuading" ? "The defenders fell. Persuasion rounds have begun." : "The defenders fell. Catapults are demolishing buildings level by level.");
            }
        }
    }
    private void EndSiege(SiegeState siege, string status, double now)
    {
        if (siege.Status != "Active") return;
        siege.Status = status; siege.Ended = now; siege.City.Besieged = false;
        var outcome = status switch
        {
            "Raided" => "The raid is complete. Loot travels with the surviving armies.",
            "SpecialistsLost" => "The required specialists were lost. The city operation has ended.",
            "Conquest" => "The village has been conquered and its ownership changed.",
            "Raze" => "The village was razed. Salvage is carried by the attacker.",
            "Defended" => "The defenders held. The siege has ended.",
            "Relieved" => "A relief force broke the siege.",
            _ => "The besieging army withdrew. The blockade has ended."
        };
        ReportEvent(siege.Id, now, outcome, status, status is "Raided" or "Conquest" or "Raze" ? siege.Attacker.OwnerId : siege.DefenderOwner);
        Note(siege.DefenderOwner, now, outcome + " Open Reports for casualties and plunder.", "war");
        foreach (var owner in siege.Attackers.Select(a => a.OwnerId).Distinct())
            Note(owner, now, outcome + " Open Reports for casualties and plunder.", "war");
        foreach(var force in siege.Attackers){if(force.Order is "Sieging"){force.Order="Idle";force.TargetId=null;}if(status is "Conquest" or "Raze" or "Raided" or "SpecialistsLost")CompleteAttack(force);}
    }
}
