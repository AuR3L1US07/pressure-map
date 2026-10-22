const assert=require('node:assert/strict');
const {boundedLogin}=require('../../src/PressureMap.App/OnlineCourses/reader.cjs');
(async()=>{
 assert.equal((await boundedLogin(()=>new Promise(()=>{}),20)).status,'timeout');
 assert.equal((await boundedLogin(async()=>({status:'invalid_credentials'}),100)).status,'invalid_credentials');
 assert.equal((await boundedLogin(async()=>({status:'success'}),100)).status,'success');
 console.log('Background login timeout and credential failure return without waiting for manual verification');
})().catch(e=>{console.error(e);process.exitCode=1;});
