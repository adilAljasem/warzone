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

    public async Task<RoomSnapshot> JoinRoom(string roomId, string playerName, string tribe)
    {
        var snapshot = server.Join(Actor, roomId, playerName, ParseTribe(tribe));
        await Clients.All.SendAsync("RoomsUpdated", server.ListRooms());
        return snapshot;
    }

    public async Task<RoomSnapshot> ConfigureRoom(RoomSettings settings)
    {
        var snapshot = server.Configure(Actor, settings);
        await Clients.All.SendAsync("RoomsUpdated", server.ListRooms());
        return snapshot;
    }

    public RoomSnapshot SetReady(bool ready) => server.Ready(Actor, ready);
    public RoomSnapshot ChangeTribe(Guid targetId, string tribeOrRandom) => server.ChangeTribe(Actor, targetId, tribeOrRandom);
    public RoomSnapshot ChangeTeam(Guid targetId, int team) => server.ChangeTeam(Actor, targetId, team);

    public async Task<RoomSnapshot> StartMatch()
    {
        var snapshot = server.Start(Actor);
        await Clients.All.SendAsync("RoomsUpdated", server.ListRooms());
        return snapshot;
    }

    public async Task LeaveRoom()
    {
        server.Leave(Actor);
        await Clients.All.SendAsync("RoomsUpdated", server.ListRooms());
    }

    public CommandResult SendCommand(GameCommand command) => server.Execute(Actor, command);
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