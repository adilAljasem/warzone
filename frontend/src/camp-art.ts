import type { Graphics } from 'pixi.js';

const strokeOutline = { color: 0x423321, width: 1.2 };

// Draws a classic conical / octagonal commander pavilion with roof stripes and flag.
function drawPavilion(g: Graphics, x: number, y: number, color: number) {
    // Ground shadow
    g.ellipse(x, y + 3, 24, 8).fill({ color: 0x18140e, alpha: 0.35 });

    const peakY = y - 36, wallTopY = y - 15;
    // Base cylinder / wall
    g.poly([x - 18, wallTopY, x + 18, wallTopY, x + 18, y, x - 18, y]).fill(0xd9c59f).stroke(strokeOutline);
    // Wall shading
    g.poly([x, wallTopY, x + 18, wallTopY, x + 18, y, x, y]).fill({ color: 0x22180f, alpha: 0.12 });

    // Open entrance doorway
    g.poly([x - 4, y, x, y - 11, x + 4, y]).fill(0x28190e);
    // Door flap accents
    g.poly([x - 7, y, x - 4, y, x, y - 11]).fill(0xedd9b5);
    g.poly([x + 4, y, x + 7, y, x, y - 11]).fill(0xceb68e);

    // Roof: sunny left slope
    g.poly([x - 20, wallTopY, x, peakY, x, wallTopY]).fill(0xf4e6cb).stroke(strokeOutline);
    // Roof: center facet
    g.poly([x - 7, wallTopY, x, peakY, x + 7, wallTopY]).fill(0xebd8b5).stroke(strokeOutline);
    // Roof: shaded right slope
    g.poly([x, wallTopY, x, peakY, x + 20, wallTopY]).fill(0xd3be96).stroke(strokeOutline);

    // Decorative player-colored eaves band
    g.poly([x - 20, wallTopY - 2, x + 20, wallTopY - 2, x + 20, wallTopY + 2, x - 20, wallTopY + 2]).fill(color);

    // Center spire & fluttering pennant
    g.moveTo(x, peakY).lineTo(x, peakY - 11).stroke({ color: 0x5a432b, width: 1.6 });
    g.poly([x, peakY - 11, x + 12, peakY - 8, x, peakY - 5]).fill(color).stroke({ color: 0x3d2b1a, width: 0.8 });

    // Guy ropes to ground pegs
    g.moveTo(x - 19, wallTopY).lineTo(x - 25, y + 2).stroke({ color: 0x7a6344, width: 0.8 });
    g.moveTo(x + 19, wallTopY).lineTo(x + 25, y + 2).stroke({ color: 0x7a6344, width: 0.8 });
}

// Draws an A-frame military wedge tent facing three-quarter isometric.
function drawWedgeTent(g: Graphics, x: number, y: number, color: number, scale = 1) {
    const w = 15 * scale, h = 18 * scale, d = 16 * scale;
    // Ground shadow
    g.ellipse(x + d * 0.4, y + 2, w + d * 0.5, 6 * scale).fill({ color: 0x18140e, alpha: 0.32 });

    const peakX = x, peakY = y - h;
    const ridgeBackX = peakX + d, ridgeBackY = peakY - d * 0.4;
    const backEavesX = x + w + d, backEavesY = y - d * 0.4;

    // Side roof plane stretching into depth
    g.poly([peakX, peakY, ridgeBackX, ridgeBackY, backEavesX, backEavesY, x + w, y]).fill(0xdac499).stroke(strokeOutline);

    // Player colored stripe along the roof side
    g.poly([
        peakX + (x + w - peakX) * 0.5, peakY + h * 0.5,
        ridgeBackX + (backEavesX - ridgeBackX) * 0.5, ridgeBackY + h * 0.5,
        ridgeBackX + (backEavesX - ridgeBackX) * 0.7, ridgeBackY + h * 0.7,
        peakX + (x + w - peakX) * 0.7, peakY + h * 0.7
    ]).fill(color);

    // Front triangle opening
    g.poly([x - w, y, peakX, peakY, x + w, y]).fill(0x28190e).stroke(strokeOutline);

    // Front flaps (partially parted)
    g.poly([x - w, y, peakX, peakY, x - w * 0.2, y]).fill(0xf4e6cb).stroke(strokeOutline);
    g.poly([x + w, y, peakX, peakY, x + w * 0.3, y]).fill(0xcbb48b).stroke(strokeOutline);

    // Wooden ridge pole tip
    g.moveTo(peakX, peakY).lineTo(peakX - 2 * scale, peakY - 3 * scale).stroke({ color: 0x5a432b, width: 1.4 });

    // Guy ropes
    g.moveTo(peakX, peakY).lineTo(x - w - 6 * scale, y + 2).stroke({ color: 0x7a6344, width: 0.8 });
    g.moveTo(ridgeBackX, ridgeBackY).lineTo(backEavesX + 6 * scale, backEavesY + 2).stroke({ color: 0x7a6344, width: 0.8 });
}

// Draws a small cozy campfire with stones and glowing embers.
function drawCampfire(g: Graphics, x: number, y: number) {
    // Firepit ash
    g.ellipse(x, y, 9, 5).fill({ color: 0x2e251a, alpha: 0.6 });
    // Ring of stones
    const stones: [number, number][] = [[-7, 0], [-5, -3], [0, -4], [5, -3], [7, 0], [5, 3], [0, 4], [-5, 3]];
    for (const [sx, sy] of stones) {
        g.circle(x + sx, y + sy, 1.8).fill(0x766d62).stroke({ color: 0x3d352b, width: 0.6 });
    }
    // Burning logs
    g.moveTo(x - 4, y + 2).lineTo(x + 4, y - 2).stroke({ color: 0x422a16, width: 1.5 });
    g.moveTo(x - 3, y - 2).lineTo(x + 3, y + 2).stroke({ color: 0x422a16, width: 1.5 });
    // Glowing embers and flame
    g.circle(x, y - 1, 3.2).fill({ color: 0xed6823, alpha: 0.85 });
    g.circle(x, y - 2, 1.8).fill({ color: 0xffda59, alpha: 0.95 });
}

// Complete camp perimeter and encampment with commander pavilion, soldier tents and campfire.
export function drawCamp(g: Graphics, x: number, y: number, radius: number, color: number) {
    const ry = radius * 0.65;

    // Treaded campsite clearing
    g.ellipse(x, y, radius * 0.92, ry * 0.92).fill({ color: 0x6e5837, alpha: 0.16 });
    g.ellipse(x, y, radius * 0.72, ry * 0.72).fill({ color: 0x8a724a, alpha: 0.12 });

    // Public perimeter ellipse
    g.ellipse(x, y, radius, ry).fill({ color, alpha: 0.06 })
     .ellipse(x, y, radius, ry).stroke({ color, width: 2.8, alpha: 0.9 });

    // Decorative corner markers / perimeter stakes
    for (const deg of [0, 60, 120, 180, 240, 300]) {
        const rad = deg * Math.PI / 180;
        const px = x + Math.cos(rad) * radius, py = y + Math.sin(rad) * ry;
        g.circle(px, py, 2.2).fill(0xd8c28a).stroke({ color: 0x423321, width: 1 });
    }

    // Tents placed neatly in the upper arc so the center and front are clear for the soldiers
    drawPavilion(g, x - 44, y - 16, color);
    drawWedgeTent(g, x + 38, y - 18, color, 1.05);
    drawWedgeTent(g, x - 4, y - 30, color, 0.88);

    // Campfire off to the front-left
    drawCampfire(g, x - 34, y + 16);
}
