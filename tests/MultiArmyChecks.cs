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
        Check(GameCatalog.Units.Count==50,"Five peoples have ten distinct recruitable units each");
        foreach(var tribe in Enum.GetValues<TribeType>())
        {
            var city=new VillageEconomy(Guid.NewGuid(),tribe,new(500,500));
            Check(city.Training.Count==10,"Every expanded roster is exposed through actual training queues");
        }
        Check(GameCatalog.Units["mongols-lightcavalry"].Stats.Speed>GameCatalog.Units["greeks-lightcavalry"].Stats.Speed&&GameCatalog.Units["greeks-lightcavalry"].Stats.Speed>GameCatalog.Units["romans-lightcavalry"].Stats.Speed,"Nomad and Greek light cavalry have distinct speed advantages");
        Check(GameCatalog.Units["romans-lightcavalry"].Stats.Speed==52.5&&GameCatalog.Units["persians-cavalry"].Stats.Speed==30,"Cavalry and elephants move faster, with heavy mounts remaining slower");
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
        var joinedReport=View().Reports.Single(r=>r.Id==battle.Id);
        Check(joinedReport.Armies.Length==5,"Late reinforcement joins the original report without creating duplicate battle reports");
        Check(joinedReport.Armies.Select(x=>x.ArmyId).SequenceEqual([a1,a2,a3,b1,b2]),"Report keeps all attacking armies at the top in join order above defending armies");
        var b3=Send(world,b,"infantry",20,now);Order(b,CommandKind.AttackArmy,b3,a2);
        Until(()=>View().Warfare.Battles.Single(x=>x.Id==battle.Id).DefenderArmyIds.Contains(b3));
        Check(View().Reports.Single(r=>r.Id==battle.Id).Armies.Select(x=>x.ArmyId).SequenceEqual([a1,a2,a3,b1,b2,b3]),"Late defending reinforcement appends at the end of the defender group");
        Until(()=>View().Warfare.Battles.Single(x=>x.Id==battle.Id).Status!="Active");report=View().Reports.Single(r=>r.Id==battle.Id);
        Check(report.Armies.SelectMany(a=>a.Units).All(u=>u.Sent>=u.Dead+u.Wounded+u.Fled+u.Destroyed),"Every participant retains a conserved final casualty breakdown");
        Check(report.AttackerLosses.Sum(l=>l.Dead+l.Wounded+l.Fled+l.Destroyed)==report.Armies.Where(a=>a.Attacking).SelectMany(a=>a.Units).Sum(l=>l.Dead+l.Wounded+l.Fled+l.Destroyed),"Per-army and aggregate attacker casualties reconcile");

        foreach(var pair in new[]{("swordsman","spearman"),("heavycavalry","swordsman"),("heavycavalry","infantry")})
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
            // Matchups are unified at 1.0 across all roles; unit outcomes depend on raw stats without counter multipliers.
            Check(GameCatalog.Units.Values.All(u =>
                (u.AttackMatchups?.Values.All(v => Math.Abs(v - 1.0) < 1e-9) ?? true)
                && (u.DefenseMatchups?.Values.All(v => Math.Abs(v - 1.0) < 1e-9) ?? true)),
                "All unit attack and defense matchups are unified at 1.0");
            var cavCity=City(500);var spearCity=City(600);var w=new LogisticsWorld([cavCity,spearCity]);double t=0;
            var cav=Send(w,cavCity,"lightcavalry",80);var spear=Send(w,spearCity,"spearman",80);
            void Step(){t+=.25;w.Advance(.25,t,0,3600);}
            for(var n=0;n<160;n++)Step();
            w.Apply(cavCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.AttackArmy,cav,DestinationId:spear),t,0);
            for(var n=0;n<600&&!w.Snapshot(spearCity.Village.OwnerId).Reports.Any(r=>r.Kind=="Battle"&&r.Status!="Active");n++)Step();
            var cavResult=w.Snapshot(spearCity.Village.OwnerId).Reports.Single(r=>r.Kind=="Battle");
            Check(cavResult.Winner==cavCity.Village.OwnerId,"Without counter multipliers, higher-attack cavalry defeat equal-count spearmen on base stats");
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
        {
            // Role-based mitigation: attacking army loses more weak-attack units (Spearmen > Clubmen),
            // whereas defending army loses more weak-defense units (Clubmen > Spearmen).
            var attCity=new VillageEconomy(Guid.NewGuid(),TribeType.Germans,new(500,500));
            var defCity=new VillageEconomy(Guid.NewGuid(),TribeType.Germans,new(600,500));
            var w=new LogisticsWorld([attCity,defCity]);double t=0;
            attCity.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["germans-infantry"]),150),new(BaseUnit.Create(GameCatalog.Units["germans-spearman"]),150)]);
            attCity.AddResources(new(5000,5000,5000,5000));
            defCity.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["germans-infantry"]),100),new(BaseUnit.Create(GameCatalog.Units["germans-spearman"]),100)]);
            defCity.AddResources(new(5000,5000,5000,5000));
            w.Apply(attCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,attCity.Village.Id,Formation:new Dictionary<string,int>{{"germans-infantry",150},{"germans-spearman",150}},Wagons:0,X:550,Y:500,Enabled:false),t,0);
            w.Apply(defCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,defCity.Village.Id,Formation:new Dictionary<string,int>{{"germans-infantry",100},{"germans-spearman",100}},Wagons:0,X:580,Y:500,Enabled:false),t,0);
            var attArmy=w.Snapshot(attCity.Village.OwnerId).Armies.Single(x=>x.OwnerId==attCity.Village.OwnerId).Id;
            var defArmy=w.Snapshot(defCity.Village.OwnerId).Armies.Single(x=>x.OwnerId==defCity.Village.OwnerId).Id;
            for(var n=0;n<80;n++){t+=.25;w.Advance(.25,t,0,3600);}
            w.Apply(attCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.AttackArmy,attArmy,DestinationId:defArmy),t,0);
            for(var n=0;n<600&&!w.Snapshot(attCity.Village.OwnerId).Reports.Any(r=>r.Kind=="Battle"&&r.Status!="Active");n++){t+=.25;w.Advance(.25,t,0,3600);}
            var res=w.Snapshot(attCity.Village.OwnerId).Reports.Single(r=>r.Kind=="Battle");
            var attClubLoss=res.AttackerLosses.Where(l=>l.UnitId=="germans-infantry").Sum(l=>l.Dead+l.Wounded+l.Fled);
            var attSpearLoss=res.AttackerLosses.Where(l=>l.UnitId=="germans-spearman").Sum(l=>l.Dead+l.Wounded+l.Fled);
            Check(res.Winner==attCity.Village.OwnerId&&attSpearLoss>attClubLoss,"When attacking, weaker-attack Spearmen suffer more losses than stronger-attack Clubmen");
        }
        {
            // Defensive units (Hoplites: 15 ATK / 72 DEF) attacking a 30-DEF unit suffer >= 2x losses, yet hold and win when defending against equal Swordsmen.
            var hopCity=new VillageEconomy(Guid.NewGuid(),TribeType.Greeks,new(500,500));
            var swCity=new VillageEconomy(Guid.NewGuid(),TribeType.Mongols,new(600,500));
            var w1=new LogisticsWorld([hopCity,swCity]);double t1=0;
            hopCity.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["greeks-infantry"]),100)]);hopCity.AddResources(new(2000,2000,2000,2000));
            swCity.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["mongols-swordsman"]),100)]);swCity.AddResources(new(2000,2000,2000,2000));
            w1.Apply(hopCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,hopCity.Village.Id,Formation:new Dictionary<string,int>{{"greeks-infantry",100}},Wagons:0,X:550,Y:500,Enabled:false),t1,0);
            w1.Apply(swCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,swCity.Village.Id,Formation:new Dictionary<string,int>{{"mongols-swordsman",100}},Wagons:0,X:580,Y:500,Enabled:false),t1,0);
            var hArmy=w1.Snapshot(hopCity.Village.OwnerId).Armies.Single(a=>a.OwnerId==hopCity.Village.OwnerId).Id;
            var sArmy=w1.Snapshot(swCity.Village.OwnerId).Armies.Single(a=>a.OwnerId==swCity.Village.OwnerId).Id;
            for(var n=0;n<80;n++){t1+=.25;w1.Advance(.25,t1,0,3600);}
            w1.Apply(hopCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.AttackArmy,hArmy,DestinationId:sArmy),t1,0);
            for(var n=0;n<600&&!w1.Snapshot(hopCity.Village.OwnerId).Reports.Any(r=>r.Kind=="Battle"&&r.Status!="Active");n++){t1+=.25;w1.Advance(.25,t1,0,3600);}
            var rep1=w1.Snapshot(hopCity.Village.OwnerId).Reports.Single(r=>r.Kind=="Battle");
            var attDead=rep1.AttackerLosses.Sum(l=>l.Dead+l.Wounded+l.Fled);
            var defDead=rep1.DefenderLosses.Sum(l=>l.Dead+l.Wounded+l.Fled);
            Check(rep1.Winner==swCity.Village.OwnerId&&attDead>=defDead*2,"Hoplites (15 ATK) attacking 30-DEF defenders require 2+ attackers per defender killed");

            var romSwCity=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(500,500));
            var grkDefCity=new VillageEconomy(Guid.NewGuid(),TribeType.Greeks,new(600,500));
            var w2=new LogisticsWorld([romSwCity,grkDefCity]);double t2=0;
            romSwCity.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["romans-swordsman"]),100)]);romSwCity.AddResources(new(2000,2000,2000,2000));
            grkDefCity.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["greeks-infantry"]),100)]);grkDefCity.AddResources(new(2000,2000,2000,2000));
            w2.Apply(romSwCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,romSwCity.Village.Id,Formation:new Dictionary<string,int>{{"romans-swordsman",100}},Wagons:0,X:550,Y:500,Enabled:false),t2,0);
            w2.Apply(grkDefCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,grkDefCity.Village.Id,Formation:new Dictionary<string,int>{{"greeks-infantry",100}},Wagons:0,X:580,Y:500,Enabled:false),t2,0);
            var rArmy=w2.Snapshot(romSwCity.Village.OwnerId).Armies.Single(a=>a.OwnerId==romSwCity.Village.OwnerId).Id;
            var gArmy=w2.Snapshot(grkDefCity.Village.OwnerId).Armies.Single(a=>a.OwnerId==grkDefCity.Village.OwnerId).Id;
            for(var n=0;n<80;n++){t2+=.25;w2.Advance(.25,t2,0,3600);}
            w2.Apply(romSwCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.AttackArmy,rArmy,DestinationId:gArmy),t2,0);
            for(var n=0;n<600&&!w2.Snapshot(romSwCity.Village.OwnerId).Reports.Any(r=>r.Kind=="Battle"&&r.Status!="Active");n++){t2+=.25;w2.Advance(.25,t2,0,3600);}
            var rep2=w2.Snapshot(romSwCity.Village.OwnerId).Reports.Single(r=>r.Kind=="Battle");
            Check(rep2.Winner==grkDefCity.Village.OwnerId,"Defending Hoplites (72 DEF) hold and defeat an equal force of attacking Roman Swordsmen (80 ATK)");
        }
        {
            var attCity=new VillageEconomy(Guid.NewGuid(),TribeType.Germans,new(500,500));
            var defCity=new VillageEconomy(Guid.NewGuid(),TribeType.Germans,new(600,500));
            var w=new LogisticsWorld([attCity,defCity]);double t=0;
            attCity.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["germans-infantry"]),100),new(BaseUnit.Create(GameCatalog.Units["germans-spearman"]),100)]);
            attCity.AddResources(new(5000,5000,5000,5000));
            defCity.ReceiveGift([new(BaseUnit.Create(GameCatalog.Units["germans-infantry"]),200),new(BaseUnit.Create(GameCatalog.Units["germans-spearman"]),200)]);
            defCity.AddResources(new(5000,5000,5000,5000));
            w.Apply(attCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,attCity.Village.Id,Formation:new Dictionary<string,int>{{"germans-infantry",100},{"germans-spearman",100}},Wagons:0,X:550,Y:500,Enabled:false),t,0);
            w.Apply(defCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,defCity.Village.Id,Formation:new Dictionary<string,int>{{"germans-infantry",200},{"germans-spearman",200}},Wagons:0,X:580,Y:500,Enabled:false),t,0);
            var attArmy=w.Snapshot(attCity.Village.OwnerId).Armies.Single(x=>x.OwnerId==attCity.Village.OwnerId).Id;
            var defArmy=w.Snapshot(defCity.Village.OwnerId).Armies.Single(x=>x.OwnerId==defCity.Village.OwnerId).Id;
            for(var n=0;n<80;n++){t+=.25;w.Advance(.25,t,0,3600);}
            w.Apply(attCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.AttackArmy,attArmy,DestinationId:defArmy),t,0);
            for(var n=0;n<600&&!w.Snapshot(defCity.Village.OwnerId).Reports.Any(r=>r.Kind=="Battle"&&r.Status!="Active");n++){t+=.25;w.Advance(.25,t,0,3600);}
            var res=w.Snapshot(defCity.Village.OwnerId).Reports.Single(r=>r.Kind=="Battle");
            var defClubLoss=res.DefenderLosses.Where(l=>l.UnitId=="germans-infantry").Sum(l=>l.Dead+l.Wounded+l.Fled);
            var defSpearLoss=res.DefenderLosses.Where(l=>l.UnitId=="germans-spearman").Sum(l=>l.Dead+l.Wounded+l.Fled);
            Check(res.Winner==defCity.Village.OwnerId&&defClubLoss>defSpearLoss,"When defending, weaker-defense Clubmen suffer more losses than stronger-defense Spearmen");
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
        var leader=Send(cityWorld,cityAttacker,"infantry",60);var supporting=Send(cityWorld,cityAttacker,"spearman",60);
        foreach(var id in new[]{leader,supporting})cityWorld.Apply(cityAttacker.Village.OwnerId,new(Guid.NewGuid(),CommandKind.BeginSiege,id,DestinationId:cityDefender.Village.Id),cityTime,0);
        for(var i=0;i<200&&cityWorld.Snapshot(cityAttacker.Village.OwnerId).Warfare.Sieges.SingleOrDefault()?.ArmyIds.Length!=2;i++){cityTime+=.25;cityWorld.Advance(.25,cityTime,0,3600);}
        var cityView=cityWorld.Snapshot(cityAttacker.Village.OwnerId);var citySiege=cityView.Warfare.Sieges.Single();
        Check(citySiege.ArmyIds.Length==2&&cityView.Reports.Single(r=>r.Id==citySiege.Id).AttackerUnits.Sum(t=>t.Sent)==120,"City sieges also aggregate attacking armies and their report");
        cityWorld.Apply(cityAttacker.Village.OwnerId,new(Guid.NewGuid(),CommandKind.RecallArmy,leader),cityTime,0);cityTime+=.25;cityWorld.Advance(.25,cityTime,0,3600);
        Check(cityDefender.Besieged&&cityWorld.Snapshot(cityAttacker.Village.OwnerId).Warfare.Sieges.Single().ArmyIds.SequenceEqual(new[]{supporting}),"Recalling the original city siege leader leaves the supporting siege intact");
        {
            // Moving a besieging army via MoveArmy (right-click) lifts only that army's siege, keeping shared sieges intact until the last besieger walks away.
            var mvAtt=City(500);var mvDef=City(600);var mvWorld=new LogisticsWorld([mvAtt,mvDef],73);double mvTime=0;
            void MvTick(){mvTime+=.25;mvWorld.AdvanceEconomies(.25,mvTime,1);mvWorld.Advance(.25,mvTime,0,3600);}
            var m1=Send(mvWorld,mvAtt,"infantry",60,mvTime);var m2=Send(mvWorld,mvAtt,"spearman",60,mvTime);
            var full1=mvWorld.Snapshot(mvAtt.Village.OwnerId).Armies.Single(x=>x.Id==m1).Formation!;
            var full2=mvWorld.Snapshot(mvAtt.Village.OwnerId).Armies.Single(x=>x.Id==m2).Formation!;
            foreach(var (id,form) in new[]{(m1,full1),(m2,full2)})
                mvWorld.Apply(mvAtt.Village.OwnerId,new(Guid.NewGuid(),CommandKind.BeginSiege,id,DestinationId:mvDef.Village.Id,Objective:AttackObjective.Blockade,Formation:form),mvTime,0);
            for(var i=0;i<240&&mvWorld.Snapshot(mvAtt.Village.OwnerId).Warfare.Sieges.SingleOrDefault()?.ArmyIds.Length!=2;i++)MvTick();
            Check(mvDef.Besieged&&mvWorld.Snapshot(mvAtt.Village.OwnerId).Warfare.Sieges.Single().ArmyIds.Length==2,"Two armies besiege the city before right-click move");
            mvWorld.Apply(mvAtt.Village.OwnerId,new(Guid.NewGuid(),CommandKind.MoveArmy,m1,X:530,Y:530,Formation:full1),mvTime,0);
            Check(mvDef.Besieged&&mvWorld.Snapshot(mvAtt.Village.OwnerId).Warfare.Sieges.Single().Status=="Active"&&mvWorld.Snapshot(mvAtt.Village.OwnerId).Warfare.Sieges.Single().ArmyIds.SequenceEqual(new[]{m2}),"Moving one city besieger leaves the other army maintaining the siege");
            Check(mvWorld.Snapshot(mvAtt.Village.OwnerId).Armies.Single(x=>x.Id==m1).Order=="Marching","Moved city besieger marches away cleanly");
            mvWorld.Apply(mvAtt.Village.OwnerId,new(Guid.NewGuid(),CommandKind.MoveArmy,m2,X:540,Y:540,Formation:full2),mvTime,0);
            Check(!mvDef.Besieged&&!mvWorld.Snapshot(mvAtt.Village.OwnerId).Warfare.Sieges.Any(s=>s.Status=="Active"),"Moving the last city besieger lifts the village siege immediately");

            var mvOasis=mvWorld.Snapshot(mvAtt.Village.OwnerId).Oases.MinBy(o=>LogisticsWorld.Distance(mvDef.Village.Position,o.Position))!;
            var og=Send(mvWorld,mvDef,"infantry",5,mvTime,mvOasis.Id);
            for(var i=0;i<2400&&mvDef.OwnedOases==0;i++)MvTick();
            var ob1=Send(mvWorld,mvAtt,"infantry",20,mvTime,mvOasis.Id,mode:OasisMode.Blockade);
            var ob2=Send(mvWorld,mvAtt,"spearman",20,mvTime,mvOasis.Id,mode:OasisMode.Blockade);
            for(var i=0;i<2400&&mvWorld.Snapshot(mvAtt.Village.OwnerId).OasisSieges.SingleOrDefault()?.ArmyIds.Length!=2;i++)MvTick();
            var ob1Form=mvWorld.Snapshot(mvAtt.Village.OwnerId).Armies.Single(x=>x.Id==ob1).Formation!;
            var ob2Form=mvWorld.Snapshot(mvAtt.Village.OwnerId).Armies.Single(x=>x.Id==ob2).Formation!;
            mvWorld.Apply(mvAtt.Village.OwnerId,new(Guid.NewGuid(),CommandKind.MoveArmy,ob1,X:520,Y:520,Formation:ob1Form),mvTime,0);
            var osAfterOne=mvWorld.Snapshot(mvAtt.Village.OwnerId).OasisSieges.Single();
            Check(mvWorld.Snapshot(mvAtt.Village.OwnerId).Oases.Single(o=>o.Id==mvOasis.Id).Blockaded&&osAfterOne.ArmyId==ob2&&osAfterOne.ArmyIds.SequenceEqual(new[]{ob2}),"Moving one oasis besieger keeps the oasis blockaded by the remaining army");
            mvWorld.Apply(mvAtt.Village.OwnerId,new(Guid.NewGuid(),CommandKind.MoveArmy,ob2,X:525,Y:525,Formation:ob2Form),mvTime,0);
            Check(!mvWorld.Snapshot(mvAtt.Village.OwnerId).Oases.Single(o=>o.Id==mvOasis.Id).Blockaded&&mvWorld.Snapshot(mvAtt.Village.OwnerId).OasisSieges.Length==0,"Moving the last oasis besieger lifts the oasis siege immediately");
        }
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
            var villageGuard=Send(spyWorld,spyB,"infantry",15,spyTime,spyB.Village.Id,reinforce:true);
            for(var i=0;i<600&&spyWorld.Snapshot(spyB.Village.OwnerId).Armies.Single(a=>a.Id==villageGuard).Order!="Guarding";i++)SpyTick();
            Check(!spyWorld.Snapshot(spyA.Village.OwnerId).Armies.Any(a=>a.Id==villageGuard)&&spyWorld.Snapshot(spyB.Village.OwnerId).Armies.Any(a=>a.Id==villageGuard&&a.DefendingSiteId==spyB.Village.Id),"Soldiers guarding a village enter inside and stay hidden from enemy map view while visible to owner");
        }
        {
            // 1. Endgame Castle occupant must be Defender when attacked by a challenger
            var holderCity=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(1380,1500));
            var challengerCity=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(1620,1500));
            var castleWorld=new LogisticsWorld([holderCity,challengerCity]);
            double cTime=1805;
            castleWorld.Advance(.25,cTime,0,3600);
            var kPos=castleWorld.Snapshot(holderCity.Village.OwnerId).Warfare.Castle.Position;
            Gift(holderCity,"romans-infantry",40);Gift(holderCity,"romans-wagon",2);holderCity.AddResources(new(5000,5000,5000,5000));
            Gift(challengerCity,"romans-infantry",40);Gift(challengerCity,"romans-wagon",2);challengerCity.AddResources(new(5000,5000,5000,5000));
            castleWorld.Apply(holderCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,holderCity.Village.Id,Formation:new Dictionary<string,int>{["romans-infantry"]=40,["romans-wagon"]=2},X:kPos.X,Y:kPos.Y),cTime,0);
            var holderArmy=castleWorld.Snapshot(holderCity.Village.OwnerId).Armies.Single(a=>a.OwnerId==holderCity.Village.OwnerId).Id;
            castleWorld.Apply(challengerCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.DispatchArmy,challengerCity.Village.Id,Formation:new Dictionary<string,int>{["romans-infantry"]=40,["romans-wagon"]=2},X:kPos.X+90,Y:kPos.Y),cTime,0);
            var challengerArmy=castleWorld.Snapshot(challengerCity.Village.OwnerId).Armies.Single(a=>a.OwnerId==challengerCity.Village.OwnerId).Id;
            castleWorld.Apply(holderCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.CaptureCastle,holderArmy),cTime,0);
            for(var i=0;i<400&&castleWorld.Snapshot(holderCity.Village.OwnerId).Warfare.Castle.ControllerId!=holderCity.Village.OwnerId;i++){cTime+=.25;castleWorld.Advance(.25,cTime,0,3600);}
            Check(castleWorld.Snapshot(holderCity.Village.OwnerId).Warfare.Castle.ControllerId==holderCity.Village.OwnerId,"Occupant takes control of Crown Keep first");
            Check(castleWorld.Snapshot(challengerCity.Village.OwnerId).Armies.Any(a=>a.Id==holderArmy),"Troops occupying Crown Keep stay visible outside on the map");
            castleWorld.Apply(challengerCity.Village.OwnerId,new(Guid.NewGuid(),CommandKind.CaptureCastle,challengerArmy),cTime,0);
            for(var i=0;i<400&&!castleWorld.Snapshot(holderCity.Village.OwnerId).Warfare.Battles.Any(b=>b.Status=="Active");i++){cTime+=.25;castleWorld.Advance(.25,cTime,0,3600);}
            var castleBattle=castleWorld.Snapshot(holderCity.Village.OwnerId).Warfare.Battles.First(b=>b.Status=="Active");
            Check(castleBattle.DefenderOwner==holderCity.Village.OwnerId&&castleBattle.AttackerOwner==challengerCity.Village.OwnerId,"Current Crown Keep occupant is treated as Defender and challenger as Attacker");
        }
        {
            // 2. Multi-army battle alliances: allies on either side can join; unallied third parties (and bots) are blocked
            var p1=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(800,800));
            var p1Ally=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(840,800));
            var p2=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(1100,800));
            var p2Ally=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(1140,800));
            var outsider=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(950,650));
            var botOutsider=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(950,950));
            var mWorld=new LogisticsWorld([p1,p1Ally,p2,p2Ally,outsider,botOutsider]);
            mWorld.SetAlliances([new AllianceSnapshot(p1.Village.OwnerId,p1Ally.Village.OwnerId,0),new AllianceSnapshot(p2.Village.OwnerId,p2Ally.Village.OwnerId,0)]);
            double mTime=0;
            void MTick(){mTime+=.25;mWorld.AdvanceEconomies(.25,mTime,1);mWorld.Advance(.25,mTime,0,3600);}
            var aMain=Send(mWorld,p1,"infantry",60,mTime);
            var aHelp=Send(mWorld,p1Ally,"infantry",40,mTime);
            var bMain=Send(mWorld,p2,"infantry",60,mTime);
            var bHelp=Send(mWorld,p2Ally,"infantry",40,mTime);
            var cLate=Send(mWorld,outsider,"infantry",40,mTime);
            var cPreMarch=Send(mWorld,outsider,"infantry",35,mTime);
            var dBot=Send(mWorld,botOutsider,"infantry",80,mTime);
            mWorld.Apply(p1.Village.OwnerId,new(Guid.NewGuid(),CommandKind.MoveArmy,aMain,X:950,Y:800),mTime,0);
            mWorld.Apply(p2.Village.OwnerId,new(Guid.NewGuid(),CommandKind.MoveArmy,bMain,X:950,Y:800),mTime,0);
            mWorld.Apply(p1Ally.Village.OwnerId,new(Guid.NewGuid(),CommandKind.MoveArmy,aHelp,X:860,Y:800),mTime,0);
            mWorld.Apply(p2Ally.Village.OwnerId,new(Guid.NewGuid(),CommandKind.MoveArmy,bHelp,X:1040,Y:800),mTime,0);
            mWorld.Apply(outsider.Village.OwnerId,new(Guid.NewGuid(),CommandKind.MoveArmy,cLate,X:950,Y:700),mTime,0);
            mWorld.Apply(outsider.Village.OwnerId,new(Guid.NewGuid(),CommandKind.MoveArmy,cPreMarch,X:750,Y:750),mTime,0);
            mWorld.Apply(botOutsider.Village.OwnerId,new(Guid.NewGuid(),CommandKind.MoveArmy,dBot,X:950,Y:900),mTime,0);
            for(var i=0;i<160&&mWorld.Snapshot(p1.Village.OwnerId).Armies.Any(x=>x.Order!="Idle");i++)MTick();
            mWorld.Apply(outsider.Village.OwnerId,new(Guid.NewGuid(),CommandKind.AttackArmy,cPreMarch,DestinationId:bMain),mTime,0);
            mWorld.Apply(p1.Village.OwnerId,new(Guid.NewGuid(),CommandKind.AttackArmy,aMain,DestinationId:bMain),mTime,0);
            for(var i=0;i<100&&!mWorld.Snapshot(p1.Village.OwnerId).Warfare.Battles.Any(b=>b.Status=="Active");i++)MTick();
            var activeB=mWorld.Snapshot(p1.Village.OwnerId).Warfare.Battles.Single(b=>b.Status=="Active");
            // Allies from both sides join via AttackArmy on the active battle opponents
            mWorld.Apply(p1Ally.Village.OwnerId,new(Guid.NewGuid(),CommandKind.AttackArmy,aHelp,DestinationId:bMain),mTime,0);
            mWorld.Apply(p2Ally.Village.OwnerId,new(Guid.NewGuid(),CommandKind.AttackArmy,bHelp,DestinationId:aMain),mTime,0);
            // Unallied third party must be rejected when trying to attack either combatant
            var rejectedAttackA=false;try{mWorld.Apply(outsider.Village.OwnerId,new(Guid.NewGuid(),CommandKind.AttackArmy,cLate,DestinationId:aMain),mTime,0);}catch(GameRuleException e)when(e.Code=="battle"){rejectedAttackA=true;}
            var rejectedAttackB=false;try{mWorld.Apply(outsider.Village.OwnerId,new(Guid.NewGuid(),CommandKind.AttackArmy,cLate,DestinationId:bMain),mTime,0);}catch(GameRuleException e)when(e.Code=="battle"){rejectedAttackB=true;}
            Check(rejectedAttackA&&rejectedAttackB,"Unallied third party cannot enter an active battle against either side");
            for(var i=0;i<160&&mWorld.Snapshot(p1.Village.OwnerId).Warfare.Battles.Any(b=>b.Id==activeB.Id&&b.Status=="Active"&&(b.AttackerArmyIds.Length<2||b.DefenderArmyIds.Length<2));i++)MTick();
            var midB=mWorld.Snapshot(p1.Village.OwnerId).Warfare.Battles.Single(b=>b.Id==activeB.Id);
            Check(midB.AttackerArmyIds.Contains(aHelp)&&midB.DefenderArmyIds.Contains(bHelp),"Allies from both sides can send armies to assist in the ongoing battle");
            // Wait long enough for cPreMarch to arrive near bMain while battle is active — must stop instead of joining
            for(var i=0;i<120;i++)MTick();
            var stillB=mWorld.Snapshot(p1.Village.OwnerId).Warfare.Battles.SingleOrDefault(b=>b.Id==activeB.Id);
            if(stillB?.Status=="Active")
            {
                Check(!stillB.AttackerArmyIds.Contains(cPreMarch)&&!stillB.DefenderArmyIds.Contains(cPreMarch),"En-route unallied army cannot join an ongoing battle upon arrival");
                var botCmds=new BotSpyCommands(mWorld,mTime);
                mWorld.BotAct(botOutsider.Village.OwnerId,botCmds,BotPersonality.Aggressive,BotDifficulty.Hard);
                var activeParticipants=stillB.AttackerArmyIds.Concat(stillB.DefenderArmyIds).ToHashSet();
                Check(!botCmds.AttackTargets.Any(activeParticipants.Contains),"Unallied bot AI does not target active battles between non-allied armies");
                var rejectedBot=false;try{mWorld.Apply(botOutsider.Village.OwnerId,new(Guid.NewGuid(),CommandKind.AttackArmy,dBot,DestinationId:aMain),mTime,0);}catch(GameRuleException e)when(e.Code=="battle"){rejectedBot=true;}
                Check(rejectedBot,"Bot attack command is likewise rejected for non-allied active battle");
            }
        }
        {
            // 3. Army merging: immediate inside same oasis/village, march-to-meet in open field, into home garrison, and combat guard
            var u1=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(600,600));
            var enemyV=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(1400,600));
            var mergeWorld=new LogisticsWorld([u1,enemyV]);
            double gTime=0;
            void GTick(){gTime+=.25;mergeWorld.AdvanceEconomies(.25,gTime,1);mergeWorld.Advance(.25,gTime,0,3600);}
            Gift(u1,"romans-infantry",100);Gift(u1,"romans-wagon",6);u1.AddResources(new(5000,5000,5000,5000));
            var ownId=u1.Village.OwnerId;
            var nearOasis=mergeWorld.Snapshot(ownId).Oases.MinBy(o=>LogisticsWorld.Distance(o.Position,u1.Village.Position))!;
            var g1=Send(mergeWorld,u1,"infantry",15,gTime,nearOasis.Id);
            for(var i=0;i<600&&mergeWorld.Snapshot(ownId).Armies.Single(a=>a.Id==g1).Order!="Guarding";i++)GTick();
            var g2=Send(mergeWorld,u1,"infantry",10,gTime,nearOasis.Id,reinforce:true);
            for(var i=0;i<600&&mergeWorld.Snapshot(ownId).Armies.Any(a=>a.Id==g2);i++)GTick();
            var afterOasisMerge=mergeWorld.Snapshot(ownId).Armies.Where(a=>a.DefendingSiteId==nearOasis.Id).ToArray();
            Check(afterOasisMerge.Length==1&&afterOasisMerge[0].Soldiers==25&&afterOasisMerge[0].Order=="Guarding","Armies entering the same oasis automatically merge while preserving Guarding status");

            // Partial unit dispatch out of the merged oasis garrison leaves unselected units guarding the oasis
            Gift(u1,"romans-swordsman",12);
            var g3=Send(mergeWorld,u1,"swordsman",12,gTime,nearOasis.Id,reinforce:true);
            for(var i=0;i<600&&mergeWorld.Snapshot(ownId).Armies.Any(a=>a.Id==g3);i++)GTick();
            var mergedOasisArmy=mergeWorld.Snapshot(ownId).Armies.Single(a=>a.DefendingSiteId==nearOasis.Id);
            Check(mergedOasisArmy.Soldiers==37&&mergedOasisArmy.Formation!.GetValueOrDefault("romans-infantry")==25&&mergedOasisArmy.Formation!.GetValueOrDefault("romans-swordsman")==12,"Reinforcing swordsmen also auto-merge with infantry in the oasis");
            mergeWorld.Apply(ownId,new(Guid.NewGuid(),CommandKind.MoveArmy,mergedOasisArmy.Id,X:nearOasis.Position.X+80,Y:nearOasis.Position.Y,Formation:new Dictionary<string,int>{{"romans-swordsman",12}}),gTime,0);
            var movingOut=mergeWorld.Snapshot(ownId).Armies.Single(a=>a.Id==mergedOasisArmy.Id);
            var leftGuarding=mergeWorld.Snapshot(ownId).Armies.Single(a=>a.DefendingSiteId==nearOasis.Id);
            Check(movingOut.Order=="Marching"&&movingOut.Soldiers==12&&leftGuarding.Order=="Guarding"&&leftGuarding.Soldiers==25,"Selected oasis units march out as one army while unselected troops remain guarding the oasis");

            // Field merge: two field armies at distinct positions walk toward each other and merge
            var f1=Send(mergeWorld,u1,"infantry",12,gTime);
            var f2=Send(mergeWorld,u1,"infantry",8,gTime);
            mergeWorld.Apply(ownId,new(Guid.NewGuid(),CommandKind.MoveArmy,f1,X:700,Y:600),gTime,0);
            mergeWorld.Apply(ownId,new(Guid.NewGuid(),CommandKind.MoveArmy,f2,X:860,Y:600),gTime,0);
            for(var i=0;i<300&&mergeWorld.Snapshot(ownId).Armies.Any(a=>(a.Id==f1||a.Id==f2)&&a.Order!="Idle");i++)GTick();
            mergeWorld.Apply(ownId,new(Guid.NewGuid(),CommandKind.MergeArmies,f1,TargetIds:[f2]),gTime,0);
            var marchingArmies=mergeWorld.Snapshot(ownId).Armies.Where(a=>a.Id==f1||a.Id==f2).ToArray();
            Check(marchingArmies.Length==2&&marchingArmies.All(a=>a.Order=="Merging"&&LogisticsWorld.Distance(a.Destination,new(780,600))<.1),"Separated field armies march toward their shared midpoint when merging");
            for(var i=0;i<240&&mergeWorld.Snapshot(ownId).Armies.Count(a=>a.Id==f1||a.Id==f2)>1;i++)GTick();
            var mergedField=mergeWorld.Snapshot(ownId).Armies.Single(a=>a.Id==f1||a.Id==f2);
            Check(mergedField.Order=="Idle"&&mergedField.Soldiers==20,"Separated field armies automatically merge into one force upon meeting");

            // Direct merge into home village garrison when at village
            var homePreTotal=u1.Snapshot().Garrison.Values.Sum();
            var hArmy=Send(mergeWorld,u1,"infantry",10,gTime);
            mergeWorld.Apply(ownId,new(Guid.NewGuid(),CommandKind.MergeArmies,u1.Village.Id,TargetIds:[hArmy]),gTime,0);
            Check(!mergeWorld.Snapshot(ownId).Armies.Any(a=>a.Id==hArmy)&&u1.Snapshot().Garrison.Values.Sum()==homePreTotal+10,"Field army at home merges directly into the village garrison");
        }
        {
            // 4. Allies can inspect full army formation/stats and convoy cargo, while enemies cannot
            var allyA=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(500,500));
            var allyB=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(900,500));
            var foeC=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(1400,500));
            var visWorld=new LogisticsWorld([allyA,allyB,foeC]);
            visWorld.SetAlliances([new AllianceSnapshot(allyA.Village.OwnerId,allyB.Village.OwnerId,0)]);
            var aArmy=Send(visWorld,allyA,"infantry",15,0);
            allyB.AddResources(new(2000,2000,2000,2000));
            visWorld.Apply(allyB.Village.OwnerId,new(Guid.NewGuid(),CommandKind.SendResources,allyB.Village.Id,DestinationId:allyA.Village.Id,Shipment:new(120,80,40,60)),0,0);
            var viewFromAlly=visWorld.Snapshot(allyB.Village.OwnerId);
            var seenAllyArmy=viewFromAlly.Armies.Single(x=>x.Id==aArmy);
            var seenOwnConvoy=viewFromAlly.Convoys.Single();
            var viewFromOwnRecipient=visWorld.Snapshot(allyA.Village.OwnerId).Convoys.Single();
            var viewFromEnemy=visWorld.Snapshot(foeC.Village.OwnerId);
            var enemyArmyView=viewFromEnemy.Armies.Single(x=>x.Id==aArmy);
            var enemyConvoyView=viewFromEnemy.Convoys.Single();
            Check(seenAllyArmy.Formation is not null&&seenAllyArmy.Formation.GetValueOrDefault("romans-infantry")==15&&seenAllyArmy.Wheat is not null,"Allies see full unit formation and provisions of allied armies");
            Check(enemyArmyView.Formation is null&&enemyArmyView.Wheat is null,"Unallied enemies still cannot see allied army formation or provisions without scouting");
            Check(seenOwnConvoy.Cargo is{Wood:120,Iron:80,Stone:40,Wheat:60}&&viewFromOwnRecipient.Cargo is{Wood:120,Iron:80,Stone:40,Wheat:60},"Own and allied convoys show carried resources on inspection");
            Check(enemyConvoyView.Cargo is null&&enemyConvoyView.Wheat is null,"Unallied enemies cannot inspect convoy cargo or wheat");
        }
        return checks;
    }
    private sealed class BotSpyCommands(LogisticsWorld world,double now):IGameCommands
    {
        public List<Guid> AttackTargets{get;}=[];
        public CommandResult Execute(Guid actorId,GameCommand command)
        {
            if(command.Kind==CommandKind.AttackArmy&&command.DestinationId is{ }dest)AttackTargets.Add(dest);
            try{world.Apply(actorId,command,now,0);return new(command.RequestId,true,"ok","ok");}
            catch(GameRuleException e){return new(command.RequestId,false,e.Code,e.Message);}
        }
    }
}
