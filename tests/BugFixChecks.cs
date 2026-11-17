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
