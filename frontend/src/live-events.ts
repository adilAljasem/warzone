import { safe } from './intelligence';
import type { WorldView } from './map';

export interface LiveEvent { key:string; action:string; id:string; title:string; detail:string; friendly:boolean; tone:'danger'|'attacking'|'friendly'; at:number; liveBattle?:boolean }
export function activeEvents(world:WorldView,you:string):LiveEvent[]{
    const events:LiveEvent[]=[],reports=world.reports??[];
    const active=reports.filter(r=>r.status==='Active');
    for(const r of active){
        if(r.parentReportId&&active.some(parent=>parent.id===r.parentReportId))continue;
        const siege=world.warfare?.sieges.find(s=>s.id===r.id);
        const isAttacker=r.attacker===you||(r.armies?.some(a=>a.ownerId===you&&a.attacking)??false);
        const title=siege?({Fighting:'Village battle',Blockading:'Village blockade',Persuading:'Persuasion in progress',Demolishing:'Demolition in progress'}[siege.phase]??'Village battle'):r.kind==='OasisSiege'?'Oasis under attack':r.kind==='Interception'?'Caravan under attack':'Battle in progress';
        const detail=siege?(siege.phase==='Persuading'?`${Math.round(siege.progress)}% persuasion`:siege.phase==='Demolishing'?`${siege.remainingBuildingLevels} building levels remaining`:siege.phase==='Blockading'?'External supplies blocked · Wheat −50%':'View live battle report'):'View live battle report';
        events.push({key:r.id,action:'report',id:r.id,title,detail,friendly:false,tone:isAttacker?'attacking':'danger',at:r.startedAt,liveBattle:true});
    }
    const engaged=new Set(active.flatMap(r=>r.armies.map(a=>a.armyId)));
    for(const a of world.armies){
        if(engaged.has(a.id))continue;
        if(a.order==='Reinforcing'&&(a.ownerId===you||a.supportedOwnerId===you))
            events.push({key:a.id,action:'event-army',id:a.id,title:'Reinforcements on the way',detail:'View expedition',friendly:true,tone:'friendly',at:Infinity});
        else if(['Approaching','Attacking','Occupying','Blockading','Intercepting'].includes(a.order)&&
            (a.ownerId===you||world.threats?.some(t=>t.armyId===a.id)||world.oases.some(o=>o.id===a.targetId&&o.ownerId===you)))
            events.push({key:a.id,action:'event-army',id:a.id,title:a.ownerId===you?'Your attack is underway':'Incoming attack',detail:'View expedition',friendly:false,tone:a.ownerId===you?'attacking':'danger',at:Infinity});
    }
    return events.sort((a,b)=>a.at-b.at||a.key.localeCompare(b.key));
}
export class LiveEventBar{
    private sequence=new Map<string,number>();
    private next=0;
    render(events:LiveEvent[]){
        const keys=new Set(events.map(e=>e.key));
        for(const key of this.sequence.keys())if(!keys.has(key))this.sequence.delete(key);
        for(const e of events)if(!this.sequence.has(e.key))this.sequence.set(e.key,this.next++);
        return [...events].sort((a,b)=>this.sequence.get(a.key)!-this.sequence.get(b.key)!).map(e=>{
            const tone=e.tone??(e.friendly?'friendly':'danger');
            return `<article class="important-alert ${tone}${e.liveBattle?' live-battle':''}" data-event="${safe(e.key)}"><button data-action="${e.action}" data-id="${safe(e.id)}"><span aria-hidden="true">${e.friendly?'🛡':'⚔'}</span><strong>${safe(e.title)}<small>${safe(e.detail)}</small></strong><span class="live-event-dot" aria-label="Live"></span></button></article>`;
        }).join('');
    }
}
