import { oasisPanel, type OasisGarrisonView } from "./oasis-panel";
import { activeEvents, LiveEventBar } from "./live-events";
import { AlertInbox } from "./alerts";
import { currentLanguage, setLanguage, localizeNow, t } from "./locale";
import { settlementStage } from "./settlements";
import { biomeAt } from "./landscape";
import { loadUnits, unitPortrait, unitArtwork, unitCatalog, provisionEstimate, initUnitTooltips, compareUnitIds } from "./units";
import "./style.css";
import "./gameplay-v3.css";
import "./command-deck.css";
import "./rts-command.css";
import "./progression.css";
import "./social.css";
import { resolveTargetBehavior, renderCursorBadge, renderRadialMenu, canAttackArmyInBattle, type RadialActionId, type RadialOption } from "./rts-command";
import { ProgressFeedback } from './progression';
import { GameAudio } from './game-audio';
import { GameGuide } from './guide';
import { GameMenu } from './game-menu';
import { GameTutorial, type TutorialContext } from './tutorial';
import './guide.css';
import './game-menu.css';
import './tutorial.css';
import { type Production, type Report, productionPanel, musterPanel, reportsPanel, reportPanel, liveReport, cargoView } from "./intelligence";
import { HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";
import { WorldMap, type MapPlayer, type MapSelection, type WorldView, type RankView, type ArmyView } from "./map";
import { buildingArt } from "./art";
import { marketPanel, marketOffersSection, type MarketDraft } from "./market";
import { DebugOverlay, type ServerDebugTelemetry } from "./debug-overlay";
import { AdminOverlay } from "./admin-overlay";
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
    castleUnlockMinutes?: number;
    shieldMinutes: number;
    resourceSpeed: number;
    botCount: number;
    botDifficulty: string;
}
const LOBBY_PALETTE = [
    "#e11d48", "#2563eb", "#facc15", "#9333ea", "#06b6d4", "#f97316", "#f8fafc", "#18181b",
    "#ec4899", "#10b981", "#78350f", "#84cc16", "#1e3a8a", "#0f766e", "#f43f5e", "#d97706"
];

interface Player extends MapPlayer {
    tribe: string;
    isBot: boolean;
    ready: boolean;
    online: boolean;
    team?: number;
    randomTribe?: boolean;
    spawnSlot?: number | null;
    effectiveSpawnSlot?: number | null;
}
interface ChatMessage { id: number; senderId: string | null; text: string; at: number; kind: string; senderName?: string | null }
interface Alliance { firstPlayerId: string; secondPlayerId: string; since: number }
interface AllianceRequest { id: string; senderId: string; recipientId: string; createdAt: number }
interface SpectatorInfo { id: string; name: string }
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
    isSpectator?: boolean;
    spectators?: SpectatorInfo[];
    speedMultiplier?: number;
}
interface RoomSummary {
    id: string;
    name: string;
    status: string;
    humans: number;
    bots: number;
    capacity: number;
    spectators?: number;
    speedMultiplier?: number;
    isBotOnly?: boolean;
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
let deferredSnapshotsCount = 0;
let adminEnabled = false;
let menuChatVisible = false;
const syncDebugButton = (open: boolean) => document.getElementById("toggle-debug")?.setAttribute("aria-pressed", String(open));
const adminOverlay = new AdminOverlay({
    onSetSpeed: async (multiplier, allBotRooms) => {
        const snap = await connection.invoke<Snapshot | null>("AdminSetSpeed", multiplier, allBotRooms);
        if (snap) render(snap);
        roomList = await connection.invoke<RoomSummary[]>("ListRooms");
        syncAdminOverlay();
    },
    onGiveResources: async (targetPlayerId, wood, iron, stone, wheat) => {
        const snap = await connection.invoke<Snapshot>("AdminGiveResources", targetPlayerId, wood, iron, stone, wheat);
        render(snap);
    },
    onGiveTroops: async (targetPlayerId, unitId, count) => {
        const snap = await connection.invoke<Snapshot>("AdminGiveTroops", targetPlayerId, unitId, count);
        render(snap);
    },
    onSpawnBotRooms: async (count, botsPerRoom, difficulty, speedMultiplier, shieldMinutes) => {
        roomList = await connection.invoke<RoomSummary[]>("AdminSpawnBotRooms", count, botsPerRoom, difficulty, speedMultiplier, shieldMinutes);
        if (panel === "rooms") renderDrawer();
        syncAdminOverlay();
    },
    onClearBotRooms: async () => {
        roomList = await connection.invoke<RoomSummary[]>("AdminClearBotRooms");
        if (state && !roomList.some(r => r.id === state!.id)) {
            closePanel();
            render(null);
        } else if (panel === "rooms") {
            renderDrawer();
        }
        syncAdminOverlay();
    },
    onSpectateRoom: async (roomId) => {
        const playerName = (document.getElementById("player-name") as HTMLInputElement | null)?.value || "Spectator";
        const snap = await connection.invoke<Snapshot>("SpectateRoom", roomId, playerName);
        el("welcome").hidden = true;
        closePanel();
        render(snap);
    },
    onVisibilityChange: (open) => {
        document.getElementById("view-admin")?.setAttribute("aria-pressed", String(open));
    }
});
function syncAdminOverlay() {
    const hudAdmin = document.getElementById("view-admin");
    if (hudAdmin) hudAdmin.hidden = !adminEnabled;
    let welcomeAdmin = document.getElementById("welcome-admin-btn") as HTMLButtonElement | null;
    if (adminEnabled && !welcomeAdmin) {
        welcomeAdmin = document.createElement("button");
        welcomeAdmin.id = "welcome-admin-btn";
        welcomeAdmin.type = "button";
        welcomeAdmin.className = "quiet wide welcome-admin-btn";
        welcomeAdmin.textContent = currentLanguage() === "ar" ? "⚡ لوحة الأدمن والشيت (بوتات / تسريع)" : "⚡ Admin & Cheat Panel";
        welcomeAdmin.onclick = () => adminOverlay.toggle();
        document.querySelector(".welcome-card")?.append(welcomeAdmin);
    }
    if (welcomeAdmin) welcomeAdmin.hidden = !adminEnabled;
    adminOverlay.update({
        enabled: adminEnabled,
        inRunningMatch: state?.status === "Running",
        youId: state?.you ?? "",
        speedMultiplier: state?.speedMultiplier ?? 1,
        players: state?.players ?? [],
        rooms: roomList
    });
}
const debugOverlay = new DebugOverlay({
    connectionState: () => connection.state,
    roomId: () => state?.id ?? null,
    roomStatus: () => state?.status ?? null,
    revision: () => state?.revision ?? null,
    pendingActions: () => pendingActions,
    deferredSnapshots: () => deferredSnapshotsCount,
    entities: () => {
        const w = state?.world;
        return {
            players: state?.players.length ?? 0,
            settlements: w?.warfare?.settlements.length ?? 0,
            armies: w?.armies.length ?? 0,
            convoys: w?.convoys.length ?? 0,
            oases: w?.oases.length ?? 0,
            combats: (w?.warfare?.battles.length ?? 0) + (w?.warfare?.sieges.length ?? 0) + (w?.oasisSieges?.length ?? 0),
            aux: (w?.reports?.length ?? 0) + (w?.alerts?.length ?? 0) + (w?.notices?.length ?? 0) + (w?.marketOffers?.length ?? 0) + (state?.chat?.length ?? 0)
        };
    },
    sendPing: (ts: number) => connection.invoke<ServerDebugTelemetry>("Ping", ts)
}, syncDebugButton);
syncDebugButton(debugOverlay.isOpen());
{
    const versionBadge = el("app-version");
    const clientVersion = (import.meta.env.VITE_APP_VERSION as string | undefined) || "v1.0.0-local";
    versionBadge.textContent = clientVersion;
    void fetch("/health", { cache: "no-store" })
        .then(r => (r.ok ? r.json() as Promise<{ version?: string }> : null))
        .then(data => {
            if (data?.version && data.version !== clientVersion)
                versionBadge.textContent = `${clientVersion} · srv:${data.version}`;
        })
        .catch(() => { /* Keep compiled version badge offline. */ });
}
initUnitTooltips();
const alertInbox = new AlertInbox();
const liveEvents = new LiveEventBar();
const gameAudio = new GameAudio();
let returnedToMenu = false;
try { returnedToMenu = sessionStorage.getItem('warzone.returned-to-menu') === 'true'; } catch {/* Session storage is optional. */ }
const resumeMatch = document.createElement('section'); resumeMatch.id = 'resume-match'; resumeMatch.hidden = true;
resumeMatch.innerHTML = '<p class="menu-note">Your match is still running. Return whenever you are ready.</p><button id="resume-game" class="primary wide">Resume match</button>';
el('play-solo').before(resumeMatch);
function setReturnedToMenu(value: boolean) {
    returnedToMenu = value; el('welcome').classList.toggle('away', value); resumeMatch.hidden = !value;
    try { if (value) sessionStorage.setItem('warzone.returned-to-menu', 'true'); else sessionStorage.removeItem('warzone.returned-to-menu'); } catch {/* Keep the current session state. */ }
}
setReturnedToMenu(returnedToMenu);
let alertBanner = "";
function renderImportantAlerts() {
    const box = el("important-alerts"); if (!state) { box.hidden = true; return; }
    alertInbox.use(state.id, state.you);
    alertInbox.names = new Map([...(state.world?.oases ?? []).map(o => [o.id, o.name + ' · ' + Math.round(o.position.x) + ':' + Math.round(o.position.y)] as [string, string]), ...(state.world?.warfare?.settlements ?? []).map(c => [c.id, owner(c.ownerId) + "'s village"] as [string, string])]); const html = liveEvents.render(state.status === "Running" && state.world ? activeEvents(state.world, state.you) : []);
    box.hidden = !html; el("game").classList.toggle("has-live-events", !!html); if (html !== alertBanner) { box.innerHTML = html; alertBanner = html; localizeNow(box); }
}
let reportDetail = "";
let state: Snapshot | null = null, panel: Panel | null = null, panelId = "", panelTab = "overview", selectedUnit = "", activeArmy = "", roomList: RoomSummary[] = [];
let selectedArmies: string[] = [];
let oasisUnitSelection: { oasisId: string; armyId: string; units: string[] } | null = null;
const armyDrafts: Record<string, Record<string, number>> = {};
const editedDrafts = new Set<string>();
let hoveredState: { target: MapSelection | null; client: { x: number; y: number } | null } = { target: null, client: null };
let radialState: { target: MapSelection; options: RadialOption[]; anchor: { x: number; y: number } } | null = null;
let selection: MapSelection | null = null, busy = false, roomBusy = false, pendingActions = 0, connecting = false, page = 0, toastTimer = 0;
let actionQueue: Promise<void> = Promise.resolve();
let pointerPressed = false, pendingSnapshot: Snapshot | null = null;
let amount = 5, afterAttack = "Hold", supplySource = "", supplyWagons = 0, escorts = 2, autoSupply = true;
let attackObjective = "Raid";
let dispatchMission: "attack" | "reinforce" | "blockade" | "scout" | "camp" | "burn" = "attack";
const marketDraft: MarketDraft = { offerResource: "Wood", offerAmount: 500, askResource: "Iron", askAmount: 500 };
const transferDraft: Resources = { wood: 100, iron: 100, stone: 100, wheat: 100 };
let transferSource = "", chatOpen = window.innerWidth > 700, chatKey = "";
let dispatchAuto = true, campLoadedOnly = true, dispatchArmy = "new", preferredDispatchArmy = "", formation: Record<string, number> = {};
let focusedMatchId = "", dismissedVictoryMatchId = "";
let victoryRaf = 0, victoryKey = "";
let clockReceived = performance.now();
const now = () => state ? state.elapsedSeconds + (state.status === "Running" ? Math.min(2, (performance.now() - clockReceived) / 1000) * (state.speedMultiplier ?? 1) : 0) : 0;
const ownArmies = () => state?.world?.armies.filter(a => a.ownerId === state!.you) ?? [];
const army = () => ownArmies().find(a => a.id === activeArmy);
const owner = (id: string | null | undefined) => state?.players.find(p => p.id === id)?.name ?? state?.spectators?.find(s => s.id === id)?.name ?? "Unclaimed";
const running = () => state?.status === "Running" && !state?.isSpectator && connection.state === HubConnectionState.Connected;
const button = (label: string, action: string, id = "", style = "", disabled = false) => `<button data-action="${action}" data-id="${esc(id)}" class="${style}" ${disabled ? "disabled" : ""}>${label}</button>`;
const select = (id: string, label: string, items: (string | { value: string; label: string })[], chosen: string) => `<fieldset class="choice-field"><legend>${label}</legend><input type="hidden" id="${id}" value="${esc(chosen)}"/><div class="choice-grid" role="group" aria-label="${label}">${items.map(item => { const i = typeof item === 'string' ? { value: item, label: pretty(item) } : item; return `<button type="button" class="choice-tile ${i.value === chosen ? 'active' : ''}" data-choice="${id}" data-value="${esc(i.value)}" aria-pressed="${i.value === chosen}"><span class="choice-check" aria-hidden="true">${i.value === chosen ? '&#10003;' : ''}</span>${esc(i.label)}</button>`; }).join('')}</div></fieldset>`;

function isAllyPlayer(playerId?: string | null): boolean {
    if (!state || !playerId || playerId === state.you) return false;
    if (state.alliances.some(a => (a.firstPlayerId === state!.you && a.secondPlayerId === playerId) || (a.secondPlayerId === state!.you && a.firstPlayerId === playerId))) return true;
    const me = state.players.find(p => p.id === state!.you), other = state.players.find(p => p.id === playerId);
    return !!(me?.team && me.team > 0 && me.team === other?.team);
}

interface ControlledArmyInfo {
    id: string;
    isHome: boolean;
    villageId: string;
    name: string;
    position: { x: number; y: number };
    order: string;
    troops: Record<string, number>;
    total: number;
    tribe: string;
}

function ownedVillages() {
    return (state?.world?.warfare?.settlements ?? []).filter(s => s.ownerId === state?.you && !s.ruined);
}

function villageGarrisonMap(villageId: string): Record<string, number> {
    if (!state?.world) return {};
    if (state.world.villageGarrisons && state.world.villageGarrisons[villageId]) return state.world.villageGarrisons[villageId];
    return state.economy?.villageId === villageId ? state.economy.garrison : {};
}

function armyTroopMap(id: string): ControlledArmyInfo | null {
    if (!state?.world) return null;
    const isAr = currentLanguage() === "ar";
    if (id.startsWith("home:")) {
        const vId = id.slice(5);
        const villages = ownedVillages();
        const idx = villages.findIndex(v => v.id === vId);
        if (idx < 0) return null;
        const v = villages[idx]!;
        const troops = Object.fromEntries(Object.entries(villageGarrisonMap(vId)).filter(([, n]) => n > 0));
        const total = Object.values(troops).reduce((s, n) => s + n, 0);
        if (total <= 0) return null;
        const name = villages.length > 1
            ? (isAr ? `جيش قرية ${idx + 1}` : `Village ${idx + 1} army`)
            : (isAr ? "جيش القرية" : "Home army");
        return { id, isHome: true, villageId: vId, name, position: v.position, order: "AtHome", troops, total, tribe: v.tribe };
    }
    const armies = ownArmies();
    const idx = armies.findIndex(a => a.id === id);
    if (idx < 0) return null;
    const a = armies[idx]!;
    const troops = Object.fromEntries(Object.entries(a.formation ?? {}).filter(([, n]) => n > 0));
    const total = a.soldiers + a.wagons;
    const name = `${isAr ? "جيش" : "Army"} ${idx + 1}`;
    return { id: a.id, isHome: false, villageId: a.homeVillageId, name, position: a.position, order: a.order, troops, total, tribe: a.tribe };
}

const halfDefaultCount = (unitId: string, available: number) => role(unitId) === "wagon" || available <= 0 ? 0 : Math.max(1, Math.floor(available / 2));

function syncArmyDraft(info: ControlledArmyInfo) {
    const draft = (armyDrafts[info.id] ??= {});
    for (const [unitId, available] of Object.entries(info.troops)) {
        const key = `${info.id}|${unitId}`;
        if (!editedDrafts.has(key) || !(unitId in draft)) {
            draft[unitId] = halfDefaultCount(unitId, available);
        } else {
            draft[unitId] = Math.max(0, Math.min(available, draft[unitId] ?? 0));
        }
    }
    for (const unitId of Object.keys(draft)) {
        if (!(unitId in info.troops)) {
            delete draft[unitId];
            editedDrafts.delete(`${info.id}|${unitId}`);
        }
    }
}

function hasEditedArmyDraft(id: string): boolean {
    const prefix = `${id}|`;
    for (const key of editedDrafts) {
        if (key.startsWith(prefix)) return true;
    }
    return false;
}

function chosenDraft(id: string): Record<string, number> {
    const info = armyTroopMap(id);
    if (!info) return {};
    syncArmyDraft(info);
    const chosen = Object.fromEntries(Object.entries(armyDrafts[id] ?? {}).filter(([, n]) => n > 0));
    return Object.keys(chosen).length > 0 ? chosen : {};
}

let disbandConfirmKey = "";
let disbandConfirmTimer: number | undefined;
function resetDisbandConfirm() {
    if (disbandConfirmTimer !== undefined) { window.clearTimeout(disbandConfirmTimer); disbandConfirmTimer = undefined; }
    disbandConfirmKey = "";
}
function currentDisbandKey(id: string, chosen: Record<string, number>) {
    return `${id}:${Object.keys(chosen).sort().map(k => `${k}=${chosen[k]}`).join(",")}`;
}
function disbandUpkeepSaved(chosen: Record<string, number>): number {
    const total = Object.entries(chosen).reduce((s, [u, n]) => s + n * (unitCatalog.get(u)?.stats.wheatUpkeep ?? 0), 0);
    return Math.round(total * 10) / 10;
}
function disbandButtonLabel(chosen: Record<string, number>, isAr: boolean, confirming: boolean): string {
    const count = Object.values(chosen).reduce((s, n) => s + n, 0);
    if (confirming) return isAr ? `تأكيد تسريح ${count.toLocaleString()} وحدة؟ (اضغط للتأكيد)` : `Confirm disband ${count.toLocaleString()}? (Click again)`;
    const saved = disbandUpkeepSaved(chosen);
    if (count > 0 && saved > 0) return isAr ? `تسريح المحدد (${count.toLocaleString()} · −${saved.toLocaleString()} قمح/د)` : `Disband (${count.toLocaleString()} · −${saved.toLocaleString()} wheat/m)`;
    return isAr ? `تسريح المحدد (${count.toLocaleString()})` : `Disband selected (${count.toLocaleString()})`;
}
function isVillageUnderSiege(villageId: string): boolean {
    if (state?.economy?.villageId === villageId && state.economy.besieged) return true;
    return (state?.world?.warfare?.settlements.some(s => s.id === villageId && s.besieged) ?? false)
        || (state?.world?.warfare?.sieges.some(s => s.status === "Active" && s.villageId === villageId) ?? false);
}

function effectiveOrderFormation(id: string): Record<string, number> {
    const info = armyTroopMap(id);
    if (!info) return {};
    if (!info.isHome && !hasEditedArmyDraft(id)) {
        return Object.fromEntries(Object.entries(info.troops).filter(([, n]) => n > 0));
    }
    return chosenDraft(id);
}

function selectedHasRole(roleName: string): boolean {
    return selectedArmies.some(id => Object.entries(effectiveOrderFormation(id)).some(([u, n]) => n > 0 && role(u) === roleName));
}

function syncMapSelections() {
    const fieldIds = selectedArmies.filter(id => !id.startsWith("home:"));
    const homeIds = selectedArmies.filter(id => id.startsWith("home:")).map(id => id.slice(5));
    map.setSelectedArmies(fieldIds, homeIds);
}

function applyOasisUnitSelection(oasisId: string, armyId: string, units: string[]) {
    closeRadial();
    const info = armyTroopMap(armyId);
    const validUnits = info ? [...new Set(units)].filter(u => (info.troops[u] ?? 0) > 0) : [];
    for (const old of selectedArmies) {
        if (old !== armyId) {
            delete armyDrafts[old];
            for (const key of [...editedDrafts]) if (key.startsWith(`${old}|`)) editedDrafts.delete(key);
        }
    }
    if (!info || validUnits.length === 0) {
        oasisUnitSelection = null;
        selectedArmies = [];
        delete armyDrafts[armyId];
        for (const key of [...editedDrafts]) if (key.startsWith(`${armyId}|`)) editedDrafts.delete(key);
    } else {
        oasisUnitSelection = { oasisId, armyId, units: validUnits };
        selectedArmies = [armyId];
        activeArmy = armyId;
        const selectedSet = new Set(validUnits);
        const draft: Record<string, number> = {};
        for (const [unitId, available] of Object.entries(info.troops)) {
            draft[unitId] = selectedSet.has(unitId) ? available : 0;
            editedDrafts.add(`${armyId}|${unitId}`);
        }
        armyDrafts[armyId] = draft;
    }
    syncMapSelections();
    renderArmiesStrip();
    refreshCursorBadge();
    renderDrawer(true);
    syncTutorial();
}

function selectArmies(ids: string[], mode: "replace" | "toggle" | "clear" = "replace", focusTarget = false) {
    closeRadial();
    oasisUnitSelection = null;
    if (mode === "clear") {
        for (const id of selectedArmies) {
            delete armyDrafts[id];
            for (const key of [...editedDrafts]) if (key.startsWith(`${id}|`)) editedDrafts.delete(key);
        }
        selectedArmies = [];
    } else if (mode === "toggle") {
        const target = ids[0];
        if (target) {
            if (selectedArmies.includes(target)) {
                selectedArmies = selectedArmies.filter(x => x !== target);
                delete armyDrafts[target];
                for (const key of [...editedDrafts]) if (key.startsWith(`${target}|`)) editedDrafts.delete(key);
            } else if (armyTroopMap(target)) {
                selectedArmies = [...selectedArmies, target];
            }
        }
    } else {
        const valid = [...new Set(ids)].filter(id => !!armyTroopMap(id));
        for (const old of selectedArmies) {
            if (!valid.includes(old)) {
                delete armyDrafts[old];
                for (const key of [...editedDrafts]) if (key.startsWith(`${old}|`)) editedDrafts.delete(key);
            }
        }
        selectedArmies = valid;
    }
    const firstField = selectedArmies.find(id => !id.startsWith("home:"));
    if (firstField) { activeArmy = firstField; loadArmy(); }
    syncMapSelections();
    if (focusTarget && selectedArmies.length === 1) {
        const info = armyTroopMap(selectedArmies[0]!);
        if (info) map.focus(info.position);
    }
    if (selectedArmies.length === 1) {
        const onlyId = selectedArmies[0]!;
        if (onlyId.startsWith("home:")) {
            const vId = onlyId.slice(5);
            if (state?.economy?.villageId !== vId) {
                void perform(async () => { await command("SelectVillage", vId); renderBuildings(); });
            }
        }
        openPanel("army", onlyId);
    } else if (selectedArmies.length > 1) {
        openPanel("army", "multi");
    } else if (panel === "army") {
        closePanel();
    }
    syncTutorial();
    renderArmiesStrip();
    refreshCursorBadge();
}

function closeRadial() {
    if (!radialState && el("radial-menu").hidden) return;
    radialState = null;
    renderRadialMenu(el("radial-menu"), { x: 0, y: 0 }, [], esc);
    refreshCursorBadge();
}

function buildTargetContext() {
    const hasFieldArmies = selectedArmies.some(id => !id.startsWith("home:"));
    const hasFighters = selectedArmies.some(id => Object.entries(effectiveOrderFormation(id)).some(([u, n]) => {
        if (n <= 0) return false;
        const r = role(u);
        return r !== "wagon" && r !== "governor";
    }));
    return {
        you: state?.you ?? "",
        isAlly: isAllyPlayer,
        selectedIds: selectedArmies,
        oases: state?.world?.oases ?? [],
        settlements: state?.world?.warfare?.settlements ?? [],
        armies: state?.world?.armies ?? [],
        convoys: state?.world?.convoys ?? [],
        battles: state?.world?.warfare?.battles ?? [],
        castle: state?.world?.warfare?.castle,
        ownedVillagesCount: ownedVillages().length,
        hasGovernor: selectedHasRole("governor"),
        hasCatapult: selectedHasRole("catapult"),
        hasFieldArmies,
        hasFighters,
        protectedEnemy: (state?.shieldRemainingSeconds ?? 0) > 0,
        isAr: currentLanguage() === "ar"
    };
}

function refreshCursorBadge() {
    const badge = el("map-cursor-badge");
    if (!state?.world || state.status !== "Running" || !selectedArmies.length || radialState || !hoveredState.target || !hoveredState.client) {
        renderCursorBadge(badge, "none", null);
        return;
    }
    const behavior = resolveTargetBehavior(hoveredState.target, buildTargetContext());
    renderCursorBadge(badge, behavior.intent, hoveredState.client);
}

const map = new WorldMap(el("map"), (pick, modifiers) => {
    if (state?.status === "Lobby") {
        if (pick.kind === "village") {
            if (pick.id.startsWith("slot:")) {
                const slot = Number(pick.id.slice(5));
                if (Number.isInteger(slot)) void perform(() => connection.invoke<Snapshot>("ChangeSpawnSlot", state!.you, slot).then(render));
                return;
            }
            const target = pick.ownerId ? state.players.find(p => p.id === pick.ownerId) : undefined;
            if (!target || target.id === state.you) return;
            if (!target.isBot) {
                notice(currentLanguage() === "ar" ? "هذا الموقع محجوز للاعب آخر." : "That spot is taken by another player.", true);
                return;
            }
            const targetSlot = target.effectiveSpawnSlot ?? target.spawnSlot;
            if (targetSlot != null) void perform(() => connection.invoke<Snapshot>("ChangeSpawnSlot", state!.you, targetSlot).then(render));
        }
        return;
    }
    if (!state?.world) return;
    closeRadial();
    selection = pick; map.select(pick); page = 0;
    if (pick.kind === "battle") { buildingOasisId = null; openReport(pick.id); return; }
    if (pick.kind === "village" && pick.ownerId === state.you) {
        if (modifiers.ctrl || modifiers.shift) {
            if (armyTroopMap(`home:${pick.id}`)) selectArmies([`home:${pick.id}`], "toggle", false);
            return;
        }
        buildingOasisId = null;
        void perform(async () => { if (state?.economy?.villageId !== pick.id) await command("SelectVillage", pick.id); openPanel("village", pick.id); renderBuildings(); });
    } else if (pick.kind === "army") {
        buildingOasisId = null;
        if (pick.ownerId === state.you) {
            selectArmies([pick.id], (modifiers.ctrl || modifiers.shift) ? "toggle" : "replace", false);
            return;
        }
        openPanel("army", pick.id);
    } else if (pick.kind === "ground") {
        buildingOasisId = null;
        openPanel("biome", String(biomeAt(state.world.regions ?? [], pick.position)?.id ?? ""));
    }
    else {
        if (pick.kind !== "oasis") buildingOasisId = null;
        openPanel(pick.kind, pick.id);
    }
});
map.setInteractionHandlers({
    onHover(target, client) {
        hoveredState = { target, client };
        refreshCursorBadge();
    },
    onRightClick(target, client) {
        if (!state?.world || state.status !== "Running") return;
        if (!selectedArmies.length) {
            if (target.kind !== "ground") {
                notice(currentLanguage() === "ar" ? "حدد جيشاً أولاً لإصدار الأوامر على الخريطة." : "Select an army first to issue map orders.", true);
            }
            return;
        }
        const behavior = resolveTargetBehavior(target, buildTargetContext());
        if (behavior.directAction) {
            closeRadial();
            if (behavior.directAction.disabled) {
                notice(currentLanguage() === "ar" ? "لا يمكن تنفيذ هذا الأمر أثناء فترة الحماية." : "Wait until the protection shield expires.", true);
                return;
            }
            void perform(() => executeRtsOrder(behavior.directAction!.id, target));
            return;
        }
        if (behavior.options.length) {
            radialState = { target, options: behavior.options, anchor: client };
            refreshCursorBadge();
            renderRadialMenu(el("radial-menu"), client, behavior.options, esc);
        } else {
            closeRadial();
        }
    },
    onCameraMove() { closeRadial(); }
});
const progressFeedback = new ProgressFeedback((point, message) => map.celebrate(point, message));
gameAudio.setScene(() => map.combatSounds());
function buildTutorialContext(): TutorialContext | null {
    if (!state?.world || !state.economy || state.status !== "Running") return null;
    const e = state.economy;
    const oases = state.world.oases ?? [];
    const myVillages = ownedVillages();
    const homePos = myVillages.find(v => v.id === e.villageId)?.position ?? myVillages[0]?.position ?? null;
    const buildingLevelSum = e.targets.filter(t => t.kind === "Building").reduce((s, t) => s + t.level, 0);
    const hasBuildingJob = e.jobs.some(j => j.lane === "Building");
    const hasTrainingJob = e.jobs.some(j => j.lane === "Training");
    const garrisonSum = Object.values(e.garrison).reduce((s, n) => s + n, 0);
    const myArmies = ownArmies();
    const totalTroops = garrisonSum + myArmies.reduce((s, a) => s + a.soldiers, 0);
    const oasisIds = new Set(oases.map(o => o.id));
    const hasArmyToOasis = myArmies.some(a => !!a.targetId && oasisIds.has(a.targetId) && ["Marching", "Occupying"].includes(a.order));
    const ownedOases = oases.filter(o => o.ownerId === state!.you);
    const neutralOases = oases.filter(o => !o.ownerId && (o.burnedRemainingSeconds ?? 0) <= 0);
    let nearestNeutralOasis: { id: string; name: string; position: { x: number; y: number } } | null = null;
    if (homePos && neutralOases.length > 0) {
        const best = neutralOases.slice().sort((a, b) =>
            Math.hypot(a.position.x - homePos.x, a.position.y - homePos.y) -
            Math.hypot(b.position.x - homePos.x, b.position.y - homePos.y)
        )[0]!;
        nearestNeutralOasis = { id: best.id, name: best.name, position: best.position };
    }
    const firstOwned = ownedOases[0];
    const firstArmy = myArmies[0];
    return {
        matchId: state.id,
        running: true,
        panel,
        panelId,
        buildingLevelSum,
        hasBuildingJob,
        hasTrainingJob,
        totalTroops,
        selectedArmiesCount: selectedArmies.length,
        hasOasisUnitSelection: !!oasisUnitSelection,
        hasArmyToOasis,
        ownedOasesCount: ownedOases.length,
        nearestNeutralOasis,
        firstOwnedOasis: firstOwned ? { id: firstOwned.id, name: firstOwned.name, position: firstOwned.position } : null,
        firstArmyId: firstArmy?.id ?? null,
        firstArmyPosition: firstArmy?.position ?? homePos
    };
}
function syncTutorial() {
    if (state?.isSpectator) { gameTutorial.stop(); return; }
    gameTutorial.sync(buildTutorialContext());
}
const gameGuide: GameGuide = new GameGuide(() => { gameTutorial.replay(buildTutorialContext()); });
const gameTutorial: GameTutorial = new GameTutorial(
    beacon => map.setTutorialBeacon(beacon),
    () => { gameGuide.setTribe(currentTribe()); gameGuide.open(); }
);
const gameMenu = new GameMenu(gameAudio, () => state?.status ?? null, async () => {
    if (busy || connecting) throw Error('Wait for your current order to finish.');
    if (!state) return;
    if (connection.state === HubConnectionState.Disconnected) await connect(true);
    if (connection.state !== HubConnectionState.Connected) throw Error('Reconnect to give orders.');
    await connection.invoke('LeaveRoom');
    setReturnedToMenu(false);
    gameTutorial.stop();
    gameAudio.reset(); closePanel(); selection = null; map.select(null); map.home(); render(null);
}, () => {
    gameGuide.setTribe(currentTribe());
    gameGuide.open();
}, () => {
    gameTutorial.replay(buildTutorialContext());
});
const menuButton = document.createElement('button'); menuButton.id = 'open-game-menu'; menuButton.textContent = '☰'; menuButton.setAttribute('aria-label', 'Game menu'); menuButton.title = 'Game menu'; menuButton.onclick = () => gameMenu.open(); el('world-tools').prepend(menuButton);
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
function openReport(id: string) { const r = state?.world?.reports?.find(r => r.id === id); if (r?.parentReportId && state?.world?.reports?.some(p => p.id === r.parentReportId)) { openReport(r.parentReportId); return; } if (r) readReport(r); openPanel("report", id); renderIntelligence(); }
function renderIntelligence() {
    const muster = state?.world?.muster;
    el("muster-hud").hidden = !muster;
    if (muster) el("muster-hud").innerHTML = `${icon("sword")}<span><strong>${muster.total.toLocaleString()}</strong><small>${Math.round(muster.upkeepPerMinute).toLocaleString()} wheat/min</small></span>`;
    const reports = state?.world?.reports ?? [];
    const unreadCount = reports.filter(r => (!r.parentReportId || !reports.some(p => p.id === r.parentReportId)) && unreadReport(r)).length;
    const badge = el("report-count");
    badge.textContent = unreadCount > 0 ? String(unreadCount) : "";
    badge.hidden = unreadCount === 0;
    const city = state?.world?.warfare?.settlements.find(c => c.id === state?.economy?.villageId);
    const biome = state?.world?.regions?.find(r => r.kind === city?.biome); el('village-biome').hidden = !biome;
    if (biome) { el('village-biome').textContent = biome.kind; el('village-biome').dataset.id = String(biome.id); }
}
void map.init().catch(error => { console.error("World renderer initialization failed", error); el("map").innerHTML = '<p class="map-error">The world could not load. Refresh to try again. Building controls remain available.</p>'; });
function notice(text: string, error = false) { el("notice").textContent = text; el("notice").hidden = false; el("notice").classList.toggle("error", error); clearTimeout(toastTimer); toastTimer = window.setTimeout(() => el("notice").hidden = true, 4500); }
function status() { el("connection").textContent = returnedToMenu ? 'Away from match' : connection.state === HubConnectionState.Connected ? "● Connected" : "○ Reconnecting"; el("reconnect").hidden = returnedToMenu || connection.state !== HubConnectionState.Disconnected; renderChat(); }
async function perform(action: () => Promise<void>, exclusive = false) {
    if (exclusive && (roomBusy || pendingActions > 0)) return;
    if (exclusive) roomBusy = true;
    const run = async () => {
        pendingActions++;
        busy = true;
        try {
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
            pendingActions = Math.max(0, pendingActions - 1);
            busy = pendingActions > 0;
            if (exclusive) roomBusy = false;
            renderBuildings(); renderDrawer(false);
        }
    };
    const next = actionQueue.then(run, run);
    actionQueue = next.catch(() => {});
    return next;
}
async function command(kind: string, targetId: string, extra: Record<string, unknown> = {}) {
    const payload = { requestId: crypto.randomUUID(), kind, targetId, quantity: 1, enabled: true, ...extra };
    debugOverlay.recordOutbound("SendCommand", payload);
    try {
        const result = await connection.invoke<CommandResult>("SendCommand", payload);
        debugOverlay.recordRpcOutcome(true);
        debugOverlay.recordInbound("CommandResult", result);
        if (!result.accepted)
            notice(result.message, true);
        else if (kind !== "SendChat")
            notice(kind === "Train" ? "Training started." : kind === "Upgrade" ? "Upgrade completed." : "Orders received.");
        const resumed = await connection.invoke<Snapshot | null>("Resume");
        debugOverlay.recordInbound("Resume", resumed);
        render(resumed);
        return result.accepted;
    } catch (error) {
        debugOverlay.recordRpcOutcome(false);
        throw error;
    }
}
function showMap() { closePanel(); map.home(); }
function renderChat() {
    const root = el("world-chat");
    const inMenu = !state;
    const inLobby = state?.status === "Lobby";
    const visible = (inMenu && menuChatVisible) || inLobby || !!state?.world;
    root.hidden = !visible;
    root.classList.toggle("menu-chat", inMenu);
    root.classList.toggle("lobby-chat", inLobby);
    root.classList.toggle("open", chatOpen);
    el("chat-content").hidden = !chatOpen;
    el("chat-toggle").setAttribute("aria-expanded", String(chatOpen));
    const toggleLabel = el("chat-toggle").firstElementChild as HTMLElement;
    const wantedLabel = inLobby ? "Lobby chat" : "Realm chat";
    if (toggleLabel.dataset.key !== wantedLabel) {
        toggleLabel.dataset.key = wantedLabel;
        toggleLabel.textContent = wantedLabel;
        root.setAttribute("aria-label", wantedLabel);
        localizeNow(root);
    }
    el("chat-toggle").lastElementChild!.textContent = chatOpen ? "−" : "+";
    if (!state) {
        const list = el("chat-messages");
        const menuKey = `menu|${currentLanguage()}`;
        if (chatKey !== menuKey) {
            chatKey = menuKey;
            list.innerHTML = '<p class="chat-empty">No messages yet. Start the conversation.</p>';
            localizeNow(list);
        }
        const canCmd = connection.state === HubConnectionState.Connected;
        el<HTMLInputElement>("chat-input").disabled = !canCmd;
        el("chat-form").querySelector<HTMLButtonElement>("button")!.disabled = !canCmd;
        return;
    }
    const messages = state.chat ?? [], list = el("chat-messages");
    const key = `${state.id}|${state.status}|${currentLanguage()}|${messages.map(m => `${m.id}:${m.text}:${m.senderName ?? ""}`).join("|")}`;
    if (key !== chatKey) {
        const follow = !chatKey || list.scrollHeight - list.scrollTop - list.clientHeight < 50;
        list.innerHTML = messages.length ? messages.map(message => {
            const system = message.kind !== "Player", mine = message.senderId === state!.you;
            const text = system ? t(message.text) : message.text;
            const author = system ? "Realm" : (message.senderName || owner(message.senderId));
            const timestamp = state!.status === "Lobby" ? "" : `<time>${time(message.at)}</time>`;
            return `<article class="chat-message ${system ? "system" : mine ? "own" : ""}"><div class="chat-author"><span>${esc(author)}</span>${timestamp}</div><div class="chat-text" data-no-translate>${esc(text)}</div></article>`;
        }).join("") : '<p class="chat-empty">No messages yet. Start the conversation.</p>';
        chatKey = key;
        if (follow) list.scrollTop = list.scrollHeight;
        localizeNow(list);
    }
    const canSend = (state.status === "Lobby" || state.status === "Running") && connection.state === HubConnectionState.Connected;
    el<HTMLInputElement>("chat-input").disabled = !canSend;
    el("chat-form").querySelector<HTMLButtonElement>("button")!.disabled = !canSend;
}
function renderSpectatorBar() {
    const bar = document.getElementById("spectator-bar");
    if (!bar) return;
    const isSpecRunning = !!state?.isSpectator && !!state?.world;
    bar.hidden = !isSpecRunning;
    if (!isSpecRunning || !state) {
        patchHtml(bar, "");
        return;
    }
    const isAr = currentLanguage() === "ar";
    const settlements = state.world?.warfare?.settlements ?? [];
    const followButtons = state.players.map(p => {
        const city = settlements.find(s => s.ownerId === p.id && !s.ruined) ?? settlements.find(s => s.ownerId === p.id);
        const pos = city?.position ?? p.position;
        if (!pos) return "";
        return `<button type="button" class="spectator-follow-btn" data-action="focus-player-pos" data-id="${pos.x},${pos.y}" style="border-color:${esc(p.flagColor || "#94a3b8")}">${p.isBot ? "🤖 " : ""}${esc(p.name)}</button>`;
    }).join("");
    const speedTag = (state.speedMultiplier ?? 1) > 1 ? ` · x${state.speedMultiplier}` : "";
    patchHtml(bar, `<span class="spectator-pill">👁 ${isAr ? "وضع المشاهدة" : "Spectator Mode"}${speedTag}</span><div class="spectator-follow-list">${followButtons}</div>${button(isAr ? "مغادرة المشاهدة" : "Leave Match", "leave", "", "quiet")}`);
}
function renderWorld() {
    const inGame = !!state?.world;
    const isSpec = !!state?.isSpectator;
    el("world-tools").hidden = !inGame;
    el("command-deck").hidden = !inGame || isSpec;
    el("scene-title").textContent = "Sunmeadow Valley"; el("scene-eyebrow").textContent = "THE WORLD BEYOND YOUR WALLS";
    el("scene-hint").textContent = isSpec
        ? (currentLanguage() === "ar" ? "أنت تشاهد هذه المباراة مع رؤية كاملة للخريطة." : "Spectating with full world visibility.")
        : "Select your village to manage buildings. Capture oases to increase production.";
    renderSpectatorBar();
    renderBuildings();
    renderChat();
}
let buildingOasisId: string | null = null;
let buildingKey = "";
let activeUpgradeTip: { action: string; id: string } | null = null;
let upgradeTipEl: HTMLElement | null = null;

function ensureUpgradeTipEl() {
    if (!upgradeTipEl) {
        upgradeTipEl = document.createElement("div");
        upgradeTipEl.id = "upgrade-cost-tooltip";
        upgradeTipEl.className = "upgrade-cost-tip";
        upgradeTipEl.hidden = true;
        upgradeTipEl.setAttribute("role", "tooltip");
        document.body.append(upgradeTipEl);
    }
    return upgradeTipEl;
}

function hideUpgradeCostTip() {
    activeUpgradeTip = null;
    if (upgradeTipEl) upgradeTipEl.hidden = true;
}

function showUpgradeCostTip(btn: HTMLElement) {
    const rawCost = btn.dataset.upgradeCost;
    const note = btn.dataset.upgradeNote || "";
    const tip = ensureUpgradeTipEl();
    let cost: Record<string, number> | null = null;
    if (rawCost) {
        try { cost = JSON.parse(rawCost); } catch { cost = null; }
    }
    if (!cost && !note) { hideUpgradeCostTip(); return; }

    const res = state?.economy?.resources as Record<string, number> | undefined;
    const checkAffordable = btn.dataset.upgradeCheck === "1" && !!res;
    const rowHtml = cost
        ? `<div class="uct-row">${(["wood", "iron", "stone", "wheat"] as const).map(k => {
            const rawNeed = Number(cost![k] ?? 0);
            const need = Math.max(0, Math.ceil(rawNeed));
            const lack = checkAffordable && (res![k] ?? 0) < rawNeed;
            return `<span class="uct-item${lack ? " lack" : ""}">${icon(k)}<b>${need.toLocaleString()}</b></span>`;
        }).join("")}</div>`
        : "";
    const noteBlocked = btn.dataset.upgradeBlocked === "1";
    const noteHtml = note ? `<small class="uct-note${!cost ? " max" : noteBlocked ? " lack" : ""}">${esc(note)}</small>` : "";
    tip.innerHTML = `${rowHtml}${noteHtml}`;
    tip.dir = currentLanguage() === "ar" ? "rtl" : "ltr";
    tip.hidden = false;

    const r = btn.getBoundingClientRect();
    const tr = tip.getBoundingClientRect();
    const left = Math.max(8, Math.min(innerWidth - tr.width - 8, r.left + r.width / 2 - tr.width / 2));
    const top = Math.max(8, r.top - tr.height - 8);
    tip.style.left = `${left}px`;
    tip.style.top = `${top}px`;
}

function refreshUpgradeCostTip() {
    if (!activeUpgradeTip || !state?.world) {
        if (upgradeTipEl && !upgradeTipEl.hidden) hideUpgradeCostTip();
        return;
    }
    const strip = el("building-strip");
    const btn = Array.from(strip.querySelectorAll<HTMLElement>(".building-upgrade"))
        .find(b => b.dataset.action === activeUpgradeTip!.action && b.dataset.id === activeUpgradeTip!.id);
    if (!btn) { hideUpgradeCostTip(); return; }
    showUpgradeCostTip(btn);
}

(() => {
    const findHoveredUpgradeBtn = (target: EventTarget | null, x: number, y: number): HTMLElement | null => {
        const card = target instanceof Element ? target.closest(".building-card") : null;
        if (!card) return null;
        const btn = card.querySelector<HTMLElement>(".building-upgrade");
        if (!btn) return null;
        const r = btn.getBoundingClientRect();
        return x >= r.left - 3 && x <= r.right + 3 && y >= r.top - 3 && y <= r.bottom + 3 ? btn : null;
    };
    document.addEventListener("pointermove", e => {
        const btn = findHoveredUpgradeBtn(e.target, e.clientX, e.clientY);
        if (btn) {
            activeUpgradeTip = { action: btn.dataset.action ?? "", id: btn.dataset.id ?? "" };
            showUpgradeCostTip(btn);
        } else if (activeUpgradeTip) {
            hideUpgradeCostTip();
        }
    });
    document.addEventListener("focusin", e => {
        const btn = e.target instanceof Element ? e.target.closest<HTMLElement>("#building-strip .building-upgrade") : null;
        if (btn) {
            activeUpgradeTip = { action: btn.dataset.action ?? "", id: btn.dataset.id ?? "" };
            showUpgradeCostTip(btn);
        }
    });
    document.addEventListener("focusout", e => {
        if (e.target instanceof Element && e.target.closest("#building-strip .building-upgrade")) hideUpgradeCostTip();
    });
    el("building-strip").addEventListener("pointerleave", hideUpgradeCostTip);
    el("building-strip").addEventListener("scroll", hideUpgradeCostTip, { passive: true });
    window.addEventListener("resize", hideUpgradeCostTip);
    window.addEventListener("blur", hideUpgradeCostTip);
    window.addEventListener("languagechange", hideUpgradeCostTip);
})();

function renderBuildings() {
    const e = state?.economy, strip = el("building-strip");
    const oasis = state?.world?.oases.find(o => o.id === buildingOasisId);
    const mainHallLevel = e?.targets.find(t => t.kind === "Building" && t.name === "MainHall")?.level ?? 1;
    const trainingLanes = new Set((e?.jobs ?? []).map(j => j.lane).filter(lane => ["Barracks", "Stable", "SiegeWorkshop"].includes(lane)));
    const key = JSON.stringify([oasis && [oasis.id, oasis.wallLevel, oasis.wallHealth, oasis.underAttack, oasis.ownerId, oasis.level, oasis.burnedRemainingSeconds, oasis.wallUpgradeCost && affordable(oasis.wallUpgradeCost), oasis.levelUpgradeCost && affordable(oasis.levelUpgradeCost)], e?.villageId, mainHallLevel, [...trainingLanes].sort(), e?.targets.filter(t => t.kind === 'Building').map(t => [t.id, t.level, t.upgradeCost && affordable(t.upgradeCost)]), running(), panelId, currentTribe(), currentLanguage()]);
    if (key === buildingKey) return; buildingKey = key;
    const isAr = currentLanguage() === 'ar';
    const maxReached = isAr ? 'تم الوصول للحد الأقصى للمستوى' : 'Maximum level reached';
    if (oasis) {
        const owned = oasis.ownerId === state?.you, canPay = e?.villageId !== oasis.villageId || affordable(oasis.wallUpgradeCost);
        const canPayLevel = e?.villageId !== oasis.villageId || affordable(oasis.levelUpgradeCost);
        const isBurned = (oasis.burnedRemainingSeconds ?? 0) > 0;
        const oasisBlockNote = !owned ? (isAr ? 'هذه الواحة تابعة لقائد آخر' : 'Owned by another commander') : oasis.underAttack ? (isAr ? 'الواحة تحت الهجوم' : 'Under attack') : isBurned ? (isAr ? 'الواحة محروقة' : 'Oasis is burned') : '';
        const wallNote = !oasis.wallUpgradeCost ? maxReached : oasisBlockNote;
        const levelNote = !oasis.levelUpgradeCost ? maxReached : oasisBlockNote;
        const checkVillage = e?.villageId === oasis.villageId ? '1' : '';
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
        const wallBlocked = !!oasis.wallUpgradeCost && !!oasisBlockNote ? "1" : "";
        const levelBlocked = !!oasis.levelUpgradeCost && !!oasisBlockNote ? "1" : "";
        patchHtml(strip, `<article class="building-card oasis-wall-card"><button class="building-open" data-action="oasis-wall-info" data-id="${oasis.id}">${buildingArt('Wall', currentTribe())}<span class="building-name">${wallCardName}</span><strong class="building-level"><small>${levelSmall}</small> ${oasis.wallLevel}</strong></button><button class="building-upgrade" data-action="oasis-wall" data-id="${oasis.id}" aria-label="${isAr ? 'ترقية سور الواحة' : 'Upgrade oasis wall'}" data-upgrade-cost='${oasis.wallUpgradeCost ? JSON.stringify(oasis.wallUpgradeCost) : ""}' data-upgrade-note="${esc(wallNote)}" data-upgrade-blocked="${wallBlocked}" data-upgrade-check="${checkVillage}" ${!running() || !owned || oasis.underAttack || !oasis.wallUpgradeCost || !canPay || isBurned ? 'disabled' : ''}>+</button></article>
        <article class="building-card oasis-level-card"><button class="building-open" data-action="oasis-wall-info" data-id="${oasis.id}"><span style="font-size:26px;display:flex;align-items:center;justify-content:center;height:48px;">🌴</span><span class="building-name">${levelCardName}</span><strong class="building-level"><small>${levelSmall}</small> ${oasis.level || 0}</strong></button><button class="building-upgrade" data-action="oasis-level" data-id="${oasis.id}" aria-label="${isAr ? 'ترقية مستوى الواحة' : 'Upgrade oasis level'}" data-upgrade-cost='${oasis.levelUpgradeCost ? JSON.stringify(oasis.levelUpgradeCost) : ""}' data-upgrade-note="${esc(levelNote)}" data-upgrade-blocked="${levelBlocked}" data-upgrade-check="${checkVillage}" ${!running() || !owned || oasis.underAttack || !oasis.levelUpgradeCost || !canPayLevel || isBurned ? 'disabled' : ''}>+</button></article>
        <div class="oasis-ribbon-info"><strong>${ribbonStats}</strong><small>${ribbonStatus}</small></div>`);
        refreshUpgradeCostTip();
        return;
    }
    const html = e ? e.targets.filter(t => t.kind === "Building").map(target => {
        const capped = target.name !== "MainHall" && target.level >= mainHallLevel;
        const training = trainingLanes.has(target.name);
        const note = !target.upgradeCost ? maxReached : capped ? (isAr ? `يتطلب المبنى الرئيسي مستوى ${target.level + 1}` : `Requires Main Hall level ${target.level + 1}`) : '';
        return `<article class="building-card ${panelId === target.id ? 'selected' : ''} ${training ? 'training' : ''}" data-building-name="${esc(target.name)}"><button class="building-open" data-action="building" data-id="${target.id}" aria-label="${esc(pretty(target.name))}">${buildingArt(target.name, currentTribe())}<span class="building-name">${pretty(target.name)}</span><strong class="building-level"><small>Level</small> ${target.level}</strong></button><button class="building-upgrade" data-action="upgrade" data-id="${target.id}" aria-label="Upgrade ${esc(pretty(target.name))}" data-upgrade-cost='${target.upgradeCost ? JSON.stringify(target.upgradeCost) : ""}' data-upgrade-note="${esc(note)}" data-upgrade-blocked="${capped && target.upgradeCost ? "1" : ""}" data-upgrade-check="1" ${!running() || !target.upgradeCost || capped || !affordable(target.upgradeCost) ? 'disabled' : ''}>+</button></article>`;
    }).join('') : '';
    patchHtml(strip, html);
    refreshUpgradeCostTip();
}
function clearOasisSelectionIfInactive(nextPanel: Panel | null, nextId = "") {
    if (!oasisUnitSelection) return;
    if (nextPanel === "oasis" && oasisUnitSelection.oasisId === nextId) return;
    const prevArmyId = oasisUnitSelection.armyId;
    oasisUnitSelection = null;
    if (selectedArmies.length === 1 && selectedArmies[0] === prevArmyId) {
        selectedArmies = [];
        delete armyDrafts[prevArmyId];
        for (const key of [...editedDrafts]) if (key.startsWith(`${prevArmyId}|`)) editedDrafts.delete(key);
        syncMapSelections();
        refreshCursorBadge();
    }
}
function openPanel(kind: Panel, id = "", tab = "overview") { clearOasisSelectionIfInactive(kind, id); buildingOasisId = kind === "oasis" ? id : null; reportDetail = ""; panel = kind; panelId = id; panelTab = tab; page = 0; el("drawer").hidden = false; el("game").classList.add("has-drawer"); renderDrawer(true); renderBuildings(); renderArmiesStrip(); syncTutorial(); }
function closePanel() { clearOasisSelectionIfInactive(null); buildingOasisId = null; panel = null; el("drawer").hidden = true; el("game").classList.remove("has-drawer"); renderBuildings(); renderArmiesStrip(); syncTutorial(); }
function openBuilding(id: string) {
    const t = state?.economy?.targets.find(t => t.id === id); if (!t)
        return; selectedUnit = ""; openPanel("building", id, t.name === "Market" ? "trade" : ["Barracks", "Stable", "SiegeWorkshop"].includes(t.name) ? "training" : "overview");
}
function loadArmy() {
    const a = army(); if (!a)
        return; afterAttack = a.afterAttack ?? "Hold"; supplySource = a.supplyVillageId; supplyWagons = a.supplyWagons; escorts = a.escorts; autoSupply = a.autoSupply;
}
function costs(cost: Resources | null, multiplier = 1) {
    if (!cost)
        return '<small>Maximum level reached</small>'; return `<div class="costs">${Object.entries(cost).map(([name, value]) => `<span class="${value * multiplier > (state?.economy?.resources[name as keyof Resources] ?? Infinity) ? "unaffordable" : ""}" title="${name}">${icon(name)}${Math.ceil(value * multiplier).toLocaleString()}</span>`).join("")}</div>`;
}
function stopVictoryFireworks() {
    if (victoryRaf) { cancelAnimationFrame(victoryRaf); victoryRaf = 0; }
}
function startVictoryFireworks(canvas: HTMLCanvasElement) {
    stopVictoryFireworks();
    const ctx = canvas.getContext("2d");
    if (!ctx || matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    interface Spark { x: number; y: number; vx: number; vy: number; alpha: number; decay: number; color: string; size: number }
    const palette = ["#ffd86b", "#ff6b6b", "#5ef2a8", "#6ecbff", "#e78cff", "#fff0a8"];
    const sparks: Spark[] = [];
    let nextBurst = 0;
    const resize = () => {
        canvas.width = window.innerWidth;
        canvas.height = window.innerHeight;
    };
    resize();
    const spawnBurst = () => {
        const cx = canvas.width * (0.18 + Math.random() * 0.64);
        const cy = canvas.height * (0.16 + Math.random() * 0.42);
        const baseColor = palette[Math.floor(Math.random() * palette.length)]!;
        const count = 42;
        for (let i = 0; i < count; i++) {
            const angle = (Math.PI * 2 * i) / count + (Math.random() - 0.5) * 0.14;
            const speed = 1.6 + Math.random() * 4.4;
            sparks.push({
                x: cx, y: cy,
                vx: Math.cos(angle) * speed,
                vy: Math.sin(angle) * speed - 0.6,
                alpha: 1,
                decay: 0.011 + Math.random() * 0.012,
                color: Math.random() < 0.28 ? "#fff7d6" : baseColor,
                size: 2 + Math.random() * 2.2
            });
        }
    };
    const tick = (t: number) => {
        if (!canvas.isConnected) { stopVictoryFireworks(); return; }
        if (canvas.width !== window.innerWidth || canvas.height !== window.innerHeight) resize();
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        if (t >= nextBurst) {
            spawnBurst();
            if (Math.random() < 0.45) spawnBurst();
            nextBurst = t + 520 + Math.random() * 420;
        }
        for (let i = sparks.length - 1; i >= 0; i--) {
            const s = sparks[i]!;
            s.x += s.vx; s.y += s.vy;
            s.vx *= 0.985; s.vy = s.vy * 0.985 + 0.045;
            s.alpha -= s.decay;
            if (s.alpha <= 0) { sparks.splice(i, 1); continue; }
            ctx.globalAlpha = Math.max(0, s.alpha);
            ctx.fillStyle = s.color;
            ctx.beginPath();
            ctx.arc(s.x, s.y, s.size, 0, Math.PI * 2);
            ctx.fill();
        }
        ctx.globalAlpha = 1;
        victoryRaf = requestAnimationFrame(tick);
    };
    victoryRaf = requestAnimationFrame(tick);
}
function renderVictoryOverlay(snapshot: Snapshot | null) {
    const existing = document.getElementById("victory-celebration");
    if (!snapshot?.result || dismissedVictoryMatchId === snapshot.id) {
        if (existing) existing.remove();
        stopVictoryFireworks();
        victoryKey = "";
        return;
    }
    const isAr = currentLanguage() === "ar";
    const names = snapshot.result.winnerIds.map(owner).join(isAr ? " و " : " & ") || (isAr ? "لا يوجد" : "None");
    const youWon = snapshot.result.winnerIds.includes(snapshot.you);
    const reasonText = snapshot.result.reason === "CrownKeep"
        ? (isAr ? "السيطرة المتواصلة على قلعة التاج" : "Crown Keep held for 5 continuous minutes")
        : snapshot.result.reason === "Domination"
            ? (isAr ? "الهيمنة الكاملة على الساحة" : "Total domination")
            : (isAr ? "نهاية وقت المباراة بأعلى نقاط" : "Time limit — highest score");
    const nextKey = `${snapshot.id}|${currentLanguage()}|${names}|${snapshot.result.reason}`;
    if (existing && victoryKey === nextKey) return;
    victoryKey = nextKey;
    const host = existing ?? document.createElement("div");
    host.id = "victory-celebration";
    host.className = "victory-celebration";
    host.innerHTML = `<canvas class="victory-fireworks" aria-hidden="true"></canvas>
        <div class="victory-card glass" role="dialog" aria-label="${isAr ? "نتيجة المباراة" : "Match result"}">
            <div class="victory-crown" aria-hidden="true">👑</div>
            <span class="victory-kicker">${youWon ? (isAr ? "مبارك لك الانتصار!" : "VICTORY ACHIEVED!") : (isAr ? "نهاية المعركة" : "MATCH COMPLETE")}</span>
            <h1 class="victory-winner-title">${isAr ? (snapshot.result.winnerIds.length > 1 ? `الفائزون: ${esc(names)}` : `الفائز: ${esc(names)}`) : (snapshot.result.winnerIds.length > 1 ? `Winners: ${esc(names)}` : `Winner: ${esc(names)}`)}</h1>
            <p class="victory-reason">${esc(reasonText)}</p>
            <div class="victory-actions">
                <button type="button" class="primary" data-action="victory-standings">${isAr ? "عرض جدول الترتيب" : "View standings"}</button>
                <button type="button" class="quiet" data-action="dismiss-victory">${isAr ? "إغلاق" : "Close"}</button>
                <button type="button" class="quiet" data-action="leave">${isAr ? "مغادرة الروم" : "Leave realm"}</button>
            </div>
        </div>`;
    if (!existing) el("game").appendChild(host);
    const canvas = host.querySelector<HTMLCanvasElement>(".victory-fireworks");
    if (canvas) startVictoryFireworks(canvas);
}

function affordable(cost: Resources | null, multiplier = 1) { return !!cost && Object.entries(cost).every(([name, n]) => n * multiplier <= (state?.economy?.resources[name as keyof Resources] ?? 0)); }
function render(snapshot: Snapshot | null) {
    if (snapshot && snapshot.id === state?.id && snapshot.revision < state.revision) {
        debugOverlay.recordStaleDrop();
        return;
    } const previous = state; state = snapshot; gameAudio.update(snapshot); clockReceived = performance.now(); el("welcome").hidden = !!snapshot || panel === "rooms"; if (!snapshot) {
            pendingSnapshot = null;
            selectedArmies = []; oasisUnitSelection = null; focusedMatchId = ""; dismissedVictoryMatchId = ""; renderVictoryOverlay(null);
            closeRadial(); renderCursorBadge(el("map-cursor-badge"), "none", null); map.setSelectedArmies([], []);
            patchHtml(el('resources'), ''); el('muster-hud').hidden = true; el('village-biome').hidden = true;
            const armyStrip = document.getElementById('army-strip'); if (armyStrip) patchHtml(armyStrip, '');
            alertBanner = ""; el("important-alerts").innerHTML = ""; el("important-alerts").hidden = true; el("game").classList.remove("has-live-events");
            el("work-queue").innerHTML = "";
            el("match-clock").textContent = "A new adventure";
            map.update(null, [], "preview");
            progressFeedback.update(previous, null);
            gameTutorial.stop();
            renderWorld();
            syncAdminOverlay();
            status();
            return;
        } if (!previous?.world && snapshot.world) {
            closePanel();
            notice(snapshot.isSpectator
                ? (currentLanguage() === "ar" ? "دخلت المباراة بوضع المشاهدة 👁" : "Joined match in Spectator mode 👁")
                : "Select your village to manage buildings. Capture oases to increase production.");
        } if (snapshot.status === "Lobby") {
            focusedMatchId = ""; dismissedVictoryMatchId = "";
            if (panel !== "lobby") openPanel("lobby");
        }
        if (snapshot.status === "Running" && snapshot.world && focusedMatchId !== snapshot.id) {
            const ownPos = snapshot.world.warfare?.settlements.find(s => s.id === snapshot.economy?.villageId)?.position
                ?? snapshot.world.warfare?.settlements.find(s => s.ownerId === snapshot.you && !s.ruined)?.position
                ?? snapshot.players.find(p => p.id === snapshot.you)?.position
                ?? snapshot.world.warfare?.settlements[0]?.position;
            if (ownPos) {
                focusedMatchId = snapshot.id;
                map.focus(ownPos, snapshot.isSpectator ? 1.8 : 2.8);
            }
        }
        if (oasisUnitSelection) {
            const oId = oasisUnitSelection.oasisId;
            const currentOwnGuard = snapshot.world?.armies.find(a => a.ownerId === snapshot.you && a.defendingSiteId === oId && a.order === "Guarding");
            const validUnits = currentOwnGuard
                ? oasisUnitSelection.units.filter(u => (currentOwnGuard.formation?.[u] ?? 0) > 0)
                : [];
            if (!currentOwnGuard || validUnits.length === 0) {
                const staleId = oasisUnitSelection.armyId;
                oasisUnitSelection = null;
                selectedArmies = selectedArmies.filter(id => id !== staleId);
                delete armyDrafts[staleId];
                for (const key of [...editedDrafts]) if (key.startsWith(`${staleId}|`)) editedDrafts.delete(key);
            } else {
                const prevId = oasisUnitSelection.armyId;
                if (prevId !== currentOwnGuard.id) {
                    delete armyDrafts[prevId];
                    for (const key of [...editedDrafts]) if (key.startsWith(`${prevId}|`)) editedDrafts.delete(key);
                    oasisUnitSelection.armyId = currentOwnGuard.id;
                    selectedArmies = [currentOwnGuard.id];
                    activeArmy = currentOwnGuard.id;
                }
                oasisUnitSelection.units = validUnits;
                const chosenSet = new Set(validUnits);
                const draft: Record<string, number> = {};
                for (const [u, n] of Object.entries(currentOwnGuard.formation ?? {})) {
                    if (n > 0) {
                        draft[u] = chosenSet.has(u) ? n : 0;
                        editedDrafts.add(`${currentOwnGuard.id}|${u}`);
                    }
                }
                armyDrafts[currentOwnGuard.id] = draft;
            }
        }
        const beforeCount = selectedArmies.length;
        selectedArmies = selectedArmies.filter(id => !!armyTroopMap(id));
        if (beforeCount !== selectedArmies.length) {
            syncMapSelections();
            if (panel === "army") {
                if (selectedArmies.length === 1) panelId = selectedArmies[0]!;
                else if (selectedArmies.length > 1) panelId = "multi";
                else closePanel();
            }
        }
        if (!ownArmies().some(a => a.id === activeArmy)) {
            activeArmy = ownArmies()[0]?.id ?? "";
            loadArmy();
        }
        const economy = snapshot.economy;
        const resourcesHtml = economy ? Object.entries(economy.resources).map(([name, count]) => {
            const p = economy.production?.find(p => p.resource.toLowerCase() === name);
            const net = Math.round(p?.netPerMinute ?? 0);
            const rateLabel = p ? `${net > 0 ? "+" : net < 0 ? "-" : ""}${Math.abs(net).toLocaleString()}` : "0";
            return `<button class="resource" data-action="production" data-id="${name}" title="${p ? `${p.netPerMinute.toFixed(1)} ${name}/minute net / ${p.percent.toFixed(1)}% of normal output` : name}">${icon(name)}<span><strong>${count.toLocaleString()}<i class="production-percent ${net < 0 ? "negative" : "positive"}">${rateLabel}</i></strong><small>${name.toUpperCase()}</small></span></button>`;
        }).join("") : "";
        patchHtml(el("resources"), resourcesHtml);
        const armyCountBadge = document.getElementById("army-count");
        if (armyCountBadge) armyCountBadge.textContent = String(ownArmies().length);
        try {
            map.setAlliances(snapshot.alliances ?? [], snapshot.you);
            syncMapSelections();
            map.update(snapshot.world, snapshot.players, snapshot.you, snapshot.status === "Lobby" ? snapshot.settings.capacity : undefined);
        } catch (err) {
            console.error("Map update error:", err);
        }
        try { renderWorld(); } catch (e) { console.error("renderWorld error:", e); }
        try { renderPossessions(); } catch (e) { console.error("renderPossessions error:", e); }
        try { renderArmiesStrip(); } catch (e) { console.error("renderArmiesStrip error:", e); }
        try { refreshCursorBadge(); } catch (e) { console.error("refreshCursorBadge error:", e); }
        try { renderBuildings(); refreshUpgradeCostTip(); } catch (e) { console.error("renderBuildings error:", e); }
        try { renderImportantAlerts(); } catch (e) { console.error("renderImportantAlerts error:", e); }
        try { renderTimers(); } catch (e) { console.error("renderTimers error:", e); }
        try { renderDrawer(); } catch (e) { console.error("renderDrawer error:", e); }
        try { renderIntelligence(); } catch (e) { console.error("renderIntelligence error:", e); }
        try { status(); } catch (e) { console.error("status error:", e); }
        try { progressFeedback.update(previous, snapshot); } catch (e) { console.error("progressFeedback error:", e); }
        try { renderVictoryOverlay(snapshot); } catch (e) { console.error("renderVictoryOverlay error:", e); }
        try { syncTutorial(); } catch (e) { console.error("syncTutorial error:", e); }
        try { syncAdminOverlay(); } catch (e) { console.error("syncAdminOverlay error:", e); }
        if (snapshot.result && !previous?.result) openPanel("standings");
}
function renderPossessions() {
    const container = el("realm-possessions");
    if (!container) return;
    if (!state?.you || !state?.world || state.status !== "Running") {
        patchHtml(container, "");
        return;
    }
    const myVillages = (state.world.warfare?.settlements ?? []).filter(s => s.ownerId === state!.you && !s.ruined);
    const myOases = (state.world.oases ?? []).filter(o => o.ownerId === state!.you);
    if (myVillages.length === 0 && myOases.length === 0) {
        patchHtml(container, "");
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
    patchHtml(container, `<div class="possession-strip">${villageBadges}${oasisBadges}</div>`);
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
function mergeSelectionInfo() {
    const homeIds = selectedArmies.filter(id => id.startsWith("home:")).map(id => id.slice(5));
    const fieldArmies = selectedArmies.filter(id => !id.startsWith("home:")).map(id => ownArmies().find(a => a.id === id)).filter((x): x is ArmyView => !!x);
    const validStructure = (homeIds.length === 0 && fieldArmies.length >= 2) || (homeIds.length === 1 && fieldArmies.length >= 1);
    const busyArmy = fieldArmies.some(a => ["Battle", "Sieging", "Returning"].includes(a.order));
    const canMerge = validStructure && !busyArmy;
    return { homeIds, fieldArmies, canMerge, eligible: running() && canMerge };
}

function renderArmiesStrip() {
    const strip = document.getElementById("army-strip");
    const mergeBar = document.getElementById("army-merge-bar");
    if (!strip) return;
    const shelf = document.querySelector('.army-shelf') as HTMLElement | null;
    if (!state?.you || !state?.world || state.status !== "Running") {
        if (shelf) shelf.hidden = true;
        if (mergeBar) { mergeBar.hidden = true; mergeBar.innerHTML = ""; }
        strip.innerHTML = "";
        return;
    }

    const myVillages = ownedVillages();
    const myVillageIds = new Set(myVillages.map(v => v.id));
    const myOases = (state.world.oases ?? []).filter(o => o.ownerId === state!.you);
    const myOasisIds = new Set(myOases.map(o => o.id));

    const homeArmies = myVillages.map((v, index) => {
        const id = `home:${v.id}`;
        const info = armyTroopMap(id);
        return info ? { info, index } : null;
    }).filter((x): x is { info: ControlledArmyInfo; index: number } => !!x);

    const foreignReinforcements = (state.world.armies ?? []).filter(a => {
        if (a.ownerId === state!.you) return false;
        if (a.supportedOwnerId === state!.you) return true;
        if (a.defendingSiteId && (myVillageIds.has(a.defendingSiteId) || myOasisIds.has(a.defendingSiteId))) return true;
        return false;
    });

    const myArmies = ownArmies();
    if (homeArmies.length === 0 && myArmies.length === 0 && foreignReinforcements.length === 0) {
        if (shelf) shelf.hidden = true;
        if (mergeBar) { mergeBar.hidden = true; mergeBar.innerHTML = ""; }
        strip.innerHTML = "";
        return;
    }
    if (shelf) {
        shelf.hidden = false;
        shelf.classList.toggle("multi-active", selectedArmies.length > 0);
    }

    const isAr = currentLanguage() === "ar";
    if (mergeBar) {
        if (selectedArmies.length > 1) {
            const merge = mergeSelectionInfo();
            const mergeKey = `${selectedArmies.length}|${merge.eligible}|${isAr}`;
            mergeBar.hidden = false;
            if (mergeBar.dataset.key !== mergeKey) {
                mergeBar.dataset.key = mergeKey;
                mergeBar.innerHTML = `<button type="button" class="army-merge-btn" data-action="merge-armies" ${merge.eligible ? "" : "disabled"}><span>⇄</span><span>${isAr ? `دمج (${selectedArmies.length})` : `Merge (${selectedArmies.length})`}</span></button>`;
            }
        } else {
            mergeBar.hidden = true;
            if (mergeBar.dataset.key) {
                delete mergeBar.dataset.key;
                mergeBar.innerHTML = "";
            }
        }
    }
    const cards: string[] = [];

    const armyLeadHtml = (formationMap: Record<string, number>, wagons = 0, soldiers = 0) => {
        const entries = Object.entries(formationMap).filter(([, n]) => n > 0).sort((x, y) => y[1] - x[1]);
        const topUnitId = entries[0]?.[0];
        if (topUnitId) return `<div class="army-lead-art" title="${esc(unitCatalog.get(topUnitId)?.displayName ?? topUnitId)}">${unitArtwork(topUnitId)}</div>`;
        if (wagons > 0 && soldiers === 0) return `<div class="army-lead-art lead-icon" title="${t('Wagons')}">🛒</div>`;
        return `<div class="army-lead-art lead-icon" title="${t('Troops')}">⚔</div>`;
    };

    // 0. Home Village Garrison Armies (available even before dispatch)
    homeArmies.forEach(({ info }) => {
        const isSelected = selectedArmies.includes(info.id);
        const toggleTitle = isSelected ? (isAr ? "إلغاء التحديد" : "Deselect") : (isAr ? "إضافة للتحديد المتعدد" : "Add to selection");
        const homeBadge = `<span class="army-status-badge status-defender" title="${isAr ? 'جيش متمركز بالقرية' : 'Stationed in village'}"><svg viewBox="0 0 24 24"><path d="M12 3 4 9v11h5v-6h6v6h5V9Z" fill="#4ade80"/><path d="M12 6 7 10v8h2v-5h6v5h2v-8Z" fill="#166534"/></svg></span>`;
        cards.push(`<article class="army-card home-army ${isSelected ? 'selected' : ''}">
            <button type="button" class="army-select-toggle" data-action="toggle-army" data-id="${info.id}" title="${esc(toggleTitle)}" aria-label="${esc(toggleTitle)}">${isSelected ? "✓" : "+"}</button>
            <button type="button" class="army-card-btn" data-action="focus-army" data-id="${info.id}" aria-label="${esc(info.name)}">
                <div class="army-card-header">
                    <span class="army-name-tag">${esc(info.name)}</span>
                    ${homeBadge}
                </div>
                ${armyLeadHtml(info.troops)}
                <div class="army-card-bottom"><strong class="army-total-count">${info.total.toLocaleString()}</strong></div>
            </button>
        </article>`);
    });

    // 1. Own field armies
    myArmies.forEach((a, index) => {
        const isSelected = selectedArmies.includes(a.id);
        const toggleTitle = isSelected ? (isAr ? "إلغاء التحديد" : "Deselect") : (isAr ? "إضافة للتحديد المتعدد" : "Add to selection");
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

        const armyTitle = `${isAr ? 'جيش' : 'Army'} ${index + 1}`;
        const totalUnits = a.soldiers + a.wagons;

        cards.push(`<article class="army-card ${isSelected ? 'selected' : ''}">
            <button type="button" class="army-select-toggle" data-action="toggle-army" data-id="${a.id}" title="${esc(toggleTitle)}" aria-label="${esc(toggleTitle)}">${isSelected ? "✓" : "+"}</button>
            <button type="button" class="army-card-btn" data-action="focus-army" data-id="${a.id}" aria-label="${esc(armyTitle)}">
                <div class="army-card-header">
                    <span class="army-name-tag">${esc(armyTitle)}</span>
                    ${statusBadge}
                </div>
                ${armyLeadHtml(a.formation ?? {}, a.wagons, a.soldiers)}
                <div class="army-card-bottom"><strong class="army-total-count">${totalUnits.toLocaleString()}</strong></div>
            </button>
        </article>`);
    });

    // 2. Foreign reinforcements stationed in our villages or oases
    foreignReinforcements.forEach((a) => {
        const isSelected = selection?.id === a.id && selectedArmies.length === 0;
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

        const ownerName = owner(a.ownerId);
        const villageName = isAr ? `قرية ${ownerName}` : `${ownerName}'s village`;
        const armyTitle = isAr ? 'تعزيز' : 'Support';
        const totalUnits = a.soldiers + a.wagons;

        cards.push(`<article class="army-card reinforcement ${isSelected ? 'selected' : ''}">
            <button type="button" class="army-card-btn" data-action="focus-army" data-id="${a.id}" aria-label="${esc(armyTitle)} · ${esc(villageName)}">
                <div class="army-card-header">
                    <span class="army-name-tag">${esc(armyTitle)}</span>
                    ${statusBadge}
                </div>
                ${armyLeadHtml(a.formation ?? {}, a.wagons, a.soldiers)}
                <div class="army-card-bottom"><strong class="army-total-count">${totalUnits.toLocaleString()}</strong><small class="army-foreign-origin" title="${esc(villageName)}">${esc(villageName)}</small></div>
            </button>
        </article>`);
    });

    patchHtml(strip, cards.join(""));
}

function refreshInspectorFooter(info: ControlledArmyInfo) {
    syncArmyDraft(info);
    const chosen = Object.fromEntries(Object.entries(armyDrafts[info.id] ?? {}).filter(([, n]) => n > 0));
    const chosenSum = Object.values(chosen).reduce((s, n) => s + n, 0);
    const blockedField = !info.isHome && ["Battle", "Sieging", "Returning"].includes(info.order);
    const createBtn = el("drawer").querySelector<HTMLButtonElement>('[data-action="inspector-create-army"]');
    if (createBtn) {
        createBtn.disabled = !running() || chosenSum <= 0 || (!info.isHome && (chosenSum >= info.total || blockedField));
    }
    const disbandBtn = el("drawer").querySelector<HTMLButtonElement>('[data-action="inspector-disband"]');
    if (disbandBtn) {
        const isAr = currentLanguage() === "ar";
        const key = currentDisbandKey(info.id, chosen);
        const confirming = chosenSum > 0 && disbandConfirmKey === key;
        const blockedHome = info.isHome && isVillageUnderSiege(info.villageId);
        disbandBtn.disabled = !running() || chosenSum <= 0 || blockedField || blockedHome;
        disbandBtn.classList.toggle("disband-confirm", confirming);
        disbandBtn.textContent = disbandButtonLabel(chosen, isAr, confirming);
    }
}

function renderUnitSplitRows(info: ControlledArmyInfo, isAr: boolean): string {
    syncArmyDraft(info);
    const draft = armyDrafts[info.id] ?? {};
    const units = Object.entries(info.troops).filter(([, n]) => n > 0).sort(([a], [b]) => compareUnitIds(a, b));
    if (!units.length) return `<p class="empty">${isAr ? "لا توجد وحدات متاحة." : "No troops available."}</p>`;
    const rows = units.map(([unitId, max]) => {
        const val = draft[unitId] ?? max;
        const unitNameText = unitCatalog.get(unitId)?.displayName ?? pretty(role(unitId));
        return `<div class="army-inspector-unit">
            ${unitPortrait(unitId, false)}
            <div class="army-inspector-unit-info">
                <strong>${esc(unitNameText)}</strong>
                <small>${max.toLocaleString()} ${isAr ? "متاح" : "available"}</small>
            </div>
            <div class="army-inspector-qty">
                <input type="number" inputmode="numeric" data-inspector-unit="${unitId}" data-army-id="${info.id}" min="0" max="${max}" value="${val}"/>
                <button type="button" class="mini-pill" data-action="inspector-quick" data-army-id="${info.id}" data-id="${unitId}" data-max="${max}">½</button>
            </div>
        </div>`;
    }).join("");
    return `<section class="army-units-editor"><div class="army-units-editor-head"><strong>${isAr ? "وحدات الجيش والتقسيم" : "Units & Army Split"}</strong><span class="army-head-actions"><button type="button" class="mini-pill" data-action="inspector-clear" data-id="${info.id}">${isAr ? "تصفير" : "Clear"}</button><button type="button" class="mini-pill" data-action="inspector-all" data-id="${info.id}">${isAr ? "الكل" : "All"}</button><small>${info.total.toLocaleString()} ${isAr ? "وحدة" : "units"}</small></span></div>${rows}</section>`;
}

function renderCasualtiesGrid(wounded: number, dead: number, fled: number, isAr: boolean): string {
    return `<section class="army-units-editor"><div class="army-units-editor-head"><strong>${isAr ? "الإصابات والخسائر" : "Casualties & Losses"}</strong></div>
        <div class="army-casualties-grid">
            <div><b class="${wounded > 0 ? "warn" : ""}">${wounded.toLocaleString()}</b><small>${isAr ? "جرحى" : "Wounded"}</small></div>
            <div><b class="${dead > 0 ? "danger" : ""}">${dead.toLocaleString()}</b><small>${isAr ? "قتلى" : "Dead"}</small></div>
            <div><b>${fled.toLocaleString()}</b><small>${isAr ? "فارّون" : "Fled"}</small></div>
        </div>
    </section>`;
}

function renderLootRow(res: Resources, heading: string): string {
    return `<section class="army-units-editor"><div class="army-units-editor-head"><strong>${esc(heading)}</strong></div>
        <div class="army-loot-row">
            <div>${icon("wood")}<b>${res.wood.toLocaleString()}</b></div>
            <div>${icon("iron")}<b>${res.iron.toLocaleString()}</b></div>
            <div>${icon("stone")}<b>${res.stone.toLocaleString()}</b></div>
            <div>${icon("wheat")}<b>${res.wheat.toLocaleString()}</b></div>
        </div>
    </section>`;
}

async function splitOrCreateArmy(id: string) {
    const info = armyTroopMap(id);
    if (!info) return;
    const chosen = chosenDraft(id);
    const chosenSum = Object.values(chosen).reduce((s, n) => s + n, 0);
    const isAr = currentLanguage() === "ar";
    if (chosenSum <= 0) {
        notice(isAr ? "اختر وحدة واحدة على الأقل." : "Choose at least one unit.", true);
        return;
    }
    if (info.isHome) {
        const before = new Set(ownArmies().map(a => a.id));
        const ok = await command("DispatchArmy", info.villageId, {
            formation: chosen, wagons: 0, enabled: dispatchAuto, escorts: 0,
            x: info.position.x, y: info.position.y, afterAttack: "Hold"
        });
        if (!ok) return;
        const created = ownArmies().find(a => !before.has(a.id));
        if (created) selectArmies([created.id], "replace", false);
        return;
    }
    if (chosenSum >= info.total) {
        notice(isAr ? "اختر عدداً أقل من إجمالي وحدات الجيش لفصل جيش جديد." : "Select fewer than all units to split off a new army.", true);
        return;
    }
    const ok = await command("MoveArmy", info.id, {
        x: info.position.x, y: info.position.y, formation: chosen
    });
    if (ok) selectArmies([info.id], "replace", false);
}

async function disbandSelectedTroops(id: string) {
    const info = armyTroopMap(id);
    if (!info) return;
    const chosen = chosenDraft(id);
    const chosenSum = Object.values(chosen).reduce((s, n) => s + n, 0);
    const isAr = currentLanguage() === "ar";
    if (chosenSum <= 0) {
        resetDisbandConfirm();
        notice(isAr ? "اختر وحدة واحدة على الأقل للتسريح." : "Choose at least one unit to disband.", true);
        return;
    }
    const key = currentDisbandKey(info.id, chosen);
    if (disbandConfirmKey !== key) {
        resetDisbandConfirm();
        disbandConfirmKey = key;
        refreshInspectorFooter(info);
        disbandConfirmTimer = window.setTimeout(() => {
            disbandConfirmKey = "";
            disbandConfirmTimer = undefined;
            const live = armyTroopMap(info.id);
            if (live) refreshInspectorFooter(live);
        }, 4500);
        return;
    }
    resetDisbandConfirm();
    const targetId = info.isHome ? info.villageId : info.id;
    const saved = disbandUpkeepSaved(chosen);
    const ok = await command("DisbandTroops", targetId, { formation: chosen });
    if (!ok) return;
    delete armyDrafts[info.id];
    for (const k of [...editedDrafts]) if (k.startsWith(`${info.id}|`)) editedDrafts.delete(k);
    if (!info.isHome && chosenSum >= info.total) {
        selectedArmies = selectedArmies.filter(x => x !== info.id);
        syncMapSelections();
        if (selectedArmies.length === 0 && panel === "army") closePanel();
    }
    notice(isAr
        ? `تم تسريح ${chosenSum.toLocaleString()} وحدة${saved > 0 ? ` وتخفيض الاستهلاك بمقدار ${saved.toLocaleString()} قمح/د` : ""}.`
        : `Disbanded ${chosenSum.toLocaleString()} units${saved > 0 ? ` (saved ${saved.toLocaleString()} wheat/min)` : ""}.`);
}

async function executeRtsOrder(actionId: RadialActionId, target: MapSelection) {
    if (!state?.world || !selectedArmies.length) return;
    closeRadial();
    const isAr = currentLanguage() === "ar";
    const activeOasisSelection = oasisUnitSelection;
    const orderIds = [...selectedArmies];
    let anySent = false;
    let missingScoutsOnly = actionId === "scout";

    for (const selId of orderIds) {
        const info = armyTroopMap(selId);
        if (!info) continue;
        if (actionId === "recall") {
            if (!info.isHome && await command("RecallArmy", info.id)) anySent = true;
            continue;
        }
        let form = effectiveOrderFormation(selId);
        if (actionId === "scout") {
            form = Object.fromEntries(Object.entries(form).filter(([u, n]) => n > 0 && role(u) === "scout"));
            if (!Object.keys(form).length) continue;
            missingScoutsOnly = false;
        }
        if (!Object.keys(form).length) continue;

        if (info.isHome) {
            if (actionId === "capture-oasis" || actionId === "besiege-oasis" || actionId === "burn-oasis") {
                const mode = actionId === "burn-oasis" ? "Burn" : actionId === "besiege-oasis" ? "Blockade" : "Capture";
                if (await command("DispatchArmy", info.villageId, {
                    formation: form, wagons: 0, enabled: dispatchAuto, escorts: 0,
                    x: target.position.x, y: target.position.y, afterAttack: "Hold",
                    oasisMode: mode, destinationId: target.id
                })) anySent = true;
            } else if (actionId === "raid" || actionId === "conquest" || actionId === "raze" || actionId === "blockade-city") {
                const objective = actionId === "conquest" ? "Conquest" : actionId === "raze" ? "Raze" : actionId === "blockade-city" ? "Blockade" : "Raid";
                if (await command("DispatchArmy", info.villageId, {
                    formation: form, wagons: 0, enabled: dispatchAuto, escorts: 0,
                    x: target.position.x, y: target.position.y, afterAttack: "Hold",
                    objective, destinationId: target.id
                })) anySent = true;
            } else if (actionId === "reinforce") {
                if (await command("DispatchArmy", info.villageId, {
                    formation: form, wagons: 0, enabled: dispatchAuto, escorts: 0,
                    x: target.position.x, y: target.position.y, afterAttack: "Hold", reinforcement: true, destinationId: target.id
                })) anySent = true;
            } else if (actionId === "camp") {
                if (await command("DispatchArmy", info.villageId, {
                    formation: form, wagons: 0, enabled: dispatchAuto, escorts: 0,
                    x: target.position.x, y: target.position.y, camp: true, loadedOnly: campLoadedOnly, afterAttack: "Hold"
                })) anySent = true;
            } else if (actionId === "move") {
                if (await command("DispatchArmy", info.villageId, {
                    formation: form, wagons: 0, enabled: dispatchAuto, escorts: 0,
                    x: target.position.x, y: target.position.y, afterAttack: "Hold"
                })) anySent = true;
            } else {
                const before = new Set(ownArmies().map(a => a.id));
                const spawned = await command("DispatchArmy", info.villageId, {
                    formation: form, wagons: 0, enabled: dispatchAuto, escorts: 0,
                    x: target.position.x, y: target.position.y, afterAttack: actionId === "scout" ? "Return" : "Hold"
                });
                if (!spawned) continue;
                const spawnedId = ownArmies().find(a => !before.has(a.id))?.id;
                if (spawnedId) {
                    const step2 = actionId === "scout" ? "Scout"
                        : actionId === "attack-army" ? "AttackArmy"
                        : actionId === "intercept-convoy" ? "InterceptConvoy"
                        : "CaptureCastle";
                    const extra = actionId === "capture-castle" ? {} : { destinationId: target.id, ...(actionId === "scout" ? {} : { afterAttack: "Hold" }) };
                    if (await command(step2, spawnedId, extra)) anySent = true;
                }
            }
            continue;
        }

        const armyAfter = ownArmies().find(a => a.id === info.id)?.afterAttack ?? "Hold";
        // Field army commands
        if (actionId === "capture-oasis") {
            if (await command("CaptureOasis", info.id, { destinationId: target.id, formation: form, afterAttack: "Hold" })) anySent = true;
        } else if (actionId === "besiege-oasis") {
            if (await command("BesiegeOasis", info.id, { destinationId: target.id, formation: form, afterAttack: "Hold" })) anySent = true;
        } else if (actionId === "burn-oasis") {
            if (await command("BurnOasis", info.id, { destinationId: target.id, formation: form, afterAttack: "Hold" })) anySent = true;
        } else if (actionId === "reinforce") {
            if (await command("Reinforce", info.id, { destinationId: target.id, formation: form })) anySent = true;
        } else if (actionId === "scout") {
            if (await command("Scout", info.id, { destinationId: target.id, formation: form })) anySent = true;
        } else if (actionId === "raid" || actionId === "conquest" || actionId === "raze" || actionId === "blockade-city") {
            const objective = actionId === "conquest" ? "Conquest" : actionId === "raze" ? "Raze" : actionId === "blockade-city" ? "Blockade" : "Raid";
            if (await command("BeginSiege", info.id, { destinationId: target.id, objective, formation: form, afterAttack: armyAfter })) anySent = true;
        } else if (actionId === "attack-army") {
            if (await command("AttackArmy", info.id, { destinationId: target.id, formation: form, afterAttack: armyAfter })) anySent = true;
        } else if (actionId === "intercept-convoy") {
            if (await command("InterceptConvoy", info.id, { destinationId: target.id, formation: form, afterAttack: armyAfter })) anySent = true;
        } else if (actionId === "capture-castle") {
            if (await command("CaptureCastle", info.id, { formation: form })) anySent = true;
        } else if (actionId === "camp") {
            if (await command("CampArmy", info.id, { x: target.position.x, y: target.position.y, loadedOnly: campLoadedOnly, afterAttack: "Hold", formation: form })) anySent = true;
        } else if (actionId === "move") {
            if (await command("MoveArmy", info.id, { x: target.position.x, y: target.position.y, formation: form })) anySent = true;
        }
    }

    if (anySent && activeOasisSelection) {
        const armId = activeOasisSelection.armyId;
        oasisUnitSelection = null;
        delete armyDrafts[armId];
        for (const key of [...editedDrafts]) if (key.startsWith(`${armId}|`)) editedDrafts.delete(key);
        selectedArmies = selectedArmies.filter(id => id !== armId);
        syncMapSelections();
        renderArmiesStrip();
        refreshCursorBadge();
        renderDrawer(true);
    }

    if (!anySent && missingScoutsOnly) {
        notice(isAr ? "الجيوش المحددة لا تحتوي على وحدات كشافة." : "None of the selected armies contain scouts.", true);
    }
}
function renderTimers() {
    if (!state)
        return;
    const elapsed = now();
    const mult = state.speedMultiplier && state.speedMultiplier > 1 ? ` · x${state.speedMultiplier}` : "";
    const spec = state.isSpectator ? " · 👁" : "";
    el("match-clock").textContent = state.status === "Running" ? `${time(elapsed)} · ${state.shieldRemainingSeconds > 0 ? "Protected" : "Open frontier"}${mult}${spec}` : `${state.status}${mult}${spec}`;
    document.querySelectorAll<HTMLElement>("[data-deadline]").forEach(item => item.textContent = time(Number(item.dataset.deadline) - elapsed));
    document.querySelectorAll<HTMLProgressElement>('[data-training-start]').forEach(bar => { const start = Number(bar.dataset.trainingStart), end = Number(bar.dataset.trainingEnd); bar.value = Math.min(1, Math.max(0, (elapsed - start) / Math.max(.01, end - start))); });
    if (pointerPressed) return;
    renderImportantAlerts();
    const queueHtml = state.economy?.jobs.slice(0, 4).map(j => `<button class="work-item" data-action="job" data-id="${j.targetId}">⚔ ${esc(state!.economy!.targets.find(t => t.id === j.targetId)?.name)}<strong>${time(j.completesAt - elapsed)}</strong></button>`).join("") ?? "";
    patchHtml(el("work-queue"), queueHtml);
    renderBuildings();
}
const descriptions: Record<string, string> = { Market: "Exchange goods with other players. Each level adds an offer slot. Goods and wagons are reserved until cancellation or trade delivery.", MainHall: "The heart of your province. Higher levels unlock more automatic training queues.", Warehouse: "Protect and store your supplies. Upgrade to increase the capacity of every resource.", Barracks: "Recruit infantry, spearmen and swordsmen. Prepare your next expedition here.", Stable: "Train cavalry, scouts and wagons to keep your armies moving.", SiegeWorkshop: "Build catapults and rams to overcome fortified cities.", Smithy: "Improve the weapons and armor of forces based in this village.", Hospital: "Wounded soldiers return here and recover automatically, using iron and wheat.", Wall: "Protect your garrison. Higher walls resist automatic attacks and siege engines." };
let drawerTabs = "", drawerBody = "";
function setTabs(tabs: {
    id: string;
    name: string;
}[]) { drawerTabs = tabs.map(t => button(t.name, "tab", t.id, panelTab === t.id ? "active" : "")).join(""); }
function pager(count: number, size = 6) { return `<div class="pager">${button("‹", "page-prev", "", "", page === 0)}<small>${page + 1} / ${Math.max(1, Math.ceil(count / size))}</small>${button("›", "page-next", "", "", (page + 1) * size >= count)}</div>`; }
function afterSelect() { return select("after-attack", "After this attack", [{ value: "Hold", label: "Hold position" }, { value: "Return", label: "Return home automatically" }], afterAttack); }
function trainingSummary(building: Target, roster: Target[]) {
    const next = roster.find(u => !u.unlocked);
    return `<section class="training-summary"><div><small>Training speed</small><strong dir="ltr">+${building.trainingSpeedBonusPercent ?? 0}%</strong></div><div><small>Units unlocked</small><strong dir="ltr">${roster.filter(u => u.unlocked).length} / ${roster.length}</strong></div><p>${building.name === 'MainHall' ? 'Higher levels speed up recruitment.' : 'Each building upgrade adds 7% training speed.'}</p>${next ? `<small>Next unlock: ${esc(next.name)} · Level ${next.requiredBuildingLevel}</small>` : '<small>All units unlocked</small>'}</section>`;
}
function trainingRoadmap(roster: Target[]) {
    return `<section class="training-roadmap" aria-label="Unit unlock progression">${roster.map(u => `<div class="${u.unlocked ? 'unlocked' : 'locked'}">${unitPortrait(unitId(u))}<span>${esc(u.name)}</span><small>Level ${u.requiredBuildingLevel} · ${u.unlocked ? 'Available' : 'Locked'}</small></div>`).join('')}</section>`;
}
function trainingProgress(job: Job) {
    return `<div class="recruit-progress"><div><strong>${job.quantity} recruits in training</strong><b data-deadline="${job.completesAt}">${time(job.completesAt - now())}</b></div><progress max="1" value="${Math.min(1, Math.max(0, (now() - job.startedAt) / Math.max(.01, job.completesAt - job.startedAt)))}" data-training-start="${job.startedAt}" data-training-end="${job.completesAt}" aria-label="Training progress"></progress></div>`;
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
    const e = state!.economy!, training = e.targets.filter(t => t.kind === "Training" && (t.trainingBuilding ?? facility(unitId(t))) === target.name).sort((a, b) => a.requiredBuildingLevel - b.requiredBuildingLevel);
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
        if (!recruit.unlocked) return `${laneQueue}${summary}${roster}<section class="recruit-lock"><div class="recruit-showcase">${unitPortrait(unitId(recruit))}</div><h3>${esc(recruit.name)}</h3><p>Requires ${pretty(target.name)} level ${recruit.requiredBuildingLevel}.</p><small class="${cappedByMainHall ? "requirement-lack" : ""}">${cappedByMainHall ? `Requires Main Hall level ${target.level + 1} first.` : 'Upgrade this building to unlock this unit.'}</small></section><div class="cost-box"><small>Next building upgrade</small>${costs(target.upgradeCost)}</div><div class="panel-footer">${button(`Upgrade to level ${target.level + 1}`, 'upgrade', target.id, 'primary wide', !running() || !target.upgradeCost || cappedByMainHall || !affordable(target.upgradeCost))}</div>`;
        return `${laneQueue}${summary}${roster}<div class="recruit-showcase">${unitPortrait(unitId(recruit))}<div><h3>${esc(recruit.name)}</h3><small>At home: ${e.garrison[unitId(recruit)] ?? 0} · Tier ${recruit.level}</small><small>${Number((recruit.trainingSeconds ?? 0).toFixed(2))} seconds per recruit</small></div></div>
        <div class="cost-box"><small>COST PER UNIT</small>${costs(recruit.trainingCost)}</div>
        <div><div class="stat-line"><span>Recruitment quantity</span><b dir="ltr">1–100</b></div><div class="quantity-row"><input id="train-range" aria-label="Recruitment quantity slider" type="range" min="1" max="100" value="${amount}"/><input id="train-amount" aria-label="Recruitment quantity" type="number" min="1" max="100" value="${amount}"/></div></div>
        <div class="cost-box"><small>TOTAL COST · <span id="total-quantity">${amount}</span> RECRUITS</small><div id="training-total">${costs(recruit.trainingCost, amount)}</div><div class="stat-line"><span>Ready in</span><b id="training-duration">${time((recruit.trainingSeconds ?? 0) * amount)}</b></div></div>
        <div class="panel-footer">${button(laneJobs.length ? `Queue ${amount} recruits` : `Train ${amount} recruits`, 'train', recruit.id, 'primary wide', !running() || queueFull || !affordable(recruit.trainingCost, amount))}<div class="button-row">${button(`Repeat batches: ${recruit.auto ? 'ON' : 'OFF'}`, 'auto', recruit.id, 'quiet', !running())}${button(`Tier ${recruit.level} → ${recruit.level + 1}`, 'tier', recruit.id, 'quiet', !running() || !recruit.upgradeCost)}</div></div>`;
    }
    if (panelTab === "govern")
        return governPanel();
    if (panelTab === "trade" && target.name === "Market") return marketPanel(state!.world!, e, state!.you, marketDraft, owner, running());
    const extra = target.name === "Warehouse" ? `Storage per resource: ${e.capacity.toLocaleString()}` : target.name === "Hospital" ? `${e.wounded} wounded awaiting recovery` : target.name === "MainHall" ? `${e.usedAutoSlots} / ${e.autoSlots} automatic training queues` : target.name === "Smithy" ? `Attack & armor bonus: ${5 * (target.level - 1)}%` : target.name === "Market" ? `Market offer slots: ${target.level}` : target.name === "Wall" ? "Higher walls increase garrison protection." : "Faster recruitment with every building level.";
    return `<div class="panel-hero">${target.kind === 'Training' ? unitPortrait(unitId(target)) : buildingArt(target.name, currentTribe())}<span class="level-seal">${target.level}<small>LEVEL</small></span></div><p class="muted">${descriptions[target.name] ?? 'Upgrade this training tier to recruit this unit faster.'}</p><div class="stat-line"><span>${extra}</span></div>${laneQueue}${training.length ? trainingSummary(target, training) + trainingRoadmap(training) : ''}<div class="cost-box"><small>${target.upgradeCost ? `UPGRADE TO LEVEL ${target.level + 1}` : 'FULLY UPGRADED'}</small>${costs(target.upgradeCost)}</div><div class="panel-footer">${button(target.kind === 'Training' ? 'Upgrade training tier' : 'Upgrade building', 'upgrade', target.id, 'primary wide', !running() || !target.upgradeCost || cappedByMainHall || !affordable(target.upgradeCost))}<small class="${cappedByMainHall ? "requirement-lack" : ""}">${cappedByMainHall ? `Requires Main Hall level ${target.level + 1}` : 'Upgrades complete instantly after payment.'}</small>${target.name === 'Market' ? button('Open market', 'tab', 'trade', 'quiet wide') : ''}</div>`;

}
function governPanel() {
    const city = state?.world?.warfare?.settlements.find(c => c.id === state?.economy?.villageId); if (!city)
        return ""; const owned = state!.world!.warfare!.settlements.filter(c => c.ownerId === state!.you && !c.ruined); const relocateBlocked = !running() || city.besieged || !!city.relocateTarget || city.relocateIn > 0; return `<p class="muted">An independent province under your banner.</p>${select("province", "Manage province", owned.map((c, i) => ({ value: c.id, label: `Province ${i + 1} · ${c.tribe} · Level ${c.level}` })), city.id)}${button("Dispatch an expedition", "rally", "", "primary wide", !running())}${city.tribe === "Mongols" ? `<p class="muted">Relocate for 4,000 of each resource (15m cooldown, speed 10 caravan). Choose a free spot on the world map first.</p>${button(city.relocateTarget ? "Relocating..." : "Choose relocation site", "relocation-map", "", "wide", relocateBlocked)}` : ""}<div class="panel-footer"><small>${state!.economy!.wounded} wounded · ${state!.economy!.usedAutoSlots}/${state!.economy!.autoSlots} automatic queues</small></div>`;
}
function formationPortraits(units: Record<string, number> | undefined | null, label = "Scouted composition", side: "friendly" | "hostile" = "hostile") {
    const entries = Object.entries(units ?? {}).filter(([, n]) => n > 0).sort(([a], [b]) => compareUnitIds(a, b));
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
    const stationedFormation: Record<string, number> = {};
    for (const a of (state!.world?.armies ?? []).filter(a => a.order === "Guarding" && a.defendingSiteId === city.id))
        for (const [unitId, count] of Object.entries(a.formation ?? {}))
            if (count > 0) stationedFormation[unitId] = (stationedFormation[unitId] ?? 0) + count;
    const stationedHtml = Object.keys(stationedFormation).length
        ? formationPortraits(stationedFormation, currentLanguage() === "ar" ? "التعزيزات المتمركزة داخل القرية" : "Stationed reinforcements inside village", "friendly")
        : "";
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
    return `<div class="panel-hero">${buildingArt("MainHall", city.tribe)}<span class="level-seal">${city.level}<small>LEVEL</small></span></div><div class="stat-line"><span>Commander</span><b>${esc(owner(city.ownerId))}</b></div><div class="stat-line"><span>Settlement</span><b>${settlementStage(city.level)}</b></div><small>Town at level 6. City at level 12.</small><div class="stat-line"><span>People</span><b>${city.tribe}</b></div><div class="stat-line"><span>Location</span><b>${Math.round(city.position.x)} : ${Math.round(city.position.y)}</b></div><div class="stat-line"><span>Status</span><b>${city.ruined ? "Ruins" : city.besieged ? "Under siege" : "Independent"}</b></div>${stationedHtml}${scouted ? (Object.keys(scouted).length ? formationPortraits(scouted, "Scouted composition", "hostile") : '<small class="muted">Scouted: no defending troops inside.</small>') : ""}${villageAlliances(city.ownerId)}${!city.ruined ? allianceControls(city.ownerId) : ""}<div class="panel-footer">${!mine && state!.shieldRemainingSeconds > 0 ? `<small>Protection expires in ${time(state!.shieldRemainingSeconds)}</small>` : ""}${villageGrid}</div>${resourceTransfer(city.id, city.ruined)}`;
}
function loadDispatchFormation() {
    formation = {};
    if (dispatchArmy !== "new") {
        const selected = ownArmies().find(a => a.id === dispatchArmy);
        formation = Object.fromEntries(
            Object.entries(selected?.formation ?? {})
                .filter(([, count]) => count > 0)
                .map(([id, count]) => [id, halfDefaultCount(id, count)])
        );
        return;
    }
}
function openDispatch(mission: "attack" | "reinforce" | "blockade" | "scout" | "camp" | "burn" = "attack") {
    dispatchMission = mission; attackObjective = "Raid"; campLoadedOnly = true;
    dispatchArmy = preferredDispatchArmy && ownArmies().some(a => a.id === preferredDispatchArmy && !["Battle", "Sieging", "Returning"].includes(a.order)) ? preferredDispatchArmy : "new";
    preferredDispatchArmy = "";
    afterAttack = mission === "scout" ? "Return" : "Hold";
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
                            : target === "castle" ? (isAr ? "إرسال جيش للهجوم على قلعة التاج والسيطرة عليها. الحفاظ على السيطرة المتواصلة لمدة 5 دقائق يمنح الفوز." : "Send an army to attack and capture Crown Keep. Holding it for 5 continuous minutes wins the match.")
                            : (isAr ? "اختر الوحدات وأرسل أوامرك." : "Choose units and send your orders.");
    const sendLabel = dispatchMission === "camp" ? (isAr ? "نصب المخيم" : "Establish camp")
        : dispatchMission === "scout" ? (isAr ? "إرسال كشافة" : "Send scouts")
            : dispatchMission === "reinforce" ? (isAr ? "إرسال تعزيزات" : "Send reinforcements")
                : dispatchMission === "blockade" ? (isAr ? "حصار الواحة" : "Besiege oasis")
                    : dispatchMission === "burn" ? (isAr ? "حرق الواحة" : "Burn oasis")
                        : target === "oasis" ? (isAr ? "احتلال الواحة" : "Capture oasis")
                            : target === "castle" ? (isAr ? "هجوم / احتلال القلعة" : "Attack / Capture Keep")
                            : (isAr ? "إرسال الجيش" : "Send army");
    const destName = oasis ? (isAr ? t(oasis.name) : oasis.name) : (selection?.name ?? (isAr ? "مرج مفتوح" : "Open meadow"));
    const formationEditor = units.length ? `<div class="dispatch-formation">${units.slice(page * 9, page * 9 + 9).map(([id, count]) => `<div class="dispatch-unit-card">${unitPortrait(id)}<span class="dispatch-unit-meta"><strong>${esc(unitCatalog.get(id)?.displayName ?? pretty(role(id)))}</strong><small>${count} available</small></span><div class="dispatch-input-row"><input data-formation="${id}" aria-label="${role(id)} count" type="number" inputmode="numeric" min="0" max="${count}" value="${formation[id] ?? 0}"/><button type="button" class="dispatch-quick-btn" data-action="formation-quick" data-id="${id}" data-max="${count}" title="Half / All">½</button></div></div>`).join("")}</div>${units.length > 9 ? pager(units.length, 9) : ""}` : '<p class="empty">No suitable units are available for this order.</p>';
    return `<div class="dispatch-destination"><strong>${esc(destName)}</strong><small>${description}</small></div>${oasis ? `<small class="oasis-specialty">${icon(oasis.resource.toLowerCase())} +50% ${oasis.resource} / +25% other resources</small>` : ""}${dispatchArmyLocation()}${dispatchArmy === "new" ? select("dispatch-source", "Dispatch from village", (state?.world?.warfare?.settlements ?? []).filter(c => c.ownerId === state?.you && !c.ruined).map((c, i) => ({ value: c.id, label: `Province ${i + 1} / Level ${c.level}` })), e.villageId) : ""}${candidateArmies.length ? select("dispatch-army", "Choose your force", [{ value: "new", label: "Raise a new expedition" }, ...candidateArmies.map((a, i) => ({ value: a.id, label: `Army ${i + 1} / ${a.soldiers + a.wagons} units` }))], dispatchArmy) : ""}${formationEditor}${dispatchArmy === "new" ? '<div id="dispatch-provisions"></div>' : '<p class="muted">Only the selected units receive this order. The remainder keeps its current assignment, food and loot.</p>'}<div id="attack-objectives"></div><div class="dispatch-directives">${dispatchMission === "camp" ? `<label class="check"><input id="camp-loaded-only" type="checkbox" ${campLoadedOnly ? "checked" : ""}/> Raid only loaded caravans and armies</label>` : ""}${["reinforce", "scout"].includes(dispatchMission) ? "" : afterSelect()}</div><div class="panel-footer">${button(sendLabel, "dispatch-send", "", "primary wide", !running())}<small>Automatic supplies depart only when provisions run low.</small></div>`;
}
function objectiveAllowed(objective = attackObjective) {
    if (selection?.kind !== "village" || dispatchMission !== "attack") return true;
    const troops = formation;
    const has = (kind: string) => Object.entries(troops).some(([id, n]) => role(id) === kind && n > 0);
    return objective === 'Conquest' ? has('governor') : objective === 'Raze' ? has('catapult') : true;
}
function renderObjectives() {
    const box = document.getElementById('attack-objectives'); if (!box) return;
    if (selection?.kind !== 'village' || dispatchMission !== 'attack') { box.innerHTML = ''; return; }
    const objectives = [['Raid', 'Raid resources', 'Take resources and leave the village standing.', '⚒'], ['Conquest', 'Conquer village', 'Requires the governor trained in the Main Hall. Persuasion advances each round.', '⚑'], ['Raze', 'Destroy village', 'Requires catapults. Buildings lose levels over about five minutes after the defense falls.', '♜'], ['Blockade', 'Besiege village', 'Block external deliveries and reinforcements. Wheat production is reduced by 50%.', '◉']];
    box.innerHTML = `<fieldset class="attack-objectives"><legend>Attack objective</legend><div>${objectives.map(([value, label, detail, mark]) => `<button type="button" data-action="attack-objective" data-id="${value}" class="objective-card ${attackObjective === value ? 'active' : ''}" aria-pressed="${attackObjective === value}" ${!objectiveAllowed(value) ? 'disabled' : ''}><span>${mark}</span><strong>${label}</strong><small>${detail}</small></button>`).join('')}</div></fieldset>`;
    localizeNow(box);
}
function renderProvisions() {
    renderObjectives();
    if (dispatchArmy !== 'new') { const send = el('drawer').querySelector<HTMLButtonElement>('[data-action="dispatch-send"]'); if (send) send.disabled = !running() || !objectiveAllowed() || !Object.values(formation).some(n => n > 0); }

    const box = document.getElementById('dispatch-provisions'); if (!box || !state?.economy) return;
    const home = state.world?.warfare?.settlements.find(c => c.id === state?.economy?.villageId)?.position ?? { x: 1000, y: 1000 }, dest = selection?.position ?? home;
    const p = provisionEstimate(formation, Math.hypot(dest.x - home.x, dest.y - home.y), state.economy.resources.wheat);
    const enough = p.total > 0 && p.food >= p.required;
    box.innerHTML = `<div class="provision-line ${enough ? 'positive' : 'negative'}">${icon('wheat')}<b>${p.food.toLocaleString()}</b><span>Provisions loaded</span><small>${Math.ceil(p.seconds / 60)} min</small></div><small>Includes the return journey and combat time.</small>${!enough ? '<small class="negative">Choose units with enough capacity and wheat before departing.</small>' : ''}`;
    const send = el('drawer').querySelector<HTMLButtonElement>('[data-action="dispatch-send"]'); if (send) send.disabled = !running() || !enough || !objectiveAllowed();
}
function armyCountBanner(totalUnits: number, isAr: boolean, details: string, hostile = false): string {
    return `<section class="army-count-banner${hostile ? " hostile" : ""}">
        <div class="acb-main">
            <span class="acb-label">${icon("sword")} ${isAr ? "عدد الوحدات" : "Total units"}</span>
            <strong class="acb-total">${totalUnits.toLocaleString()}</strong>
        </div>
        ${details ? `<small class="acb-sub">${details}</small>` : ""}
    </section>`;
}
function armyPanel() {
    const isAr = currentLanguage() === "ar";
    if (panelId === "multi" || (selectedArmies.length > 1 && panelId === selectedArmies[0])) {
        const infos = selectedArmies.map(id => armyTroopMap(id)).filter((x): x is ControlledArmyInfo => !!x);
        if (!infos.length) return `<p class="empty">${isAr ? "لا توجد جيوش محددة." : "No armies selected."}</p>`;
        if (infos.length === 1) { panelId = infos[0]!.id; } else {
            const totalUnits = infos.reduce((s, i) => s + i.total, 0);
            const fieldArmies = infos.filter(i => !i.isHome).map(i => ownArmies().find(a => a.id === i.id)).filter((x): x is ArmyView => !!x);
            const wheatCarried = fieldArmies.reduce((s, a) => s + Math.floor(a.wheat ?? 0), 0);
            const capacityTotal = fieldArmies.reduce((s, a) => s + Math.floor(a.capacity ?? 0), 0);
            const sumLoot = fieldArmies.reduce<Resources>((acc, a) => ({
                wood: acc.wood + (a.loot?.wood ?? 0),
                iron: acc.iron + (a.loot?.iron ?? 0),
                stone: acc.stone + (a.loot?.stone ?? 0),
                wheat: acc.wheat + (a.loot?.wheat ?? 0)
            }), { wood: 0, iron: 0, stone: 0, wheat: 0 });
            const woundedSum = fieldArmies.reduce((s, a) => s + (a.wounded ?? 0), 0);
            const deadSum = fieldArmies.reduce((s, a) => s + (a.dead ?? 0), 0);
            const fledSum = fieldArmies.reduce((s, a) => s + (a.fled ?? 0), 0);
            const chips = infos.map(i => `<div class="army-inspector-chip">
                <button type="button" class="quiet" style="flex:1;text-align:start;border:0;background:transparent;padding:0" data-action="inspector-solo" data-id="${i.id}">
                    <strong>${esc(i.name)}</strong> · <small>${i.total.toLocaleString()} ${isAr ? "وحدة" : "units"}</small>
                </button>
                <button type="button" class="icon-button" data-action="toggle-army" data-id="${i.id}" title="${isAr ? 'إزالة من التحديد' : 'Remove'}">×</button>
            </div>`).join("");
            return `<div class="stat-line"><span>${isAr ? "إجمالي الجيوش" : "Selected forces"}</span><b>${infos.length} · ${totalUnits.toLocaleString()} ${isAr ? "وحدة" : "units"}</b></div>
                ${capacityTotal > 0 ? `<div><div class="stat-line"><span>${isAr ? "المؤونة المحمولة" : "Carried food"}</span><b>${wheatCarried.toLocaleString()} / ${capacityTotal.toLocaleString()}</b></div><div class="meter food"><i style="width:${Math.min(100, wheatCarried / Math.max(1, capacityTotal) * 100)}%"></i></div></div>` : ""}
                ${renderLootRow(sumLoot, isAr ? "الموارد المحمولة (الإجمالي)" : "Carried resources (total)")}
                ${renderCasualtiesGrid(woundedSum, deadSum, fledSum, isAr)}
                <section class="army-units-editor"><div class="army-units-editor-head"><strong>${isAr ? "الجيوش المحددة" : "Selected armies"}</strong></div>${chips}</section>
                <div class="army-inspector-hint">${isAr ? "كليك يمين على أي هدف أو على الخريطة لإرسال جميع الجيوش المحددة دفعة واحدة." : "Right-click any target or map position to command all selected armies."}</div>
                <div class="panel-footer">${button(isAr ? "دمج الجيوش" : "Merge armies", "merge-armies", "multi", "primary wide", !mergeSelectionInfo().eligible)}${fieldArmies.length ? button(isAr ? "العودة للقرية" : "Return to village", "recall", "multi", "quiet wide", !running() || fieldArmies.every(a => a.order === "Returning")) : ""}</div>`;
        }
    }

    if (panelId.startsWith("home:")) {
        const info = armyTroopMap(panelId);
        if (!info) return `<p class="empty">${isAr ? "لم تعد هناك وحدات متمركزة في هذه القرية." : "No troops remain in this village garrison."}</p>`;
        const sameEco = state?.economy?.villageId === info.villageId ? state.economy : null;
        const chosen = chosenDraft(info.id);
        const chosenSum = Object.values(chosen).reduce((s, n) => s + n, 0);
        const villageHospital = sameEco?.wounded ?? 0;
        const recentVillageReports = (state?.world?.reports ?? []).filter(r => r.villageId === info.villageId && r.defender === state?.you);
        const vDead = recentVillageReports.reduce((s, r) => s + r.garrisonUnits.reduce((k, u) => k + u.dead, 0), 0);
        const vFled = recentVillageReports.reduce((s, r) => s + r.garrisonUnits.reduce((k, u) => k + u.fled, 0), 0);
        const confirmingDisband = chosenSum > 0 && disbandConfirmKey === currentDisbandKey(info.id, chosen);
        const blockedHome = isVillageUnderSiege(info.villageId);

        return `<div class="stat-line"><span>${isAr ? "حامية متمركزة" : "Home garrison"}</span><b>${info.total.toLocaleString()} ${isAr ? "وحدة" : "units"} · ${Math.round(info.position.x)}:${Math.round(info.position.y)}</b></div>
            <div><div class="stat-line"><span>${isAr ? "المؤونة المحمولة" : "Carried provisions"}</span><b>0</b></div>
            <small class="muted">${isAr ? "تتغذى الحامية مباشرة داخل القرية ولا تحمل مؤونة أو موارد ميدانية حتى تكوّن جيشاً." : "Stationed garrison eats inside the village and carries no field provisions or resources yet."}</small></div>
            ${renderLootRow({ wood: 0, iron: 0, stone: 0, wheat: 0 }, isAr ? "الموارد المحمولة" : "Carried resources")}
            ${renderCasualtiesGrid(villageHospital, vDead, vFled, isAr)}
            ${renderUnitSplitRows(info, isAr)}
            <div class="army-inspector-hint">${isAr ? "حدد عدد الوحدات ثم اضغط «إنشاء جيش جديد» أو «تسريح المحدد» لتقليل استهلاك القمح." : "Adjust counts to create a new army, or disband surplus units to reduce wheat upkeep."}</div>
            <div class="panel-footer">${button(isAr ? "إنشاء جيش جديد" : "Create new army", "inspector-create-army", info.id, "primary wide", !running() || chosenSum <= 0)}${button(disbandButtonLabel(chosen, isAr, confirmingDisband), "inspector-disband", info.id, `danger wide${confirmingDisband ? " disband-confirm" : ""}`, !running() || chosenSum <= 0 || blockedHome)}</div>`;
    }

    const a = state!.world?.armies.find(a => a.id === panelId);
    if (!a)
        return '<p class="empty">This army has returned or left the field.</p>';
    const mine = a.ownerId === state!.you;
    if (!mine) {
        const totalUnits = a.soldiers + a.wagons;
        if (isAllyPlayer(a.ownerId)) {
            return `${armyCountBanner(totalUnits, isAr, `${esc(t(a.order))} · ${a.soldiers.toLocaleString()} ${isAr ? "جندي" : "troops"} · ${a.wagons.toLocaleString()} ${isAr ? "عربة" : "wagons"}`)}
                <div class="stat-line"><span>${isAr ? "القائد الحليف" : "Allied commander"}</span><b>${esc(owner(a.ownerId))}</b></div>
                <div><div class="stat-line"><span>${isAr ? "المؤونة" : "Food"}</span><b>${Math.floor(a.wheat ?? 0).toLocaleString()} / ${Math.floor(a.capacity).toLocaleString()}</b></div><div class="meter food"><i style="width:${Math.min(100, (a.wheat ?? 0) / Math.max(1, a.capacity) * 100)}%"></i></div></div>
                ${renderLootRow(a.loot ?? { wood: 0, iron: 0, stone: 0, wheat: 0 }, isAr ? "الموارد المحمولة" : "Carried resources")}
                ${renderCasualtiesGrid(a.wounded ?? 0, a.dead ?? 0, a.fled ?? 0, isAr)}
                ${formationPortraits(a.formation, isAr ? "تشكيلة جيش الحليف" : "Allied army", "friendly")}`;
        }
        const scoutActive = isArmyScouted(a);
        const attackBlocked = !running() || state!.shieldRemainingSeconds > 0 || !canAttackArmyInBattle(a.id, buildTargetContext());
        const enemyDetails = scoutActive
            ? `${a.soldiers.toLocaleString()} ${isAr ? "جندي" : "troops"} · ${a.wagons.toLocaleString()} ${isAr ? "عربة" : "wagons"}`
            : (isAr ? "التفاصيل مخفية حتى الكشف" : "Breakdown hidden until scouted");
        return `<div class="panel-hero">${icon("sword")}</div>${armyCountBanner(totalUnits, isAr, enemyDetails, true)}<div class="stat-line"><span>${isAr ? "القائد" : "Commander"}</span><b>${esc(owner(a.ownerId))}</b></div>${enemyArmySlots(a, isAr)}<div class="panel-footer">${button(isAr ? "مهاجمة هذا الجيش" : "Intercept this army", "plan-attack", a.id, "primary wide", attackBlocked)}${button(isAr ? "إرسال كشافة" : "Send scouts", "scout-target", a.id, "quiet wide", !running())}</div>`;
    }
    setTabs([{ id: "overview", name: "Orders" }, { id: "supply", name: "Supply" }, { id: "cargo", name: "Cargo" }]);
    const battle = state!.world!.warfare?.battles.find(b => b.status === "Active" && ((b.attackerArmyIds ?? []).includes(a.id) || (b.defenderArmyIds ?? []).includes(a.id))), siege = state!.world!.warfare?.sieges.find(s => (s.armyIds ?? []).includes(a.id) && ["Active"].includes(s.status));
    const inCombat = !!battle || !!siege || !!state!.world!.oasisSieges?.some(s => (s.armyIds ?? []).includes(a.id));
    const stats = `<div><div class="stat-line"><span>Food</span><b>${Math.floor(a.wheat ?? 0)} / ${a.capacity}</b></div><div class="meter food"><i style="width:${Math.min(100, (a.wheat ?? 0) / Math.max(1, a.capacity) * 100)}%"></i></div></div>`;
    if (panelTab === "supply") {
        const cities = state!.world!.warfare!.settlements.filter(c => c.ownerId === state!.you && !c.ruined);
        return `${stats}<p class="muted">${esc(a.supplyStatus)}</p>${select("supply-source", "Supply province", cities.map((c, i) => ({ value: c.id, label: `Province ${i + 1} · ${c.tribe}` })), supplySource)}<div class="form-grid"><label>Wagons (0 = auto)<input id="supply-wagons" type="number" min="0" max="20" value="${supplyWagons}"/></label><label>Infantry escorts<input id="supply-escorts" type="number" min="0" max="100" value="${escorts}"/></label></div><label class="check"><input id="supply-auto" type="checkbox" ${autoSupply ? "checked" : ""}/> Automatic supply deliveries</label><div class="panel-footer">${button("Update supply line", "supply", a.id, "primary wide", !running())}</div>`;
    }
    if (panelTab === "cargo") return `${cargoView(a.loot ?? { wood: 0, iron: 0, stone: 0, wheat: 0 }, "MATERIALS & LOOT ON BOARD")}<div class="stat-line"><span>Carried wheat / food capacity</span><b>${Math.floor(a.wheat ?? 0)} / ${Math.floor(a.capacity)}</b></div><p class="muted">Wheat is consumed during travel and combat. Remaining food and loot are credited when this army returns home.</p><div class="panel-footer">${button("Open battle reports", "show-reports", "", "primary wide")}</div>`;

    const info = armyTroopMap(a.id);
    const chosen = info ? chosenDraft(info.id) : {};
    const chosenSum = Object.values(chosen).reduce((s, n) => s + n, 0);
    const blockedField = inCombat || a.order === "Returning";
    const canSplit = !!info && running() && chosenSum > 0 && chosenSum < info.total && !blockedField;
    const confirmingDisband = !!info && chosenSum > 0 && disbandConfirmKey === currentDisbandKey(info.id, chosen);

    const totalUnits = a.soldiers + a.wagons;
    return `${armyCountBanner(totalUnits, isAr, `${esc(t(a.order))} · ${a.soldiers.toLocaleString()} ${isAr ? "جندي" : "troops"} · ${a.wagons.toLocaleString()} ${isAr ? "عربة" : "wagons"}`)}
        ${stats}
        ${renderLootRow(a.loot ?? { wood: 0, iron: 0, stone: 0, wheat: 0 }, isAr ? "الموارد المحمولة" : "Carried resources")}
        ${renderCasualtiesGrid(a.wounded ?? 0, a.dead ?? 0, a.fled ?? 0, isAr)}
        ${battle ? `<div class="battle-line"><strong>Battle · Round ${battle.round}</strong><p>${battle.attackerTroops} vs ${battle.defenderTroops} · ${time(battle.elapsed)}</p></div>` : ""}
        ${siege ? `<div class="battle-line"><strong>${siege.phase === "Persuading" ? "Establishing control" : "Siege in progress"}</strong><p>Gate ${Math.round(siege.gate)}% · Wall ${Math.round(siege.wall)}%<br/>Loyalty ${Math.round(siege.loyalty)}%</p></div>` : ""}
        ${info ? renderUnitSplitRows(info, isAr) : formationPortraits(a.formation, "Your army", "friendly")}
        ${afterSelect()}
        <div class="army-inspector-hint">${isAr ? "قلّل الأعداد عن الإجمالي لفصل جيش جديد، أو اختر عدداً واضغط «تسريح المحدد»، أو كليك يمين للأوامر." : "Reduce counts below total to split a new army, select counts to disband, or right-click to command."}</div>
        <div class="panel-footer">${button(isAr ? "إنشاء جيش جديد" : "Create new army", "inspector-create-army", a.id, "primary wide", !canSplit)}${button(disbandButtonLabel(chosen, isAr, confirmingDisband), "inspector-disband", a.id, `danger wide${confirmingDisband ? " disband-confirm" : ""}`, !info || !running() || chosenSum <= 0 || blockedField)}${button(isAr ? "العودة للقرية" : "Return to village", "recall", a.id, "quiet wide", !running() || inCombat || a.order === "Returning")}</div>`;
}
function isArmyScouted(a: ArmyView): boolean {
    return Boolean(a.formation && Object.values(a.formation).some(n => n > 0) && (a.scoutedUntil == null || a.scoutedUntil > now()));
}

function enemyArmySlots(a: ArmyView, isAr: boolean): string {
    if (isArmyScouted(a)) {
        const remaining = a.scoutedUntil != null ? Math.max(0, a.scoutedUntil - now()) : null;
        const label = remaining != null
            ? `${isAr ? "تشكيلة الجيش المكشوفة · متبقي" : "Scouted composition ·"} <b data-deadline="${a.scoutedUntil}">${time(remaining)}</b>`
            : (isAr ? "تشكيلة الجيش (مكشوفة)" : "Scouted composition");
        return formationPortraits(a.formation, label, "hostile");
    }
    const figCount = Object.values(a.mapFigures ?? {}).filter(n => n > 0).length;
    const slotCount = figCount > 0 ? figCount : (a.soldiers + a.wagons > 0 ? 1 : 0);
    if (!slotCount) {
        return `<small class="muted">${isAr ? "تشكيلة هذا الجيش مجهولة. أرسل كشافة لكشف الوحدات لمدة دقيقة." : "This army's units are unknown. Send scouts to reveal them for 1 minute."}</small>`;
    }
    const cols = Math.min(4, slotCount);
    const unknownSlots = Array.from({ length: slotCount }, () =>
        `<div class="report-unit unknown-unit-slot" title="?"><span class="unit-portrait unknown-unit-portrait" aria-label="?">?</span><span class="unit-counts"><b>?</b></span></div>`
    ).join("");
    return `<div class="report-forces scouted-forces"><div class="report-force hostile"><strong>${isAr ? "خانات جنود العدو (مجهولة)" : "Enemy unit slots (unknown)"}</strong><div class="report-unit-grid" style="--unit-cols:${cols}">${unknownSlots}</div></div></div><small class="muted">${isAr ? "أنواع الوحدات وأعدادها مخفية — أرسل كشافة لكشف هذا الجيش لمدة دقيقة واحدة." : "Unit types and counts are hidden — send scouts to reveal this army for 1 minute."}</small>`;
}
function castlePanel() {
    const war = state!.world!.warfare!, keep = war.castle, isAr = currentLanguage() === "ar";
    const unlockSeconds = (state!.settings.castleUnlockMinutes ?? 45) * 60;
    const ruler = keep.controllerId ? esc(owner(keep.controllerId)) : (isAr ? "غير محتلة" : "Unclaimed");
    const statusText = !keep.active
        ? (isAr ? `تفتح بعد ${time(Math.max(0, unlockSeconds - now()))}` : `Opens in ${time(Math.max(0, unlockSeconds - now()))}`)
        : keep.contested
            ? (isAr ? "متنازع عليها — العداد متوقف مؤقتاً أثناء القتال" : "Contested — hold timer paused during battle")
            : keep.controllerId
                ? (isAr ? `المسيطر الحالي: ${ruler}` : `Controlled by ${ruler}`)
                : (isAr ? "مفتوحة للهجوم والسيطرة" : "Open for capture");
    const pct = Math.min(100, (keep.captureProgress / 300) * 100);
    return `<div class="panel-hero">${buildingArt("Wall", currentTribe())}<span class="level-seal">👑</span></div>
        <div class="stat-line"><span>${isAr ? "الموقع" : "Location"}</span><b>${Math.round(keep.position.x)} : ${Math.round(keep.position.y)}</b></div>
        <div class="stat-line"><span>${isAr ? "المسيطر" : "Ruler"}</span><b>${ruler}</b></div>
        <div class="battle-line">
            <strong>${isAr ? "حالة قلعة التاج" : "Crown Keep status"}</strong>
            <p>${statusText}</p>
            ${keep.active ? `<div class="stat-line"><span>${isAr ? "مدة السيطرة" : "Continuous hold"}</span><b>${time(keep.captureProgress)} / 5:00</b></div><div class="meter"><i style="width:${pct}%"></i></div>` : ""}
        </div>
        <div class="panel-footer">
            ${button(isAr ? "هجوم / احتلال القلعة" : "Attack / Capture Keep", "plan-castle-attack", keep.id, "primary wide", !running() || !keep.active)}
            ${button(isAr ? "نصب معسكر عند القلعة" : "Camp here", "plan-camp", keep.id, "quiet wide", !running())}
            ${army() ? button(isAr ? "توجيه الجيش المحدد مباشرة للقلعة" : "Send selected army to Keep", "castle-march", "", "quiet wide", !running() || !keep.active) : ""}
            ${button(isAr ? "عرض الممالك والترتيب" : "View Kingdoms & Standings", "show-standings", "", "quiet wide")}
        </div>`;
}
function pendingAllianceRequests() {
    const requests = state?.allianceRequests ?? [];
    if (!requests.length) return "";
    return `<section class="alliance-requests"><h3>Alliance requests</h3>${requests.map(request => {
        const incoming = request.recipientId === state!.you, other = incoming ? request.senderId : request.recipientId;
        return `<div class="alliance-request"><span>${incoming ? `${esc(owner(other))} invited you to an alliance.` : `Request sent to ${esc(owner(other))}`}</span><div class="alliance-actions">${incoming ? `${button("Accept", "alliance-accept", request.id, "primary", !running())}${button("Decline", "alliance-decline", request.id, "quiet", !running())}` : button("Cancel request", "alliance-cancel", request.id, "quiet", !running())}</div></div>`;
    }).join("")}</section>`;
}
function standingsPanel() {
    const war = state!.world!.warfare!, ranks = state!.result?.rankings ?? war.rankings, keep = war.castle, isAr = currentLanguage() === "ar";
    const winnerNames = state!.result ? state!.result.winnerIds.map(owner).join(isAr ? " و " : " & ") : "";
    const unlockSeconds = (state!.settings.castleUnlockMinutes ?? 45) * 60;
    return `${pendingAllianceRequests()}${state!.result ? `<div class="victory"><h3>${state!.result.winnerIds.includes(state!.you) ? (isAr ? "انتصرت!" : "Victory!") : (isAr ? "انتهت المباراة" : "Match complete")}</h3><div class="victory-winner-inline">${isAr ? (state!.result.winnerIds.length > 1 ? `الفائزون: ${esc(winnerNames)}` : `الفائز: ${esc(winnerNames)}`) : (state!.result.winnerIds.length > 1 ? `Winners: ${esc(winnerNames)}` : `Winner: ${esc(winnerNames)}`)}</div><p class="muted">${esc(state!.result.reason)}</p></div>` : `<div class="battle-line"><strong>${isAr ? "قلعة التاج" : "Crown Keep"}</strong><p>${keep.active ? `${keep.contested ? (isAr ? "متنازع عليها (متوقف)" : "Contested (Paused)") : keep.controllerId ? (isAr ? "المسيطر: " : "Ruler: ") + esc(owner(keep.controllerId)) : (isAr ? "مفتوحة" : "Open")} · ${time(keep.captureProgress)} / 5:00` : `${isAr ? `تفتح عند ${time(unlockSeconds)}` : `Opens at ${time(unlockSeconds)}`} (${time(Math.max(0, unlockSeconds - now()))})`}</p>${keep.active ? button(isAr ? "هجوم / احتلال القلعة" : "Attack / Capture Keep", "plan-castle-attack", keep.id, "primary wide", !running()) : ""}</div>`}<div>${ranks.slice(page * 6, page * 6 + 6).map((r, i) => `<div class="rank-row ${r.playerId === state!.you ? "own" : ""}"><span>${page * 6 + i + 1}</span><span>${esc(owner(r.playerId))}<small>${r.eliminated ? "Eliminated" : `${r.settlements} provinces · ${r.kills} kills · ${r.wounded} wounded`}</small></span><strong>${r.score.toLocaleString()}</strong></div>`).join("")}</div>${pager(ranks.length)}${state!.status !== "Running" ? button("Leave realm", "leave", "", "quiet wide") : ""}`;
}
function lobbyPanel() {
    const s = state!;
    const isHost = s.hostId === s.you;
    const isAr = currentLanguage() === "ar";
    setTabs([
        { id: "overview", name: isAr ? "اللوبي واللاعبون" : "Commanders" },
        { id: "settings", name: isAr ? "إعدادات الروم" : "Room settings" }
    ]);
    if (panelTab === "settings") {
        const t = s.settings;
        const ro = !isHost ? "disabled" : "";
        return `<div class="lobby-settings-pane${!isHost ? " readonly" : ""}">
            ${!isHost ? `<p class="muted">${isAr ? "هذه إعدادات الروم الحالية (منشئ الروم فقط يمكنه تعديلها)." : "Current room settings (only the host can modify them)."}</p>` : ""}
            <div class="form-grid">
                <label>${isAr ? "عدد اللاعبين" : "Seats"}<input id="capacity" type="number" min="8" max="16" value="${t.capacity}" ${ro}/></label>
                <label>${isAr ? "المدة (دقيقة)" : "Minutes"}<input id="duration" type="number" min="30" max="999" value="${t.durationMinutes}" ${ro}/></label>
                <label>${isAr ? "ظهور قلعة التاج (دقيقة)" : "Crown Keep opens (min)"}<input id="castle-unlock" type="number" min="1" max="999" value="${t.castleUnlockMinutes ?? 45}" ${ro}/></label>
                ${select("shield", isAr ? "درع الحماية (دقائق)" : "Protection", ["0", "5", "10"], String(t.shieldMinutes))}
                <label>${isAr ? "سرعة الموارد" : "Resource speed"}<input id="speed" type="number" min="0.5" max="5" step="0.5" value="${t.resourceSpeed}" ${ro}/></label>
                <label>${isAr ? "عدد البوتات" : "AI neighbors"}<input id="bots" type="number" min="0" max="15" value="${t.botCount}" ${ro}/></label>
                ${select("difficulty", isAr ? "صعوبة البوتات" : "AI difficulty", ["Easy", "Normal", "Hard"], t.botDifficulty)}
            </div>
        </div>
        <div class="panel-footer">
            ${isHost ? button(isAr ? "حفظ إعدادات الروم" : "Apply settings", "settings", "", "primary wide") : ""}
            ${button(isAr ? "العودة للاعبين" : "Back to lobby", "tab", "overview", "quiet wide")}
        </div>`;
    }

    const tribes = ["Romans", "Germans", "Greeks", "Mongols", "Persians"];
    const t = s.settings;
    const youPlayer = s.players.find(p => p.id === s.you);
    const cap = Math.max(2, s.settings.capacity);
    const slotButtons = Array.from({ length: cap }, (_, slot) => {
        const angle = (slot * Math.PI * 2) / cap;
        const x = 80 + 60 * Math.cos(angle);
        const y = 80 + 60 * Math.sin(angle);
        const occ = s.players.find(p => (p.effectiveSpawnSlot ?? p.spawnSlot) === slot);
        const isMine = occ?.id === s.you;
        const humanLocked = !!occ && !occ.isBot && !isMine;
        const title = occ
            ? `${slot + 1}: ${occ.name}${humanLocked ? (isAr ? " (محجوز للاعب)" : " (Taken by player)") : occ.isBot ? (isAr ? " (بوت - اضغط لأخذ مكانه)" : " (Bot - click to take spot)") : ""}`
            : (isAr ? `اختيار الموقع ${slot + 1}` : `Choose spot ${slot + 1}`);
        return `<button type="button" class="lobby-map-slot${isMine ? " mine" : ""}${occ ? " taken" : ""}${occ?.isBot ? " bot-slot" : ""}${humanLocked ? " locked" : ""}" data-action="lobby-slot" data-id="${slot}" ${humanLocked ? "disabled" : ""} style="left:${(x / 160 * 100).toFixed(2)}%;top:${(y / 160 * 100).toFixed(2)}%;--slot-color:${occ?.flagColor ? esc(occ.flagColor) : "rgba(255,255,255,0.22)"}" title="${esc(title)}" data-no-translate="1">${slot + 1}</button>`;
    }).join("");
    const ringGuides = Array.from({ length: cap }, (_, slot) => {
        const angle = (slot * Math.PI * 2) / cap;
        const x = (80 + 60 * Math.cos(angle)).toFixed(1);
        const y = (80 + 60 * Math.sin(angle)).toFixed(1);
        return `<line x1="80" y1="80" x2="${x}" y2="${y}" stroke="rgba(218,187,114,0.16)" stroke-width="1"/>`;
    }).join("");
    const swatches = LOBBY_PALETTE.map(hex => {
        const occ = s.players.find(p => (p.flagColor ?? "").toLowerCase() === hex);
        const isMine = occ?.id === s.you;
        const humanLocked = !!occ && !occ.isBot && !isMine;
        const title = occ
            ? `${occ.name}${humanLocked ? (isAr ? " (مستخدم)" : " (In use)") : occ.isBot ? (isAr ? " (تبديل)" : " (Swap)") : ""}`
            : hex;
        return `<button type="button" class="lobby-swatch${isMine ? " active" : ""}${humanLocked ? " locked" : ""}" data-action="lobby-color" data-id="${hex}" ${humanLocked ? "disabled" : ""} style="--swatch:${hex}" title="${esc(title)}" data-no-translate="1">${isMine ? "✓" : humanLocked ? "×" : ""}</button>`;
    }).join("");

    return `<p class="muted">${isAr ? `المقاعد: ${t.capacity} · أعضاء الفريق الواحد يبدأون متجاورين وفي نفس التحالف تلقائياً` : `Seats: ${t.capacity} · Teammates spawn adjacent and allied automatically.`}</p>
    <div class="lobby-setup">
        <div class="lobby-map-col">
            <div class="lobby-map-head">
                <small>${isAr ? "موقعك على الخريطة" : "Map position"}</small>
                <button type="button" class="quiet lobby-auto-btn${youPlayer?.spawnSlot == null ? " active" : ""}" data-action="lobby-slot" data-id="-1">${isAr ? "تلقائي" : "Auto"}</button>
            </div>
            <div class="lobby-spawn-map">
                <svg viewBox="0 0 160 160" aria-hidden="true">
                    <circle cx="80" cy="80" r="74" fill="rgba(16,22,19,0.72)" stroke="rgba(218,187,114,0.22)" stroke-width="1"/>
                    <circle cx="80" cy="80" r="60" fill="none" stroke="rgba(218,187,114,0.34)" stroke-dasharray="3 3" stroke-width="1.2"/>
                    ${ringGuides}
                    <circle cx="80" cy="80" r="10" fill="rgba(218,187,114,0.2)" stroke="#e7c673" stroke-width="1.2"/>
                    <text x="80" y="83" text-anchor="middle" font-size="10" fill="#f7df9d">♛</text>
                </svg>
                ${slotButtons}
            </div>
        </div>
        <div class="lobby-color-col">
            <small>${isAr ? "لون الراية" : "Flag color"}</small>
            <div class="lobby-color-bar">${swatches}</div>
            <small class="lobby-hint">${isAr ? `موقعك: ${youPlayer?.spawnSlot != null ? `#${youPlayer.spawnSlot + 1}` : (youPlayer?.effectiveSpawnSlot != null ? `تلقائي (#${youPlayer.effectiveSpawnSlot + 1})` : "تلقائي")}` : `Your spot: ${youPlayer?.spawnSlot != null ? `#${youPlayer.spawnSlot + 1}` : (youPlayer?.effectiveSpawnSlot != null ? `Auto (#${youPlayer.effectiveSpawnSlot + 1})` : "Auto")}`}</small>
        </div>
    </div>
    <div class="lobby-leaders-list">${s.players.map(p => {
        const canEdit = p.id === s.you || (p.isBot && isHost);
        const tribeOptions = [
            ...(p.isBot ? [`<option value="Random" ${p.randomTribe ? "selected" : ""}>${isAr ? "عشوائي" : "Random"}</option>`] : []),
            ...tribes.map(tr => `<option value="${tr}" ${!p.randomTribe && p.tribe === tr ? "selected" : ""}>${tr}</option>`)
        ].join("");
        const currentTeam = p.team ?? 0;
        const teamOptions = [
            `<option value="0" ${currentTeam === 0 ? "selected" : ""}>${isAr ? "بدون فريق" : "No team"}</option>`,
            ...[1, 2, 3, 4].map(tm => `<option value="${tm}" ${currentTeam === tm ? "selected" : ""}>${isAr ? `فريق ${tm}` : `Team ${tm}`}</option>`)
        ].join("");

        return `<div class="rank-row lobby-player-row ${p.ready ? "ready-row" : ""}">
            <span>${p.ready ? "✓" : "○"}</span>
            <div class="lobby-player-info">
                <strong><span class="lobby-player-color" style="background:${esc(p.flagColor ?? "#94a3b8")}"></span>${p.id === s.hostId ? "👑 " : ""}${p.isBot ? "🤖 " : ""}${esc(p.name)}</strong>
                <div class="lobby-selectors">
                    <select data-lobby-tribe="${p.id}" ${!canEdit ? "disabled" : ""}>${tribeOptions}</select>
                    <select data-lobby-team="${p.id}" ${!canEdit ? "disabled" : ""}>${teamOptions}</select>
                </div>
            </div>
            <small>${p.isBot ? "بوت" : p.online ? "متصل" : "بعيد"}</small>
        </div>`;
    }).join("")}</div>
    ${(s.spectators?.length ?? 0) > 0 ? `<div class="muted" style="margin-top:6px;">👁 ${isAr ? "المشاهدون:" : "Spectators:"} ${s.spectators!.map(sp => esc(sp.name)).join(", ")}</div>` : ""}
    <div class="panel-footer">${s.isSpectator
        ? `${button(isAr ? "الانتقال كلاعب" : "Join as Player", "switch-to-player", "", "primary wide")}${button("مغادرة الروم", "leave", "", "quiet wide")}`
        : `${button(youPlayer?.ready ? "إلغاء الجاهزية" : "جاهز للعب ✓", "ready", "", "wide")}${isHost ? button("بدء المباراة", "start", "", "primary wide") : ""}${button(isAr ? "مشاهدة 👁" : "Spectate 👁", "switch-to-spectator", "", "quiet wide")}${button("مغادرة الروم", "leave", "", "quiet wide")}`
    }</div>`;
} function patchPanel(target: Node, source: Node) {
    const wanted = Array.from(source.childNodes);
    wanted.forEach((next, i) => {
        const current = target.childNodes[i];
        if (!current) { target.appendChild(next.cloneNode(true)); return; }
        if (current.nodeType !== next.nodeType || current.nodeName !== next.nodeName) { target.replaceChild(next.cloneNode(true), current); return; }
        if (current instanceof Element && next instanceof Element) {
            for (const attr of Array.from(current.attributes)) if (!next.hasAttribute(attr.name)) current.removeAttribute(attr.name);
            for (const attr of Array.from(next.attributes)) if (current.getAttribute(attr.name) !== attr.value) current.setAttribute(attr.name, attr.value);
            patchPanel(current, next);
            if (current instanceof HTMLSelectElement && next instanceof HTMLSelectElement && document.activeElement !== current) {
                current.value = next.value;
            }
        } else if (current.nodeValue !== next.nodeValue) current.nodeValue = next.nodeValue;
    });
    while (target.childNodes.length > wanted.length) target.lastChild!.remove();
}
function patchHtml(container: HTMLElement, html: string) {
    const key = `${currentLanguage()}|${html}`;
    if (container.dataset.htmlKey === key) return;
    container.dataset.htmlKey = key;
    const temp = document.createElement('div');
    temp.innerHTML = html;
    localizeNow(temp);
    patchPanel(container, temp);
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
    if (panel === "alerts") {
        title = "Important alerts"; kicker = "YOUR KINGDOM"; html = alertInbox.panel(state?.world?.alerts ?? []);
    } else if (panel === "biome") {
        const region = state?.world?.regions?.find(r => String(r.id) === panelId);
        title = (region?.kind ?? "Meadow"); kicker = "BIOME INFO";
        const ground = selection?.kind === "ground";
        const city = state?.world?.warfare?.settlements.find(c => c.id === state?.economy?.villageId);
        html = region ? `<div class="biome-preview biome-${region.kind.toLowerCase()}"><span>${region.kind === 'Highlands' ? '&#9968;' : region.kind === 'Woodland' ? '&#9827;' : region.kind === 'Desert' ? '&#9728;' : '&#10048;'}</span></div><h3>Regional production bonuses</h3><div class="biome-bonuses">${(['wood', 'iron', 'stone', 'wheat'] as const).map(k => `<div>${icon(k)}<strong class="positive">+${Math.round((region[k] - 1) * 100)}%</strong><span>${k}</span></div>`).join('')}</div><p class="muted">This village benefits from its surrounding terrain.</p><div class="panel-footer">${ground ? button('March here', 'plan-attack', '', 'primary wide', !running()) : ''}${ground ? button('Camp here', 'plan-camp', '', 'primary wide', !running()) : ''}${ground && city?.tribe === 'Mongols' ? button(city.relocateTarget ? 'Relocating...' : 'Relocate village here', 'relocate', '', 'quiet wide', !running() || city.besieged || !!city.relocateTarget || city.relocateIn > 0) : ''}</div>` : '<p class="muted">No region selected.</p>';
    } else if (panel === "muster") {
        title = "Your forces"; kicker = "KINGDOM MUSTER";
        setTabs([{ id: "overview", name: "Troops" }, { id: "food", name: "Food supply" }]);
        html = musterPanel(state?.world?.muster, page, panelTab);
    } else if (panel === "production") {
        title = pretty(panelId[0]!.toUpperCase() + panelId.slice(1)); kicker = "PRODUCTION";
        html = productionPanel(state?.economy?.production?.find(p => p.resource.toLowerCase() === panelId), state?.economy?.oasisBonuses[["wood", "iron", "stone", "wheat"].indexOf(panelId)] ?? 0, state?.economy?.ownedOases ?? 0);
    } else if (panel === "reports") {
        title = "Reports"; kicker = "YOUR CAMPAIGN CHRONICLE";
        setTabs([{ id: "all", name: "All" }, { id: "defense", name: "Defense" }, { id: "attack", name: "Attack" }]);
        html = reportsPanel(state?.world?.reports ?? [], state!.you, owner, page, panelTab === "overview" ? "all" : panelTab, unreadReport);
    } else if (panel === "report") {
        title = "Battle report"; kicker = "LIVE INTELLIGENCE";
        const report = state?.world?.reports?.find(r => r.id === panelId);
        if (report) { readReport(report); renderIntelligence(); if (!liveReport(report)) kicker = "CAMPAIGN RECORD"; }

        const field = state?.world?.warfare?.battles.find(b => b.id === panelId), os = state?.world?.oasisSieges?.find(b => b.id === panelId);
        const citySiege = state?.world?.warfare?.sieges.find(s => s.id === panelId && ["Active"].includes(s.status));
        const publicEncounter = field ? { attackers: field.attackerTroops, defenders: field.defenderTroops, round: field.round } : os ? { attackers: state?.world?.armies.filter(a => (os.armyIds ?? []).includes(a.id)).reduce((n, a) => n + a.soldiers, 0) ?? 0, defenders: state?.world?.oases.find(o => o.id === os.oasisId)?.defenders ?? 0, round: os.round, wall: os.wall, maximum: os.wallMaximum } : citySiege ? { attackers: state?.world?.armies.find(a => a.id === citySiege.armyId)?.soldiers ?? 0, wall: citySiege.wall, maximum: 100 } : undefined;
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

        const you = state?.you ?? "";
        const stationedArmies = (oasis && state?.world?.armies)
            ? state.world.armies.filter(a => a.defendingSiteId === oasis.id && (a.order === "Guarding" || a.order === "Battle"))
            : [];
        const ownStationed = stationedArmies.filter(a => a.ownerId === you);
        const controllableOwn = ownStationed.find(a => a.order === "Guarding") ?? null;
        const ownUnitCounts: Record<string, number> = {};
        for (const a of ownStationed) {
            for (const [unitId, count] of Object.entries(a.formation ?? {})) {
                if (count > 0) ownUnitCounts[unitId] = (ownUnitCounts[unitId] ?? 0) + count;
            }
        }
        const ownUnits = Object.entries(ownUnitCounts).sort(([a], [b]) => compareUnitIds(a, b));
        const alliedForces = stationedArmies
            .filter(a => a.ownerId !== you)
            .map(a => ({
                ownerId: a.ownerId,
                ownerName: owner(a.ownerId),
                units: Object.entries(a.formation ?? {}).filter(([, n]) => n > 0).sort(([x], [y]) => compareUnitIds(x, y)),
                totalSoldiers: a.soldiers + a.wagons
            }));
        const garrisonView: OasisGarrisonView = {
            ownArmyId: controllableOwn?.id ?? null,
            ownUnits,
            alliedForces,
            selectedUnits: oasis && oasisUnitSelection?.oasisId === oasis.id ? oasisUnitSelection.units : []
        };

        const protectedEnemy = !!oasis?.ownerId && oasis.ownerId !== state?.you && (state?.shieldRemainingSeconds ?? 0) > 0;
        html = oasis && state ? oasisPanel(oasis, state.you, owner(oasis.ownerId), running(), protectedEnemy,
            (state.world?.warfare?.settlements ?? []).filter(c => c.ownerId === state!.you && !c.ruined).length > 1,
            state.world?.warfare?.settlements.find(c => c.id === oasis.villageId)?.position,
            garrisonView) : '<p class="empty">Oasis not found.</p>';
    }
    else if (panel === "village") {
        title = owner(state!.world?.warfare?.settlements.find(c => c.id === panelId)?.ownerId) + "'s village";
        kicker = "SETTLEMENT";
        html = villagePanel();
    }
    else if (panel === "army") {
        const info = armyTroopMap(panelId);
        const isAr = currentLanguage() === "ar";
        if (panelId === "multi" || (selectedArmies.length > 1 && panelId === selectedArmies[0])) {
            title = isAr ? `جيوش محددة (${selectedArmies.length})` : `Selected armies (${selectedArmies.length})`;
            kicker = isAr ? "القيادة الميدانية" : "FIELD COMMAND";
        } else if (info) {
            title = info.name;
            kicker = info.isHome ? (isAr ? "حامية القرية" : "GARRISON COMMAND") : (isAr ? "القيادة الميدانية" : "FIELD COMMAND");
        } else {
            const foreign = state?.world?.armies.find(a => a.id === panelId);
            title = foreign ? (isAr ? `جيش ${owner(foreign.ownerId)}` : `${owner(foreign.ownerId)}'s army`) : "Expedition";
            kicker = "FIELD COMMAND";
        }
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
        html = ownArmies().map((a, i) => button(`<span><b>Army ${i + 1} · ${a.soldiers} troops</b><small>${a.order}</small></span><span>→</span>`, "army", a.id, "list-button")).join("") || '<p class="empty">Your soldiers are at home.<br/>Raise an expedition to explore.</p>';
        html += `<div class="panel-footer">${button("Raise expedition", "rally", "", "primary wide", !running() || !state!.economy)}</div>`;
    }
    else if (panel === "castle") {
        const isAr = currentLanguage() === "ar";
        title = isAr ? "قلعة التاج" : "Crown Keep";
        kicker = isAr ? "المعركة الحاسمة" : "THE CROWN OF THE VALLEY";
        html = castlePanel();
    }
    else if (panel === "standings") {
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
        const isAr = currentLanguage() === "ar";
        title = isAr ? "ساحات القتال والغرف" : "Realms & Lobbies";
        kicker = isAr ? "اللعب الجماعي والمشاهدة" : "MULTIPLAYER";
        const roomCards = roomList.map(r => {
            const full = r.humans + r.bots >= r.capacity;
            const canJoin = r.status === "Lobby" && !full;
            const canWatch = r.status === "Lobby" || r.status === "Running";
            const isLive = r.status === "Running";
            const statusText = isLive ? (isAr ? "جارية الآن" : "Live") : r.status === "Lobby" ? (isAr ? "في الانتظار" : "Lobby") : r.status;
            const speedBadge = (r.speedMultiplier ?? 1) > 1 ? `<span class="room-pill speed">x${r.speedMultiplier}</span>` : "";
            const joinLabel = canJoin
                ? (isAr ? "دخول الغرفة" : "Join")
                : isLive
                    ? (isAr ? "بدأت المباراة" : "Started")
                    : (isAr ? "ممتلئة" : "Full");
            return `<article class="room-card">
                <div class="room-card-top">
                    <span class="room-card-name">${r.isBotOnly ? "🤖 " : "⚔ "}${esc(r.name)}</span>
                    <div class="room-card-badges">
                        ${speedBadge}
                        <span class="room-pill ${isLive ? "live" : "lobby"}">${esc(statusText)}</span>
                    </div>
                </div>
                <div class="room-card-meta">
                    <span>👤 ${r.humans} ${isAr ? "لاعب" : "humans"}</span>
                    <span>🤖 ${r.bots} ${isAr ? "بوت" : "AI"}</span>
                    <span>🪑 ${r.humans + r.bots} / ${r.capacity}</span>
                    ${(r.spectators ?? 0) > 0 ? `<span>👁 ${r.spectators} ${isAr ? "مشاهد" : "watching"}</span>` : ""}
                </div>
                <div class="room-card-actions">
                    ${button(isAr ? "👁 مشاهدة" : "👁 Watch", "spectate", r.id, "quiet", !canWatch)}
                    ${button(joinLabel, "join", r.id, "primary", !canJoin)}
                </div>
            </article>`;
        }).join("") || `<div class="rooms-empty">${isAr ? "لا توجد غرف مفتوحة حالياً. أنشئ غرفة جديدة لتبدأ المعركة." : "No open realms yet. Create one to begin."}</div>`;
        html = `<div class="rooms-browser">
            <section class="rooms-create-card">
                <div class="rooms-create-grid">
                    <label>${isAr ? "اسم الغرفة" : "Realm name"}<input id="room-name" maxlength="32" value="Sunmeadow friends"/></label>
                    <label>${isAr ? "عدد البوتات (0-7)" : "AI neighbors"}<input id="create-bots" type="number" min="0" max="7" value="6"/></label>
                </div>
                ${button(isAr ? "إنشاء غرفة جديدة" : "Create realm", "create", "", "primary wide")}
            </section>
            <div class="rooms-list-header">
                <span class="rooms-list-title">${isAr ? "الغرف المتاحة" : "Available Realms"} <span class="rooms-count-badge">${roomList.length}</span></span>
                ${button(isAr ? "تحديث" : "Refresh", "refresh", "", "quiet")}
            </div>
            <div class="rooms-list">${roomCards}</div>
        </div>`;
    }
    else {
        const isAr = currentLanguage() === "ar";
        title = selection?.name ?? "Open meadow";
        kicker = panel.toUpperCase();
        const convoy = state?.world?.convoys.find(c => c.id === panelId);
        const canSeeCargo = !!convoy && (convoy.ownerId === state!.you || isAllyPlayer(convoy.ownerId) || (!!convoy.recipientId && (convoy.recipientId === state!.you || isAllyPlayer(convoy.recipientId))));
        const friendlyConvoy = !!convoy && (convoy.ownerId === state!.you || isAllyPlayer(convoy.ownerId));
        const convoyCargo: Resources = convoy ? {
            wood: convoy.cargo?.wood ?? 0,
            iron: convoy.cargo?.iron ?? 0,
            stone: convoy.cargo?.stone ?? 0,
            wheat: (convoy.cargo?.wheat ?? 0) + Math.floor(convoy.wheat ?? 0)
        } : { wood: 0, iron: 0, stone: 0, wheat: 0 };
        const convoyKindLabel = convoy ? ({
            Supply: isAr ? "قافلة إمداد" : "Supply convoy",
            Trade: isAr ? "قافلة تجارية" : "Trade caravan",
            Oasis: isAr ? "نقل موارد الواحة" : "Oasis transport",
            Shipment: isAr ? "إرسال موارد" : "Resource shipment"
        }[convoy.kind] ?? convoy.kind) : "";
        const convoyMeta = convoy
            ? `<div class="stat-line"><span>${isAr ? "المالك" : "Owner"}</span><b>${esc(owner(convoy.ownerId))}</b></div>
               <div class="stat-line"><span>${esc(convoyKindLabel)}</span><b>${convoy.wagons} ${isAr ? "عربة" : "wagons"} · ${convoy.escorts} ${isAr ? "حراسة" : "escorts"}</b></div>
               ${convoy.recipientId ? `<div class="stat-line"><span>${isAr ? "المستلم" : "Recipient"}</span><b>${esc(owner(convoy.recipientId))}</b></div>` : ""}
               ${convoy.returning ? `<div class="stat-line"><span>${isAr ? "الحالة" : "Status"}</span><b>${isAr ? "عائدة للقرية" : "Returning"}</b></div>` : ""}
               ${canSeeCargo ? renderLootRow(convoyCargo, isAr ? "الموارد المحمولة" : "Carried resources") : ""}`
            : "";
        const ownCity = state?.world?.warfare?.settlements.find(c => c.id === state?.economy?.villageId && c.tribe === "Mongols");
        html = `<div class="panel-hero">${buildingArt("Warehouse")}</div><div class="stat-line"><span>${isAr ? "الإحداثيات" : "Coordinates"}</span><b>${Math.round(selection?.position.x ?? 0)} : ${Math.round(selection?.position.y ?? 0)}</b></div>${convoyMeta}<div class="panel-footer">${friendlyConvoy ? "" : button(panel === "convoy" ? "Intercept convoy" : "March here", "plan-attack", panelId, "primary wide", !running() || !!convoy && (convoy.ownerId === state!.you || convoy.inBattle || state!.shieldRemainingSeconds > 0))}${friendlyConvoy ? "" : button("Camp here", "plan-camp", "", "primary wide", !running())}${panel === "ground" && ownCity ? button(ownCity.relocateTarget ? "Relocating..." : "Relocate village here", "relocate", "", "quiet wide", !running() || ownCity.besieged || !!ownCity.relocateTarget || ownCity.relocateIn > 0) : ""}</div>`;
    }
    el("drawer-title").textContent = title;
    el("drawer-kicker").textContent = kicker;
    el("drawer").dataset.panel = panel ?? "";
    el("drawer").dataset.buildingName = panel === "building" ? state?.economy?.targets.find(t => t.id === panelId)?.name ?? "" : "";
    const report = panel === 'report' ? state?.world?.reports?.find(r => r.id === panelId) : null;
    el('drawer').style.setProperty('--report-width', `${Math.min(1080, Math.max(560, Math.min(9, Math.max(report?.attackerUnits?.length ?? 0, report?.defenderUnits?.length ?? 0)) * 74 + 150))}px`);
    const bodyKey = `${currentLanguage()}|${panel}|${panelId}|${html}`;
    if (force || drawerBody !== bodyKey) {
        const scroll = el("drawer-body").scrollTop;
        const body = el("drawer-body");
        if (!force) {
            const template = document.createElement('template'); template.innerHTML = html;
            patchPanel(body, template.content);
        } else body.innerHTML = html;
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
function updateTotal() {
    const t = state?.economy?.targets.find(t => t.id === selectedUnit); if (!t || !el("training-total"))
        return; el("training-total").innerHTML = costs(t.trainingCost, amount); el("total-quantity").textContent = String(amount); el("training-duration").textContent = time((t.trainingSeconds ?? 0) * amount); const train = document.querySelector<HTMLButtonElement>('[data-action="train"]'); if (train) {
            const laneJobs = state!.economy!.jobs.filter(j => j.lane === (t.trainingBuilding ?? facility(unitId(t))));
            train.textContent = laneJobs.length ? `Queue ${amount} recruits` : `Train ${amount} recruits`;
            train.disabled = !running() || !t.unlocked || !affordable(t.trainingCost, amount) || laneJobs.length >= 12;
        }
}
async function launch() {
    if (!state?.economy || !selection) return;
    if (!objectiveAllowed()) throw Error("The selected army lacks the required specialist.");
    const chosen = Object.fromEntries(Object.entries(formation).filter(([, n]) => n > 0));
    if (!Object.keys(chosen).length) throw Error("Choose at least one unit.");
    let id = dispatchArmy, ordered = false;
    const kind = selection.kind, city = state.world?.warfare?.settlements.find(c => c.id === selection?.id);
    if (id === "new") {
        const before = new Set(ownArmies().map(a => a.id));
        ordered = dispatchMission === "camp" || dispatchMission !== "scout" && (dispatchMission === "reinforce" || kind === "oasis" || kind === "village" || kind === "castle");
        if (!await command("DispatchArmy", state.economy.villageId, {
            formation: chosen, wagons: 0, enabled: dispatchAuto, escorts: 0, x: selection.position.x, y: selection.position.y, afterAttack,
            objective: attackObjective, destinationId: dispatchMission === "camp" ? null : ordered ? selection.id : null, reinforcement: dispatchMission === "reinforce", oasisMode: dispatchMission === 'burn' ? 'Burn' : dispatchMission === 'blockade' ? 'Blockade' : 'Capture', camp: dispatchMission === 'camp', loadedOnly: campLoadedOnly
        })) return;
        id = ownArmies().find(a => !before.has(a.id))?.id ?? '';
    }
    if (!id) return;
    const action = dispatchMission === "camp" ? "CampArmy" : dispatchMission === 'scout' ? 'Scout' : dispatchMission === 'reinforce' ? 'Reinforce' : kind === 'castle' ? 'CaptureCastle' : kind === 'village' && city?.ownerId !== state.you ? 'BeginSiege' : kind === 'army' && selection.ownerId !== state.you ? 'AttackArmy' : kind === 'convoy' ? 'InterceptConvoy' : kind === 'oasis' ? (dispatchMission === 'burn' ? 'BurnOasis' : dispatchMission === 'blockade' ? 'BesiegeOasis' : 'CaptureOasis') : 'MoveArmy';
    if (ordered || await command(action, id, { destinationId: dispatchMission === "camp" || kind === "ground" ? null : selection.id, x: selection.position.x, y: selection.position.y, afterAttack, objective: attackObjective, formation: chosen, loadedOnly: campLoadedOnly })) {
        activeArmy = id; loadArmy(); openPanel('army', id);
    }
}
document.addEventListener("input", event => {
    const input = event.target as HTMLInputElement;
    if (input.id.startsWith("transfer-") && input.id !== "transfer-source") {
        const resource = input.id.slice(9) as keyof Resources;
        if (resource in transferDraft) transferDraft[resource] = Math.max(0, Math.min(1000000, Math.floor(Number(input.value) || 0)));
    }
    if (input.dataset.lobbyTribe) {
        void perform(async () => { render(await connection.invoke<Snapshot>("ChangeTribe", input.dataset.lobbyTribe, input.value)); });
        return;
    }
    if (input.dataset.lobbyTeam) {
        void perform(async () => { render(await connection.invoke<Snapshot>("ChangeTeam", input.dataset.lobbyTeam, Number(input.value))); });
        return;
    }
    if (input.id === 'market-offer-amount') marketDraft.offerAmount = Math.floor(Number(input.value));
    if (input.id === 'market-ask-amount') marketDraft.askAmount = Math.floor(Number(input.value));
    if (input.dataset.formation) {
        formation[input.dataset.formation] = Math.min(Number(input.max), Math.max(0, Math.floor(Number(input.value) || 0)));
        const quickBtn = input.parentElement?.querySelector<HTMLButtonElement>('[data-action="formation-quick"]');
        if (quickBtn) { delete quickBtn.dataset.quickStage; quickBtn.textContent = "½"; }
        renderProvisions();
        return;
    }
    if (input.dataset.inspectorUnit && input.dataset.armyId) {
        const armId = input.dataset.armyId, unitId = input.dataset.inspectorUnit;
        const val = Math.min(Number(input.max), Math.max(0, Math.floor(Number(input.value) || 0)));
        (armyDrafts[armId] ??= {})[unitId] = val;
        editedDrafts.add(`${armId}|${unitId}`);
        resetDisbandConfirm();
        const quickBtn = input.parentElement?.querySelector<HTMLButtonElement>('[data-action="inspector-quick"]');
        if (quickBtn) { delete quickBtn.dataset.quickStage; quickBtn.textContent = "½"; }
        const info = armyTroopMap(armId);
        if (info) refreshInspectorFooter(info);
        return;
    } if (input.id === "train-range" || input.id === "train-amount") {
        amount = Math.max(1, Math.min(100, Math.floor(Number(input.value) || 1)));
        const other = el<HTMLInputElement>(input.id === "train-range" ? "train-amount" : "train-range");
        if (other)
            other.value = String(amount);
        updateTotal();
    } if (input.id === "supply-wagons")
        supplyWagons = Number(input.value); if (input.id === "supply-escorts")
        escorts = Number(input.value);
});
document.addEventListener("change", event => {
    const input = event.target as HTMLInputElement, key = input.id; if (key === "transfer-source") transferSource = input.value; if (key === "market-offer-resource") marketDraft.offerResource = input.value; if (key === "market-ask-resource") marketDraft.askResource = input.value; if (key.startsWith("market-")) renderDrawer(true); if (key === "after-attack") { afterAttack = input.value; if (panel === "dispatch") renderDrawer(true); } if (key === "after-attack" && panel === "army" && army()) void perform(async () => { await command("SetAfterAttack", army()!.id, { afterAttack }); }); if (key === "supply-source")
        supplySource = input.value; if (key === "dispatch-army") {
            dispatchArmy = input.value;
            page = 0; loadDispatchFormation(); highlightDispatchArmy(); renderDrawer(true);
        } if (key === "supply-auto")
        autoSupply = input.checked; if (key === "dispatch-auto")
        dispatchAuto = input.checked; if (key === "camp-loaded-only")
        campLoadedOnly = input.checked; if (key === "province")
        void perform(async () => { await command("SelectVillage", input.value); openPanel("building", state!.economy!.targets.find(t => t.name === "MainHall")!.id, "govern"); });
});
let pressedRadialBtn: { id: RadialActionId; rect: DOMRect } | null = null;
let suppressNextRadialClick = false;
el("radial-menu").addEventListener("pointerdown", event => {
    if (event.button !== 0) return;
    const btn = (event.target as Element).closest<HTMLButtonElement>('button[data-action="radial-cmd"]');
    if (!btn || btn.disabled || !btn.dataset.id) { pressedRadialBtn = null; return; }
    pressedRadialBtn = { id: btn.dataset.id as RadialActionId, rect: btn.getBoundingClientRect() };
});
el("radial-menu").addEventListener("contextmenu", event => {
    event.preventDefault();
    pressedRadialBtn = null;
    closeRadial();
    map.triggerRightClick(event.clientX, event.clientY);
});
document.addEventListener("pointerup", event => {
    if (event.button !== 0 || !pressedRadialBtn) return;
    const pending = pressedRadialBtn;
    pressedRadialBtn = null;
    if (!radialState) return;
    const pad = 18;
    const hit = event.clientX >= pending.rect.left - pad && event.clientX <= pending.rect.right + pad
        && event.clientY >= pending.rect.top - pad && event.clientY <= pending.rect.bottom + pad;
    if (!hit) return;
    suppressNextRadialClick = true;
    window.setTimeout(() => { suppressNextRadialClick = false; }, 0);
    const target = radialState.target;
    void perform(() => executeRtsOrder(pending.id, target));
}, true);
document.addEventListener("pointercancel", () => { pressedRadialBtn = null; }, true);
document.addEventListener("click", event => {
    if (suppressNextRadialClick) {
        suppressNextRadialClick = false;
        return;
    }
    const targetEl = event.target as Element;
    if (radialState && !targetEl.closest("#radial-menu")) closeRadial();
    if (targetEl.closest(".unit-help,#unit-tooltip")) return;
    const choice = targetEl.closest<HTMLButtonElement>('[data-choice]');
    if (choice) { const input = el<HTMLInputElement>(choice.dataset.choice!); input.value = choice.dataset.value!; choice.closest('.choice-grid')?.querySelectorAll<HTMLButtonElement>('button').forEach(b => { const active = b === choice; b.classList.toggle('active', active); b.setAttribute('aria-pressed', String(active)); b.querySelector('.choice-check')!.innerHTML = active ? '&#10003;' : ''; }); input.dispatchEvent(new Event('change', { bubbles: true })); return; }
    const b = targetEl.closest<HTMLButtonElement>("button");
    if (!b || b.disabled)
        return;
    const action = b.dataset.action, id = b.dataset.id ?? "";
    if (!action)
        return;
    if (action === "radial-cmd") {
        if (radialState) {
            const target = radialState.target;
            void perform(() => executeRtsOrder(id as RadialActionId, target));
        }
        return;
    }
    if (action === "oasis-select-unit") {
        const oasisId = b.dataset.oasisId ?? "";
        const armyId = b.dataset.armyId ?? "";
        const info = armyTroopMap(armyId);
        if (!info || !oasisId) return;
        const multi = event.shiftKey || event.ctrlKey || event.metaKey;
        const current = (oasisUnitSelection?.oasisId === oasisId && oasisUnitSelection.armyId === armyId) ? oasisUnitSelection.units : [];
        let next: string[];
        if (multi) {
            next = current.includes(id) ? current.filter(u => u !== id) : [...current, id];
        } else {
            next = (current.length === 1 && current[0] === id) ? [] : [id];
        }
        applyOasisUnitSelection(oasisId, armyId, next);
        return;
    }
    if (action === "oasis-select-all") {
        const oasisId = b.dataset.oasisId ?? "";
        const armyId = b.dataset.armyId ?? "";
        const info = armyTroopMap(armyId);
        if (!info || !oasisId) return;
        applyOasisUnitSelection(oasisId, armyId, Object.keys(info.troops));
        return;
    }
    if (action === "oasis-clear-units") {
        const oasisId = b.dataset.oasisId ?? "";
        const armyId = b.dataset.armyId ?? "";
        applyOasisUnitSelection(oasisId, armyId, []);
        return;
    }
    if (action === "toggle-army") {
        selectArmies([id], "toggle", false);
        return;
    }
    if (action === "clear-armies") {
        selectArmies([], "clear", false);
        return;
    }
    if (action === "inspector-solo") {
        selectArmies([id], "replace", true);
        return;
    }
    if (action === "inspector-locate") {
        const info = armyTroopMap(id);
        if (info) map.focus(info.position);
        return;
    }
    if (action === "inspector-quick") {
        const armId = b.dataset.armyId ?? "";
        const max = Math.max(0, Number(b.dataset.max ?? 0));
        const half = max > 0 ? Math.max(1, Math.floor(max / 2)) : 0;
        const isSecondClick = b.dataset.quickStage === "half";
        const next = isSecondClick ? max : half;
        b.dataset.quickStage = isSecondClick ? "all" : "half";
        b.textContent = isSecondClick ? "½" : (currentLanguage() === "ar" ? "الكل" : "All");
        (armyDrafts[armId] ??= {})[id] = next;
        editedDrafts.add(`${armId}|${id}`);
        resetDisbandConfirm();
        const input = b.parentElement?.querySelector<HTMLInputElement>(`input[data-inspector-unit="${id}"][data-army-id="${armId}"]`);
        if (input) input.value = String(next);
        const info = armyTroopMap(armId);
        if (info) refreshInspectorFooter(info);
        return;
    }
    if (action === "inspector-clear" || action === "inspector-all") {
        const info = armyTroopMap(id);
        if (!info) return;
        const draft = (armyDrafts[info.id] ??= {});
        for (const [u, max] of Object.entries(info.troops)) {
            if (max <= 0) continue;
            const next = action === "inspector-all" ? max : 0;
            draft[u] = next;
            editedDrafts.add(`${info.id}|${u}`);
            const input = el("drawer").querySelector<HTMLInputElement>(`input[data-inspector-unit="${u}"][data-army-id="${info.id}"]`);
            if (input) input.value = String(next);
            const quickBtn = input?.parentElement?.querySelector<HTMLButtonElement>('[data-action="inspector-quick"]');
            if (quickBtn) { delete quickBtn.dataset.quickStage; quickBtn.textContent = "½"; }
        }
        resetDisbandConfirm();
        refreshInspectorFooter(info);
        return;
    }
    if (action === "inspector-create-army") {
        void perform(() => splitOrCreateArmy(id));
        return;
    }
    if (action === "inspector-disband") {
        void perform(() => disbandSelectedTroops(id));
        return;
    }
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
    if (action === 'attack-objective') { if (objectiveAllowed(id)) { attackObjective = id; renderProvisions(); } return; }
    if (action === 'event-army') { const a = state?.world?.armies.find(a => a.id === id); if (a) { selection = { ...a, kind: 'army', name: owner(a.ownerId) }; map.focus(a.position); if (a.ownerId === state?.you) { selectArmies([id], "replace", false); } else { openPanel('army', id); } } return; }
    if (action === 'dismiss-alert' || action === 'read-alerts') { for (const a of state?.world?.alerts ?? []) if (action === 'read-alerts' || a.id === Number(id)) alertInbox.dismiss(a.id); renderImportantAlerts(); if (panel === 'alerts') renderDrawer(true); return; }
    if (action === 'all-alerts') { openPanel('alerts'); return; }
    if (action === 'important-alert') {
        const a = state?.world?.alerts?.find(a => a.id === Number(id)); if (!a) return;
        const oasis = state?.world?.oases.find(o => o.id === a.targetId), city = state?.world?.warfare?.settlements.find(c => c.id === a.targetId), convoy = state?.world?.convoys.find(c => c.id === a.targetId);
        alertInbox.dismiss(a.id); renderImportantAlerts();
        if (oasis) { selection = { ...oasis, kind: 'oasis' }; map.focus(oasis.position); map.select(selection); openPanel('oasis', oasis.id); }
        else if (city) { selection = { ...city, kind: 'village', name: owner(city.ownerId) }; map.focus(city.position); openPanel('village', city.id); }
        else if (convoy) { selection = { ...convoy, kind: 'convoy', name: 'Caravan' }; map.focus(convoy.position); openPanel('convoy', convoy.id); }
        else openPanel('alerts'); return;
    }
    if (action === 'oasis-wall-info') { selection = state?.world?.oases.find(o => o.id === id) ? { ...state.world.oases.find(o => o.id === id)!, kind: 'oasis' } : null; openPanel('oasis', id); return; }
    if (action === 'focus-possession') {
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
        if (armyTroopMap(id)) {
            const multi = event.ctrlKey || event.metaKey || event.shiftKey;
            selectArmies([id], multi ? "toggle" : "replace", !multi);
            return;
        }
        const a = state?.world?.armies.find(x => x.id === id);
        if (a) {
            selection = { ...a, kind: 'army', name: owner(a.ownerId) };
            map.focus(a.position);
            map.select(selection);
            openPanel('army', a.id);
            renderArmiesStrip();
        }
        return;
    }
    if (action === 'plan-castle-attack') {
        const keep = state?.world?.warfare?.castle;
        if (keep) {
            selection = { id: keep.id, kind: 'castle', name: 'Crown Keep', position: keep.position };
            map.select(selection);
            openDispatch('attack');
        }
        return;
    }
    if (action === 'show-standings') { openPanel('standings'); return; }
    if (action === 'dismiss-victory') { dismissedVictoryMatchId = state?.id ?? ""; renderVictoryOverlay(null); return; }
    if (action === 'victory-standings') { dismissedVictoryMatchId = state?.id ?? ""; renderVictoryOverlay(null); openPanel('standings'); return; }
    if (action === 'plan-blockade') { openDispatch('blockade'); return; }
    if (action === 'plan-burn') { openDispatch('burn'); return; }
    if (action === 'plan-camp') { openDispatch('camp'); return; }
    if (action === 'scout-target') { openDispatch('scout'); return; }
    if (action === 'oasis-level') { void perform(async () => { const oasis = state?.world?.oases.find(o => o.id === id); if (!oasis) return; await command('UpgradeOasisLevel', id); }); return; }
    if (action?.startsWith('oasis-')) { void perform(async () => { const oasis = state?.world?.oases.find(o => o.id === id); if (!oasis) return; await command(action === 'oasis-auto' ? 'SetOasisTransport' : action === 'oasis-wall' ? 'UpgradeOasisWall' : 'CollectOasis', id, { enabled: !oasis.autoTransport }); }); return; }
    if (action === "unit-detail") { reportDetail = b.dataset.detail ?? ""; el("report-detail").textContent = reportDetail; return; }
    if (action === "production") { openPanel("production", id); return; }
    if (action === "report") {
        if (b.closest("#important-alerts")) {
            const w = state?.world;
            const pos = w?.warfare?.battles.find(x => x.id === id)?.position
                ?? w?.warfare?.sieges.find(x => x.id === id)?.position
                ?? w?.oasisSieges?.find(x => x.id === id)?.position
                ?? w?.reports?.find(x => x.id === id)?.position;
            if (pos) map.focus(pos);
        }
        openReport(id); renderIntelligence(); return;
    }
    if (action === "focus-player-pos") {
        const [x, y] = id.split(",").map(Number);
        if (x !== undefined && y !== undefined && Number.isFinite(x) && Number.isFinite(y)) map.focus({ x, y }, 2.4);
        return;
    }
    if (action === "show-reports") { openPanel("reports", "", "all"); return; }
    if (action === "show-armies") { openPanel("armies"); return; }
    if (action === "report-map") { const r = state?.world?.reports?.find(r => r.id === id); if (r) { showMap(); map.focus(r.position); } return; }
    if (action === "report-orders") {
        const os = state?.world?.oasisSieges?.find(s => s.id === id);
        if (os) { if (os.attackerOwners?.includes(state!.you)) { activeArmy = state?.world?.armies.find(a => (os.armyIds ?? []).includes(a.id) && a.ownerId === state?.you)?.id ?? os.armyId; loadArmy(); openPanel('army', activeArmy); } else { const oasis = state?.world?.oases.find(o => o.id === os.oasisId); if (oasis) { selection = { ...oasis, kind: 'oasis' }; openPanel('oasis', oasis.id); } } return; }
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
    if (action === 'battle') {
        const battle = state?.world?.warfare?.battles.find(b => b.attackerId === id || b.defenderId === id); if (battle) openReport(battle.id); return;
    }
    if (action === "army") {
        if (armyTroopMap(id)) {
            selectArmies([id], "replace", true);
        } else {
            activeArmy = id;
            loadArmy();
            openPanel("army", id);
        }
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
    if (action === "trade-convoy") { const convoy = state?.world?.convoys.find(c => c.id === id); if (convoy) { selection = { ...convoy, kind: "convoy", name: "Trade caravan" }; map.focus(convoy.position); openPanel("convoy", id); } return; }
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
        else if (action === 'market-post') await command('PostMarketOffer', state!.economy!.villageId, marketDraft as unknown as Record<string, unknown>);
        else if (action === 'market-accept') await command('AcceptMarketOffer', state!.economy!.villageId, { destinationId: id });
        else if (action === 'market-cancel') await command('CancelMarketOffer', id);
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
        else if (action === "recall") {
            if (id === "multi") {
                for (const selId of selectedArmies.filter(x => !x.startsWith("home:"))) await command("RecallArmy", selId);
            } else {
                await command("RecallArmy", id);
            }
        }
        else if (action === "merge-armies") {
            const { homeIds, fieldArmies, canMerge } = mergeSelectionInfo();
            if (!canMerge) return;
            if (homeIds.length === 1) {
                await command("MergeArmies", homeIds[0]!, { targetIds: fieldArmies.map(a => a.id) });
            } else if (fieldArmies.length >= 2) {
                await command("MergeArmies", fieldArmies[0]!.id, { targetIds: fieldArmies.map(a => a.id) });
            }
        }
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
        else if (action === "create") {
            const initialTribe = el("tribe-picker").hidden ? "Random" : el<HTMLInputElement>("tribe").value;
            const createBots = Math.max(0, Math.min(7, Math.floor(Number((document.getElementById("create-bots") as HTMLInputElement | null)?.value ?? 6) || 0)));
            render(await connection.invoke<Snapshot>("CreateRoom", el<HTMLInputElement>("player-name").value, initialTribe, el<HTMLInputElement>("room-name").value, { capacity: 8, durationMinutes: 60, castleUnlockMinutes: 45, shieldMinutes: 5, resourceSpeed: 1, botCount: createBots, botDifficulty: "Normal" }));
        }
        else if (action === "join") {
            const initialTribe = el("tribe-picker").hidden ? "Random" : el<HTMLInputElement>("tribe").value;
            render(await connection.invoke<Snapshot>("JoinRoom", id, el<HTMLInputElement>("player-name").value, initialTribe));
        }
        else if (action === "spectate") {
            render(await connection.invoke<Snapshot>("SpectateRoom", id, el<HTMLInputElement>("player-name").value || "Spectator"));
            el("welcome").hidden = true;
            closePanel();
        }
        else if (action === "switch-to-spectator") {
            render(await connection.invoke<Snapshot>("SwitchSpectator", true, "Random"));
        }
        else if (action === "switch-to-player") {
            const initialTribe = el("tribe-picker").hidden ? "Random" : el<HTMLInputElement>("tribe").value;
            render(await connection.invoke<Snapshot>("SwitchSpectator", false, initialTribe));
        }
        else if (action === "ready")
            render(await connection.invoke<Snapshot>("SetReady", !state!.players.find(p => p.id === state!.you)?.ready));
        else if (action === "lobby-slot")
            render(await connection.invoke<Snapshot>("ChangeSpawnSlot", state!.you, Number(id)));
        else if (action === "lobby-color")
            render(await connection.invoke<Snapshot>("ChangeColor", state!.you, id));
        else if (action === "start")
            render(await connection.invoke<Snapshot>("StartMatch"));
        else if (action === "settings") {
            const v = (id: string) => el<HTMLInputElement>(id).value;
            panelTab = "overview";
            render(await connection.invoke<Snapshot>("ConfigureRoom", { capacity: Number(v("capacity")), durationMinutes: Number(v("duration")), castleUnlockMinutes: Number(v("castle-unlock")), shieldMinutes: Number(v("shield")), resourceSpeed: Number(v("speed")), botCount: Number(v("bots")), botDifficulty: v("difficulty") }));
            notice(currentLanguage() === "ar" ? "تم حفظ إعدادات العالم." : "Realm settings updated.");
        }
        else if (action === "leave") {
            await connection.invoke("LeaveRoom");
            closePanel();
            render(null);
            roomList = await connection.invoke<RoomSummary[]>("ListRooms");
        }
    }, !!action && ["refresh", "create", "join", "spectate", "switch-to-spectator", "switch-to-player", "ready", "lobby-slot", "lobby-color", "start", "settings", "leave"].includes(action));
});
el('language-picker').innerHTML = select('language-select', '', [{ value: 'ar', label: 'AR' }, { value: 'en', label: 'EN' }], currentLanguage());
el('tribe-picker').innerHTML = select('tribe', 'Your people', ['Romans', 'Germans', 'Greeks', 'Mongols', 'Persians'], 'Romans');
el('language-select').onchange = () => setLanguage(el<HTMLInputElement>('language-select').value);
el<HTMLInputElement>('language-select').value = currentLanguage();
el('village-biome').onclick = () => { selection = null; openPanel('biome', el('village-biome').dataset.id ?? ''); };
window.addEventListener('languagechange', () => { map.refreshLanguage(); renderArmiesStrip(); renderDrawer(true); renderChat(); renderSpectatorBar(); adminOverlay.refreshLanguageAndState(); localizeNow(); });
window.addEventListener('keydown', event => {
    if (!state) {
        const active = document.activeElement;
        const inOtherInput = (active instanceof HTMLInputElement || active instanceof HTMLTextAreaElement || active instanceof HTMLSelectElement) && active.id !== "chat-input";
        const isBackquote = event.code === "Backquote" || event.key === "`";
        if (!inOtherInput && isBackquote && (active?.id !== "chat-input" || event.key === "`")) {
            event.preventDefault();
            menuChatVisible = !menuChatVisible;
            if (menuChatVisible) chatOpen = true;
            renderChat();
            if (menuChatVisible) window.setTimeout(() => el<HTMLInputElement>("chat-input").focus(), 0);
            return;
        }
        if (event.key === 'Escape' && menuChatVisible) {
            menuChatVisible = false;
            renderChat();
            return;
        }
    }
    if (event.key === 'F1') {
        event.preventDefault();
        if (gameGuide.isOpen()) gameGuide.close();
        else { gameGuide.setTribe(currentTribe()); gameGuide.open(); }
        return;
    }
    if (event.key !== 'Escape') return;
    if (gameGuide.isOpen()) { gameGuide.close(); return; }
    if (radialState) { closeRadial(); return; }
    if (selectedArmies.length) selectArmies([], 'clear', false);
});
document.getElementById("view-buildings")?.addEventListener("click", () => void perform(async () => {
    buildingOasisId = null;
    const primary = state?.players.find(p => p.id === state?.you)?.villageId;
    if (primary && state?.economy?.villageId !== primary) await command("SelectVillage", primary);
    closePanel(); renderBuildings();
}));
document.getElementById("view-world")?.addEventListener("click", () => showMap());
el("view-reports").onclick = () => openPanel("reports", "", "all");
el("muster-hud").onclick = () => openPanel("muster");
document.getElementById("view-armies")?.addEventListener("click", () => openPanel("armies"));
el("view-standings").onclick = () => openPanel("standings");
el("view-guide").onclick = () => { gameGuide.setTribe(currentTribe()); gameGuide.open(); };
el("welcome-guide").onclick = () => { gameGuide.setTribe(el<HTMLInputElement>("tribe")?.value); gameGuide.open(); };
el("realm-menu").onclick = () => gameMenu.open();
el("close-drawer").onclick = () => {
    closePanel(); if (!state)
        el("welcome").hidden = false;
};
el("zoom-in").onclick = () => map.zoom(.2);
el("zoom-out").onclick = () => map.zoom(-.2);
el("map-home").onclick = () => map.home();
el("map-biomes").onclick = () => { el("map-biomes").setAttribute("aria-pressed", String(map.toggleBoundaries())); };
el("toggle-debug").onclick = () => { debugOverlay.toggle(); };
document.getElementById("view-admin")?.addEventListener("click", () => { adminOverlay.toggle(); });
el("chat-toggle").onclick = () => { chatOpen = !chatOpen; renderChat(); };
el("chat-form").addEventListener("submit", event => {
    event.preventDefault();
    const input = el<HTMLInputElement>("chat-input"), message = input.value.trim();
    if (!message) return;
    if (/^\/enablecheat(?:\s|$)/i.test(message)) {
        const password = message.replace(/^\/enablecheat\s*/i, "").trim();
        input.value = "";
        void perform(async () => {
            await connection.invoke<boolean>("EnableCheat", password);
            adminEnabled = true;
            menuChatVisible = false;
            roomList = await connection.invoke<RoomSummary[]>("ListRooms");
            syncAdminOverlay();
            adminOverlay.setOpen(true);
            renderChat();
            notice(currentLanguage() === "ar" ? "تم تفعيل وضع الأدمن والشيت ⚡" : "Admin & Cheat mode enabled ⚡");
        }, true);
        return;
    }
    if (!state) return;
    void perform(async () => { if (await command("SendChat", "00000000-0000-0000-0000-000000000000", { message })) input.value = ""; });
});
el("multiplayer").onclick = () => void perform(async () => { roomList = await connection.invoke<RoomSummary[]>("ListRooms"); el("welcome").hidden = true; openPanel("rooms"); }, true);
el("play-solo").onclick = () => void perform(async () => { render(await connection.invoke<Snapshot>("CreateRoom", el<HTMLInputElement>("player-name").value, el<HTMLInputElement>("tribe").value, "Sunmeadow Valley", { capacity: 8, durationMinutes: 60, castleUnlockMinutes: 45, shieldMinutes: 5, resourceSpeed: 1, botCount: 7, botDifficulty: "Normal" })); await connection.invoke("SetReady", true); render(await connection.invoke<Snapshot>("StartMatch")); }, true);
el("play-tutorial").onclick = () => void perform(async () => {
    render(await connection.invoke<Snapshot>("CreateRoom", el<HTMLInputElement>("player-name").value, el<HTMLInputElement>("tribe").value, "Sunmeadow Valley (Tutorial)", { capacity: 4, durationMinutes: 30, castleUnlockMinutes: 20, shieldMinutes: 10, resourceSpeed: 2, botCount: 1, botDifficulty: "Easy" }));
    await connection.invoke("SetReady", true);
    render(await connection.invoke<Snapshot>("StartMatch"));
    gameTutorial.replay(buildTutorialContext());
}, true);
// Defer background Snapshot DOM patches only while a UI button press is in flight so map drags never stall world snapshots.
let pointerReleaseTimer = 0;
document.addEventListener("pointerdown", event => {
    const target = event.target instanceof Element ? event.target : null;
    if (!target || target.tagName === "CANVAS" || !target.closest("button, a, input, select, [data-action], [role='button']")) return;
    pointerPressed = true;
    window.clearTimeout(pointerReleaseTimer);
    pointerReleaseTimer = window.setTimeout(flushDeferredSnapshot, 1200);
}, true);
function flushDeferredSnapshot() {
    window.clearTimeout(pointerReleaseTimer);
    pointerReleaseTimer = 0;
    if (!pointerPressed) return;
    pointerPressed = false;
    if (pendingSnapshot) {
        const snap = pendingSnapshot;
        pendingSnapshot = null;
        if (!state || state.id === snap.id) render(snap);
    }
}
function releasePointer() {
    if (!pointerPressed) return;
    window.setTimeout(flushDeferredSnapshot, 30);
}
document.addEventListener("pointerup", releasePointer, true);
document.addEventListener("pointercancel", flushDeferredSnapshot, true);
document.addEventListener("contextmenu", flushDeferredSnapshot, true);
window.addEventListener("blur", flushDeferredSnapshot);
const supportsGzipDecompress = typeof DecompressionStream !== "undefined";
let snapshotSeq = 0;
let latestAppliedSnapshotSeq = 0;

async function decodeSnapshotGz(base64: string): Promise<Snapshot> {
    const bin = atob(base64);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream("gzip"));
    return JSON.parse(await new Response(stream).text()) as Snapshot;
}

function applyInboundSnapshot(snapshot: Snapshot, wireBytes?: number) {
    debugOverlay.recordInbound("Snapshot", snapshot, wireBytes);
    if (snapshot.status === "Finished" || snapshot.status === "Faulted") {
        roomList = roomList.filter(r => r.id !== snapshot.id);
        syncAdminOverlay();
        if (panel === "rooms") renderDrawer();
    }
    if (state?.id !== snapshot.id) return;
    if (pointerPressed) { pendingSnapshot = snapshot; deferredSnapshotsCount++; return; }
    render(snapshot);
}

connection.on("Snapshot", (snapshot: Snapshot) => {
    latestAppliedSnapshotSeq = ++snapshotSeq;
    applyInboundSnapshot(snapshot);
});
connection.on("SnapshotGz", (base64: string) => {
    const seq = ++snapshotSeq;
    void decodeSnapshotGz(base64).then(snapshot => {
        if (seq < latestAppliedSnapshotSeq) return;
        latestAppliedSnapshotSeq = seq;
        applyInboundSnapshot(snapshot, base64.length);
    }).catch(() => { /* Next periodic snapshot recovers cleanly. */ });
});
connection.on("RoomsUpdated", (rooms: RoomSummary[]) => {
    debugOverlay.recordInbound("RoomsUpdated", rooms);
    roomList = rooms.filter(r => r.status === "Lobby" || r.status === "Running");
    syncAdminOverlay();
    if (panel === "rooms") renderDrawer();
});
window.setInterval(async () => {
    if ((panel === "rooms" || adminOverlay.isOpen()) && connection.state === HubConnectionState.Connected && !busy) {
        try {
            roomList = (await connection.invoke<RoomSummary[]>("ListRooms")).filter(r => r.status === "Lobby" || r.status === "Running");
            syncAdminOverlay();
            if (panel === "rooms") renderDrawer();
        } catch { /* ignore background polling errors */ }
    }
}, 3000);
connection.onreconnecting(() => { gameAudio.reset(); status(); });
connection.onclose(() => { gameAudio.reset(); status(); });
connection.onreconnected(() => {
    debugOverlay.recordReconnect();
    status();
    void perform(async () => {
        if (supportsGzipDecompress) await connection.invoke("SetSnapshotCompression", true).catch(() => { /* Fall back to plain JSON snapshots. */ });
        const resumed = await connection.invoke<Snapshot | null>("Resume");
        if (resumed || state?.status !== "Finished") render(resumed);
    });
});
async function connect(resumeReserved = false) {
    if (connecting || connection.state !== HubConnectionState.Disconnected)
        return; connecting = true; el<HTMLButtonElement>('resume-game').disabled = true; try {
            const response = await fetch("/api/session", { method: "POST", credentials: "same-origin" });
            if (!response.ok)
                throw Error("Could not enter the realm.");
            await loadUnits();
            await connection.start();
            if (supportsGzipDecompress) await connection.invoke("SetSnapshotCompression", true).catch(() => { /* Fall back to plain JSON snapshots. */ });
            adminEnabled = await connection.invoke<boolean>("CheckAdmin");
            roomList = await connection.invoke<RoomSummary[]>("ListRooms");
            const snapshot = await connection.invoke<Snapshot | null>('Resume');
            debugOverlay.recordInbound("Resume", snapshot);
            if (returnedToMenu && !resumeReserved && snapshot?.status === 'Running') {
                await connection.stop(); render(null);
            } else { setReturnedToMenu(false); render(snapshot); }
            syncAdminOverlay();
            status();
        }
    catch (e) {
        notice(String(e), true);
        status();
    }
    finally {
        connecting = false;
        el<HTMLButtonElement>('resume-game').disabled = false;
    }
}
el("reconnect").onclick = () => void connect();
el('resume-game').onclick = () => void connect(true);
window.addEventListener("selectstart", event => {
    const target = event.target instanceof Element ? event.target : (event.target as Node | null)?.parentElement;
    if (!target?.closest("input, textarea, [contenteditable='true']")) event.preventDefault();
});
window.addEventListener("dragstart", event => event.preventDefault());
window.addEventListener("resize", () => { if (panel && ["muster", "reports", "report", "production"].includes(panel)) { page = 0; renderDrawer(true); } });
new ResizeObserver(entries => { for (const entry of entries) { const t = entry.target as HTMLElement; const name = entry.target.id === 'drawer' ? '--drawer-space' : '--events-height'; el('game').style.setProperty(name, `${entry.target.id === 'drawer' ? t.offsetWidth : t.offsetHeight}px`); } }).observe(el('drawer'));
new ResizeObserver(() => el('game').style.setProperty('--events-height', `${el('important-alerts').offsetHeight}px`)).observe(el('important-alerts'));
window.setInterval(renderTimers, 250);
void connect();
