import { currentLanguage } from "./locale";

interface BeforeInstallPromptEvent extends Event {
    prompt(): Promise<void>;
    userChoice: Promise<{ outcome: "accepted" | "dismissed" }>;
}

const APK_URL = "/downloads/warzonee.apk";

export function isAndroidDevice(): boolean {
    return typeof navigator !== "undefined" && /Android/i.test(navigator.userAgent);
}

export function isIOSDevice(): boolean {
    if (typeof navigator === "undefined") return false;
    return /iPhone|iPad|iPod/i.test(navigator.userAgent)
        || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
}

export function isStandaloneMobileApp(): boolean {
    if (typeof window === "undefined") return false;
    const nav = navigator as Navigator & { standalone?: boolean };
    const cap = window as Window & { Capacitor?: { isNativePlatform?: () => boolean } };
    return Boolean(
        cap.Capacitor?.isNativePlatform?.()
        || nav.standalone === true
        || window.matchMedia?.("(display-mode: standalone)").matches
        || window.matchMedia?.("(display-mode: fullscreen)").matches
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
    let apkAvailable = false;
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

    function isInFullscreen(): boolean {
        const doc = document as Document & { webkitFullscreenElement?: Element | null };
        return Boolean(doc.fullscreenElement || doc.webkitFullscreenElement);
    }

    async function requestLandscapeFullscreen(): Promise<void> {
        const docEl = document.documentElement as HTMLElement & { webkitRequestFullscreen?: () => Promise<void> | void };
        try {
            if (!isInFullscreen()) {
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
        if (isInFullscreen()) {
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
        const active = isInFullscreen();
        const isAr = currentLanguage() === "ar";
        fullscreenBtn.setAttribute("aria-pressed", String(active));
        fullscreenBtn.title = isAr
            ? (active ? "إنهاء ملء الشاشة" : "ملء الشاشة بالعرض")
            : (active ? "Exit fullscreen" : "Landscape fullscreen");
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
                : "لأفضل تجربة تحكم بالجيوش والمباني، افتح اللعبة بالوضع الأفقي.")
            : (matchActive
                ? "Warzonee plays in landscape so the battlefield and command panels stay clear and touch-friendly."
                : "For the best battlefield controls and HUD sizing, switch to landscape mode.");
        const rotateBtn = canAutoRotate
            ? `<button type="button" class="primary wide mobile-rotate-btn" data-mobile-action="fullscreen-landscape">${isAr ? "↻ تدوير وملء الشاشة تلقائياً" : "↻ Enter Landscape Fullscreen"}</button>`
            : `<p class="mobile-rotate-tip">${isAr ? "تأكد من إلغاء قفل تدوير الشاشة في جوالك ثم أدره أفقياً." : "Make sure rotation lock is off, then turn your phone sideways."}</p>`;
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
            ${skipBtn}
        </div>`;
    }

    function renderInstallBar(): void {
        if (!installBar) return;
        const phone = isMobilePhone();
        const installed = isStandaloneMobileApp();
        const isAr = currentLanguage() === "ar";
        if (!phone || installed) {
            installBar.hidden = true;
            installBar.innerHTML = "";
            return;
        }

        if (isAndroidDevice()) {
            const buttons: string[] = [];
            if (apkAvailable) {
                buttons.push(`<a class="mobile-install-btn apk" href="${APK_URL}" download="Warzonee.apk"><span>📱</span><b>${isAr ? "تحميل تطبيق الأندرويد (APK)" : "Download Android App (APK)"}</b></a>`);
            }
            if (deferredInstallPrompt) {
                buttons.push(`<button type="button" class="mobile-install-btn pwa" data-mobile-action="install-pwa"><span>⚡</span><b>${isAr ? "تثبيت سريع كتطبيق" : "Quick Install App"}</b></button>`);
            }
            if (supportsFullscreen()) {
                buttons.push(`<button type="button" class="mobile-install-btn fs" data-mobile-action="fullscreen-landscape"><span>⛶</span><b>${isAr ? "ملء الشاشة بالعرض" : "Fullscreen Landscape"}</b></button>`);
            }
            if (buttons.length === 0) {
                installBar.hidden = true;
                return;
            }
            installBar.hidden = false;
            installBar.innerHTML = `<div class="mobile-install-row">${buttons.join("")}</div><small class="mobile-install-note">${isAr ? "التطبيق يتحدث تلقائياً مع كل تحديث للموقع" : "App updates automatically with every site release"}</small>`;
            return;
        }

        if (isIOSDevice()) {
            installBar.hidden = false;
            installBar.innerHTML = `<div class="mobile-install-row">
                <button type="button" class="mobile-install-btn ios" data-mobile-action="open-ios-guide"><span>📱</span><b>${isAr ? "تثبيت اللعبة على الآيفون (ملء الشاشة)" : "Install on iPhone (Fullscreen)"}</b></button>
            </div><small class="mobile-install-note">${isAr ? "يعمل بدون شريط المتصفح ويتحدث تلقائياً مع الموقع" : "Runs fullscreen without Safari bars & auto-updates"}</small>`;
            return;
        }

        installBar.hidden = true;
    }

    function renderIosModal(open: boolean): void {
        if (!iosModal) return;
        iosModal.hidden = !open;
        if (!open) return;
        const isAr = currentLanguage() === "ar";
        iosModal.innerHTML = `<div class="ios-guide-card glass" role="dialog" aria-modal="true">
            <div class="ios-guide-head">
                <strong>${isAr ? "تثبيت Warzonee على الآيفون" : "Install Warzonee on iPhone"}</strong>
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

    // Check quietly if a direct Android APK is hosted at /downloads/warzonee.apk.
    if (isAndroidDevice() && !isStandaloneMobileApp()) {
        void fetch(APK_URL, { method: "HEAD" })
            .then(res => {
                const ct = res.headers.get("content-type") ?? "";
                if (res.ok && !ct.includes("text/html")) {
                    apkAvailable = true;
                    renderInstallBar();
                }
            })
            .catch(() => { /* APK not uploaded yet; fallback to PWA/fullscreen button. */ });
    }

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
            if (active && isMobilePhone() && isAndroidDevice() && !isInFullscreen() && !isStandaloneMobileApp()) {
                // Best-effort auto-landscape when entering a live match on mobile.
                const orientation = screen.orientation as ScreenOrientation & { lock?: (mode: string) => Promise<void> };
                if (orientation?.lock) void orientation.lock("landscape").catch(() => { /* Needs fullscreen first */ });
            }
            renderRotateGate();
            void syncWakeLock();
        }
    };
}
