import { SoundEvents, type SoundCue, type SoundState, type CombatSound } from './audio-events';

// Procedural effects use inharmonic metal tones, filtered impacts and short horn motifs.
// No network requests or music loops are needed, and audio starts only after a gesture.
export class GameAudio {
    private context:AudioContext|null=null;
    private master:GainNode|null=null;
    private noise:AudioBuffer|null=null;
    private voices=new Set<AudioScheduledSourceNode>();
    private ambientVoices=new Set<AudioScheduledSourceNode>();
    private creatingAmbient=false;
    private events=new SoundEvents();
    private scene:()=>CombatSound[]=()=>[];
    private active=false;
    private menu=false;
    private nextClash=0;
    private lastCue=new Map<SoundCue,number>();
    private timer:number;
    volume=.65;
    muted=false;
    constructor(){
        try{
            const stored=JSON.parse(localStorage.getItem('warzone.audio')??'null');
            if(typeof stored?.volume==='number'&&Number.isFinite(stored.volume))this.volume=Math.max(0,Math.min(1,stored.volume));
            this.muted=stored?.muted===true;
        }catch{/* Audio preferences are optional. */}
        document.addEventListener('pointerdown',this.gesture,{capture:true});
        document.addEventListener('keydown',this.gesture,{capture:true});
        document.addEventListener('visibilitychange',this.visibility);
        this.timer=window.setInterval(()=>this.ambience(),180);
    }
    private gesture=()=>{void this.unlock();};
    private visibility=()=>{if(document.hidden)this.stopVoices();this.applyVolume();};
    async unlock(){
        if(this.muted||this.volume===0||document.hidden)return;
        try{
            if(!this.context){
                if(typeof AudioContext==='undefined')return;
                const context=this.context=new AudioContext();
                const compressor=context.createDynamicsCompressor();compressor.threshold.value=-12;compressor.ratio.value=5;compressor.connect(context.destination);
                this.master=context.createGain();this.master.connect(compressor);this.applyVolume();
                this.noise=context.createBuffer(1,context.sampleRate,context.sampleRate);
                const data=this.noise.getChannelData(0);for(let i=0;i<data.length;i++)data[i]=Math.random()*2-1;
            }
            if(this.context.state==='suspended')await this.context.resume();
        }catch{/* The game remains usable if the browser has no audio output. */}
    }
    setScene(scene:()=>CombatSound[]){this.scene=scene;}
    setMenu(open:boolean){this.menu=open;this.nextClash=0;if(open)this.stopVoices();}
    setVolume(value:number){this.volume=Math.max(0,Math.min(1,Number.isFinite(value)?value:0));this.save();this.applyVolume();if(this.volume===0)this.stopVoices();else void this.unlock();}
    setMuted(value:boolean){this.muted=value;this.save();this.applyVolume();if(value)this.stopVoices();else void this.unlock();}
    private save(){try{localStorage.setItem('warzone.audio',JSON.stringify({volume:this.volume,muted:this.muted}));}catch{/* Keep the current session settings. */}}
    private applyVolume(){
        if(!this.context||!this.master)return;
        const value=this.muted||document.hidden?0:this.volume;
        this.master.gain.cancelScheduledValues(this.context.currentTime);
        this.master.gain.setTargetAtTime(value,this.context.currentTime,.025);
    }
    update(state:SoundState|null){
        this.active=state?.status==='Running'&&!!state.world;
        if(!this.active)this.stopVoices();
        const cues=this.events.update(state);
        cues.forEach((cue,i)=>this.play(cue,i*.85));
    }
    reset(){this.active=false;this.events.reset();this.lastCue.clear();this.stopVoices();}
    private audible(){return !!this.context&&this.context.state==='running'&&!this.muted&&this.volume>0&&!document.hidden;}
    private envelope(source:AudioScheduledSourceNode,at:number,duration:number,level:number,pan:number,filter?:BiquadFilterNode,attack=.008){
        const context=this.context!,gain=context.createGain(),panner=context.createStereoPanner();
        gain.gain.setValueAtTime(.0001,at);gain.gain.exponentialRampToValueAtTime(Math.max(.0002,level),at+attack);gain.gain.exponentialRampToValueAtTime(.0001,at+duration);
        if(filter){source.connect(filter);filter.connect(gain);}else source.connect(gain);
        gain.connect(panner);panner.pan.value=Math.max(-.95,Math.min(.95,pan));panner.connect(this.master!);
        this.voices.add(source);if(this.creatingAmbient)this.ambientVoices.add(source);
        source.onended=()=>{this.voices.delete(source);this.ambientVoices.delete(source);source.disconnect();gain.disconnect();panner.disconnect();filter?.disconnect();};
        source.start(at);source.stop(at+duration+.015);
    }
    private tone(frequency:number,at:number,duration:number,level:number,pan=0,type:OscillatorType='sine',end=frequency){
        const oscillator=this.context!.createOscillator();oscillator.type=type;oscillator.frequency.setValueAtTime(frequency,at);oscillator.frequency.exponentialRampToValueAtTime(end,at+duration);
        this.envelope(oscillator,at,duration,level,pan);
    }
    private impact(at:number,level:number,pan:number,frequency:number,duration=.16){
        const noise=this.context!.createBufferSource(),filter=this.context!.createBiquadFilter();noise.buffer=this.noise;
        filter.type='bandpass';filter.frequency.value=frequency;filter.Q.value=.7;
        this.envelope(noise,at,duration,level,pan,filter);
    }
    private noiseSweep(at:number,duration:number,level:number,pan:number,startFreq:number,endFreq:number,q:number,type:BiquadFilterType='bandpass',attack=.003){
        const noise=this.context!.createBufferSource(),filter=this.context!.createBiquadFilter();noise.buffer=this.noise;
        filter.type=type;filter.Q.value=q;
        filter.frequency.setValueAtTime(startFreq,at);
        filter.frequency.exponentialRampToValueAtTime(Math.max(40,endFreq),at+duration);
        this.envelope(noise,at,duration,level,pan,filter,attack);
    }
    private steelTone(frequency:number,at:number,duration:number,level:number,pan:number){
        const context=this.context!,oscillator=context.createOscillator(),filter=context.createBiquadFilter();
        oscillator.type='triangle';
        oscillator.frequency.setValueAtTime(frequency*1.028,at);
        oscillator.frequency.exponentialRampToValueAtTime(frequency,at+.016);
        oscillator.frequency.exponentialRampToValueAtTime(frequency*.992,at+duration);
        filter.type='highpass';filter.frequency.value=2100;filter.Q.value=1.2;
        this.envelope(oscillator,at,duration,level,pan,filter,.0018);
    }
    private swordStrike(at:number,level:number,pan:number,pitch:number,ring=.18,scrape=true){
        // Fast air cut (blade whoosh) leading into the steel collision
        this.noiseSweep(at,.036,level*.16,pan,950*pitch,2900*pitch,2.1,'bandpass',.012);
        const hit=at+.016;
        // Crisp high-frequency steel edge impact ("ching")
        this.noiseSweep(hit,.042,level*.34,pan,4600*pitch,6400*pitch,4.5,'bandpass',.0015);
        // Metallic blade-on-blade slide/scrape ("shhhk-ring")
        if(scrape)this.noiseSweep(hit+.005,Math.min(.11,ring*.6),level*.25,pan,2750*pitch,5900*pitch,5.8,'bandpass',.003);
        // High tempered-steel blade partials with subtle detuned shimmer (no low mining thuds)
        const modes=[2420,3480,4960,6640];
        modes.forEach((freq,i)=>{
            const f=freq*pitch,d=ring*(1-i*.15),amp=level*(.062/(1+i*.48));
            this.steelTone(f,hit,d,amp,pan);
            if(i<2)this.steelTone(f*(i===0?1.007:.994),hit+.002,d*.88,amp*.65,pan);
        });
    }
    private clash(at:number,level:number,pan:number){
        const basePitch=.9+Math.random()*.22;
        const pattern=Math.floor(Math.random()*3);
        const spread=(offset:number)=>Math.max(-.9,Math.min(.9,pan+offset));
        if(pattern===0){
            // Double sword parry & riposte
            this.swordStrike(at,level,spread(-.1),basePitch,.15,true);
            const riposte=at+.085+Math.random()*.04;
            this.swordStrike(riposte,level*.92,spread(.1),basePitch*1.14,.24,true);
        }else if(pattern===1){
            // Rapid 3-hit crossing sword flurry
            this.swordStrike(at,level*.88,spread(-.12),basePitch*.95,.11,false);
            const second=at+.07+Math.random()*.025;
            this.swordStrike(second,level*.9,spread(.09),basePitch*1.08,.13,true);
            const third=second+.08+Math.random()*.03;
            this.swordStrike(third,level,spread(-.03),basePitch*1.21,.26,true);
        }else{
            // Heavy blade bind, steel grind & counter-slash
            this.swordStrike(at,level,spread(-.08),basePitch,.19,true);
            this.noiseSweep(at+.032,.13,level*.28,spread(0),2400*basePitch,6800*basePitch,6.5,'bandpass',.005);
            const counter=at+.165+Math.random()*.035;
            this.swordStrike(counter,level*.95,spread(.11),basePitch*1.17,.22,true);
        }
    }
    play(cue:SoundCue,delay=0){
        if(!this.audible())return;
        const context=this.context!,at=context.currentTime+delay+.01;
        if((this.lastCue.get(cue)??-Infinity)>context.currentTime-1.1)return;
        this.lastCue.set(cue,context.currentTime);
        if(cue==='attack'){
            [0,.23,.52].forEach((offset,i)=>{
                const duration=i===2?.48:.18;
                this.tone(164.8,at+offset,duration,.15,0,'triangle',196);
                this.tone(329.6,at+offset,duration,.035,0,'sine',392);
                this.impact(at+offset,.16,0,170,.16);
            });
        }else{
            const notes=cue==='village-captured'?[261.63,329.63,392,523.25]:cue==='oasis-captured'?[523.25,659.25,783.99]:[329.63,277.18,220];
            notes.forEach((frequency,i)=>{
                const start=at+i*.16,duration=i===notes.length-1?.65:.28;
                this.tone(frequency,start,duration,.12,0,'triangle');
                this.tone(frequency*2,start,duration,.024);
            });
            if(cue==='oasis-lost')this.tone(110,at,.8,.1);
        }
    }
    async playMatchSearchStart(){
        await this.unlock();
        if(!this.audible()||!this.context)return;
        const at=this.context.currentTime+.015;
        // Soft rising sonar/radar chime signaling matchmaking search start
        this.tone(440,at,.18,.11,0,'sine',493.88);
        this.tone(880,at,.14,.025,0,'triangle',987.77);
        this.tone(587.33,at+.11,.28,.12,0,'sine',659.25);
        this.tone(1174.66,at+.11,.22,.024,0,'sine',1318.51);
        this.noiseSweep(at,.16,.03,0,1200,2600,2.5,'bandpass',.015);
    }
    async playMatchFound(){
        await this.unlock();
        if(!this.audible()||!this.context)return;
        const at=this.context.currentTime+.015;
        // Crisp war-drum pulse + bright ascending ready fanfare when match is found
        this.impact(at,.18,0,150,.18);
        this.tone(130.81,at,.28,.13,0,'triangle',98);
        const chord=[523.25,659.25,783.99,1046.5];
        chord.forEach((freq,i)=>{
            const start=at+i*.085;
            const dur=i===chord.length-1?.75:.32;
            this.tone(freq,start,dur,.14,0,'triangle');
            this.tone(freq*2,start,dur*.8,.032,0,'sine');
        });
    }
    async preview(){
        await this.unlock();
        if(this.audible()&&this.context)this.clash(this.context.currentTime+.01,.8,0);
        this.lastCue.delete('oasis-captured');this.play('oasis-captured',.4);
    }
    private ambience(){
        if(!this.active||this.menu||!this.audible())return;
        const sources=this.scene().sort((a,b)=>b.level-a.level).slice(0,3);
        if(!sources.length){this.stopAmbient();return;}
        const now=this.context!.currentTime;if(now<this.nextClash)return;
        const source=sources[Math.floor(Math.random()*sources.length)]!;
        this.creatingAmbient=true;
        try{
            this.clash(now+.01,source.level,source.pan);
            if(sources.length>1&&Math.random()<.55){
                const secondary=sources.find(s=>s!==source)??source;
                this.swordStrike(now+.14+Math.random()*.08,secondary.level*.72,secondary.pan,.95+Math.random()*.22,.16,true);
            }
        }finally{this.creatingAmbient=false;}
        this.nextClash=now+.28+Math.random()*.32;
    }
    private stopAmbient(){for(const source of this.ambientVoices){try{source.stop();}catch{/* A completed source is already silent. */}this.voices.delete(source);}this.ambientVoices.clear();this.nextClash=0;}
    private stopVoices(){for(const source of this.voices){try{source.stop();}catch{/* A completed source is already silent. */}}this.voices.clear();this.ambientVoices.clear();this.nextClash=0;}
    destroy(){clearInterval(this.timer);this.stopVoices();document.removeEventListener('pointerdown',this.gesture,true);document.removeEventListener('keydown',this.gesture,true);document.removeEventListener('visibilitychange',this.visibility);void this.context?.close();}
}
