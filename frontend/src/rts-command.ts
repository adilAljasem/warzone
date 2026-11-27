import type { OasisView, Point, SettlementView, ArmyView, ConvoyView, WarfareView, BattleView, SiegeView, OasisSiegeView, MapSelection } from "./map";

export type RadialIconKind =
  | "swords"
  | "shield"
  | "eye"
  | "tent"
  | "flame"
  | "tower"
  | "flag"
  | "blockade"
  | "move"
  | "crown"
  | "transfer"
  | "home";

export type RadialActionId =
  | "capture-oasis"
  | "besiege-oasis"
  | "burn-oasis"
  | "reinforce"
  | "recall"
  | "camp"
  | "scout"
  | "raid"
  | "conquest"
  | "raze"
  | "blockade-city"
  | "attack-army"
  | "intercept-convoy"
  | "move"
  | "capture-castle";

export interface RadialOption {
  id: RadialActionId;
  label: string;
  icon: RadialIconKind;
  tone: "danger" | "friendly" | "warn" | "default";
  disabled?: boolean;
  targetOverride?: MapSelection;
}

export interface HoverBehavior {
  intent: "attack" | "ally" | "move" | "none";
  directAction?: RadialOption;
  options: RadialOption[];
}

export const CROSSED_SWORDS_SVG = `<svg viewBox="0 0 24 24" aria-hidden="true">
  <path d="M5 19L19 5M19 5h-4M19 5v4" stroke="#fca5a5" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" fill="none"/>
  <path d="M4.2 15.8l4 4M19 19L5 5M5 5h4M5 5v4" stroke="#ef4444" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" fill="none"/>
  <path d="M19.8 15.8l-4 4" stroke="#f87171" stroke-width="2.2" stroke-linecap="round"/>
</svg>`;

export const GREEN_SHIELD_SVG = `<svg viewBox="0 0 24 24" aria-hidden="true">
  <path d="M12 2.5L4 6v5.8c0 5.1 3.4 9.5 8 10.7 4.6-1.2 8-5.6 8-10.7V6l-8-3.5z" fill="#22c55e" stroke="#bbf7d0" stroke-width="1.6"/>
  <path d="M9 12.2l2 2.1 4.2-4.3" fill="none" stroke="#052e16" stroke-width="2.3" stroke-linecap="round" stroke-linejoin="round"/>
</svg>`;

export const MOVE_RETICLE_SVG = `<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="#f5d57a" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
  <circle cx="12" cy="12" r="5"/>
  <path d="M12 3v3M12 18v3M3 12h3M18 12h3"/>
</svg>`;

const RADIAL_ICONS: Record<RadialIconKind, string> = {
  swords: CROSSED_SWORDS_SVG,
  shield: GREEN_SHIELD_SVG,
  eye: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M2 12s3.8-6 10-6 10 6 10 6-3.8 6-10 6-10-6-10-6z"/><circle cx="12" cy="12" r="2.8" fill="currentColor"/></svg>`,
  tent: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 20L12 4l9 16H3z"/><path d="M12 4v16M9.5 20l2.5-5 2.5 5"/></svg>`,
  flame: `<svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M12.7 2.5c.3 3.2-1.8 5-3.2 6.8-1.2 1.5-1.5 3-.6 4.7.2-1 .9-1.9 2-2.4-.2 2 .8 3.2 2.1 4.1 1 .7 1.6 1.8 1.3 3 2.5-1 4.2-3.4 4.2-6.2 0-4.4-3-7.7-5.8-10zM12 21.5a5.5 5.5 0 0 1-5.5-5.5c0-2.6 1.5-4.7 3.1-6.5"/></svg>`,
  tower: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 20h12M7.5 20l1-10h7l1 10M6.5 10V4h3v2.5h5V4h3v6h-11z"/><path d="M10.5 20v-4a1.5 1.5 0 0 1 3 0v4"/></svg>`,
  flag: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M5 21V4m0 0h11l-2.2 4L16 12H5"/></svg>`,
  blockade: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><circle cx="12" cy="12" r="8"/><path d="M6.4 6.4l11.2 11.2M12 4v3M12 17v3M4 12h3M17 12h3"/></svg>`,
  move: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M5 12h14m-6-6 6 6-6 6"/></svg>`,
  crown: `<svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M3 18l2-10 4.5 4L12 5l2.5 7L19 8l2 10H3zm1 2h16v2H4v-2z"/></svg>`,
  transfer: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M17 3l4 4-4 4"/><path d="M3 7h18"/><path d="M7 21l-4-4 4-4"/><path d="M21 17H3"/></svg>`,
  home: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 10.5 12 3l9 7.5"/><path d="M5 9.5V20h5v-6h4v6h5V9.5"/></svg>`
};

export interface TargetRuleContext {
  you: string;
  isAlly: (playerId?: string | null) => boolean;
  selectedIds: string[];
  oases: OasisView[];
  settlements: SettlementView[];
  armies: ArmyView[];
  convoys: ConvoyView[];
  battles?: BattleView[];
  sieges?: SiegeView[];
  oasisSieges?: OasisSiegeView[];
  castle?: WarfareView["castle"];
  ownedVillagesCount: number;
  hasGovernor: boolean;
  hasCatapult: boolean;
  hasFieldArmies?: boolean;
  hasFighters?: boolean;
  protectedEnemy: boolean;
  isAr: boolean;
}

export function canAttackArmyInBattle(
  armyId: string,
  ctx: Pick<TargetRuleContext, "you" | "isAlly" | "armies" | "battles">
): boolean {
  const activeBattle = (ctx.battles ?? []).find(
    b => b.status === "Active" && (b.attackerArmyIds.includes(armyId) || b.defenderArmyIds.includes(armyId))
  );
  if (!activeBattle) return true;
  const targetOnAttacker = activeBattle.attackerArmyIds.includes(armyId);
  const targetSideIds = targetOnAttacker ? activeBattle.attackerArmyIds : activeBattle.defenderArmyIds;
  const opposingSideIds = targetOnAttacker ? activeBattle.defenderArmyIds : activeBattle.attackerArmyIds;
  const sideHasAllyOrSelf = (ids: string[], fallbackOwner?: string) => {
    const matched = ids
      .map(id => ctx.armies.find(a => a.id === id))
      .filter((a): a is ArmyView => !!a);
    if (matched.length > 0) return matched.some(a => a.ownerId === ctx.you || ctx.isAlly(a.ownerId));
    return !!fallbackOwner && (fallbackOwner === ctx.you || ctx.isAlly(fallbackOwner));
  };
  if (sideHasAllyOrSelf(targetSideIds, targetOnAttacker ? activeBattle.attackerOwner : activeBattle.defenderOwner)) {
    return false;
  }
  return sideHasAllyOrSelf(opposingSideIds, targetOnAttacker ? activeBattle.defenderOwner : activeBattle.attackerOwner);
}

function allyHasObjective(villageId: string, objective: string, ctx: TargetRuleContext): boolean {
  const active = (ctx.sieges ?? []).some(
    s => s.villageId === villageId && s.status === "Active" && ctx.isAlly(s.attackerOwner) && s.objective === objective
  );
  if (active) return true;
  return ctx.armies.some(
    a => a.order === "Approaching" && a.targetId === villageId && ctx.isAlly(a.ownerId) && a.objective === objective
  );
}

function siegeJoinOption(
  objective: string | null | undefined,
  city: SettlementView,
  ctx: TargetRuleContext,
  L: (ar: string, en: string) => string
): RadialOption {
  const targetOverride: MapSelection = {
    id: city.id,
    kind: "village",
    name: city.id,
    position: city.position,
    ownerId: city.ownerId
  };
  if (objective === "Raze") {
    return { id: "raze", label: L("انضمام للتدمير", "Join demolition"), icon: "tower", tone: "danger", disabled: ctx.protectedEnemy, targetOverride };
  }
  if (objective === "Conquest") {
    return { id: "conquest", label: L("انضمام للاحتلال", "Join conquest"), icon: "flag", tone: "danger", disabled: ctx.protectedEnemy, targetOverride };
  }
  if (objective === "Raid") {
    return { id: "raid", label: L("انضمام للغارة", "Join raid"), icon: "swords", tone: "danger", disabled: ctx.protectedEnemy, targetOverride };
  }
  return { id: "blockade-city", label: L("انضمام للحصار", "Join siege"), icon: "blockade", tone: "warn", disabled: ctx.protectedEnemy, targetOverride };
}

function oasisJoinOption(
  mode: string | null | undefined,
  oasis: OasisView,
  ctx: TargetRuleContext,
  L: (ar: string, en: string) => string
): RadialOption {
  const targetOverride: MapSelection = {
    id: oasis.id,
    kind: "oasis",
    name: oasis.name,
    position: oasis.position,
    ownerId: oasis.ownerId
  };
  const blocked = !!oasis.ownerId && ctx.protectedEnemy;
  if (mode === "Burn") return { id: "burn-oasis", label: L("انضمام للحرق", "Join oasis burn"), icon: "flame", tone: "danger", disabled: blocked, targetOverride };
  if (mode === "Blockade") return { id: "besiege-oasis", label: L("انضمام للحصار", "Join oasis siege"), icon: "tower", tone: "warn", disabled: blocked, targetOverride };
  return { id: "capture-oasis", label: L("انضمام لاحتلال الواحة", "Join oasis capture"), icon: "swords", tone: "danger", disabled: blocked, targetOverride };
}

function siteTargetSelection(siteId: string | null | undefined, ctx: TargetRuleContext): MapSelection | null {
  if (!siteId) return null;
  const city = ctx.settlements.find(v => v.id === siteId && !v.ruined);
  if (city) return { id: city.id, kind: "village", name: city.id, position: city.position, ownerId: city.ownerId };
  const oasis = ctx.oases.find(o => o.id === siteId && (o.burnedRemainingSeconds ?? 0) <= 0);
  if (oasis) return { id: oasis.id, kind: "oasis", name: oasis.name, position: oasis.position, ownerId: oasis.ownerId };
  return null;
}

function resolveAllyArmyJoinBehavior(
  a: ArmyView,
  ctx: TargetRuleContext,
  L: (ar: string, en: string) => string
): HoverBehavior {
  const activeSiege = (ctx.sieges ?? []).find(s => s.status === "Active" && s.armyIds.includes(a.id));
  if (activeSiege) {
    const city = ctx.settlements.find(v => v.id === activeSiege.villageId && !v.ruined && v.ownerId !== ctx.you && !ctx.isAlly(v.ownerId));
    if (city) return { intent: "ally", directAction: siegeJoinOption(activeSiege.objective, city, ctx, L), options: [] };
  }
  if (a.order === "Approaching" && a.targetId) {
    const city = ctx.settlements.find(v => v.id === a.targetId && !v.ruined && v.ownerId !== ctx.you && !ctx.isAlly(v.ownerId));
    if (city) return { intent: "ally", directAction: siegeJoinOption(a.objective ?? "Blockade", city, ctx, L), options: [] };
  }
  const activeOasisSiege = (ctx.oasisSieges ?? []).find(s => s.armyIds.includes(a.id));
  if (activeOasisSiege) {
    const oasis = ctx.oases.find(o => o.id === activeOasisSiege.oasisId && (o.burnedRemainingSeconds ?? 0) <= 0);
    if (oasis) return { intent: "ally", directAction: oasisJoinOption(activeOasisSiege.mode, oasis, ctx, L), options: [] };
  }
  if ((a.order === "Approaching" || a.order === "Occupying") && a.targetId) {
    const oasis = ctx.oases.find(o => o.id === a.targetId && (o.burnedRemainingSeconds ?? 0) <= 0);
    if (oasis && oasis.ownerId !== ctx.you && !ctx.isAlly(oasis.ownerId)) {
      return { intent: "ally", directAction: oasisJoinOption("Capture", oasis, ctx, L), options: [] };
    }
  }
  const activeBattle = (ctx.battles ?? []).find(
    b => b.status === "Active" && (b.attackerArmyIds.includes(a.id) || b.defenderArmyIds.includes(a.id))
  );
  if (activeBattle) {
    const onAttacker = activeBattle.attackerArmyIds.includes(a.id);
    if (!onAttacker) {
      const defSite = siteTargetSelection(a.defendingSiteId ?? activeBattle.defenderId, ctx);
      if (defSite) {
        return {
          intent: "ally",
          directAction: { id: "reinforce", label: L("انضمام للمدافع الحليف", "Join allied defender"), icon: "shield", tone: "friendly", targetOverride: defSite },
          options: []
        };
      }
    }
    const opposingIds = onAttacker ? activeBattle.defenderArmyIds : activeBattle.attackerArmyIds;
    const enemyArmy = opposingIds
      .map(id => ctx.armies.find(x => x.id === id))
      .find(x => x && x.ownerId !== ctx.you && !ctx.isAlly(x.ownerId));
    if (enemyArmy) {
      return {
        intent: "ally",
        directAction: {
          id: "attack-army",
          label: onAttacker ? L("انضمام للمهاجم الحليف", "Join allied attacker") : L("انضمام للمدافع الحليف", "Join allied defender"),
          icon: onAttacker ? "swords" : "shield",
          tone: onAttacker ? "danger" : "friendly",
          disabled: ctx.protectedEnemy,
          targetOverride: { id: enemyArmy.id, kind: "army", name: enemyArmy.id, position: enemyArmy.position, ownerId: enemyArmy.ownerId }
        },
        options: []
      };
    }
  }
  if (a.order === "Attacking" && a.targetId) {
    const enemyArmy = ctx.armies.find(x => x.id === a.targetId && x.ownerId !== ctx.you && !ctx.isAlly(x.ownerId));
    if (enemyArmy) {
      return {
        intent: "ally",
        directAction: {
          id: "attack-army",
          label: L("انضمام للمهاجم الحليف", "Join allied attack"),
          icon: "swords",
          tone: "danger",
          disabled: ctx.protectedEnemy,
          targetOverride: { id: enemyArmy.id, kind: "army", name: enemyArmy.id, position: enemyArmy.position, ownerId: enemyArmy.ownerId }
        },
        options: []
      };
    }
  }
  if (a.defendingSiteId && (a.order === "Guarding" || a.order === "Reinforcing" || a.order === "Battle")) {
    const site = siteTargetSelection(a.defendingSiteId, ctx);
    if (site) {
      return {
        intent: "ally",
        directAction: { id: "reinforce", label: L("انضمام للمدافع الحليف", "Join allied defense"), icon: "shield", tone: "friendly", targetOverride: site },
        options: []
      };
    }
  }
  return { intent: "none", options: [] };
}

function resolveBattleTargetBehavior(
  battleId: string,
  ctx: TargetRuleContext,
  L: (ar: string, en: string) => string
): HoverBehavior {
  const siege = (ctx.sieges ?? []).find(s => s.id === battleId && s.status === "Active");
  if (siege) {
    const city = ctx.settlements.find(v => v.id === siege.villageId && !v.ruined);
    if (city) {
      if (siege.defenderOwner === ctx.you || ctx.isAlly(siege.defenderOwner)) {
        return {
          intent: "ally",
          directAction: {
            id: "reinforce",
            label: L("تعزيز الدفاع", "Join defender"),
            icon: "shield",
            tone: "friendly",
            targetOverride: { id: city.id, kind: "village", name: city.id, position: city.position, ownerId: city.ownerId }
          },
          options: []
        };
      }
      if (siege.attackerOwner === ctx.you || ctx.isAlly(siege.attackerOwner)) {
        return { intent: "ally", directAction: siegeJoinOption(siege.objective, city, ctx, L), options: [] };
      }
    }
  }
  const oasisSiege = (ctx.oasisSieges ?? []).find(s => s.id === battleId);
  if (oasisSiege) {
    const oasis = ctx.oases.find(o => o.id === oasisSiege.oasisId && (o.burnedRemainingSeconds ?? 0) <= 0);
    if (oasis) {
      if (oasisSiege.defenderOwner === ctx.you || ctx.isAlly(oasisSiege.defenderOwner)) {
        return {
          intent: "ally",
          directAction: {
            id: "reinforce",
            label: L("تعزيز الواحة", "Join oasis defense"),
            icon: "shield",
            tone: "friendly",
            targetOverride: { id: oasis.id, kind: "oasis", name: oasis.name, position: oasis.position, ownerId: oasis.ownerId }
          },
          options: []
        };
      }
      if (oasisSiege.attackerOwners.some(id => id === ctx.you || ctx.isAlly(id))) {
        return { intent: "ally", directAction: oasisJoinOption(oasisSiege.mode, oasis, ctx, L), options: [] };
      }
    }
  }
  const battle = (ctx.battles ?? []).find(b => b.id === battleId && b.status === "Active");
  if (battle) {
    const atkAllied = battle.attackerOwner === ctx.you || ctx.isAlly(battle.attackerOwner)
      || battle.attackerArmyIds.some(id => { const x = ctx.armies.find(a => a.id === id); return !!x && (x.ownerId === ctx.you || ctx.isAlly(x.ownerId)); });
    const defAllied = battle.defenderOwner === ctx.you || ctx.isAlly(battle.defenderOwner)
      || battle.defenderArmyIds.some(id => { const x = ctx.armies.find(a => a.id === id); return !!x && (x.ownerId === ctx.you || ctx.isAlly(x.ownerId)); });
    if (defAllied && !atkAllied) {
      const defArmy = battle.defenderArmyIds.map(id => ctx.armies.find(a => a.id === id)).find(Boolean);
      const site = siteTargetSelection(defArmy?.defendingSiteId ?? battle.defenderId, ctx);
      if (site) {
        return {
          intent: "ally",
          directAction: { id: "reinforce", label: L("انضمام للمدافع الحليف", "Join allied defender"), icon: "shield", tone: "friendly", targetOverride: site },
          options: []
        };
      }
      const enemyAtk = battle.attackerArmyIds.map(id => ctx.armies.find(a => a.id === id)).find(a => a && a.ownerId !== ctx.you && !ctx.isAlly(a.ownerId));
      if (enemyAtk) {
        return {
          intent: "ally",
          directAction: {
            id: "attack-army",
            label: L("انضمام للمدافع الحليف", "Join allied defender"),
            icon: "shield",
            tone: "friendly",
            disabled: ctx.protectedEnemy,
            targetOverride: { id: enemyAtk.id, kind: "army", name: enemyAtk.id, position: enemyAtk.position, ownerId: enemyAtk.ownerId }
          },
          options: []
        };
      }
    }
    if (atkAllied && !defAllied) {
      const enemyDef = battle.defenderArmyIds.map(id => ctx.armies.find(a => a.id === id)).find(a => a && a.ownerId !== ctx.you && !ctx.isAlly(a.ownerId));
      if (enemyDef) {
        return {
          intent: "ally",
          directAction: {
            id: "attack-army",
            label: L("انضمام للمهاجم الحليف", "Join allied attacker"),
            icon: "swords",
            tone: "danger",
            disabled: ctx.protectedEnemy,
            targetOverride: { id: enemyDef.id, kind: "army", name: enemyDef.id, position: enemyDef.position, ownerId: enemyDef.ownerId }
          },
          options: []
        };
      }
    }
  }
  return { intent: "none", options: [] };
}

export function resolveTargetBehavior(target: MapSelection, ctx: TargetRuleContext): HoverBehavior {
  const L = (ar: string, en: string) => (ctx.isAr ? ar : en);

  if (target.kind === "oasis") {
    const o = ctx.oases.find(x => x.id === target.id);
    if (!o || (o.burnedRemainingSeconds ?? 0) > 0) return { intent: "none", options: [] };
    if (o.ownerId && o.ownerId !== ctx.you && ctx.isAlly(o.ownerId)) {
      return {
        intent: "ally",
        directAction: { id: "reinforce", label: L("تعزيز", "Reinforce"), icon: "shield", tone: "friendly" },
        options: []
      };
    }
    if (o.ownerId === ctx.you) {
      const options: RadialOption[] = [
        { id: "reinforce", label: L("تعزيز", "Reinforce"), icon: "shield", tone: "friendly" },
        { id: "camp", label: L("معسكر", "Camp"), icon: "tent", tone: "default" }
      ];
      if (ctx.ownedVillagesCount > 1) {
        options.push({ id: "capture-oasis", label: L("نقل", "Transfer"), icon: "transfer", tone: "warn" });
      }
      return { intent: "ally", options };
    }
    const enemyBlocked = !!o.ownerId && ctx.protectedEnemy;
    const options: RadialOption[] = [
      { id: "capture-oasis", label: L("احتلال", "Capture"), icon: "swords", tone: "danger", disabled: enemyBlocked },
      ...(o.ownerId ? [{ id: "besiege-oasis" as const, label: L("حصار", "Besiege"), icon: "tower" as const, tone: "warn" as const, disabled: enemyBlocked }] : []),
      { id: "scout", label: L("كشافة", "Scout"), icon: "eye", tone: "default" },
      { id: "camp", label: L("معسكر", "Camp"), icon: "tent", tone: "default" },
      { id: "burn-oasis", label: L("حرق", "Burn"), icon: "flame", tone: "danger", disabled: enemyBlocked }
    ];
    return { intent: "attack", options };
  }

  if (target.kind === "village") {
    const v = ctx.settlements.find(x => x.id === target.id);
    if (!v || v.ruined) return { intent: "none", options: [] };
    if (v.ownerId === ctx.you) {
      const onlySameHome = ctx.selectedIds.length > 0 && ctx.selectedIds.every(id => id === `home:${v.id}`);
      if (onlySameHome) return { intent: "none", options: [] };
      if (ctx.hasFieldArmies) {
        if (!(ctx.hasFighters ?? true)) {
          return {
            intent: "ally",
            directAction: { id: "recall", label: L("العودة للقرية", "Return to village"), icon: "home", tone: "friendly" },
            options: []
          };
        }
        return {
          intent: "ally",
          options: [
            { id: "recall", label: L("العودة للقرية", "Return to village"), icon: "home", tone: "friendly" },
            { id: "reinforce", label: L("تعزيز", "Reinforce"), icon: "shield", tone: "friendly" }
          ]
        };
      }
      return {
        intent: "ally",
        directAction: { id: "reinforce", label: L("تعزيز", "Reinforce"), icon: "shield", tone: "friendly" },
        options: []
      };
    }
    if (ctx.isAlly(v.ownerId)) {
      return {
        intent: "ally",
        directAction: { id: "reinforce", label: L("تعزيز", "Reinforce"), icon: "shield", tone: "friendly" },
        options: []
      };
    }
    const activeAlliedSiege = (ctx.sieges ?? []).find(
      s => s.villageId === v.id && s.status === "Active" && ctx.isAlly(s.attackerOwner)
    );
    if (activeAlliedSiege) {
      return {
        intent: "ally",
        directAction: siegeJoinOption(activeAlliedSiege.objective, v, ctx, L),
        options: []
      };
    }
    const canConquer = ctx.hasGovernor || allyHasObjective(v.id, "Conquest", ctx);
    const canRaze = ctx.hasCatapult || allyHasObjective(v.id, "Raze", ctx);
    const options: RadialOption[] = [
      { id: "raid", label: L("غارة", "Raid"), icon: "swords", tone: "danger", disabled: ctx.protectedEnemy },
      { id: "conquest", label: L("فتح", "Conquer"), icon: "flag", tone: "danger", disabled: ctx.protectedEnemy || !canConquer },
      { id: "raze", label: L("تدمير", "Raze"), icon: "tower", tone: "danger", disabled: ctx.protectedEnemy || !canRaze },
      { id: "blockade-city", label: L("حصار", "Blockade"), icon: "blockade", tone: "warn", disabled: ctx.protectedEnemy },
      { id: "scout", label: L("كشافة", "Scout"), icon: "eye", tone: "default" }
    ];
    return { intent: "attack", options };
  }

  if (target.kind === "army") {
    const a = ctx.armies.find(x => x.id === target.id);
    if (!a) return { intent: "none", options: [] };
    if (ctx.isAlly(a.ownerId)) return resolveAllyArmyJoinBehavior(a, ctx, L);
    if (a.ownerId === ctx.you) {
      if (!ctx.selectedIds.includes(a.id)) {
        const inActiveBattle = (ctx.battles ?? []).some(
          b => b.status === "Active" && (b.attackerArmyIds.includes(a.id) || b.defenderArmyIds.includes(a.id))
        );
        const inActiveSiege = (ctx.sieges ?? []).some(s => s.status === "Active" && s.armyIds.includes(a.id))
          || (ctx.oasisSieges ?? []).some(s => s.armyIds.includes(a.id));
        if (inActiveBattle || inActiveSiege) return resolveAllyArmyJoinBehavior(a, ctx, L);
      }
      return { intent: "none", options: [] };
    }
    const attackBlocked = ctx.protectedEnemy || !canAttackArmyInBattle(a.id, ctx);
    return {
      intent: "attack",
      options: [
        { id: "attack-army", label: L("هجوم", "Attack"), icon: "swords", tone: "danger", disabled: attackBlocked },
        { id: "scout", label: L("كشافة", "Scout"), icon: "eye", tone: "default" }
      ]
    };
  }

  if (target.kind === "battle") {
    return resolveBattleTargetBehavior(target.id, ctx, L);
  }

  if (target.kind === "convoy") {
    const c = ctx.convoys.find(x => x.id === target.id);
    if (!c || c.ownerId === ctx.you || ctx.isAlly(c.ownerId) || c.inBattle) return { intent: "none", options: [] };
    return {
      intent: "attack",
      directAction: { id: "intercept-convoy", label: L("اعتراض", "Intercept"), icon: "swords", tone: "danger", disabled: ctx.protectedEnemy },
      options: []
    };
  }

  if (target.kind === "castle") {
    const ctrl = ctx.castle?.controllerId;
    const isFriendly = !!ctrl && (ctrl === ctx.you || ctx.isAlly(ctrl));
    const options: RadialOption[] = [];
    if (ctx.castle?.active) {
      options.push({ id: "capture-castle", label: L("هجوم / احتلال", "Attack / Capture"), icon: "crown", tone: isFriendly ? "friendly" : "danger" });
    }
    options.push(
      { id: "move", label: L("تحرك", "Move"), icon: "move", tone: "default" },
      { id: "camp", label: L("معسكر", "Camp"), icon: "tent", tone: "default" }
    );
    return { intent: ctx.castle?.active ? (isFriendly ? "ally" : "attack") : "move", options };
  }

  if (target.kind === "ground") {
    return {
      intent: "move",
      options: [
        { id: "move", label: L("تحرك", "Move"), icon: "move", tone: "default" },
        { id: "camp", label: L("معسكر", "Camp"), icon: "tent", tone: "default" }
      ]
    };
  }

  return { intent: "none", options: [] };
}

export function renderCursorBadge(el: HTMLElement, intent: HoverBehavior["intent"], client: Point | null) {
  if (!client || intent === "none") {
    el.hidden = true;
    return;
  }
  el.hidden = false;
  el.dataset.intent = intent;
  el.style.transform = `translate(${Math.round(client.x + 12)}px, ${Math.round(client.y - 36)}px)`;
  el.innerHTML = intent === "attack" ? CROSSED_SWORDS_SVG : intent === "ally" ? GREEN_SHIELD_SVG : MOVE_RETICLE_SVG;
}

export function renderRadialMenu(
  host: HTMLElement,
  anchor: Point,
  options: RadialOption[],
  escape: (s: string) => string,
  compact = false
) {
  if (!options.length) {
    host.hidden = true;
    host.innerHTML = "";
    return;
  }
  const marginX = compact ? 86 : 135;
  const minY = compact ? 96 : 150;
  const maxBottomPad = compact ? 40 : 55;
  const x = Math.max(marginX, Math.min(window.innerWidth - marginX, anchor.x));
  const y = Math.max(minY, Math.min(window.innerHeight - maxBottomPad, anchor.y));
  const radius = compact
    ? (options.length >= 5 ? 74 : options.length >= 3 ? 66 : 56)
    : (options.length >= 5 ? 108 : options.length >= 3 ? 96 : 84);
  const startDeg = options.length === 2 ? -130 : -162;
  const endDeg = options.length === 2 ? -50 : -18;

  const buttons = options.map((opt, idx) => {
    const deg = options.length === 1 ? -90 : startDeg + ((endDeg - startDeg) * idx) / (options.length - 1);
    const rad = (deg * Math.PI) / 180;
    const bx = Math.round(Math.cos(rad) * radius);
    const by = Math.round(Math.sin(rad) * radius);
    const toneClass = opt.tone === "default" ? "" : opt.tone;
    return `<button type="button" class="radial-action ${toneClass}" data-action="radial-cmd" data-id="${opt.id}" style="left:${bx}px;top:${by}px" ${opt.disabled ? "disabled" : ""} aria-label="${escape(opt.label)}">
      <span class="radial-action-label">${escape(opt.label)}</span>
      <span class="radial-action-icon">${RADIAL_ICONS[opt.icon]}</span>
    </button>`;
  }).join("");

  host.hidden = false;
  host.style.left = `${Math.round(x)}px`;
  host.style.top = `${Math.round(y)}px`;
  host.innerHTML = `<div class="radial-arc-ring" aria-hidden="true"></div><div class="radial-center-dot" aria-hidden="true"></div>${buttons}`;
}
