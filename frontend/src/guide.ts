import { buildingArt, oasisArt, tribes } from './art';
import { currentLanguage, t } from './locale';
import { unitArtwork } from './unit-art';
import { compareUnitIds, loadUnits, unitCatalog, unitPortrait, type UnitDefinition } from './units';

export type GuideTabId =
    | 'quickstart'
    | 'buildings'
    | 'units'
    | 'controls'
    | 'combat'
    | 'oases'
    | 'victory';

interface Copy {
    ar: string;
    en: string;
}

interface TribeGuideInfo {
    id: string;
    name: Copy;
    badge: Copy;
    summary: Copy;
    pros: Copy[];
    cons: Copy[];
    timeMultiplier: number;
}

const pick = (text: Copy) => (currentLanguage() === 'ar' ? text.ar : text.en);
const esc = (text: string) =>
    text.replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[ch]!);

const TABS: { id: GuideTabId; label: Copy }[] = [
    { id: 'quickstart', label: { ar: 'البداية السريعة', en: 'Quick Start' } },
    { id: 'buildings', label: { ar: 'الموارد والمباني', en: 'Resources & Buildings' } },
    { id: 'units', label: { ar: 'القبائل والوحدات', en: 'Tribes & Units' } },
    { id: 'controls', label: { ar: 'التحكم بالجيوش', en: 'Army Controls (RTS)' } },
    { id: 'combat', label: { ar: 'المعارك والتموين', en: 'Combat & Supply' } },
    { id: 'oases', label: { ar: 'الواحات والتحالفات', en: 'Oases & Allies' } },
    { id: 'victory', label: { ar: 'قلعة التاج والنصر', en: 'Crown Keep & Victory' } }
];

const TRIBE_NAMES: Record<string, Copy> = {
    Romans: { ar: 'الرومان', en: 'Romans' },
    Germans: { ar: 'الجرمان', en: 'Germans' },
    Greeks: { ar: 'الإغريق', en: 'Greeks' },
    Mongols: { ar: 'المغول', en: 'Mongols' },
    Persians: { ar: 'الفرس', en: 'Persians' }
};

const BUILDING_LABELS: Record<string, Copy> = {
    MainHall: { ar: 'المبنى الرئيسي', en: 'Main Hall' },
    Warehouse: { ar: 'المخزن', en: 'Warehouse' },
    Barracks: { ar: 'الثكنة', en: 'Barracks' },
    Stable: { ar: 'الإسطبل', en: 'Stable' },
    SiegeWorkshop: { ar: 'ورشة الحصار', en: 'Siege Workshop' },
    Smithy: { ar: 'الحدادة', en: 'Smithy' },
    Hospital: { ar: 'المشفى', en: 'Hospital' },
    Wall: { ar: 'السور', en: 'Wall' },
    Market: { ar: 'السوق', en: 'Market' }
};

const TRIBE_PROFILES: TribeGuideInfo[] = [
    {
        id: 'Romans',
        name: { ar: 'الرومان (Romans)', en: 'Romans' },
        badge: { ar: '+1 تدريب تلقائي · نخبة متوازنة', en: '+1 Auto Slot · Balanced Elite' },
        summary: {
            ar: 'قبيلة متوازنة تبدأ بـ 4 خانات تدريب تلقائي بدلاً من 3، وتملك أقوى فرسان ثقيلة ومشاة بداية ممتازة.',
            en: 'A balanced empire starting with 4 auto-queue slots instead of 3, fielding top-tier Heavy Cavalry and strong early infantry.'
        },
        pros: [
            { ar: 'خانة تدريب تلقائي إضافية منذ البداية (4 خانات بدل 3).', en: 'Starts with 4 auto-training slots instead of 3.' },
            { ar: 'أقوى مشاة بداية (جندي أول: هجوم 46، دفاع 25/25) وأقوى فرسان ثقيلة (فرسان قيصر: هجوم 128، دفاع 88/64).', en: 'Strongest basic infantry (46 ATK, 25/25 DEF) and strongest Heavy Cavalry (128 ATK, 88/64 DEF).' },
            { ar: 'رماحة متوازنون (60 دفاع مشاة، 40 دفاع فرسان) وسهولة في بناء جيش هجومي ودفاعي.', en: 'Well-rounded Spearman defense (60/40) and reliable all-around troop stats.' }
        ],
        cons: [
            { ar: 'تكلفة التدريب واعتمادها على الحديد مرتفعان نسبياً (فرسان قيصر 320 مورد).', en: 'High iron and overall training costs (Heavy Cavalry costs 320 total).' },
            { ar: 'لا تملك مكافأة نهب أو تقليص وقت تدريب أو سرعة حركة أو سور إضافي.', en: 'No bonus to raid loot, training speed, march speed, or wall defense.' }
        ],
        timeMultiplier: 1
    },
    {
        id: 'Germans',
        name: { ar: 'الجرمان (Germans)', en: 'Germans' },
        badge: { ar: 'تدريب أسرع 30% · +20% نهب', en: '-30% Train Time · +20% Loot' },
        summary: {
            ar: 'قبيلة هجومية سريعة تعتمد على الحشود الرخيصة والتدريب السريع (-30% وقت) وغنائم النهب الإضافية (+20%).',
            en: 'An aggressive raiding tribe built for cheap massed squads, 30% faster training time, and +20% raid loot.'
        },
        pros: [
            { ar: 'وقت تدريب أقل بنسبة 30% (معامل 0.7x) وغنائم نهب إضافية +20% عند مهاجمة القرى.', en: '30% faster training time (0.7x) and +20% bonus raid loot on villages.' },
            { ar: 'أرخص جندي بداية في اللعبة (مقاتل بهراوة بـ 35 مورد فقط دون حديد أو حجر) وأقل تكلفة وحدات عموماً.', en: 'Cheapest starter unit (Clubman: only 35 total resources, 0 iron/stone) and low unit costs overall.' },
            { ar: 'فرسان خفيفة قوية ضد المشاة (مقاتل القيصر: 65 دفاع مشاة، سرعة 65).', en: 'Strong anti-infantry Light Cavalry (65 DEF vs infantry, speed 65).' }
        ],
        cons: [
            { ar: 'أضعف دفاع للمشاة الأساسية (10/10 لـ مقاتل بهراوة) ومقاتل بفأس (20 دفاع فرسان) — تنهار بسرعة في الدفاع المفاجئ.', en: 'Weakest basic infantry defense (10/10 on Clubman, 20 cav DEF on Axe/Swordsman) when caught defending.' },
            { ar: 'لا تملك مكافأة سور مضاعف أو علاج سريع أو تقليص في إعالة القمح.', en: 'No bonus to wall defense, hospital recovery, or wheat upkeep.' }
        ],
        timeMultiplier: 0.7
    },
    {
        id: 'Greeks',
        name: { ar: 'الإغريق (Greeks)', en: 'Greeks' },
        badge: { ar: 'سور 2x · مستشفى 2x', en: '2x Wall · 2x Healing' },
        summary: {
            ar: 'أقوى قبيلة دفاعية؛ سورها مضاعف (+8% للمستوى)، ومستشفاها يعالج الجرحى بضعف السرعة، ووحداتها لا تستهلك القمح في التدريب.',
            en: 'The premier defensive civilization: 2x Wall protection (+8%/level), 2x Hospital healing speed, and zero-wheat training on most units.'
        },
        pros: [
            { ar: 'حماية السور مضاعفة 2x (+8% لكل مستوى بدل +4%) وشفاء المستشفى أسرع 2x.', en: 'Double Wall protection (+8%/level instead of +4%) and 2x faster Hospital healing.' },
            { ar: 'أقوى دفاع حامية في اللعبة: الكتيبة (72 دفاع مشاة بتكلفة 45) ورماح الفالانكس (92 دفاع فرسان).', en: 'Highest defensive troops: Hoplite (72 INF DEF for 45 cost) and Phalanx Spearman (92 CAV DEF).' },
            { ar: 'معظم وحداتها تتدرب بـ 0 قمح مما يوفر القمح لإطعام الجيوش الكبيرة.', en: 'Most units cost 0 Wheat to train, leaving more wheat for army upkeep.' }
        ],
        cons: [
            { ar: 'هجوم الوحدات الأولى والفرسان الثقيلة أضعف مقارنة بباقي القبائل (15 للكتيبة، 100 للفرسان الثقيلة).', en: 'Lower offensive power on early infantry (15 ATK) and Heavy Cavalry (100 ATK).' },
            { ar: 'اعتماد كبير على مورد الحجر في تدريب معظم الوحدات.', en: 'Heavy reliance on Stone across almost all unit training costs.' }
        ],
        timeMultiplier: 1
    },
    {
        id: 'Mongols',
        name: { ar: 'المغول (Mongols)', en: 'Mongols' },
        badge: { ar: 'نقل القرية · أسرع حركة · ½ قمح', en: 'Relocate · Fastest Army · ½ Wheat' },
        summary: {
            ar: 'أسرع قبيلة على الخريطة بنصف استهلاك القمح لكل الوحدات، والوحيدة القادرة على نقل القرية لموقع استراتيجي جديد.',
            en: 'Unmatched map mobility with half wheat upkeep on every unit, plus the exclusive ability to relocate settlements.'
        },
        pros: [
            { ar: 'القدرة الفريدة على نقل القرية لموقع جديد كقافلة عربات تسير بسرعة بطيئة (10) بتكلفة 4,000 من كل مورد ومهلة تهدئة 15 دقيقة (يتوقف الترحيل إذا حوصرت القرية، وتُشترط مسافة 350 عن القرى، 250 عن القلعة، و150 عن الواحات).', en: 'Can relocate settlements as a wagon caravan marching at speed 10 (costs 4,000 of each resource, 15m cooldown, halts under siege, requires 350 spacing from settlements, 250 from Keep, and 150 from oases).' },
            { ar: 'أسرع وحدات في الخريطة (كشاف 97.5، مغير السهوب 82، فارس ثقيل 60، وحتى الكباش والمقاليع سرعتها 20!).', en: 'Fastest map speeds across the board (Scout 97.5, Light Cav 82, Heavy Cav 60, Siege 20!).' },
            { ar: 'نصف استهلاك القمح لجميع الوحدات (0.5 للمشاة، 1 للخفيفة، 1.75 للثقيلة، 2 للمقاليع).', en: '50% reduced Wheat upkeep per minute on every single unit.' }
        ],
        cons: [
            { ar: 'لا تملك تقليصاً لوقت التدريب ولا مكافأة نهب أو سور أو مستشفى (إنتاج الموارد الأساسي متساوٍ بين جميع القبائل).', en: 'No training speed, raid loot, wall, or hospital bonus (base resource production is equal across all tribes).' },
            { ar: 'تكلفة الفرسان الثقيلة (150 قمح) والحاكم (600 قمح) تتطلب مخزون قمح كبير عند التجنيد.', en: 'High Wheat training cost on Heavy Cavalry (150) and Governor (600).' }
        ],
        timeMultiplier: 1
    },
    {
        id: 'Persians',
        name: { ar: 'الفرس (Persians)', en: 'Persians' },
        badge: { ar: 'تدريب أسرع 2x · فيل الحرب', en: '2x Train Speed · War Elephant' },
        summary: {
            ar: 'قوة تدريب وحصار ثقيل؛ وقت التدريب للنصف (سرعة 2x)، وتملك فيل الحرب الحصري وأقوى آلات الحصار، مع إنتاج موارد متساوٍ كبقية القبائل.',
            en: 'An elite recruitment & heavy siege powerhouse: 50% training time (2x speed), War Elephants, and heavy siege, with standard resource output like all tribes.'
        },
        pros: [
            { ar: 'وقت تدريب أقل بـ 50% لجميع الوحدات (سرعة تدريب مضاعفة 2x!).', en: '50% shorter training duration for all units (2x faster recruitment!).' },
            { ar: 'وحدة حصرية فتاكة هي فيل الحرب (145 هجوم، 70/60 دفاع، وحمولة 80) بدلاً من الفرسان الخفيفة.', en: 'Exclusive War Elephant unit (145 ATK, 70 INF / 60 CAV DEF, 80 carry) in place of Light Cavalry.' },
            { ar: 'أقوى منجنيق ثقيل (125 هجوم) وكبش حديدي (50 هجوم) ومشاة بداية متوازنة (35/35/35).', en: 'Strongest Heavy Catapult (125 ATK), Iron Ram (50 ATK), and balanced Immortals (35/35/35).' }
        ],
        cons: [
            { ar: 'تفتقد للفرسان الخفيفة السريعة؛ فيل الحرب بطيء نسبياً (سرعة 30) ويستهلك 5 قمح/دقيقة (أعلى استهلاك في اللعبة).', en: 'No fast Light Cavalry; War Elephants move at speed 30 and burn 5 Wheat/min (highest upkeep).' },
            { ar: 'مشاتها أبطأ (سرعة 14) وتكلفة الحديد والحجر مرتفعة لوحداتها الثقيلة.', en: 'Slower infantry march speed (14) and high Iron/Stone demands for elite troops.' }
        ],
        timeMultiplier: 0.5
    }
];

function icon(id: string) {
    return `<svg aria-hidden="true"><use href="#${id}"/></svg>`;
}

function formatNum(value: number): string {
    return Number.isInteger(value) ? String(value) : value.toFixed(1).replace(/\.0$/, '');
}

function roleDescription(unit: UnitDefinition): Copy {
    switch (unit.role) {
        case 'Infantry':
            return {
                ar: 'العمود الأساسي المبكر للجيش؛ رخيصة وتفتح من مستوى 1 في الثكنة لاحتلال الواحات والحراسة.',
                en: 'Core early frontline unlocked at Barracks Lv 1; cost-effective for early expansion and oasis capture.'
            };
        case 'Spearman':
            return {
                ar: 'خط دفاع متخصص ضد الفرسان (دفاع فرسان مرتفع)، ممتاز لتأمين القرى والواحات.',
                en: 'Anti-cavalry defender with high Cavalry Defense; ideal for guarding villages and oases.'
            };
        case 'Swordsman':
            return {
                ar: 'مشاة هجومية ثقيلة (هجوم 75–84) لكسر دفاعات العدو بقوة عالية في القتال المباشر.',
                en: 'Heavy assault infantry (75–84 ATK) built to punch through enemy frontlines.'
            };
        case 'LightCavalry':
            return {
                ar: 'خيالة سريعة جداً (سرعة 60–82، حمولة 100) مثالية للغارات الخاطفة ونهب الموارد.',
                en: 'High-speed cavalry (speed 60–82, 100 carry) built for rapid raids and resource plunder.'
            };
        case 'Cavalry':
            return {
                ar: 'وحدة فيل الحرب الحصرية للفرس: أعلى هجوم قتالي (145) وصمود قوي لكنها تستهلك 5 قمح/دقيقة.',
                en: 'Persian exclusive War Elephant: highest combat attack (145) and strong armor, at 5 Wheat/min upkeep.'
            };
        case 'HeavyCavalry':
            return {
                ar: 'نخبة الفرسان المدرعة (100–128 هجوم ودفاع عالٍ) لحسم المعارك الكبرى واقتحام الحاميات.',
                en: 'Armored elite cavalry (100–128 ATK and high DEF) for decisive breakthroughs and heavy defense.'
            };
        case 'Scout':
            return {
                ar: 'وحدة استطلاع سريعة جداً تكشف قوات القرى والواحات والجيوش وتصد كشافة العدو.',
                en: 'Ultra-fast reconnaissance unit that reveals enemy forces and intercepts rival scouts.'
            };
        case 'Wagon':
            return {
                ar: 'تحمل 5,000 وحدة حمولة لتغذية الجيوش البعيدة بالقمح ونقل الموارد والغنائم والتجارة.',
                en: 'Carries 5,000 cargo to keep distant armies supplied with wheat, haul loot, and trade.'
            };
        case 'Ram':
            return {
                ar: 'يكسر بوابة القرية بسرعة 12% لكل كبش في الجولة، ويحطم سور الواحة بسرعة مضاعفة.',
                en: 'Breaches village gates (+12% per ram/round) and smashes oasis walls rapidly.'
            };
        case 'Catapult':
            return {
                ar: 'يخفض سلامة سور القرية 5% لكل مقلاع في الجولة، ويهدم مباني القرية بعد سقوط الحامية.',
                en: 'Drops village wall integrity by 5% per catapult/round and demolishes buildings in Raze sieges.'
            };
        case 'Governor':
            return {
                ar: 'يُدرّب في المبنى الرئيسي (مستوى 5)؛ يخفض ولاء قرية العدو بعد سقوط حاميتها لاحتلالها (5% لكل حاكم، بحد 3 حكام/جولة).',
                en: 'Trained in Main Hall (Lv 5); reduces conquered village loyalty after the garrison falls (5% per governor, max 3/round).'
            };
        default:
            return { ar: '', en: '' };
    }
}

export class GameGuide {
    private dialog: HTMLDialogElement;
    private frame: HTMLDivElement;
    private activeTab: GuideTabId = 'quickstart';
    private previewTribe = 'Romans';

    constructor(private replayTutorial?: () => void) {
        this.dialog = document.createElement('dialog');
        this.dialog.id = 'game-guide';
        this.dialog.className = 'game-guide';
        this.dialog.setAttribute('data-no-translate', '');
        this.dialog.setAttribute('aria-labelledby', 'guide-heading');

        this.frame = document.createElement('div');
        this.frame.className = 'guide-frame';
        this.dialog.append(this.frame);
        document.body.append(this.dialog);

        this.dialog.addEventListener('click', event => {
            if (event.target === this.dialog) {
                this.close();
                return;
            }
            const target = (event.target as Element).closest<HTMLElement>('[data-guide-action]');
            if (!target) return;
            const action = target.dataset.guideAction;
            if (action === 'close') this.close();
            else if (action === 'tab' && target.dataset.tab) {
                this.activeTab = target.dataset.tab as GuideTabId;
                this.render();
            } else if (action === 'tribe' && target.dataset.tribe) {
                this.previewTribe = target.dataset.tribe;
                this.render(false);
            } else if (action === 'replay-tutorial') {
                this.close();
                this.replayTutorial?.();
            } else if (action === 'prev' || action === 'next') {
                const idx = TABS.findIndex(tab => tab.id === this.activeTab);
                const nextIdx = action === 'next' ? Math.min(TABS.length - 1, idx + 1) : Math.max(0, idx - 1);
                this.activeTab = TABS[nextIdx]!.id;
                this.render();
            }
        });

        window.addEventListener('languagechange', () => {
            if (this.dialog.open) this.render(false);
        });
    }

    setTribe(tribe?: string | null) {
        if (tribe && tribes.includes(tribe)) this.previewTribe = tribe;
    }

    isOpen() {
        return this.dialog.open;
    }

    open(tab?: GuideTabId) {
        if (tab) this.activeTab = tab;
        this.render();
        if (!this.dialog.open) this.dialog.showModal();
        if (unitCatalog.size === 0) {
            void loadUnits().then(() => {
                if (this.dialog.open) this.render(false);
            }).catch(() => { /* Catalog remains optional until connected. */ });
        }
    }

    close() {
        if (this.dialog.open) this.dialog.close();
    }

    private unitBadge(unitId: string): string {
        return unitPortrait(unitId, true) || unitArtwork(unitId);
    }

    private render(scrollToTop = true) {
        const ar = currentLanguage() === 'ar';
        this.dialog.dir = ar ? 'rtl' : 'ltr';
        const contentScroll = this.frame.querySelector('.guide-content')?.scrollTop ?? 0;
        const idx = TABS.findIndex(tab => tab.id === this.activeTab);
        const tribeBtns = tribes
            .map(tr => `<button type="button" class="guide-tribe-pill ${tr === this.previewTribe ? 'active' : ''}" data-guide-action="tribe" data-tribe="${tr}">${pick(TRIBE_NAMES[tr]!)}</button>`)
            .join('');
        const navBtns = TABS.map((tab, i) => `
            <button type="button" class="guide-tab ${tab.id === this.activeTab ? 'active' : ''}" data-guide-action="tab" data-tab="${tab.id}">
                <span class="guide-tab-num">${i + 1}</span>
                <span>${pick(tab.label)}</span>
            </button>`).join('');

        this.frame.innerHTML = `
            <header class="guide-head">
                <div class="guide-title-wrap">
                    <span class="guide-badge-icon">${icon('i-book')}</span>
                    <div>
                        <h2 id="guide-heading">${pick({ ar: 'دليل Warzonee — كيف تلعب وتنتصر', en: 'Warzonee Field Manual — How to Play & Win' })}</h2>
                        <p>${pick({ ar: 'شرح دقيق ومحدّث لآليات اللعب والقبائل والوحدات', en: 'Accurate, up-to-date guide to tribes, units, and battlefield rules' })}</p>
                    </div>
                </div>
                <div class="guide-head-actions">
                    <div class="guide-tribe-pills" title="${pick({ ar: 'اختر قبيلة لمعاينة مبانيها ووحداتها', en: 'Select a tribe to preview its buildings and units' })}">${tribeBtns}</div>
                    <button type="button" class="guide-close" data-guide-action="close" aria-label="${pick({ ar: 'إغلاق', en: 'Close' })}">×</button>
                </div>
            </header>
            <div class="guide-shell">
                <nav class="guide-nav" aria-label="${pick({ ar: 'أقسام الدليل', en: 'Guide sections' })}">${navBtns}</nav>
                <main class="guide-content">${this.renderTab()}</main>
            </div>
            <footer class="guide-foot">
                <small>${pick({ ar: 'تلميح: اضغط F1 أو زر التعليمات في أي وقت لفتح هذا الدليل، و(?) على أي وحدة لعرض إحصاءاتها.', en: 'Tip: Press F1 or Guide anytime, and hover or click (?) on any unit to inspect stats.' })}</small>
                <div class="guide-foot-btns">
                    ${this.replayTutorial ? `<button type="button" class="quiet" data-guide-action="replay-tutorial">${pick({ ar: 'إعادة التيتوريال التفاعلي', en: 'Replay Tutorial' })}</button>` : ''}
                    <button type="button" class="quiet" data-guide-action="prev" ${idx === 0 ? 'disabled' : ''}>${pick({ ar: 'السابق', en: 'Previous' })}</button>
                    ${idx < TABS.length - 1
                        ? `<button type="button" class="primary" data-guide-action="next">${pick({ ar: 'التالي', en: 'Next' })}</button>`
                        : `<button type="button" class="primary" data-guide-action="close">${pick({ ar: 'فهمت — ابدأ اللعب', en: 'Got it — Back to Game' })}</button>`}
                </div>
            </footer>`;

        const content = this.frame.querySelector('.guide-content');
        if (content) content.scrollTop = scrollToTop ? 0 : contentScroll;
    }

    private renderTab(): string {
        switch (this.activeTab) {
            case 'quickstart': return this.tabQuickStart();
            case 'buildings': return this.tabBuildings();
            case 'units': return this.tabUnits();
            case 'controls': return this.tabControls();
            case 'combat': return this.tabCombat();
            case 'oases': return this.tabOases();
            case 'victory': return this.tabVictory();
        }
    }

    private tabQuickStart(): string {
        const tId = this.previewTribe.toLowerCase();
        const mountRole = tId === 'persians' ? 'cavalry' : 'lightcavalry';
        return `
            <section class="guide-hero">
                <div>
                    <h3>${pick({ ar: 'فكرة اللعبة في 30 ثانية', en: 'The Game in 30 Seconds' })}</h3>
                    <p>${pick({
                        ar: 'تبدأ بقرية تحت حماية مؤقتة، ومعك 1,500 من كل مورد، و20 جندي مشاة، وعربتا إمداد. طوّر مبانيك لإنتاج الموارد، درّب جيشك، سيطر على الواحات المجاورة لتسريع اقتصادك، ثم نافس على احتلال قلعة التاج في منتصف الخريطة أو تصدّر النقاط للفوز.',
                        en: 'You start with one protected village, 1,500 of each resource, 20 Infantry, and 2 Supply Wagons. Upgrade buildings, train armies, claim nearby oases for production bonuses, and capture the central Crown Keep or lead the standings to win.'
                    })}</p>
                </div>
                <div class="guide-hero-art">${buildingArt('MainHall', this.previewTribe)}</div>
            </section>

            <div class="guide-steps">
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">1</span><strong>${pick({ ar: 'طوّر القرية', en: 'Grow Village' })}</strong></div>
                    <div class="guide-step-visual">${buildingArt('MainHall', this.previewTribe)}${buildingArt('Warehouse', this.previewTribe)}</div>
                    <p>${pick({ ar: 'ابدأ بالمبنى الرئيسي لرفع الحد الأقصى لباقي المباني، والمخزن لزيادة السعة، ثم الثكنة والإسطبل.', en: 'Upgrade Main Hall to raise building caps, Warehouse for storage, then Barracks and Stable.' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">2</span><strong>${pick({ ar: 'درّب جيشك', en: 'Train Troops' })}</strong></div>
                    <div class="guide-step-visual">${this.unitBadge(`${tId}-infantry`)}${this.unitBadge(`${tId}-${mountRole}`)}</div>
                    <p>${pick({ ar: 'من الشريط السفلي اختر الثكنة أو الإسطبل، واضغط علامة (?) فوق أي وحدة لمقارنة قوتها قبل التدريب.', en: 'Recruit troops from Barracks or Stable in the bottom bar; hover or click (?) on any unit to inspect stats.' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">3</span><strong>${pick({ ar: 'سيطر على الواحات', en: 'Claim Oases' })}</strong></div>
                    <div class="guide-step-visual">${oasisArt('Wood')}${oasisArt('Wheat')}</div>
                    <p>${pick({ ar: 'حدّد جيشك بالزر الأيسر ثم بالزر الأيمن على أقرب واحة محايدة (15 ثانية احتلال) للحصول على +50% من موردها و+25% للباقي.', en: 'Left-click your army and Right-click the nearest neutral oasis (15s capture) for +50% primary and +25% other income.' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">4</span><strong>${pick({ ar: 'قلعة التاج والنصر', en: 'Crown Keep' })}</strong></div>
                    <div class="guide-step-visual">${icon('i-shield')}${icon('i-sword')}</div>
                    <p>${pick({ ar: 'عندما تفتح قلعة التاج بوسط الخريطة، احتلها وحافظ عليها 5 دقائق متواصلة للفوز الفوري أو تصدّر النقاط.', en: 'When Crown Keep unlocks in the center, hold it for 5 continuous minutes to win immediately or top the scoreboard.' })}</p>
                </article>
            </div>

            <div class="guide-example">
                <strong>${pick({ ar: 'مثال عملي لأول دقيقتين في المباراة:', en: 'Practical Example — Your First 2 Minutes:' })}</strong>
                <p>${pick({
                    ar: '1) أرسل الجنود الـ20 المبدئيين بالزر الأيمن لاحتلال أقرب واحة محايدة فوراً — 2) طوّر المبنى الرئيسي والثكنة أو المخزن من الشريط السفلي — 3) فعّل التدريب التلقائي (AUTO) لوحداتك الأساسية حتى لا يتوقف بناء جيشك.',
                    en: '1) Dispatch your 20 starting infantry right away to capture the closest neutral oasis — 2) Upgrade Main Hall and Barracks or Warehouse — 3) Enable AUTO queue on your starter troops so recruitment never stalls.'
                })}</p>
            </div>`;
    }

    private tabBuildings(): string {
        const items: { key: string; name: Copy; tag: Copy; desc: Copy }[] = [
            {
                key: 'MainHall',
                name: BUILDING_LABELS.MainHall!,
                tag: { ar: 'سقف التطوير + الحاكم (م5)', en: 'Level Cap + Governor (Lv 5)' },
                desc: {
                    ar: 'يحدد الحد الأقصى لمستوى كل المباني (حتى 30). يفتح تدريب الحاكم عند المستوى 5 (+8% سرعة/مستوى)، ويمنح +1 خانة تدريب تلقائي كل 5 مستويات (6، 11، 16...).',
                    en: 'Caps all other building levels (max 30). Trains Governors at Lv 5 (+8% speed/lvl) and grants +1 auto-training slot every 5 levels (6, 11, 16...).'
                }
            },
            {
                key: 'Warehouse',
                name: BUILDING_LABELS.Warehouse!,
                tag: { ar: '2,000 + 2,000×المستوى', en: '2,000 + 2,000×Level' },
                desc: {
                    ar: 'يرفع سعة تخزين كل مورد بمقدار 2,000 لكل مستوى (من 4,000 عند مستوى 1 حتى 62,000 عند مستوى 30). إذا امتلأ المخزن يتوقف الإنتاج.',
                    en: 'Raises capacity per resource by 2,000 per level (4,000 at Lv 1 up to 62,000 at Lv 30). Production pauses when storage is full.'
                }
            },
            {
                key: 'Barracks',
                name: BUILDING_LABELS.Barracks!,
                tag: { ar: 'المشاة (+7% سرعة/مستوى)', en: 'Infantry (+7% speed/lvl)' },
                desc: {
                    ar: 'تدريب المشاة (م1)، الرمّاحة (م6)، والسيّافين (م9)، كما تُدرّب فيه الكشافة للرومان والجرمان والإغريق (م1).',
                    en: 'Trains Infantry (Lv 1), Spearmen (Lv 6), Swordsmen (Lv 9), plus Scouts at Lv 1 for Romans, Germans, and Greeks.'
                }
            },
            {
                key: 'Stable',
                name: BUILDING_LABELS.Stable!,
                tag: { ar: 'الفرسان والعربات (+7%/مستوى)', en: 'Cavalry & Wagons (+7%/lvl)' },
                desc: {
                    ar: 'تدريب عربات الإمداد (م1)، الكشافة للمغول والفرس (م1)، الفرسان الخفيفة أو فيل الحرب للفرس (م6)، والفرسان الثقيلة (م10).',
                    en: 'Trains Supply Wagons (Lv 1), Scouts for Mongols/Persians (Lv 1), Light Cavalry or War Elephants (Lv 6), and Heavy Cavalry (Lv 10).'
                }
            },
            {
                key: 'SiegeWorkshop',
                name: BUILDING_LABELS.SiegeWorkshop!,
                tag: { ar: 'الكباش (م1) والمقاليع (م3)', en: 'Rams (Lv 1) & Catapults (Lv 3)' },
                desc: {
                    ar: 'تصنيع الكباش لكسر البوابات وأسوار الواحات (م1) والمقاليع لتدمير الأسوار والمباني (م3). كل مستوى يسرّع التصنيع +7%.',
                    en: 'Builds Rams (Lv 1) to smash gates/oasis walls and Catapults (Lv 3) to break walls and demolish buildings (+7% speed/lvl).'
                }
            },
            {
                key: 'Smithy',
                name: BUILDING_LABELS.Smithy!,
                tag: { ar: '+5% قوة قتالية/مستوى', en: '+5% Combat Power/lvl' },
                desc: {
                    ar: 'كل مستوى فوق 1 يرفع قوة هجوم ودفاع جميع جيوش هذه القرية بنسبة 5% في القتال المباشر والحصار.',
                    en: 'Each level above 1 increases both Attack and Defense power of armies from this village by 5%.'
                }
            },
            {
                key: 'Hospital',
                name: BUILDING_LABELS.Hospital!,
                tag: { ar: 'علاج 60% من خسائر الجنود', en: 'Recovers 60% Soldier Losses' },
                desc: {
                    ar: '60% من خسائر الجنود (باستثناء العربات وآلات الحصار) يعودون للمستشفى ويُشفون بتكلفة 1 حديد + 1 قمح للجندي (بسرعة مضاعفة 2x للإغريق).',
                    en: '60% of fallen soldiers (excluding wagons/siege) enter recovery and heal at 1 Iron + 1 Wheat each (2x speed for Greeks).'
                }
            },
            {
                key: 'Wall',
                name: BUILDING_LABELS.Wall!,
                tag: { ar: '+4% حماية/مستوى (8% إغريق)', en: '+4% Protection/lvl (8% Greeks)' },
                desc: {
                    ar: 'يخفف خسائر الحامية داخل القرية حسب سلامة السور، ويعكس ضرراً إضافياً معتدلاً (+35% من مكافأة السور) على المهاجمين.',
                    en: 'Reduces garrison casualties proportionally to wall integrity and adds a moderate (+35% of wall bonus) counter-damage factor.'
                }
            },
            {
                key: 'Market',
                name: BUILDING_LABELS.Market!,
                tag: { ar: 'التجارة والإمداد (+1 عرض/مستوى)', en: 'Trade & Shipments (+1 Slot/lvl)' },
                desc: {
                    ar: 'يتيح نشر وقبول عروض المبادلة مع اللاعبين (عرض إضافي لكل مستوى) وإرسال قوافل الموارد بين قراك. يتوقف عند حصار القرية.',
                    en: 'Enables player market trades (1 active offer slot per level) and village-to-village resource shipments. Paused while besieged.'
                }
            }
        ];

        const biomes: { name: Copy; bonus: Copy }[] = [
            { name: { ar: 'الغابات (Woodland)', en: 'Woodland' }, bonus: { ar: '+30% خشب', en: '+30% Wood' } },
            { name: { ar: 'المرتفعات (Highlands)', en: 'Highlands' }, bonus: { ar: '+25% حديد · +20% حجر', en: '+25% Iron · +20% Stone' } },
            { name: { ar: 'السهول العشبية (Grassland)', en: 'Grassland' }, bonus: { ar: '+25% قمح', en: '+25% Wheat' } },
            { name: { ar: 'الصحراء (Desert)', en: 'Desert' }, bonus: { ar: '+15% قمح · +15% حجر', en: '+15% Wheat · +15% Stone' } },
            { name: { ar: 'المروج (Meadow)', en: 'Meadow' }, bonus: { ar: 'إنتاج متوازن (100%)', en: 'Balanced output (100%)' } }
        ];

        return `
            <div class="guide-resource-strip">
                <div class="guide-res-pill">${icon('i-wood')}<div><strong>${pick({ ar: 'الخشب', en: 'Wood' })}</strong><small>${pick({ ar: 'أساس 240/د · للبناء والواحات', en: 'Base 240/m · Buildings & Infantry' })}</small></div></div>
                <div class="guide-res-pill">${icon('i-iron')}<div><strong>${pick({ ar: 'الحديد', en: 'Iron' })}</strong><small>${pick({ ar: 'أساس 240/د · للأسلحة والفرسان', en: 'Base 240/m · Arms & Cavalry' })}</small></div></div>
                <div class="guide-res-pill">${icon('i-stone')}<div><strong>${pick({ ar: 'الحجر', en: 'Stone' })}</strong><small>${pick({ ar: 'أساس 240/د · للسور والتحصين', en: 'Base 240/m · Walls & Siege' })}</small></div></div>
                <div class="guide-res-pill">${icon('i-wheat')}<div><strong>${pick({ ar: 'القمح', en: 'Wheat' })}</strong><small>${pick({ ar: 'أساس 360/د · لإطعام الجيش', en: 'Base 360/m · Army Upkeep' })}</small></div></div>
            </div>

            <div class="guide-example">
                <strong>${pick({ ar: 'القاعدة الذهبية للتطوير (انتبه للون الأحمر!):', en: 'Golden Upgrade Rule (Watch the Red Indicators!):' })}</strong>
                <p>${pick({
                    ar: 'لا يمكنك تطوير أي مبنى أعلى من مستوى المبنى الرئيسي، وإذا رأيت مورداً أو عبارة "يتطلب المبنى الرئيسي" باللون الأحمر فهذا يوضح ما ينقصك. كما أن تطور مستوى تدريب الوحدة نفسها يسرّع تدريبها +5% لكل مستوى (حتى مستوى 20).',
                    en: 'No building can exceed Main Hall level. Any missing requirement highlights in red. Upgrading a unit\'s own Training Tier also speeds its training by +5% per level (up to Lv 20).'
                })}</p>
            </div>

            <div class="guide-grid-cards">
                ${items.map(b => `
                    <article class="guide-media-card">
                        <div class="guide-thumb">${buildingArt(b.key, this.previewTribe)}</div>
                        <div class="guide-media-body">
                            <strong>${pick(b.name)}</strong>
                            <span class="guide-tag gold">${pick(b.tag)}</span>
                            <p>${pick(b.desc)}</p>
                        </div>
                    </article>`).join('')}
            </div>

            <div class="guide-diagram">
                <div class="guide-diagram-title">${pick({ ar: 'أقاليم الخريطة ومكافآت الإنتاج (Biomes)', en: 'Map Biomes & Regional Bonuses' })}</div>
                <div class="guide-biomes-grid">
                    ${biomes.map(b => `<div class="guide-biome-pill"><strong>${pick(b.name)}</strong><span>${pick(b.bonus)}</span></div>`).join('')}
                </div>
            </div>`;
    }

    private formatCostBadges(u: UnitDefinition): string {
        const c = u.trainingCost;
        const parts: [string, number][] = [
            ['i-wood', c.wood],
            ['i-iron', c.iron],
            ['i-stone', c.stone],
            ['i-wheat', c.wheat]
        ];
        return parts
            .filter(([, amount]) => amount > 0)
            .map(([ic, amount]) => `<span class="guide-cost-chip">${icon(ic)}<b dir="ltr">${amount}</b></span>`)
            .join('');
    }

    private renderUnitCard(u: UnitDefinition, tribeProfile: TribeGuideInfo): string {
        const ar = currentLanguage() === 'ar';
        const s = u.stats;
        const baseSecs = u.trainingCost ? (() => {
            const raw: Record<string, number> = {
                Infantry: 4,
                Scout: (u.tribeType === 'Mongols' || u.tribeType === 'Persians') ? 4 : 3,
                Wagon: 6,
                Catapult: 15,
                Ram: 12,
                Governor: 30,
                Spearman: 5,
                Swordsman: 6,
                LightCavalry: 6,
                Cavalry: 7,
                HeavyCavalry: 10
            };
            return raw[u.role] ?? 5;
        })() : 5;
        const effectiveSecs = baseSecs * tribeProfile.timeMultiplier;
        const timeText = tribeProfile.timeMultiplier < 1
            ? (ar ? `${formatNum(effectiveSecs)}ث (أصل ${baseSecs}ث)` : `${formatNum(effectiveSecs)}s (base ${baseSecs}s)`)
            : (ar ? `${baseSecs}ث` : `${baseSecs}s`);
        const buildingName = BUILDING_LABELS[u.trainingBuilding ?? 'Barracks'];
        const buildingText = buildingName ? pick(buildingName) : (u.trainingBuilding ?? '');
        const reqLevel = u.requiredBuildingLevel ?? 1;
        const unitName = ar ? u.displayName : `${t(u.displayName)} (${u.role})`;

        return `
            <article class="guide-unit-card">
                <div class="guide-unit-head">
                    <div class="guide-thumb is-unit">${this.unitBadge(u.unitId)}</div>
                    <div class="guide-unit-title">
                        <strong>${esc(unitName)}</strong>
                        <div class="guide-unit-tags">
                            <span class="guide-tag gold">${esc(t(u.role))}</span>
                            <span class="guide-tag">${esc(buildingText)} · ${ar ? `م${reqLevel}` : `Lv ${reqLevel}`}</span>
                            <span class="guide-tag time">${ar ? `⏱ ${timeText}` : `⏱ ${timeText}`}</span>
                        </div>
                    </div>
                </div>
                <div class="guide-unit-cost">${this.formatCostBadges(u)}</div>
                <div class="guide-unit-stats-mini">
                    <span>${ar ? 'هجوم' : 'ATK'} <b dir="ltr">${s.attack}</b></span>
                    <span>${ar ? 'د.مشاة' : 'DEF-I'} <b dir="ltr">${s.defenseInfantry}</b></span>
                    <span>${ar ? 'د.فرسان' : 'DEF-C'} <b dir="ltr">${s.defenseCavalry}</b></span>
                    <span>${ar ? 'سرعة' : 'SPD'} <b dir="ltr">${s.speed}</b></span>
                    <span>${ar ? 'حمولة' : 'CARRY'} <b dir="ltr">${s.carryCapacity}</b></span>
                    <span>${ar ? 'قمح/د' : 'UPK'} <b dir="ltr">${s.wheatUpkeep}</b></span>
                </div>
                <p class="guide-unit-desc">${pick(roleDescription(u))}</p>
            </article>`;
    }

    private tabUnits(): string {
        const ar = currentLanguage() === 'ar';
        const activeProfile = TRIBE_PROFILES.find(tp => tp.id === this.previewTribe) ?? TRIBE_PROFILES[0]!;
        const tribeUnits = Array.from(unitCatalog.values())
            .filter(u => u.tribeType === this.previewTribe)
            .sort((a, b) => compareUnitIds(a.unitId, b.unitId));

        const tribeCards = TRIBE_PROFILES.map(tp => {
            const active = tp.id === this.previewTribe;
            return `
                <article class="guide-tribe-card ${active ? 'active' : ''}" role="button" tabindex="0" data-guide-action="tribe" data-tribe="${tp.id}">
                    <div class="guide-tribe-card-head">
                        <div class="guide-thumb small">${unitArtwork(`${tp.id.toLowerCase()}-infantry`)}</div>
                        <div>
                            <strong>${pick(tp.name)}</strong>
                            <span class="guide-tag">${pick(tp.badge)}</span>
                        </div>
                    </div>
                    <p class="guide-tribe-summary">${pick(tp.summary)}</p>
                    <div class="guide-pros-cons">
                        <div class="guide-pro">
                            <b>${ar ? '✓ الإيجابيات' : '✓ Pros'}</b>
                            <ul>${tp.pros.map(item => `<li>${pick(item)}</li>`).join('')}</ul>
                        </div>
                        <div class="guide-con">
                            <b>${ar ? '✕ السلبيات' : '✕ Cons'}</b>
                            <ul>${tp.cons.map(item => `<li>${pick(item)}</li>`).join('')}</ul>
                        </div>
                    </div>
                </article>`;
        }).join('');

        const inlineSwitch = TRIBE_PROFILES.map(tp =>
            `<button type="button" class="guide-tribe-pill ${tp.id === this.previewTribe ? 'active' : ''}" data-guide-action="tribe" data-tribe="${tp.id}">${pick(TRIBE_NAMES[tp.id]!)}</button>`
        ).join('');

        const unitsHtml = tribeUnits.length > 0
            ? `<div class="guide-unit-grid">${tribeUnits.map(u => this.renderUnitCard(u, activeProfile)).join('')}</div>`
            : `<div class="guide-example"><p>${pick({ ar: 'جارٍ تحميل بيانات الوحدات من السيرفر...', en: 'Loading live unit catalog from server...' })}</p></div>`;

        return `
            <div class="guide-tribes-grid">${tribeCards}</div>

            <div class="guide-example">
                <strong>${pick({ ar: 'اضغط علامة (?) على أي وحدة لرؤية كامل خصائصها:', en: 'Click or Hover (?) on Any Unit for Full Stats:' })}</strong>
                <p>${pick({
                    ar: 'جميع معاملات المواجهة بين الوحدات متوازنة على (×1)، وتعتمد المعارك على أرقام الهجوم، الدفاع ضد المشاة والفرسان، نقاط الصحة، الحدادة، والسور. تحرك أي جيش مختلط يساوي سرعة أبطأ وحدة فيه.',
                    en: 'Unit role matchups are unified at (×1) so fights resolve cleanly from Attack, Infantry/Cavalry Defense, HP, Smithy, and Wall bonuses. Mixed armies march at their slowest unit speed.'
                })}</p>
            </div>

            <div class="guide-units-header">
                <div>
                    <h4>${ar ? `وحدات قبيلة ${pick(TRIBE_NAMES[this.previewTribe]!)} (${tribeUnits.length || 10} وحدات)` : `${pick(TRIBE_NAMES[this.previewTribe]!)} Roster (${tribeUnits.length || 10} Units)`}</h4>
                    <small>${pick({ ar: 'اضغط على أي قبيلة أعلاه أو من الأزرار بجانبك لاستبدال الوحدات المعروضة', en: 'Select any tribe above or below to inspect all 10 of its units' })}</small>
                </div>
                <div class="guide-tribe-switcher">${inlineSwitch}</div>
            </div>

            ${unitsHtml}`;
    }

    private tabControls(): string {
        return `
            <section class="guide-hero">
                <div>
                    <h3>${pick({ ar: 'التحكم السريع على الخريطة (RTS)', en: 'Fast Map RTS Controls' })}</h3>
                    <p>${pick({
                        ar: 'أصدر أوامرك مباشرة على الخريطة: حدّد بالزر الأيسر، اسحب الخريطة للتحريك، واستخدم الزر الأيمن لفتح القائمة الشعاعية أو التنفيذ الفوري!',
                        en: 'Command armies straight on the map: select with Left Click, pan by dragging, and Right Click to open the Radial Menu or issue quick orders!'
                    })}</p>
                </div>
                <div class="guide-hero-art">${icon('i-map')}</div>
            </section>

            <div class="guide-keys-grid">
                <article class="guide-key-card">
                    <span class="guide-kbd">${pick({ ar: 'Left Click + السحب', en: 'Left Click & Drag' })}</span>
                    <p>${pick({ ar: 'ضغطة يسار على الجيش أو القرية أو الواحة لتحديدها وعرض تفاصيلها، والسحب بالزر الأيسر على الخريطة يحرك الكاميرا.', en: 'Left-click an army, village, or oasis to select it; Left-click drag pans across the world map.' })}</p>
                </article>
                <article class="guide-key-card">
                    <span class="guide-kbd">${pick({ ar: 'Right Click (القائمة الشعاعية)', en: 'Right Click (Radial Menu)' })}</span>
                    <p>${pick({ ar: 'مع تحديد جيشك: ضغطة يمين على الأرض (تحرك / معسكر)، قرية عدو (غارة / فتح / تدمير / حصار / كشافة)، واحة (احتلال / حصار / حرق / كشافة / تعزيز)، أو قافلة للاعتراض.', en: 'With an army selected: Right-click ground (Move/Camp), enemy village (Raid/Conquer/Raze/Blockade/Scout), oasis (Capture/Besiege/Burn/Scout/Reinforce), or convoy (Intercept).' })}</p>
                </article>
                <article class="guide-key-card">
                    <span class="guide-kbd">${pick({ ar: 'Shift / Ctrl + Click', en: 'Shift / Ctrl + Click' })}</span>
                    <p>${pick({ ar: 'لتحديد أكثر من جيش معاً من شريط الجيوش أو الخريطة، أو لتحديد عدة أنواع وحدات داخل الواحة وإرسالها كجيش واحد.', en: 'Select multiple field armies together or pick multiple unit types inside an oasis to march out as one squad.' })}</p>
                </article>
                <article class="guide-key-card">
                    <span class="guide-kbd">${pick({ ar: 'عجلة الماوس / الأسهم / F1 / ESC', en: 'Wheel / Arrows / F1 / ESC' })}</span>
                    <p>${pick({ ar: 'عجلة الماوس و(+/−) للتكبير والتصغير، الأسهم لتحريك الخريطة وHome للتركيز، F1 للدليل، وESC للقائمة أو إغلاق التحديد والقوائم.', en: 'Wheel or +/- zooms, Arrows pan and Home centers, F1 toggles this guide, and ESC opens Menu or clears selection.' })}</p>
                </article>
            </div>

            <div class="guide-example">
                <strong>${pick({ ar: 'مثال عملي (تحريك جزئي من الواحة):', en: 'Practical Example (Split Units from an Oasis):' })}</strong>
                <p>${pick({
                    ar: 'عند فتح واحتك، اضغط على نوع وحدة ثم Shift+Click على نوع ثانٍ لتحديدهما معاً (مثلاً الفرسان + المشاة)، ثم اضغط Right Click على الهدف على الخريطة: تخرج الوحدات المحددة فقط وتبقى بقية الوحدات في حراسة الواحة.',
                    en: 'Open your oasis, click one unit type and Shift+Click a second, then Right Click a map target: only the selected units march out while the remaining troops stay guarding the oasis.'
                })}</p>
            </div>`;
    }

    private tabCombat(): string {
        return `
            <div class="guide-steps">
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">1</span><strong>${pick({ ar: 'توزيع خسائر المعركة', en: 'Where Casualties Go' })}</strong></div>
                    <p>${pick({ ar: 'كل 5 ثوانٍ تدور جولة قتال: 60% من الجنود الساقطين يُصابون للمستشفى، 30% يُقتلون، و10% يفرّون. أما العربات والكباش والمقاليع فتُدمّر بالكامل ولا تُعالج (والخط الأمامي يحميها حتى يسقط).', en: 'Rounds run every 5s: 60% of lost soldiers go to Hospital, 30% die, and 10% flee. Wagons and siege engines are destroyed permanently (protected behind frontline troops until frontline falls).' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">2</span><strong>${pick({ ar: 'السور والحداد والانسحاب', en: 'Wall, Smithy & Retreat' })}</strong></div>
                    <p>${pick({ ar: 'الحدادة ترفع قوة الجيش +5%/مستوى. السور يعطي +4%/مستوى (8% للإغريق) بتوازن عادل دون مضاعفات مبالغة. تذكّر: سحب جيش من قتال أو حصار جارٍ يوقّع خسائر انسحاب 20%!', en: 'Smithy adds +5% power/lvl. Wall gives +4% protection/lvl (8% Greeks) with balanced counter-damage. Recalling an army out of active battle/siege inflicts 20% retreat losses!' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">3</span><strong>${pick({ ar: 'أوضاع مهاجمة القرى', en: 'City Attack Modes' })}</strong></div>
                    <p>${pick({ ar: 'غارة (Raid): نهب 25% (+20% للجرمان). حصار (Blockade): إيقاف القوافل وخفض القمح 50%. تدمير (Raze): يحتاج مقلاعاً لهدم المباني. فتح (Conquest): يحتاج حاكماً (م5 بالمبنى الرئيسي) لخفض الولاء 5%/حاكم حتى 3 حكام بالجولة.', en: 'Raid plunders 25% (+20% Germans); Blockade halts trade and halves wheat; Raze needs Catapults; Conquest needs a Governor (Main Hall Lv 5) lowering loyalty 5%/governor (max 3/round).' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">4</span><strong>${pick({ ar: 'القمح، المخيمات، والتسريح', en: 'Wheat, Camps & Disband' })}</strong></div>
                    <p>${pick({ ar: 'نفاد قمح الجيش الميداني يرسل جنديين للمستشفى كل 10 ثوانٍ — فعّل الإمداد التلقائي أو أضف عربات. المعسكر (نصف قطر 90) يشتبك تلقائياً ويطلب جمع الغنائم عند امتلاء 90% من حمولته. وإذا صار القمح سالباً بالقرية يمكنك تسريح الفائض من قائمة الجيش!', en: 'Starving field armies lose 2 wounded every 10s — enable auto-supply or attach wagons. Camps (radius 90) auto-engage enemies and request loot collection at 90% load. Use Disband Troops inside your village/army drawer if upkeep turns negative.' })}</p>
                </article>
            </div>

            <div class="guide-example">
                <strong>${pick({ ar: 'قواعد الاستطلاع واقتحام الحصون (حتى 16 جيشاً ميدانياً):', en: 'Scouting Rules & Fortified Sieges (Max 16 Field Armies):' })}</strong>
                <p>${pick({
                    ar: '1) أرسل جيش كشافة فقط؛ إذا امتلك المدافع كشافة أكثر منك يُقتل كشافتك وتُحجب المعلومات، وإذا تفوقت يكشف التقرير قواته لمدة 60ث للجيوش المتحركة — 2) الكباش تكسر البوابة 12%/جولة والمقاليع تخفّض السور 5%/جولة — 3) في الجيوش المختلطة، الوحدات الأضعف دفاعاً أو هجوماً تتلقى حصة أكبر من الضرر، فاحرص على توازن تشكيلتك.',
                    en: '1) Send Scout-only squads: strictly more enemy scouts kills all attackers with zero intel; otherwise you reveal forces (60s on moving armies) — 2) Rams break gates 12%/round, Catapults lower walls 5%/round — 3) Weak low-stat troops absorb a higher loss share in mixed armies, so protect fragile units.'
                })}</p>
            </div>`;
    }

    private tabOases(): string {
        return `
            <div class="guide-steps">
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">1</span><strong>${pick({ ar: 'واحة الخشب', en: 'Wood Oasis' })}</strong></div>
                    <div class="guide-step-visual">${oasisArt('Wood')}</div>
                    <p>${pick({ ar: 'تمنح قريتك +50% خشب و+25% لبقية الموارد (وتزيد +5% لكل ترقية لمستوى الواحة حتى م5)، وتنتج محلياً 240 خشب و120 لبقية الموارد بالدقيقة.', en: 'Grants +50% Wood and +25% other resources (+5% per oasis level up to Lv 5) plus local 240/min primary & 120/min secondary output.' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">2</span><strong>${pick({ ar: 'واحة الحديد', en: 'Iron Oasis' })}</strong></div>
                    <div class="guide-step-visual">${oasisArt('Iron')}</div>
                    <p>${pick({ ar: 'ممتازة لتمويل الحدادة والفرسان والسيوف. تنقل العربات الموارد تلقائياً للقرية عند وصول المخزون الإجمالي إلى 2,000 (السعة 15,000).', en: 'Ideal for funding Smithy upgrades and heavy units. Auto-transport wagons collect once oasis stock hits 2,000 (cap 15,000).' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">3</span><strong>${pick({ ar: 'واحة الحجر والأسوار', en: 'Stone Oasis & Walls' })}</strong></div>
                    <div class="guide-step-visual">${oasisArt('Stone')}</div>
                    <p>${pick({ ar: 'تغذي بناء الأسوار والتحصينات. يمكنك بناء سور للواحة حتى مستوى 20 (متانة 150×المستوى) يضطر المهاجم لكسره قبل القتال أو الاحتلال.', en: 'Fuels village and oasis walls. You can upgrade any oasis wall up to Lv 20 (150 HP per level), forcing attackers to breach it first.' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">4</span><strong>${pick({ ar: 'واحة القمح والحرق', en: 'Wheat Oasis & Burning' })}</strong></div>
                    <div class="guide-step-visual">${oasisArt('Wheat')}</div>
                    <p>${pick({ ar: 'الجيوش الحارسة في واحتك تتغذى أولاً من قمح الواحة المخزون! انتبه: يمكن للعدو حصار واحتك (يوقف مكافآتها فوراً) أو حرقها خلال 90ث لتصبح خربة 5 دقائق.', en: 'Garrisons defending your oasis feed from its stored wheat first! Watch out: enemies can Blockade it or Burn it in 90s for 5 minutes.' })}</p>
                </article>
            </div>

            <div class="guide-split-preview">
                <div class="guide-mock-box">
                    <strong>${pick({ ar: 'وحداتك داخل الواحة + الدمج التلقائي', en: 'Your Oasis Garrison & Auto-Merge' })}</strong>
                    <p>${pick({
                        ar: 'أي جيشين لك يحرسان نفس الواحة يندمجان تلقائياً في جيش واحد. تظهر كل وحدة بشكل منفصل لتختار منها بـ Shift وترسلها كجيش واحد بالزر الأيمن.',
                        en: 'Your armies guarding the same oasis merge automatically. Each unit type lists separately for Shift-selection and dispatch.'
                    })}</p>
                </div>
                <div class="guide-mock-box green">
                    <strong>${pick({ ar: 'التحالفات وتعزيزات الحلفاء (الصف الأخضر)', en: 'Alliances & Allied Reinforcements (Green Row)' })}</strong>
                    <p>${pick({
                        ar: 'يمكنك إرسال طلب تحالف من قائمة الممالك أو بدء اللعب في فريق واحد. الحلفاء لا يهاجمون بعضهم، ينضمون معاً لنفس المعركة، يتشاركون قلعة التاج دون نزاع، وتظهر تعزيزاتهم في الصف الأخضر.',
                        en: 'Form alliances from Kingdoms or team up in the lobby. Allies never clash, reinforce your sites (shown in the green row), join ongoing allied battles, and hold Crown Keep together without contesting.'
                    })}</p>
                </div>
            </div>`;
    }

    private tabVictory(): string {
        return `
            <section class="guide-hero">
                <div>
                    <h3>${pick({ ar: 'طريق النصر وحسم المباراة', en: 'How Matches Are Won' })}</h3>
                    <p>${pick({
                        ar: 'المدة الافتراضية للمباراة 60 دقيقة مع حماية 5 دقائق في البداية، وتفتح قلعة التاج في وسط الخريطة عند الدقيقة 45 (قابلة للتعديل من إعدادات الغرفة).',
                        en: 'Default matches run 60 minutes with a 5-minute peace shield, and Crown Keep unlocks at 45 minutes (all customizable in room lobby).'
                    })}</p>
                </div>
                <div class="guide-hero-art">${icon('i-shield')}</div>
            </section>

            <div class="guide-steps">
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">1</span><strong>${pick({ ar: 'فترة الحماية والبناء', en: 'Peace Shield Phase' })}</strong></div>
                    <p>${pick({ ar: 'تُمنع الهجمات على قرى وواحات وقوافل اللاعبين خلال فترة الحماية — استغلها في تطوير الحدادة والمباني واحتلال الواحات المحايدة.', en: 'Attacks against enemy villages, occupied oases, and convoys are blocked during peace shield — expand safely.' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">2</span><strong>${pick({ ar: 'النصر الفوري عبر قلعة التاج', en: 'Crown Keep Instant Win' })}</strong></div>
                    <p>${pick({ ar: 'فور فتح قلعة التاج، السيطرة المتواصلة داخل دائرتها (نصف قطر 45) لمدة 5 دقائق متواصلة تمنحك الفوز الفوري! دخول جيش غير حليف يبدأ قتالاً ويوقف العداد.', en: 'Once Crown Keep unlocks, holding its zone (radius 45) uncontested for 5 continuous minutes wins the match immediately!' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">3</span><strong>${pick({ ar: 'حسم النقاط أو الإقصاء', en: 'Score & Domination Victory' })}</strong></div>
                    <p>${pick({ ar: 'إذا بقيت المملكة الوحيدة الصامدة تفوز فوراً، وإذا انتهى الوقت تُحسم النتيجة بالنقاط: (ثواني القلعة×10) + (مستويات القرية×30) + (الموارد÷200) + (القتلى×5) + (الحامية÷10).', en: 'Eliminating all rivals wins by domination; otherwise highest Score wins: (CastleSec×10) + (VillageLvls×30) + (Resources/200) + (Kills×5) + (Garrison/10).' })}</p>
                </article>
            </div>

            <div class="guide-example">
                <strong>${pick({ ar: 'نصيحة ختامية:', en: 'Pro Tip:' })}</strong>
                <p>${pick({
                    ar: 'لا تهاجم قرية محصنة بدون كشافة أو كباش ومقاليع، ولا تترك جيشاً ميدانياً بدون قمح أو إمداد تلقائي! استفد من ميزة تسريح القوات عند أزمات القمح ومن الدمج التلقائي داخل الواحات لإعادة بناء صفوفك بسرعة.',
                    en: 'Never attack fortified cities blind or without siege engines, and keep field armies supplied! Use Disband Troops during wheat crunches and oasis auto-merge to regroup rapidly.'
                })}</p>
            </div>`;
    }
}
