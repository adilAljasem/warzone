import { Container, Graphics } from 'pixi.js';

function drawSword(blade: number, guard: number): Graphics {
  return new Graphics()
    .roundRect(-2.5, -24, 5, 30, 2).fill(blade).stroke({ color: 0x3b3532, width: 1 })
    .rect(-8, 4, 16, 4).fill(guard)
    .rect(-2, 8, 4, 9).fill(0x5d3d28)
    .circle(0, 18, 2.5).fill(guard);
}

/** Draws animated crossed steel swords with per-army red attacker-swords / defender-shield badges. */
export class BattleClash {
  readonly root = new Container();
  private readonly left = drawSword(0xf1f4f8, 0xd8b15f);
  private readonly right = drawSword(0xe3e8ef, 0xc79d4b);
  private readonly sparks = new Graphics();
  private readonly badgesLayer = new Container();
  private readonly attackerBadges: Container[] = [];
  private readonly defenderBadges: Container[] = [];

  constructor() {
    this.left.position.set(-4, 2);
    this.right.position.set(4, 2);
    this.left.rotation = -.62;
    this.right.rotation = .62;
    this.root.addChild(this.left, this.right, this.sparks, this.badgesLayer);
    this.root.eventMode = 'none';
  }

  /** Positions small red crossed swords in front of every attacking army and a red shield in front of every defending army. */
  setRoleBadges(attackerWorldPoints: { x: number; y: number }[], defenderWorldPoints: { x: number; y: number }[]) {
    while (this.attackerBadges.length < attackerWorldPoints.length) {
      const badge = this.drawSmallRedSwords();
      this.attackerBadges.push(badge);
      this.badgesLayer.addChild(badge);
    }
    for (let i = 0; i < this.attackerBadges.length; i++) {
      const badge = this.attackerBadges[i]!;
      const pt = attackerWorldPoints[i];
      if (!pt) {
        badge.visible = false;
      } else {
        badge.visible = true;
        badge.position.set(pt.x - this.root.x, pt.y - this.root.y);
      }
    }

    while (this.defenderBadges.length < defenderWorldPoints.length) {
      const badge = this.drawRedShield();
      this.defenderBadges.push(badge);
      this.badgesLayer.addChild(badge);
    }
    for (let i = 0; i < this.defenderBadges.length; i++) {
      const badge = this.defenderBadges[i]!;
      const pt = defenderWorldPoints[i];
      if (!pt) {
        badge.visible = false;
      } else {
        badge.visible = true;
        badge.position.set(pt.x - this.root.x, pt.y - this.root.y);
      }
    }
  }

  /** Fallback single-side helper for tests or 1v1 positioning. */
  setSides(attackerSide?: -1 | 1) {
    if (!attackerSide) {
      this.setRoleBadges([], []);
      return;
    }
    this.setRoleBadges(
      [{ x: this.root.x + attackerSide * 32, y: this.root.y + 2 }],
      [{ x: this.root.x - attackerSide * 32, y: this.root.y + 2 }]
    );
  }

  private drawSmallRedSwords(): Container {
    const group = new Container();
    for (const dir of [-1, 1] as const) {
      const g = new Graphics()
        .moveTo(0, 7.5).lineTo(0, -9).stroke({ color: 0x2a0707, width: 4.2, cap: 'round' })
        .moveTo(0, 7.5).lineTo(0, -9).stroke({ color: 0xff3b30, width: 2.6, cap: 'round' })
        .moveTo(0, 6).lineTo(0, -8.5).stroke({ color: 0xffc1ba, width: 0.9, cap: 'round' })
        .rect(-4.2, 2.5, 8.4, 2.2).fill(0x8b1414).stroke({ color: 0x2a0707, width: 0.9 })
        .circle(0, 8, 1.7).fill(0xff3b30).stroke({ color: 0x2a0707, width: 0.8 });
      g.rotation = dir * 0.62;
      group.addChild(g);
    }
    group.visible = false;
    return group;
  }

  private drawRedShield(): Container {
    const group = new Container();
    const g = new Graphics()
      .moveTo(-7, -7.5)
      .lineTo(7, -7.5)
      .lineTo(7, -0.5)
      .quadraticCurveTo(7, 6, 0, 9.5)
      .quadraticCurveTo(-7, 6, -7, -0.5)
      .closePath()
      .fill(0xe02828)
      .stroke({ color: 0x2a0707, width: 2.2 })
      .moveTo(-6, -6.5)
      .lineTo(6, -6.5)
      .lineTo(6, -0.5)
      .quadraticCurveTo(6, 5, 0, 8.2)
      .quadraticCurveTo(-6, 5, -6, -0.5)
      .closePath()
      .stroke({ color: 0xffb3ad, width: 1.2 })
      .moveTo(0, -4.5)
      .lineTo(0, 5.5)
      .moveTo(-3.5, -1)
      .lineTo(3.5, -1)
      .stroke({ color: 0x7c1010, width: 1.4 });
    group.addChild(g);
    group.visible = false;
    return group;
  }

  animate(now: number, reduced: boolean) {
    if (this.root.destroyed) return;
    if (reduced) { this.sparks.clear(); return; }
    const swing = Math.sin(now / 150) * .16;
    this.left.rotation = -.62 + swing;
    this.right.rotation = .62 - swing;
    this.sparks.clear();
    const flash = Math.abs(Math.sin(now / 150));
    if (flash > .75) {
      for (let i = 0; i < 4; i++) {
        const a = (i / 4) * Math.PI * 2 + now / 180;
        this.sparks.moveTo(Math.cos(a) * 4, Math.sin(a) * 4 - 4).lineTo(Math.cos(a) * 11, Math.sin(a) * 11 - 4).stroke({ color: 0xfff0a5, width: 1.5, alpha: (flash - .75) * 4 });
      }
    }
  }

  destroy() { if (!this.root.destroyed) this.root.destroy({ children: true }); }
}
