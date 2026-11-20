using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;

namespace Warzone;

public sealed record SnapshotDelivery(string[] Connections, RoomSnapshot Snapshot, string[]? CompressedConnections = null);
public sealed record MatchmakingStatus(bool InQueue, int QueuedPlayers);
public sealed record AdminOnlinePlayer(Guid PlayerId, string Name, string Location, string? RoomId, string? RoomName, int Connections, bool IsAdmin);

internal sealed class MatchmakingEntry(Guid playerId, string name, TribeType? tribe, DateTimeOffset joinedAt, DateTimeOffset fillAt)
{
    public Guid PlayerId { get; } = playerId;
    public string Name { get; set; } = name;
    public TribeType? Tribe { get; set; } = tribe;
    public DateTimeOffset JoinedAt { get; } = joinedAt;
    public DateTimeOffset FillAt { get; } = fillAt;
}

internal static class SnapshotCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static byte[] Compress(RoomSnapshot snapshot)
    {
        using var buffer = new MemoryStream(16 * 1024);
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            JsonSerializer.Serialize(gzip, snapshot, JsonOptions);
        return buffer.ToArray();
    }

    public static Task SendDeliveryAsync(IHubClients<IClientProxy> clients, SnapshotDelivery delivery, string? excludeConnectionId = null, CancellationToken cancellationToken = default)
    {
        var compressedLookup = delivery.CompressedConnections is { Length: > 0 } c ? c : null;
        var plain = delivery.Connections.Where(id => id != excludeConnectionId && (compressedLookup is null || !compressedLookup.Contains(id))).ToArray();
        var compressed = compressedLookup is null
            ? []
            : compressedLookup.Where(id => id != excludeConnectionId).ToArray();

        if (plain.Length == 0 && compressed.Length == 0) return Task.CompletedTask;
        if (compressed.Length == 0)
            return clients.Clients(plain).SendAsync("Snapshot", delivery.Snapshot, cancellationToken);
        var payload = Compress(delivery.Snapshot);
        if (plain.Length == 0)
            return clients.Clients(compressed).SendAsync("SnapshotGz", payload, cancellationToken);
        return Task.WhenAll(
            clients.Clients(plain).SendAsync("Snapshot", delivery.Snapshot, cancellationToken),
            clients.Clients(compressed).SendAsync("SnapshotGz", payload, cancellationToken));
    }
}

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
    private static string ResolveCheatPassword()
    {
        var configured = Environment.GetEnvironmentVariable("WARZONE_ADMIN_PASSWORD")?.Trim();
        return string.IsNullOrEmpty(configured) ? "aa123456" : configured;
    }
    // Keep 1 hardware thread reserved for Kestrel/SignalR Ping and player command latency during multi-core room ticks.
    private static readonly ParallelOptions RoomParallelOptions = new() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };
    // Lock hierarchy (strict acyclic order): gate -> room.SyncRoot -> connectionGate; telemetryGate is independent.
    private readonly object gate = new();
    private readonly object connectionGate = new();
    private readonly object telemetryGate = new();
    private readonly Dictionary<string, GameRoom> rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, string> memberships = [];
    private readonly Dictionary<string, Guid> connections = [];
    private readonly HashSet<string> compressedConnections = [];
    private readonly Dictionary<string, (long Started, int Count)> rates = [];
    private readonly HashSet<Guid> admins = [];
    private readonly List<MatchmakingEntry> matchmakingQueue = [];
    private readonly List<SnapshotDelivery> pendingMatchFound = [];
    private readonly double[] recentTickMs = new double[40];
    private int botRoomSeq;
    private bool roomsDirty;
    private int ambientTargetCount;
    private DateTimeOffset nextAmbientTargetShift = DateTimeOffset.MinValue;
    private DateTimeOffset nextAmbientCheck = DateTimeOffset.MinValue;
    public bool AmbientRoomsEnabled { get; set; }
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
        lock (connectionGate)
        {
            if (connections.Count >= 1024 || connections.Values.Count(x => x == playerId) >= 4)
                throw new GameRuleException("connections", "Connection limit reached.");
            connections.Add(connectionId, playerId);
        }
    }
    public void Disconnect(string connectionId)
    {
        Guid disconnectedPlayer = Guid.Empty;
        var stillOnline = false;
        lock (connectionGate)
        {
            if (connections.Remove(connectionId, out disconnectedPlayer))
                stillOnline = connections.Values.Contains(disconnectedPlayer);
            compressedConnections.Remove(connectionId);
            rates.Remove(connectionId);
        }
        if (disconnectedPlayer != Guid.Empty && !stillOnline)
        {
            lock (gate) matchmakingQueue.RemoveAll(e => e.PlayerId == disconnectedPlayer);
        }
    }
    public void SetSnapshotCompression(string connectionId, bool enabled)
    {
        lock (connectionGate)
        {
            if (!connections.ContainsKey(connectionId)) return;
            if (enabled) compressedConnections.Add(connectionId);
            else compressedConnections.Remove(connectionId);
        }
    }
    public void CheckRate(string connectionId)
    {
        lock (connectionGate)
        {
            if (!connections.ContainsKey(connectionId)) throw new GameRuleException("connection", "Connection is not registered.");
            var now = time.GetTimestamp();
            var rate = rates.GetValueOrDefault(connectionId, (Started: now, Count: 0));
            if (time.GetElapsedTime(rate.Started, now) >= TimeSpan.FromSeconds(1)) rate = (now, 0);
            if (rate.Count >= 20) throw new GameRuleException("rate_limit", "Too many requests. Try again shortly.");
            rates[connectionId] = (rate.Started, rate.Count + 1);
        }
    }
    private bool Online(Guid id) { lock (connectionGate) return connections.Values.Contains(id); }
    private HashSet<Guid> OnlineIds() { lock (connectionGate) return [.. connections.Values]; }
    private GameRoom RoomFor(Guid actor) => memberships.TryGetValue(actor, out var id) && rooms.TryGetValue(id, out var room)
        ? room : throw new GameRuleException("membership", "Join a room first.");
    private GameRoom ResolveRoom(Guid actor) { lock (gate) return RoomFor(actor); }

    public bool IsAdmin(Guid actor)
    {
        lock (gate) return admins.Contains(actor);
    }

    public bool EnableCheat(Guid actor, string password)
    {
        var expected = ResolveCheatPassword();
        var candidate = password?.Trim() ?? string.Empty;
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        var candidateBytes = System.Text.Encoding.UTF8.GetBytes(candidate);
        if (expectedBytes.Length != candidateBytes.Length
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expectedBytes, candidateBytes))
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

    private void RemoveRoomLocked(GameRoom room)
    {
        Guid[] viewerIds;
        lock (room.SyncRoot) viewerIds = room.ViewerIds.ToArray();
        foreach (var id in viewerIds)
            if (memberships.TryGetValue(id, out var joined) && joined == room.Id)
                memberships.Remove(id);
        rooms.Remove(room.Id);
        roomsDirty = true;
    }

    private static RoomSummary[] SummarizeVisible(GameRoom[] list)
    {
        var result = new List<RoomSummary>(list.Length);
        foreach (var room in list)
        {
            lock (room.SyncRoot)
            {
                if (room.Status is RoomStatus.Lobby or RoomStatus.Running)
                    result.Add(room.Summary());
            }
        }
        return [.. result];
    }

    public RoomSummary[] ListRooms()
    {
        GameRoom[] list;
        lock (gate) list = [.. rooms.Values];
        return SummarizeVisible(list);
    }

    public RoomSummary[]? ConsumeRoomsUpdated()
    {
        GameRoom[] list;
        lock (gate)
        {
            if (!roomsDirty) return null;
            roomsDirty = false;
            list = [.. rooms.Values];
        }
        return SummarizeVisible(list);
    }
    public RoomSnapshot? Resume(Guid actor)
    {
        lock (gate)
        {
            if (!memberships.TryGetValue(actor, out var id) || !rooms.TryGetValue(id, out var room))
                return null;
            lock (room.SyncRoot) return room.Contains(actor) ? room.Snapshot(actor, Online) : null;
        }
    }
    private HashSet<string> CollectActivePlayerNamesLocked()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rooms.Values)
        {
            lock (r.SyncRoot)
            {
                if (r.Status is not (RoomStatus.Lobby or RoomStatus.Running)) continue;
                foreach (var p in r.Players.Values) names.Add(p.Name);
            }
        }
        return names;
    }

    public RoomSnapshot Create(Guid actor, string playerName, TribeType? tribe, string name, RoomSettings settings)
    {
        lock (gate)
        {
            var activeNames = CollectActivePlayerNamesLocked();
            var room = new GameRoom(actor, playerName, tribe, name, settings, time.GetUtcNow(), activeNames);
            if (memberships.ContainsKey(actor)) LeaveLocked(actor);
            rooms.Add(room.Id, room);
            memberships.Add(actor, room.Id);
            roomsDirty = true;
            lock (room.SyncRoot) return room.Snapshot(actor, Online);
        }
    }
    public RoomSnapshot Join(Guid actor, string roomId, string playerName, TribeType? tribe)
    {
        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(roomId) || !rooms.TryGetValue(roomId.Trim(), out var room))
                throw new GameRuleException("not_found", "Room not found.");
            if (memberships.TryGetValue(actor, out var previous) && previous == room.Id)
            {
                lock (room.SyncRoot)
                {
                    if (room.IsSpectator(actor)) room.Join(actor, playerName, tribe);
                    return room.Snapshot(actor, Online);
                }
            }
            lock (room.SyncRoot) room.Join(actor, playerName, tribe);
            if (memberships.ContainsKey(actor)) LeaveLocked(actor);
            memberships.Add(actor, room.Id);
            roomsDirty = true;
            lock (room.SyncRoot) return room.Snapshot(actor, Online);
        }
    }
    public RoomSnapshot Spectate(Guid actor, string roomId, string playerName)
    {
        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(roomId) || !rooms.TryGetValue(roomId.Trim(), out var room))
                throw new GameRuleException("not_found", "Room not found.");
            if (memberships.TryGetValue(actor, out var previous) && previous == room.Id)
            {
                lock (room.SyncRoot)
                {
                    if (!room.IsSpectator(actor))
                    {
                        if (room.Status != RoomStatus.Lobby)
                            throw new GameRuleException("running", "You are already playing in this match.");
                        room.SwitchSpectator(actor, true);
                    }
                    return room.Snapshot(actor, Online);
                }
            }
            lock (room.SyncRoot) room.JoinSpectator(actor, playerName);
            if (memberships.ContainsKey(actor)) LeaveLocked(actor);
            memberships.Add(actor, room.Id);
            roomsDirty = true;
            lock (room.SyncRoot) return room.Snapshot(actor, Online);
        }
    }
    public RoomSnapshot SwitchSpectator(Guid actor, bool spectate, TribeType? tribe = null)
    {
        var room = ResolveRoom(actor);
        lock (room.SyncRoot) { room.SwitchSpectator(actor, spectate, tribe); return room.Snapshot(actor, Online); }
    }
    public RoomSnapshot Configure(Guid actor, RoomSettings settings)
    {
        lock (gate)
        {
            var room = RoomFor(actor);
            var activeNames = CollectActivePlayerNamesLocked();
            lock (room.SyncRoot) { room.Configure(actor, settings, activeNames); return room.Snapshot(actor, Online); }
        }
    }
    public RoomSnapshot Ready(Guid actor, bool ready)
    {
        var room = ResolveRoom(actor);
        lock (room.SyncRoot) { room.SetReady(actor, ready); return room.Snapshot(actor, Online); }
    }
    public RoomSnapshot ChangeTribe(Guid actor, Guid targetId, string tribeOrRandom)
    {
        var room = ResolveRoom(actor);
        lock (room.SyncRoot) { room.ChangeTribe(actor, targetId, tribeOrRandom); return room.Snapshot(actor, Online); }
    }
    public RoomSnapshot ChangeTeam(Guid actor, Guid targetId, int team)
    {
        var room = ResolveRoom(actor);
        lock (room.SyncRoot) { room.ChangeTeam(actor, targetId, team); return room.Snapshot(actor, Online); }
    }
    public RoomSnapshot ChangeColor(Guid actor, Guid targetId, string color)
    {
        var room = ResolveRoom(actor);
        lock (room.SyncRoot) { room.ChangeColor(actor, targetId, color); return room.Snapshot(actor, Online); }
    }
    public RoomSnapshot ChangeSpawnSlot(Guid actor, Guid targetId, int? slot)
    {
        var room = ResolveRoom(actor);
        lock (room.SyncRoot) { room.ChangeSpawnSlot(actor, targetId, slot); return room.Snapshot(actor, Online); }
    }
    public string? RoomIdFor(Guid actor)
    {
        lock (gate) return memberships.TryGetValue(actor, out var id) ? id : null;
    }
    public SnapshotDelivery[] RoomDeliveries(string? roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return [];
        GameRoom? room;
        Dictionary<Guid, (string[] All, string[] Compressed)> targets = [];
        HashSet<Guid> onlineIds;
        lock (gate)
        {
            if (!rooms.TryGetValue(roomId, out room)) return [];
            lock (connectionGate)
            {
                onlineIds = [.. connections.Values];
                foreach (var group in connections.Where(x => memberships.TryGetValue(x.Value, out var id) && id == roomId).GroupBy(x => x.Value))
                {
                    var all = group.Select(c => c.Key).ToArray();
                    var comp = all.Where(compressedConnections.Contains).ToArray();
                    targets[group.Key] = (all, comp);
                }
            }
        }
        if (targets.Count == 0) return [];
        lock (room.SyncRoot)
        {
            return targets.Select(x => new SnapshotDelivery(x.Value.All, room.Snapshot(x.Key, onlineIds.Contains), x.Value.Compressed)).ToArray();
        }
    }
    public RoomSnapshot Start(Guid actor)
    {
        var room = ResolveRoom(actor);
        lock (room.SyncRoot) { room.Start(actor, Online); return room.Snapshot(actor, Online); }
    }
    public void Leave(Guid actor)
    {
        lock (gate) LeaveLocked(actor);
    }
    private void LeaveLocked(Guid actor)
    {
        if (!memberships.TryGetValue(actor, out var id)) return;
        if (rooms.TryGetValue(id, out var room))
        {
            lock (room.SyncRoot)
            {
                room.Abandon(actor);
                if (!room.ViewerIds.Any() && !room.KeepAliveWhenEmpty)
                {
                    rooms.Remove(room.Id);
                    roomsDirty = true;
                }
            }
        }
        memberships.Remove(actor);
    }
    public CommandResult Execute(Guid actor, GameCommand command)
    {
        var room = ResolveRoom(actor);
        lock (room.SyncRoot) return room.Execute(actor, command);
    }

    public RoomSnapshot? AdminSetSpeed(Guid actor, double multiplier, bool allBotRooms = false)
    {
        if (allBotRooms)
        {
            GameRoom[] botRooms;
            lock (gate)
            {
                RequireAdmin(actor);
                botRooms = rooms.Values.Where(r => r.KeepAliveWhenEmpty).ToArray();
            }
            foreach (var botRoom in botRooms)
                lock (botRoom.SyncRoot) botRoom.AdminSetSpeed(multiplier);
            lock (gate)
            {
                if (!memberships.TryGetValue(actor, out var id) || !rooms.TryGetValue(id, out var viewerRoom))
                    return null;
                lock (viewerRoom.SyncRoot) return viewerRoom.Contains(actor) ? viewerRoom.Snapshot(actor, Online) : null;
            }
        }
        lock (gate)
        {
            RequireAdmin(actor);
            var room = RoomFor(actor);
            lock (room.SyncRoot) { room.AdminSetSpeed(multiplier); return room.Snapshot(actor, Online); }
        }
    }

    public RoomSnapshot AdminGiveResources(Guid actor, Guid targetPlayerId, long wood, long iron, long stone, long wheat)
    {
        lock (gate)
        {
            RequireAdmin(actor);
            var room = RoomFor(actor);
            lock (room.SyncRoot) { room.AdminGiveResources(targetPlayerId, wood, iron, stone, wheat); return room.Snapshot(actor, Online); }
        }
    }

    public RoomSnapshot AdminGiveTroops(Guid actor, Guid targetPlayerId, string unitId, int count)
    {
        lock (gate)
        {
            RequireAdmin(actor);
            var room = RoomFor(actor);
            lock (room.SyncRoot) { room.AdminGiveTroops(targetPlayerId, unitId, count); return room.Snapshot(actor, Online); }
        }
    }

    public RoomSummary[] AdminSpawnBotRooms(Guid actor, int count, int botsPerRoom, BotDifficulty difficulty, double speedMultiplier, int shieldMinutes = 5)
    {
        GameRoom[] snapshot;
        lock (gate)
        {
            const int MaxActiveRooms = 100;
            RequireAdmin(actor);
            if (count is < 1 or > 50) throw new GameRuleException("count", "Bot room count must be between 1 and 50.");
            if (rooms.Count + count > MaxActiveRooms) throw new GameRuleException("count", $"Total active room limit ({MaxActiveRooms}) exceeded.");
            if (botsPerRoom is < 8 or > 16) throw new GameRuleException("capacity", "Bots per room must be between 8 and 16.");
            if (!Enum.IsDefined(difficulty)) throw new GameRuleException("difficulty", "Invalid bot difficulty.");
            if (shieldMinutes is not (0 or 5 or 10)) shieldMinutes = 5;

            var now = time.GetUtcNow();
            var activeNames = CollectActivePlayerNamesLocked();
            var settings = new RoomSettings(botsPerRoom, 60, shieldMinutes, 1, botsPerRoom, difficulty, 45);
            for (var i = 0; i < count; i++)
            {
                var room = GameRoom.CreateBotMatch($"Bot Arena #{++botRoomSeq}", settings, now, speedMultiplier, activeNames);
                rooms.Add(room.Id, room);
            }
            snapshot = [.. rooms.Values];
        }
        return SummarizeVisible(snapshot);
    }

    public RoomSummary[] AdminClearBotRooms(Guid actor)
    {
        GameRoom[] snapshot;
        lock (gate)
        {
            RequireAdmin(actor);
            var botRooms = rooms.Values.Where(r => r.KeepAliveWhenEmpty).ToArray();
            foreach (var room in botRooms) RemoveRoomLocked(room);
            snapshot = [.. rooms.Values];
        }
        return SummarizeVisible(snapshot);
    }

    public AdminOnlinePlayer[] AdminListOnlinePlayers(Guid actor)
    {
        lock (gate)
        {
            RequireAdmin(actor);
            Dictionary<Guid, int> onlineCounts;
            lock (connectionGate)
            {
                onlineCounts = connections.Values
                    .GroupBy(id => id)
                    .ToDictionary(g => g.Key, g => g.Count());
            }

            var list = new List<AdminOnlinePlayer>(onlineCounts.Count);
            foreach (var (playerId, connCount) in onlineCounts)
            {
                var isAdmin = admins.Contains(playerId);
                if (memberships.TryGetValue(playerId, out var roomId) && rooms.TryGetValue(roomId, out var room))
                {
                    lock (room.SyncRoot)
                    {
                        if (room.SpectatorName(playerId) is { } specName)
                        {
                            list.Add(new(playerId, specName, "Spectating", room.Id, room.Name, connCount, isAdmin));
                            continue;
                        }
                        if (room.Players.TryGetValue(playerId, out var rp) && !rp.IsBot)
                        {
                            var loc = room.Status == RoomStatus.Running ? "InMatch" : "InLobby";
                            list.Add(new(playerId, rp.Name, loc, room.Id, room.Name, connCount, isAdmin));
                            continue;
                        }
                    }
                }

                var queued = matchmakingQueue.FirstOrDefault(e => e.PlayerId == playerId);
                if (queued is not null)
                {
                    list.Add(new(playerId, queued.Name, "Matchmaking", null, null, connCount, isAdmin));
                    continue;
                }

                list.Add(new(playerId, string.Empty, "Menu", null, null, connCount, isAdmin));
            }

            static int LocationRank(string loc) => loc switch
            {
                "InMatch" => 0,
                "InLobby" => 1,
                "Matchmaking" => 2,
                "Spectating" => 3,
                _ => 4
            };

            return list
                .OrderBy(p => LocationRank(p.Location))
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.PlayerId)
                .ToArray();
        }
    }

    private void ProcessRoomTick(GameRoom room, double seconds, DateTimeOffset now, HashSet<Guid> onlineIds, ConcurrentBag<GameRoom> toRemove, ref int finishedTransition)
    {
        lock (room.SyncRoot)
        {
            var hasOnlineViewer = room.ViewerIds.Any(onlineIds.Contains);
            if (hasOnlineViewer) room.LastOccupiedAt = now;
            if (room.Status is RoomStatus.Finished or RoomStatus.Faulted)
            {
                room.FinishedAt ??= now;
                if (!hasOnlineViewer || room.FinalBroadcastSent || now - room.FinishedAt.Value > TimeSpan.FromSeconds(5))
                    toRemove.Add(room);
                return;
            }
            if (!room.KeepAliveWhenEmpty && now - room.LastOccupiedAt > TimeSpan.FromMinutes(10))
            {
                toRemove.Add(room);
                return;
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
                Volatile.Write(ref finishedTransition, 1);
                if (!hasOnlineViewer)
                    toRemove.Add(room);
            }
        }
    }

    public MatchmakingStatus StartMatchmaking(Guid actor, string playerName, TribeType? tribe)
    {
        var cleanName = string.IsNullOrWhiteSpace(playerName) ? "Commander" : playerName.Trim();
        if (cleanName.Length > 32 || cleanName.Any(char.IsControl))
            throw new GameRuleException("name", "Names must contain 1-32 visible characters.");

        lock (gate)
        {
            if (memberships.ContainsKey(actor)) LeaveLocked(actor);
            matchmakingQueue.RemoveAll(e => e.PlayerId == actor);
            var now = time.GetUtcNow();
            var waitSeconds = 3.0 + Random.Shared.NextDouble() * 12.0;
            matchmakingQueue.Add(new(actor, cleanName, tribe, now, now.AddSeconds(waitSeconds)));
            if (matchmakingQueue.Count >= 8)
                ProcessMatchmakingLocked(now);
            return new(matchmakingQueue.Any(e => e.PlayerId == actor), matchmakingQueue.Count);
        }
    }

    public void CancelMatchmaking(Guid actor)
    {
        lock (gate) matchmakingQueue.RemoveAll(e => e.PlayerId == actor);
    }

    public SnapshotDelivery[] ConsumeMatchFoundDeliveries()
    {
        lock (gate)
        {
            if (pendingMatchFound.Count == 0) return [];
            var result = pendingMatchFound.ToArray();
            pendingMatchFound.Clear();
            return result;
        }
    }

    private void ProcessMatchmakingLocked(DateTimeOffset now)
    {
        if (matchmakingQueue.Count == 0) return;
        var onlineIds = OnlineIds();
        matchmakingQueue.RemoveAll(e => !onlineIds.Contains(e.PlayerId));
        if (matchmakingQueue.Count == 0) return;

        while (matchmakingQueue.Count > 0)
        {
            var readyByCount = matchmakingQueue.Count >= 8;
            var readyByTimer = matchmakingQueue.Any(e => e.FillAt <= now);
            if (!readyByCount && !readyByTimer) break;

            var take = Math.Min(8, matchmakingQueue.Count);
            var batch = matchmakingQueue.Take(take).ToList();
            matchmakingQueue.RemoveRange(0, take);

            var existingNames = rooms.Values.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var roomName = BotNames.PickRoomName(existingNames);
            var botCount = Math.Max(0, 8 - batch.Count);
            var settings = new RoomSettings(
                Capacity: 8,
                DurationMinutes: 60,
                ShieldMinutes: 5,
                ResourceSpeed: 1,
                BotCount: botCount,
                BotDifficulty: BotDifficulty.Mixed,
                CastleUnlockMinutes: 45);
            var humans = batch.Select(e => (e.PlayerId, e.Name, e.Tribe)).ToArray();
            var activeNames = CollectActivePlayerNamesLocked();
            var room = GameRoom.CreateMatchmakingMatch(roomName, humans, settings, now, activeNames);

            foreach (var entry in batch)
            {
                if (memberships.ContainsKey(entry.PlayerId)) LeaveLocked(entry.PlayerId);
                memberships[entry.PlayerId] = room.Id;
            }
            rooms.Add(room.Id, room);
            roomsDirty = true;

            lock (room.SyncRoot)
            {
                Dictionary<Guid, (string[] All, string[] Compressed)> targets = [];
                HashSet<Guid> currentOnline;
                lock (connectionGate)
                {
                    currentOnline = [.. connections.Values];
                    var matchedIds = batch.Select(b => b.PlayerId).ToHashSet();
                    foreach (var group in connections.Where(c => matchedIds.Contains(c.Value)).GroupBy(c => c.Value))
                    {
                        var all = group.Select(c => c.Key).ToArray();
                        var comp = all.Where(compressedConnections.Contains).ToArray();
                        targets[group.Key] = (all, comp);
                    }
                }
                foreach (var entry in targets)
                    pendingMatchFound.Add(new(entry.Value.All, room.Snapshot(entry.Key, currentOnline.Contains), entry.Value.Compressed));
            }
        }
    }

    public void MaintainAmbientRooms(bool forceImmediate = false)
    {
        if (!AmbientRoomsEnabled && !forceImmediate) return;
        lock (gate) MaintainAmbientRoomsLocked(time.GetUtcNow(), forceImmediate);
    }

    private void MaintainAmbientRoomsLocked(DateTimeOffset now, bool forceImmediate)
    {
        if (!forceImmediate && now < nextAmbientCheck) return;
        nextAmbientCheck = now.AddSeconds(5);

        if (ambientTargetCount is < 5 or > 15 || now >= nextAmbientTargetShift)
        {
            ambientTargetCount = Random.Shared.Next(5, 16); // Always 5..15 active games randomly
            nextAmbientTargetShift = now.AddSeconds(120 + Random.Shared.Next(121));
        }

        var visibleCount = 0;
        var ambientUnwatched = new List<GameRoom>();
        foreach (var room in rooms.Values)
        {
            lock (room.SyncRoot)
            {
                if (room.Status is RoomStatus.Lobby or RoomStatus.Running)
                {
                    visibleCount++;
                    if (room.IsAmbient && !room.ViewerIds.Any())
                        ambientUnwatched.Add(room);
                }
            }
        }

        if (visibleCount < ambientTargetCount)
        {
            var needed = ambientTargetCount - visibleCount;
            // On startup or whenever below the 5-game floor, fill immediately; otherwise add 1-2 per cycle organically.
            var toSpawn = forceImmediate || visibleCount < 5 ? needed : Math.Min(needed, 2);
            var existingNames = rooms.Values.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var activePlayerNames = CollectActivePlayerNamesLocked();

            for (var i = 0; i < toSpawn; i++)
            {
                if (rooms.Count >= 90) break;
                var capRoll = Random.Shared.Next(100);
                var capacity = capRoll < 65 ? 8 : capRoll < 88 ? 10 : 12;
                var durRoll = Random.Shared.Next(3);
                var duration = durRoll == 0 ? 30 : durRoll == 1 ? 45 : 60;
                var castleUnlock = duration == 30 ? 20 : duration == 45 ? 30 : 45;
                var speedRoll = Random.Shared.Next(100);
                var resourceSpeed = speedRoll < 70 ? 1.0 : speedRoll < 90 ? 2.0 : 3.0;

                var settings = new RoomSettings(
                    Capacity: capacity,
                    DurationMinutes: duration,
                    ShieldMinutes: 5,
                    ResourceSpeed: resourceSpeed,
                    BotCount: capacity,
                    BotDifficulty: BotDifficulty.Mixed,
                    CastleUnlockMinutes: castleUnlock);
                var roomName = BotNames.PickRoomName(existingNames);
                var room = GameRoom.CreateAmbientMatch(roomName, settings, now, speedMultiplier: 1, reservedNames: activePlayerNames);
                rooms.Add(room.Id, room);
                roomsDirty = true;
            }
        }
        else if (visibleCount > 15 && ambientUnwatched.Count > 0)
        {
            // Keep total ambient-backed active count capped naturally within 5..15 if target drifted down and excess unwatched ambient rooms exist.
            var excess = Math.Min(visibleCount - 15, ambientUnwatched.Count);
            for (var i = 0; i < excess; i++)
                RemoveRoomLocked(ambientUnwatched[i]);
        }
    }

    public void Tick(double seconds)
    {
        var now = time.GetUtcNow();
        GameRoom[] activeRooms;
        lock (gate)
        {
            ProcessMatchmakingLocked(now);
            if (AmbientRoomsEnabled)
                MaintainAmbientRoomsLocked(now, forceImmediate: false);
            activeRooms = [.. rooms.Values];
        }
        if (activeRooms.Length == 0) return;

        var onlineIds = OnlineIds();
        var toRemove = new ConcurrentBag<GameRoom>();
        var finishedTransition = 0;

        if (activeRooms.Length == 1)
        {
            ProcessRoomTick(activeRooms[0], seconds, now, onlineIds, toRemove, ref finishedTransition);
        }
        else
        {
            Parallel.ForEach(activeRooms, RoomParallelOptions, room =>
            {
                var finished = 0;
                ProcessRoomTick(room, seconds, now, onlineIds, toRemove, ref finished);
                if (finished != 0) Volatile.Write(ref finishedTransition, 1);
            });
        }

        if (toRemove.IsEmpty && finishedTransition == 0) return;
        lock (gate)
        {
            if (finishedTransition != 0) roomsDirty = true;
            foreach (var room in toRemove)
            {
                if (!rooms.TryGetValue(room.Id, out var current) || !ReferenceEquals(current, room)) continue;
                bool stillRemovable;
                lock (room.SyncRoot)
                {
                    var currentOnline = OnlineIds();
                    var hasOnline = room.ViewerIds.Any(currentOnline.Contains);
                    stillRemovable = room.Status is RoomStatus.Finished or RoomStatus.Faulted
                        ? (!hasOnline || room.FinalBroadcastSent || (room.FinishedAt is { } fin && now - fin > TimeSpan.FromSeconds(5)))
                        : (!room.KeepAliveWhenEmpty && now - room.LastOccupiedAt > TimeSpan.FromMinutes(10) && !hasOnline);
                }
                if (stillRemovable) RemoveRoomLocked(room);
            }
        }
    }
    public SnapshotDelivery[] Deliveries(bool markFinalBroadcastSent = true)
    {
        Dictionary<GameRoom, List<(Guid PlayerId, string[] All, string[] Compressed)>> byRoom = [];
        HashSet<Guid> onlineIds;
        lock (gate)
        {
            lock (connectionGate)
            {
                if (connections.Count == 0) return [];
                onlineIds = [.. connections.Values];
                foreach (var group in connections.GroupBy(x => x.Value))
                {
                    if (!memberships.TryGetValue(group.Key, out var roomId) || !rooms.TryGetValue(roomId, out var room))
                        continue;
                    if (!byRoom.TryGetValue(room, out var list)) byRoom[room] = list = [];
                    var all = group.Select(c => c.Key).ToArray();
                    var comp = all.Where(compressedConnections.Contains).ToArray();
                    list.Add((group.Key, all, comp));
                }
            }
        }
        if (byRoom.Count == 0) return [];
        if (byRoom.Count == 1)
        {
            var (room, viewers) = byRoom.First();
            lock (room.SyncRoot)
            {
                if (markFinalBroadcastSent && room.Status is RoomStatus.Finished or RoomStatus.Faulted) room.FinalBroadcastSent = true;
                return viewers.Select(v => new SnapshotDelivery(v.All, room.Snapshot(v.PlayerId, onlineIds.Contains), v.Compressed)).ToArray();
            }
        }
        var output = new ConcurrentBag<SnapshotDelivery>();
        Parallel.ForEach(byRoom, RoomParallelOptions, entry =>
        {
            var (room, viewers) = entry;
            lock (room.SyncRoot)
            {
                if (markFinalBroadcastSent && room.Status is RoomStatus.Finished or RoomStatus.Faulted) room.FinalBroadcastSent = true;
                foreach (var v in viewers)
                    output.Add(new SnapshotDelivery(v.All, room.Snapshot(v.PlayerId, onlineIds.Contains), v.Compressed));
            }
        });
        return [.. output];
    }

    internal void MarkFinalBroadcastSent(IEnumerable<string> roomIds)
    {
        lock (gate)
        {
            foreach (var id in roomIds)
            {
                if (!rooms.TryGetValue(id, out var room)) continue;
                lock (room.SyncRoot)
                {
                    if (room.Status is RoomStatus.Finished or RoomStatus.Faulted)
                        room.FinalBroadcastSent = true;
                }
            }
        }
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
        GameRoom[] roomList;
        int connectedClients;
        lock (gate) roomList = [.. rooms.Values];
        lock (connectionGate) connectedClients = connections.Count;
        var runningRooms = 0;
        foreach (var room in roomList)
            lock (room.SyncRoot) if (room.Status == RoomStatus.Running) runningRooms++;
        var activeRooms = roomList.Length;
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
            await Task.WhenAll(deliveries.Select(x => SnapshotCodec.SendDeliveryAsync(hub.Clients, x, cancellationToken: timeout.Token)));
            var finalRooms = deliveries
                .Where(x => x.Snapshot.Status is RoomStatus.Finished or RoomStatus.Faulted)
                .Select(x => x.Snapshot.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (finalRooms.Length > 0) server.MarkFinalBroadcastSent(finalRooms);
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
        if (server.AmbientRoomsEnabled)
            server.MaintainAmbientRooms(forceImmediate: true);
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
                if (steps > 0)
                {
                    var matchFound = server.ConsumeMatchFoundDeliveries();
                    if (matchFound.Length > 0)
                        _ = Task.WhenAll(matchFound.Select(d => hub.Clients.Clients(d.Connections).SendAsync("MatchFound", d.Snapshot, stoppingToken)));
                    if (server.ConsumeRoomsUpdated() is { } updatedRooms)
                        _ = hub.Clients.All.SendAsync("RoomsUpdated", updatedRooms, stoppingToken);
                }
                ticksSinceBroadcast += steps;
                if (ticksSinceBroadcast < 4) continue;
                ticksSinceBroadcast %= 4;
                if (Interlocked.CompareExchange(ref broadcastInFlight, 1, 0) != 0) continue;
                var broadcastStart = time.GetTimestamp();
                var deliveries = server.Deliveries(markFinalBroadcastSent: false);
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