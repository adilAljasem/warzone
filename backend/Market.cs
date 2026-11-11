namespace Warzone;

public sealed record MarketOfferSnapshot(Guid Id, Guid OwnerId, Guid VillageId, ResourceType OfferResource,
    long OfferAmount, ResourceType AskResource, long AskAmount, int Wagons, double CreatedAt);
internal sealed record MarketOffer(Guid Id, Guid OwnerId, Guid VillageId, ResourceType OfferResource,
    long OfferAmount, ResourceType AskResource, long AskAmount, TroopStack[] Wagons, double CreatedAt);

public sealed partial class LogisticsWorld
{
    private readonly List<MarketOffer> marketOffers = [];
    private static Resources ResourceAmount(ResourceType type, long quantity) => type switch
    {
        ResourceType.Wood => new(quantity, 0, 0, 0), ResourceType.Iron => new(0, quantity, 0, 0),
        ResourceType.Stone => new(0, 0, quantity, 0), ResourceType.Wheat => new(0, 0, 0, quantity),
        _ => throw new GameRuleException("resource", "Unknown resource.")
    };
    private static Dictionary<string, int> TransportWagons(VillageEconomy city, long cargo)
    {
        var id = UnitId(city, UnitRole.Wagon);
        var stats = GameCatalog.Units[id].Stats;
        var provisions = Math.Ceiling(stats.WheatUpkeep / 60 * (2 * Math.Sqrt(2) * (MapMaximum - MapMinimum) / stats.Speed + 60));
        var count = (int)Math.Ceiling(cargo / Math.Max(1, stats.CarryCapacity - provisions));
        if (city.Available(id) < count) throw new GameRuleException("wagons", "Not enough available wagons for this trade.");
        return new() { [id] = count };
    }
    private static void RequireMarket(VillageEconomy city)
    {
        if (city.Ruined || city.Besieged) throw new GameRuleException("market", "A blockaded or ruined market cannot trade.");
        if (city.BuildingLevel(BuildingType.Market) < 1) throw new GameRuleException("market", "A market is required.");
    }
    private bool ApplyMarket(Guid actor, GameCommand command, double now)
    {
        if (command.Kind is not (CommandKind.PostMarketOffer or CommandKind.AcceptMarketOffer or CommandKind.CancelMarketOffer)) return false;
        if (command.Kind == CommandKind.CancelMarketOffer)
        {
            var offer = marketOffers.Find(o => o.Id == command.TargetId && o.OwnerId == actor)
                ?? throw new GameRuleException("offer", "This offer is unavailable or belongs to another player.");
            RefundOffer(offer); Note(actor, now, "Market offer cancelled. Reserved goods and wagons are available again."); return true;
        }
        var city = EconomyTarget(actor, command.TargetId); RequireMarket(city);
        if (command.Kind == CommandKind.PostMarketOffer)
        {
            if (!Enum.IsDefined(command.OfferResource) || !Enum.IsDefined(command.AskResource)
                || command.OfferResource == command.AskResource || command.OfferAmount is < 1 or > 1000000 || command.AskAmount is < 1 or > 1000000)
                throw new GameRuleException("offer", "Offer two different resources in positive whole quantities, up to 1000000 each.");
            if (marketOffers.Count(o => o.VillageId == city.Village.Id) >= city.BuildingLevel(BuildingType.Market))
                throw new GameRuleException("market_slots", "Upgrade the market to list more offers.");
            var requested = TransportWagons(city, command.OfferAmount);
            var goods = ResourceAmount(command.OfferResource, command.OfferAmount);
            if (!city.Resources.Covers(goods)) throw new GameRuleException("resources", "Not enough resources.");
            var wagons = city.Depart(requested, 0); city.Pay(goods);
            city.ReservedTradeWagons += wagons.Sum(w => w.Count);
            marketOffers.Add(new(Guid.NewGuid(), actor, city.Village.Id, command.OfferResource, command.OfferAmount, command.AskResource, command.AskAmount, wagons, now));
            Note(actor, now, "Market offer posted. Goods and transport wagons are reserved."); return true;
        }
        var selected = marketOffers.Find(o => o.Id == command.DestinationId)
            ?? throw new GameRuleException("offer", "This offer was already accepted or cancelled.");
        if (selected.OwnerId == actor) throw new GameRuleException("offer", "You cannot accept your own offer.");
        var seller = villages[selected.VillageId]; RequireMarket(seller);
        if (seller.Village.OwnerId != selected.OwnerId) throw new GameRuleException("offer", "This market changed ownership.");
        var buyerWagons = TransportWagons(city, selected.AskAmount);
        var sellerFood = RoundTripFood(selected.Wagons, seller.Village.Position, city.Village.Position);
        var buyerStacks = buyerWagons.Select(w => new TroopStack(BaseUnit.Create(GameCatalog.Units[w.Key]), w.Value)).ToArray();
        var buyerFood = RoundTripFood(buyerStacks, city.Village.Position, seller.Village.Position);
        var payment = ResourceAmount(selected.AskResource, selected.AskAmount);
        if (!seller.Resources.Covers(new(0, 0, 0, sellerFood)) || !city.Resources.Covers(payment + new Resources(0, 0, 0, buyerFood)))
            throw new GameRuleException("resources", "Both markets need goods and wheat provisions for the round trip.");
        // All validation precedes mutation; the room lock also makes accepting an offer single-use.
        seller.Pay(new(0, 0, 0, sellerFood));
        city.Pay(payment); var transport = city.Depart(buyerWagons, buyerFood);
        StartTrade(seller, city, selected.Wagons, sellerFood, ResourceAmount(selected.OfferResource, selected.OfferAmount));
        StartTrade(city, seller, transport, buyerFood, payment);
        marketOffers.Remove(selected);
        seller.ReservedTradeWagons -= selected.Wagons.Sum(w => w.Count);
        Note(actor, now, "Trade accepted. Two caravans are carrying the exchange; wagons return after delivery.");
        Note(seller.Village.OwnerId, now, "Your market offer was accepted. Trade caravans have departed."); return true;
    }
    private static long RoundTripFood(TroopStack[] wagons, Position from, Position to) =>
        (long)Math.Ceiling(wagons.Sum(w => w.Count * w.Unit.WheatUpkeep) / 60 * (2 * Distance(from, to) / wagons.Min(w => w.Unit.Speed) + 60));
    private void StartTrade(VillageEconomy from, VillageEconomy to, TroopStack[] wagons, long food, Resources cargo) =>
        convoys.Add(new(from.Village.OwnerId, from.Village.Position, from.Village.Id, null, wagons, food)
        { Kind = ConvoyKind.Trade, RecipientId = to.Village.OwnerId, TargetVillageId = to.Village.Id, Cargo = cargo });
    private void RefundOffer(MarketOffer offer)
    {
        var city = villages[offer.VillageId];
        city.Arrive(offer.Wagons, 0); city.ReceiveCargo(ResourceAmount(offer.OfferResource, offer.OfferAmount));
        city.ReservedTradeWagons -= offer.Wagons.Sum(w => w.Count);
        marketOffers.Remove(offer);
    }
    private void CancelVillageOffers(VillageEconomy city)
    { foreach (var offer in marketOffers.Where(o => o.VillageId == city.Village.Id).ToArray()) RefundOffer(offer); }
    private MarketOfferSnapshot[] MarketSnapshot() => marketOffers.Select(o => new MarketOfferSnapshot(o.Id, o.OwnerId, o.VillageId,
        o.OfferResource, o.OfferAmount, o.AskResource, o.AskAmount, o.Wagons.Sum(w => w.Count), o.CreatedAt)).ToArray();
    private void AdvanceVillageDelivery(Convoy convoy, double seconds, double now)
    {
        var home = villages[convoy.SourceVillageId];
        var recipient = villages.GetValueOrDefault(convoy.TargetVillageId ?? Guid.Empty);
        if (recipient is null || recipient.Ruined || recipient.Village.OwnerId != convoy.RecipientId) convoy.Returning = true;
        var destination = ConvoyCityDestination(convoy,convoy.Returning ? home : recipient!);
        Move(convoy, destination, convoy.Speed * seconds);
        if (Distance(convoy.Position, destination) > 3) return;
        if (convoy.Returning)
        {
            if (home.Besieged) return;
            home.Arrive(convoy.Troops, convoy.WheatCargo); home.ReceiveCargo(convoy.Cargo); convoys.Remove(convoy);
            Note(convoy.OwnerId, now, convoy.Kind == ConvoyKind.Shipment
                ? "Resource shipment wagons have returned and are available again."
                : "Trade wagons have returned and are available again.");
        }
        else
        {
            if (recipient!.Besieged) return;
            recipient.ReceiveCargo(convoy.Cargo); convoy.Cargo = new(); convoy.Returning = true;
            Note(recipient.Village.OwnerId, now, convoy.Kind == ConvoyKind.Shipment
                ? "A resource shipment has arrived at your village."
                : "Purchased goods arrived with a trade caravan.");
            Note(convoy.OwnerId, now, convoy.Kind == ConvoyKind.Shipment
                ? "Resource shipment delivered. Your wagons are returning."
                : "Trade goods delivered. Your wagons are returning.");
        }
    }
}
