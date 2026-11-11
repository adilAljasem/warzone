import arabic from './locales/ar.json';
let language = 'ar';
try { language = localStorage.getItem('warzone.language') ?? 'ar'; } catch { /* Storage is optional. */ }
export const currentLanguage = () => language;
const phrases = arabic.phrases as Record<string,string>;
const patterns = arabic.patterns.map(([source, replacement]) => [new RegExp(source!, 'gi'), replacement!] as const);
const words = Object.keys(phrases).filter(k=>k.length>2).sort((a,b)=>b.length-a.length);
const expression = new RegExp('\\b(' + words.map(k=>k.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')).join('|') + ')\\b', 'gi');
const insensitive = new Map(Object.entries(phrases).map(([k,v])=>[k.toLowerCase(),v]));
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
new MutationObserver(()=>{if(!pending){pending=true;queueMicrotask(()=>{pending=false;localizeNow();});}}).observe(document.body,{childList:true,subtree:true,characterData:true});
localizeNow();
