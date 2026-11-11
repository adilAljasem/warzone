import type {ArmyView,Point,WorldView} from './map';
import {armyFigureCount,armyFigureLayout} from './army-visual';

// Keep slots stable when another army joins or leaves the encirclement.
export class ArmyLayout {
    private slots=new Map<string,{site:string;slot:number}>();
    position(army:ArmyView,world:WorldView):Point{
        if(army.campRadius) return {x:army.position.x,y:army.position.y*.65};
        const base={x:army.position.x+54,y:army.position.y*.65+22};
        const oasis=world.oases.find(o=>o.id===(army.defendingSiteId??army.targetId));
        const city=world.warfare?.settlements.find(c=>c.id===(army.defendingSiteId??army.targetId));
        const battle=world.warfare?.battles.find(b=>b.status==='Active'&&[...b.attackerArmyIds,...b.defenderArmyIds].includes(army.id));
        const site=oasis??city??battle;if(!site){this.slots.delete(army.id);return base;}
        const distance=Math.hypot(army.position.x-site.position.x,army.position.y-site.position.y);if(distance>=180)return base;
        const guarding=!!army.defendingSiteId,key=site.id;let assigned=this.slots.get(army.id);
        if(!assigned||assigned.site!==key){
            const used=new Set([...this.slots.values()].filter(s=>s.site===key).map(s=>s.slot));
            const angle=Math.atan2(army.position.y-site.position.y,army.position.x-site.position.x),preferred=(Math.round(angle/(Math.PI*2)*12)+12)%12;
            let ring=0;while(Array.from({length:12},(_,i)=>ring*12+i).every(i=>used.has(i)))ring++;
            const candidates=Array.from({length:12},(_,i)=>ring*12+i).filter(i=>!used.has(i));
            const angular=(a:number,b:number)=>Math.min(Math.abs(a%12-b%12),12-Math.abs(a%12-b%12));
            const score=(slot:number)=>Math.min(6,...[...used].filter(i=>Math.floor(i/12)===ring).map(i=>angular(slot,i)))-angular(slot,preferred)*.001;
            const slot=candidates.sort((a,b)=>score(b)-score(a))[0]!;assigned={site:key,slot};this.slots.set(army.id,assigned);
        }
        // Leave room for the whole formation, so a larger army does not cover its opponents or the clash.
        let battleRadius=125;
        if(battle){
            const participants=new Set([...battle.attackerArmyIds,...battle.defenderArmyIds]);
            const count=Math.max(0,...world.armies.filter(a=>participants.has(a.id)).map(a=>{
                const entries=Object.entries(a.mapFigures??a.formation??{}).filter(([id])=>!id.endsWith('-wagon'));
                return entries.length?entries.reduce((sum,[,amount])=>sum+(a.mapFigures?amount:armyFigureCount(amount)),0):armyFigureCount(a.soldiers);
            }));
            const layout=armyFigureLayout(count);
            battleRadius=Math.max(125,layout.columns*23*.625+65,(Math.max(0,layout.rows-1)*17+layout.width)*1.25/.65+55);
        }
        const ring=Math.floor(assigned.slot/12),angle=assigned.slot%12*Math.PI/6,radius=(battle?battleRadius:guarding?75:125)+ring*65,blend=Math.min(1,Math.max(0,(180-distance)/140));
        return{x:base.x+(site.position.x+Math.cos(angle)*radius-base.x)*blend,y:base.y+(site.position.y*.65+Math.sin(angle)*radius*.65-base.y)*blend};
    }
    retain(ids:Set<string>){for(const id of this.slots.keys())if(!ids.has(id))this.slots.delete(id);}
}
