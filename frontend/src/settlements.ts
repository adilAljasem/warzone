import { Container, Graphics } from 'pixi.js';
import { buildingSprite, terrainSprite } from './art';

export const settlementStage=(level:number)=>level>=12?'City':level>=6?'Town':'Village';
function cottage(tribe:string,x:number,y:number,size=1){
    const sprite=buildingSprite('Warehouse',tribe,62*size);sprite.position.set(x,y);return sprite;
}
export function settlementArt(tribe:string,level:number,own:boolean){
    const root=new Container(),stage=settlementStage(level),radius=stage==='Village'?45:stage==='Town'?58:71;
    root.addChild(new Graphics().ellipse(0,7,radius+10,radius*.46).fill({color:0x99895e,alpha:.12}).ellipse(0,9,radius,radius*.42).stroke({color:own?0xe5d292:0xb8ae88,width:1.5,alpha:own?.65:.12}));
    if(stage==='Village'){
        root.addChild(cottage(tribe,-17,-7,.9),cottage(tribe,23,10,.7),cottage(tribe,-27,22,.6));
        root.addChild(terrainSprite(6,39,38,22));
    }else{
        const hall=buildingSprite('MainHall',tribe,stage==='Town'?100:123);hall.position.set(0,-3);root.addChild(hall);
        root.addChild(cottage(tribe,-44,15,.75),cottage(tribe,44,12,.72));
        if(stage==='City'){
            root.addChild(cottage(tribe,-47,-17,.65),cottage(tribe,45,-19,.65));
            const wall=buildingSprite('Wall',tribe,99);wall.position.set(1,34);root.addChild(wall);
        }else root.addChild(terrainSprite(6,43,41,28));
    }
    return root;
}
