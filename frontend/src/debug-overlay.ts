import { currentLanguage } from "./locale";
import "./debug-overlay.css";

export interface ServerDebugTelemetry {
    clientTimestamp: number;
    serverTickTargetHz: number;
    serverTickActualHz: number;
    lastTickMs: number;
    avgTickMs: number;
    maxTickMs: number;
    lastBroadcastMs: number;
    lastTickCatchupSteps: number;
    totalTicks: number;
    overrunTicks: number;
    activeRooms: number;
    runningRooms: number;
    connectedClients: number;
}

export interface DebugEntityCounts {
    players: number;
    settlements: number;
    armies: number;
    convoys: number;
    oases: number;
    combats: number;
    aux: number;
}

export interface DebugContext {
    connectionState: () => string;
    roomId: () => string | null;
    roomStatus: () => string | null;
    revision: () => number | null;
    pendingActions: () => number;
    deferredSnapshots: () => number;
    entities: () => DebugEntityCounts;
    sendPing: (clientTimestamp: number) => Promise<ServerDebugTelemetry>;
}

interface ByteSample {
    at: number;
    bytes: number;
}

interface DragOffset {
    x: number;
    y: number;
}

const OPEN_KEY = "warzone.debugOpen";
const COLLAPSE_KEY = "warzone.debugCollapsed";
const POS_KEY = "warzone.debugPos";
const RATE_WINDOW_MS = 5000;
const PING_INTERVAL_MS = 1000;
const RENDER_INTERVAL_MS = 350;
const SNAPSHOT_EXPECTED_MS = 1000;
const SNAPSHOT_STALL_MS = 1650;

const encoder = new TextEncoder();

function measureJsonBytes(value: unknown): number {
    if (value === undefined || value === null) return 4;
    try {
        return encoder.encode(JSON.stringify(value)).length;
    } catch {
        return 0;
    }
}

function formatBytes(bytes: number): string {
    if (bytes < 1024) return `${Math.round(bytes)} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}

function formatRate(bytesPerSec: number): string {
    if (bytesPerSec < 1024) return `${Math.round(bytesPerSec)} B/s`;
    return `${(bytesPerSec / 1024).toFixed(1)} KB/s`;
}

export class DebugOverlay {
    private root = document.createElement("aside");
    private strip = document.createElement("div");
    private body = document.createElement("div");
    private dot = document.createElement("span");
    private titleLabel = document.createElement("strong");
    private collapseBtn = document.createElement("button");
    private resetBtn = document.createElement("button");
    private closeBtn = document.createElement("button");

    private open = false;
    private collapsed = false;
    private pingTimer = 0;
    private renderTimer = 0;
    private rafId = 0;
    private pingInFlight = false;
    private position: DragOffset | null = null;

    private lastFrameAt = 0;
    private frameAvgMs = 16.7;
    private frameMaxMs = 16.7;
    private fps = 0;
    private fpsWindowFrames = 0;
    private fpsWindowStart = 0;

    private pingLastMs: number | null = null;
    private pingAvgMs: number | null = null;
    private pingMinMs = Infinity;
    private pingMaxMs = 0;
    private jitterMs = 0;
    private serverTelemetry: ServerDebugTelemetry | null = null;

    private rxSamples: ByteSample[] = [];
    private txSamples: ByteSample[] = [];
    private rxTotalBytes = 0;
    private txTotalBytes = 0;
    private lastSnapshotBytes = 0;
    private lastSnapshotAt = 0;
    private snapshotIntervalMs = 0;
    private staleDrops = 0;
    private reconnects = 0;
    private rpcSent = 0;
    private rpcFailed = 0;
    private expectedSnapshots = 0;
    private missedSnapshots = 0;
    private wasHidden = document.hidden;

    constructor(private readonly ctx: DebugContext, private readonly onToggle?: (open: boolean) => void) {
        try {
            this.open = localStorage.getItem(OPEN_KEY) === "1";
            this.collapsed = localStorage.getItem(COLLAPSE_KEY) === "1";
            const savedPos = localStorage.getItem(POS_KEY);
            if (savedPos) {
                const parsed = JSON.parse(savedPos) as Partial<DragOffset>;
                if (typeof parsed.x === "number" && typeof parsed.y === "number") this.position = { x: parsed.x, y: parsed.y };
            }
        } catch { /* Ignore storage access errors */ }

        this.buildDom();
        this.bindEvents();
        this.applyPosition();
        this.syncVisibility();
    }

    isOpen(): boolean {
        return this.open;
    }

    toggle(force?: boolean): boolean {
        const next = force ?? !this.open;
        if (next === this.open) return this.open;
        this.open = next;
        try { localStorage.setItem(OPEN_KEY, this.open ? "1" : "0"); } catch { /* Ignore */ }
        this.syncVisibility();
        this.onToggle?.(this.open);
        return this.open;
    }

    reset(): void {
        this.rxSamples = [];
        this.txSamples = [];
        this.rxTotalBytes = 0;
        this.txTotalBytes = 0;
        this.lastSnapshotBytes = 0;
        this.snapshotIntervalMs = 0;
        this.pingLastMs = null;
        this.pingAvgMs = null;
        this.pingMinMs = Infinity;
        this.pingMaxMs = 0;
        this.jitterMs = 0;
        this.frameMaxMs = this.frameAvgMs;
        this.staleDrops = 0;
        this.reconnects = 0;
        this.rpcSent = 0;
        this.rpcFailed = 0;
        this.expectedSnapshots = 0;
        this.missedSnapshots = 0;
        if (this.open) this.render();
    }

    recordInbound(kind: string, payload: unknown, wireBytes?: number): void {
        if (!this.open && kind !== "Snapshot") return;
        const now = performance.now();
        const bytes = this.open ? (wireBytes ?? measureJsonBytes(payload)) : 0;
        if (bytes > 0) {
            this.rxTotalBytes += bytes;
            this.rxSamples.push({ at: now, bytes });
            this.pruneSamples(now);
        }

        if (kind === "Snapshot") {
            if (bytes > 0) this.lastSnapshotBytes = bytes;
            if (this.lastSnapshotAt > 0 && this.ctx.roomStatus() === "Running" && !this.wasHidden && !document.hidden) {
                const gap = now - this.lastSnapshotAt;
                this.snapshotIntervalMs = this.snapshotIntervalMs === 0 ? gap : this.snapshotIntervalMs * 0.75 + gap * 0.25;
                if (gap > SNAPSHOT_STALL_MS) {
                    const missed = Math.max(1, Math.round(gap / SNAPSHOT_EXPECTED_MS) - 1);
                    this.missedSnapshots += missed;
                    this.expectedSnapshots += missed + 1;
                } else {
                    this.expectedSnapshots += 1;
                }
            } else if (this.ctx.roomStatus() === "Running") {
                this.expectedSnapshots += 1;
            }
            this.lastSnapshotAt = now;
        }
    }

    recordOutbound(_kind: string, payload?: unknown): void {
        if (!this.open) return;
        const now = performance.now();
        const bytes = payload === undefined ? 32 : measureJsonBytes(payload) + 24;
        this.txTotalBytes += bytes;
        this.txSamples.push({ at: now, bytes });
        this.pruneSamples(now);
    }

    recordRpcOutcome(ok: boolean): void {
        this.rpcSent++;
        if (!ok) this.rpcFailed++;
    }

    recordStaleDrop(): void {
        this.staleDrops++;
    }

    recordReconnect(): void {
        this.reconnects++;
    }

    private buildDom(): void {
        this.root.id = "debug-overlay";
        this.root.className = "debug-overlay";
        this.root.setAttribute("data-no-translate", "");
        this.root.setAttribute("aria-label", "Network & Performance Telemetry");

        const header = document.createElement("header");
        header.className = "debug-header";

        const titleWrap = document.createElement("div");
        titleWrap.className = "debug-title";
        this.dot.className = "debug-dot";
        const kbd = document.createElement("kbd");
        kbd.textContent = "F3";
        titleWrap.append(this.dot, this.titleLabel, kbd);

        const actions = document.createElement("div");
        actions.className = "debug-actions";
        this.resetBtn.type = "button";
        this.resetBtn.textContent = "↺";
        this.collapseBtn.type = "button";
        this.closeBtn.type = "button";
        this.closeBtn.textContent = "×";
        actions.append(this.resetBtn, this.collapseBtn, this.closeBtn);

        header.append(titleWrap, actions);

        this.strip.className = "debug-strip";
        this.body.className = "debug-body";

        this.root.append(header, this.strip, this.body);
        document.body.append(this.root);

        this.enableDrag(header);
    }

    private bindEvents(): void {
        this.resetBtn.onclick = e => { e.stopPropagation(); this.reset(); };
        this.collapseBtn.onclick = e => {
            e.stopPropagation();
            this.collapsed = !this.collapsed;
            try { localStorage.setItem(COLLAPSE_KEY, this.collapsed ? "1" : "0"); } catch { /* Ignore */ }
            this.root.classList.toggle("is-collapsed", this.collapsed);
            this.updateChromeLabels();
            if (this.open) this.render();
        };
        this.closeBtn.onclick = e => { e.stopPropagation(); this.toggle(false); };

        document.addEventListener("visibilitychange", () => {
            if (!document.hidden) {
                this.lastSnapshotAt = 0;
                this.wasHidden = true;
                window.setTimeout(() => { this.wasHidden = document.hidden; }, 1500);
            } else {
                this.wasHidden = true;
            }
        });

        window.addEventListener("keydown", event => {
            if (event.key !== "F3") return;
            event.preventDefault();
            if (!event.repeat) this.toggle();
        });

        window.addEventListener("languagechange", () => {
            this.updateChromeLabels();
            if (this.open) this.render();
        });

        window.addEventListener("resize", () => this.applyPosition());
    }

    private enableDrag(handle: HTMLElement): void {
        let startX = 0, startY = 0, originX = 0, originY = 0, dragging = false;
        handle.addEventListener("pointerdown", event => {
            if ((event.target as HTMLElement).closest("button")) return;
            dragging = true;
            const rect = this.root.getBoundingClientRect();
            startX = event.clientX;
            startY = event.clientY;
            originX = rect.left;
            originY = rect.top;
            handle.setPointerCapture(event.pointerId);
        });
        handle.addEventListener("pointermove", event => {
            if (!dragging) return;
            this.position = {
                x: originX + (event.clientX - startX),
                y: originY + (event.clientY - startY)
            };
            this.applyPosition();
        });
        const finishDrag = () => {
            if (!dragging) return;
            dragging = false;
            if (this.position) {
                try { localStorage.setItem(POS_KEY, JSON.stringify(this.position)); } catch { /* Ignore */ }
            }
        };
        handle.addEventListener("pointerup", finishDrag);
        handle.addEventListener("pointercancel", finishDrag);
    }

    private applyPosition(): void {
        if (!this.position) return;
        const maxW = Math.max(24, window.innerWidth - 360);
        const maxH = Math.max(24, window.innerHeight - 80);
        this.position.x = Math.max(8, Math.min(maxW, this.position.x));
        this.position.y = Math.max(8, Math.min(maxH, this.position.y));
        this.root.style.left = `${this.position.x}px`;
        this.root.style.top = `${this.position.y}px`;
    }

    private updateChromeLabels(): void {
        const isAr = currentLanguage() === "ar";
        this.titleLabel.textContent = isAr ? "نافذة المراقبة والشبكة" : "Telemetry & Net Debug";
        this.resetBtn.title = isAr ? "تصفير العدادات" : "Reset counters";
        this.collapseBtn.textContent = this.collapsed ? "+" : "−";
        this.collapseBtn.title = isAr ? (this.collapsed ? "توسيع" : "تصغير") : (this.collapsed ? "Expand" : "Collapse");
        this.closeBtn.title = isAr ? "إغلاق (F3)" : "Close (F3)";
    }

    private syncVisibility(): void {
        this.root.hidden = !this.open;
        this.root.classList.toggle("is-collapsed", this.collapsed);
        this.updateChromeLabels();

        window.clearInterval(this.pingTimer);
        window.clearInterval(this.renderTimer);
        window.cancelAnimationFrame(this.rafId);
        this.pingTimer = 0;
        this.renderTimer = 0;
        this.rafId = 0;

        if (!this.open) return;

        this.lastFrameAt = performance.now();
        this.fpsWindowStart = this.lastFrameAt;
        this.fpsWindowFrames = 0;

        const onFrame = (now: number) => {
            if (!this.open) return;
            const dt = now - this.lastFrameAt;
            this.lastFrameAt = now;
            if (dt > 0 && dt < 2000) {
                this.frameAvgMs = this.frameAvgMs * 0.9 + dt * 0.1;
                if (dt > this.frameMaxMs) this.frameMaxMs = dt;
                else this.frameMaxMs = this.frameMaxMs * 0.985 + dt * 0.015;
            }
            this.fpsWindowFrames++;
            const span = now - this.fpsWindowStart;
            if (span >= 500) {
                this.fps = Math.round((this.fpsWindowFrames * 1000) / span);
                this.fpsWindowFrames = 0;
                this.fpsWindowStart = now;
            }
            this.rafId = window.requestAnimationFrame(onFrame);
        };
        this.rafId = window.requestAnimationFrame(onFrame);

        void this.probePing();
        this.pingTimer = window.setInterval(() => void this.probePing(), PING_INTERVAL_MS);
        this.render();
        this.renderTimer = window.setInterval(() => this.render(), RENDER_INTERVAL_MS);
    }

    private async probePing(): Promise<void> {
        if (!this.open || this.pingInFlight || this.ctx.connectionState() !== "Connected") return;
        this.pingInFlight = true;
        const sentAt = performance.now();
        this.recordOutbound("Ping", sentAt);
        try {
            const telemetry = await this.ctx.sendPing(sentAt);
            const rtt = Math.max(0.1, performance.now() - sentAt);
            this.recordInbound("Ping", telemetry);
            this.recordRpcOutcome(true);
            if (this.pingLastMs !== null) {
                const delta = Math.abs(rtt - this.pingLastMs);
                this.jitterMs = this.jitterMs === 0 ? delta : this.jitterMs * 0.75 + delta * 0.25;
            }
            this.pingLastMs = rtt;
            this.pingAvgMs = this.pingAvgMs === null ? rtt : this.pingAvgMs * 0.75 + rtt * 0.25;
            if (rtt < this.pingMinMs) this.pingMinMs = rtt;
            if (rtt > this.pingMaxMs) this.pingMaxMs = rtt;
            this.serverTelemetry = telemetry;
        } catch {
            this.recordRpcOutcome(false);
            this.pingLastMs = null;
        } finally {
            this.pingInFlight = false;
        }
    }

    private pruneSamples(now: number): void {
        const cutoff = now - RATE_WINDOW_MS;
        while (this.rxSamples.length && this.rxSamples[0]!.at < cutoff) this.rxSamples.shift();
        while (this.txSamples.length && this.txSamples[0]!.at < cutoff) this.txSamples.shift();
    }

    private rates(now: number): { rxPerSec: number; txPerSec: number } {
        this.pruneSamples(now);
        const windowSec = RATE_WINDOW_MS / 1000;
        const rxSum = this.rxSamples.reduce((s, item) => s + item.bytes, 0);
        const txSum = this.txSamples.reduce((s, item) => s + item.bytes, 0);
        return { rxPerSec: rxSum / windowSec, txPerSec: txSum / windowSec };
    }

    private lossSummary(): { pct: number; lost: number; total: number } {
        const lost = this.rpcFailed + this.missedSnapshots;
        const total = this.rpcSent + this.expectedSnapshots;
        if (total <= 0) return { pct: 0, lost: 0, total: 0 };
        return { pct: Math.min(100, (lost / total) * 100), lost, total };
    }

    private healthClass(): "good" | "warn" | "bad" {
        if (this.ctx.connectionState() !== "Connected") return "bad";
        const loss = this.lossSummary().pct;
        const ping = this.pingLastMs ?? 0;
        const tickMs = this.serverTelemetry?.lastTickMs ?? 0;
        if (loss >= 5 || ping >= 180 || tickMs >= 200) return "bad";
        if (loss >= 1 || ping >= 75 || tickMs >= 100 || (this.fps > 0 && this.fps < 35)) return "warn";
        return "good";
    }

    private render(): void {
        const now = performance.now();
        const isAr = currentLanguage() === "ar";
        const connState = this.ctx.connectionState();
        const { rxPerSec, txPerSec } = this.rates(now);
        const loss = this.lossSummary();
        const entities = this.ctx.entities();
        const totalEntities = entities.players + entities.settlements + entities.armies + entities.convoys + entities.oases + entities.combats;
        const srv = this.serverTelemetry;

        const health = this.healthClass();
        this.dot.className = `debug-dot ${health}`;

        const pingClass = this.pingLastMs === null ? "bad" : this.pingLastMs < 60 ? "good" : this.pingLastMs < 140 ? "warn" : "bad";
        const fpsClass = this.fps === 0 ? "muted" : this.fps >= 50 ? "good" : this.fps >= 30 ? "warn" : "bad";
        const lossClass = loss.pct === 0 ? "good" : loss.pct < 2 ? "warn" : "bad";
        const tickClass = !srv ? "muted" : srv.lastTickMs < 80 ? "good" : srv.lastTickMs < 180 ? "warn" : "bad";

        const pingLabel = this.pingLastMs === null ? "—" : `${Math.round(this.pingLastMs)} ms`;
        const tickLabel = srv ? `${srv.lastTickMs.toFixed(1)} ms` : "—";

        this.strip.innerHTML = `
            <span><label>FPS</label><b class="debug-val ${fpsClass}">${this.fps || "—"}</b></span>
            <span><label>PING</label><b class="debug-val ${pingClass}">${pingLabel}</b></span>
            <span><label>LOSS</label><b class="debug-val ${lossClass}">${loss.pct.toFixed(1)}%</b></span>
            <span><label>SRV</label><b class="debug-val ${tickClass}">${tickLabel}</b></span>`;

        if (this.collapsed) return;

        const snapAgeMs = this.lastSnapshotAt > 0 ? Math.max(0, now - this.lastSnapshotAt) : null;
        const rev = this.ctx.revision();
        const roomId = this.ctx.roomId();
        const roomStatus = this.ctx.roomStatus();
        const pending = this.ctx.pendingActions();
        const deferred = this.ctx.deferredSnapshots();

        const memInfo = (performance as Performance & { memory?: { usedJSHeapSize: number; jsHeapSizeLimit: number } }).memory;
        const heapText = memInfo ? `${Math.round(memInfo.usedJSHeapSize / (1024 * 1024))} / ${Math.round(memInfo.jsHeapSizeLimit / (1024 * 1024))} MB` : "N/A";
        const domNodes = document.getElementsByTagName("*").length;

        const row = (label: string, val: string, cls = "") =>
            `<div class="debug-row"><span>${label}</span><span class="debug-val ${cls}">${val}</span></div>`;

        const chip = (label: string, n: number) =>
            `<span class="debug-chip"><span>${label}</span><b>${n}</b></span>`;

        this.body.innerHTML = `
            <section class="debug-section">
                <h4>${isAr ? "الشبكة والبنج" : "Network & Latency"}<span>${connState}</span></h4>
                <div class="debug-grid">
                    ${row(isAr ? "البنج (RTT)" : "Ping (RTT)", this.pingLastMs === null ? "—" : `${this.pingLastMs.toFixed(1)} ms`, pingClass)}
                    ${row(isAr ? "التذبذب (Jitter)" : "Jitter", this.pingLastMs === null ? "—" : `±${this.jitterMs.toFixed(1)} ms`)}
                    ${row(isAr ? "المتوسط / أقصى" : "Avg / Max", this.pingAvgMs === null ? "—" : `${Math.round(this.pingAvgMs)} / ${Math.round(this.pingMaxMs)} ms`)}
                    ${row(isAr ? "فقدان الحزم" : "Packet Loss", `${loss.pct.toFixed(1)}% (${loss.lost}/${loss.total})`, lossClass)}
                    ${row(isAr ? "استقبال (RX)" : "Bandwidth RX", formatRate(rxPerSec))}
                    ${row(isAr ? "إرسال (TX)" : "Bandwidth TX", formatRate(txPerSec))}
                    ${row(isAr ? "حجم اللقطة" : "Snapshot Size", this.lastSnapshotBytes ? formatBytes(this.lastSnapshotBytes) : "—")}
                    ${row(isAr ? "إجمالي الجلسة" : "Total RX / TX", `${formatBytes(this.rxTotalBytes)} / ${formatBytes(this.txTotalBytes)}`, "muted")}
                </div>
            </section>
            <section class="debug-section">
                <h4>${isAr ? "فريم السيرفر والمزامنة" : "Server Frame & Sync"}<span>${srv ? `${srv.serverTickActualHz.toFixed(1)} / ${srv.serverTickTargetHz.toFixed(0)} Hz` : "—"}</span></h4>
                <div class="debug-grid">
                    ${row(isAr ? "فريم السيرفر" : "Server Tick", srv ? `${srv.lastTickMs.toFixed(2)} ms` : "—", tickClass)}
                    ${row(isAr ? "متوسط / ذروة" : "Avg / Max Tick", srv ? `${srv.avgTickMs.toFixed(2)} / ${srv.maxTickMs.toFixed(2)} ms` : "—")}
                    ${row(isAr ? "زمن البث" : "Broadcast", srv ? `${srv.lastBroadcastMs.toFixed(2)} ms` : "—")}
                    ${row(isAr ? "تأخر / تكرار" : "Overruns / Ticks", srv ? `${srv.overrunTicks} / ${srv.totalTicks}` : "—", srv && srv.overrunTicks > 0 ? "warn" : "")}
                    ${row(isAr ? "الغرف / المتصلون" : "Rooms / Clients", srv ? `${srv.runningRooms}/${srv.activeRooms} · ${srv.connectedClients}` : "—")}
                    ${row(isAr ? "فاصل اللقطات" : "Snap Interval", this.snapshotIntervalMs > 0 ? `${Math.round(this.snapshotIntervalMs)} ms` : "—")}
                    ${row(isAr ? "عمر اللقطة" : "Snap Age", snapAgeMs !== null ? `${Math.round(snapAgeMs)} ms` : "—", snapAgeMs !== null && snapAgeMs > 1800 ? "warn" : "")}
                    ${row(isAr ? "مرفوضة / إعادة" : "Stale / Reconn", `${this.staleDrops} / ${this.reconnects}`, this.staleDrops > 0 || this.reconnects > 0 ? "warn" : "")}
                </div>
            </section>
            <section class="debug-section">
                <h4>${isAr ? "كيانات الشبكة" : "Network Entities"}<span>${totalEntities} ${isAr ? "كيان" : "synced"}</span></h4>
                <div class="debug-entities-chips">
                    ${chip(isAr ? "قرى" : "Villages", entities.settlements)}
                    ${chip(isAr ? "جيوش" : "Armies", entities.armies)}
                    ${chip(isAr ? "قوافل" : "Convoys", entities.convoys)}
                    ${chip(isAr ? "واحات" : "Oases", entities.oases)}
                    ${chip(isAr ? "معارك/حصار" : "Combats", entities.combats)}
                    ${chip(isAr ? "لاعبين" : "Players", entities.players)}
                    ${chip(isAr ? "فرعي" : "Aux", entities.aux)}
                </div>
            </section>
            <section class="debug-section">
                <h4>${isAr ? "أداء الكلاينت واللعبة" : "Client & Match"}<span>${roomId ? `${roomId} #${rev ?? 0}` : (isAr ? "بلا غرفة" : "No room")}</span></h4>
                <div class="debug-grid">
                    ${row(isAr ? "فريمات الشاشة" : "Client FPS", `${this.fps || "—"} fps`, fpsClass)}
                    ${row(isAr ? "زمن الفريم" : "Frame (Avg/Max)", `${this.frameAvgMs.toFixed(1)} / ${this.frameMaxMs.toFixed(1)} ms`)}
                    ${row(isAr ? "طابور الأوامر" : "Cmd / Held Snap", `${pending} / ${deferred}`, pending > 0 ? "warn" : "")}
                    ${row(isAr ? "حالة الغرفة" : "Match Status", roomStatus ?? "Menu")}
                    ${row(isAr ? "الذاكرة (Heap)" : "JS Heap", heapText, "muted")}
                    ${row(isAr ? "عناصر DOM" : "DOM Nodes", `${domNodes} · DPR ${window.devicePixelRatio.toFixed(2)}`, "muted")}
                </div>
            </section>`;
    }
}
