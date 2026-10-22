const assert=require('node:assert/strict');
const {spawnSync}=require('node:child_process');
const path=require('node:path');
const fs=require('node:fs');
const assets=path.resolve(__dirname,'../../src/PressureMap.App/OnlineCourses');
const {chromium}=require(path.join(assets,'.runtime/playwright-core'));
for(const action of ['brush','visits','open','quiz']) {
 const run=spawnSync(process.execPath,[path.join(assets,'reader.cjs'),'chaoxing',action],{encoding:'utf8',timeout:10000});
 assert.equal(run.status,1);assert.equal(JSON.parse(run.stdout).type,'error');
}
(async()=>{
 let executablePath;
 for(const relative of ['Google/Chrome/Application/chrome.exe','Microsoft/Edge/Application/msedge.exe'])for(const key of ['PROGRAMFILES','PROGRAMFILES(X86)','LOCALAPPDATA']) {
  const candidate=process.env[key]&&path.join(process.env[key],relative);
  if(!executablePath&&candidate&&fs.existsSync(candidate))executablePath=candidate;
 }
 assert.ok(executablePath,'Browser required for runtime check');
 const browser=await chromium.launch({executablePath,headless:true});
 try {
  const context=await browser.newContext();const page=await context.newPage();
  await page.goto('about:blank');await page.setContent('<h1>课程助手运行时检查</h1>');
  assert.equal(await page.locator('h1').innerText(),'课程助手运行时检查');
  const state=await context.storageState();assert.deepEqual(state,{cookies:[],origins:[]});
  await context.close();
 }finally{await browser.close();}
 console.log('Packaged browser runtime and unsupported-action rejection passed');
})().catch(error=>{console.error(error);process.exitCode=1;});
