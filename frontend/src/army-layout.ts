import { ArmyView, BattleView, Point, SiegeView, WorldView, project } from './map';

const BATTLE_SIDE_X = 88;
const BATTLE_STACK_Y = 112;

function battleParticipants(battle: BattleView): { attackers: string[]; defenders: string[] } {
  const attackers = [...new Set((battle.attackerArmyIds?.length ? battle.attackerArmyIds : [battle.attackerId]).filter(Boolean))];
  const defenders = [...new Set((battle.defenderArmyIds?.length ? battle.defenderArmyIds : [battle.defenderId]).filter(Boolean))];
  return { attackers, defenders };
}

function siegeParticipants(siege: SiegeView): string[] {
  return [...new Set((siege.armyIds?.length ? siege.armyIds : [siege.armyId]).filter(Boolean))];
}

/** Computes neat non-overlapping screen coordinates for stationary, battling, and sieging armies. */
export class ArmyLayout {
  private readonly slots = new Map<string, { siteId: string; slot: number }>();
  private readonly battleSides = new Map<string, -1 | 1>();

  /** Resolves and caches which horizontal side (-1 left, +1 right) the attacker occupies in a battle. */
  battleOrientation(battle: BattleView, world?: WorldView): -1 | 1 {
    const existing = this.battleSides.get(battle.id);
    if (existing) return existing;
    const { attackers, defenders } = battleParticipants(battle);
    const pointOf = (id: string | undefined): Point | undefined => {
      if (!id || !world) return undefined;
      return world.armies.find(a => a.id === id)?.position
        ?? world.convoys.find(c => c.id === id)?.position
        ?? world.warfare?.settlements.find(s => s.id === id)?.position;
    };
    const attPos = pointOf(attackers[0]);
    const defPos = pointOf(defenders[0]) ?? battle.position;
    const dir: -1 | 1 = attPos && defPos && Math.abs(attPos.x - defPos.x) >= 2
      ? (attPos.x <= defPos.x ? -1 : 1)
      : -1;
    this.battleSides.set(battle.id, dir);
    return dir;
  }

  /** Resolves and caches which horizontal side (-1 left, +1 right) sieging armies occupy relative to the village. */
  siegeOrientation(siege: SiegeView, world?: WorldView): -1 | 1 {
    const existing = this.battleSides.get(siege.id);
    if (existing) return existing;
    const leadId = siegeParticipants(siege)[0];
    const leadPos = leadId ? world?.armies.find(a => a.id === leadId)?.position : undefined;
    const sitePos = siege.position;
    const dir: -1 | 1 = leadPos && sitePos && Math.abs(leadPos.x - sitePos.x) >= 2
      ? (leadPos.x <= sitePos.x ? -1 : 1)
      : -1;
    this.battleSides.set(siege.id, dir);
    return dir;
  }

  /** Returns which side (-1 left, +1 right) an entity sits on if engaged in an active battle or siege, else 0. */
  activeBattleSide(entityId: string, world: WorldView): -1 | 1 | 0 {
    const battles = world.warfare?.battles ?? [];
    for (const battle of battles) {
      if (battle.status !== 'Active') continue;
      const { attackers, defenders } = battleParticipants(battle);
      const attackerSide = this.battleOrientation(battle, world);
      if (attackers.includes(entityId)) return attackerSide;
      if (defenders.includes(entityId)) return attackerSide === -1 ? 1 : -1;
    }
    const sieges = world.warfare?.sieges ?? [];
    for (const siege of sieges) {
      if (siege.status !== 'Active') continue;
      if (siegeParticipants(siege).includes(entityId)) return this.siegeOrientation(siege, world);
    }
    return 0;
  }

  /** Positions convoys so ambushed convoys stand opposite the attacker during an active battle. */
  convoyPosition(convoyId: string, rawPosition: Point, world: WorldView): Point {
    const battles = world.warfare?.battles ?? [];
    for (const battle of battles) {
      if (battle.status !== 'Active') continue;
      const { defenders } = battleParticipants(battle);
      if (!defenders.includes(convoyId)) continue;
      const attackerSide = this.battleOrientation(battle, world);
      const defenderSide: -1 | 1 = attackerSide === -1 ? 1 : -1;
      const center = project(battle.position ?? rawPosition);
      const index = Math.max(0, defenders.indexOf(convoyId));
      return { x: center.x + defenderSide * BATTLE_SIDE_X, y: center.y - index * BATTLE_STACK_Y };
    }
    return project(rawPosition);
  }

  position(army: ArmyView, world: WorldView): Point {
    const battles = world.warfare?.battles ?? [];
    for (const battle of battles) {
      if (battle.status !== 'Active') continue;
      const { attackers, defenders } = battleParticipants(battle);
      const isAttacker = attackers.includes(army.id);
      const isDefender = !isAttacker && defenders.includes(army.id);
      if (!isAttacker && !isDefender) continue;
      const attackerSide = this.battleOrientation(battle, world);
      const sideDir: -1 | 1 = isAttacker ? attackerSide : (attackerSide === -1 ? 1 : -1);
      const sideIndex = Math.max(0, (isAttacker ? attackers : defenders).indexOf(army.id));
      const center = project(battle.position ?? army.position);
      return { x: center.x + sideDir * BATTLE_SIDE_X, y: center.y - sideIndex * BATTLE_STACK_Y };
    }

    const sieges = world.warfare?.sieges ?? [];
    for (const siege of sieges) {
      if (siege.status !== 'Active') continue;
      const ids = siegeParticipants(siege);
      const sideIndex = ids.indexOf(army.id);
      if (sideIndex < 0) continue;
      const sideDir = this.siegeOrientation(siege, world);
      const center = project(siege.position ?? army.position);
      return { x: center.x + sideDir * BATTLE_SIDE_X, y: center.y - sideIndex * BATTLE_STACK_Y };
    }

    const base = project(army.position);
    const dx = army.destination.x - army.position.x, dy = army.destination.y - army.position.y;
    if (dx * dx + dy * dy > 16) {
      this.slots.delete(army.id);
      return base;
    }

    const oasis = world.oases.find(o => Math.hypot(o.position.x - army.position.x, o.position.y - army.position.y) <= 22);
    const city = world.warfare?.settlements.find(c => Math.hypot(c.position.x - army.position.x, c.position.y - army.position.y) <= 30);
    const site = oasis ?? city;
    if (!site) {
      this.slots.delete(army.id);
      return base;
    }

    const center = project(site.position);
    const nearby = world.armies.filter(other => {
      const mdx = other.destination.x - other.position.x, mdy = other.destination.y - other.position.y;
      return mdx * mdx + mdy * mdy <= 16 && Math.hypot(other.position.x - site.position.x, other.position.y - site.position.y) <= 30;
    });
    if (nearby.length <= 1) {
      this.slots.delete(army.id);
      return base;
    }

    let assigned = this.slots.get(army.id);
    if (!assigned || assigned.siteId !== site.id) {
      const used = new Set(nearby.map(other => this.slots.get(other.id)).filter(entry => entry?.siteId === site.id).map(entry => entry!.slot));
      let slot = 0;
      while (used.has(slot)) slot++;
      assigned = { siteId: site.id, slot };
      this.slots.set(army.id, assigned);
    }

    const radius = 58 + Math.floor(assigned.slot / 12) * 32;
    const angle = (assigned.slot % 12) * (Math.PI / 6);
    return { x: center.x + Math.cos(angle) * radius, y: center.y + Math.sin(angle) * radius * .65 };
  }

  retain(world: WorldView, live?: Set<string>) {
    const activeArmies = live ?? new Set(world.armies.map(a => a.id));
    for (const id of [...this.slots.keys()]) if (!activeArmies.has(id)) this.slots.delete(id);
    const activeConflicts = new Set<string>([
      ...(world.warfare?.battles.filter(b => b.status === 'Active').map(b => b.id) ?? []),
      ...(world.warfare?.sieges.filter(s => s.status === 'Active').map(s => s.id) ?? []),
    ]);
    for (const id of [...this.battleSides.keys()]) if (!activeConflicts.has(id)) this.battleSides.delete(id);
  }
}
