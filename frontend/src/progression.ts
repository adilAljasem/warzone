import { t } from './locale';
import type { Point } from './map';

interface ProgressTarget { id:string; name:string; kind:string; level:number; unlocked:boolean; trainingBuilding?:string }
interface ProgressState {
    id:string; you:string;
    economy:{villageId:string;targets:ProgressTarget[]}|null;
    world:{notices:{id:number;text:string;kind:string;endsAt:number|null}[];
        warfare?:{settlements:{id:string;position:Point}[]}}|null;
}
interface Achievement { title:string; text:string; glyph:string }

export class ProgressFeedback {
    private box:HTMLElement;
    private queue:Achievement[]=[];
    private timer=0;
    constructor(private celebrate:(position:Point,message:string)=>void){
        this.box=document.createElement('aside');this.box.className='achievement';this.box.hidden=true;
        this.box.setAttribute('role','status');this.box.setAttribute('aria-live','polite');
        document.body.append(this.box);
    }
    private enqueue(item:Achievement){
        if(document.hidden)return;
        this.queue.push(item);if(this.queue.length>4)this.queue.shift();
        if(this.box.hidden)this.showNext();
    }
    private showNext(){
        clearTimeout(this.timer);const item=this.queue.shift();this.box.replaceChildren();
        this.box.hidden=!item;if(!item)return;
        const glyph=document.createElement('span'),body=document.createElement('div'),title=document.createElement('strong'),detail=document.createElement('p'),close=document.createElement('button');
        glyph.textContent=item.glyph;glyph.setAttribute('aria-hidden','true');title.textContent=t(item.title);detail.textContent=t(item.text);
        close.textContent='×';close.setAttribute('aria-label',t('Dismiss'));close.onclick=()=>this.showNext();
        body.append(title,detail);this.box.append(glyph,body,close);
        this.timer=window.setTimeout(()=>this.showNext(),5500);
    }
    update(previous:ProgressState|null,next:ProgressState|null){
        if(!next||previous?.id!==next.id){this.queue=[];clearTimeout(this.timer);this.box.hidden=true;return;}
        if(!previous?.world||!next.world)return;
        const before=previous.economy,after=next.economy;
        const home=next.world.warfare?.settlements.find(c=>c.id===after?.villageId);
        if(before&&after&&before.villageId===after.villageId){
            for(const target of after.targets.filter(u=>u.kind==='Building')){
                const old=before.targets.find(u=>u.id===target.id);if(!old||old.level>=target.level)continue;
                const unlocked=after.targets.filter(u=>u.kind==='Training'&&u.trainingBuilding===target.name&&u.unlocked&&before.targets.some(p=>p.id===u.id&&!p.unlocked));
                const title=`${target.name} · Level ${target.level}`;
                this.enqueue({title,text:unlocked.length?`${t('New units unlocked')}: ${unlocked.map(u=>t(u.name)).join(' · ')}`:'Your village is growing.',glyph:'✦'});
                document.querySelector(`[data-action="building"][data-id="${target.id}"]`)?.closest('.building-card')?.classList.add('level-up');
                if(home)this.celebrate(home.position,`Level ${target.level}`);
            }
        }
        const known=new Set(previous.world.notices.map(n=>n.id));
        for(const event of next.world.notices.filter(n=>!known.has(n.id))){
            if(event.kind==='train'&&event.endsAt===null&&event.text.includes('ready for duty')){
                this.enqueue({title:'Recruits ready',text:event.text,glyph:'⚔'});
            }
            if(event.text.includes('now supports its occupying village'))this.enqueue({title:'Oasis secured',text:event.text,glyph:'⚑'});
        }
    }
}
