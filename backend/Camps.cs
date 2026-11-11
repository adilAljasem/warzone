namespace Warzone;

public sealed partial class LogisticsWorld
{
    private void JoinCampArmy(BattleState battle, Army camp, Army enemy, double now)
    {
        if (battle.Contains(enemy) || enemy.Routing || Fighters(enemy) == 0) return;
        var attacking = battle.Defenders.Any(m => m.Army == camp);
        (attacking ? battle.Attackers : battle.Defenders).Add(new(enemy));
        enemy.Order = "Battle";
        AddReportArmy(reports.Single(r => r.Id == battle.Id), enemy, attacking);
        ReportEvent(battle.Id, now, "Another hostile army entered the camp perimeter and joined the battle.");
        Note(enemy.OwnerId, now, "Your army entered a hostile camp perimeter and is under attack.", "war", targetId: camp.Id);
    }
    private void JoinCampConvoy(BattleState battle, Army camp, Convoy convoy, double now)
    {
        if (convoy.InBattle || battle.ConvoyMembers.Any(m => m.Convoy == convoy)) return;
        var attacking = battle.Defenders.Any(m => m.Army == camp);
        var force = new Army(convoy.OwnerId, convoy.Position, convoy.SourceVillageId, convoy.Troops) { Wheat = convoy.WheatCargo };
        (attacking ? battle.Attackers : battle.Defenders).Add(new(force, convoy));
        force.Order = "Battle"; convoy.InBattle = true;
        AddReportArmy(reports.Single(r => r.Id == battle.Id), force, attacking);
        ReportEvent(battle.Id, now, "Another caravan entered the camp perimeter and joined the engagement.");
        Note(convoy.OwnerId, now, "Your caravan entered a hostile camp perimeter and joined the battle.", "convoy-attack", targetId: convoy.Id);
    }
    private void AdvanceCampBattles(double now, double shieldUntil)
    {
        if (now < shieldUntil) return;
        foreach (var camp in armies.Where(a => a.CampRadius is not null && !a.Routing && Fighters(a) > 0).ToArray())
        {
            if (!armies.Contains(camp)) continue;
            var radius = camp.CampRadius!.Value;
            var enemy = armies.Where(a => a != camp && !a.Routing && !Engaged(a) && Fighters(a) > 0
                    && !CampAllied(camp.OwnerId, a.OwnerId) && Distance(camp.Position, a.Position) <= radius)
                .OrderBy(a => Distance(camp.Position, a.Position)).FirstOrDefault();
            if (enemy is not null)
            {
                var battle = battles.FirstOrDefault(b => b.Status == "Active" && b.Contains(camp));
                if (battle is null) StartBattle(camp, enemy, now);
                else JoinCampArmy(battle, camp, enemy, now);
                continue;
            }
            var convoy = convoys.Where(c => !c.InBattle && !CampAllied(camp.OwnerId, c.OwnerId)
                    && (c.Loaded || !camp.CampLoadedOnly) && Distance(camp.Position, c.Position) <= radius)
                .OrderBy(c => Distance(camp.Position, c.Position)).FirstOrDefault();
            if (convoy is null) continue;
            var engagement = battles.FirstOrDefault(b => b.Status == "Active" && b.Contains(camp));
            if (engagement is null) StartConvoyBattle(camp, convoy, now);
            else JoinCampConvoy(engagement, camp, convoy, now);
        }
    }
}
