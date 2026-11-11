import { Assets, Rectangle, Sprite, Texture } from 'pixi.js';

export const tribes = ['Romans', 'Germans', 'Greeks', 'Mongols', 'Persians'];
export const buildings = ['MainHall', 'Warehouse', 'Barracks', 'Stable', 'SiegeWorkshop', 'Smithy', 'Hospital', 'Wall'];
const atlasWidth=1585, atlasHeight=992;
// Measured transparent gutters: generated rows are not perfectly uniform in height or column spacing.
const rowEdges=[0,202,401,599,784,992];
const columnEdges=[[0,207,394,597,791,988,1189,1381,1585],[0,207,402,593,793,991,1185,1387,1585],[0,208,393,599,793,987,1189,1383,1585],[0,207,393,593,789,987,1182,1380,1585],[0,208,392,594,790,989,1183,1378,1585]];
let buildingsAtlas: Texture, terrainAtlas:Texture, groundAtlas:Texture, oasisAtlas:Texture;
const terrainRows=[0,346,708,1086],terrainColumns=[[0,352,714,1079,1448],[0,354,714,1076,1448],[0,352,714,1076,1448]];
const terrainTextures:Texture[]=[];
const oasisTextures=new Map<string,Texture>();
const frames = new Map<string, Texture>();
export async function loadArt() {
    [buildingsAtlas,terrainAtlas,groundAtlas,oasisAtlas]=await Promise.all([
        Assets.load<Texture>('/art/buildings-v3.png'),Assets.load<Texture>('/art/terrain-v3.png'),Assets.load<Texture>('/art/ground-v5.png'),Assets.load<Texture>('/art/oases-v5.png')
    ]);
    for(let i=0;i<12;i++)terrainTextures[i]=new Texture({source:terrainAtlas.source,frame:terrainFrame(i)});
    ['Wood','Iron','Stone','Wheat'].forEach((r,i)=>oasisTextures.set(r,new Texture({source:oasisAtlas.source,frame:new Rectangle(i*543,60,543,580)})));
}
export function groundTexture(index:number){
    const key=`ground-${index}`;
    if(!frames.has(key))frames.set(key,new Texture({source:groundAtlas.source,frame:new Rectangle(index%2*groundAtlas.width/2,Math.floor(index/2)*groundAtlas.height/2,groundAtlas.width/2,groundAtlas.height/2)}));
    return frames.get(key)!;
}
function terrainFrame(index:number){
    const row=Math.floor(index/4),column=index%4,columns=terrainColumns[row]!;
    return new Rectangle(columns[column]!,terrainRows[row]!,columns[column+1]!-columns[column]!,terrainRows[row+1]!-terrainRows[row]!);
}
function buildingFrame(name:string, tribe:string){
    const row=Math.max(0,tribes.indexOf(tribe)),column=Math.max(0,buildings.indexOf(name));
    const columns=columnEdges[row]!;
    return new Rectangle(columns[column]!,rowEdges[row]!,columns[column+1]!-columns[column]!,rowEdges[row+1]!-rowEdges[row]!);
}
export function terrainSprite(index: number, width: number, x = 0, y = 0) {
    const s = new Sprite(terrainTextures[index]);
    s.anchor.set(.5, .9); s.width = width; s.height = width; s.position.set(x, y); return s;
}
export function buildingSprite(name: string, tribe = 'Romans', width = 120) {
    const key=`${tribe}-${name}`,rect=buildingFrame(name,tribe);
    if(!frames.has(key))frames.set(key,new Texture({source:buildingsAtlas.source,frame:rect}));
    const s=new Sprite(frames.get(key));
    s.anchor.set(.5,.88);s.width=width;s.height=width*rect.height/rect.width;return s;
}
export function oasisSprite(resource:string, width=120) {
    const s=new Sprite(oasisTextures.get(resource)??oasisTextures.get('Wood'));
    s.anchor.set(.5,.8);s.width=width;s.height=width*580/543;return s;
}
function croppedArt(path:string,rect:Rectangle,width:number,height:number,css:string){
    return `<span class="${css}" aria-hidden="true"><svg viewBox="${rect.x} ${rect.y} ${rect.width} ${rect.height}" style="overflow:hidden" xmlns="http://www.w3.org/2000/svg"><image href="${path}" width="${width}" height="${height}"/></svg></span>`;
}
export function buildingArt(name:string,tribe='Romans'){
    if(name==='Market')return '<span class="building-art" aria-hidden="true"><svg viewBox="0 0 160 150" xmlns="http://www.w3.org/2000/svg"><ellipse cx="80" cy="131" rx="68" ry="12" fill="#263b2d" opacity=".2"/><path d="m22 71 63-28 57 25v58l-59 16-61-21Z" fill="#bd9760"/><path d="m83 85 59-17v58l-59 16Z" fill="#876039"/><path d="m15 65 68-35 66 29-7 19-59 19-61-17Z" fill="#b75442"/><path d="m31 57 16-8 17 40-17-5Zm33-17 19-10 5 65-18-3Zm38-2 18 8-11 42-18 5Zm34 15 13 6-7 19-17 5Z" fill="#f1dba7"/><path d="M24 78v43m57-26v43m58-60v46" stroke="#63452c" stroke-width="5"/><path d="m26 108 55 13 55-17v21l-55 16-55-16Z" fill="#b07c47"/><path d="m26 108 55 13 55-17-55-12Z" fill="#d8b57b"/><g fill="#e8c16b" stroke="#967441" stroke-width="1"><circle cx="43" cy="107" r="6"/><circle cx="54" cy="108" r="6"/><circle cx="48" cy="99" r="6"/></g><path d="m92 106 15-14 17 5-8 14Z" fill="#809899"/><path d="m65 106 10-15 12 5-8 15Z" fill="#a5bb70"/><path d="M81 31V9" stroke="#715234" stroke-width="3"/><path d="m82 9 23 4-7 9-16-3Z" fill="#e1bd68"/><rect x="4" y="117" width="21" height="17" rx="3" fill="#b68750"/><path d="m6 120 17 11m-17 0 17-11" stroke="#795333" stroke-width="2"/></svg></span>';
    return croppedArt('/art/buildings-v3.png',buildingFrame(name,tribe),atlasWidth,atlasHeight,'building-art');
}
