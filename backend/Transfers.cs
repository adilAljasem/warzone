namespace Warzone;

public sealed partial class LogisticsWorld
{
    private bool ApplyShipment(Guid actor, GameCommand command, double now)
    {
        if (command.Kind != CommandKind.SendResources) return false;
        var source = EconomyTarget(actor, command.TargetId);
        if (source.Village.Id != command.TargetId)
            throw new GameRuleException("not_owned", "Send resources from an owned village.");
        var recipient = villages.GetValueOrDefault(command.DestinationId ?? Guid.Empty);
        if (recipient is null || recipient.Ruined || recipient == source)
            throw new GameRuleException("target", "Choose another surviving village for this shipment.");
        if (source.Besieged)
            throw new GameRuleException("blockaded", "A blockaded village cannot send resource shipments.");
        var cargo = command.Shipment;
        if (cargo.Total < 1 || new[] { cargo.Wood, cargo.Iron, cargo.Stone, cargo.Wheat }.Any(amount => amount > 1000000))
            throw new GameRuleException("shipment", "Send at least one resource, up to 1000000 of each type.");
        if (!source.Resources.Covers(cargo))
            throw new GameRuleException("resources", "Not enough resources.");
        var requested = TransportWagons(source, cargo.Total);
        var stacks = requested.Select(w => new TroopStack(BaseUnit.Create(GameCatalog.Units[w.Key]), w.Value)).ToArray();
        var food = RoundTripFood(stacks, source.Village.Position, recipient.Village.Position);
        if (!source.Resources.Covers(cargo + new Resources(0, 0, 0, food)))
            throw new GameRuleException("resources", "The village also needs enough wheat for the shipment's round trip.");
        var wagons = source.Depart(requested, food);
        source.Pay(cargo);
        convoys.Add(new(actor, source.Village.Position, source.Village.Id, null, wagons, food)
        {
            Kind = ConvoyKind.Shipment, RecipientId = recipient.Village.OwnerId,
            TargetVillageId = recipient.Village.Id, Cargo = cargo
        });
        Note(actor, now, "Your resource shipment has departed. Its wagons will return after delivery.");
        Note(recipient.Village.OwnerId, now, "A resource shipment is on its way to your village.", "shipment", targetId: recipient.Village.Id);
        return true;
    }
}
