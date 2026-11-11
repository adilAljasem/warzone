import { safe } from './intelligence';
import type { ImportantAlert } from './map';

export class AlertInbox {
    names=new Map<string,string>();
    private key='';
    private read=new Set<number>();
    private items:ImportantAlert[]=[];
    use(match:string,viewer:string){
        const key=`warzone:alerts:${match}:${viewer}`;
        if(key===this.key)return;this.key=key;
        try{this.read=new Set(JSON.parse(localStorage.getItem(key)??'[]'));}catch{this.read=new Set();}
    }
    unread(items:ImportantAlert[]){this.items=items;const latest=new Map<string,ImportantAlert>();for(const a of items)latest.set(a.targetId??String(a.id),a);return [...latest.values()].filter(a=>!this.read.has(a.id)).sort((a,b)=>b.at-a.at||b.id-a.id);}
    dismiss(id:number){const selected=this.items.find(a=>a.id===id);for(const item of this.items)if(item.id===id||selected?.targetId&&item.targetId===selected.targetId&&item.id<=id)this.read.add(item.id);this.read.add(id);try{localStorage.setItem(this.key,JSON.stringify([...this.read].slice(-200)));}catch{/* Session read state remains usable. */}}
    card(a:ImportantAlert){return `<article class="important-alert ${a.kind==='reinforcement'?'friendly':'danger'}"><button data-action="important-alert" data-id="${a.id}"><span>${a.kind==='reinforcement'?'🛡':'⚔'}</span><strong>${safe(a.text)}<small>${safe(this.names.get(a.targetId??'')??'')}</small></strong></button><button data-action="dismiss-alert" data-id="${a.id}" aria-label="Dismiss alert">×</button></article>`;}
    panel(items:ImportantAlert[]){const unread=this.unread(items);return unread.length?`<p class="muted">Select an alert to inspect its location.</p>${unread.map(a=>this.card(a)).join('')}<button data-action="read-alerts">Mark all as read</button>`:'<p class="muted">No unread important alerts.</p>';}
}
