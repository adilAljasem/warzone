using Warzone;

internal static class OasisEconomyChecks
{
    public static int Run()
    {
        var checks=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        void Reject(Action action,string code){try{action();}catch(GameRuleException e)when(e.Code==code){checks++;return;}throw new Exception($"Expected {code}");}
        var home=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(700,700));
        var enemy=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(1100,700));
        var world=new LogisticsWorld([home,enemy],73);double now=0;
        WorldSnapshot View()=>world.Snapshot(home.Village.OwnerId);
        var site=View().Oases.Where(o=>o.Resource==ResourceType.Wood).MinBy(o=>LogisticsWorld.Distance(home.Village.Position,o.Position))!;
        OasisSnapshot Oasis()=>View().Oases.Single(o=>o.Id==site.Id);
        void Tick(){now+=.25;world.Advance(.25,now,0,3600);}
        void Until(Func<bool> done){for(var i=0;i<4000&&!done();i++)Tick();Check(done(),"Oasis task completes within its travel/combat budget");}
        void Order(VillageEconomy city,CommandKind kind,Guid target,Guid? destination=null,bool enabled=true)=>world.Apply(city.Village.OwnerId,new(Guid.NewGuid(),kind,target,DestinationId:destination,Enabled:enabled),now,0);
        Guid Dispatch(VillageEconomy city,Dictionary<string,int> troops,Guid? target=null,bool reinforce=false){
            world.Apply(city.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,city.Village.Id,Formation:troops,Wagons:0,DestinationId:target,Reinforcement:reinforce,Enabled:false,X:site.Position.X,Y:site.Position.Y),now,0);
            return world.Snapshot(city.Village.OwnerId).Armies.Last(a=>a.OwnerId==city.Village.OwnerId).Id;
        }
        var guard=Dispatch(home,new(){{"romans-infantry",5}},site.Id);
        Until(()=>Oasis().OwnerId==home.Village.OwnerId);
        Check(Oasis().AutoTransport==true&&Oasis().Stock!.Value.Total==0,"Newly captured oases default to automatic transport without a free resource payout");
        var position=View().Armies.Single(a=>a.Id==guard).Position;
        for(var i=0;i<80;i++)Tick();
        Check(View().Armies.Single(a=>a.Id==guard).Position==position,"An arrived oasis guard remains stationary across repeated snapshots");
        Reject(()=>Order(enemy,CommandKind.SetOasisTransport,site.Id),"not_owned");
        Reject(()=>Order(enemy,CommandKind.UpgradeOasisWall,site.Id),"not_owned");
        var stock=home.Resources;var wallCost=Oasis().WallUpgradeCost!.Value;
        Order(home,CommandKind.UpgradeOasisWall,site.Id);
        Check(Oasis().WallLevel==1&&Oasis().WallHealth==150&&home.Resources==stock-wallCost,"Oasis wall pays its owning village and completes immediately");
        Order(home,CommandKind.RecallArmy,guard);Until(()=>!View().Armies.Any(a=>a.Id==guard));
        world.AdvanceEconomies(199.75,now,1);Tick();
        Check(Oasis().Stock!.Value.Total<2000&&!View().Convoys.Any(c=>c.Kind==ConvoyKind.Oasis),"Automatic collection waits below 2000 resources");
        world.AdvanceEconomies(.25,now,1);for(var i=0;i<24;i++)Tick();
        var collecting=View().Convoys.Single(c=>c.Kind==ConvoyKind.Oasis);
        Check(!collecting.Returning&&collecting.Cargo!.Value.Total==0&&home.Available("romans-wagon")==1,"Collection sends an actual empty wagon and reserves it from the village");
        Check(Oasis().Stock!.Value.Total==2000,"Sending wagons does not teleport stored oasis resources");
        var privateSite=world.Snapshot(enemy.Village.OwnerId).Oases.Single(o=>o.Id==site.Id);
        Check(privateSite.Stock is null&&privateSite.AutoTransport is null&&privateSite.WallLevel==1,"Oasis stocks and controls are private; walls remain public");
        var villageWood=home.Resources.Wood;
        Until(()=>View().Convoys.Any(c=>c.Id==collecting.Id&&c.Returning));
        var loaded=View().Convoys.Single(c=>c.Id==collecting.Id);
        Check(Oasis().Stock!.Value.Wood==0&&loaded.Cargo!.Value==new Resources(800,400,400,400)&&home.Resources.Wood==villageWood,"Pickup moves resources into the returning caravan only");
        Until(()=>!View().Convoys.Any(c=>c.Id==collecting.Id));
        Check(home.Resources.Wood==villageWood+800&&home.Available("romans-wagon")==2,"Resources and wagons reach the village exactly once after return");
        Order(home,CommandKind.SetOasisTransport,site.Id,enabled:false);
        world.AdvanceEconomies(500,now,1);Tick();
        Check(Oasis().Stock!.Value.Total==5000&&!View().Convoys.Any(c=>c.Kind==ConvoyKind.Oasis),"Disabling automatic transport preserves accumulated resources");
        var held=home.Depart(new Dictionary<string,int>{{"romans-wagon",1}},0);
        Reject(()=>Order(home,CommandKind.CollectOasis,site.Id),"wagons");
        Check(home.Available("romans-wagon")==1&&Oasis().Stock!.Value.Total==5000,"Collection requires enough actual wagons for the whole current stock without partial debits");
        home.Arrive(held,0);Order(home,CommandKind.CollectOasis,site.Id);
        collecting=View().Convoys.Single(c=>c.Kind==ConvoyKind.Oasis);
        Check(collecting.Wagons==2&&home.Available("romans-wagon")==0,"A full wagon load includes room for travel provisions and adds a second wagon when needed");
        Reject(()=>Order(home,CommandKind.CollectOasis,site.Id),"convoy");
        Until(()=>View().Convoys.Any(c=>c.Id==collecting.Id&&c.Returning));
        var raider=Dispatch(enemy,new(){{"romans-infantry",20},{"romans-wagon",1}});
        Until(()=>world.Snapshot(enemy.Village.OwnerId).Armies.Single(a=>a.Id==raider).Order=="Idle");
        // Send another caravan and stage the attacker at its pickup point for a real return-leg interception.
        Until(()=>!View().Convoys.Any(c=>c.Id==collecting.Id));
        world.AdvanceEconomies(250,now,1);Order(home,CommandKind.CollectOasis,site.Id);
        collecting=View().Convoys.Single(c=>c.Kind==ConvoyKind.Oasis);
        Until(()=>View().Convoys.Any(c=>c.Id==collecting.Id&&c.Returning));
        Order(enemy,CommandKind.InterceptConvoy,raider,collecting.Id);
        Tick();
        Check(View().Convoys.Single(c=>c.Id==collecting.Id).InBattle,"A loaded returning oasis caravan can be intercepted");
        Check(View().Alerts.Any(a=>a.Kind=="convoy-attack"&&a.TargetId==collecting.Id),"A caravan attack creates a targeted important alert");
        Until(()=>!View().Convoys.Any(c=>c.Id==collecting.Id));
        Check(View().Alerts.Any(a=>a.Kind=="convoy-loss"),"A lost caravan generates a persistent critical alert");
        Check(world.Snapshot(enemy.Village.OwnerId).Alerts.All(a=>a.Kind!="convoy-loss"),"Important alerts remain private to their recipient");

        // Compare actual wall damage using identical sites, defenders and elapsed time.
        double Breach(bool engines){
            var a=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(700,700));
            var b=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(1100,700));
            var w=new LogisticsWorld([a,b],73);double t=0;
            var o=w.Snapshot(a.Village.OwnerId).Oases.MinBy(o=>LogisticsWorld.Distance(o.Position,a.Village.Position))!;
            void Step(){t+=.25;w.Advance(.25,t,0,3600);}
            w.Apply(a.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,a.Village.Id,Formation:new Dictionary<string,int>{{"romans-infantry",1}},Wagons:0,DestinationId:o.Id,Enabled:false,AfterAttack:AfterAttack.Return),t,0);
            for(var i=0;i<2000&&a.OwnedOases==0;i++)Step();
            w.Apply(a.Village.OwnerId,new(Guid.NewGuid(),CommandKind.UpgradeOasisWall,o.Id),t,0);
            var units=new Dictionary<string,int>{{"romans-infantry",10}};
            if(engines){b.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["romans-ram"]),2),new(BaseUnit.Create(GameCatalog.Units["romans-catapult"]),1)]);units["romans-ram"]=2;units["romans-catapult"]=1;units["romans-wagon"]=1;}
            w.Apply(b.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,b.Village.Id,Formation:units,Wagons:0,DestinationId:o.Id,Enabled:false),t,0);
            Check(w.Snapshot(a.Village.OwnerId).Alerts.Any(x=>x.Kind=="oasis-attack"&&x.TargetId==o.Id),"Incoming occupation creates an oasis attack alert before arrival");
            Reject(()=>w.Apply(a.Village.OwnerId,new(Guid.NewGuid(),CommandKind.UpgradeOasisWall,o.Id),t,0),"siege");
            for(var i=0;i<3000&&w.Snapshot(a.Village.OwnerId).Oases.Single(x=>x.Id==o.Id).WallHealth==150;i++)Step();
            var start=t;
            for(var i=0;i<2000&&w.Snapshot(a.Village.OwnerId).Oases.Single(x=>x.Id==o.Id).WallHealth>0;i++)Step();
            var duration=t-start;
            Check(a.OwnedOases==1&&b.OwnedOases==0,"Wall breach precedes ownership transfer");
            for(var i=0;i<200&&b.OwnedOases==0;i++)Step();
            var captured=w.Snapshot(b.Village.OwnerId).Oases.Single(x=>x.Id==o.Id);
            Check(captured.OwnerId==b.Village.OwnerId&&captured.WallLevel==0&&captured.WallHealth==0,"Hostile occupation destroys the wall instead of inheriting its level");
            Check(w.Snapshot(a.Village.OwnerId).Alerts.Any(x=>x.Kind=="oasis-loss"),"Oasis loss remains visible as an important alert");
            return duration;
        }
        var infantry=Breach(false);var siege=Breach(true);
        Check(infantry>100&&siege<infantry/3,"Siege engines breach oasis walls substantially faster than infantry alone");
        var seller=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(500,500));
        var buyer=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(900,500));
        var trade=new LogisticsWorld([seller,buyer]);double tradeTime=0;
        trade.Apply(seller.Village.OwnerId,new(Guid.NewGuid(),CommandKind.PostMarketOffer,seller.Village.Id,OfferAmount:500,AskAmount:400),0,0);
        var offer=trade.Snapshot(buyer.Village.OwnerId).MarketOffers.Single();
        trade.Apply(buyer.Village.OwnerId,new(Guid.NewGuid(),CommandKind.AcceptMarketOffer,buyer.Village.Id,DestinationId:offer.Id),0,0);
        while(!trade.Snapshot(seller.Village.OwnerId).Convoys.Any(c=>c.OwnerId==buyer.Village.OwnerId&&c.Returning)&&tradeTime<200){tradeTime+=.25;trade.Advance(.25,tradeTime,0);}
        var returning=trade.Snapshot(seller.Village.OwnerId).Convoys.Single(c=>c.OwnerId==buyer.Village.OwnerId&&c.Returning);
        trade.Apply(seller.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,seller.Village.Id,Formation:new Dictionary<string,int>{{"romans-infantry",10}},Wagons:0,X:500,Y:500),tradeTime,0);
        var interceptor=trade.Snapshot(seller.Village.OwnerId).Armies.Single();
        Reject(()=>trade.Apply(seller.Village.OwnerId,new(Guid.NewGuid(),CommandKind.InterceptConvoy,interceptor.Id,DestinationId:returning.Id),tradeTime,tradeTime+10),"protected");
        trade.Apply(seller.Village.OwnerId,new(Guid.NewGuid(),CommandKind.InterceptConvoy,interceptor.Id,DestinationId:returning.Id),tradeTime,0);
        tradeTime+=.25;trade.Advance(.25,tradeTime,0);
        Check(trade.Snapshot(seller.Village.OwnerId).Convoys.Single(c=>c.Id==returning.Id).InBattle,"Empty trade wagons are attackable on the return leg after protection expires");
        return checks;
    }
}
