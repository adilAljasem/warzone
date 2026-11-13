using Microsoft.AspNetCore.SignalR;

namespace Warzone;

public sealed record SnapshotDelivery(string[] Connections, RoomSnapshot Snapshot);

public sealed class GameServer(TimeProvider time, ILogger<GameServer> logger)
{
    private readonly object gate = new();
    private readonly Dictionary<string, GameRoom> rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, string> memberships = [];
    private readonly Dictionary<string, Guid> connections = [];
    private readonly Dictionary<string, (long Started, int Count)> rates = [];

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

    public RoomSummary[] ListRooms()
    {
        lock (gate) return rooms.Values.Select(x => x.Summary()).ToArray();
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
            if (rooms.Count >= 64) throw new GameRuleException("room_limit", "The server has reached its room limit.");
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
                if (previous == room.Id) return room.Snapshot(actor, Online);
                Leave(actor);
            }
            room.Join(actor, playerName, tribe);
            memberships.Add(actor, room.Id);
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
                if (!room.HumanIds.Any()) rooms.Remove(room.Id);
            }
            memberships.Remove(actor);
        }
    }
    public CommandResult Execute(Guid actor, GameCommand command)
    {
        lock (gate) return RoomFor(actor).Execute(actor, command);
    }

    public void Tick(double seconds)
    {
        lock (gate)
        {
            var now = time.GetUtcNow();
            foreach (var room in rooms.Values.ToArray())
            {
                if (room.HumanIds.Any(Online)) room.LastOccupiedAt = now;
                if (now - room.LastOccupiedAt > TimeSpan.FromMinutes(10))
                {
                    foreach (var id in room.HumanIds) memberships.Remove(id);
                    rooms.Remove(room.Id);
                    continue;
                }
                try { room.Advance(seconds); }
                catch (Exception error)
                {
                    logger.LogError(error, "Simulation failed for room {RoomId}", room.Id);
                    room.MarkFaulted();
                }
            }
        }
    }
    public SnapshotDelivery[] Deliveries()
    {
        lock (gate)
            return connections.GroupBy(x => x.Value).Where(x => memberships.ContainsKey(x.Key))
                .Select(x => new SnapshotDelivery(x.Select(c => c.Key).ToArray(), RoomFor(x.Key).Snapshot(x.Key, Online))).ToArray();
    }
}

public sealed class GameLoop(GameServer server, IHubContext<GameHub> hub, TimeProvider time, ILogger<GameLoop> logger) : BackgroundService
{
    public const double StepSeconds = .25;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(StepSeconds), time);
        var previous = time.GetTimestamp();
        var accumulator = 0d;
        var broadcastTime = 0d;
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var current = time.GetTimestamp();
                var elapsed = time.GetElapsedTime(previous, current).TotalSeconds;
                previous = current;
                accumulator += elapsed;
                broadcastTime += elapsed;
                // Retain debt while bounding each catch-up batch to five simulated seconds.
                for (var steps = 0; accumulator >= StepSeconds && steps < 20; steps++)
                {
                    server.Tick(StepSeconds);
                    accumulator -= StepSeconds;
                }
                if (broadcastTime < 1) continue;
                broadcastTime %= 1;
                var deliveries = server.Deliveries();
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
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}