import type { WorldView } from './map';

export type SoundCue='attack'|'village-captured'|'oasis-captured'|'oasis-lost';
export interface SoundState { id:string;you:string;status:string;world:WorldView|null }
export interface CombatSound { level:number;pan:number }

// Only transitions after the initial snapshot produce notification sounds.
export class SoundEvents {
    private key='';
    private noticeId=0;
    private incoming=new Set<string>();
    private oases=new Map<string,string|null>();
    private villages=new Map<string,string|null>();
    reset(){this.key='';this.noticeId=0;this.incoming.clear();this.oases.clear();this.villages.clear();}
    update(state:SoundState|null):SoundCue[]{
        if(!state?.world||state.status!=='Running'){this.reset();return [];}
        const {world,you}=state,key=`${state.id}:${you}`,initial=key!==this.key;
        if(initial)this.reset();
        const cues=new Set<SoundCue>();
        const incoming=new Set((world.threats??[]).filter(t=>t.ownerId!==you).map(t=>t.armyId));
        for(const a of world.armies)if(a.ownerId!==you&&['Approaching','Attacking','Occupying','Blockading','Intercepting'].includes(a.order)&&world.oases.some(o=>o.id===a.targetId&&o.ownerId===you))incoming.add(a.id);
        if(!initial&&[...incoming].some(id=>!this.incoming.has(id)))cues.add('attack');
        for(const event of [...world.notices,...world.alerts??[]]){
            if(!initial&&event.id>this.noticeId){
                if(['oasis-attack','convoy-attack'].includes(event.kind)||event.kind==='war'&&/^(An enemy army|Enemy troops|Your army is under attack|Your settlement is under siege|A caravan is under attack)/.test(event.text))cues.add('attack');
                if(event.kind==='oasis-loss')cues.add('oasis-lost');
            }
        }
        const oases=new Map(world.oases.map(o=>[o.id,o.ownerId]));
        const villages=new Map((world.warfare?.settlements??[]).map(c=>[c.id,c.ruined?null:c.ownerId]));
        if(!initial){
            for(const [id,owner] of oases){
                if(this.oases.has(id)&&this.oases.get(id)!==you&&owner===you)cues.add('oasis-captured');
                if(this.oases.get(id)===you&&owner!==you)cues.add('oasis-lost');
            }
            for(const [id,owner] of villages)if(this.villages.has(id)&&this.villages.get(id)!==you&&owner===you)cues.add('village-captured');
        }
        this.key=key;this.incoming=incoming;this.oases=oases;this.villages=villages;
        this.noticeId=Math.max(this.noticeId,0,...world.notices.map(n=>n.id),...(world.alerts??[]).map(n=>n.id));
        return [...cues];
    }
}

// A source far from the camera center is silent. Sources near the center sound closer and fade out with distance.
export function combatAudibility(x:number,y:number,width:number,height:number):CombatSound|null {
    if(width<=0||height<=0||x<0||y<0||x>width||y>height)return null;
    const dx=(x-width/2)/(width/2),dy=(y-height/2)/(height/2),dist=Math.hypot(dx,dy),radius=.65;
    if(dist>=radius)return null;
    const level=Math.pow(1-dist/radius,1.8);
    return level<.03?null:{level,pan:Math.max(-.85,Math.min(.85,dx))};
}
