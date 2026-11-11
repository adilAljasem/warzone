import { Container, Graphics } from 'pixi.js';

function sword() {
    return new Graphics()
        .poly([-3,-9,-4,-39,0,-47,4,-39,3,-9]).fill(0xdce8e9).stroke({color:0x344b50,width:1.5})
        .poly([0,-43,3,-38,2,-10,0,-10]).fill(0x8ea6af)
        .roundRect(-10,-11,20,5,2).fill(0xe6bb63).stroke({color:0x73542e,width:1})
        .roundRect(-2.5,-6,5,13,1).fill(0x79513b)
        .circle(0,8,4).fill(0xe6bb63).stroke({color:0x73542e,width:1});
}

// Persistent across snapshots; the clock drives a swing, contact flash and recoil.
export class BattleClash {
    readonly root=new Container();
    private left=sword();
    private right=sword();
    private sparks=new Graphics();
    constructor() {
        this.root.label='battle-clash';this.root.eventMode='none';
        this.root.addChild(new Graphics().ellipse(0,-3,32,29).fill({color:0x24352c,alpha:.55}));
        this.left.position.set(-17,20);this.right.position.set(17,20);
        this.root.addChild(this.left,this.right,this.sparks);
        this.sparks.position.set(0,-5);
        for(let i=0;i<8;i++){
            const angle=i*Math.PI/4;
            this.sparks.moveTo(Math.cos(angle)*5,Math.sin(angle)*5).lineTo(Math.cos(angle)*13,Math.sin(angle)*13);
        }
        this.sparks.stroke({color:0xffe8a5,width:2}).circle(0,0,3).fill(0xfffbde);
        this.animate(0,true);
    }
    animate(now:number,reduced:boolean) {
        const phase=(now%1100)/1100;
        const swing=reduced?1:phase<.32?Math.pow(phase/.32,2):phase<.43?1:Math.pow((1-phase)/.57,2);
        this.left.rotation=-.12+swing*.82;this.right.rotation=.12-swing*.82;
        this.sparks.alpha=reduced?0:phase>=.32&&phase<.51?1-(phase-.32)/.19:0;
        this.sparks.scale.set(1+(1-this.sparks.alpha)*.5);
    }
    destroy(){this.root.destroy({children:true});}
}
