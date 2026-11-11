namespace Warzone;

public sealed partial class LogisticsWorld
{
    private static Position ConvoyCityDestination(Convoy convoy,VillageEconomy city)
    {
        if(!city.Besieged)return city.Village.Position;
        var point=city.Village.Position;
        var angle=Math.Atan2(convoy.Position.Y-point.Y,convoy.Position.X-point.X);
        return new(point.X+Math.Cos(angle)*65,point.Y+Math.Sin(angle)*65);
    }
    private void ValidateCityAttack(Guid actor, VillageEconomy city, AttackObjective objective,
        IReadOnlyDictionary<string, int> formation, double now, double shieldUntil)
    {
        if (!Enum.IsDefined(objective)) throw new GameRuleException("objective", "Choose a valid attack objective.");
        if (city.Ruined || city.Village.OwnerId == actor) throw new GameRuleException("target", "Select an enemy settlement.");
        if (now < shieldUntil) throw new GameRuleException("protected", "Wait until the protection shield expires.");
        bool Has(UnitRole role) => formation.Any(t=>t.Value>0 && GameCatalog.Units[t.Key].Role==role);
        if (!formation.Any(t=>t.Value>0 && GameCatalog.Units[t.Key].Role is not (UnitRole.Wagon or UnitRole.Governor)))
            throw new GameRuleException("troops", "Fighting troops are required.");
        if (objective == AttackObjective.Conquest && !Has(UnitRole.Governor))
            throw new GameRuleException("governor", "Include a governor in this army for conquest.");
        if (objective == AttackObjective.Raze && !Has(UnitRole.Catapult))
            throw new GameRuleException("catapult", "Include a catapult in this army for destruction.");
        var current=sieges.Find(s=>s.City==city && s.Status=="Active");
        if (current is not null && (current.Attacker.OwnerId!=actor || current.Objective!=objective))
            throw new GameRuleException("siege", "Joining armies must share the siege commander and attack objective.");
    }

    private void AdvanceCityObjective(SiegeState siege, double now)
    {
        var forces=siege.Attackers.Where(a=>armies.Contains(a)&&!a.Routing&&a.Order=="Sieging"&&Fighters(a)>0).ToArray();
        var leader=forces.FirstOrDefault(a=>RoleCount(a,siege.Objective==AttackObjective.Conquest ? UnitRole.Governor : UnitRole.Catapult)>0);
        if (leader is null) { EndSiege(siege,"SpecialistsLost",now); return; }
        // The same surviving formation must carry the specialists; reassignment never grants missing units.
        siege.Attackers.Remove(leader); siege.Attackers.Insert(0,leader);
        if (siege.Objective==AttackObjective.Conquest)
        {
            siege.Loyalty=Math.Max(0,siege.Loyalty-5*Math.Min(3,RoleCount(leader,UnitRole.Governor)));
            siege.Progress=100-siege.Loyalty;
            ReportEvent(siege.Id,now,$"Persuasion round {siege.Round}: loyalty {siege.Loyalty:0}%, persuasion {siege.Progress:0}%.");
            if (siege.Loyalty==0) ResolveCity(siege,now);
            return;
        }
        // Sixty rounds (~five minutes) for the levels present when demolition began.
        // Additional upgrades remain physical levels and must also be destroyed.
        siege.DemolitionCredit+=Math.Max(1,siege.InitialBuildingLevels)/60d;
        while (siege.DemolitionCredit>=1-1e-9)
        {
            var building=siege.City.Village.Buildings.Where(b=>b.Level>0)
                .OrderBy(b=>b.Type==BuildingType.MainHall).ThenByDescending(b=>b.Level).ThenBy(b=>b.Type).FirstOrDefault();
            if (building is null) break;
            siege.City.DamageBuilding(building.Type,building.Level==1);
            siege.DemolitionCredit=Math.Max(0,siege.DemolitionCredit-1);
            ReportEvent(siege.Id,now,$"Demolition: {building.Type} is now level {building.Level}.");
        }
        var remaining=siege.City.Village.Buildings.Sum(b=>b.Level);
        siege.Progress=Math.Clamp(100*(1-remaining/(double)Math.Max(1,siege.InitialBuildingLevels)),0,100);
        if (remaining==0) ResolveCity(siege,now);
        else ReportEvent(siege.Id,now,$"Demolition progress {siege.Progress:0}%. Remaining building levels: {remaining}.");
    }
}
