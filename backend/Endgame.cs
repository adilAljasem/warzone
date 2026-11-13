namespace Warzone;

public sealed record SettlementSnapshot(Guid Id, Guid OwnerId, TribeType Tribe, Position Position, int Level, bool Ruined,
    bool Besieged, double RelocateIn, BiomeType Biome, double SmokeSeconds = 0);
public sealed record RankSnapshot(Guid PlayerId, long Score, double CastleSeconds, int Settlements, int Kills, int Dead, int Wounded, int Fled, bool Eliminated);
public sealed record CastleSnapshot(Guid Id, Position Position, bool Active, Guid? ControllerId, Guid? ClaimantId, double CaptureProgress, bool Contested);
public sealed record WarfareSnapshot(SettlementSnapshot[] Settlements, BattleSnapshot[] Battles, SiegeSnapshot[] Sieges, CastleSnapshot Castle, RankSnapshot[] Rankings);
public sealed record MatchResult(string Reason, Guid[] WinnerIds, RankSnapshot[] Rankings);
internal sealed class WarStatistics
{
    public int Dead; public int Wounded; public int Fled; public int Kills; public int LossSequence;
    public double CastleSeconds;
}
internal sealed class CentralCastle() : MapEntity(Guid.NewGuid(), new(LogisticsWorld.MapCenter, LogisticsWorld.MapCenter))
{
    public bool Active; public Guid? Controller; public Guid? Claimant; public double Progress; public bool Contested;
}

public sealed partial class LogisticsWorld
{
    private readonly Dictionary<Guid, WarStatistics> statistics = [];
    private readonly CentralCastle castle = new();
    private double warTime, protectionUntil, matchDuration = 1800;
    private void InitializeWarfare() { foreach (var owner in economies.Keys) statistics[owner] = new(); }
    private void RegisterLoss(Army force, BaseUnit unit, int count, double now, Guid enemy, bool deployed)
    {
        if (count <= 0) return;
        var home = villages[force.HomeVillageId];
        if (deployed) home.LoseDeployed(count);
        if (unit is SupplyWagon or SiegeEngine) { force.DeadCount += count; ReportLosses(force, unit, 0, 0, 0, count, deployed); return; }
        var stats = statistics[force.OwnerId];
        var woundedCount = 0; var deadCount = 0; var fledCount = 0;
        for (var i = 0; i < count; i++)
        {
            var category = stats.LossSequence++ % 10;
            if (category < 6) { woundedCount++; stats.Wounded++; }
            else if (category < 9) { stats.Dead++; deadCount++; if (enemy != force.OwnerId) statistics[enemy].Kills++; }
            else { stats.Fled++; fledCount++; }
        }
        force.WoundedCount += woundedCount; force.DeadCount += deadCount; force.FledCount += fledCount;
        ReportLosses(force, unit, deadCount, woundedCount, fledCount, 0, deployed);
        home.AdmitWounded(unit.UnitId, woundedCount, now + 60 + Distance(force.Position, home.Village.Position) / unit.Speed);
    }
    private static void TakeLoot(Army army, Resources loot)
    {
        if (loot.Total == 0) return;
        var space = army.LootSpace;
        if (space <= 0) return;
        army.Loot += space >= loot.Total ? loot : loot.Fraction(space / (double)loot.Total);
    }
    private bool ApplyPolitics(Guid actor, GameCommand command, double now)
    {
        if (command.Kind == CommandKind.CaptureCastle)
        {
            if (!castle.Active) throw new GameRuleException("castle", "The central castle opens at 30 minutes.");
            var army = Owned(actor, command.TargetId);
            if (army.Routing || Engaged(army)) throw new GameRuleException("engaged", "This army cannot take castle orders now.");
            if (OrderFighters(army, command.Formation) == 0) throw new GameRuleException("troops", "Fighting troops are required to claim the castle.");
            army = Detach(army, command.Formation, now);
            army.DefendingSiteId = null; army.SupportedOwnerId = null; army.CampRadius = null;
            army.TargetId = castle.Id; army.Destination = castle.Position; army.Order = "Capturing"; return true;
        }
        if (command.Kind == CommandKind.RelocateVillage)
        {
            var city = EconomyTarget(actor, command.TargetId);
            if (!GameCatalog.Tribes[city.Village.Tribe].Bonuses.CanRelocate) throw new GameRuleException("tribe", "Only nomad settlements can relocate.");
            if (city.Besieged || now < city.RelocateAfter) throw new GameRuleException("cooldown", "This settlement cannot relocate yet.");
            var point = Point(command.X, command.Y);
            if (villages.Values.Any(x => x != city && !x.Ruined && Distance(point, x.Village.Position) < 120) || Distance(point, castle.Position) < 100)
                throw new GameRuleException("spacing", "Keep 120 map units from another settlement and 100 from the castle.");
            city.Pay(new(300, 0, 0, 300)); city.Village.Relocate(point); city.Biome = Biomes.At(regions, point); city.RelocateAfter = now + 120;
            Note(actor, now, "Your nomad settlement has relocated. Return and supply routes now follow its new position."); return true;
        }
        return false;
    }
    private void ResolveCity(SiegeState siege, double now)
    {
        var city = siege.City; var attacker = siege.Attacker; var former = city.Village.OwnerId;
            CancelVillageOffers(city);
            var fallback = villages.Values.FirstOrDefault(x => x != city && !x.Ruined && x.Village.OwnerId == former);
            if (fallback is not null) fallback.ReceiveGift(city.Garrison());
            city.ReplaceGarrison([]);
            if (siege.Objective == AttackObjective.Conquest)
            {
                var governor = attacker.Troops.First(x => x.Unit.Definition.Role == UnitRole.Governor);
                villages[attacker.HomeVillageId].LoseDeployed(1);
                attacker.Troops = Array.AsReadOnly(attacker.Troops.Select(x => x == governor ? x.Count > 1 ? new TroopStack(x.Unit, x.Count - 1) : null : x)
                    .Where(x => x is not null).Cast<TroopStack>().ToArray());
                city.ChangeOwner(attacker.OwnerId, fallback);
                Note(attacker.OwnerId, now, "The city's loyalty reached zero. Your governor has established a new province.");
                Note(former, now, "Your city has been conquered.");
            }
            else
            {
                var before = Carried(attacker);
                var salvage = city.Raze(); TakeLoot(attacker, salvage);
                var report = reports.Find(r => r.Id == siege.Id);
                if (report is not null) { report.AttackerLoot += Gained(before, Carried(attacker)); report.DefenderLoss += salvage; }
                Note(attacker.OwnerId, now, "Every building has been destroyed. The village has been removed from the map.");
                Note(former, now, "Your city has been reduced to ruins.");
            }
            foreach (var oasis in oases.Where(o => o.VillageId == city.Village.Id)) { oasis.OwnerId = null; oasis.VillageId = null; }
            RefreshOasisBonuses();
            Rehome(former, city, fallback, now);
        EndSiege(siege, siege.Objective.ToString(), now);
    }
    private void Rehome(Guid formerOwner, VillageEconomy lost, VillageEconomy? fallback, double now)
    {
        foreach (var battle in battles.Where(x => x.Status == "Active" && (x.Attacker.OwnerId == formerOwner || x.Defender.OwnerId == formerOwner)).ToArray())
            if (fallback is null) EndBattle(battle, battle.Attacker.OwnerId == formerOwner ? battle.Defender : battle.Attacker, now);
        foreach (var army in armies.Where(x => x.OwnerId == formerOwner).ToArray())
        {
            if (fallback is null) { villages[army.HomeVillageId].LoseDeployed(Count(army)); armies.Remove(army); continue; }
            if (army.HomeVillageId == lost.Village.Id)
            { lost.Rebase(fallback, Count(army)); army.HomeVillageId = fallback.Village.Id; }
            if (army.SupplyVillageId == lost.Village.Id) army.SupplyVillageId = fallback.Village.Id;
        }
        foreach (var convoy in convoys.Where(x => x.OwnerId == formerOwner).ToArray())
        {
            if (fallback is null) { villages[convoy.SourceVillageId].LoseDeployed(convoy.Troops.Sum(x => x.Count)); convoys.Remove(convoy); continue; }
            if (convoy.SourceVillageId == lost.Village.Id)
            { lost.Rebase(fallback, convoy.Troops.Sum(x => x.Count)); convoy.SourceVillageId = fallback.Village.Id; convoy.Returning = true; }
        }
        if (fallback is null)
        {
            economies.Remove(formerOwner);
            Note(formerOwner, now, "Your last settlement has fallen. You can follow the remaining match as a spectator.");
        }
        else economies[formerOwner] = fallback;
    }
    private Position ConvoyDestination(Convoy convoy) => convoy.Returning ? ConvoyCityDestination(convoy,villages[convoy.SourceVillageId])
        : convoy.OasisId is { } oasisId && oases.Find(o => o.Id == oasisId) is { } oasis ? oasis.Position
        : convoy.TargetVillageId is { } cityId && villages.TryGetValue(cityId, out var city) ? ConvoyCityDestination(convoy,city)
        : armies.FirstOrDefault(a => a.Id == convoy.TargetArmyId)?.Position ?? villages[convoy.SourceVillageId].Village.Position;
    public Guid? CrownWinner { get; private set; }
    private readonly Dictionary<string, (Guid EnemyId, double ExpiresAt)> teamFocusTargets = [];
    private readonly Dictionary<string, Guid> lastTeamFocusTargets = [];
    private void AdvancePolitics(double seconds, double now, double duration)
    {
        castle.Active = now >= 1800;
        if (!castle.Active) return;
        var present = armies.Where(x => Distance(x.Position, castle.Position) < 45 && !x.Routing && Fighters(x) > 0).ToArray();
        var owners = present.Select(x => x.OwnerId).Distinct().ToArray();
        var alliedHolders = owners.Length > 0 && owners.All(o => CampAllied(owners[0], o));
        castle.Contested = owners.Length > 1 && !alliedHolders;
        if (castle.Contested)
        {
            Army? defender;
            Army? attacker;
            if (castle.Controller is { } controller)
            {
                defender = present.Where(x => !Engaged(x) && (x.OwnerId == controller || CampAllied(x.OwnerId, controller)))
                    .OrderByDescending(x => x.OwnerId == controller)
                    .ThenBy(x => Distance(x.Position, castle.Position))
                    .FirstOrDefault();
                attacker = present.Where(x => !Engaged(x) && x.OwnerId != controller && !CampAllied(x.OwnerId, controller))
                    .OrderBy(x => Distance(x.Position, castle.Position))
                    .FirstOrDefault();
            }
            else
            {
                defender = present.Where(x => !Engaged(x))
                    .OrderBy(x => Distance(x.Position, castle.Position))
                    .FirstOrDefault();
                attacker = defender is null ? null : present.FirstOrDefault(x => !CampAllied(defender.OwnerId, x.OwnerId) && !Engaged(x));
            }
            if (attacker is not null && defender is not null) StartBattle(attacker, defender, now);
            return;
        }
        if (owners.Length == 0) { castle.Progress = 0; castle.Claimant = null; castle.Controller = null; return; }
        var keptByHolderOrAlly = castle.Controller is { } prior && owners.Any(o => o == prior || CampAllied(o, prior));
        var owner = castle.Controller is { } existing && owners.Contains(existing) ? existing : owners[0];
        if (!keptByHolderOrAlly)
        {
            castle.Controller = owner; castle.Claimant = owner; castle.TransferTo(owner); castle.Progress = 0;
            foreach (var player in statistics.Keys) Note(player, now, "The central castle has a new ruler. Hold it for 5 continuous minutes to win the match.");
        }
        else if (castle.Controller != owner)
        {
            castle.Controller = owner; castle.Claimant = owner; castle.TransferTo(owner);
        }
        castle.Progress = Math.Min(300, castle.Progress + seconds);
        statistics[owner].CastleSeconds += seconds;
        if (castle.Progress >= 300 && CrownWinner is null)
        {
            CrownWinner = owner;
            foreach (var player in statistics.Keys)
                Note(player, now, player == owner ? "You held Crown Keep for 5 continuous minutes and won the match!" : "Crown Keep was held for 5 continuous minutes. The match is over!");
        }
    }
    public RankSnapshot[] Rankings() => statistics.Select(entry =>
    {
        var owned = villages.Values.Where(x => !x.Ruined && x.Village.OwnerId == entry.Key).ToArray();
        var stats = entry.Value;
        var score = (long)Math.Floor(stats.CastleSeconds * 10) + owned.Sum(x => x.Village.Level * 30L + x.Resources.Total / 200)
            + stats.Kills * 5L + owned.Sum(x => x.Garrison().Sum(t => t.Count)) / 10;
        return new RankSnapshot(entry.Key, score, Math.Floor(stats.CastleSeconds), owned.Length, stats.Kills, stats.Dead, stats.Wounded, stats.Fled, owned.Length == 0);
    }).OrderBy(x => x.Eliminated).ThenByDescending(x => x.Score).ThenBy(x => x.PlayerId).ToArray();
    public bool Dominated => statistics.Keys.Count(x => Primary(x) is not null) <= 1;
    public MatchResult Result(string reason)
    {
        var ranks = Rankings();
        if (CrownWinner is { } crownWinner) return new(reason, [crownWinner], ranks);
        var survivors = ranks.Where(x => !x.Eliminated).ToArray();
        var winners = survivors.Length == 0 ? [] : survivors.Where(x => x.Score == survivors[0].Score).Select(x => x.PlayerId).ToArray();
        return new(reason, winners, ranks);
    }
    private WarfareSnapshot WarfareSnapshot(Guid viewer) => new(
        villages.Values.Select(x => new SettlementSnapshot(x.Village.Id, x.Village.OwnerId, x.Village.Tribe, x.Village.Position, x.Village.Level,
            x.Ruined, x.Besieged, Math.Max(0, x.RelocateAfter - warTime), x.Biome, x.Ruined ? 0 : Math.Max(0, x.RaidedUntil - warTime))).ToArray(),
        battles.Select(x => new BattleSnapshot(x.Id, x.Attacker.Position, x.Attacker.Id, x.Convoy?.Id ?? x.Defender.Id, x.Attacker.OwnerId, x.Defender.OwnerId,
            x.Convoy is null ? "Army" : "Convoy", Math.Max(0, (x.Status == "Active" ? warTime : x.Ended) - x.Started), x.Round,
            x.Attackers.Where(m=>!m.Withdrawn).Sum(m=>Count(m.Army)), x.Defenders.Where(m=>!m.Withdrawn).Sum(m=>Count(m.Army)), x.Status, x.Winner, x.Attackers.Where(m=>!m.Withdrawn).Select(m=>m.Army.Id).ToArray(), x.Defenders.Where(m=>!m.Withdrawn).Select(m=>m.Army.Id).ToArray())).ToArray(),
        sieges.Select(x => new SiegeSnapshot(x.Id, x.Attacker.Id, x.City.Village.Id, x.Attacker.OwnerId, x.DefenderOwner, x.City.Village.Position,
            Math.Max(0, (x.Status == "Active" ? warTime : x.Ended) - x.Started), x.Wall, x.Gate, x.Loyalty, x.Status, x.Attackers.Where(a=>a.Order is "Sieging" or "Battle").Select(a=>a.Id).ToArray(), x.Objective, x.Phase, x.City.Village.Buildings.Sum(b=>b.Level), x.Progress)).ToArray(),
        new(castle.Id, castle.Position, castle.Active, castle.Controller, castle.Claimant, castle.Progress, castle.Contested), Rankings());
    private bool InActiveBattle(Army army) => battles.Any(x => x.Status == "Active" && x.ActiveContains(army));
    private bool SiteUnderThreat(Guid? siteId)
    {
        if (siteId is not { } id) return false;
        if (villages.TryGetValue(id, out var v))
            return sieges.Any(s => s.Status == "Active" && s.City == v)
                || armies.Any(a => !a.Routing && a.Order == "Approaching" && a.TargetId == id && !CampAllied(v.Village.OwnerId, a.OwnerId));
        if (oases.Find(o => o.Id == id) is { } oasis)
            return OasisUnderAttack(oasis) || oasisSieges.Any(s => s.Status == "Active" && s.Oasis == oasis);
        return false;
    }
    private bool IsReassignableArmy(Guid actor, Army a, BotPersonality personality)
    {
        if (Engaged(a) || a.Routing) return false;
        if (a.Order == "Idle") return true;
        if (a.Order == "Camped")
            return !armies.Any(e => e.OwnerId != actor && !CampAllied(actor, e.OwnerId) && !e.Routing && Distance(a.Position, e.Position) <= CampRadius * 2.5)
                && !convoys.Any(c => c.OwnerId != actor && !CampAllied(actor, c.OwnerId) && !c.InBattle && Distance(a.Position, c.Position) <= CampRadius * 2.5);
        if (a.Order != "Guarding" || SiteUnderThreat(a.DefendingSiteId)) return false;
        if (a.SupportedOwnerId is { } sup && sup != actor)
            return armies.All(e => CampAllied(actor, e.OwnerId) || e.Routing || Distance(e.Position, a.Position) > 450);
        return (Primary(actor)?.OwnedOases ?? 0) == 0
            || armies.Count(x => x.OwnerId == actor && x.DefendingSiteId == a.DefendingSiteId) > 1
            || (warTime >= protectionUntil && Fighters(a) >= (personality == BotPersonality.Aggressive ? 22 : 30));
    }
    private int EffectiveCityThreatDefense(VillageEconomy city)
    {
        var ownerId = city.Village.OwnerId;
        var nearbyField = armies
            .Where(a => a.OwnerId == ownerId && !a.Routing && a.Order is "Idle" or "Marching" or "Camped"
                && Distance(a.Position, city.Village.Position) <= 500)
            .Sum(Fighters);
        return city.Garrison().Sum(t => t.Count) + city.BuildingLevel(BuildingType.Wall) * 6 + Guards(city.Village.Id, ownerId).Sum(Fighters) + nearbyField / 2;
    }
    private int IncomingPressureOnOwner(Guid enemyId, IEnumerable<Guid> allies)
    {
        var allySet = allies as HashSet<Guid> ?? [.. allies];
        var activeSiegeCount = sieges.Count(s => s.Status == "Active" && s.DefenderOwner == enemyId && allySet.Contains(s.Attacker.OwnerId));
        var activeApproachCount = armies.Count(a => !a.Routing && a.Order == "Approaching" && allySet.Contains(a.OwnerId)
            && a.TargetId is { } tid && villages.TryGetValue(tid, out var vc) && !vc.Ruined && vc.Village.OwnerId == enemyId);
        return activeSiegeCount + activeApproachCount;
    }
    private (int DistinctOwners, int CommittedFighters) CityPressureStats(Guid cityId, HashSet<Guid> allySet)
    {
        var attackers = armies.Where(a => !a.Routing && a.Order is "Approaching" or "Sieging" && a.TargetId == cityId && allySet.Contains(a.OwnerId)).ToArray();
        return (attackers.Select(a => a.OwnerId).Distinct().Count(), attackers.Sum(Fighters));
    }
    private (Guid EnemyId, VillageEconomy City)? ResolveTeamFocusTarget(Guid actor, VillageEconomy home)
    {
        var enemyCities = villages.Values.Where(v => !v.Ruined && v.Village.OwnerId != actor && !CampAllied(actor, v.Village.OwnerId)).ToArray();
        if (enemyCities.Length == 0) return null;

        var allies = economies.Keys.Where(id => (id == actor || CampAllied(actor, id)) && Primary(id) is not null).OrderBy(id => id).ToArray();
        if (allies.Length <= 1)
        {
            var soloTarget = enemyCities.OrderBy(c =>
                Distance(c.Village.Position, home.Village.Position)
                + EffectiveCityThreatDefense(c) * 2
                + IncomingPressureOnOwner(c.Village.OwnerId, [actor]) * 550).First();
            return (soloTarget.Village.OwnerId, soloTarget);
        }

        var allySet = allies.ToHashSet();
        var distinctEnemyOwners = enemyCities.Select(c => c.Village.OwnerId).Distinct().Count();

        // Converge up to two allies on an active siege/march; overflow extra allies to spread multi-front pressure across all enemy players.
        var activeAlliedSiege = sieges.Where(s => s.Status == "Active" && allySet.Contains(s.Attacker.OwnerId) && !CampAllied(actor, s.DefenderOwner) && !s.City.Ruined)
            .OrderBy(s => s.Started).FirstOrDefault();
        if (activeAlliedSiege is not null)
        {
            var stats = CityPressureStats(activeAlliedSiege.City.Village.Id, allySet);
            var targetDefense = EffectiveCityThreatDefense(activeAlliedSiege.City);
            if (distinctEnemyOwners <= 1 || (stats.DistinctOwners < 2 && stats.CommittedFighters < Math.Max(28, targetDefense * 0.9)))
                return (activeAlliedSiege.DefenderOwner, activeAlliedSiege.City);
        }

        var alliedMarch = armies.Where(a => allySet.Contains(a.OwnerId) && a.Order == "Approaching" && a.TargetId is { } tid
                && villages.TryGetValue(tid, out var vc) && !vc.Ruined && !CampAllied(actor, vc.Village.OwnerId))
            .OrderBy(a => a.AttackOrderedAt).FirstOrDefault();
        if (alliedMarch is not null)
        {
            var city = villages[alliedMarch.TargetId!.Value];
            var stats = CityPressureStats(city.Village.Id, allySet);
            var targetDefense = EffectiveCityThreatDefense(city);
            if (distinctEnemyOwners <= 1 || (stats.DistinctOwners < 2 && stats.CommittedFighters < Math.Max(24, targetDefense * 0.85)))
                return (city.Village.OwnerId, city);
        }

        var allyHomes = allies.Select(id => Primary(id)!).ToArray();
        var centroid = new Position(allyHomes.Average(h => h.Village.Position.X), allyHomes.Average(h => h.Village.Position.Y));
        var teamKey = string.Join(':', allies);
        if (teamFocusTargets.TryGetValue(teamKey, out var cached) && cached.ExpiresAt > warTime
            && enemyCities.Any(c => c.Village.OwnerId == cached.EnemyId)
            && (distinctEnemyOwners <= 1 || IncomingPressureOnOwner(cached.EnemyId, allySet) < 2))
        {
            var stickyCity = enemyCities.Where(c => c.Village.OwnerId == cached.EnemyId)
                .OrderBy(c => Distance(c.Village.Position, centroid)).First();
            return (cached.EnemyId, stickyCity);
        }

        var hasLastTarget = lastTeamFocusTargets.TryGetValue(teamKey, out var previousEnemyId);
        var chosen = enemyCities.GroupBy(c => c.Village.OwnerId)
            .Select(g =>
            {
                var city = g.OrderBy(c => Distance(c.Village.Position, centroid)).First();
                var attackingTeam = sieges.Any(s => s.Status == "Active" && s.Attacker.OwnerId == g.Key && allySet.Contains(s.DefenderOwner))
                    || armies.Any(a => a.OwnerId == g.Key && !a.Routing && a.Order == "Approaching" && a.TargetId is { } tid
                        && villages.TryGetValue(tid, out var vc) && allySet.Contains(vc.Village.OwnerId));
                var defense = EffectiveCityThreatDefense(city);
                var pressurePenalty = IncomingPressureOnOwner(g.Key, allySet) * 650;
                var repeatPenalty = distinctEnemyOwners > 1 && hasLastTarget && previousEnemyId == g.Key ? 420 : 0;
                var distScore = Distance(centroid, city.Village.Position) * 0.55 + Distance(home.Village.Position, city.Village.Position) * 0.45;
                var score = distScore + defense * 2.2 + pressurePenalty + repeatPenalty - (attackingTeam ? 180 : 0);
                return (EnemyId: g.Key, City: city, Score: score);
            })
            .OrderBy(x => x.Score).ThenBy(x => x.EnemyId).First();

        lastTeamFocusTargets[teamKey] = chosen.EnemyId;
        teamFocusTargets[teamKey] = (chosen.EnemyId, warTime + 45);
        return (chosen.EnemyId, chosen.City);
    }
    private AttackObjective? ChooseCityObjective(Guid actor, VillageEconomy targetCity, SiegeState? alliedSiege,
        int fighters, int catapults, int governors, BotPersonality personality, BotDifficulty difficulty, bool hasAllies)
    {
        var isContender = villages.Values.Count(v => !v.Ruined && v.Village.OwnerId == targetCity.Village.OwnerId) > 1
            || armies.Any(a => a.OwnerId == targetCity.Village.OwnerId) || targetCity.Village.Level > 12;
        if (alliedSiege is not null)
        {
            if (alliedSiege.Objective == AttackObjective.Raze) return catapults > 0 ? AttackObjective.Raze : null;
            if (alliedSiege.Objective == AttackObjective.Conquest) return governors > 0 ? AttackObjective.Conquest : null;
            if (alliedSiege.Objective == AttackObjective.Raid) return AttackObjective.Raid;
            if (catapults > 0 && (difficulty == BotDifficulty.Hard || (isContender && (personality == BotPersonality.Aggressive || hasAllies)))) return AttackObjective.Raze;
            if (isContender && governors > 0 && fighters >= 35) return AttackObjective.Conquest;
            return fighters >= 26 ? AttackObjective.Raid : null;
        }
        if (catapults > 0 && (difficulty == BotDifficulty.Hard || (isContender && (personality == BotPersonality.Aggressive || hasAllies))))
            return AttackObjective.Raze;
        if (isContender && governors > 0 && fighters >= 35) return AttackObjective.Conquest;
        var cityDefense = targetCity.Garrison().Sum(t => t.Count) + targetCity.BuildingLevel(BuildingType.Wall) * 5 + Guards(targetCity.Village.Id, actor).Sum(Fighters);
        if (difficulty == BotDifficulty.Hard && Guards(targetCity.Village.Id, actor).Sum(Fighters) == 0 && fighters >= 18
            && (fighters < cityDefense * .9 || (hasAllies && fighters < 28)))
            return AttackObjective.Blockade;
        return AttackObjective.Raid;
    }
    private bool HelpAlliedBots(Guid actor, VillageEconomy home, Army[] ownArmies, IGameCommands commands, BotPersonality personality, BotDifficulty difficulty, int maxArmies)
    {
        var allies = economies.Keys.Where(id => id != actor && CampAllied(actor, id) && Primary(id) is not null).ToArray();
        if (allies.Length == 0) return false;

        var helper = ownArmies.Where(a => IsReassignableArmy(actor, a, personality) && Fighters(a) >= 10)
            .OrderByDescending(Fighters).FirstOrDefault();
        var homeTroops = HomeCombatTroops(home);

        // 1. Join nearby active allied field battles
        var allyBattle = battles.FirstOrDefault(b => b.Status == "Active"
            && (SideAlliedWith(b.Attackers, actor) ^ SideAlliedWith(b.Defenders, actor))
            && !b.Members.Any(m => !m.Withdrawn && m.Army.OwnerId == actor));
        if (allyBattle is not null && helper is not null)
        {
            var opposingSide = SideAlliedWith(allyBattle.Attackers, actor) ? allyBattle.Defenders : allyBattle.Attackers;
            var enemyInBattle = opposingSide
                .Where(m => !m.Withdrawn && !m.Army.Routing && Count(m.Army) > 0 && !CampAllied(actor, m.Army.OwnerId))
                .Select(m => m.Army).FirstOrDefault();
            if (enemyInBattle is not null && CanAttackIntoActiveBattle(actor, enemyInBattle)
                && Distance(helper.Position, enemyInBattle.Position) <= (difficulty == BotDifficulty.Hard ? 1150 : 750))
            {
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.AttackArmy, helper.Id, DestinationId: enemyInBattle.Id));
                return true;
            }
        }

        // 2. Reinforce or relieve allied settlements under siege or approaching threat
        var threatenedCity = villages.Values
            .Where(v => !v.Ruined && v.Village.OwnerId != actor && CampAllied(actor, v.Village.OwnerId) && SiteUnderThreat(v.Village.Id))
            .OrderByDescending(v => sieges.Any(s => s.Status == "Active" && s.City == v))
            .ThenBy(v => Distance(v.Village.Position, home.Village.Position))
            .FirstOrDefault();
        if (threatenedCity is not null)
        {
            var activeSiege = sieges.FirstOrDefault(s => s.Status == "Active" && s.City == threatenedCity);
            var approaching = armies.FirstOrDefault(a => !a.Routing && a.Order == "Approaching" && a.TargetId == threatenedCity.Village.Id && !CampAllied(actor, a.OwnerId));
            var threat = activeSiege?.Attacker ?? approaching;
            var alreadyHelping = ownArmies.Any(a => (a.Order is "Reinforcing" or "Guarding" && a.DefendingSiteId == threatenedCity.Village.Id)
                || (threat is not null && a.Order == "Attacking" && a.TargetId == threat.Id));
            if (!alreadyHelping)
            {
                if (helper is not null)
                {
                    if (activeSiege is not null && CanAttackIntoActiveBattle(actor, activeSiege.Attacker) && Fighters(helper) >= Math.Max(12, (int)(Fighters(activeSiege.Attacker) * .6)))
                        commands.Execute(actor, new(Guid.NewGuid(), CommandKind.AttackArmy, helper.Id, DestinationId: activeSiege.Attacker.Id));
                    else
                        commands.Execute(actor, new(Guid.NewGuid(), CommandKind.Reinforce, helper.Id, DestinationId: threatenedCity.Village.Id));
                    return true;
                }
                if (ownArmies.Length < maxArmies && homeTroops >= 18)
                {
                    var relief = BuildBotFormation(home, threatenedCity.Village.Position, 70, false, false, 4);
                    if (relief is not null)
                    {
                        commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
                            relief.Values.Sum(), true, threatenedCity.Village.Position.X, threatenedCity.Village.Position.Y,
                            UnitId(home, UnitRole.Infantry), 0, 2, threatenedCity.Village.Id, Reinforcement: true, Formation: relief));
                        return true;
                    }
                }
            }
        }

        // 3. Reinforce allied oases under attack
        var threatenedAllyOasis = oases
            .Where(o => o.OwnerId is { } owner && owner != actor && CampAllied(actor, owner) && o.BurnedUntil <= warTime && SiteUnderThreat(o.Id))
            .OrderBy(o => Distance(o.Position, home.Village.Position)).FirstOrDefault();
        if (threatenedAllyOasis is not null && !ownArmies.Any(a => a.Order is "Reinforcing" or "Guarding" && a.DefendingSiteId == threatenedAllyOasis.Id))
        {
            if (helper is not null && Distance(helper.Position, threatenedAllyOasis.Position) <= (difficulty == BotDifficulty.Hard ? 1000 : 650))
            {
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.Reinforce, helper.Id, DestinationId: threatenedAllyOasis.Id));
                return true;
            }
            if (difficulty == BotDifficulty.Hard && ownArmies.Length < maxArmies - 1 && homeTroops >= 24)
            {
                var relief = BuildBotFormation(home, threatenedAllyOasis.Position, 50, false, false, 6);
                if (relief is not null)
                {
                    commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
                        relief.Values.Sum(), true, threatenedAllyOasis.Position.X, threatenedAllyOasis.Position.Y,
                        UnitId(home, UnitRole.Infantry), 0, 2, threatenedAllyOasis.Id, Reinforcement: true, Formation: relief));
                    return true;
                }
            }
        }

        // 4. Proactive reinforcement for vulnerable teammates on Hard or Defensive bots
        if ((difficulty == BotDifficulty.Hard || personality == BotPersonality.Defensive)
            && ownArmies.Length < maxArmies - 1 && homeTroops >= 44
            && !ownArmies.Any(a => a.Order is "Reinforcing" or "Guarding" && a.SupportedOwnerId is { } s && s != actor))
        {
            var vulnerableAlly = allies.Select(id => Primary(id)!).Where(v =>
            {
                var alliedGuards = armies.Where(a => a.DefendingSiteId == v.Village.Id && a.Order is "Guarding" or "Reinforcing" && CampAllied(actor, a.OwnerId)).Sum(Fighters);
                return v.Garrison().Sum(t => t.Count) + alliedGuards < 16;
            }).OrderBy(v => Distance(v.Village.Position, home.Village.Position)).FirstOrDefault();
            if (vulnerableAlly is not null)
            {
                var guardForce = BuildBotFormation(home, vulnerableAlly.Village.Position, 16, false, false, 24);
                if (guardForce is not null)
                {
                    commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
                        guardForce.Values.Sum(), true, vulnerableAlly.Village.Position.X, vulnerableAlly.Village.Position.Y,
                        UnitId(home, UnitRole.Infantry), 0, 2, vulnerableAlly.Village.Id, Reinforcement: true, Formation: guardForce));
                    return true;
                }
            }
        }

        return false;
    }

    private bool WarBotAct(Guid actor, IGameCommands commands, BotPersonality personality, BotDifficulty difficulty = BotDifficulty.Normal)
    {
        var home = Primary(actor); if (home is null) return true;
        var ownArmies = armies.Where(x => x.OwnerId == actor).ToArray();
        var maxArmies = warTime < protectionUntil ? 4 : difficulty == BotDifficulty.Hard ? 6 : personality == BotPersonality.Aggressive ? 5 : 4;
        var hasAllies = economies.Keys.Any(id => id != actor && CampAllied(actor, id) && Primary(id) is not null);

        // 1. Emergency homeland and oasis defense
        if (warTime >= protectionUntil)
        {
            var activeSiege = sieges.FirstOrDefault(s => s.Status == "Active" && s.City.Village.OwnerId == actor);
            var approachingThreat = armies.FirstOrDefault(a => a.OwnerId != actor && !CampAllied(actor, a.OwnerId) && !a.Routing && a.Order == "Approaching"
                && villages.TryGetValue(a.TargetId ?? Guid.Empty, out var c) && c.Village.OwnerId == actor);
            var invader = activeSiege?.Attacker ?? approachingThreat;
            if (invader is not null && CanAttackIntoActiveBattle(actor, invader) && !invader.Routing)
            {
                var defenderArmy = ownArmies.Where(a => !Engaged(a) && !a.Routing && a.Order is "Idle" or "Guarding" or "Occupying" && Fighters(a) >= 10)
                    .OrderBy(a => Distance(a.Position, invader.Position)).FirstOrDefault();
                if (defenderArmy is not null)
                {
                    commands.Execute(actor, new(Guid.NewGuid(), CommandKind.AttackArmy, defenderArmy.Id, DestinationId: invader.Id));
                    return true;
                }
                if (activeSiege is not null && ownArmies.Length < 4 && HomeCombatTroops(activeSiege.City) >= Math.Max(14, (int)(Fighters(invader) * .7)))
                {
                    var sally = BuildBotFormation(activeSiege.City, invader.Position, 80, false, false, 0);
                    if (sally is not null)
                    {
                        commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DispatchArmy, activeSiege.City.Village.Id,
                            sally.Values.Sum(), true, invader.Position.X, invader.Position.Y, UnitId(activeSiege.City, UnitRole.Infantry), 0, 2, Formation: sally));
                        return true;
                    }
                }
            }

            var threatenedOasis = oases.FirstOrDefault(o => o.OwnerId == actor && OasisUnderAttack(o));
            if (threatenedOasis is not null)
            {
                var raider = armies.FirstOrDefault(a => a.OwnerId != actor && !CampAllied(actor, a.OwnerId) && !a.Routing && CanAttackIntoActiveBattle(actor, a)
                    && (a.TargetId == threatenedOasis.Id || Distance(a.Position, threatenedOasis.Position) < 25));
                var relief = raider is null ? null : ownArmies.FirstOrDefault(a => !Engaged(a) && !a.Routing && a.Order is "Idle" or "Guarding"
                    && a.DefendingSiteId != threatenedOasis.Id && Fighters(a) >= Math.Max(10, Fighters(raider) * 4 / 5)
                    && Distance(a.Position, threatenedOasis.Position) < 550);
                if (raider is not null && relief is not null)
                {
                    commands.Execute(actor, new(Guid.NewGuid(), CommandKind.AttackArmy, relief.Id, DestinationId: raider.Id));
                    return true;
                }
            }

            // 1b. Allied defense & team reinforcements
            if (HelpAlliedBots(actor, home, ownArmies, commands, personality, difficulty, maxArmies))
                return true;
        }

        // 2. Central Castle finale contest
        if (castle.Active)
        {
            var allyHolding = castle.Controller is { } holder && holder != actor && CampAllied(actor, holder);
            var champion = ownArmies.Where(a => !Engaged(a) && !a.Routing && a.Order != "Returning")
                .OrderByDescending(Fighters).FirstOrDefault();
            if (allyHolding)
            {
                if (champion is not null && Fighters(champion) >= 10)
                {
                    var challenger = armies.FirstOrDefault(a => !CampAllied(actor, a.OwnerId) && !a.Routing && Fighters(a) > 0
                        && CanAttackIntoActiveBattle(actor, a)
                        && (Distance(a.Position, castle.Position) < 60
                            || (difficulty == BotDifficulty.Hard && (a.TargetId == castle.Id || Distance(a.Position, castle.Position) < 220))));
                    if (challenger is not null)
                    {
                        commands.Execute(actor, new(Guid.NewGuid(), CommandKind.AttackArmy, champion.Id, DestinationId: challenger.Id));
                        return true;
                    }
                }
                if (!castle.Contested)
                    return false;
            }
            if (!allyHolding && champion is not null && Fighters(champion) >= 12)
            {
                if (champion.Order != "Capturing" && Distance(champion.Position, castle.Position) > 35)
                    commands.Execute(actor, new(Guid.NewGuid(), CommandKind.CaptureCastle, champion.Id));
                return true;
            }
            if (!allyHolding && ownArmies.Length < 3 && HomeCombatTroops(home) >= 18)
            {
                var castleForce = BuildBotFormation(home, castle.Position, 75, false, false, 4, difficulty);
                if (castleForce is not null)
                {
                    commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
                        castleForce.Values.Sum(), true, castle.Position.X, castle.Position.Y, UnitId(home, UnitRole.Infantry), 0, 2, Formation: castleForce));
                    return true;
                }
            }
            return ownArmies.Any(a => a.Order is "Battle" or "Capturing" or "Returning");
        }

        if (warTime < protectionUntil) return false;

        // 3. Coordinated team focus target, field interception, tactical regroup, convoy raids, and settlement sieges
        var focus = ResolveTeamFocusTarget(actor, home);
        var focusEnemyId = focus?.EnemyId;
        var focusCity = focus?.City;

        var strikeArmy = ownArmies.Where(a => IsReassignableArmy(actor, a, personality))
            .OrderByDescending(Fighters).FirstOrDefault();

        if (strikeArmy is null)
        {
            var maxFieldArmies = difficulty == BotDifficulty.Hard ? 3 : personality == BotPersonality.Aggressive ? 3 : 2;
            var targetCity = focusCity;
            var alliedPushingFocus = targetCity is not null && armies.Any(a => a.OwnerId != actor && CampAllied(actor, a.OwnerId)
                && a.Order is "Approaching" or "Sieging" && a.TargetId == targetCity.Village.Id);
            // If focus city already has allied siege pressure and Hard bot has surplus capacity, open a second front against a vulnerable undefended enemy city.
            if (difficulty == BotDifficulty.Hard && alliedPushingFocus && ownArmies.Length < maxFieldArmies && HomeCombatTroops(home) >= 34)
            {
                var altCity = villages.Values
                    .Where(v => !v.Ruined && v.Village.OwnerId != actor && !CampAllied(actor, v.Village.OwnerId)
                        && (targetCity is null || v.Village.OwnerId != targetCity.Village.OwnerId)
                        && !sieges.Any(s => s.City == v && s.Status == "Active"))
                    .OrderBy(v => EffectiveCityThreatDefense(v) * 4 + Distance(v.Village.Position, home.Village.Position))
                    .FirstOrDefault();
                if (altCity is not null && EffectiveCityThreatDefense(altCity) <= HomeCombatTroops(home) * 0.85)
                {
                    targetCity = altCity;
                    alliedPushingFocus = false;
                }
            }
            var minHomeToDispatch = alliedPushingFocus ? 24 : difficulty == BotDifficulty.Hard ? 30 : 34;
            var canLaunchFromHome = personality == BotPersonality.Aggressive || difficulty == BotDifficulty.Hard || alliedPushingFocus;
            if (canLaunchFromHome && targetCity is not null && ownArmies.Length < maxFieldArmies && HomeCombatTroops(home) >= minHomeToDispatch)
            {
                var activeFocusSiege = sieges.FirstOrDefault(s => s.City == targetCity && s.Status == "Active");
                if (activeFocusSiege is null || CampAllied(actor, activeFocusSiege.Attacker.OwnerId))
                {
                    var alliedSiege = activeFocusSiege is not null && CampAllied(actor, activeFocusSiege.Attacker.OwnerId) ? activeFocusSiege : null;
                    var hasCatapult = TryUnitId(home, UnitRole.Catapult) is { } catId && home.Available(catId) > 0;
                    var isContender = villages.Values.Count(v => !v.Ruined && v.Village.OwnerId == targetCity.Village.OwnerId) > 1
                        || armies.Any(a => a.OwnerId == targetCity.Village.OwnerId) || targetCity.Village.Level > 12;
                    var wantsRaze = hasCatapult && (difficulty == BotDifficulty.Hard || (isContender && (personality == BotPersonality.Aggressive || hasAllies)))
                        && (alliedSiege is null || alliedSiege.Objective is AttackObjective.Raze or AttackObjective.Blockade);
                    var siegeForce = BuildBotFormation(home, targetCity.Village.Position, 75, true, !wantsRaze, 6, difficulty);
                    if (siegeForce is not null)
                    {
                        var cats = TryUnitId(home, UnitRole.Catapult) is { } cId ? siegeForce.GetValueOrDefault(cId) : 0;
                        var govs = TryUnitId(home, UnitRole.Governor) is { } gId ? siegeForce.GetValueOrDefault(gId) : 0;
                        var fighters = siegeForce.Where(x => GameCatalog.Units[x.Key].Role is not (UnitRole.Wagon or UnitRole.Governor)).Sum(x => x.Value);
                        var alliedCommitted = armies.Where(a => CampAllied(actor, a.OwnerId) && a.Order is "Approaching" or "Sieging" && a.TargetId == targetCity.Village.Id).Sum(Fighters);
                        var cityDefense = targetCity.Garrison().Sum(t => t.Count) + targetCity.BuildingLevel(BuildingType.Wall) * 5 + Guards(targetCity.Village.Id, actor).Sum(Fighters);
                        var obj = ChooseCityObjective(actor, targetCity, alliedSiege, fighters, cats, govs, personality, difficulty, hasAllies);
                        if (obj is { } chosenObj && (fighters + alliedCommitted >= Math.Max(18, cityDefense * (difficulty == BotDifficulty.Hard || personality == BotPersonality.Aggressive ? .75 : .95))
                            || chosenObj == AttackObjective.Blockade))
                        {
                            commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
                                siegeForce.Values.Sum(), true, targetCity.Village.Position.X, targetCity.Village.Position.Y,
                                UnitId(home, UnitRole.Infantry), 0, 2, targetCity.Village.Id, Objective: chosenObj, Formation: siegeForce));
                            return true;
                        }
                    }
                }
            }
            return ownArmies.Any(a => a.Order is "Attacking" or "Approaching" or "Capturing" or "Battle" or "Sieging")
                && !(focusEnemyId is { } fe && oases.Any(o => o.OwnerId == fe && o.BurnedUntil <= warTime) && ownArmies.Length < maxArmies && HomeCombatTroops(home) >= 18);
        }

        // Tactical regroup / retreat if a much stronger hostile field force closes in on this isolated army
        if (difficulty != BotDifficulty.Easy && !Engaged(strikeArmy) && !strikeArmy.Routing)
        {
            var ambushThreat = armies.FirstOrDefault(e => e.OwnerId != actor && !CampAllied(actor, e.OwnerId) && !e.Routing
                && Distance(e.Position, strikeArmy.Position) < 150 && Fighters(e) > Fighters(strikeArmy) * 1.45);
            if (ambushThreat is not null)
            {
                var partner = ownArmies.FirstOrDefault(o => o.Id != strikeArmy.Id && o.Order == "Idle" && !Engaged(o) && !o.Routing
                    && Distance(o.Position, strikeArmy.Position) < 600
                    && Fighters(o) + Fighters(strikeArmy) >= Fighters(ambushThreat) * 0.95);
                if (partner is not null)
                {
                    commands.Execute(actor, new(Guid.NewGuid(), CommandKind.MergeArmies, strikeArmy.Id, TargetIds: [strikeArmy.Id, partner.Id]));
                    return true;
                }
                if (Distance(strikeArmy.Position, home.Village.Position) > 120)
                {
                    commands.Execute(actor, new(Guid.NewGuid(), CommandKind.RecallArmy, strikeArmy.Id));
                    return true;
                }
            }
        }

        var scanRange = (personality == BotPersonality.Aggressive ? 260 : personality == BotPersonality.Defensive ? 145 : 120) + (difficulty == BotDifficulty.Hard ? 80 : 0);
        var powerRatio = personality == BotPersonality.Aggressive ? .82 : difficulty == BotDifficulty.Hard ? .92 : 1.05;

        // Clear threatening hostile camps near homeland or allied cities
        var hostileCamp = armies.Where(x => x.OwnerId != actor && !CampAllied(actor, x.OwnerId) && x.CampRadius is not null && CanAttackIntoActiveBattle(actor, x) && !x.Routing
                && (Distance(x.Position, home.Village.Position) < 340 || economies.Keys.Any(id => id != actor && CampAllied(actor, id) && Primary(id) is { } allyHome && Distance(x.Position, allyHome.Village.Position) < 300))
                && Fighters(strikeArmy) >= Math.Max(10, Fighters(x) * powerRatio))
            .OrderBy(x => Distance(x.Position, strikeArmy.Position)).FirstOrDefault();
        if (hostileCamp is not null)
        {
            commands.Execute(actor, new(Guid.NewGuid(), CommandKind.AttackArmy, strikeArmy.Id, DestinationId: hostileCamp.Id));
            return true;
        }

        var enemy = armies.Where(x => x.OwnerId != actor && !CampAllied(actor, x.OwnerId) && CanAttackIntoActiveBattle(actor, x) && !x.Routing
                && (Distance(x.Position, strikeArmy.Position) < scanRange || (focusEnemyId == x.OwnerId && Distance(x.Position, strikeArmy.Position) < scanRange + 150))
                && Fighters(strikeArmy) >= Math.Max(10, Fighters(x) * powerRatio))
            .OrderBy(x => (x.OwnerId == focusEnemyId ? -150 : 0) + Distance(x.Position, strikeArmy.Position)).FirstOrDefault();
        if (enemy is not null)
        {
            commands.Execute(actor, new(Guid.NewGuid(), CommandKind.AttackArmy, strikeArmy.Id, DestinationId: enemy.Id));
            return true;
        }

        if (personality != BotPersonality.Defensive || difficulty == BotDifficulty.Hard)
        {
            var convoyRange = difficulty == BotDifficulty.Hard ? 290 : 170;
            var convoy = convoys.Where(c => c.OwnerId != actor && !CampAllied(actor, c.OwnerId) && !c.InBattle
                    && Distance(c.Position, strikeArmy.Position) < convoyRange
                    && Fighters(strikeArmy) > c.Troops.Where(t => t.Unit is not SupplyWagon).Sum(t => t.Count) + 4)
                .OrderBy(c => (c.OwnerId == focusEnemyId ? -130 : 0) + Distance(c.Position, strikeArmy.Position)).FirstOrDefault();
            if (convoy is not null)
            {
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.InterceptConvoy, strikeArmy.Id, DestinationId: convoy.Id));
                return true;
            }
        }

        var minSiegeSoldiers = difficulty == BotDifficulty.Hard ? 20 : personality == BotPersonality.Aggressive ? 24 : personality == BotPersonality.Defensive ? 36 : 40;
        if (Fighters(strikeArmy) >= minSiegeSoldiers)
        {
            var strikeFighters = Fighters(strikeArmy);
            var strikeCats = RoleCount(strikeArmy, UnitRole.Catapult);
            var strikeGovs = RoleCount(strikeArmy, UnitRole.Governor);
            var candidateCities = villages.Values
                .Where(x => x.Village.OwnerId != actor && !CampAllied(actor, x.Village.OwnerId) && !x.Ruined)
                .Select(x =>
                {
                    var active = sieges.FirstOrDefault(s => s.City == x && s.Status == "Active");
                    var alliedSiege = active is not null && CampAllied(actor, active.Attacker.OwnerId) ? active : null;
                    var blockedByRival = active is not null && alliedSiege is null;
                    var alliedCommitted = armies.Where(a => CampAllied(actor, a.OwnerId) && a.Order is "Approaching" or "Sieging" && a.TargetId == x.Village.Id).Sum(Fighters);
                    var defense = EffectiveCityThreatDefense(x);
                    var obj = blockedByRival ? null : ChooseCityObjective(actor, x, alliedSiege, strikeFighters, strikeCats, strikeGovs, personality, difficulty, hasAllies);
                    return (City: x, Defense: defense, AlliedCommitted: alliedCommitted, Objective: obj);
                })
                .Where(x => x.Objective is not null && (x.Objective == AttackObjective.Blockade
                    || strikeFighters + x.AlliedCommitted >= Math.Max(18, x.Defense * (personality == BotPersonality.Aggressive || difficulty == BotDifficulty.Hard ? .8 : 1.0))))
                .OrderBy(x => (x.City.Village.OwnerId == focusEnemyId ? -1000 : 0) + (x.AlliedCommitted > 0 ? -350 : 0) + Distance(x.City.Village.Position, strikeArmy.Position) + x.Defense * 3)
                .FirstOrDefault();

            if (candidateCities.City is not null && candidateCities.Objective is { } objective)
            {
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.BeginSiege, strikeArmy.Id, DestinationId: candidateCities.City.Village.Id, Objective: objective));
                return true;
            }
        }

        return strikeArmy.Order is "Attacking" or "Approaching" or "Capturing";
    }
}
