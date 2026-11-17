import { Assets, Rectangle, Sprite, Texture } from 'pixi.js';

// Matches units-v2-prompts.json: left to right, top to bottom; last two cells empty.
export const unitRoles = ['infantry', 'archer', 'cavalry', 'scout', 'wagon', 'catapult', 'ram', 'siegetower', 'governor', 'spearman', 'swordsman', 'crossbowman', 'lightcavalry', 'heavycavalry'];
const tribes = ['romans', 'germans', 'greeks', 'mongols', 'persians'];
const textures = new Map<string, Texture>();
let loading: Promise<void> | undefined;

export function unitAtlasFrame(id: string) {
    const [tribe, role] = id.split('-'), index = unitRoles.indexOf(role ?? '');
    if (!tribes.includes(tribe ?? '') || index < 0) return null;
    return { path: `/art/units-${tribe}-v2.png`, x: index % 4, y: Math.floor(index / 4) };
}

export function unitArtwork(id: string) {
    const frame = unitAtlasFrame(id);
    return frame ? `<svg class="unit-art" viewBox="${frame.x} ${frame.y} 1 1" aria-hidden="true"><image href="${frame.path}" width="4" height="4"/></svg>` : '';
}

export function loadUnitArt() {
    return loading ??= Promise.all(tribes.map(async tribe => {
        const atlas = await Assets.load<Texture>(`/art/units-${tribe}-v2.png`);
        const size = atlas.width / 4, height = atlas.height / 4;
        unitRoles.forEach((role, index) => textures.set(`${tribe}-${role}`, new Texture({
            source: atlas.source, frame: new Rectangle(index % 4 * size, Math.floor(index / 4) * height, size, height)
        })));
    })).then(() => undefined, err => {
        loading = undefined;
        throw err;
    });
}

export function unitSprite(id: string, width = 52) {
    const sprite = new Sprite(textures.get(id) ?? Texture.EMPTY);
    const size = id === 'persians-cavalry' ? Math.round(width * 1.35) : width;
    sprite.anchor.set(.5, .9);
    sprite.width = size; sprite.height = size;
    return sprite;
}
