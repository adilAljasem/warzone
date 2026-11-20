using System.Text.Json;

namespace Warzone;

public enum RoomStatus { Lobby, Running, Finished, Faulted }
public enum BotPersonality { Aggressive, Economic, Defensive }
public enum BotDifficulty { Easy, Normal, Hard, Mixed }
public sealed record RoomSettings(int Capacity = 8, int DurationMinutes = 60, int ShieldMinutes = 5,
    double ResourceSpeed = 1, int BotCount = 7, BotDifficulty BotDifficulty = BotDifficulty.Normal, int CastleUnlockMinutes = 45)
{
    public void Validate(bool allowBotOnly = false)
    {
        var maxBots = allowBotOnly ? Capacity : Capacity - 1;
        if (Capacity is < 8 or > 16 || DurationMinutes is < 30 or > 999 || CastleUnlockMinutes is < 1 or > 999
            || ShieldMinutes is not (0 or 5 or 10) || !double.IsFinite(ResourceSpeed) || ResourceSpeed is < .5 or > 5
            || BotCount < 0 || BotCount > maxBots || !Enum.IsDefined(BotDifficulty))
            throw new GameRuleException("settings", "Invalid room settings: capacity 8-16, duration 30-999, castle unlock 1-999, shield 0/5/10, speed 0.5-5.");
    }
}

public sealed record PlayerSnapshot(Guid Id, string Name, TribeType Tribe, bool IsBot, BotPersonality? Personality,
    bool Ready, bool Online, Guid? VillageId, Position? Position, int? Level, string FlagColor, int Team = 0, bool RandomTribe = false, int? SpawnSlot = null, int? EffectiveSpawnSlot = null);
public sealed record SpectatorSnapshot(Guid Id, string Name);
public sealed record RoomSummary(string Id, string Name, RoomStatus Status, int Humans, int Bots, int Capacity, int Spectators = 0, double SpeedMultiplier = 1, bool IsBotOnly = false, bool MaskBots = false, bool IsAmbient = false);
public sealed record RoomSnapshot(string Id, string Name, Guid HostId, Guid You, long Revision, RoomStatus Status,
    RoomSettings Settings, double ElapsedSeconds, double RemainingSeconds, double ShieldRemainingSeconds,
    string Phase, string? EndReason, PlayerSnapshot[] Players, EconomySnapshot? Economy, WorldSnapshot? World, MatchResult? Result,
    ChatMessageSnapshot[] Chat, AllianceSnapshot[] Alliances, AllianceRequestSnapshot[] AllianceRequests,
    bool IsSpectator = false, SpectatorSnapshot[]? Spectators = null, double SpeedMultiplier = 1, bool MaskBots = false);

public interface IGameCommands
{
    CommandResult Execute(Guid actorId, GameCommand command);
}

internal sealed class RoomPlayer(Guid id, string name, TribeType tribe, string flagColor, BotPersonality? personality = null, BotDifficulty? difficulty = null)
{
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    public TribeType Tribe { get; set; } = tribe;
    public bool RandomTribe { get; set; }
    public int Team { get; set; } = 0;
    public int? SpawnSlot { get; set; }
    public string FlagColor { get; set; } = flagColor;
    public BotPersonality? Personality { get; set; } = personality;
    public BotDifficulty? Difficulty { get; set; } = difficulty;
    public BotDifficulty EffectiveDifficulty(BotDifficulty roomDifficulty) =>
        Difficulty ?? (roomDifficulty == BotDifficulty.Mixed ? BotDifficulty.Normal : roomDifficulty);
    public bool IsBot => Personality.HasValue;
    public bool Ready { get; set; }
    public VillageEconomy? Economy { get; set; }
    public double NextBotAction { get; set; }
    public double NextSocialAction { get; set; }
    public Dictionary<Guid, double> PendingResponseAt { get; } = [];
    public Guid? SelectedVillageId { get; set; }
    public Dictionary<Guid, (string Fingerprint, CommandResult Result)> Receipts { get; } = [];
    public Queue<Guid> ReceiptOrder { get; } = [];
}

// Each room owns its own SyncRoot lock; GameServer runs separate rooms concurrently. No network calls occur inside it.
public sealed partial class GameRoom : IGameCommands
{
    internal readonly object SyncRoot = new();
    private static readonly string[] FlagPalette = ["#e11d48", "#2563eb", "#facc15", "#9333ea", "#06b6d4", "#f97316", "#f8fafc", "#18181b", "#ec4899", "#10b981", "#78350f", "#84cc16", "#1e3a8a", "#0f766e", "#f43f5e", "#d97706"];
    private readonly Dictionary<Guid, RoomPlayer> players = [];
    private readonly Dictionary<Guid, string> spectators = [];
    private LogisticsWorld? world;
    private string NextFlagColor() => FlagPalette.FirstOrDefault(color => players.Values.All(player => player.FlagColor != color)) ?? FlagPalette[players.Count % FlagPalette.Length];
    public string Id { get; } = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    public string Name { get; }
    public Guid HostId { get; private set; }
    public RoomSettings Settings { get; private set; }
    public RoomStatus Status { get; private set; }
    public double ElapsedSeconds { get; private set; }
    public long Revision { get; private set; }
    public string? EndReason { get; private set; }
    public MatchResult? Result { get; private set; }
    public DateTimeOffset LastOccupiedAt { get; internal set; }
    internal DateTimeOffset? FinishedAt { get; set; }
    internal bool FinalBroadcastSent { get; set; }
    public bool KeepAliveWhenEmpty { get; private set; }
    public bool MaskBots { get; private set; }
    public bool IsAmbient { get; private set; }
    public double SpeedMultiplier { get; private set; } = 1;
    public IEnumerable<Guid> HumanIds => players.Values.Where(x => !x.IsBot).Select(x => x.Id);
    public IEnumerable<Guid> ViewerIds => HumanIds.Concat(spectators.Keys);
    public bool IsSpectator(Guid actor) => spectators.ContainsKey(actor);
    public bool Contains(Guid playerId) => players.ContainsKey(playerId) || spectators.ContainsKey(playerId);
    public BotDifficulty? BotDifficultyFor(Guid playerId) => players.TryGetValue(playerId, out var p) && p.IsBot
        ? p.EffectiveDifficulty(Settings.BotDifficulty) : null;
    internal IReadOnlyDictionary<Guid, RoomPlayer> Players => players;

    public GameRoom(Guid hostId, string playerName, TribeType? tribe, string name, RoomSettings settings, DateTimeOffset now, HashSet<string>? reservedNames = null)
    {
        settings.Validate();
        Name = ValidateName(name);
        HostId = hostId;
        Settings = settings;
        LastOccupiedAt = now;
        Join(hostId, playerName, tribe);
        SyncBots(reservedNames);
    }

    private GameRoom(string name, RoomSettings settings, DateTimeOffset now, double speedMultiplier, bool isAmbient = false, bool maskBots = false, HashSet<string>? reservedNames = null)
    {
        settings.Validate(allowBotOnly: true);
        if (settings.BotCount < 2) throw new GameRuleException("settings", "Bot-only rooms require at least 2 bots.");
        Name = ValidateName(name);
        HostId = Guid.Empty;
        Settings = settings;
        LastOccupiedAt = now;
        KeepAliveWhenEmpty = true;
        IsAmbient = isAmbient;
        MaskBots = maskBots;
        SpeedMultiplier = ClampSpeed(speedMultiplier);
        SyncBots(reservedNames);
        StartInternal();
    }

    private GameRoom(string name, IReadOnlyList<(Guid Id, string Name, TribeType? Tribe)> humans, RoomSettings settings, DateTimeOffset now, HashSet<string>? reservedNames = null)
    {
        settings.Validate(allowBotOnly: humans.Count == 0);
        if (humans.Count == 0) throw new GameRuleException("minimum_players", "Matchmaking requires at least one player.");
        Name = ValidateName(name);
        HostId = humans[0].Id;
        Settings = settings;
        LastOccupiedAt = now;
        MaskBots = true;
        foreach (var (id, playerName, tribe) in humans)
        {
            Join(id, playerName, tribe);
            players[id].Ready = true;
        }
        SyncBots(reservedNames);
        StartInternal();
    }

    public static GameRoom CreateBotMatch(string name, RoomSettings settings, DateTimeOffset now, double speedMultiplier = 1, HashSet<string>? reservedNames = null)
        => new(name, settings, now, speedMultiplier, reservedNames: reservedNames);

    public static GameRoom CreateAmbientMatch(string name, RoomSettings settings, DateTimeOffset now, double speedMultiplier = 1, HashSet<string>? reservedNames = null)
        => new(name, settings, now, speedMultiplier, isAmbient: true, maskBots: true, reservedNames: reservedNames);

    public static GameRoom CreateMatchmakingMatch(string name, IReadOnlyList<(Guid Id, string Name, TribeType? Tribe)> humans, RoomSettings settings, DateTimeOffset now, HashSet<string>? reservedNames = null)
        => new(name, humans, settings, now, reservedNames);

    private static double ClampSpeed(double speed)
        => !double.IsFinite(speed) ? 1 : Math.Clamp(Math.Round(speed), 1, 20);

    private TribeType GetAvailableTribe()
    {
        var allTribes = Enum.GetValues<TribeType>();
        var taken = players.Values
            .Where(p => !p.IsBot || !p.RandomTribe)
            .Select(p => p.Tribe)
            .ToHashSet();

        var available = allTribes.Where(t => !taken.Contains(t)).ToArray();
        return available.Length > 0
            ? available[Random.Shared.Next(available.Length)]
            : allTribes[Random.Shared.Next(allTribes.Length)];
    }

    private void SyncBots(HashSet<string>? reservedNames = null)
    {
        var currentBots = players.Values.Where(x => x.IsBot).ToList();
        if (currentBots.Count < Settings.BotCount)
        {
            var usedNames = reservedNames is not null
                ? new HashSet<string>(reservedNames, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var existing in players.Values) usedNames.Add(existing.Name);

            var toAdd = Settings.BotCount - currentBots.Count;
            for (var i = 0; i < toAdd; i++)
            {
                var botIndex = currentBots.Count + i + 1;
                var id = Guid.NewGuid();
                var botName = BotNames.PickPlayerName(usedNames);
                reservedNames?.Add(botName);
                var personality = (BotPersonality)((botIndex - 1) % 3);
                var bot = new RoomPlayer(id, botName, (TribeType)((botIndex - 1) % 5), NextFlagColor(), personality)
                {
                    Ready = true,
                    RandomTribe = true,
                    Team = 0
                };
                players.Add(id, bot);
            }
        }
        else if (currentBots.Count > Settings.BotCount)
        {
            var toRemove = currentBots.Count - Settings.BotCount;
            foreach (var bot in currentBots.TakeLast(toRemove))
                players.Remove(bot.Id);
        }
        AssignBotDifficulties();
    }

    private void AssignBotDifficulties()
    {
        var bots = players.Values.Where(x => x.IsBot).ToList();
        if (bots.Count == 0) return;

        if (Settings.BotDifficulty != BotDifficulty.Mixed)
        {
            foreach (var bot in bots)
                bot.Difficulty = Settings.BotDifficulty;
            return;
        }

        // Balanced mix of Hard, Normal, and Easy skill levels within the same match, shuffled randomly.
        BotDifficulty[] pattern = [BotDifficulty.Hard, BotDifficulty.Normal, BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard, BotDifficulty.Easy, BotDifficulty.Normal];
        var pool = new BotDifficulty[bots.Count];
        for (var i = 0; i < bots.Count; i++)
            pool[i] = pattern[i % pattern.Length];
        Random.Shared.Shuffle(pool);
        for (var i = 0; i < bots.Count; i++)
            bots[i].Difficulty = pool[i];
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 32 || name.Any(char.IsControl))
            throw new GameRuleException("name", "Names must contain 1-32 visible characters.");
        return name.Trim();
    }
    private void RequireLobby()
    {
        if (Status != RoomStatus.Lobby) throw new GameRuleException("lobby_only", "This action requires an open lobby.");
    }
    private void RequireHost(Guid actor)
    {
        if (actor != HostId) throw new GameRuleException("host_only", "Only the room host can do that.");
    }

    public void Join(Guid id, string name, TribeType? tribe = null)
    {
        RequireLobby();
        if (id == Guid.Empty) throw new GameRuleException("player", "Invalid player.");
        if (tribe is { } chosen && !Enum.IsDefined(chosen)) throw new GameRuleException("player", "Invalid player or tribe.");
        if (players.ContainsKey(id)) return;
        if (HumanIds.Count() + Settings.BotCount >= Settings.Capacity)
            throw new GameRuleException("full", "This room is full, including reserved bot seats.");

        spectators.Remove(id);
        var assignedTribe = tribe ?? GetAvailableTribe();
        players.Add(id, new(id, ValidateName(name), assignedTribe, NextFlagColor())
        {
            Team = 0,
            RandomTribe = false
        });
        if (HostId == Guid.Empty || !players.ContainsKey(HostId)) HostId = id;
        Revision++;
    }

    public void JoinSpectator(Guid id, string name)
    {
        if (id == Guid.Empty) throw new GameRuleException("player", "Invalid spectator.");
        if (Status == RoomStatus.Faulted) throw new GameRuleException("faulted", "This room has stopped due to an error.");
        if (players.ContainsKey(id)) return;
        if (!spectators.ContainsKey(id) && spectators.Count >= 32)
            throw new GameRuleException("spectator_full", "Spectator limit reached for this room.");
        spectators[id] = ValidateName(name);
        Revision++;
    }

    public void SwitchSpectator(Guid actor, bool spectate, TribeType? tribe = null)
    {
        RequireLobby();
        if (spectate)
        {
            if (spectators.ContainsKey(actor)) return;
            if (!players.TryGetValue(actor, out var current) || current.IsBot)
                throw new GameRuleException("membership", "Join the room first.");
            if (spectators.Count >= 32)
                throw new GameRuleException("spectator_full", "Spectator limit reached for this room.");
            var name = current.Name;
            players.Remove(actor);
            spectators[actor] = name;
            if (HostId == actor) HostId = HumanIds.FirstOrDefault();
            Revision++;
            return;
        }

        if (players.ContainsKey(actor)) return;
        if (!spectators.TryGetValue(actor, out var specName))
            throw new GameRuleException("membership", "Join the room as spectator first.");
        Join(actor, specName, tribe);
    }

    public void ChangeTribe(Guid actor, Guid targetId, string tribeOrRandom)
    {
        RequireLobby();
        if (!players.TryGetValue(targetId, out var target))
            throw new GameRuleException("player", "Player not found.");
        if (targetId != actor && !(actor == HostId && target.IsBot))
            throw new GameRuleException("permission", "You can only change your own tribe or a bot's tribe as host.");

        if (string.Equals(tribeOrRandom, "Random", StringComparison.OrdinalIgnoreCase))
        {
            if (!target.IsBot) throw new GameRuleException("tribe", "Only bots can be set to random tribe.");
            target.RandomTribe = true;
            Revision++;
            return;
        }

        if (!Enum.TryParse<TribeType>(tribeOrRandom, true, out var parsedTribe) || !Enum.IsDefined(parsedTribe))
            throw new GameRuleException("tribe", "Invalid tribe selected.");

        target.Tribe = parsedTribe;
        target.RandomTribe = false;
        Revision++;
    }

    public void ChangeTeam(Guid actor, Guid targetId, int team)
    {
        RequireLobby();
        if (team is < 0 or > 4)
            throw new GameRuleException("team", "Team must be between 0 and 4.");
        if (!players.TryGetValue(targetId, out var target))
            throw new GameRuleException("player", "Player not found.");
        if (targetId != actor && !(actor == HostId && target.IsBot))
            throw new GameRuleException("permission", "You can only change your own team or a bot's team as host.");

        target.Team = team;
        Revision++;
    }

    public void ChangeColor(Guid actor, Guid targetId, string color)
    {
        RequireLobby();
        if (!players.TryGetValue(targetId, out var target))
            throw new GameRuleException("player", "Player not found.");
        if (targetId != actor && !(actor == HostId && target.IsBot))
            throw new GameRuleException("permission", "You can only change your own color or a bot's color as host.");

        var normalized = (color ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length != 7 || normalized[0] != '#' || !normalized.Skip(1).All(Uri.IsHexDigit))
            throw new GameRuleException("color", "Choose a valid hex color.");

        var existing = players.Values.FirstOrDefault(p => p.Id != targetId && string.Equals(p.FlagColor, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (!existing.IsBot)
                throw new GameRuleException("color", "That color is already taken by another player.");
            existing.FlagColor = target.FlagColor;
        }

        target.FlagColor = normalized;
        Revision++;
    }

    public void ChangeSpawnSlot(Guid actor, Guid targetId, int? slot)
    {
        RequireLobby();
        if (!players.TryGetValue(targetId, out var target))
            throw new GameRuleException("player", "Player not found.");
        if (targetId != actor && !(actor == HostId && target.IsBot))
            throw new GameRuleException("permission", "You can only change your own map position or a bot's position as host.");

        if (slot is { } chosen)
        {
            if (chosen < 0 || chosen >= Settings.Capacity)
                throw new GameRuleException("spawn_slot", $"Map position must be between 0 and {Settings.Capacity - 1}.");
            var resolved = ResolveLobbySlots();
            var occupantId = resolved.FirstOrDefault(kv => kv.Value == chosen && kv.Key != targetId).Key;
            var occupant = occupantId != Guid.Empty ? players.GetValueOrDefault(occupantId) : null;
            if (occupant is not null)
            {
                if (!occupant.IsBot)
                    throw new GameRuleException("spawn_slot", "That map position is already taken by another player.");
                occupant.SpawnSlot = null;
            }
        }

        target.SpawnSlot = slot;
        Revision++;
    }

    public void Configure(Guid actor, RoomSettings settings, HashSet<string>? reservedNames = null)
    {
        RequireHost(actor); RequireLobby(); settings.Validate();
        if (HumanIds.Count() + settings.BotCount > settings.Capacity)
            throw new GameRuleException("full", "Settings would exceed room capacity.");
        Settings = settings;
        SyncBots(reservedNames);
        foreach (var p in players.Values.Where(x => x.SpawnSlot >= settings.Capacity)) p.SpawnSlot = null;
        foreach (var player in players.Values.Where(x => !x.IsBot)) player.Ready = false;
        Revision++;
    }

    public void SetReady(Guid actor, bool ready)
    {
        RequireLobby();
        if (!players.TryGetValue(actor, out var player) || player.IsBot)
            throw new GameRuleException("membership", "Join the room first.");
        player.Ready = ready;
        Revision++;
    }

    public void Leave(Guid actor)
    {
        if (spectators.Remove(actor))
        {
            Revision++;
            return;
        }
        if (Status == RoomStatus.Running) throw new GameRuleException("running", "Your seat is reserved until the match ends. Disconnect to leave temporarily.");
        if (!players.Remove(actor)) return;
        if (HostId == actor) HostId = HumanIds.FirstOrDefault();
        Revision++;
    }

    internal void Abandon(Guid actor)
    {
        if (spectators.Remove(actor))
        {
            Revision++;
            return;
        }
        if (Status == RoomStatus.Lobby) { Leave(actor); return; }
        if (!players.TryGetValue(actor, out var player)) return;
        player.Personality ??= BotPersonality.Economic;
        if (HostId == actor) HostId = HumanIds.FirstOrDefault();
        Revision++;
    }

    private static List<RoomPlayer> ArrangeSpawnOrder(IEnumerable<RoomPlayer> roster)
    {
        var ordered = new List<RoomPlayer>();
        foreach (var group in roster.GroupBy(x => x.Team is >= 1 and <= 4 ? x.Team : int.MaxValue).OrderBy(g => g.Key))
        {
            if (group.Key == int.MaxValue)
            {
                ordered.AddRange(group.OrderBy(x => x.IsBot));
                continue;
            }
            var humans = group.Where(x => !x.IsBot).ToList();
            var bots = group.Where(x => x.IsBot).ToList();
            // Place human commanders inside the center of their team arc so bots buffer their flanks fairly.
            var firstHalf = bots.Count / 2;
            ordered.AddRange(bots.Take(firstHalf));
            ordered.AddRange(humans);
            ordered.AddRange(bots.Skip(firstHalf));
        }
        return ordered;
    }

    private static Position SlotPosition(int slot, int ringSize)
    {
        var angle = slot * Math.Tau / ringSize;
        return new(
            LogisticsWorld.MapCenter + LogisticsWorld.SettlementRadius * Math.Cos(angle),
            LogisticsWorld.MapCenter + LogisticsWorld.SettlementRadius * Math.Sin(angle));
    }

    private Dictionary<Guid, int> ResolveLobbySlots()
    {
        var ringSize = Math.Max(players.Count, Settings.Capacity);
        var slots = new RoomPlayer?[ringSize];
        foreach (var player in players.Values.Where(p => p.SpawnSlot is { } s && s >= 0 && s < ringSize).OrderBy(p => p.IsBot))
        {
            if (slots[player.SpawnSlot!.Value] is null)
                slots[player.SpawnSlot!.Value] = player;
        }

        var placed = slots.Where(p => p is not null).ToHashSet()!;

        int? NextOpenFrom(int start, int dir)
        {
            for (var step = 1; step < ringSize; step++)
            {
                var idx = ((start + dir * step) % ringSize + ringSize) % ringSize;
                if (slots[idx] is null) return idx;
            }
            return null;
        }

        void PlaceAdjacentToAnchor(int anchor, List<RoomPlayer> members)
        {
            var humans = members.Where(m => !m.IsBot).ToList();
            var bots = members.Where(m => m.IsBot).ToList();
            var half = bots.Count / 2;
            var left = bots.Take(half).ToList();
            var right = humans.Concat(bots.Skip(half)).ToList();

            foreach (var m in left)
            {
                var idx = NextOpenFrom(anchor, -1);
                if (idx is { } chosen) { slots[chosen] = m; placed.Add(m); }
            }
            foreach (var m in right)
            {
                var idx = NextOpenFrom(anchor, 1);
                if (idx is { } chosen) { slots[chosen] = m; placed.Add(m); }
            }
        }

        (int Start, int Length) LargestEmptyGap()
        {
            if (slots.All(s => s is null)) return (0, ringSize);
            var bestStart = 0;
            var bestLen = 0;
            for (var i = 0; i < ringSize; i++)
            {
                if (slots[i] is not null) continue;
                var prev = (i - 1 + ringSize) % ringSize;
                if (slots[prev] is null) continue;
                var len = 0;
                while (len < ringSize && slots[(i + len) % ringSize] is null) len++;
                if (len > bestLen) { bestLen = len; bestStart = i; }
            }
            return (bestStart, bestLen);
        }

        // Place teamed players together so same-team bots and humans sit adjacently on the map.
        foreach (var teamGroup in players.Values
            .Where(p => p.Team is >= 1 and <= 4)
            .GroupBy(p => p.Team)
            .OrderBy(g => g.Key))
        {
            var unpinnedTeam = teamGroup.Where(p => !placed.Contains(p)).ToList();
            if (unpinnedTeam.Count == 0) continue;

            var pinnedAnchor = -1;
            for (var i = 0; i < ringSize; i++)
            {
                if (slots[i]?.Team == teamGroup.Key) { pinnedAnchor = i; break; }
            }

            if (pinnedAnchor >= 0)
            {
                PlaceAdjacentToAnchor(pinnedAnchor, unpinnedTeam);
                continue;
            }

            var orderedTeam = ArrangeSpawnOrder(unpinnedTeam);
            var (gapStart, gapLen) = LargestEmptyGap();
            if (gapLen > 0)
            {
                var offset = Math.Max(0, (gapLen - orderedTeam.Count) / 2);
                var cursor = (gapStart + offset) % ringSize;
                var remaining = new List<RoomPlayer>();
                foreach (var member in orderedTeam)
                {
                    if (slots[cursor] is null)
                    {
                        slots[cursor] = member;
                        placed.Add(member);
                        cursor = (cursor + 1) % ringSize;
                    }
                    else
                    {
                        remaining.Add(member);
                    }
                }
                if (remaining.Count > 0) PlaceAdjacentToAnchor(gapStart, remaining);
            }
        }

        var unaligned = ArrangeSpawnOrder(players.Values.Where(p => !placed.Contains(p)));
        var openSlots = Enumerable.Range(0, ringSize).Where(i => slots[i] is null).ToList();
        for (var i = 0; i < unaligned.Count && openSlots.Count > 0; i++)
        {
            var idx = openSlots[i * openSlots.Count / unaligned.Count];
            slots[idx] = unaligned[i];
            placed.Add(unaligned[i]);
        }

        var result = new Dictionary<Guid, int>(players.Count);
        for (var i = 0; i < ringSize; i++)
        {
            if (slots[i] is { } p) result[p.Id] = i;
        }
        return result;
    }

    public void Start(Guid actor, Func<Guid, bool> online)
    {
        RequireHost(actor); RequireLobby();
        SyncBots();
        if (players.Count < 2)
            throw new GameRuleException("minimum_players", "At least two players are required to start the match.");
        if (players.Values.Where(x => !x.IsBot).Any(x => !x.Ready || !online(x.Id)))
            throw new GameRuleException("not_ready", "Every human player must be online and ready.");
        StartInternal();
        
    }

    private void StartInternal()
    {
        foreach (var bot in players.Values.Where(x => x.IsBot && x.RandomTribe))
        {
            bot.Tribe = GetAvailableTribe();
            bot.RandomTribe = false;
        }

        var lobbySlots = ResolveLobbySlots();
        var ringSize = Math.Max(players.Count, Settings.Capacity);
        var orderedPlayers = players.Values
            .OrderBy(p => lobbySlots[p.Id])
            .ToList();
        var seq = 0;
        foreach (var player in orderedPlayers)
        {
            player.Economy = new(player.Id, player.Tribe, SlotPosition(lobbySlots[player.Id], ringSize));
            var diff = player.EffectiveDifficulty(Settings.BotDifficulty);
            player.NextBotAction = diff == BotDifficulty.Hard ? 0 : 1 + (++seq) * .25;
            player.NextSocialAction = 20 + Random.Shared.NextDouble() * 90;
        }

        world = new(orderedPlayers.Select(x => x.Economy!), Random.Shared.Next(), Settings.CastleUnlockMinutes * 60);
        world.ConfigureBotContext(Settings.BotDifficulty,
            players.Values.Where(p => p.IsBot && p.EffectiveDifficulty(Settings.BotDifficulty) == BotDifficulty.Hard).Select(p => p.Id),
            players.Values.Where(p => !p.IsBot).Select(p => p.Id));
        InitializeTeamAlliances();
        world.SetAlliances(AllianceView());
        Status = RoomStatus.Running;
        Revision++;
    }

    public CommandResult Execute(Guid actorId, GameCommand command)
    {
        var isSpectator = spectators.ContainsKey(actorId);
        if (!isSpectator && !players.TryGetValue(actorId, out _))
            return new(command.RequestId, false, "membership", "You are not in this room.");
        var player = isSpectator ? null : players[actorId];
        if (isSpectator)
        {
            if (command.Kind != CommandKind.SendChat || Status is not (RoomStatus.Lobby or RoomStatus.Running))
                return new(command.RequestId, false, "spectator", "Spectators can only use chat.");
            try
            {
                ApplySocial(actorId, command);
                Revision++;
                return new(command.RequestId, true, "ok", "Command accepted.");
            }
            catch (GameRuleException error) { return new(command.RequestId, false, error.Code, error.Message); }
        }
        var fingerprint = !player!.IsBot
            ? JsonSerializer.Serialize(command with { Formation = command.Formation?.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value) })
            : string.Empty;
        if (!player.IsBot && player.Receipts.TryGetValue(command.RequestId, out var receipt))
            return receipt.Fingerprint == fingerprint ? receipt.Result : new(command.RequestId, false, "request_conflict", "Request ID was already used for another command.");
        if (command.RequestId == Guid.Empty)
            return new(command.RequestId, false, "request_limit", "A non-empty unique request ID is required.");
        CommandResult result;
        try
        {
            if (command.Kind == CommandKind.SendChat && Status is RoomStatus.Lobby or RoomStatus.Running)
            {
                ApplySocial(actorId, command);
            }
            else
            {
                if (Status != RoomStatus.Running) throw new GameRuleException("not_running", "The match is not running.");
                if (world!.Primary(actorId) is null) throw new GameRuleException("eliminated", "Your settlements have fallen. You may spectate the remaining match.");
                if (ApplySocial(actorId, command))
                {
                    if (command.Kind == CommandKind.RespondAlliance && command.Enabled) world.SetAlliances(AllianceView());
                }
                else if (command.Kind is CommandKind.Upgrade or CommandKind.Train or CommandKind.SetAutoQueue)
                    world.EconomyTarget(actorId, command.TargetId).Apply(command, ElapsedSeconds);
                else if (command.Kind == CommandKind.SelectVillage) player.SelectedVillageId = world.EconomyTarget(actorId, command.TargetId).Village.Id;
                else world.Apply(actorId, command, ElapsedSeconds, Settings.ShieldMinutes * 60);
            }
            Revision++;
            result = new(command.RequestId, true, "ok", "Command accepted.");
        }
        catch (GameRuleException error) { result = new(command.RequestId, false, error.Code, error.Message); }
        if (!player.IsBot)
        {
            if (player.Receipts.Count >= 4096 && player.ReceiptOrder.TryDequeue(out var oldest))
                player.Receipts.Remove(oldest);
            player.Receipts.Add(command.RequestId, (fingerprint, result));
            player.ReceiptOrder.Enqueue(command.RequestId);
        }
        return result;
    }

    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > 1) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Status != RoomStatus.Running) return;
        var repeats = Math.Clamp((int)Math.Round(SpeedMultiplier), 1, 20);
        for (var i = 0; i < repeats && Status == RoomStatus.Running; i++)
            AdvanceStep(seconds);
    }

    private void AdvanceStep(double seconds)
    {
        if (Status != RoomStatus.Running) return;
        var step = Math.Min(seconds, Settings.DurationMinutes * 60 - ElapsedSeconds);
        ElapsedSeconds += step;
        world!.AdvanceEconomies(step, ElapsedSeconds, Settings.ResourceSpeed);
        world.Advance(step, ElapsedSeconds, Settings.ShieldMinutes * 60, Settings.DurationMinutes * 60);
        if (world.CrownWinner is not null || world.Dominated || ElapsedSeconds >= Settings.DurationMinutes * 60)
        {
            Status = RoomStatus.Finished;
            EndReason = world.CrownWinner is not null ? "CrownKeep" : world.Dominated ? "Domination" : "TimeLimit";
            Result = world.Result(EndReason);
        }
        else
        {
            foreach (var p in players.Values)
                if (p.Economy is not null && (p.Economy.Ruined || p.Economy.Village.OwnerId != p.Id))
                    p.Economy = world.Primary(p.Id);
            foreach (var player in players.Values.Where(x => x.IsBot && x.NextBotAction <= ElapsedSeconds))
            {
                player.Economy = world.Primary(player.Id);
                if (player.Economy is null) continue;
                var diff = player.EffectiveDifficulty(Settings.BotDifficulty);
                BotController.Act(this, player, diff);
                BotController.ActSocial(this, player);
                if (diff == BotDifficulty.Hard || ElapsedSeconds > 8)
                    world.BotAct(player.Id, this, player.Personality!.Value, diff);
                player.NextBotAction = ElapsedSeconds + (diff switch { BotDifficulty.Easy => 8, BotDifficulty.Hard => 0.5, _ => 4 });
            }
        }
        Revision++;
    }

    internal void AdminSetSpeed(double multiplier)
    {
        SpeedMultiplier = ClampSpeed(multiplier);
        Revision++;
    }

    internal void AdminGiveResources(Guid targetPlayerId, long wood, long iron, long stone, long wheat)
    {
        if (Status != RoomStatus.Running || world is null)
            throw new GameRuleException("not_running", "Resources can only be granted during a running match.");
        var city = world.Primary(targetPlayerId) ?? throw new GameRuleException("player", "Target player has no active settlement.");
        var clamp = (long v) => Math.Clamp(v, 0L, 10_000_000L);
        city.ReceiveCargo(new Resources(clamp(wood), clamp(iron), clamp(stone), clamp(wheat)));
        Revision++;
    }

    internal void AdminGiveTroops(Guid targetPlayerId, string unitId, int count)
    {
        if (Status != RoomStatus.Running || world is null)
            throw new GameRuleException("not_running", "Troops can only be granted during a running match.");
        if (count is < 1 or > 20000)
            throw new GameRuleException("count", "Troop gift count must be between 1 and 20,000.");
        var city = world.Primary(targetPlayerId) ?? throw new GameRuleException("player", "Target player has no active settlement.");
        if (!GameCatalog.Units.TryGetValue(unitId, out var def))
            throw new GameRuleException("unit", "Unknown unit ID.");
        if (def.TribeType != city.Village.Tribe)
            throw new GameRuleException("tribe", "Selected unit must match the target player's tribe.");
        if (!city.ReceiveGift([new TroopStack(BaseUnit.Create(def), count)]))
            throw new GameRuleException("population", "Target village exceeded maximum unit limit (100,000).");
        Revision++;
    }

    internal void MarkFaulted()
    {
        Status = RoomStatus.Faulted;
        EndReason = "ServerError";
        Revision++;
    }

    public RoomSummary Summary() => new(Id, Name, Status, HumanIds.Count(), Settings.BotCount, Settings.Capacity, spectators.Count, SpeedMultiplier, KeepAliveWhenEmpty, MaskBots, IsAmbient);
    public RoomSnapshot Snapshot(Guid viewer, Func<Guid, bool> online)
    {
        var isSpectator = spectators.ContainsKey(viewer);
        if (!isSpectator && !players.ContainsKey(viewer)) throw new GameRuleException("membership", "You are not in this room.");
        var running = Status != RoomStatus.Lobby;
        var remaining = Math.Max(0, Settings.DurationMinutes * 60 - ElapsedSeconds);
        var castleUnlockSeconds = Settings.CastleUnlockMinutes * 60;
        var phase = Status != RoomStatus.Running ? Status.ToString() : ElapsedSeconds >= castleUnlockSeconds || remaining <= 900 ? "Finale" : ElapsedSeconds < 600 ? "Foundation" : "Expansion";
        var lobbySlots = world is null ? ResolveLobbySlots() : null;
        var ringSize = Math.Max(players.Count, Settings.Capacity);
        var orderedPlayers = MaskBots
            ? players.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            : players.Values.OrderBy(x => x.IsBot).ThenBy(x => x.Name);
        return new(Id, Name, HostId, viewer, Revision, Status, Settings, ElapsedSeconds, remaining,
            running ? Math.Max(0, Settings.ShieldMinutes * 60 - ElapsedSeconds) : Settings.ShieldMinutes * 60,
            phase, EndReason, orderedPlayers.Select(x =>
            {
                var city = world is null ? x.Economy : world.Primary(x.Id);
                var effSlot = lobbySlots is not null && lobbySlots.TryGetValue(x.Id, out var slotIndex) ? slotIndex : x.SpawnSlot;
                var pos = city?.Village.Position ?? (effSlot is { } s ? SlotPosition(s, ringSize) : (Position?)null);
                return new PlayerSnapshot(x.Id, x.Name, x.Tribe, x.IsBot, x.Personality,
                    x.Ready, x.IsBot || online(x.Id), city?.Village.Id, pos, city?.Village.Level ?? (world is null ? 1 : (int?)null), x.FlagColor, x.Team, x.RandomTribe, x.SpawnSlot, effSlot);
            }).ToArray(),
            isSpectator ? null : world?.EconomySnapshot(viewer, players[viewer].SelectedVillageId, Settings.ResourceSpeed),
            world?.Snapshot(viewer, omniscient: isSpectator || (!isSpectator && world.Primary(viewer) is null)),
            Result,
            ChatView(), AllianceView(), AllianceRequestView(viewer),
            isSpectator,
            spectators.OrderBy(s => s.Value).Select(s => new SpectatorSnapshot(s.Key, s.Value)).ToArray(),
            SpeedMultiplier,
            MaskBots);
    }
}

internal static class BotController
{
    private static int UnitPriority(TargetSnapshot target, BotPersonality? personality, BotDifficulty difficulty = BotDifficulty.Normal)
    {
        var role = target.UnitId?.Split('-').LastOrDefault() ?? "";
        if (difficulty == BotDifficulty.Hard)
        {
            return role switch
            {
                "heavycavalry" => 98,
                "swordsman" => 95,
                "catapult" => 92,
                "cavalry" => 89,
                "spearman" => 85,
                "lightcavalry" => 78,
                "infantry" => 60,
                _ => 0
            };
        }
        return (role, personality) switch
        {
            ("heavycavalry", _) => 94,
            ("swordsman", BotPersonality.Aggressive) => 91,
            ("cavalry", BotPersonality.Aggressive) => 89,
            ("spearman", BotPersonality.Defensive) => 90,
            ("swordsman", _) => 86,
            ("cavalry", _) => 84,
            ("spearman", _) => 80,
            ("lightcavalry", _) => 76,
            ("infantry", _) => 55,
            _ => 0
        };
    }

    private static int BuildingScore(TargetSnapshot b, EconomySnapshot economy, BotPersonality? personality, BotDifficulty difficulty)
    {
        var score = b.Level * 10;
        var nearCap = economy.Resources.Wood >= economy.Capacity * 2 / 3
            || economy.Resources.Iron >= economy.Capacity * 2 / 3
            || economy.Resources.Stone >= economy.Capacity * 2 / 3
            || economy.Resources.Wheat >= economy.Capacity * 2 / 3;
        var mainHallLevel = economy.Targets.FirstOrDefault(t => t.Name == "MainHall")?.Level ?? 1;
        var barracksLevel = economy.Targets.FirstOrDefault(t => t.Name == "Barracks")?.Level ?? 1;
        if (difficulty == BotDifficulty.Hard)
        {
            var militaryCapped = economy.Targets.Any(t => t.Kind == "Building"
                && t.Name is "Barracks" or "Stable" or "SiegeWorkshop" or "Smithy"
                && t.Level >= mainHallLevel);
            score -= b.Name switch
            {
                "MainHall" => (militaryCapped ? 72 : mainHallLevel < 5 ? 52 : 34) + (personality == BotPersonality.Economic ? 6 : 0),
                "Barracks" => (b.Level < 3 ? 84 : b.Level < 10 ? 66 : 46) + (personality == BotPersonality.Aggressive ? 8 : 0),
                "Stable" => (b.Level < 5 ? 82 : b.Level < 10 ? 64 : 44) + (personality != BotPersonality.Defensive ? 6 : 0),
                "SiegeWorkshop" => (barracksLevel >= 2 && b.Level < 3 ? 78 : b.Level < 10 ? 60 : 40),
                "Smithy" => (b.Level < 10 ? 68 : 48) + (personality == BotPersonality.Aggressive ? 6 : 0),
                "Warehouse" => nearCap ? 90 : b.Level < 6 ? 32 : 16,
                "Hospital" => economy.Wounded > 0 ? 46 : b.Level < 4 ? 18 : 8,
                "Wall" => economy.Besieged ? 68 : b.Level < 4 ? 18 : 8,
                "Market" => b.Level < 2 ? 26 : 6,
                _ => 0
            };
            return score;
        }
        score -= b.Name switch
        {
            "Warehouse" => (nearCap ? 82 : 24) + (personality == BotPersonality.Economic ? 12 : 0),
            "MainHall" => 26 + (personality == BotPersonality.Economic ? 12 : 0),
            "Barracks" => (b.Level < 3 ? 52 : b.Level < 5 ? 30 : b.Level < 8 ? 18 : 8) + (personality == BotPersonality.Aggressive ? 12 : 0),
            "Stable" => (b.Level < 3 ? 42 : b.Level < 5 ? 26 : b.Level < 8 ? 16 : 6) + (personality != BotPersonality.Defensive ? 8 : 0),
            "Smithy" => (b.Level < 7 ? 26 : 14) + (personality == BotPersonality.Aggressive ? 10 : 0),
            "Wall" => (economy.Besieged ? 60 : 14) + (personality == BotPersonality.Defensive ? 24 : b.Level < 3 ? 10 : 0),
            "Hospital" => (economy.Wounded > 0 ? 40 : 10) + (personality == BotPersonality.Defensive ? 16 : b.Level < 3 ? 8 : 0),
            "SiegeWorkshop" => (barracksLevel >= 3 && b.Level < 3 ? 30 : b.Level < 5 && personality == BotPersonality.Aggressive ? 20 : 6),
            "Market" => b.Level < (personality == BotPersonality.Economic ? 4 : 2) ? (personality == BotPersonality.Economic ? 24 : 10) : 0,
            _ => 0
        };
        return score;
    }

    public static void Act(IGameCommands commands, RoomPlayer player, BotDifficulty difficulty = BotDifficulty.Normal) =>
        ActEconomy(commands, player.Id, player.Economy!.Snapshot(), player.Personality, difficulty, () => player.Economy!.Snapshot());

    public static void ActEconomy(IGameCommands commands, Guid actorId, EconomySnapshot economy, BotPersonality? personality,
        BotDifficulty difficulty = BotDifficulty.Normal, Func<EconomySnapshot>? refresh = null)
    {
        var isHard = difficulty == BotDifficulty.Hard;
        var garrisonTotal = economy.Garrison.Values.Sum();
        var marchWheatReserve = garrisonTotal >= (isHard ? 140 : 100)
            ? Math.Clamp(garrisonTotal * 3L, isHard ? 700L : 450L, isHard ? 4500L : 2500L)
            : 0L;
        var wheatNet = economy.Production.FirstOrDefault(p => p.Resource == "Wheat")?.NetPerMinute ?? 0;
        var starving = isHard
            ? (economy.FoodShortage && wheatNet < 0) || (garrisonTotal >= 200 && (wheatNet < 20 || (economy.Resources.Wheat < marchWheatReserve && wheatNet < 160)))
            : economy.FoodShortage || (wheatNet < 0 && (economy.Resources.Wheat < Math.Max(400L, marchWheatReserve) || garrisonTotal >= 120))
                || (garrisonTotal >= 150 && economy.Resources.Wheat < marchWheatReserve && wheatNet < 80);

        var autoPasses = isHard ? 8 : 1;
        for (var pass = 0; pass < autoPasses; pass++)
        {
            var trainings = economy.Targets.Where(x => x.Kind == "Training").ToArray();
            var acted = false;
            if (starving)
            {
                var active = trainings.Where(x => x.Auto).OrderBy(x => UnitPriority(x, personality, difficulty)).FirstOrDefault();
                if (active is not null)
                    acted = commands.Execute(actorId, new(Guid.NewGuid(), CommandKind.SetAutoQueue, active.Id, Enabled: false)).Accepted;
            }
            else if (isHard || (wheatNet >= (garrisonTotal >= 140 ? 15 : -5) && (economy.Resources.Wheat >= Math.Max(300L, marchWheatReserve) || wheatNet >= 20)))
            {
                var nonCombatAuto = trainings.FirstOrDefault(x => x.Auto && UnitPriority(x, personality, difficulty) == 0);
                if (nonCombatAuto is not null)
                {
                    acted = commands.Execute(actorId, new(Guid.NewGuid(), CommandKind.SetAutoQueue, nonCombatAuto.Id, Enabled: false)).Accepted;
                }
                else
                {
                    var unlockedCombat = trainings.Where(x => x.Unlocked && UnitPriority(x, personality, difficulty) > 0)
                        .OrderByDescending(x => UnitPriority(x, personality, difficulty)).ToArray();
                    var desired = new List<TargetSnapshot>();
                    if (unlockedCombat.FirstOrDefault(x => x.TrainingBuilding == "Barracks") is { } bestBarracks)
                        desired.Add(bestBarracks);
                    if (unlockedCombat.FirstOrDefault(x => x.TrainingBuilding == "Stable") is { } bestStable)
                        desired.Add(bestStable);
                    if (isHard && unlockedCombat.FirstOrDefault(x => x.TrainingBuilding == "SiegeWorkshop") is { } bestSiege)
                        desired.Add(bestSiege);
                    foreach (var unit in unlockedCombat)
                        if (desired.Count < economy.AutoSlots && !desired.Contains(unit)
                            && (difficulty == BotDifficulty.Easy || UnitPriority(unit, personality, difficulty) >= 75 || desired.Count == 0))
                            desired.Add(unit);

                    var obsolete = trainings.Where(x => x.Auto && !desired.Contains(x))
                        .OrderBy(x => UnitPriority(x, personality, difficulty)).FirstOrDefault();
                    var missing = desired.FirstOrDefault(x => !x.Auto);
                    if (missing is not null)
                    {
                        if (economy.UsedAutoSlots < economy.AutoSlots)
                            acted = commands.Execute(actorId, new(Guid.NewGuid(), CommandKind.SetAutoQueue, missing.Id, Enabled: true)).Accepted;
                        else if (obsolete is not null)
                            acted = commands.Execute(actorId, new(Guid.NewGuid(), CommandKind.SetAutoQueue, obsolete.Id, Enabled: false)).Accepted;
                    }
                }
            }
            if (!acted || refresh is null) break;
            economy = refresh();
        }

        var maxUnitTier = isHard ? 10 : difficulty == BotDifficulty.Normal ? 5 : 2;
        var upgradePasses = isHard ? 8 : 1;
        for (var pass = 0; pass < upgradePasses; pass++)
        {
            var mainHallLevel = economy.Targets.FirstOrDefault(t => t.Name == "MainHall")?.Level ?? 1;
            var remaining = economy.Resources;
            var trainings = economy.Targets.Where(x => x.Kind == "Training").ToArray();
            var upgradeWheatFloor = garrisonTotal >= 60 ? Math.Max(400L, marchWheatReserve) : (isHard ? 140L : 0L);
            var building = economy.Targets
                .Where(x => x.Kind == "Building" && (x.Name == "MainHall" || x.Level < mainHallLevel)
                    && x.UpgradeCost is { } cost && remaining.Covers(cost)
                    && remaining.Wheat >= cost.Wheat + (x.Name == "Warehouse" && economy.Resources.Total >= economy.Capacity * 0.8 ? 0L : upgradeWheatFloor))
                .OrderBy(x => BuildingScore(x, economy, personality, difficulty))
                .FirstOrDefault();

            var upgradedAny = false;
            if (building is not null && commands.Execute(actorId, new(Guid.NewGuid(), CommandKind.Upgrade, building.Id)).Accepted)
            {
                upgradedAny = true;
                if (refresh is not null)
                {
                    economy = refresh();
                    remaining = economy.Resources;
                    mainHallLevel = economy.Targets.FirstOrDefault(t => t.Name == "MainHall")?.Level ?? 1;
                    trainings = economy.Targets.Where(x => x.Kind == "Training").ToArray();
                }
                else
                {
                    remaining -= building.UpgradeCost!.Value;
                }
            }

            var canInvestTier = isHard
                ? mainHallLevel >= 2 && (building is null || remaining.Total > economy.Capacity * 0.35)
                : difficulty == BotDifficulty.Normal && mainHallLevel >= 3 && building is null && remaining.Total > economy.Capacity * 1.1;
            if (canInvestTier)
            {
                var tier = trainings
                    .Where(x => (x.Auto || UnitPriority(x, personality, difficulty) >= 84) && x.Unlocked && x.Level < maxUnitTier
                        && x.UpgradeCost is { } c && remaining.Covers(c) && remaining.Wheat >= c.Wheat + Math.Max(upgradeWheatFloor, marchWheatReserve))
                    .OrderBy(x => x.Level)
                    .ThenByDescending(x => UnitPriority(x, personality, difficulty))
                    .FirstOrDefault();
                if (tier is not null && commands.Execute(actorId, new(Guid.NewGuid(), CommandKind.Upgrade, tier.Id)).Accepted)
                {
                    upgradedAny = true;
                    if (refresh is not null) economy = refresh();
                }
            }

            if (!upgradedAny || refresh is null) break;
        }

        if (isHard && !starving)
        {
            var siegeLvl = economy.Targets.FirstOrDefault(t => t.Name == "SiegeWorkshop")?.Level ?? 1;
            var mainLvl = economy.Targets.FirstOrDefault(t => t.Name == "MainHall")?.Level ?? 1;
            var needUnlockSave = mainLvl < 5 || siegeLvl < 3;
            var keepWood = needUnlockSave ? 220L : 0L;
            var keepIron = needUnlockSave ? 200L : 0L;
            var keepStone = needUnlockSave ? 150L : 0L;
            foreach (var lane in new[] { "Barracks", "Stable", "SiegeWorkshop" })
            {
                if (lane == "SiegeWorkshop" && siegeLvl < 3) continue;
                for (var batchPass = 0; batchPass < 2; batchPass++)
                {
                    if (economy.Jobs.Count(j => j.Lane == lane) >= 2) break;
                    var bestUnit = economy.Targets
                        .Where(x => x.Kind == "Training" && x.Unlocked && x.TrainingBuilding == lane
                            && UnitPriority(x, personality, difficulty) > 0 && x.TrainingCost is not null)
                        .OrderByDescending(x => UnitPriority(x, personality, difficulty))
                        .FirstOrDefault();
                    if (bestUnit?.TrainingCost is not { } unitCost) break;
                    var batch = Math.Max(1, bestUnit.AutoBatchSize);
                    var batchCost = new Resources(unitCost.Wood * batch, unitCost.Iron * batch, unitCost.Stone * batch, unitCost.Wheat * batch);
                    var wheatReserve = Math.Max(needUnlockSave ? 150L : 0L, Math.Max(marchWheatReserve, wheatNet > 60 ? 0L : 200L));
                    if (!economy.Resources.Covers(batchCost)
                        || economy.Resources.Wood < batchCost.Wood + keepWood
                        || economy.Resources.Iron < batchCost.Iron + keepIron
                        || economy.Resources.Stone < batchCost.Stone + keepStone
                        || economy.Resources.Wheat < batchCost.Wheat + wheatReserve) break;
                    if (!commands.Execute(actorId, new(Guid.NewGuid(), CommandKind.Train, bestUnit.Id, Quantity: batch)).Accepted) break;
                    if (refresh is null) break;
                    economy = refresh();
                }
            }
        }
    }

    public static void ActSocial(GameRoom room, RoomPlayer player)
    {
        var isHardCoalition = room.Settings.BotDifficulty == BotDifficulty.Hard && room.HumanIds.Any();
        var pending = room.PendingRequestsFor(player.Id);
        if (player.PendingResponseAt.Count > 0)
        {
            var activeReqIds = pending.Select(r => r.Id).ToHashSet();
            foreach (var staleId in player.PendingResponseAt.Keys.Where(id => !activeReqIds.Contains(id)).ToArray())
                player.PendingResponseAt.Remove(staleId);
        }

        foreach (var req in pending)
        {
            var sender = room.Players.GetValueOrDefault(req.SenderId);
            var sameTeam = sender is not null && player.Team > 0 && player.Team == sender.Team;
            var teamMismatch = sender is not null && (player.Team > 0 || sender.Team > 0) && player.Team != sender.Team;
            bool accept;
            if (isHardCoalition)
            {
                if (sender is not null && !sender.IsBot)
                    accept = sameTeam;
                else
                    accept = sender is not null && sender.IsBot && !teamMismatch;
            }
            else
            {
                // Realistic thinking pause before answering so bots don't accept/decline instantaneously.
                if (!player.PendingResponseAt.TryGetValue(req.Id, out var respondAt))
                {
                    var thinkSeconds = 4.0 + Random.Shared.NextDouble() * 8.0; // 4s - 12s
                    player.PendingResponseAt[req.Id] = room.ElapsedSeconds + thinkSeconds;
                    continue;
                }
                if (room.ElapsedSeconds < respondAt)
                    continue;
                player.PendingResponseAt.Remove(req.Id);

                var currentAlliances = room.AllianceCount(player.Id);
                if (sender is not null && !sender.IsBot)
                {
                    if (teamMismatch || currentAlliances >= 2)
                    {
                        accept = false;
                    }
                    else
                    {
                        var acceptChance = currentAlliances == 0
                            ? (player.Personality == BotPersonality.Aggressive ? 0.72 : 0.88)
                            : 0.55;
                        accept = Random.Shared.NextDouble() < acceptChance;
                    }
                }
                else
                {
                    var hasUnalliedHuman = room.Players.Values.Any(p => !p.IsBot && p.Economy is not null && p.Team == 0 && room.AllianceCount(p.Id) == 0);
                    var maxBotAlliances = hasUnalliedHuman ? 1 : (player.Personality == BotPersonality.Economic ? 2 : 1);
                    accept = !teamMismatch && currentAlliances < maxBotAlliances;
                }
            }
            room.Execute(player.Id, new(Guid.NewGuid(), CommandKind.RespondAlliance, req.Id, Enabled: accept));
        }

        if (!isHardCoalition && (room.ElapsedSeconds < 20 || player.NextSocialAction > room.ElapsedSeconds)) return;
        if (!isHardCoalition) player.NextSocialAction = room.ElapsedSeconds + 55 + Random.Shared.Next(0, 85);

        // Team alliances are established at match start; bots assigned to a team never ally outside it.
        if (player.Team > 0) return;

        if (isHardCoalition)
        {
            var botAllies = room.Players.Values
                .Where(p => p.Id != player.Id && p.IsBot && p.Economy is not null && p.Team == 0
                    && !room.IsAllied(player.Id, p.Id)
                    && !room.HasPendingRequest(player.Id, p.Id))
                .ToArray();
            foreach (var botAlly in botAllies)
                room.Execute(player.Id, new(Guid.NewGuid(), CommandKind.ProposeAlliance, botAlly.Id));
            return;
        }

        var maxOwnAlliances = player.Personality == BotPersonality.Aggressive ? 1 : 2;
        if (room.AllianceCount(player.Id) >= maxOwnAlliances) return;

        var candidates = room.Players.Values
            .Where(p => p.Id != player.Id && p.Economy is not null && p.Team == 0
                && !room.IsAllied(player.Id, p.Id)
                && !room.HasPendingRequest(player.Id, p.Id)
                && !room.RecentlyDeclinedAlliance(player.Id, p.Id)
                && room.AllianceCount(p.Id) < 2)
            .ToArray();

        if (candidates.Length == 0) return;

        RoomPlayer? chosen = null;

        // Include human players organically without piling multiple invites on them at once.
        var unalliedHumans = candidates
            .Where(c => !c.IsBot && room.AllianceCount(c.Id) == 0 && room.PendingIncomingCount(c.Id) == 0)
            .ToArray();
        if (unalliedHumans.Length > 0 && Random.Shared.NextDouble() < 0.55)
        {
            chosen = unalliedHumans[Random.Shared.Next(unalliedHumans.Length)];
        }
        else if (player.Personality == BotPersonality.Economic)
        {
            var preferred = candidates
                .Where(c => !c.IsBot ? room.PendingIncomingCount(c.Id) == 0 : c.Personality is BotPersonality.Economic or BotPersonality.Defensive)
                .ToArray();
            var pool = preferred.Length > 0 ? preferred : candidates;
            chosen = pool[Random.Shared.Next(pool.Length)];
        }
        else if (player.Personality == BotPersonality.Defensive)
        {
            chosen = candidates
                .Where(c => c.IsBot || room.PendingIncomingCount(c.Id) == 0)
                .MinBy(c => player.Economy is not null && c.Economy is not null
                    ? LogisticsWorld.Distance(player.Economy.Village.Position, c.Economy.Village.Position) : 1000);
        }
        else if (player.Personality == BotPersonality.Aggressive && candidates.Length > 1)
        {
            chosen = candidates
                .Where(c => c.IsBot || room.PendingIncomingCount(c.Id) == 0)
                .MaxBy(c => player.Economy is not null && c.Economy is not null
                    ? LogisticsWorld.Distance(player.Economy.Village.Position, c.Economy.Village.Position) : 0);
        }
        else
        {
            var open = candidates.Where(c => c.IsBot || room.PendingIncomingCount(c.Id) == 0).ToArray();
            if (open.Length > 0) chosen = open[Random.Shared.Next(open.Length)];
        }

        if (chosen is not null)
            room.Execute(player.Id, new(Guid.NewGuid(), CommandKind.ProposeAlliance, chosen.Id));
    }
}