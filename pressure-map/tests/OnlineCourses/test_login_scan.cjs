const assert=require('node:assert/strict');
const {scanAfterSuccessfulLogin}=require('../../src/PressureMap.App/OnlineCourses/reader.cjs');
(async()=>{
 for(const status of ['invalid_credentials','captcha','cancelled','error','not_logged_in']) {
  await scanAfterSuccessfulLogin({status},{cancelled:()=>false,saveSession:()=>assert.fail('failed login saved'),scan:()=>assert.fail('failed login scanned')});
 }
 const calls=[];
 await scanAfterSuccessfulLogin({status:'success'},{cancelled:()=>false,saveSession:async()=>calls.push('save'),progress:()=>calls.push('progress'),scan:async()=>calls.push('scan')});
 assert.deepEqual(calls,['save','progress','scan']);
 await scanAfterSuccessfulLogin({status:'success'},{cancelled:()=>true,saveSession:()=>assert.fail('cancelled login saved'),scan:()=>assert.fail('cancelled login scanned')});
 let cancelled=false;
 await scanAfterSuccessfulLogin({status:'success'},{cancelled:()=>cancelled,saveSession:async()=>{cancelled=true;},scan:()=>assert.fail('stop after login scanned')});
 console.log('Successful login scans once after saving session; failures and cancellation skip scanning');
})().catch(error=>{console.error(error);process.exitCode=1;});
