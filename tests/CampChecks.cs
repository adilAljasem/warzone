using Warzone;

internal static class CampChecks
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        void Reject(Action action, string code)
        {
            try { action(); }
            catch (GameRuleException error) when (error.Code == code) { checks++; return; }
            throw new Exception($"Expected rule rejection: {code}");
        }
        VillageEconomy City(double x, double y = 500) => new(Guid.NewGuid(), TribeType.Romans, new(x, y));
        void Gift(VillageEconomy city, string role, int count) => city.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units[$"romans-{role}"]), count)]);
        Guid Send(LogisticsWorld world, VillageEconomy city, string role, int count, double x, double y, bool camp = false)
        {
            Gift(city, role, count); city.AddResources(new(2000, 2000, 2000, 2000));
            world.Apply(city.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, city.Village.Id,
                Formation: new Dictionary<string, int> { [$"romans-{role}"] = count }, Wagons: 0, Enabled: false, X: x, Y: y, Camp: camp), 0, 0);
            return world.Snapshot(city.Village.OwnerId).Armies.Last(a => a.OwnerId == city.Village.OwnerId).Id;
        }

        var campCity = City(500); var enemyCity = City(700); var secondEnemyCity = City(700); var allyCity = City(700);
        var caravanCity = City(700); var recipientCity = City(400);
        var world = new LogisticsWorld([campCity, enemyCity, secondEnemyCity, allyCity, caravanCity, recipientCity], 731);
        var now = 0d;
        void Tick() { now += .25; world.Advance(.25, now, 0, 3600); }
        void Until(Func<bool> condition, int limit = 1200)
        {
            for (var i = 0; i < limit && !condition(); i++) Tick();
            Check(condition(), "The expected camp transition completed");
        }
        var camp = Send(world, campCity, "infantry", 120, 500, 500, true);
        var enemy = Send(world, enemyCity, "infantry", 5, 700, 500);
        var secondEnemy = Send(world, secondEnemyCity, "infantry", 5, 700, 500);
        var ally = Send(world, allyCity, "infantry", 20, 700, 500);
        world.SetAlliances([new(campCity.Village.OwnerId, allyCity.Village.OwnerId, 0)]);
        Tick();
        var initial = world.Snapshot(campCity.Village.OwnerId);
        Check(initial.Armies.Single(a => a.Id == camp).Order == "Camped" && initial.Armies.Single(a => a.Id == camp).CampRadius == LogisticsWorld.CampRadius,
            "An arriving army establishes a camp with its public radius");
        Check(world.Snapshot(enemyCity.Village.OwnerId).Armies.Single(a => a.Id == camp).CampRadius == LogisticsWorld.CampRadius,
            "Enemy players receive the same camp perimeter");
        Check(initial.Warfare.Battles.Length == 0, "Armies outside the perimeter and allies do not trigger combat");
        world.Apply(enemyCity.Village.OwnerId, new(Guid.NewGuid(), CommandKind.MoveArmy, enemy, X: 560, Y: 500), now, 0);
        Until(() => world.Snapshot(campCity.Village.OwnerId).Warfare.Battles.Any(b => b.Status == "Active"));
        var battleId = world.Snapshot(campCity.Village.OwnerId).Warfare.Battles.Single(b => b.Status == "Active").Id;
        Check(world.Snapshot(allyCity.Village.OwnerId).Armies.Single(a => a.Id == ally).Order == "Idle",
            "An allied army inside the perimeter remains unharmed");
        world.Apply(secondEnemyCity.Village.OwnerId, new(Guid.NewGuid(), CommandKind.MoveArmy, secondEnemy, X: 550, Y: 500), now, 0);
        Until(() => world.Snapshot(campCity.Village.OwnerId).Warfare.Battles.Single(b => b.Id == battleId).DefenderArmyIds.Contains(secondEnemy));
        Check(true, "A hostile army entering later joins the active camp battle");
        for (var i = 0; i < 2; i++) world.Apply(caravanCity.Village.OwnerId, new(Guid.NewGuid(), CommandKind.SendResources,
            caravanCity.Village.Id, DestinationId: recipientCity.Village.Id, Shipment: new(50, 0, 0, 0)), now, 0);
        Until(() => world.Snapshot(caravanCity.Village.OwnerId).Convoys.Count(c => c.InBattle) == 2);
        var joined = world.Snapshot(campCity.Village.OwnerId);
        Check(joined.Warfare.Battles.Single(b => b.Id == battleId).DefenderArmyIds.Length == 4,
            "Two late caravans join the existing battle rather than waiting outside");
        Check(joined.Reports.Single(r => r.Id == battleId).Armies.Count(a => a.OwnerId == caravanCity.Village.OwnerId && !a.Attacking) == 2,
            "Each joining caravan appears as a participant in the shared report");
        Check(joined.Armies.Single(a => a.Id == ally).Order == "Idle", "Allies remain outside the engagement after caravans join");
        Until(() => world.Snapshot(campCity.Village.OwnerId).Warfare.Battles.FirstOrDefault(b => b.Id == battleId)?.Status != "Active");
        Check(world.Snapshot(campCity.Village.OwnerId).Armies.Single(a => a.Id == camp).Order == "Camped"
            && world.Snapshot(campCity.Village.OwnerId).Armies.Single(a => a.Id == camp).CampRadius == LogisticsWorld.CampRadius,
            "A victorious camp remains pitched after the engagement");

        var quietCity = City(500); var sender = City(700); var receiver = City(300);
        var quiet = new LogisticsWorld([quietCity, sender, receiver], 731);
        var quietTime = 0d;
        bool QuietUntil(Func<bool> condition) { for (var i = 0; i < 4000 && !condition(); i++) { quietTime += .25; quiet.Advance(.25, quietTime, 0, 3600); } return condition(); }
        quiet.Apply(sender.Village.OwnerId, new(Guid.NewGuid(), CommandKind.SendResources, sender.Village.Id,
            DestinationId: receiver.Village.Id, Shipment: new(50, 0, 0, 0)), 0, 0);
        Check(QuietUntil(() => quiet.Snapshot(sender.Village.OwnerId).Convoys.Any(c => c.Returning)), "The shipment delivers and heads home empty");
        Send(quiet, quietCity, "infantry", 50, 500, 500, true);
        Check(QuietUntil(() => quiet.Snapshot(sender.Village.OwnerId).Convoys.Length == 0 || quiet.Snapshot(sender.Village.OwnerId).Convoys.Any(c => c.InBattle))
            && quiet.Snapshot(quietCity.Village.OwnerId).Warfare.Battles.Length == 0, "A loaded-only camp lets an empty caravan pass");

        var splitCity = City(500); var splitWorld = new LogisticsWorld([splitCity], 731);
        Gift(splitCity, "infantry", 10); Gift(splitCity, "archer", 5); splitCity.AddResources(new(2000, 2000, 2000, 2000));
        splitWorld.Apply(splitCity.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, splitCity.Village.Id,
            Formation: new Dictionary<string, int> { ["romans-infantry"] = 10, ["romans-archer"] = 5, ["romans-wagon"] = 1 },
            Wagons: 0, Enabled: false, X: 550, Y: 500), 0, 0);
        var original = splitWorld.Snapshot(splitCity.Village.OwnerId).Armies.Single();
        Reject(() => splitWorld.Apply(splitCity.Village.OwnerId, new(Guid.NewGuid(), CommandKind.MoveArmy, original.Id,
            Formation: new Dictionary<string, int> { ["romans-infantry"] = 11 }, X: 600, Y: 500), 0, 0), "troops");
        Reject(() => splitWorld.Apply(splitCity.Village.OwnerId, new(Guid.NewGuid(), CommandKind.AttackArmy, original.Id,
            Formation: new Dictionary<string, int> { ["romans-infantry"] = 6 }, DestinationId: Guid.NewGuid()), 0, 0), "target");
        Check(splitWorld.Snapshot(splitCity.Village.OwnerId).Armies.Length == 1, "Rejected orders do not split an army");
        splitWorld.Apply(splitCity.Village.OwnerId, new(Guid.NewGuid(), CommandKind.MoveArmy, original.Id,
            Formation: new Dictionary<string, int> { ["romans-infantry"] = 6, ["romans-archer"] = 2 }, X: 600, Y: 500), 0, 0);
        var split = splitWorld.Snapshot(splitCity.Village.OwnerId).Armies;
        Check(split.Length == 2 && split.Single(a => a.Id == original.Id).Formation!["romans-infantry"] == 6
            && split.Single(a => a.Id == original.Id).Formation!["romans-archer"] == 2, "The selected detachment receives the new order");
        var remainder = split.Single(a => a.Id != original.Id);
        Check(remainder.Order == "Marching" && remainder.Destination.X == 550 && remainder.Formation!["romans-infantry"] == 4 && remainder.Formation!["romans-archer"] == 3
            && remainder.Formation["romans-wagon"] == 1, "The unselected troops and wagons keep their current orders");
        Check(split.Sum(a => a.Soldiers + a.Wagons) == 16 && Math.Abs(split.Sum(a => a.Wheat ?? 0) - original.Wheat!.Value) < .000001,
            "Splitting conserves every unit and all carried wheat");
        splitWorld.Apply(splitCity.Village.OwnerId, new(Guid.NewGuid(), CommandKind.MoveArmy, original.Id,
            Formation: new Dictionary<string, int> { ["romans-infantry"] = 6, ["romans-archer"] = 2 }, X: 650, Y: 500), 0, 0);
        Check(splitWorld.Snapshot(splitCity.Village.OwnerId).Armies.Length == 2, "Sending the full detachment does not create an empty army");
        return checks;
    }
}
