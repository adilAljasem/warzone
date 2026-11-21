using Warzone;

internal static class GameplayChecks
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        void Reject(Action action, string code) { try { action(); } catch (GameRuleException e) when (e.Code == code) { checks++; return; } throw new Exception($"Expected {code}"); }
        VillageEconomy City(double x, double y) => new(Guid.NewGuid(), TribeType.Romans, new(x,y));
        var instant = City(300,300);
        var mainHall = instant.Village.Buildings.Single(b=>b.Type==BuildingType.MainHall);
        var warehouse = instant.Village.Buildings.Single(b=>b.Type==BuildingType.Warehouse);
        Reject(()=>instant.Apply(new(Guid.NewGuid(),CommandKind.Upgrade,warehouse.QueueId),0),"main_hall");
        mainHall.CompleteUpgrade(1); mainHall.CompleteUpgrade(2);
        var cost = instant.Snapshot().Targets.Single(t => t.Id == warehouse.QueueId).UpgradeCost!.Value; var stock = instant.Resources;
        instant.Apply(new(Guid.NewGuid(),CommandKind.Upgrade,warehouse.QueueId),0);
        Check(warehouse.Level==2 && warehouse.MaxLevel==30 && instant.Resources==stock-cost && instant.Snapshot().Jobs.Length==0,"Building upgrades pay once and complete instantly without construction jobs");
        Check(instant.Snapshot().Targets.Count(t=>t.Kind=="Building")==9 && instant.Snapshot().Targets.All(t=>t.Kind is "Building" or "Training"),"Nine buildings include a market and no resource fields");
        Reject(()=>instant.Apply(new(Guid.NewGuid(),CommandKind.SetAutoQueue,warehouse.QueueId),0),"command");
        var training=instant.Training.First();
        instant.Apply(new(Guid.NewGuid(),CommandKind.Train,training.QueueId,Quantity:2),0);
        Check(instant.Available(training.Unit.UnitId)==20 && instant.Snapshot().Jobs.Length==1,"Recruitment still takes time");
        instant.Apply(new(Guid.NewGuid(),CommandKind.Upgrade,warehouse.QueueId),0);
        Check(warehouse.Level==3 && instant.Snapshot().Jobs.Length==1,"Instant upgrades do not block active troop training");

        var a=City(700,700); var b=City(1100,700); var world=new LogisticsWorld([a,b],731); double now=0;
        void Tick(int seconds) { for(var i=0;i<seconds*4;i++) { now+=.25;world.Advance(.25,now,0,3600); } }
        void Apply(VillageEconomy city,GameCommand command) => world.Apply(city.Village.OwnerId,command,now,0);
        var offerCommand=new GameCommand(Guid.NewGuid(),CommandKind.PostMarketOffer,a.Village.Id,OfferResource:ResourceType.Wood,OfferAmount:500,AskResource:ResourceType.Iron,AskAmount:400);
        var beforeA=a.Resources; var beforeB=b.Resources;
        Apply(a,offerCommand); var offer=world.Snapshot(a.Village.OwnerId).MarketOffers.Single();
        Check(a.Resources.Wood==beforeA.Wood-500 && a.Available("romans-wagon")==2-offer.Wagons && a.ReservedTradeWagons==offer.Wagons,"Posting reserves actual goods and actual wagons");
        Check(world.Snapshot(a.Village.OwnerId).Muster.Total==22,"Reserved market wagons remain in population and muster totals");
        Reject(()=>Apply(b,new(Guid.NewGuid(),CommandKind.CancelMarketOffer,offer.Id)),"offer");
        Reject(()=>Apply(a,new(Guid.NewGuid(),CommandKind.AcceptMarketOffer,a.Village.Id,DestinationId:offer.Id)),"offer");
        Apply(a,new(Guid.NewGuid(),CommandKind.CancelMarketOffer,offer.Id));
        Check(a.Resources==beforeA&&a.Available("romans-wagon")==2&&a.ReservedTradeWagons==0,"Cancellation refunds goods and releases wagons exactly once");
        Reject(()=>Apply(a,new(Guid.NewGuid(),CommandKind.CancelMarketOffer,offer.Id)),"offer");
        Reject(()=>Apply(a,offerCommand with { OfferAmount=0 }),"offer");
        Reject(()=>Apply(a,offerCommand with { AskResource=ResourceType.Wood }),"offer");
        Check(a.Resources==beforeA,"Invalid offers have no resource effects");
        Apply(a,offerCommand);offer=world.Snapshot(a.Village.OwnerId).MarketOffers.Single();
        Apply(b,new(Guid.NewGuid(),CommandKind.AcceptMarketOffer,b.Village.Id,DestinationId:offer.Id));
        var caravans=world.Snapshot(a.Village.OwnerId).Convoys;
        Check(caravans.Length==2&&caravans.All(c=>c.Kind==ConvoyKind.Trade&&!c.Returning),"Acceptance launches two visible trade caravans");
        Check(a.Resources.Iron==beforeA.Iron&&b.Resources.Wood==beforeB.Wood,"Accepted trade does not teleport goods");
        Check(world.Snapshot(a.Village.OwnerId).MarketOffers.Length==0&&a.ReservedTradeWagons==0,"Accepted offer leaves the market and its wagons enter transit");
        Reject(()=>Apply(b,new(Guid.NewGuid(),CommandKind.AcceptMarketOffer,b.Village.Id,DestinationId:offer.Id)),"offer");
        Tick(200);
        Check(a.Resources.Iron==beforeA.Iron+400&&b.Resources.Wood==beforeB.Wood+500,"Both exchanged materials arrive after travel");
        Check(world.Snapshot(a.Village.OwnerId).Convoys.Length==0&&a.Available("romans-wagon")==2&&b.Available("romans-wagon")==2,"Only returned trade wagons become available again");
        Apply(a,offerCommand);offer=world.Snapshot(a.Village.OwnerId).MarketOffers.Single();
        a.AddResources(new(100000,0,0,0));Apply(a,new(Guid.NewGuid(),CommandKind.CancelMarketOffer,offer.Id));
        var overflow=a.Resources.Wood;a.Advance(.25,now,1);
        Check(overflow==a.Capacity+500&&a.Resources.Wood==overflow,"Refunds above storage capacity are retained and production pauses");

        var occupier=City(1000,1000);var rival=City(1500,1000);var territory=new LogisticsWorld([occupier,rival],731);double clock=0;
        void TerritoryTick(int seconds) { for(var i=0;i<seconds*4;i++){clock+=.25;territory.AdvanceEconomies(.25,clock,1);territory.Advance(.25,clock,0,3600);} }
        ArmySnapshot Dispatch(VillageEconomy city,Guid? destination,Position point,bool reinforce=false,AfterAttack after=AfterAttack.Hold) {
            var previous=territory.Snapshot(city.Village.OwnerId).Armies.Select(a=>a.Id).ToHashSet();
            territory.Apply(city.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,city.Village.Id,Formation:new Dictionary<string,int>{{"romans-infantry",10}},Wagons:0,DestinationId:destination,X:point.X,Y:point.Y,Reinforcement:reinforce,AfterAttack:after),clock,0);
            return territory.Snapshot(city.Village.OwnerId).Armies.Single(a=>!previous.Contains(a.Id));
        }
        var wood=territory.Snapshot(occupier.Village.OwnerId).Oases.Where(o=>o.Resource==ResourceType.Wood).OrderBy(o=>LogisticsWorld.Distance(o.Position,occupier.Village.Position)).Take(2).ToArray();
        var force=Dispatch(occupier,wood[0].Id,wood[0].Position);var initial=occupier.Resources;
        TerritoryTick(400);
        Check(territory.Snapshot(occupier.Village.OwnerId).Oases.Single(o=>o.Id==wood[0].Id).OwnerId==occupier.Village.OwnerId,"Army captures an unclaimed oasis");
        Check(occupier.OwnedOases==1&&occupier.OasisBonuses.SequenceEqual(new[]{.5,.25,.25,.25}),"Wood oasis adds +50% wood and +25% other production to its dispatch village");
        Check(territory.Snapshot(occupier.Village.OwnerId).Reports.First(r=>r.Kind=="Occupation").AttackerLoot.Total==0,"Occupation grants no instant resource loot");
        territory.Apply(occupier.Village.OwnerId,new(Guid.NewGuid(),CommandKind.CaptureOasis,force.Id,DestinationId:wood[1].Id),clock,0);TerritoryTick(400);
        Check(occupier.OwnedOases==2&&occupier.OasisBonuses.SequenceEqual(new[]{1d,.5,.5,.5}),"Multiple oasis bonuses add, rather than compound");
        var gross=occupier.Production().Single(p=>p.Resource=="Wood").GrossPerMinute;
        Check(gross==240*Biomes.Multiplier(occupier.Biome,ResourceType.Wood)*2,"Actual production uses the accumulated oasis multiplier");
        var attacker=Dispatch(rival,wood[0].Id,wood[0].Position);TerritoryTick(400);
        Check(rival.OwnedOases==1&&occupier.OwnedOases==1&&occupier.OasisBonuses[0]==.5,"Capturing another player's undefended oasis transfers production without duplicating bonuses");
        Reject(()=>territory.Apply(occupier.Village.OwnerId,new(Guid.NewGuid(),CommandKind.CaptureOasis,force.Id,DestinationId:wood[0].Id),clock,clock+50),"protected");
        var protectedView=territory.Snapshot(occupier.Village.OwnerId).Armies.Single(a=>a.Id==force.Id);
        Check(protectedView.Order=="Guarding"&&protectedView.DefendingSiteId==wood[1].Id,"Rejected hostile orders preserve the current defense mission");

        var ally=City(500,700);var defended=City(800,700);var enemy=City(1100,700);var defense=new LogisticsWorld([ally,defended,enemy]);double battleTime=0;
        void DefenseTick(int seconds){for(var i=0;i<seconds*4;i++){battleTime+=.25;defense.Advance(.25,battleTime,0,3600);}}
        defense.Apply(ally.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,ally.Village.Id,Formation:new Dictionary<string,int>{{"romans-infantry",10}},Wagons:0,DestinationId:defended.Village.Id,Reinforcement:true),0,0);
        DefenseTick(90);var guardArmy=defense.Snapshot(ally.Village.OwnerId).Armies.Single();
        Check(guardArmy.Order=="Guarding"&&guardArmy.OwnerId==ally.Village.OwnerId&&defended.Available("romans-infantry")==20,"Reinforcements defend another village without becoming its owner's troops");
        defense.Apply(enemy.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,enemy.Village.Id,Formation:new Dictionary<string,int>{{"romans-infantry",20}},Wagons:0,X:800,Y:700),battleTime,0);
        var attack=defense.Snapshot(enemy.Village.OwnerId).Armies.Single(a=>a.OwnerId==enemy.Village.OwnerId);
        defense.Apply(enemy.Village.OwnerId,new(Guid.NewGuid(),CommandKind.BeginSiege,attack.Id,DestinationId:defended.Village.Id),battleTime,0);
        for(var i=0;i<400&&!defense.Snapshot(ally.Village.OwnerId).Warfare.Battles.Any();i++)DefenseTick(1);
        var contact=defense.Snapshot(ally.Village.OwnerId).Warfare.Battles.First();
        Check(contact.DefenderId==guardArmy.Id&&!defended.Besieged,"Reinforcements intercept attackers before a village siege begins");
        defense.Apply(ally.Village.OwnerId,new(Guid.NewGuid(),CommandKind.RecallArmy,guardArmy.Id),battleTime,0);
        Check(defense.Snapshot(ally.Village.OwnerId).Armies.Single(a=>a.Id==guardArmy.Id).Order=="Returning","The reinforcement owner retains withdrawal control");

        var holder=City(700,700);var supporter=City(900,700);var invader=City(1100,700);
        var guardedOasis=new LogisticsWorld([holder,supporter,invader],73);double oasisTime=0;
        var site=guardedOasis.Snapshot(holder.Village.OwnerId).Oases.MinBy(o=>LogisticsWorld.Distance(o.Position,holder.Village.Position))!;
        void OasisTick(){oasisTime+=.25;guardedOasis.Advance(.25,oasisTime,0,3600);}
        void Until(Func<bool> done){for(var i=0;i<2400&&!done();i++)OasisTick();Check(done(),"Oasis mission completes within ten minutes");}
        Guid Send(VillageEconomy city,int count,bool reinforce=false){
            guardedOasis.Apply(city.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,city.Village.Id,Formation:new Dictionary<string,int>{{"romans-infantry",count},{"romans-wagon",1}},Wagons:0,DestinationId:site.Id,Reinforcement:reinforce),oasisTime,0);
            return guardedOasis.Snapshot(city.Village.OwnerId).Armies.Single(a=>a.OwnerId==city.Village.OwnerId).Id;
        }
        var holderArmy=Send(holder,2);Until(()=>holder.OwnedOases==1);
        var supportArmy=Send(supporter,2,true);
        Until(()=>guardedOasis.Snapshot(supporter.Village.OwnerId).Armies.Single(a=>a.Id==supportArmy).Order=="Guarding");
        Check(guardedOasis.Snapshot(holder.Village.OwnerId).Oases.Single(o=>o.Id==site.Id).Defenders==4,"An allied reinforcement adds real defenders to an owned oasis");
        var invaderArmy=Send(invader,20);
        Until(()=>guardedOasis.Snapshot(invader.Village.OwnerId).Warfare.Battles.Any());
        Check(holder.OwnedOases==1&&invader.OwnedOases==0,"An occupied oasis cannot change hands while its defenders are fighting");
        Until(()=>invader.OwnedOases==1);
        var victorious=guardedOasis.Snapshot(invader.Village.OwnerId);
        Check(holder.OwnedOases==0&&victorious.Armies.Single(a=>a.Id==invaderArmy).Order=="Guarding","An attacker resumes occupation after defeating defenders and then guards its captured oasis");
        Check(victorious.Reports.Any(r=>r.Kind=="Battle"&&r.Status!="Active"&&r.Armies.Count(a=>!a.Attacking)==2),"Both allied defenders fight in one encounter before occupation");
        return checks;
    }
}
