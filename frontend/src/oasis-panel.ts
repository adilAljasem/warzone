import { safe, unitName } from './intelligence';
import { unitPortrait } from './units';
import { currentLanguage } from './locale';
import type { OasisView } from './map';

const resources=['wood','iron','stone','wheat'] as const;
const resourceIcon=(name:string)=>`<svg aria-hidden="true"><use href="#i-${name}"/></svg>`;
export function oasisPanel(o:OasisView,you:string,owner:string,running:boolean,protectedEnemy:boolean,canTransfer=false,linkedPosition?:{x:number;y:number}){
    const isAr = currentLanguage() === 'ar';
    const mine=o.ownerId===you,total=Object.values(o.stock??{}).reduce((a,b)=>a+b,0);
    const btn=(label:string,action:string,disabled=false,extra='')=>`<button data-action="${action}" data-id="${o.id}" class="wide ${action==='plan-attack'?'action-capture':action==='plan-blockade'?'action-blockade':action==='reinforce'?'action-reinforce':action==='plan-camp'?'primary':''}" ${disabled?'disabled':''} ${extra}>${label}</button>`;
    const actionBtn = (icon: string, label: string, action: string, disabled = false, cls = '') =>
        `<button data-action="${action}" data-id="${o.id}" class="action-btn ${cls}" ${disabled ? 'disabled' : ''}><span class="btn-icon">${icon}</span><span>${label}</span></button>`;

    const garrisonEntries = Object.entries(o.garrison ?? {});
    let garrisonHtml = "";
    if (mine && garrisonEntries.length > 0) {
        garrisonHtml = `
    <section class="oasis-section oasis-garrison-section">
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
    } else if (mine && o.defenders > 0) {
        garrisonHtml = `
    <section class="oasis-section oasis-garrison-section">
        <div class="report-forces oasis-stationed-forces">
            <div class="report-unit-grid" style="--unit-cols:1">
                <div class="report-unit" title="${isAr ? 'الجنود المدافعون' : 'Defending troops'}">
                    <span class="unit-portrait" style="display:flex;align-items:center;justify-content:center;font-size:22px;border:1px solid #ebd09055;border-radius:4px;background:#0d2319;">⚔</span>
                    <span class="unit-counts"><b class="positive">${o.defenders.toLocaleString()}</b></span>
                </div>
            </div>
        </div>
    </section>`;
    }

    const isBurned = (o.burnedRemainingSeconds ?? 0) > 0;
    const isBurning = (o.burningProgress ?? 0) > 0;

    const actions: string[] = [];
    if (!mine || canTransfer) actions.push(actionBtn('⚔', isAr ? (mine ? 'نقل تبعية الواحة' : 'احتلال الواحة') : (mine ? 'Transfer oasis allegiance' : 'Capture oasis'), 'plan-attack', !running || protectedEnemy || isBurned, 'action-capture'));
    if (!mine && o.ownerId) actions.push(actionBtn('♜', isAr ? 'حصار الواحة' : 'Besiege oasis', 'plan-blockade', !running || protectedEnemy || isBurned, 'action-blockade'));
    if (!mine) actions.push(actionBtn('👁', isAr ? 'إرسال كشافة' : 'Send scouts', 'scout-target', !running || isBurned, 'action-scout'));
    actions.push(actionBtn('🛡', isAr ? 'إرسال تعزيزات' : 'Send reinforcements', 'reinforce', !running || !o.ownerId || isBurned, 'action-reinforce'));
    actions.push(actionBtn('⛺', isAr ? 'عسكر هنا' : 'Camp here', 'plan-camp', !running || isBurned, 'action-camp'));
    actions.push(actionBtn('🔥', isAr ? 'حرق الواحة' : 'Burn oasis', 'plan-burn', !running || protectedEnemy || isBurned, 'action-burn'));

    const gridHtml = `<div class="panel-action-grid">${actions.map((b, i) => actions.length % 2 === 1 && i === actions.length - 1 ? b.replace('class="action-btn', 'class="action-btn grid-full') : b).join('')}</div>`;

    const burnAlert = isBurned ? `
    <div class="warning-box" style="background:#4a1212;border:1px solid #e54b38;color:#ffcccc;padding:10px;border-radius:6px;margin:8px 0;text-align:center;">
        <strong style="display:block;font-size:13px;color:#ff8877;">${isAr ? '🔥 واحة محروقة' : '🔥 OASIS BURNED'}</strong>
        <small style="display:block;margin-top:4px;">${isAr ? `في حالة خراب لمدة ${Math.ceil(o.burnedRemainingSeconds)} ثانية أخرى · لا يمكن احتلالها أو تطويرها أو الاستفادة منها` : `In ruins for another ${Math.ceil(o.burnedRemainingSeconds)}s · Cannot be claimed, upgraded, or collected`}</small>
    </div>` : isBurning ? `
    <div class="warning-box" style="background:#4a2c12;border:1px solid #e58538;color:#ffe6cc;padding:10px;border-radius:6px;margin:8px 0;text-align:center;">
        <strong style="display:block;font-size:13px;color:#ffaa66;">${isAr ? '🔥 جاري حرق الواحة' : '🔥 ARSON IN PROGRESS'}</strong>
        <small style="display:block;margin-top:4px;">${isAr ? `تقدم الحرق: ${Math.round(o.burningProgress * 100)}% (متبقي ${Math.ceil(90 * (1 - o.burningProgress))} ثانية)` : `Burning: ${Math.round(o.burningProgress * 100)}% (${Math.ceil(90 * (1 - o.burningProgress))}s remaining)`}</small>
        <meter min="0" max="1" value="${o.burningProgress}" style="width:100%;height:10px;margin-top:6px;"></meter>
    </div>` : '';

    const matchingBonus = 50 + o.level * 5;
    const otherBonus = 25 + o.level * 5;
    const levelLabel = o.level > 0 ? (isAr ? `المستوى ${o.level} من 5` : `Level ${o.level} / 5`) : (isAr ? 'بدون مستوى (مستوى 0)' : 'Level 0 (Unranked)');

    return `<div class="oasis-summary"><div class="stat-line"><span>${safe(owner)} · <b>${levelLabel}</b></span>${mine ? `<small>${isAr ? `${o.defenders.toLocaleString()} جندي مدافع` : `${o.defenders.toLocaleString()} defending troops`}</small>` : ''}</div>
    ${burnAlert}
    <h3>${isAr ? 'موارد الواحة' : 'Oasis resources'}</h3><div class="oasis-resource-grid">${resources.map(r=>`<div>${resourceIcon(r)}<b>${o.stock?o.stock[r].toLocaleString():'—'}</b><small>+${o.productionPerMinute[r]} / ${isAr ? 'دقيقة' : 'minute'}</small></div>`).join('')}</div>
    <h3>${isAr ? `مكافأة إنتاج القرية (+${o.level * 5}% من المستوى ${o.level})` : `Village production bonus (+${o.level * 5}% from Level ${o.level})`}</h3><div class="biome-bonuses ${o.blockaded||isBurned?'bonus-suspended':''}">${resources.map(r=>`<div>${resourceIcon(r)}<strong>+${r===o.resource.toLowerCase()?matchingBonus:otherBonus}%</strong></div>`).join('')}</div>${o.blockaded?(isAr?'<small class="negative">مكافأة الإنتاج معلقة بسبب الحصار</small>':'<small class="negative">Production bonus suspended by blockade</small>'):isBurned?(isAr?'<small class="negative">مكافأة الإنتاج معلقة: الواحة محروقة</small>':'<small class="negative">Production bonus suspended: oasis is burned</small>'):''}</div>
    ${garrisonHtml}
    ${o.siegeReportId?`<button data-action="report" data-id="${o.siegeReportId}" class="oasis-danger wide">${isAr ? '⚔ عرض معركة الواحة المباشرة' : '⚔ View live oasis battle'}</button>`:''}
    ${mine && !isBurned ? `<section class="oasis-section"><div class="stat-line"><span><b>${isAr ? 'تطوير الواحة' : 'Oasis Development'}</b></span><small class="positive">${o.level >= 5 ? (isAr ? 'الحد الأقصى المستوى 5' : 'Max Level 5') : (isAr ? `+5% في المستوى ${o.level + 1}` : `+5% at Level ${o.level + 1}`)}</small></div>
    <small>${isAr ? 'ترقية الواحة تضيف 5%+ لمكافأة إنتاج القرية و 5%+ تسريعاً لإنتاج موارد الواحة.' : 'Upgrading the oasis adds +5% village production bonus and +5% faster oasis resource generation.'}</small>
    ${o.level < 5 ? `<button data-action="oasis-level" data-id="${o.id}" class="wide primary action-upgrade-oasis" ${!running || o.underAttack || !o.levelUpgradeCost ? 'disabled' : ''}>${isAr ? `🌴 ترقية الواحة إلى المستوى ${o.level + 1} (${o.levelUpgradeCost ? Object.entries(o.levelUpgradeCost).map(([r, n]) => `${n} ${isAr ? (r === 'wood' ? 'خشب' : r === 'iron' ? 'حديد' : r === 'stone' ? 'حجر' : 'قمح') : r}`).join(' / ') : 'الحد الأقصى'})` : `🌴 Upgrade oasis to Level ${o.level + 1} (${o.levelUpgradeCost ? Object.entries(o.levelUpgradeCost).map(([r, n]) => `${n} ${r}`).join(' / ') : 'Max'})`}</button>` : `<strong class="positive" style="display:block;margin-top:6px;">${isAr ? '✓ تم الوصول للحد الأقصى المستوى 5 (مكافأة +25%)' : '✓ Maximum Level 5 Reached (+25% bonus)'}</strong>`}</section>` : ''}
    ${mine && !isBurned ?`<section class="oasis-section"><small>${isAr ? `سعة كل مورد: ${o.capacity.toLocaleString()}` : `Capacity per resource: ${o.capacity.toLocaleString()}`}</small><small>${isAr ? `${Number((o.wheatUpkeepPerMinute??0).toFixed(1))} قمح / دقيقة لحماة الواحة` : `${Number((o.wheatUpkeepPerMinute??0).toFixed(1))} wheat / minute for oasis defenders`}</small>
    ${btn(o.autoTransport?(isAr?'✓ النقل التلقائي مفعّل':'✓ Automatic transport enabled'):(isAr?'النقل التلقائي متوقف':'Automatic transport paused'),'oasis-auto',!running,`role="switch" aria-checked="${!!o.autoTransport}"`)}<p class="muted">${isAr ? 'تنقل العربات جميع الموارد عند بلوغ الإجمالي 2000. يتوقف النقل أثناء الحصار.' : 'Wagons collect all resource types when the total reaches 2000. A blockade stops collection.'}</p><small>${safe(o.transportStatus)}</small>${btn(isAr ? 'نقل الموارد الآن' : 'Collect resources now','oasis-collect',!running||o.blockaded||!!o.convoyId||total===0)}</section>`:''}
    <div class="panel-footer">${gridHtml}</div>`;
}
