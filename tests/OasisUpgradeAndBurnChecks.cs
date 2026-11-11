using Warzone;

internal static class OasisUpgradeAndBurnChecks
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        void Reject(Action action, string code)
        {
            try { action(); }
            catch (GameRuleException e) when (e.Code == code) { checks++; return; }
            throw new Exception($"Expected {code}");
        }

        var home = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(600, 600));
        var enemy = new VillageEconomy(Guid.NewGuid(), TribeType.Romans, new(1000, 600));
        var world = new LogisticsWorld([home, enemy], 101);
        double now = 0;
        WorldSnapshot View() => world.Snapshot(home.Village.OwnerId);
        var site = View().Oases.Where(o => o.Resource == ResourceType.Wood).MinBy(o => LogisticsWorld.Distance(home.Village.Position, o.Position))!;
        OasisSnapshot Oasis() => View().Oases.Single(o => o.Id == site.Id);
        void Tick() { now += .25; world.Advance(.25, now, 0, 3600); world.AdvanceEconomies(.25, now, 1); }
        void Until(Func<bool> done, int max = 4000)
        {
            for (var i = 0; i < max && !done(); i++) Tick();
            Check(done(), "Oasis operation completed within budget");
        }

        // 1. Initial State: level 0, default bonuses
        Check(Oasis().Level == 0, "Oasis starts at level 0 (unranked)");
        Check(Oasis().BurnedRemainingSeconds == 0, "Oasis starts not burned");

        // Dispatch army to claim oasis
        world.Apply(home.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
            Formation: new Dictionary<string, int> { ["romans-infantry"] = 8 }, Wagons: 0, DestinationId: site.Id, Enabled: false,
            X: site.Position.X, Y: site.Position.Y), now, 0);
        Until(() => Oasis().OwnerId == home.Village.OwnerId);

        Check(home.OwnedOases == 1, "Home village owns the oasis");
        Check(home.OasisBonuses[0] == 0.5 && home.OasisBonuses[1] == 0.25, "Level 0 oasis gives base +50% matching, +25% other");
        Check(Oasis().ProductionPerMinute == new Resources(240, 120, 120, 120), "Level 0 oasis produces base 240/120");

        // 2. Upgrading Oasis Level
        home.AddResources(new(10000, 10000, 10000, 10000));
        Reject(() => world.Apply(enemy.Village.OwnerId, new(Guid.NewGuid(), CommandKind.UpgradeOasisLevel, site.Id), now, 0), "not_owned");

        var cost0 = Oasis().LevelUpgradeCost!.Value;
        var resBefore = home.Resources;
        world.Apply(home.Village.OwnerId, new(Guid.NewGuid(), CommandKind.UpgradeOasisLevel, site.Id), now, 0);
        Check(Oasis().Level == 1, "Oasis upgraded to level 1");
        Check(home.Resources == resBefore - cost0, "Oasis upgrade debited home village resources");
        Check(Math.Abs(home.OasisBonuses[0] - 0.55) < 0.001, "Level 1 oasis adds +5% matching bonus (now 55%)");
        Check(Math.Abs(home.OasisBonuses[1] - 0.30) < 0.001, "Level 1 oasis adds +5% other bonus (now 30%)");
        Tick();
        Check(Oasis().ProductionPerMinute.Wood == 252 && Oasis().ProductionPerMinute.Iron == 126, "Level 1 oasis accelerates production by 5% (252 wood / 126 iron)");

        // Upgrade through level 5
        while (Oasis().Level < 5)
        {
            home.ReceiveCargo(new(10000, 10000, 10000, 10000));
            world.Apply(home.Village.OwnerId, new(Guid.NewGuid(), CommandKind.UpgradeOasisLevel, site.Id), now, 0);
        }
        Check(Oasis().Level == 5, "Oasis upgraded to maximum level 5");
        Check(Math.Abs(home.OasisBonuses[0] - 0.75) < 0.001, "Level 5 adds total +25% bonus (75% matching)");
        Check(Math.Abs(home.OasisBonuses[1] - 0.50) < 0.001, "Level 5 adds total +25% bonus (50% other)");
        Check(Oasis().LevelUpgradeCost is null, "Level 5 has reached max level (no further upgrade cost)");
        Reject(() => world.Apply(home.Village.OwnerId, new(Guid.NewGuid(), CommandKind.UpgradeOasisLevel, site.Id), now, 0), "level");

        // 3. Burning the Oasis
        enemy.AddResources(new(5000, 5000, 5000, 5000));
        world.Apply(enemy.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, enemy.Village.Id,
            Formation: new Dictionary<string, int> { ["romans-infantry"] = 15 }, Wagons: 0, DestinationId: site.Id,
            OasisMode: OasisMode.Burn, X: site.Position.X, Y: site.Position.Y), now, 0);

        Until(() =>
        {
            var armies = world.Snapshot(enemy.Village.OwnerId).Armies;
            return armies.Any(a => a.TargetId == site.Id && a.Order == "Burning");
        });
        Check(world.Snapshot(enemy.Village.OwnerId).Armies.Any(a => a.TargetId == site.Id && a.Order == "Burning"), "Enemy army begins burning the oasis");
        Check(Oasis().BurningProgress > 0, "Burning progress is tracked");

        // Wait 60 seconds for burning to complete
        Until(() => Oasis().BurnedRemainingSeconds > 0, 1000);
        Check(Oasis().BurnedRemainingSeconds > 280, "Oasis is now burned for ~300 seconds");
        Check(Oasis().Level == 0, "Burned oasis level resets to 0");
        Check(Oasis().OwnerId is null, "Burned oasis loses its owner");
        Check(home.OwnedOases == 0, "Home village no longer owns the burned oasis");
        Check(home.OasisBonuses.All(b => b == 0), "Home village receives 0 bonus from burned oasis");

        Tick();
        Check(Oasis().ProductionPerMinute == default, "Burned oasis has 0 resource production");

        // Actions on burned oasis must be rejected
        Reject(() => world.Apply(home.Village.OwnerId, new(Guid.NewGuid(), CommandKind.UpgradeOasisLevel, site.Id), now, 0), "not_owned");
        Reject(() => world.Apply(home.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
            Formation: new Dictionary<string, int> { ["romans-infantry"] = 5 }, Wagons: 0, DestinationId: site.Id,
            X: site.Position.X, Y: site.Position.Y), now, 0), "burned");

        // Fast forward 300 seconds so the burned duration expires
        var burnedDuration = Oasis().BurnedRemainingSeconds;
        for (var i = 0; i < burnedDuration * 4 + 4; i++) Tick();
        Check(Oasis().BurnedRemainingSeconds == 0, "Burned timer expires after 5 minutes");

        // Can now be claimed again as a fresh neutral oasis
        world.Apply(home.Village.OwnerId, new(Guid.NewGuid(), CommandKind.DispatchArmy, home.Village.Id,
            Formation: new Dictionary<string, int> { ["romans-infantry"] = 8 }, Wagons: 0, DestinationId: site.Id, Enabled: false,
            X: site.Position.X, Y: site.Position.Y), now, 0);
        Until(() => Oasis().OwnerId == home.Village.OwnerId);
        Check(home.OwnedOases == 1 && Oasis().Level == 0, "Oasis can be recaptured fresh after burn cooldown");

        return checks;
    }
}
