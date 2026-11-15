using Microsoft.Extensions.Logging.Abstractions;
using Warzone;

static class AdminSpectatorChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Reject(Action action, string code)
    {
        try { action(); }
        catch (GameRuleException err) when (err.Code == code) { return; }
        throw new Exception($"Expected rule rejection '{code}'.");
    }

    public static int Run()
    {
        var count = 0;
        var time = new ManualTime();
        var server = new GameServer(time, NullLogger<GameServer>.Instance);

        var admin = Guid.NewGuid();
        var host = Guid.NewGuid();
        var watcher = Guid.NewGuid();

        server.Connect("c-admin", admin);
        server.Connect("c-host", host);
        server.Connect("c-watch", watcher);

        // 1. Auth & Cheat Validation before any lobby
        Check(!server.IsAdmin(admin), "User is not admin initially.");
        Reject(() => server.EnableCheat(admin, "wrong123"), "cheat");
        Reject(() => server.AdminSpawnBotRooms(admin, 15, 8, BotDifficulty.Normal, 10), "admin");

        Check(server.EnableCheat(admin, "aa123456"), "Valid cheat password unlocks admin before lobby.");
        Check(server.IsAdmin(admin), "Admin status persists on server.");
        Check(!server.IsAdmin(host) && !server.IsAdmin(watcher), "Admin status is strictly isolated to the player who ran the cheat command.");
        count += 5;

        // 2. Bot-only rooms stress spawn (15-50 rooms, auto-started)
        var afterSpawn = server.AdminSpawnBotRooms(admin, 20, 8, BotDifficulty.Hard, 10, 0);
        Check(afterSpawn.Length == 20, "Spawned 20 auto-started bot-only rooms.");
        Check(afterSpawn.All(r => r.Status == RoomStatus.Running && r.Bots == 8 && r.Humans == 0 && r.IsBotOnly), "All bot-only rooms start immediately in Running state.");
        var botRoomId = afterSpawn[0].Id;

        // Advance simulation: x10 speed on 0.25s tick advances 2.5s; verify Hard bot-only rooms do NOT put all bots in one alliance
        server.Tick(0.25);
        var watched = server.Spectate(watcher, botRoomId, "Observer");
        Check(watched.IsSpectator && watched.Status == RoomStatus.Running, "Any player can spectate a running bot-only room.");
        Check(watched.ElapsedSeconds >= 2.49 && watched.ElapsedSeconds <= 2.51, "x10 speed advances 2.5s in a 0.25s server tick.");
        Check(watched.Economy is null, "Spectator does not own an economy.");
        Check((watched.World?.VillageGarrisons?.Count ?? 0) >= 8, "Spectator sees omniscient village garrisons.");
        for (var i = 0; i < 8; i++) server.Tick(0.25); // 20s more at x10
        var hardBotSnap = server.Resume(watcher)!;
        var maxPossiblePairs = hardBotSnap.Players.Length * (hardBotSnap.Players.Length - 1) / 2;
        Check(hardBotSnap.Alliances.Length < maxPossiblePairs, "Hard bot-only room does not unite all bots into a single alliance.");
        count++;

        // Spectator can chat & cannot send gameplay orders, and can leave mid-game
        var badOrder = server.Execute(watcher, new(Guid.NewGuid(), CommandKind.Upgrade, watched.Players[0].VillageId!.Value));
        Check(!badOrder.Accepted && badOrder.Code == "spectator", "Spectators cannot issue gameplay commands.");
        var chatOk = server.Execute(watcher, new(Guid.NewGuid(), CommandKind.SendChat, Guid.Empty, Message: "Nice fight!"));
        Check(chatOk.Accepted, "Spectators can send chat messages.");
        var snapAfterChat = server.Resume(watcher)!;
        Check(snapAfterChat.Chat.Any(c => c.SenderId == watcher && (c.SenderName ?? "").Contains("Spectator")), "Spectator chat attaches spectator display name.");

        server.Leave(watcher);
        Check(server.Resume(watcher) is null, "Spectator can leave a running match cleanly.");
        Check(server.ListRooms().Length == 20, "Bot-only rooms remain alive when spectators leave.");
        count += 10;

        // 3. Lobby spectator switch + normal room cheats (speed x20, resources, same-tribe troops)
        var lobby = server.Create(host, "Host", TribeType.Romans, "CheatArena", new(8, 60, 5, 1, 6, BotDifficulty.Normal, 45));
        var specLobby = server.Spectate(watcher, lobby.Id, "GuestSpec");
        Check(specLobby.IsSpectator && specLobby.Spectators?.Length == 1, "Can join open lobby directly as spectator.");

        server.Join(admin, lobby.Id, "AdminPlayer", TribeType.Greeks);
        server.Ready(host, true);
        server.Ready(admin, true);
        var runningSnap = server.Start(host);
        Check(runningSnap.Status == RoomStatus.Running, "Match starts with host + admin + bots while spectator watches.");

        // Speed x20 (allowed for admin, rejected for non-admin in the same room)
        Reject(() => server.AdminSetSpeed(host, 20), "admin");
        var fastSnap = server.AdminSetSpeed(admin, 20)!;
        Check(Math.Abs(fastSnap.SpeedMultiplier - 20) < 0.01, "Admin can set match speed to x20.");

        // Give resources beyond warehouse cap
        var giftedResSnap = server.AdminGiveResources(admin, admin, 25000, 15000, 12000, 30000);
        Check(giftedResSnap.Economy!.Resources.Wood >= 25000 && giftedResSnap.Economy.Resources.Wheat >= 30000, "Admin resources bypass starter storage cap.");

        // Same-tribe troop validation + spawn
        Reject(() => server.AdminGiveTroops(admin, admin, "romans-infantry", 100), "tribe");
        var giftedTroopsSnap = server.AdminGiveTroops(admin, admin, "greeks-infantry", 250);
        Check(giftedTroopsSnap.Economy!.Garrison.GetValueOrDefault("greeks-infantry") >= 250, "Admin can grant same-tribe troops immediately.");
        count += 7;

        // 4. Clear bot rooms cleans all bot-only rooms without deleting normal player rooms
        var remaining = server.AdminClearBotRooms(admin);
        Check(remaining.Length == 1 && remaining[0].Id == lobby.Id, "Clear bot rooms deletes only bot-only rooms.");
        count += 1;

        // 5. Finished match is removed from ListRooms UI immediately and deleted from server after final delivery
        var quick = server.Create(admin, "AdminHost", TribeType.Romans, "QuickFinish", new(8, 30, 0, 1, 7, BotDifficulty.Easy, 1));
        server.Ready(admin, true);
        server.Start(admin);
        server.AdminSetSpeed(admin, 20);
        for (var i = 0; i < 400 && server.ListRooms().Any(r => r.Id == quick.Id); i++)
            server.Tick(0.25);

        Check(!server.ListRooms().Any(r => r.Id == quick.Id), "Finished match disappears immediately from ListRooms (UI).");
        Check(server.ConsumeRoomsUpdated() is not null, "Server marks room list dirty on match completion.");
        var deliveries = server.Deliveries();
        Check(deliveries.Any(d => d.Snapshot.Id == quick.Id && d.Snapshot.Status == RoomStatus.Finished), "Connected viewer receives final Finished snapshot.");
        server.Tick(0.25);
        Check(server.Resume(admin) is null, "Finished match is completely deleted from server and memberships after final delivery.");
        count += 4;

        // 6. Multi-core parallel Tick + concurrent Execute / Deliveries / ListRooms / GetTelemetry safety
        server.AdminSpawnBotRooms(admin, 24, 8, BotDifficulty.Normal, 10, 0);
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        server.Connect("c-p1", p1);
        server.Connect("c-p2", p2);
        var humanRoom1 = server.Create(p1, "P1", TribeType.Romans, "ParallelA", new(8, 60, 5, 1, 7, BotDifficulty.Normal, 45));
        var humanRoom2 = server.Create(p2, "P2", TribeType.Germans, "ParallelB", new(8, 60, 5, 1, 7, BotDifficulty.Normal, 45));
        server.Ready(p1, true); server.Start(p1);
        server.Ready(p2, true); server.Start(p2);
        var mainHall1 = server.Resume(p1)!.Economy!.Targets.First(t => t.Kind == "Building").Id;
        var mainHall2 = server.Resume(p2)!.Economy!.Targets.First(t => t.Kind == "Building").Id;

        using var cts = new CancellationTokenSource();
        var simTask = Task.Run(() =>
        {
            for (var i = 0; i < 16; i++)
            {
                server.Tick(0.25);
                var batch = server.Deliveries();
                Check(batch.Any(d => d.Snapshot.Id == humanRoom1.Id) && batch.Any(d => d.Snapshot.Id == humanRoom2.Id), "Deliveries returns both connected players' rooms.");
            }
        });
        var readerTask = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                _ = server.ListRooms();
                _ = server.GetTelemetry(0);
                _ = server.Execute(p1, new(Guid.NewGuid(), CommandKind.Upgrade, mainHall1));
                _ = server.Execute(p2, new(Guid.NewGuid(), CommandKind.Upgrade, mainHall2));
            }
        });
        simTask.GetAwaiter().GetResult();
        cts.Cancel();
        readerTask.GetAwaiter().GetResult();

        var telemetry = server.GetTelemetry(0);
        Check(telemetry.ActiveRooms >= 26 && telemetry.RunningRooms == telemetry.ActiveRooms, "All active rooms keep Running cleanly under parallel Tick + concurrent Execute.");
        Check(server.Resume(p1)!.ElapsedSeconds >= 3.99 && server.Resume(p2)!.ElapsedSeconds >= 3.99, "Both human rooms advanced 4.0s cleanly during parallel simulation.");
        server.AdminClearBotRooms(admin);
        count += 3;

        return count;
    }
}
