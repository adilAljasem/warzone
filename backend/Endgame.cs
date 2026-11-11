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
        if (unit is SupplyWagon or SiegeEngine) { ReportLosses(force, unit, 0, 0, 0, count, deployed); return; }
        var stats = statistics[force.OwnerId];
        var woundedCount = 0; var deadCount = 0; var fledCount = 0;
        for (var i = 0; i < count; i++)
        {
            var category = stats.LossSequence++ % 10;
            if (category < 6) { woundedCount++; stats.Wounded++; }
            else if (category < 9) { stats.Dead++; deadCount++; if (enemy != force.OwnerId) statistics[enemy].Kills++; }
            else { stats.Fled++; fledCount++; }
        }
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
    private void AdvancePolitics(double seconds, double now, double duration)
    {
        castle.Active = now >= 1800;
        if (!castle.Active) return;
        var present = armies.Where(x => Distance(x.Position, castle.Position) < 45 && !x.Routing && Fighters(x) > 0 && x.Morale > 10).ToArray();
        var owners = present.Select(x => x.OwnerId).Distinct().ToArray();
        castle.Contested = owners.Length > 1;
        if (castle.Contested)
        {
            var attacker = present.FirstOrDefault(x => !Engaged(x));
            var defender = present.FirstOrDefault(x => x.OwnerId != attacker?.OwnerId && !Engaged(x));
            if (attacker is not null && defender is not null) StartBattle(attacker, defender, now);
            castle.Progress = 0; castle.Claimant = null; castle.Controller = null; return;
        }
        if (owners.Length == 0) { castle.Progress = 0; castle.Claimant = null; castle.Controller = null; return; }
        var owner = owners[0];
        if (castle.Controller != owner)
        {
            castle.Controller = owner; castle.Claimant = owner; castle.TransferTo(owner); castle.Progress = 0;
            foreach (var player in statistics.Keys) Note(player, now, "The central castle has a new ruler. Hold it for 5 continuous minutes to win the match.");
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
            x.Convoy is null ? "Army" : "Convoy", Math.Min(120, (x.Status == "Active" ? warTime : x.Ended) - x.Started), x.Round,
            x.Attackers.Where(m=>!m.Withdrawn).Sum(m=>Count(m.Army)), x.Defenders.Where(m=>!m.Withdrawn).Sum(m=>Count(m.Army)), x.Attacker.Morale, x.Defender.Morale, x.Status, x.Winner, x.Attackers.Where(m=>!m.Withdrawn).Select(m=>m.Army.Id).ToArray(), x.Defenders.Where(m=>!m.Withdrawn).Select(m=>m.Army.Id).ToArray())).ToArray(),
        sieges.Select(x => new SiegeSnapshot(x.Id, x.Attacker.Id, x.City.Village.Id, x.Attacker.OwnerId, x.DefenderOwner, x.City.Village.Position,
            Math.Max(0, (x.Status == "Active" ? warTime : x.Ended) - x.Started), x.Wall, x.Gate, x.Morale, x.Loyalty, x.Status, x.Attackers.Where(a=>a.Order is "Sieging" or "Battle").Select(a=>a.Id).ToArray(), x.Objective, x.Phase, x.City.Village.Buildings.Sum(b=>b.Level), x.Progress)).ToArray(),
        new(castle.Id, castle.Position, castle.Active, castle.Controller, castle.Claimant, castle.Progress, castle.Contested), Rankings());
    private bool WarBotAct(Guid actor, IGameCommands commands, BotPersonality personality, BotDifficulty difficulty = BotDifficulty.Normal)
    {
        var home = Primary(actor); if (home is null) return true;
        var ownArmies = armies.Where(x => x.OwnerId == actor).ToArray();

        // 1. Emergency homeland and oasis defense
        if (warTime >= protectionUntil)
        {
            var activeSiege = sieges.FirstOrDefault(s => s.Status == "Active" && s.City.Village.OwnerId == actor);
            var approachingThreat = armies.FirstOrDefault(a => a.OwnerId != actor && !CampAllied(actor, a.OwnerId) && !a.Routing && a.Order == "Approaching"
                && villages.TryGetValue(a.TargetId ?? Guid.Empty, out var c) && c.Village.OwnerId == actor);
            var invader = activeSiege?.Attacker ?? approachingThreat;
            if (invader is not null && !Engaged(invader) && !invader.Routing)
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
                var raider = armies.FirstOrDefault(a => a.OwnerId != actor && !a.Routing && !Engaged(a)
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
        }

        // 2. Central Castle finale contest
        if (castle.Active)
        {
            var champion = ownArmies.Where(a => !Engaged(a) && !a.Routing && a.Order != "Returning")
                .OrderByDescending(Fighters).FirstOrDefault();
            if (champion is not null && Fighters(champion) >= 12)
            {
                if (champion.Order != "Capturing" && Distance(champion.Position, castle.Position) > 35)
                    commands.Execute(actor, new(Guid.NewGuid(), CommandKind.CaptureCastle, champion.Id));
                return true;
            }
            if (ownArmies.Length < 3 && HomeCombatTroops(home) >= 18)
            {
                var castleForce = BuildBotFormation(home, castle.Position, 75, false, false, 4);
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

        // 3. Tactical field interception, convoy raids, and settlement sieges
        var strikeArmy = ownArmies.Where(a => !Engaged(a) && !a.Routing && (a.Order == "Idle"
                || (a.Order == "Guarding" && (Fighters(a) >= (personality == BotPersonality.Aggressive ? 22 : 30)
                    || ownArmies.Count(x => x.DefendingSiteId == a.DefendingSiteId) > 1))))
            .OrderByDescending(Fighters).FirstOrDefault();

        if (strikeArmy is null)
        {
            var maxFieldArmies = difficulty == BotDifficulty.Hard ? 3 : personality == BotPersonality.Aggressive ? 3 : 2;
            if (personality == BotPersonality.Aggressive && ownArmies.Length < maxFieldArmies && HomeCombatTroops(home) >= 34)
            {
                var targetCity = villages.Values.Where(x => x.Village.OwnerId != actor && !CampAllied(actor, x.Village.OwnerId) && !x.Ruined && !x.Besieged)
                    .OrderBy(x => Distance(x.Village.Position, home.Village.Position)).FirstOrDefault();
                if (targetCity is not null)
                {
                    var siegeForce = BuildBotFormation(home, targetCity.Village.Position, 70, true, true, 8);
                    if (siegeForce is not null)
                    {
                        var isContender = villages.Values.Count(v => !v.Ruined && v.Village.OwnerId == targetCity.Village.OwnerId) > 1
                            || armies.Any(a => a.OwnerId == targetCity.Village.OwnerId) || targetCity.Village.Level > 12;
                        var objective = isContender && siegeForce.ContainsKey(UnitId(home, UnitRole.Governor)) ? AttackObjective.Conquest : AttackObjective.Raid;
                        commands.Execute(actor, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
                            siegeForce.Values.Sum(), true, targetCity.Village.Position.X, targetCity.Village.Position.Y,
                            UnitId(home, UnitRole.Infantry), 0, 2, targetCity.Village.Id, Objective: objective, Formation: siegeForce));
                        return true;
                    }
                }
            }
            return ownArmies.Any(a => a.Order is "Attacking" or "Approaching" or "Capturing" or "Battle" or "Sieging");
        }

        var scanRange = personality == BotPersonality.Aggressive ? 240 : personality == BotPersonality.Defensive ? 135 : 100;
        var powerRatio = personality == BotPersonality.Aggressive ? .85 : 1.05;

        // Clear threatening hostile camps near homeland
        var hostileCamp = armies.Where(x => x.OwnerId != actor && !CampAllied(actor, x.OwnerId) && x.CampRadius is not null && !Engaged(x) && !x.Routing
                && Distance(x.Position, home.Village.Position) < 320
                && Fighters(strikeArmy) >= Math.Max(10, Fighters(x) * powerRatio))
            .OrderBy(x => Distance(x.Position, home.Village.Position)).FirstOrDefault();
        if (hostileCamp is not null)
        {
            commands.Execute(actor, new(Guid.NewGuid(), CommandKind.AttackArmy, strikeArmy.Id, DestinationId: hostileCamp.Id));
            return true;
        }

        var enemy = armies.Where(x => x.OwnerId != actor && !CampAllied(actor, x.OwnerId) && !Engaged(x) && !x.Routing && Distance(x.Position, strikeArmy.Position) < scanRange
                && Fighters(strikeArmy) >= Math.Max(10, Fighters(x) * powerRatio))
            .OrderBy(x => Distance(x.Position, strikeArmy.Position)).FirstOrDefault();
        if (enemy is not null)
        {
            commands.Execute(actor, new(Guid.NewGuid(), CommandKind.AttackArmy, strikeArmy.Id, DestinationId: enemy.Id));
            return true;
        }

        if (personality != BotPersonality.Defensive)
        {
            var convoy = convoys.Where(c => c.OwnerId != actor && !CampAllied(actor, c.OwnerId) && !c.InBattle && Distance(c.Position, strikeArmy.Position) < 150
                    && Fighters(strikeArmy) > c.Troops.Where(t => t.Unit is not SupplyWagon).Sum(t => t.Count) + 4)
                .OrderBy(c => Distance(c.Position, strikeArmy.Position)).FirstOrDefault();
            if (convoy is not null)
            {
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.InterceptConvoy, strikeArmy.Id, DestinationId: convoy.Id));
                return true;
            }
        }

        var minSiegeSoldiers = personality == BotPersonality.Aggressive ? 24 : personality == BotPersonality.Defensive ? 36 : 40;
        if (Fighters(strikeArmy) >= minSiegeSoldiers)
        {
            var city = villages.Values.Where(x => x.Village.OwnerId != actor && !CampAllied(actor, x.Village.OwnerId) && !x.Ruined && !x.Besieged)
                .Select(x => (City: x, Defense: x.Garrison().Sum(t => t.Count) + x.BuildingLevel(BuildingType.Wall) * 5 + Guards(x.Village.Id, actor).Sum(Fighters)))
                .Where(x => Fighters(strikeArmy) >= Math.Max(18, x.Defense * (personality == BotPersonality.Aggressive ? .8 : 1.0)))
                .OrderBy(x => Distance(x.City.Village.Position, strikeArmy.Position) + x.Defense * 4)
                .Select(x => x.City).FirstOrDefault();
            if (city is not null)
            {
                var isContender = villages.Values.Count(v => !v.Ruined && v.Village.OwnerId == city.Village.OwnerId) > 1
                    || armies.Any(a => a.OwnerId == city.Village.OwnerId) || city.Village.Level > 12;
                var objective = isContender && RoleCount(strikeArmy, UnitRole.Governor) > 0 && Fighters(strikeArmy) >= 35
                    ? AttackObjective.Conquest : AttackObjective.Raid;
                commands.Execute(actor, new(Guid.NewGuid(), CommandKind.BeginSiege, strikeArmy.Id, DestinationId: city.Village.Id, Objective: objective));
                return true;
            }
        }

        return strikeArmy.Order is "Attacking" or "Approaching" or "Capturing";
    }
}
