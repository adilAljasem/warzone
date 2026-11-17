import arabic from './locales/ar.json';
const detectInitialLanguage = (): 'ar' | 'en' => {
    try {
        const params = new URLSearchParams(window.location.search);
        const paramLang = params.get('lang')?.toLowerCase();
        if (paramLang === 'ar' || paramLang === 'en') {
            try { localStorage.setItem('warzone.language', paramLang); } catch { /* Storage is optional. */ }
            return paramLang;
        }
        const path = window.location.pathname.toLowerCase();
        if (path === '/en' || path.startsWith('/en/')) return 'en';
        if (path === '/ar' || path.startsWith('/ar/')) return 'ar';
    } catch { /* Ignore URL parsing errors. */ }
    try {
        const stored = localStorage.getItem('warzone.language');
        if (stored === 'en' || stored === 'ar') return stored;
    } catch { /* Storage is optional. */ }
    try {
        const nav = (navigator.language || (navigator.languages && navigator.languages[0]) || '').toLowerCase();
        if (nav.startsWith('ar')) return 'ar';
        if (nav) return 'en';
    } catch { /* Ignore navigator errors. */ }
    return 'en';
};
let language: 'ar' | 'en' = detectInitialLanguage();
export const currentLanguage = () => language;
const phrases = arabic.phrases as Record<string,string>;
const patterns = arabic.patterns.map(([source, replacement]) => [new RegExp(source!, 'gi'), replacement!] as const);
const words = Object.keys(phrases).filter(k=>k.length>2).sort((a,b)=>b.length-a.length);
const expression = new RegExp('\\b(' + words.map(k=>k.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')).join('|') + ')\\b', 'gi');
const insensitive = new Map<string, string>();
for (const [k, v] of Object.entries(phrases)) {
    const low = k.toLowerCase();
    if (!insensitive.has(low) || k === low) insensitive.set(low, v);
}
export function t(text: string): string {
    if(language!=='ar'||!/[a-z]/i.test(text))return text;
    const trim=text.trim(), direct=phrases[trim]??insensitive.get(trim.toLowerCase());
    if(direct)return text.replace(trim,direct);
    let translated=text;
    for(const [pattern,replacement] of patterns)translated=translated.replace(pattern,replacement);
    return translated.replace(expression,word=>insensitive.get(word.toLowerCase())??word);
}
const originals = new WeakMap<Node,{original:string;rendered:string}>();
const attributes = new WeakMap<Element,Map<string,{original:string;rendered:string}>>();
export function localizeNow(root: Node = document.body) {
    const walk=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);
    let node:Node|null;
    while((node=walk.nextNode())){
        if(node.parentElement?.closest('script,style,canvas,[data-no-translate]'))continue;
        const value=node.nodeValue??'', previous=originals.get(node),original=previous?.rendered===value?previous.original:value;
        const option=node.parentElement;if(option?.tagName==='OPTION'&&!option.hasAttribute('value'))option.setAttribute('value',original);
        const rendered=t(original);originals.set(node,{original,rendered});if(value!==rendered)node.nodeValue=rendered;
    }
    if(root instanceof Element)for(const element of [root,...Array.from(root.querySelectorAll('[title],[aria-label],[placeholder]'))]){
        if(element.closest('[data-no-translate]'))continue;
        const cache=attributes.get(element)??new Map();attributes.set(element,cache);
        for(const name of ['title','aria-label','placeholder']){const value=element.getAttribute(name);if(value===null)continue;const prior=cache.get(name),original=prior?.rendered===value?prior.original:value,rendered=t(original);cache.set(name,{original,rendered});if(value!==rendered)element.setAttribute(name,rendered);}
    }
}
export function setLanguage(next:string){language=next==='ar'?'ar':'en';try{localStorage.setItem('warzone.language',language);}catch{/* Keep the current session preference. */}document.documentElement.lang=language;document.documentElement.dir=language==='ar'?'rtl':'ltr';localizeNow();window.dispatchEvent(new Event('languagechange'));}
document.documentElement.lang=language;document.documentElement.dir=language==='ar'?'rtl':'ltr';
let pending=false;
new MutationObserver(records=>{
    const relevant=records.some(r=>{
        const el=r.target instanceof Element?r.target:r.target.parentElement;
        return el&&!el.closest('[data-no-translate]');
    });
    if(!relevant)return;
    if(!pending){pending=true;queueMicrotask(()=>{pending=false;localizeNow();});}
}).observe(document.body,{childList:true,subtree:true,characterData:true});
localizeNow();
