import { safe } from './intelligence';
import type { WorldView } from './map';

export type LiveEventIcon = 'oasis' | 'village' | 'troops' | 'shield';
export interface LiveEvent { key:string; action:string; id:string; title:string; detail:string; friendly:boolean; tone:'danger'|'attacking'|'friendly'; at:number; liveBattle?:boolean; iconKind:LiveEventIcon; count?:number }

function eventIconSvg(kind: LiveEventIcon): string {
    if (kind === 'oasis') {
        return `<svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M12 20c0-4.8.4-8.2 1.8-11.2" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><path d="M13.6 8.8C11.4 6.1 8 5.6 5 7.4c2.6.8 4.9 1.6 6.8 3.1M13.8 8.8c2.4-2.5 5.6-2.4 8-0.2-2.6.4-4.7 1.1-6.5 2.4M13.5 8.6c.4-3.1-1.3-5.6-4.1-6.5 1.3 2.1 2 4.1 2.1 6.3" fill="currentColor"/><path d="M3.5 19.2c2.5-1.6 5.5-2.2 8.5-2.2s6 .6 8.5 2.2c-2.5 1.6-5.5 2.3-8.5 2.3s-6-.7-8.5-2.3Z" fill="currentColor" fill-opacity="0.8"/></svg>`;
    }
    if (kind === 'village') {
        return `<svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M3 10.5 12 3l9 7.5v1.3h-1.8V20H4.8v-8.2H3v-1.3Z" fill="currentColor" fill-opacity="0.9"/><path d="M10 20v-5.2a2 2 0 0 1 4 0V20h-4Z" fill="#182220" fill-opacity="0.72"/><path d="M16.5 4.2h2.2v3.3l-2.2-1.8V4.2Z" fill="currentColor"/></svg>`;
    }
    if (kind === 'troops') {
        return `<svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><circle cx="12" cy="7.5" r="2.7" fill="currentColor"/><path d="M7.6 19.5c0-2.7 2-4.6 4.4-4.6s4.4 1.9 4.4 4.6H7.6Z" fill="currentColor"/><circle cx="6.2" cy="9.2" r="2.1" fill="currentColor" fill-opacity="0.82"/><path d="M2.5 19c0-2.1 1.5-3.6 3.5-3.6.7 0 1.4.2 1.9.5-.8 1-1.2 2.1-1.2 3.1H2.5Z" fill="currentColor" fill-opacity="0.82"/><circle cx="17.8" cy="9.2" r="2.1" fill="currentColor" fill-opacity="0.82"/><path d="M21.5 19c0-2.1-1.5-3.6-3.5-3.6-.7 0-1.4.2-1.9.5.8 1 1.2 2.1 1.2 3.1h4.2Z" fill="currentColor" fill-opacity="0.82"/></svg>`;
    }
    return `<svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M12 2.8 19.5 6v5.8c0 4.6-3.1 8.1-7.5 9.6-4.4-1.5-7.5-5-7.5-9.6V6L12 2.8Z" fill="currentColor" fill-opacity="0.88"/></svg>`;
}

export function activeEvents(world:WorldView,you:string):LiveEvent[]{
    const events:LiveEvent[]=[],reports=world.reports??[];
    const active=reports.filter(r=>r.status==='Active');
    const isPlayerInWorld=world.warfare?.settlements.some(s=>s.ownerId===you)||world.armies.some(a=>a.ownerId===you);
    for(const r of active){
        if(r.parentReportId&&active.some(parent=>parent.id===r.parentReportId))continue;
        const siege=world.warfare?.sieges.find(s=>s.id===r.id);
        const oasisSiege=world.oasisSieges?.find(os=>os.id===r.id);
        const battle=world.warfare?.battles.find(b=>b.id===r.id);
        if(isPlayerInWorld){
            const isDefending=r.defender===you||siege?.defenderOwner===you||oasisSiege?.defenderOwner===you||battle?.defenderOwner===you;
            const stillFighting=(r.armies?.some(a=>a.ownerId===you&&a.active)??false)
                ||(siege?.armyIds.some(id=>world.armies.some(a=>a.id===id&&a.ownerId===you))??false)
                ||(oasisSiege?.armyIds.some(id=>world.armies.some(a=>a.id===id&&a.ownerId===you))??false)
                ||(battle?[...battle.attackerArmyIds,...battle.defenderArmyIds].some(id=>world.armies.some(a=>a.id===id&&a.ownerId===you)):false);
            if(!isDefending&&!stillFighting)continue;
        }
        const isAttacker=r.attacker===you||(r.armies?.some(a=>a.ownerId===you&&a.attacking)??false);
        const title=siege?({Fighting:'Village battle',Blockading:'Village blockade',Persuading:'Persuasion in progress',Demolishing:'Demolition in progress'}[siege.phase]??'Village battle'):r.kind==='OasisSiege'?'Oasis under attack':r.kind==='Interception'?'Caravan under attack':'Battle in progress';
        const detail=siege?(siege.phase==='Persuading'?`${Math.round(siege.progress)}% persuasion`:siege.phase==='Demolishing'?`${siege.remainingBuildingLevels} building levels remaining`:siege.phase==='Blockading'?'External supplies blocked · Wheat −50%':'View live battle report'):'View live battle report';
        const iconKind:LiveEventIcon=siege||r.kind==='Siege'||!!r.cityAttack?'village':(oasisSiege||r.kind==='OasisSiege'||!!r.oasisDefense?'oasis':'troops');
        events.push({key:r.id,action:'report',id:r.id,title,detail,friendly:false,tone:isAttacker?'attacking':'danger',at:r.startedAt,liveBattle:true,iconKind});
    }
    const engaged=new Set(active.flatMap(r=>(r.armies??[]).map(a=>a.armyId)));
    for(const a of world.armies){
        if(engaged.has(a.id))continue;
        if(a.order==='Reinforcing'&&(a.ownerId===you||a.supportedOwnerId===you))
            events.push({key:a.id,action:'event-army',id:a.id,title:'Reinforcements on the way',detail:'View expedition',friendly:true,tone:'friendly',at:Infinity,iconKind:'shield',count:a.soldiers+a.wagons});
        else if(['Approaching','Attacking','Occupying','Blockading','Intercepting','Burning'].includes(a.order)&&
            (a.ownerId===you||world.threats?.some(t=>t.armyId===a.id)||world.oases.some(o=>o.id===a.targetId&&o.ownerId===you))) {
            const isOasis=world.oases.some(o=>o.id===a.targetId)||a.order==='Occupying'||a.order==='Blockading'||a.order==='Burning';
            const isVillage=!isOasis&&(a.order==='Approaching'||(world.warfare?.settlements.some(s=>s.id===a.targetId)??false)||(world.threats?.some(t=>t.armyId===a.id&&(world.warfare?.settlements.some(s=>s.id===t.targetId)??false))??false));
            const iconKind:LiveEventIcon=isOasis?'oasis':isVillage?'village':'troops';
            events.push({key:a.id,action:'event-army',id:a.id,title:a.ownerId===you?'Your attack is underway':'Incoming attack',detail:'View expedition',friendly:false,tone:a.ownerId===you?'attacking':'danger',at:Infinity,iconKind,count:a.soldiers+a.wagons});
        }
    }
    return events.sort((a,b)=>(b.liveBattle?1:0)-(a.liveBattle?1:0)||a.at-b.at||a.key.localeCompare(b.key));
}
export class LiveEventBar{
    private sequence=new Map<string,number>();
    private next=0;
    render(events:LiveEvent[]){
        const keys=new Set(events.map(e=>e.key));
        for(const key of this.sequence.keys())if(!keys.has(key))this.sequence.delete(key);
        for(const e of events)if(!this.sequence.has(e.key))this.sequence.set(e.key,this.next++);
        return [...events].sort((a,b)=>(b.liveBattle?1:0)-(a.liveBattle?1:0)||this.sequence.get(a.key)!-this.sequence.get(b.key)!).map(e=>{
            const tone=e.tone??(e.friendly?'friendly':'danger');
            const tail = e.count != null
                ? `<span class="live-event-count" data-no-translate="1"><span class="live-event-dot" aria-hidden="true"></span><b>${e.count.toLocaleString()}</b></span>`
                : `<span class="live-event-dot" aria-label="Live"></span>`;
            return `<article class="important-alert ${tone}${e.liveBattle?' live-battle':''}" data-event="${safe(e.key)}"><button data-action="${e.action}" data-id="${safe(e.id)}"><span class="live-event-icon" aria-hidden="true">${eventIconSvg(e.iconKind)}</span><strong>${safe(e.title)}<small>${safe(e.detail)}</small></strong>${tail}</button></article>`;
        }).join('');
    }
}
