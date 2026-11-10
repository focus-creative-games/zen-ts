// Exercise the test infrastructure without Unity; failure must not hide later cases.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
const project = process.argv[2];
if (!project) throw Error('Usage: node test-suite-runner.mjs <Unity project>');
const source = fs.readFileSync(path.join(project,'Assets/StreamingAssets/Tests/Js/jstest/suite.js'),'utf8');
const load = suffix => import('data:text/javascript;base64,'+Buffer.from(source+'\n// '+suffix).toString('base64'));
const suite = await load('first');
const saved = {info:console.info,error:console.error};
try {
  console.info=()=>{};console.error=()=>{};
  const ran=[];
  const run=suite.defineSuite('harness',{test_a(){ran.push('a');throw Error('one');},test_b(){ran.push('b');},test_c(){suite.skip('platform');},test_d(){throw undefined;}});
  assert.throws(run,/one[\s\S]*undefined/);
  assert.deepEqual(ran,['a','b']);
  let report=JSON.parse(suite.report());
  assert.equal(report.total,4);assert.equal(report.passed,1);assert.equal(report.failed,2);assert.equal(report.skipped,1);
  assert.throws(suite.defineSuite('empty',{}),/empty/);
  // Import aliases must see the same per-environment report and skip marker.
  const alias=await load('alias');
  alias.defineSuite('alias',{test_skip(){suite.skip('alias marker');}})();
  assert.equal(JSON.parse(suite.report()).skipped,2);
  suite.defineSuite('harness',{test_replacement(){}})();
  report=JSON.parse(alias.report());assert.equal(report.total,2);assert.equal(report.failed,0);
} finally {Object.assign(console,saved);delete globalThis.__zentsTestReports;}
console.log('Suite infrastructure PASS: continue after failure, throw undefined, explicit skip, empty-suite rejection, alias-safe counts, rerun replacement.');

// Acceptance must reject false-green logs, including unchanged module markers.
const {caseInventory,verifyCaseSummary}=await import('./test-inventory.mjs');
const inventory=caseInventory(project);
const suites=[...inventory].map(([id,total])=>({id,total,passed:total,failed:0,skipped:0,failures:[],skips:[]}));
const total=suites.reduce((n,s)=>n+s.total,0);
const makeReport=()=>({suites:JSON.parse(JSON.stringify(suites)),total,passed:total,failed:0,skipped:0});
const lines=r=>['[ZenTS][case-summary] '+JSON.stringify(r)+'\nUnityEngine.Debug:Log(Object)'];
assert.equal(verifyCaseSummary(lines(makeReport()),project).total,total);
const missing=makeReport();missing.suites.pop();assert.throws(()=>verifyCaseSummary(lines(missing),project),/inventory/);
const fewer=makeReport();fewer.suites[0].total--;assert.throws(()=>verifyCaseSummary(lines(fewer),project),/cases/);
const failed=makeReport();failed.suites[0].failed++;failed.suites[0].passed--;assert.throws(()=>verifyCaseSummary(lines(failed),project),/cases/);
const skippedReport=makeReport();const thread=skippedReport.suites.find(s=>s.id==='p3.alignment');
thread.passed--;thread.skipped++;thread.skips.push({name:'test_main_thread_contract',reason:'Host VM single-thread build has no managed Thread'});skippedReport.passed--;skippedReport.skipped++;
assert.equal(verifyCaseSummary(lines(skippedReport),project).skipped,1);
assert.throws(()=>verifyCaseSummary(lines(skippedReport),project,{host:false}),/skip/);
thread.skips[0].name='test_exact_bigint_limits';assert.throws(()=>verifyCaseSummary(lines(skippedReport),project),/skip/);
assert.throws(()=>verifyCaseSummary([],project),/summary/);
console.log('Acceptance inventory PASS: missing/failed cases, unauthorized skips, absent summary, Unity multiline logging.');
