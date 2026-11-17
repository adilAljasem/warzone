import {t,currentLanguage} from './locale';
import {unitCatalog,unitPortrait,unitTitle} from './units';
const safe=(s:string)=>s.replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'})[c]!);
const paths:Record<string,string>={attack:'M5 19 19 5M14 5h5v5M4 14l6 6M3 21l3-3',shield:'M12 3 3 7v5c0 5 9 9 9 9s9-4 9-9V7Z',heart:'M12 21 3 12C-2 4 8-1 12 7c4-8 14-3 9 5Z',speed:'M3 8h9M1 12h8M4 16h7M15 3l-4 10h7l-4 8 9-13h-7Z',cargo:'m3 7 9-4 9 4v12l-9 3-9-3Zm0 0 9 4 9-4M12 11v11',wheat:'M12 22V3M12 8Q3 9 5 2q8 0 7 6Zm0 6q10 0 7-8-8 2-7 8Zm0 6q-10 0-7-8 8 2 7 8Z'};
const icon=(name:string)=>`<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${paths[name]}"/></svg>`;
export function initUnitTooltips(){
    const tip=document.createElement('aside');tip.id='unit-tooltip';tip.className='unit-tooltip';tip.hidden=true;tip.setAttribute('role','tooltip');document.body.append(tip);
    let trigger:HTMLElement|null=null,timer=0;
    const hide=()=>{clearTimeout(timer);tip.hidden=true;trigger?.removeAttribute('aria-describedby');trigger=null;if(tip.parentElement!==document.body)document.body.append(tip);};
    const show=(element:HTMLElement)=>{
        const u=unitCatalog.get(element.dataset.helpUnit??'');if(!u)return;clearTimeout(timer);trigger=element;const s=u.stats;
        const host=element.closest('dialog[open]')??document.body;
        if(tip.parentElement!==host)host.append(tip);
        const stats:[string,number,string][]=[['Attack power',s.attack,'attack'],['Defense vs infantry',s.defenseInfantry,'shield'],['Defense vs cavalry',s.defenseCavalry,'shield'],['Hit points',s.hitPoints,'heart'],['Movement speed',s.speed,'speed'],['Carrying capacity',s.carryCapacity,'cargo'],['Wheat / minute',s.wheatUpkeep,'wheat']];
        const counters=(strong:boolean)=>Object.entries(u.matchups).filter(([,v])=>strong?v>1:v<1).map(([role,v])=>`<span>${safe(t(role))}<b dir="ltr">×${v}</b></span>`).join('');
        tip.innerHTML=`<header>${unitPortrait(u.unitId,false)}<div><strong>${safe(unitTitle(u))}</strong><small>${safe(t(u.tribeType))} · ${safe(t(u.role))}</small></div></header><div class="unit-stat-grid">${stats.map(([label,n,i])=>`<span>${icon(i)}<small>${t(label)}</small><b>${n}</b></span>`).join('')}</div><div class="unit-counters"><section class="strong"><strong>${t('Strong against')}</strong>${counters(true)||`<small>${t('Balanced role')}</small>`}</section><section class="weak"><strong>${t('Weak against')}</strong>${counters(false)||`<small>${t('No special penalty')}</small>`}</section></div><footer>${t('Multipliers apply to attack against that unit type. Mixed armies move at their slowest unit speed.')}</footer>`;
        tip.dir=currentLanguage()==='ar'?'rtl':'ltr';tip.hidden=false;element.setAttribute('aria-describedby',tip.id);
        const anchor=element.getBoundingClientRect(),r=tip.getBoundingClientRect();
        const leftPreferred=anchor.left-r.width-8;
        const leftPos=leftPreferred>=8?leftPreferred:Math.min(innerWidth-r.width-8,anchor.right+8);
        tip.style.left=`${Math.max(8,leftPos)}px`;tip.style.top=`${Math.max(8,Math.min(innerHeight-r.height-8,anchor.top))}px`;
    };
    document.addEventListener('pointerover',e=>{const help=(e.target as Element).closest<HTMLElement>('[data-help-unit]');if(help&&help!==trigger)show(help);if((e.target as Element).closest('#unit-tooltip'))clearTimeout(timer);});
    document.addEventListener('pointerout',e=>{if((e.target as Element).closest('[data-help-unit],#unit-tooltip'))timer=window.setTimeout(hide,160);});
    document.addEventListener('focusin',e=>{const help=(e.target as Element).closest<HTMLElement>('[data-help-unit]');if(help)show(help);});
    document.addEventListener('focusout',e=>{if((e.target as Element).closest('[data-help-unit]'))hide();});
    document.addEventListener('click',e=>{const help=(e.target as Element).closest<HTMLElement>('[data-help-unit]');if(help){e.preventDefault();show(help);}else if(!(e.target as Element).closest('#unit-tooltip'))hide();});
    document.addEventListener('keydown',e=>{if(e.key==='Escape'&&!tip.hidden){e.preventDefault();hide();}const help=(e.target as Element).closest<HTMLElement>('[data-help-unit]');if(help&&['Enter',' '].includes(e.key)){e.preventDefault();show(help);}});
    window.addEventListener('resize',hide);window.addEventListener('blur',hide);window.addEventListener('languagechange',hide);
    document.addEventListener('close',hide,true);
}
