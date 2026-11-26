namespace Warzone;

public sealed record ChatMessageSnapshot(long Id, Guid? SenderId, string Text, double At, string Kind, string? SenderName = null);
public sealed record AllianceSnapshot(Guid FirstPlayerId, Guid SecondPlayerId, double Since);
public sealed record AllianceRequestSnapshot(Guid Id, Guid SenderId, Guid RecipientId, double CreatedAt);
internal sealed record AllianceRequest(Guid Id, Guid SenderId, Guid RecipientId, double CreatedAt);

public sealed partial class GameRoom
{
    private readonly List<ChatMessageSnapshot> chatMessages = [];
    private readonly List<AllianceRequest> allianceRequests = [];
    private readonly Dictionary<(Guid First, Guid Second), double> alliances = [];
    private readonly Dictionary<(Guid First, Guid Second), double> declinedAlliances = [];
    private long chatMessageId;

    private static (Guid First, Guid Second) AlliancePair(Guid first, Guid second) =>
        first.CompareTo(second) < 0 ? (first, second) : (second, first);

    internal AllianceRequest[] PendingRequestsFor(Guid recipientId) =>
        allianceRequests.Where(r => r.RecipientId == recipientId).ToArray();

    internal int PendingIncomingCount(Guid recipientId) =>
        allianceRequests.Count(r => r.RecipientId == recipientId);

    internal bool RecentlyDeclinedAlliance(Guid first, Guid second) =>
        declinedAlliances.TryGetValue(AlliancePair(first, second), out var until) && ElapsedSeconds < until;

    public int AllianceCount(Guid playerId) =>
        alliances.Keys.Count(pair => pair.First == playerId || pair.Second == playerId);

    public bool IsAllied(Guid first, Guid second) =>
        first == second || alliances.ContainsKey(AlliancePair(first, second));

    public bool HasPendingRequest(Guid first, Guid second) =>
        allianceRequests.Any(r => (r.SenderId == first && r.RecipientId == second) || (r.SenderId == second && r.RecipientId == first));

    private void AddChat(Guid? sender, string text, string kind = "Player", string? senderName = null)
    {
        var resolvedName = senderName ?? (sender is { } id
            ? (players.TryGetValue(id, out var p) ? p.Name : spectators.TryGetValue(id, out var sName) ? sName : null)
            : null);
        chatMessages.Add(new(++chatMessageId, sender, text, ElapsedSeconds, kind, resolvedName));
        if (chatMessages.Count > 120) chatMessages.RemoveAt(0);
    }

    private void InitializeTeamAlliances()
    {
        var groupedByTeam = players.Values
            .Where(p => p.Team is >= 1 and <= 4)
            .GroupBy(p => p.Team)
            .Where(g => g.Count() > 1);

        foreach (var group in groupedByTeam)
        {
            var members = group.ToList();
            for (var i = 0; i < members.Count; i++)
            {
                for (var j = i + 1; j < members.Count; j++)
                {
                    var pair = AlliancePair(members[i].Id, members[j].Id);
                    alliances.TryAdd(pair, ElapsedSeconds);
                }
            }
        }
    }

    private bool ApplySocial(Guid actor, GameCommand command)
    {
        if (command.Kind == CommandKind.SendChat)
        {
            var text = command.Message?.Trim() ?? "";
            if (text.Length is < 1 or > 300 || text.Any(char.IsControl))
                throw new GameRuleException("chat", "Messages must contain 1-300 visible characters.");
            if (text.StartsWith('/'))
                throw new GameRuleException("chat", "Slash commands cannot be sent to chat.");
            var authorName = players.TryGetValue(actor, out var p) ? p.Name
                : spectators.TryGetValue(actor, out var sName) ? $"{sName} (Spectator)" : null;
            AddChat(actor, text, senderName: authorName);
            return true;
        }
        if (command.Kind == CommandKind.ProposeAlliance)
        {
            if (!players.TryGetValue(command.TargetId, out var recipient) || command.TargetId == actor)
                throw new GameRuleException("alliance", "Choose another commander.");
            if (alliances.ContainsKey(AlliancePair(actor, command.TargetId)))
                throw new GameRuleException("alliance", "You are already allied with this commander.");
            if (allianceRequests.Any(r => r.SenderId == actor && r.RecipientId == command.TargetId))
                throw new GameRuleException("alliance", "An alliance request is already pending for this commander.");
            if (allianceRequests.Any(r => r.SenderId == command.TargetId && r.RecipientId == actor))
                throw new GameRuleException("alliance", "This commander has already invited you. Accept their request instead.");
            allianceRequests.Add(new(Guid.NewGuid(), actor, command.TargetId, ElapsedSeconds));
            return true;
        }
        if (command.Kind == CommandKind.RespondAlliance)
        {
            var request = allianceRequests.FirstOrDefault(r => r.Id == command.TargetId && r.RecipientId == actor)
                ?? throw new GameRuleException("alliance", "This alliance request is no longer available.");
            allianceRequests.Remove(request);
            var pair = AlliancePair(request.SenderId, request.RecipientId);
            if (!command.Enabled)
            {
                declinedAlliances[pair] = ElapsedSeconds + 180;
                return true;
            }
            declinedAlliances.Remove(pair);
            if (alliances.TryAdd(pair, ElapsedSeconds))
                AddChat(null, $"Alliance formed between {players[request.SenderId].Name} and {players[request.RecipientId].Name}.", "Alliance");
            return true;
        }
        if (command.Kind == CommandKind.CancelAllianceRequest)
        {
            var request = allianceRequests.FirstOrDefault(r => r.Id == command.TargetId && r.SenderId == actor)
                ?? throw new GameRuleException("alliance", "This alliance request is no longer available.");
            allianceRequests.Remove(request);
            return true;
        }
        if (command.Kind == CommandKind.BreakAlliance)
        {
            if (!players.TryGetValue(command.TargetId, out var formerAlly) || command.TargetId == actor)
                throw new GameRuleException("alliance", "Choose an allied commander.");
            var actorPlayer = players[actor];
            if (actorPlayer.Team > 0 && actorPlayer.Team == formerAlly.Team)
                throw new GameRuleException("alliance", "Fixed team alliances cannot be broken during the match.");
            var pair = AlliancePair(actor, command.TargetId);
            if (!alliances.Remove(pair))
                throw new GameRuleException("alliance", "You are not allied with this commander.");
            declinedAlliances[pair] = ElapsedSeconds + 120;
            AddChat(null, $"Alliance broken! {actorPlayer.Name} betrayed {formerAlly.Name}.", "Betrayal");
            return true;
        }
        return false;
    }

    public ChatMessageSnapshot[] ChatView() => chatMessages.TakeLast(100).ToArray();
    public AllianceSnapshot[] AllianceView() => alliances
        .Select(pair => new AllianceSnapshot(pair.Key.First, pair.Key.Second, pair.Value)).ToArray();
    public AllianceRequestSnapshot[] AllianceRequestView(Guid viewer) => allianceRequests
        .Where(r => r.SenderId == viewer || r.RecipientId == viewer)
        .Select(r => new AllianceRequestSnapshot(r.Id, r.SenderId, r.RecipientId, r.CreatedAt)).ToArray();
}

public sealed partial class LogisticsWorld
{
    private readonly HashSet<(Guid First, Guid Second)> campAlliances = [];
    public void SetAlliances(IEnumerable<AllianceSnapshot> current)
    {
        campAlliances.Clear();
        foreach (var alliance in current) campAlliances.Add(alliance.FirstPlayerId.CompareTo(alliance.SecondPlayerId) < 0
            ? (alliance.FirstPlayerId, alliance.SecondPlayerId) : (alliance.SecondPlayerId, alliance.FirstPlayerId));
    }
    public void RecallBrokenAllianceSupport(Guid first, Guid second, double now)
    {
        foreach (var army in armies.Where(a => !Engaged(a) && !a.Routing
            && a.Order is "Reinforcing" or "Guarding"
            && ((a.OwnerId == first && a.SupportedOwnerId == second) || (a.OwnerId == second && a.SupportedOwnerId == first))).ToArray())
        {
            Route(army);
            Note(army.OwnerId, now, "Alliance broken: your stationed reinforcements are returning home.", "war");
        }
        Note(second, now, "Your ally broke the treaty! Guard your borders!", "war");
    }
    private bool CampAllied(Guid first, Guid second) => first == second || campAlliances.Contains(first.CompareTo(second) < 0
        ? (first, second) : (second, first));
}