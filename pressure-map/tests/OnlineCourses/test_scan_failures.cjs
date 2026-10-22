const assert=require('node:assert/strict');
const {failureReason,scanOutcome}=require('../../src/PressureMap.App/OnlineCourses/reader.cjs');
assert.match(failureReason(Error('net::ERR_NAME_NOT_RESOLVED')),/网络/);
assert.match(failureReason(Error('page.goto: Timeout 30000ms exceeded')),/超时/);
assert.match(failureReason(Error('未找到 Chrome 或 Microsoft Edge')),/安装/);
assert.equal(scanOutcome('检查结束：发现 3 门，成功读取 2 门。').ok,true);
for(const message of ['尚未登录，请重新登录。','未识别到课程，原有记录未删除。','课程分页未加载完成'])assert.equal(scanOutcome(message).ok,false);
assert.ok(!failureReason(Error('unknown https://private.example/?token=secret')).includes('secret'));
console.log('Failure reasons distinguish network, timeout, missing browser and unsuccessful scan results');
