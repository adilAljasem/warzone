using System.Text.Json;

namespace Warzone;

public enum RoomStatus { Lobby, Running, Finished, Faulted }
public enum BotPersonality { Aggressive, Economic, Defensive }
public enum BotDifficulty { Easy, Normal, Hard }
public sealed record RoomSettings(int Capacity = 8, int DurationMinutes = 45, int ShieldMinutes = 5,
    double ResourceSpeed = 1, int BotCount = 7, BotDifficulty BotDifficulty = BotDifficulty.Normal)
{
    public void Validate()
    {
        if (Capacity is < 8 or > 16 || DurationMinutes is < 30 or > 60
            || ShieldMinutes is not (0 or 5 or 10) || !double.IsFinite(ResourceSpeed) || ResourceSpeed is < .5 or > 5
            || BotCount < 0 || BotCount >= Capacity || !Enum.IsDefined(BotDifficulty))
            throw new GameRuleException("settings", "Invalid room settings: capacity 8-16, duration 30-60, shield 0/5/10, speed 0.5-5.");
    }
}

public sealed record PlayerSnapshot(Guid Id, string Name, TribeType Tribe, bool IsBot, BotPersonality? Personality,
    bool Ready, bool Online, Guid? VillageId, Position? Position, int? Level, string FlagColor);
public sealed record RoomSummary(string Id, string Name, RoomStatus Status, int Humans, int Bots, int Capacity);
public sealed record RoomSnapshot(string Id, string Name, Guid HostId, Guid You, long Revision, RoomStatus Status,
    RoomSettings Settings, double ElapsedSeconds, double RemainingSeconds, double ShieldRemainingSeconds,
    string Phase, string? EndReason, PlayerSnapshot[] Players, EconomySnapshot? Economy, WorldSnapshot? World, MatchResult? Result,
    ChatMessageSnapshot[] Chat, AllianceSnapshot[] Alliances, AllianceRequestSnapshot[] AllianceRequests);

public interface IGameCommands
{
    CommandResult Execute(Guid actorId, GameCommand command);
}

internal sealed class RoomPlayer(Guid id, string name, TribeType tribe, string flagColor, BotPersonality? personality = null)
{
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    public TribeType Tribe { get; } = tribe;
    public string FlagColor { get; } = flagColor;
    public BotPersonality? Personality { get; set; } = personality;
    public bool IsBot => Personality.HasValue;
    public bool Ready { get; set; }
    public VillageEconomy? Economy { get; set; }
    public double NextBotAction { get; set; }
    public double NextSocialAction { get; set; }
    public Guid? SelectedVillageId { get; set; }
    public Dictionary<Guid, (string Fingerprint, CommandResult Result)> Receipts { get; } = [];
}

// Accessed only while GameServer holds its state lock. No network calls occur inside it.
public sealed partial class GameRoom : IGameCommands
{
    private static readonly string[] FlagPalette = ["#368ac2", "#e57859", "#9570be", "#e8b642", "#53a591", "#e58fab", "#7298d5", "#99ae55", "#d85a72", "#42a5a8", "#b47b4d", "#776fd0", "#7da53d", "#d278b5", "#4f9bd5", "#c45d3c"];
    private readonly Dictionary<Guid, RoomPlayer> players = [];
    private LogisticsWorld? world;
    private string NextFlagColor() => FlagPalette.First(color => players.Values.All(player => player.FlagColor != color));
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
    public IEnumerable<Guid> HumanIds => players.Values.Where(x => !x.IsBot).Select(x => x.Id);
    public bool Contains(Guid playerId) => players.ContainsKey(playerId);
    internal IReadOnlyDictionary<Guid, RoomPlayer> Players => players;

    public GameRoom(Guid hostId, string playerName, TribeType tribe, string name, RoomSettings settings, DateTimeOffset now)
    {
        settings.Validate();
        Name = ValidateName(name);
        HostId = hostId;
        Settings = settings;
        LastOccupiedAt = now;
        Join(hostId, playerName, tribe);
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
    public void Join(Guid id, string name, TribeType tribe)
    {
        RequireLobby();
        if (id == Guid.Empty || !Enum.IsDefined(tribe)) throw new GameRuleException("player", "Invalid player or tribe.");
        if (players.ContainsKey(id)) return;
        if (players.Count + Settings.BotCount >= Settings.Capacity) throw new GameRuleException("full", "This room is full, including reserved bot seats.");
        players.Add(id, new(id, ValidateName(name), tribe, NextFlagColor()));
        Revision++;
    }
    public void Configure(Guid actor, RoomSettings settings)
    {
        RequireHost(actor); RequireLobby(); settings.Validate();
        if (players.Count + settings.BotCount > settings.Capacity) throw new GameRuleException("full", "Settings would exceed room capacity.");
        Settings = settings;
        foreach (var player in players.Values) player.Ready = false;
        Revision++;
    }
    public void SetReady(Guid actor, bool ready)
    {
        RequireLobby();
        if (!players.TryGetValue(actor, out var player)) throw new GameRuleException("membership", "Join the room first.");
        player.Ready = ready;
        Revision++;
    }
    public void Leave(Guid actor)
    {
        if (Status == RoomStatus.Running) throw new GameRuleException("running", "Your seat is reserved until the match ends. Disconnect to leave temporarily.");
        if (!players.Remove(actor)) return;
        if (HostId == actor) HostId = HumanIds.FirstOrDefault();
        Revision++;
    }
    internal void Abandon(Guid actor)
    {
        if (Status == RoomStatus.Lobby) { Leave(actor); return; }
        if (!players.TryGetValue(actor, out var player)) return;
        player.Personality ??= BotPersonality.Economic;
        if (HostId == actor) HostId = HumanIds.FirstOrDefault();
        Revision++;
    }
    public void Start(Guid actor, Func<Guid, bool> online)
    {
        RequireHost(actor); RequireLobby();
        if (players.Count + Settings.BotCount < 8) throw new GameRuleException("minimum_players", "At least eight players, including bots, are required.");
        if (players.Values.Any(x => !x.Ready || !online(x.Id))) throw new GameRuleException("not_ready", "Every human player must be online and ready.");
        for (var i = 0; i < Settings.BotCount; i++)
        {
            var id = Guid.NewGuid();
            players.Add(id, new(id, $"Bot {i + 1}", (TribeType)(i % 5), NextFlagColor(), (BotPersonality)(i % 3)) { Ready = true });
        }
        var index = 0;
        foreach (var player in players.Values)
        {
            var angle = index++ * Math.Tau / players.Count;
            player.Economy = new(player.Id, player.Tribe, new(LogisticsWorld.MapCenter + LogisticsWorld.SettlementRadius * Math.Cos(angle), LogisticsWorld.MapCenter + LogisticsWorld.SettlementRadius * Math.Sin(angle)));
            player.NextBotAction = 1 + index * .25;
        }
        world = new(players.Values.Select(x => x.Economy!), Random.Shared.Next());
        Status = RoomStatus.Running;
        Revision++;
    }

    public CommandResult Execute(Guid actorId, GameCommand command)
    {
        if (!players.TryGetValue(actorId, out var player)) return new(command.RequestId, false, "membership", "You are not in this room.");
        var fingerprint = JsonSerializer.Serialize(command with { Formation = command.Formation?.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value) });
        if (player.Receipts.TryGetValue(command.RequestId, out var receipt))
            return receipt.Fingerprint == fingerprint ? receipt.Result : new(command.RequestId, false, "request_conflict", "Request ID was already used for another command.");
        if (command.RequestId == Guid.Empty || player.Receipts.Count >= 4096)
            return new(command.RequestId, false, "request_limit", "A non-empty unique request ID is required; limit 4096 per player per match.");
        CommandResult result;
        try
        {
            if (Status != RoomStatus.Running) throw new GameRuleException("not_running", "The match is not running.");
            if (command.Kind == CommandKind.SendChat) ApplySocial(actorId, command);
            else
            {
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
        player.Receipts.Add(command.RequestId, (fingerprint, result));
        return result;
    }

    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > 1) throw new ArgumentOutOfRangeException(nameof(seconds));
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
            foreach (var player in players.Values.Where(x => x.IsBot && x.NextBotAction <= ElapsedSeconds))
            {
                player.Economy = world.Primary(player.Id);
                if (player.Economy is null) continue;
                BotController.Act(this, player, Settings.BotDifficulty);
                BotController.ActSocial(this, player);
                if (ElapsedSeconds > 8) world.BotAct(player.Id, this, player.Personality!.Value, Settings.BotDifficulty);
                player.NextBotAction = ElapsedSeconds + (Settings.BotDifficulty switch { BotDifficulty.Easy => 8, BotDifficulty.Hard => 2, _ => 4 });
            }
        Revision++;
    }

    internal void MarkFaulted()
    {
        Status = RoomStatus.Faulted;
        EndReason = "ServerError";
        Revision++;
    }

    public RoomSummary Summary() => new(Id, Name, Status, HumanIds.Count(), Settings.BotCount, Settings.Capacity);
    public RoomSnapshot Snapshot(Guid viewer, Func<Guid, bool> online)
    {
        if (!players.ContainsKey(viewer)) throw new GameRuleException("membership", "You are not in this room.");
        var running = Status != RoomStatus.Lobby;
        var remaining = Math.Max(0, Settings.DurationMinutes * 60 - ElapsedSeconds);
        var phase = Status != RoomStatus.Running ? Status.ToString() : ElapsedSeconds >= 1800 || remaining <= 900 ? "Finale" : ElapsedSeconds < 600 ? "Foundation" : "Expansion";
        return new(Id, Name, HostId, viewer, Revision, Status, Settings, ElapsedSeconds, remaining,
            running ? Math.Max(0, Settings.ShieldMinutes * 60 - ElapsedSeconds) : Settings.ShieldMinutes * 60,
            phase, EndReason, players.Values.Select(x =>
            {
                var city = world is null ? x.Economy : world.Primary(x.Id);
                return new PlayerSnapshot(x.Id, x.Name, x.Tribe, x.IsBot, x.Personality,
                    x.Ready, x.IsBot || online(x.Id), city?.Village.Id, city?.Village.Position, city?.Village.Level, x.FlagColor);
            }).ToArray(),
            world?.EconomySnapshot(viewer, players[viewer].SelectedVillageId, Settings.ResourceSpeed), world?.Snapshot(viewer), Result,
            ChatView(), AllianceView(), AllianceRequestView(viewer));
    }
}

internal static class BotController
{
    private static int UnitPriority(TargetSnapshot target, BotPersonality? personality)
    {
        var role = target.UnitId?.Split('-').LastOrDefault() ?? "";
        return (role, personality) switch
        {
            ("crossbowman", _) => 96,
            ("heavycavalry", _) => 94,
            ("swordsman", BotPersonality.Aggressive) => 91,
            ("cavalry", BotPersonality.Aggressive) => 89,
            ("spearman", BotPersonality.Defensive) => 90,
            ("archer", BotPersonality.Defensive) => 85,
            ("swordsman", _) => 86,
            ("cavalry", _) => 84,
            ("spearman", _) => 80,
            ("lightcavalry", _) => 76,
            ("archer", _) => 72,
            ("infantry", _) => 55,
            _ => 0
        };
    }

    private static int BuildingScore(TargetSnapshot b, EconomySnapshot economy, BotPersonality? personality)
    {
        var score = b.Level * 10;
        var nearCap = economy.Resources.Wood >= economy.Capacity * 3 / 4
            || economy.Resources.Iron >= economy.Capacity * 3 / 4
            || economy.Resources.Stone >= economy.Capacity * 3 / 4
            || economy.Resources.Wheat >= economy.Capacity * 3 / 4;
        var barracksLevel = economy.Targets.FirstOrDefault(t => t.Name == "Barracks")?.Level ?? 1;
        score -= b.Name switch
        {
            "Warehouse" => (nearCap ? 75 : 22) + (personality == BotPersonality.Economic ? 12 : 0),
            "MainHall" => 24 + (personality == BotPersonality.Economic ? 12 : 0),
            "Barracks" => (b.Level < 3 ? 48 : b.Level < 5 ? 28 : b.Level < 8 ? 16 : 6) + (personality == BotPersonality.Aggressive ? 12 : 0),
            "Stable" => (b.Level < 3 ? 38 : b.Level < 5 ? 24 : b.Level < 8 ? 14 : 5) + (personality != BotPersonality.Defensive ? 8 : 0),
            "Smithy" => (b.Level < 6 ? 24 : 12) + (personality == BotPersonality.Aggressive ? 10 : 0),
            "Wall" => (economy.Besieged ? 55 : 14) + (personality == BotPersonality.Defensive ? 24 : b.Level < 3 ? 10 : 0),
            "Hospital" => (economy.Wounded > 0 ? 36 : 10) + (personality == BotPersonality.Defensive ? 16 : b.Level < 3 ? 8 : 0),
            "SiegeWorkshop" => (barracksLevel >= 3 && b.Level < 3 ? 28 : b.Level < 5 && personality == BotPersonality.Aggressive ? 18 : 6),
            "Market" => b.Level < (personality == BotPersonality.Economic ? 4 : 2) ? (personality == BotPersonality.Economic ? 24 : 10) : 0,
            _ => 0
        };
        return score;
    }

    public static void Act(IGameCommands commands, RoomPlayer player, BotDifficulty difficulty = BotDifficulty.Normal) =>
        ActEconomy(commands, player.Id, player.Economy!.Snapshot(), player.Personality, difficulty);

    public static void ActEconomy(IGameCommands commands, Guid actorId, EconomySnapshot economy, BotPersonality? personality, BotDifficulty difficulty = BotDifficulty.Normal)
    {
        var wheatNet = economy.Production.FirstOrDefault(p => p.Resource == "Wheat")?.NetPerMinute ?? 0;
        var starving = economy.FoodShortage || (economy.Resources.Wheat < 220 && wheatNet < 0);
        var trainings = economy.Targets.Where(x => x.Kind == "Training").ToArray();
        if (starving)
        {
            var active = trainings.Where(x => x.Auto).OrderBy(x => UnitPriority(x, personality)).FirstOrDefault();
            if (active is not null)
                commands.Execute(actorId, new(Guid.NewGuid(), CommandKind.SetAutoQueue, active.Id, Enabled: false));
        }
        else if (economy.Resources.Wheat >= 300 || wheatNet >= 15)
        {
            var nonCombatAuto = trainings.FirstOrDefault(x => x.Auto && UnitPriority(x, personality) == 0);
            if (nonCombatAuto is not null)
                commands.Execute(actorId, new(Guid.NewGuid(), CommandKind.SetAutoQueue, nonCombatAuto.Id, Enabled: false));
            else
            {
                var unlockedCombat = trainings.Where(x => x.Unlocked && UnitPriority(x, personality) > 0)
                    .OrderByDescending(x => UnitPriority(x, personality)).ToArray();
                var desired = new List<TargetSnapshot>();
                if (unlockedCombat.FirstOrDefault(x => x.TrainingBuilding == "Barracks") is { } bestBarracks)
                    desired.Add(bestBarracks);
                if (unlockedCombat.FirstOrDefault(x => x.TrainingBuilding == "Stable") is { } bestStable)
                    desired.Add(bestStable);
                foreach (var unit in unlockedCombat)
                    if (desired.Count < economy.AutoSlots && !desired.Contains(unit))
                        desired.Add(unit);
                var missing = desired.FirstOrDefault(x => !x.Auto);
                if (missing is not null)
                {
                    if (economy.UsedAutoSlots < economy.AutoSlots)
                        commands.Execute(actorId, new(Guid.NewGuid(), CommandKind.SetAutoQueue, missing.Id, Enabled: true));
                    else if (trainings.Where(x => x.Auto && !desired.Contains(x)).OrderBy(x => UnitPriority(x, personality)).FirstOrDefault() is { } obsolete)
                        commands.Execute(actorId, new(Guid.NewGuid(), CommandKind.SetAutoQueue, obsolete.Id, Enabled: false));
                }
            }
        }

        var mainHallLevel = economy.Targets.FirstOrDefault(t => t.Name == "MainHall")?.Level ?? 1;
        var building = economy.Targets.Where(x => x.Kind == "Building" && (x.Name == "MainHall" || x.Level < mainHallLevel) && x.UpgradeCost is { } cost && economy.Resources.Covers(cost))
            .OrderBy(x => BuildingScore(x, economy, personality)).FirstOrDefault();
        if (building is not null)
        {
            commands.Execute(actorId, new(Guid.NewGuid(), CommandKind.Upgrade, building.Id));
            if (difficulty == BotDifficulty.Hard && economy.Resources.Total > economy.Capacity * 2)
            {
                var tier = trainings.Where(x => x.Auto && x.Unlocked && x.Level < 5 && x.UpgradeCost is { } c && (economy.Resources - building.UpgradeCost!.Value).Covers(c))
                    .OrderBy(x => x.Level).FirstOrDefault();
                if (tier is not null)
                    commands.Execute(actorId, new(Guid.NewGuid(), CommandKind.Upgrade, tier.Id));
            }
        }
    }

    public static void ActSocial(GameRoom room, RoomPlayer player)
    {
        var pending = room.PendingRequestsFor(player.Id);
        foreach (var req in pending)
        {
            var currentAlliances = room.AllianceCount(player.Id);
            var maxAlliances = player.Personality == BotPersonality.Economic ? 2 : 1;
            var accept = currentAlliances < maxAlliances;
            room.Execute(player.Id, new(Guid.NewGuid(), CommandKind.RespondAlliance, req.Id, Enabled: accept));
        }

        if (room.ElapsedSeconds < 15 || player.NextSocialAction > room.ElapsedSeconds) return;
        player.NextSocialAction = room.ElapsedSeconds + 35;

        if (room.AllianceCount(player.Id) >= (player.Personality == BotPersonality.Economic ? 2 : 1)) return;

        var candidates = room.Players.Values
            .Where(p => p.Id != player.Id && p.Economy is not null
                && !room.IsAllied(player.Id, p.Id)
                && !room.HasPendingRequest(player.Id, p.Id))
            .ToArray();

        if (candidates.Length == 0) return;

        RoomPlayer? chosen = null;
        if (player.Personality == BotPersonality.Economic)
            chosen = candidates.FirstOrDefault(c => c.Personality is BotPersonality.Economic or BotPersonality.Defensive) ?? candidates[0];
        else if (player.Personality == BotPersonality.Defensive)
            chosen = candidates.MinBy(c => player.Economy is not null && c.Economy is not null ? LogisticsWorld.Distance(player.Economy.Village.Position, c.Economy.Village.Position) : 1000);
        else if (player.Personality == BotPersonality.Aggressive && candidates.Length > 1)
            chosen = candidates.MaxBy(c => player.Economy is not null && c.Economy is not null ? LogisticsWorld.Distance(player.Economy.Village.Position, c.Economy.Village.Position) : 0);

        if (chosen is not null)
            room.Execute(player.Id, new(Guid.NewGuid(), CommandKind.ProposeAlliance, chosen.Id));
    }
}
