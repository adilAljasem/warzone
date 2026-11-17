import { safe } from './intelligence';
import { unitCatalog } from './units';
import type { WorldView } from './map';
export interface MarketOffer { id: string; ownerId: string; villageId: string; offerResource: string; offerAmount: number; askResource: string; askAmount: number; wagons: number; createdAt: number }
export interface MarketDraft { offerResource: string; offerAmount: number; askResource: string; askAmount: number; swapGiveResource: string; swapTakeResource: string; swapAmount: number }
interface MarketEconomy { villageId: string; capacity: number; resources: { wood:number; iron:number; stone:number; wheat:number }; reservedTradeWagons: number; garrison: Record<string,number>; targets: {name:string;level:number}[]; besieged:boolean }
const resources = ['Wood','Iron','Stone','Wheat'];
const icon = (name:string) => `<svg aria-hidden="true"><use href="#i-${name.toLowerCase()}"/></svg>`;
export function estimateTradeWagons(economy: MarketEconomy, world: WorldView, amount: number): number {
    const city = world.warfare?.settlements.find(c => c.id === economy.villageId);
    const wagonId = `${city?.tribe.toLowerCase() ?? 'romans'}-wagon`;
    const stats = unitCatalog.get(wagonId)?.stats;
    return stats ? Math.ceil(amount / Math.max(1, stats.carryCapacity - Math.ceil(stats.wheatUpkeep / 60 * (2 * Math.SQRT2 * 4410 / stats.speed + 60)))) : 0;
}
export function marketOffersSection(world: WorldView, economy: MarketEconomy, you: string, owner: (id:string|null)=>string, running:boolean) {
    const city=world.warfare?.settlements.find(c=>c.id===economy.villageId);
    const wagonId=`${city?.tribe.toLowerCase()??'romans'}-wagon`;
    const available=economy.garrison[wagonId]??0;
    const wagonsFor=(amount:number)=>estimateTradeWagons(economy,world,amount);
    const offers=world.marketOffers??[], blocked=!running||economy.besieged;
    return `<h3>Open offers</h3><div class="market-offers">${offers.map(o=>{
        const mine=o.ownerId===you, amount=o.askAmount, payment=economy.resources[o.askResource.toLowerCase() as keyof typeof economy.resources];
        return `<article class="market-offer"><small>${safe(owner(o.ownerId))}${mine?' · Your offer':''}</small><div><span>${icon(o.offerResource)}<b>${o.offerAmount.toLocaleString()}</b></span><span>⇄</span><span>${icon(o.askResource)}<b>${o.askAmount.toLocaleString()}</b></span></div><button data-action="${mine?'market-cancel':'market-accept'}" data-id="${o.id}" ${!running||!mine&&(blocked||payment<amount||wagonsFor(amount)>available)?'disabled':''}>${mine?'Cancel offer':'Accept & send caravans'}</button>${!mine?`<small>${wagonsFor(amount)} transport wagons required</small>`:''}</article>`;
    }).join('')||'<p class="muted">No market offers yet.</p>'}</div><h3>Trade caravans</h3>${world.convoys.filter(c=>c.kind==='Trade'&&(c.ownerId===you||c.recipientId===you)).map(c=>`<button class="list-button" data-action="trade-convoy" data-id="${c.id}"><span>${c.returning?'Returning wagons':c.recipientId===you?'Incoming goods':'Outgoing goods'}<small>${c.wagons} wagons · ${Math.ceil(Math.hypot(c.position.x-c.destination.x,c.position.y-c.destination.y)/Math.max(.1,c.speed))} seconds</small></span><b>${c.inBattle?'Under attack':'→'}</b></button>`).join('')||'<p class="muted">No trade caravans in transit.</p>'}<small>Both sides need wheat provisions. Caravans can be intercepted after protection ends. Paid goods are retained even if storage fills during travel.</small>`;
}
export function marketPanel(world: WorldView, economy: MarketEconomy, you: string, draft: MarketDraft, owner: (id:string|null)=>string, running:boolean) {
    const city=world.warfare?.settlements.find(c=>c.id===economy.villageId);
    const wagonId=`${city?.tribe.toLowerCase()??'romans'}-wagon`;
    const available=economy.garrison[wagonId]??0;
    const wagonsFor=(amount:number)=>estimateTradeWagons(economy,world,amount);
    const offers=world.marketOffers??[], own=offers.filter(o=>o.villageId===economy.villageId), slots=economy.targets.find(t=>t.name==='Market')?.level??1;
    const selector=(id:string,labelText:string,value:string)=>`<select id="${id}" aria-label="${labelText}">${resources.map(r=>`<option value="${r}" ${r===value?'selected':''}>${r}</option>`).join('')}</select>`;
    const blocked=!running||economy.besieged;
    const swapAmt=Math.max(0,Math.floor(draft.swapAmount||0)), swapCost=swapAmt*2;
    const giveStock=economy.resources[draft.swapGiveResource.toLowerCase() as keyof typeof economy.resources]??0;
    const takeStock=economy.resources[draft.swapTakeResource.toLowerCase() as keyof typeof economy.resources]??0;
    const swapDisabled=blocked||swapAmt<1||draft.swapGiveResource===draft.swapTakeResource||giveStock<swapCost||takeStock+swapAmt>economy.capacity;
    return `<h3>Instant swap (2:1)</h3><p class="muted">Trade resources instantly without wagons by paying double what you receive.</p><div class="market-create"><label>Give (2x)${selector('market-swap-give','Resource to pay (2x)',draft.swapGiveResource)}<small id="market-swap-cost-note">Cost: ${swapCost.toLocaleString()} ${draft.swapGiveResource} (have ${Math.floor(giveStock).toLocaleString()})</small></label><label>Receive (1x)${selector('market-swap-take','Resource to receive (1x)',draft.swapTakeResource)}<input id="market-swap-amount" aria-label="Amount to receive" type="number" min="1" max="500000" step="1" value="${draft.swapAmount}"/></label></div><button class="primary wide" id="market-swap-btn" data-action="market-swap" ${swapDisabled?'disabled':''}>Swap now (2:1)</button><hr/><h3>Player market</h3><p class="muted">Goods and wagons are reserved when you post. Accepting sends two caravans; resources arrive after travel and wagons return afterward.</p><div class="stat-line"><span>Available wagons</span><b id="market-wagons-available">${available}</b></div><div class="stat-line"><span>Wagons reserved in offers</span><b id="market-wagons-reserved">${economy.reservedTradeWagons}</b></div><div class="market-create"><label>Resource offered${selector('market-offer-resource','Resource offered',draft.offerResource)}<input id="market-offer-amount" aria-label="Quantity offered" type="number" min="1" max="1000000" step="1" value="${draft.offerAmount}"/></label><label>Resource requested${selector('market-ask-resource','Resource requested',draft.askResource)}<input id="market-ask-amount" aria-label="Quantity requested" type="number" min="1" max="1000000" step="1" value="${draft.askAmount}"/></label></div><small id="market-wagon-estimate">${wagonsFor(draft.offerAmount)} transport wagons required</small><button class="primary wide" data-action="market-post" ${blocked||own.length>=slots?'disabled':''}>Post offer</button><small id="market-slots-info">${own.length} / ${slots} offer slots. Upgrade the market for more slots.</small><div id="market-live-section">${marketOffersSection(world,economy,you,owner,running)}</div>`;
}
