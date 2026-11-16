using System.Diagnostics;
using System.Text.Json;
using Warzone;

internal static class WarfareChecks
{
    private static int checks;
    private static void Check(bool condition, string text) { if (!condition) throw new Exception(text); checks++; }
    private static void Reject(Action action, string code)
    {
        try { action(); } catch (GameRuleException e) when (e.Code == code) { checks++; return; }
        throw new Exception($"Expected {code}");
    }
    private sealed class Realm(TribeType tribe = TribeType.Romans, bool thirdCity = false)
    {
        public VillageEconomy A { get; } = new(Guid.NewGuid(), tribe, new(200, 500));
        public VillageEconomy B { get; } = new(Guid.NewGuid(), TribeType.Greeks, new(300, 500));
        public VillageEconomy? C { get; } = thirdCity ? new(Guid.NewGuid(), TribeType.Greeks, new(700, 500)) : null;
        private LogisticsWorld? world;
        public LogisticsWorld World => world ??= new(C is null ? [A, B] : [A, B, C]);
        public double Now;
        public WorldSnapshot View => World.Snapshot(A.Village.OwnerId);
        public void Tick(double seconds, bool economy = true, double duration = 1800)
        {
            for (var i = 0; i < seconds * 4; i++)
            { Now += .25; if (economy) World.AdvanceEconomies(.25, Now, 1); World.Advance(.25, Now, 0, duration); }
        }
        public void Order(VillageEconomy city, CommandKind kind, Guid target, Guid? destination = null, AttackObjective objective = AttackObjective.Raid)
            => World.Apply(city.Village.OwnerId, new(Guid.NewGuid(), kind, target, DestinationId: destination, Objective: objective), Now, 0);
        public Guid Dispatch(VillageEconomy city, Dictionary<string, int> formation, int wagons = 2, bool supply = true)
        {
            var food = formation.Sum(x=>GameCatalog.Units[x.Key].Stats.WheatUpkeep*x.Value)*10+wagons*10;
            while (city.Capacity < food && city.BuildingLevel(BuildingType.Warehouse) < 20) city.Village.Buildings.Single(x=>x.Type==BuildingType.Warehouse).CompleteUpgrade(city.BuildingLevel(BuildingType.Warehouse));
            city.AddResources(new(4000, 4000, 4000, (long)food));
            World.Apply(city.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, city.Village.Id, X: 250, Y: 500,
                Formation: formation, Wagons: wagons, Escorts: 0, Enabled: supply), Now, 0);
            return World.Snapshot(city.Village.OwnerId).Armies.Last(x => x.OwnerId == city.Village.OwnerId).Id;
        }
    }
    private static void Gift(VillageEconomy city, string unit, int amount) => Check(city.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units[unit]), amount)]), "Fixture must respect population cap");
    private static void Upgrade(VillageEconomy city, BuildingType type, int level)
    { var building = city.Village.Buildings.Single(x => x.Type == type); while (building.Level < level) building.CompleteUpgrade(building.Level); }
    private static (Realm Realm, Guid Army) Siege(Realm? existing = null, AttackObjective objective = AttackObjective.Conquest)
    {
        var r = existing ?? new Realm(); Gift(r.A, "romans-infantry", 300); Gift(r.A, "romans-wagon", 6);
        Gift(r.A, "romans-catapult", 3); Gift(r.A, "romans-ram", 3); Gift(r.A, "romans-governor", 1);
        var id = r.Dispatch(r.A, new() { ["romans-infantry"] = 250, ["romans-catapult"] = 3, ["romans-ram"] = 3, ["romans-governor"] = 1 });
        r.Order(r.A, CommandKind.BeginSiege, id, r.B.Village.Id, objective);
        for (var i = 0; i < 800 && !r.View.Warfare.Sieges.Any(x => x.Phase is "Persuading" or "Demolishing" || x.Status=="Raided"); i++) r.Tick(.25);
        Check(r.View.Warfare.Sieges.Any(x => x.Phase is "Persuading" or "Demolishing" || x.Status=="Raided"), "Automatic siege defeats the garrison and starts city objective");
        Check(r.B.Garrison().Length == 0, "Garrison falls completely before persuasion or demolition begins");
        return (r, id);
    }
    public static int Run()
    {
        var production = new Realm();
        var baseOutput = production.A.Snapshot().Production;
        Check(baseOutput.Single(p => p.Resource == "Wood").Percent == 100, "Normal production is 100 percent at every field level");
        Check(baseOutput.Single(p => p.Resource == "Wheat").NetPerMinute == 338, "Net wheat deducts twenty soldiers and two wagons exactly once");
        Check(production.A.Snapshot(2).Production.Single(p => p.Resource == "Wood").Percent == 200, "Realm speed is reflected in production efficiency");
        Gift(production.A, "romans-infantry", 400);
        Check(production.A.Snapshot().Production.Single(p => p.Resource == "Wheat").Percent < 0, "Overgrown garrison reports negative net food production");
        var musterBefore = production.View.Muster;
        var deployed = production.Dispatch(production.A, new() { ["romans-infantry"] = 10 }, 1);
        Check(production.View.Muster.Total == musterBefore.Total && production.View.Muster.Wagons == musterBefore.Wagons, "Muster conserves troops and vehicles across dispatch");
        Check(production.View.Muster.Units.Single(u => u.UnitId == "romans-infantry") is { Home: 410, Deployed: 10 }, "Muster separates home and deployed units by type");
        Check(production.View.Muster.UpkeepPerMinute == 422 && production.View.Muster.FieldUpkeepPerMinute == 11, "Muster includes carried food upkeep without charging it to village production");
        Check(production.World.Snapshot(Guid.NewGuid()).Muster.Total == 0, "Muster never exposes another player's private formation");
        var persianOutput = new VillageEconomy(Guid.NewGuid(), TribeType.Persians, new(1500,1500));
        Check(persianOutput.Snapshot().Production.Single(p => p.Resource == "Wood").Percent == 100, "All tribes start with equal base resource production efficiency");
        var journal = new Realm(); _ = journal.World;
        var field = journal.A.Snapshot().Targets.First(x => x.Kind == "Building");
        journal.A.Apply(new(Guid.NewGuid(), CommandKind.Upgrade, field.Id), 0);
        Check(journal.View.Notices.Any(x => x.Kind == "build" && x.EndsAt is null), "An instant upgrade creates a completed journal entry without a deadline");
        journal.Tick(8);
        Check(journal.View.Notices.Any(x => x.Kind == "build" && x.Text.Contains("reached level 2")), "Completed upgrades remain in the server journal");
        var recruitment = journal.A.Training.First();
        journal.A.Apply(new(Guid.NewGuid(), CommandKind.Train, recruitment.QueueId, Quantity: 2), journal.Now);
        Check(journal.View.Notices.Any(x => x.Kind == "train" && x.EndsAt > journal.Now), "Recruitment has its own journal category and timer");
        var returning = new Realm(); var raidingArmy = returning.Dispatch(returning.A, new() { ["romans-infantry"] = 10 }, 1);
        var outpost = returning.View.Oases.OrderBy(c => LogisticsWorld.Distance(returning.A.Village.Position, c.Position)).First();
        returning.World.Apply(returning.A.Village.OwnerId, new(Guid.NewGuid(), CommandKind.CaptureOasis, raidingArmy, DestinationId: outpost.Id, AfterAttack: AfterAttack.Return), returning.Now, 0);
        Check(returning.View.Armies.Single().AfterAttack == AfterAttack.Return, "Post-attack orders are retained in snapshots for reconnect");
        Reject(() => returning.World.Apply(returning.B.Village.OwnerId, new(Guid.NewGuid(), CommandKind.SetAfterAttack, raidingArmy, AfterAttack: AfterAttack.Hold), 0, 0), "not_owned");
        for (var i = 0; i < 1000 && returning.View.Armies.Any(); i++) returning.Tick(1);
        Check(!returning.View.Armies.Any() && returning.A.Available("romans-infantry") == 20, "Successful raid returns the army home automatically without another command");
        Check(returning.View.Reports.Any(r => r.Kind == "Occupation" && r.AttackerLoot.Total == 0), "Occupation retains its ownership report after the army returns");
        Check(returning.World.Snapshot(returning.B.Village.OwnerId).Reports.Length == 0, "Raid report is private to its owner");
        Check(GameCatalog.Units.Values.Select(GameCatalog.UnitSprite).Distinct().Count() == 50, "Every unit has distinct generated artwork");
        foreach (var definition in GameCatalog.Units.Values) System.Xml.Linq.XDocument.Parse(GameCatalog.UnitSprite(definition));
        Check(!JsonSerializer.Serialize(new Resources(1, 2, 3, 4)).Contains("Total"), "Derived resource total is excluded from the wire contract");
        var r = new Realm(); Gift(r.A, "romans-infantry", 100); Gift(r.B, "greeks-infantry", 100);
        var a = r.Dispatch(r.A, new() { ["romans-infantry"] = 100 }, 1);
        Reject(() => r.World.Apply(r.A.Village.OwnerId, new(Guid.NewGuid(), CommandKind.ConfigureSupply, a, SourceVillageId: r.A.Training[0].QueueId), 0, 0), "not_owned");
        var b = r.Dispatch(r.B, new() { ["greeks-infantry"] = 100 }, 1);
        r.Tick(10);
        Reject(() => r.World.Apply(r.A.Village.OwnerId, new(Guid.NewGuid(), CommandKind.AttackArmy, a, DestinationId: b), r.Now, 300), "protected");
        Reject(() => r.World.Apply(r.B.Village.OwnerId, new(Guid.NewGuid(), CommandKind.RecallArmy, a), r.Now, 0), "not_owned");
        r.Order(r.A, CommandKind.AttackArmy, a, b); r.Tick(1);
        Check(r.View.Warfare.Battles.Single().Status == "Active", "Contact starts a visible battle");
        Reject(() => r.Order(r.A, CommandKind.MoveArmy, a), "engaged");
        for (var i = 0; i < 800 && r.View.Warfare.Battles.Any(x => x.Status == "Active"); i++) r.Tick(.25);
        var battle = r.View.Warfare.Battles.Single();
        Check(battle.Status == "Resolved" && battle.Elapsed >= 5, "Battle resolves naturally by eliminating one side");
        var report = r.View.Reports.Single(x => x.Id == battle.Id);
        Check(report.Status == "Resolved" && report.Events.Length >= 7, "Resolved battle retains its combat timeline");
        Check(report.AttackerUnits.Concat(report.DefenderUnits).All(u => u.Sent >= u.Dead + u.Wounded + u.Fled + u.Destroyed),
            "Reported casualties never exceed committed units");
        Check(report.AttackerLosses.Sum(l => l.Dead) == r.View.Warfare.Rankings.Single(x => x.PlayerId == r.A.Village.OwnerId).Dead, "Per-unit battle deaths equal actual casualty accounting");
        Check(report.AttackerLosses.All(l => l.UnitId.StartsWith("romans-")) && report.DefenderLosses.All(l => l.UnitId.StartsWith("greeks-")), "Losses preserve actual unit types for each side");
        Check(r.World.Snapshot(Guid.NewGuid()).Reports.Length == 0, "Uninvolved players cannot inspect battle reports");
        Check(r.World.Snapshot(r.B.Village.OwnerId).Reports.Single().Id == report.Id, "Both combat participants receive the same retained report");
        foreach (var city in new[] { r.A, r.B })
        {
            var rank = r.View.Warfare.Rankings.Single(x => x.PlayerId == city.Village.OwnerId);
            var humanLoss = rank.Dead + rank.Wounded + rank.Fled;
            var cycles = humanLoss / 10; var rem = humanLoss % 10;
            Check(rank.Wounded == cycles * 6 + Math.Min(6, rem) && rank.Dead == cycles * 3 + Math.Clamp(rem - 6, 0, 3) && rank.Fled == cycles,
                "Casualty stream preserves the 60/30/10 wounded/dead/fled distribution");
            var living = city.Snapshot().Garrison.Where(x => !x.Key.EndsWith("wagon")).Sum(x => x.Value)
                + r.View.Armies.Where(x => x.OwnerId == city.Village.OwnerId).Sum(x => x.Soldiers)
                + r.View.Convoys.Where(x => x.OwnerId == city.Village.OwnerId).Sum(x => x.Escorts) + city.Snapshot().Wounded;
            Check(living + rank.Dead + rank.Fled == 120, "Combat conserves all human troops across field, home and hospital");
        }
        var hospital = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(50, 50));
        Check(GameCatalog.Units.Values.Where(u => u.Role != UnitRole.Wagon).All(u => u.Stats.HitPoints == 100)
            && GameCatalog.Units.Values.Where(u => u.Role == UnitRole.Wagon).All(u => u.Stats.HitPoints == 25),
            "All combat units default to 100 HP while supply wagons keep 25 HP");
        var mix = new Realm(); Gift(mix.A, "germans-infantry", 200); Gift(mix.B, "romans-spearman", 50); Gift(mix.B, "germans-infantry", 50);
        var attackersMix = mix.Dispatch(mix.A, new() { ["germans-infantry"] = 100 }, 1, false);
        var mixedDefenders = mix.Dispatch(mix.B, new() { ["romans-spearman"] = 50, ["germans-infantry"] = 50 }, 1, false);
        mix.Order(mix.A, CommandKind.AttackArmy, attackersMix, mixedDefenders);
        for (var i = 0; i < 120 && (mix.View.Warfare.Battles.SingleOrDefault()?.Round ?? 0) < 3; i++) mix.Tick(.25);
        var mixDefSnap = mix.World.Snapshot(mix.B.Village.OwnerId).Armies.Single(x => x.Id == mixedDefenders).Formation!;
        Check(mixDefSnap.GetValueOrDefault("germans-infantry") < mixDefSnap.GetValueOrDefault("romans-spearman"),
            "Defense mitigation causes low-defense clubswingers to suffer heavier casualties than spearmen");
        var starve = new Realm(); Gift(starve.B, "greeks-infantry", 40);
        var hungryForce = starve.Dispatch(starve.B, new() { ["greeks-infantry"] = 40 }, 1, false);
        for (var i = 0; i < 3200 && (starve.World.Snapshot(starve.B.Village.OwnerId).Armies.FirstOrDefault(x => x.Id == hungryForce)?.Wheat ?? 0) > 0; i++) starve.Tick(.25);
        var preStarveSnap = starve.World.Snapshot(starve.B.Village.OwnerId).Armies.Single(x => x.Id == hungryForce);
        Check((preStarveSnap.Wheat ?? -1) == 0, "Force eventually exhausts its carried wheat");
        var beforeStarveCount = preStarveSnap.Soldiers;
        starve.Tick(22);
        var afterStarve = starve.World.Snapshot(starve.B.Village.OwnerId).Armies.Single(x => x.Id == hungryForce);
        var starveRank = starve.View.Warfare.Rankings.Single(x => x.PlayerId == starve.B.Village.OwnerId);
        Check(beforeStarveCount - afterStarve.Soldiers >= 4 && afterStarve.Wounded >= 4 && starveRank.Dead == 0,
            "Running out of food converts 2 soldiers every 10 seconds to wounded without killing them");
        hospital.AdmitWounded("romans-infantry", 10, 100); hospital.Advance(60, 60, 1);
        Check(hospital.Snapshot().Wounded == 10, "Wounded cannot heal before reaching hospital");
        hospital.Advance(1, 101, 1);
        Check(hospital.Snapshot().Wounded < 10 && hospital.Snapshot().Garrison["romans-infantry"] > 20, "Hospital returns wounded to the garrison after transit");
        var retreat = new Realm(); Gift(retreat.A, "romans-wagon", 2);
        var fleeing = retreat.Dispatch(retreat.A, new() { ["romans-infantry"] = 10 }, 4, false); retreat.Tick(5);
        retreat.Order(retreat.A, CommandKind.RecallArmy, fleeing);
        Check(retreat.View.Armies.Single().Wagons == 4 && retreat.View.Armies.Single().Order == "Returning", "Recall preserves the convoy formation");
        retreat.Tick(20); Check(retreat.A.Available("romans-infantry") == 20 && retreat.A.Available("romans-wagon") == 4, "Retreat credits survivors once");
        var escort = new Realm(); Gift(escort.B, "greeks-infantry", 100);
        var raider = escort.Dispatch(escort.A, new() { ["romans-infantry"] = 10 }, 1, false);
        var supplyTarget = escort.Dispatch(escort.B, new() { ["greeks-infantry"] = 5 }, 1);
        escort.World.Apply(escort.B.Village.OwnerId, new(Guid.NewGuid(), CommandKind.ConfigureSupply, supplyTarget, Wagons: 1, Escorts: 100), 0, 0);
        for (var i = 0; i < 2400 && !escort.View.Convoys.Any(); i++) escort.Tick(.25);
        var guarded = escort.View.Convoys.Single();
        Check(guarded.Escorts == 100 && escort.World.Snapshot(escort.B.Village.OwnerId).Convoys.Single().Wheat > 100, "Supply plans provision both the army and its escorts");
        escort.Order(escort.A, CommandKind.InterceptConvoy, raider, guarded.Id);
        for (var i = 0; i < 300 && !escort.View.Warfare.Battles.Any(x => x.Status == "Resolved"); i++) escort.Tick(.25);
        Check(escort.View.Warfare.Battles.Single().Winner == escort.B.Village.OwnerId, "A well-supplied strong escort repels a smaller interception force");

        var (conquest, conqueror) = Siege(); var former = conquest.B.Village.OwnerId;
        conquest.Tick(5);
        Check(conquest.View.Warfare.Sieges.Single().Loyalty == 95 && conquest.B.Village.OwnerId == former, "Governor conquest reduces loyalty over timed rounds");
        conquest.Tick(95);
        Check(conquest.B.Village.OwnerId == conquest.A.Village.OwnerId && conquest.World.Primary(former) is null, "Conquest transfers ownership and eliminates the last-city defender");
        Check(conquest.View.Armies.Single().Formation!.GetValueOrDefault("romans-governor") == 0, "Governor is consumed exactly once");
        Check(conquest.World.Selected(conquest.A.Village.OwnerId, conquest.B.Village.Id) == conquest.B, "New province can be selected");
        Reject(() => conquest.World.EconomyTarget(former, conquest.B.Village.Id), "not_owned");
        Check(conquest.World.Dominated && conquest.World.Result("Domination").WinnerIds.SequenceEqual([conquest.A.Village.OwnerId]), "Last surviving kingdom wins domination");
        conquest.Tick(1); Check(conquest.B.Resources.Total > 0, "Conquered economy continues running");
        conquest.World.Apply(conquest.A.Village.OwnerId, new(Guid.NewGuid(), CommandKind.ConfigureSupply, conqueror, SourceVillageId: conquest.B.Village.Id, Wagons: 1, Escorts: 0), conquest.Now, 0);
        Check(conquest.View.Armies.Single().SupplyVillageId == conquest.B.Village.Id, "Army supply can be reassigned to a conquered province");
        var provinces = new Realm(thirdCity: true); _ = provinces.World;
        var defendingOwner = provinces.B.Village.OwnerId;
        provinces.C!.ChangeOwner(defendingOwner, null);
        var refugees = provinces.Dispatch(provinces.B, new() { ["greeks-infantry"] = 10 }, 1);
        provinces.B.AdmitWounded("greeks-infantry", 3, 1000);
        var (_, invader) = Siege(existing: provinces);
        provinces.Tick(100);
        var relocatedArmy = provinces.View.Armies.Single(x => x.Id == refugees);
        Check(relocatedArmy.HomeVillageId == provinces.C.Village.Id && relocatedArmy.SupplyVillageId == provinces.C.Village.Id, "Loss of one province rebases its armies and supply to a surviving city");
        Check(provinces.C.Snapshot().Wounded >= 3 && provinces.World.Primary(defendingOwner) == provinces.C, "Wounded evacuate to the remaining hospital without eliminating their owner");
        Check(provinces.B.Snapshot().Wounded == 0, "Conquest does not turn defeated wounded into free enemy recruits");

        var (raze, razer) = Siege(objective: AttackObjective.Raze); raze.Tick(300);
        Check(raze.B.Ruined && raze.B.Village.Level == 0 && raze.B.Resources.Total == 0, "Razing creates an empty non-operating ruin");
        var loot = raze.View.Armies.Single().Loot!.Value.Total; Check(loot > 0, "Destruction plunders remaining resources from the ruined village");
        raze.Tick(10); Check(raze.B.Resources.Total == 0 && raze.View.Armies.Single().Loot!.Value.Total <= loot, "Ruins produce neither resources nor repeat salvage");
        Reject(() => raze.Order(raze.A, CommandKind.BeginSiege, razer, raze.B.Village.Id, AttackObjective.Raze), "target");
        Check(raze.B.Snapshot().Targets.Single(x => x.Name == "MainHall").UpgradeCost is null, "A ruined main hall has no invalid upgrade step");

        var bombard = new Realm(); Upgrade(bombard.B, BuildingType.Wall, 6); Upgrade(bombard.B, BuildingType.Warehouse, 4);
        Gift(bombard.B, "romans-infantry", 200);
        Gift(bombard.A, "romans-infantry", 300); Gift(bombard.A, "romans-catapult", 3); Gift(bombard.A, "romans-ram", 3); Gift(bombard.A, "romans-wagon", 6);
        var battery = bombard.Dispatch(bombard.A, new() { ["romans-infantry"] = 250, ["romans-catapult"] = 3, ["romans-ram"] = 3 });
        bombard.World.Apply(bombard.A.Village.OwnerId, new(Guid.NewGuid(), CommandKind.BeginSiege, battery, DestinationId: bombard.B.Village.Id), 0, 0);
        bombard.Tick(15);
        Check(bombard.B.BuildingLevel(BuildingType.Warehouse) == 4, "Automatic siege attacks fortifications without a building-target command");
        Check(bombard.View.Warfare.Sieges.Single().Gate < 100 && bombard.View.Warfare.Sieges.Single().Wall < 100, "Rams damage the gate and catapults weaken the wall");
        var beforeRetreat = bombard.View.Armies.Single(x => x.Id == battery);
        bombard.Order(bombard.A, CommandKind.RecallArmy, battery); Check(!bombard.B.Besieged, "Retreat releases the blockade immediately");
        var afterRetreat = bombard.View.Armies.Single(x => x.Id == battery);
        Check(beforeRetreat.Soldiers-afterRetreat.Soldiers==Math.Ceiling((beforeRetreat.Soldiers+beforeRetreat.Wagons)*.2), "Combat withdrawal removes exactly twenty percent of the remaining army");
        Check(afterRetreat.Wagons == beforeRetreat.Wagons && afterRetreat.Formation!["romans-catapult"] == beforeRetreat.Formation!["romans-catapult"], "Frontline troops absorb this withdrawal penalty before transport and siege engines");

        var (raid, raiderHome) = Siege(objective: AttackObjective.Raid);
        Check(!raid.B.Ruined && raid.B.Village.OwnerId!=raid.A.Village.OwnerId && !raid.B.Besieged,"Raids leave ownership and buildings intact and end the blockade");
        Check(raid.View.Warfare.Settlements.Single(s => s.Id == raid.B.Village.Id).SmokeSeconds >= 60, "Successful village raid emits black smoke for at least one minute");
        var tie = new Realm(); Check(tie.World.Result("TimeLimit").WinnerIds.Length == 2, "Exact score ties award shared victory");

        var foreign = new Realm(); Gift(foreign.A,"greeks-infantry",20);
        var foreignArmy=foreign.Dispatch(foreign.A,new(){["greeks-infantry"]=20},1,false);
        Check(foreign.View.Armies.Single(a=>a.Id==foreignArmy).Formation!["greeks-infantry"]==20,"Existing foreign garrison soldiers can be dispatched without creation");

        var blockade = new Realm();
        var starvationArmy = blockade.Dispatch(blockade.A, new() { ["romans-infantry"] = 15 }, 1);
        blockade.Order(blockade.A, CommandKind.BeginSiege, starvationArmy, blockade.B.Village.Id, AttackObjective.Blockade);
        blockade.Tick(18, false);
        var starvationOutput = blockade.World.EconomySnapshot(blockade.B.Village.OwnerId, null, 1)!.Production.Single(p => p.Resource == "Wheat");
        Check(starvationOutput.GrossPerMinute == 180, "Blockade halves gross wheat output without an extra hidden drain");
        Check(starvationOutput.NetPerMinute == starvationOutput.GrossPerMinute - starvationOutput.UpkeepPerMinute, "Production breakdown reconciles exactly with siege and garrison costs");

        var (reportRealm, reportArmy) = Siege(objective: AttackObjective.Raid);
        var siegeReport = reportRealm.World.Snapshot(reportRealm.B.Village.OwnerId).Reports.Single(r => r.Kind == "Siege");
        Check(siegeReport.Status == "Raided" && siegeReport.DefenderLoss.Total > 0, "Raid report records exact plunder on completion");
        Check(siegeReport.AttackerLoot.Total <= siegeReport.DefenderLoss.Total, "Captured plunder cannot exceed actual resources removed from the defender");
        reportRealm.Tick(40);
        var archived = reportRealm.World.Snapshot(reportRealm.B.Village.OwnerId).Reports.Single(r => r.Id == siegeReport.Id);
        Check(archived.Status == "Raided" && archived.Events.Last().Text.Contains("Loot"), "Raid consequences remain in reports after markers expire");
        Check(!reportRealm.View.Warfare.Sieges.Any() && archived.DefenderLoss == siegeReport.DefenderLoss, "Report archives survive transient siege cleanup without changing plunder totals");

        var nomad = new Realm(TribeType.Mongols);
        var oasesForNomad = nomad.World.Snapshot(nomad.A.Village.OwnerId).Oases;
        Position FreeNomadDest(Position preferred)
        {
            for (var dy = 0; dy <= 1200; dy += 160)
            {
                var cand = new Position(preferred.X, Math.Clamp(preferred.Y + dy, LogisticsWorld.MapMinimum + 50, LogisticsWorld.MapMaximum - 50));
                if (LogisticsWorld.Distance(cand, nomad.B.Village.Position) >= LogisticsWorld.NomadRelocationVillageSpacing
                    && LogisticsWorld.Distance(cand, new(LogisticsWorld.MapCenter, LogisticsWorld.MapCenter)) >= LogisticsWorld.NomadRelocationCastleSpacing
                    && oasesForNomad.All(o => LogisticsWorld.Distance(cand, o.Position) >= LogisticsWorld.NomadRelocationOasisSpacing))
                    return cand;
            }
            return preferred;
        }
        var nomadDest = FreeNomadDest(new(300, 1200));
        Reject(() => nomad.World.Apply(nomad.A.Village.OwnerId, new(Guid.NewGuid(), CommandKind.RelocateVillage, nomad.A.Village.Id, X: nomadDest.X, Y: nomadDest.Y), 0, 0), "resources");
        Upgrade(nomad.A, BuildingType.Warehouse, 2); nomad.A.AddResources(new(3000, 3000, 3000, 3000));
        var homeStock = nomad.A.Resources;
        Reject(() => nomad.World.Apply(nomad.A.Village.OwnerId, new(Guid.NewGuid(), CommandKind.RelocateVillage, nomad.A.Village.Id, X: 350, Y: 500), 0, 0), "spacing");
        nomad.World.Apply(nomad.A.Village.OwnerId, new(Guid.NewGuid(), CommandKind.RelocateVillage, nomad.A.Village.Id, X: nomadDest.X, Y: nomadDest.Y), 0, 0);
        Check(nomad.A.Relocating && nomad.A.Village.Position == new Position(200, 500) && nomad.A.Resources == homeStock - new Resources(4000, 4000, 4000, 4000),
            "Nomad relocation begins a caravan move instead of teleporting and charges 4000 of each resource");
        Reject(() => nomad.World.Apply(nomad.A.Village.OwnerId, new(Guid.NewGuid(), CommandKind.RelocateVillage, nomad.A.Village.Id, X: 150, Y: 1600), 1, 0), "cooldown");
        Reject(() => nomad.Order(nomad.B, CommandKind.RelocateVillage, nomad.B.Village.Id), "tribe");
        nomad.Tick(10, false);
        Check(Math.Abs(LogisticsWorld.Distance(new(200, 500), nomad.A.Village.Position) - 100) < .1 && nomad.A.Relocating,
            "Nomad caravan advances smoothly at speed 10 map units per second");
        for (var i = 0; i < 1000 && nomad.A.Relocating; i++) nomad.Tick(1, false);
        Check(!nomad.A.Relocating && LogisticsWorld.Distance(nomad.A.Village.Position, nomadDest) < .01,
            "Nomad settlement finishes relocation upon reaching its target coordinates");
        Reject(() => nomad.World.Apply(nomad.A.Village.OwnerId, new(Guid.NewGuid(), CommandKind.RelocateVillage, nomad.A.Village.Id, X: 800, Y: 1800), nomad.Now, 0), "cooldown");
        nomad.Tick(900, false);
        var siegeReloc = new Realm(TribeType.Mongols);
        Upgrade(siegeReloc.A, BuildingType.Warehouse, 2); siegeReloc.A.AddResources(new(3000, 3000, 3000, 3000));
        Gift(siegeReloc.B, "greeks-wagon", 2); siegeReloc.B.AddResources(new(1000, 1000, 1000, 1000));
        siegeReloc.World.Apply(siegeReloc.B.Village.OwnerId, new(Guid.NewGuid(), CommandKind.SendResources, siegeReloc.B.Village.Id, DestinationId: siegeReloc.A.Village.Id, Shipment: new(100, 0, 0, 0)), 0, 0);
        var dest2 = FreeNomadDest(new(300, 1400));
        siegeReloc.World.Apply(siegeReloc.A.Village.OwnerId, new(Guid.NewGuid(), CommandKind.RelocateVillage, siegeReloc.A.Village.Id, X: dest2.X, Y: dest2.Y), 0, 0);
        Reject(() => siegeReloc.World.Apply(siegeReloc.B.Village.OwnerId, new(Guid.NewGuid(), CommandKind.SendResources, siegeReloc.B.Village.Id, DestinationId: siegeReloc.A.Village.Id, Shipment: new(50, 0, 0, 0)), 1, 0), "relocating");
        var beforeWoodShip = siegeReloc.A.Resources.Wood;
        for (var i = 0; i < 60 && siegeReloc.World.Snapshot(siegeReloc.B.Village.OwnerId).Convoys.Any(); i++) siegeReloc.Tick(1, false);
        Check(!siegeReloc.World.Snapshot(siegeReloc.B.Village.OwnerId).Convoys.Any() && siegeReloc.A.Resources.Wood >= beforeWoodShip + 100,
            "In-flight convoys track a relocating nomad village and deliver without glitches");
        Gift(siegeReloc.B, "greeks-infantry", 80);
        var chaseArmy = siegeReloc.Dispatch(siegeReloc.B, new() { ["greeks-infantry"] = 80 }, 1);
        siegeReloc.Order(siegeReloc.B, CommandKind.BeginSiege, chaseArmy, siegeReloc.A.Village.Id, AttackObjective.Blockade);
        for (var i = 0; i < 200 && !siegeReloc.A.Besieged; i++) siegeReloc.Tick(1, false);
        Check(siegeReloc.A.Besieged && siegeReloc.A.Relocating, "Pursuing army catches and besieges the slow relocation caravan");
        var frozenPos = siegeReloc.A.Village.Position;
        siegeReloc.Tick(10, false);
        Check(siegeReloc.A.Village.Position == frozenPos, "Besieged settlement stops relocating until the siege is lifted");

        var castle = new Realm(); castle.A.Village.Relocate(new(1950, 2250)); castle.B.Village.Relocate(new(2050, 2250)); castle.Now = 1799; castle.Tick(.75, false);
        Check(!castle.View.Warfare.Castle.Active, "Keep stays closed until 30 minutes");
        castle.Tick(.25, false); Check(castle.View.Warfare.Castle.Active, "Keep opens at 30 minutes");
        Gift(castle.A, "romans-wagon", 3); var claim = castle.Dispatch(castle.A, new() { ["romans-infantry"] = 10 }, 1);
        castle.Order(castle.A, CommandKind.CaptureCastle, claim); castle.Tick(95);
        Check(castle.View.Warfare.Castle.ControllerId == castle.A.Village.OwnerId && castle.World.CrownWinner is null, "Uncontested supplied army captures the central castle and starts the 5-minute hold timer");
        Check(castle.View.Warfare.Rankings.Single(x => x.PlayerId == castle.A.Village.OwnerId).CastleSeconds > 0, "Castle occupation accumulates score");
        var challenger = castle.Dispatch(castle.B, new() { ["greeks-infantry"] = 10 }, 1);
        castle.Order(castle.B, CommandKind.CaptureCastle, challenger); castle.Tick(32);
        Check(castle.View.Warfare.Castle.Contested && castle.View.Warfare.Battles.Any(x => x.Status == "Active"), "Rival armies automatically contest the keep and engage");
        var holdWin = new Realm(); holdWin.A.Village.Relocate(new(2100, 2250)); holdWin.Now = 1800; holdWin.Tick(.25, false);
        Gift(holdWin.A, "romans-wagon", 4); var crownArmy = holdWin.Dispatch(holdWin.A, new() { ["romans-infantry"] = 15 }, 2);
        holdWin.Order(holdWin.A, CommandKind.CaptureCastle, crownArmy); holdWin.Tick(360);
        Check(holdWin.World.CrownWinner == holdWin.A.Village.OwnerId && holdWin.World.Result("CrownKeep").WinnerIds.SequenceEqual([holdWin.A.Village.OwnerId]), "Holding Crown Keep continuously for 5 minutes wins the match");

        var wall = new Realm(); Upgrade(wall.B, BuildingType.Wall, 6);
        Gift(wall.A, "romans-infantry", 150); Gift(wall.A, "romans-wagon", 5);
        var besieger = wall.Dispatch(wall.A, new() { ["romans-infantry"] = 120 }, 2);
        wall.Order(wall.A, CommandKind.BeginSiege, besieger, wall.B.Village.Id);
        var incoming = wall.World.Snapshot(wall.B.Village.OwnerId).Threats.Single();
        Check(incoming.TargetId == wall.B.Village.Id && incoming.ArrivesAt > wall.Now && wall.View.Threats.Length == 0, "Only the threatened player receives a server-calculated arrival estimate");
        wall.Tick(14);
        var blockedOutput = wall.B.Snapshot().Production.Single(p => p.Resource == "Wheat");
        Check(blockedOutput.Blockaded && blockedOutput.GrossPerMinute == 180, "Active blockade reports the actual fifty percent wheat production penalty");
        Check(wall.World.Snapshot(wall.B.Village.OwnerId).Reports.Any(r => r.Kind == "Siege" && r.Status == "Active"), "Defender receives a live siege report as the blockade starts");
        Check(wall.World.Snapshot(wall.B.Village.OwnerId).Threats.Length == 0, "Arrival alerts disappear when the siege starts");
        Check(wall.B.Besieged, "Besieging force establishes a blockade");
        var relief = wall.Dispatch(wall.B, new() { ["greeks-infantry"] = 10 }, 1);
        wall.Order(wall.B, CommandKind.AttackArmy, relief, besieger); wall.Tick(10);
        Check(wall.B.Besieged && wall.View.Warfare.Battles.Any(x => x.Status == "Active"), "Relief army engages the besieger; blockade persists until the besieger is defeated");

        foreach (var count in new[] { 8, 16 })
        {
            var host = Guid.NewGuid(); var room = new GameRoom(host, "Host", TribeType.Romans, "Simulation", new(Capacity: count, DurationMinutes: 30, ShieldMinutes: 0, ResourceSpeed: 3, BotCount: count - 1), DateTimeOffset.UtcNow);
            room.SetReady(host, true); room.Start(host, _ => true); var timer = Stopwatch.StartNew();
            for (var tick = 0; tick < 7200; tick++) room.Advance(.25);
            var final = room.Snapshot(host, _ => true);
            Check(final.Status == RoomStatus.Finished && final.Result is { WinnerIds.Length: > 0 }, $"{count}-player complete match produces winners");
            Check(final.Result!.Rankings.Length == count && final.Result.Rankings.All(x => x.Score >= 0), "Every kingdom has valid final statistics");
            var frozen = JsonSerializer.Serialize(final.Result); room.Advance(1);
            Check(JsonSerializer.Serialize(room.Snapshot(host, _ => true).Result) == frozen, "Finished results are immutable");
            Check(!room.Execute(host, new(Guid.NewGuid(), CommandKind.CaptureCastle, Guid.NewGuid())).Accepted, "Commands cannot alter a finished match");
            Console.WriteLine($"Simulated {count} players / 30 minutes in {timer.ElapsedMilliseconds} ms; snapshot {JsonSerializer.Serialize(final).Length} bytes.");
        }
        return checks;
    }
}

