import "./admin-overlay.css";
import { currentLanguage } from "./locale";
import { compareUnitIds, unitCatalog } from "./units";

export interface AdminPlayerInfo {
    id: string;
    name: string;
    tribe: string;
    isBot: boolean;
    villageId?: string | null;
    flagColor?: string;
}

export interface AdminRoomInfo {
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

export interface AdminStateView {
    enabled: boolean;
    inRunningMatch: boolean;
    youId: string;
    speedMultiplier: number;
    players: AdminPlayerInfo[];
    rooms: AdminRoomInfo[];
}

export interface AdminHandlers {
    onSetSpeed: (multiplier: number, allBotRooms: boolean) => Promise<void>;
    onGiveResources: (targetPlayerId: string, wood: number, iron: number, stone: number, wheat: number) => Promise<void>;
    onGiveTroops: (targetPlayerId: string, unitId: string, count: number) => Promise<void>;
    onSpawnBotRooms: (count: number, botsPerRoom: number, difficulty: string, speedMultiplier: number, shieldMinutes: number) => Promise<void>;
    onClearBotRooms: () => Promise<void>;
    onSpectateRoom: (roomId: string) => Promise<void>;
    onVisibilityChange?: (open: boolean) => void;
}

export class AdminOverlay {
    private root = document.createElement("aside");
    private header = document.createElement("div");
    private body = document.createElement("div");
    private collapseBtn = document.createElement("button");
    private closeBtn = document.createElement("button");

    private open = false;
    private collapsed = false;
    private busy = false;
    private position: { x: number; y: number } | null = null;
    private latestState: AdminStateView = {
        enabled: false,
        inRunningMatch: false,
        youId: "",
        speedMultiplier: 1,
        players: [],
        rooms: []
    };

    private resTargetSelect!: HTMLSelectElement;
    private woodInput!: HTMLInputElement;
    private ironInput!: HTMLInputElement;
    private stoneInput!: HTMLInputElement;
    private wheatInput!: HTMLInputElement;
    private giveResBtn!: HTMLButtonElement;

    private troopTargetSelect!: HTMLSelectElement;
    private troopUnitSelect!: HTMLSelectElement;
    private troopCountInput!: HTMLInputElement;
    private giveTroopsBtn!: HTMLButtonElement;

    private botCountInput!: HTMLInputElement;
    private botCapacityInput!: HTMLInputElement;
    private botDifficultySelect!: HTMLSelectElement;
    private botSpeedSelect!: HTMLSelectElement;
    private botShieldSelect!: HTMLSelectElement;
    private botRoomsList!: HTMLDivElement;
    private speedButtons = new Map<number, HTMLButtonElement>();
    private applyAllBotsCheckbox!: HTMLInputElement;
    private inMatchSection!: HTMLDivElement;
    private statusMsg!: HTMLDivElement;

    private lastPlayersKey = "";
    private lastTroopTribe = "";
    private lastRoomsKey = "";

    constructor(private readonly handlers: AdminHandlers) {
        this.buildDom();
        this.bindEvents();
        this.syncVisibility();
    }

    isOpen(): boolean {
        return this.open;
    }

    setOpen(open: boolean): void {
        if (!this.latestState.enabled && open) return;
        this.open = open;
        if (open) this.refreshLanguageAndState();
        this.syncVisibility();
        this.handlers.onVisibilityChange?.(this.open);
    }

    toggle(): void {
        this.setOpen(!this.open);
    }

    update(next: AdminStateView): void {
        this.latestState = next;
        if (!next.enabled && this.open) {
            this.open = false;
            this.syncVisibility();
        }
        this.refreshDynamicControls();
    }

    refreshLanguageAndState(): void {
        this.lastPlayersKey = "";
        this.lastTroopTribe = "";
        this.lastRoomsKey = "";
        this.updateLabels();
        this.refreshDynamicControls();
    }

    private isAr(): boolean {
        return currentLanguage() === "ar";
    }

    private L(ar: string, en: string): string {
        return this.isAr() ? ar : en;
    }

    private buildDom(): void {
        this.root.id = "admin-overlay";
        this.root.className = "admin-overlay";
        this.root.hidden = true;
        this.root.setAttribute("data-no-translate", "1");

        this.header.className = "admin-header";
        const title = document.createElement("div");
        title.className = "admin-title";
        title.innerHTML = `<span>⚡ Admin &amp; Cheats</span><span class="admin-badge">ADMIN</span>`;

        const controls = document.createElement("div");
        controls.className = "admin-controls";
        this.collapseBtn.type = "button";
        this.collapseBtn.textContent = "−";
        this.closeBtn.type = "button";
        this.closeBtn.textContent = "×";
        controls.append(this.collapseBtn, this.closeBtn);
        this.header.append(title, controls);

        this.body.className = "admin-body";

        // 1. Speed Control Section
        const speedSec = document.createElement("div");
        speedSec.className = "admin-section";
        speedSec.innerHTML = `<div class="admin-section-title"><span data-lbl="speed-title"></span></div>`;
        const speedBar = document.createElement("div");
        speedBar.className = "admin-speed-bar";
        for (const mult of [1, 5, 10, 15, 20]) {
            const btn = document.createElement("button");
            btn.type = "button";
            btn.className = "admin-btn";
            btn.textContent = `x${mult}`;
            btn.addEventListener("click", () => void this.runAction(async () => {
                await this.handlers.onSetSpeed(mult, this.applyAllBotsCheckbox.checked);
            }));
            this.speedButtons.set(mult, btn);
            speedBar.append(btn);
        }
        const allBotsRow = document.createElement("label");
        allBotsRow.className = "admin-row admin-muted";
        this.applyAllBotsCheckbox = document.createElement("input");
        this.applyAllBotsCheckbox.type = "checkbox";
        this.applyAllBotsCheckbox.style.width = "auto";
        const allBotsText = document.createElement("span");
        allBotsText.dataset.lbl = "all-bots-speed";
        allBotsRow.append(this.applyAllBotsCheckbox, allBotsText);
        speedSec.append(speedBar, allBotsRow);

        // 2. Resources & Troops Section (in-match)
        this.inMatchSection = document.createElement("div");
        this.inMatchSection.className = "admin-section";
        this.inMatchSection.innerHTML = `<div class="admin-section-title"><span data-lbl="cheats-title"></span><span class="admin-muted" data-lbl="match-only"></span></div>`;

        const resTargetWrap = document.createElement("label");
        resTargetWrap.className = "admin-field";
        resTargetWrap.innerHTML = `<span data-lbl="target-player"></span>`;
        this.resTargetSelect = document.createElement("select");
        resTargetWrap.append(this.resTargetSelect);

        const resGrid = document.createElement("div");
        resGrid.className = "admin-grid-4";
        this.woodInput = this.numInput(10000);
        this.ironInput = this.numInput(10000);
        this.stoneInput = this.numInput(10000);
        this.wheatInput = this.numInput(10000);
        resGrid.append(
            this.labeledField("res-wood", this.woodInput),
            this.labeledField("res-iron", this.ironInput),
            this.labeledField("res-stone", this.stoneInput),
            this.labeledField("res-wheat", this.wheatInput)
        );

        const resBtnRow = document.createElement("div");
        resBtnRow.className = "admin-row";
        this.giveResBtn = document.createElement("button");
        this.giveResBtn.type = "button";
        this.giveResBtn.className = "admin-btn primary";
        this.giveResBtn.dataset.lbl = "give-res";
        const quick50k = document.createElement("button");
        quick50k.type = "button";
        quick50k.className = "admin-btn";
        quick50k.textContent = "+50k All";
        quick50k.addEventListener("click", () => {
            this.woodInput.value = "50000";
            this.ironInput.value = "50000";
            this.stoneInput.value = "50000";
            this.wheatInput.value = "50000";
            void this.runAction(() => this.handlers.onGiveResources(this.resTargetSelect.value, 50000, 50000, 50000, 50000));
        });
        this.giveResBtn.addEventListener("click", () => void this.runAction(() =>
            this.handlers.onGiveResources(
                this.resTargetSelect.value,
                Number(this.woodInput.value) || 0,
                Number(this.ironInput.value) || 0,
                Number(this.stoneInput.value) || 0,
                Number(this.wheatInput.value) || 0
            )
        ));
        resBtnRow.append(this.giveResBtn, quick50k);

        // Troops sub-block
        const troopGrid = document.createElement("div");
        troopGrid.className = "admin-grid-2";
        this.troopTargetSelect = document.createElement("select");
        this.troopUnitSelect = document.createElement("select");
        troopGrid.append(
            this.labeledField("troop-target", this.troopTargetSelect),
            this.labeledField("troop-unit", this.troopUnitSelect)
        );

        const troopActions = document.createElement("div");
        troopActions.className = "admin-row";
        this.troopCountInput = this.numInput(200);
        this.troopCountInput.style.width = "84px";
        this.giveTroopsBtn = document.createElement("button");
        this.giveTroopsBtn.type = "button";
        this.giveTroopsBtn.className = "admin-btn primary";
        this.giveTroopsBtn.dataset.lbl = "give-troops";
        this.giveTroopsBtn.addEventListener("click", () => void this.runAction(() =>
            this.handlers.onGiveTroops(
                this.troopTargetSelect.value,
                this.troopUnitSelect.value,
                Math.max(1, Math.min(20000, Math.floor(Number(this.troopCountInput.value) || 1)))
            )
        ));
        for (const q of [100, 500, 2000]) {
            const qb = document.createElement("button");
            qb.type = "button";
            qb.className = "admin-btn";
            qb.textContent = `+${q}`;
            qb.addEventListener("click", () => {
                this.troopCountInput.value = String(q);
                void this.runAction(() => this.handlers.onGiveTroops(this.troopTargetSelect.value, this.troopUnitSelect.value, q));
            });
            troopActions.append(qb);
        }
        troopActions.prepend(this.troopCountInput, this.giveTroopsBtn);

        this.inMatchSection.append(resTargetWrap, resGrid, resBtnRow, troopGrid, troopActions);

        // 3. Bot Stress-Test Rooms Section
        const botSec = document.createElement("div");
        botSec.className = "admin-section";
        botSec.innerHTML = `<div class="admin-section-title"><span data-lbl="bot-rooms-title"></span></div>`;

        const botPresets = document.createElement("div");
        botPresets.className = "admin-row";
        for (const preset of [1, 15, 25, 50]) {
            const pb = document.createElement("button");
            pb.type = "button";
            pb.className = "admin-btn";
            pb.textContent = `${preset}`;
            pb.addEventListener("click", () => { this.botCountInput.value = String(preset); });
            botPresets.append(pb);
        }

        const botGrid = document.createElement("div");
        botGrid.className = "admin-grid-2";
        this.botCountInput = this.numInput(15, 1, 50);
        this.botCapacityInput = this.numInput(8, 8, 16);
        this.botDifficultySelect = document.createElement("select");
        for (const diff of ["Easy", "Normal", "Hard"]) {
            const opt = document.createElement("option");
            opt.value = diff;
            opt.textContent = diff;
            if (diff === "Normal") opt.selected = true;
            this.botDifficultySelect.append(opt);
        }
        this.botSpeedSelect = document.createElement("select");
        for (const sp of [1, 5, 10, 15, 20]) {
            const opt = document.createElement("option");
            opt.value = String(sp);
            opt.textContent = `x${sp}`;
            if (sp === 10) opt.selected = true;
            this.botSpeedSelect.append(opt);
        }
        this.botShieldSelect = document.createElement("select");
        for (const sh of [0, 5, 10]) {
            const opt = document.createElement("option");
            opt.value = String(sh);
            opt.textContent = `${sh} min`;
            if (sh === 0) opt.selected = true;
            this.botShieldSelect.append(opt);
        }

        botGrid.append(
            this.labeledField("bot-room-count", this.botCountInput),
            this.labeledField("bot-per-room", this.botCapacityInput),
            this.labeledField("bot-diff", this.botDifficultySelect),
            this.labeledField("bot-speed", this.botSpeedSelect),
            this.labeledField("bot-shield", this.botShieldSelect)
        );

        const botBtns = document.createElement("div");
        botBtns.className = "admin-row";
        const spawnBtn = document.createElement("button");
        spawnBtn.type = "button";
        spawnBtn.className = "admin-btn primary";
        spawnBtn.dataset.lbl = "spawn-bots";
        spawnBtn.addEventListener("click", () => void this.runAction(() =>
            this.handlers.onSpawnBotRooms(
                Math.max(1, Math.min(50, Math.floor(Number(this.botCountInput.value) || 15))),
                Math.max(8, Math.min(16, Math.floor(Number(this.botCapacityInput.value) || 8))),
                this.botDifficultySelect.value,
                Number(this.botSpeedSelect.value) || 1,
                Number(this.botShieldSelect.value) || 0
            )
        ));
        const clearBtn = document.createElement("button");
        clearBtn.type = "button";
        clearBtn.className = "admin-btn danger";
        clearBtn.dataset.lbl = "clear-bots";
        clearBtn.addEventListener("click", () => void this.runAction(() => this.handlers.onClearBotRooms()));
        botBtns.append(spawnBtn, clearBtn);

        this.botRoomsList = document.createElement("div");
        this.botRoomsList.className = "admin-bot-rooms";

        botSec.append(botPresets, botGrid, botBtns, this.botRoomsList);

        this.statusMsg = document.createElement("div");
        this.statusMsg.className = "admin-muted";

        this.body.append(speedSec, this.inMatchSection, botSec, this.statusMsg);
        this.root.append(this.header, this.body);
        document.body.append(this.root);

        this.updateLabels();
    }

    private numInput(value: number, min = 0, max = 10000000): HTMLInputElement {
        const input = document.createElement("input");
        input.type = "number";
        input.min = String(min);
        input.max = String(max);
        input.value = String(value);
        return input;
    }

    private labeledField(lblKey: string, control: HTMLElement): HTMLLabelElement {
        const wrap = document.createElement("label");
        wrap.className = "admin-field";
        const span = document.createElement("span");
        span.dataset.lbl = lblKey;
        wrap.append(span, control);
        return wrap;
    }

    private bindEvents(): void {
        this.collapseBtn.addEventListener("click", () => {
            this.collapsed = !this.collapsed;
            this.syncVisibility();
        });
        this.closeBtn.addEventListener("click", () => this.setOpen(false));

        this.resTargetSelect.addEventListener("change", () => {
            if (this.troopTargetSelect.value !== this.resTargetSelect.value) {
                this.troopTargetSelect.value = this.resTargetSelect.value;
                this.syncTroopUnitsForSelectedPlayer();
            }
        });
        this.troopTargetSelect.addEventListener("change", () => {
            if (this.resTargetSelect.value !== this.troopTargetSelect.value) {
                this.resTargetSelect.value = this.troopTargetSelect.value;
            }
            this.syncTroopUnitsForSelectedPlayer();
        });

        let dragging = false;
        let offsetX = 0;
        let offsetY = 0;
        this.header.addEventListener("pointerdown", e => {
            if ((e.target as HTMLElement).closest("button")) return;
            dragging = true;
            const rect = this.root.getBoundingClientRect();
            offsetX = e.clientX - rect.left;
            offsetY = e.clientY - rect.top;
            this.header.setPointerCapture(e.pointerId);
        });
        this.header.addEventListener("pointermove", e => {
            if (!dragging) return;
            const x = Math.max(8, Math.min(window.innerWidth - 160, e.clientX - offsetX));
            const y = Math.max(8, Math.min(window.innerHeight - 60, e.clientY - offsetY));
            this.position = { x, y };
            this.root.style.left = `${x}px`;
            this.root.style.top = `${y}px`;
            this.root.style.right = "auto";
        });
        this.header.addEventListener("pointerup", () => { dragging = false; });
        this.header.addEventListener("pointercancel", () => { dragging = false; });
    }

    private async runAction(fn: () => Promise<void>): Promise<void> {
        if (this.busy) return;
        this.busy = true;
        this.statusMsg.textContent = this.L("جاري التنفيذ...", "Applying...");
        try {
            await fn();
            this.statusMsg.textContent = this.L("تم التنفيذ بنجاح ✓", "Applied successfully ✓");
        } catch (err) {
            this.statusMsg.textContent = String(err);
        } finally {
            this.busy = false;
        }
    }

    private updateLabels(): void {
        const dict: Record<string, string> = {
            "speed-title": this.L("⚡ سرعة الجيم", "⚡ Game Speed"),
            "all-bots-speed": this.L("تطبيق على جميع رومات البوتات النشطة", "Apply to all active bot rooms"),
            "cheats-title": this.L("🎁 الموارد والجنود الفوري", "🎁 Instant Resources & Troops"),
            "match-only": this.L("(أثناء اللعب فقط)", "(In running match)"),
            "target-player": this.L("اللاعب المستهدف", "Target player"),
            "res-wood": this.L("خشب", "Wood"),
            "res-iron": this.L("حديد", "Iron"),
            "res-stone": this.L("حجر", "Stone"),
            "res-wheat": this.L("قمح", "Wheat"),
            "give-res": this.L("إرسال الموارد", "Give Resources"),
            "troop-target": this.L("جنود للاعب", "Troops for"),
            "troop-unit": this.L("الوحدة (نفس الشعب)", "Unit (Same Tribe)"),
            "give-troops": this.L("إرسال الجنود", "Spawn Troops"),
            "bot-rooms-title": this.L("🤖 إنشاء وتشغيل رومات بوتات (تست الضغط 15-50)", "🤖 Bot-Only Stress Test Rooms (15-50)"),
            "bot-room-count": this.L("عدد الرومات (1-50)", "Rooms (1-50)"),
            "bot-per-room": this.L("بوتات/روم (8-16)", "Bots/room (8-16)"),
            "bot-diff": this.L("الصعوبة", "Difficulty"),
            "bot-speed": this.L("السرعة الابتدائية", "Initial Speed"),
            "bot-shield": this.L("مدة الحماية", "Shield"),
            "spawn-bots": this.L("🚀 إنشاء وبدء تلقائي", "🚀 Spawn & Auto-Start"),
            "clear-bots": this.L("🗑 حذف رومات البوتات", "🗑 Clear Bot Rooms")
        };
        this.root.querySelectorAll<HTMLElement>("[data-lbl]").forEach(el => {
            const key = el.dataset.lbl;
            if (key && dict[key]) el.textContent = dict[key]!;
        });
    }

    private syncVisibility(): void {
        this.root.hidden = !this.open || !this.latestState.enabled;
        this.root.classList.toggle("is-collapsed", this.collapsed);
        this.body.hidden = this.collapsed;
        this.collapseBtn.textContent = this.collapsed ? "+" : "−";
        if (this.position) {
            this.root.style.left = `${this.position.x}px`;
            this.root.style.top = `${this.position.y}px`;
            this.root.style.right = "auto";
        }
    }

    private syncTroopUnitsForSelectedPlayer(): void {
        const targetId = this.troopTargetSelect.value;
        const player = this.latestState.players.find(p => p.id === targetId);
        const tribe = player?.tribe ?? "";
        if (tribe === this.lastTroopTribe && this.troopUnitSelect.options.length > 0) return;
        this.lastTroopTribe = tribe;

        const units = Array.from(unitCatalog.values())
            .filter(u => !tribe || u.tribeType.toLowerCase() === tribe.toLowerCase())
            .sort((a, b) => compareUnitIds(a.unitId, b.unitId));

        this.troopUnitSelect.innerHTML = "";
        for (const u of units) {
            const opt = document.createElement("option");
            opt.value = u.unitId;
            opt.textContent = `${u.displayName} (${u.role})`;
            this.troopUnitSelect.append(opt);
        }
    }

    private refreshDynamicControls(): void {
        const s = this.latestState;
        for (const [mult, btn] of this.speedButtons) {
            btn.classList.toggle("active", Math.abs(s.speedMultiplier - mult) < 0.1);
        }

        const alivePlayers = s.players.filter(p => !!p.villageId);
        const inMatch = s.inRunningMatch && alivePlayers.length > 0;
        this.inMatchSection.style.opacity = inMatch ? "1" : "0.55";
        this.giveResBtn.disabled = !inMatch;
        this.giveTroopsBtn.disabled = !inMatch;
        this.resTargetSelect.disabled = !inMatch;
        this.troopTargetSelect.disabled = !inMatch;
        this.troopUnitSelect.disabled = !inMatch;

        const playersKey = alivePlayers.map(p => `${p.id}:${p.name}:${p.tribe}:${p.isBot ? 1 : 0}`).join("|");
        if (playersKey !== this.lastPlayersKey) {
            this.lastPlayersKey = playersKey;
            const prevRes = this.resTargetSelect.value;
            const prevTroop = this.troopTargetSelect.value;
            const fill = (sel: HTMLSelectElement, prev: string) => {
                sel.innerHTML = "";
                for (const p of alivePlayers) {
                    const opt = document.createElement("option");
                    opt.value = p.id;
                    opt.textContent = `${p.id === s.youId ? "★ " : p.isBot ? "🤖 " : ""}${p.name} (${p.tribe})`;
                    sel.append(opt);
                }
                const preferred = alivePlayers.some(p => p.id === prev) ? prev
                    : alivePlayers.some(p => p.id === s.youId) ? s.youId
                    : alivePlayers[0]?.id ?? "";
                if (preferred) sel.value = preferred;
            };
            fill(this.resTargetSelect, prevRes);
            fill(this.troopTargetSelect, prevTroop);
            this.lastTroopTribe = "";
        }
        this.syncTroopUnitsForSelectedPlayer();

        const botRooms = s.rooms.filter(r => r.isBotOnly || (r.humans === 0 && r.bots >= 2));
        const roomsKey = botRooms.map(r => `${r.id}:${r.status}:${r.speedMultiplier}:${r.spectators ?? 0}`).join("|");
        if (roomsKey !== this.lastRoomsKey) {
            this.lastRoomsKey = roomsKey;
            this.botRoomsList.innerHTML = "";
            if (!botRooms.length) {
                const empty = document.createElement("div");
                empty.className = "admin-muted";
                empty.textContent = this.L("لا توجد رومات بوتات حالياً.", "No active bot-only rooms.");
                this.botRoomsList.append(empty);
            } else {
                const header = document.createElement("div");
                header.className = "admin-muted";
                header.textContent = this.L(`رومات البوتات النشطة: ${botRooms.length}`, `Active bot rooms: ${botRooms.length}`);
                this.botRoomsList.append(header);
                for (const r of botRooms.slice(0, 12)) {
                    const row = document.createElement("div");
                    row.className = "admin-bot-room-item";
                    row.innerHTML = `<span>🤖 <strong>${r.name}</strong> · ${r.bots} AI · x${r.speedMultiplier ?? 1} (${r.status})</span>`;
                    const watch = document.createElement("button");
                    watch.type = "button";
                    watch.className = "admin-btn";
                    watch.textContent = this.L("مشاهدة 👁", "Watch 👁");
                    watch.addEventListener("click", () => void this.runAction(() => this.handlers.onSpectateRoom(r.id)));
                    row.append(watch);
                    this.botRoomsList.append(row);
                }
            }
        }
    }
}
