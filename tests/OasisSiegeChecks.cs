using Warzone;

internal static class OasisSiegeChecks
{
    public static int Run()
    {
        var checks=0;
        void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
        void Reject(Action action,string code){try{action();}catch(GameRuleException e)when(e.Code==code){checks++;return;}throw new Exception($"Expected {code}");}
        var home=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(700,700));
        var enemy=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(1100,700));
        var ally=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(1200,1100));
        var world=new LogisticsWorld([home,enemy,ally],73);double now=0;
        WorldSnapshot View(Guid? viewer=null)=>world.Snapshot(viewer??home.Village.OwnerId);
        var target=View().Oases.Where(o=>o.Resource==ResourceType.Wood).MinBy(o=>LogisticsWorld.Distance(o.Position,home.Village.Position))!;
        OasisSnapshot Site()=>View().Oases.Single(o=>o.Id==target.Id);
        void Tick(){now+=.25;world.Advance(.25,now,0,3600);}
        void Until(Func<bool> done){for(var i=0;i<4000&&!done();i++)Tick();Check(done(),"Oasis siege transition completes within its travel budget");}
        void Seconds(int seconds){for(var i=0;i<seconds*4;i++)Tick();}
        Guid Send(VillageEconomy city,int infantry,OasisMode mode=OasisMode.Capture,bool reinforce=false){
            world.Apply(city.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,city.Village.Id,Formation:new Dictionary<string,int>{{"romans-infantry",infantry},{"romans-wagon",1}},Wagons:0,DestinationId:target.Id,Reinforcement:reinforce,OasisMode:mode),now,0);
            return View(city.Village.OwnerId).Armies.Last(a=>a.OwnerId==city.Village.OwnerId).Id;
        }
        void Order(VillageEconomy city,CommandKind kind,Guid id,Guid? destination=null,bool enabled=true)=>world.Apply(city.Village.OwnerId,new(Guid.NewGuid(),kind,id,DestinationId:destination,Enabled:enabled),now,0);
        var guard=Send(home,5);Until(()=>home.OwnedOases==1);
        Order(home,CommandKind.SetOasisTransport,target.Id,enabled:false);
        world.AdvanceEconomies(60,now,1);
        Check(Site().Stock==new Resources(240,120,120,120)&&Site().ProductionPerMinute==new Resources(240,120,120,120),"A wood oasis produces all four materials, with double output for wood");
        var armyBefore=View().Armies.Single(a=>a.Id==guard);var villageWheat=home.Resources.Wheat;var wheat=Site().Stock!.Value.Wheat;
        Seconds(60);
        var armyAfter=View().Armies.Single(a=>a.Id==guard);
        Check(armyAfter.Wheat==armyBefore.Wheat&&Site().Stock!.Value.Wheat<wheat,"Own oasis defenders eat local wheat before their carried provisions");
        Check(home.Resources.Wheat==villageWheat&&!View().Convoys.Any(c=>c.TargetArmyId==guard),"Oasis-fed guards request no village supply convoy or village wheat");
        Check(armyAfter.SupplyStatus=="Supplied by oasis wheat"&&Site().WheatUpkeepPerMinute>0,"Food source and local upkeep are exposed to the owner");
        Order(home,CommandKind.UpgradeOasisWall,target.Id);
        var blockader=Send(enemy,10,OasisMode.Blockade);
        Check(!Site().Blockaded&&home.OasisBonuses[0]==.5,"A marching army does not prematurely disable production bonuses");
        Reject(()=>world.Apply(ally.Village.OwnerId,new(Guid.NewGuid(),CommandKind.BesiegeOasis,blockader,DestinationId:target.Id),now,0),"not_owned");
        Until(()=>LogisticsWorld.Distance(View(enemy.Village.OwnerId).Armies.Single(a=>a.Id==blockader).Position,target.Position)<75);
        world.AdvanceEconomies(120,now,1);var available=Site().Stock!.Value.Wood;
        Order(home,CommandKind.CollectOasis,target.Id);
        var collection=View().Convoys.Single(c=>c.Kind==ConvoyKind.Oasis);
        Until(()=>Site().Blockaded);
        Check(home.OwnedOases==1&&home.OasisBonuses.All(b=>b==0),"A blockade suspends every oasis bonus without changing ownership");
        Check(Site().Stock!.Value.Wood==available,"A blockade does not delete stored oasis resources");
        Check(View().Convoys.Single(c=>c.Id==collection.Id).Returning&&View().Convoys.Single(c=>c.Id==collection.Id).Cargo!.Value.Total==0,"An in-flight collection caravan turns home without loading at a blockaded oasis");
        Reject(()=>Order(home,CommandKind.CollectOasis,target.Id),"siege");
        var siege=View().OasisSieges.Single();var report=View().Reports.Single(r=>r.Id==siege.Id);
        Check(report.Kind=="OasisSiege"&&report.Status=="Active"&&report.OasisDefense is {Blockaded:true,Level:1},"A blockade immediately creates a live report linked to its map marker");
        Check(View(ally.Village.OwnerId).Reports.All(r=>r.Id!=siege.Id)&&View(ally.Village.OwnerId).OasisSieges.Any(s=>s.Id==siege.Id),"Battle markers are public while detailed troop reports stay private");
        var events=report.Events.Length;Seconds(10);
        Check(Site().WallHealth==150&&View().Reports.Single(r=>r.Id==siege.Id).Events.Length>events,"A blockade holds position and updates its live report without silently capturing or damaging the wall");
        Order(enemy,CommandKind.RecallArmy,blockader);Tick();
        Check(!Site().Blockaded&&home.OasisBonuses.SequenceEqual(new[]{.5,.25,.25,.25}),"Withdrawing the besieger immediately restores all production bonuses");
        Check(View().Reports.Single(r=>r.Id==siege.Id).Status=="Withdrawn"&&View().OasisSieges.Length==0,"Ended siege reports remain archived while their live map marker is removed");
        Order(enemy,CommandKind.CaptureOasis,blockader,target.Id);
        Until(()=>Site().WallHealth<150);
        siege=View().OasisSieges.Single();Seconds(5);
        report=View().Reports.Single(r=>r.Id==siege.Id);
        Check(report.OasisDefense!.Health<150&&report.OasisDefense.Health>0&&report.Status=="Active","Wall damage is visible in the ongoing oasis battle report before a field battle begins");
        Check(report.AttackerUnits.Sum(u=>u.Sent)==8&&report.DefenderUnits.Sum(u=>u.Sent)==6,"The live wall report retains the twenty-percent withdrawal loss and actual defending formation");
        Order(enemy,CommandKind.BesiegeOasis,blockader,target.Id);Tick();
        var wall=Site().WallHealth;Seconds(6);
        Check(Site().WallHealth==wall&&View().Reports.Single(r=>r.Id==siege.Id).OasisDefense!.Mode==OasisMode.Blockade,"Changing to blockade updates the same live report and stops wall damage");
        ally.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]),80)]);
        var relief=Send(ally,80,reinforce:true);
        Until(()=>View().Warfare.Battles.Any(b=>b.Status=="Active"&&(b.AttackerId==relief||b.DefenderId==relief)));
        Check(View().OasisSieges.Any(s=>s.Id==siege.Id),"The siege report remains live while a relief army fights its besieger");
        Until(()=>!Site().Blockaded);
        Check(Site().OwnerId==home.Village.OwnerId&&home.OasisBonuses[0]==.5,"A successful relief restores the original owner's oasis and production bonus");
        report=View().Reports.Single(r=>r.Id==siege.Id);
        Check(report.Status!="Active"&&report.AttackerLosses.Sum(l=>l.Dead+l.Wounded+l.Fled+l.Destroyed)>0,"The archived oasis report includes casualties from the relief battle");
        Check(Site().ProductionPerMinute.Wood==240&&Site().Stock!.Value.Iron>0&&Site().Stock!.Value.Stone>0,"Every resource remains available after siege resolution");
        return checks;
    }
}
