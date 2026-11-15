import { currentLanguage } from './locale';
import type { Point } from './map';

const STORAGE_KEY = 'warzone.tutorialDone';

interface Copy {
    ar: string;
    en: string;
}

const pick = (text: Copy) => (currentLanguage() === 'ar' ? text.ar : text.en);

export interface TutorialContext {
    matchId: string;
    running: boolean;
    panel: string | null;
    panelId: string;
    buildingLevelSum: number;
    hasBuildingJob: boolean;
    hasTrainingJob: boolean;
    totalTroops: number;
    selectedArmiesCount: number;
    hasOasisUnitSelection: boolean;
    hasArmyToOasis: boolean;
    ownedOasesCount: number;
    nearestNeutralOasis: { id: string; name: string; position: Point } | null;
    firstOwnedOasis: { id: string; name: string; position: Point } | null;
    firstArmyId: string | null;
    firstArmyPosition: Point | null;
}

interface StepDef {
    id: string;
    shortTitle: Copy;
    title: Copy;
    body: Copy;
    tip: Copy;
}

const STEPS: StepDef[] = [
    {
        id: 'upgrade',
        shortTitle: { ar: 'تطوير مبنى بالقرية', en: 'Upgrade a village building' },
        title: { ar: '1) طوّر المبنى الرئيسي', en: '1) Upgrade Your Main Hall' },
        body: {
            ar: 'اضغط على زر (+) بجانب "المبنى الرئيسي" في الشريط السفلي لبدء أول تطوير وفتح مستويات أعلى لباقي المباني.',
            en: 'Click the (+) button next to Main Hall in the bottom bar to start upgrading and unlock higher building tiers.'
        },
        tip: {
            ar: 'تذكير: أي مورد أو شرط غير مكتمل سيظهر باللون الأحمر لتعرف ما ينقصك.',
            en: 'Tip: Any missing resource or requirement shows in red so you know immediately.'
        }
    },
    {
        id: 'train',
        shortTitle: { ar: 'تدريب أول فرقة مقاتلة', en: 'Train your first squad' },
        title: { ar: '2) افتح الثكنة ودرّب جنودك', en: '2) Open Barracks & Train Troops' },
        body: {
            ar: 'اضغط على "الثكنة" في الشريط السفلي، ثم اضغط "تدريب" لإعداد أول فرقة مشاة.',
            en: 'Click Barracks in the bottom bar, then click Train to recruit your first infantry squad.'
        },
        tip: {
            ar: 'بمجرد بدء التدريب ستنتقل هذه الخطوة تلقائياً للمرحلة التالية.',
            en: 'Once recruitment begins, this step completes automatically.'
        }
    },
    {
        id: 'oasis',
        shortTitle: { ar: 'تحريك الجيش لأقرب واحة', en: 'March to nearest oasis' },
        title: { ar: '3) سيطر على أقرب واحة مهجورة', en: '3) Claim the Nearest Oasis' },
        body: {
            ar: 'اختر جيشك (Left Click من الشريط السفلي أو الخريطة)، ثم اضغط Right Click على الواحة المضيئة لإرسال جيشك واحتلالها.',
            en: 'Select your army with Left Click, then Right Click the glowing Oasis on the map to send your troops to capture it.'
        },
        tip: {
            ar: 'الواحة تمنح قريتك مكافأة إنتاج مستمرة وموارد إضافية!',
            en: 'Capturing an oasis gives your village a continuous production bonus.'
        }
    },
    {
        id: 'manage-oasis',
        shortTitle: { ar: 'فتح لوحة الواحة وتجربة Shift', en: 'Inspect oasis & Shift-select' },
        title: { ar: '4) إدارة الواحة وتحديد الوحدات', en: '4) Manage Oasis & Shift-Select' },
        body: {
            ar: 'اضغط Left Click على واحتك لرؤية إنتاجها ووحداتها منفصلة. يمكنك تحديد أكثر من نوع وحدة معاً بالضغط على Shift وإرسالهم بالرايت كليك!',
            en: 'Left-click an oasis to open its panel. Select multiple unit types with Shift+Click and dispatch them together with Right Click.'
        },
        tip: {
            ar: 'جيوشك تندمج تلقائياً داخل الواحة، وتعزيزات أصدقائك تظهر في الصف الأخضر.',
            en: 'Your armies merge automatically inside oases; ally reinforcements show in a green row.'
        }
    },
    {
        id: 'finish',
        shortTitle: { ar: 'مراقبة القمح وقلعة التاج', en: 'Watch wheat & Crown Keep' },
        title: { ar: '5) القمح وقلعة التاج — جاهز للمعركة!', en: '5) Wheat & Crown Keep — Ready!' },
        body: {
            ar: 'راقب مؤشر الجيش والقمح ووقت ظهور "قلعة التاج" في الشريط العلوي، وتابع تقارير معاركك من زر "التقارير". بالتوفيق!',
            en: 'Keep an eye on Wheat supply, Crown Keep unlock timer in the top bar, and your battle Reports. Good luck, Commander!'
        },
        tip: {
            ar: 'يمكنك مراجعة الدليل المصوّر في أي وقت عبر زر "التعليمات" أو مفتاح F1.',
            en: 'You can reopen the full illustrated Guide anytime via the Guide button or F1.'
        }
    }
];

export class GameTutorial {
    private coach: HTMLElement;
    private ring: HTMLElement;
    private ringTag: HTMLElement;
    private active = false;
    private step = 0;
    private matchId = '';
    private baselineBuildingSum = 0;
    private baselineTroops = 0;
    private baselineOases = 0;
    private lastContext: TutorialContext | null = null;
    private rafId = 0;

    constructor(
        private setMapBeacon: (target: { id?: string; position?: Point } | null) => void,
        private openGuide?: () => void
    ) {
        this.coach = document.createElement('aside');
        this.coach.id = 'tutorial-coach';
        this.coach.className = 'tutorial-coach';
        this.coach.setAttribute('data-no-translate', '');
        this.coach.setAttribute('aria-live', 'polite');
        this.coach.hidden = true;

        this.ring = document.createElement('div');
        this.ring.className = 'tutorial-spotlight-ring';
        this.ring.setAttribute('data-no-translate', '');
        this.ring.hidden = true;
        this.ringTag = document.createElement('span');
        this.ringTag.className = 'tutorial-spotlight-tag';
        this.ring.append(this.ringTag);

        document.body.append(this.ring, this.coach);

        this.coach.addEventListener('click', event => {
            const btn = (event.target as Element).closest<HTMLElement>('[data-tut-action]');
            if (!btn) return;
            const action = btn.dataset.tutAction;
            if (action === 'skip') this.skip();
            else if (action === 'next') {
                if (this.step >= STEPS.length - 1) this.finish();
                else {
                    this.step++;
                    this.render();
                    this.refreshSpotlights();
                }
            } else if (action === 'prev' && this.step > 0) {
                this.step--;
                this.render();
                this.refreshSpotlights();
            } else if (action === 'guide') {
                this.openGuide?.();
            }
        });

        window.addEventListener('languagechange', () => {
            if (this.active) {
                this.render();
                this.refreshSpotlights();
            }
        });
        window.addEventListener('resize', () => {
            if (this.active) this.refreshSpotlights();
        });
    }

    static isDone(): boolean {
        try {
            return localStorage.getItem(STORAGE_KEY) === '1';
        } catch {
            return false;
        }
    }

    private markDone() {
        try {
            localStorage.setItem(STORAGE_KEY, '1');
        } catch {
            /* Storage optional */
        }
    }

    start(force = false, ctx?: TutorialContext | null) {
        if (!force && GameTutorial.isDone()) return;
        this.active = true;
        this.step = 0;
        const source = ctx ?? this.lastContext;
        if (source) {
            this.matchId = source.matchId;
            this.baselineBuildingSum = source.buildingLevelSum;
            this.baselineTroops = source.totalTroops;
            this.baselineOases = source.ownedOasesCount;
        } else {
            this.matchId = '';
            this.baselineBuildingSum = 0;
            this.baselineTroops = 0;
            this.baselineOases = 0;
        }
        this.render();
        this.startLoop();
    }

    replay(ctx?: TutorialContext | null) {
        try {
            localStorage.removeItem(STORAGE_KEY);
        } catch {
            /* Storage optional */
        }
        this.start(true, ctx);
    }

    skip() {
        this.markDone();
        this.stop();
    }

    finish() {
        this.markDone();
        this.stop();
    }

    stop() {
        this.active = false;
        this.coach.hidden = true;
        this.ring.hidden = true;
        this.setMapBeacon(null);
        if (this.rafId) {
            cancelAnimationFrame(this.rafId);
            this.rafId = 0;
        }
    }

    sync(ctx: TutorialContext | null) {
        this.lastContext = ctx;
        if (!ctx || !ctx.running) {
            if (this.active) {
                this.coach.hidden = true;
                this.ring.hidden = true;
                this.setMapBeacon(null);
            }
            return;
        }

        if (!this.active) {
            if (!GameTutorial.isDone()) {
                this.start(true, ctx);
            }
            return;
        }

        if (this.matchId !== ctx.matchId) {
            this.matchId = ctx.matchId;
            this.baselineBuildingSum = ctx.buildingLevelSum;
            this.baselineTroops = ctx.totalTroops;
            this.baselineOases = ctx.ownedOasesCount;
        }

        // Auto-advance based on actual live game progress
        let advanced = true;
        while (advanced && this.step < STEPS.length - 1) {
            advanced = false;
            if (this.step === 0 && (ctx.hasBuildingJob || ctx.buildingLevelSum > this.baselineBuildingSum)) {
                this.step = 1;
                this.baselineTroops = Math.min(this.baselineTroops, ctx.totalTroops);
                advanced = true;
            } else if (this.step === 1 && (ctx.hasTrainingJob || ctx.totalTroops > this.baselineTroops)) {
                this.step = 2;
                advanced = true;
            } else if (this.step === 2 && (ctx.hasArmyToOasis || ctx.ownedOasesCount > this.baselineOases)) {
                this.step = 3;
                advanced = true;
            } else if (this.step === 3 && ctx.panel === 'oasis' && ctx.hasOasisUnitSelection) {
                this.step = 4;
                advanced = true;
            }
        }

        this.coach.hidden = false;
        this.render();
        this.refreshSpotlights();
    }

    private startLoop() {
        if (this.rafId) cancelAnimationFrame(this.rafId);
        const tick = () => {
            if (!this.active) {
                this.rafId = 0;
                return;
            }
            this.refreshSpotlights();
            this.rafId = requestAnimationFrame(tick);
        };
        this.rafId = requestAnimationFrame(tick);
    }

    private findSpotlightTarget(): { element: HTMLElement | null; tag: string; beacon: { id?: string; position?: Point } | null } {
        const ctx = this.lastContext;
        if (!ctx || !ctx.running) return { element: null, tag: '', beacon: null };

        if (this.step === 0) {
            const btn = document.querySelector<HTMLElement>('#building-strip [data-building-name="MainHall"] .building-upgrade:not(:disabled)')
                ?? document.querySelector<HTMLElement>('#building-strip .building-upgrade:not(:disabled)')
                ?? document.querySelector<HTMLElement>('#building-strip [data-building-name="MainHall"]');
            return {
                element: btn,
                tag: pick({ ar: 'اضغط هنا للتطوير', en: 'Click to upgrade' }),
                beacon: null
            };
        }

        if (this.step === 1) {
            const trainBtn = ctx.panel === 'building'
                ? document.querySelector<HTMLElement>('#drawer [data-action="train"]:not(:disabled)')
                    ?? document.querySelector<HTMLElement>('#drawer [data-action="select-unit"]')
                : null;
            if (trainBtn) {
                return {
                    element: trainBtn,
                    tag: pick({ ar: 'ابدأ التدريب هنا', en: 'Train here' }),
                    beacon: null
                };
            }
            const barracks = document.querySelector<HTMLElement>('#building-strip [data-building-name="Barracks"] .building-open')
                ?? document.querySelector<HTMLElement>('#building-strip [data-building-name="Stable"] .building-open');
            return {
                element: barracks,
                tag: pick({ ar: 'افتح الثكنة', en: 'Open Barracks' }),
                beacon: null
            };
        }

        if (this.step === 2) {
            if (ctx.selectedArmiesCount === 0) {
                const armyBtn = document.querySelector<HTMLElement>('#army-strip .army-card .army-card-btn')
                    ?? document.querySelector<HTMLElement>('#army-strip .army-card');
                return {
                    element: armyBtn,
                    tag: pick({ ar: 'اختر جيشك أولاً', en: 'Select army first' }),
                    beacon: ctx.firstArmyId && ctx.firstArmyPosition
                        ? { id: ctx.firstArmyId, position: ctx.firstArmyPosition }
                        : ctx.nearestNeutralOasis ? { position: ctx.nearestNeutralOasis.position } : null
                };
            }
            return {
                element: null,
                tag: '',
                beacon: ctx.nearestNeutralOasis ? { position: ctx.nearestNeutralOasis.position } : null
            };
        }

        if (this.step === 3) {
            if (ctx.panel === 'oasis') {
                const tile = document.querySelector<HTMLElement>('#drawer .oasis-own-block');
                return {
                    element: tile,
                    tag: pick({ ar: 'جرّب التحديد بـ Shift', en: 'Try Shift-selecting' }),
                    beacon: null
                };
            }
            const targetOasis = ctx.firstOwnedOasis ?? ctx.nearestNeutralOasis;
            return {
                element: null,
                tag: '',
                beacon: targetOasis ? { position: targetOasis.position } : null
            };
        }

        if (this.step === 4) {
            const hud = document.getElementById('muster-hud')?.hidden === false
                ? document.getElementById('muster-hud')
                : document.getElementById('match-clock');
            return {
                element: hud,
                tag: pick({ ar: 'راقب التموين والوقت', en: 'Watch supply & clock' }),
                beacon: null
            };
        }

        return { element: null, tag: '', beacon: null };
    }

    private refreshSpotlights() {
        if (!this.active || !this.lastContext?.running || document.querySelector('dialog[open]')) {
            this.ring.hidden = true;
            this.setMapBeacon(null);
            return;
        }
        const { element, tag, beacon } = this.findSpotlightTarget();
        this.setMapBeacon(beacon);

        if (!element) {
            this.ring.hidden = true;
            return;
        }
        const rect = element.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) {
            this.ring.hidden = true;
            return;
        }
        this.ring.hidden = false;
        this.ring.style.left = `${Math.round(rect.left - 5)}px`;
        this.ring.style.top = `${Math.round(rect.top - 5)}px`;
        this.ring.style.width = `${Math.round(rect.width + 10)}px`;
        this.ring.style.height = `${Math.round(rect.height + 10)}px`;
        this.ringTag.textContent = tag;
        this.ringTag.hidden = !tag;
    }

    private render() {
        const ar = currentLanguage() === 'ar';
        this.coach.dir = ar ? 'rtl' : 'ltr';
        const cur = STEPS[this.step]!;
        const pct = Math.round(((this.step + 1) / STEPS.length) * 100);
        const listHtml = STEPS.map((s, i) => {
            const stateClass = i < this.step ? 'done' : i === this.step ? 'active' : '';
            const marker = i < this.step ? '✓' : String(i + 1);
            return `<div class="tutorial-check-item ${stateClass}"><span class="tutorial-check-dot">${marker}</span><span>${pick(s.shortTitle)}</span></div>`;
        }).join('');

        this.coach.innerHTML = `
            <div class="tutorial-head">
                <span class="tutorial-kicker">
                    <span>✦ ${pick({ ar: 'المرشد التفاعلي', en: 'Interactive Guide' })}</span>
                    <span class="tutorial-progress-pill">${this.step + 1} / ${STEPS.length}</span>
                </span>
                <button type="button" class="tutorial-skip-btn" data-tut-action="skip">${pick({ ar: 'تخطي', en: 'Skip' })}</button>
            </div>
            <div class="tutorial-bar"><div class="tutorial-bar-fill" style="width:${pct}%"></div></div>
            <div class="tutorial-body">
                <h3>${pick(cur.title)}</h3>
                <p>${pick(cur.body)}</p>
                <div class="tutorial-tip">${pick(cur.tip)}</div>
            </div>
            <div class="tutorial-checklist">${listHtml}</div>
            <div class="tutorial-actions">
                <button type="button" class="tutorial-btn" data-tut-action="guide">${pick({ ar: 'التعليمات الكاملة', en: 'Full Guide' })}</button>
                <div style="display:flex;gap:6px">
                    ${this.step > 0 ? `<button type="button" class="tutorial-btn" data-tut-action="prev">${pick({ ar: 'السابق', en: 'Back' })}</button>` : ''}
                    <button type="button" class="tutorial-btn primary" data-tut-action="next">${this.step === STEPS.length - 1
                        ? pick({ ar: 'إنهاء التيتوريال ✓', en: 'Finish Tutorial ✓' })
                        : pick({ ar: 'التالي →', en: 'Next →' })}</button>
                </div>
            </div>`;
    }
}
