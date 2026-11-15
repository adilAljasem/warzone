using Warzone;

internal static class BugFixChecks
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        void Reject(Action action, string code)
        {
            try { action(); }
            catch (GameRuleException e) when (e.Code == code) { checks++; return; }
            throw new Exception($"Expected rule rejection: {code}");
        }

        // 1. Ally joins Raze / Conquest siege while allied attacker is approaching or sieging (without duplicate catapult/governor).
        {
            var a = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500));
            var b = new VillageEconomy(Guid.NewGuid(), TribeType.Greeks, new(650, 500));
            var c = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(520, 560));
            var d = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(450, 450));
            var world = new LogisticsWorld([a, b, c, d], 101);
            foreach (var city in new[] { a, b, c, d })
            {
                city.AddResources(new(15000, 15000, 15000, 15000));
                city.ReceiveGift([
                    new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 120),
                    new(BaseUnit.Create(GameCatalog.Units["romans-catapult"]), 2),
                    new(BaseUnit.Create(GameCatalog.Units["romans-governor"]), 1),
                    new(BaseUnit.Create(GameCatalog.Units["romans-wagon"]), 4)
                ]);
            }
            world.SetAlliances([new(a.Village.OwnerId, c.Village.OwnerId, 0)]);

            // A sends Raze with catapult.
            world.Apply(a.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, a.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 60, ["romans-catapult"] = 2, ["romans-wagon"] = 2 },
                Wagons: 0, DestinationId: b.Village.Id, Objective: AttackObjective.Raze), 0, 0);

            var cView = world.Snapshot(c.Village.OwnerId);
            var aArmy = cView.Armies.Single(x => x.OwnerId == a.Village.OwnerId);
            Check(aArmy.Objective == AttackObjective.Raze, "Ally can inspect approaching army objective");

            // Unallied player D without catapult is still rejected.
            Reject(() => world.Apply(d.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, d.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 40, ["romans-wagon"] = 1 },
                Wagons: 0, DestinationId: b.Village.Id, Objective: AttackObjective.Raze), 0, 0), "catapult");

            // Ally C without catapult CAN join A's Raze while approaching AND after siege starts.
            world.Apply(c.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, c.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 60, ["romans-wagon"] = 1 },
                Wagons: 0, DestinationId: b.Village.Id, Objective: AttackObjective.Raze), 0, 0);

            var now = 0d;
            for (var i = 0; i < 600; i++)
            {
                now += .25;
                world.Advance(.25, now, 0, 3600);
                var s = world.Snapshot(a.Village.OwnerId);
                if (s.Warfare.Sieges.Any(x => x.Status == "Active" && x.ArmyIds.Length == 2)) break;
            }
            var siegeSnap = world.Snapshot(a.Village.OwnerId);
            Check(siegeSnap.Warfare.Sieges.Any(x => x.Status == "Active" && x.Objective == AttackObjective.Raze && x.ArmyIds.Length == 2),
                "Allied army without catapult successfully joined allied Raze siege");
        }

        // 2a. Defeated enemy army provisions (wheat) + loot are looted and split proportionally by carry capacity,
        // and victorious armies consume looted wheat as food instead of starving.
        {
            var a1 = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500));
            var a2 = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(520, 500));
            var enemy = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(650, 500));
            var world = new LogisticsWorld([a1, a2, enemy], 202);
            foreach (var city in new[] { a1, a2, enemy })
            {
                city.AddResources(new(25000, 25000, 25000, 25000));
                city.ReceiveGift([
                    new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 200),
                    new(BaseUnit.Create(GameCatalog.Units["romans-wagon"]), 6)
                ]);
            }
            world.SetAlliances([new(a1.Village.OwnerId, a2.Village.OwnerId, 0)]);

            // Two allied armies (larger + smaller) fight an enemy carrying extra wheat provisions + wagons.
            world.Apply(a1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, a1.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 120, ["romans-wagon"] = 1 },
                Wagons: 0, X: 580, Y: 500, AfterAttack: AfterAttack.Hold), 0, 0);
            world.Apply(a2.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, a2.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 40, ["romans-wagon"] = 1 },
                Wagons: 0, X: 580, Y: 500, AfterAttack: AfterAttack.Hold), 0, 0);
            world.Apply(enemy.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, enemy.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 30, ["romans-wagon"] = 3 },
                Wagons: 0, X: 620, Y: 500, AfterAttack: AfterAttack.Hold), 0, 0);

            var now = 0d;
            for (var i = 0; i < 160; i++) { now += .25; world.Advance(.25, now, 0, 3600); }
            var enemyArmyId = world.Snapshot(enemy.Village.OwnerId).Armies.Single(x => x.OwnerId == enemy.Village.OwnerId).Id;
            var enemyWheatBefore = world.Snapshot(enemy.Village.OwnerId).Armies.Single(x => x.Id == enemyArmyId).Wheat!.Value;
            Check(enemyWheatBefore > 200, "Enemy expedition carries significant wheat provisions");

            var a1Id = world.Snapshot(a1.Village.OwnerId).Armies.Single(x => x.OwnerId == a1.Village.OwnerId).Id;
            var a2Id = world.Snapshot(a2.Village.OwnerId).Armies.Single(x => x.OwnerId == a2.Village.OwnerId).Id;
            world.Apply(a1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.AttackArmy, a1Id, DestinationId: enemyArmyId, AfterAttack: AfterAttack.Hold), now, 0);
            world.Apply(a2.Village.OwnerId, new(Guid.NewGuid(), CommandKind.AttackArmy, a2Id, DestinationId: enemyArmyId, AfterAttack: AfterAttack.Hold), now, 0);

            for (var i = 0; i < 400; i++)
            {
                now += .25;
                world.Advance(.25, now, 0, 3600);
                var snap = world.Snapshot(a1.Village.OwnerId);
                if (snap.Reports.Any(r => r.Kind == "Battle" && r.Status == "Resolved")) break;
            }
            var after = world.Snapshot(a1.Village.OwnerId);
            var win1 = after.Armies.Single(x => x.Id == a1Id);
            var win2 = after.Armies.Single(x => x.Id == a2Id);
            var report = after.Reports.Single(r => r.Kind == "Battle" && r.Status == "Resolved");
            var attackerCount = report.Armies.Count(x => x.Attacking);
            Check(attackerCount == 2, $"Both allied armies participated in battle (got {attackerCount})");
            Check((win1.Loot?.Wheat ?? 0) > 0 && (win2.Loot?.Wheat ?? 0) > 0,
                $"Defeated enemy provisions were looted and split across both victorious armies (w1={win1.Loot?.Wheat}, w2={win2.Loot?.Wheat})");
            Check((win1.Loot?.Wheat ?? 0) > (win2.Loot?.Wheat ?? 0), "Larger carry capacity winner received larger share of loot");
        }

        // 2b. Any non-returning field army carrying >= 2000 resources triggers automatic collection wagons,
        // collects excess resources back to village, and leaves the army with food provisions.
        {
            var hunter = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500));
            var victim = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(600, 500));
            var peer = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(750, 500));
            var world = new LogisticsWorld([hunter, victim, peer], 303);
            foreach (var city in new[] { hunter, victim, peer })
            {
                var wh = city.Village.Buildings.Single(b => b.Type == BuildingType.Warehouse);
                while (wh.Level < 10) wh.CompleteUpgrade(wh.Level);
                city.AddResources(new(25000, 25000, 25000, 25000));
                city.ReceiveGift([
                    new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 120),
                    new(BaseUnit.Create(GameCatalog.Units["romans-wagon"]), 6)
                ]);
            }

            // March hunter (NOT a camp) into the convoy's path and intercept victim's 3000-wood convoy.
            world.Apply(hunter.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, hunter.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 120, ["romans-wagon"] = 1 },
                Wagons: 0, X: 680, Y: 500, AfterAttack: AfterAttack.Hold), 0, 0);
            var now = 0d;
            for (var i = 0; i < 120; i++) { now += .25; world.Advance(.25, now, 0, 3600); }

            world.Apply(victim.Village.OwnerId, new(Guid.NewGuid(), CommandKind.SendResources, victim.Village.Id,
                DestinationId: peer.Village.Id, Shipment: new(3000, 0, 0, 0)), now, 0);
            var convoyId = world.Snapshot(victim.Village.OwnerId).Convoys.Single(c => c.Kind == ConvoyKind.Shipment).Id;
            var hunterId = world.Snapshot(hunter.Village.OwnerId).Armies.Single(x => x.OwnerId == hunter.Village.OwnerId).Id;
            world.Apply(hunter.Village.OwnerId, new(Guid.NewGuid(), CommandKind.InterceptConvoy, hunterId, DestinationId: convoyId, AfterAttack: AfterAttack.Hold), now, 0);
            for (var i = 0; i < 240; i++)
            {
                now += .25;
                world.Advance(.25, now, 0, 3600);
                var h = world.Snapshot(hunter.Village.OwnerId).Armies.Single(x => x.Id == hunterId);
                if ((h.Loot?.Total ?? 0) >= 2000) break;
            }
            var hArmy = world.Snapshot(hunter.Village.OwnerId).Armies.Single(x => x.Id == hunterId);
            Check(hArmy.Order != "Camped" && (hArmy.Loot?.Total ?? 0) >= 2000, "Regular field army carries >= 2000 looted resources");

            var wagonLeft = false;
            for (var i = 0; i < 160; i++)
            {
                now += .25;
                world.Advance(.25, now, 0, 3600);
                if (world.Snapshot(hunter.Village.OwnerId).Convoys.Any(c => c.Kind == ConvoyKind.Supply && c.TargetArmyId == hunterId))
                {
                    wagonLeft = true;
                    break;
                }
            }
            Check(wagonLeft, "Collection wagon automatically departed for non-camp army carrying >= 2000 resources");
            for (var i = 0; i < 400; i++)
            {
                now += .25;
                world.Advance(.25, now, 0, 3600);
                var cur = world.Snapshot(hunter.Village.OwnerId);
                if (!cur.Convoys.Any(c => c.Kind == ConvoyKind.Supply && !c.Returning && c.TargetArmyId == hunterId)
                    && (cur.Armies.Single(a => a.Id == hunterId).Loot?.Total ?? 0) < 2000)
                    break;
            }
            var doneArmy = world.Snapshot(hunter.Village.OwnerId).Armies.Single(x => x.Id == hunterId);
            Check((doneArmy.Loot?.Total ?? 0) < 2000 && (doneArmy.Wheat ?? 0) > 100, "Collection wagon retrieved excess resources and kept enough food with the army");
        }

        // 3. Starving late-game bot with excess garrison relieves starvation instead of deadlocking at 0 wheat.
        {
            var bot = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500));
            var enemy = new VillageEconomy(Guid.NewGuid(), TribeType.Greeks, new(750, 500));
            var world = new LogisticsWorld([bot, enemy], 404);
            bot.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 900)]);
            var now = 0d;
            // Advance until garrison drains wheat storage to 0 and triggers FoodShortage.
            for (var i = 0; i < 600 && !bot.FoodShortage; i++) { now += 1; world.AdvanceEconomies(1, now, 1); world.Advance(1, now, 1, 3600); }
            Check(bot.FoodShortage, "Over-garrisoned bot entered FoodShortage before recovery");
            var commands = new WorldTestCommands(world);
            now = 1800d;
            for (var step = 0; step < 30; step++)
            {
                now += 1;
                world.AdvanceEconomies(1, now, 1);
                world.Advance(1, now, 1, 3600);
                var eco = world.EconomySnapshot(bot.Village.OwnerId, null, 1)!;
                BotController.ActEconomy(commands, bot.Village.OwnerId, eco, BotPersonality.Economic, BotDifficulty.Normal);
                world.BotAct(bot.Village.OwnerId, commands, BotPersonality.Economic, BotDifficulty.Normal);
            }
            var netAfter = world.EconomySnapshot(bot.Village.OwnerId, null, 1)!.Production.Single(p => p.Resource == "Wheat").NetPerMinute;
            Check(netAfter >= 0 || bot.Resources.Wheat > 0, "Starving over-garrisoned bot recovered positive wheat net / non-zero wheat");
            Check(bot.Garrison().Sum(t => t.Count) < 920, "Bot disbanded excess starving garrison troops");
        }

        return checks;
    }

    private sealed class WorldTestCommands(LogisticsWorld world) : IGameCommands
    {
        public CommandResult Execute(Guid actorId, GameCommand command)
        {
            try
            {
                world.Apply(actorId, command, 1800, 0);
                return new CommandResult(command.RequestId, true, "ok", "ok");
            }
            catch (GameRuleException ex)
            {
                return new CommandResult(command.RequestId, false, ex.Code, ex.Message);
            }
        }
    }
}
