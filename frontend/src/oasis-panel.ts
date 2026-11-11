import { safe, unitName } from './intelligence';
import { unitPortrait } from './units';
import type { OasisView } from './map';

const resources=['wood','iron','stone','wheat'] as const;
const resourceIcon=(name:string)=>`<svg aria-hidden="true"><use href="#i-${name}"/></svg>`;
export function oasisPanel(o:OasisView,you:string,owner:string,running:boolean,protectedEnemy:boolean,canTransfer=false,linkedPosition?:{x:number;y:number}){
    const mine=o.ownerId===you,total=Object.values(o.stock??{}).reduce((a,b)=>a+b,0);
    const btn=(label:string,action:string,disabled=false,extra='')=>`<button data-action="${action}" data-id="${o.id}" class="wide ${action==='plan-attack'?'action-capture':action==='plan-blockade'?'action-blockade':action==='reinforce'?'action-reinforce':action==='plan-camp'?'primary':''}" ${disabled?'disabled':''} ${extra}>${label}</button>`;
    const actionBtn = (icon: string, label: string, action: string, disabled = false, cls = '') =>
        `<button data-action="${action}" data-id="${o.id}" class="action-btn ${cls}" ${disabled ? 'disabled' : ''}><span class="btn-icon">${icon}</span><span>${label}</span></button>`;

    const garrisonEntries = Object.entries(o.garrison ?? {});
    let garrisonHtml = "";
    if (garrisonEntries.length > 0) {
        garrisonHtml = `
    <section class="oasis-section oasis-garrison-section">
        <h3>Stationed troops</h3>
        <div class="report-forces oasis-stationed-forces">
            <div class="report-unit-grid" style="--unit-cols:4">
                ${garrisonEntries.map(([id, count]) => `
                    <div class="report-unit" title="${safe(unitName(id))}">
                        ${unitPortrait(id, false)}
                        <span class="unit-counts"><b class="positive">${count.toLocaleString()}</b></span>
                    </div>
                `).join('')}
            </div>
        </div>
    </section>`;
    } else if (o.defenders > 0) {
        garrisonHtml = `
    <section class="oasis-section oasis-garrison-section">
        <h3>Stationed troops</h3>
        <div class="report-forces oasis-stationed-forces">
            <div class="report-unit-grid" style="--unit-cols:1">
                <div class="report-unit" title="Defending troops">
                    <span class="unit-portrait" style="display:flex;align-items:center;justify-content:center;font-size:22px;border:1px solid #ebd09055;border-radius:4px;background:#0d2319;">⚔</span>
                    <span class="unit-counts"><b class="positive">${o.defenders.toLocaleString()}</b></span>
                </div>
            </div>
        </div>
    </section>`;
    }

    const actions: string[] = [];
    if (!mine || canTransfer) actions.push(actionBtn('⚔', mine ? 'Transfer oasis allegiance' : 'Capture oasis', 'plan-attack', !running || protectedEnemy, 'action-capture'));
    if (!mine && o.ownerId) actions.push(actionBtn('♜', 'Besiege oasis', 'plan-blockade', !running || protectedEnemy, 'action-blockade'));
    if (!mine) actions.push(actionBtn('👁', 'Send scouts', 'scout-target', !running, 'action-scout'));
    actions.push(actionBtn('🛡', 'Send reinforcements', 'reinforce', !running || !o.ownerId, 'action-reinforce'));
    actions.push(actionBtn('⛺', 'Camp here', 'plan-camp', !running, 'action-camp'));

    const gridHtml = `<div class="panel-action-grid">${actions.map((b, i) => actions.length % 2 === 1 && i === actions.length - 1 ? b.replace('class="action-btn', 'class="action-btn grid-full') : b).join('')}</div>`;

    return `<div class="oasis-summary"><div class="stat-line"><span>${safe(owner)}</span><small>${o.defenders} defending troops</small></div>
    <h3>Oasis resources</h3><div class="oasis-resource-grid">${resources.map(r=>`<div>${resourceIcon(r)}<b>${o.stock?o.stock[r].toLocaleString():'—'}</b><small>+${o.productionPerMinute[r]} / minute</small></div>`).join('')}</div>
    <h3>Village production bonus</h3><div class="biome-bonuses ${o.blockaded?'bonus-suspended':''}">${resources.map(r=>`<div>${resourceIcon(r)}<strong>+${r===o.resource.toLowerCase()?50:25}%</strong></div>`).join('')}</div>${o.blockaded?'<small class="negative">Production bonus suspended by blockade</small>':''}</div>
    ${garrisonHtml}
    ${o.siegeReportId?`<button data-action="report" data-id="${o.siegeReportId}" class="oasis-danger wide">⚔ View live oasis battle</button>`:''}
    ${mine?`<section class="oasis-section"><small>Capacity per resource: ${o.capacity.toLocaleString()}</small><small>${Number((o.wheatUpkeepPerMinute??0).toFixed(1))} wheat / minute for oasis defenders</small>
    ${btn(o.autoTransport?'✓ Automatic transport enabled':'Automatic transport paused','oasis-auto',!running,`role="switch" aria-checked="${!!o.autoTransport}"`)}<p class="muted">Wagons collect all resource types when the total reaches 2000. A blockade stops collection.</p><small>${safe(o.transportStatus)}</small>${btn('Collect resources now','oasis-collect',!running||o.blockaded||!!o.convoyId||total===0)}</section>`:''}
    <div class="panel-footer">${gridHtml}</div>`;
}
