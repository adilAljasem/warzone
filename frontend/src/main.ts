import { oasisPanel } from "./oasis-panel";
import { activeEvents, LiveEventBar } from "./live-events";
import { AlertInbox } from "./alerts";
import { currentLanguage, setLanguage, localizeNow, t } from "./locale";
import { settlementStage } from "./settlements";
import { biomeAt } from "./landscape";
import { loadUnits, unitPortrait, unitArtwork, unitCatalog, provisionEstimate, initUnitTooltips } from "./units";
import "./style.css";
import "./gameplay-v3.css";
import "./command-deck.css";
import "./progression.css";
import "./social.css";
import { ProgressFeedback } from './progression';
import { GameAudio } from './game-audio';
import { GameMenu } from './game-menu';
import './game-menu.css';
import { type Production, type Report, productionPanel, musterPanel, reportsPanel, reportPanel, liveReport, cargoView } from "./intelligence";
import { HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";
import { WorldMap, type MapPlayer, type MapSelection, type WorldView, type RankView, type ArmyView } from "./map";
import { buildingArt } from "./art";
import { marketPanel, marketOffersSection, type MarketDraft } from "./market";
interface Resources {
    wood: number;
    iron: number;
    stone: number;
    wheat: number;
}
interface Target {
    unitId?: string;
    trainingBuilding?: string;
    requiredBuildingLevel: number;
    unlocked: boolean;
    trainingSpeedBonusPercent: number;
    id: string;
    name: string;
    kind: string;
    level: number;
    maxLevel: number;
    auto: boolean;
    upgradeCost: Resources | null;
    trainingCost: Resources | null;
    trainingSeconds: number | null;
    iconUrl: string | null;
}
interface Job {
    targetId: string;
    lane: string;
    quantity: number;
    completesAt: number;
    startedAt: number;
}
interface Economy {
    biome: string;
    ownedOases: number;
    oasisBonuses: number[];
    reservedTradeWagons: number;
    production: Production[];
    villageId: string;
    resources: Resources;
    capacity: number;
    autoSlots: number;
    usedAutoSlots: number;
    targets: Target[];
    jobs: Job[];
    garrison: Record<string, number>;
    wounded: number;
    besieged: boolean;
}
interface Settings {
    capacity: number;
    durationMinutes: number;
    shieldMinutes: number;
    resourceSpeed: number;
    botCount: number;
    botDifficulty: string;
}
interface Player extends MapPlayer {
    tribe: string;
    isBot: boolean;
    ready: boolean;
    online: boolean;
}
interface ChatMessage { id: number; senderId: string | null; text: string; at: number; kind: string }
interface Alliance { firstPlayerId: string; secondPlayerId: string; since: number }
interface AllianceRequest { id: string; senderId: string; recipientId: string; createdAt: number }
interface Snapshot {
    id: string;
    name: string;
    hostId: string;
    you: string;
    revision: number;
    status: string;
    settings: Settings;
    elapsedSeconds: number;
    remainingSeconds: number;
    shieldRemainingSeconds: number;
    players: Player[];
    chat: ChatMessage[];
    alliances: Alliance[];
    allianceRequests: AllianceRequest[];
    economy: Economy | null;
    world: WorldView | null;
    result: {
        reason: string;
        winnerIds: string[];
        rankings: RankView[];
    } | null;
}
interface RoomSummary {
    id: string;
    name: string;
    status: string;
    humans: number;
    bots: number;
    capacity: number;
}
interface CommandResult {
    accepted: boolean;
    code: string;
    message: string;
}
type Panel = "alerts" | "biome" | "muster" | "reports" | "report" | "production" | "building" | "village" | "army" | "armies" | "dispatch" | "standings" | "lobby" | "rooms" | "oasis" | "ground" | "convoy" | "castle";
const el = <T extends HTMLElement = HTMLElement>(id: string) => document.getElementById(id)! as T;
const esc = (text: unknown) => String(text ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]!);
const icon = (name: string) => `<svg aria-hidden="true"><use href="#i-${name}"/></svg>`;
const time = (seconds: number) => { const n = Math.max(0, Math.ceil(seconds)); return `${Math.floor(n / 60)}:${String(n % 60).padStart(2, "0")}`; };
const pretty = (name: string) => name.replace(/([a-z])([A-Z])/g, "$1 $2");
const unitId = (target: Target) => target.unitId ?? target.iconUrl?.split("/")[3] ?? "";
const role = (id: string) => id.split("-").at(-1) ?? "";
const facility = (id: string) => unitCatalog.get(id)?.trainingBuilding ?? ({ infantry: "Barracks", archer: "Barracks", spearman: "Barracks", swordsman: "Barracks", crossbowman: "Barracks", cavalry: "Stable", lightcavalry: "Stable", heavycavalry: "Stable", scout: "Stable", wagon: "Stable", catapult: "SiegeWorkshop", ram: "SiegeWorkshop", siegetower: "SiegeWorkshop", governor: "MainHall" } as Record<string, string>)[role(id)];
const connection = new HubConnectionBuilder().withUrl("/game").withAutomaticReconnect([0, 2000, 5000, 10000]).configureLogging(LogLevel.Warning).build();
initUnitTooltips();
const alertInbox = new AlertInbox();
const liveEvents = new LiveEventBar();
const gameAudio = new GameAudio();
let returnedToMenu=false;
try{returnedToMenu=sessionStorage.getItem('warzone.returned-to-menu')==='true';}catch{/* Session storage is optional. */}
const resumeMatch=document.createElement('section');resumeMatch.id='resume-match';resumeMatch.hidden=true;
resumeMatch.innerHTML='<p class="menu-note">Your match is still running. Return whenever you are ready.</p><button id="resume-game" class="primary wide">Resume match</button>';
el('play-solo').before(resumeMatch);
function setReturnedToMenu(value:boolean){
    returnedToMenu=value;el('welcome').classList.toggle('away',value);resumeMatch.hidden=!value;
    try{if(value)sessionStorage.setItem('warzone.returned-to-menu','true');else sessionStorage.removeItem('warzone.returned-to-menu');}catch{/* Keep the current session state. */}
}
setReturnedToMenu(returnedToMenu);
let alertBanner = "";
function renderImportantAlerts(){
 const box=el("important-alerts"); if(!state){box.hidden=true;return;}
 alertInbox.use(state.id,state.you);
 alertInbox.names=new Map([...(state.world?.oases??[]).map(o=>[o.id,o.name+' · '+Math.round(o.position.x)+':'+Math.round(o.position.y)] as [string,string]),...(state.world?.warfare?.settlements??[]).map(c=>[c.id,owner(c.ownerId)+"'s village"] as [string,string])]);const html=liveEvents.render(state.status==="Running"&&state.world?activeEvents(state.world,state.you):[]);
 box.hidden=!html; el("game").classList.toggle("has-live-events",!!html);if(html!==alertBanner){box.innerHTML=html;alertBanner=html;localizeNow(box);}
}
let reportDetail = "";
let state: Snapshot | null = null, panel: Panel | null = null, panelId = "", panelTab = "overview", selectedUnit = "", activeArmy = "", roomList: RoomSummary[] = [];
let selection: MapSelection | null = null, busy = false, connecting = false, page = 0, toastTimer = 0;
let amount = 5, afterAttack = "Return", supplySource = "", supplyWagons = 0, escorts = 2, autoSupply = true;
let attackObjective = "Raid";
let dispatchMission: "attack" | "reinforce" | "blockade" | "scout" | "camp" | "burn" = "attack";
const marketDraft: MarketDraft = { offerResource: "Wood", offerAmount: 500, askResource: "Iron", askAmount: 500 };
const transferDraft: Resources = { wood: 100, iron: 100, stone: 100, wheat: 100 };
let transferSource = "", chatOpen = window.innerWidth > 700, chatKey = "";
let dispatchAuto = true, campLoadedOnly = true, dispatchArmy = "new", preferredDispatchArmy = "", formation: Record<string, number> = {};
let clockReceived = performance.now();
const now = () => state ? state.elapsedSeconds + (state.status === "Running" ? Math.min(2, (performance.now() - clockReceived) / 1000) : 0) : 0;
const ownArmies = () => state?.world?.armies.filter(a => a.ownerId === state!.you) ?? [];
const army = () => ownArmies().find(a => a.id === activeArmy);
const owner = (id: string | null | undefined) => state?.players.find(p => p.id === id)?.name ?? "Unclaimed";
const running = () => state?.status === "Running" && !busy && connection.state === HubConnectionState.Connected;
const button = (label: string, action: string, id = "", style = "", disabled = false) => `<button data-action="${action}" data-id="${esc(id)}" class="${style}" ${disabled ? "disabled" : ""}>${label}</button>`;
const select = (id: string, label: string, items: (string | {value:string;label:string})[], chosen: string) => `<fieldset class="choice-field"><legend>${label}</legend><input type="hidden" id="${id}" value="${esc(chosen)}"/><div class="choice-grid" role="group" aria-label="${label}">${items.map(item=>{const i=typeof item==='string'?{value:item,label:pretty(item)}:item;return `<button type="button" class="choice-tile ${i.value===chosen?'active':''}" data-choice="${id}" data-value="${esc(i.value)}" aria-pressed="${i.value===chosen}"><span class="choice-check" aria-hidden="true">${i.value===chosen?'&#10003;':''}</span>${esc(i.label)}</button>`;}).join('')}</div></fieldset>`;
const map = new WorldMap(el("map"), pick => {
    if (!state?.world) return;
    selection = pick; map.select(pick); page = 0;
    if(pick.kind==="battle"){buildingOasisId=null;openReport(pick.id);return;}
    if (pick.kind === "village" && pick.ownerId === state.you) {
        buildingOasisId=null;
        void perform(async () => { if (state?.economy?.villageId !== pick.id) await command("SelectVillage", pick.id); openPanel("village", pick.id); renderBuildings(); });
    } else if (pick.kind === "army") {
        buildingOasisId=null;
        if (pick.ownerId === state.you) { activeArmy = pick.id; loadArmy(); }
        openPanel("army", pick.id);
    } else if (pick.kind === "ground") {
        buildingOasisId=null;
        openPanel("biome", String(biomeAt(state.world.regions ?? [], pick.position)?.id ?? ""));
    }
    else {
        if (pick.kind !== "oasis") buildingOasisId=null;
        openPanel(pick.kind, pick.id);
    }
});
const progressFeedback = new ProgressFeedback((point,message)=>map.celebrate(point,message));
gameAudio.setScene(()=>map.combatSounds());
const gameMenu=new GameMenu(gameAudio,()=>state?.status??null,async()=>{
    if(busy||connecting)throw Error('Wait for your current order to finish.');
    if(!state)return;
    if(connection.state===HubConnectionState.Disconnected)await connect(true);
    if(connection.state!==HubConnectionState.Connected)throw Error('Reconnect to give orders.');
    await connection.invoke('LeaveRoom');
    setReturnedToMenu(false);
    gameAudio.reset();closePanel();selection=null;map.select(null);map.home();render(null);
});
const menuButton=document.createElement('button');menuButton.id='open-game-menu';menuButton.textContent='☰';menuButton.setAttribute('aria-label','Game menu');menuButton.title='Game menu';menuButton.onclick=()=>gameMenu.open();el('world-tools').prepend(menuButton);
function currentTribe() { return state?.world?.warfare?.settlements.find(c => c.id === state?.economy?.villageId)?.tribe ?? "Romans"; }
const seenReports: Record<string, string> = {};
function reportVersion(r: Report) { return r.status; }
function reportKey(r: Report) { return `warzone:report:${state?.id}:${state?.you}:${r.id}`; }
function unreadReport(r: Report) { const key = reportKey(r); if (!(key in seenReports)) { try { seenReports[key] = localStorage.getItem(key) ?? ""; } catch { seenReports[key] = ""; } } return seenReports[key] !== reportVersion(r); }
function readReport(r: Report) {
    for (const item of state?.world?.reports ?? [r]) {
        if (item.id === r.id || item.parentReportId === r.id || (r.parentReportId && item.id === r.parentReportId)) {
            const key = reportKey(item);
            seenReports[key] = reportVersion(item);
            try { localStorage.setItem(key, reportVersion(item)); } catch { /* Keep in-memory read state when storage is unavailable. */ }
        }
    }
}
function openReport(id: string) { const r = state?.world?.reports?.find(r => r.id === id); if(r?.parentReportId&&state?.world?.reports?.some(p=>p.id===r.parentReportId)){openReport(r.parentReportId);return;} if (r) readReport(r); openPanel("report", id); renderIntelligence(); }
function renderIntelligence() {
    const muster = state?.world?.muster;
    el("muster-hud").hidden = !muster;
    if (muster) el("muster-hud").innerHTML = `${icon("sword")}<span><strong>${muster.total.toLocaleString()}</strong><small>${Math.round(muster.upkeepPerMinute).toLocaleString()} wheat/min</small></span>`;
    const reports = state?.world?.reports ?? [];
    const unreadCount = reports.filter(r=>(!r.parentReportId||!reports.some(p=>p.id===r.parentReportId))&&unreadReport(r)).length;
    const badge = el("report-count");
    badge.textContent = unreadCount > 0 ? String(unreadCount) : "";
    badge.hidden = unreadCount === 0;
    const city = state?.world?.warfare?.settlements.find(c => c.id === state?.economy?.villageId);
    const biome = state?.world?.regions?.find(r=>r.kind===city?.biome);el('village-biome').hidden=!biome;
    if(biome){el('village-biome').textContent=biome.kind;el('village-biome').dataset.id=String(biome.id);}
}
void map.init().catch(error => { console.error("World renderer initialization failed", error); el("map").innerHTML = '<p class="map-error">The world could not load. Refresh to try again. Building controls remain available.</p>'; });
function notice(text: string, error = false) { el("notice").textContent = text; el("notice").hidden = false; el("notice").classList.toggle("error", error); clearTimeout(toastTimer); toastTimer = window.setTimeout(() => el("notice").hidden = true, 4500); }
function status() { el("connection").textContent = returnedToMenu ? 'Away from match' : connection.state === HubConnectionState.Connected ? "● Connected" : "○ Reconnecting"; el("reconnect").hidden = returnedToMenu || connection.state !== HubConnectionState.Disconnected; }
async function perform(action: () => Promise<void>) { if (busy)
    return; busy = true; try {
    if (connection.state === HubConnectionState.Disconnected)
        await connect(true);
    if (connection.state !== HubConnectionState.Connected)
        throw Error("Reconnect to give orders.");
    await action();
}
catch (e) {
    notice(e instanceof Error ? e.message : String(e), true);
}
finally {
    busy = false;
    renderBuildings(); renderDrawer(true);
} }
async function command(kind: string, targetId: string, extra: Record<string, unknown> = {}) { const result = await connection.invoke<CommandResult>("SendCommand", { requestId: crypto.randomUUID(), kind, targetId, quantity: 1, enabled: true, ...extra }); if (!result.accepted)
    notice(result.message, true);
else
    notice(kind === "Train" ? "Training started." : kind === "Upgrade" ? "Upgrade completed." : "Orders received."); render(await connection.invoke<Snapshot | null>("Resume")); return result.accepted; }
function showMap() { closePanel(); map.home(); }
function renderChat() {
    const root = el("world-chat"), visible = !!state?.world;
    root.hidden = !visible;
    root.classList.toggle("open", chatOpen);
    el("chat-content").hidden = !chatOpen;
    el("chat-toggle").setAttribute("aria-expanded", String(chatOpen));
    el("chat-toggle").lastElementChild!.textContent = chatOpen ? "−" : "+";
    if (!state) { chatKey = ""; return; }
    const messages = state.chat ?? [], list = el("chat-messages");
    const key = `${currentLanguage()}|${messages.map(m => `${m.id}:${m.text}`).join("|")}`;
    if (key !== chatKey) {
        const follow = !chatKey || list.scrollHeight - list.scrollTop - list.clientHeight < 50;
        list.innerHTML = messages.length ? messages.map(message => {
            const system = message.kind !== "Player", mine = message.senderId === state!.you;
            const text = system ? t(message.text) : message.text;
            return `<article class="chat-message ${system ? "system" : mine ? "own" : ""}"><div class="chat-author"><span>${esc(system ? "Realm" : owner(message.senderId))}</span><time>${time(message.at)}</time></div><div class="chat-text" data-no-translate>${esc(text)}</div></article>`;
        }).join("") : '<p class="chat-empty">No messages yet. Start the conversation.</p>';
        chatKey = key;
        if (follow) list.scrollTop = list.scrollHeight;
        localizeNow(list);
    }
    const canSend = state.status === "Running" && connection.state === HubConnectionState.Connected;
    el<HTMLInputElement>("chat-input").disabled = !canSend;
    el("chat-form").querySelector<HTMLButtonElement>("button")!.disabled = !canSend;
}
function renderWorld() {
    const inGame = !!state?.world;
    el("world-tools").hidden = !inGame; el("command-deck").hidden = !inGame;
    el("scene-title").textContent = "Sunmeadow Valley"; el("scene-eyebrow").textContent = "THE WORLD BEYOND YOUR WALLS";
    el("scene-hint").textContent = "Select your village to manage buildings. Capture oases to increase production.";
    renderBuildings(); renderChat();
}
let buildingOasisId: string | null = null;
let buildingKey = "";
function renderBuildings() {
    const e = state?.economy, strip = el("building-strip");
    const oasis=state?.world?.oases.find(o=>o.id===buildingOasisId);
    const mainHallLevel = e?.targets.find(t => t.kind === "Building" && t.name === "MainHall")?.level ?? 1;
    const key = JSON.stringify([oasis&&[oasis.id,oasis.wallLevel,oasis.wallHealth,oasis.underAttack,oasis.ownerId,oasis.level,oasis.burnedRemainingSeconds,oasis.wallUpgradeCost&&affordable(oasis.wallUpgradeCost),oasis.levelUpgradeCost&&affordable(oasis.levelUpgradeCost)],e?.villageId, mainHallLevel, e?.targets.filter(t => t.kind === 'Building').map(t => [t.id,t.level,t.upgradeCost && affordable(t.upgradeCost)]), running(), panelId, currentTribe(), currentLanguage()]);
    if (key === buildingKey) return; buildingKey = key;
    if(oasis){
        const isAr = currentLanguage() === 'ar';
        const owned=oasis.ownerId===state?.you, canPay=e?.villageId!==oasis.villageId||affordable(oasis.wallUpgradeCost);
        const canPayLevel=e?.villageId!==oasis.villageId||affordable(oasis.levelUpgradeCost);
        const isBurned = (oasis.burnedRemainingSeconds ?? 0) > 0;
        const maxReached = isAr ? 'تم الوصول للحد الأقصى للمستوى' : 'Maximum level reached';
        const wallTitle = oasis.wallUpgradeCost ? Object.entries(oasis.wallUpgradeCost).map(([r,n])=>`${n} ${isAr ? t(r) : r}`).join(' / ') : maxReached;
        const levelTitle = !oasis.levelUpgradeCost ? maxReached : Object.entries(oasis.levelUpgradeCost).map(([r,n])=>`${n} ${isAr ? t(r) : r}`).join(' / ');
        const wallCardName = isAr ? 'سور الواحة' : 'Oasis wall';
        const levelCardName = isAr ? 'مستوى الواحة' : 'Oasis level';
        const levelSmall = isAr ? 'المستوى' : 'Level';
        const ribbonStats = isAr
            ? `المستوى ${oasis.level || 0} من 5 (+${(oasis.level || 0) * 5}٪) · ${oasis.wallLevel ? Math.ceil(oasis.wallHealth) + ' / ' + oasis.wallMaximum + ' سور' : 'بدون سور'}`
            : `Level ${oasis.level || 0} / 5 (+${(oasis.level || 0) * 5}%) · ${oasis.wallLevel ? Math.ceil(oasis.wallHealth) + ' / ' + oasis.wallMaximum + ' Wall' : 'No wall'}`;
        const ribbonStatus = isBurned
            ? (isAr ? 'الواحة محروقة وفي حالة خراب.' : 'Oasis is burned to ashes.')
            : owned
            ? (isAr ? 'استخدم + لترقية مستوى الواحة أو سورها.' : 'Use + to upgrade oasis level or wall.')
            : (isAr ? 'هذه الواحة تابعة لقائد آخر.' : 'This oasis belongs to another commander.');
        strip.innerHTML=`<article class="building-card oasis-wall-card"><button class="building-open" data-action="oasis-wall-info" data-id="${oasis.id}">${buildingArt('Wall',currentTribe())}<span class="building-name">${wallCardName}</span><strong class="building-level"><small>${levelSmall}</small> ${oasis.wallLevel}</strong></button><button class="building-upgrade" data-action="oasis-wall" data-id="${oasis.id}" aria-label="${isAr ? 'ترقية سور الواحة' : 'Upgrade oasis wall'}" title="${wallTitle}" ${!running()||!owned||oasis.underAttack||!oasis.wallUpgradeCost||!canPay||isBurned?'disabled':''}>+</button></article>
        <article class="building-card oasis-level-card"><button class="building-open" data-action="oasis-wall-info" data-id="${oasis.id}"><span style="font-size:26px;display:flex;align-items:center;justify-content:center;height:48px;">🌴</span><span class="building-name">${levelCardName}</span><strong class="building-level"><small>${levelSmall}</small> ${oasis.level || 0}</strong></button><button class="building-upgrade" data-action="oasis-level" data-id="${oasis.id}" aria-label="${isAr ? 'ترقية مستوى الواحة' : 'Upgrade oasis level'}" title="${levelTitle}" ${!running()||!owned||oasis.underAttack||!oasis.levelUpgradeCost||!canPayLevel||isBurned?'disabled':''}>+</button></article>
        <div class="oasis-ribbon-info"><strong>${ribbonStats}</strong><small>${ribbonStatus}</small></div>`;
        localizeNow(strip);return;
    }
    strip.innerHTML = e ? e.targets.filter(t => t.kind === "Building").map(target => {
        const capped = target.name !== "MainHall" && target.level >= mainHallLevel;
        const title = !target.upgradeCost ? "Maximum level reached" : capped ? `Requires Main Hall level ${target.level + 1}` : Object.entries(target.upgradeCost).map(([k,v])=>v+' '+k).join(' / ');
        return `<article class="building-card ${panelId===target.id?'selected':''}"><button class="building-open" data-action="building" data-id="${target.id}" aria-label="${esc(pretty(target.name))}">${buildingArt(target.name,currentTribe())}<span class="building-name">${pretty(target.name)}</span><strong class="building-level"><small>Level</small> ${target.level}</strong></button><button class="building-upgrade" data-action="upgrade" data-id="${target.id}" aria-label="Upgrade ${esc(pretty(target.name))}" title="${title}" ${!running()||!target.upgradeCost||capped||!affordable(target.upgradeCost)?'disabled':''}>+</button></article>`;
    }).join('') : '';
    localizeNow(strip);
}
function openPanel(kind: Panel, id = "", tab = "overview") { buildingOasisId = kind === "oasis" ? id : null; reportDetail = ""; panel = kind; panelId = id; panelTab = tab; page = 0; el("drawer").hidden = false; el("game").classList.add("has-drawer"); renderDrawer(true); renderBuildings(); renderArmiesStrip(); }
function closePanel() { buildingOasisId = null; panel = null; el("drawer").hidden = true; el("game").classList.remove("has-drawer"); renderBuildings(); renderArmiesStrip(); }
function openBuilding(id: string) { const t = state?.economy?.targets.find(t => t.id === id); if (!t)
    return; selectedUnit = ""; openPanel("building", id, t.name === "Market" ? "trade" : ["Barracks", "Stable", "SiegeWorkshop"].includes(t.name) ? "training" : "overview"); }
function loadArmy() { const a = army(); if (!a)
    return; afterAttack = a.afterAttack ?? "Hold"; supplySource = a.supplyVillageId; supplyWagons = a.supplyWagons; escorts = a.escorts; autoSupply = a.autoSupply;
}
function costs(cost: Resources | null, multiplier = 1) { if (!cost)
    return '<small>Maximum level reached</small>'; return `<div class="costs">${Object.entries(cost).map(([name, value]) => `<span class="${value * multiplier > (state?.economy?.resources[name as keyof Resources] ?? Infinity) ? "unaffordable" : ""}" title="${name}">${icon(name)}${Math.ceil(value * multiplier).toLocaleString()}</span>`).join("")}</div>`; }
function affordable(cost: Resources | null, multiplier = 1) { return !!cost && Object.entries(cost).every(([name, n]) => n * multiplier <= (state?.economy?.resources[name as keyof Resources] ?? 0)); }
function render(snapshot: Snapshot | null) { if (snapshot && snapshot.id === state?.id && snapshot.revision < state.revision)
    return; const previous = state; state = snapshot; gameAudio.update(snapshot); clockReceived = performance.now(); el("welcome").hidden = !!snapshot || panel === "rooms"; if (!snapshot) {
    el('resources').innerHTML='';el('muster-hud').hidden=true;el('village-biome').hidden=true;
    const armyStrip = document.getElementById('army-strip'); if (armyStrip) armyStrip.innerHTML = '';
    alertBanner = ""; el("important-alerts").innerHTML = ""; el("important-alerts").hidden = true; el("game").classList.remove("has-live-events");
    el("work-queue").innerHTML = "";
    el("match-clock").textContent = "A new adventure";
    map.update(null, [], "preview");
    progressFeedback.update(previous,null);
    renderWorld();
    status();
    return;
} if (!previous?.world && snapshot.world) {
    closePanel();
    notice("Select your village to manage buildings. Capture oases to increase production.");
} if (snapshot.status === "Lobby" && panel !== "lobby")
    openPanel("lobby"); if (!ownArmies().some(a => a.id === activeArmy)) {
    activeArmy = ownArmies()[0]?.id ?? "";
    loadArmy();
} const economy = snapshot.economy; el("resources").innerHTML = economy ? Object.entries(economy.resources).map(([name, count]) => {
    const p = economy.production?.find(p => p.resource.toLowerCase() === name);
    return `<button class="resource" data-action="production" data-id="${name}" title="${p ? `${p.netPerMinute.toFixed(1)} ${name}/minute net / ${p.percent.toFixed(1)}% of normal output` : name}">${icon(name)}<span><strong>${count.toLocaleString()}<i class="production-percent ${(p?.netPerMinute ?? 0) < 0 ? "negative" : "positive"}">${(p?.percent ?? 100).toFixed(0)}%</i></strong><small>${name.toUpperCase()}</small></span></button>`;
}).join("") : ""; const armyCountBadge = document.getElementById("army-count"); if (armyCountBadge) armyCountBadge.textContent = String(ownArmies().length); map.setAlliances(snapshot.alliances, snapshot.you); map.update(snapshot.world, snapshot.players, snapshot.you); renderWorld(); renderPossessions(); renderArmiesStrip(); renderBuildings(); renderImportantAlerts(); renderTimers(); renderDrawer(); renderIntelligence(); status(); progressFeedback.update(previous,snapshot); if (snapshot.result && !previous?.result)
    openPanel("standings"); }
function renderPossessions() {
    const container = el("realm-possessions");
    if (!container) return;
    if (!state?.you || !state?.world || state.status !== "Running") {
        container.innerHTML = "";
        return;
    }
    const myVillages = (state.world.warfare?.settlements ?? []).filter(s => s.ownerId === state!.you && !s.ruined);
    const myOases = (state.world.oases ?? []).filter(o => o.ownerId === state!.you);
    if (myVillages.length === 0 && myOases.length === 0) {
        container.innerHTML = "";
        return;
    }
    const villageBadges = myVillages.map((v, i) => {
        const isCurrent = state?.economy?.villageId === v.id;
        return `<button class="possession-badge village-badge ${isCurrent ? 'active' : ''}" data-action="focus-possession" data-type="village" data-id="${v.id}" title="Province ${i + 1} · Level ${v.level}">
            <span class="badge-icon">🏛</span><span class="badge-tag">P${i + 1}</span>
        </button>`;
    }).join("");
    const oasisBadges = myOases.map(o => {
        const res = o.resource.toLowerCase();
        return `<button class="possession-badge oasis-badge oasis-${res}" data-action="focus-possession" data-type="oasis" data-id="${o.id}" title="${esc(o.name)} · +50% ${o.resource}">
            <span class="badge-icon">${icon(res)}</span><span class="badge-tag">${o.resource[0]}</span>
        </button>`;
    }).join("");
    container.innerHTML = `<div class="possession-strip">${villageBadges}${oasisBadges}</div>`;
    localizeNow(container);
}
function getArmyStatus(a: ArmyView, isReinforcement: boolean): { kind: "defender" | "attacker" | "idle" | "other"; label: string } {
    if (!state?.world) return { kind: "idle", label: "Idle" };

    const battles = state.world.warfare?.battles ?? [];
    for (const b of battles) {
        if (b.status !== "Active") continue;
        if (b.defenderId === a.id || b.defenderArmyIds?.includes(a.id)) {
            return { kind: "defender", label: "Defending" };
        }
        if (b.attackerId === a.id || b.attackerArmyIds?.includes(a.id)) {
            return { kind: "attacker", label: "Attacking" };
        }
    }

    const oasisSieges = state.world.oasisSieges ?? [];
    for (const os of oasisSieges) {
        if (os.armyId === a.id || os.armyIds?.includes(a.id) || (os.attackerOwners?.includes(a.ownerId) && a.order === "Sieging")) {
            return { kind: "attacker", label: "Sieging Oasis" };
        }
        if (a.defendingSiteId === os.oasisId && (a.order === "Guarding" || a.order === "Battle")) {
            return { kind: "defender", label: "Defending Oasis" };
        }
    }

    const sieges = state.world.warfare?.sieges ?? [];
    for (const s of sieges) {
        if (s.status !== "Active") continue;
        if (s.armyId === a.id || s.armyIds?.includes(a.id)) {
            return { kind: "attacker", label: "Sieging City" };
        }
        if (a.defendingSiteId === s.villageId && (a.order === "Guarding" || a.order === "Battle")) {
            return { kind: "defender", label: "Defending City" };
        }
    }

    if (["Attacking", "Approaching", "Sieging", "Intercepting"].includes(a.order)) {
        return { kind: "attacker", label: a.order };
    }

    if (a.order === "Battle") {
        if (a.defendingSiteId || isReinforcement) return { kind: "defender", label: "Defending" };
        return { kind: "attacker", label: "Attacking" };
    }

    if (a.order === "Idle" || a.order === "Camped" || a.order === "Guarding") {
        return { kind: "idle", label: a.order };
    }

    return { kind: "other", label: a.order };
}
function renderArmiesStrip() {
    const strip = document.getElementById("army-strip");
    if (!strip) return;
    if (!state?.you || !state?.world || state.status !== "Running") {
        strip.innerHTML = "";
        return;
    }

    const myVillages = (state.world.warfare?.settlements ?? []).filter(s => s.ownerId === state!.you && !s.ruined);
    const myVillageIds = new Set(myVillages.map(v => v.id));
    const myOases = (state.world.oases ?? []).filter(o => o.ownerId === state!.you);
    const myOasisIds = new Set(myOases.map(o => o.id));

    const foreignReinforcements = (state.world.armies ?? []).filter(a => {
        if (a.ownerId === state!.you) return false;
        if (a.supportedOwnerId === state!.you) return true;
        if (a.defendingSiteId && (myVillageIds.has(a.defendingSiteId) || myOasisIds.has(a.defendingSiteId))) return true;
        return false;
    });

    const myArmies = ownArmies();
    const shelf = document.querySelector('.army-shelf') as HTMLElement;
    if (myArmies.length === 0 && foreignReinforcements.length === 0) {
        if (shelf) shelf.hidden = true;
        strip.innerHTML = "";
        return;
    }
    if (shelf) shelf.hidden = false;

    const cards: string[] = [];

    // 1. Own armies
    myArmies.forEach((a, index) => {
        const isSelected = activeArmy === a.id || selection?.id === a.id;
        const status = getArmyStatus(a, false);
        let statusBadge = "";
        if (status.kind === "defender") {
            statusBadge = `<span class="army-status-badge status-defender" title="${t('Defending in battle')}"><svg viewBox="0 0 24 24"><path d="M12 2 3 6v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V6Z" fill="#38bdf8"/><path d="M12 5 5 8v4c0 4.2 2.9 8.1 7 9.2 4.1-1.1 7-5 7-9.2V8Z" fill="#0284c7"/></svg></span>`;
        } else if (status.kind === "attacker") {
            statusBadge = `<span class="army-status-badge status-attacker" title="${t('Attacking')}"><svg viewBox="0 0 24 24"><path d="m14 4 6 6-11 11-5-1 1-5L15 4Zm4-2 3 3-2 2-3-3 2-2Z" fill="#ef4444"/><path d="m10 4-6 6 11 11 5-1-1-5L9 4Zm-4-2-3 3 2 2 3-3-2-2Z" fill="#f87171"/></svg></span>`;
        } else if (status.kind === "idle") {
            statusBadge = `<span class="army-status-badge status-idle" title="${t('Idle')}"><span class="badge-zzz">ZzZz</span></span>`;
        } else {
            statusBadge = `<span class="army-status-badge status-moving" title="${esc(t(status.label))}"><span class="badge-move">${a.order === "Returning" ? "↩" : "➜"}</span></span>`;
        }

        const formation = a.formation ?? {};
        const entries = Object.entries(formation).filter(([, n]) => n > 0).sort((x, y) => y[1] - x[1]);
        const topUnitId = entries[0]?.[0];

        let leadArtHtml = "";
        if (topUnitId) {
            leadArtHtml = `<div class="army-lead-art" title="${esc(unitCatalog.get(topUnitId)?.displayName ?? topUnitId)}">${unitArtwork(topUnitId)}</div>`;
        } else if (a.wagons > 0 && a.soldiers === 0) {
            leadArtHtml = `<div class="army-lead-art lead-icon" title="${t('Wagons')}">🛒</div>`;
        } else {
            leadArtHtml = `<div class="army-lead-art lead-icon" title="${t('Troops')}">⚔</div>`;
        }

        const armyTitle = `${currentLanguage() === 'ar' ? 'جيش' : 'Army'} ${index + 1}`;
        const totalUnits = a.soldiers + a.wagons;
        const countHtml = `<strong class="army-total-count">${totalUnits.toLocaleString()}</strong>`;

        cards.push(`<article class="army-card ${isSelected ? 'selected' : ''}">
            <button class="army-card-btn" data-action="focus-army" data-id="${a.id}" aria-label="${esc(armyTitle)}">
                <div class="army-card-header">
                    <span class="army-name-tag">${esc(armyTitle)}</span>
                    ${statusBadge}
                </div>
                ${leadArtHtml}
                <div class="army-card-bottom">${countHtml}</div>
            </button>
        </article>`);
    });

    // 2. Foreign reinforcements stationed in our villages or oases
    foreignReinforcements.forEach((a) => {
        const isSelected = selection?.id === a.id;
        const status = getArmyStatus(a, true);
        let statusBadge = "";
        if (status.kind === "defender") {
            statusBadge = `<span class="army-status-badge status-defender" title="${t('Defending in battle')}"><svg viewBox="0 0 24 24"><path d="M12 2 3 6v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V6Z" fill="#38bdf8"/><path d="M12 5 5 8v4c0 4.2 2.9 8.1 7 9.2 4.1-1.1 7-5 7-9.2V8Z" fill="#0284c7"/></svg></span>`;
        } else if (status.kind === "attacker") {
            statusBadge = `<span class="army-status-badge status-attacker" title="${t('Attacking')}"><svg viewBox="0 0 24 24"><path d="m14 4 6 6-11 11-5-1 1-5L15 4Zm4-2 3 3-2 2-3-3 2-2Z" fill="#ef4444"/><path d="m10 4-6 6 11 11 5-1-1-5L9 4Zm-4-2-3 3 2 2 3-3-2-2Z" fill="#f87171"/></svg></span>`;
        } else if (status.kind === "idle") {
            statusBadge = `<span class="army-status-badge status-idle" title="${t('Idle')}"><span class="badge-zzz">ZzZz</span></span>`;
        } else {
            statusBadge = `<span class="army-status-badge status-moving" title="${esc(t(status.label))}"><span class="badge-move">${a.order === "Returning" ? "↩" : "➜"}</span></span>`;
        }

        const formation = a.formation ?? {};
        const entries = Object.entries(formation).filter(([, n]) => n > 0).sort((x, y) => y[1] - x[1]);
        const topUnitId = entries[0]?.[0];

        let leadArtHtml = "";
        if (topUnitId) {
            leadArtHtml = `<div class="army-lead-art" title="${esc(unitCatalog.get(topUnitId)?.displayName ?? topUnitId)}">${unitArtwork(topUnitId)}</div>`;
        } else if (a.wagons > 0 && a.soldiers === 0) {
            leadArtHtml = `<div class="army-lead-art lead-icon" title="${t('Wagons')}">🛒</div>`;
        } else {
            leadArtHtml = `<div class="army-lead-art lead-icon" title="${t('Troops')}">⚔</div>`;
        }

        const ownerName = owner(a.ownerId);
        const villageName = currentLanguage() === 'ar' ? `قرية ${ownerName}` : `${ownerName}'s village`;
        const armyTitle = currentLanguage() === 'ar' ? 'تعزيز' : 'Support';
        const totalUnits = a.soldiers + a.wagons;
        const countHtml = `<strong class="army-total-count">${totalUnits.toLocaleString()}</strong>`;

        cards.push(`<article class="army-card reinforcement ${isSelected ? 'selected' : ''}">
            <button class="army-card-btn" data-action="focus-army" data-id="${a.id}" aria-label="${esc(armyTitle)} · ${esc(villageName)}">
                <div class="army-card-header">
                    <span class="army-name-tag">${esc(armyTitle)}</span>
                    ${statusBadge}
                </div>
                ${leadArtHtml}
                <div class="army-card-bottom">${countHtml}<small class="army-foreign-origin" title="${esc(villageName)}">${esc(villageName)}</small></div>
            </button>
        </article>`);
    });

    strip.innerHTML = cards.join("");
    localizeNow(strip);
}
function renderTimers() { if (!state)
    return; const elapsed = now(); el("match-clock").textContent = state.status === "Running" ? `${time(elapsed)} · ${state.shieldRemainingSeconds > 0 ? "Protected" : "Open frontier"}` : state.status; renderImportantAlerts(); el("work-queue").innerHTML = state.economy?.jobs.slice(0, 4).map(j => `<button class="work-item" data-action="job" data-id="${j.targetId}">⚔ ${esc(state!.economy!.targets.find(t => t.id === j.targetId)?.name)}<strong>${time(j.completesAt - elapsed)}</strong></button>`).join("") ?? ""; document.querySelectorAll<HTMLElement>("[data-deadline]").forEach(item => item.textContent = time(Number(item.dataset.deadline) - elapsed)); document.querySelectorAll<HTMLProgressElement>('[data-training-start]').forEach(bar => { const start=Number(bar.dataset.trainingStart),end=Number(bar.dataset.trainingEnd);bar.value=Math.min(1,Math.max(0,(elapsed-start)/Math.max(.01,end-start))); }); renderBuildings(); }
const descriptions: Record<string, string> = { Market: "Exchange goods with other players. Each level adds an offer slot. Goods and wagons are reserved until cancellation or trade delivery.", MainHall: "The heart of your province. Higher levels unlock more automatic training queues.", Warehouse: "Protect and store your supplies. Upgrade to increase the capacity of every resource.", Barracks: "Recruit infantry and archers. Prepare your next expedition here.", Stable: "Train cavalry, scouts and wagons to keep your armies moving.", SiegeWorkshop: "Build catapults, rams and siege towers to overcome fortified cities.", Smithy: "Improve the weapons and armor of forces based in this village.", Hospital: "Wounded soldiers return here and recover automatically, using iron and wheat.", Wall: "Protect your garrison. Higher walls resist automatic attacks and siege engines." };
let drawerTabs = "", drawerBody = "";
function setTabs(tabs: {
    id: string;
    name: string;
}[]) { drawerTabs = tabs.map(t => button(t.name, "tab", t.id, panelTab === t.id ? "active" : "")).join(""); }
function pager(count: number, size = 6) { return `<div class="pager">${button("‹", "page-prev", "", "", page === 0)}<small>${page + 1} / ${Math.max(1, Math.ceil(count / size))}</small>${button("›", "page-next", "", "", (page + 1) * size >= count)}</div>`; }
function afterSelect() { return select("after-attack", "After this attack", [{ value: "Return", label: "Return home automatically" }, { value: "Hold", label: "Hold position" }], afterAttack); }
function trainingSummary(building: Target, roster: Target[]) {
    const next = roster.find(u => !u.unlocked);
    return `<section class="training-summary"><div><small>Training speed</small><strong dir="ltr">+${building.trainingSpeedBonusPercent ?? 0}%</strong></div><div><small>Units unlocked</small><strong dir="ltr">${roster.filter(u => u.unlocked).length} / ${roster.length}</strong></div><p>${building.name === 'MainHall' ? 'Higher levels speed up recruitment.' : 'Each building upgrade adds 2.5% training speed.'}</p>${next ? `<small>Next unlock: ${esc(next.name)} · Level ${next.requiredBuildingLevel}</small>` : '<small>All units unlocked</small>'}</section>`;
}
function trainingRoadmap(roster: Target[]) {
    return `<section class="training-roadmap" aria-label="Unit unlock progression">${roster.map(u => `<div class="${u.unlocked ? 'unlocked' : 'locked'}">${unitPortrait(unitId(u))}<span>${esc(u.name)}</span><small>Level ${u.requiredBuildingLevel} · ${u.unlocked ? 'Available' : 'Locked'}</small></div>`).join('')}</section>`;
}
function trainingProgress(job: Job) {
    return `<div class="recruit-progress"><div><strong>${job.quantity} recruits in training</strong><b data-deadline="${job.completesAt}">${time(job.completesAt-now())}</b></div><progress max="1" value="${Math.min(1,Math.max(0,(now()-job.startedAt)/Math.max(.01,job.completesAt-job.startedAt)))}" data-training-start="${job.startedAt}" data-training-end="${job.completesAt}" aria-label="Training progress"></progress></div>`;
}
function facilityTrainingQueue(laneName: string, jobs: Job[], targets: Target[]) {
    const laneJobs = jobs.filter(j => j.lane === laneName);
    if (!laneJobs.length) return '';
    const current = now();
    return `<section class="facility-training-queue" aria-label="Training queue"><div class="stat-line"><strong>Training queue (${laneJobs.length})</strong><small>${laneJobs.reduce((n, j) => n + j.quantity, 0)} total recruits</small></div><div class="queue-list">${laneJobs.map((j, idx) => {
        const unitTarget = targets.find(t => t.id === j.targetId);
        const id = unitTarget ? unitId(unitTarget) : '';
        const name = unitTarget?.name ?? 'Recruits';
        const active = idx === 0 || current >= j.startedAt;
        const progress = active ? Math.min(1, Math.max(0, (current - j.startedAt) / Math.max(.01, j.completesAt - j.startedAt))) : 0;
        return `<div class="queue-item ${active ? 'active' : 'queued'}"><div class="queue-item-head">${id ? unitPortrait(id) : ''}<div class="queue-item-info"><strong>${j.quantity} × ${esc(name)}</strong><small>${active ? 'Training now' : `Waiting in queue (#${idx + 1})`}</small></div><b data-deadline="${j.completesAt}">${time(j.completesAt - current)}</b></div><progress max="1" value="${progress}" data-training-start="${j.startedAt}" data-training-end="${j.completesAt}" aria-label="Training progress"></progress></div>`;
    }).join('')}</div></section>`;
}
function buildingPanel(target: Target) {
    const e = state!.economy!, training = e.targets.filter(t => t.kind === "Training" && (t.trainingBuilding ?? facility(unitId(t))) === target.name).sort((a,b) => a.requiredBuildingLevel - b.requiredBuildingLevel);
    const mainHallLevel = e.targets.find(t => t.kind === "Building" && t.name === "MainHall")?.level ?? 1;
    const cappedByMainHall = target.kind === "Building" && target.name !== "MainHall" && target.level >= mainHallLevel;
    const laneJobs = e.jobs.filter(j => j.lane === target.name);
    const laneQueue = facilityTrainingQueue(target.name, e.jobs, e.targets);
    setTabs([{ id: "overview", name: "Building" }, ...(training.length ? [{ id: "training", name: "Training" }] : []), ...(target.name === "Market" ? [{ id: "trade", name: "Market" }] : []), ...(target.name === "MainHall" ? [{ id: "govern", name: "Province" }] : [])]);
    if (panelTab === "training" && training.length) {
        const recruit = training.find(u => u.id === selectedUnit) ?? training.find(u => u.unlocked) ?? training[0]!;
        selectedUnit = recruit.id;
        const queueFull = laneJobs.length >= 12;
        const roster = `<div class="unit-tabs recruitment-roster" style="--roster-count:${training.length}">${training.map(u => {
            const queuedCount = laneJobs.filter(j => j.targetId === u.id).reduce((n, j) => n + j.quantity, 0);
            return `<button data-action="unit" data-id="${u.id}" class="${u.id === recruit.id ? "active" : ""} ${u.unlocked ? "" : "unit-locked"}" aria-pressed="${u.id === recruit.id}">${unitPortrait(unitId(u))}<span>${esc(u.name)}</span><small>${u.unlocked ? (queuedCount ? `In queue: ${queuedCount}` : 'Available') : `Level ${u.requiredBuildingLevel} · Locked`}</small></button>`;
        }).join('')}</div>`;
        const summary = trainingSummary(target, training);
        if (!recruit.unlocked) return `${laneQueue}${summary}${roster}<section class="recruit-lock"><div class="recruit-showcase">${unitPortrait(unitId(recruit))}</div><h3>${esc(recruit.name)}</h3><p>Requires ${pretty(target.name)} level ${recruit.requiredBuildingLevel}.</p><small>${cappedByMainHall ? `Requires Main Hall level ${target.level + 1} first.` : 'Upgrade this building to unlock this unit.'}</small></section><div class="cost-box"><small>Next building upgrade</small>${costs(target.upgradeCost)}</div><div class="panel-footer">${button(`Upgrade to level ${target.level + 1}`, 'upgrade', target.id, 'primary wide', !running() || !target.upgradeCost || cappedByMainHall || !affordable(target.upgradeCost))}</div>`;
        return `${laneQueue}${summary}${roster}<div class="recruit-showcase">${unitPortrait(unitId(recruit))}<div><h3>${esc(recruit.name)}</h3><small>At home: ${e.garrison[unitId(recruit)] ?? 0} · Tier ${recruit.level}</small><small>${Number((recruit.trainingSeconds ?? 0).toFixed(2))} seconds per recruit</small></div></div>
        <div class="cost-box"><small>COST PER UNIT</small>${costs(recruit.trainingCost)}</div>
        <div><div class="stat-line"><span>Recruitment quantity</span><b dir="ltr">1–100</b></div><div class="quantity-row"><input id="train-range" aria-label="Recruitment quantity slider" type="range" min="1" max="100" value="${amount}"/><input id="train-amount" aria-label="Recruitment quantity" type="number" min="1" max="100" value="${amount}"/></div></div>
        <div class="cost-box"><small>TOTAL COST · <span id="total-quantity">${amount}</span> RECRUITS</small><div id="training-total">${costs(recruit.trainingCost, amount)}</div><div class="stat-line"><span>Ready in</span><b id="training-duration">${time((recruit.trainingSeconds ?? 0) * amount)}</b></div></div>
        <div class="panel-footer">${button(laneJobs.length ? `Queue ${amount} recruits` : `Train ${amount} recruits`, 'train', recruit.id, 'primary wide', !running() || queueFull || !affordable(recruit.trainingCost, amount))}<div class="button-row">${button(`Repeat batches: ${recruit.auto ? 'ON' : 'OFF'}`, 'auto', recruit.id, 'quiet', !running())}${button(`Tier ${recruit.level} → ${recruit.level + 1}`, 'tier', recruit.id, 'quiet', !running() || !recruit.upgradeCost)}</div></div>`;
    }
    if (panelTab === "govern")
        return governPanel();
    if (panelTab === "trade" && target.name === "Market") return marketPanel(state!.world!, e, state!.you, marketDraft, owner, running());
    const extra = target.name === "Warehouse" ? `Storage per resource: ${e.capacity.toLocaleString()}` : target.name === "Hospital" ? `${e.wounded} wounded awaiting recovery` : target.name === "MainHall" ? `${e.usedAutoSlots} / ${e.autoSlots} automatic training queues` : target.name === "Smithy" ? `Attack & armor bonus: ${5*(target.level-1)}%` : target.name === "Market" ? `Market offer slots: ${target.level}` : target.name === "Wall" ? "Higher walls increase garrison protection." : "Faster recruitment with every building level.";
    return `<div class="panel-hero">${target.kind === 'Training'?unitPortrait(unitId(target)):buildingArt(target.name,currentTribe())}<span class="level-seal">${target.level}<small>LEVEL</small></span></div><p class="muted">${descriptions[target.name]??'Upgrade this training tier to recruit this unit faster.'}</p><div class="stat-line"><span>${extra}</span></div>${laneQueue}${training.length ? trainingSummary(target, training) + trainingRoadmap(training) : ''}<div class="cost-box"><small>${target.upgradeCost?`UPGRADE TO LEVEL ${target.level+1}`:'FULLY UPGRADED'}</small>${costs(target.upgradeCost)}</div><div class="panel-footer">${button(target.kind==='Training'?'Upgrade training tier':'Upgrade building','upgrade',target.id,'primary wide',!running()||!target.upgradeCost||cappedByMainHall||!affordable(target.upgradeCost))}<small>${cappedByMainHall?`Requires Main Hall level ${target.level + 1}`:'Upgrades complete instantly after payment.'}</small>${target.name==='Market'?button('Open market','tab','trade','quiet wide'):''}</div>`;

}
function governPanel() { const city = state?.world?.warfare?.settlements.find(c => c.id === state?.economy?.villageId); if (!city)
    return ""; const owned = state!.world!.warfare!.settlements.filter(c => c.ownerId === state!.you && !c.ruined); return `<p class="muted">An independent province under your banner.</p>${select("province", "Manage province", owned.map((c, i) => ({ value: c.id, label: `Province ${i + 1} · ${c.tribe} · Level ${c.level}` })), city.id)}${button("Dispatch an expedition", "rally", "", "primary wide", !running())}${city.tribe === "Mongols" ? `<p class="muted">Relocate for 300 wood and 300 wheat. Choose a free spot on the world map first.</p>${button("Choose relocation site", "relocation-map", "", "wide", !running() || city.besieged || city.relocateIn > 0)}` : ""}<div class="panel-footer"><small>${state!.economy!.wounded} wounded · ${state!.economy!.usedAutoSlots}/${state!.economy!.autoSlots} automatic queues</small></div>`; }
function formationPortraits(units: Record<string, number> | undefined | null, label = "Scouted composition", side: "friendly" | "hostile" = "hostile") {
    const entries = Object.entries(units ?? {}).filter(([, n]) => n > 0);
    if (!entries.length) return "";
    const cols = Math.min(4, entries.length);
    return `<div class="report-forces scouted-forces"><div class="report-force ${side}"><strong>${label}</strong><div class="report-unit-grid" style="--unit-cols:${cols}">${entries.map(([id, n]) => `<div class="report-unit" title="${esc(unitCatalog.get(id)?.displayName ?? pretty(role(id)))}">${unitPortrait(id)}<span class="unit-counts"><b class="positive">${n.toLocaleString()}</b></span></div>`).join("")}</div></div></div>`;
}
function scoutedSiteUnits(villageId: string | null, position?: { x: number; y: number }): Record<string, number> | null {
    const reports = (state?.world?.reports ?? [])
        .filter(r => r.kind === "Scout" && r.status === "Scouted" && (villageId ? r.villageId === villageId : !!position && Math.hypot(r.position.x - position.x, r.position.y - position.y) < 5))
        .sort((a, b) => b.updatedAt - a.updatedAt);
    const latest = reports[0];
    if (!latest) return null;
    const counts: Record<string, number> = {};
    for (const u of latest.defenderUnits) {
        const alive = Math.max(0, u.sent - u.dead - u.wounded - u.fled - u.destroyed);
        if (alive > 0) counts[u.unitId] = (counts[u.unitId] ?? 0) + alive;
    }
    return counts;
}
function villageAlliances(playerId: string) {
    const names = (state?.alliances ?? []).flatMap(alliance =>
        alliance.firstPlayerId === playerId ? [owner(alliance.secondPlayerId)]
            : alliance.secondPlayerId === playerId ? [owner(alliance.firstPlayerId)] : []);
    return `<section class="village-alliances"><div class="stat-line"><h3>Alliances</h3><b>${names.length}</b></div><div class="alliance-names">${names.length ? names.map(name => `<span>${esc(name)}</span>`).join("") : '<small>No alliances for this commander.</small>'}</div></section>`;
}
function allianceControls(playerId: string) {
    if (!state || playerId === state.you || state.players.find(p => p.id === playerId)?.isBot) return "";
    if (state.alliances.some(a => (a.firstPlayerId === state!.you && a.secondPlayerId === playerId) || (a.secondPlayerId === state!.you && a.firstPlayerId === playerId)))
        return '<small class="positive">Allied commanders</small>';
    const incoming = state.allianceRequests.find(r => r.senderId === playerId && r.recipientId === state!.you);
    if (incoming) return `<div class="alliance-actions">${button("Accept alliance", "alliance-accept", incoming.id, "primary", !running())}${button("Decline", "alliance-decline", incoming.id, "quiet", !running())}</div>`;
    const outgoing = state.allianceRequests.find(r => r.senderId === state!.you && r.recipientId === playerId);
    if (outgoing) return `<div class="alliance-actions"><small>Alliance request sent</small>${button("Cancel request", "alliance-cancel", outgoing.id, "quiet", !running())}</div>`;
    return button("Propose alliance", "alliance-propose", playerId, "quiet wide", !running());
}
function resourceTransfer(cityId: string, ruined: boolean) {
    const sources = (state?.world?.warfare?.settlements ?? []).filter(c => c.ownerId === state?.you && !c.ruined && c.id !== cityId);
    if (!sources.some(c => c.id === transferSource)) transferSource = sources[0]?.id ?? "";
    const total = Object.values(transferDraft).reduce((sum, value) => sum + value, 0);
    return `<section class="resource-transfer"><h3>Send resources</h3><p class="muted">Choose a source village and cargo. Wagons deliver it after travel, then return home.</p>${sources.length ? select("transfer-source", "Source village", sources.map((c, i) => ({ value: c.id, label: `Province ${i + 1} · Level ${c.level}` })), transferSource) : '<small>You need another village to send resources here.</small>'}<div class="transfer-grid">${(["wood", "iron", "stone", "wheat"] as const).map(resource => `<label><span>${icon(resource)}${resource}</span><input id="transfer-${resource}" type="number" inputmode="numeric" min="0" max="1000000" step="1" value="${transferDraft[resource]}" aria-label="${resource} amount"/></label>`).join("")}</div>${button("Send resource shipment", "send-resources", cityId, "primary wide", !running() || ruined || !transferSource || total < 1)}</section>`;
}
function villagePanel() {
    const city = state!.world?.warfare?.settlements.find(c => c.id === panelId);
    if (!city) return '<p class="empty">This settlement is no longer available.</p>';
    const mine = city.ownerId === state!.you, scouted = !mine ? scoutedSiteUnits(city.id, city.position) : null;
    const villageActions: string[] = [];
    if (mine) {
        villageActions.push(`<button data-action="enter" data-id="${city.id}" class="action-btn primary grid-full" ${city.ruined ? 'disabled' : ''}><span class="btn-icon">🏛</span><span>Manage buildings</span></button>`);
        villageActions.push(`<button data-action="reinforce" data-id="${city.id}" class="action-btn action-reinforce" ${!running() || city.ruined ? 'disabled' : ''}><span class="btn-icon">🛡</span><span>Send reinforcements</span></button>`);
        villageActions.push(`<button data-action="plan-camp" data-id="${city.id}" class="action-btn action-camp" ${!running() || city.ruined ? 'disabled' : ''}><span class="btn-icon">⛺</span><span>Camp here</span></button>`);
        villageActions.push(`<button data-action="rally" class="action-btn grid-full" ${!running() ? 'disabled' : ''}><span class="btn-icon">⚔</span><span>Send an expedition</span></button>`);
    } else {
        villageActions.push(`<button data-action="plan-attack" data-id="${city.id}" class="action-btn action-capture primary" ${!running() || city.ruined || state!.shieldRemainingSeconds > 0 ? 'disabled' : ''}><span class="btn-icon">⚔</span><span>Plan an attack →</span></button>`);
        villageActions.push(`<button data-action="scout-target" data-id="${city.id}" class="action-btn action-scout" ${!running() || city.ruined ? 'disabled' : ''}><span class="btn-icon">👁</span><span>Send scouts</span></button>`);
        villageActions.push(`<button data-action="reinforce" data-id="${city.id}" class="action-btn action-reinforce" ${!running() || city.ruined ? 'disabled' : ''}><span class="btn-icon">🛡</span><span>Send reinforcements</span></button>`);
        villageActions.push(`<button data-action="plan-camp" data-id="${city.id}" class="action-btn action-camp" ${!running() || city.ruined ? 'disabled' : ''}><span class="btn-icon">⛺</span><span>Camp here</span></button>`);
    }
    const villageGrid = `<div class="panel-action-grid">${villageActions.join('')}</div>`;
    return `<div class="panel-hero">${buildingArt("MainHall", city.tribe)}<span class="level-seal">${city.level}<small>LEVEL</small></span></div><div class="stat-line"><span>Commander</span><b>${esc(owner(city.ownerId))}</b></div><div class="stat-line"><span>Settlement</span><b>${settlementStage(city.level)}</b></div><small>Town at level 6. City at level 12.</small><div class="stat-line"><span>People</span><b>${city.tribe}</b></div><div class="stat-line"><span>Location</span><b>${Math.round(city.position.x)} : ${Math.round(city.position.y)}</b></div><div class="stat-line"><span>Status</span><b>${city.ruined ? "Ruins" : city.besieged ? "Under siege" : "Independent"}</b></div>${scouted ? (Object.keys(scouted).length ? formationPortraits(scouted, "Scouted composition", "hostile") : '<small class="muted">Scouted: no defending troops inside.</small>') : ""}${villageAlliances(city.ownerId)}${!city.ruined ? allianceControls(city.ownerId) : ""}<div class="panel-footer">${!mine && state!.shieldRemainingSeconds > 0 ? `<small>Protection expires in ${time(state!.shieldRemainingSeconds)}</small>` : ""}${villageGrid}</div>${resourceTransfer(city.id, city.ruined)}`;
}
function loadDispatchFormation() {
    formation = {};
    if (dispatchArmy !== "new") {
        const selected = ownArmies().find(a => a.id === dispatchArmy);
        formation = Object.fromEntries(Object.entries(selected?.formation ?? {}).filter(([, count]) => count > 0));
        return;
    }
}
function openDispatch(mission: "attack" | "reinforce" | "blockade" | "scout" | "camp" | "burn" = "attack") {
    dispatchMission = mission; attackObjective = "Raid"; campLoadedOnly = true;
    dispatchArmy = preferredDispatchArmy && ownArmies().some(a => a.id === preferredDispatchArmy && !["Battle", "Sieging", "Returning"].includes(a.order)) ? preferredDispatchArmy : "new";
    preferredDispatchArmy = "";
    afterAttack = ((selection?.kind === "oasis" && mission !== "scout") || mission === "camp") ? "Hold" : "Return";
    page = 0; loadDispatchFormation(); openPanel("dispatch");
}
function highlightDispatchArmy(focus = false) {
    const selected = ownArmies().find(a => a.id === dispatchArmy);
    if (!selected) { map.select(selection); return; }
    map.select({ id: selected.id, kind: "army", name: "Expedition", position: selected.position, ownerId: selected.ownerId });
    if (focus) map.focus(selected.position);
}
function dispatchArmyLocation() {
    const selected = ownArmies().find(a => a.id === dispatchArmy);
    if (!selected) return "";
    const index = ownArmies().findIndex(a => a.id === selected.id);
    return `<section class="selected-army-location"><div><small>Selected army location</small><strong>Army ${index + 1} · ${Math.round(selected.position.x)} : ${Math.round(selected.position.y)}</strong><span>${selected.order} · ${selected.soldiers + selected.wagons} units</span></div>${button("Show on map", "locate-army", selected.id, "quiet")}</section>`;
}
function dispatchPanel() {
    const e = state?.economy; if (!e) return '<p class="empty">You need a village to send an expedition.</p>';
    const target = selection?.kind ?? "ground";
    const selected = ownArmies().find(a => a.id === dispatchArmy);
    const available = dispatchArmy === "new" ? e.garrison : selected?.formation ?? {};
    const allUnits = Object.entries(available).filter(([, n]) => n > 0);
    const units = dispatchMission === "scout" ? allUnits.filter(([id]) => role(id) === "scout") : allUnits;
    const oasis = state?.world?.oases.find(c => c.id === selection?.id);
    const candidateArmies = ownArmies().filter(a => !["Battle", "Sieging", "Returning"].includes(a.order) && (dispatchMission !== "scout" || (a.wagons === 0 && Object.entries(a.formation ?? {}).every(([id, n]) => n <= 0 || role(id) === "scout"))));
    const isAr = currentLanguage() === "ar";
    const description = dispatchMission === "camp" ? (isAr ? "يسير الجيش إلى هذا الموقع وينصب خيامه. يشتبك المخيم تلقائيًا مع الجيوش والقوافل المعادية داخل دائرته الظاهرة، ولا يهاجم الحلفاء." : "March here and pitch tents. The camp automatically engages hostile armies and caravans inside its visible perimeter; allies are not attacked.")
        : dispatchMission === "scout" ? (isAr ? "إرسال كشافة سريعة للتجسس على الهدف. إذا كان لدى الهدف كشافة أكثر مما أرسلت، فسيتم القضاء على كشافتك ولن يقدموا تقريرًا." : "Send fast scouts to spy on the target. If the target has more scouts than you send, your scouts will be killed and report nothing.")
        : dispatchMission === "reinforce" ? (isAr ? "الدفاع عن هذا الموقع مع الاحتفاظ بالتحكم في جيشك." : "Defend this site while keeping control of your army.")
        : dispatchMission === "blockade" ? (isAr ? "محاصرة هذه الواحة لتعليق مكافأة إنتاجها وجمع الموارد منها." : "Surround this oasis to suspend its production bonus and resource collection.")
        : dispatchMission === "burn" ? (isAr ? "حرق هذه الواحة وتسويتها بالأرض. يستغرق الحرق دقيقة ونصف دون مقاومة. في حال النجاح، تدمر الواحة وتظل محروقة لمدة 5 دقائق." : "Burn this oasis to ashes. Burning takes 1.5 minutes uncontested. If successful, the oasis is destroyed and remains burned for 5 minutes.")
        : target === "oasis" ? (isAr ? "احتلال هذه الواحة لزيادة إنتاج قريتك الرئيسية. لا توجد غنائم موارد." : "Capture this oasis to increase your home village production. No resource loot.")
        : (isAr ? "اختر الوحدات وأرسل أوامرك." : "Choose units and send your orders.");
    const sendLabel = dispatchMission === "camp" ? (isAr ? "نصب المخيم" : "Establish camp")
        : dispatchMission === "scout" ? (isAr ? "إرسال كشافة" : "Send scouts")
        : dispatchMission === "reinforce" ? (isAr ? "إرسال تعزيزات" : "Send reinforcements")
        : dispatchMission === "blockade" ? (isAr ? "حصار الواحة" : "Besiege oasis")
        : dispatchMission === "burn" ? (isAr ? "حرق الواحة" : "Burn oasis")
        : target === "oasis" ? (isAr ? "احتلال الواحة" : "Capture oasis")
        : (isAr ? "إرسال الجيش" : "Send army");
    const destName = oasis ? (isAr ? t(oasis.name) : oasis.name) : (selection?.name ?? (isAr ? "مرج مفتوح" : "Open meadow"));
    const formationEditor = units.length ? `<div class="dispatch-formation">${units.slice(page * 9, page * 9 + 9).map(([id, count]) => `<div class="dispatch-unit-card">${unitPortrait(id)}<span class="dispatch-unit-meta"><strong>${esc(unitCatalog.get(id)?.displayName ?? pretty(role(id)))}</strong><small>${count} available</small></span><div class="dispatch-input-row"><input data-formation="${id}" aria-label="${role(id)} count" type="number" inputmode="numeric" min="0" max="${count}" value="${formation[id] ?? 0}"/><button type="button" class="dispatch-quick-btn" data-action="formation-quick" data-id="${id}" data-max="${count}" title="Half / All">½</button></div></div>`).join("")}</div>${units.length > 9 ? pager(units.length, 9) : ""}` : '<p class="empty">No suitable units are available for this order.</p>';
    return `<div class="dispatch-destination"><strong>${esc(destName)}</strong><small>${description}</small></div>${oasis ? `<small class="oasis-specialty">${icon(oasis.resource.toLowerCase())} +50% ${oasis.resource} / +25% other resources</small>` : ""}${dispatchArmyLocation()}${dispatchArmy === "new" ? select("dispatch-source", "Dispatch from village", (state?.world?.warfare?.settlements ?? []).filter(c => c.ownerId === state?.you && !c.ruined).map((c, i) => ({ value: c.id, label: `Province ${i + 1} / Level ${c.level}` })), e.villageId) : ""}${candidateArmies.length ? select("dispatch-army", "Choose your force", [{ value: "new", label: "Raise a new expedition" }, ...candidateArmies.map((a, i) => ({ value: a.id, label: `Army ${i + 1} / ${a.soldiers + a.wagons} units` }))], dispatchArmy) : ""}${formationEditor}${dispatchArmy === "new" ? '<div id="dispatch-provisions"></div>' : '<p class="muted">Only the selected units receive this order. The remainder keeps its current assignment, food and loot.</p>'}<div id="attack-objectives"></div><div class="dispatch-directives">${dispatchMission === "camp" ? `<label class="check"><input id="camp-loaded-only" type="checkbox" ${campLoadedOnly ? "checked" : ""}/> Raid only loaded caravans and armies</label>` : ""}${["reinforce", "scout"].includes(dispatchMission) ? "" : afterSelect()}</div><div class="panel-footer">${button(sendLabel, "dispatch-send", "", "primary wide", !running())}<small>Automatic supplies depart only when provisions run low.</small></div>`;
}
function objectiveAllowed(objective=attackObjective) {
    if(selection?.kind!=="village"||dispatchMission!=="attack")return true;
    const troops=formation;
    const has=(kind:string)=>Object.entries(troops).some(([id,n])=>role(id)===kind&&n>0);
    return objective==='Conquest'?has('governor'):objective==='Raze'?has('catapult'):true;
}
function renderObjectives(){
    const box=document.getElementById('attack-objectives'); if(!box)return;
    if(selection?.kind!=='village'||dispatchMission!=='attack'){box.innerHTML='';return;}
    const objectives=[['Raid','Raid resources','Take resources and leave the village standing.','⚒'],['Conquest','Conquer village','Requires the governor trained in the Main Hall. Persuasion advances each round.','⚑'],['Raze','Destroy village','Requires catapults. Buildings lose levels over about five minutes after the defense falls.','♜'],['Blockade','Besiege village','Block external deliveries and reinforcements. Wheat production is reduced by 50%.','◉']];
    box.innerHTML=`<fieldset class="attack-objectives"><legend>Attack objective</legend><div>${objectives.map(([value,label,detail,mark])=>`<button type="button" data-action="attack-objective" data-id="${value}" class="objective-card ${attackObjective===value?'active':''}" aria-pressed="${attackObjective===value}" ${!objectiveAllowed(value)?'disabled':''}><span>${mark}</span><strong>${label}</strong><small>${detail}</small></button>`).join('')}</div></fieldset>`;
    localizeNow(box);
}
function renderProvisions() {
    renderObjectives();
    if(dispatchArmy!=='new'){const send=el('drawer').querySelector<HTMLButtonElement>('[data-action="dispatch-send"]');if(send)send.disabled=!running()||!objectiveAllowed()||!Object.values(formation).some(n=>n>0);}

    const box=document.getElementById('dispatch-provisions'); if(!box||!state?.economy)return;
    const home=state.world?.warfare?.settlements.find(c=>c.id===state?.economy?.villageId)?.position??{x:1000,y:1000}, dest=selection?.position??home;
    const p=provisionEstimate(formation,Math.hypot(dest.x-home.x,dest.y-home.y),state.economy.resources.wheat);
    const enough=p.total>0&&p.food>=p.required;
    box.innerHTML=`<div class="provision-line ${enough?'positive':'negative'}">${icon('wheat')}<b>${p.food.toLocaleString()}</b><span>Provisions loaded</span><small>${Math.ceil(p.seconds/60)} min</small></div><small>Includes the return journey and combat time.</small>${!enough?'<small class="negative">Choose units with enough capacity and wheat before departing.</small>':''}`;
    const send=el('drawer').querySelector<HTMLButtonElement>('[data-action="dispatch-send"]');if(send)send.disabled=!running()||!enough||!objectiveAllowed();
}
function armyPanel() {
    const a = state!.world?.armies.find(a => a.id === panelId);
    if (!a)
        return '<p class="empty">This army has returned or left the field.</p>';
    const mine = a.ownerId === state!.you;
    if (!mine)
        return `<div class="panel-hero">${icon("sword")}</div><div class="stat-line"><span>Commander</span><b>${esc(owner(a.ownerId))}</b></div><div class="stat-line"><span>Visible force</span><b>${a.soldiers} troops · ${a.wagons} wagons</b></div>${a.formation ? formationPortraits(a.formation, "Scouted composition", "hostile") : `<small class="muted">Exact unit counts are unknown. Send scouts to reveal this army's composition.</small>`}<div class="panel-footer">${button("Intercept this army", "plan-attack", a.id, "primary wide", !running() || state!.shieldRemainingSeconds > 0)}${button("Send scouts", "scout-target", a.id, "quiet wide", !running())}</div>`;
    setTabs([{ id: "overview", name: "Orders" }, { id: "supply", name: "Supply" }, { id: "cargo", name: "Cargo" }]);
    const battle = state!.world!.warfare?.battles.find(b => b.status === "Active" && (b.attackerArmyIds.includes(a.id) || b.defenderArmyIds.includes(a.id))), siege = state!.world!.warfare?.sieges.find(s => s.armyIds.includes(a.id) && ["Active"].includes(s.status));
    const inCombat = !!battle || !!siege || !!state!.world!.oasisSieges?.some(s => s.armyIds.includes(a.id));
    const stats = `<div><div class="stat-line"><span>Morale</span><b>${Math.round(a.morale ?? 0)}%</b></div><div class="meter"><i style="width:${a.morale ?? 0}%"></i></div><div class="stat-line"><span>Food</span><b>${Math.floor(a.wheat ?? 0)} / ${a.capacity}</b></div><div class="meter food"><i style="width:${Math.min(100, (a.wheat ?? 0) / Math.max(1, a.capacity) * 100)}%"></i></div></div>`;
    if (panelTab === "supply") {
        const cities = state!.world!.warfare!.settlements.filter(c => c.ownerId === state!.you && !c.ruined);
        return `${stats}<p class="muted">${esc(a.supplyStatus)}</p>${select("supply-source", "Supply province", cities.map((c, i) => ({ value: c.id, label: `Province ${i + 1} · ${c.tribe}` })), supplySource)}<div class="form-grid"><label>Wagons (0 = auto)<input id="supply-wagons" type="number" min="0" max="20" value="${supplyWagons}"/></label><label>Infantry escorts<input id="supply-escorts" type="number" min="0" max="100" value="${escorts}"/></label></div><label class="check"><input id="supply-auto" type="checkbox" ${autoSupply ? "checked" : ""}/> Automatic supply deliveries</label><div class="panel-footer">${button("Update supply line", "supply", a.id, "primary wide", !running())}</div>`;
    }
    if (panelTab === "cargo") return `${cargoView(a.loot ?? {wood:0,iron:0,stone:0,wheat:0}, "MATERIALS & LOOT ON BOARD")}<div class="stat-line"><span>Carried wheat / food capacity</span><b>${Math.floor(a.wheat ?? 0)} / ${Math.floor(a.capacity)}</b></div><p class="muted">Wheat is consumed during travel and combat. Remaining food and loot are credited when this army returns home.</p><div class="panel-footer">${button("Open battle reports", "show-reports", "", "primary wide")}</div>`;
    return `<div class="stat-line"><span>${a.order}</span><b>${a.soldiers} troops · ${a.wagons} wagons</b></div>${stats}${battle ? `<div class="battle-line"><strong>Battle · Round ${battle.round}</strong><p>${battle.attackerTroops} vs ${battle.defenderTroops} · ${time(battle.elapsed)}</p></div>` : ""}${siege ? `<div class="battle-line"><strong>${siege.phase === "Persuading" ? "Establishing control" : "Siege in progress"}</strong><p>Gate ${Math.round(siege.gate)}% · Wall ${Math.round(siege.wall)}%<br/>Garrison ${Math.round(siege.garrisonMorale)}% · Loyalty ${Math.round(siege.loyalty)}%</p></div>` : ""}${formationPortraits(a.formation, "Your army", "friendly")}${afterSelect()}${inCombat ? '<small class="negative">Withdrawing now causes losses equal to 20% of the remaining army.</small>' : ""}<div class="panel-footer"><div class="button-row">${button("Choose destination", "army-destination", a.id, "quiet", !running())}${button(inCombat ? "Withdraw · 20% losses" : "Return home", "recall", a.id, "quiet", !running() || a.order === "Returning")}</div></div>`;
}
function pendingAllianceRequests() {
    const requests = state?.allianceRequests ?? [];
    if (!requests.length) return "";
    return `<section class="alliance-requests"><h3>Alliance requests</h3>${requests.map(request => {
        const incoming = request.recipientId === state!.you, other = incoming ? request.senderId : request.recipientId;
        return `<div class="alliance-request"><span>${incoming ? `${esc(owner(other))} invited you to an alliance.` : `Request sent to ${esc(owner(other))}`}</span><div class="alliance-actions">${incoming ? `${button("Accept", "alliance-accept", request.id, "primary", !running())}${button("Decline", "alliance-decline", request.id, "quiet", !running())}` : button("Cancel request", "alliance-cancel", request.id, "quiet", !running())}</div></div>`;
    }).join("")}</section>`;
}
function standingsPanel() { const war = state!.world!.warfare!, ranks = state!.result?.rankings ?? war.rankings, keep = war.castle; return `${pendingAllianceRequests()}${state!.result ? `<div class="victory"><h3>${state!.result.winnerIds.includes(state!.you) ? "Victory!" : "Match complete"}</h3><p class="muted">${esc(state!.result.winnerIds.map(owner).join(" & "))} · ${state!.result.reason}</p></div>` : `<div class="battle-line"><strong>Crown Keep</strong><p>${keep.active ? `${keep.contested ? "Contested" : keep.controllerId ? "Ruler: " + esc(owner(keep.controllerId)) : "Open"} · ${time(keep.captureProgress)} / 5:00 continuous hold` : `Opens at 30:00 (${time(Math.max(0, 1800 - now()))})`}</p>${keep.active ? button("March for the crown", "castle-march", "", "wide", !running() || !army()) : ""}</div>`}<div>${ranks.slice(page * 6, page * 6 + 6).map((r, i) => `<div class="rank-row ${r.playerId === state!.you ? "own" : ""}"><span>${page * 6 + i + 1}</span><span>${esc(owner(r.playerId))}<small>${r.eliminated ? "Eliminated" : `${r.settlements} provinces · ${r.kills} kills · ${r.wounded} wounded`}</small></span><strong>${r.score.toLocaleString()}</strong></div>`).join("")}</div>${pager(ranks.length)}${state!.status !== "Running" ? button("Leave realm", "leave", "", "quiet wide") : ""}`; }
function lobbyPanel() { const s = state!; setTabs([{ id: "overview", name: "Commanders" }, ...(s.hostId === s.you ? [{ id: "settings", name: "Realm settings" }] : [])]); if (panelTab === "settings") {
    const t = s.settings;
    return `<div class="form-grid"><label>Seats<input id="capacity" type="number" min="8" max="16" value="${t.capacity}"/></label><label>Minutes<input id="duration" type="number" min="30" max="60" value="${t.durationMinutes}"/></label>${select("shield", "Protection", ["0", "5", "10"], String(t.shieldMinutes))}<label>Resource speed<input id="speed" type="number" min="0.5" max="5" step="0.5" value="${t.resourceSpeed}"/></label><label>AI neighbors<input id="bots" type="number" min="0" max="15" value="${t.botCount}"/></label>${select("difficulty", "AI difficulty", ["Easy", "Normal", "Hard"], t.botDifficulty)}</div><div class="panel-footer">${button("Apply settings", "settings", "", "primary wide")}</div>`;
} return `<p class="muted">Realm code: <b>${esc(s.id)}</b><br/>${s.settings.botCount} AI neighbors · ${s.settings.durationMinutes} minutes</p><div>${s.players.slice(page * 6, page * 6 + 6).map(p => `<div class="rank-row"><span>${p.ready ? "✓" : "○"}</span><span>${esc(p.name)}<small>${p.tribe}</small></span><small>${p.online ? "Online" : "Away"}</small></div>`).join("")}</div>${s.players.length > 6 ? pager(s.players.length) : ""}<div class="panel-footer">${button(s.players.find(p => p.id === s.you)?.ready ? "Cancel ready" : "Ready to play", "ready", "", "wide")}${s.hostId === s.you ? button("Begin match", "start", "", "primary wide") : ""}${button("Leave realm", "leave", "", "quiet wide")}</div>`; }
function patchPanel(target:Node,source:Node){
    const wanted=Array.from(source.childNodes);
    wanted.forEach((next,i)=>{
        const current=target.childNodes[i];
        if(!current){target.appendChild(next.cloneNode(true));return;}
        if(current.nodeType!==next.nodeType||current.nodeName!==next.nodeName){target.replaceChild(next.cloneNode(true),current);return;}
        if(current instanceof Element&&next instanceof Element){
            for(const attr of Array.from(current.attributes))if(!next.hasAttribute(attr.name))current.removeAttribute(attr.name);
            for(const attr of Array.from(next.attributes))if(current.getAttribute(attr.name)!==attr.value)current.setAttribute(attr.name,attr.value);
            patchPanel(current,next);
        }else if(current.nodeValue!==next.nodeValue)current.nodeValue=next.nodeValue;
    });
    while(target.childNodes.length>wanted.length)target.lastChild!.remove();
}
function renderDrawer(force = false) {
    if (!panel)
        return;
    const focused = document.activeElement;
    if (!force && focused instanceof HTMLElement && el("drawer").contains(focused) && focused.matches("input,select")) {
        if (panel === "building" && panelTab === "trade" && state?.world && state?.economy) {
            const liveSection = document.getElementById("market-live-section");
            if (liveSection) {
                liveSection.innerHTML = marketOffersSection(state.world, state.economy, state.you, owner, running());
                localizeNow(liveSection);
            }
        }
        return;
    }
    if (!state && panel !== "rooms") {
        closePanel();
        return;
    }
    drawerTabs = "";
    let title = "", kicker = "", html = "";
    if(panel === "alerts") {title="Important alerts";kicker="YOUR KINGDOM";html=alertInbox.panel(state?.world?.alerts??[]);
    } else if (panel === "biome") {
        const region = state?.world?.regions?.find(r=>String(r.id)===panelId);
        title = (region?.kind ?? "Meadow"); kicker = "BIOME INFO";
        const ground = selection?.kind === "ground";
        const city = state?.world?.warfare?.settlements.find(c => c.id === state?.economy?.villageId);
        html = region ? `<div class="biome-preview biome-${region.kind.toLowerCase()}"><span>${region.kind==='Highlands'?'&#9968;':region.kind==='Woodland'?'&#9827;':region.kind==='Desert'?'&#9728;':'&#10048;'}</span></div><h3>Regional production bonuses</h3><div class="biome-bonuses">${(['wood','iron','stone','wheat'] as const).map(k=>`<div>${icon(k)}<strong class="positive">+${Math.round((region[k]-1)*100)}%</strong><span>${k}</span></div>`).join('')}</div><p class="muted">This village benefits from its surrounding terrain.</p><div class="panel-footer">${ground?button('March here','plan-attack','','primary wide',!running()):''}${ground?button('Camp here','plan-camp','','primary wide',!running()):''}${ground&&city?.tribe==='Mongols'?button('Relocate village here','relocate','','quiet wide',!running()||city.besieged||city.relocateIn>0):''}</div>` : '<p class="muted">No region selected.</p>';
    } else if (panel === "muster") {
        title = "Your forces"; kicker = "KINGDOM MUSTER";
        setTabs([{id:"overview",name:"Troops"},{id:"food",name:"Food supply"}]);
        html = musterPanel(state?.world?.muster, page, panelTab);
    } else if (panel === "production") {
        title = pretty(panelId[0]!.toUpperCase() + panelId.slice(1)); kicker = "PRODUCTION";
        html = productionPanel(state?.economy?.production?.find(p => p.resource.toLowerCase() === panelId), state?.economy?.oasisBonuses[["wood","iron","stone","wheat"].indexOf(panelId)] ?? 0, state?.economy?.ownedOases ?? 0);
    } else if (panel === "reports") {
        title = "Reports"; kicker = "YOUR CAMPAIGN CHRONICLE";
        setTabs([{id:"all",name:"All"},{id:"defense",name:"Defense"},{id:"attack",name:"Attack"}]);
        html = reportsPanel(state?.world?.reports ?? [], state!.you, owner, page, panelTab === "overview" ? "all" : panelTab, unreadReport);
    } else if (panel === "report") {
        title = "Battle report"; kicker = "LIVE INTELLIGENCE";
        const report = state?.world?.reports?.find(r => r.id === panelId);
        if (report) { readReport(report); renderIntelligence(); if (!liveReport(report)) kicker = "CAMPAIGN RECORD"; }
        
        const field=state?.world?.warfare?.battles.find(b=>b.id===panelId),os=state?.world?.oasisSieges?.find(b=>b.id===panelId);
        const citySiege=state?.world?.warfare?.sieges.find(s=>s.id===panelId&&["Active"].includes(s.status));
        const publicEncounter=field?{attackers:field.attackerTroops,defenders:field.defenderTroops,round:field.round}:os?{attackers:state?.world?.armies.filter(a=>os.armyIds.includes(a.id)).reduce((n,a)=>n+a.soldiers,0)??0,defenders:state?.world?.oases.find(o=>o.id===os.oasisId)?.defenders??0,round:os.round,wall:os.wall,maximum:os.wallMaximum}:citySiege?{attackers:state?.world?.armies.find(a=>a.id===citySiege.armyId)?.soldiers??0,wall:citySiege.wall,maximum:100}:undefined;
        html = reportPanel(report, state!.you, owner, page, panelTab, citySiege, publicEncounter);
    } else if (panel === "building") {
        const t = state?.economy?.targets.find(t => t.id === panelId);
        if (!t) {
            closePanel();
            return;
        }
        title = pretty(t.name);
        kicker = "VILLAGE BUILDING";
        html = buildingPanel(t);
    }
    else if (panel === "oasis") {
        const oasis = state?.world?.oases.find(o => o.id === panelId);
        const isAr = currentLanguage() === 'ar';
        title = oasis ? (isAr ? t(oasis.name) : oasis.name) : (isAr ? "واحة" : "Oasis");
        if (oasis?.ownerId) {
            const linkedCity = state?.world?.warfare?.settlements.find(c => c.id === oasis.villageId);
            const vOwner = owner(linkedCity?.ownerId ?? oasis.ownerId);
            const isMine = (linkedCity?.ownerId ?? oasis.ownerId) === state?.you;
            const vName = isMine
                ? (isAr ? 'قريتك' : 'Your village')
                : (isAr ? `قرية ${vOwner}` : `${vOwner}'s village`);
            kicker = isAr ? `هذه الواحة تابعة لـ ${vName}` : `Linked to ${vName}`;
        } else {
            kicker = isAr ? 'واحة حرة غير محتلة' : 'Unclaimed oasis';
        }
        if ((oasis?.burnedRemainingSeconds ?? 0) > 0) {
            kicker += isAr ? ' · [واحة محروقة]' : ' · [Burned oasis]';
        } else if ((oasis?.burningProgress ?? 0) > 0) {
            kicker += isAr ? ' · [جاري الحرق]' : ' · [Burning in progress]';
        }

        let effectiveGarrison: Record<string, number> | null = null;
        if (oasis && oasis.ownerId === state?.you && state?.world?.armies && state?.you) {
            effectiveGarrison = { ...(oasis.garrison ?? {}) };
            const you = state.you;
            const stationedArmies = state.world.armies.filter(a =>
                a.defendingSiteId === oasis.id ||
                (Math.hypot(a.position.x - oasis.position.x, a.position.y - oasis.position.y) <= 40 &&
                 (a.ownerId === you || a.supportedOwnerId === you || oasis.ownerId === you))
            );
            for (const a of stationedArmies) {
                for (const [unitId, count] of Object.entries(a.formation ?? {})) {
                    if (count > 0 && !(oasis.garrison && oasis.garrison[unitId])) {
                        effectiveGarrison[unitId] = (effectiveGarrison[unitId] ?? 0) + count;
                    }
                }
            }
        }
        const oasisWithGarrison = oasis ? { ...oasis, garrison: effectiveGarrison } : null;

        const protectedEnemy = !!oasis?.ownerId && oasis.ownerId !== state?.you && (state?.shieldRemainingSeconds ?? 0) > 0;
        html = oasisWithGarrison && state ? oasisPanel(oasisWithGarrison, state.you, owner(oasisWithGarrison.ownerId), running(), protectedEnemy,
            (state.world?.warfare?.settlements ?? []).filter(c => c.ownerId === state!.you && !c.ruined).length > 1,
            state.world?.warfare?.settlements.find(c => c.id === oasisWithGarrison.villageId)?.position) : '<p class="empty">Oasis not found.</p>';
    }
    else if (panel === "village") {
        title = owner(state!.world?.warfare?.settlements.find(c => c.id === panelId)?.ownerId) + "'s village";
        kicker = "SETTLEMENT";
        html = villagePanel();
    }
    else if (panel === "army") {
        title = "Expedition";
        kicker = "FIELD COMMAND";
        html = armyPanel();
    }
    else if (panel === "dispatch") {
        title = "Plan an expedition";
        kicker = "MARCHING ORDERS";
        html = dispatchPanel();
    }
    else if (panel === "armies") {
        title = "Your armies";
        kicker = "RALLY POINT";
        html = ownArmies().map((a, i) => button(`<span><b>Army ${i + 1} · ${a.soldiers} troops</b><small>${a.order} · ${Math.floor(a.morale ?? 0)}% morale</small></span><span>→</span>`, "army", a.id, "list-button")).join("") || '<p class="empty">Your soldiers are at home.<br/>Raise an expedition to explore.</p>';
        html += `<div class="panel-footer">${button("Raise expedition", "rally", "", "primary wide", !running() || !state!.economy)}</div>`;
    }
    else if (panel === "standings" || panel === "castle") {
        title = "Kingdoms";
        kicker = "THE CROWN OF THE VALLEY";
        html = standingsPanel();
    }
    else if (panel === "lobby") {
        title = state!.name;
        kicker = "REALM LOBBY";
        html = lobbyPanel();
    }
    else if (panel === "rooms") {
        title = "Play with friends";
        kicker = "MULTIPLAYER";
        html = `<label>Realm name<input id="room-name" maxlength="32" value="Sunmeadow friends"/></label>${button("Create realm", "create", "", "primary wide")}<div class="stat-line"><span>Open realms</span>${button("Refresh", "refresh", "", "quiet")}</div>${roomList.slice(page * 4, page * 4 + 4).map(r => button(`<span>${esc(r.name)}<small>${r.humans} humans + ${r.bots} AI / ${r.capacity}</small></span><span>Join</span>`, "join", r.id, "list-button", r.status !== "Lobby" || r.humans + r.bots >= r.capacity)).join("") || '<p class="muted">No open realms. Create one to invite your friends.</p>'}${roomList.length > 4 ? pager(roomList.length, 4) : ""}`;
    }
    else {
        title = selection?.name ?? "Open meadow";
        kicker = panel.toUpperCase();
        const convoy = state?.world?.convoys.find(c => c.id === panelId);
        html = `<div class="panel-hero">${buildingArt("Warehouse")}</div><div class="stat-line"><span>Coordinates</span><b>${Math.round(selection?.position.x ?? 0)} : ${Math.round(selection?.position.y ?? 0)}</b></div>${convoy ? `<p class="muted">${esc(owner(convoy.ownerId))} · ${convoy.wagons} wagons · ${convoy.escorts} escorts</p>` : ""}<div class="panel-footer">${button(panel === "convoy" ? "Intercept convoy" : "March here", "plan-attack", panelId, "primary wide", !running() || !!convoy && (convoy.ownerId === state!.you || convoy.inBattle || state!.shieldRemainingSeconds > 0))}${button("Camp here", "plan-camp", "", "primary wide", !running())}${panel === "ground" && state?.world?.warfare?.settlements.some(c => c.id === state?.economy?.villageId && c.tribe === "Mongols") ? button("Relocate village here", "relocate", "", "quiet wide", !running()) : ""}</div>`;
    }
    el("drawer-title").textContent = title;
    el("drawer-kicker").textContent = kicker;
    el("drawer").dataset.panel = panel ?? "";
    el("drawer").dataset.buildingName = panel === "building" ? state?.economy?.targets.find(t=>t.id===panelId)?.name ?? "" : "";
    const report = panel === 'report' ? state?.world?.reports?.find(r=>r.id===panelId) : null;
    el('drawer').style.setProperty('--report-width', `${Math.min(1080,Math.max(560,Math.min(9,Math.max(report?.attackerUnits?.length??0,report?.defenderUnits?.length??0))*74+150))}px`);
    const bodyKey = `${currentLanguage()}|${panel}|${panelId}|${html}`;
    if (force || drawerBody !== bodyKey) {
        const scroll = el("drawer-body").scrollTop;
        const body=el("drawer-body");
        if(!force){
            const template=document.createElement('template');template.innerHTML=html;
            patchPanel(body,template.content);
        } else body.innerHTML=html;
        el("drawer-body").scrollTop = force ? 0 : scroll;
        drawerBody = bodyKey;
    }
    if (el("drawer-tabs").dataset.content !== drawerTabs) {
        el("drawer-tabs").innerHTML = drawerTabs;
        el("drawer-tabs").dataset.content = drawerTabs;
    }
    if (panel === "report" && reportDetail && document.getElementById("report-detail")) el("report-detail").textContent = reportDetail;
    renderProvisions();
    localizeNow();
}
function updateTotal() { const t = state?.economy?.targets.find(t => t.id === selectedUnit); if (!t || !el("training-total"))
    return; el("training-total").innerHTML = costs(t.trainingCost, amount); el("total-quantity").textContent = String(amount); el("training-duration").textContent = time((t.trainingSeconds ?? 0) * amount); const train = document.querySelector<HTMLButtonElement>('[data-action="train"]'); if (train) {
    const laneJobs = state!.economy!.jobs.filter(j => j.lane === (t.trainingBuilding ?? facility(unitId(t))));
    train.textContent = laneJobs.length ? `Queue ${amount} recruits` : `Train ${amount} recruits`;
    train.disabled = !running() || !t.unlocked || !affordable(t.trainingCost, amount) || laneJobs.length >= 12;
} }
async function launch() {
    if (!state?.economy || !selection) return;
    if(!objectiveAllowed())throw Error("The selected army lacks the required specialist.");
    const chosen = Object.fromEntries(Object.entries(formation).filter(([, n]) => n > 0));
    if (!Object.keys(chosen).length) throw Error("Choose at least one unit.");
    let id = dispatchArmy, ordered = false;
    const kind = selection.kind, city = state.world?.warfare?.settlements.find(c => c.id === selection?.id);
    if (id === "new") {
        const before = new Set(ownArmies().map(a => a.id));
        ordered = dispatchMission === "camp" || dispatchMission !== "scout" && (dispatchMission === "reinforce" || kind === "oasis" || kind === "village");
        if (!await command("DispatchArmy",state.economy.villageId,{formation:chosen,wagons:0,enabled:dispatchAuto,escorts:0,x:selection.position.x,y:selection.position.y,afterAttack,
            objective:attackObjective, destinationId: dispatchMission === "camp" ? null : ordered ? selection.id : null, reinforcement: dispatchMission === "reinforce",oasisMode:dispatchMission==='burn'?'Burn':dispatchMission==='blockade'?'Blockade':'Capture',camp:dispatchMission==='camp',loadedOnly:campLoadedOnly})) return;
        id = ownArmies().find(a => !before.has(a.id))?.id ?? '';
    }
    if (!id) return;
    const action = dispatchMission === "camp" ? "CampArmy" : dispatchMission === 'scout' ? 'Scout' : dispatchMission === 'reinforce' ? 'Reinforce' : kind === 'village' && city?.ownerId !== state.you ? 'BeginSiege' : kind === 'army' && selection.ownerId !== state.you ? 'AttackArmy' : kind === 'convoy' ? 'InterceptConvoy' : kind === 'oasis' ? (dispatchMission==='burn'?'BurnOasis':dispatchMission==='blockade'?'BesiegeOasis':'CaptureOasis') : 'MoveArmy';
    if (ordered || await command(action,id,{destinationId:dispatchMission === "camp" || kind === "ground" ? null : selection.id,x:selection.position.x,y:selection.position.y,afterAttack,objective:attackObjective,formation:chosen,loadedOnly:campLoadedOnly})) {
        activeArmy=id; loadArmy(); openPanel('army',id);
    }
}
document.addEventListener("input", event => { const input = event.target as HTMLInputElement;
    if (input.id.startsWith("transfer-") && input.id !== "transfer-source") {
        const resource = input.id.slice(9) as keyof Resources;
        if (resource in transferDraft) transferDraft[resource] = Math.max(0, Math.min(1000000, Math.floor(Number(input.value) || 0)));
    }
    if (input.id === 'market-offer-amount') marketDraft.offerAmount = Math.floor(Number(input.value));
    if (input.id === 'market-ask-amount') marketDraft.askAmount = Math.floor(Number(input.value));
    if (input.dataset.formation) {
    formation[input.dataset.formation] = Math.min(Number(input.max), Math.max(0, Math.floor(Number(input.value) || 0)));
    const quickBtn = input.parentElement?.querySelector<HTMLButtonElement>('[data-action="formation-quick"]');
    if (quickBtn) { delete quickBtn.dataset.quickStage; quickBtn.textContent = "½"; }
    renderProvisions();
    return;
} if (input.id === "train-range" || input.id === "train-amount") {
    amount = Math.max(1, Math.min(100, Math.floor(Number(input.value) || 1)));
    const other = el<HTMLInputElement>(input.id === "train-range" ? "train-amount" : "train-range");
    if (other)
        other.value = String(amount);
    updateTotal();
} if (input.id === "supply-wagons")
    supplyWagons = Number(input.value); if (input.id === "supply-escorts")
    escorts = Number(input.value); });
document.addEventListener("change", event => { const input = event.target as HTMLInputElement, key = input.id; if (key === "transfer-source") transferSource = input.value; if(key==="market-offer-resource") marketDraft.offerResource=input.value; if(key==="market-ask-resource") marketDraft.askResource=input.value; if(key.startsWith("market-")) renderDrawer(true); if (key === "after-attack") { afterAttack = input.value; if(panel === "dispatch") renderDrawer(true); } if (key === "after-attack" && panel === "army" && army()) void perform(async()=>{await command("SetAfterAttack", army()!.id, {afterAttack});}); if (key === "supply-source")
    supplySource = input.value; if (key === "dispatch-army") { dispatchArmy = input.value;
    page = 0; loadDispatchFormation(); highlightDispatchArmy(); renderDrawer(true);
} if (key === "supply-auto")
    autoSupply = input.checked; if (key === "dispatch-auto")
    dispatchAuto = input.checked; if (key === "camp-loaded-only")
    campLoadedOnly = input.checked; if (key === "province")
    void perform(async () => { await command("SelectVillage", input.value); openPanel("building", state!.economy!.targets.find(t => t.name === "MainHall")!.id, "govern"); }); });
document.addEventListener("click", event => {
    if((event.target as Element).closest(".unit-help,#unit-tooltip"))return;
    const choice = (event.target as Element).closest<HTMLButtonElement>('[data-choice]');
    if(choice){const input=el<HTMLInputElement>(choice.dataset.choice!);input.value=choice.dataset.value!;choice.closest('.choice-grid')?.querySelectorAll<HTMLButtonElement>('button').forEach(b=>{const active=b===choice;b.classList.toggle('active',active);b.setAttribute('aria-pressed',String(active));b.querySelector('.choice-check')!.innerHTML=active?'&#10003;':'';});input.dispatchEvent(new Event('change',{bubbles:true}));return;}
    const b = (event.target as Element).closest<HTMLButtonElement>("button");
    if (!b || b.disabled)
        return;
    const action = b.dataset.action, id = b.dataset.id ?? "";
    if (!action)
        return;
    if (action === "formation-quick") {
        const max = Math.max(0, Number(b.dataset.max ?? state?.economy?.garrison[id] ?? 0));
        const half = max > 0 ? Math.max(1, Math.floor(max / 2)) : 0;
        const isSecondClick = b.dataset.quickStage === "half";
        const next = isSecondClick ? max : half;
        b.dataset.quickStage = isSecondClick ? "all" : "half";
        b.textContent = isSecondClick ? "½" : (currentLanguage() === "ar" ? "الكل" : "All");
        formation[id] = next;
        const input = b.parentElement?.querySelector<HTMLInputElement>(`input[data-formation="${id}"]`);
        if (input) input.value = String(next);
        renderProvisions();
        return;
    }
    if(action==='attack-objective'){if(objectiveAllowed(id)){attackObjective=id;renderProvisions();}return;}
    if(action==='event-army'){const a=state?.world?.armies.find(a=>a.id===id);if(a){selection={...a,kind:'army',name:owner(a.ownerId)};map.focus(a.position);if(a.ownerId===state?.you){activeArmy=id;loadArmy();}openPanel('army',id);}return;}
    if(action === 'dismiss-alert' || action === 'read-alerts') {for(const a of state?.world?.alerts??[])if(action==='read-alerts'||a.id===Number(id))alertInbox.dismiss(a.id);renderImportantAlerts();if(panel==='alerts')renderDrawer(true);return;}
    if(action==='all-alerts'){openPanel('alerts');return;}
    if(action==='important-alert'){
        const a=state?.world?.alerts?.find(a=>a.id===Number(id));if(!a)return;
        const oasis=state?.world?.oases.find(o=>o.id===a.targetId),city=state?.world?.warfare?.settlements.find(c=>c.id===a.targetId),convoy=state?.world?.convoys.find(c=>c.id===a.targetId);
        alertInbox.dismiss(a.id);renderImportantAlerts();
        if(oasis){selection={...oasis,kind:'oasis'};map.focus(oasis.position);map.select(selection);openPanel('oasis',oasis.id);}
        else if(city){selection={...city,kind:'village',name:owner(city.ownerId)};map.focus(city.position);openPanel('village',city.id);}
        else if(convoy){selection={...convoy,kind:'convoy',name:'Caravan'};map.focus(convoy.position);openPanel('convoy',convoy.id);}
        else openPanel('alerts');return;
    }
    if(action==='oasis-wall-info'){selection=state?.world?.oases.find(o=>o.id===id)?{...state.world.oases.find(o=>o.id===id)!,kind:'oasis'}:null;openPanel('oasis',id);return;}
    if(action==='focus-possession'){
        const type = b.dataset.type;
        if (type === 'village') {
            const v = (state?.world?.warfare?.settlements ?? []).find(s => s.id === id);
            if (v) {
                map.focus(v.position);
                selection = { ...v, kind: 'village', name: owner(v.ownerId) };
                map.select(selection);
                void perform(async () => {
                    if (state?.economy?.villageId !== v.id) await command("SelectVillage", v.id);
                    openPanel("village", v.id);
                    renderBuildings();
                });
            }
        } else if (type === 'oasis') {
            const o = (state?.world?.oases ?? []).find(s => s.id === id);
            if (o) {
                map.focus(o.position);
                selection = { ...o, kind: 'oasis' };
                map.select(selection);
                openPanel("oasis", o.id);
            }
        }
        return;
    }
    if (action === 'focus-army') {
        const a = state?.world?.armies.find(x => x.id === id);
        if (a) {
            selection = { ...a, kind: 'army', name: owner(a.ownerId) };
            map.focus(a.position);
            map.select(selection);
            if (a.ownerId === state?.you) {
                activeArmy = a.id;
                loadArmy();
            }
            openPanel('army', a.id);
            renderArmiesStrip();
        }
        return;
    }
    if(action==='plan-blockade'){openDispatch('blockade');return;}
    if(action==='plan-burn'){openDispatch('burn');return;}
    if(action==='plan-camp'){openDispatch('camp');return;}
    if(action==='scout-target'){openDispatch('scout');return;}
    if(action==='oasis-level'){void perform(async()=>{const oasis=state?.world?.oases.find(o=>o.id===id);if(!oasis)return;await command('UpgradeOasisLevel',id);});return;}
    if(action?.startsWith('oasis-')){void perform(async()=>{const oasis=state?.world?.oases.find(o=>o.id===id);if(!oasis)return;await command(action==='oasis-auto'?'SetOasisTransport':action==='oasis-wall'?'UpgradeOasisWall':'CollectOasis',id,{enabled:!oasis.autoTransport});});return;}
    if (action === "unit-detail") { reportDetail = b.dataset.detail ?? ""; el("report-detail").textContent = reportDetail; return; }
    if (action === "production") { openPanel("production", id); return; }
    if (action === "report") { openReport(id); renderIntelligence(); return; }
    if (action === "show-reports") { openPanel("reports", "", "all"); return; }
    if (action === "show-armies") { openPanel("armies"); return; }
    if (action === "report-map") { const r = state?.world?.reports?.find(r => r.id === id); if (r) { showMap(); map.focus(r.position); } return; }
    if (action === "report-orders") {
        const os=state?.world?.oasisSieges?.find(s=>s.id===id);
        if(os){if(os.attackerOwners.includes(state!.you)){activeArmy=state?.world?.armies.find(a=>os.armyIds.includes(a.id)&&a.ownerId===state?.you)?.id??os.armyId;loadArmy();openPanel('army',activeArmy);}else{const oasis=state?.world?.oases.find(o=>o.id===os.oasisId);if(oasis){selection={...oasis,kind:'oasis'};openPanel('oasis',oasis.id);}}return;}
        const r = state?.world?.reports?.find(r => r.id === id), siege = state?.world?.warfare?.sieges.find(s => s.id === id);
        if (r && siege && r.attacker === state?.you) { activeArmy = siege.armyId; loadArmy(); openPanel("army", activeArmy, "overview"); }
        else if (r?.villageId) void perform(async () => { if (await command("SelectVillage", r.villageId!)) { const wall = state?.economy?.targets.find(t => t.name === "Wall"); if (wall) openPanel("building", wall.id); } });
        return;
    }
    if (action === "tab") {
        panelTab = id;
        page = 0;
        renderDrawer(true);
        return;
    }
    if (action === "unit") {
        selectedUnit = id;
        renderDrawer(true);
        return;
    }
    if (action === "page-prev" || action === "page-next") {
        page = Math.max(0, page + (action === "page-next" ? 1 : -1));
        renderDrawer(true);
        return;
    }
    if (action === "building") {
        openBuilding(id);
        return;
    }
    if (action === "tier") {
        openPanel("building", id);
        return;
    }
    if(action==='battle'){
        const battle=state?.world?.warfare?.battles.find(b=>b.attackerId===id||b.defenderId===id);if(battle)openReport(battle.id);return;
    }
    if (action === "army") {
        activeArmy = id;
        loadArmy();
        openPanel("army", id);
        return;
    }
    if (action === "rally") {
        const home = state?.world?.warfare?.settlements.find(c => c.id === state?.economy?.villageId);
        const oasis = [...(state?.world?.oases ?? [])].sort((a, b) => Math.hypot(a.position.x - (home?.position.x ?? 500), a.position.y - (home?.position.y ?? 500)) - Math.hypot(b.position.x - (home?.position.x ?? 500), b.position.y - (home?.position.y ?? 500)))[0];
        selection = oasis ? { ...oasis, kind: "oasis" } : null;
        preferredDispatchArmy = ""; openDispatch();
        renderDrawer(true);
        return;
    }
    if (action === "reinforce") { openDispatch("reinforce"); return; }
    if (action === "trade-convoy") { const convoy = state?.world?.convoys.find(c=>c.id===id); if(convoy){selection={...convoy,kind:"convoy",name:"Trade caravan"}; map.focus(convoy.position);openPanel("convoy",id);} return; }
    if (action === "locate-army") {
        if (dispatchArmy !== id) { dispatchArmy = id; loadDispatchFormation(); }
        highlightDispatchArmy(true); renderDrawer(true);
        return;
    }
    if (action === "plan-attack") { openDispatch(); return; }
    if (action === "army-destination") {
        preferredDispatchArmy = id; showMap();
        notice("Choose a destination on the world map.");
        return;
    }
    if (action === "relocation-map") {
        showMap();
        notice("Choose a destination on the world map.");
        return;
    }
    if (action === "threat") {
        const t = state?.world?.armies.find(a => a.id === id);
        if (t) {
            renderWorld();
            selection = { ...t, kind: "army", name: owner(t.ownerId) + " army" };
            map.select(selection);
            map.focus(t.position);
            openPanel("army", id);
        }
        return;
    }
    if (action === "job") {
        const t = state?.economy?.targets.find(t => t.id === id);
        if (t) {
            if (t.kind === "Training") {
                const f = state!.economy!.targets.find(f => f.name === (t.trainingBuilding ?? facility(unitId(t))));
                if (f) {
                    openPanel("building", f.id, "training");
                    selectedUnit = id;
                    renderDrawer(true);
                }
            }
            else
                openBuilding(id);
        }
        return;
    }
    void perform(async () => {
        if (action === 'send-resources') await command('SendResources', transferSource, { destinationId: id, shipment: transferDraft });
        else if (action === 'alliance-propose') await command('ProposeAlliance', id);
        else if (action === 'alliance-accept' || action === 'alliance-decline') await command('RespondAlliance', id, { enabled: action === 'alliance-accept' });
        else if (action === 'alliance-cancel') await command('CancelAllianceRequest', id);
        else if (action === 'market-post') await command('PostMarketOffer',state!.economy!.villageId,marketDraft as unknown as Record<string,unknown>);
        else if (action === 'market-accept') await command('AcceptMarketOffer',state!.economy!.villageId,{destinationId:id});
        else if (action === 'market-cancel') await command('CancelMarketOffer',id);
        else if (action === "upgrade")
            await command("Upgrade", id);
        else if (action === "train")
            await command("Train", id, { quantity: amount });
        else if (action === "auto") {
            const t = state?.economy?.targets.find(t => t.id === id);
            await command("SetAutoQueue", id, { enabled: !t?.auto });
        }
        else if (action === "enter" || action === "defend") {
            if (state?.economy?.villageId !== id)
                await command("SelectVillage", id);
            renderWorld();
            if (action === "defend")
                openPanel("building", state!.economy!.targets.find(t => t.name === "Wall")!.id);
            else
                closePanel();
        }
        else if (action === "dispatch-send")
            await launch();
        else if (action === "after")
            await command("SetAfterAttack", id, { afterAttack });
        else if (action === "recall")
            await command("RecallArmy", id);
        else if (action === "supply")
            await command("ConfigureSupply", id, { sourceVillageId: supplySource, wagons: supplyWagons, escorts, enabled: autoSupply });
        else if (action === "relocate")
            await command("RelocateVillage", state!.economy!.villageId, { x: selection?.position.x, y: selection?.position.y });
        else if (action === "castle-march")
            await command("CaptureCastle", activeArmy);
        else if (action === "refresh") {
            roomList = await connection.invoke<RoomSummary[]>("ListRooms");
            renderDrawer(true);
        }
        else if (action === "create")
            render(await connection.invoke<Snapshot>("CreateRoom", el<HTMLInputElement>("player-name").value, el<HTMLInputElement>("tribe").value, el<HTMLInputElement>("room-name").value, { capacity: 8, durationMinutes: 45, shieldMinutes: 5, resourceSpeed: 1, botCount: 6, botDifficulty: "Normal" }));
        else if (action === "join")
            render(await connection.invoke<Snapshot>("JoinRoom", id, el<HTMLInputElement>("player-name").value, el<HTMLInputElement>("tribe").value));
        else if (action === "ready")
            render(await connection.invoke<Snapshot>("SetReady", !state!.players.find(p => p.id === state!.you)?.ready));
        else if (action === "start")
            render(await connection.invoke<Snapshot>("StartMatch"));
        else if (action === "settings") {
            const v = (id: string) => el<HTMLInputElement>(id).value;
            render(await connection.invoke<Snapshot>("ConfigureRoom", { capacity: Number(v("capacity")), durationMinutes: Number(v("duration")), shieldMinutes: Number(v("shield")), resourceSpeed: Number(v("speed")), botCount: Number(v("bots")), botDifficulty: v("difficulty") }));
        }
        else if (action === "leave") {
            await connection.invoke("LeaveRoom");
            closePanel();
            render(null);
            await connection.invoke("ListRooms");
        }
    });
});
el('language-picker').innerHTML=select('language-select','',[{value:'ar',label:'AR'},{value:'en',label:'EN'}],currentLanguage());
el('tribe-picker').innerHTML=select('tribe','Your people',['Romans','Germans','Greeks','Mongols','Persians'],'Romans');
el('language-select').onchange = () => setLanguage(el<HTMLInputElement>('language-select').value);
el<HTMLInputElement>('language-select').value=currentLanguage();
el('village-biome').onclick=()=>{selection=null;openPanel('biome',el('village-biome').dataset.id??'');};
window.addEventListener('languagechange',()=>{map.refreshLanguage();renderDrawer(true);localizeNow();});
document.getElementById("view-buildings")?.addEventListener("click", () => void perform(async () => {
    buildingOasisId=null;
    const primary = state?.players.find(p => p.id === state?.you)?.villageId;
    if (primary && state?.economy?.villageId !== primary) await command("SelectVillage", primary);
    closePanel(); renderBuildings();
}));
document.getElementById("view-world")?.addEventListener("click", () => showMap());
el("view-reports").onclick = () => openPanel("reports", "", "all");
el("muster-hud").onclick = () => openPanel("muster");
document.getElementById("view-armies")?.addEventListener("click", () => openPanel("armies"));
el("view-standings").onclick = () => openPanel("standings");
el("realm-menu").onclick = () => gameMenu.open();
el("close-drawer").onclick = () => { closePanel(); if (!state)
    el("welcome").hidden = false; };
el("zoom-in").onclick = () => map.zoom(.2);
el("zoom-out").onclick = () => map.zoom(-.2);
el("map-home").onclick = () => map.home();
el("map-biomes").onclick = () => { el("map-biomes").setAttribute("aria-pressed", String(map.toggleBoundaries())); };
el("chat-toggle").onclick = () => { chatOpen = !chatOpen; renderChat(); };
el("chat-form").addEventListener("submit", event => {
    event.preventDefault();
    const input = el<HTMLInputElement>("chat-input"), message = input.value.trim();
    if (!message) return;
    void perform(async () => { if (await command("SendChat", "00000000-0000-0000-0000-000000000000", { message })) input.value = ""; });
});
el("multiplayer").onclick = () => void perform(async () => { roomList = await connection.invoke<RoomSummary[]>("ListRooms"); el("welcome").hidden = true; openPanel("rooms"); });
el("play-solo").onclick = () => void perform(async () => { render(await connection.invoke<Snapshot>("CreateRoom", el<HTMLInputElement>("player-name").value, el<HTMLInputElement>("tribe").value, "Sunmeadow Valley", { capacity: 8, durationMinutes: 45, shieldMinutes: 5, resourceSpeed: 1, botCount: 7, botDifficulty: "Normal" })); await connection.invoke("SetReady", true); render(await connection.invoke<Snapshot>("StartMatch")); });
connection.on("Snapshot", (snapshot: Snapshot) => { if (state?.id === snapshot.id)
    render(snapshot); });
connection.onreconnecting(()=>{gameAudio.reset();status();});
connection.onclose(()=>{gameAudio.reset();status();});
connection.onreconnected(() => { status(); void perform(async () => render(await connection.invoke<Snapshot | null>("Resume"))); });
async function connect(resumeReserved=false) { if (connecting || connection.state !== HubConnectionState.Disconnected)
    return; connecting = true; el<HTMLButtonElement>('resume-game').disabled=true; try {
    const response = await fetch("/api/session", { method: "POST", credentials: "same-origin" });
    if (!response.ok)
        throw Error("Could not enter the realm.");
    await loadUnits();
    await connection.start();
    const snapshot=await connection.invoke<Snapshot | null>('Resume');
    if(returnedToMenu&&!resumeReserved&&snapshot?.status==='Running'){
        await connection.stop();render(null);
    }else{setReturnedToMenu(false);render(snapshot);}
    status();
}
catch (e) {
    notice(String(e), true);
    status();
}
finally {
    connecting = false;
    el<HTMLButtonElement>('resume-game').disabled=false;
} }
el("reconnect").onclick = () => void connect();
el('resume-game').onclick=()=>void connect(true);
window.addEventListener("resize", () => { if (panel && ["muster", "reports", "report", "production"].includes(panel)) { page = 0; renderDrawer(true); } });
new ResizeObserver(entries=>{for(const entry of entries){const name=entry.target.id==='drawer'?'--drawer-space':'--events-height';el('game').style.setProperty(name,`${entry.target.getBoundingClientRect()[entry.target.id==='drawer'?'width':'height']}px`);}}).observe(el('drawer'));
new ResizeObserver(()=>el('game').style.setProperty('--events-height',`${el('important-alerts').getBoundingClientRect().height}px`)).observe(el('important-alerts'));
window.setInterval(renderTimers, 250);
void connect();
