// Each figure represents up to ten units of its own type, including a partial group.
export function armyFigureCount(soldiers: number): number {
    return soldiers > 0 ? Math.ceil(soldiers / 10) : 0;
}

export function armyFigures(soldiers:number, tribe:string, formation:Record<string,number>|null, mapFigures?:Record<string,number>|null):string[] {
    if(soldiers<=0)return [];
    const entries=Object.entries(mapFigures??formation??{}).filter(([id,count])=>count>0&&!id.endsWith('-wagon')).sort((a,b)=>a[0].localeCompare(b[0]));
    if(!entries.length)return Array(armyFigureCount(soldiers)).fill(`${tribe.toLowerCase()}-infantry`);
    return entries.flatMap(([id,count])=>Array(mapFigures?Math.floor(count):armyFigureCount(count)).fill(id));
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

export function isDemolishing(siege:{status:string;objective:string;phase:string}) {
    return siege.status==='Active'&&siege.objective==='Raze'&&siege.phase==='Demolishing';
}

export function isSmoking(settlement:{ruined:boolean;smokeSeconds?:number}) {
    return !settlement.ruined&&(settlement.smokeSeconds??0)>0;
}
