import { armyFigureCount, armyFigures, armyFigureLayout, isDemolishing, isSmoking } from './army-visual';
import { BattleClash } from './battle-clash';
import { combatAudibility, type CombatSound } from './audio-events';
import { VillageFire, VillageSmoke } from './village-fire';
import {ArmyLayout} from './army-layout';
import { drawCamp } from './camp-art';
import { biomeAt, drawLandscape, previewRegions, type BiomeRegion, type Landscape } from "./landscape";
import { settlementArt, settlementStage } from "./settlements";
import { t, currentLanguage } from "./locale";
import type { Muster, Report, Cargo } from "./intelligence";
import type { MarketOffer } from "./market";
import { Application, Container, Graphics, Sprite, Text } from "pixi.js";
import { loadUnitArt, unitSprite } from './unit-art';
import { loadArt, oasisSprite } from "./art";

export interface Point { x: number; y: number }
export interface MapPlayer { tribe?: string; id: string; name: string; villageId: string | null; position: Point | null; level: number | null; ruined?: boolean; flagColor?: string }
export interface ArmyView { id: string; ownerId: string; homeVillageId: string; position: Point; destination: Point; order: string; soldiers: number; wagons: number; speed: number; wheat: number | null; capacity: number; autoSupply: boolean; supplyWagons: number; escorts: number; supplyStatus: string | null; targetId: string | null; actionSeconds: number }
export interface ConvoyView { id: string; ownerId: string; targetArmyId: string | null; position: Point; destination: Point; returning: boolean; wagons: number; escorts: number; wheat: number | null; speed: number; cargo: Cargo | null; recipientId: string | null }
export interface OasisView { id: string; name: string; position: Point; resource: string; ownerId: string | null; villageId: string | null; defenders: number; stock: Cargo | null; capacity: number; productionPerMinute: Cargo; autoTransport: boolean | null; transportStatus: string | null; convoyId: string | null; wallLevel: number; wallHealth: number; wallMaximum: number; wallUpgradeCost: Cargo | null; underAttack: boolean; blockaded:boolean; siegeReportId:string|null; wheatUpkeepPerMinute:number|null; garrison?: Record<string, number> | null; level: number; levelUpgradeCost: Cargo | null; burnedRemainingSeconds: number; burningProgress: number }
export interface ArmyView { supplyVillageId: string; formation: Record<string, number> | null; mapFigures?:Record<string,number>|null; loot: Cargo | null }
export interface ArmyView { afterAttack: string; tribe: string; defendingSiteId: string | null; supportedOwnerId: string | null; campRadius: number | null; scoutedUntil?: number | null; wounded?: number; dead?: number; fled?: number }
export interface ConvoyView { kind: "Supply" | "Trade" | "Oasis" | "Shipment"; inBattle: boolean }
export interface SettlementView { id: string; ownerId: string; tribe: string; position: Point; level: number; ruined: boolean; besieged: boolean; relocateIn: number; biome: string; smokeSeconds?: number }
export interface RankView { playerId: string; score: number; castleSeconds: number; settlements: number; kills: number; dead: number; wounded: number; fled: number; eliminated: boolean }
export interface BattleView { id: string; position: Point; attackerId: string; defenderId: string; attackerOwner: string; defenderOwner: string; defenderKind: string; elapsed: number; round: number; attackerTroops: number; defenderTroops: number; status: string; winner: string | null; attackerArmyIds:string[];defenderArmyIds:string[] }
export interface SiegeView { id: string; armyId: string; villageId: string; attackerOwner: string; defenderOwner: string; position: Point; elapsed: number; wall: number; gate: number; loyalty: number; status: string; armyIds: string[]; objective:string; phase:string; remainingBuildingLevels:number; progress:number }
export interface WarfareView { settlements: SettlementView[]; battles: BattleView[]; sieges: SiegeView[]; rankings: RankView[]; castle: { id: string; position: Point; active: boolean; controllerId: string | null; claimantId: string | null; captureProgress: number; contested: boolean } }
export interface ImportantAlert { id: number; at: number; text: string; kind: string; targetId: string | null }
export interface OasisSiegeView { id:string; armyId:string; oasisId:string; attackerOwner:string; defenderOwner:string; position:Point; elapsed:number; mode:string; wall:number; wallMaximum:number; round:number; armyIds:string[];attackerOwners:string[] }
export interface WorldView { oasisSieges?: OasisSiegeView[]; alerts?: ImportantAlert[]; marketOffers?: MarketOffer[]; regions?: BiomeRegion[];  muster?: Muster; reports?: Report[]; armies: ArmyView[]; convoys: ConvoyView[]; oases: OasisView[]; notices: { id: number; at: number; text: string; kind: string; endsAt: number | null }[]; warfare?: WarfareView; threats?: { armyId: string; ownerId: string; targetId: string; arrivesAt: number }[]; villageGarrisons?: Record<string, Record<string, number>> }
export interface MapSelection { id: string; kind: "village" | "army" | "convoy" | "oasis" | "ground" | "castle" | "battle"; name: string; position: Point; ownerId?: string | null }
const colors = [0xe11d48, 0x2563eb, 0xfacc15, 0x16a34a, 0xf97316, 0x06b6d4, 0xc026d3, 0x84cc16, 0x4f46e5, 0xf9a8d4, 0x78350f, 0x7f1d1d, 0x1e3a8a, 0x115e59, 0xf8fafc, 0x1e293b];
const parseColor = (value: string) => /^#[0-9a-f]{6}$/i.test(value) ? Number.parseInt(value.slice(1), 16) : null;
export const project = (point: Point) => ({ x: point.x, y: point.y * .65 });
const label = (text: string, size = 12, color = 0xfff0cc) => new Text({ text: t(text), style: { fontFamily: "Tahoma, sans-serif", fontSize: size, fontWeight: "bold", fill: color, stroke: { color: 0x243f30, width: 3 }, align: "center" } });

// The same framed resource symbols appear on every interactive oasis, never on scenery.
function resourceBadge(resource: string): Container {
  const root = new Container();
  const g = new Graphics().circle(0, 0, 16).fill(0x203e35).circle(0, 0, 16).stroke({ color: 0xf1d596, width: 2 });
  if (resource === 'Wood') {
    g.roundRect(-9, -4, 18, 8, 3).fill(0xb78d56).circle(6, 0, 4).fill(0xe5bf7e).circle(6, 0, 2).stroke({ color: 0x876039, width: 1 });
    g.moveTo(-7, -1).lineTo(1, -1).stroke({ color: 0x775735, width: 1.5 });
  } else if (resource === 'Wheat') {
    g.moveTo(0, 10).lineTo(0, -10).stroke({ color: 0xf0d18a, width: 2 });
    for (const y of [-6, 0, 6]) g.ellipse(-4, y - 2, 4, 2.5).ellipse(4, y - 4, 4, 2.5).fill(0xe5bb60);
  } else if (resource === 'Iron') {
    g.poly([-10, 4, -6, -5, 3, -9, 10, -1, 6, 8, -4, 9]).fill(0x97acb4)
      .poly([-6, -5, 3, -9, 1, 0, -10, 4]).fill(0xd2e0df).moveTo(1, 0).lineTo(6, 8).stroke({ color: 0x546977, width: 2 });
  } else {
    g.roundRect(-10, -2, 13, 10, 2).fill(0x9c9e92).roundRect(-3, -8, 13, 10, 2).fill(0xdbd6bd)
      .moveTo(-1, -5).lineTo(7, -5).stroke({ color: 0xf6ecd0, width: 2 });
  }
  root.addChild(g); return root;
}

interface MovingNode { root: Container; from: Point; to: Point; at: number; label: Text; kind: "army" | "convoy"; artworkKey?: string; party?: Container; partyY?: number; dust?: Graphics; flag?: Graphics; marching?: boolean; figures?:{sprite:Sprite;y:number}[]; lastSoldiers?: number; lastEscorts?: number; wasFighting?: boolean; }
interface LossIndicator { root: Container; caption: Text; at: number; duration: number; startX: number; startY: number; jitterX: number; targetArmyId?: string; }
export class WorldMap {
  private armyLayout=new ArmyLayout();
  private app = new Application();
  private world = new Container();
  private terrain = new Container();
  private landscape: Landscape | null = null;
  private showBoundaries = false;
  private hovered: MapSelection | null = null;
  private oasisMarkers = new Map<string, { ring: Graphics; badge: Container; plate: Container; ownerId: string | null }>();
  private secondaryLabels: Text[] = [];
  private oasisRipples:Graphics[]=[];
  private settlements = new Container();
  private castleLayer = new Container();
  private nameplates = new Container();
  private paths = new Graphics();
  private camps = new Graphics();
  private units = new Container();
  private warfare = new Container();
  private effects: Container[] = [];
  private selection = new Graphics();
  private sky = new Container();
  private clouds: Container[] = [];
  private flags: Graphics[] = [];
  private ownerColors=new Map<string,number>();
  private clashes=new Map<string,{effect:BattleClash;battle?:BattleView;siege?:SiegeView;pick:MapSelection}>();
  private battleLayer=new Container();
  private lossLayer=new Container();
  private losses: LossIndicator[] = [];
  private burningVillages=new Map<string,VillageFire>();
  private smokingVillages=new Map<string,VillageSmoke>();
  private burningOases=new Map<string,VillageFire>();
  private smokingOases=new Map<string,VillageSmoke>();
  private fireLayer=new Container();
  private smoke: {sprite:Graphics; origin:Point; phase:number}[]=[];
  private celebrations: {root:Container; at:number; ring:Graphics; caption:Text}[]=[];
  private moving = new Map<string, MovingNode>();
  private picks: MapSelection[] = [];
  private players: MapPlayer[] = [];
  private viewer = "preview";
  private alliedOwners = new Set<string>();
  private signature = "";
  private terrainSignature = "";
  private scale = 1;
  private viewportCenterY = 650;
  private zoomFactor = 1;
  private pan = { x: 0, y: 0 };
  private ready = false;
  private reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
  private selected: MapSelection | null = null;
  private selectedArmyIds = new Set<string>();
  private selectedHomeVillageIds = new Set<string>();
  private hoverHandler?: (target: MapSelection | null, client: Point | null) => void;
  private rightClickHandler?: (target: MapSelection, client: Point) => void;
  private cameraMoveHandler?: () => void;
  private pendingFocus: { point: Point; zoom: number } | null = null;
  private last: { world: WorldView | null; players: MapPlayer[]; viewer: string } | null = null;
  private dragging: { x: number; y: number; panX: number; panY: number; moved: boolean } | null = null;
  constructor(private host: HTMLElement, private onSelect: (selection: MapSelection, modifiers: { ctrl: boolean; shift: boolean }) => void) {}

  async init() {
    await Promise.all([loadArt(), loadUnitArt()]);
    await this.app.init({ resizeTo: this.host, backgroundAlpha: 0, antialias: true, resolution: Math.min(devicePixelRatio, 2), autoDensity: true, preference: "webgl" });
    this.host.appendChild(this.app.canvas);
    this.app.canvas.setAttribute("aria-label", "Interactive world map. Drag to pan, scroll to zoom, click to select. Arrow keys pan; plus and minus zoom.");
    this.app.canvas.tabIndex = 0;
    this.app.stage.addChild(this.sky, this.world);
    this.lossLayer.eventMode = 'none';
    this.world.addChild(this.terrain, this.camps, this.paths, this.settlements, this.castleLayer, this.selection, this.units, this.fireLayer, this.warfare, this.nameplates, this.battleLayer, this.lossLayer);
    this.settlements.sortableChildren = true; this.units.sortableChildren=true;
    this.ready = true;
    const pending = this.last;
    this.update(null, [], "preview");
    this.bind();
    matchMedia("(prefers-reduced-motion: reduce)").addEventListener("change",event=>{this.reduced=event.matches;});
    new ResizeObserver(() => this.fit()).observe(this.host);
    this.app.ticker.add(() => this.animate());
    this.fit();
    if (pending) this.update(pending.world, pending.players, pending.viewer);
    if (this.pendingFocus) {
      const target = this.pendingFocus;
      this.pendingFocus = null;
      this.focus(target.point, target.zoom);
    }
  }
  private fit() {
    if (!this.ready) return;
    const width = this.host.clientWidth, height = this.host.clientHeight;
    this.app.renderer.resize(width, height);
    if(width<1||height<1)return;
    const bottom = Number.parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--command-height')) || 220;
    const available = Math.max(120, height - 105 - bottom - 25);
    this.viewportCenterY = 105 + available / 2;
    this.scale = Math.min((width - 60) / 4500, available / 2925) * this.zoomFactor;
    this.world.scale.set(this.scale);
    this.world.position.set((width - 4500 * this.scale) / 2 + this.pan.x, this.viewportCenterY - 1462.5 * this.scale + this.pan.y);
    this.updateDetail();
    this.sky.removeChildren().forEach(child => child.destroy({ children: true })); this.clouds = [];
    const sky = new Graphics();
    for (let i = 0; i < 30; i++) {
      const t = i / 30; const r = Math.round(157 + t * 68), gg = Math.round(210 + t * 24), b = Math.round(225 - t * 18);
      sky.rect(0, height * t, width, height / 30 + 1).fill((r << 16) | (gg << 8) | b);
    }
    sky.circle(width - 95, 57, 31).fill({ color: 0xffedbb, alpha: .45 }).circle(width - 95, 57, 23).fill(0xfff3c6);
    sky.poly([0, 160, width * .12, 57, width * .26, 163, width * .48, 45, width * .72, 164, width * .9, 69, width, 155, width, height, 0, height]).fill({ color: 0x86b6ad, alpha: .45 });
    this.sky.addChild(sky);
    for (let i = 0; i < 5; i++) {
      const cloud = new Container(); cloud.position.set((i + .2) * width / 5, 25 + (i % 2) * 35);
      cloud.addChild(new Graphics().ellipse(0, 5, 35, 10).circle(-17, 0, 13).circle(3, -5, 18).circle(22, 1, 12).fill({ color: 0xffffff, alpha: .7 }));
      this.sky.addChild(cloud); this.clouds.push(cloud);
    }
  }
  refreshLanguage() { this.signature = ""; this.terrainSignature = ""; const view = this.last; if(view)this.update(view.world,view.players,view.viewer); }
  zoom(delta: number) { this.zoomFactor = Math.max(.7, Math.min(8, this.zoomFactor + delta)); this.fit(); this.cameraMoveHandler?.(); }
  home() { this.zoomFactor = 1; this.pan = { x: 0, y: 0 }; this.fit(); this.cameraMoveHandler?.(); }
  focus(point: Point, zoom = 3.2) {
    if (!this.ready) {
      this.pendingFocus = { point, zoom };
      return;
    }
    this.zoomFactor = zoom; this.fit(); const p = project(point);
    this.pan = { x: (2250 - p.x) * this.scale, y: (1462.5 - p.y) * this.scale }; this.fit(); this.cameraMoveHandler?.();
  }
  select(selection: MapSelection | null) { this.selected = selection; this.updateDetail(); }
  setSelectedArmies(armyIds: string[], villageIds: string[]) { this.selectedArmyIds = new Set(armyIds); this.selectedHomeVillageIds = new Set(villageIds); }
  setInteractionHandlers(handlers: { onHover?: (target: MapSelection | null, client: Point | null) => void; onRightClick?: (target: MapSelection, client: Point) => void; onCameraMove?: () => void }) {
    this.hoverHandler = handlers.onHover; this.rightClickHandler = handlers.onRightClick; this.cameraMoveHandler = handlers.onCameraMove;
  }
  setAlliances(alliances: { firstPlayerId: string; secondPlayerId: string }[], viewer: string) {
    this.alliedOwners = new Set(alliances.flatMap(a => a.firstPlayerId === viewer ? [a.secondPlayerId] : a.secondPlayerId === viewer ? [a.firstPlayerId] : []));
  }
  combatSounds():CombatSound[]{
    const view=this.last?.world;if(!this.ready||!view)return [];
    const positions:Point[]=[...this.clashes.values()].filter(c=>!c.effect.root.destroyed).map(c=>({x:c.effect.root.x,y:c.effect.root.y}));
    for(const siege of view.warfare?.sieges??[])if(siege.status==='Active'&&siege.phase!=='Blockading'&&siege.position)positions.push(project(siege.position));
    for(const siege of view.oasisSieges??[])if(siege.mode!=='Blockade'&&siege.position)positions.push(project(siege.position));
    const distinct=positions.filter((p,i)=>!positions.slice(0,i).some(q=>Math.hypot(p.x-q.x,p.y-q.y)<75));
    return distinct.flatMap(p=>{
      const screen=this.world.toGlobal(p),sound=combatAudibility(screen.x,screen.y,this.host.clientWidth,this.host.clientHeight);
      return sound?[sound]:[];
    });
  }
  toggleBoundaries() { this.showBoundaries = !this.showBoundaries; this.landscape?.setBoundaries(this.showBoundaries); return this.showBoundaries; }
  private updateDetail() {
    this.landscape?.setDetail(this.scale);
    this.secondaryLabels.forEach(text => { text.visible = this.scale >= .55; });
    for (const [id, marker] of this.oasisMarkers) {
      const selected = this.selected?.id === id, hovered = this.hovered?.id === id;
      marker.plate.visible = this.scale >= .58 || selected || hovered;
      marker.badge.scale.set(Math.max(1, Math.min(3.5, .65 / this.scale)));
      marker.ring.clear().ellipse(0, 12, 65, 29).fill({ color: 0x274b3c, alpha: selected || hovered ? .2 : .09 })
        .ellipse(0, 12, 65, 29).stroke({ color: selected ? 0xfff4c4 : hovered ? 0xffe5a0 : marker.ownerId === this.viewer ? 0x80d6a6 : marker.ownerId ? 0xe49d85 : 0xe4cc93, width: selected || hovered ? 3 : 1.7, alpha: selected || hovered ? 1 : .7 });
    }
  }
  private colorFor(ownerId: string) {
    const assigned=this.ownerColors.get(ownerId);if(assigned!==undefined)return assigned;
    let hash=0;for(let i=0;i<ownerId.length;i++)hash=(hash*31+ownerId.charCodeAt(i))>>>0;
    return colors[hash%colors.length]!;
  }
  private flag(root: Container, color: number, height=0, x=16) {
    const top=-90-height;
    root.addChild(new Graphics()
      .moveTo(x,-20-height).lineTo(x,top-3).stroke({color:0x203126,width:5})
      .moveTo(x,-20-height).lineTo(x,top-3).stroke({color:0xd8ba79,width:2.5})
      .circle(x,top-4,3.5).fill(0xffe2a0).stroke({color:0x354331,width:1}));
    const flag = new Graphics().poly([0,0,43,4,36,15,43,27,0,24]).fill(0xffffff)
      .stroke({color:0x17251b,width:2.5})
      .moveTo(4,4).lineTo(34,7).stroke({color:0xffffff,alpha:.45,width:1.5});
    flag.tint=color;flag.label='owner-flag';
    flag.position.set(x,top); root.addChild(flag); this.flags.push(flag);
  }
  private unitFlagShaft(root: Container, top = -50, baseY = -10, x = -13) {
    root.addChild(new Graphics()
      .moveTo(x, baseY).lineTo(x, top - 1).stroke({ color: 0x203126, width: 2.8 })
      .moveTo(x, baseY).lineTo(x, top - 1).stroke({ color: 0xd8ba79, width: 1.5 }));
  }
  private unitFlagPennant(root: Container, color: number, top = -50, x = -13): Graphics {
    root.addChild(new Graphics()
      .moveTo(x, top + 11).lineTo(x, top - 1).stroke({ color: 0x203126, width: 2.8 })
      .moveTo(x, top + 11).lineTo(x, top - 1).stroke({ color: 0xd8ba79, width: 1.5 })
      .circle(x, top - 2.5, 2.2).fill(0xffe2a0).stroke({ color: 0x354331, width: .9 }));
    const flag = new Graphics().poly([0, 0, -21, 2.5, -16, 6.5, -21, 12, 0, 10.5]).fill(0xffffff)
      .stroke({ color: 0x17251b, width: 1.6 })
      .moveTo(-2, 2.2).lineTo(-15, 4).stroke({ color: 0xffffff, alpha: .45, width: 1.1 });
    flag.tint = color; flag.label = 'owner-flag';
    flag.position.set(x, top); root.addChild(flag);
    return flag;
  }
  update(world: WorldView | null, players: MapPlayer[], viewer: string) {
    this.last = { world, players, viewer };
    if (!this.ready) return;
    try {
      if (!world) {
      players = Array.from({ length: 8 }, (_, i) => ({ id: `preview-${i}`, villageId: `v-${i}`, tribe: ["Romans","Germans","Greeks","Mongols","Persians"][i % 5], name: ["Your new beginning", "Willowbrook", "Golden Fields", "Rosewood", "Suncove", "Windfall", "Oakridge", "Bluehaven"][i]!, level: i === 0 ? 6 : 1,
        position: { x: 2250 + Math.cos(i * Math.PI / 4) * 1750, y: 2250 + Math.sin(i * Math.PI / 4) * 1750 } }));
      viewer = "preview-0";
      world = { regions:previewRegions, armies: [], convoys: [], oases: [{ id: "preview-oasis", name: "Wood oasis", resource: "Wood", position: { x: 1500, y: 785 }, ownerId: null, villageId: null, defenders: 0, stock:null, capacity:15000, productionPerMinute:{wood:240,iron:120,stone:120,wheat:120}, autoTransport:null, transportStatus:null, convoyId:null, wallLevel:0, wallHealth:0, wallMaximum:0, wallUpgradeCost:null, underAttack:false,blockaded:false,siegeReportId:null,wheatUpkeepPerMinute:null, level: 0, levelUpgradeCost: null, burnedRemainingSeconds: 0, burningProgress: 0 }], notices: [] };
    }
    for(const player of players){const color=player.flagColor?parseColor(player.flagColor):null;if(color!==null)this.ownerColors.set(player.id,color);}
    if (world.warfare) players = world.warfare.settlements.filter(city=>!city.ruined).map(city => ({ id: city.ownerId, villageId: city.id, position: city.position, level: city.level, ruined: city.ruined, tribe: city.tribe, name: players.find(player => player.id === city.ownerId)?.name ?? "Settlement", flagColor: players.find(player => player.id === city.ownerId)?.flagColor }));
    const terrainKey = JSON.stringify([world.regions,players.map(p=>p.position),world.oases.map(c=>c.position)]);
    if(world.regions?.length && terrainKey !== this.terrainSignature){
      this.terrainSignature=terrainKey; this.landscape?.destroy();
      this.landscape=drawLandscape(this.terrain,world.regions,players.flatMap(p=>p.position?[p.position]:[]),world.oases.map(c=>c.position));
      this.landscape.setBoundaries(this.showBoundaries);
    }
    this.players = players; this.viewer = viewer;
    const signature = JSON.stringify(players.map(x => [x.id, x.name, x.level, x.position, x.tribe, x.ruined])) + JSON.stringify(world.oases.map(x => [x.id,x.position,x.resource,x.ownerId,x.villageId,x.wallLevel,x.wallHealth>0,x.level,(x.burnedRemainingSeconds??0)>0,(x.burningProgress??0)>0]));
    this.picks = [];
    if (signature !== this.signature) {
      this.signature = signature; this.smoke=[];
      this.settlements.removeChildren().forEach(child => child.destroy({ children: true })); this.nameplates.removeChildren().forEach(child=>child.destroy({children:true})); this.flags = []; this.oasisRipples = [];
      this.oasisMarkers.clear(); this.secondaryLabels = [];
      players.forEach((player,playerIndex) => {
        if (!player.position || !player.villageId) return;
        const root = new Container(); const p = project(player.position); root.position.set(p.x, p.y); root.scale.set(1.5);
        root.zIndex = p.y;
        const color = this.colorFor(player.id);

        root.addChild(settlementArt(player.tribe??'Romans',player.level??1,player.id===viewer));
        this.flag(root,color);
        for(let i=0;i<3;i++){
          const puff=new Graphics().circle(0,0,4).fill(0xe5ddbd);puff.alpha=0;
          root.addChild(puff);this.smoke.push({sprite:puff,origin:{x:-12,y:-43},phase:i/3+playerIndex*.17});
        }
        const stage=settlementStage(player.level??1);
        const plate = new Container(); plate.position.copyFrom(root.position); plate.scale.set(1.36);
        plate.addChild(new Graphics().roundRect(-58,36,116,31,5).fill({color:0x173529,alpha:.9}).roundRect(-58,36,116,31,5).stroke({color:player.id===viewer?0xe4c27a:0x9dab83,width:1,alpha:.7}));
        const name = label(player.id === viewer ? `YOUR ${stage.toUpperCase()}` : player.name.slice(0, 15), 12); name.anchor.set(.5, 0); name.position.set(0, 39); plate.addChild(name);
        const level=label(`${stage} / LEVEL ${player.level}`,9,0xd2d6b2);level.anchor.set(.5,0);level.position.set(0,54);plate.addChild(level);
        this.secondaryLabels.push(level);
        this.nameplates.addChild(plate);
        this.settlements.addChild(root);
      });
      for (const oasis of world.oases) {
        const root = new Container(); const p = project(oasis.position); root.position.set(p.x, p.y);
        root.zIndex = p.y;
        const ring = new Graphics(); root.addChild(ring);
        root.addChild(oasisSprite(oasis.resource??'Wood',137));
        const isBurned = (oasis.burnedRemainingSeconds ?? 0) > 0;
        const isBurning = (oasis.burningProgress ?? 0) > 0;
        if(oasis.wallLevel>0){
          const wall=new Graphics();
          wall.ellipse(0,12,69,30).stroke({color:oasis.wallHealth>0?0x9b8b70:0x655c4a,width:oasis.wallHealth>0?7:3,alpha:.95});
          if(oasis.wallHealth>0)for(let i=0;i<13;i++){const a=i/13*Math.PI*2;wall.rect(Math.cos(a)*69-4,Math.sin(a)*30+6,8,10).fill(0xc6b89a);}
          root.addChild(wall);const level=label(''+oasis.wallLevel,11);level.position.set(-61,17);root.addChild(level);
        }
        if (isBurned) {
          const burnedOverlay = new Graphics();
          burnedOverlay.ellipse(0, 15, 60, 26).fill({ color: 0x1f100c, alpha: 0.75 }).stroke({ color: 0xd94426, width: 2, alpha: 0.85 });
          root.addChild(burnedOverlay);
        }
        const ripple=new Graphics();ripple.position.set(0,8);root.addChild(ripple);this.oasisRipples.push(ripple);
        const badge = resourceBadge(oasis.resource ?? 'Wood'); badge.position.set(48, 14); root.addChild(badge);
        if(oasis.ownerId && !isBurned)this.flag(root,this.colorFor(oasis.ownerId),18,56);
        const isAr = currentLanguage() === 'ar';
        const localizedName = isAr ? t(oasis.name) : oasis.name;
        const levelTag = oasis.level > 0 ? (isAr ? ` (مستوى ${oasis.level})` : ` (Lvl ${oasis.level})`) : '';
        const statusTag = isBurned ? (isAr ? ' 🔥 [محروقة]' : ' 🔥 [Burned]') : isBurning ? (isAr ? ' ⏳ [جاري الحرق]' : ' ⏳ [Burning]') : '';
        const nameStr = `${localizedName}${levelTag}${statusTag}`;
        const plateWidth = Math.max(116, nameStr.length * 8 + 18);
        const plate = new Container(); plate.position.copyFrom(root.position);
        plate.addChild(new Graphics().roundRect(-plateWidth/2,46,plateWidth,24,5).fill({color:isBurned?0x3e1f1a:0x213e32,alpha:.94}).roundRect(-plateWidth/2,46,plateWidth,24,5).stroke({color:isBurned?0xd95a43:0xd9c393,alpha:.6,width:1}));
        const name=label(nameStr,13,isBurned ? 0xff9988 : 0xffedbb);name.anchor.set(.5,0);name.position.y=50;plate.addChild(name);
        this.nameplates.addChild(plate); this.settlements.addChild(root);
        this.oasisMarkers.set(oasis.id, { ring, badge, plate, ownerId: oasis.ownerId });

      }
    }
    for (const player of players) if (player.position && player.villageId) this.picks.push({ id: player.villageId, kind: "village", name: player.name, position: player.position, ownerId: player.id });
    for (const oasis of world.oases) this.picks.push({ ...oasis, kind: "oasis" });
    const oasisIds = new Set(world.oases.map(o => o.id));
    const villageIds = new Set((world.warfare?.settlements ?? []).map(s => s.id));
    const live = new Set<string>(); this.paths.clear(); this.camps.clear();
    for(const army of world.armies)if(army.campRadius){const p=project(army.position);drawCamp(this.camps,p.x,p.y,army.campRadius,this.colorFor(army.ownerId));}
    for (const entity of [...world.armies, ...world.convoys]) {
      const isArmy = "soldiers" in entity;
      if (isArmy && entity.order === "Guarding" && entity.defendingSiteId && (oasisIds.has(entity.defendingSiteId) || villageIds.has(entity.defendingSiteId))) continue;
      if (isArmy && entity.ownerId !== viewer && entity.order === "Scouting") continue;
      const kind = isArmy ? "army" : "convoy";
      const color = this.colorFor(entity.ownerId);
      live.add(entity.id);
      const position = isArmy ? this.armyLayout.position(entity, world) : this.armyLayout.convoyPosition(entity.id, entity.position, world);
      let node = this.moving.get(entity.id);
      if (!node) {
        const root = new Container();
        root.position.set(position.x, position.y);
        const text = label(isArmy ? `${entity.soldiers} troops` : "Supply", 10); text.anchor.set(.5, 0); text.position.set(0, 26); root.addChild(text);
        root.scale.set(1.25);
        node = { root, from: position, to: position, at: performance.now(), label: text, kind, lastSoldiers: isArmy ? entity.soldiers : undefined, lastEscorts: !isArmy ? entity.escorts : undefined }; this.moving.set(entity.id, node); this.units.addChild(root);
      }
      node.from = { x: node.root.x, y: node.root.y }; node.to = position; node.at = performance.now();
      const tribe=(isArmy ? entity.tribe : players.find(p=>p.id===entity.ownerId)?.tribe ?? 'Romans').toLowerCase();
      const formation=isArmy?armyFigures(entity.soldiers,tribe,entity.formation,entity.mapFigures):[];
      const wagonCount=armyFigureCount(entity.wagons),escortCount=!isArmy?armyFigureCount(entity.escorts):0;
      const artworkKey=formation.join(',')+`|${tribe}|${wagonCount}|${escortCount}|${color}|${isArmy&&entity.campRadius?'camp':''}`;
      if (node.artworkKey !== artworkKey) {
        node.artworkKey=artworkKey;
        node.root.removeChildren().forEach(child=>{if(child!==node!.label)child.destroy({children:true});});
        const party=new Container(),dust=new Graphics(),layout=armyFigureLayout(formation.length);
        node.figures=[];
        const wagonLayout=armyFigureLayout(wagonCount);
        const flagX=-13-Math.max(0,Math.max(layout.columns,wagonLayout.columns)-1)*9;
        const flagTop=-50-Math.max(0,layout.rows-1)*17;
        const flagBase=-12-Math.max(0,layout.rows-1)*17;
        party.addChild(new Graphics().ellipse(0,5-Math.max(0,layout.rows-1)*8,Math.max(26,layout.columns*13),12+layout.rows*7).fill({color,alpha:.12}));
        // Camped troops stand in the middle of the perimeter rather than above its centre point.
        node.partyY=isArmy&&entity.campRadius?Math.max(0,layout.rows-1)*8.5+16:0;
        this.unitFlagShaft(party,flagTop,flagBase,flagX);
        formation.forEach((id,index)=>{
          const point=layout.points[index]!,sprite=unitSprite(id,layout.width);sprite.position.set(point.x,point.y);
          sprite.label='army-unit';party.addChild(sprite);node!.figures!.push({sprite,y:point.y});
        });
        for(let i=0;i<wagonCount;i++){
          const supply=unitSprite(`${tribe}-wagon`,isArmy?40:55);
          const point=wagonLayout.points[i]!;
          supply.position.set(point.x,point.y+Math.max(0,wagonLayout.rows-1)*17+(formation.length?22:0));supply.label='army-wagon';party.addChild(supply);
        }
        for(let i=0;i<escortCount;i++){const escort=unitSprite(`${tribe}-infantry`,36);escort.position.set((i-(escortCount-1)/2)*22,18);party.addChild(escort);}
        node.flag=this.unitFlagPennant(party,color,flagTop,flagX);
        node.party=party;node.dust=dust;node.label.y=(formation.length&&wagonCount?42:27)+Math.max(0,wagonLayout.rows-1)*17+node.partyY;node.root.addChild(dust,party,node.label);
      }
      node.root.zIndex = position.y;
      const battleSide = this.armyLayout.activeBattleSide(entity.id, world);
      node.marching = battleSide === 0 && Math.hypot(entity.position.x - entity.destination.x, entity.position.y - entity.destination.y) > 4;
      if (node.party) {
        if (battleSide !== 0) node.party.scale.x = battleSide === -1 ? 1 : -1;
        else if (node.marching) node.party.scale.x = entity.destination.x < entity.position.x ? -1 : 1;
      }
      const ownerName = players.find(p => p.id === entity.ownerId)?.name ?? t("Settlement");
      const statusText = t(isArmy ? entity.order === "Camping" ? "Establishing camp" : entity.campRadius ? "Camped" : entity.order === "Guarding" ? "Guarding" : entity.order === "Reinforcing" ? "Reinforcing" : entity.order === "Merging" ? "Merging" : `${entity.soldiers + entity.wagons} units` : entity.inBattle ? "AMBUSH" : entity.returning ? "Returning" : entity.kind === "Oasis" ? "Oasis collection" : entity.kind === "Trade" ? "Trade" : entity.kind === "Shipment" ? "Resource shipment" : "Supply");
      node.label.text = isArmy ? statusText : `${ownerName.slice(0, 12)} · ${statusText}`;
      const isFighting = isArmy ? (
        entity.order === "Battle" || entity.order === "Sieging" ||
        (world.warfare?.battles.some(b => b.status === "Active" && (b.attackerArmyIds?.includes(entity.id) || b.defenderArmyIds?.includes(entity.id) || b.attackerId === entity.id || b.defenderId === entity.id)) ?? false) ||
        (world.warfare?.sieges.some(s => s.status === "Active" && (s.armyIds?.includes(entity.id) || s.armyId === entity.id)) ?? false) ||
        (world.oasisSieges?.some(s => s.armyIds?.includes(entity.id) || s.armyId === entity.id) ?? false)
      ) : (entity as ConvoyView).inBattle;

      if (isArmy && node.lastSoldiers !== undefined) {
        const diff = node.lastSoldiers - entity.soldiers;
        if (diff > 0 && (isFighting || node.wasFighting)) {
          this.showLoss(entity.id, position, diff);
        }
      } else if (!isArmy && node.lastEscorts !== undefined) {
        const diff = node.lastEscorts - entity.escorts;
        if (diff > 0 && (isFighting || node.wasFighting)) {
          this.showLoss(entity.id, position, diff);
        }
      }
      if (isArmy) {
        node.lastSoldiers = entity.soldiers;
      } else {
        node.lastEscorts = entity.escorts;
      }
      node.wasFighting = isFighting;
      this.picks.push({ id: entity.id, kind, name: isArmy ? "Expedition" : "Supply convoy", position: entity.position, ownerId: entity.ownerId });
      const friendly = (ownerId: string | null | undefined) => ownerId === viewer || !!ownerId && this.alliedOwners.has(ownerId);
      const attackingFriendly = isArmy && !friendly(entity.ownerId) && (
        world.threats?.some(th => th.armyId === entity.id) ||
        (entity.order === "Capturing" && friendly(world.warfare?.castle?.controllerId)) ||
        (["Approaching", "Attacking", "Occupying", "Blockading", "Intercepting", "Burning"].includes(entity.order) && (
          world.oases.some(o => o.id === entity.targetId && friendly(o.ownerId)) ||
          world.warfare?.settlements.some(c => c.id === entity.targetId && friendly(c.ownerId)) ||
          world.armies.some(a => a.id === entity.targetId && friendly(a.ownerId)) ||
          world.convoys.some(c => c.id === entity.targetId && friendly(c.ownerId))
        ))
      );
      const involvesFriendly = friendly(entity.ownerId) ||
        (isArmy && entity.order === "Reinforcing" && friendly(entity.supportedOwnerId)) ||
        (!isArmy && friendly(entity.recipientId));
      const offensiveOrder = isArmy && ["Approaching", "Attacking", "Occupying", "Blockading", "Intercepting", "Burning", "Capturing"].includes(entity.order);
      if (attackingFriendly) {
        const targetCity = world.warfare?.settlements.find(c => c.id === entity.targetId && friendly(c.ownerId));
        const targetOasis = world.oases.find(o => o.id === entity.targetId && friendly(o.ownerId));
        const dest = targetCity?.position ?? targetOasis?.position ?? entity.destination;
        if (dest && Math.hypot(entity.position.x - dest.x, entity.position.y - dest.y) > 4)
          this.dash(position, project(dest), 0xe83b2e, 2.8);
      } else if (involvesFriendly && entity.destination && Math.hypot(entity.position.x - entity.destination.x, entity.position.y - entity.destination.y) > 4) {
        const lineColor = !isArmy ? 0xfff3b2
          : entity.order === "Reinforcing" ? 0x4ade80
          : offensiveOrder ? 0x60a5fa
          : 0xf8fafc;
        this.dash(position, project(entity.destination), lineColor);
      }
    }
    this.armyLayout.retain(world, live);
    for (const [id, node] of this.moving) if (!live.has(id)) {
      if (node.wasFighting && (node.lastSoldiers ?? 0) > 0) {
        this.showLoss(undefined, { x: node.root.x, y: node.root.y }, node.lastSoldiers!);
      }
      node.root.destroy({ children: true }); this.moving.delete(id);
    }
    this.castleLayer.removeChildren().forEach(child => child.destroy({ children: true }));
    this.warfare.removeChildren().forEach(child => child.destroy({ children: true })); this.effects = [];
    const war = world.warfare;
    const demolitions=new Set<string>();
    for(const siege of war?.sieges.filter(isDemolishing)??[]){
      demolitions.add(siege.villageId);
      let fire=this.burningVillages.get(siege.villageId);
      if(!fire){fire=new VillageFire();this.burningVillages.set(siege.villageId,fire);this.fireLayer.addChild(fire.root);}
      const position=project(siege.position);fire.root.position.set(position.x,position.y);
    }
    for(const [id,fire] of this.burningVillages)if(!demolitions.has(id)){fire.destroy();this.burningVillages.delete(id);}
    const smoking=new Set<string>();
    for(const city of war?.settlements.filter(s=>isSmoking(s)&&!demolitions.has(s.id))??[]){
      smoking.add(city.id);
      let smoke=this.smokingVillages.get(city.id);
      if(!smoke){smoke=new VillageSmoke();this.smokingVillages.set(city.id,smoke);this.fireLayer.addChild(smoke.root);}
      const position=project(city.position);smoke.root.position.set(position.x,position.y);
    }
    for(const [id,smoke] of this.smokingVillages)if(!smoking.has(id)){smoke.destroy();this.smokingVillages.delete(id);}
    const burningOasisIds = new Set<string>();
    const smokingOasisIds = new Set<string>();
    for (const oasis of world.oases) {
      const isBurned = (oasis.burnedRemainingSeconds ?? 0) > 0;
      const isBurning = (oasis.burningProgress ?? 0) > 0;
      if (isBurning) {
        burningOasisIds.add(oasis.id);
        let fire = this.burningOases.get(oasis.id);
        if (!fire) {
          fire = new VillageFire();
          this.burningOases.set(oasis.id, fire);
          this.fireLayer.addChild(fire.root);
        }
        const p = project(oasis.position);
        fire.root.position.set(p.x, p.y + 12);
      } else if (isBurned) {
        smokingOasisIds.add(oasis.id);
        let smoke = this.smokingOases.get(oasis.id);
        if (!smoke) {
          smoke = new VillageSmoke();
          this.smokingOases.set(oasis.id, smoke);
          this.fireLayer.addChild(smoke.root);
        }
        const p = project(oasis.position);
        smoke.root.position.set(p.x, p.y + 12);
      }
    }
    for (const [id, fire] of this.burningOases) {
      if (!burningOasisIds.has(id)) {
        fire.destroy();
        this.burningOases.delete(id);
      }
    }
    for (const [id, smoke] of this.smokingOases) {
      if (!smokingOasisIds.has(id)) {
        smoke.destroy();
        this.smokingOases.delete(id);
      }
    }
    if (war?.castle.active) {
      const root = new Container(); const p = project(war.castle.position); root.position.set(p.x, p.y);
      const color = war.castle.controllerId ? this.colorFor(war.castle.controllerId) : 0xe4b758;
      const g = new Graphics()
        .ellipse(0, 14, 58, 28).fill({ color: 0xffe58c, alpha: .48 }).stroke({ color: 0xf7d788, width: 2.2, alpha: .9 })
        .rect(-38, -26, 76, 42).fill(0xece6cb).stroke({ color: 0x3b3528, width: 1.5 });
      for (const x of [-42, 26]) {
        g.rect(x, -46, 16, 62).fill(0xc9d4ce).stroke({ color: 0x3b3528, width: 1.5 })
          .poly([x - 4, -46, x + 8, -66, x + 20, -46]).fill(color).stroke({ color: 0x20271d, width: 1.4 });
      }
      g.roundRect(-12, -10, 24, 28, 10).fill(0x5b503b)
        .rect(-9, -58, 18, 34).fill(0xf3eed6).stroke({ color: 0x3b3528, width: 1.5 })
        .poly([-14, -58, 0, -80, 14, -58]).fill(color).stroke({ color: 0x20271d, width: 1.5 });
      g.scale.set(1.75);
      root.addChild(g);
      if (war.castle.controllerId) this.flag(root, color, 52, 24);
      const isAr = currentLanguage() === 'ar';
      const titleText = war.castle.contested
        ? (isAr ? "قلعة التاج · متنازع عليها" : "CROWN KEEP · CONTESTED")
        : (isAr ? "قلعة التاج" : "CROWN KEEP");
      const progressSec = Math.max(0, Math.ceil(war.castle.captureProgress));
      const subText = war.castle.controllerId
        ? `${Math.floor(progressSec / 60)}:${String(progressSec % 60).padStart(2, '0')} / 5:00${war.castle.contested ? (isAr ? ' (متوقف)' : ' (Paused)') : ''}`
        : (isAr ? 'مفتوحة للهجوم والسيطرة' : 'Open for capture');
      const plateW = Math.max(150, Math.max(titleText.length, subText.length) * 8 + 26);
      const plate = new Container(); plate.position.copyFrom(p);
      plate.addChild(new Graphics().roundRect(-plateW / 2, 54, plateW, 40, 6).fill({ color: war.castle.contested ? 0x4b1e1b : 0x1e382b, alpha: .94 }).stroke({ color: 0xf1d18a, width: 1.5, alpha: .85 }));
      const name = label(titleText, 14, war.castle.contested ? 0xffb3a7 : 0xffe9ab); name.anchor.set(.5, 0); name.position.y = 58;
      const sub = label(subText, 11, 0xd8e0be); sub.anchor.set(.5, 0); sub.position.y = 76;
      plate.addChild(name, sub);
      this.castleLayer.addChild(root);
      this.warfare.addChild(plate); if (war.castle.position) this.picks.push({ id: war.castle.id, kind: "castle", name: "Crown Keep", position: war.castle.position });
    }
    for (const siege of war?.sieges.filter(x => ["Active"].includes(x.status)) ?? []) {
      if (!siege.position) continue;
      const p = project(siege.position); const ring = new Graphics().ellipse(p.x, p.y + 4, 100, 58).stroke({ color: 0xe88552, width: 4, alpha: .8 });
      this.warfare.addChild(ring);

      const name = label(siege.phase === "Persuading" ? `${t("Persuasion in progress")} ${Math.ceil(siege.progress)}%` : siege.phase === "Demolishing" ? `${t("Demolition in progress")} ${Math.ceil(siege.progress)}%` : t("Siege in progress"), 10); name.anchor.set(.5, 0); name.position.set(p.x, p.y - 110); this.warfare.addChild(name);
    }
    for(const siege of war?.sieges.filter(s=>["Active"].includes(s.status))??[]) if (siege.position) this.picks.push({id:siege.id,kind:"battle",name:"Siege in progress",position:{x:siege.position.x,y:siege.position.y-110/.65}});
    const activeClashes = new Set<string>();
    for (const battle of war?.battles.filter(x => x.status === "Active") ?? []) {
      activeClashes.add(battle.id);
      let clash = this.clashes.get(battle.id);
      if (!clash) {
        clash = { effect: new BattleClash(), battle, pick: { id: battle.id, kind: 'battle', name: 'Battle in progress', position: battle.position ?? { x: 2250, y: 1462.5 } } };
        this.clashes.set(battle.id, clash); this.battleLayer.addChild(clash.effect.root);
      }
      clash.battle = battle; clash.siege = undefined; this.positionClash(clash); this.picks.push(clash.pick);
    }
    for (const siege of war?.sieges.filter(x => ["Active"].includes(x.status)) ?? []) {
      activeClashes.add(siege.id);
      let clash = this.clashes.get(siege.id);
      if (!clash) {
        clash = { effect: new BattleClash(), siege, pick: { id: siege.id, kind: 'battle', name: 'Siege in progress', position: siege.position ?? { x: 2250, y: 1462.5 } } };
        this.clashes.set(siege.id, clash); this.battleLayer.addChild(clash.effect.root);
      }
      clash.siege = siege; clash.battle = undefined; this.positionClash(clash); this.picks.push(clash.pick);
    }
    for (const [id, clash] of this.clashes) if (!activeClashes.has(id)) { clash.effect.destroy(); this.clashes.delete(id); }
    for (const siege of world.oasisSieges ?? []) {
      if (!siege.position) continue;
      const p = project(siege.position), root = new Container();
      root.position.set(p.x, p.y - 83);
      this.warfare.addChild(new Graphics().ellipse(p.x, p.y + 4, 76, 42).stroke({color:0xe8754b,width:4,alpha:.85}));
      root.addChild(new Graphics().circle(0,0,17).fill(0xb94432).moveTo(-8,-8).lineTo(8,8).moveTo(8,-8).lineTo(-8,8).stroke({color:0xffefbf,width:4}));
      const name = label(siege.mode === "Blockade" ? "Oasis blockade" : "Oasis battle", 11);
      name.anchor.set(.5,1); name.position.y=-23; root.addChild(name);
      this.warfare.addChild(root); this.effects.push(root);
      this.picks.push({id:siege.id,kind:"battle",name:"Battle in progress",position:{x:siege.position.x,y:siege.position.y-83/.65}});
    }
    this.updateDetail();
    } catch (err) {
      console.error("Map update error:", err);
    }
  }
  private positionClash(clash:{effect:BattleClash;battle?:BattleView;siege?:SiegeView;pick:MapSelection}) {
    const {battle,siege,effect,pick}=clash;
    if (effect.root.destroyed) return;
    if (battle) {
      const world = this.last?.world ?? undefined;
      const leadPoint = (ids: string[] | undefined, fallbackId: string | undefined) => {
        const ordered = [...new Set([...(ids ?? []), ...(fallbackId ? [fallbackId] : [])])];
        for (const id of ordered) {
          const node = this.moving.get(id);
          if (node && !node.root.destroyed) return node.root.position;
        }
        return project(battle.position ?? { x: 2250, y: 1462.5 });
      };
      const attacker = leadPoint(battle.attackerArmyIds, battle.attackerId);
      const defender = leadPoint(battle.defenderArmyIds, battle.defenderId);
      effect.root.position.set((attacker.x + defender.x) / 2, (attacker.y + defender.y) / 2 - 25);
      effect.setSides(this.armyLayout.battleOrientation(battle, world));
      pick.position = { x: effect.root.x, y: effect.root.y / .65 };
    } else if (siege) {
      const world = this.last?.world ?? undefined;
      const villagePos = project(siege.position ?? { x: 2250, y: 1462.5 });
      const ordered = [...new Set([...(siege.armyIds ?? []), ...(siege.armyId ? [siege.armyId] : [])])];
      const leadNode = ordered.map(id => this.moving.get(id)).find(node => node && !node.root.destroyed);
      const siegeSide = this.armyLayout.siegeOrientation(siege, world);
      const attacker = leadNode ? leadNode.root.position : { x: villagePos.x + siegeSide * 88, y: villagePos.y };
      effect.root.position.set((attacker.x + villagePos.x) / 2, (attacker.y + villagePos.y) / 2 - 25);
      effect.setSides(siegeSide);
      pick.position = { x: effect.root.x, y: effect.root.y / .65 };
    }
  }
  private dash(from: Point, to: Point, color: number, width = 2) {
    const length = Math.hypot(to.x - from.x, to.y - from.y);
    for (let step = 0; step < length; step += 12) {
      const end = Math.min(step + 6, length);
      this.paths.moveTo(from.x + (to.x - from.x) * step / length, from.y + (to.y - from.y) * step / length)
        .lineTo(from.x + (to.x - from.x) * end / length, from.y + (to.y - from.y) * end / length).stroke({ color, width, alpha: width > 2 ? .92 : .8 });
    }
  }
  showLoss(armyId: string | undefined, pos: Point, count: number) {
    if (!this.ready || document.hidden || count <= 0) return;
    const root = new Container();
    root.eventMode = 'none';
    const jitterX = (Math.random() - 0.5) * 16;
    root.position.set(pos.x + jitterX, pos.y - 38);
    const caption = new Text({
      text: `-${count}`,
      style: {
        fontFamily: "Tahoma, 'Segoe UI', system-ui, sans-serif",
        fontSize: 16,
        fontWeight: "bold",
        fill: 0xff3838,
        stroke: { color: 0x1f0202, width: 4 },
        align: "center",
      }
    });
    caption.anchor.set(0.5, 0.5);
    root.addChild(caption);
    this.lossLayer.addChild(root);
    this.losses.push({
      root,
      caption,
      at: performance.now(),
      duration: 1500,
      startX: pos.x,
      startY: pos.y - 38,
      jitterX,
      targetArmyId: armyId,
    });
  }
  celebrate(position:Point, message:string){
    if(!this.ready||document.hidden)return;
    if(this.celebrations.length>=5)this.celebrations.shift()!.root.destroy({children:true});
    const root=new Container(),ring=new Graphics(),caption=label(message,18,0xffdf91);
    const p=project(position);root.position.set(p.x,p.y);root.eventMode='none';caption.anchor.set(.5);caption.y=-75;
    root.addChild(ring,caption);this.world.addChild(root);this.celebrations.push({root,ring,caption,at:performance.now()});
  }
  private animate() {
    try {
      const now = performance.now();
      for (const node of this.moving.values()) {
        if (node.root.destroyed) continue;
        const t = Math.min(1, (now - node.at) / 1000);
        node.root.position.set(node.from.x + (node.to.x - node.from.x) * t, node.from.y + (node.to.y - node.from.y) * t);
        node.figures?.forEach(({sprite,y},i)=>{
          if (!sprite.destroyed) {
            sprite.y=y+(node.marching&&!this.reduced&&!document.hidden?Math.sin(now/110+i*1.7)*1.5:0);
          }
        });
        if(node.party && !node.party.destroyed)node.party.y=(node.partyY??0)+(!this.reduced&&!document.hidden?(node.marching?Math.sin(now/110)*1.1:Math.sin(now/1000)*.3):0);
        if(node.flag && !node.flag.destroyed && !this.reduced&&!document.hidden)node.flag.scale.x=.96+Math.sin(now/500)*.04;
        if(node.dust && !node.dust.destroyed){node.dust.clear();if(node.marching&&!this.reduced&&!document.hidden)for(let i=0;i<3;i++){const phase=(now/1100+i/3)%1;node.dust.ellipse(-phase*22,8+phase*5,2+phase*4,1+phase*2).fill({color:0xdac48e,alpha:(1-phase)*.22});}}
      }
      if (!this.reduced && !document.hidden && this.host.clientWidth>0) {
        this.landscape?.animate(now);
        this.smoke.forEach(({sprite,origin,phase})=>{
          if (!sprite.destroyed) {
            const progress=(now/4200+phase)%1;sprite.position.set(origin.x+progress*13,origin.y-progress*29);sprite.scale.set(.5+progress);sprite.alpha=Math.sin(progress*Math.PI)*.19;
          }
        });
        this.oasisRipples.forEach((ripple,i)=>{
          if (!ripple.destroyed) {
            const phase=(now/4400+i*.27)%1;ripple.clear().ellipse(0,0,5+phase*12,2+phase*5).stroke({color:0xd9eed4,width:1,alpha:Math.sin(phase*Math.PI)*.25});
          }
        });
        this.effects.forEach((effect, i) => { if (!effect.destroyed) effect.scale.set(1 + Math.sin(now / 160 + i) * .12); });
        this.flags.forEach((flag, i) => { if (!flag.destroyed) flag.scale.x = .96 + Math.sin(now / 500 + i) * .04; });
        this.clouds.forEach((cloud, i) => { if (!cloud.destroyed) { cloud.x += .04 + i * .01; if (cloud.x > this.host.clientWidth + 50) cloud.x = -50; } });
      }
      if(!document.hidden)for(const fire of this.burningVillages.values())fire.animate(now,this.reduced);
      if(!document.hidden)for(const fire of this.burningOases.values())fire.animate(now,this.reduced);
      if(!document.hidden)for(const smoke of this.smokingVillages.values())smoke.animate(now,this.reduced);
      if(!document.hidden)for(const smoke of this.smokingOases.values())smoke.animate(now,this.reduced);
      for(const clash of this.clashes.values()){
        this.positionClash(clash);
        if(!document.hidden && !clash.effect.root.destroyed)clash.effect.animate(now,this.reduced);
      }
      if(this.reduced){this.smoke.forEach(({sprite})=>{ if (!sprite.destroyed) sprite.alpha=0; });}
      for(const effect of [...this.celebrations]){
        if (effect.root.destroyed) { this.celebrations.splice(this.celebrations.indexOf(effect),1); continue; }
        const progress=(now-effect.at)/2100;
        if(progress>=1){effect.root.destroy({children:true});this.celebrations.splice(this.celebrations.indexOf(effect),1);continue;}
        effect.root.alpha=Math.min(1,(1-progress)*3);effect.caption.y=-75-(this.reduced?0:progress*35);
        effect.ring.clear().ellipse(0,8,65+(this.reduced?0:progress*35),29+(this.reduced?0:progress*17)).stroke({color:0xffdb88,width:3,alpha:1-progress});
      }
      for (const loss of [...this.losses]) {
        if (loss.root.destroyed) { this.losses.splice(this.losses.indexOf(loss), 1); continue; }
        const elapsed = now - loss.at;
        const progress = elapsed / loss.duration;
        if (progress >= 1) {
          loss.root.destroy({ children: true });
          this.losses.splice(this.losses.indexOf(loss), 1);
          continue;
        }
        let scale = 1.0;
        if (elapsed < 120) {
          scale = 0.75 + (elapsed / 120) * 0.5;
        } else if (elapsed < 240) {
          scale = 1.25 - ((elapsed - 120) / 120) * 0.25;
        }
        loss.root.scale.set(scale);

        const floatOffset = this.reduced ? 10 : 40 * Math.pow(Math.min(1, progress), 0.7);
        const armyNode = loss.targetArmyId ? this.moving.get(loss.targetArmyId) : null;
        const baseX = (armyNode && !armyNode.root.destroyed ? armyNode.root.x : loss.startX) + loss.jitterX;
        const baseY = (armyNode && !armyNode.root.destroyed ? armyNode.root.y - 38 : loss.startY) - floatOffset;
        loss.root.position.set(baseX, baseY);
        loss.root.alpha = progress < 0.6 ? 1 : Math.max(0, 1 - (progress - 0.6) / 0.4);
      }
      this.selection.clear();
      const drawnSelected = new Set<string>();
      const drawArmyRing = (armyId: string, color: number, width = 3) => {
        const moving = this.moving.get(armyId);
        if (!moving || moving.root.destroyed) return;
        drawnSelected.add(armyId);
        const pos = { x: moving.root.x, y: moving.root.y };
        if (moving.party && !moving.party.destroyed) {
          try {
            const bounds = moving.party.getLocalBounds(), w = bounds.width * moving.root.scale.x, d = Math.max(18, (bounds.height - 35) * moving.root.scale.y);
            this.selection.ellipse(pos.x, pos.y - d / 2 + 12, w / 2 + 7, d / 2 + 13).stroke({ color, width });
            return;
          } catch { /* fallback below */ }
        }
        this.selection.ellipse(pos.x, pos.y + 5, 26, 14).stroke({ color, width });
      };
      for (const armyId of this.selectedArmyIds) drawArmyRing(armyId, 0x56f09b, 3);
      for (const vid of this.selectedHomeVillageIds) {
        const p = this.players.find(pl => pl.villageId === vid)?.position;
        if (p) {
          const pt = project(p);
          drawnSelected.add(vid);
          this.selection.ellipse(pt.x, pt.y + 5, 80, 40).stroke({ color: 0x56f09b, width: 3.5 })
            .ellipse(pt.x, pt.y + 5, 74, 35).stroke({ color: 0xfff3b0, width: 1.5 });
        }
      }
      if (this.selected && !drawnSelected.has(this.selected.id)) {
        const moving = this.moving.get(this.selected.id);
        const position = (moving && !moving.root.destroyed) ? { x: moving.root.x, y: moving.root.y } : this.selected.position ? project(this.selected.position) : null;
        if (position) {
          if(moving?.party && !moving.party.destroyed){
            try {
              const bounds=moving.party.getLocalBounds(),width=bounds.width*moving.root.scale.x,depth=Math.max(18,(bounds.height-35)*moving.root.scale.y);
              this.selection.ellipse(position.x,position.y-depth/2+12,width/2+6,depth/2+12).stroke({color:0xfff8d1,width:3});
            } catch {
              this.selection.ellipse(position.x, position.y + 5, 24, 13).stroke({ color: 0xfff8d1, width: 3 });
            }
          }else if (this.selected.kind !== 'oasis') this.selection.ellipse(position.x, position.y + 5, this.selected.kind === "village" || this.selected.kind === "castle" ? 78 : 24, this.selected.kind === "village" || this.selected.kind === "castle" ? 39 : 13)
            .stroke({ color: 0xfff8d1, width: 3 });
        }
      }
    } catch (err) {
      console.error("Map animate error:", err);
    }
  }
  private resolveAtClient(clientX: number, clientY: number): MapSelection {
    const rect = this.app.canvas.getBoundingClientRect();
    const p = this.world.toLocal({ x: clientX - rect.left, y: clientY - rect.top });
    let target = this.pickAt(p);
    target ??= { id: "ground", kind: "ground", name: "Open meadow", position: { x: Math.round(Math.max(45, Math.min(4455, p.x))), y: Math.round(Math.max(45, Math.min(4455, p.y / .65))) } };
    if (target.kind === 'ground') target.name = (biomeAt(this.last?.world?.regions ?? [], target.position)?.kind ?? 'Meadow');
    return target;
  }
  private pickAt(p: Point): MapSelection | undefined {
    let selected: MapSelection | undefined, best = 1;
    for (const pick of this.picks) {
      const moving = this.moving.get(pick.id);
      const point = (moving && !moving.root.destroyed) ? moving.root.position : pick.position ? project(pick.position) : null;
      if (!point) continue;
      const dx = p.x - point.x, dy = p.y - point.y;
      let score = Math.hypot(dx, dy) / 42;
      if(moving?.party && !moving.party.destroyed){
        try {
          const local=moving.party.toLocal(p,this.world),bounds=moving.party.getLocalBounds();
          score=Math.min(score,Math.max(Math.abs(local.x-bounds.x-bounds.width/2)/(bounds.width/2+7),Math.abs(local.y-bounds.y-bounds.height/2)/(bounds.height/2+7)));
        } catch { /* ignore unmeasurable or destroyed party */ }
      }
      if (pick.kind === 'village') score = Math.min(Math.hypot(dx / 76, (dy + 28) / 88), Math.hypot(dx / 84, (dy - 58) / 26));
      if (pick.kind === 'castle') score = Math.min(Math.hypot(dx / 92, (dy + 35) / 108), Math.hypot(dx / 88, (dy - 74) / 28));
      if (pick.kind === 'oasis') {
        const marker = this.oasisMarkers.get(pick.id);
        score = Math.min(Math.hypot(dx / 68, (dy + 30) / 88), Math.hypot(dx - 48, dy - 14) / (18 * (marker?.badge.scale.x ?? 1)));
        if (marker?.plate.visible && Math.abs(dx) <= 58 && dy >= 46 && dy <= 70) score = Math.min(score, .8);
      }
      if(pick.kind==='battle')score=Math.hypot(dx,dy)<28?-1:Infinity;
      if (score < best) { best = score; selected = pick; }
    }
    return selected;
  }
  private bind() {
    const canvas = this.app.canvas;
    canvas.addEventListener("contextmenu", event => {
      event.preventDefault();
      try {
        if (!this.rightClickHandler) return;
        const target = this.resolveAtClient(event.clientX, event.clientY);
        this.rightClickHandler(target, { x: event.clientX, y: event.clientY });
      } catch (err) {
        console.error("Contextmenu error:", err);
      }
    });
    canvas.addEventListener("pointerdown", event => {
      if (event.button !== 0) return;
      try {
        canvas.setPointerCapture(event.pointerId);
      } catch { /* ignore */ }
      this.dragging = { x: event.clientX, y: event.clientY, panX: this.pan.x, panY: this.pan.y, moved: false };
    });
    canvas.addEventListener("pointermove", event => {
      try {
        const rect = canvas.getBoundingClientRect(), p = this.world.toLocal({x:event.clientX-rect.left,y:event.clientY-rect.top});
        const target = this.pickAt(p) ?? null;
        const hover=target?this.moving.get(target.id):undefined;
        if(hover?.artworkKey)canvas.dataset.unit=hover.artworkKey.split('|')[0]?.split(',')[0]??'';else delete canvas.dataset.unit;
        if (target?.id !== this.hovered?.id) { this.hovered = target; this.updateDetail(); }
        canvas.style.cursor = this.dragging?.moved ? 'grabbing' : target ? 'pointer' : 'grab';
        if (!this.dragging) {
          this.hoverHandler?.(target ?? this.resolveAtClient(event.clientX, event.clientY), { x: event.clientX, y: event.clientY });
          return;
        }
        const dx = event.clientX - this.dragging.x, dy = event.clientY - this.dragging.y;
        if (Math.hypot(dx, dy) > 5) this.dragging.moved = true;
        if (this.dragging.moved) {
          this.hoverHandler?.(null, null);
          this.cameraMoveHandler?.();
          this.pan.x = Math.max(-6000, Math.min(6000, this.dragging.panX + dx)); this.pan.y = Math.max(-4200, Math.min(4200, this.dragging.panY + dy));
          this.world.position.set((this.host.clientWidth - 4500 * this.scale) / 2 + this.pan.x, this.viewportCenterY - 1462.5 * this.scale + this.pan.y);
        }
      } catch (err) {
        console.error("Pointermove error:", err);
      }
    });
    canvas.addEventListener("pointerup", event => {
      try {
        if (event.button === 0 && this.dragging && !this.dragging.moved) {
          const selected = this.resolveAtClient(event.clientX, event.clientY);
          this.selected = selected; this.updateDetail();
          this.onSelect(selected, { ctrl: event.ctrlKey || event.metaKey, shift: event.shiftKey });
        }
      } catch (err) {
        console.error("Pointerup error:", err);
      } finally {
        this.dragging = null;
        try {
          if (canvas.hasPointerCapture(event.pointerId)) canvas.releasePointerCapture(event.pointerId);
        } catch { /* ignore */ }
        canvas.style.cursor = this.hovered ? 'pointer' : 'grab';
      }
    });
    canvas.addEventListener("pointercancel", () => { this.dragging = null; this.hoverHandler?.(null, null); });
    canvas.addEventListener("pointerleave", () => { this.hovered = null; delete canvas.dataset.unit; this.updateDetail(); this.hoverHandler?.(null, null); });
    canvas.addEventListener("wheel", event => { event.preventDefault(); this.zoom(event.deltaY > 0 ? -.12 : .12); }, { passive: false });
    canvas.addEventListener("keydown", event => {
      if (["+", "="].includes(event.key)) this.zoom(.2);
      else if (event.key === "-") this.zoom(-.2);
      else if (event.key === "Home") this.home();
      else if (event.key.startsWith("Arrow")) { this.pan.x += event.key === "ArrowLeft" ? 25 : event.key === "ArrowRight" ? -25 : 0; this.pan.y += event.key === "ArrowUp" ? 25 : event.key === "ArrowDown" ? -25 : 0; this.fit(); }
      else return;
      event.preventDefault();
    });
  }
}
