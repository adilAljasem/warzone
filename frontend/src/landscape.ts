import { Container, Graphics, Sprite, Text, Texture, TilingSprite } from 'pixi.js';
import { t } from './locale';
import { terrainSprite, groundTexture } from './art';
export interface BiomeRegion { id: number; kind: string; center: {x:number;y:number}; boundary:{x:number;y:number}[]; wood:number; iron:number; stone:number; wheat:number }

export function biomeAt(regions: BiomeRegion[], point: {x:number;y:number}) {
    return regions.find(r=>{let inside=false;for(let i=0,j=r.boundary.length-1;i<r.boundary.length;j=i++){const a=r.boundary[i]!,b=r.boundary[j]!;if((a.y>point.y)!==(b.y>point.y)&&point.x<(b.x-a.x)*(point.y-a.y)/(b.y-a.y)+a.x)inside=!inside;}return inside;})??[...regions].sort((a,b)=>Math.hypot(a.center.x-point.x,a.center.y-point.y)-Math.hypot(b.center.x-point.x,b.center.y-point.y))[0];
}
type Point = { x: number; y: number };
export interface Landscape {
    animate(time: number): void;
    setDetail(scale: number): void;
    setBoundaries(visible: boolean): void;
    destroy(): void;
}
export function riverX(y: number) { return 2205 + Math.sin(y / 480) * 225 + Math.sin(y / 1012) * 90; }
const riverWidth = (y: number) => 26 + Math.sin(y / 225) * 5 + Math.sin(y / 100) * 2;
const bridgeYs = [600, 1470, 2325];
const project = (p: Point): Point => ({ x: p.x, y: p.y * .65 });
const distance = (a: Point, b: Point) => Math.hypot(a.x - b.x, a.y - b.y);

// Route roads along each bank and cross only at a bridge. Roads remain decorative.
function roadRoute(a: Point, b: Point): Point[] {
    const sideA = Math.sign(a.x - riverX(a.y)) || 1;
    const sideB = Math.sign(b.x - riverX(b.y)) || 1;
    const approach = (from: Point, to: Point, side: number) => {
        const steps = Math.max(2, Math.ceil(distance(from, to) / 24));
        return Array.from({ length: steps + 1 }, (_, i) => {
            const t = i / steps, y = from.y + (to.y - from.y) * t;
            const offset = (from.x - riverX(from.y)) * (1 - t) + (to.x - riverX(to.y)) * t;
            return { x: riverX(y) + side * Math.max(riverWidth(y) + 22, Math.abs(offset)), y };
        });
    };
    if (sideA === sideB) return [a, ...approach(a, b, sideA), b];
    const bridgeY = [...bridgeYs].sort((y1, y2) => {
        const cost = (y: number) => distance(a, { x: riverX(y) + sideA * 66, y }) + distance(b, { x: riverX(y) + sideB * 66, y });
        return cost(y1) - cost(y2);
    })[0]!;
    const near = { x: riverX(bridgeY) + sideA * 66, y: bridgeY };
    const far = { x: riverX(bridgeY) + sideB * 66, y: bridgeY };
    return [a, ...approach(a, near, sideA), far, ...approach(far, b, sideB), b];
}

export function drawLandscape(root: Container, regions: BiomeRegion[], settlements: Point[], oases: Point[] = []): Landscape {
    root.removeChildren().forEach(c => c.destroy({ children: true }));
    let seed = regions.reduce((s, r) => s + Math.floor(r.center.x + r.center.y), 43);
    const random = () => { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; };
    const kindAt = (x: number, y: number) => biomeAt(regions, { x, y: y / .65 })?.kind;
    const palette: Record<string, number> = { Woodland: 0x536e49, Highlands: 0x939183, Grassland: 0xa8b77c, Desert: 0xdac18b };
    const WORLD_LEFT = -6966, WORLD_TOP = -4527, TERRAIN_SCALE = 4.5, TERRAIN_W = 4096, TERRAIN_H = 2662;
    const WORLD_W = TERRAIN_W * TERRAIN_SCALE, WORLD_H = TERRAIN_H * TERRAIN_SCALE;
    const ownedTextures: Texture[] = [];
    const base = new TilingSprite({ texture: groundTexture(0), width: WORLD_W, height: WORLD_H });
    base.position.set(WORLD_LEFT, WORLD_TOP); base.tileScale.set(.6); base.tint = 0xd6dfbc; root.addChild(base);
    // Bake the feathered textures once within the universal 4096px GPU texture limit while
    // expanding coverage across the wider surrounding horizon.
    const terrainCanvas = document.createElement('canvas');
    terrainCanvas.width = TERRAIN_W; terrainCanvas.height = TERRAIN_H;
    const terrainContext = terrainCanvas.getContext('2d')!;
    const layerCanvas = document.createElement('canvas');
    layerCanvas.width = terrainCanvas.width; layerCanvas.height = terrainCanvas.height;
    const layerContext = layerCanvas.getContext('2d')!;
    const maskCanvas = document.createElement('canvas');
    maskCanvas.width = terrainCanvas.width; maskCanvas.height = terrainCanvas.height;
    const maskContext = maskCanvas.getContext('2d')!;
    for (const region of regions) {
        const texture = groundTexture(region.kind === 'Desert' ? 3 : region.kind === 'Highlands' ? 2 : 0);
        const frame = texture.frame, tile = document.createElement('canvas');
        tile.width = Math.max(64, Math.round(frame.width * .14)); tile.height = Math.max(64, Math.round(frame.height * .14));
        tile.getContext('2d')!.drawImage(texture.source.resource as CanvasImageSource, frame.x, frame.y, frame.width, frame.height, 0, 0, tile.width, tile.height);
        layerContext.clearRect(0, 0, terrainCanvas.width, terrainCanvas.height);
        layerContext.fillStyle = layerContext.createPattern(tile, 'repeat')!;
        layerContext.fillRect(0, 0, terrainCanvas.width, terrainCanvas.height);
        layerContext.globalAlpha = region.kind === 'Woodland' ? .36 : .18;
        layerContext.fillStyle = '#' + (palette[region.kind] ?? 0xa8b77c).toString(16).padStart(6, '0');
        layerContext.fillRect(0, 0, terrainCanvas.width, terrainCanvas.height); layerContext.globalAlpha = 1;
        maskContext.clearRect(0, 0, terrainCanvas.width, terrainCanvas.height);
        maskContext.filter = 'blur(12px)'; maskContext.fillStyle = '#ffffff'; maskContext.beginPath();
        region.boundary.forEach((p, i) => {
            const wx = p.x <= 46 ? WORLD_LEFT : p.x >= 4454 ? WORLD_LEFT + WORLD_W : p.x;
            const wy = p.y <= 46 ? WORLD_TOP : p.y >= 4454 ? WORLD_TOP + WORLD_H : p.y * .65;
            const x = (wx - WORLD_LEFT) / TERRAIN_SCALE;
            const y = (wy - WORLD_TOP) / TERRAIN_SCALE;
            if (i) maskContext.lineTo(x, y); else maskContext.moveTo(x, y);
        });
        maskContext.closePath(); maskContext.fill();
        layerContext.globalCompositeOperation = 'destination-in'; layerContext.drawImage(maskCanvas, 0, 0);
        layerContext.globalCompositeOperation = 'source-over'; terrainContext.drawImage(layerCanvas, 0, 0);
    }
    const terrainTexture = Texture.from(terrainCanvas); ownedTextures.push(terrainTexture);
    const terrain = new Sprite(terrainTexture); terrain.position.set(WORLD_LEFT, WORLD_TOP); terrain.scale.set(TERRAIN_SCALE); root.addChild(terrain);
    layerCanvas.width = maskCanvas.width = 1;

    const ground = new Graphics(), details = new Graphics(); root.addChild(ground, details);
    // Low relief and ground cover establish biome identity without looking collectible.
    for (let i = 0; i < 5600; i++) {
        const x = -2200 + random() * 8900, y = -1500 + random() * 5925, kind = kindAt(x, y);
        if (kind === 'Desert') {
            const w = 18 + random() * 62, h = 4 + random() * 11;
            ground.moveTo(x - w, y).quadraticCurveTo(x, y - h * 2, x + w, y + h)
                .quadraticCurveTo(x, y - h, x - w, y).fill({ color: 0xb49968, alpha: .09 });
            details.moveTo(x - w * .8, y - 1).quadraticCurveTo(x, y - h * 1.5, x + w * .8, y + h * .6)
                .stroke({ color: 0xffe5ad, alpha: .22, width: 1.4 });
        } else if (kind === 'Highlands') {
            ground.ellipse(x, y, 18 + random() * 35, 6 + random() * 12).fill({ color: 0x666b5c, alpha: .07 });
            details.poly([x - 4, y, x - 2, y - 4, x + 3, y - 3, x + 5, y + 1]).fill({ color: 0x646e60, alpha: .22 });
        } else {
            ground.ellipse(x, y, 12 + random() * 30, 4 + random() * 10).fill({ color: kind === 'Woodland' ? 0x3d603c : 0xd3ce92, alpha: .07 });
            details.moveTo(x - 3, y).lineTo(x - 5, y - 4).moveTo(x, y).lineTo(x, y - 6).moveTo(x + 2, y).lineTo(x + 5, y - 3)
                .stroke({ color: 0x4e7044, alpha: .20, width: 1 });
        }
    }

    const towns = settlements.map(project), springs = oases.map(project);
    // Minimum spanning tree of route distances: connected nearby settlements, no list-order ring.
    const edges = towns.flatMap((a, i) => towns.slice(i + 1).map((b, offset) => {
        const route = roadRoute(a, b);
        return { a: i, b: i + offset + 1, route, length: route.reduce((sum, p, k) => sum + (k ? distance(route[k - 1]!, p) : 0), 0) };
    })).sort((a, b) => a.length - b.length);
    const groups = towns.map((_, i) => i), routes: Point[][] = [];
    for (const edge of edges) {
        const from = groups[edge.a]!, to = groups[edge.b]!;
        if (from === to) continue;
        routes.push(edge.route);
        groups.forEach((group, i) => { if (group === to) groups[i] = from; });
    }
    const roads = new Graphics(); root.addChild(roads);
    for (const route of routes) {
        for (const [width, color, alpha] of [[17, 0x756849, .14], [10, 0xb8a580, .65], [5, 0xd5c09a, .48]] as const) {
            roads.moveTo(route[0]!.x, route[0]!.y);
            route.slice(1).forEach(p => roads.lineTo(p.x, p.y));
            roads.stroke({ color, width, alpha, join: 'round', cap: 'round' });
        }
    }
    const roadPoints = routes.flat();
    const usedBridgeYs = bridgeYs.filter(y => routes.some(route => route.some((p, i) => {
        const next = route[i + 1];
        return next && p.y === y && next.y === y && (p.x - riverX(y)) * (next.x - riverX(y)) < 0;
    })));
    const river = new Graphics();
    for (const [extra, color, alpha] of [[24, 0x789064, .22], [14, 0xa3ab7d, .6], [7, 0xc0b28a, 1], [2, 0x527e77, 1], [0, 0x6faaa0, 1], [-7, 0x8dbeb0, .7]] as const) {
        const left: number[] = [], right: number[] = [];
        for (let y = WORLD_TOP; y <= WORLD_TOP + WORLD_H; y += 16) {
            const width = riverWidth(y) + extra;
            left.push(riverX(y) - width, y); right.unshift(riverX(y) + width, y);
        }
        river.poly([...left, ...right]).fill({ color, alpha });
    }
    root.addChild(river);
    const water = new Graphics(); root.addChild(water);
    // Bridges align with the road centerline and span the varying river banks.
    const bridges = new Graphics();
    for (const y of usedBridgeYs) {
        const x = riverX(y);
        bridges.roundRect(x - 64, y - 12, 128, 28, 5).fill({ color: 0x3e584a, alpha: .25 })
            .roundRect(x - 62, y - 12, 124, 23, 3).fill(0xa99a79)
            .rect(x - 58, y - 8, 116, 15).fill(0xc5b18b);
        for (let dx = -54; dx < 58; dx += 9) bridges.moveTo(x + dx, y - 8).lineTo(x + dx, y + 7).stroke({ color: 0x8a7959, width: 1.3, alpha: .6 });
        for (const dy of [-13, 9]) bridges.roundRect(x - 64, y + dy, 128, 5, 2).fill(0x807c66)
            .moveTo(x - 62, y + dy).lineTo(x + 62, y + dy).stroke({ color: 0xe0d6b5, width: 2 });
        for (const dx of [-61, 57]) for (const dy of [-16, 7]) bridges.rect(x + dx, y + dy, 7, 9).fill(0xb7ae91).rect(x + dx, y + dy, 7, 2).fill(0xe0d6b5);
    }
    root.addChild(bridges);

    const scenery = new Container(); scenery.sortableChildren = true; root.addChild(scenery);
    const swaying: { sprite: Sprite; phase: number }[] = [];
    const placed: { x: number; y: number; width: number }[] = [];
    // Keep all trees, boulders, and mountains outside/behind the village perimeter so nothing stands between villages,
    // while maintaining wide clearings around outer settlements, oases, roads, and the river.
    const villageOuterRadius = Math.max(1750, ...settlements.map(s => distance(s, { x: 2250, y: 2250 }))) + 260;
    const clear = (x: number, y: number, size: number) => {
        const visual = { x, y: y - size * .42 };
        const outsideVillageRing = (pt: Point) => Math.hypot(pt.x - 2250, pt.y / .65 - 2250) >= villageOuterRadius;
        const clearOf = (p: Point, r: number) => distance(p, { x, y }) >= r && distance(p, visual) >= r;
        return outsideVillageRing({ x, y }) && outsideVillageRing(visual) &&
            towns.every(p => clearOf(p, 260 + size * .55) &&
                !(Math.abs(x - p.x) < 175 + size * .4 && y > p.y - 20 && y < p.y + 155 + size * .5)) &&
            springs.every(p => clearOf(p, 230 + size * .55) &&
                !(Math.abs(x - p.x) < 140 + size * .35 && y > p.y - 20 && y < p.y + 135 + size * .45)) &&
            Math.abs(x - riverX(y)) > riverWidth(y) + 48 + size * .4 &&
            !roadPoints.some(p => distance(p, { x, y }) < 34 + size * .45 || distance(p, visual) < 30 + size * .4) &&
            clearOf({ x: 2250, y: 1462.5 }, 220 + size * .5);
    };
    const place = (index: number, size: number, x: number, y: number, kind: string) => {
        if (!clear(x, y, size) || kindAt(x, y) !== kind || placed.some(p => distance(p, { x, y }) < (p.width + size) * .24)) return;
        const sprite = terrainSprite(index, size, x, y); sprite.zIndex = y;
        if (random() < .5) sprite.scale.x *= -1;
        sprite.tint = kind === 'Woodland' ? 0x9aae92 : kind === 'Highlands' ? 0xb4b6a6 : 0xcbbf9e;
        sprite.alpha = .78;
        scenery.addChild(sprite); placed.push({ x, y, width: size });
        if (index < 2) swaying.push({ sprite, phase: random() * Math.PI * 2 });
    };
    // Paint low, ground-blended boulder clusters directly into ground/details so rocks match the biome palette
    // and form natural organic formations instead of stamped straight-line rows of alpine icons.
    const drawOutcrop = (cx: number, cy: number, kind: string, span: number) => {
        if (!clear(cx, cy, span * 1.4) || kindAt(cx, cy) !== kind || placed.some(p => distance(p, { x: cx, y: cy }) < (p.width + span) * .45)) return;
        const palette = kind === 'Desert'
            ? { base: 0xa67e49, mid: 0xc2975e, hi: 0xdbba83, rim: 0x6f512f }
            : { base: 0x6c6e60, mid: 0x878879, hi: 0xa4a393, rim: 0x4b4e42 };
        const count = 3 + Math.floor(random() * 4);
        ground.ellipse(cx, cy + span * .12, span * .72, span * .34).fill({ color: palette.rim, alpha: .14 });
        const stones = Array.from({ length: count }, (_, idx) => {
            const a = random() * Math.PI * 2, d = Math.sqrt(random()) * span * .48;
            return {
                x: cx + Math.cos(a) * d,
                y: cy + Math.sin(a) * d * .62,
                r: (idx === 0 ? .52 : .24 + random() * .26) * span * .55,
            };
        }).sort((a, b) => a.y - b.y);
        for (const s of stones) {
            const w = s.r * (1.05 + random() * .35), h = s.r * (.62 + random() * .32);
            ground.ellipse(s.x + w * .12, s.y + h * .28, w * .98, h * .46).fill({ color: palette.rim, alpha: .18 });
            ground.poly([
                s.x - w, s.y + h * .18,
                s.x - w * .68, s.y - h * .65,
                s.x - w * .08, s.y - h * .95,
                s.x + w * .64, s.y - h * .58,
                s.x + w, s.y + h * .15,
                s.x + w * .38, s.y + h * .38,
                s.x - w * .45, s.y + h * .35,
            ]).fill({ color: palette.base, alpha: .85 }).stroke({ color: palette.rim, alpha: .24, width: 1.2 });
            ground.poly([
                s.x - w * .68, s.y - h * .65,
                s.x - w * .08, s.y - h * .95,
                s.x + w * .45, s.y - h * .55,
                s.x + w * .06, s.y - h * .18,
                s.x - w * .52, s.y - h * .22,
            ]).fill({ color: palette.hi, alpha: .66 });
            ground.poly([
                s.x + w * .06, s.y - h * .18,
                s.x + w * .64, s.y - h * .58,
                s.x + w, s.y + h * .15,
                s.x + w * .38, s.y + h * .38,
            ]).fill({ color: palette.mid, alpha: .58 });
        }
        placed.push({ x: cx, y: cy, width: span * 1.1 });
    };
    for (let i = 0; i < 640; i++) {
        const x = -2100 + random() * 8700, y = -1400 + random() * 5725, kind = kindAt(x, y);
        const worldDist = Math.hypot(x - 2250, y / .65 - 2250);
        if (kind === 'Woodland') {
            const span = 110 + random() * 60;
            for (let j = 0; j < 20; j++) {
                const angle = random() * Math.PI * 2, radius = Math.sqrt(random());
                place(j % 4 === 0 ? 0 : 1, 44 + random() * 26, x + Math.cos(angle) * radius * span, y + Math.sin(angle) * radius * span * .65, kind);
            }
        } else if (kind === 'Highlands') {
            if (i % 3 === 0) {
                const clusterCount = 2 + Math.floor(random() * 3);
                for (let j = 0; j < clusterCount; j++) {
                    const a = random() * Math.PI * 2, dist = 35 + random() * 85;
                    drawOutcrop(x + Math.cos(a) * dist, y + Math.sin(a) * dist * .62, kind, 36 + random() * 28);
                }
            }
            if (worldDist > 2650 && i % 24 === 0) {
                place(2, 118 + random() * 36, x, y, kind);
            }
        } else if (kind === 'Desert' && i % 4 === 0) {
            drawOutcrop(x, y, kind, 28 + random() * 24);
            if (worldDist > 2450 && i % 20 === 0) {
                place(4, 62 + random() * 22, x + (random() - .5) * 90, y + (random() - .5) * 60, kind);
            }
        }
    }
    const names = new Container(); root.addChild(names);
    for (const region of regions) {
        const candidates = [region.center, ...Array.from({ length: 65 }, () => ({ x: 320 + random() * 3860, y: 260 + random() * 3980 }))]
            .filter(p => biomeAt(regions, p)?.id === region.id);
        const clearance = (p: Point) => {
            const point = project(p);
            return Math.min(...towns.map(s => distance(s, point) - 145), ...springs.map(s => distance(s, point) - 115),
                ...placed.map(s => distance(s, point) - s.width * .55), Math.abs(point.x - riverX(point.y)) - 65);
        };
        const spot = candidates.sort((a, b) => clearance(b) - clearance(a))[0] ?? region.center;
        const name = new Text({ text: t(region.kind), style: { fontFamily: 'Tahoma, sans-serif', fontSize: 24, fontWeight: 'bold', fill: 0xf6edd2, stroke: { color: 0x515e42, width: 2 } } });
        name.anchor.set(.5); name.position.set(spot.x, spot.y * .65); name.alpha = .76; names.addChild(name);
    }
    const boundaries = new Graphics(); boundaries.visible = false; root.addChild(boundaries);
    const seen = new Set<string>();
    for (const region of regions) for (let i = 0; i < region.boundary.length; i++) {
        const a = region.boundary[i]!, b = region.boundary[(i + 1) % region.boundary.length]!;
        if (a.x === b.x && (a.x === 45 || a.x === 4455) || a.y === b.y && (a.y === 45 || a.y === 4455)) continue;
        const key = [a, b].map(p => `${p.x.toFixed(4)},${p.y.toFixed(4)}`).sort().join(':');
        if (seen.has(key)) continue; seen.add(key);
        boundaries.moveTo(a.x, a.y * .65).lineTo(b.x, b.y * .65).stroke({ color: 0x384936, width: 5, alpha: .7 })
            .moveTo(a.x, a.y * .65).lineTo(b.x, b.y * .65).stroke({ color: 0xf9e4a7, width: 2, alpha: .95 });
    }
    return {
        setBoundaries(visible) { boundaries.visible = visible; },
        setDetail(scale) { details.visible = scale >= .62; names.alpha = scale < .38 ? .6 : 1; },
        destroy() { root.removeChildren().forEach(c => c.destroy({ children: true })); ownedTextures.forEach(texture => texture.destroy(true)); },
        animate(time) {
            swaying.forEach(({ sprite, phase }) => { sprite.skew.x = Math.sin(time / 3400 + phase) * .006; });
            water.clear();
            for (let i = 0; i < 60; i++) {
                const y = ((i * 80 + time * .014) % 4000) - 500;
                if (usedBridgeYs.some(bridge => Math.abs(bridge - y) < 22)) continue;
                const x = riverX(y) + Math.sin(i * 2.3) * (riverWidth(y) - 8);
                water.moveTo(x - 4, y).quadraticCurveTo(x, y + 2, x + 6, y).stroke({ color: 0xd5e5c7, width: 1.2, alpha: .24 });
            }
        }
    };
}
export const previewRegions:BiomeRegion[]=[
    {id:0,kind:'Desert',center:{x:1035,y:1148},boundary:[{x:45,y:45},{x:2138,y:45},{x:2430,y:1778},{x:45,y:2542}],wood:1,iron:1,stone:1.15,wheat:1.15},
    {id:1,kind:'Highlands',center:{x:3398,y:1102},boundary:[{x:2138,y:45},{x:4455,y:45},{x:4455,y:2115},{x:2430,y:1778}],wood:1,iron:1.25,stone:1.2,wheat:1},
    {id:2,kind:'Woodland',center:{x:3442,y:3420},boundary:[{x:2430,y:1778},{x:4455,y:2115},{x:4455,y:4455},{x:2362,y:4455}],wood:1.3,iron:1,stone:1,wheat:1},
    {id:3,kind:'Grassland',center:{x:1058,y:3398},boundary:[{x:45,y:2542},{x:2430,y:1778},{x:2362,y:4455},{x:45,y:4455}],wood:1,iron:1,stone:1,wheat:1.25}
];

