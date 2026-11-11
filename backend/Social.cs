namespace Warzone;

public sealed record ChatMessageSnapshot(long Id, Guid? SenderId, string Text, double At, string Kind);
public sealed record AllianceSnapshot(Guid FirstPlayerId, Guid SecondPlayerId, double Since);
public sealed record AllianceRequestSnapshot(Guid Id, Guid SenderId, Guid RecipientId, double CreatedAt);
internal sealed record AllianceRequest(Guid Id, Guid SenderId, Guid RecipientId, double CreatedAt);

public sealed partial class GameRoom
{
    private readonly List<ChatMessageSnapshot> chatMessages = [];
    private readonly List<AllianceRequest> allianceRequests = [];
    private readonly Dictionary<(Guid First, Guid Second), double> alliances = [];
    private long chatMessageId;

    private static (Guid First, Guid Second) AlliancePair(Guid first, Guid second) =>
        first.CompareTo(second) < 0 ? (first, second) : (second, first);

    private void AddChat(Guid? sender, string text, string kind = "Player")
    {
        chatMessages.Add(new(++chatMessageId, sender, text, ElapsedSeconds, kind));
        if (chatMessages.Count > 120) chatMessages.RemoveAt(0);
    }

    private bool ApplySocial(Guid actor, GameCommand command)
    {
        if (command.Kind == CommandKind.SendChat)
        {
            var text = command.Message?.Trim() ?? "";
            if (text.Length is < 1 or > 300 || text.Any(char.IsControl))
                throw new GameRuleException("chat", "Messages must contain 1-300 visible characters.");
            AddChat(actor, text);
            return true;
        }
        if (command.Kind == CommandKind.ProposeAlliance)
        {
            if (!players.TryGetValue(command.TargetId, out var recipient) || recipient.IsBot || command.TargetId == actor)
                throw new GameRuleException("alliance", "Choose another human commander.");
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
            if (!command.Enabled) return true;
            var pair = AlliancePair(request.SenderId, request.RecipientId);
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
    private bool CampAllied(Guid first, Guid second) => first == second || campAlliances.Contains(first.CompareTo(second) < 0
        ? (first, second) : (second, first));
}
