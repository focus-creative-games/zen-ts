// Shared acceptance gate: compare executed named cases with the current sources.
import fs from 'node:fs';
import path from 'node:path';
export function caseInventory(project) {
  const root=path.join(project,'Assets/StreamingAssets/Tests/Js/cases');
  const suites=new Map();
  function visit(dir) {
    for(const entry of fs.readdirSync(dir,{withFileTypes:true})) {
      const file=path.join(dir,entry.name);
      if(entry.isDirectory()){visit(file);continue;}
      if(!entry.name.endsWith('.js'))continue;
      const source=fs.readFileSync(file,'utf8').replace(/\/\*[\s\S]*?\*\//g,'');
      const id=/defineSuite\(\s*['"]([^'"]+)/.exec(source)?.[1];
      if(!id)continue; // C#-driven export support modules are not named JS suites.
      if(suites.has(id))throw Error('Duplicate suite ID '+id);
      const names=[...source.matchAll(/^\s*(test_\w+)\s*\(/gm)].map(m=>m[1]);
      if(!names.length||new Set(names).size!==names.length)throw Error('Empty/duplicate case names '+file);
      suites.set(id,names.length);
    }
  }
  visit(root);return suites;
}
export function verifyCaseSummary(lines,project,{host=true}={}) {
  const found=lines.filter(x=>x.includes('[ZenTS][case-summary] '));
  if(found.length!==1)throw Error('Expected one named-case summary, got '+found.length);
  const report=JSON.parse(found[0].split('[ZenTS][case-summary] ')[1].split(/\r?\n/)[0].trim());
  const expected=caseInventory(project),actual=new Map(report.suites.map(s=>[s.id,s]));
  if(actual.size!==expected.size||actual.size!==report.suites.length)throw Error('Executed suite inventory mismatch');
  for(const [id,count] of expected) {
    const s=actual.get(id);
    if(!s||s.total!==count||s.passed+s.failed+s.skipped!==count||s.failed)throw Error('Incomplete/failed cases '+id);
    if(s.skips.length!==s.skipped)throw Error('Missing skip reasons '+id);
    for(const skip of s.skips)if(!host||id!=='p3.alignment'||skip.name!=='test_main_thread_contract'||!skip.reason)throw Error('Unexpected skip '+JSON.stringify(skip));
  }
  for(const key of ['total','passed','failed','skipped'])if(report[key]!==report.suites.reduce((n,s)=>n+s[key],0))throw Error('Incorrect '+key+' total');
  return {total:report.total,passed:report.passed,failed:report.failed,skipped:report.skipped};
}
