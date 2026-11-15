import { buildingArt, oasisArt, tribes } from './art';
import { currentLanguage } from './locale';
import { unitArtwork } from './unit-art';

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

const pick = (text: Copy) => (currentLanguage() === 'ar' ? text.ar : text.en);

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
    Germans: { ar: 'الألمان', en: 'Germans' },
    Greeks: { ar: 'الإغريق', en: 'Greeks' },
    Mongols: { ar: 'المغول', en: 'Mongols' },
    Persians: { ar: 'الفرس', en: 'Persians' }
};

function icon(id: string) {
    return `<svg aria-hidden="true"><use href="#${id}"/></svg>`;
}

export class GameGuide {
    private dialog: HTMLDialogElement;
    private activeTab: GuideTabId = 'quickstart';
    private previewTribe = 'Romans';

    constructor(private replayTutorial?: () => void) {
        this.dialog = document.createElement('dialog');
        this.dialog.id = 'game-guide';
        this.dialog.className = 'game-guide';
        this.dialog.setAttribute('data-no-translate', '');
        this.dialog.setAttribute('aria-labelledby', 'guide-heading');
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
                this.render();
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
            if (this.dialog.open) this.render();
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
    }

    close() {
        if (this.dialog.open) this.dialog.close();
    }

    private render() {
        const ar = currentLanguage() === 'ar';
        this.dialog.dir = ar ? 'rtl' : 'ltr';
        const idx = TABS.findIndex(tab => tab.id === this.activeTab);
        const tribeBtns = tribes
            .map(tr => `<button type="button" class="guide-tribe-pill ${tr === this.previewTribe ? 'active' : ''}" data-guide-action="tribe" data-tribe="${tr}">${pick(TRIBE_NAMES[tr]!)}</button>`)
            .join('');
        const navBtns = TABS.map((tab, i) => `
            <button type="button" class="guide-tab ${tab.id === this.activeTab ? 'active' : ''}" data-guide-action="tab" data-tab="${tab.id}">
                <span class="guide-tab-num">${i + 1}</span>
                <span>${pick(tab.label)}</span>
            </button>`).join('');

        this.dialog.innerHTML = `
            <header class="guide-head">
                <div class="guide-title-wrap">
                    <span class="guide-badge-icon">${icon('i-book')}</span>
                    <div>
                        <h2 id="guide-heading">${pick({ ar: 'دليل Warzonee — كيف تلعب وتنتصر', en: 'Warzonee Field Manual — How to Play & Win' })}</h2>
                        <p>${pick({ ar: 'شرح مبسط بالصور والأمثلة لكل أساسيات اللعبة', en: 'Simple visual guide with real game art and practical examples' })}</p>
                    </div>
                </div>
                <div class="guide-head-actions">
                    <div class="guide-tribe-pills" title="${pick({ ar: 'اختر قبيلة لمعاينة أشكال مبانيها وجنودها', en: 'Preview art for any tribe' })}">${tribeBtns}</div>
                    <button type="button" class="guide-close" data-guide-action="close" aria-label="${pick({ ar: 'إغلاق', en: 'Close' })}">×</button>
                </div>
            </header>
            <div class="guide-shell">
                <nav class="guide-nav" aria-label="${pick({ ar: 'أقسام الدليل', en: 'Guide sections' })}">${navBtns}</nav>
                <main class="guide-content">${this.renderTab()}</main>
            </div>
            <footer class="guide-foot">
                <small>${pick({ ar: 'تلميح: اضغط F1 أو زر التعليمات في أي وقت أثناء المباراة لفتح هذا الدليل.', en: 'Tip: Press F1 or the Guide button anytime during a match to open this manual.' })}</small>
                <div class="guide-foot-btns">
                    ${this.replayTutorial ? `<button type="button" class="quiet" data-guide-action="replay-tutorial">${pick({ ar: 'إعادة التيتوريال التفاعلي', en: 'Replay Tutorial' })}</button>` : ''}
                    <button type="button" class="quiet" data-guide-action="prev" ${idx === 0 ? 'disabled' : ''}>${pick({ ar: 'السابق', en: 'Previous' })}</button>
                    ${idx < TABS.length - 1
                        ? `<button type="button" class="primary" data-guide-action="next">${pick({ ar: 'التالي', en: 'Next' })}</button>`
                        : `<button type="button" class="primary" data-guide-action="close">${pick({ ar: 'فهمت — ابدأ اللعب', en: 'Got it — Back to Game' })}</button>`}
                </div>
            </footer>`;

        this.dialog.querySelector('.guide-content')?.scrollTo({ top: 0 });
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
        return `
            <section class="guide-hero">
                <div>
                    <h3>${pick({ ar: 'فكرة اللعبة في 30 ثانية', en: 'The Game in 30 Seconds' })}</h3>
                    <p>${pick({
                        ar: 'أنت تبدأ بقرية صغيرة وحماية مؤقتة. طوّر مبانيك لإنتاج الموارد، درّب جيشك، سيطر على الواحات المجاورة لتسريع اقتصادك، ثم نافس على احتلال قلعة التاج في منتصف الخريطة أو تصدّر النقاط للفوز.',
                        en: 'You start with one village under temporary starter protection. Upgrade buildings, train armies, capture nearby oases to boost resource income, then fight for the central Crown Keep or lead the scoreboard.'
                    })}</p>
                </div>
                <div class="guide-hero-art">${buildingArt('MainHall', this.previewTribe)}</div>
            </section>

            <div class="guide-steps">
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">1</span><strong>${pick({ ar: 'طوّر القرية', en: 'Grow Village' })}</strong></div>
                    <div class="guide-step-visual">${buildingArt('MainHall', this.previewTribe)}${buildingArt('Warehouse', this.previewTribe)}</div>
                    <p>${pick({ ar: 'ابدأ بالمبنى الرئيسي والمخزن ثم الثكنة لفتح الوحدات ورفع سرعة التدريب.', en: 'Upgrade Main Hall and Warehouse first, then Barracks to unlock troops.' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">2</span><strong>${pick({ ar: 'درّب جيشك', en: 'Train Troops' })}</strong></div>
                    <div class="guide-step-visual">${unitArtwork(`${tId}-infantry`)}${unitArtwork(`${tId}-cavalry`)}</div>
                    <p>${pick({ ar: 'من الشريط السفلي اختر الثكنة أو الإسطبل ودرّب مشاة أو فرسان لحماية قريتك والهجوم.', en: 'Use the bottom shelf to train infantry and cavalry from Barracks and Stable.' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">3</span><strong>${pick({ ar: 'سيطر على الواحات', en: 'Claim Oases' })}</strong></div>
                    <div class="guide-step-visual">${oasisArt('Wood')}${oasisArt('Wheat')}</div>
                    <p>${pick({ ar: 'أرسل جيشاً لأقرب واحة مهجورة بالرايت كليك لزيادة إنتاج الخشب أو الحديد أو الحجر أو القمح.', en: 'Right-click an unowned oasis with your army to boost Wood, Iron, Stone, or Wheat.' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">4</span><strong>${pick({ ar: 'قلعة التاج والنصر', en: 'Crown Keep' })}</strong></div>
                    <div class="guide-step-visual">${icon('i-shield')}${icon('i-sword')}</div>
                    <p>${pick({ ar: 'عندما تفتح قلعة التاج بوسط الخريطة، أرسل أقوى جيوشك لاحتلالها وحمايتها حتى النهاية.', en: 'When Crown Keep opens in the center, capture and defend it or top the Kingdom ranks.' })}</p>
                </article>
            </div>

            <div class="guide-example">
                <strong>${pick({ ar: 'مثال عملي لأول دقيقتين في المباراة:', en: 'Practical Example — Your First 2 Minutes:' })}</strong>
                <p>${pick({
                    ar: '1) اضغط على المبنى الرئيسي في الشريط السفلي وطوّره للمستوى 2 — 2) طوّر الثكنة للمستوى 2 وابدأ تدريب 10–15 جندي مشاة — 3) فور جاهزية الجيش، اضغط عليه بالزر الأيسر ثم بالزر الأيمن على أقرب واحة مهجورة لاحتلالها والحصول على موارد إضافية مبكراً.',
                    en: '1) Click Main Hall in the bottom bar and upgrade it to Level 2 — 2) Upgrade Barracks and train 10–15 Infantry — 3) Select the army with Left Click, then Right Click the closest neutral Oasis to capture early bonus income.'
                })}</p>
            </div>`;
    }

    private tabBuildings(): string {
        const items: { key: string; name: Copy; tag: Copy; desc: Copy }[] = [
            { key: 'MainHall', name: { ar: 'المبنى الرئيسي', en: 'Main Hall' }, tag: { ar: 'أساس التطوير', en: 'Master Cap' }, desc: { ar: 'يحدد الحد الأقصى لمستوى باقي المباني ويسرّع اقتصاد القرية.', en: 'Sets the max level cap for all other buildings and boosts output.' } },
            { key: 'Warehouse', name: { ar: 'المخزن', en: 'Warehouse' }, tag: { ar: 'السعة', en: 'Storage' }, desc: { ar: 'يرفع سعة تخزين الخشب والحديد والحجر والقمح حتى لا تتوقف مواردك.', en: 'Increases resource storage capacity so production never stalls.' } },
            { key: 'Barracks', name: { ar: 'الثكنة', en: 'Barracks' }, tag: { ar: 'المشاة (+7% سرعة/مستوى)', en: 'Infantry (+7% speed/lvl)' }, desc: { ar: 'تدريب المشاة والرماة والرمّاحة والمبارزين وفتح الوحدات المتقدمة.', en: 'Trains infantry, archers, spearmen, and swordsmen; +7% speed per level.' } },
            { key: 'Stable', name: { ar: 'الإسطبل', en: 'Stable' }, tag: { ar: 'الفرسان (+7% سرعة/مستوى)', en: 'Cavalry (+7% speed/lvl)' }, desc: { ar: 'تدريب الكشافة والفرسان السريعة والثقيلة للغارات السريعة.', en: 'Trains scouts, light/heavy cavalry, and horsemen.' } },
            { key: 'SiegeWorkshop', name: { ar: 'ورشة الحصار', en: 'Siege Workshop' }, tag: { ar: 'الحصار والحاكم', en: 'Siege & Governor' }, desc: { ar: 'تصنيع الكباش والمقاليع وأبراج الحصار وتدريب الحاكم لاحتلال القرى.', en: 'Builds rams, catapults, siege towers, and Governors for city conquest.' } },
            { key: 'Smithy', name: { ar: 'الحداد', en: 'Smithy' }, tag: { ar: '+5% قوة قتالية/مستوى', en: '+5% Combat Power/lvl' }, desc: { ar: 'كل مستوى فوق 1 يرفع قوة هجوم ودفاع جميع جيوش هذه القرية بنسبة 5%.', en: 'Each level above 1 boosts combat power of armies from this village by 5%.' } },
            { key: 'Hospital', name: { ar: 'المستشفى', en: 'Hospital' }, tag: { ar: 'علاج 60% من الخسائر', en: 'Heals Wounded' }, desc: { ar: '60% من الجنود المصابين يعودون للمستشفى ويُشفون تلقائياً (ضعف السرعة للإغريق).', en: '60% of casualties become wounded and recover here over time (2x for Greeks).' } },
            { key: 'Wall', name: { ar: 'السور', en: 'Wall' }, tag: { ar: '+4% حماية/مستوى (8% إغريق)', en: '+4% Def/lvl (8% Greeks)' }, desc: { ar: 'يخفف خسائر الحامية داخل القرية أثناء الحصار ويزيد ضررها بشكل متوازن على المهاجمين.', en: 'Reduces garrison casualties during sieges and gives defenders a moderate wall-top counter bonus.' } },
            { key: 'Market', name: { ar: 'السوق', en: 'Market' }, tag: { ar: 'التجارة والإمداد', en: 'Trade & Supply' }, desc: { ar: 'مبادلة الموارد مع اللاعبين أو إرسال قوافل إمداد بين قراك.', en: 'Trade resources with other players or dispatch resource deliveries.' } }
        ];

        return `
            <div class="guide-resource-strip">
                <div class="guide-res-pill">${icon('i-wood')}<div><strong>${pick({ ar: 'الخشب', en: 'Wood' })}</strong><small>${pick({ ar: 'للبناء والمشاة', en: 'Buildings & Infantry' })}</small></div></div>
                <div class="guide-res-pill">${icon('i-iron')}<div><strong>${pick({ ar: 'الحديد', en: 'Iron' })}</strong><small>${pick({ ar: 'للأسلحة والفرسان', en: 'Weapons & Cavalry' })}</small></div></div>
                <div class="guide-res-pill">${icon('i-stone')}<div><strong>${pick({ ar: 'الحجر', en: 'Stone' })}</strong><small>${pick({ ar: 'للسور والتحصين', en: 'Walls & Fortifications' })}</small></div></div>
                <div class="guide-res-pill">${icon('i-wheat')}<div><strong>${pick({ ar: 'القمح', en: 'Wheat' })}</strong><small>${pick({ ar: 'إطعام الجيش المتحرك', en: 'Feeds Marching Armies' })}</small></div></div>
            </div>

            <div class="guide-example">
                <strong>${pick({ ar: 'القاعدة الذهبية للتطوير (انتبه للون الأحمر!):', en: 'Golden Upgrade Rule (Watch the Red Indicators!):' })}</strong>
                <p>${pick({
                    ar: 'لا يمكنك تطوير أي مبنى أعلى من مستوى المبنى الرئيسي، وإذا رأيت مورداً أو عبارة "يتطلب المبنى الرئيسي" باللون الأحمر فهذا يوضح بالضبط ما ينقصك للتطوير التالي.',
                    en: 'No building can exceed your Main Hall level. Any missing resource or "Requires Main Hall" message highlights in red so you know what to upgrade first.'
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
            </div>`;
    }

    private tabUnits(): string {
        const tId = this.previewTribe.toLowerCase();
        const tribePerks: { name: Copy; badge: Copy; desc: Copy }[] = [
            { name: { ar: 'الرومان (Romans)', en: 'Romans' }, badge: { ar: '+1 بناء تلقائي', en: '+1 Auto Slot' }, desc: { ar: 'خانة تطوير تلقائي إضافية لتسريع نمو القرية بشكل مريح.', en: 'Extra automatic build slot to keep village upgrades flowing.' } },
            { name: { ar: 'الألمان (Germans)', en: 'Germans' }, badge: { ar: '-20% تكلفة · +20% نهب', en: '-20% Cost · +20% Loot' }, desc: { ar: 'تدريب أسرع 30%، تكلفة أقل 20%، وغنائم أكبر عند الإغارة.', en: '30% faster training, 20% cheaper troops, and +20% raid loot.' } },
            { name: { ar: 'الإغريق (Greeks)', en: 'Greeks' }, badge: { ar: 'سور 2x · علاج 2x', en: '2x Wall · 2x Healing' }, desc: { ar: 'دفاع سور القرية مضاعف (+8% للمستوى) وشفاء المستشفى أسرع مرتين.', en: 'Double wall defense bonus (+8%/level) and 2x faster hospital healing.' } },
            { name: { ar: 'المغول (Mongols)', en: 'Mongols' }, badge: { ar: 'نقل القرية', en: 'Relocate Village' }, desc: { ar: 'قدرة فريدة على نقل القرية لموقع استراتيجي جديد على الخريطة.', en: 'Can relocate settlements to new strategic positions on the map.' } },
            { name: { ar: 'الفرس (Persians)', en: 'Persians' }, badge: { ar: '+20% موارد · تدريب أسرع 50%', en: '+20% Resources · 2x Train Speed' }, desc: { ar: 'اقتصاد قوي جداً مع سرعة تدريب مضاعفة لبناء الجيوش.', en: '+20% resource production and 50% faster unit training time.' } }
        ];

        const roles: { role: string; name: Copy; tag: Copy; desc: Copy }[] = [
            { role: 'infantry', name: { ar: 'مشاة / رماح / سيوف', en: 'Infantry / Spearmen / Swordsmen' }, tag: { ar: 'خط أمامي', en: 'Frontline' }, desc: { ar: 'رخيصة وقوية في الحشود، ممتازة لاحتلال الواحات وتشكيل عمود الجيش.', en: 'Cost-effective backbone for holding ground, defense, and capturing oases.' } },
            { role: 'archer', name: { ar: 'رماة ونشابون', en: 'Archers & Crossbowmen' }, tag: { ar: 'دعم ناري', en: 'Ranged Support' }, desc: { ar: 'تقدم ضرراً ممتازاً خلف المشاة بتكلفة متوازنة.', en: 'Steady firepower backing up frontline infantry.' } },
            { role: 'cavalry', name: { ar: 'فرسان خفيفة وثقيلة', en: 'Light / Cavalry / Heavy Cavalry' }, tag: { ar: 'سرعة وضربات خاطفة', en: 'Fast Strike' }, desc: { ar: 'تصل بسرعة للأهداف والواحات، والفرسان الثقيلة ممتازة لكسر الدفاعات.', en: 'High map speed for raids and flanking; heavy cavalry hits hard.' } },
            { role: 'scout', name: { ar: 'الكشافة', en: 'Scouts' }, tag: { ar: 'استطلاع ومضاد استطلاع', en: 'Recon & Counter-Spy' }, desc: { ar: 'تكشف قوات العدو داخل القرى والواحات والجيوش وتصد كشافة الخصم.', en: 'Reveals enemy troops in cities, oases, or field armies, and blocks enemy spies.' } },
            { role: 'wagon', name: { ar: 'عربة الإمداد', en: 'Supply Wagon' }, tag: { ar: 'نقل قمح وموارد', en: 'Food & Cargo' }, desc: { ar: 'تحمل القمح للجيش البعيد عن قريته وتنقل الموارد والغنائم.', en: 'Carries extra wheat to keep distant armies fed and hauls extra loot.' } },
            { role: 'ram', name: { ar: 'الكبش', en: 'Battering Ram' }, tag: { ar: 'كسر البوابة وسور الواحة', en: 'Breaks Gates & Walls' }, desc: { ar: 'يدمر بوابة القرية أسرع بـ 12x ويسرّع اختراق أسوار الواحات.', en: 'Smashes city gates rapidly and tears down oasis walls.' } },
            { role: 'catapult', name: { ar: 'المقلاع', en: 'Catapult' }, tag: { ar: 'تدمير السور والمباني', en: 'Wall & Building Siege' }, desc: { ar: 'يخفض سلامة سور القرية 5% لكل مقلاع في الجولة ويهدم مباني العدو.', en: 'Reduces village wall integrity by 5% per catapult per round and demolishes buildings.' } },
            { role: 'governor', name: { ar: 'الحاكم', en: 'Governor' }, tag: { ar: 'احتلال القرى', en: 'City Conquest' }, desc: { ar: 'يخفض ولاء قرية العدو بعد سقوط حاميتها ليضمها لمملكتك.', en: 'Lowers enemy village loyalty after defenders fall to conquer the settlement.' } }
        ];

        return `
            <div class="guide-grid-cards">
                ${tribePerks.map(tp => `
                    <article class="guide-key-card">
                        <strong style="color:#fce2a6;display:block;margin-bottom:4px">${pick(tp.name)}</strong>
                        <span class="guide-tag">${pick(tp.badge)}</span>
                        <p>${pick(tp.desc)}</p>
                    </article>`).join('')}
            </div>

            <div class="guide-example">
                <strong>${pick({ ar: 'توحيد المواجهات وتمرير الماوس على أي وحدة:', en: 'Balanced Stats & Unit Tooltip (?):' })}</strong>
                <p>${pick({
                    ar: 'جميع معاملات الضرب متوازنة على 1 (تعتمد المعركة على أرقام القوة والدفاع والحداد والسور). اضغط على علامة (?) بجانب أي وحدة أثناء اللعب لرؤية هجومها ودفاعها وسرعتها وحمولتها.',
                    en: 'Unit matchups are unified at 1.0 so outcomes rely cleanly on base Attack/Defense, Smithy, and Walls. Hover or click (?) on any unit card anytime to inspect full stats.'
                })}</p>
            </div>

            <div class="guide-grid-cards">
                ${roles.map(r => `
                    <article class="guide-media-card">
                        <div class="guide-thumb">${unitArtwork(`${tId}-${r.role}`)}</div>
                        <div class="guide-media-body">
                            <strong>${pick(r.name)}</strong>
                            <span class="guide-tag gold">${pick(r.tag)}</span>
                            <p>${pick(r.desc)}</p>
                        </div>
                    </article>`).join('')}
            </div>`;
    }

    private tabControls(): string {
        return `
            <section class="guide-hero">
                <div>
                    <h3>${pick({ ar: 'التحكم السريع على الخريطة (مثل ألعاب الاستراتيجية RTS)', en: 'Fast Map RTS Controls' })}</h3>
                    <p>${pick({
                        ar: 'يمكنك إصدار معظم الأوامر مباشرة على الخريطة بالماوس بدون فتح نوافذ كثيرة: حدد بالزر الأيسر، ونفّذ بالزر الأيمن!',
                        en: 'Issue most orders directly on the map without extra menus: select with Left Click, act with Right Click!'
                    })}</p>
                </div>
                <div class="guide-hero-art">${icon('i-map')}</div>
            </section>

            <div class="guide-keys-grid">
                <article class="guide-key-card">
                    <span class="guide-kbd">${pick({ ar: 'Left Click (ضغطة يسار)', en: 'Left Click' })}</span>
                    <p>${pick({ ar: 'تحديد جيشك على الخريطة، أو فتح تفاصيل القرية / الواحة، أو السحب لتحديد عدة جيوش معاً.', en: 'Select an army, inspect a village/oasis, or drag a box to select multiple armies.' })}</p>
                </article>
                <article class="guide-key-card">
                    <span class="guide-kbd">${pick({ ar: 'Right Click (ضغطة يمين)', en: 'Right Click' })}</span>
                    <p>${pick({ ar: 'بعد تحديد جيشك: ضغطة يمين على الأرض = تحرك، على عدو/قرية = هجوم، على واحة حليفة = تعزيز. الضغط المطوّل أو القائمة الشعاعية تمنحك خيارات مثل التخييم والنهب.', en: 'With an army selected: Right-click ground to Move, enemy to Attack, allied site to Reinforce, or open the Radial Menu for Camp/Raid/Blockade.' })}</p>
                </article>
                <article class="guide-key-card">
                    <span class="guide-kbd">${pick({ ar: 'Shift + Click', en: 'Shift + Click' })}</span>
                    <p>${pick({ ar: 'إضافة/إزالة جيش للتحديد، أو تحديد أكثر من نوع وحدة داخل الواحة لإرسالها معاً كجيش واحد.', en: 'Add/remove armies in selection, or select multiple unit types inside an oasis to dispatch together as one army.' })}</p>
                </article>
                <article class="guide-key-card">
                    <span class="guide-kbd">${pick({ ar: 'عجلة الماوس / ESC / F1', en: 'Scroll / ESC / F1' })}</span>
                    <p>${pick({ ar: 'عجلة الماوس للتكبير والتصغير، ESC لإلغاء التحديد أو فتح قائمة اللعبة، و F1 لفتح دليل التعليمات.', en: 'Mouse wheel zooms the map, ESC clears selection or opens the menu, and F1 opens this guide.' })}</p>
                </article>
            </div>

            <div class="guide-example">
                <strong>${pick({ ar: 'مثال عملي (تحريك جزئي من الواحة أو الجيش):', en: 'Practical Example (Split Units from an Oasis or Army):' })}</strong>
                <p>${pick({
                    ar: 'عند فتح واحتك، اضغط على نوع وحدة ثم Shift+Click على نوع ثانٍ لتحديدهما معاً (مثلاً الفرسان + الرماة)، ثم اضغط Right Click على الهدف على الخريطة: تخرج الوحدات المحددة فقط كجيش واحد وتبقى بقية الوحدات في حراسة الواحة.',
                    en: 'Open your oasis, click one unit type and Shift+Click a second (e.g. Cavalry + Archers), then Right Click a target on the map: only the chosen troops march out together while the rest remain guarding the oasis.'
                })}</p>
            </div>`;
    }

    private tabCombat(): string {
        return `
            <div class="guide-steps">
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">1</span><strong>${pick({ ar: 'نتيجة خسائر المعركة', en: 'Where Casualties Go' })}</strong></div>
                    <p>${pick({ ar: 'من كل 10 جنود يسقطون: 6 يُصابون ويُرسلون للمستشفى (60%)، 3 يُقتلون (30%)، و1 ينجو هارباً (10%). كلما طوّرت المستشفى عاد الجرحى للقتال أسرع.', en: 'For every 10 troops lost: 60% go to Hospital, 30% die, and 10% flee. Upgrade Hospital to recover troops faster.' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">2</span><strong>${pick({ ar: 'السور والحداد', en: 'Wall & Smithy' })}</strong></div>
                    <p>${pick({ ar: 'الحداد يرفع قوة جيشك +5% لكل مستوى. سور القرية يعطي +4% حماية للمستوى (8% للإغريق) ويعزز صمود الحامية أثناء الحصار.', en: 'Smithy adds +5% army combat power per level. Village Wall gives +4% protection/lvl (8% for Greeks) to bolster garrison defense during sieges.' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">3</span><strong>${pick({ ar: 'أوضاع الهجوم على القرية', en: 'City Attack Modes' })}</strong></div>
                    <p>${pick({ ar: 'نهب (Raid) لسرقة الموارد، حصار اقتصادي (Blockade) لتعطيل القرية، تدمير (Demolish) بالمقاليع، واحتلال (Conquest) مع الحاكم.', en: 'Raid steals resources; Blockade halves wheat and cuts trade; Demolish uses catapults; Conquest uses a Governor.' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">4</span><strong>${pick({ ar: 'القمح وعربات الإمداد', en: 'Wheat & Supply' })}</strong></div>
                    <p>${pick({ ar: 'الجيوش البعيدة تستهلك القمح من مخزونها. إذا نفد القمح تتضرر وحداتك — أضف عربة إمداد أو أمرّر الجيش بقرية/واحة حليفة.', en: 'Marching armies consume carried wheat. Bring Supply Wagons or pass by allied villages/oases to stay fed.' })}</p>
                </article>
            </div>

            <div class="guide-example">
                <strong>${pick({ ar: 'مثال عملي لاقتحام قرية محصنة:', en: 'Practical Example — Breaking a Fortified Village:' })}</strong>
                <p>${pick({
                    ar: '1) أرسل كشافة أولاً لمعرفة عدد المدافعين ومستوى السور — 2) جهّز جيشاً يضم كباشاً لكسر البوابة ومقاليع لخفض حماية السور — 3) إذا كان هدفك احتلال القرية وليس نهبها فقط، لا تنسَ ضم "حاكم" مع الجيش.',
                    en: '1) Send Scouts first to reveal defenders — 2) Include Rams to break the gate and Catapults to drop Wall integrity — 3) Bring a Governor if your goal is Conquest rather than a quick Raid.'
                })}</p>
            </div>`;
    }

    private tabOases(): string {
        return `
            <div class="guide-steps">
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">1</span><strong>${pick({ ar: 'واحة الخشب', en: 'Wood Oasis' })}</strong></div>
                    <div class="guide-step-visual">${oasisArt('Wood')}</div>
                    <p>${pick({ ar: 'ترفع إنتاج الخشب في القرية المرتبطة وتجمع موارد قابلة للنقل التلقائي.', en: 'Boosts Wood output in its linked village and collects harvestable timber.' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">2</span><strong>${pick({ ar: 'واحة الحديد', en: 'Iron Oasis' })}</strong></div>
                    <div class="guide-step-visual">${oasisArt('Iron')}</div>
                    <p>${pick({ ar: 'ممتازة لتجهيز الجيوش المدرعة والفرسان والحداد.', en: 'Ideal for funding armored units, cavalry, and Smithy upgrades.' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">3</span><strong>${pick({ ar: 'واحة الحجر', en: 'Stone Oasis' })}</strong></div>
                    <div class="guide-step-visual">${oasisArt('Stone')}</div>
                    <p>${pick({ ar: 'تسرّع بناء الأسوار والمباني الأساسية وتحصين واحاتك.', en: 'Fuels Wall construction, core village upgrades, and oasis walls.' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">4</span><strong>${pick({ ar: 'واحة القمح', en: 'Wheat Oasis' })}</strong></div>
                    <div class="guide-step-visual">${oasisArt('Wheat')}</div>
                    <p>${pick({ ar: 'تحل أزمة الجوع عند امتلاك جيوش ضخمة وتُبقي تموينها متصلاً.', en: 'Vital for large armies so wheat income covers troop upkeep.' })}</p>
                </article>
            </div>

            <div class="guide-split-preview">
                <div class="guide-mock-box">
                    <strong>${pick({ ar: 'وحداتك داخل الواحة + الدمج التلقائي', en: 'Your Oasis Garrison & Auto-Merge' })}</strong>
                    <p>${pick({
                        ar: 'أي جيشين لك يدخلان نفس الواحة يندمجان تلقائياً في جيش واحد. تظهر كل وحدة بشكل منفصل لتختار منها بـ Shift وترسلها كجيش واحد بالرايت كليك.',
                        en: 'Your armies entering the same oasis merge automatically. Each unit type displays separately so you can Shift-select them and dispatch together.'
                    })}</p>
                </div>
                <div class="guide-mock-box green">
                    <strong>${pick({ ar: 'تعزيزات الحلفاء (الصف الأخضر)', en: 'Allied Reinforcements (Green Row)' })}</strong>
                    <p>${pick({
                        ar: 'عندما يرسل لاعب آخر تعزيزاً لحماية واحتك، تظهر وحداته بوضوح في صف ثانٍ بخلفية خضراء خفيفة للمعرفة فقط ولا يمكنك تحريكها.',
                        en: 'Allied troops reinforcing your oasis appear clearly in a second light-green row with their owner name (view-only for coordination).'
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
                        ar: 'تبدأ كل مباراة بوقت محدد (الافتراضي 60 دقيقة) وفترة حماية أولية للبناء، ثم تفتح قلعة التاج في منتصف الخريطة (الافتراضي عند الدقيقة 45 أو حسب إعدادات الغرفة).',
                        en: 'Matches run for a configured duration (default 60m) starting with a peace shield, then Crown Keep unlocks in the center (default 45m, configurable per room).'
                    })}</p>
                </div>
                <div class="guide-hero-art">${icon('i-shield')}</div>
            </section>

            <div class="guide-steps">
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">1</span><strong>${pick({ ar: 'فترة الحماية', en: 'Peace Shield' })}</strong></div>
                    <p>${pick({ ar: 'لا يمكن للاعبين مهاجمة قراك أو واحاتك في الدقائق الأولى — استغلها في التطوير واحتلال الواحات القريبة.', en: 'Enemy attacks are blocked early on — focus on buildings, training, and nearby oases.' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">2</span><strong>${pick({ ar: 'توسيع المملكة', en: 'Kingdom Growth' })}</strong></div>
                    <p>${pick({ ar: 'طوّر الحداد والسور والمستشفى وابنِ جيشاً متوازناً، أو تعاون مع أصدقائك لتعزيز الواحات والقرى الحدودية.', en: 'Level Smithy, Wall, and Hospital, and coordinate reinforcements on key borders.' })}</p>
                </article>
                <article class="guide-step-card">
                    <div class="guide-step-head"><span class="guide-step-badge">3</span><strong>${pick({ ar: 'ظهور قلعة التاج', en: 'Crown Keep Opens' })}</strong></div>
                    <p>${pick({ ar: 'فور فتح قلعة التاج في المنتصف، تصبح هي ساحة القتال الرئيسية. السيطرة المتواصلة على القلعة تحسم اللقب وتمنح أعلى نقاط.', en: 'Once Crown Keep opens, holding it earns decisive victory progress and Crown points.' })}</p>
                </article>
            </div>

            <div class="guide-example">
                <strong>${pick({ ar: 'نصيحة ختامية:', en: 'Pro Tip:' })}</strong>
                <p>${pick({
                    ar: 'لا تضع كل جيشك في هجوم واحد بدون استطلاع أو قمح كافٍ! احتفظ دائماً بجزء للحراسة أو واحة قريبة، واستفد من المستشفى والدمج التلقائي لإعادة تنظيم قواتك بسرعة.',
                    en: 'Never march your entire army blind or without wheat! Keep a reserve garrison, scout ahead, and use Hospital recovery plus oasis auto-merge to regroup fast.'
                })}</p>
            </div>`;
    }
}
