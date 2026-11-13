import { localizeNow, t } from './locale';
import type { GameAudio } from './game-audio';

const UI_SCALE_KEY = 'warzone.uiScale';
const clampScale = (value: number) => Math.min(1.25, Math.max(0.75, Math.round(value * 20) / 20));

export class GameMenu {
    private dialog=document.createElement('dialog');
    private pending=false;
    private uiScale=1;
    constructor(private audio:GameAudio,private status:()=>string|null,private leave:()=>Promise<void>){
        try {
            const saved = Number(localStorage.getItem(UI_SCALE_KEY));
            if (Number.isFinite(saved) && saved >= 0.75 && saved <= 1.25) this.uiScale = clampScale(saved);
        } catch { /* Keep default 100% scale */ }
        this.applyUiScale();
        this.dialog.id='game-menu';this.dialog.className='game-menu';
        this.dialog.setAttribute('aria-labelledby','game-menu-title');
        this.dialog.innerHTML=`<header><div><span class="eyebrow">WARZONE</span><h2 id="game-menu-title">Game menu</h2></div><button type="button" id="close-game-menu" aria-label="Close menu">×</button></header>
            <p id="menu-live-note" class="menu-note">The match continues while this menu is open.</p>
            <section class="sound-settings" aria-labelledby="sound-heading"><h3 id="sound-heading">Sound effects</h3>
              <label for="sound-volume"><span>Volume</span><output id="sound-volume-value" for="sound-volume" data-no-translate></output></label>
              <input id="sound-volume" type="range" min="0" max="100" step="1" aria-label="Volume"/>
              <div class="sound-actions"><button type="button" id="sound-mute" aria-pressed="false">Mute sound</button><button type="button" id="sound-preview">Test sound</button></div>
              <p class="menu-note">Battle sounds follow the camera. Attack and territory alerts play wherever you are looking.</p>
            </section>
            <section class="sound-settings display-settings" aria-labelledby="display-heading"><h3 id="display-heading">Display & UI</h3>
              <label for="ui-scale"><span>Interface size</span><output id="ui-scale-value" for="ui-scale" data-no-translate></output></label>
              <input id="ui-scale" type="range" min="75" max="125" step="5" aria-label="Interface size"/>
              <div class="sound-actions"><button type="button" id="ui-scale-reset">Reset size</button></div>
              <p class="menu-note">Scales menus, HUD bars and report panels without shrinking the battlefield map.</p>
            </section>
            <p id="menu-error" role="alert" hidden></p>
            <button type="button" id="return-game" class="primary wide">Return to game</button>
            <button type="button" id="exit-game" class="wide menu-exit">Exit to main menu</button>
            <p id="menu-exit-note" class="menu-note">Your match keeps running after you exit. You can return from the main menu.</p>
            <footer><kbd>ESC</kbd><span>Open or close this menu</span></footer>`;
        document.body.append(this.dialog);
        this.element('close-game-menu').onclick=()=>this.close();this.element('return-game').onclick=()=>this.close();
        this.element<HTMLInputElement>('sound-volume').oninput=event=>{this.audio.setVolume(Number((event.target as HTMLInputElement).value)/100);this.refreshSound();};
        this.element('sound-mute').onclick=()=>{this.audio.setMuted(!this.audio.muted);this.refreshSound();};
        this.element('sound-preview').onclick=()=>void this.audio.preview();
        this.element<HTMLInputElement>('ui-scale').oninput=event=>{this.setUiScale(Number((event.target as HTMLInputElement).value)/100);};
        this.element('ui-scale-reset').onclick=()=>this.setUiScale(1);
        this.element('exit-game').onclick=()=>void this.exit();
        this.dialog.addEventListener('cancel',event=>{event.preventDefault();this.close();});
        this.dialog.addEventListener('click',event=>{
            if(event.target!==this.dialog)return;
            const bounds=this.dialog.getBoundingClientRect();
            if(event.clientX<bounds.left||event.clientX>bounds.right||event.clientY<bounds.top||event.clientY>bounds.bottom)this.close();
        });
        document.addEventListener('keydown',event=>{
            if(event.key!=='Escape')return;
            event.preventDefault();event.stopPropagation();
            if(!event.repeat){if(this.dialog.open)this.close();else this.open();}
        },true);
        window.addEventListener('languagechange',()=>this.refreshSound());
    }
    private element<T extends HTMLElement=HTMLElement>(id:string){return this.dialog.querySelector<T>(`#${id}`)!;}
    private applyUiScale(){
        document.documentElement.style.setProperty('--ui-scale', String(this.uiScale));
    }
    private setUiScale(scale:number){
        this.uiScale = clampScale(scale);
        this.applyUiScale();
        try { localStorage.setItem(UI_SCALE_KEY, String(this.uiScale)); } catch { /* Ignore storage quota errors */ }
        this.refreshDisplay();
    }
    private refreshDisplay(){
        const pct = Math.round(this.uiScale * 100);
        this.element<HTMLInputElement>('ui-scale').value = String(pct);
        this.element('ui-scale-value').textContent = `${pct}%`;
        this.element<HTMLButtonElement>('ui-scale-reset').disabled = pct === 100;
    }
    private refreshSound(){
        const volume=Math.round(this.audio.volume*100);
        this.element<HTMLInputElement>('sound-volume').value=String(volume);
        this.element('sound-volume-value').textContent=`${volume}%`;
        this.element('sound-mute').textContent=t(this.audio.muted?'Unmute sound':'Mute sound');
        this.element('sound-mute').setAttribute('aria-pressed',String(this.audio.muted));
        this.element<HTMLButtonElement>('sound-preview').disabled=this.audio.muted||volume===0;
    }
    open(){
        if(this.dialog.open)return;
        const status=this.status();
        this.element('exit-game').hidden=!status;
        this.element('menu-exit-note').hidden=status!=='Running';
        this.element('menu-live-note').hidden=status!=='Running';
        this.element('return-game').textContent=t(status?'Return to game':'Close menu');
        this.element('menu-error').hidden=true;
        this.refreshSound();this.refreshDisplay();localizeNow(this.dialog);this.dialog.showModal();this.audio.setMenu(true);
        this.element('return-game').focus();
    }
    close(){if(this.pending)return;this.dialog.close();this.audio.setMenu(false);}
    private async exit(){
        if(this.pending)return;this.pending=true;this.element<HTMLButtonElement>('exit-game').disabled=true;
        try{await this.leave();this.pending=false;this.close();}
        catch(error){const text=this.element('menu-error');text.textContent=t(error instanceof Error?error.message:String(error));text.hidden=false;}
        finally{this.pending=false;this.element<HTMLButtonElement>('exit-game').disabled=false;}
    }
}
