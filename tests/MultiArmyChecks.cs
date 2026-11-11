using Warzone;

internal static class MultiArmyChecks
{
    public static int Run()
    {
        var checks=0;
        void Check(bool valid,string message){if(!valid)throw new Exception(message);checks++;}
        VillageEconomy City(double x)=>new(Guid.NewGuid(),TribeType.Romans,new(x,500));
        void Gift(VillageEconomy city,string id,int count)=>city.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units[id]),count)]);
        Guid Send(LogisticsWorld world,VillageEconomy city,string role,int count,double now=0,Guid? target=null,bool reinforce=false,OasisMode mode=OasisMode.Capture)
        {
            Gift(city,"romans-"+role,count);city.AddResources(new(2000,2000,2000,2000));
            world.Apply(city.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,city.Village.Id,Formation:new Dictionary<string,int>{{"romans-"+role,count}},Wagons:0,X:550,Y:500,Enabled:false,DestinationId:target,Reinforcement:reinforce,OasisMode:mode),now,0);
            return world.Snapshot(city.Village.OwnerId).Armies.Last(a=>a.OwnerId==city.Village.OwnerId).Id;
        }
        Check(GameCatalog.Units.Count==70,"Five peoples have fourteen distinct recruitable units each");
        foreach(var tribe in Enum.GetValues<TribeType>())
        {
            var city=new VillageEconomy(Guid.NewGuid(),tribe,new(500,500));
            Check(city.Training.Count==14,"Every expanded roster is exposed through actual training queues");
        }
        Check(GameCatalog.Units["mongols-lightcavalry"].Stats.Speed>GameCatalog.Units["greeks-lightcavalry"].Stats.Speed&&GameCatalog.Units["greeks-lightcavalry"].Stats.Speed>GameCatalog.Units["romans-lightcavalry"].Stats.Speed,"Nomad and Greek light cavalry have distinct speed advantages");
        Check(GameCatalog.Units["romans-cavalry"].Stats.Speed==28&&GameCatalog.Units["persians-cavalry"].Stats.Speed==20,"Cavalry and elephants move faster, with heavy mounts remaining slower");
        var a=City(500);var b=City(600);var world=new LogisticsWorld([a,b]);double now=0;
        WorldSnapshot View()=>world.Snapshot(a.Village.OwnerId);
        void Tick(){now+=.25;world.AdvanceEconomies(.25,now,1);world.Advance(.25,now,0,3600);}
        void Until(Func<bool> condition){for(var i=0;i<2400&&!condition();i++)Tick();Check(condition(),"Grouped combat completes its expected transition");}
        void Order(VillageEconomy city,CommandKind kind,Guid id,Guid? target=null)=>world.Apply(city.Village.OwnerId,new(Guid.NewGuid(),kind,id,DestinationId:target),now,0);
        var a1=Send(world,a,"infantry",40);var a2=Send(world,a,"infantry",40);var b1=Send(world,b,"infantry",40);var b2=Send(world,b,"infantry",40);
        Until(()=>View().Armies.All(a=>a.Order=="Idle"));Order(a,CommandKind.AttackArmy,a1,b1);Order(a,CommandKind.AttackArmy,a2,b1);Tick();
        var battle=View().Warfare.Battles.Single(x=>x.Status=="Active");var report=View().Reports.Single(r=>r.Id==battle.Id);
        Check(battle.AttackerArmyIds.Length==2&&battle.DefenderArmyIds.Length==2&&battle.AttackerTroops==80&&battle.DefenderTroops==80,"Two armies on each side fight one shared battle");
        Check(report.Armies.Length==4&&report.AttackerUnits.Sum(t=>t.Sent)==80&&report.DefenderUnits.Sum(t=>t.Sent)==80,"Reports count every army and aggregate equal unit types without overwriting them");
        Until(()=>View().Warfare.Battles.Single().Round>=2);report=View().Reports.Single(r=>r.Id==battle.Id);
        Check(report.Armies.All(force=>force.Units.Sum(u=>u.Dead+u.Wounded+u.Fled)>0),"Casualties are distributed across actual armies on both sides");
        Order(a,CommandKind.RecallArmy,a1);Tick();
        Check(View().Warfare.Battles.Single().Status=="Active"&&View().Armies.Single(x=>x.Id==a2).Order=="Battle","Recalling one army leaves the other armies fighting");
        Check(!View().Warfare.Battles.Single().AttackerArmyIds.Contains(a1)&&View().Warfare.Battles.Single().AttackerArmyIds.Contains(a2),"Battle map markers follow active participants rather than withdrawn armies");
        Check(!View().Reports.Single(r=>r.Id==battle.Id).Armies.Single(x=>x.ArmyId==a1).Active,"A recalled army is retained in the report with an inactive state");
        var a3=Send(world,a,"spearman",40,now);Order(a,CommandKind.AttackArmy,a3,b1);
        Until(()=>View().Warfare.Battles.Single(x=>x.Id==battle.Id).AttackerArmyIds.Contains(a3));
        Check(View().Reports.Single(r=>r.Id==battle.Id).Armies.Length==5,"Late reinforcement joins the original report without creating duplicate battle reports");
        Until(()=>View().Warfare.Battles.Single(x=>x.Id==battle.Id).Status!="Active");report=View().Reports.Single(r=>r.Id==battle.Id);
        Check(report.Armies.SelectMany(a=>a.Units).All(u=>u.Sent>=u.Dead+u.Wounded+u.Fled+u.Destroyed),"Every participant retains a conserved final casualty breakdown");
        Check(report.AttackerLosses.Sum(l=>l.Dead+l.Wounded+l.Fled+l.Destroyed)==report.Armies.Where(a=>a.Attacking).SelectMany(a=>a.Units).Sum(l=>l.Dead+l.Wounded+l.Fled+l.Destroyed),"Per-army and aggregate attacker casualties reconcile");

        foreach(var pair in new[]{("swordsman","spearman"),("archer","spearman"),("lightcavalry","archer"),("crossbowman","heavycavalry")})
        {
            var x=City(500);var y=City(600);var w=new LogisticsWorld([x,y]);double t=0;
            var left=Send(w,x,pair.Item1,80);var right=Send(w,y,pair.Item2,80);
            void Step(){t+=.25;w.Advance(.25,t,0,3600);}
            for(var n=0;n<160;n++)Step();
            w.Apply(x.Village.OwnerId,new(Guid.NewGuid(),CommandKind.AttackArmy,left,DestinationId:right),t,0);
            for(var n=0;n<600&&!w.Snapshot(x.Village.OwnerId).Reports.Any(r=>r.Kind=="Battle"&&r.Status!="Active");n++)Step();
            var result=w.Snapshot(x.Village.OwnerId).Reports.Single(r=>r.Kind=="Battle");
            Check(result.Winner==x.Village.OwnerId,$"Composition counter: {pair.Item1} defeats an equal {pair.Item2} force");
        }
        {
            // Spearmen are strong in defense against cavalry, not when attacking cavalry.
            var cavCity=City(500);var spearCity=City(600);var w=new LogisticsWorld([cavCity,spearCity]);double t=0;
            var cav=Send(w,cavCity,"cavalry",80);var spear=Send(w,spearCity,"spearman",80);
            void Step(){t+=.25;w.Advance(.25,t,0,3600);}
            for(var n=0;n<160;n++)Step();
            w.Apply(cavCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.AttackArmy,cav,DestinationId:spear),t,0);
            for(var n=0;n<600&&!w.Snapshot(spearCity.Village.OwnerId).Reports.Any(r=>r.Kind=="Battle"&&r.Status!="Active");n++)Step();
            var defResult=w.Snapshot(spearCity.Village.OwnerId).Reports.Single(r=>r.Kind=="Battle");
            Check(defResult.Winner==spearCity.Village.OwnerId&&defResult.DefenderUnits.Sum(u=>u.Sent-u.Dead-u.Wounded-u.Fled-u.Destroyed)>=55,"Defending spearmen decisively defeat attacking cavalry");
        }
        {
            // Clubmen (germans-infantry) are strong when attacking but weak when defending against infantry/cavalry.
            var romCity=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(500,500));
            var gerCity=new VillageEconomy(Guid.NewGuid(),TribeType.Germans,new(600,500));
            var w=new LogisticsWorld([romCity,gerCity]);double t=0;
            romCity.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["romans-infantry"]),80)]);romCity.AddResources(new(2000,2000,2000,2000));
            gerCity.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["germans-infantry"]),80)]);gerCity.AddResources(new(2000,2000,2000,2000));
            w.Apply(romCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,romCity.Village.Id,Formation:new Dictionary<string,int>{{"romans-infantry",80}},Wagons:0,X:550,Y:500,Enabled:false),t,0);
            w.Apply(gerCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,gerCity.Village.Id,Formation:new Dictionary<string,int>{{"germans-infantry",80}},Wagons:0,X:580,Y:500,Enabled:false),t,0);
            var romArmy=w.Snapshot(romCity.Village.OwnerId).Armies.Single(a=>a.OwnerId==romCity.Village.OwnerId).Id;
            var gerArmy=w.Snapshot(gerCity.Village.OwnerId).Armies.Single(a=>a.OwnerId==gerCity.Village.OwnerId).Id;
            for(var n=0;n<80;n++){t+=.25;w.Advance(.25,t,0,3600);}
            // Roman army orders attack at t, German army orders attack later at t+1 -> Roman army is Attacker, German Clubmen are Defender.
            w.Apply(romCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.AttackArmy,romArmy,DestinationId:gerArmy),t,0);
            t+=1;
            w.Apply(gerCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.AttackArmy,gerArmy,DestinationId:romArmy),t,0);
            for(var n=0;n<600&&!w.Snapshot(romCity.Village.OwnerId).Reports.Any(r=>r.Kind=="Battle"&&r.Status!="Active");n++){t+=.25;w.Advance(.25,t,0,3600);}
            var clubResult=w.Snapshot(romCity.Village.OwnerId).Reports.Single(r=>r.Kind=="Battle");
            Check(clubResult.Attacker==romCity.Village.OwnerId&&clubResult.Defender==gerCity.Village.OwnerId,"Army that issued attack order first is the Attacker");
            Check(clubResult.Winner==romCity.Village.OwnerId&&clubResult.DefenderLosses.Sum(l=>l.Dead+l.Wounded+l.Fled)>clubResult.AttackerLosses.Sum(l=>l.Dead+l.Wounded+l.Fled)*2,"Defending Clubmen suffer heavy losses due to weak defensive stats");
        }

        var holder=City(700);var invader=City(1100);var ally=City(900);var territory=new LogisticsWorld([holder,invader,ally],73);double time=0;
        var oasis=territory.Snapshot(holder.Village.OwnerId).Oases.MinBy(o=>LogisticsWorld.Distance(holder.Village.Position,o.Position))!;
        WorldSnapshot SiteView()=>territory.Snapshot(holder.Village.OwnerId);
        void StepSite(){time+=.25;territory.AdvanceEconomies(.25,time,1);territory.Advance(.25,time,0,3600);}
        void SiteUntil(Func<bool> done){for(var n=0;n<4000&&!done();n++)StepSite();Check(done(),"Multi-army oasis transition completes");}
        var guard=Send(territory,holder,"infantry",5,time,oasis.Id);SiteUntil(()=>holder.OwnedOases==1);
        territory.Apply(holder.Village.OwnerId,new(Guid.NewGuid(),CommandKind.UpgradeOasisWall,oasis.Id),time,0);
        var first=Send(territory,invader,"infantry",10,time,oasis.Id,mode:OasisMode.Blockade);
        var second=Send(territory,invader,"infantry",10,time,oasis.Id,mode:OasisMode.Blockade);
        SiteUntil(()=>SiteView().OasisSieges.SingleOrDefault()?.ArmyIds.Length==2);
        var siege=SiteView().OasisSieges.Single();var siegeReport=SiteView().Reports.Single(r=>r.Id==siege.Id);
        Check(siegeReport.Armies.Count(a=>a.Attacking)==2&&siegeReport.AttackerUnits.Sum(u=>u.Sent)==20,"One oasis report includes both besieging armies");
        territory.Apply(invader.Village.OwnerId,new(Guid.NewGuid(),CommandKind.RecallArmy,first),time,0);StepSite();
        Check(SiteView().Oases.Single(o=>o.Id==oasis.Id).Blockaded&&SiteView().OasisSieges.Single().ArmyIds.SequenceEqual(new[]{second}),"One withdrawing besieger cannot lift another army's blockade");
        var relief=Send(territory,ally,"swordsman",60,time,oasis.Id,true);
        SiteUntil(()=>SiteView().Warfare.Battles.Any(b=>b.AttackerArmyIds.Contains(relief)||b.DefenderArmyIds.Contains(relief)));
        Check(territory.Snapshot(ally.Village.OwnerId).Reports.Any(r=>r.Id==siege.Id),"A reinforcement owner receives the shared oasis report after joining its defense");
        Check(SiteView().Reports.Single(r=>r.Id==siege.Id).Armies.Any(a=>a.ArmyId==relief&&!a.Attacking),"The live siege report includes relief troops arriving during the blockade");
        SiteUntil(()=>!SiteView().Oases.Single(o=>o.Id==oasis.Id).Blockaded);
        Check(holder.OwnedOases==1&&SiteView().Reports.Single(r=>r.Id==siege.Id).Status!="Active","Relief wins against remaining besiegers and archives the shared report");
        var cityAttacker=City(500);var cityDefender=City(600);var cityWorld=new LogisticsWorld([cityAttacker,cityDefender]);double cityTime=0;
        var leader=Send(cityWorld,cityAttacker,"infantry",60);var supporting=Send(cityWorld,cityAttacker,"archer",60);
        foreach(var id in new[]{leader,supporting})cityWorld.Apply(cityAttacker.Village.OwnerId,new(Guid.NewGuid(),CommandKind.BeginSiege,id,DestinationId:cityDefender.Village.Id),cityTime,0);
        for(var i=0;i<200&&cityWorld.Snapshot(cityAttacker.Village.OwnerId).Warfare.Sieges.SingleOrDefault()?.ArmyIds.Length!=2;i++){cityTime+=.25;cityWorld.Advance(.25,cityTime,0,3600);}
        var cityView=cityWorld.Snapshot(cityAttacker.Village.OwnerId);var citySiege=cityView.Warfare.Sieges.Single();
        Check(citySiege.ArmyIds.Length==2&&cityView.Reports.Single(r=>r.Id==citySiege.Id).AttackerUnits.Sum(t=>t.Sent)==120,"City sieges also aggregate attacking armies and their report");
        cityWorld.Apply(cityAttacker.Village.OwnerId,new(Guid.NewGuid(),CommandKind.RecallArmy,leader),cityTime,0);cityTime+=.25;cityWorld.Advance(.25,cityTime,0,3600);
        Check(cityDefender.Besieged&&cityWorld.Snapshot(cityAttacker.Village.OwnerId).Warfare.Sieges.Single().ArmyIds.SequenceEqual(new[]{supporting}),"Recalling the original city siege leader leaves the supporting siege intact");
        {
            // Scouting and counter-scouting checks (villages, armies, oases) + oasis reinforcement map visibility.
            var spyA=City(500);var spyB=City(620);var spyWorld=new LogisticsWorld([spyA,spyB],73);double spyTime=0;
            void SpyTick(){spyTime+=.25;spyWorld.Advance(.25,spyTime,0,3600);}
            // Give spyB 10 scouts at home; spyA sends 4 scouts -> defender has more scouts, so all 4 attacking scouts die, defender loses some scouts, and attacker sees no defender units in report.
            Gift(spyB,"romans-scout",10);
            var failedScout=Send(spyWorld,spyA,"scout",4,spyTime);
            spyWorld.Apply(spyA.Village.OwnerId,new(Guid.NewGuid(),CommandKind.Scout,failedScout,DestinationId:spyB.Village.Id),spyTime,0);
            Check(!spyWorld.Snapshot(spyB.Village.OwnerId).Armies.Any(a=>a.Id==failedScout),"Scout armies are only visible on the map to their owner");
            for(var i=0;i<200&&!spyWorld.Snapshot(spyA.Village.OwnerId).Reports.Any(r=>r.Kind=="Scout");i++)SpyTick();
            var intercepted=spyWorld.Snapshot(spyA.Village.OwnerId).Reports.Single(r=>r.Kind=="Scout");
            Check(intercepted.Status=="Intercepted"&&intercepted.DefenderUnits.Length==0&&intercepted.GarrisonUnits.Length==0,"Outnumbered scouts die and reveal no target intelligence to the attacker");
            Check(spyB.Available("romans-scout")<10,"Defending scouts suffer casualties when intercepting enemy scouts");
            // Now spyA sends 12 scouts (> remaining defending scouts) -> succeeds and reveals spyB's garrison!
            var successScout=Send(spyWorld,spyA,"scout",12,spyTime);
            spyWorld.Apply(spyA.Village.OwnerId,new(Guid.NewGuid(),CommandKind.Scout,successScout,DestinationId:spyB.Village.Id),spyTime,0);
            for(var i=0;i<200&&spyWorld.Snapshot(spyA.Village.OwnerId).Reports.Count(r=>r.Kind=="Scout")<2;i++)SpyTick();
            var scouted=spyWorld.Snapshot(spyA.Village.OwnerId).Reports.First(r=>r.Kind=="Scout"&&r.Id!=intercepted.Id);
            Check(scouted.Status=="Scouted"&&scouted.DefenderUnits.Sum(u=>u.Sent)>0,"Superior scouting force survives and reveals target garrison composition");
            // Scouting an enemy army reveals its exact formation in the world snapshot; oasis reinforcements stay hidden from enemy map view.
            var targetOasis=spyWorld.Snapshot(spyB.Village.OwnerId).Oases.MinBy(o=>LogisticsWorld.Distance(o.Position,spyB.Village.Position))!;
            var oasisGuard=Send(spyWorld,spyB,"infantry",15,spyTime,targetOasis.Id);
            for(var i=0;i<600&&spyWorld.Snapshot(spyB.Village.OwnerId).Armies.Single(a=>a.Id==oasisGuard).Order!="Guarding";i++)SpyTick();
            Check(!spyWorld.Snapshot(spyA.Village.OwnerId).Armies.Any(a=>a.Id==oasisGuard),"Soldiers guarding an oasis do not appear on the enemy world map");
        }
        return checks;
    }
}
