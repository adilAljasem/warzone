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
    public RoomSnapshot CreateRoom(string playerName, TribeType tribe, string roomName, RoomSettings settings)
        => server.Create(Actor, playerName, tribe, roomName, settings);
    public RoomSnapshot JoinRoom(string roomId, string playerName, TribeType tribe) => server.Join(Actor, roomId, playerName, tribe);
    public RoomSnapshot ConfigureRoom(RoomSettings settings) => server.Configure(Actor, settings);
    public RoomSnapshot SetReady(bool ready) => server.Ready(Actor, ready);
    public RoomSnapshot StartMatch() => server.Start(Actor);
    public void LeaveRoom() => server.Leave(Actor);
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
