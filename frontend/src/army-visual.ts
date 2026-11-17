// Each figure represents up to ten units of its own type, including a partial group.
export function armyFigureCount(soldiers: number): number {
    return soldiers > 0 ? Math.ceil(soldiers / 10) : 0;
}

export function armyFigures(soldiers:number, tribe:string, formation:Record<string,number>|null, mapFigures?:Record<string,number>|null):string[] {
    if(soldiers<=0)return [];
    const useMap = !!mapFigures && Object.keys(mapFigures).length > 0;
    const entries=Object.entries((useMap?mapFigures:formation)??{}).filter(([id,count])=>count>0&&!id.endsWith('-wagon')).sort((a,b)=>a[0].localeCompare(b[0]));
    if(!entries.length)return Array(armyFigureCount(soldiers)).fill(`${tribe.toLowerCase()}-infantry`);
    return entries.flatMap(([id,count])=>Array(useMap?Math.max(1,Math.floor(count)):armyFigureCount(count)).fill(id));
}

export function armyFigureLayout(count:number) {
    const columns=Math.ceil(Math.sqrt(count*1.3)),rows=Math.ceil(count/Math.max(1,columns));
    const width=count<=2?54:count<=8?46:40;
    const points=Array.from({length:count},(_,i)=>{
        const row=Math.floor(i/columns),size=Math.min(columns,count-row*columns);
        return {x:(i%columns-(size-1)/2)*23+(row%2?4:0),y:(row-rows+1)*17};
    });
    return {width,points,columns,rows};
}

export interface ArmyFlagSlot {
    x: number;
    top: number;
    base: number;
}

// Keep the primary banner in the top-left corner and spread additional banners across the formation.
export function armyFlagSlots(
    flagCount: number,
    points: { x: number; y: number }[],
    rows: number,
    columns: number,
    wagonColumns: number
): ArmyFlagSlot[] {
    const cornerX = -13 - Math.max(0, Math.max(columns, wagonColumns) - 1) * 9;
    const cornerRowY = -Math.max(0, rows - 1) * 17;
    const slots: ArmyFlagSlot[] = [{ x: cornerX, top: cornerRowY - 50, base: cornerRowY - 12 }];
    if (flagCount <= 1) return slots;
    if (points.length === 0) {
        for (let i = 1; i < flagCount; i++) {
            slots.push({ x: cornerX + i * 12, top: cornerRowY - 50 - (i % 2) * 4, base: cornerRowY - 12 - (i % 2) * 4 });
        }
        return slots;
    }

    const used = new Set<number>();
    for (let i = 1; i < flagCount; i++) {
        let bestIndex = -1;
        let bestDist = -1;
        for (let idx = 0; idx < points.length; idx++) {
            if (used.has(idx)) continue;
            const pt = points[idx]!;
            let minDist = Infinity;
            for (const s of slots) {
                const d = Math.hypot(pt.x - s.x, pt.y - (s.base + 12));
                if (d < minDist) minDist = d;
            }
            if (minDist > bestDist) {
                bestDist = minDist;
                bestIndex = idx;
            }
        }
        if (bestIndex < 0) break;
        used.add(bestIndex);
        const pt = points[bestIndex]!;
        slots.push({
            x: pt.x + 6,
            top: pt.y - 50 - (i % 2) * 3,
            base: pt.y - 12 - (i % 2) * 3,
        });
    }
    return slots;
}

export function isDemolishing(siege:{status:string;objective:string;phase:string}) {
    return siege.status==='Active'&&siege.objective==='Raze'&&siege.phase==='Demolishing';
}

export function isSmoking(settlement:{ruined:boolean;smokeSeconds?:number}) {
    return !settlement.ruined&&(settlement.smokeSeconds??0)>0;
}
