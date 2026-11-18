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

        // 4. Recalled / withdrawing returning armies are never locked as "broken" and can be re-commanded immediately.
        {
            var a = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500));
            var b = new VillageEconomy(Guid.NewGuid(), TribeType.Greeks, new(750, 500));
            var world = new LogisticsWorld([a, b], 505);
            a.AddResources(new(10000, 10000, 10000, 10000));
            a.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 50)]);
            world.Apply(a.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, a.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 50 }, Wagons: 0, X: 620, Y: 500), 0, 0);
            var armyId = world.Snapshot(a.Village.OwnerId).Armies.Single().Id;
            world.Advance(1, 1, 0, 3600);
            world.Apply(a.Village.OwnerId, new(Guid.NewGuid(), CommandKind.RecallArmy, armyId), 1, 0);
            Check(world.Snapshot(a.Village.OwnerId).Armies.Single(x => x.Id == armyId).Order == "Returning", "Recalled army is returning home");
            world.Apply(a.Village.OwnerId, new(Guid.NewGuid(), CommandKind.MoveArmy, armyId, X: 660, Y: 520), 1, 0);
            var redirected = world.Snapshot(a.Village.OwnerId).Armies.Single(x => x.Id == armyId);
            Check(redirected.Order == "Marching" && Math.Abs(redirected.Destination.X - 660) < 0.01, "Returning army can be immediately re-commanded without broken lock");
        }

        // 5. First-to-arrive ally sets city siege objective; second ally arriving with a different objective adopts and joins.
        {
            var a1 = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500));
            var a2 = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(540, 560));
            var enemy = new VillageEconomy(Guid.NewGuid(), TribeType.Greeks, new(620, 500));
            var world = new LogisticsWorld([a1, a2, enemy], 606);
            foreach (var c in new[] { a1, a2, enemy })
            {
                c.AddResources(new(15000, 15000, 15000, 15000));
                c.ReceiveGift([
                    new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 120),
                    new(BaseUnit.Create(GameCatalog.Units["romans-catapult"]), 2),
                    new(BaseUnit.Create(GameCatalog.Units["romans-wagon"]), 4)
                ]);
            }
            world.SetAlliances([new(a1.Village.OwnerId, a2.Village.OwnerId, 0)]);

            // Faster infantry Raid launched slightly earlier or slower Raze arrives first: Raze arrives first? Launch Raze closer (a1 is at 500,500) and Raid further away (a2 is at 540,560) AFTER a1 has catapults -> actually send Blockade from a2 later so a1 arrives first as Raze!
            world.Apply(a1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, a1.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 80, ["romans-catapult"] = 2, ["romans-wagon"] = 2 },
                Wagons: 0, DestinationId: enemy.Village.Id, Objective: AttackObjective.Raze), 0, 0);
            var now = 0d;
            for (var i = 0; i < 40; i++) { now += .25; world.Advance(.25, now, 0, 3600); }
            // a2 marches with Raid before siege is established, arrives second after a1 starts Raze.
            world.Apply(a2.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, a2.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 40, ["romans-wagon"] = 1 },
                Wagons: 0, DestinationId: enemy.Village.Id, Objective: AttackObjective.Raid), now, 0);
            for (var i = 0; i < 600; i++)
            {
                now += .25;
                world.Advance(.25, now, 0, 3600);
                if (world.Snapshot(a1.Village.OwnerId).Warfare.Sieges.Any(s => s.Status == "Active" && s.ArmyIds.Length == 2)) break;
            }
            var siege = world.Snapshot(a1.Village.OwnerId).Warfare.Sieges.Single(s => s.Status == "Active");
            Check(siege.Objective == AttackObjective.Raze && siege.ArmyIds.Length == 2,
                "Second allied army arriving after first establishes Raze adopts Raze and joins the siege");
        }

        // 6. Capturing an owned oasis with stockpiled resources loots those resources.
        {
            var prev = new VillageEconomy(Guid.NewGuid(), TribeType.Greeks, new(700, 500));
            var taker = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500));
            var world = new LogisticsWorld([prev, taker], 707);
            prev.AddResources(new(10000, 10000, 10000, 10000));
            taker.AddResources(new(10000, 10000, 10000, 10000));
            taker.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 80), new(BaseUnit.Create(GameCatalog.Units["romans-wagon"]), 2)]);

            var oasis = world.Snapshot(prev.Village.OwnerId).Oases.OrderBy(o => LogisticsWorld.Distance(prev.Village.Position, o.Position)).First();
            world.Apply(prev.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, prev.Village.Id,
                Formation: new Dictionary<string, int> { ["greeks-infantry"] = 5 }, Wagons: 0, DestinationId: oasis.Id, AfterAttack: AfterAttack.Return), 0, 0);
            var now = 0d;
            for (var i = 0; i < 400 && world.Snapshot(prev.Village.OwnerId).Oases.Single(o => o.Id == oasis.Id).OwnerId != prev.Village.OwnerId; i++)
            {
                now += .25;
                world.Advance(.25, now, 0, 3600);
            }
            world.Apply(prev.Village.OwnerId, new(Guid.NewGuid(), CommandKind.SetOasisTransport, oasis.Id, Enabled: false), now, 0);
            for (var i = 0; i < 120; i++) { now += 1; world.AdvanceEconomies(1, now, 1); world.Advance(1, now, 0, 3600); }
            var storedBeforeCapture = world.Snapshot(prev.Village.OwnerId).Oases.Single(o => o.Id == oasis.Id).Stock?.Total ?? 0;
            Check(storedBeforeCapture > 0, "Owned oasis accumulated stockpiled resources");

            world.Apply(taker.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, taker.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 60, ["romans-wagon"] = 1 },
                Wagons: 0, DestinationId: oasis.Id, OasisMode: OasisMode.Capture, AfterAttack: AfterAttack.Hold), now, 0);
            for (var i = 0; i < 600 && world.Snapshot(taker.Village.OwnerId).Oases.Single(o => o.Id == oasis.Id).OwnerId != taker.Village.OwnerId; i++)
            {
                now += 1;
                world.AdvanceEconomies(1, now, 1);
                world.Advance(1, now, 0, 3600);
            }
            var captorArmy = world.Snapshot(taker.Village.OwnerId).Armies.Single(a => a.OwnerId == taker.Village.OwnerId);
            Check((captorArmy.Loot?.Total ?? 0) >= storedBeforeCapture, "Capturing army looted stockpiled resources from the conquered oasis");
        }

        // 7. A single wagon (carry capacity 5000) loads > 2000 resources (e.g. 4200) up to its capacity while leaving provisions.
        {
            var hunter = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500));
            var victim = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(600, 500));
            var peer = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(780, 500));
            var world = new LogisticsWorld([hunter, victim, peer], 808);
            foreach (var city in new[] { hunter, victim, peer })
            {
                var wh = city.Village.Buildings.Single(b => b.Type == BuildingType.Warehouse);
                while (wh.Level < 10) wh.CompleteUpgrade(wh.Level);
                city.AddResources(new(25000, 25000, 25000, 25000));
                city.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 120), new(BaseUnit.Create(GameCatalog.Units["romans-wagon"]), 6)]);
            }
            // Leave hunter with exact 1 free wagon at home after dispatch (start had 2+6=8, keep 1 at home)
            world.Apply(hunter.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, hunter.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 120, ["romans-wagon"] = 7 },
                Wagons: 0, X: 690, Y: 500, AfterAttack: AfterAttack.Hold), 0, 0);
            var now = 0d;
            for (var i = 0; i < 120; i++) { now += .25; world.Advance(.25, now, 0, 3600); }
            world.Apply(victim.Village.OwnerId, new(Guid.NewGuid(), CommandKind.SendResources, victim.Village.Id,
                DestinationId: peer.Village.Id, Shipment: new(4200, 0, 0, 0)), now, 0);
            var cid = world.Snapshot(victim.Village.OwnerId).Convoys.Single(c => c.Kind == ConvoyKind.Shipment).Id;
            var hid = world.Snapshot(hunter.Village.OwnerId).Armies.Single(x => x.OwnerId == hunter.Village.OwnerId).Id;
            world.Apply(hunter.Village.OwnerId, new(Guid.NewGuid(), CommandKind.InterceptConvoy, hid, DestinationId: cid, AfterAttack: AfterAttack.Hold), now, 0);
            for (var i = 0; i < 240 && (world.Snapshot(hunter.Village.OwnerId).Armies.Single(x => x.Id == hid).Loot?.Total ?? 0) < 4000; i++)
            {
                now += .25; world.Advance(.25, now, 0, 3600);
            }
            for (var i = 0; i < 400; i++)
            {
                now += .25; world.Advance(.25, now, 0, 3600);
                if (world.Snapshot(hunter.Village.OwnerId).Convoys.Any(c => c.Kind == ConvoyKind.Supply && c.Returning && c.TargetArmyId == hid)) break;
            }
            var retConvoy = world.Snapshot(hunter.Village.OwnerId).Convoys.Single(c => c.Kind == ConvoyKind.Supply && c.Returning && c.TargetArmyId == hid);
            Check(retConvoy.Wagons == 1 && (retConvoy.Cargo?.Total ?? 0) >= 4200,
                $"Single 5000-cap wagon loaded > 2000 resources in one trip (loaded={retConvoy.Cargo?.Total})");
        }

        // 8. Late-game Hard bot with 1500+ starving troops inside village recovers wheat and dispatches field armies.
        {
            var bot = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500));
            var enemy = new VillageEconomy(Guid.NewGuid(), TribeType.Greeks, new(1600, 500));
            var world = new LogisticsWorld([bot, enemy], 909);
            bot.ReceiveGift([
                new(BaseUnit.Create(GameCatalog.Units["romans-heavycavalry"]), 300),
                new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 1200),
                new(BaseUnit.Create(GameCatalog.Units["romans-catapult"]), 10),
                new(BaseUnit.Create(GameCatalog.Units["romans-ram"]), 10),
                new(BaseUnit.Create(GameCatalog.Units["romans-wagon"]), 8),
            ]);
            var now = 0d;
            for (var i = 0; i < 600 && !bot.FoodShortage; i++) { now += 1; world.AdvanceEconomies(1, now, 1); world.Advance(1, now, 1, 3600); }
            Check(bot.FoodShortage && bot.Resources.Wheat == 0, "Late-game Hard bot started in severe FoodShortage at 0 wheat");

            now = 2100d;
            var commands = new WorldTestCommands(world, () => now);
            for (var step = 0; step < 120; step++)
            {
                now += 1;
                world.AdvanceEconomies(1, now, 1);
                world.Advance(1, now, 0, 3600);
                var eco = world.EconomySnapshot(bot.Village.OwnerId, null, 1)!;
                BotController.ActEconomy(commands, bot.Village.OwnerId, eco, BotPersonality.Defensive, BotDifficulty.Hard);
                world.BotAct(bot.Village.OwnerId, commands, BotPersonality.Defensive, BotDifficulty.Hard);
            }
            var armiesOut = world.Snapshot(bot.Village.OwnerId).Armies.Count(a => a.OwnerId == bot.Village.OwnerId && a.Order != "Guarding");
            Check(bot.Resources.Wheat > 0, $"Late-game bot accumulated wheat instead of staying stuck at 0 (wheat={bot.Resources.Wheat})");
            Check(armiesOut >= 1, $"Late-game bot dispatched army out of the village after recovering from wheat shortage (armiesOut={armiesOut})");
        }

        // 9. Instant Market 2:1 swap + bot uses SwapMarket to rescue wheat shortage + MainHall 50% production bonus per 5 levels.
        {
            var c1 = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500));
            var c2 = new VillageEconomy(Guid.NewGuid(), TribeType.Greeks, new(900, 500));
            var world = new LogisticsWorld([c1, c2], 1010);
            var wh = c1.Village.Buildings.Single(b => b.Type == BuildingType.Warehouse);
            while (wh.Level < 5) wh.CompleteUpgrade(wh.Level);
            c1.AddResources(new(4000, 4000, 4000, 0));
            var ironBefore = c1.Resources.Iron;
            var wheatBefore = c1.Resources.Wheat;

            // 2:1 swap: receive 500 Wheat, pay 1000 Iron
            world.Apply(c1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.SwapMarket, c1.Village.Id,
                OfferResource: ResourceType.Iron, AskResource: ResourceType.Wheat, AskAmount: 500), 0, 0);
            Check(c1.Resources.Iron == ironBefore - 1000 && c1.Resources.Wheat == wheatBefore + 500,
                "Instant 2:1 market swap deducts 2x resource and adds 1x target resource");

            // Main Hall +50% resource production per 5 levels
            var hall = c1.Village.Buildings.Single(b => b.Type == BuildingType.MainHall);
            var lvl1Wood = c1.Production().Single(p => p.Resource == "Wood").GrossPerMinute;
            while (hall.Level < 5) hall.CompleteUpgrade(hall.Level);
            var lvl5Wood = c1.Production().Single(p => p.Resource == "Wood").GrossPerMinute;
            Check(Math.Abs(lvl5Wood - lvl1Wood * 1.5) < 0.01, "Main Hall level 5 boosts all resource production by +50%");
            while (hall.Level < 10) hall.CompleteUpgrade(hall.Level);
            var lvl10Wood = c1.Production().Single(p => p.Resource == "Wood").GrossPerMinute;
            Check(Math.Abs(lvl10Wood - lvl1Wood * 2.0) < 0.01, "Main Hall level 10 boosts all resource production by +100%");

            // Besieged village cannot upgrade buildings to stall demolition
            c1.Besieged = true;
            Reject(() => c1.Apply(new(Guid.NewGuid(), CommandKind.Upgrade,
                c1.Snapshot().Targets.First(t => t.Name == "Barracks").Id), 0), "siege");
            c1.Besieged = false;

            // Bot rescues FoodShortage instantly using surplus Iron via SwapMarket
            c2.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["greeks-infantry"]), 700)]);
            var now = 0d;
            for (var i = 0; i < 600 && !c2.FoodShortage; i++) { now += 1; world.AdvanceEconomies(1, now, 1); world.Advance(1, now, 1, 3600); }
            c2.AddResources(new(3000, 3000, 0, 0));
            var botCmd = new WorldTestCommands(world, () => now);
            world.BotAct(c2.Village.OwnerId, botCmd, BotPersonality.Economic, BotDifficulty.Normal);
            Check(c2.Resources.Wheat > 0, "Bot used instant 2:1 market swap to convert surplus Wood/Iron into Wheat immediately");
        }

        // 10. Crown Keep requires the current controller to hold for a full 5 continuous minutes (no inheriting prior ally's elapsed hold time).
        {
            var p1 = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(2200, 2250));
            var p2 = new VillageEconomy(Guid.NewGuid(), TribeType.Greeks, new(2300, 2250));
            var world = new LogisticsWorld([p1, p2], 1111, castleUnlockSeconds: 0);
            world.SetAlliances([new(p1.Village.OwnerId, p2.Village.OwnerId, 0)]);
            p1.AddResources(new(10000, 10000, 10000, 20000));
            p1.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 40), new(BaseUnit.Create(GameCatalog.Units["romans-wagon"]), 4)]);
            p2.AddResources(new(10000, 10000, 10000, 20000));
            p2.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["greeks-infantry"]), 40), new(BaseUnit.Create(GameCatalog.Units["greeks-wagon"]), 4)]);
            world.Advance(.25, .25, 0, 3600); // activate castle
            var keepId = world.Snapshot(p1.Village.OwnerId).Warfare.Castle.Id;
            world.Apply(p1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, p1.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 40, ["romans-wagon"] = 4 }, Wagons: 4, DestinationId: keepId,
                X: LogisticsWorld.MapCenter, Y: LogisticsWorld.MapCenter), .25, 0);
            var now = .25d;
            for (var i = 0; i < 245; i++) { now += 1; world.Advance(1, now, 0, 3600); }
            var mid = world.Snapshot(p1.Village.OwnerId).Warfare.Castle;
            Check(mid.ControllerId == p1.Village.OwnerId && mid.CaptureProgress >= 220, $"First ally held Crown Keep near 4 minutes (controller={mid.ControllerId}, progress={mid.CaptureProgress})");

            // Ally p2 moves in and p1 recalls: p2 becomes the new ruler and timer must reset to 0!
            world.Apply(p2.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, p2.Village.Id,
                Formation: new Dictionary<string, int> { ["greeks-infantry"] = 40, ["greeks-wagon"] = 4 }, Wagons: 4, DestinationId: keepId,
                X: LogisticsWorld.MapCenter, Y: LogisticsWorld.MapCenter), now, 0);
            for (var i = 0; i < 20; i++) { now += 1; world.Advance(1, now, 0, 3600); }
            var p1Army = world.Snapshot(p1.Village.OwnerId).Armies.Single(a => a.OwnerId == p1.Village.OwnerId).Id;
            world.Apply(p1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.RecallArmy, p1Army), now, 0);
            for (var i = 0; i < 45; i++) { now += 1; world.Advance(1, now, 0, 3600); }
            var handoff = world.Snapshot(p2.Village.OwnerId).Warfare.Castle;
            Check(world.CrownWinner is null && handoff.ControllerId == p2.Village.OwnerId && handoff.CaptureProgress < 60,
                $"Crown Keep reset timer on ruler change instead of letting ally win in < 1 minute (progress={handoff.CaptureProgress})");
        }

        // 11. Critical backend fixes regression checks:
        // 11a. Biome Voronoi generation stays finite across many seeds
        {
            for (var seed = 0; seed < 300; seed++)
            {
                var regions = Biomes.Generate(seed);
                Check(regions.All(r => r.Boundary.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y))),
                    $"Seed {seed} produces finite biome boundary coordinates");
            }
        }

        // 11b. Intercepted supply convoy awards its full WheatCargo as spoils even when all wagons die
        {
            var fieldOwner = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500));
            var interceptor = new VillageEconomy(Guid.NewGuid(), TribeType.Germans, new(600, 620));
            var world = new LogisticsWorld([fieldOwner, interceptor], 303);
            foreach (var c in new[] { fieldOwner, interceptor })
            {
                var wh = c.Village.Buildings.Single(b => b.Type == BuildingType.Warehouse);
                while (wh.Level < 10) wh.CompleteUpgrade(wh.Level);
                c.AddResources(new(10000, 10000, 10000, 20000));
                c.ReceiveGift([
                    new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 450),
                    new(BaseUnit.Create(GameCatalog.Units["romans-wagon"]), 25),
                    new(BaseUnit.Create(GameCatalog.Units["germans-lightcavalry"]), 120)
                ]);
            }
            // Send large infantry army far away with 0 wagons so Supply dispatches an unescorted wheat convoy (WheatCargo > 0)
            world.Apply(fieldOwner.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, fieldOwner.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 400 }, Wagons: 0, X: 950, Y: 500), 0, 0);
            var now = 0d;
            for (var i = 0; i < 3200 && !world.Snapshot(fieldOwner.Village.OwnerId).Convoys.Any(c => c.Kind == ConvoyKind.Supply); i++)
            { now += .25; world.Advance(.25, now, 0, 3600); }
            var convoy = world.Snapshot(fieldOwner.Village.OwnerId).Convoys.First(c => c.Kind == ConvoyKind.Supply);
            world.Apply(interceptor.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, interceptor.Village.Id,
                Formation: new Dictionary<string, int> { ["germans-lightcavalry"] = 120 }, Wagons: 0, X: convoy.Position.X, Y: convoy.Position.Y), now, 0);
            var raider = world.Snapshot(interceptor.Village.OwnerId).Armies.Single(a => a.OwnerId == interceptor.Village.OwnerId);
            world.Apply(interceptor.Village.OwnerId, new(Guid.NewGuid(), CommandKind.InterceptConvoy, raider.Id, DestinationId: convoy.Id), now, 0);
            for (var i = 0; i < 200; i++) { now += .25; world.Advance(.25, now, 0, 3600); }
            var report = world.Snapshot(interceptor.Village.OwnerId).Reports.FirstOrDefault(r => r.Kind == "Interception");
            Check(report is not null && report.DefenderLoss.Wheat > 0 && report.AttackerLoot.Wheat > 0,
                $"Intercepted wheat supply convoy preserves WheatCargo spoils (loss={report?.DefenderLoss.Wheat}, loot={report?.AttackerLoot.Wheat})");
        }

        // 11c. Raze siege awards building salvage on completion
        {
            var att = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500));
            var def = new VillageEconomy(Guid.NewGuid(), TribeType.Greeks, new(650, 500));
            var world = new LogisticsWorld([att, def], 404);
            var wh = def.Village.Buildings.Single(b => b.Type == BuildingType.Warehouse);
            while (wh.Level < 8) wh.CompleteUpgrade(wh.Level);
            att.AddResources(new(10000, 10000, 10000, 20000));
            att.ReceiveGift([
                new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 120),
                new(BaseUnit.Create(GameCatalog.Units["romans-catapult"]), 4),
                new(BaseUnit.Create(GameCatalog.Units["romans-wagon"]), 10)
            ]);
            def.AddResources(new(200, 200, 200, 200));
            world.Apply(att.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, att.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 120, ["romans-catapult"] = 4, ["romans-wagon"] = 10 },
                Wagons: 0, DestinationId: def.Village.Id, Objective: AttackObjective.Raze, AfterAttack: AfterAttack.Hold), 0, 0);
            var now = 0d;
            for (var i = 0; i < 2000 && !def.Ruined; i++) { now += .25; world.Advance(.25, now, 0, 3600); }
            var razeReport = world.Snapshot(att.Village.OwnerId).Reports.Single(r => r.Kind == "Siege");
            Check(def.Ruined && razeReport.DefenderLoss.Wood > 200 && razeReport.AttackerLoot.Wood > 200,
                $"Raze siege preserves building salvage beyond stored village stock (defLossWood={razeReport.DefenderLoss.Wood}, lootWood={razeReport.AttackerLoot.Wood})");
        }

        // 11d. Rehome does not end allied battle prematurely and ResumeBattleWinner preserves distant Reinforcing & Capturing orders
        {
            var p1 = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500));
            var p2 = new VillageEconomy(Guid.NewGuid(), TribeType.Greeks, new(560, 500));
            var enemy = new VillageEconomy(Guid.NewGuid(), TribeType.Germans, new(620, 500));
            var raider = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 540));
            var world = new LogisticsWorld([p1, p2, enemy, raider], 505, castleUnlockSeconds: 0);
            world.SetAlliances([new(p1.Village.OwnerId, p2.Village.OwnerId, 0), new(enemy.Village.OwnerId, raider.Village.OwnerId, 0)]);
            foreach (var c in new[] { p1, p2, enemy, raider })
            {
                var wh = c.Village.Buildings.Single(b => b.Type == BuildingType.Warehouse);
                while (wh.Level < 10) wh.CompleteUpgrade(wh.Level);
                c.AddResources(new(15000, 15000, 15000, 25000));
            }
            p1.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 50)]);
            p2.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["greeks-infantry"]), 450)]);
            enemy.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["germans-infantry"]), 400)]);
            raider.ReceiveGift([
                new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 150),
                new(BaseUnit.Create(GameCatalog.Units["romans-governor"]), 3),
                new(BaseUnit.Create(GameCatalog.Units["romans-wagon"]), 10)
            ]);
            // Start multi-army battle: p1 attacks enemy army, p2 joins on p1's side, while raider conquers undefended p1!
            world.Apply(enemy.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, enemy.Village.Id,
                Formation: new Dictionary<string, int> { ["germans-infantry"] = 400 }, Wagons: 0, X: 820, Y: 500), 0, 0);
            var now = 0d;
            for (var i = 0; i < 60; i++) { now += .25; world.Advance(.25, now, 0, 3600); }
            var enemyArmyId = world.Snapshot(enemy.Village.OwnerId).Armies.Single(a => a.OwnerId == enemy.Village.OwnerId).Id;
            world.Apply(p1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, p1.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 50 }, Wagons: 0, X: 800, Y: 500), now, 0);
            world.Apply(p2.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, p2.Village.Id,
                Formation: new Dictionary<string, int> { ["greeks-infantry"] = 450 }, Wagons: 0, X: 800, Y: 500), now, 0);
            var p1FieldId = world.Snapshot(p1.Village.OwnerId).Armies.Single(a => a.OwnerId == p1.Village.OwnerId).Id;
            var p2FieldId = world.Snapshot(p2.Village.OwnerId).Armies.Single(a => a.OwnerId == p2.Village.OwnerId).Id;
            world.Apply(p1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.AttackArmy, p1FieldId, DestinationId: enemyArmyId), now, 0);
            world.Apply(p2.Village.OwnerId, new(Guid.NewGuid(), CommandKind.AttackArmy, p2FieldId, DestinationId: enemyArmyId), now, 0);
            for (var i = 0; i < 200 && !world.Snapshot(p2.Village.OwnerId).Warfare.Battles.Any(b => b.Status == "Active" && b.AttackerArmyIds.Length >= 2); i++)
            { now += .25; world.Advance(.25, now, 0, 3600); }
            Check(world.Snapshot(p2.Village.OwnerId).Warfare.Battles.Any(b => b.Status == "Active" && b.AttackerArmyIds.Length >= 2),
                "Multi-army allied battle is active before elimination");
            // Raider conquers p1's sole village -> triggers Rehome(p1, p1, null, now)
            world.Apply(raider.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, raider.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 150, ["romans-governor"] = 3, ["romans-wagon"] = 4 },
                Wagons: 0, DestinationId: p1.Village.Id, Objective: AttackObjective.Conquest), now, 0);
            for (var i = 0; i < 800 && p1.Village.OwnerId != raider.Village.OwnerId; i++) { now += .25; world.Advance(.25, now, 0, 3600); }
            var postSnap = world.Snapshot(p2.Village.OwnerId);
            var p1GoneFromBattle = !postSnap.Warfare.Battles.Any(b => b.AttackerArmyIds.Contains(p1FieldId));
            var battleContinuedOrAllyWon = postSnap.Warfare.Battles.Any(b => b.Status == "Active" && b.AttackerArmyIds.Contains(p2FieldId))
                || postSnap.Warfare.Battles.Any(b => b.Winner == p2.Village.OwnerId);
            Check(p1GoneFromBattle && battleContinuedOrAllyWon,
                "Eliminating p1 withdraws p1 without ghost forces and lets ally p2 finish the battle");

            // ResumeBattleWinner keeps distant Reinforcing order (no teleport to Guarding at oasis)
            var vOwner = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500));
            var vEnemy = new VillageEconomy(Guid.NewGuid(), TribeType.Germans, new(530, 500));
            var allyWorld = new LogisticsWorld([vOwner, vEnemy], 606);
            foreach (var c in new[] { vOwner, vEnemy })
            {
                var wh = c.Village.Buildings.Single(b => b.Type == BuildingType.Warehouse);
                while (wh.Level < 10) wh.CompleteUpgrade(wh.Level);
                c.AddResources(new(10000, 10000, 10000, 20000));
            }
            vOwner.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 200)]);
            vEnemy.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["germans-infantry"]), 5)]);
            var nearOasis = allyWorld.Snapshot(vOwner.Village.OwnerId).Oases.OrderBy(o => LogisticsWorld.Distance(o.Position, vOwner.Village.Position)).First();
            allyWorld.Apply(vOwner.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, vOwner.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 50 }, Wagons: 0, DestinationId: nearOasis.Id, AfterAttack: AfterAttack.Hold), 0, 0);
            now = 0d;
            for (var i = 0; i < 800 && allyWorld.Snapshot(vOwner.Village.OwnerId).Oases.Single(o => o.Id == nearOasis.Id).OwnerId != vOwner.Village.OwnerId; i++)
            { now += .25; allyWorld.Advance(.25, now, 0, 3600); }
            allyWorld.Apply(vOwner.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, vOwner.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 80 }, Wagons: 0, DestinationId: nearOasis.Id, Reinforcement: true), now, 0);
            var reinfId = allyWorld.Snapshot(vOwner.Village.OwnerId).Armies.First(a => a.Order == "Reinforcing").Id;
            var reinfPos = allyWorld.Snapshot(vOwner.Village.OwnerId).Armies.First(a => a.Id == reinfId).Position;
            allyWorld.Apply(vEnemy.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, vEnemy.Village.Id,
                Formation: new Dictionary<string, int> { ["germans-infantry"] = 5 }, Wagons: 0, X: reinfPos.X, Y: reinfPos.Y), now, 0);
            var smallEnemyId = allyWorld.Snapshot(vEnemy.Village.OwnerId).Armies.Single(a => a.OwnerId == vEnemy.Village.OwnerId).Id;
            allyWorld.Apply(vEnemy.Village.OwnerId, new(Guid.NewGuid(), CommandKind.AttackArmy, smallEnemyId, DestinationId: reinfId), now, 0);
            var verifiedReinforcingResume = false;
            for (var i = 0; i < 400; i++)
            {
                now += .25; allyWorld.Advance(.25, now, 0, 3600);
                var rArmy = allyWorld.Snapshot(vOwner.Village.OwnerId).Armies.FirstOrDefault(a => a.Id == reinfId);
                if (rArmy is not null && rArmy.Order != "Battle" && allyWorld.Snapshot(vOwner.Village.OwnerId).Warfare.Battles.Any(b => b.Winner == vOwner.Village.OwnerId))
                {
                    if (LogisticsWorld.Distance(rArmy.Position, nearOasis.Position) > 4)
                    {
                        Check(rArmy.Order == "Reinforcing", $"Winning battle en route keeps Reinforcing instead of Guarding/merging at distance (order={rArmy.Order})");
                        verifiedReinforcingResume = true;
                    }
                    break;
                }
            }
            Check(verifiedReinforcingResume, "Reinforcing army resumed Reinforcing without teleporting into Guarding");
        }

        // 11e. GameServer Create/Join/Spectate failure preserves existing room & Receipts evict above 4096 & deferred FinalBroadcastSent
        {
            var server = new GameServer(TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<GameServer>.Instance);
            var host = Guid.NewGuid();
            var switcher = Guid.NewGuid();
            server.Connect("c-host", host);
            server.Connect("c-switcher", switcher);
            server.EnableCheat(host, "aa123456");
            var room1 = server.Create(host, "Host", TribeType.Romans, "RoomOne", new(8, 30, 0, 1, 0, BotDifficulty.Easy, 20));
            server.Join(switcher, room1.Id, "Switcher", TribeType.Greeks);
            // Invalid Create (bad capacity = 4) must NOT kick switcher out of RoomOne!
            Reject(() => server.Create(switcher, "Switcher", TribeType.Romans, "BadRoom", new(4, 30, 0, 1, 0, BotDifficulty.Easy, 20)), "settings");
            Check(server.RoomIdFor(switcher) == room1.Id && server.Resume(switcher)?.Id == room1.Id,
                "Failed Create preserves caller's existing room membership");
            // Invalid Join must NOT kick switcher out of RoomOne!
            Reject(() => server.Join(switcher, "NOPE", "Switcher", TribeType.Romans), "not_found");
            Check(server.RoomIdFor(switcher) == room1.Id, "Failed Join preserves caller's existing room membership");
            // Exceeding 4096 receipts in Lobby/Running chat works without request_limit lockout
            for (var i = 0; i < 4105; i++)
            {
                var res = server.Execute(host, new(Guid.NewGuid(), CommandKind.SendChat, Guid.Empty, Message: $"m{i % 5}"));
                if (!res.Accepted && res.Code == "request_limit")
                    throw new Exception($"Hit request_limit lockout at command {i}");
            }
            checks++;
            // Deliveries(markFinalBroadcastSent: false) keeps Finished room until MarkFinalBroadcastSent runs
            var quick = server.Create(host, "Host", TribeType.Romans, "QuickDeferred", new(8, 30, 0, 1, 7, BotDifficulty.Easy, 1));
            server.Ready(host, true);
            server.Start(host);
            server.AdminSetSpeed(host, 20);
            for (var i = 0; i < 400 && server.ListRooms().Any(r => r.Id == quick.Id); i++)
                server.Tick(0.25);
            var deferred = server.Deliveries(markFinalBroadcastSent: false);
            Check(deferred.Any(d => d.Snapshot.Id == quick.Id && d.Snapshot.Status == RoomStatus.Finished),
                "Deferred delivery still produces final Finished snapshot");
            server.Tick(0.25);
            Check(server.Resume(host) is not null,
                "Finished room stays alive until transport confirms SendSnapshotsAsync completed");
            server.MarkFinalBroadcastSent([quick.Id]);
            server.Tick(0.25);
            Check(server.Resume(host) is null,
                "Finished room is cleaned up after MarkFinalBroadcastSent completes");
        }

        // 12. Medium backend gameplay & logic regression checks:
        {
            // 12a. TrainingRules matches units.json RequiredBuildingLevel across all units
            foreach (var u in GameCatalog.Units.Values)
            {
                if (u.Role == UnitRole.Scout) continue;
                Check(u.RequiredBuildingLevel == TrainingRules.RequiredLevel(u.Role),
                    $"TrainingRules matches units.json level for {u.UnitId}");
            }

            // 12b. Allied city/oasis/convoy targets & own-oasis Burn are rejected, and co-occupying allies don't siege each other
            var a1 = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500));
            var a2 = new VillageEconomy(Guid.NewGuid(), TribeType.Greeks, new(540, 500));
            var fo = new VillageEconomy(Guid.NewGuid(), TribeType.Germans, new(650, 500));
            var world = new LogisticsWorld([a1, a2, fo], 707);
            world.SetAlliances([new(a1.Village.OwnerId, a2.Village.OwnerId, 0)]);
            foreach (var c in new[] { a1, a2, fo })
            {
                var wh = c.Village.Buildings.Single(b => b.Type == BuildingType.Warehouse);
                while (wh.Level < 10) wh.CompleteUpgrade(wh.Level);
                c.AddResources(new(15000, 15000, 15000, 25000));
                c.ReceiveGift([
                    new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 120),
                    new(BaseUnit.Create(GameCatalog.Units["greeks-infantry"]), 120),
                    new(BaseUnit.Create(GameCatalog.Units["germans-infantry"]), 120),
                    new(BaseUnit.Create(GameCatalog.Units["romans-wagon"]), 10),
                    new(BaseUnit.Create(GameCatalog.Units["greeks-wagon"]), 10)
                ]);
            }
            Reject(() => world.Apply(a1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, a1.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 20 }, Wagons: 0, DestinationId: a2.Village.Id, Objective: AttackObjective.Raid), 0, 0), "ally");
            world.Apply(a1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, a1.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 30, ["romans-wagon"] = 2 }, Wagons: 0, X: 900, Y: 500), 0, 0);
            world.Apply(a2.Village.OwnerId, new(Guid.NewGuid(), CommandKind.SendResources, a2.Village.Id,
                DestinationId: a1.Village.Id, Shipment: new(100, 0, 0, 0)), 0, 0);
            var allyConvoyId = world.Snapshot(a1.Village.OwnerId).Convoys.Single(c => c.OwnerId == a2.Village.OwnerId).Id;
            var a1ArmyId = world.Snapshot(a1.Village.OwnerId).Armies.Single().Id;
            Reject(() => world.Apply(a1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.InterceptConvoy, a1ArmyId, DestinationId: allyConvoyId), 0, 0), "ally");
            Reject(() => world.Apply(a1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.BeginSiege, a1ArmyId, DestinationId: a2.Village.Id, Objective: AttackObjective.Raid), 0, 0), "ally");
            // Both allies capture the same unclaimed oasis
            var o1 = world.Snapshot(a1.Village.OwnerId).Oases.OrderBy(o => LogisticsWorld.Distance(o.Position, a1.Village.Position)).First();
            world.Apply(a1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.CaptureOasis, a1ArmyId, DestinationId: o1.Id, AfterAttack: AfterAttack.Hold), 0, 0);
            world.Apply(a2.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, a2.Village.Id,
                Formation: new Dictionary<string, int> { ["greeks-infantry"] = 30 }, Wagons: 0, DestinationId: o1.Id, AfterAttack: AfterAttack.Hold), 0, 0);
            var now = 0d;
            for (var i = 0; i < 600 && world.Snapshot(a1.Village.OwnerId).Oases.Single(o => o.Id == o1.Id).OwnerId is null; i++)
            { now += .25; world.Advance(.25, now, 0, 3600); }
            for (var i = 0; i < 100; i++) { now += .25; world.Advance(.25, now, 0, 3600); }
            Check(!world.Snapshot(a1.Village.OwnerId).Reports.Any(r => r.Kind == "OasisSiege"),
                "Co-occupying allies at the same oasis transition cleanly without besieging each other");
            var ownedOasis = world.Snapshot(a1.Village.OwnerId).Oases.Single(o => o.Id == o1.Id);
            var holder = ownedOasis.OwnerId == a1.Village.OwnerId ? a1 : a2;
            var partner = holder == a1 ? a2 : a1;
            var holderUnit = holder == a1 ? "romans-infantry" : "greeks-infantry";
            var partnerUnit = partner == a1 ? "romans-infantry" : "greeks-infantry";
            Reject(() => world.Apply(holder.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, holder.Village.Id,
                Formation: new Dictionary<string, int> { [holderUnit] = 10 }, Wagons: 0, DestinationId: o1.Id, OasisMode: OasisMode.Burn), now, 0), "target");
            Reject(() => world.Apply(partner.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, partner.Village.Id,
                Formation: new Dictionary<string, int> { [partnerUnit] = 10 }, Wagons: 0, DestinationId: o1.Id, OasisMode: OasisMode.Burn), now, 0), "ally");
            Reject(() => world.Apply(partner.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, partner.Village.Id,
                Formation: new Dictionary<string, int> { [partnerUnit] = 10 }, Wagons: 0, DestinationId: o1.Id, OasisMode: OasisMode.Capture), now, 0), "ally");

            // 12c. Splitting a Merging army preserves MergeVillageId on leftBehind so it finishes merging cleanly
            world.Apply(a1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, a1.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 40, ["romans-wagon"] = 2 }, Wagons: 0, X: 900, Y: 500), now, 0);
            var mergeSrc = world.Snapshot(a1.Village.OwnerId).Armies.Last(a => a.OwnerId == a1.Village.OwnerId && a.Order == "Marching").Id;
            for (var i = 0; i < 40; i++) { now += .25; world.Advance(.25, now, 0, 3600); }
            world.Apply(a1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.MergeArmies, a1.Village.Id, TargetIds: [mergeSrc]), now, 0);
            // Split half out while Merging
            world.Apply(a1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.MoveArmy, mergeSrc,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 20, ["romans-wagon"] = 1 }, X: 950, Y: 500), now, 0);
            var leftBehindArmy = world.Snapshot(a1.Village.OwnerId).Armies.Single(a => a.OwnerId == a1.Village.OwnerId && a.Order == "Merging" && a.Id != mergeSrc);
            for (var i = 0; i < 400 && world.Snapshot(a1.Village.OwnerId).Armies.Any(a => a.Id == leftBehindArmy.Id); i++)
            { now += .25; world.Advance(.25, now, 0, 3600); }
            Check(!world.Snapshot(a1.Village.OwnerId).Armies.Any(a => a.Id == leftBehindArmy.Id),
                "Left-behind split from Merging army keeps MergeVillageId and completes village merge");

            // 12d. DisbandTroops removing all fighters from an Attacking army routes surviving wagons home
            world.Apply(fo.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, fo.Village.Id,
                Formation: new Dictionary<string, int> { ["germans-infantry"] = 20 }, Wagons: 0, X: 1200, Y: 500), now, 0);
            var foTargetId = world.Snapshot(fo.Village.OwnerId).Armies.Last(a => a.OwnerId == fo.Village.OwnerId).Id;
            world.Apply(a1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, a1.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 20, ["romans-wagon"] = 1 }, Wagons: 0,
                X: 1200, Y: 500), now, 0);
            var atkArmyId = world.Snapshot(a1.Village.OwnerId).Armies.Last(a => a.OwnerId == a1.Village.OwnerId && a.Order == "Marching").Id;
            world.Apply(a1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.AttackArmy, atkArmyId, DestinationId: foTargetId), now, 0);
            world.Apply(a1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DisbandTroops, atkArmyId,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 20 }), now, 0);
            var disbandedAtk = world.Snapshot(a1.Village.OwnerId).Armies.Single(a => a.Id == atkArmyId);
            Check(disbandedAtk.Order == "Returning",
                $"Attacking army reduced to wagons via DisbandTroops routes home cleanly (order={disbandedAtk.Order})");

            // 12e. Burn -> Recall resets oasis.BurningSeconds (BurningProgress) to 0 immediately
            var foArmyBurn = world.Snapshot(fo.Village.OwnerId).Armies.Last(a => a.OwnerId == fo.Village.OwnerId);
            var o2 = world.Snapshot(fo.Village.OwnerId).Oases.Where(o => o.Id != o1.Id && o.OwnerId is null)
                .OrderBy(o => LogisticsWorld.Distance(o.Position, foArmyBurn.Position)).First();
            world.Apply(fo.Village.OwnerId, new(Guid.NewGuid(), CommandKind.BurnOasis, foArmyBurn.Id, DestinationId: o2.Id), now, 0);
            for (var i = 0; i < 1600 && !(world.Snapshot(fo.Village.OwnerId).Oases.Single(o => o.Id == o2.Id).BurningProgress > 0); i++)
            { now += .25; world.Advance(.25, now, 0, 3600); }
            Check(world.Snapshot(fo.Village.OwnerId).Oases.Single(o => o.Id == o2.Id).BurningProgress > 0,
                "Burning oasis accrues BurningProgress");
            world.Apply(fo.Village.OwnerId, new(Guid.NewGuid(), CommandKind.RecallArmy, foArmyBurn.Id), now, 0);
            Check(world.Snapshot(fo.Village.OwnerId).Oases.Single(o => o.Id == o2.Id).BurningProgress == 0,
                "Recalling burning army resets oasis BurningProgress to 0");
        }

        // 13. Selection-circle collision battle engagement:
        {
            var c1 = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500));
            var c2 = new VillageEconomy(Guid.NewGuid(), TribeType.Germans, new(1500, 500));
            var c3 = new VillageEconomy(Guid.NewGuid(), TribeType.Greeks, new(500, 1100));
            var world = new LogisticsWorld([c1, c2, c3], 808);
            foreach (var c in new[] { c1, c2, c3 })
            {
                var wh = c.Village.Buildings.Single(b => b.Type == BuildingType.Warehouse);
                while (wh.Level < 10) wh.CompleteUpgrade(wh.Level);
                c.AddResources(new(15000, 15000, 15000, 25000));
                c.ReceiveGift([
                    new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 200),
                    new(BaseUnit.Create(GameCatalog.Units["germans-infantry"]), 200),
                    new(BaseUnit.Create(GameCatalog.Units["greeks-infantry"]), 200),
                    new(BaseUnit.Create(GameCatalog.Units["romans-wagon"]), 10),
                    new(BaseUnit.Create(GameCatalog.Units["germans-wagon"]), 10),
                    new(BaseUnit.Create(GameCatalog.Units["greeks-wagon"]), 10)
                ]);
            }

            // 13a. Small armies start battle when selection ellipses collide (centers remain well over 50 units apart)
            world.Apply(c1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, c1.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 10 }, Wagons: 0, X: 950, Y: 500), 0, 0);
            world.Apply(c2.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, c2.Village.Id,
                Formation: new Dictionary<string, int> { ["germans-infantry"] = 10 }, Wagons: 0, X: 1250, Y: 500), 0, 0);
            var now = 0d;
            for (var i = 0; i < 400; i++)
            {
                now += .25;
                world.Advance(.25, now, 0, 3600);
                var s = world.Snapshot(c1.Village.OwnerId, omniscient: true);
                if (s.Armies.Length == 2 && s.Armies.All(a => a.Order == "Idle")) break;
            }
            var smallAttId = world.Snapshot(c1.Village.OwnerId).Armies.Single(a => a.OwnerId == c1.Village.OwnerId).Id;
            var smallDefId = world.Snapshot(c2.Village.OwnerId).Armies.Single(a => a.OwnerId == c2.Village.OwnerId).Id;
            world.Apply(c1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.AttackArmy, smallAttId, DestinationId: smallDefId), now, 0);
            now += .25;
            world.Advance(.25, now, 0, 3600);
            var beforeSmall = world.Snapshot(c1.Village.OwnerId, omniscient: true);
            Check(beforeSmall.Armies.Single(a => a.Id == smallAttId).Order == "Attacking",
                "Small army outside selection ellipse stays Attacking without premature battle");
            var smallThreat = world.Snapshot(c2.Village.OwnerId).Threats.Single(t => t.ArmyId == smallAttId);
            Check(smallThreat.ArrivesAt > now, "IncomingThreat reports future ETA while attacker approaches selection circle");

            double smallEngageDist = 0;
            for (var i = 0; i < 400; i++)
            {
                now += .25;
                world.Advance(.25, now, 0, 3600);
                var s = world.Snapshot(c1.Village.OwnerId, omniscient: true);
                var a = s.Armies.Single(x => x.Id == smallAttId);
                if (a.Order == "Battle")
                {
                    var d = s.Armies.Single(x => x.Id == smallDefId);
                    smallEngageDist = LogisticsWorld.Distance(a.Position, d.Position);
                    break;
                }
            }
            Check(smallEngageDist > 50, $"Small armies engage when selection circles collide (dist={smallEngageDist:0.1})");

            // 13b. Larger formations collide at a strictly wider center distance matching their larger selection circles
            world.Apply(c1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, c1.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 120, ["romans-wagon"] = 2 }, Wagons: 0, X: 800, Y: 800), now, 0);
            world.Apply(c2.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, c2.Village.Id,
                Formation: new Dictionary<string, int> { ["germans-infantry"] = 120, ["germans-wagon"] = 2 }, Wagons: 0, X: 1300, Y: 800), now, 0);
            for (var i = 0; i < 600; i++)
            {
                now += .25;
                world.Advance(.25, now, 0, 3600);
                var s = world.Snapshot(c1.Village.OwnerId, omniscient: true);
                var a = s.Armies.FirstOrDefault(x => x.OwnerId == c1.Village.OwnerId && x.Soldiers >= 100);
                var d = s.Armies.FirstOrDefault(x => x.OwnerId == c2.Village.OwnerId && x.Soldiers >= 100);
                if (a is not null && d is not null && a.Order == "Idle" && d.Order == "Idle") break;
            }
            var bigAttId = world.Snapshot(c1.Village.OwnerId).Armies.Single(a => a.OwnerId == c1.Village.OwnerId && a.Soldiers >= 100).Id;
            var bigDefId = world.Snapshot(c2.Village.OwnerId).Armies.Single(a => a.OwnerId == c2.Village.OwnerId && a.Soldiers >= 100).Id;
            world.Apply(c1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.AttackArmy, bigAttId, DestinationId: bigDefId), now, 0);
            double bigEngageDist = 0;
            for (var i = 0; i < 600; i++)
            {
                now += .25;
                world.Advance(.25, now, 0, 3600);
                var s = world.Snapshot(c1.Village.OwnerId, omniscient: true);
                var a = s.Armies.Single(x => x.Id == bigAttId);
                if (a.Order == "Battle")
                {
                    var d = s.Armies.Single(x => x.Id == bigDefId);
                    bigEngageDist = LogisticsWorld.Distance(a.Position, d.Position);
                    break;
                }
            }
            Check(bigEngageDist > smallEngageDist + 30,
                $"Larger formations engage farther apart on selection circle contact (big={bigEngageDist:0.1}, small={smallEngageDist:0.1})");

            // 13c. Convoy interception starts when hunter selection circle collides with convoy selection circle (> 12 center distance)
            world.Apply(c1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, c1.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 60 }, Wagons: 0, X: 950, Y: 1100), now, 0);
            for (var i = 0; i < 600; i++)
            {
                now += .25;
                world.Advance(.25, now, 0, 3600);
                if (world.Snapshot(c1.Village.OwnerId).Armies.Any(a => a.OwnerId == c1.Village.OwnerId && a.Order == "Idle" && Math.Abs(a.Position.Y - 1100) < 2))
                    break;
            }
            var hunterId = world.Snapshot(c1.Village.OwnerId).Armies.Single(a => a.OwnerId == c1.Village.OwnerId && a.Order == "Idle" && Math.Abs(a.Position.Y - 1100) < 2).Id;
            world.Apply(c2.Village.OwnerId, new(Guid.NewGuid(), CommandKind.SendResources, c2.Village.Id,
                DestinationId: c3.Village.Id, Shipment: new(1500, 0, 0, 0)), now, 0);
            var convoyId = world.Snapshot(c2.Village.OwnerId).Convoys.Single(c => c.Kind == ConvoyKind.Shipment).Id;
            world.Apply(c1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.InterceptConvoy, hunterId, DestinationId: convoyId), now, 0);
            double interceptDist = 0;
            for (var i = 0; i < 400; i++)
            {
                now += .25;
                world.Advance(.25, now, 0, 3600);
                var s = world.Snapshot(c1.Village.OwnerId, omniscient: true);
                var convoy = s.Convoys.FirstOrDefault(c => c.Id == convoyId);
                if (convoy is not null && convoy.InBattle)
                {
                    var hunter = s.Armies.Single(a => a.Id == hunterId);
                    interceptDist = LogisticsWorld.Distance(hunter.Position, convoy.Position);
                    break;
                }
            }
            Check(interceptDist > 20, $"Convoy interception begins on selection circle collision (dist={interceptDist:0.1})");

            // 13d. Village siege starts once the attacking army steps slightly inside the village circle (well outside center)
            c1.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 150)]);
            c3.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["greeks-infantry"]), 100)]);
            world.Apply(c1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, c1.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 60 }, Wagons: 0,
                DestinationId: c3.Village.Id, Objective: AttackObjective.Raid), now, 0);
            var citySiegerId = world.Snapshot(c1.Village.OwnerId).Armies
                .Single(a => a.OwnerId == c1.Village.OwnerId && a.Order == "Approaching" && a.TargetId == c3.Village.Id).Id;
            var cityThreat = world.Snapshot(c3.Village.OwnerId).Threats.Single(t => t.ArmyId == citySiegerId);
            Check(cityThreat.ArrivesAt > now, "Village threat reports future ETA before reaching village perimeter");
            double cityEngageDist = 0;
            for (var i = 0; i < 800; i++)
            {
                now += .25;
                world.Advance(.25, now, 0, 3600);
                var s = world.Snapshot(c1.Village.OwnerId, omniscient: true);
                var a = s.Armies.Single(x => x.Id == citySiegerId);
                if (a.Order is "Sieging" or "Battle")
                {
                    cityEngageDist = LogisticsWorld.Distance(a.Position, c3.Village.Position);
                    break;
                }
            }
            Check(cityEngageDist is > 40 and < 78,
                $"Village battle/siege starts slightly inside the village circle (dist={cityEngageDist:0.1})");

            // 13e. Oasis battle/burn starts slightly inside the oasis circle and stops advancing inward
            var targetOasis = world.Snapshot(c3.Village.OwnerId).Oases
                .Where(o => o.OwnerId is null)
                .OrderBy(o => LogisticsWorld.Distance(o.Position, c1.Village.Position))
                .First();
            world.Apply(c1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, c1.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 60 }, Wagons: 0,
                DestinationId: targetOasis.Id, OasisMode: OasisMode.Burn,
                X: targetOasis.Position.X, Y: targetOasis.Position.Y), now, 0);
            var oasisBurnerId = world.Snapshot(c1.Village.OwnerId).Armies
                .Single(a => a.OwnerId == c1.Village.OwnerId && a.TargetId == targetOasis.Id && a.Order == "Occupying").Id;
            double oasisEngageDist = 0;
            Position? engagePos = null;
            for (var i = 0; i < 2000; i++)
            {
                now += .25;
                world.Advance(.25, now, 0, 3600);
                var oState = world.Snapshot(c1.Village.OwnerId, omniscient: true).Oases.Single(o => o.Id == targetOasis.Id);
                var burner = world.Snapshot(c1.Village.OwnerId, omniscient: true).Armies.Single(a => a.Id == oasisBurnerId);
                if (oState.BurningProgress > 0 && engagePos is null)
                {
                    oasisEngageDist = LogisticsWorld.Distance(burner.Position, targetOasis.Position);
                    engagePos = burner.Position;
                }
                else if (engagePos is not null)
                {
                    Check(LogisticsWorld.Distance(burner.Position, engagePos.Value) < 0.01,
                        "Oasis-engaging army holds position on the oasis circle instead of walking to the center");
                    break;
                }
            }
            Check(oasisEngageDist is > 28 and < 65,
                $"Oasis action begins slightly inside the oasis circle (dist={oasisEngageDist:0.1})");
        }

        // 14. Nearby allied (and own idle) armies never auto-join battles; players and bots join manually as support.
        {
            var friend = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500));
            var defenderOwner = new VillageEconomy(Guid.NewGuid(), TribeType.Greeks, new(500, 620));
            var botAlly = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 740));
            var enemy = new VillageEconomy(Guid.NewGuid(), TribeType.Germans, new(750, 560));
            var world = new LogisticsWorld([friend, defenderOwner, botAlly, enemy], 808);
            world.SetAlliances([
                new(friend.Village.OwnerId, defenderOwner.Village.OwnerId, 0),
                new(friend.Village.OwnerId, botAlly.Village.OwnerId, 0),
                new(defenderOwner.Village.OwnerId, botAlly.Village.OwnerId, 0)
            ]);
            foreach (var c in new[] { friend, defenderOwner, botAlly, enemy })
            {
                c.AddResources(new(10000, 10000, 10000, 15000));
                c.ReceiveGift([
                    new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 120),
                    new(BaseUnit.Create(GameCatalog.Units["greeks-infantry"]), 120),
                    new(BaseUnit.Create(GameCatalog.Units["germans-infantry"]), 120),
                    new(BaseUnit.Create(GameCatalog.Units["romans-wagon"]), 4)
                ]);
            }

            // Place friend's attacker, defenderOwner's defensive army, and botAlly's army right at the same battle point (620, 560),
            // plus a second idle army owned by friend right next to them.
            world.Apply(friend.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, friend.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 50 }, Wagons: 0, X: 620, Y: 560), 0, 0);
            world.Apply(friend.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, friend.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 25 }, Wagons: 0, X: 625, Y: 560), 0, 0);
            world.Apply(defenderOwner.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, defenderOwner.Village.Id,
                Formation: new Dictionary<string, int> { ["greeks-infantry"] = 60 }, Wagons: 0, X: 620, Y: 565), 0, 0);
            world.Apply(botAlly.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, botAlly.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 40 }, Wagons: 0, X: 615, Y: 560), 0, 0);
            world.Apply(enemy.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, enemy.Village.Id,
                Formation: new Dictionary<string, int> { ["germans-infantry"] = 100 }, Wagons: 0, X: 640, Y: 560), 0, 0);

            var friendAtk = world.Snapshot(friend.Village.OwnerId).Armies.Where(a => a.OwnerId == friend.Village.OwnerId).First().Id;
            var friendReserve = world.Snapshot(friend.Village.OwnerId).Armies.Where(a => a.OwnerId == friend.Village.OwnerId).Last().Id;
            var defArmy = world.Snapshot(defenderOwner.Village.OwnerId).Armies.Single(a => a.OwnerId == defenderOwner.Village.OwnerId).Id;
            var botArmy = world.Snapshot(botAlly.Village.OwnerId).Armies.Single(a => a.OwnerId == botAlly.Village.OwnerId).Id;
            var enemyArmy = world.Snapshot(enemy.Village.OwnerId).Armies.Single(a => a.OwnerId == enemy.Village.OwnerId).Id;

            double now = 0;
            for (var i = 0; i < 160 && world.Snapshot(friend.Village.OwnerId).Armies.Any(a => a.Order != "Idle"); i++)
            { now += .25; world.Advance(.25, now, 0, 3600); }

            // Friend attacks enemy right next to defenderOwner's defensive army, friendReserve, and botArmy.
            world.Apply(friend.Village.OwnerId, new(Guid.NewGuid(), CommandKind.AttackArmy, friendAtk, DestinationId: enemyArmy), now, 0);
            for (var i = 0; i < 60 && !world.Snapshot(friend.Village.OwnerId).Warfare.Battles.Any(b => b.Status == "Active"); i++)
            { now += .25; world.Advance(.25, now, 0, 3600); }
            for (var i = 0; i < 8; i++) { now += .25; world.Advance(.25, now, 0, 3600); }

            var activeBattle = world.Snapshot(friend.Village.OwnerId).Warfare.Battles.Single(b => b.Status == "Active");
            Check(activeBattle.AttackerArmyIds.SequenceEqual([friendAtk]) && activeBattle.DefenderArmyIds.SequenceEqual([enemyArmy]),
                "Nearby allied defensive armies and own idle reserves do NOT auto-join a nearby battle");
            Check(world.Snapshot(defenderOwner.Village.OwnerId).Armies.Single(a => a.Id == defArmy).Order == "Idle"
                && world.Snapshot(friend.Village.OwnerId).Armies.Single(a => a.Id == friendReserve).Order == "Idle"
                && world.Snapshot(botAlly.Village.OwnerId).Armies.Single(a => a.Id == botArmy).Order == "Idle",
                "Uncommanded nearby allied and reserve armies keep Idle status during nearby combat");

            // Player manually orders defArmy to join as support, and bot manually joins via BotAct.
            world.Apply(defenderOwner.Village.OwnerId, new(Guid.NewGuid(), CommandKind.AttackArmy, defArmy, DestinationId: enemyArmy), now, 0);
            var botCmds = new WorldTestCommands(world, () => now);
            world.BotAct(botAlly.Village.OwnerId, botCmds, BotPersonality.Defensive, BotDifficulty.Normal);
            for (var i = 0; i < 40; i++) { now += .25; world.Advance(.25, now, 0, 3600); }

            var joinedBattle = world.Snapshot(friend.Village.OwnerId).Warfare.Battles.Single(b => b.Id == activeBattle.Id);
            Check(joinedBattle.AttackerArmyIds.Contains(defArmy) && joinedBattle.AttackerArmyIds.Contains(botArmy)
                && !joinedBattle.AttackerArmyIds.Contains(friendReserve),
                "Manually commanded player and bot allies join the active battle while uncommanded reserve stays out");
        }

        return checks;
    }

    private sealed class WorldTestCommands(LogisticsWorld world, Func<double>? getNow = null) : IGameCommands
    {
        public CommandResult Execute(Guid actorId, GameCommand command)
        {
            try
            {
                world.Apply(actorId, command, getNow?.Invoke() ?? 1800, 0);
                return new CommandResult(command.RequestId, true, "ok", "ok");
            }
            catch (GameRuleException ex)
            {
                return new CommandResult(command.RequestId, false, ex.Code, ex.Message);
            }
        }
    }
}
