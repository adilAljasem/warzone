import { safe } from './intelligence';
import type { OasisView } from './map';

const resources=['wood','iron','stone','wheat'] as const;
const resourceIcon=(name:string)=>`<svg aria-hidden="true"><use href="#i-${name}"/></svg>`;
export function oasisPanel(o:OasisView,you:string,owner:string,running:boolean,protectedEnemy:boolean,canTransfer=false,linkedPosition?:{x:number;y:number}){
    const mine=o.ownerId===you,total=Object.values(o.stock??{}).reduce((a,b)=>a+b,0);
    const btn=(label:string,action:string,disabled=false,extra='')=>`<button data-action="${action}" data-id="${o.id}" class="wide ${action==='plan-attack'?'action-capture':action==='plan-blockade'?'action-blockade':action==='reinforce'?'action-reinforce':action==='plan-camp'?'primary':''}" ${disabled?'disabled':''} ${extra}>${label}</button>`;
    return `<div class="oasis-summary"><div class="stat-line"><span>${safe(owner)}</span><small>${o.defenders} defending troops</small></div>
    <h3>Oasis resources</h3><div class="oasis-resource-grid">${resources.map(r=>`<div>${resourceIcon(r)}<b>${o.stock?o.stock[r].toLocaleString():'—'}</b><small>+${o.productionPerMinute[r]} / minute</small></div>`).join('')}</div>
    <h3>Village production bonus</h3><div class="biome-bonuses ${o.blockaded?'bonus-suspended':''}">${resources.map(r=>`<div>${resourceIcon(r)}<strong>+${r===o.resource.toLowerCase()?50:25}%</strong></div>`).join('')}</div>${o.blockaded?'<small class="negative">Production bonus suspended by blockade</small>':''}</div>
    ${o.siegeReportId?`<button data-action="report" data-id="${o.siegeReportId}" class="oasis-danger wide">⚔ View live oasis battle</button>`:''}
    ${mine?`<section class="oasis-section"><strong>Linked village</strong><span dir="ltr">${linkedPosition?Math.round(linkedPosition.x)+' : '+Math.round(linkedPosition.y):'—'}</span><p class="muted">The linked village receives this oasis production bonus and collected resources.</p>${canTransfer?'<p class="muted">To transfer it, send an army based in the destination village. The link changes when that army completes occupation.</p>':''}</section>`:''}
    ${mine?`<section class="oasis-section"><small>Capacity per resource: ${o.capacity.toLocaleString()}</small><small>${Number((o.wheatUpkeepPerMinute??0).toFixed(1))} wheat / minute for oasis defenders</small>
    ${btn(o.autoTransport?'✓ Automatic transport enabled':'Automatic transport paused','oasis-auto',!running,`role="switch" aria-checked="${!!o.autoTransport}"`)}<p class="muted">Wagons collect all resource types when the total reaches 2000. A blockade stops collection.</p><small>${safe(o.transportStatus)}</small>${btn('Collect resources now','oasis-collect',!running||o.blockaded||!!o.convoyId||total===0)}</section>`:''}
    <div class="panel-footer">${!mine||canTransfer?btn(mine?'Transfer oasis allegiance':'Capture oasis','plan-attack',!running||protectedEnemy):''}${!mine&&o.ownerId?btn('Besiege oasis','plan-blockade',!running||protectedEnemy):''}${!mine?btn('Send scouts','scout-target',!running):''}${btn('Send reinforcements','reinforce',!running||!o.ownerId)}${btn('Camp here','plan-camp',!running)}</div>`;
}
