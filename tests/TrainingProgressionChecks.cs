using Warzone;

internal static class TrainingProgressionChecks
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        void Reject(Action action, string code)
        {
            try { action(); } catch (GameRuleException e) when (e.Code == code) { checks++; return; }
            throw new Exception($"Expected {code}");
        }
        void Upgrade(VillageEconomy city, Building building)
        {
            var hall = city.Village.Buildings.Single(b => b.Type == BuildingType.MainHall);
            while (building.Type != BuildingType.MainHall && hall.Level <= building.Level)
            {
                city.AddResources(new(50000,50000,50000,50000));
                city.Apply(new(Guid.NewGuid(),CommandKind.Upgrade,hall.QueueId),0);
            }
            city.AddResources(new(50000,50000,50000,50000));
            city.Apply(new(Guid.NewGuid(),CommandKind.Upgrade,building.QueueId),0);
        }
        foreach (var tribe in Enum.GetValues<TribeType>())
        {
            var city = new VillageEconomy(Guid.NewGuid(),tribe,new(500,500));
            Check(city.Training.Count == 14, "Every tribe has fourteen recruits");
            foreach (var unit in city.Training)
            {
                var view = city.Snapshot().Targets.Single(t=>t.Id==unit.QueueId);
                Check(view.UnitId == unit.Unit.UnitId && view.TrainingBuilding == unit.Unit.TrainingBuilding.ToString(), "Snapshot identifies the unit and its authoritative facility");
                Check(view.Unlocked == (unit.Unit.RequiredBuildingLevel == 1), "Initial unlocks match catalog requirements");
                if (view.Unlocked) continue;
                var stock=city.Resources;
                Reject(()=>city.Apply(new(Guid.NewGuid(),CommandKind.Train,unit.QueueId),0),"unit_locked");
                Reject(()=>city.Apply(new(Guid.NewGuid(),CommandKind.SetAutoQueue,unit.QueueId),0),"unit_locked");
                Reject(()=>city.Apply(new(Guid.NewGuid(),CommandKind.Upgrade,unit.QueueId),0),"unit_locked");
                Check(city.Resources==stock && city.Snapshot().Jobs.Length==0 && !unit.AutoQueueEnabled, "Locked commands neither spend resources nor queue work");
            }
            foreach (var facility in new[]{BuildingType.Barracks,BuildingType.Stable,BuildingType.SiegeWorkshop})
            {
                var building=city.Village.Buildings.Single(b=>b.Type==facility);
                var recruit=city.Training.First(t=>t.Unit.TrainingBuilding==facility && t.Unit.RequiredBuildingLevel==1);
                var baseline=city.Snapshot().Targets.Single(t=>t.Id==recruit.QueueId).TrainingSeconds!.Value;
                for(var level=2;level<=20;level++)
                {
                    Upgrade(city,building);
                    var view=city.Snapshot();
                    Check(Math.Abs(view.Targets.Single(t=>t.Id==recruit.QueueId).TrainingSeconds!.Value-baseline/(1+.025*(level-1)))<1e-9,
                        "Every facility upgrade adds 2.5 percentage points of speed, including tribe bonuses");
                    Check(Math.Abs(view.Targets.Single(t=>t.Id==building.QueueId).TrainingSpeedBonusPercent-2.5*(level-1))<1e-9,
                        "UI speed bonus matches server duration");
                    Check(view.Targets.Where(t=>t.TrainingBuilding==facility.ToString()).All(t=>t.Unlocked==(level>=t.RequiredBuildingLevel)),
                        "Units unlock at exactly the facility requirement");
                }
            }
        }
        var running=new VillageEconomy(Guid.NewGuid(),TribeType.Romans,new(500,500));
        var barracks=running.Village.Buildings.Single(b=>b.Type==BuildingType.Barracks);
        var infantry=running.Training.Single(t=>t.Unit.Role==UnitRole.Infantry);
        running.Apply(new(Guid.NewGuid(),CommandKind.Train,infantry.QueueId,Quantity:2),10);
        var job=running.Snapshot().Jobs.Single();
        Upgrade(running,barracks);
        Check(running.Snapshot().Jobs.Single()==job && job.StartedAt==10 && job.CompletesAt==18,
            "Upgrading preserves the paid batch's start and completion times");
        running.Advance(8,18,0);
        Check(running.Available(infantry.Unit.UnitId)==22,"Existing paid batch completes exactly once");
        var archer=running.Training.Single(t=>t.Unit.Role==UnitRole.Archer);
        running.Apply(new(Guid.NewGuid(),CommandKind.SetAutoQueue,archer.QueueId),18);
        running.DamageBuilding(BuildingType.Barracks);
        Check(!archer.AutoQueueEnabled && !running.Snapshot().Targets.Single(t=>t.Id==archer.QueueId).Unlocked,
            "Demolition below the requirement disables repeat recruitment and relocks the unit");
        Reject(()=>running.Apply(new(Guid.NewGuid(),CommandKind.Train,archer.QueueId),19),"unit_locked");
        return checks;
    }
}
