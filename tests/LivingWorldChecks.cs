using Microsoft.Extensions.Logging.Abstractions;
using Warzone;

internal static class LivingWorldChecks
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }

        // 1. 220 unique realistic player names mixed evenly between Arabic and English
        Check(BotNames.Pool.Length == 220, $"Expected 220 bot names, got {BotNames.Pool.Length}");
        Check(BotNames.Pool.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 220, "All 220 bot names are unique");
        Check(BotNames.Pool.All(n => !string.IsNullOrWhiteSpace(n) && n.Trim().Length <= 32 && !n.Contains("Bot", StringComparison.OrdinalIgnoreCase)),
            "All bot names are valid visible names without 'Bot'");
        var arabicCount = BotNames.Pool.Count(n => n.Any(c => c is >= '\u0600' and <= '\u06FF'));
        var englishCount = BotNames.Pool.Count(n => n.All(c => c < 128));
        Check(arabicCount == 110 && englishCount == 110, $"Balanced Arabic ({arabicCount}) and English ({englishCount}) name pool");

        // 2. Matchmaking queue: single player waits 3-15s then starts match with disguised mixed-difficulty bots
        {
            var time = new ManualTime();
            var server = new GameServer(time, NullLogger<GameServer>.Instance);
            var p1 = Guid.NewGuid();
            server.Connect("c-p1", p1);

            var status = server.StartMatchmaking(p1, "سعد", TribeType.Romans);
            Check(status.InQueue && status.QueuedPlayers == 1, "Player enters matchmaking queue");
            Check(server.ConsumeMatchFoundDeliveries().Length == 0, "Single player waits before backfill");

            // At 2.5 seconds, still waiting (minimum wait is 3s)
            time.Advance(TimeSpan.FromSeconds(2.5));
            server.Tick(0.25);
            Check(server.ConsumeMatchFoundDeliveries().Length == 0, "Single player does not match before 3 seconds");

            // Advance up to 16 seconds total in 0.5s increments until matched
            SnapshotDelivery[] matched = [];
            for (var i = 0; i < 30 && matched.Length == 0; i++)
            {
                time.Advance(TimeSpan.FromSeconds(0.5));
                server.Tick(0.25);
                matched = server.ConsumeMatchFoundDeliveries();
            }
            Check(matched.Length == 1, "Single player is matched within 3-15 seconds");
            var snap = matched[0].Snapshot;
            Check(snap.Status == RoomStatus.Running && snap.MaskBots && snap.Players.Length == 8, "Matchmaking starts an 8-player masked live match");
            Check(snap.Settings.BotDifficulty == BotDifficulty.Mixed, "Matchmaking uses Mixed bot difficulty");
            Check(snap.Players.Count(p => !p.IsBot) == 1 && snap.Players.Count(p => p.IsBot) == 7, "1 human + 7 bots in solo matchmaking");
            Check(snap.Players.Where(p => p.IsBot).All(b => BotNames.Pool.Contains(b.Name)), "All matchmaking bots use realistic names from the pool");
            Check(snap.Players.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 8, "All players in match have distinct names");
        }

        // 3. Matchmaking queue: two real players queued together join the SAME match + cancel & disconnect work
        {
            var time = new ManualTime();
            var server = new GameServer(time, NullLogger<GameServer>.Instance);
            var p1 = Guid.NewGuid();
            var p2 = Guid.NewGuid();
            var p3 = Guid.NewGuid();
            var p4 = Guid.NewGuid();
            server.Connect("c1", p1);
            server.Connect("c2", p2);
            server.Connect("c3", p3);
            server.Connect("c4", p4);

            server.StartMatchmaking(p1, "PlayerOne", TribeType.Romans);
            server.StartMatchmaking(p3, "Canceled", TribeType.Greeks);
            server.CancelMatchmaking(p3);
            server.StartMatchmaking(p4, "Disconnected", TribeType.Persians);
            server.Disconnect("c4");

            time.Advance(TimeSpan.FromSeconds(1.5));
            server.StartMatchmaking(p2, "PlayerTwo", TribeType.Mongols);

            SnapshotDelivery[] matched = [];
            for (var i = 0; i < 35 && matched.Length == 0; i++)
            {
                time.Advance(TimeSpan.FromSeconds(0.5));
                server.Tick(0.25);
                matched = server.ConsumeMatchFoundDeliveries();
            }
            Check(matched.Length == 2, "Both waiting real players are matched together");
            Check(matched[0].Snapshot.Id == matched[1].Snapshot.Id, "Both real players land in the exact same room");
            Check(matched[0].Snapshot.Players.Count(p => !p.IsBot) == 2 && matched[0].Snapshot.Players.Count(p => p.IsBot) == 6,
                "Matched room holds 2 humans + 6 bots (canceled & disconnected players excluded)");
            Check(server.Resume(p3) is null && server.Resume(p4) is null, "Canceled and disconnected players were not placed in any room");
        }

        // 4. Mixed bot difficulty levels in the same room (Easy + Normal + Hard) & selective Hard bonuses
        {
            var host = Guid.NewGuid();
            var room = new GameRoom(host, "Human", TribeType.Romans, "MixedArena",
                new(Capacity: 8, DurationMinutes: 60, ShieldMinutes: 5, ResourceSpeed: 1, BotCount: 7, BotDifficulty: BotDifficulty.Mixed),
                DateTimeOffset.UtcNow);
            room.SetReady(host, true);
            room.Start(host, _ => true);

            var snap = room.Snapshot(host, _ => true);
            var botIds = snap.Players.Where(p => p.IsBot).Select(p => p.Id).ToArray();
            var diffs = botIds.Select(id => room.BotDifficultyFor(id)!.Value).ToArray();
            Check(diffs.Contains(BotDifficulty.Easy) && diffs.Contains(BotDifficulty.Normal) && diffs.Contains(BotDifficulty.Hard),
                $"Mixed room contains Easy, Normal, and Hard bots together ({string.Join(", ", diffs)})");

            var hardBotId = botIds.First(id => room.BotDifficultyFor(id) == BotDifficulty.Hard);
            var easyBotId = botIds.First(id => room.BotDifficultyFor(id) == BotDifficulty.Easy);
            var hardEco = room.Snapshot(hardBotId, _ => true).Economy!;
            var easyEco = room.Snapshot(easyBotId, _ => true).Economy!;
            Check(hardEco.Capacity > easyEco.Capacity, "Only Hard bots receive Hard economy capacity bonuses in Mixed mode");
        }

        // 5. Realistic bot diplomacy: bots think before answering (no instant accept), and send unique personalized invites
        {
            var host = Guid.NewGuid();
            var room = new GameRoom(host, "Human", TribeType.Romans, "DiplomacyMixed",
                new(Capacity: 8, DurationMinutes: 60, ShieldMinutes: 5, ResourceSpeed: 1, BotCount: 7, BotDifficulty: BotDifficulty.Mixed),
                DateTimeOffset.UtcNow);
            room.SetReady(host, true);
            room.Start(host, _ => true);

            // Pick a Hard bot (ticks every 0.5s) to prove even fast-ticking bots pause and think instead of instant-accepting!
            var hardBot = room.Snapshot(host, _ => true).Players.First(p => p.IsBot && room.BotDifficultyFor(p.Id) == BotDifficulty.Hard);
            Check(room.Execute(host, new(Guid.NewGuid(), CommandKind.ProposeAlliance, hardBot.Id)).Accepted,
                "Human can send alliance invitation to a bot");

            // After 1.5s (3 bot ticks), the bot MUST still be thinking (request still pending, not immediately accepted!)
            for (var i = 0; i < 6; i++) room.Advance(0.25);
            Check(room.HasPendingRequest(host, hardBot.Id) && !room.IsAllied(host, hardBot.Id),
                "Bot pauses to think instead of accepting alliance invitation instantly");

            // After 15s total, the bot finishes thinking and responds
            for (var i = 0; i < 60; i++) room.Advance(0.25);
            Check(!room.HasPendingRequest(host, hardBot.Id), "Bot responds after realistic thinking delay");

            // Advance match so bots send alliance invitations to an unallied human (without any preset message text)
            var host2 = Guid.NewGuid();
            var room2 = new GameRoom(host2, "SoloHuman", TribeType.Greeks, "InviteHumanTest",
                new(Capacity: 8, DurationMinutes: 60, ShieldMinutes: 5, ResourceSpeed: 1, BotCount: 7, BotDifficulty: BotDifficulty.Mixed),
                DateTimeOffset.UtcNow);
            room2.SetReady(host2, true);
            room2.Start(host2, _ => true);

            var receivedInvites = new List<AllianceRequestSnapshot>();
            for (var step = 0; step < 900 && receivedInvites.Count < 2; step++) // up to 225s
            {
                room2.Advance(0.25);
                var incoming = room2.Snapshot(host2, _ => true).AllianceRequests.FirstOrDefault(r => r.RecipientId == host2);
                if (incoming is not null)
                {
                    receivedInvites.Add(incoming);
                    var accept = receivedInvites.Count >= 2;
                    Check(room2.Execute(host2, new(Guid.NewGuid(), CommandKind.RespondAlliance, incoming.Id, Enabled: accept)).Accepted,
                        "Human can decline or accept bot alliance invitations");
                }
            }
            Check(receivedInvites.Count >= 1, "Bots naturally send alliance invitations to the human player");
            if (receivedInvites.Count >= 2)
                Check(room2.IsAllied(host2, receivedInvites[1].SenderId), "Accepting bot invitation forms an active alliance");

            var finalSnap = room2.Snapshot(host2, _ => true);
            Check(finalSnap.Alliances.Length < 10, $"Bots form realistic small alliances rather than one giant coalition (count={finalSnap.Alliances.Length})");
        }

        // 6. Periodic active live ambient games (5..15) AND ZERO duplicate player names across ALL active rooms!
        {
            var time = new ManualTime();
            var server = new GameServer(time, NullLogger<GameServer>.Instance) { AmbientRoomsEnabled = true };
            server.MaintainAmbientRooms(forceImmediate: true);

            var initial = server.ListRooms();
            Check(initial.Length is >= 5 and <= 15, $"Initial ambient live games count is between 5 and 15 (got {initial.Length})");
            Check(initial.All(r => r.Status == RoomStatus.Running && r.IsAmbient && r.MaskBots && r.Bots >= 8),
                "All ambient games are Running, masked, and populated");
            Check(initial.All(r => !r.Name.StartsWith("Bot Arena", StringComparison.OrdinalIgnoreCase)),
                "Ambient games use realistic arena names");

            // Also queue a human into matchmaking while all ambient rooms are running
            var human = Guid.NewGuid();
            var spectator = Guid.NewGuid();
            server.Connect("c-hum", human);
            server.Connect("c-spec", spectator);
            server.StartMatchmaking(human, "خالد_الحقيقي", TribeType.Romans);
            for (var i = 0; i < 35 && server.Resume(human) is null; i++)
            {
                time.Advance(TimeSpan.FromSeconds(0.5));
                server.Tick(0.25);
            }
            Check(server.Resume(human) is not null, "Human matched while ambient rooms are running");

            // Inspect EVERY active room on the server via spectator and ensure ZERO player names repeat across rooms!
            var seenGlobalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var duplicateName = (string?)null;
            foreach (var summary in server.ListRooms())
            {
                var roomSnap = server.Spectate(spectator, summary.Id, "Inspector");
                foreach (var player in roomSnap.Players)
                {
                    if (!seenGlobalNames.Add(player.Name))
                    {
                        duplicateName = player.Name;
                        break;
                    }
                }
                server.Leave(spectator);
                if (duplicateName is not null) break;
            }
            Check(duplicateName is null, $"No player/bot name plays in more than one active game simultaneously (duplicate: {duplicateName})");

            // Advance across several ambient target shifts and verify count stays between 5 and 15
            for (var cycle = 0; cycle < 4; cycle++)
            {
                time.Advance(TimeSpan.FromMinutes(4));
                server.Tick(0.25);
                var current = server.ListRooms();
                Check(current.Length is >= 5 and <= 16, $"Active game count stays bounded after cycle {cycle + 1} (got {current.Length})");
            }
        }

        // 7. Manual/custom room keeps classic Bot 1, Bot 2, ... names and leaves open human seats
        {
            var time = new ManualTime();
            var server = new GameServer(time, NullLogger<GameServer>.Instance);
            var host = Guid.NewGuid();
            server.Connect("c-manual", host);
            var manualSnap = server.Create(host, "ManualHost", TribeType.Romans, "FriendsRoom",
                new(Capacity: 8, DurationMinutes: 60, ShieldMinutes: 5, ResourceSpeed: 1, BotCount: 3, BotDifficulty: BotDifficulty.Mixed, CastleUnlockMinutes: 45));
            Check(!manualSnap.MaskBots, "Manual room does not mask bots");
            Check(manualSnap.Settings.Capacity == 8 && manualSnap.Players.Length == 4, "Manual room starts with 1 human + 3 bots (4/8 seats, leaving 4 open slots)");
            var botNames = manualSnap.Players.Where(p => p.IsBot).Select(p => p.Name).OrderBy(n => n).ToArray();
            Check(botNames.SequenceEqual(["Bot 1", "Bot 2", "Bot 3"]), $"Manual room keeps classic Bot 1..3 names (got {string.Join(", ", botNames)})");
        }

        // 8. Flag colors and spawn slots vary across matches instead of binding the same color to the same map spot
        {
            var slot0Colors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hostSlots = new HashSet<int>();
            for (var i = 0; i < 12; i++)
            {
                var h = Guid.NewGuid();
                var r = new GameRoom(h, $"Host{i}", TribeType.Romans, $"Var{i}",
                    new(Capacity: 8, DurationMinutes: 60, ShieldMinutes: 5, ResourceSpeed: 1, BotCount: 7, BotDifficulty: BotDifficulty.Mixed),
                    DateTimeOffset.UtcNow);
                var snap = r.Snapshot(h, _ => true);
                var atSlot0 = snap.Players.Single(p => p.EffectiveSpawnSlot == 0);
                slot0Colors.Add(atSlot0.FlagColor);
                hostSlots.Add(snap.Players.Single(p => p.Id == h).EffectiveSpawnSlot!.Value);

                // Lobby effective slots remain stable between repeated snapshot polls
                var snap2 = r.Snapshot(h, _ => true);
                Check(snap.Players.All(p => p.EffectiveSpawnSlot == snap2.Players.Single(q => q.Id == p.Id).EffectiveSpawnSlot),
                    "Lobby spawn slots remain stable across polls");
            }
            Check(slot0Colors.Count >= 3, $"Flag color at slot 0 varies across rooms (got {slot0Colors.Count} distinct colors)");
            Check(hostSlots.Count >= 3, $"Host spawn position varies across rooms (got {hostSlots.Count} distinct slots)");
        }

        return checks;
    }
}
