import type { OasisView, Point, SettlementView, ArmyView, ConvoyView, WarfareView, BattleView, MapSelection } from "./map";

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
    const options: RadialOption[] = [
      { id: "raid", label: L("غارة", "Raid"), icon: "swords", tone: "danger", disabled: ctx.protectedEnemy },
      { id: "conquest", label: L("فتح", "Conquer"), icon: "flag", tone: "danger", disabled: ctx.protectedEnemy || !ctx.hasGovernor },
      { id: "raze", label: L("تدمير", "Raze"), icon: "tower", tone: "danger", disabled: ctx.protectedEnemy || !ctx.hasCatapult },
      { id: "blockade-city", label: L("حصار", "Blockade"), icon: "blockade", tone: "warn", disabled: ctx.protectedEnemy },
      { id: "scout", label: L("كشافة", "Scout"), icon: "eye", tone: "default" }
    ];
    return { intent: "attack", options };
  }

  if (target.kind === "army") {
    const a = ctx.armies.find(x => x.id === target.id);
    if (!a || a.ownerId === ctx.you || ctx.isAlly(a.ownerId)) return { intent: "none", options: [] };
    const attackBlocked = ctx.protectedEnemy || !canAttackArmyInBattle(a.id, ctx);
    return {
      intent: "attack",
      options: [
        { id: "attack-army", label: L("هجوم", "Attack"), icon: "swords", tone: "danger", disabled: attackBlocked },
        { id: "scout", label: L("كشافة", "Scout"), icon: "eye", tone: "default" }
      ]
    };
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
  escape: (s: string) => string
) {
  if (!options.length) {
    host.hidden = true;
    host.innerHTML = "";
    return;
  }
  const marginX = 135;
  const x = Math.max(marginX, Math.min(window.innerWidth - marginX, anchor.x));
  const y = Math.max(150, Math.min(window.innerHeight - 55, anchor.y));
  const radius = options.length >= 5 ? 108 : options.length >= 3 ? 96 : 84;
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
