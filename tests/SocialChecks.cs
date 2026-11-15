using Warzone;

internal static class SocialChecks
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        void Reject(Action action, string code)
        {
            try { action(); }
            catch (GameRuleException error) when (error.Code == code) { checks++; return; }
            throw new Exception($"Expected rule rejection: {code}");
        }

        var sender = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(700, 700));
        var recipient = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(1100, 700));
        var world = new LogisticsWorld([sender, recipient], 731);
        var before = recipient.Resources;
        Reject(() => world.Apply(recipient.Village.OwnerId, new(Guid.NewGuid(), CommandKind.SendResources,
            sender.Village.Id, DestinationId: recipient.Village.Id, Shipment: new(100, 0, 0, 0)), 0, 0), "not_owned");
        Reject(() => world.Apply(sender.Village.OwnerId, new(Guid.NewGuid(), CommandKind.SendResources,
            sender.Village.Id, DestinationId: recipient.Village.Id), 0, 0), "shipment");
        world.Apply(sender.Village.OwnerId, new(Guid.NewGuid(), CommandKind.SendResources,
            sender.Village.Id, DestinationId: recipient.Village.Id, Shipment: new(100, 50, 25, 0)), 0, 0);
        Check(sender.Resources.Wood == 1400 && sender.Resources.Iron == 1450 && sender.Resources.Stone == 1475,
            "A resource shipment debits the exact cargo at departure");
        var shipment = world.Snapshot(sender.Village.OwnerId).Convoys.Single();
        Check(shipment.Kind == ConvoyKind.Shipment && shipment.RecipientId == recipient.Village.OwnerId
            && shipment.Cargo == new Resources(100, 50, 25, 0), "A physical shipment carries resources to its recipient");
        Check(recipient.Resources == before, "Shipment resources do not teleport to the recipient");
        for (var i = 0; i < 1200; i++) world.Advance(.25, (i + 1) * .25, 0, 3600);
        Check(recipient.Resources.Wood == before.Wood + 100 && recipient.Resources.Iron == before.Iron + 50
            && recipient.Resources.Stone == before.Stone + 25, "Shipment cargo arrives after travel");
        Check(world.Snapshot(sender.Village.OwnerId).Convoys.Length == 0 && sender.Available("romans-wagon") == 2,
            "Shipment wagons return home after delivery");

        var host = Guid.NewGuid();
        var guest = Guid.NewGuid();
        var third = Guid.NewGuid();
        var room = new GameRoom(host, "Host", TribeType.Romans, "Social", new(BotCount: 5), DateTimeOffset.UtcNow);
        room.Join(guest, "Guest", TribeType.Greeks);
        room.Join(third, "Third", TribeType.Mongols);
        foreach (var id in new[] { host, guest, third }) room.SetReady(id, true);
        room.Start(host, _ => true);
        var chatCommand = new GameCommand(Guid.NewGuid(), CommandKind.SendChat, Guid.Empty, Message: "Hello allies");
        Check(room.Execute(host, chatCommand).Accepted && room.Execute(host, chatCommand).Accepted
            && room.Snapshot(guest, _ => true).Chat.Count(m => m.Text == "Hello allies") == 1,
            "Chat is shared and replay cannot duplicate a message");
        Check(room.Execute(host, new(Guid.NewGuid(), CommandKind.SendChat, Guid.Empty, Message: " ")).Code == "chat",
            "Blank chat messages are rejected");
        Check(room.Execute(host, new(Guid.NewGuid(), CommandKind.ProposeAlliance, guest)).Accepted,
            "A player can propose an alliance");
        Check(room.Execute(host, new(Guid.NewGuid(), CommandKind.ProposeAlliance, third)).Accepted,
            "A player can send independent requests to multiple players");
        var guestRequest = room.Snapshot(guest, _ => true).AllianceRequests.Single();
        Check(room.Snapshot(host, _ => true).AllianceRequests.Length == 2
            && room.Snapshot(third, _ => true).AllianceRequests.Single().RecipientId == third,
            "Only the sender and recipient see each pending request");
        Check(room.Execute(host, new(Guid.NewGuid(), CommandKind.RespondAlliance, guestRequest.Id)).Code == "alliance",
            "The sender cannot accept their own alliance request");
        Check(room.Execute(guest, new(Guid.NewGuid(), CommandKind.RespondAlliance, guestRequest.Id, Enabled: true)).Accepted,
            "The recipient accepts an alliance");
        Check(room.Snapshot(third, _ => true).Alliances.Single().FirstPlayerId != third
            && room.Snapshot(third, _ => true).Alliances.Single().SecondPlayerId != third,
            "Alliances are public without adding unrelated players");
        Check(room.Snapshot(guest, _ => true).Chat.Any(m => m.Kind == "Alliance"
            && m.Text == "Alliance formed between Host and Guest."), "Acceptance announces both players in the shared chat");
        Check(room.Execute(host, new(Guid.NewGuid(), CommandKind.ProposeAlliance, guest)).Code == "alliance",
            "Duplicate alliances are rejected");
        Check(room.Execute(host, new(Guid.NewGuid(), CommandKind.ProposeAlliance, host)).Code == "alliance",
            "Self alliance is rejected");
        var bot = room.Snapshot(host, _ => true).Players.First(p => p.IsBot);
        Check(room.Execute(host, new(Guid.NewGuid(), CommandKind.ProposeAlliance, bot.Id)).Accepted,
            "Bots can receive alliance requests and answer them");
        var botRequest = room.Snapshot(host, _ => true).AllianceRequests.Single(r => r.RecipientId == bot.Id);
        room.Execute(host, new(Guid.NewGuid(), CommandKind.CancelAllianceRequest, botRequest.Id));
        var thirdRequest = room.Snapshot(host, _ => true).AllianceRequests.Single(r => r.RecipientId == third);
        Check(room.Execute(host, new(Guid.NewGuid(), CommandKind.CancelAllianceRequest, thirdRequest.Id)).Accepted
            && room.Snapshot(third, _ => true).AllianceRequests.Length == 0, "A sender can cancel a pending request");
        var hostPlayers = room.Snapshot(host, _ => true).Players;
        var guestPlayers = room.Snapshot(guest, _ => true).Players;
        Check(hostPlayers.All(player => player.FlagColor == guestPlayers.Single(other => other.Id == player.Id).FlagColor),
            "Every viewer receives the same authoritative player flag colors");
        Check(hostPlayers.Select(player => player.FlagColor).Distinct().Count() == hostPlayers.Length
            && hostPlayers.All(player => player.FlagColor.StartsWith('#')), "Every room participant has a unique valid flag color");

        var cHost = Guid.NewGuid();
        var cGuest = Guid.NewGuid();
        var lobby = new GameRoom(cHost, "ColorHost", TribeType.Romans, "CustomLobby", new(Capacity: 8, BotCount: 6), DateTimeOffset.UtcNow);
        lobby.Join(cGuest, "ColorGuest", TribeType.Greeks);
        var botPlayer = lobby.Snapshot(cHost, _ => true).Players.First(p => p.IsBot);
        var botColor = botPlayer.FlagColor;
        lobby.ChangeColor(cHost, cHost, botColor);
        var afterColor = lobby.Snapshot(cHost, _ => true).Players;
        Check(afterColor.Single(p => p.Id == cHost).FlagColor == botColor
            && afterColor.Select(p => p.FlagColor).Distinct().Count() == afterColor.Length,
            "Player can pick any color including swapping with a bot while keeping colors unique");
        Reject(() => lobby.ChangeColor(cGuest, cGuest, botColor), "color");

        lobby.ChangeSpawnSlot(cHost, cHost, 2);
        Reject(() => lobby.ChangeSpawnSlot(cGuest, cGuest, 2), "spawn_slot");
        lobby.ChangeSpawnSlot(cHost, botPlayer.Id, 4);
        lobby.ChangeSpawnSlot(cGuest, cGuest, 4);
        Check(lobby.Snapshot(cHost, _ => true).Players.Single(p => p.Id == cGuest).SpawnSlot == 4
            && lobby.Snapshot(cHost, _ => true).Players.Single(p => p.Id == botPlayer.Id).SpawnSlot is null,
            "Human selecting a bot spawn slot swaps the bot back to auto placement");
        lobby.ChangeSpawnSlot(cGuest, cGuest, 6);
        var preStartSnap = lobby.Snapshot(cHost, _ => true);
        Check(preStartSnap.Players.All(p => p.Position is not null && p.EffectiveSpawnSlot is not null),
            "Lobby snapshots expose resolved positions and effective spawn slots for all players and bots before match start");
        var botAutoSlot = preStartSnap.Players.First(p => p.IsBot).EffectiveSpawnSlot!.Value;
        var botAutoId = preStartSnap.Players.First(p => p.EffectiveSpawnSlot == botAutoSlot).Id;
        lobby.ChangeSpawnSlot(cGuest, cGuest, botAutoSlot);
        var afterTakeBotSnap = lobby.Snapshot(cHost, _ => true);
        Check(afterTakeBotSnap.Players.Single(p => p.Id == cGuest).EffectiveSpawnSlot == botAutoSlot
            && afterTakeBotSnap.Players.Single(p => p.Id == botAutoId).EffectiveSpawnSlot != botAutoSlot,
            "Human selecting an auto-placed bot slot moves into it while the bot shifts to another empty slot");
        var guestEffective = afterTakeBotSnap.Players.Single(p => p.Id == cGuest).EffectiveSpawnSlot!.Value;
        Reject(() => lobby.ChangeSpawnSlot(cHost, cHost, guestEffective), "spawn_slot");
        lobby.ChangeSpawnSlot(cGuest, cGuest, 6);

        var tcHost = Guid.NewGuid();
        var teamLobby = new GameRoom(tcHost, "B", TribeType.Romans, "Cluster", new(Capacity: 8, BotCount: 3), DateTimeOffset.UtcNow);
        var teamBots = teamLobby.Snapshot(tcHost, _ => true).Players.Where(p => p.IsBot).ToList();
        foreach (var tb in teamBots) teamLobby.ChangeTeam(tcHost, tb.Id, 2);
        var botSlots = teamLobby.Snapshot(tcHost, _ => true).Players.Where(p => p.IsBot).Select(p => p.EffectiveSpawnSlot!.Value).OrderBy(s => s).ToList();
        var botWrapGaps = new[] { botSlots[1] - botSlots[0], botSlots[2] - botSlots[1], 8 - botSlots[2] + botSlots[0] };
        Check(botSlots.Count == 3 && botWrapGaps.Count(g => g == 1) == 2,
            "Same-team bots automatically occupy contiguous adjacent lobby slots");

        lobby.SetReady(cHost, true);
        lobby.SetReady(cGuest, true);
        lobby.Start(cHost, _ => true);
        var settlements = lobby.Snapshot(cHost, _ => true).World!.Warfare!.Settlements;
        var hostVillage = settlements.Single(s => s.OwnerId == cHost);
        var guestVillage = settlements.Single(s => s.OwnerId == cGuest);
        var hostAngle = 2 * Math.Tau / 8;
        var guestAngle = 6 * Math.Tau / 8;
        Check(Math.Abs(hostVillage.Position.X - (2250 + 1750 * Math.Cos(hostAngle))) < 1
            && Math.Abs(hostVillage.Position.Y - (2250 + 1750 * Math.Sin(hostAngle))) < 1
            && Math.Abs(guestVillage.Position.X - (2250 + 1750 * Math.Cos(guestAngle))) < 1
            && Math.Abs(guestVillage.Position.Y - (2250 + 1750 * Math.Sin(guestAngle))) < 1,
            "Players who choose lobby spawn slots start at the corresponding map angles");

        return checks;
    }
}
