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
        if (CampAllied(city.Village.OwnerId, actor)) throw new GameRuleException("ally", "You cannot attack an allied settlement.");
        if (now < shieldUntil) throw new GameRuleException("protected", "Wait until the protection shield expires.");
        bool Has(UnitRole role) => formation.Any(t=>t.Value>0 && GameCatalog.Units[t.Key].Role==role);
        if (!formation.Any(t=>t.Value>0 && GameCatalog.Units[t.Key].Role is not (UnitRole.Wagon or UnitRole.Governor)))
            throw new GameRuleException("troops", "Fighting troops are required.");
        var current = sieges.Find(s => s.City == city && s.Status == "Active");
        var neededRole = objective == AttackObjective.Conquest ? UnitRole.Governor : UnitRole.Catapult;
        var allyHasSpecialist = objective is AttackObjective.Conquest or AttackObjective.Raze && (
            (current is not null && CampAllied(current.Attacker.OwnerId, actor) && current.Objective == objective
                && current.Attackers.Any(a => armies.Contains(a) && !a.Routing && RoleCount(a, neededRole) > 0))
            || armies.Any(a => a.Order == "Approaching" && a.TargetId == city.Village.Id && !a.Routing
                && CampAllied(a.OwnerId, actor) && a.Objective == objective && RoleCount(a, neededRole) > 0));
        if (objective == AttackObjective.Conquest && !Has(UnitRole.Governor) && !allyHasSpecialist)
            throw new GameRuleException("governor", "Include a governor in this army for conquest.");
        if (objective == AttackObjective.Raze && !Has(UnitRole.Catapult) && !allyHasSpecialist)
            throw new GameRuleException("catapult", "Include a catapult in this army for destruction.");
        if (current is not null && !CampAllied(current.Attacker.OwnerId, actor))
            throw new GameRuleException("siege", "A rival commander is already besieging this settlement.");
    }

    private void AdvanceCityObjective(SiegeState siege, double now)
    {
        var neededRole = siege.Objective == AttackObjective.Conquest ? UnitRole.Governor : UnitRole.Catapult;
        var forces=siege.Attackers.Where(a=>armies.Contains(a)&&!a.Routing&&a.Order=="Sieging"&&Fighters(a)>0).ToArray();
        var leader=forces.FirstOrDefault(a=>RoleCount(a,neededRole)>0);
        if (leader is null)
        {
            var specialistApproaching = siege.Phase is "Persuading" or "Demolishing" && armies.Any(a => a.Order == "Approaching" && a.TargetId == siege.City.Village.Id
                && !a.Routing && CampAllied(a.OwnerId, siege.Attacker.OwnerId) && a.Objective == siege.Objective
                && Distance(a.Position, siege.City.Village.Position) <= 350 && RoleCount(a, neededRole) > 0);
            if (specialistApproaching) return;
            EndSiege(siege,"SpecialistsLost",now);
            return;
        }
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
            var removedLevel = building.Level;
            siege.City.DamageBuilding(building.Type,building.Level==1);
            siege.DemolishedBuildingValue += removedLevel * 45L;
            siege.DemolitionCredit=Math.Max(0,siege.DemolitionCredit-1);
            ReportEvent(siege.Id,now,$"Demolition: {building.Type} is now level {building.Level}.");
        }
        var remaining=siege.City.Village.Buildings.Sum(b=>b.Level);
        if (remaining > siege.InitialBuildingLevels) siege.InitialBuildingLevels = remaining;
        siege.Progress=Math.Clamp(100*(1-remaining/(double)Math.Max(1,siege.InitialBuildingLevels)),0,100);
        if (remaining==0) ResolveCity(siege,now);
        else ReportEvent(siege.Id,now,$"Demolition progress {siege.Progress:0}%. Remaining building levels: {remaining}.");
    }
}
