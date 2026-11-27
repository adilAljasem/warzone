import { currentLanguage } from "./locale";

interface BeforeInstallPromptEvent extends Event {
    prompt(): Promise<void>;
    userChoice: Promise<{ outcome: "accepted" | "dismissed" }>;
}

export function isAndroidDevice(): boolean {
    return typeof navigator !== "undefined" && /Android/i.test(navigator.userAgent);
}

export function isIOSDevice(): boolean {
    if (typeof navigator === "undefined") return false;
    return /iPhone|iPad|iPod/i.test(navigator.userAgent)
        || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
}

function isBrowserFullscreenActive(): boolean {
    if (typeof document === "undefined") return false;
    const doc = document as Document & { webkitFullscreenElement?: Element | null };
    return Boolean(doc.fullscreenElement || doc.webkitFullscreenElement);
}

export function isStandaloneMobileApp(): boolean {
    if (typeof window === "undefined") return false;
    const nav = navigator as Navigator & { standalone?: boolean };
    const installedPwaFullscreen = (window.matchMedia?.("(display-mode: fullscreen)").matches ?? false)
        && !isBrowserFullscreenActive();
    return Boolean(
        nav.standalone === true
        || window.matchMedia?.("(display-mode: standalone)").matches
        || installedPwaFullscreen
    );
}

export function isMobilePhone(): boolean {
    if (typeof window === "undefined") return false;
    const coarse = window.matchMedia?.("(pointer: coarse)").matches ?? false;
    const touch = coarse || navigator.maxTouchPoints > 0 || isAndroidDevice() || isIOSDevice();
    const shortSide = Math.min(window.innerWidth, window.innerHeight);
    return touch && shortSide > 0 && shortSide <= 560;
}

export function isMobileLandscape(): boolean {
    return isMobilePhone() && window.innerWidth >= window.innerHeight;
}

export interface MobileShellHandle {
    refreshLanguage(): void;
    setMatchActive(active: boolean): void;
}

export function initMobileShell(onLayoutChange?: () => void): MobileShellHandle {
    let deferredInstallPrompt: BeforeInstallPromptEvent | null = null;
    let matchActive = false;
    let portraitDismissed = false;
    let wakeLock: { release(): Promise<void> } | null = null;

    const root = document.documentElement;
    const rotateGate = document.getElementById("mobile-rotate-gate");
    const installBar = document.getElementById("mobile-install-bar");
    const iosModal = document.getElementById("ios-install-modal");
    const fullscreenBtn = document.getElementById("toggle-fullscreen") as HTMLButtonElement | null;

    function supportsFullscreen(): boolean {
        const el = document.documentElement as HTMLElement & { webkitRequestFullscreen?: () => Promise<void> | void };
        return Boolean(el.requestFullscreen || el.webkitRequestFullscreen);
    }

    async function requestLandscapeFullscreen(): Promise<void> {
        const docEl = document.documentElement as HTMLElement & { webkitRequestFullscreen?: () => Promise<void> | void };
        try {
            if (!isBrowserFullscreenActive()) {
                if (docEl.requestFullscreen) await docEl.requestFullscreen({ navigationUI: "hide" } as FullscreenOptions);
                else if (docEl.webkitRequestFullscreen) await docEl.webkitRequestFullscreen();
            }
        } catch {
            // Some mobile browsers (especially iOS Safari) disallow Fullscreen API.
        }
        try {
            const orientation = screen.orientation as ScreenOrientation & { lock?: (mode: string) => Promise<void> };
            if (orientation?.lock) await orientation.lock("landscape");
        } catch {
            // Physical rotation handles orientation when lock is unsupported.
        }
        syncViewportClasses();
    }

    async function toggleFullscreen(): Promise<void> {
        const doc = document as Document & { webkitExitFullscreen?: () => Promise<void> | void };
        if (isBrowserFullscreenActive()) {
            try {
                if (doc.exitFullscreen) await doc.exitFullscreen();
                else if (doc.webkitExitFullscreen) await doc.webkitExitFullscreen();
            } catch { /* Ignore */ }
        } else {
            await requestLandscapeFullscreen();
        }
        syncFullscreenButton();
    }

    async function syncWakeLock(): Promise<void> {
        const nav = navigator as Navigator & { wakeLock?: { request(type: "screen"): Promise<{ release(): Promise<void> }> } };
        if (!nav.wakeLock || !isMobilePhone()) return;
        if (matchActive && !document.hidden) {
            if (!wakeLock) {
                try { wakeLock = await nav.wakeLock.request("screen"); } catch { /* Optional */ }
            }
        } else if (wakeLock) {
            const current = wakeLock;
            wakeLock = null;
            try { await current.release(); } catch { /* Optional */ }
        }
    }

    function syncFullscreenButton(): void {
        if (!fullscreenBtn) return;
        const show = isMobilePhone() && !isStandaloneMobileApp() && supportsFullscreen();
        fullscreenBtn.hidden = !show;
        if (!show) return;
        const active = isBrowserFullscreenActive();
        const isAr = currentLanguage() === "ar";
        fullscreenBtn.setAttribute("aria-pressed", String(active));
        fullscreenBtn.title = isAr
            ? (active ? "إنهاء ملء الشاشة" : "ملء الشاشة بالعرض")
            : (active ? "Exit fullscreen" : "Landscape fullscreen");
    }

    function installCtaHtml(compact = false): string {
        if (isStandaloneMobileApp()) return "";
        const isAr = currentLanguage() === "ar";
        if (isAndroidDevice()) {
            const btns: string[] = [];
            if (deferredInstallPrompt) {
                btns.push(`<button type="button" class="mobile-install-btn pwa" data-mobile-action="install-pwa"><span>⚡</span><b>${isAr ? "إضافة أيقونة للشاشة الرئيسية" : "Add to Home Screen"}</b></button>`);
            }
            if (!compact && supportsFullscreen() && !isBrowserFullscreenActive()) {
                btns.push(`<button type="button" class="mobile-install-btn fs" data-mobile-action="fullscreen-landscape"><span>⛶</span><b>${isAr ? "ملء الشاشة بالعرض" : "Fullscreen Landscape"}</b></button>`);
            }
            if (btns.length === 0) return "";
            return `<div class="mobile-install-row">${btns.join("")}</div><small class="mobile-install-note">${isAr ? "العب مباشرة من المتصفح بملء الشاشة بدون تحميل" : "Play fullscreen directly in your browser — no download needed"}</small>`;
        }
        if (isIOSDevice()) {
            return `<div class="mobile-install-row">
                <button type="button" class="mobile-install-btn ios" data-mobile-action="open-ios-guide"><span>📱</span><b>${isAr ? "إضافة للشاشة الرئيسية (ملء الشاشة)" : "Add to Home Screen (Fullscreen)"}</b></button>
            </div><small class="mobile-install-note">${isAr ? "تفتح بدون شريط Safari وتتحدث تلقائياً بدون تحميل" : "Opens without Safari bars & updates automatically"}</small>`;
        }
        return "";
    }

    function renderRotateGate(): void {
        if (!rotateGate) return;
        const phone = isMobilePhone();
        const portrait = phone && window.innerHeight > window.innerWidth;
        if (!portrait) portraitDismissed = false;
        const shouldShow = portrait && (!portraitDismissed || matchActive);
        rotateGate.hidden = !shouldShow;
        if (!shouldShow) return;

        const isAr = currentLanguage() === "ar";
        const canAutoRotate = supportsFullscreen() && !isIOSDevice();
        const title = isAr ? "أدِر جوالك بالعرض للعب" : "Rotate Your Phone to Landscape";
        const sub = isAr
            ? (matchActive
                ? "اللعبة مصممة للوضع الأفقي لتظهر الخريطة والقوائم بوضوح وبأزرار مريحة للمس."
                : "لأفضل تجربة تحكم بالجيوش والمباني، افتح اللعبة بالوضع الأفقي بملء الشاشة.")
            : (matchActive
                ? "Warzonee plays in landscape so the battlefield and command panels stay clear and touch-friendly."
                : "Switch to landscape fullscreen for the best battlefield controls.");
        const rotateBtn = canAutoRotate
            ? `<button type="button" class="primary wide mobile-rotate-btn" data-mobile-action="fullscreen-landscape">${isAr ? "↻ العب بالعرض بشاشة ممتلئة" : "↻ Play in Landscape Fullscreen"}</button>`
            : `<p class="mobile-rotate-tip">${isAr ? "تأكد من إلغاء قفل تدوير الشاشة في جوالك ثم أدره أفقياً." : "Make sure rotation lock is off, then turn your phone sideways."}</p>`;
        const installSection = !matchActive ? installCtaHtml(true) : "";
        const skipBtn = !matchActive
            ? `<button type="button" class="quiet mobile-rotate-skip" data-mobile-action="dismiss-rotate">${isAr ? "متابعة القائمة بالطول مؤقتاً" : "Continue menu in portrait"}</button>`
            : "";

        rotateGate.innerHTML = `<div class="mobile-rotate-card glass">
            <div class="mobile-rotate-anim" aria-hidden="true">
                <svg viewBox="0 0 64 64" fill="none">
                    <rect x="18" y="8" width="28" height="48" rx="5" stroke="currentColor" stroke-width="2.6"/>
                    <line x1="28" y1="49" x2="36" y2="49" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"/>
                    <path d="M51 22c3.5 5 3.5 14-1.5 19.5M50 37v5h5" stroke="#f5db9f" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"/>
                </svg>
            </div>
            <h2>${title}</h2>
            <p>${sub}</p>
            ${rotateBtn}
            ${installSection ? `<div class="mobile-rotate-install">${installSection}</div>` : ""}
            ${skipBtn}
        </div>`;
    }

    function renderInstallBar(): void {
        if (!installBar) return;
        const phone = isMobilePhone();
        if (!phone || isStandaloneMobileApp()) {
            installBar.hidden = true;
            installBar.innerHTML = "";
            return;
        }
        const html = installCtaHtml(false);
        installBar.hidden = !html;
        installBar.innerHTML = html;
    }

    function renderIosModal(open: boolean): void {
        if (!iosModal) return;
        iosModal.hidden = !open;
        if (!open) return;
        const isAr = currentLanguage() === "ar";
        iosModal.innerHTML = `<div class="ios-guide-card glass" role="dialog" aria-modal="true">
            <div class="ios-guide-head">
                <strong>${isAr ? "إضافة Warzonee للشاشة الرئيسية" : "Add Warzonee to Home Screen"}</strong>
                <button type="button" class="icon-button" data-mobile-action="close-ios-guide" aria-label="Close">×</button>
            </div>
            <ol class="ios-guide-steps">
                <li><span>1</span><div>${isAr ? "افتح الموقع في متصفح <b>Safari</b> واضغط زر المشاركة <b>⎋</b> بالأسفل." : "Open in <b>Safari</b> and tap the Share button <b>⎋</b> at the bottom."}</div></li>
                <li><span>2</span><div>${isAr ? "اختر <b>«إضافة إلى الشاشة الرئيسية»</b> ثم اضغط <b>إضافة</b>." : "Tap <b>Add to Home Screen</b>, then tap <b>Add</b>."}</div></li>
                <li><span>3</span><div>${isAr ? "افتح أيقونة <b>Warzonee</b> من شاشتك الرئيسية وأدر الجوال بالعرض — ستعمل بملء الشاشة وتتحدث تلقائياً!" : "Launch <b>Warzonee</b> from your Home Screen in landscape — fullscreen & always up to date!"}</div></li>
            </ol>
            <button type="button" class="primary wide" data-mobile-action="close-ios-guide">${isAr ? "فهمت" : "Got it"}</button>
        </div>`;
    }

    function syncViewportClasses(): void {
        const phone = isMobilePhone();
        const landscape = phone && window.innerWidth >= window.innerHeight;
        const portrait = phone && window.innerHeight > window.innerWidth;
        const installed = isStandaloneMobileApp();

        root.classList.toggle("is-mobile-phone", phone);
        root.classList.toggle("is-mobile-landscape", landscape);
        root.classList.toggle("is-mobile-portrait", portrait);
        root.classList.toggle("is-installed-app", installed);

        syncFullscreenButton();
        renderRotateGate();
        renderInstallBar();
        onLayoutChange?.();
    }

    document.addEventListener("click", event => {
        const target = (event.target as Element | null)?.closest<HTMLElement>("[data-mobile-action]");
        if (!target) return;
        const action = target.dataset.mobileAction;
        if (action === "fullscreen-landscape") {
            void requestLandscapeFullscreen();
        } else if (action === "dismiss-rotate") {
            portraitDismissed = true;
            renderRotateGate();
        } else if (action === "open-ios-guide") {
            renderIosModal(true);
        } else if (action === "close-ios-guide") {
            renderIosModal(false);
        } else if (action === "install-pwa" && deferredInstallPrompt) {
            const promptEvent = deferredInstallPrompt;
            deferredInstallPrompt = null;
            void promptEvent.prompt().then(() => promptEvent.userChoice).finally(() => renderInstallBar());
        }
    });

    iosModal?.addEventListener("click", event => {
        if (event.target === iosModal) renderIosModal(false);
    });

    fullscreenBtn?.addEventListener("click", () => {
        void toggleFullscreen();
    });

    window.addEventListener("beforeinstallprompt", event => {
        event.preventDefault();
        deferredInstallPrompt = event as BeforeInstallPromptEvent;
        renderRotateGate();
        renderInstallBar();
    });

    window.addEventListener("appinstalled", () => {
        deferredInstallPrompt = null;
        syncViewportClasses();
    });

    window.addEventListener("resize", syncViewportClasses);
    window.addEventListener("orientationchange", () => {
        window.setTimeout(syncViewportClasses, 80);
    });
    document.addEventListener("fullscreenchange", syncViewportClasses);
    document.addEventListener("visibilitychange", () => { void syncWakeLock(); });

    syncViewportClasses();

    return {
        refreshLanguage() {
            syncFullscreenButton();
            renderRotateGate();
            renderInstallBar();
            if (iosModal && !iosModal.hidden) renderIosModal(true);
        },
        setMatchActive(active: boolean) {
            if (matchActive === active) return;
            matchActive = active;
            if (active && isMobilePhone() && isAndroidDevice() && !isBrowserFullscreenActive() && !isStandaloneMobileApp()) {
                const orientation = screen.orientation as ScreenOrientation & { lock?: (mode: string) => Promise<void> };
                if (orientation?.lock) void orientation.lock("landscape").catch(() => { /* Needs fullscreen first */ });
            }
            renderRotateGate();
            void syncWakeLock();
        }
    };
}
