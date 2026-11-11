namespace Warzone;

public sealed record OasisSnapshot(Guid Id, string Name, Position Position, ResourceType Resource,
    Guid? OwnerId, Guid? VillageId, int Defenders, Resources? Stock, long Capacity, Resources ProductionPerMinute,
    bool? AutoTransport, string? TransportStatus, Guid? ConvoyId, int WallLevel, double WallHealth, double WallMaximum, Resources? WallUpgradeCost, bool UnderAttack,
    bool Blockaded, Guid? SiegeReportId, double? WheatUpkeepPerMinute);

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
    public static ResourceOasis[] Generate(Position[] settlements, int seed)
    {
        var random = new Random(seed ^ 0x4f415349);
        var result = new List<ResourceOasis>();
        var types = Enum.GetValues<ResourceType>(); random.Shuffle(types);
        var count = Math.Clamp(32 - settlements.Length, 16, 24);
        for (var attempt = 0; result.Count < count && attempt < 60000; attempt++)
        {
            var point = new Position(random.Next(120, 2881), random.Next(120, 2881));
            var py = point.Y * .65;
            var river = 1470 + Math.Sin(py / 320) * 150 + Math.Sin(py / 675) * 60;
            if (Math.Abs(point.X - river) < 85 || LogisticsWorld.Distance(point, new(LogisticsWorld.MapCenter, LogisticsWorld.MapCenter)) < 190
                || settlements.Any(p => LogisticsWorld.Distance(p, point) < 380
                    || double.Hypot(p.X - point.X, (p.Y - point.Y) * .65) < 275)
                || result.Any(o => LogisticsWorld.Distance(o.Position, point) < 330
                    || double.Hypot(o.Position.X - point.X, (o.Position.Y - point.Y) * .65) < 235)) continue;
            result.Add(new(point, types[result.Count % types.Length]));
        }
        return result.ToArray();
    }
}

public sealed partial class LogisticsWorld
{
    private bool OasisUnderAttack(ResourceOasis oasis) => armies.Any(a => a.OwnerId != oasis.OwnerId &&
        (a.Order is "Occupying" or "Blockading" && a.TargetId == oasis.Id || a.Order == "Battle" && Distance(a.Position, oasis.Position) < 20));
    private OasisSnapshot OasisView(ResourceOasis oasis, Guid viewer)
    {
        var mine = oasis.OwnerId == viewer;
        var convoy = convoys.Find(c => c.OasisId == oasis.Id);
        var blockaded = OasisBlockaded(oasis);
        var status = blockaded ? "Oasis blockaded: collection suspended" : convoy is not null ? convoy.InBattle ? "Caravan under attack" : convoy.Returning ? "Resources returning" : "Wagons collecting"
            : !oasis.AutoTransport ? "Automatic transport paused" : oasis.Stock.Total < 2000 ? "Waiting for 2000 resources" : "Waiting for available wagons and provisions";
        return new(oasis.Id, oasis.Name, oasis.Position, oasis.Resource, oasis.OwnerId, oasis.VillageId,
            armies.Where(a => a.DefendingSiteId == oasis.Id && a.Order is "Guarding" or "Battle").Sum(Fighters),
            mine ? oasis.Stock : null, ResourceOasis.StorageCapacity, oasis.ProductionPerMinute,
            mine ? oasis.AutoTransport : null, mine ? status : null, mine ? convoy?.Id : null,
            oasis.WallLevel, oasis.WallHealth, oasis.WallMaximum, mine ? oasis.WallUpgradeCost : null, OasisUnderAttack(oasis),
            blockaded, oasisSieges.Find(s=>s.Oasis==oasis&&s.Status=="Active")?.Id,
            mine ? armies.Where(a=>OasisFoodSource(a)==oasis).Sum(a=>a.Upkeep)*60 : null);
    }
    private void AdvanceOasisProduction(double seconds, double speed)
    {
        foreach (var oasis in oases)
        {
            var rates = Enumerable.Range(0,4).Select(i=>(long)((i==(int)oasis.Resource?240:120)*speed)).ToArray();
            oasis.ProductionPerMinute = new(rates[0],rates[1],rates[2],rates[3]);
            if (oasis.OwnerId is not null)
                for(var i=0;i<4;i++) oasis.Stockpile[i]=Math.Min(ResourceOasis.StorageCapacity,oasis.Stockpile[i]+(decimal)(rates[i]*seconds/60));
        }
    }
    private bool ApplyOasis(Guid actor, GameCommand command, double now)
    {
        if (command.Kind is not (CommandKind.SetOasisTransport or CommandKind.CollectOasis or CommandKind.UpgradeOasisWall)) return false;
        var oasis = oases.Find(o => o.Id == command.TargetId && o.OwnerId == actor)
            ?? throw new GameRuleException("not_owned", "This oasis does not belong to you.");
        var city = EconomyTarget(actor, oasis.VillageId ?? Guid.Empty);
        if (command.Kind == CommandKind.SetOasisTransport) { oasis.AutoTransport = command.Enabled; oasis.NextTransportAt = now; return true; }
        if (command.Kind == CommandKind.CollectOasis) { SendOasisConvoy(oasis, city, now); return true; }
        if (OasisUnderAttack(oasis)) throw new GameRuleException("siege", "An oasis wall cannot be upgraded while under attack.");
        var cost = oasis.WallUpgradeCost ?? throw new GameRuleException("level", "Maximum wall level reached.");
        city.Pay(cost); oasis.WallLevel++; oasis.WallHealth = oasis.WallMaximum;
        Note(actor, now, "Oasis wall upgraded.", "build", targetId: oasis.Id); return true;
    }
    private void WarnOasisAttack(Army army, ResourceOasis oasis, double now)
    {
        if (oasis.OwnerId is { } owner && owner != army.OwnerId)
            Note(owner, now, "Enemy troops are marching on your oasis.", "oasis-attack", targetId: oasis.Id);
    }
    private void AdvanceOasisTransport(double now)
    {
        foreach (var oasis in oases.Where(o => o.OwnerId is not null && o.AutoTransport && o.Stock.Total >= 2000 && !OasisBlockaded(o) && now >= o.NextTransportAt))
        {
            oasis.NextTransportAt = now + 5;
            if (convoys.Any(c => c.OasisId == oasis.Id)) continue;
            if (villages.GetValueOrDefault(oasis.VillageId ?? Guid.Empty) is not { Ruined: false } city) continue;
            try { SendOasisConvoy(oasis, city, now); }
            catch (GameRuleException) { /* Wait for actual transport capacity and provisions. */ }
        }
    }
    private void SendOasisConvoy(ResourceOasis oasis, VillageEconomy city, double now)
    {
        if (city.Besieged) throw new GameRuleException("siege", "A blockaded village cannot send collection wagons.");
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
        if (oases.Find(o => o.Id == id) is { } oasis) return (oasis.Position, oasis.OwnerId);
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
    private IEnumerable<Army> Guards(Guid site, Guid attacker) => armies.Where(a =>
        a.DefendingSiteId == site && a.SupportedOwnerId != attacker && a.OwnerId != attacker && a.Order == "Guarding" && Fighters(a) > 0);
    private void RefreshOasisBonuses()
    {
        foreach (var city in villages.Values) { Array.Clear(city.OasisBonuses); city.OwnedOases = 0; }
        foreach (var oasis in oases)
        {
            if (oasis.VillageId is not { } id || villages.GetValueOrDefault(id) is not { Ruined: false } city || city.Village.OwnerId != oasis.OwnerId)
            { oasis.OwnerId = null; oasis.VillageId = null; oasis.WallLevel = 0; oasis.WallHealth = 0; continue; }
            city.OwnedOases++;
            if (OasisBlockaded(oasis)) continue;
            for (var i = 0; i < 4; i++) city.OasisBonuses[i] += i == (int)oasis.Resource ? .5 : .25;
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
            if (citySiege is not null && citySiege.Attacker.OwnerId!=army.OwnerId && Distance(army.Position,site.Position)<=65)
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
        }
        if (army.Order is not ("Occupying" or "Blockading") || Distance(army.Position, army.Destination) > 4) return;
        var oasis = oases.Find(o => o.Id == army.TargetId);
        if (oasis is null) { CompleteAttack(army); return; }
        if (oasis.OwnerId is { } owner && owner != army.OwnerId && now < shieldUntil)
        { army.ActionSeconds = 0; return; }
        if(oasis.OwnerId is not null&&oasis.OwnerId!=army.OwnerId) BeginOasisSiege(army,oasis,now);
        if(army.OasisMode==OasisMode.Blockade){if(oasis.OwnerId is null||oasis.OwnerId==army.OwnerId)CompleteAttack(army);else army.Order="Blockading";return;}
        if (oasis.OwnerId != army.OwnerId && oasis.WallHealth > 0)
        {
            var damage = Fighters(army) * .1 + RoleCount(army, UnitRole.Ram) * 2.4 + RoleCount(army, UnitRole.Catapult) + RoleCount(army, UnitRole.SiegeTower) * .4;
            oasis.WallHealth = Math.Max(0, oasis.WallHealth - damage * seconds); army.ActionSeconds = 0;
            if (oasis.WallHealth == 0 && oasis.OwnerId is { } defender)
                Note(defender, now, "Your oasis wall has been breached.", "oasis-attack", targetId: oasis.Id);
            return;
        }
        var ongoingBattle=battles.Find(b=>b.Status=="Active"&&b.Members.Any(m=>m.Army.DefendingSiteId==oasis.Id));
        if(ongoingBattle is not null){var defender=ongoingBattle.Members.First(m=>m.Army.DefendingSiteId==oasis.Id).Army;StartBattle(army,defender,now);return;}
        var guard = Guards(oasis.Id, army.OwnerId).FirstOrDefault();
        if (guard is not null) { army.ActionSeconds = 0; StartBattle(army, guard, now); return; }
        if (armies.Any(a => a.DefendingSiteId == oasis.Id && a.OwnerId != army.OwnerId && a.Order == "Battle")) return;
        army.ActionSeconds += seconds;
        if (army.ActionSeconds < 15) return;
        var formerOwner = oasis.OwnerId;
        if (formerOwner != army.OwnerId) { oasis.WallLevel = 0; oasis.WallHealth = 0; oasis.AutoTransport = true; }
        oasis.OwnerId = army.OwnerId; oasis.VillageId = army.HomeVillageId;
        var parentReport=oasisSieges.Find(s=>s.Oasis==oasis&&s.Status=="Active")?.Id;
        foreach(var siege in oasisSieges.Where(s=>s.Oasis==oasis&&s.Status=="Active").ToArray()) EndOasisSiege(siege,"Captured",now,army.OwnerId);
        RefreshOasisBonuses();
        var report = BeginReport(Guid.NewGuid(), "Occupation", now, army, formerOwner, null, oasis.Position);
        report.ParentReportId=parentReport;
        ReportEvent(report.Id, now, $"{oasis.Name} captured. Home production gains +50% {oasis.Resource} and +25% other resources. No resources were looted.", "Victory", army.OwnerId);
        Note(army.OwnerId, now, $"{oasis.Name} now supports its occupying village.", "war");
        if (formerOwner is { } former && former != army.OwnerId) Note(former, now, "Your oasis was captured. Its production bonus has ended.", "oasis-loss", targetId: oasis.Id);
        army.ActionSeconds = 0;
        if (army.AfterAttack == AfterAttack.Return) CompleteAttack(army);
        else { army.Order = "Guarding"; army.DefendingSiteId = oasis.Id; army.SupportedOwnerId = army.OwnerId; }
        foreach(var support in armies.Where(a=>a!=army&&a.OwnerId==army.OwnerId&&a.TargetId==oasis.Id&&a.Order=="Occupying"&&Distance(a.Position,oasis.Position)<=20))
        {
            if(support.AfterAttack==AfterAttack.Return)CompleteAttack(support);
            else{support.Order="Guarding";support.DefendingSiteId=oasis.Id;support.SupportedOwnerId=army.OwnerId;support.ActionSeconds=0;}
        }
    }
}

