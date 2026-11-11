import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import ts from '../frontend/node_modules/typescript/lib/typescript.js';

const source=await readFile(new URL('../frontend/src/army-visual.ts',import.meta.url),'utf8');
const js=ts.transpileModule(source,{compilerOptions:{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.ES2022}}).outputText;
const {armyFigureCount,armyFigures,armyFigureLayout,isDemolishing,isSmoking}=await import(`data:text/javascript;base64,${Buffer.from(js).toString('base64')}`);

assert.deepEqual([0,1,4,9,10,11,20,25,100,400,1024,100000].map(armyFigureCount),[0,1,1,1,1,2,2,3,10,40,103,10000]);
for(let count=1;count<=2000;count++){
    assert(armyFigureCount(count)>=armyFigureCount(count-1));
    assert(armyFigureCount(count)<=count);
    const layout=armyFigureLayout(armyFigureCount(count));
    assert.equal(new Set(layout.points.map(p=>`${p.x},${p.y}`)).size,armyFigureCount(count));
    assert(layout.points.every(p=>Number.isFinite(p.x)&&Number.isFinite(p.y)));
    assert(layout.rows<=layout.columns,'Large armies stay in a balanced formation instead of a long column');
}
const mixed=armyFigures(100,'Romans',{'romans-infantry':80,'romans-archer':20,'romans-wagon':4,'romans-cavalry':0});
assert.equal(mixed.length,10);assert.equal(mixed.filter(id=>id==='romans-infantry').length,8);assert.equal(mixed.filter(id=>id==='romans-archer').length,2);
const combined=armyFigures(151,'Romans',{'romans-infantry':100,'romans-cavalry':20,'romans-catapult':10,'romans-ram':11,'romans-siegetower':10,'romans-wagon':30});
assert.deepEqual(Object.fromEntries([...new Set(combined)].map(id=>[id,combined.filter(x=>x===id).length])),{'romans-catapult':1,'romans-cavalry':2,'romans-infantry':10,'romans-ram':2,'romans-siegetower':1});
assert.deepEqual(armyFigures(1,'Germans',{'germans-heavycavalry':1}),['germans-heavycavalry'],'Small specialist groups remain visible');
assert.deepEqual(armyFigures(31,'Romans',null,{'romans-cavalry':3,'romans-catapult':1,'romans-wagon':2}),['romans-catapult','romans-cavalry','romans-cavalry','romans-cavalry'],'Public map figures preserve enemy roles without exposing exact formation');
assert(armyFigures(100,'Germans',null).every(id=>id==='germans-infantry'),'Private formations reveal only generic tribe infantry');
assert.deepEqual(armyFigures(0,'Romans',{'romans-wagon':20}),[]);
assert(armyFigures(25,'Romans',{'romans-infantry':25}).length<armyFigures(100,'Romans',{'romans-infantry':100}).length,'Casualties shrink the representative formation');
assert(isDemolishing({status:'Active',objective:'Raze',phase:'Demolishing'}));
for(const state of [{status:'Ended',objective:'Raze',phase:'Demolishing'},{status:'Active',objective:'Raze',phase:'Fighting'},{status:'Active',objective:'Conquest',phase:'Persuading'},{status:'Active',objective:'Blockade',phase:'Blockading'}])assert(!isDemolishing(state),'Only ongoing demolition can burn a village');
assert(isSmoking({ruined:false,smokeSeconds:62}));
assert(!isSmoking({ruined:true,smokeSeconds:62}));
assert(!isSmoking({ruined:false,smokeSeconds:0}));
console.log('Passed map visual rules: one figure per ten of each role, partial groups, uncapped balanced layouts, casualties, public figures and demolition/smoke lifecycle.');
