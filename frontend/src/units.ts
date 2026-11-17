import { currentLanguage, t } from './locale';
import { unitArtwork } from './unit-art';
export { unitArtwork } from './unit-art';
export {initUnitTooltips} from './unit-tooltip';
export interface UnitDefinition { unitId: string; tribeType: string; role: string; displayName: string; trainingBuilding?: string; requiredBuildingLevel?: number; stats: { speed: number; carryCapacity: number; wheatUpkeep: number; attack:number; defenseInfantry:number; defenseCavalry:number; hitPoints:number }; matchups:Record<string,number>; trainingCost:{wood:number;iron:number;stone:number;wheat:number} }

const UNIT_ROLE_ORDER = [
    'infantry', 'spearman', 'swordsman', 'archer', 'crossbowman',
    'scout', 'lightcavalry', 'cavalry', 'heavycavalry',
    'ram', 'catapult', 'siegetower',
    'governor', 'wagon',
];

/** Canonical unit ordering: soldiers -> scouts/cavalry -> siege engines -> governor -> wagons. */
export function unitOrderIndex(unitId: string): number {
    const role = unitId.split('-')[1]?.toLowerCase() ?? '';
    const idx = UNIT_ROLE_ORDER.indexOf(role);
    return idx >= 0 ? idx : 99;
}

export function compareUnitIds(aId: string, bId: string): number {
    return unitOrderIndex(aId) - unitOrderIndex(bId) || aId.localeCompare(bId);
}
export const unitCatalog = new Map<string, UnitDefinition>();
let loadingUnits: Promise<void> | undefined;
export async function loadUnits() {
    if (unitCatalog.size > 0) return;
    loadingUnits ??= fetch('/api/catalog')
        .then(async response => {
            if (!response.ok) throw Error('Unit catalog is unavailable.');
            for (const unit of await response.json() as UnitDefinition[]) unitCatalog.set(unit.unitId, unit);
        })
        .catch(err => {
            loadingUnits = undefined;
            throw err;
        });
    return loadingUnits;
}
const escAttr = (s: string) => s.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);
export const unitTitle = (unit: UnitDefinition, lang = currentLanguage()) =>
    lang === 'ar' ? unit.displayName : `${unit.tribeType} ${unit.role.replace(/([a-z])([A-Z])/g, '$1 $2')}`;
export function unitPortrait(id:string,help=true){
    const unit=unitCatalog.get(id);if(!unit)return '';
    const safeId = escAttr(id);
    return `<span class="unit-portrait" data-unit="${safeId}" role="img" aria-label="${escAttr(unitTitle(unit))}">${unitArtwork(id)}${help?`<span class="unit-help" role="button" tabindex="0" aria-label="${escAttr(t('Unit properties'))}" data-help-unit="${safeId}">?</span>`:''}</span>`;
}
export function provisionEstimate(formation: Record<string, number>, distance: number, stock = Infinity) {
    let upkeep = 0, capacity = 0, speed = Infinity, total = 0;
    for (const [id, count] of Object.entries(formation)) { if (count <= 0) continue; const unit = unitCatalog.get(id); if (!unit) continue; total += count; upkeep += count * unit.stats.wheatUpkeep / 60; capacity += count * unit.stats.carryCapacity; speed = Math.min(speed, unit.stats.speed); }
    const travel = distance / Math.max(.1, speed * .8), seconds = Math.max(600, travel * 2 + 180);
    const required = Math.ceil(upkeep * (travel * 2 + 180));
    const food = Math.floor(Math.min(Math.ceil(upkeep * seconds), stock, capacity));
    return { food, required, seconds: upkeep > 0 ? food / upkeep : seconds, capacity, total, travel };
}

