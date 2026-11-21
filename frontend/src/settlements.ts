import { Container, Graphics, Sprite } from 'pixi.js';
import { buildingSprite, terrainSprite } from './art';

export const settlementStage = (level: number) => level >= 12 ? 'City' : level >= 6 ? 'Town' : 'Village';

export interface SettlementBuildingSlot {
  building: string;
  x: number;
  y: number;
  width: number;
  unlockLevel: number;
}

const MILESTONE_SLOTS: readonly SettlementBuildingSlot[] = [
  { building: 'Barracks', x: -26, y: -34, width: 50, unlockLevel: 5 },
  { building: 'Stable', x: 28, y: -34, width: 50, unlockLevel: 10 },
  { building: 'Smithy', x: -66, y: -2, width: 52, unlockLevel: 15 },
  { building: 'Hospital', x: 66, y: -3, width: 52, unlockLevel: 20 },
  { building: 'SiegeWorkshop', x: -56, y: 28, width: 50, unlockLevel: 25 },
  { building: 'Warehouse', x: 57, y: 27, width: 52, unlockLevel: 30 }
];

/** Returns one additional exterior building for every 5 levels of the Main Hall (levels 5, 10, 15, 20, 25, 30). */
export function settlementMilestoneBuildings(level: number): SettlementBuildingSlot[] {
  const count = Math.min(MILESTONE_SLOTS.length, Math.floor(Math.max(0, level) / 5));
  return MILESTONE_SLOTS.slice(0, count);
}

function cottage(tribe: string, x: number, y: number, size = 1) {
  const sprite = buildingSprite('Warehouse', tribe, 62 * size);
  sprite.position.set(x, y);
  return sprite;
}

export function settlementArt(tribe: string, level: number, own: boolean) {
  const root = new Container();
  const stage = settlementStage(level);
  const milestones = settlementMilestoneBuildings(level);
  const baseRadius = stage === 'Village' ? 45 : stage === 'Town' ? 58 : 71;
  const radius = baseRadius + milestones.length * 4;

  root.addChild(
    new Graphics()
      .ellipse(0, 7, radius + 10, radius * .46).fill({ color: 0x99895e, alpha: .12 })
      .ellipse(0, 9, radius, radius * .42).stroke({ color: own ? 0xe5d292 : 0xb8ae88, width: 1.5, alpha: own ? .65 : .12 })
  );

  const pieces: { sprite: Sprite; y: number }[] = [];
  const push = (sprite: Sprite) => pieces.push({ sprite, y: sprite.y });

  for (const slot of milestones) {
    const sprite = buildingSprite(slot.building, tribe, slot.width);
    sprite.position.set(slot.x, slot.y);
    push(sprite);
  }

  if (stage === 'Village') {
    push(cottage(tribe, -17, -7, .9));
    push(cottage(tribe, 23, 10, .7));
    push(cottage(tribe, -27, 22, .6));
    push(terrainSprite(6, 39, 38, 22));
  } else {
    const hall = buildingSprite('MainHall', tribe, stage === 'Town' ? 100 : 123);
    hall.position.set(0, -3);
    push(hall);
    push(cottage(tribe, -44, 15, .75));
    push(cottage(tribe, 44, 12, .72));
    if (stage === 'City') {
      push(cottage(tribe, -47, -17, .65));
      push(cottage(tribe, 45, -19, .65));
      const wall = buildingSprite('Wall', tribe, 99);
      wall.position.set(1, 34);
      push(wall);
    } else {
      push(terrainSprite(6, 43, 41, 28));
    }
  }

  pieces.sort((a, b) => a.y - b.y);
  for (const piece of pieces) root.addChild(piece.sprite);
  return root;
}
