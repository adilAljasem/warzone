using Microsoft.AspNetCore.SignalR;

namespace Warzone;

public sealed record SnapshotDelivery(string[] Connections, RoomSnapshot Snapshot);

public sealed record ServerDebugTelemetry(
    double ClientTimestamp,
    double ServerTickTargetHz,
    double ServerTickActualHz,
    double LastTickMs,
    double AvgTickMs,
    double MaxTickMs,
    double LastBroadcastMs,
    int LastTickCatchupSteps,
    long TotalTicks,
    long OverrunTicks,
    int ActiveRooms,
    int RunningRooms,
    int ConnectedClients);

public sealed class GameServer(TimeProvider time, ILogger<GameServer> logger)
{
    private const string CheatPassword = "aa123456";
    private readonly object gate = new();
    private readonly object telemetryGate = new();
    private readonly Dictionary<string, GameRoom> rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, string> memberships = [];
    private readonly Dictionary<string, Guid> connections = [];
    private readonly Dictionary<string, (long Started, int Count)> rates = [];
    private readonly HashSet<Guid> admins = [];
    private readonly double[] recentTickMs = new double[40];
    private int botRoomSeq;
    private bool roomsDirty;
    private int recentTickIndex;
    private int recentTickCount;
    private double lastTickMs;
    private double avgTickMs;
    private double avgLoopElapsed = 0.25;
    private double avgStepsPerWake = 1;
    private double lastBroadcastMs;
    private int lastCatchupSteps;
    private long totalTicks;
    private long overrunTicks;

    public void Connect(string connectionId, Guid playerId)
    {
        lock (gate)
        {
            if (connections.Count >= 1024 || connections.Values.Count(x => x == playerId) >= 4)
                throw new GameRuleException("connections", "Connection limit reached.");
            connections.Add(connectionId, playerId);
        }
    }
    public void Disconnect(string connectionId)
    {
        lock (gate) { connections.Remove(connectionId); rates.Remove(connectionId); }
    }
    public void CheckRate(string connectionId)
    {
        lock (gate)
        {
            if (!connections.ContainsKey(connectionId)) throw new GameRuleException("connection", "Connection is not registered.");
            var now = time.GetTimestamp();
            var rate = rates.GetValueOrDefault(connectionId, (Started: now, Count: 0));
            if (time.GetElapsedTime(rate.Started, now) >= TimeSpan.FromSeconds(1)) rate = (now, 0);
            if (rate.Count >= 20) throw new GameRuleException("rate_limit", "Too many requests. Try again shortly.");
            rates[connectionId] = (rate.Started, rate.Count + 1);
        }
    }
    private bool Online(Guid id) => connections.Values.Contains(id);
    private GameRoom RoomFor(Guid actor) => memberships.TryGetValue(actor, out var id) && rooms.TryGetValue(id, out var room)
        ? room : throw new GameRuleException("membership", "Join a room first.");

    public bool IsAdmin(Guid actor)
    {
        lock (gate) return admins.Contains(actor);
    }

    public bool EnableCheat(Guid actor, string password)
    {
        if (!string.Equals(password?.Trim(), CheatPassword, StringComparison.Ordinal))
            throw new GameRuleException("cheat", "Invalid cheat password.");
        lock (gate)
        {
            admins.Add(actor);
            return true;
        }
    }

    private void RequireAdmin(Guid actor)
    {
        if (!admins.Contains(actor)) throw new GameRuleException("admin", "Admin cheat mode is not enabled.");
    }

    private void RemoveRoom(GameRoom room)
    {
        foreach (var id in room.ViewerIds.ToArray()) memberships.Remove(id);
        rooms.Remove(room.Id);
        roomsDirty = true;
    }

    public RoomSummary[] ListRooms()
    {
        lock (gate) return rooms.Values.Where(x => x.Status is RoomStatus.Lobby or RoomStatus.Running).Select(x => x.Summary()).ToArray();
    }

    public RoomSummary[]? ConsumeRoomsUpdated()
    {
        lock (gate)
        {
            if (!roomsDirty) return null;
            roomsDirty = false;
            return rooms.Values.Where(x => x.Status is RoomStatus.Lobby or RoomStatus.Running).Select(x => x.Summary()).ToArray();
        }
    }
    public RoomSnapshot? Resume(Guid actor)
    {
        lock (gate) return memberships.ContainsKey(actor) ? RoomFor(actor).Snapshot(actor, Online) : null;
    }
    public RoomSnapshot Create(Guid actor, string playerName, TribeType? tribe, string name, RoomSettings settings)
    {
        lock (gate)
        {
            if (memberships.ContainsKey(actor)) Leave(actor);
            if (rooms.Count >= 128) throw new GameRuleException("room_limit", "The server has reached its room limit.");
            var room = new GameRoom(actor, playerName, tribe, name, settings, time.GetUtcNow());
            rooms.Add(room.Id, room);
            memberships.Add(actor, room.Id);
            return room.Snapshot(actor, Online);
        }
    }
    public RoomSnapshot Join(Guid actor, string roomId, string playerName, TribeType? tribe)
    {
        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(roomId) || !rooms.TryGetValue(roomId.Trim(), out var room))
                throw new GameRuleException("not_found", "Room not found.");
            if (memberships.TryGetValue(actor, out var previous))
            {
                if (previous == room.Id && !room.IsSpectator(actor)) return room.Snapshot(actor, Online);
                Leave(actor);
            }
            room.Join(actor, playerName, tribe);
            memberships.Add(actor, room.Id);
            return room.Snapshot(actor, Online);
        }
    }
    public RoomSnapshot Spectate(Guid actor, string roomId, string playerName)
    {
        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(roomId) || !rooms.TryGetValue(roomId.Trim(), out var room))
                throw new GameRuleException("not_found", "Room not found.");
            if (memberships.TryGetValue(actor, out var previous))
            {
                if (previous == room.Id)
                {
                    if (!room.IsSpectator(actor))
                    {
                        if (room.Status != RoomStatus.Lobby)
                            throw new GameRuleException("running", "You are already playing in this match.");
                        room.SwitchSpectator(actor, true);
                    }
                    return room.Snapshot(actor, Online);
                }
                Leave(actor);
            }
            room.JoinSpectator(actor, playerName);
            memberships.Add(actor, room.Id);
            return room.Snapshot(actor, Online);
        }
    }
    public RoomSnapshot SwitchSpectator(Guid actor, bool spectate, TribeType? tribe = null)
    {
        lock (gate)
        {
            var room = RoomFor(actor);
            room.SwitchSpectator(actor, spectate, tribe);
            return room.Snapshot(actor, Online);
        }
    }
    public RoomSnapshot Configure(Guid actor, RoomSettings settings)
    {
        lock (gate) { var room = RoomFor(actor); room.Configure(actor, settings); return room.Snapshot(actor, Online); }
    }
    public RoomSnapshot Ready(Guid actor, bool ready)
    {
        lock (gate) { var room = RoomFor(actor); room.SetReady(actor, ready); return room.Snapshot(actor, Online); }
    }
    public RoomSnapshot ChangeTribe(Guid actor, Guid targetId, string tribeOrRandom)
    {
        lock (gate) { var room = RoomFor(actor); room.ChangeTribe(actor, targetId, tribeOrRandom); return room.Snapshot(actor, Online); }
    }
    public RoomSnapshot ChangeTeam(Guid actor, Guid targetId, int team)
    {
        lock (gate) { var room = RoomFor(actor); room.ChangeTeam(actor, targetId, team); return room.Snapshot(actor, Online); }
    }
    public RoomSnapshot ChangeColor(Guid actor, Guid targetId, string color)
    {
        lock (gate) { var room = RoomFor(actor); room.ChangeColor(actor, targetId, color); return room.Snapshot(actor, Online); }
    }
    public RoomSnapshot ChangeSpawnSlot(Guid actor, Guid targetId, int? slot)
    {
        lock (gate) { var room = RoomFor(actor); room.ChangeSpawnSlot(actor, targetId, slot); return room.Snapshot(actor, Online); }
    }
    public string? RoomIdFor(Guid actor)
    {
        lock (gate) return memberships.TryGetValue(actor, out var id) ? id : null;
    }
    public SnapshotDelivery[] RoomDeliveries(string? roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return [];
        lock (gate)
        {
            if (!rooms.TryGetValue(roomId, out var room)) return [];
            return connections.Where(x => memberships.TryGetValue(x.Value, out var id) && id == roomId)
                .GroupBy(x => x.Value)
                .Select(x => new SnapshotDelivery(x.Select(c => c.Key).ToArray(), room.Snapshot(x.Key, Online)))
                .ToArray();
        }
    }
    public RoomSnapshot Start(Guid actor)
    {
        lock (gate) { var room = RoomFor(actor); room.Start(actor, Online); return room.Snapshot(actor, Online); }
    }
    public void Leave(Guid actor)
    {
        lock (gate)
        {
            if (!memberships.TryGetValue(actor, out var id)) return;
            if (rooms.TryGetValue(id, out var room))
            {
                room.Abandon(actor);
                if (!room.ViewerIds.Any() && !room.KeepAliveWhenEmpty) rooms.Remove(room.Id);
            }
            memberships.Remove(actor);
        }
    }
    public CommandResult Execute(Guid actor, GameCommand command)
    {
        lock (gate) return RoomFor(actor).Execute(actor, command);
    }

    public RoomSnapshot? AdminSetSpeed(Guid actor, double multiplier, bool allBotRooms = false)
    {
        lock (gate)
        {
            RequireAdmin(actor);
            if (allBotRooms)
            {
                foreach (var botRoom in rooms.Values.Where(r => r.KeepAliveWhenEmpty))
                    botRoom.AdminSetSpeed(multiplier);
                return memberships.ContainsKey(actor) ? RoomFor(actor).Snapshot(actor, Online) : null;
            }
            var room = RoomFor(actor);
            room.AdminSetSpeed(multiplier);
            return room.Snapshot(actor, Online);
        }
    }

    public RoomSnapshot AdminGiveResources(Guid actor, Guid targetPlayerId, long wood, long iron, long stone, long wheat)
    {
        lock (gate)
        {
            RequireAdmin(actor);
            var room = RoomFor(actor);
            room.AdminGiveResources(targetPlayerId, wood, iron, stone, wheat);
            return room.Snapshot(actor, Online);
        }
    }

    public RoomSnapshot AdminGiveTroops(Guid actor, Guid targetPlayerId, string unitId, int count)
    {
        lock (gate)
        {
            RequireAdmin(actor);
            var room = RoomFor(actor);
            room.AdminGiveTroops(targetPlayerId, unitId, count);
            return room.Snapshot(actor, Online);
        }
    }

    public RoomSummary[] AdminSpawnBotRooms(Guid actor, int count, int botsPerRoom, BotDifficulty difficulty, double speedMultiplier, int shieldMinutes = 5)
    {
        lock (gate)
        {
            RequireAdmin(actor);
            if (count is < 1 or > 50) throw new GameRuleException("count", "Bot room count must be between 1 and 50.");
            if (botsPerRoom is < 8 or > 16) throw new GameRuleException("capacity", "Bots per room must be between 8 and 16.");
            if (!Enum.IsDefined(difficulty)) throw new GameRuleException("difficulty", "Invalid bot difficulty.");
            if (shieldMinutes is not (0 or 5 or 10)) shieldMinutes = 5;
            if (rooms.Count + count > 128) throw new GameRuleException("room_limit", $"Server room limit (128) exceeded ({rooms.Count} active).");

            var now = time.GetUtcNow();
            var settings = new RoomSettings(botsPerRoom, 60, shieldMinutes, 1, botsPerRoom, difficulty, 45);
            for (var i = 0; i < count; i++)
            {
                var room = GameRoom.CreateBotMatch($"Bot Arena #{++botRoomSeq}", settings, now, speedMultiplier);
                rooms.Add(room.Id, room);
            }
            return rooms.Values.Where(x => x.Status is RoomStatus.Lobby or RoomStatus.Running).Select(x => x.Summary()).ToArray();
        }
    }

    public RoomSummary[] AdminClearBotRooms(Guid actor)
    {
        lock (gate)
        {
            RequireAdmin(actor);
            var botRooms = rooms.Values.Where(r => r.KeepAliveWhenEmpty).ToArray();
            foreach (var room in botRooms) RemoveRoom(room);
            return rooms.Values.Where(x => x.Status is RoomStatus.Lobby or RoomStatus.Running).Select(x => x.Summary()).ToArray();
        }
    }

    public void Tick(double seconds)
    {
        lock (gate)
        {
            var now = time.GetUtcNow();
            foreach (var room in rooms.Values.ToArray())
            {
                if (room.ViewerIds.Any(Online)) room.LastOccupiedAt = now;
                if (room.Status is RoomStatus.Finished or RoomStatus.Faulted)
                {
                    room.FinishedAt ??= now;
                    if (!room.ViewerIds.Any(Online) || room.FinalBroadcastSent || now - room.FinishedAt.Value > TimeSpan.FromSeconds(5))
                        RemoveRoom(room);
                    continue;
                }
                if (!room.KeepAliveWhenEmpty && now - room.LastOccupiedAt > TimeSpan.FromMinutes(10))
                {
                    RemoveRoom(room);
                    continue;
                }
                var previousStatus = room.Status;
                try { room.Advance(seconds); }
                catch (Exception error)
                {
                    logger.LogError(error, "Simulation failed for room {RoomId}", room.Id);
                    room.MarkFaulted();
                }
                if (previousStatus != room.Status && room.Status is RoomStatus.Finished or RoomStatus.Faulted)
                {
                    room.FinishedAt ??= now;
                    roomsDirty = true;
                    if (!room.ViewerIds.Any(Online))
                        RemoveRoom(room);
                }
            }
        }
    }
    public SnapshotDelivery[] Deliveries()
    {
        lock (gate)
            return connections.GroupBy(x => x.Value).Where(x => memberships.ContainsKey(x.Key))
                .Select(x =>
                {
                    var room = RoomFor(x.Key);
                    if (room.Status is RoomStatus.Finished or RoomStatus.Faulted) room.FinalBroadcastSent = true;
                    return new SnapshotDelivery(x.Select(c => c.Key).ToArray(), room.Snapshot(x.Key, Online));
                }).ToArray();
    }

    public void RecordLoopTick(double elapsedSeconds, double tickMs, int steps)
    {
        lock (telemetryGate)
        {
            if (elapsedSeconds > 0 && double.IsFinite(elapsedSeconds))
            {
                avgLoopElapsed = avgLoopElapsed * 0.85 + elapsedSeconds * 0.15;
                avgStepsPerWake = avgStepsPerWake * 0.85 + steps * 0.15;
            }
            if (steps <= 0) return;
            lastTickMs = tickMs;
            lastCatchupSteps = steps;
            totalTicks += steps;
            if (steps > 2 || tickMs >= GameLoop.StepSeconds * 1000) overrunTicks++;
            avgTickMs = recentTickCount == 0 ? tickMs : avgTickMs * 0.85 + tickMs * 0.15;
            recentTickMs[recentTickIndex] = tickMs;
            recentTickIndex = (recentTickIndex + 1) % recentTickMs.Length;
            if (recentTickCount < recentTickMs.Length) recentTickCount++;
        }
    }

    public void RecordBroadcast(double broadcastMs)
    {
        lock (telemetryGate) lastBroadcastMs = broadcastMs;
    }

    public ServerDebugTelemetry GetTelemetry(double clientTimestamp)
    {
        int activeRooms, runningRooms, connectedClients;
        lock (gate)
        {
            activeRooms = rooms.Count;
            runningRooms = rooms.Values.Count(r => r.Status == RoomStatus.Running);
            connectedClients = connections.Count;
        }
        lock (telemetryGate)
        {
            var maxTick = 0d;
            for (var i = 0; i < recentTickCount; i++)
                if (recentTickMs[i] > maxTick) maxTick = recentTickMs[i];
            var actualHz = avgLoopElapsed > 0.001 ? avgStepsPerWake / avgLoopElapsed : 0d;
            return new ServerDebugTelemetry(
                clientTimestamp,
                1d / GameLoop.StepSeconds,
                Math.Round(actualHz, 2),
                Math.Round(lastTickMs, 3),
                Math.Round(avgTickMs, 3),
                Math.Round(maxTick, 3),
                Math.Round(lastBroadcastMs, 3),
                lastCatchupSteps,
                totalTicks,
                overrunTicks,
                activeRooms,
                runningRooms,
                connectedClients);
        }
    }
}

public sealed class GameLoop(GameServer server, IHubContext<GameHub> hub, TimeProvider time, ILogger<GameLoop> logger) : BackgroundService
{
    public const double StepSeconds = .25;
    private int broadcastInFlight;

    private async Task SendSnapshotsAsync(SnapshotDelivery[] deliveries, long startedAt, CancellationToken stoppingToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await Task.WhenAll(deliveries.Select(x => hub.Clients.Clients(x.Connections)
                .SendAsync("Snapshot", x.Snapshot, timeout.Token)));
        }
        catch (Exception error) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(error, "Snapshot delivery failed; clients will receive the next full snapshot.");
        }
        finally
        {
            server.RecordBroadcast(time.GetElapsedTime(startedAt, time.GetTimestamp()).TotalMilliseconds);
            Volatile.Write(ref broadcastInFlight, 0);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(StepSeconds), time);
        var previous = time.GetTimestamp();
        var accumulator = 0d;
        var ticksSinceBroadcast = 0;
        const double timerJitterTolerance = 0.015;
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var current = time.GetTimestamp();
                var elapsed = time.GetElapsedTime(previous, current).TotalSeconds;
                previous = current;
                accumulator += elapsed;
                // Retain debt (allowing slight Windows PeriodicTimer early wakeups) so ticks and 1s broadcasts stay in exact lockstep.
                var tickStart = time.GetTimestamp();
                var steps = 0;
                for (; accumulator + timerJitterTolerance >= StepSeconds && steps < 20; steps++)
                {
                    server.Tick(StepSeconds);
                    accumulator -= StepSeconds;
                }
                var tickMs = steps > 0 ? time.GetElapsedTime(tickStart, time.GetTimestamp()).TotalMilliseconds : 0d;
                server.RecordLoopTick(elapsed, tickMs, steps);
                if (steps > 0 && server.ConsumeRoomsUpdated() is { } updatedRooms)
                    _ = hub.Clients.All.SendAsync("RoomsUpdated", updatedRooms, stoppingToken);
                ticksSinceBroadcast += steps;
                if (ticksSinceBroadcast < 4) continue;
                ticksSinceBroadcast %= 4;
                if (Interlocked.CompareExchange(ref broadcastInFlight, 1, 0) != 0) continue;
                var broadcastStart = time.GetTimestamp();
                var deliveries = server.Deliveries();
                if (deliveries.Length == 0)
                {
                    server.RecordBroadcast(time.GetElapsedTime(broadcastStart, time.GetTimestamp()).TotalMilliseconds);
                    Volatile.Write(ref broadcastInFlight, 0);
                    continue;
                }
                _ = SendSnapshotsAsync(deliveries, broadcastStart, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}