import { t } from './locale';
import { unitArtwork } from './unit-art';
export {initUnitTooltips} from './unit-tooltip';
export interface UnitDefinition { unitId: string; tribeType: string; role: string; displayName: string; trainingBuilding?: string; requiredBuildingLevel?: number; stats: { speed: number; carryCapacity: number; wheatUpkeep: number; attack:number; defenseInfantry:number; defenseCavalry:number; hitPoints:number }; matchups:Record<string,number>; trainingCost:{wood:number;iron:number;stone:number;wheat:number} }
export const unitCatalog = new Map<string, UnitDefinition>();
export async function loadUnits() { const response = await fetch('/api/catalog'); if (!response.ok) throw Error('Unit catalog is unavailable.'); for (const unit of await response.json() as UnitDefinition[]) unitCatalog.set(unit.unitId, unit); }
export function unitPortrait(id:string,help=true){
    const unit=unitCatalog.get(id);if(!unit)return '';
    return `<span class="unit-portrait" data-unit="${id}" role="img" aria-label="${t(unit.displayName)}">${unitArtwork(id)}${help?`<span class="unit-help" role="button" tabindex="0" aria-label="${t('Unit properties')}" data-help-unit="${id}">?</span>`:''}</span>`;
}
export function provisionEstimate(formation: Record<string, number>, distance: number, stock = Infinity) {
    let upkeep = 0, capacity = 0, speed = Infinity, total = 0;
    for (const [id, count] of Object.entries(formation)) { if (count <= 0) continue; const unit = unitCatalog.get(id); if (!unit) continue; total += count; upkeep += count * unit.stats.wheatUpkeep / 60; capacity += count * unit.stats.carryCapacity; speed = Math.min(speed, unit.stats.speed); }
    const travel = distance / Math.max(.1, speed * .8), seconds = Math.max(600, travel * 2 + 180);
    const required = Math.ceil(upkeep * (travel * 2 + 180));
    const food = Math.floor(Math.min(Math.ceil(upkeep * seconds), stock, capacity));
    return { food, required, seconds: upkeep > 0 ? food / upkeep : seconds, capacity, total, travel };
}

