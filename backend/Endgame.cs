namespace Warzone;

public sealed record SettlementSnapshot(Guid Id, Guid OwnerId, TribeType Tribe, Position Position, int Level, bool Ruined,
    bool Besieged, double RelocateIn, BiomeType Biome, double SmokeSeconds = 0, Position? RelocateTarget = null);
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
    private double warTime, protectionUntil, matchDuration = 1800, castleUnlockSeconds = 1800;
    public const double NomadRelocationSpeed = 10;
    public const double NomadRelocationCooldownSeconds = 900;
    public static readonly Resources NomadRelocationCost = new(4000, 4000, 4000, 4000);
    public const double NomadRelocationVillageSpacing = 350;
    public const double NomadRelocationCastleSpacing = 250;
    public const double NomadRelocationOasisSpacing = 150;
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
            if (!castle.Active) throw new GameRuleException("castle", $"The central castle opens at {Math.Round(castleUnlockSeconds / 60)} minutes.");
            var army = Owned(actor, command.TargetId);
            if (Engaged(army)) throw new GameRuleException("engaged", "This army cannot take castle orders now.");
            if (OrderFighters(army, command.Formation) == 0) throw new GameRuleException("troops", "Fighting troops are required to claim the castle.");
            army = Detach(army, command.Formation, now);
            army.DefendingSiteId = null; army.SupportedOwnerId = null; army.CampRadius = null;
            army.TargetId = castle.Id; army.Destination = castle.Position; army.Order = "Capturing"; return true;
        }
        if (command.Kind == CommandKind.RelocateVillage)
        {
            var city = EconomyTarget(actor, command.TargetId);
            if (!GameCatalog.Tribes[city.Village.Tribe].Bonuses.CanRelocate) throw new GameRuleException("tribe", "Only nomad settlements can relocate.");
            var underThreatBattle = armies.Any(a => a.DefendingSiteId == city.Village.Id && a.Order == "Battle");
            if (city.Besieged || underThreatBattle) throw new GameRuleException("cooldown", "Cannot relocate while under siege or active defense.");
            if (city.Relocating || now < city.RelocateAfter) throw new GameRuleException("cooldown", "This settlement cannot relocate yet.");
            var point = Point(command.X, command.Y);
            if (Distance(point, city.Village.Position) < 40)
                throw new GameRuleException("spacing", "Choose a destination further from your settlement's current position.");
            if (villages.Values.Any(x => x != city && !x.Ruined && (Distance(point, x.Village.Position) < NomadRelocationVillageSpacing
                    || (x.RelocateTarget is { } reserved && Distance(point, reserved) < NomadRelocationVillageSpacing)))
                || Distance(point, castle.Position) < NomadRelocationCastleSpacing
                || oases.Any(o => Distance(point, o.Position) < NomadRelocationOasisSpacing))
                throw new GameRuleException("spacing", $"Keep {NomadRelocationVillageSpacing:0} units from other settlements, {NomadRelocationCastleSpacing:0} from Crown Keep, and {NomadRelocationOasisSpacing:0} from oases.");
            city.Pay(NomadRelocationCost);
            CancelVillageOffers(city);
            city.RelocateTarget = point;
            city.RelocateAfter = now + NomadRelocationCooldownSeconds;
            Note(actor, now, "Your nomad caravan has packed the settlement and begun moving at speed 10 toward its new site."); return true;
        }
        return false;
    }
    private void ResolveCity(SiegeState siege, double now)
    {
        var city = siege.City; var attacker = siege.Attacker; var former = city.Village.OwnerId;
            city.RelocateTarget = null;
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
                city.SetHardBotBonuses(worldBotDifficulty == BotDifficulty.Hard && hardBotOwners.Contains(attacker.OwnerId));
                Note(attacker.OwnerId, now, "The city's loyalty reached zero. Your governor has established a new province.");
                Note(former, now, "Your city has been conquered.");
            }
            else
            {
                var salvage = city.Raze();
                var recipients = siege.Attackers.Where(a => armies.Contains(a) && Count(a) > 0).ToArray();
                if (recipients.Length == 0) recipients = [attacker];
                var captured = DistributeLoot(recipients, salvage);
                var report = reports.Find(r => r.Id == siege.Id);
                if (report is not null) { report.AttackerLoot += captured; report.DefenderLoss += salvage; }
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
    private readonly HashSet<Guid> hardBotOwners = [];
    private readonly HashSet<Guid> humanOwners = [];
    private BotDifficulty worldBotDifficulty = BotDifficulty.Normal;
    internal void ConfigureBotContext(BotDifficulty difficulty, IEnumerable<Guid> bots, IEnumerable<Guid> humans)
    {
        worldBotDifficulty = difficulty;
        hardBotOwners.Clear();
        foreach (var id in bots) hardBotOwners.Add(id);
        humanOwners.Clear();
        foreach (var id in humans) humanOwners.Add(id);
        foreach (var v in villages.Values)
            v.SetHardBotBonuses(difficulty == BotDifficulty.Hard && hardBotOwners.Contains(v.Village.OwnerId));
    }
    private void AdvanceNomadRelocations(double seconds, double now)
    {
        foreach (var city in villages.Values)
        {
            if (city.Ruined || city.RelocateTarget is not { } target) continue;
            if (city.Besieged || armies.Any(a => a.DefendingSiteId == city.Village.Id && a.Order == "Battle")) continue;
            Move(city.Village, target, NomadRelocationSpeed * seconds);
            city.Biome = Biomes.At(regions, city.Village.Position);
            if (Distance(city.Village.Position, target) < .01)
            {
                city.Village.Relocate(target);
                city.Biome = Biomes.At(regions, target);
                city.RelocateTarget = null;
                Note(city.Village.OwnerId, now, "Your nomad settlement has arrived at its new location. Return and supply routes follow its new position.");
            }
        }
    }
    private void AdvancePolitics(double seconds, double now, double duration)
    {
        AdvanceNomadRelocations(seconds, now);
        castle.Active = now >= castleUnlockSeconds;
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
            x.Ruined, x.Besieged, Math.Max(0, x.RelocateAfter - warTime), x.Biome, x.Ruined ? 0 : Math.Max(0, x.RaidedUntil - warTime), x.RelocateTarget)).ToArray(),
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

        var prioritizingHumans = worldBotDifficulty == BotDifficulty.Hard && humanOwners.Count > 0;
        if (prioritizingHumans)
        {
            var humanCities = enemyCities.Where(v => humanOwners.Contains(v.Village.OwnerId)).ToArray();
            if (humanCities.Length > 0) enemyCities = humanCities;
            else prioritizingHumans = false;
        }

        var allies = economies.Keys.Where(id => (id == actor || CampAllied(actor, id)) && Primary(id) is not null).OrderBy(id => id).ToArray();
        if (allies.Length <= 1)
        {
            var soloTarget = enemyCities.OrderBy(c =>
                (prioritizingHumans && sieges.Any(s => s.Status == "Active" && s.City == c) ? 1200 : 0)
                + Distance(c.Village.Position, home.Village.Position)
                + EffectiveCityThreatDefense(c) * 2
                + IncomingPressureOnOwner(c.Village.OwnerId, [actor]) * 550).First();
            return (soloTarget.Village.OwnerId, soloTarget);
        }

        var allySet = allies.ToHashSet();
        var distinctEnemyOwners = enemyCities.Select(c => c.Village.OwnerId).Distinct().Count();

        if (prioritizingHumans)
        {
            // Coordinate mass attack against human players first; open 2nd/3rd fronts if a human owns multiple cities and one is already besieged.
            var unbesiegedHumanCity = enemyCities
                .Where(c => !sieges.Any(s => s.Status == "Active" && s.City == c))
                .OrderBy(c => CityPressureStats(c.Village.Id, allySet).DistinctOwners * 900
                    + EffectiveCityThreatDefense(c) * 1.5
                    + Distance(c.Village.Position, home.Village.Position))
                .FirstOrDefault();
            if (unbesiegedHumanCity is not null && enemyCities.Any(c => sieges.Any(s => s.Status == "Active" && s.City == c)))
                return (unbesiegedHumanCity.Village.OwnerId, unbesiegedHumanCity);

            var primaryHuman = enemyCities
                .OrderBy(c => CityPressureStats(c.Village.Id, allySet).DistinctOwners * 400
                    + EffectiveCityThreatDefense(c) * 1.5
                    + Distance(c.Village.Position, home.Village.Position))
                .First();
            return (primaryHuman.Village.OwnerId, primaryHuman);
        }

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
            if (governors > 0 && (difficulty == BotDifficulty.Hard ? fighters >= 24 : isContender && fighters >= 35)) return AttackObjective.Conquest;
            return fighters >= (difficulty == BotDifficulty.Hard ? 20 : 26) ? AttackObjective.Raid : null;
        }
        if (difficulty == BotDifficulty.Hard)
        {
            if (catapults > 0) return AttackObjective.Raze;
            if (governors > 0 && fighters >= 24) return AttackObjective.Conquest;
        }
        if (catapults > 0 && (isContender && (personality == BotPersonality.Aggressive || hasAllies)))
            return AttackObjective.Raze;
        if (isContender && governors > 0 && fighters >= 35) return AttackObjective.Conquest;
        var cityDefense = targetCity.Garrison().Sum(t => t.Count) + targetCity.BuildingLevel(BuildingType.Wall) * 5 + Guards(targetCity.Village.Id, actor).Sum(Fighters);
        var alliedAssaultersApproaching = armies.Any(a => CampAllied(actor, a.OwnerId) && a.Order == "Approaching" && a.TargetId == targetCity.Village.Id && a.Objective != AttackObjective.Blockade);
        if (difficulty == BotDifficulty.Hard && Guards(targetCity.Village.Id, actor).Sum(Fighters) == 0 && fighters >= 18
            && fighters < cityDefense * .85 && (alliedAssaultersApproaching || cityDefense >= 35))
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

        var fieldCount = ActiveFieldArmyCount(ownArmies);
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
                if (fieldCount < maxArmies && ownArmies.Length < 12 && homeTroops >= 18)
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
            if (difficulty == BotDifficulty.Hard && fieldCount < maxArmies && ownArmies.Length < 12 && homeTroops >= 24)
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
            && fieldCount < maxArmies && ownArmies.Length < 12 && homeTroops >= 44
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
        var maxArmies = warTime < protectionUntil ? 3 : difficulty == BotDifficulty.Hard ? 6 : personality == BotPersonality.Aggressive ? 5 : 4;
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
            if (difficulty == BotDifficulty.Hard)
            {
                if (HandleHardBotCastleControl(actor, home, ownArmies, commands))
                    return true;
            }
            else
            {
                var allyHolding = castle.Controller is { } holder && holder != actor && CampAllied(actor, holder);
                var selfHolding = castle.Controller == actor;
                var champion = ownArmies.Where(a => !Engaged(a) && !a.Routing && a.Order != "Returning")
                    .OrderByDescending(Fighters).FirstOrDefault();
                if (allyHolding)
                {
                    if (champion is not null && Fighters(champion) >= 10)
                    {
                        var challenger = armies.FirstOrDefault(a => !CampAllied(actor, a.OwnerId) && !a.Routing && Fighters(a) > 0
                            && CanAttackIntoActiveBattle(actor, a)
                            && Distance(a.Position, castle.Position) < 60);
                        if (challenger is not null)
                        {
                            commands.Execute(actor, new(Guid.NewGuid(), CommandKind.AttackArmy, champion.Id, DestinationId: challenger.Id));
                            return true;
                        }
                    }
                }
                if (!allyHolding && (!selfHolding || castle.Contested) && champion is not null && Fighters(champion) >= 12)
                {
                    if (champion.Order != "Capturing" && Distance(champion.Position, castle.Position) > 35)
                        commands.Execute(actor, new(Guid.NewGuid(), CommandKind.CaptureCastle, champion.Id));
                    return true;
                }
                var nonGuardField = ActiveFieldArmyCount(ownArmies);
                if (!allyHolding && (!selfHolding || castle.Contested) && nonGuardField < 3 && ownArmies.Count(a => a.Order != "Guarding") < 10 && HomeCombatTroops(home) >= 18)
                {
                    var castleCap = Math.Max(75, (int)(HomeCombatTroops(home) * 0.6));
                    var castleForce = BuildBotFormation(home, castle.Position, castleCap, false, false, 4, difficulty);
                    if (castleForce is not null)
                    {
                        commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
                            castleForce.Values.Sum(), true, castle.Position.X, castle.Position.Y, UnitId(home, UnitRole.Infantry), 0, 2, Formation: castleForce));
                        return true;
                    }
                }
                if (!allyHolding && (!selfHolding || castle.Contested) && ownArmies.Any(a => a.Order is "Battle" or "Capturing"))
                    return HomeCombatTroops(home) < 120 || nonGuardField >= 4;
            }
        }

        if (warTime < protectionUntil) return false;

        // 3. Coordinated team focus target, kiting/ambush, convoy/cavalry interception, and multi-front sieges
        var focus = ResolveTeamFocusTarget(actor, home);
        var focusEnemyId = focus?.EnemyId;
        var focusCity = focus?.City;

        if (difficulty == BotDifficulty.Hard && TryHardBotKitingAndAmbush(actor, home, ownArmies, commands))
            return true;

        var strikeArmy = ownArmies.Where(a => IsReassignableArmy(actor, a, personality))
            .OrderByDescending(Fighters).FirstOrDefault();

        var fieldArmies = ActiveFieldArmyCount(ownArmies);
        var activeSiegeArmies = ownArmies.Count(a => a.Order is "Approaching" or "Sieging");
        var maxFieldArmies = difficulty == BotDifficulty.Hard ? 4 : 3;
        var minHomeDispatchThreshold = difficulty == BotDifficulty.Hard ? (activeSiegeArmies == 0 ? 45 : 75) : 55;
        if (strikeArmy is null || (fieldArmies < maxFieldArmies && ownArmies.Count(a => a.Order != "Guarding") < 8 && HomeCombatTroops(home) >= minHomeDispatchThreshold))
        {
            var targetCity = focusCity;
            var alliedPushingFocus = targetCity is not null && armies.Any(a => a.OwnerId != actor && CampAllied(actor, a.OwnerId)
                && a.Order is "Approaching" or "Sieging" && a.TargetId == targetCity.Village.Id);
            var ownPushingFocus = targetCity is not null && ownArmies.Any(a => a.Order is "Approaching" or "Sieging" && a.TargetId == targetCity.Village.Id);
            // If this bot is already attacking targetCity or allies are pushing, avoid dogpiling small squads; pick an unbesieged enemy city when ready.
            if ((alliedPushingFocus || ownPushingFocus) && fieldArmies < maxFieldArmies && ownArmies.Count(a => a.Order != "Guarding") < 8 && HomeCombatTroops(home) >= 55)
            {
                var altCity = villages.Values
                    .Where(v => !v.Ruined && v != targetCity && v.Village.OwnerId != actor && !CampAllied(actor, v.Village.OwnerId)
                        && !sieges.Any(s => s.City == v && s.Status == "Active")
                        && !armies.Any(a => CampAllied(actor, a.OwnerId) && a.Order is "Approaching" or "Sieging" && a.TargetId == v.Village.Id))
                    .OrderBy(v => (humanOwners.Contains(v.Village.OwnerId) ? -1400 : 0) + EffectiveCityThreatDefense(v) * 4 + Distance(v.Village.Position, home.Village.Position))
                    .FirstOrDefault();
                if (altCity is not null && (EffectiveCityThreatDefense(altCity) <= HomeCombatTroops(home) * 0.95 || !ownPushingFocus))
                {
                    targetCity = altCity;
                    alliedPushingFocus = false;
                    ownPushingFocus = false;
                }
            }
            var hasFocusOasisToHit = difficulty == BotDifficulty.Hard && !ownArmies.Any(a => a.Order == "Occupying")
                && oases.Any(o => o.OwnerId is { } oid && oid != actor && !CampAllied(actor, oid) && o.BurnedUntil <= warTime
                    && (oid == focusEnemyId || Distance(o.Position, home.Village.Position) <= 1800));
            var minHomeToDispatch = difficulty == BotDifficulty.Hard
                ? (activeSiegeArmies == 0 ? (hasFocusOasisToHit && HomeCombatTroops(home) < 75 ? 75 : 45) : (hasFocusOasisToHit ? 120 : 75))
                : alliedPushingFocus ? 32 : 42;
            var canLaunchFromHome = personality == BotPersonality.Aggressive || difficulty == BotDifficulty.Hard || alliedPushingFocus || HomeCombatTroops(home) >= 80;
            if (canLaunchFromHome && targetCity is not null && !ownPushingFocus && (difficulty != BotDifficulty.Hard || activeSiegeArmies < 2) && fieldArmies < maxFieldArmies && ownArmies.Count(a => a.Order != "Guarding") < 8 && HomeCombatTroops(home) >= minHomeToDispatch)
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
                    var siegeCap = difficulty == BotDifficulty.Hard ? Math.Max(120, (int)(HomeCombatTroops(home) * 0.85)) : Math.Max(75, (int)(HomeCombatTroops(home) * 0.65));
                    var siegeForce = BuildBotFormation(home, targetCity.Village.Position, siegeCap, true, !wantsRaze, 6, difficulty)
                        ?? (HomeCombatTroops(home) >= 200 || home.FoodShortage
                            ? BuildFastCavalryFormation(home, targetCity.Village.Position, siegeCap, 6, 16)
                            : null);
                    if (siegeForce is not null)
                    {
                        var cats = TryUnitId(home, UnitRole.Catapult) is { } cId ? siegeForce.GetValueOrDefault(cId) : 0;
                        var govs = TryUnitId(home, UnitRole.Governor) is { } gId ? siegeForce.GetValueOrDefault(gId) : 0;
                        var fighters = siegeForce.Where(x => GameCatalog.Units[x.Key].Role is not (UnitRole.Wagon or UnitRole.Governor)).Sum(x => x.Value);
                        var alliedCommitted = armies.Where(a => CampAllied(actor, a.OwnerId) && a.Order is "Approaching" or "Sieging" && a.TargetId == targetCity.Village.Id).Sum(Fighters);
                        var cityDefense = targetCity.Garrison().Sum(t => t.Count) + targetCity.BuildingLevel(BuildingType.Wall) * 5 + Guards(targetCity.Village.Id, actor).Sum(Fighters);
                        var obj = ChooseCityObjective(actor, targetCity, alliedSiege, fighters, cats, govs, personality, difficulty, hasAllies);
                        var minSiegeCutoff = difficulty == BotDifficulty.Hard ? 32 : 18;
                        if (obj is { } chosenObj && (fighters + alliedCommitted >= Math.Max(minSiegeCutoff, cityDefense * (difficulty == BotDifficulty.Hard ? .75 : personality == BotPersonality.Aggressive ? .75 : .95))
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

            // Fallback: if home still holds >= 120 combat troops and targetCity was blocked/unaffordable, launch a consolidated raid on any unbesieged enemy settlement.
            if (HomeCombatTroops(home) >= 120 && fieldArmies < maxFieldArmies && ownArmies.Count(a => a.Order != "Guarding") < 8)
            {
                var openEnemy = villages.Values
                    .Where(v => !v.Ruined && v.Village.OwnerId != actor && !CampAllied(actor, v.Village.OwnerId)
                        && !ownArmies.Any(a => a.Order is "Approaching" or "Sieging" && a.TargetId == v.Village.Id)
                        && !sieges.Any(s => s.City == v && s.Status == "Active" && !CampAllied(actor, s.Attacker.OwnerId)))
                    .OrderBy(v => Distance(v.Village.Position, home.Village.Position))
                    .FirstOrDefault();
                if (openEnemy is not null && BuildBotFormation(home, openEnemy.Village.Position, Math.Max(80, (int)(HomeCombatTroops(home) * 0.6)), false, false, 6, difficulty) is { } fallbackForce)
                {
                    var obj = sieges.FirstOrDefault(s => s.City == openEnemy && s.Status == "Active")?.Objective ?? AttackObjective.Raid;
                    if (commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
                            fallbackForce.Values.Sum(), true, openEnemy.Village.Position.X, openEnemy.Village.Position.Y,
                            UnitId(home, UnitRole.Infantry), 0, 2, openEnemy.Village.Id, Objective: obj, Formation: fallbackForce)).Accepted)
                        return true;
                }
            }
        }

        if (difficulty == BotDifficulty.Hard && !ownArmies.Any(a => a.Order == "Occupying") && strikeArmy is null
            && HomeCombatTroops(home) is >= 32 and < 75
            && oases.Any(o => o.OwnerId is { } oid && oid != actor && !CampAllied(actor, oid) && o.BurnedUntil <= warTime
                && (oid == focusEnemyId || Distance(o.Position, home.Village.Position) <= 1800)))
            return false;

        if (difficulty == BotDifficulty.Hard && TryHardBotIntercept(actor, home, ownArmies, focusEnemyId, commands))
            return true;

        if (strikeArmy is null)
        {
            return ownArmies.Any(a => a.Order is "Attacking" or "Approaching" or "Capturing" or "Battle" or "Sieging")
                && !(focusEnemyId is { } fe && oases.Any(o => o.OwnerId == fe && o.BurnedUntil <= warTime) && fieldArmies < maxFieldArmies && ownArmies.Count(a => a.Order != "Guarding") < 12 && HomeCombatTroops(home) >= (difficulty == BotDifficulty.Hard ? 36 : 18));
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

        var scanRange = (personality == BotPersonality.Aggressive ? 260 : personality == BotPersonality.Defensive ? 145 : 120) + (difficulty == BotDifficulty.Hard ? 120 : 0);
        var powerRatio = personality == BotPersonality.Aggressive ? .82 : difficulty == BotDifficulty.Hard ? .88 : 1.05;

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
                && (Distance(x.Position, strikeArmy.Position) < scanRange || (focusEnemyId == x.OwnerId && Distance(x.Position, strikeArmy.Position) < scanRange + 200))
                && Fighters(strikeArmy) >= Math.Max(10, Fighters(x) * powerRatio))
            .OrderBy(x => (humanOwners.Contains(x.OwnerId) ? -300 : 0) + (x.OwnerId == focusEnemyId ? -150 : 0) + Distance(x.Position, strikeArmy.Position)).FirstOrDefault();
        if (enemy is not null)
        {
            commands.Execute(actor, new(Guid.NewGuid(), CommandKind.AttackArmy, strikeArmy.Id, DestinationId: enemy.Id));
            return true;
        }

        if (personality != BotPersonality.Defensive || difficulty == BotDifficulty.Hard)
        {
            var convoyRange = difficulty == BotDifficulty.Hard ? 450 : 170;
            var convoy = convoys.Where(c => c.OwnerId != actor && !CampAllied(actor, c.OwnerId) && !c.InBattle
                    && Distance(c.Position, strikeArmy.Position) < convoyRange
                    && Fighters(strikeArmy) > c.Troops.Where(t => t.Unit is not SupplyWagon).Sum(t => t.Count) + 4)
                .OrderBy(c => (humanOwners.Contains(c.OwnerId) ? -250 : 0) + (c.OwnerId == focusEnemyId ? -130 : 0) + Distance(c.Position, strikeArmy.Position)).FirstOrDefault();
            if (convoy is not null)
            {
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.InterceptConvoy, strikeArmy.Id, DestinationId: convoy.Id));
                return true;
            }
        }

        var minSiegeSoldiers = difficulty == BotDifficulty.Hard ? 32 : personality == BotPersonality.Aggressive ? 24 : personality == BotPersonality.Defensive ? 36 : 40;
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
                    || strikeFighters + x.AlliedCommitted >= Math.Max(difficulty == BotDifficulty.Hard ? 32 : 18, x.Defense * (personality == BotPersonality.Aggressive || difficulty == BotDifficulty.Hard ? .8 : 1.0))))
                .OrderBy(x => (humanOwners.Contains(x.City.Village.OwnerId) ? -1400 : 0) + (x.City.Village.OwnerId == focusEnemyId ? -1000 : 0) + (x.AlliedCommitted > 0 ? -350 : 0) + Distance(x.City.Village.Position, strikeArmy.Position) + x.Defense * 3)
                .FirstOrDefault();

            if (candidateCities.City is not null && candidateCities.Objective is { } objective)
            {
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.BeginSiege, strikeArmy.Id, DestinationId: candidateCities.City.Village.Id, Objective: objective));
                return true;
            }
        }

        return strikeArmy.Order is "Attacking" or "Approaching" or "Capturing";
    }

    private bool HandleHardBotCastleControl(Guid actor, VillageEconomy home, Army[] ownArmies, IGameCommands commands)
    {
        var fieldCount = ActiveFieldArmyCount(ownArmies);
        var allyHolding = castle.Controller is { } holder && holder != actor && CampAllied(actor, holder);
        var selfHolding = castle.Controller == actor;
        var capturingCount = ownArmies.Count(a => a.Order == "Capturing");

        // 1. Intercept any hostile (prioritizing human) army closing within 350 units of the Castle or already targeting it.
        var challenger = armies
            .Where(a => a.OwnerId != actor && !CampAllied(actor, a.OwnerId) && !a.Routing && Fighters(a) > 0
                && CanAttackIntoActiveBattle(actor, a)
                && (Distance(a.Position, castle.Position) <= 350 || (a.TargetId == castle.Id && Distance(a.Position, castle.Position) <= 500)))
            .OrderBy(a => (humanOwners.Contains(a.OwnerId) ? -400 : 0) + Distance(a.Position, castle.Position))
            .FirstOrDefault();
        if (challenger is not null)
        {
            var interceptor = ownArmies
                .Where(a => !Engaged(a) && !a.Routing && !IsScoutOnly(a)
                    && a.Order is not ("Returning" or "Merging" or "Guarding" or "Camping")
                    && (a.Order != "Capturing" || allyHolding || (selfHolding && !castle.Contested) || capturingCount > 1)
                    && Fighters(a) >= Math.Max(10, (int)Math.Ceiling(Fighters(challenger) * 0.75)))
                .OrderBy(a => (a.TargetId == challenger.Id ? -200 : 0) + Distance(a.Position, challenger.Position))
                .FirstOrDefault();
            if (interceptor is not null)
            {
                if (interceptor.Order != "Attacking" || interceptor.TargetId != challenger.Id)
                {
                    commands.Execute(actor, new(Guid.NewGuid(), CommandKind.AttackArmy, interceptor.Id, DestinationId: challenger.Id));
                    return true;
                }
            }
            else if (fieldCount < 5 && ownArmies.Count(a => a.Order != "Guarding") < 12 && HomeCombatTroops(home) >= 34)
            {
                var waveCap = Math.Max(85, (int)(HomeCombatTroops(home) * 0.55));
                var cavFloor = warTime >= protectionUntil ? 28 : 8;
                var interceptWave = BuildFastCavalryFormation(home, challenger.Position, waveCap, 4, cavFloor)
                    ?? BuildBotFormation(home, challenger.Position, waveCap, false, false, 4, BotDifficulty.Hard);
                if (interceptWave is not null && commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
                        interceptWave.Values.Sum(), true, challenger.Position.X, challenger.Position.Y, UnitId(home, UnitRole.Infantry), 0, 2, Formation: interceptWave)).Accepted)
                {
                    var spawned = armies.LastOrDefault(a => a.OwnerId == actor && a.Order == "Marching");
                    if (spawned is not null)
                        commands.Execute(actor, new(Guid.NewGuid(), CommandKind.AttackArmy, spawned.Id, DestinationId: challenger.Id));
                    return true;
                }
            }
        }

        // 2. Capture Castle with any free field army (never pull stationary oasis guards) if unheld or contested.
        var minFreeCapture = HomeCombatTroops(home) >= 120 ? 60 : 24;
        if (!allyHolding && (!selfHolding || castle.Contested))
        {
            var freeArmy = ownArmies.Where(a => !Engaged(a) && !a.Routing && !IsScoutOnly(a)
                    && a.Order is "Idle" or "Marching" or "Approaching" && Fighters(a) >= minFreeCapture)
                .OrderByDescending(Fighters).FirstOrDefault();
            if (freeArmy is not null && Distance(freeArmy.Position, castle.Position) > 35)
            {
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.CaptureCastle, freeArmy.Id));
                return true;
            }
        }

        // 3. Send consolidated Castle capture waves when unheld/contested, or station perimeter guard armies around the Castle.
        var desiredCastleWaves = castle.Contested || castle.Controller is not null || challenger is not null ? 2 : 1;
        var castleCommitted = ownArmies.Count(a => !IsScoutOnly(a) && a.Order != "Guarding" && (a.Order == "Capturing" || Distance(a.Position, castle.Position) <= 280));
        if (!allyHolding && (!selfHolding || castle.Contested) && castleCommitted < desiredCastleWaves && fieldCount < 7 && ownArmies.Count(a => a.Order != "Guarding") < 12 && HomeCombatTroops(home) >= 36)
        {
            var castleCap = Math.Max(120, (int)(HomeCombatTroops(home) * 0.65));
            var castleWave = BuildBotFormation(home, castle.Position, castleCap, false, false, 4, BotDifficulty.Hard);
            if (castleWave is not null && commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
                    castleWave.Values.Sum(), true, castle.Position.X, castle.Position.Y, UnitId(home, UnitRole.Infantry), 0, 2, Formation: castleWave)).Accepted)
            {
                var spawned = armies.LastOrDefault(a => a.OwnerId == actor && a.Order == "Marching");
                if (spawned is not null)
                    commands.Execute(actor, new(Guid.NewGuid(), CommandKind.CaptureCastle, spawned.Id));
                return true;
            }
        }

        var guardCount = ownArmies.Count(a => a.Order is "Idle" or "Marching" && Distance(a.Position, castle.Position) is > 60 and <= 260);
        if ((allyHolding || selfHolding) && !castle.Contested && guardCount < 1 && fieldCount < 4 && ownArmies.Count(a => a.Order != "Guarding") < 12 && HomeCombatTroops(home) >= 36 && HomeCombatTroops(home) < 160)
        {
            var baseAngle = (Math.Abs(actor.GetHashCode()) % 360 + guardCount * 120) * Math.PI / 180.0;
            var guardPos = new Position(
                Math.Clamp(castle.Position.X + Math.Cos(baseAngle) * 165, MapMinimum + 100, MapMaximum - 100),
                Math.Clamp(castle.Position.Y + Math.Sin(baseAngle) * 165, MapMinimum + 100, MapMaximum - 100));
            var guardWave = BuildBotFormation(home, guardPos, 90, false, false, 4, BotDifficulty.Hard);
            if (guardWave is not null && commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
                    guardWave.Values.Sum(), true, guardPos.X, guardPos.Y, UnitId(home, UnitRole.Infantry), 0, 2, Formation: guardWave)).Accepted)
            {
                return true;
            }
        }

        // Only pause enemy village attacks while fighting/marching for the Castle unless a large home garrison is idle.
        return !allyHolding && (!selfHolding || castle.Contested)
            && ownArmies.Any(a => a.Order is "Battle" or "Capturing" or "Attacking")
            && (HomeCombatTroops(home) < 120 || fieldCount >= 6);
    }

    private bool TryHardBotKitingAndAmbush(Guid actor, VillageEconomy home, Army[] ownArmies, IGameCommands commands)
    {
        foreach (var army in ownArmies.OrderByDescending(Fighters))
        {
            var totalSoldiers = army.Troops.Sum(t => t.Count);
            if (Engaged(army) || army.Routing || army.Order is "Returning" or "Merging" or "Camping" || Fighters(army) < 4
                || RoleCount(army, UnitRole.Scout) >= totalSoldiers)
                continue;

            var threat = armies
                .Where(e => e.OwnerId != actor && !CampAllied(actor, e.OwnerId) && !e.Routing
                    && Fighters(e) > Fighters(army) * 1.15
                    && (Distance(e.Position, army.Position) < 260 || (e.TargetId == army.Id && Distance(e.Position, army.Position) < 420)))
                .OrderBy(e => (humanOwners.Contains(e.OwnerId) ? -250 : 0) + Distance(e.Position, army.Position))
                .FirstOrDefault();
            if (threat is null) continue;

            // Spring ambush immediately if combined friendly forces already nearby outweigh the pursuer.
            var nearbySupport = armies
                .Where(f => f.Id != army.Id && CampAllied(actor, f.OwnerId) && !f.Routing && Distance(f.Position, army.Position) <= 180)
                .Sum(Fighters);
            if (Fighters(army) + nearbySupport >= Fighters(threat) * 1.15 && CanAttackIntoActiveBattle(actor, threat))
            {
                if (army.Order != "Attacking" || army.TargetId != threat.Id)
                {
                    commands.Execute(actor, new(Guid.NewGuid(), CommandKind.AttackArmy, army.Id, DestinationId: threat.Id));
                    return true;
                }
                continue;
            }

            // Merge with a nearby own army to turn the tide.
            var mergePartner = ownArmies.FirstOrDefault(o => o.Id != army.Id && !Engaged(o) && !o.Routing
                && o.Order is not ("Returning" or "Battle" or "Sieging" or "Merging")
                && Distance(o.Position, army.Position) <= 650
                && Fighters(o) + Fighters(army) >= Fighters(threat) * 0.95);
            if (mergePartner is not null)
            {
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.MergeArmies, army.Id, TargetIds: [army.Id, mergePartner.Id]));
                return true;
            }

            // Otherwise lure the pursuing army toward a strong allied ambush cluster, or recall safely home.
            var ambushAnchor = armies
                .Where(f => f.Id != army.Id && CampAllied(actor, f.OwnerId) && f.OwnerId != actor && !f.Routing && !Engaged(f)
                    && Distance(f.Position, army.Position) is > 120 and <= 950)
                .Select(f => new
                {
                    Army = f,
                    ClusterPower = armies.Where(g => CampAllied(actor, g.OwnerId) && !g.Routing && Distance(g.Position, f.Position) <= 220).Sum(Fighters)
                })
                .Where(x => x.ClusterPower + Fighters(army) >= Fighters(threat) * 1.05)
                .OrderBy(x => Distance(x.Army.Position, army.Position))
                .FirstOrDefault();
            if (ambushAnchor is not null)
            {
                if (army.Order != "Marching" || Distance(army.Position, ambushAnchor.Army.Position) > 130)
                {
                    commands.Execute(actor, new(Guid.NewGuid(), CommandKind.MoveArmy, army.Id, X: ambushAnchor.Army.Position.X, Y: ambushAnchor.Army.Position.Y));
                    return true;
                }
                continue;
            }

            if (Distance(army.Position, home.Village.Position) > 120)
            {
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.RecallArmy, army.Id));
                return true;
            }
        }

        return false;
    }

    private bool TryHardBotIntercept(Guid actor, VillageEconomy home, Army[] ownArmies, Guid? focusEnemyId, IGameCommands commands)
    {
        var fieldCount = ActiveFieldArmyCount(ownArmies);
        if (ownArmies.Count(a => a.Order is "Intercepting" or "Attacking") >= 2)
            return false;
        // Intercept vulnerable convoys first with fast reassignable forces or a newly dispatched cavalry wing.
        var targetConvoy = convoys
            .Where(c => c.OwnerId != actor && !CampAllied(actor, c.OwnerId) && !c.InBattle)
            .OrderBy(c => (humanOwners.Contains(c.OwnerId) ? -500 : 0) + (c.OwnerId == focusEnemyId ? -150 : 0) + Distance(c.Position, home.Village.Position))
            .FirstOrDefault();
        if (targetConvoy is not null)
        {
            var escortFighters = targetConvoy.Troops.Where(t => t.Unit is not SupplyWagon).Sum(t => t.Count);
            var chaser = ownArmies
                .Where(a => !Engaged(a) && !a.Routing && a.Order is "Idle" or "Marching" && Distance(a.Position, targetConvoy.Position) < 550
                    && Fighters(a) > escortFighters + 4 && RoleCount(a, UnitRole.Catapult) == 0)
                .OrderBy(a => Distance(a.Position, targetConvoy.Position))
                .FirstOrDefault();
            if (chaser is not null && commands.Execute(actor, new(Guid.NewGuid(), CommandKind.InterceptConvoy, chaser.Id, DestinationId: targetConvoy.Id)).Accepted)
                return true;

            if (fieldCount < 5 && ownArmies.Count(a => a.Order != "Guarding") < 12 && Distance(targetConvoy.Position, home.Village.Position) < 850 && HomeCombatTroops(home) >= Math.Max(16, escortFighters + 10))
            {
                var cavRaid = BuildFastCavalryFormation(home, targetConvoy.Position, Math.Clamp(escortFighters + 24, 16, 56), 4, 8);
                if (cavRaid is not null && commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
                        cavRaid.Values.Sum(), true, targetConvoy.Position.X, targetConvoy.Position.Y, UnitId(home, UnitRole.Infantry), 0, 2, Formation: cavRaid)).Accepted)
                {
                    var spawned = armies.LastOrDefault(a => a.OwnerId == actor && a.Order == "Marching");
                    if (spawned is not null)
                        commands.Execute(actor, new(Guid.NewGuid(), CommandKind.InterceptConvoy, spawned.Id, DestinationId: targetConvoy.Id));
                    return true;
                }
            }
        }

        // Dispatch consolidated fast cavalry from home to hunt exposed hostile field armies without spamming tiny squads.
        if (fieldCount < 5 && ownArmies.Count(a => a.Order != "Guarding") < 12 && ownArmies.Count(a => a.Order is "Intercepting" or "Attacking") == 0 && HomeCombatTroops(home) >= 40)
        {
            var vulnerableArmy = armies
                .Where(a => a.OwnerId != actor && !CampAllied(actor, a.OwnerId) && !a.Routing && Fighters(a) is >= 10 and <= 30
                    && CanAttackIntoActiveBattle(actor, a)
                    && Distance(a.Position, home.Village.Position) < 650
                    && !ownArmies.Any(o => o.Order == "Attacking" && o.TargetId == a.Id))
                .OrderBy(a => (humanOwners.Contains(a.OwnerId) ? -450 : 0) + (a.OwnerId == focusEnemyId ? -150 : 0) + Distance(a.Position, home.Village.Position))
                .FirstOrDefault();
            if (vulnerableArmy is not null && HomeCombatTroops(home) >= Math.Max(32, (int)Math.Ceiling(Fighters(vulnerableArmy) * 1.4) + 6))
            {
                var hunterSquad = BuildFastCavalryFormation(home, vulnerableArmy.Position, Math.Clamp(Fighters(vulnerableArmy) * 2 + 12, 28, 65), 4, warTime >= protectionUntil ? 28 : 8);
                if (hunterSquad is not null && commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
                        hunterSquad.Values.Sum(), true, vulnerableArmy.Position.X, vulnerableArmy.Position.Y, UnitId(home, UnitRole.Infantry), 0, 2, Formation: hunterSquad)).Accepted)
                {
                    var spawned = armies.LastOrDefault(a => a.OwnerId == actor && a.Order == "Marching");
                    if (spawned is not null)
                        commands.Execute(actor, new(Guid.NewGuid(), CommandKind.AttackArmy, spawned.Id, DestinationId: vulnerableArmy.Id));
                    return true;
                }
            }
        }

        return false;
    }
}
