namespace Warzone;

public sealed record OasisSnapshot(Guid Id, string Name, Position Position, ResourceType Resource,
    Guid? OwnerId, Guid? VillageId, int Defenders, Resources? Stock, long Capacity, Resources ProductionPerMinute,
    bool? AutoTransport, string? TransportStatus, Guid? ConvoyId, int WallLevel, double WallHealth, double WallMaximum, Resources? WallUpgradeCost, bool UnderAttack,
    bool Blockaded, Guid? SiegeReportId, double? WheatUpkeepPerMinute, IReadOnlyDictionary<string, int>? Garrison = null,
    int Level = 0, Resources? LevelUpgradeCost = null, double BurnedRemainingSeconds = 0, double BurningProgress = 0);

internal sealed class ResourceOasis(Position position, ResourceType resource)
{
    public Guid Id { get; } = Guid.NewGuid();
    public Position Position { get; } = position;
    public ResourceType Resource { get; } = resource;
    public string Name => $"{Resource} oasis";
    public Guid? OwnerId { get; set; }
    public Guid? VillageId { get; set; }
    public decimal[] Stockpile { get; } = new decimal[4];
    public Resources Stock => new((long)Stockpile[0], (long)Stockpile[1], (long)Stockpile[2], (long)Stockpile[3]);
    public const long StorageCapacity = 15000;
    public bool AutoTransport { get; set; } = true;
    public double NextTransportAt { get; set; }
    public Resources ProductionPerMinute { get; set; } = new(resource==ResourceType.Wood?240:120,resource==ResourceType.Iron?240:120,resource==ResourceType.Stone?240:120,resource==ResourceType.Wheat?240:120);
    public int WallLevel { get; set; }
    public double WallHealth { get; set; }
    public double WallMaximum => WallLevel * 150;
    public Resources? WallUpgradeCost => WallLevel >= 20 ? null : new((long)(120 * Math.Pow(WallLevel + 1, 1.6)), (long)(60 * Math.Pow(WallLevel + 1, 1.6)), (long)(160 * Math.Pow(WallLevel + 1, 1.6)), 0);
    public int Level { get; set; }
    public double BurnedUntil { get; set; }
    public double BurningSeconds { get; set; }
    public Resources? LevelUpgradeCost => Level >= 5 ? null : new(
        (long)(350 * Math.Pow(Level + 1, 1.5)),
        (long)(350 * Math.Pow(Level + 1, 1.5)),
        (long)(350 * Math.Pow(Level + 1, 1.5)),
        (long)(250 * Math.Pow(Level + 1, 1.5)));
    public static ResourceOasis[] Generate(Position[] settlements, int seed)
    {
        var random = new Random(seed ^ 0x4f415349);
        var result = new List<ResourceOasis>();
        var types = Enum.GetValues<ResourceType>(); random.Shuffle(types);
        var count = Math.Clamp(32 - settlements.Length, 16, 24);
        var center = new Position(LogisticsWorld.MapCenter, LogisticsWorld.MapCenter);
        var ringRadius = settlements.Length > 0
            ? settlements.Average(s => LogisticsWorld.Distance(center, s))
            : LogisticsWorld.SettlementRadius;

        bool IsValid(Position point)
        {
            var py = point.Y * .65;
            var river = 2205 + Math.Sin(py / 480) * 225 + Math.Sin(py / 1012) * 90;
            if (Math.Abs(point.X - river) < 125 || LogisticsWorld.Distance(point, center) < 285) return false;
            if (settlements.Any(p => LogisticsWorld.Distance(p, point) < 580
                || double.Hypot(p.X - point.X, (p.Y - point.Y) * .65) < 420)) return false;
            if (result.Any(o => LogisticsWorld.Distance(o.Position, point) < 500
                || double.Hypot(o.Position.X - point.X, (o.Position.Y - point.Y) * .65) < 360)) return false;
            return true;
        }

        // Place outer oases in an ordered perimeter frame behind players' villages,
        // place remaining oases randomly in the center, and avoid same-type neighbors.
        var outerTarget = count >= 20 ? 16 : count / 2 + 2;
        Position PerimeterPoint(double angle, double radiusScale)
        {
            var c = Math.Cos(angle);
            var s = Math.Sin(angle);
            var boxR = 2040.0 / Math.Max(Math.Abs(c), Math.Abs(s));
            var r = Math.Min(boxR, Math.Max(ringRadius + 180.0, boxR * radiusScale));
            return new(Math.Clamp(center.X + c * r, 190.0, 4310.0), Math.Clamp(center.Y + s * r, 190.0, 4310.0));
        }

        ResourceType PickResource(Position pos, int? preferredSlot = null)
        {
            var nearTypes = result
                .Where(o => LogisticsWorld.Distance(o.Position, pos) < 1100.0)
                .Select(o => o.Resource)
                .ToHashSet();
            var counts = types.ToDictionary(t => t, t => result.Count(o => o.Resource == t));
            var minCount = counts.Values.Min();
            if (preferredSlot is { } slot)
            {
                var preferred = types[slot % 4];
                if (!nearTypes.Contains(preferred) && counts[preferred] <= minCount + 1)
                    return preferred;
            }

            var candidates = types.Where(t => !nearTypes.Contains(t)).ToArray();
            if (candidates.Length == 0 && result.Count > 0)
            {
                var nearestType = result.MinBy(o => LogisticsWorld.Distance(o.Position, pos))!.Resource;
                candidates = types.Where(t => t != nearestType).ToArray();
            }

            var pool = candidates.Length > 0 ? candidates : types;
            return pool.MinBy(t => counts[t]);
        }

        for (var i = 0; i < outerTarget; i++)
        {
            var baseAngle = (i + 0.5) * Math.Tau / outerTarget;
            var placed = false;
            for (var step = 0; step <= 18 && !placed; step++)
            {
                ReadOnlySpan<int> signs = step == 0 ? [0] : [1, -1];
                foreach (var sign in signs)
                {
                    if (placed) break;
                    var angle = baseAngle + step * sign * 0.025;
                    foreach (var rScale in new[] { 1.0, 0.94, 0.88 })
                    {
                        var point = PerimeterPoint(angle, rScale);
                        if (LogisticsWorld.Distance(point, center) < ringRadius + 80 || !IsValid(point)) continue;
                        result.Add(new(point, PickResource(point, result.Count)));
                        placed = true;
                        break;
                    }
                }
            }
        }

        for (var attempt = 0; result.Count < count && attempt < 40000; attempt++)
        {
            var point = new Position(random.Next(180, 4321), random.Next(180, 4321));
            if (LogisticsWorld.Distance(point, center) >= ringRadius - 100 || !IsValid(point)) continue;
            result.Add(new(point, PickResource(point)));
        }

        for (var attempt = 0; result.Count < count && attempt < 20000; attempt++)
        {
            var point = new Position(random.Next(180, 4321), random.Next(180, 4321));
            if (!IsValid(point)) continue;
            result.Add(new(point, PickResource(point)));
        }

        return result.ToArray();
    }
}

public sealed partial class LogisticsWorld
{
    private bool OasisUnderAttack(ResourceOasis oasis) => armies.Any(a => a.OwnerId != oasis.OwnerId &&
        (a.Order is "Occupying" or "Blockading" or "Burning" && a.TargetId == oasis.Id || a.Order == "Battle" && (a.TargetId == oasis.Id || OasisReached(a, oasis))));
    private OasisSnapshot OasisView(ResourceOasis oasis, Guid viewer, bool omniscient = false)
    {
        var mine = oasis.OwnerId == viewer;
        var reveal = mine || omniscient;
        var convoy = convoys.Find(c => c.OasisId == oasis.Id);
        var blockaded = OasisBlockaded(oasis);
        var isBurned = oasis.BurnedUntil > warTime;
        var status = isBurned ? $"Oasis burned for {Math.Ceiling(oasis.BurnedUntil - warTime)}s"
            : blockaded ? "Oasis blockaded: collection suspended" : convoy is not null ? convoy.InBattle ? "Caravan under attack" : convoy.Returning ? "Resources returning" : "Wagons collecting"
            : !oasis.AutoTransport ? "Automatic transport paused" : oasis.Stock.Total < 2000 ? "Waiting for 2000 resources" : "Waiting for available wagons and provisions";
        var guarding = armies.Where(a => a.DefendingSiteId == oasis.Id || (Distance(a.Position, oasis.Position) <= 35 && a.Order is "Guarding" or "Camped" or "Idle" or "Battle" or "Camping" or "Burning")).ToArray();
        var visibleArmies = guarding.Where(a => reveal || a.OwnerId == viewer || a.SupportedOwnerId == viewer || IsScoutedBy(viewer, a.Id)).ToArray();
        var garrison = visibleArmies.SelectMany(a => a.Troops).GroupBy(t => t.Unit.UnitId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Count));
        var burnedRemaining = Math.Max(0, oasis.BurnedUntil - warTime);
        var burningProgress = Math.Clamp(oasis.BurningSeconds / 90.0, 0, 1);
        return new(oasis.Id, oasis.Name, oasis.Position, oasis.Resource, oasis.OwnerId, oasis.VillageId,
            reveal ? guarding.Sum(Fighters) : 0,
            reveal && !isBurned ? oasis.Stock : null, ResourceOasis.StorageCapacity, isBurned ? default : oasis.ProductionPerMinute,
            mine ? oasis.AutoTransport : null, reveal ? status : null, reveal ? convoy?.Id : null,
            oasis.WallLevel, oasis.WallHealth, oasis.WallMaximum, mine && !isBurned ? oasis.WallUpgradeCost : null, OasisUnderAttack(oasis),
            blockaded, oasisSieges.Find(s=>s.Oasis==oasis&&s.Status=="Active")?.Id,
            reveal ? armies.Where(a=>OasisFoodSource(a)==oasis).Sum(a=>a.Upkeep)*60 : null,
            reveal && garrison.Count > 0 ? garrison : null,
            oasis.Level,
            mine && !isBurned ? oasis.LevelUpgradeCost : null,
            burnedRemaining,
            burningProgress);
    }
    private void AdvanceOasisProduction(double seconds, double speed)
    {
        foreach (var oasis in oases)
        {
            if (oasis.BurnedUntil > warTime)
            {
                oasis.ProductionPerMinute = default;
                continue;
            }
            var levelMultiplier = 1.0 + (oasis.Level * 0.05);
            var rates = Enumerable.Range(0,4).Select(i=>(long)((i==(int)oasis.Resource?240:120)*speed*levelMultiplier)).ToArray();
            oasis.ProductionPerMinute = new(rates[0],rates[1],rates[2],rates[3]);
            if (oasis.OwnerId is not null)
                for(var i=0;i<4;i++) oasis.Stockpile[i]=Math.Min(ResourceOasis.StorageCapacity,oasis.Stockpile[i]+(decimal)(rates[i]*seconds/60));
        }
    }
    private bool ApplyOasis(Guid actor, GameCommand command, double now)
    {
        if (command.Kind is not (CommandKind.SetOasisTransport or CommandKind.CollectOasis or CommandKind.UpgradeOasisWall or CommandKind.UpgradeOasisLevel)) return false;
        var oasis = oases.Find(o => o.Id == command.TargetId && o.OwnerId == actor)
            ?? throw new GameRuleException("not_owned", "This oasis does not belong to you.");
        var city = EconomyTarget(actor, oasis.VillageId ?? Guid.Empty);
        if (oasis.BurnedUntil > now) throw new GameRuleException("burned", "A burned oasis cannot be managed.");
        if (command.Kind == CommandKind.SetOasisTransport) { oasis.AutoTransport = command.Enabled; oasis.NextTransportAt = now; return true; }
        if (command.Kind == CommandKind.CollectOasis) { SendOasisConvoy(oasis, city, now); return true; }
        if (OasisUnderAttack(oasis)) throw new GameRuleException("siege", "An oasis cannot be upgraded while under attack.");
        if (command.Kind == CommandKind.UpgradeOasisWall)
        {
            var cost = oasis.WallUpgradeCost ?? throw new GameRuleException("level", "Maximum wall level reached.");
            city.Pay(cost); oasis.WallLevel++; oasis.WallHealth = oasis.WallMaximum;
            Note(actor, now, "Oasis wall upgraded.", "build", targetId: oasis.Id); return true;
        }
        if (command.Kind == CommandKind.UpgradeOasisLevel)
        {
            var cost = oasis.LevelUpgradeCost ?? throw new GameRuleException("level", "Maximum oasis level reached.");
            city.Pay(cost); oasis.Level++;
            RefreshOasisBonuses();
            Note(actor, now, $"{oasis.Name} upgraded to Level {oasis.Level}.", "build", targetId: oasis.Id); return true;
        }
        return false;
    }
    private void WarnOasisAttack(Army army, ResourceOasis oasis, double now)
    {
        if (oasis.OwnerId is { } owner && owner != army.OwnerId)
            Note(owner, now, "Enemy troops are marching on your oasis.", "oasis-attack", targetId: oasis.Id);
    }
    private void AdvanceOasisTransport(double now)
    {
        foreach (var oasis in oases.Where(o => o.OwnerId is not null && o.BurnedUntil <= now && o.AutoTransport && o.Stock.Total >= 2000 && !OasisBlockaded(o) && now >= o.NextTransportAt))
        {
            oasis.NextTransportAt = now + 5;
            if (convoys.Any(c => c.OasisId == oasis.Id)) continue;
            if (villages.GetValueOrDefault(oasis.VillageId ?? Guid.Empty) is not { Ruined: false, Relocating: false } city) continue;
            try { SendOasisConvoy(oasis, city, now); }
            catch (GameRuleException) { /* Wait for actual transport capacity and provisions. */ }
        }
    }
    private void SendOasisConvoy(ResourceOasis oasis, VillageEconomy city, double now)
    {
        if (oasis.BurnedUntil > now) throw new GameRuleException("burned", "A burned oasis cannot send collection wagons.");
        if (city.Besieged) throw new GameRuleException("siege", "A blockaded village cannot send collection wagons.");
        if (city.Relocating) throw new GameRuleException("relocating", "A relocating village cannot send collection wagons.");
        if (OasisBlockaded(oasis)) throw new GameRuleException("siege", "Oasis blockaded: collection suspended");
        if (oasis.Stock.Total < 1) throw new GameRuleException("resources", "The oasis has no stored resources yet.");
        if (convoys.Any(c => c.OasisId == oasis.Id)) throw new GameRuleException("convoy", "Collection wagons are already in transit.");
        var requested = TransportWagons(city, oasis.Stock.Total);
        var stacks = requested.Select(w => new TroopStack(BaseUnit.Create(GameCatalog.Units[w.Key]), w.Value)).ToArray();
        var food = RoundTripFood(stacks, city.Village.Position, oasis.Position);
        var troops = city.Depart(requested, food);
        convoys.Add(new(city.Village.OwnerId, city.Village.Position, city.Village.Id, null, troops, food)
            { Kind = ConvoyKind.Oasis, OasisId = oasis.Id });
        oasis.NextTransportAt = now + 5;
    }
    private void AdvanceOasisConvoy(Convoy convoy, double seconds, double now)
    {
        var home = villages[convoy.SourceVillageId];
        var oasis = oases.Find(o => o.Id == convoy.OasisId);
        if (oasis is null || oasis.OwnerId != convoy.OwnerId || oasis.VillageId != convoy.SourceVillageId) convoy.Returning = true;
        if (!convoy.Returning && oasis is not null && OasisBlockaded(oasis)) convoy.Returning = true;
        var destination = convoy.Returning ? ConvoyCityDestination(convoy,home) : oasis!.Position;
        Move(convoy, destination, convoy.Speed * seconds);
        if (Distance(convoy.Position, destination) > 3) return;
        if (convoy.Returning)
        {
            if (home.Besieged) return;
            home.Arrive(convoy.Troops, convoy.WheatCargo); home.ReceiveCargo(convoy.Cargo); convoys.Remove(convoy);
        }
        else
        {
            var capacity = convoy.Troops.Sum(t => (double)t.Count * t.Unit.CarryCapacity) - convoy.WheatCargo;
            var stock = oasis!.Stock;
            var cargo = stock.Total <= capacity ? stock : stock.Fraction(Math.Max(0,capacity)/Math.Max(1,stock.Total));
            oasis.Stockpile[0]-=cargo.Wood; oasis.Stockpile[1]-=cargo.Iron; oasis.Stockpile[2]-=cargo.Stone; oasis.Stockpile[3]-=cargo.Wheat;
            convoy.Cargo = cargo; convoy.Returning = true;
        }
    }
    private (Position Position, Guid? Owner) DefenseSite(Guid id)
    {
        if (villages.GetValueOrDefault(id) is { Ruined: false } city) return (city.Village.Position, city.Village.OwnerId);
        if (oases.Find(o => o.Id == id) is { } oasis)
        {
            if (oasis.BurnedUntil > warTime) throw new GameRuleException("burned", "This oasis is burned and in ruins.");
            return (oasis.Position, oasis.OwnerId);
        }
        throw new GameRuleException("target", "This defense site is no longer available.");
    }
    private void OrderReinforcement(Army army, Guid destination, double now, IReadOnlyDictionary<string, int>? formation = null)
    {
        var site = DefenseSite(destination);
        if (site.Owner is null) throw new GameRuleException("target", "Capture an unclaimed oasis before reinforcing it.");
        if (OrderFighters(army, formation) == 0) throw new GameRuleException("troops", "Fighting troops are required for reinforcement.");
        army = Detach(army, formation, now);
        army.DefendingSiteId = destination; army.SupportedOwnerId = site.Owner; army.CampRadius = null;
        army.TargetId = destination; army.Destination = site.Position; army.Order = "Reinforcing"; army.ActionSeconds = 0;
        Note(site.Owner.Value, now, "Reinforcements are on their way.", "reinforcement", targetId: destination);
    }
    private void MergeOasisGuards(Guid siteId)
    {
        if (oases.FirstOrDefault(o => o.Id == siteId && o.BurnedUntil <= warTime) is not { } oasis) return;
        var groups = armies
            .Where(a => a.DefendingSiteId == siteId && a.Order == "Guarding" && !Engaged(a) && !a.Routing && Count(a) > 0
                && Distance(a.Position, oasis.Position) <= 4)
            .GroupBy(a => a.OwnerId)
            .Where(g => g.Count() > 1)
            .ToArray();
        foreach (var group in groups)
        {
            var ordered = group.OrderByDescending(Count).ToArray();
            var survivor = ordered[0];
            for (var i = 1; i < ordered.Length; i++)
            {
                AbsorbIntoArmy(survivor, ordered[i]);
                armies.Remove(ordered[i]);
            }
        }
    }
    private IEnumerable<Army> Guards(Guid site, Guid attacker) => armies.Where(a =>
        a.DefendingSiteId == site && a.SupportedOwnerId != attacker && a.OwnerId != attacker
        && !CampAllied(a.OwnerId, attacker) && (a.SupportedOwnerId is null || !CampAllied(a.SupportedOwnerId.Value, attacker))
        && a.Order == "Guarding" && Fighters(a) > 0);
    private void RefreshOasisBonuses()
    {
        foreach (var city in villages.Values) { Array.Clear(city.OasisBonuses); city.OwnedOases = 0; }
        foreach (var oasis in oases)
        {
            if (oasis.BurnedUntil > warTime) continue;
            if (oasis.VillageId is not { } id || villages.GetValueOrDefault(id) is not { Ruined: false } city || city.Village.OwnerId != oasis.OwnerId)
            { oasis.OwnerId = null; oasis.VillageId = null; oasis.WallLevel = 0; oasis.WallHealth = 0; continue; }
            city.OwnedOases++;
            if (OasisBlockaded(oasis)) continue;
            var levelBonus = oasis.Level * 0.05;
            for (var i = 0; i < 4; i++) city.OasisBonuses[i] += (i == (int)oasis.Resource ? .5 : .25) + levelBonus;
        }
    }
    private void AdvanceOasisAndReinforcements(Army army, double seconds, double now, double shieldUntil)
    {
        var newlyArrived = false;
        if (army.Order is "Reinforcing" or "Guarding")
        {
            (Position Position, Guid? Owner) site;
            try { site = DefenseSite(army.DefendingSiteId ?? Guid.Empty); }
            catch (GameRuleException) { Route(army); return; }
            if (site.Owner != army.SupportedOwnerId) { Route(army); return; }
            army.Destination = site.Position;
            var citySiege = sieges.Find(s=>s.City.Village.Id==army.DefendingSiteId && s.Status=="Active");
            if (citySiege is not null && citySiege.Attacker.OwnerId != army.OwnerId && !CampAllied(citySiege.Attacker.OwnerId, army.OwnerId)
                && (ArmiesCollide(army, citySiege.Attacker) || Distance(army.Position, site.Position) <= 65))
            {
                // Relief meets the surrounding army outside the gates; it cannot deliver troops through a blockade.
                if (now>=shieldUntil) StartBattle(army,citySiege.Attacker,now);
                return;
            }
            if (Distance(army.Position, site.Position) <= 4 && army.Order == "Reinforcing")
            {
                army.Order = "Guarding";
                newlyArrived = true;
                Note(site.Owner!.Value, now, "Reinforcements have arrived.", "reinforcement", targetId: army.DefendingSiteId);
                var arrival=BeginReport(Guid.NewGuid(),"Reinforcement",now,army,site.Owner,null,site.Position);
                arrival.ParentReportId=oasisSieges.Find(s=>s.Status=="Active"&&s.Oasis.Id==army.DefendingSiteId)?.Id;
                ReportEvent(arrival.Id,now,"Reinforcements have arrived and are defending the site.","Arrived",site.Owner);
            }
            var oasisSiege = oasisSieges.Find(s=>s.Oasis.Id==army.DefendingSiteId&&ValidOasisSiege(s));
            if(newlyArrived&&oasisSiege is not null&&now>=shieldUntil){var besieger=oasisSiege.Attackers.FirstOrDefault(a=>ValidOasisArmy(oasisSiege,a));if(besieger is not null)StartBattle(army,besieger,now);}
            if (army.Order == "Guarding" && army.DefendingSiteId is { } guardSiteId && !Engaged(army)) MergeOasisGuards(guardSiteId);
        }
        if (army.Order is not ("Occupying" or "Blockading" or "Burning")) return;
        var oasis = oases.Find(o => o.Id == army.TargetId);
        if (oasis is null || oasis.BurnedUntil > now) { CompleteAttack(army); return; }
        if (!OasisReached(army, oasis)) return;
        if (oasis.OwnerId is { } allyOwner && allyOwner != army.OwnerId && CampAllied(army.OwnerId, allyOwner))
        {
            army.ActionSeconds = 0;
            if (army.OasisMode == OasisMode.Capture && army.AfterAttack != AfterAttack.Return)
            { army.Order = "Guarding"; army.DefendingSiteId = oasis.Id; army.SupportedOwnerId = allyOwner; MergeOasisGuards(oasis.Id); }
            else CompleteAttack(army);
            return;
        }
        if (oasis.OwnerId is { } owner && owner != army.OwnerId && now < shieldUntil)
        { army.ActionSeconds = 0; return; }
        if(oasis.OwnerId is not null&&oasis.OwnerId!=army.OwnerId) BeginOasisSiege(army,oasis,now);
        if(army.OasisMode==OasisMode.Blockade){if(oasis.OwnerId is null||oasis.OwnerId==army.OwnerId)CompleteAttack(army);else army.Order="Blockading";return;}
        if (oasis.OwnerId != army.OwnerId && oasis.WallHealth > 0)
        {
            var damage = Fighters(army) * .1 + RoleCount(army, UnitRole.Ram) * 2.4 + RoleCount(army, UnitRole.Catapult);
            oasis.WallHealth = Math.Max(0, oasis.WallHealth - damage * seconds); army.ActionSeconds = 0;
            if (oasis.WallHealth == 0 && oasis.OwnerId is { } defender)
                Note(defender, now, "Your oasis wall has been breached.", "oasis-attack", targetId: oasis.Id);
            return;
        }
        bool ActiveOasisDefenderMember(BattleMember m) => !m.Withdrawn && !m.Army.Routing && Count(m.Army) > 0
            && m.Army.DefendingSiteId == oasis.Id && (Distance(m.Army.Position, oasis.Position) <= 45 || OasisReached(m.Army, oasis))
            && m.Army.OwnerId != army.OwnerId && !CampAllied(m.Army.OwnerId, army.OwnerId)
            && (m.Army.SupportedOwnerId is null || (m.Army.SupportedOwnerId != army.OwnerId && !CampAllied(m.Army.SupportedOwnerId.Value, army.OwnerId)));
        var ongoingBattle = battles.Find(b => b.Status == "Active" && b.Members.Any(ActiveOasisDefenderMember));
        if (ongoingBattle is not null) { var defender = ongoingBattle.Members.First(ActiveOasisDefenderMember).Army; StartBattle(army, defender, now); return; }
        var guard = Guards(oasis.Id, army.OwnerId).FirstOrDefault();
        if (guard is not null) { army.ActionSeconds = 0; StartBattle(army, guard, now); return; }
        if (armies.Any(a => a.DefendingSiteId == oasis.Id && a.OwnerId != army.OwnerId && a.Order == "Battle")) return;

        if (army.OasisMode == OasisMode.Burn)
        {
            army.Order = "Burning";
            army.ActionSeconds += seconds;
            oasis.BurningSeconds = army.ActionSeconds;
            if (army.ActionSeconds < 90) return;

            var formerOwner = oasis.OwnerId;
            oasis.BurnedUntil = now + 300;
            oasis.BurningSeconds = 0;
            oasis.Level = 0;
            oasis.WallLevel = 0;
            oasis.WallHealth = 0;
            oasis.OwnerId = null;
            oasis.VillageId = null;
            Array.Clear(oasis.Stockpile);
            foreach (var convoy in convoys.Where(c => c.OasisId == oasis.Id).ToArray())
                convoy.Returning = true;
            foreach (var siege in oasisSieges.Where(s => s.Oasis == oasis && s.Status == "Active").ToArray())
                EndOasisSiege(siege, "Burned", now, army.OwnerId);
            RefreshOasisBonuses();
            var report = BeginReport(Guid.NewGuid(), "OasisBurned", now, army, formerOwner, null, oasis.Position);
            ReportEvent(report.Id, now, $"{oasis.Name} burned to ashes! It is in ruins and cannot be captured or utilized for 5 minutes.", "Victory", army.OwnerId);
            Note(army.OwnerId, now, $"{oasis.Name} burned to ashes for 5 minutes.", "war", targetId: oasis.Id);
            if (formerOwner is { } former && former != army.OwnerId)
                Note(former, now, $"Your {oasis.Name} was burned to ashes! Its production bonus has ended.", "oasis-loss", targetId: oasis.Id);
            army.ActionSeconds = 0;
            CompleteAttack(army);
            return;
        }

        army.ActionSeconds += seconds;
        if (army.ActionSeconds < 15) return;
        var formerOwner2 = oasis.OwnerId;
        var oasisStock = oasis.Stock;
        Resources oasisCaptured = new();
        if (formerOwner2 != army.OwnerId && oasisStock.Total > 0)
        {
            var captors = armies.Where(a => armies.Contains(a) && Fighters(a) > 0 && (a == army
                || (CampAllied(army.OwnerId, a.OwnerId) && a.TargetId == oasis.Id && a.Order == "Occupying" && OasisReached(a, oasis)))).ToArray();
            oasisCaptured = DistributeLoot(captors, oasisStock);
            if (oasisCaptured.Total > 0)
            {
                oasis.Stockpile[0] = Math.Max(0m, oasis.Stockpile[0] - oasisCaptured.Wood);
                oasis.Stockpile[1] = Math.Max(0m, oasis.Stockpile[1] - oasisCaptured.Iron);
                oasis.Stockpile[2] = Math.Max(0m, oasis.Stockpile[2] - oasisCaptured.Stone);
                oasis.Stockpile[3] = Math.Max(0m, oasis.Stockpile[3] - oasisCaptured.Wheat);
            }
        }
        if (formerOwner2 != army.OwnerId) { oasis.WallLevel = 0; oasis.WallHealth = 0; oasis.AutoTransport = true; }
        oasis.OwnerId = army.OwnerId; oasis.VillageId = army.HomeVillageId;
        var parentReport=oasisSieges.Find(s=>s.Oasis==oasis&&s.Status=="Active")?.Id;
        foreach(var siege in oasisSieges.Where(s=>s.Oasis==oasis&&s.Status=="Active").ToArray()) EndOasisSiege(siege,"Captured",now,army.OwnerId);
        RefreshOasisBonuses();
        var report2 = BeginReport(Guid.NewGuid(), "Occupation", now, army, formerOwner2, null, oasis.Position);
        report2.ParentReportId=parentReport;
        if (oasisCaptured.Total > 0) { report2.AttackerLoot += oasisCaptured; report2.DefenderLoss += oasisCaptured; }
        var lootSummary = oasisCaptured.Total > 0 ? $"Looted {ResourceText(oasisCaptured)}." : "No resources were looted.";
        ReportEvent(report2.Id, now, $"{oasis.Name} captured. Home production gains +50% {oasis.Resource} and +25% other resources. {lootSummary}", "Victory", army.OwnerId);
        Note(army.OwnerId, now, $"{oasis.Name} now supports its occupying village.", "war");
        if (formerOwner2 is { } former2 && former2 != army.OwnerId) Note(former2, now, "Your oasis was captured. Its production bonus has ended.", "oasis-loss", targetId: oasis.Id);
        army.ActionSeconds = 0;
        if (army.AfterAttack == AfterAttack.Return) CompleteAttack(army);
        else { army.Order = "Guarding"; army.DefendingSiteId = oasis.Id; army.SupportedOwnerId = army.OwnerId; }
        foreach(var support in armies.Where(a=>a!=army&&(a.OwnerId==army.OwnerId||CampAllied(army.OwnerId,a.OwnerId))&&a.TargetId==oasis.Id&&a.Order=="Occupying"&&OasisReached(a,oasis)))
        {
            if(support.AfterAttack==AfterAttack.Return||support.OasisMode!=OasisMode.Capture)CompleteAttack(support);
            else{support.Order="Guarding";support.DefendingSiteId=oasis.Id;support.SupportedOwnerId=army.OwnerId;support.ActionSeconds=0;}
        }
        MergeOasisGuards(oasis.Id);
    }
}

