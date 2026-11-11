import { Container, Graphics, Sprite, Texture } from 'pixi.js';

let smokeTexture:Texture;
function softSmoke(){
    if(!smokeTexture){
        const canvas=document.createElement('canvas');canvas.width=canvas.height=64;
        const context=canvas.getContext('2d')!,gradient=context.createRadialGradient(32,32,3,32,32,31);
        gradient.addColorStop(0,'rgba(17,18,19,.95)');gradient.addColorStop(.4,'rgba(24,24,25,.85)');gradient.addColorStop(.75,'rgba(30,29,28,.5)');gradient.addColorStop(1,'rgba(30,29,28,0)');
        context.fillStyle=gradient;context.fillRect(0,0,64,64);smokeTexture=Texture.from(canvas);
    }
    const sprite=new Sprite(smokeTexture);sprite.anchor.set(.5);return sprite;
}

// Kept alive across snapshots so the smoke does not restart on every server tick.
export class VillageFire {
    readonly root=new Container();
    private glow=new Graphics();
    private flames:Graphics[]=[];
    private smoke:Sprite[]=[];
    private embers:Graphics[]=[];
    constructor(){
        this.root.eventMode='none';this.root.label='village-demolition-fire';
        this.glow.ellipse(0,-12,62,33).fill({color:0xff650d,alpha:.18});this.root.addChild(this.glow);
        for(let i=0;i<12;i++){
            const puff=softSmoke();
            this.smoke.push(puff);this.root.addChild(puff);
        }
        for(let i=0;i<5;i++){
            const flame=new Graphics()
                .moveTo(-10,0).bezierCurveTo(-18,-17,-2,-23,-4,-42).bezierCurveTo(13,-27,2,-22,12,-11).quadraticCurveTo(18,2,0,5).closePath().fill(0xe7470c)
                .moveTo(-6,0).quadraticCurveTo(-8,-10,1,-27).quadraticCurveTo(1,-13,7,-7).quadraticCurveTo(10,3,0,4).closePath().fill(0xffa323)
                .moveTo(-3,1).quadraticCurveTo(-3,-6,2,-13).quadraticCurveTo(2,-5,4,1).closePath().fill(0xffe18a);
            flame.position.set((i-2)*20,-10+(i%2)*-17);this.flames.push(flame);this.root.addChild(flame);
        }
        for(let i=0;i<9;i++){const ember=new Graphics().circle(0,0,1.5).fill(0xffbb51);this.embers.push(ember);this.root.addChild(ember);}
        this.animate(0,true);
    }
    animate(time:number,reduced:boolean){
        const clock=reduced?1700:time;
        this.glow.alpha=reduced ? .8 : .75+Math.sin(clock/210)*.15;
        this.flames.forEach((flame,i)=>{flame.scale.set(.82+(reduced?0:Math.sin(clock/170+i*2)*.12),.85+(reduced?0:Math.sin(clock/130+i)*.2));flame.skew.x=reduced?0:Math.sin(clock/190+i)*.09;});
        this.smoke.forEach((puff,i)=>{const phase=(clock/3700+i/12)%1;puff.position.set((i%3-1)*19+phase*28,-32-phase*112);puff.scale.set(.4+phase*.95);puff.alpha=Math.sin(phase*Math.PI)*.9;});
        this.embers.forEach((ember,i)=>{const phase=(clock/1700+i/9)%1;ember.visible=!reduced;ember.position.set((i%5-2)*16+Math.sin(i+phase*4)*9,-20-phase*75);ember.alpha=(1-phase)*.8;});
    }
    destroy(){this.root.destroy({children:true});}
}

export class VillageSmoke {
    readonly root=new Container();
    private shadow=new Graphics();
    private smoke:Sprite[]=[];
    constructor(){
        this.root.eventMode='none';this.root.label='village-raid-smoke';
        this.shadow.ellipse(0,-10,54,28).fill({color:0x111213,alpha:.28});this.root.addChild(this.shadow);
        for(let i=0;i<16;i++){
            const puff=softSmoke();
            this.smoke.push(puff);this.root.addChild(puff);
        }
        this.animate(0,true);
    }
    animate(time:number,reduced:boolean){
        const clock=reduced?1700:time;
        this.shadow.alpha=reduced ? .75 : .68+Math.sin(clock/260)*.14;
        this.smoke.forEach((puff,i)=>{
            const phase=(clock/3400+i/16)%1;
            puff.position.set((i%4-1.5)*16+phase*34+Math.sin(i+phase*3)*6,-22-phase*128);
            puff.scale.set(.48+phase*1.1);
            puff.alpha=Math.sin(phase*Math.PI)*.94;
        });
    }
    destroy(){this.root.destroy({children:true});}
}
