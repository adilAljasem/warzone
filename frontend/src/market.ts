import { safe } from './intelligence';
import { unitCatalog } from './units';
import type { WorldView } from './map';
export interface MarketOffer { id: string; ownerId: string; villageId: string; offerResource: string; offerAmount: number; askResource: string; askAmount: number; wagons: number; createdAt: number }
export interface MarketDraft { offerResource: string; offerAmount: number; askResource: string; askAmount: number }
interface MarketEconomy { villageId: string; resources: { wood:number; iron:number; stone:number; wheat:number }; reservedTradeWagons: number; garrison: Record<string,number>; targets: {name:string;level:number}[]; besieged:boolean }
const resources = ['Wood','Iron','Stone','Wheat'];
const icon = (name:string) => `<svg aria-hidden="true"><use href="#i-${name.toLowerCase()}"/></svg>`;
export function marketOffersSection(world: WorldView, economy: MarketEconomy, you: string, owner: (id:string|null)=>string, running:boolean) {
    const city=world.warfare?.settlements.find(c=>c.id===economy.villageId);
    const wagonId=`${city?.tribe.toLowerCase()??'romans'}-wagon`, stats=unitCatalog.get(wagonId)?.stats;
    const available=economy.garrison[wagonId]??0;
    const wagonsFor=(amount:number)=>stats?Math.ceil(amount/Math.max(1,stats.carryCapacity-Math.ceil(stats.wheatUpkeep/60*(2*Math.SQRT2*1940/stats.speed+60)))):0;
    const offers=world.marketOffers??[], blocked=!running||economy.besieged;
    return `<h3>Open offers</h3><div class="market-offers">${offers.map(o=>{
        const mine=o.ownerId===you, amount=o.askAmount, payment=economy.resources[o.askResource.toLowerCase() as keyof typeof economy.resources];
        return `<article class="market-offer"><small>${safe(owner(o.ownerId))}${mine?' · Your offer':''}</small><div><span>${icon(o.offerResource)}<b>${o.offerAmount.toLocaleString()}</b></span><span>⇄</span><span>${icon(o.askResource)}<b>${o.askAmount.toLocaleString()}</b></span></div><button data-action="${mine?'market-cancel':'market-accept'}" data-id="${o.id}" ${!running||!mine&&(blocked||payment<amount||wagonsFor(amount)>available)?'disabled':''}>${mine?'Cancel offer':'Accept & send caravans'}</button>${!mine?`<small>${wagonsFor(amount)} transport wagons required</small>`:''}</article>`;
    }).join('')||'<p class="muted">No market offers yet.</p>'}</div><h3>Trade caravans</h3>${world.convoys.filter(c=>c.kind==='Trade'&&(c.ownerId===you||c.recipientId===you)).map(c=>`<button class="list-button" data-action="trade-convoy" data-id="${c.id}"><span>${c.returning?'Returning wagons':c.recipientId===you?'Incoming goods':'Outgoing goods'}<small>${c.wagons} wagons · ${Math.ceil(Math.hypot(c.position.x-c.destination.x,c.position.y-c.destination.y)/Math.max(.1,c.speed))} seconds</small></span><b>${c.inBattle?'Under attack':'→'}</b></button>`).join('')||'<p class="muted">No trade caravans in transit.</p>'}<small>Both sides need wheat provisions. Caravans can be intercepted after protection ends. Paid goods are retained even if storage fills during travel.</small>`;
}
export function marketPanel(world: WorldView, economy: MarketEconomy, you: string, draft: MarketDraft, owner: (id:string|null)=>string, running:boolean) {
    const city=world.warfare?.settlements.find(c=>c.id===economy.villageId);
    const wagonId=`${city?.tribe.toLowerCase()??'romans'}-wagon`, stats=unitCatalog.get(wagonId)?.stats;
    const available=economy.garrison[wagonId]??0;
    const wagonsFor=(amount:number)=>stats?Math.ceil(amount/Math.max(1,stats.carryCapacity-Math.ceil(stats.wheatUpkeep/60*(2*Math.SQRT2*1940/stats.speed+60)))):0;
    const offers=world.marketOffers??[], own=offers.filter(o=>o.villageId===economy.villageId), slots=economy.targets.find(t=>t.name==='Market')?.level??1;
    const selector=(id:string,value:string)=>`<select id="${id}" aria-label="${id==='market-offer-resource'?'Resource offered':'Resource requested'}">${resources.map(r=>`<option value="${r}" ${r===value?'selected':''}>${r}</option>`).join('')}</select>`;
    const blocked=!running||economy.besieged;
    return `<p class="muted">Goods and wagons are reserved when you post. Accepting sends two caravans; resources arrive after travel and wagons return afterward.</p><div class="stat-line"><span>Available wagons</span><b id="market-wagons-available">${available}</b></div><div class="stat-line"><span>Wagons reserved in offers</span><b id="market-wagons-reserved">${economy.reservedTradeWagons}</b></div><div class="market-create"><label>Resource offered${selector('market-offer-resource',draft.offerResource)}<input id="market-offer-amount" aria-label="Quantity offered" type="number" min="1" max="1000000" step="1" value="${draft.offerAmount}"/></label><label>Resource requested${selector('market-ask-resource',draft.askResource)}<input id="market-ask-amount" aria-label="Quantity requested" type="number" min="1" max="1000000" step="1" value="${draft.askAmount}"/></label></div><small id="market-wagon-estimate">${wagonsFor(draft.offerAmount)} transport wagons required</small><button class="primary wide" data-action="market-post" ${blocked||own.length>=slots?'disabled':''}>Post offer</button><small id="market-slots-info">${own.length} / ${slots} offer slots. Upgrade the market for more slots.</small><div id="market-live-section">${marketOffersSection(world,economy,you,owner,running)}</div>`;
}
