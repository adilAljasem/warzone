using System.Text.Json;
using System.Text.Json.Serialization;
using Warzone;

internal static class CityObjectiveChecks
{
    private sealed class Realm
    {
        public VillageEconomy A=new(Guid.NewGuid(),TribeType.Romans,new(500,500));
        public VillageEconomy B=new(Guid.NewGuid(),TribeType.Greeks,new(650,500));
        public VillageEconomy C=new(Guid.NewGuid(),TribeType.Romans,new(800,500));
        public LogisticsWorld World;
        public double Now;
        public Realm(){World=new([A,B,C]); foreach(var city in new[]{A,B,C}){var store=city.Village.Buildings.Single(b=>b.Type==BuildingType.Warehouse);while(store.Level<10)store.CompleteUpgrade(store.Level);city.AddResources(new(15000,15000,15000,15000));}}
        public WorldSnapshot View=>World.Snapshot(A.Village.OwnerId);
        public void Tick(int seconds){for(var i=0;i<seconds*4;i++){Now+=.25;World.Advance(.25,Now,0,1800);}}
        public void Until(Func<bool> ready,int seconds=600){for(var i=0;i<seconds&&!ready();i++)Tick(1);if(!ready())throw new Exception("City objective did not reach its expected phase");}
        public Guid Send(AttackObjective goal, bool governor=false,bool catapult=false, VillageEconomy? from=null,bool reinforce=false,int troops=100)
        {
            var city=from??A;var tribe=city.Village.Tribe.ToString().ToLowerInvariant();
            var formation=new Dictionary<string,int>{{tribe+"-infantry",troops},{tribe+"-wagon",2}};
            if(governor)formation[tribe+"-governor"]=1;if(catapult)formation[tribe+"-catapult"]=2;
            foreach(var (unit,count) in formation)city.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units[unit]),count)]);
            city.AddResources(new(5000,5000,5000,15000));
            World.Apply(city.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,city.Village.Id,Formation:formation,Wagons:0,DestinationId:B.Village.Id,Objective:goal,Reinforcement:reinforce,AfterAttack:AfterAttack.Return),Now,0);
            return World.Snapshot(city.Village.OwnerId).Armies.Last(a=>a.OwnerId==city.Village.OwnerId).Id;
        }
        public void Recall(Guid id)=>World.Apply(A.Village.OwnerId,new(Guid.NewGuid(),CommandKind.RecallArmy,id),Now,0);
        public void Fixture(string name,Guid? viewer=null)
        {
            var folder=Environment.GetEnvironmentVariable("WARZONE_TEST_FIXTURES");if(folder is null)return;
            Directory.CreateDirectory(folder);var you=viewer??A.Village.OwnerId;
            var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);options.Converters.Add(new JsonStringEnumConverter());
            File.WriteAllText(Path.Combine(folder,name+".json"),JsonSerializer.Serialize(new{id="objectives",name="Objectives",hostId=A.Village.OwnerId,you,revision=1,status="Running",elapsedSeconds=Now,remainingSeconds=1800-Now,shieldRemainingSeconds=0,settings=new{capacity=8,durationMinutes=30,shieldMinutes=0,resourceSpeed=1,botCount=0,botDifficulty="Easy"},players=new[]{A,B,C}.Select(c=>new{id=c.Village.OwnerId,name=c==A?"المهاجم":c==B?"المدافع":"الحليف",tribe=c.Village.Tribe,villageId=c.Village.Id,position=c.Village.Position,level=c.Village.Level,isBot=false,ready=true,online=true}),economy=World.EconomySnapshot(you,null,1),world=World.Snapshot(you),result=(object?)null},options));
        }
    }
    public static int Run()
    {
        var checks=0;
        void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
        void Reject(Action action,string code){try{action();}catch(GameRuleException e)when(e.Code==code){checks++;return;}throw new Exception("Expected "+code);}
        var validation=new Realm();
        Reject(()=>validation.Send(AttackObjective.Conquest),"governor");
        Reject(()=>validation.Send(AttackObjective.Raze,governor:true),"catapult");
        Check(validation.View.Armies.Length==0,"Missing specialists cannot create orphan expeditions");
        var stock=validation.A.Resources;var garrison=validation.A.Garrison().Sum(t=>t.Count);
        Reject(()=>validation.World.Apply(validation.A.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,validation.A.Village.Id,Formation:new Dictionary<string,int>{{"romans-infantry",10}},Wagons:0,DestinationId:validation.B.Village.Id,Objective:AttackObjective.Conquest),0,0),"governor");
        Check(validation.A.Resources==stock&&validation.A.Garrison().Sum(t=>t.Count)==garrison,"Invalid city objectives atomically preserve resources and troops");
        var raid=new Realm();var raider=raid.Send(AttackObjective.Raid);
        raid.Until(()=>raid.View.Reports.Any(r=>r.Status=="Raided"));
        var raidReport=raid.View.Reports.Single(r=>r.Kind=="Siege");
        Check(raidReport.AttackerLoot.Total>0&&raidReport.AttackerLoot.Total<=raidReport.DefenderLoss.Total,"Raid transfers only actual plunder into army cargo");
        Check(raid.B.Village.OwnerId!=raid.A.Village.OwnerId&&!raid.B.Ruined&&!raid.B.Besieged,"Raid preserves the enemy settlement");
        Check(raid.View.Armies.Single(a=>a.Id==raider).Order=="Returning","Raid follows the chosen return order immediately");

        var raze=new Realm();var razer=raze.Send(AttackObjective.Raze,catapult:true);
        Check(!raze.View.Armies.Single().Formation!.ContainsKey("romans-governor"),"Razing accepts catapults without any governor");
        raze.Until(()=>raze.View.Warfare.Sieges.Any(s=>s.Phase=="Demolishing"));
        var started=raze.Now;var levels=raze.B.Village.Buildings.Sum(b=>b.Level);
        raze.Tick(60);
        Check(!raze.B.Ruined&&raze.B.Village.Buildings.Sum(b=>b.Level)<levels,"Demolition removes actual building levels gradually, never instantly");
        Check(raze.View.Reports.Single(r=>r.Kind=="Siege").Status=="Active","Demolition remains a live battle in reports");
        raze.Fixture("demolition");
        raze.Until(()=>raze.B.Ruined);
        Check(raze.Now-started>=300&&raze.Now-started<=301,"Destruction takes approximately five minutes after defense falls");
        Check(raze.B.Village.Buildings.All(b=>b.Level==0)&&raze.B.Resources.Total==0,"All buildings and production are gone at demolition completion");
        var completed=raze.View.Reports.Single(r=>r.Kind=="Siege");
        Check(completed.Status=="Raze"&&completed.CityAttack is {Progress:100,RemainingBuildingLevels:0},"Completed destruction archives objective and final progress");
        raze.Tick(40);Check(!raze.View.Warfare.Sieges.Any()&&raze.View.Reports.Any(r=>r.Id==completed.Id),"Finished battle markers expire while the final report stays");
        raze.Fixture("destroyed");

        var conquest=new Realm();var governor=conquest.Send(AttackObjective.Conquest,governor:true);
        conquest.Until(()=>conquest.View.Warfare.Sieges.Any(s=>s.Phase=="Persuading"));
        var former=conquest.B.Village.OwnerId;conquest.Tick(10);
        Check(conquest.B.Village.OwnerId==former&&conquest.View.Warfare.Sieges.Single().Loyalty==90,"Existing Main Hall governor persuades over distinct rounds");
        conquest.Fixture("persuasion");
        conquest.Until(()=>conquest.B.Village.OwnerId==conquest.A.Village.OwnerId);
        Check(!conquest.B.Ruined&&!conquest.B.Besieged&&conquest.View.Armies.Single(a=>a.Id==governor).Formation!.GetValueOrDefault("romans-governor")==0,"Conquest consumes one governor once and preserves the buildings");
        Check(conquest.View.Reports.Single(r=>r.Kind=="Siege").CityAttack is {Progress:100,Loyalty:0},"Conquest progress remains in archived reports");

        var withdrawn=new Realm();var withdrawer=withdrawn.Send(AttackObjective.Raze,catapult:true);
        withdrawn.Until(()=>withdrawn.View.Warfare.Sieges.Any(s=>s.Phase=="Demolishing"));withdrawn.Tick(60);withdrawn.Recall(withdrawer);
        var surviving=withdrawn.B.Village.Buildings.Sum(b=>b.Level);withdrawn.Tick(120);
        Check(!withdrawn.B.Ruined&&!withdrawn.B.Besieged&&withdrawn.B.Village.Buildings.Sum(b=>b.Level)==surviving,"Recall stops demolition without silently restoring or destroying remaining buildings");

        var blocked=new Realm();var besieger=blocked.Send(AttackObjective.Blockade,troops:30);blocked.Until(()=>blocked.B.Besieged);
        var pre=blocked.B.Garrison().Sum(t=>t.Count);var gross=blocked.B.Production().Single(p=>p.Resource=="Wheat").GrossPerMinute;
        Check(gross==120&&blocked.B.Production().Single(p=>p.Resource=="Wood").GrossPerMinute==240,"Only wheat production is halved by village blockade");
        blocked.Tick(200);
        Check(blocked.B.Besieged&&blocked.B.Garrison().Sum(t=>t.Count)==pre&&blocked.View.Warfare.Sieges.Single().Wall==100,"Pure blockade keeps surrounding the city without automatic assault or the old attack timeout");
        Reject(()=>blocked.World.Apply(blocked.A.Village.OwnerId,new(Guid.NewGuid(),CommandKind.BeginSiege,besieger,DestinationId:blocked.B.Village.Id,Objective:AttackObjective.Raid),blocked.Now,0),"engaged");
        var aid=blocked.Send(AttackObjective.Raid,from:blocked.C,reinforce:true,troops:100);
        blocked.Until(()=>blocked.World.Snapshot(blocked.C.Village.OwnerId).Armies.Any(a=>a.Id==aid&&a.Order=="Battle"));
        var relief=blocked.World.Snapshot(blocked.C.Village.OwnerId).Armies.Single(a=>a.Id==aid);
        Check(LogisticsWorld.Distance(relief.Position,blocked.B.Village.Position)>4&&blocked.B.Besieged,"Reinforcements must fight the blockade outside the city instead of entering its defense");
        Check(blocked.View.Reports.Single(r=>r.Kind=="Siege").Armies.Any(a=>a.ArmyId==aid&&!a.Attacking),"City battle report lists relief armies on the defending side");
        blocked.Fixture("blockade",blocked.B.Village.OwnerId);
        blocked.Recall(besieger);blocked.Tick(1);
        Check(!blocked.B.Besieged&&blocked.B.Production().Single(p=>p.Resource=="Wheat").GrossPerMinute==240,"Lifting the last blockade restores normal wheat output");

        var trade=new Realm();
        trade.World.Apply(trade.B.Village.OwnerId,new(Guid.NewGuid(),CommandKind.PostMarketOffer,trade.B.Village.Id,OfferResource:ResourceType.Wood,OfferAmount:200,AskResource:ResourceType.Iron,AskAmount:120),0,0);
        var offer=trade.View.MarketOffers.Single();
        trade.World.Apply(trade.C.Village.OwnerId,new(Guid.NewGuid(),CommandKind.AcceptMarketOffer,trade.C.Village.Id,DestinationId:offer.Id),0,0);
        var iron=trade.B.Resources.Iron;var merchantBlocker=trade.Send(AttackObjective.Blockade,troops:30);
        trade.Until(()=>trade.B.Besieged);trade.Tick(45);
        var cargo=trade.World.Snapshot(trade.B.Village.OwnerId).Convoys.Single(c=>c.RecipientId==trade.B.Village.OwnerId);
        Check(trade.B.Resources.Iron==iron&&cargo.Cargo!.Value.Iron==120,"In-transit market goods remain on wagons while the recipient is blockaded");
        Check(LogisticsWorld.Distance(cargo.Position,trade.B.Village.Position)>=64,"Market wagons wait visibly outside the surrounded village");
        trade.Recall(merchantBlocker);trade.Until(()=>trade.B.Resources.Iron==iron+120);
        Check(trade.B.Resources.Iron==iron+120,"Lifting a blockade delivers the original trade cargo exactly once");

        var collection=new Realm();var oasis=collection.View.Oases.OrderBy(o=>LogisticsWorld.Distance(o.Position,collection.B.Village.Position)).First();
        collection.World.Apply(collection.B.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,collection.B.Village.Id,Formation:new Dictionary<string,int>{{"greeks-infantry",5}},Wagons:0,DestinationId:oasis.Id,AfterAttack:AfterAttack.Return),0,0);
        collection.Until(()=>collection.View.Oases.Single(o=>o.Id==oasis.Id).OwnerId==collection.B.Village.OwnerId);
        collection.World.Apply(collection.B.Village.OwnerId,new(Guid.NewGuid(),CommandKind.SetOasisTransport,oasis.Id,Enabled:false),collection.Now,0);
        collection.Until(()=>!collection.View.Armies.Any());
        collection.B.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["greeks-wagon"]),20)]);
        collection.World.AdvanceEconomies(100,collection.Now,1);
        collection.World.Apply(collection.B.Village.OwnerId,new(Guid.NewGuid(),CommandKind.CollectOasis,oasis.Id),collection.Now,0);
        var wood=collection.B.Resources.Wood;var collectorBlocker=collection.Send(AttackObjective.Blockade,troops:30);
        collection.Until(()=>collection.B.Besieged);
        collection.Until(()=>collection.World.Snapshot(collection.B.Village.OwnerId).Convoys.Any(c=>c.Kind==ConvoyKind.Oasis&&c.Returning&&LogisticsWorld.Distance(c.Position,c.Destination)<1));
        var harvest=collection.World.Snapshot(collection.B.Village.OwnerId).Convoys.Single(c=>c.Kind==ConvoyKind.Oasis);
        Check(collection.B.Resources.Wood==wood&&harvest.Cargo!.Value.Total>0,"Collected oasis goods cannot enter a blockaded village");
        Check(LogisticsWorld.Distance(harvest.Position,collection.B.Village.Position)>=64,"Oasis wagons wait outside the blockade with their full cargo");
        var harvestedWood=harvest.Cargo!.Value.Wood;collection.Recall(collectorBlocker);
        collection.Until(()=>!collection.View.Convoys.Any(c=>c.Kind==ConvoyKind.Oasis));
        Check(collection.B.Resources.Wood==wood+harvestedWood,"Oasis cargo is delivered after relief without duplication or loss");
        return checks;
    }
}
