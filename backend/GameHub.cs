using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Warzone;

// Actor identity always comes from the signed guest cookie, never from a command.
[Authorize]
public sealed class GameHub(GameServer server) : Hub
{
    private Guid Actor => Guid.Parse(Context.UserIdentifier!);
    public override Task OnConnectedAsync()
    {
        try { server.Connect(Context.ConnectionId, Actor); }
        catch (GameRuleException) { Context.Abort(); throw; }
        return base.OnConnectedAsync();
    }
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        server.Disconnect(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
    public RoomSummary[] ListRooms() => server.ListRooms();
    public RoomSnapshot? Resume() => server.Resume(Actor);
    public bool CheckAdmin() => server.IsAdmin(Actor);
    public bool EnableCheat(string password) => server.EnableCheat(Actor, password);

    private static TribeType? ParseTribe(string tribe)
    {
        if (string.Equals(tribe, "Random", StringComparison.OrdinalIgnoreCase)) return null;
        if (!int.TryParse(tribe, out _) && Enum.TryParse<TribeType>(tribe, true, out var parsed) && Enum.IsDefined(parsed))
            return parsed;
        throw new GameRuleException("player", "Invalid player or tribe.");
    }

    public async Task<RoomSnapshot> CreateRoom(string playerName, string tribe, string roomName, RoomSettings settings)
    {
        var snapshot = server.Create(Actor, playerName, ParseTribe(tribe), roomName, settings);
        await Clients.All.SendAsync("RoomsUpdated", server.ListRooms());
        return snapshot;
    }

    private async Task BroadcastRoomAsync(string? roomId, string? excludeConnectionId = null)
    {
        var deliveries = server.RoomDeliveries(roomId);
        if (deliveries.Length == 0) return;
        await Task.WhenAll(deliveries.Select(d =>
        {
            var targets = excludeConnectionId is null
                ? d.Connections
                : d.Connections.Where(c => c != excludeConnectionId).ToArray();
            return targets.Length == 0 ? Task.CompletedTask : Clients.Clients(targets).SendAsync("Snapshot", d.Snapshot);
        }));
    }

    public async Task<RoomSnapshot> JoinRoom(string roomId, string playerName, string tribe)
    {
        var snapshot = server.Join(Actor, roomId, playerName, ParseTribe(tribe));
        await Clients.All.SendAsync("RoomsUpdated", server.ListRooms());
        await BroadcastRoomAsync(snapshot.Id, Context.ConnectionId);
        return snapshot;
    }

    public async Task<RoomSnapshot> SpectateRoom(string roomId, string playerName)
    {
        var snapshot = server.Spectate(Actor, roomId, playerName);
        await Clients.All.SendAsync("RoomsUpdated", server.ListRooms());
        await BroadcastRoomAsync(snapshot.Id, Context.ConnectionId);
        return snapshot;
    }

    public async Task<RoomSnapshot> SwitchSpectator(bool spectate, string tribe = "Random")
    {
        var snapshot = server.SwitchSpectator(Actor, spectate, ParseTribe(tribe));
        await Clients.All.SendAsync("RoomsUpdated", server.ListRooms());
        await BroadcastRoomAsync(snapshot.Id, Context.ConnectionId);
        return snapshot;
    }

    public async Task<RoomSnapshot> ConfigureRoom(RoomSettings settings)
    {
        var snapshot = server.Configure(Actor, settings);
        await Clients.All.SendAsync("RoomsUpdated", server.ListRooms());
        await BroadcastRoomAsync(snapshot.Id, Context.ConnectionId);
        return snapshot;
    }

    public async Task<RoomSnapshot> SetReady(bool ready)
    {
        var snapshot = server.Ready(Actor, ready);
        await BroadcastRoomAsync(snapshot.Id, Context.ConnectionId);
        return snapshot;
    }
    public async Task<RoomSnapshot> ChangeTribe(Guid targetId, string tribeOrRandom)
    {
        var snapshot = server.ChangeTribe(Actor, targetId, tribeOrRandom);
        await BroadcastRoomAsync(snapshot.Id, Context.ConnectionId);
        return snapshot;
    }
    public async Task<RoomSnapshot> ChangeTeam(Guid targetId, int team)
    {
        var snapshot = server.ChangeTeam(Actor, targetId, team);
        await BroadcastRoomAsync(snapshot.Id, Context.ConnectionId);
        return snapshot;
    }
    public async Task<RoomSnapshot> ChangeColor(Guid targetId, string color)
    {
        var snapshot = server.ChangeColor(Actor, targetId, color);
        await BroadcastRoomAsync(snapshot.Id, Context.ConnectionId);
        return snapshot;
    }
    public async Task<RoomSnapshot> ChangeSpawnSlot(Guid targetId, int slot)
    {
        var snapshot = server.ChangeSpawnSlot(Actor, targetId, slot < 0 ? null : slot);
        await BroadcastRoomAsync(snapshot.Id, Context.ConnectionId);
        return snapshot;
    }

    public async Task<RoomSnapshot> StartMatch()
    {
        var snapshot = server.Start(Actor);
        await Clients.All.SendAsync("RoomsUpdated", server.ListRooms());
        await BroadcastRoomAsync(snapshot.Id, Context.ConnectionId);
        return snapshot;
    }

    public async Task LeaveRoom()
    {
        var roomId = server.RoomIdFor(Actor);
        server.Leave(Actor);
        await Clients.All.SendAsync("RoomsUpdated", server.ListRooms());
        await BroadcastRoomAsync(roomId, Context.ConnectionId);
    }

    public CommandResult SendCommand(GameCommand command) => server.Execute(Actor, command);
    public ServerDebugTelemetry Ping(double clientTimestamp) => server.GetTelemetry(clientTimestamp);

    public async Task<RoomSnapshot?> AdminSetSpeed(double multiplier, bool allBotRooms = false)
    {
        var snapshot = server.AdminSetSpeed(Actor, multiplier, allBotRooms);
        await Clients.All.SendAsync("RoomsUpdated", server.ListRooms());
        if (snapshot is not null) await BroadcastRoomAsync(snapshot.Id, Context.ConnectionId);
        return snapshot;
    }

    public async Task<RoomSnapshot> AdminGiveResources(Guid targetPlayerId, long wood, long iron, long stone, long wheat)
    {
        var snapshot = server.AdminGiveResources(Actor, targetPlayerId, wood, iron, stone, wheat);
        await BroadcastRoomAsync(snapshot.Id, Context.ConnectionId);
        return snapshot;
    }

    public async Task<RoomSnapshot> AdminGiveTroops(Guid targetPlayerId, string unitId, int count)
    {
        var snapshot = server.AdminGiveTroops(Actor, targetPlayerId, unitId, count);
        await BroadcastRoomAsync(snapshot.Id, Context.ConnectionId);
        return snapshot;
    }

    public async Task<RoomSummary[]> AdminSpawnBotRooms(int count, int botsPerRoom, BotDifficulty difficulty, double speedMultiplier, int shieldMinutes = 5)
    {
        var rooms = server.AdminSpawnBotRooms(Actor, count, botsPerRoom, difficulty, speedMultiplier, shieldMinutes);
        await Clients.All.SendAsync("RoomsUpdated", rooms);
        return rooms;
    }

    public async Task<RoomSummary[]> AdminClearBotRooms()
    {
        var rooms = server.AdminClearBotRooms(Actor);
        await Clients.All.SendAsync("RoomsUpdated", rooms);
        return rooms;
    }
}

public sealed class GameHubFilter(GameServer server) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            server.CheckRate(context.Context.ConnectionId);
            if (context.HubMethodArguments.Any(x => x is null)) throw new GameRuleException("argument", "Arguments cannot be null.");
            return await next(context);
        }
        catch (GameRuleException error) { throw new HubException($"{error.Code}: {error.Message}"); }
    }
}