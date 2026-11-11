import type { Graphics } from 'pixi.js';

const outline = { color: 0x4a3b28, width: 1.2 };

// A canvas tent in three-quarter view, standing on (x, y), with a pennant in the owner's colour.
export function drawTent(g: Graphics, x: number, y: number, size: number, color: number) {
    const apex = { x: x - size * .2, y: y - size }, ridge = { x: x + size * .55, y: y - size * .85 };
    g.ellipse(x + size * .2, y + 1, size * .95, size * .28).fill({ color: 0x1d160c, alpha: .28 });
    g.poly([apex.x, apex.y, ridge.x, ridge.y, x + size * .95, y - size * .08, x + size * .2, y]).fill(0xb89a63).stroke(outline);
    g.poly([x - size * .6, y, apex.x, apex.y, x + size * .2, y]).fill(0xe2cb97).stroke(outline);
    g.poly([apex.x - size * .14, y, apex.x, y - size * .55, apex.x + size * .14, y]).fill(0x4a3420);
    g.moveTo(x + size * .2, y).lineTo(x + size * .95, y - size * .08).stroke({ color, width: 2.5 });
    g.moveTo(apex.x, apex.y).lineTo(apex.x, apex.y - size * .4).stroke({ color: 0x5b4730, width: 1.4 });
    g.poly([apex.x, apex.y - size * .4, apex.x + size * .32, apex.y - size * .32, apex.x, apex.y - size * .24]).fill(color);
}

// The camp perimeter with tents pitched along its rear half, leaving the centre free for the troops.
export function drawCamp(g: Graphics, x: number, y: number, radius: number, color: number) {
    const ry = radius * .65;
    g.ellipse(x, y, radius, ry).fill({ color, alpha: .07 }).ellipse(x, y, radius, ry).stroke({ color, width: 3, alpha: .9 });
    g.ellipse(x, y, radius * .45, ry * .45).fill({ color: 0x8a6a3e, alpha: .18 });
    for (const degrees of [200, 235, 270, 305, 340]) {
        const angle = degrees * Math.PI / 180;
        drawTent(g, x + Math.cos(angle) * radius * .72, y + Math.sin(angle) * ry * .72 + 6, 20, color);
    }
}
