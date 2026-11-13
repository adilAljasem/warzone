using Warzone;

internal static class BotCooperationChecks
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }

        // 1. Reduced caravan spam & larger trade quantities per caravan
        {
            var host = Guid.NewGuid();
            var room = new GameRoom(host, "Host", TribeType.Romans, "TradeTest",
                new(Capacity: 8, DurationMinutes: 30, ShieldMinutes: 0, ResourceSpeed: 3, BotCount: 7, BotDifficulty: BotDifficulty.Hard),
                DateTimeOffset.UtcNow);
            room.SetReady(host, true);
            room.Start(host, _ => true);

            var maxSimultaneousTrades = 0;
            var tradeCargoSamples = new List<long>();
            var seenTradeIds = new HashSet<Guid>();
            var maxTradesBySingleBot = 0;

            for (var step = 0; step < 3600; step++) // 15 minutes at 0.25s steps
            {
                room.Advance(.25);
                if (step % 8 == 0)
                {
                    var snap = room.Snapshot(host, _ => true);
                    var world = snap.World!;
                    var trades = world.Convoys.Where(c => c.Kind == ConvoyKind.Trade).ToArray();
                    maxSimultaneousTrades = Math.Max(maxSimultaneousTrades, trades.Length);
                    var perBot = trades.GroupBy(c => c.OwnerId).Select(g => g.Count()).DefaultIfEmpty(0).Max();
                    maxTradesBySingleBot = Math.Max(maxTradesBySingleBot, perBot);
                    foreach (var c in trades)
                    {
                        if (!c.Returning && seenTradeIds.Add(c.Id))
                        {
                            var ownerSnap = room.Snapshot(c.OwnerId, _ => true).World?.Convoys.FirstOrDefault(x => x.Id == c.Id);
                            if (ownerSnap?.Cargo is { Total: > 0 } cargo)
                                tradeCargoSamples.Add(cargo.Total);
                        }
                    }
                }
            }

            Check(maxSimultaneousTrades <= 21, $"Trade caravans stay bounded across 7 active bots (observed max {maxSimultaneousTrades})");
            Check(maxTradesBySingleBot <= 3, $"Each bot runs at most 3 outbound trade caravans at a time (observed max {maxTradesBySingleBot})");
            Check(tradeCargoSamples.Count > 0, "Bots still perform genuine resource trades");
            Check(tradeCargoSamples.Average() >= 1200, $"Bot trade caravans carry large bulk quantities (observed avg {tradeCargoSamples.Average():0})");
        }

        // 2. Team awareness, cross-team alliance rejection, coordinated focus target, reinforcements, oasis burning & razing on Hard
        {
            var host = Guid.NewGuid();
            var room = new GameRoom(host, "Host", TribeType.Romans, "TeamWar",
                new(Capacity: 8, DurationMinutes: 30, ShieldMinutes: 0, ResourceSpeed: 4, BotCount: 5, BotDifficulty: BotDifficulty.Hard),
                DateTimeOffset.UtcNow);

            var lobby = room.Snapshot(host, _ => true);
            var bots = lobby.Players.Where(p => p.IsBot).ToArray();
            Check(bots.Length == 5, "Five bots present in 6-player team match");

            // Team 1: Host + Bot 0 + Bot 1
            // Team 2: Bot 2 + Bot 3 + Bot 4
            room.ChangeTeam(host, host, 1);
            room.ChangeTeam(host, bots[0].Id, 1);
            room.ChangeTeam(host, bots[1].Id, 1);
            room.ChangeTeam(host, bots[2].Id, 2);
            room.ChangeTeam(host, bots[3].Id, 2);
            room.ChangeTeam(host, bots[4].Id, 2);
            room.SetReady(host, true);
            room.Start(host, _ => true);

            var startSnap = room.Snapshot(host, _ => true);
            Check(room.IsAllied(host, bots[0].Id) && room.IsAllied(bots[0].Id, bots[1].Id), "Team 1 players start allied");
            Check(room.IsAllied(bots[2].Id, bots[3].Id) && room.IsAllied(bots[3].Id, bots[4].Id), "Team 2 bots start allied");
            Check(!room.IsAllied(host, bots[2].Id) && !room.IsAllied(bots[0].Id, bots[2].Id), "Opposing teams start unallied");

            // Cross-team alliance request must be rejected by bots
            Check(room.Execute(host, new(Guid.NewGuid(), CommandKind.ProposeAlliance, bots[2].Id)).Accepted, "Cross-team proposal submitted");
            for (var i = 0; i < 40; i++) room.Advance(.25); // 10s so bot processes pending requests
            Check(!room.IsAllied(host, bots[2].Id), "Team 2 bot rejects cross-team alliance request");

            var observedRaze = false;
            var observedBlockade = false;
            var observedConquest = false;
            var observedBurnedOrBurningOasis = false;
            var observedReinforcementOrAlliedGuard = false;
            var observedMultiOwnerBattleOrSiege = false;
            var observedFocusConvergence = false;
            var attackedAlly = false;

            for (var step = 0; step < 4800; step++) // 20 minutes
            {
                room.Advance(.25);
                if (room.Status != RoomStatus.Running) break;
                if (step % 8 != 0) continue;

                var snap = room.Snapshot(host, _ => true);
                var world = snap.World!;
                var team1Ids = new HashSet<Guid> { host, bots[0].Id, bots[1].Id };
                var team2Ids = new HashSet<Guid> { bots[2].Id, bots[3].Id, bots[4].Id };

                // Ensure bots never besiege or burn an ally's settlement/oasis
                foreach (var s in world.Warfare.Sieges)
                {
                    if ((team1Ids.Contains(s.AttackerOwner) && team1Ids.Contains(s.DefenderOwner))
                        || (team2Ids.Contains(s.AttackerOwner) && team2Ids.Contains(s.DefenderOwner)))
                        attackedAlly = true;

                    if (s.Objective == AttackObjective.Raze) observedRaze = true;
                    if (s.Objective == AttackObjective.Blockade) observedBlockade = true;
                    if (s.Objective == AttackObjective.Conquest) observedConquest = true;

                    if (s.ArmyIds.Length >= 2)
                        observedMultiOwnerBattleOrSiege = true;
                }

                if (world.Oases.Any(o => o.BurnedRemainingSeconds > 0 || o.BurningProgress > 0)
                    || world.OasisSieges.Any(os => os.Mode is OasisMode.Burn or OasisMode.Blockade))
                    observedBurnedOrBurningOasis = true;

                // Inspect internal world snapshots from a bot's POV to observe reinforcements & coordinated marches
                foreach (var b in bots)
                {
                    var bWorld = room.Snapshot(b.Id, _ => true).World;
                    if (bWorld is null) continue;
                    if (bWorld.Armies.Any(a => a.OwnerId == b.Id && a.Order is "Reinforcing" or "Guarding" && a.SupportedOwnerId is { } s && s != b.Id))
                        observedReinforcementOrAlliedGuard = true;
                }

                // Check if two team-2 bots are simultaneously marching/sieging the same team-1 target, or vice-versa
                var t2SiegeTargets = bots.Skip(2).SelectMany(b => room.Snapshot(b.Id, _ => true).World?.Armies ?? [])
                    .Where(a => a.Order is "Approaching" or "Sieging" && a.TargetId is not null)
                    .GroupBy(a => a.TargetId!.Value)
                    .Any(g => g.Select(a => a.OwnerId).Distinct().Count() >= 2);
                var t1SiegeTargets = bots.Take(2).SelectMany(b => room.Snapshot(b.Id, _ => true).World?.Armies ?? [])
                    .Where(a => a.Order is "Approaching" or "Sieging" && a.TargetId is not null)
                    .GroupBy(a => a.TargetId!.Value)
                    .Any(g => g.Select(a => a.OwnerId).Distinct().Count() >= 2);
                if (t2SiegeTargets || t1SiegeTargets)
                    observedFocusConvergence = true;

                if (world.Warfare.Battles.Any(b => b.AttackerArmyIds.Length >= 2 || b.DefenderArmyIds.Length >= 2))
                    observedMultiOwnerBattleOrSiege = true;

                // Ensure bots never trade with opposing team
                var crossTeamTrade = world.Convoys.Any(c => c.Kind == ConvoyKind.Trade && c.RecipientId is { } rec
                    && ((team1Ids.Contains(c.OwnerId) && team2Ids.Contains(rec)) || (team2Ids.Contains(c.OwnerId) && team1Ids.Contains(rec))));
                Check(!crossTeamTrade, "Bots never execute trade caravans with opposing team enemies");
            }

            Check(!attackedAlly, "Teammate bots never siege an ally");
            Check(observedRaze || observedBlockade || observedConquest, "Hard bots execute strong city objectives (Raze/Blockade/Conquest)");
            Check(observedBurnedOrBurningOasis, "Hard bots burn or blockade enemy oases");
            Check(observedReinforcementOrAlliedGuard, "Team bots send reinforcements to allies");
            Check(observedFocusConvergence || observedMultiOwnerBattleOrSiege, "Team bots focus the same enemy and attack/fight together");
        }

        // 3. Direct unit check: allied siege escalation (Blockade -> Raze), joint siege, joint field battle, and Castle non-aggression
        {
            var a1 = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(1000, 1000));
            var a2 = new VillageEconomy(Guid.NewGuid(), TribeType.Greeks, new(1000, 1150));
            var enemy = new VillageEconomy(Guid.NewGuid(), TribeType.Germans, new(1400, 1075));
            var world = new LogisticsWorld([a1, a2, enemy], 77);
            world.SetAlliances([new(a1.Village.OwnerId, a2.Village.OwnerId, 0)]);
            double now = 0;
            void Step() { now += .25; world.Advance(.25, now, 0, 3600); }

            a1.AddResources(new(5000, 5000, 5000, 10000));
            a2.AddResources(new(5000, 5000, 5000, 10000));
            enemy.AddResources(new(5000, 5000, 5000, 10000));
            a1.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 60)]);
            a2.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["greeks-infantry"]), 60), new(BaseUnit.Create(GameCatalog.Units["greeks-catapult"]), 2)]);
            enemy.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["germans-infantry"]), 50)]);

            // 3a. Ally 1 starts a Blockade siege; Ally 2 arrives with Catapults and escalates it to Raze with both armies
            world.Apply(a1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, a1.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 30 },
                DestinationId: enemy.Village.Id, X: enemy.Village.Position.X, Y: enemy.Village.Position.Y, Objective: AttackObjective.Blockade), now, 0);
            for (var i = 0; i < 400 && !enemy.Besieged; i++) Step();
            Check(enemy.Besieged && world.Snapshot(a1.Village.OwnerId).Warfare.Sieges.Single().Objective == AttackObjective.Blockade, "First ally establishes Blockade siege on enemy");

            world.Apply(a2.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, a2.Village.Id,
                Formation: new Dictionary<string, int> { ["greeks-infantry"] = 30, ["greeks-catapult"] = 2 },
                DestinationId: enemy.Village.Id, X: enemy.Village.Position.X, Y: enemy.Village.Position.Y, Objective: AttackObjective.Raze), now, 0);
            for (var i = 0; i < 400 && (world.Snapshot(a1.Village.OwnerId).Warfare.Sieges.SingleOrDefault()?.ArmyIds.Length ?? 0) < 2; i++) Step();

            var siege = world.Snapshot(a1.Village.OwnerId).Warfare.Sieges.SingleOrDefault();
            Check(siege is not null && siege.ArmyIds.Length == 2 && siege.Objective == AttackObjective.Raze,
                "Second allied commander joins active Blockade siege and escalates it to Raze");

            // 3b. Two allied armies join the same side in an active field battle
            world.Apply(enemy.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, enemy.Village.Id,
                Formation: new Dictionary<string, int> { ["germans-infantry"] = 40 },
                X: 1200, Y: 1075), now, 0);
            world.Apply(a1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, a1.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 25 },
                X: 1200, Y: 1075), now, 0);
            world.Apply(a2.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, a2.Village.Id,
                Formation: new Dictionary<string, int> { ["greeks-infantry"] = 25 },
                X: 1200, Y: 1075), now, 0);
            var enemyFieldArmy = world.Snapshot(enemy.Village.OwnerId).Armies.Single(a => a.OwnerId == enemy.Village.OwnerId).Id;
            var a1FieldArmy = world.Snapshot(a1.Village.OwnerId).Armies.Last(a => a.OwnerId == a1.Village.OwnerId).Id;
            var a2FieldArmy = world.Snapshot(a2.Village.OwnerId).Armies.Last(a => a.OwnerId == a2.Village.OwnerId).Id;
            world.Apply(a1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.AttackArmy, a1FieldArmy, DestinationId: enemyFieldArmy), now, 0);
            world.Apply(a2.Village.OwnerId, new(Guid.NewGuid(), CommandKind.AttackArmy, a2FieldArmy, DestinationId: enemyFieldArmy), now, 0);
            for (var i = 0; i < 400 && (world.Snapshot(a1.Village.OwnerId).Warfare.Battles.FirstOrDefault()?.AttackerArmyIds.Length ?? 0) < 2; i++) Step();
            var jointBattle = world.Snapshot(a1.Village.OwnerId).Warfare.Battles.FirstOrDefault(b => b.AttackerArmyIds.Contains(a1FieldArmy));
            Check(jointBattle is not null && jointBattle.AttackerArmyIds.Contains(a2FieldArmy), "Allied commanders fight on the same side in field battles");

            // 3c. Allied commanders standing together at the Central Castle do not fight each other
            var c1 = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(1850, 2250));
            var c2 = new VillageEconomy(Guid.NewGuid(), TribeType.Greeks, new(1850, 2350));
            var castleWorld = new LogisticsWorld([c1, c2], 78);
            castleWorld.SetAlliances([new(c1.Village.OwnerId, c2.Village.OwnerId, 0)]);
            double cNow = 1805;
            c1.AddResources(new(5000, 5000, 5000, 5000));
            c2.AddResources(new(5000, 5000, 5000, 5000));
            c1.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 15), new(BaseUnit.Create(GameCatalog.Units["romans-wagon"]), 2)]);
            c2.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["greeks-infantry"]), 15), new(BaseUnit.Create(GameCatalog.Units["greeks-wagon"]), 2)]);
            castleWorld.Apply(c1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, c1.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 15, ["romans-wagon"] = 2 },
                X: LogisticsWorld.MapCenter, Y: LogisticsWorld.MapCenter), cNow, 0);
            castleWorld.Apply(c2.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, c2.Village.Id,
                Formation: new Dictionary<string, int> { ["greeks-infantry"] = 15, ["greeks-wagon"] = 2 },
                X: LogisticsWorld.MapCenter, Y: LogisticsWorld.MapCenter), cNow, 0);
            for (var i = 0; i < 600; i++) { cNow += .25; castleWorld.Advance(.25, cNow, 0, 3600); }
            var castleSnap = castleWorld.Snapshot(c1.Village.OwnerId).Warfare;
            Check(!castleSnap.Castle.Contested && castleSnap.Castle.ControllerId is not null && !castleSnap.Battles.Any(),
                "Allied armies at Crown Keep hold the castle together without fighting each other");
        }

        // 4. Direct castle dispatch + hold timer pauses on contest and only resets when a new rival captures the castle
        {
            var holder = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(2050, 2250));
            var weakChallenger = new VillageEconomy(Guid.NewGuid(), TribeType.Greeks, new(2450, 2250));
            var strongChallenger = new VillageEconomy(Guid.NewGuid(), TribeType.Germans, new(2250, 2050));
            var w = new LogisticsWorld([holder, weakChallenger, strongChallenger], 79);
            double t = 1805;
            void StepW() { t += .25; w.Advance(.25, t, 0, 3600); }
            StepW();

            var keepId = w.Snapshot(holder.Village.OwnerId).Warfare.Castle.Id;
            holder.AddResources(new(5000, 5000, 5000, 15000));
            weakChallenger.AddResources(new(5000, 5000, 5000, 15000));
            strongChallenger.AddResources(new(5000, 5000, 5000, 15000));
            holder.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["romans-swordsman"]), 50), new(BaseUnit.Create(GameCatalog.Units["romans-wagon"]), 3)]);
            weakChallenger.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["greeks-infantry"]), 12), new(BaseUnit.Create(GameCatalog.Units["greeks-wagon"]), 1)]);
            strongChallenger.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["germans-heavycavalry"]), 120), new(BaseUnit.Create(GameCatalog.Units["germans-wagon"]), 5)]);

            // Direct DispatchArmy to castle.Id
            w.Apply(holder.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, holder.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-swordsman"] = 50, ["romans-wagon"] = 3 },
                DestinationId: keepId, X: LogisticsWorld.MapCenter, Y: LogisticsWorld.MapCenter), t, 0);
            Check(w.Snapshot(holder.Village.OwnerId).Armies.Single().Order == "Capturing", "Direct DispatchArmy to Crown Keep sets Capturing order");

            for (var i = 0; i < 400 && w.Snapshot(holder.Village.OwnerId).Warfare.Castle.CaptureProgress < 60; i++) StepW();
            var progressBeforeContest = w.Snapshot(holder.Village.OwnerId).Warfare.Castle.CaptureProgress;
            Check(progressBeforeContest >= 60 && w.Snapshot(holder.Village.OwnerId).Warfare.Castle.ControllerId == holder.Village.OwnerId,
                "Holder accumulates Crown Keep progress");

            // Weak challenger contests the castle: timer must pause (not reset to 0) and resume once weak challenger is defeated
            w.Apply(weakChallenger.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, weakChallenger.Village.Id,
                Formation: new Dictionary<string, int> { ["greeks-infantry"] = 12, ["greeks-wagon"] = 1 },
                DestinationId: keepId, X: LogisticsWorld.MapCenter, Y: LogisticsWorld.MapCenter), t, 0);
            for (var i = 0; i < 200 && !w.Snapshot(holder.Village.OwnerId).Warfare.Castle.Contested; i++) StepW();
            Check(w.Snapshot(holder.Village.OwnerId).Warfare.Castle.Contested, "Challenger triggers contested state");
            for (var i = 0; i < 16; i++) StepW();
            var duringContest = w.Snapshot(holder.Village.OwnerId).Warfare.Castle;
            Check(duringContest.CaptureProgress >= progressBeforeContest - 0.01 && duringContest.ControllerId == holder.Village.OwnerId,
                "Crown Keep progress is held (not reset) while a battle is in progress");

            // Wait for holder to defeat weak challenger and verify timer resumes from held progress
            for (var i = 0; i < 600 && w.Snapshot(holder.Village.OwnerId).Warfare.Castle.Contested; i++) StepW();
            for (var i = 0; i < 20; i++) StepW();
            var afterDefense = w.Snapshot(holder.Village.OwnerId).Warfare.Castle;
            Check(!afterDefense.Contested && afterDefense.ControllerId == holder.Village.OwnerId && afterDefense.CaptureProgress > progressBeforeContest + 2,
                "Winning defense resumes Crown Keep hold timer without resetting");

            // Strong challenger defeats holder and captures castle: timer resets for the new ruler
            w.Apply(strongChallenger.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, strongChallenger.Village.Id,
                Formation: new Dictionary<string, int> { ["germans-heavycavalry"] = 120, ["germans-wagon"] = 5 },
                DestinationId: keepId, X: LogisticsWorld.MapCenter, Y: LogisticsWorld.MapCenter), t, 0);
            for (var i = 0; i < 800 && w.Snapshot(holder.Village.OwnerId).Warfare.Castle.ControllerId != strongChallenger.Village.OwnerId; i++) StepW();
            var newRuler = w.Snapshot(holder.Village.OwnerId).Warfare.Castle;
            Check(newRuler.ControllerId == strongChallenger.Village.OwnerId && newRuler.CaptureProgress < 10,
                "Only when a rival actually defeats the holder and takes Crown Keep does the hold timer reset");
        }

        // 5. Unscouted enemy army shows only slot count (MapFigures keys) with null Formation, and scouting reveals Formation for 60s then expires
        {
            var scoutPlayer = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(2000, 2100));
            var enemyPlayer = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(2260, 2100));
            var sw = new LogisticsWorld([scoutPlayer, enemyPlayer], 81);
            double sNow = 100;
            void StepS() { sNow += .25; sw.Advance(.25, sNow, 0, 3600); }
            StepS();

            scoutPlayer.AddResources(new(5000, 5000, 5000, 20000));
            enemyPlayer.AddResources(new(5000, 5000, 5000, 20000));
            scoutPlayer.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["romans-scout"]), 5)]);
            enemyPlayer.ReceiveGift([
                new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 10),
                new(BaseUnit.Create(GameCatalog.Units["romans-spearman"]), 10),
                new(BaseUnit.Create(GameCatalog.Units["romans-swordsman"]), 10),
                new(BaseUnit.Create(GameCatalog.Units["romans-lightcavalry"]), 10),
                new(BaseUnit.Create(GameCatalog.Units["romans-heavycavalry"]), 10)
            ]);

            sw.Apply(enemyPlayer.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, enemyPlayer.Village.Id,
                Formation: new Dictionary<string, int>
                {
                    ["romans-infantry"] = 10,
                    ["romans-spearman"] = 10,
                    ["romans-swordsman"] = 10,
                    ["romans-lightcavalry"] = 10,
                    ["romans-heavycavalry"] = 10
                },
                Wagons: 0, Enabled: false, X: 2150, Y: 2100), sNow, 0);
            var targetArmyId = sw.Snapshot(enemyPlayer.Village.OwnerId).Armies.Single(a => a.OwnerId == enemyPlayer.Village.OwnerId).Id;

            var unscouted = sw.Snapshot(scoutPlayer.Village.OwnerId).Armies.Single(a => a.Id == targetArmyId);
            Check(unscouted.Formation is null && unscouted.ScoutedUntil is null && unscouted.MapFigures?.Count == 5,
                "Unscouted enemy army hides Formation/ScoutedUntil while exposing 5 slot keys in MapFigures");

            sw.Apply(scoutPlayer.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, scoutPlayer.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-scout"] = 5 },
                Wagons: 0, Enabled: false, X: 2040, Y: 2100), sNow, 0);
            var scoutArmyId = sw.Snapshot(scoutPlayer.Village.OwnerId).Armies.Single(a => a.OwnerId == scoutPlayer.Village.OwnerId).Id;
            sw.Apply(scoutPlayer.Village.OwnerId, new(Guid.NewGuid(), CommandKind.Scout, scoutArmyId, DestinationId: targetArmyId), sNow, 0);

            for (var i = 0; i < 120 && sw.Snapshot(scoutPlayer.Village.OwnerId).Armies.Single(a => a.Id == targetArmyId).Formation is null; i++) StepS();
            var scouted = sw.Snapshot(scoutPlayer.Village.OwnerId).Armies.Single(a => a.Id == targetArmyId);
            Check(scouted.Formation is { Count: 5 } && scouted.ScoutedUntil is { } until && until > sNow + 58 && until <= sNow + 60.5,
                "Scouting reveals enemy Formation and sets ~60s expiry");

            for (var i = 0; i < 120; i++) StepS(); // +30s
            var mid = sw.Snapshot(scoutPlayer.Village.OwnerId).Armies.Single(a => a.Id == targetArmyId);
            Check(mid.Formation is { Count: 5 } && mid.ScoutedUntil is not null,
                "Scouted enemy composition remains visible halfway through the 60s window");

            for (var i = 0; i < 130; i++) StepS(); // +32.5s (> 60s total since reveal)
            var expired = sw.Snapshot(scoutPlayer.Village.OwnerId).Armies.Single(a => a.Id == targetArmyId);
            Check(expired.Formation is null && expired.ScoutedUntil is null && expired.MapFigures?.Count == 5,
                "Scouted enemy composition expires after 60 seconds and reverts to unknown");
        }

        // 6. Bots must capture nearby enemy oases instead of burning them, and only burn oases very far from friendly cities
        {
            var bot = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(600, 2250));
            var enemy = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(3900, 2250));
            var ow = new LogisticsWorld([bot, enemy], 91);
            double oNow = 500;
            void StepO() { oNow += .25; ow.Advance(.25, oNow, 0, 3600); }
            StepO();

            bot.AddResources(new(50000, 50000, 50000, 100000));
            enemy.AddResources(new(50000, 50000, 50000, 100000));
            enemy.ReceiveGift([
                new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 350),
                new(BaseUnit.Create(GameCatalog.Units["romans-lightcavalry"]), 60)
            ]);
            bot.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["romans-lightcavalry"]), 18)]);

            var initialOases = ow.Snapshot(bot.Village.OwnerId).Oases;
            var nearOasis = initialOases
                .Where(o => LogisticsWorld.Distance(o.Position, bot.Village.Position) < 1500)
                .OrderBy(o => LogisticsWorld.Distance(o.Position, bot.Village.Position))
                .First();
            var farOasis = initialOases
                .Where(o => LogisticsWorld.Distance(o.Position, bot.Village.Position) >= 1900
                    && LogisticsWorld.Distance(o.Position, enemy.Village.Position) < 1200)
                .OrderBy(o => LogisticsWorld.Distance(o.Position, enemy.Village.Position))
                .First();

            ow.Apply(enemy.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, enemy.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-lightcavalry"] = 20 },
                DestinationId: nearOasis.Id, AfterAttack: AfterAttack.Return, Wagons: 0, Enabled: false), oNow, 0);
            ow.Apply(enemy.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, enemy.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-lightcavalry"] = 20 },
                DestinationId: farOasis.Id, AfterAttack: AfterAttack.Return, Wagons: 0, Enabled: false), oNow, 0);

            for (var i = 0; i < 600; i++)
            {
                StepO();
                var oSnap = ow.Snapshot(bot.Village.OwnerId).Oases;
                if (oSnap.Single(o => o.Id == nearOasis.Id).OwnerId == enemy.Village.OwnerId
                    && oSnap.Single(o => o.Id == farOasis.Id).OwnerId == enemy.Village.OwnerId
                    && !ow.Snapshot(enemy.Village.OwnerId).Armies.Any(a => a.OwnerId == enemy.Village.OwnerId))
                    break;
            }
            ow.Apply(enemy.Village.OwnerId, new(Guid.NewGuid(), CommandKind.UpgradeOasisLevel, nearOasis.Id), oNow, 0);

            // Station bot army near the close enemy oasis (18 fighters: skips city siege threshold 20, hits oasis dispatch >= 16)
            ow.Apply(bot.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, bot.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-lightcavalry"] = 18 },
                X: nearOasis.Position.X - 20, Y: nearOasis.Position.Y, Wagons: 0, Enabled: false), oNow, 0);
            for (var i = 0; i < 200; i++)
            {
                StepO();
                var a = ow.Snapshot(bot.Village.OwnerId).Armies.Single(x => x.OwnerId == bot.Village.OwnerId);
                if (a.Order == "Idle") break;
            }

            var cmdLog = new RecordingCommands(ow, () => oNow);
            ow.BotAct(bot.Village.OwnerId, cmdLog, BotPersonality.Aggressive, BotDifficulty.Hard);
            var nearCmd = cmdLog.Commands.LastOrDefault(c => c.Kind is CommandKind.CaptureOasis or CommandKind.BurnOasis or CommandKind.BesiegeOasis);
            Check(nearCmd is not null && nearCmd.Kind == CommandKind.CaptureOasis && nearCmd.DestinationId == nearOasis.Id,
                $"Hard bot must capture nearby enemy oasis instead of burning it (got {nearCmd?.Kind}, all={string.Join(",", cmdLog.Commands.Select(c => c.Kind))})");

            // Move the same bot army next to the far enemy oasis inside enemy territory
            var botArmyId = ow.Snapshot(bot.Village.OwnerId).Armies.Single(x => x.OwnerId == bot.Village.OwnerId).Id;
            ow.Apply(bot.Village.OwnerId, new(Guid.NewGuid(), CommandKind.MoveArmy, botArmyId,
                X: farOasis.Position.X + 20, Y: farOasis.Position.Y), oNow, 0);
            for (var i = 0; i < 400; i++)
            {
                StepO();
                var a = ow.Snapshot(bot.Village.OwnerId).Armies.Single(x => x.Id == botArmyId);
                if (a.Order == "Idle") break;
            }

            cmdLog.Commands.Clear();
            ow.BotAct(bot.Village.OwnerId, cmdLog, BotPersonality.Aggressive, BotDifficulty.Hard);
            var farCmd = cmdLog.Commands.LastOrDefault(c => c.Kind is CommandKind.CaptureOasis or CommandKind.BurnOasis or CommandKind.BesiegeOasis);
            Check(farCmd is not null && farCmd.Kind == CommandKind.BurnOasis && farCmd.DestinationId == farOasis.Id,
                $"Hard bot burns distant enemy oasis far from its own village (got {farCmd?.Kind}, all={string.Join(",", cmdLog.Commands.Select(c => c.Kind))})");
        }

        // 7. Per-unit-type casualties: weak squads in a mixed attacking or defending army take heavier losses than strong squads
        {
            var att = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(500, 500));
            var def = new VillageEconomy(Guid.NewGuid(), TribeType.Germans, new(620, 500));
            var bw = new LogisticsWorld([att, def]);
            double bNow = 0;
            void StepB() { bNow += .25; bw.Advance(.25, bNow, 0, 3600); }

            att.AddResources(new(5000, 5000, 5000, 20000));
            def.AddResources(new(5000, 5000, 5000, 20000));
            att.ReceiveGift([
                new(BaseUnit.Create(GameCatalog.Units["romans-spearman"]), 50),
                new(BaseUnit.Create(GameCatalog.Units["romans-swordsman"]), 50),
                new(BaseUnit.Create(GameCatalog.Units["romans-lightcavalry"]), 50),
                new(BaseUnit.Create(GameCatalog.Units["romans-heavycavalry"]), 50)
            ]);
            def.ReceiveGift([
                new(BaseUnit.Create(GameCatalog.Units["germans-infantry"]), 100),
                new(BaseUnit.Create(GameCatalog.Units["germans-spearman"]), 50)
            ]);

            // 7A: Mixed infantry attack (50 stronger-attack Clubmen + 50 weaker-attack Spearmen) vs defending Greeks Phalanx
            att.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["germans-infantry"]), 50)]);
            def.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["greeks-infantry"]), 50)]);
            bw.Apply(att.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, att.Village.Id,
                Formation: new Dictionary<string, int> { ["germans-infantry"] = 50, ["romans-spearman"] = 50 },
                Wagons: 0, Enabled: false, X: 540, Y: 500), bNow, 0);
            bw.Apply(def.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, def.Village.Id,
                Formation: new Dictionary<string, int> { ["greeks-infantry"] = 50 },
                Wagons: 0, Enabled: false, X: 580, Y: 500), bNow, 0);
            for (var i = 0; i < 80; i++) StepB();
            var a1 = bw.Snapshot(att.Village.OwnerId).Armies.Single(x => x.OwnerId == att.Village.OwnerId).Id;
            var d1 = bw.Snapshot(def.Village.OwnerId).Armies.Single(x => x.OwnerId == def.Village.OwnerId).Id;
            bw.Apply(att.Village.OwnerId, new(Guid.NewGuid(), CommandKind.AttackArmy, a1, DestinationId: d1), bNow, 0);
            for (var i = 0; i < 400 && !bw.Snapshot(att.Village.OwnerId).Reports.Any(r => r.Kind == "Battle" && r.Status != "Active"); i++) StepB();
            var r1 = bw.Snapshot(att.Village.OwnerId).Reports.Single(r => r.Kind == "Battle");
            var spearLost = r1.AttackerUnits.Single(u => u.UnitId == "romans-spearman");
            var clubAttLost = r1.AttackerUnits.Single(u => u.UnitId == "germans-infantry");
            var spearCasualties = spearLost.Dead + spearLost.Wounded + spearLost.Fled;
            var clubAttCasualties = clubAttLost.Dead + clubAttLost.Wounded + clubAttLost.Fled;
            Check(spearCasualties > clubAttCasualties,
                $"Weaker-attack Spearmen ({spearCasualties}) suffer heavier attacking casualties than stronger-attack Clubmen ({clubAttCasualties})");

            // 7B: Mixed defender wins -> low-defense Clubmen suffer more losses than anti-cavalry Spearmen
            def.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["germans-infantry"]), 50), new(BaseUnit.Create(GameCatalog.Units["germans-spearman"]), 100)]);
            bw.Apply(att.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, att.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-lightcavalry"] = 50, ["romans-heavycavalry"] = 50 },
                Wagons: 0, Enabled: false, X: 540, Y: 550), bNow, 0);
            bw.Apply(def.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, def.Village.Id,
                Formation: new Dictionary<string, int> { ["germans-infantry"] = 100, ["germans-spearman"] = 100 },
                Wagons: 0, Enabled: false, X: 580, Y: 550), bNow, 0);
            for (var i = 0; i < 80; i++) StepB();
            var a2 = bw.Snapshot(att.Village.OwnerId).Armies.Last(x => x.OwnerId == att.Village.OwnerId && x.Order == "Idle").Id;
            var d2 = bw.Snapshot(def.Village.OwnerId).Armies.Last(x => x.OwnerId == def.Village.OwnerId && x.Order == "Idle").Id;
            bw.Apply(att.Village.OwnerId, new(Guid.NewGuid(), CommandKind.AttackArmy, a2, DestinationId: d2), bNow, 0);
            for (var i = 0; i < 400 && bw.Snapshot(att.Village.OwnerId).Reports.Count(r => r.Kind == "Battle" && r.Status != "Active") < 2; i++) StepB();
            var r2 = bw.Snapshot(att.Village.OwnerId).Reports.Where(r => r.Kind == "Battle" && r.Id != r1.Id).Single();

            var clubDefLost = r2.DefenderUnits.Single(u => u.UnitId == "germans-infantry");
            var spearDefLost = r2.DefenderUnits.Single(u => u.UnitId == "germans-spearman");
            var clubDefCasualties = clubDefLost.Dead + clubDefLost.Wounded + clubDefLost.Fled;
            var spearDefCasualties = spearDefLost.Dead + spearDefLost.Wounded + spearDefLost.Fled;
            Check(clubDefCasualties > spearDefCasualties,
                $"Weak-defense Clubmen ({clubDefCasualties}) take heavier defending losses than anti-cavalry Spearmen ({spearDefCasualties})");

            // 7C: Mixed cavalry wins -> Light Cavalry suffer heavier casualties than Heavy Cavalry
            att.AddResources(new(0, 0, 0, 10000));
            def.AddResources(new(0, 0, 0, 10000));
            att.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["romans-lightcavalry"]), 50), new(BaseUnit.Create(GameCatalog.Units["romans-heavycavalry"]), 50)]);
            def.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["greeks-infantry"]), 50)]);
            bw.Apply(att.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, att.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-lightcavalry"] = 50, ["romans-heavycavalry"] = 50 },
                Wagons: 0, Enabled: false, X: 540, Y: 600), bNow, 0);
            bw.Apply(def.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, def.Village.Id,
                Formation: new Dictionary<string, int> { ["greeks-infantry"] = 50 },
                Wagons: 0, Enabled: false, X: 580, Y: 600), bNow, 0);
            for (var i = 0; i < 80; i++) StepB();
            var a3 = bw.Snapshot(att.Village.OwnerId).Armies.Last(x => x.OwnerId == att.Village.OwnerId && x.Order == "Idle").Id;
            var d3 = bw.Snapshot(def.Village.OwnerId).Armies.Last(x => x.OwnerId == def.Village.OwnerId && x.Order == "Idle").Id;
            bw.Apply(att.Village.OwnerId, new(Guid.NewGuid(), CommandKind.AttackArmy, a3, DestinationId: d3), bNow, 0);
            for (var i = 0; i < 400 && bw.Snapshot(att.Village.OwnerId).Reports.Count(r => r.Kind == "Battle" && r.Status != "Active") < 3; i++) StepB();
            var r3 = bw.Snapshot(att.Village.OwnerId).Reports.Where(r => r.Kind == "Battle" && r.Id != r1.Id && r.Id != r2.Id).Single();

            var lcLost = r3.AttackerUnits.Single(u => u.UnitId == "romans-lightcavalry");
            var hcLost = r3.AttackerUnits.Single(u => u.UnitId == "romans-heavycavalry");
            var lcCasualties = lcLost.Dead + lcLost.Wounded + lcLost.Fled;
            var hcCasualties = hcLost.Dead + hcLost.Wounded + hcLost.Fled;
            Check(lcCasualties > hcCasualties,
                $"Light Cavalry ({lcCasualties}) take heavier attacking losses than Heavy Cavalry ({hcCasualties})");
        }

        // 8. Balanced team spawn centering, bot MergeArmies consolidation, bot Scouting, and anti-dogpile target distribution
        {
            // 8a. Human host sits centered between bot teammates inside a 3-player team
            var host = Guid.NewGuid();
            var spawnRoom = new GameRoom(host, "Host", TribeType.Romans, "SpawnBalance",
                new(Capacity: 8, DurationMinutes: 30, ShieldMinutes: 5, ResourceSpeed: 2, BotCount: 5, BotDifficulty: BotDifficulty.Hard),
                DateTimeOffset.UtcNow);
            var lobbyBots = spawnRoom.Snapshot(host, _ => true).Players.Where(p => p.IsBot).ToArray();
            spawnRoom.ChangeTeam(host, host, 1);
            spawnRoom.ChangeTeam(host, lobbyBots[0].Id, 1);
            spawnRoom.ChangeTeam(host, lobbyBots[1].Id, 1);
            spawnRoom.ChangeTeam(host, lobbyBots[2].Id, 2);
            spawnRoom.ChangeTeam(host, lobbyBots[3].Id, 2);
            spawnRoom.ChangeTeam(host, lobbyBots[4].Id, 2);
            spawnRoom.SetReady(host, true);
            spawnRoom.Start(host, _ => true);

            var players = spawnRoom.Snapshot(host, _ => true).Players;
            var hostPos = players.Single(p => p.Id == host).Position!.Value;
            var team1Bots = players.Where(p => p.Team == 1 && p.IsBot).Select(p => p.Position!.Value).ToArray();
            var team2Bots = players.Where(p => p.Team == 2).Select(p => p.Position!.Value).ToArray();
            var minDistHostToEnemy = team2Bots.Min(e => LogisticsWorld.Distance(hostPos, e));
            var minDistAllyEdgeToEnemy = team1Bots.Min(b => team2Bots.Min(e => LogisticsWorld.Distance(b, e)));
            Check(minDistHostToEnemy > minDistAllyEdgeToEnemy + 50,
                $"Host sits centered inside Team 1 ({minDistHostToEnemy:0} > edge {minDistAllyEdgeToEnemy:0}) rather than on the exposed border");

            // 8b. Hard bot consolidates fragmented idle field armies via MergeArmies and launches Scouts
            var botV = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(700, 700));
            var foe1 = new VillageEconomy(Guid.NewGuid(), TribeType.Germans, new(1500, 700));
            var foe2 = new VillageEconomy(Guid.NewGuid(), TribeType.Greeks, new(1500, 1200));
            var bId = botV.Village.OwnerId;
            var aiWorld = new LogisticsWorld([botV, foe1, foe2]);
            double aiNow = 0;
            void StepAi() { aiNow += .25; aiWorld.AdvanceEconomies(.25, aiNow, 1); aiWorld.Advance(.25, aiNow, 0, 3600); }

            botV.AddResources(new(8000, 8000, 8000, 12000));
            botV.ReceiveGift([
                new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]), 50),
                new(BaseUnit.Create(GameCatalog.Units["romans-scout"]), 3),
                new(BaseUnit.Create(GameCatalog.Units["romans-wagon"]), 6)
            ]);
            aiWorld.Apply(bId, new(Guid.NewGuid(), CommandKind.DispatchArmy, botV.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 14 }, X: 820, Y: 700, Wagons: 0, Enabled: false), aiNow, 0);
            aiWorld.Apply(bId, new(Guid.NewGuid(), CommandKind.DispatchArmy, botV.Village.Id,
                Formation: new Dictionary<string, int> { ["romans-infantry"] = 12 }, X: 900, Y: 700, Wagons: 0, Enabled: false), aiNow, 0);
            for (var i = 0; i < 120 && aiWorld.Snapshot(bId).Armies.Any(a => a.Order != "Idle"); i++) StepAi();

            var aiCmds = new RecordingCommands(aiWorld, () => aiNow);
            aiWorld.BotAct(bId, aiCmds, BotPersonality.Aggressive, BotDifficulty.Hard);
            Check(aiCmds.Commands.Any(c => c.Kind == CommandKind.MergeArmies), "Hard bot uses MergeArmies to combine fragmented field armies");
            Check(aiCmds.Commands.Any(c => c.Kind == CommandKind.Scout), "Hard bot dispatches scouts toward unscouted enemy targets");
            for (var i = 0; i < 120 && aiWorld.Snapshot(bId).Armies.Count(a => a.Order != "Scouting") > 1; i++) StepAi();
            var mergedField = aiWorld.Snapshot(bId).Armies.Where(a => a.Order != "Scouting").ToArray();
            Check(mergedField.Length == 1 && mergedField[0].Soldiers == 26,
                $"Bot field armies merged cleanly into single 26-soldier force (count={mergedField.Length}, soldiers={mergedField.FirstOrDefault()?.Soldiers})");

            // 8c. Enemy team avoids dogpiling one player when >= 2 allies are already marching against them
            var allyA = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(600, 700));
            var allyB = new VillageEconomy(Guid.NewGuid(), TribeType.Greeks, new(600, 1200));
            var e1 = new VillageEconomy(Guid.NewGuid(), TribeType.Germans, new(1500, 700));
            var e2 = new VillageEconomy(Guid.NewGuid(), TribeType.Mongols, new(1500, 950));
            var e3 = new VillageEconomy(Guid.NewGuid(), TribeType.Persians, new(1500, 1200));
            var teamW = new LogisticsWorld([allyA, allyB, e1, e2, e3]);
            teamW.SetAlliances([
                new(allyA.Village.OwnerId, allyB.Village.OwnerId, 0),
                new(e1.Village.OwnerId, e2.Village.OwnerId, 0),
                new(e1.Village.OwnerId, e3.Village.OwnerId, 0),
                new(e2.Village.OwnerId, e3.Village.OwnerId, 0)
            ]);
            foreach (var v in new[] { allyA, allyB, e1, e2, e3 })
            {
                var pfx = v.Village.Tribe.ToString().ToLowerInvariant();
                v.AddResources(new(12000, 12000, 12000, 20000));
                v.ReceiveGift([
                    new(BaseUnit.Create(GameCatalog.Units[pfx + "-infantry"]), 50),
                    new(BaseUnit.Create(GameCatalog.Units[pfx + "-wagon"]), 6)
                ]);
            }
            // e1 and e2 are already marching to besiege allyA
            teamW.Apply(e1.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, e1.Village.Id,
                Formation: new Dictionary<string, int> { ["germans-infantry"] = 28 }, X: allyA.Village.Position.X, Y: allyA.Village.Position.Y,
                Wagons: 1, DestinationId: allyA.Village.Id, Objective: AttackObjective.Raid), 1, 0);
            teamW.Apply(e2.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, e2.Village.Id,
                Formation: new Dictionary<string, int> { ["mongols-infantry"] = 28 }, X: allyA.Village.Position.X, Y: allyA.Village.Position.Y,
                Wagons: 1, DestinationId: allyA.Village.Id, Objective: AttackObjective.Raid), 1, 0);
            var e3Cmds = new RecordingCommands(teamW, () => 2);
            teamW.BotAct(e3.Village.OwnerId, e3Cmds, BotPersonality.Aggressive, BotDifficulty.Hard);
            var e3SiegeTarget = e3Cmds.Commands.LastOrDefault(c => c.Kind is CommandKind.DispatchArmy or CommandKind.BeginSiege && c.DestinationId is not null)?.DestinationId;
            var cmdSummary = string.Join(",", e3Cmds.Commands.Select(c => $"{c.Kind}:{c.DestinationId}:{c.Objective}"));
            Check(e3SiegeTarget == allyB.Village.Id,
                $"Third enemy bot spreads pressure onto unpressured teammate instead of dogpiling oversaturated player (target={e3SiegeTarget}, allyA={allyA.Village.Id}, allyB={allyB.Village.Id}, cmds={cmdSummary})");
        }

        return checks;
    }

    private sealed class RecordingCommands(LogisticsWorld world, Func<double> now) : IGameCommands
    {
        public List<GameCommand> Commands { get; } = [];
        public CommandResult Execute(Guid actorId, GameCommand command)
        {
            Commands.Add(command);
            try
            {
                world.Apply(actorId, command, now(), 0);
                return new(command.RequestId, true, "ok", "ok");
            }
            catch (GameRuleException ex)
            {
                return new(command.RequestId, false, ex.Code, ex.Message);
            }
        }
    }
}
