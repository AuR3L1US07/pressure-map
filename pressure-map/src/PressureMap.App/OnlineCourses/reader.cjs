// Ported read-only course inspection and login only. No playback or submission actions.
const readline = require('node:readline');
const path = require('node:path');
const fs = require('node:fs');
const {chromium} = require('./.runtime/playwright-core');
const {readStudyRecords} = require('./study_records.cjs');
const {readOtherCategories} = require('./category_reader.cjs');
const {loginMooc,authenticated:moocAuthenticated} = require('./mooc_login.cjs');
const {scanMooc} = require('./mooc_scanner.cjs');
const emit = (type,fields) => process.stdout.write(JSON.stringify({type,...fields})+'\n');
let stopped=false,credentials=null;
const taskPages=new Set();
function trackPage(page){taskPages.add(page);page.on('close',()=>taskPages.delete(page));}
async function taskPage(context){const page=await context.newPage();trackPage(page);return page;}
function stopTask(){stopped=true;for(const page of taskPages)page.close().catch(()=>{});}
const allowed = frame => {try{const u=new URL(frame.url());return u.protocol==='https:'&&u.hostname.endsWith('.chaoxing.com');}catch{return false;}};
const delay=ms=>new Promise(r=>setTimeout(r,ms));
function browserExecutable(){
  for(const relative of ['Google/Chrome/Application/chrome.exe','Microsoft/Edge/Application/msedge.exe']){
    for(const key of ['PROGRAMFILES','PROGRAMFILES(X86)','LOCALAPPDATA']){
      const base=process.env[key];
      if(!base)continue;
      const candidate=path.join(base,...relative.split('/'));
      try{if(fs.statSync(candidate).isFile())return candidate;}catch{}
    }
  }
  throw new Error('未找到 Chrome 或 Microsoft Edge，请先安装其中一种浏览器。');
}

function parseProgress(text){
  const match=text.match(/已完成任务点\s*[:：]?\s*(\d+)\s*\/\s*(\d+)/);
  return match && +match[1]<=+match[2] ? [+match[1],+match[2]] : null;
}

async function links(page) {
  const found=new Map();
  for(const frame of page.frames().filter(allowed)) {
    try {
      const entries=await frame.evaluate(()=>[...document.querySelectorAll('li.course h3 a[href], a[href*="stucoursemiddle"]')].map(el=>{
        const card=el.closest('li.course');
        const name=(card?.querySelector('.course-name')?.getAttribute('title') || el.innerText || '').trim();
        return {name,url:el.href};
      }));
      for(const entry of entries){
        const u=new URL(entry.url), q=u.searchParams;
        if(entry.name && u.protocol==='https:' && u.hostname.endsWith('.chaoxing.com') && /^\d+$/.test(q.get('courseid')) && /^\d+$/.test(q.get('clazzid')))
          found.set(`${q.get('courseid')}:${q.get('clazzid')}:${q.get('cpi')||''}`,entry);
      }
    }catch{}
  }
  return found;
}

async function openLearningCourses(page){
  // The learning-space landing page only loads its course iframe after selecting
  // the sidebar link. Follow the site's navigation so its SSO URL is preserved.
  for(const frame of page.frames().filter(allowed)){
    try{
      const navigation=frame.locator('a[href*="switchM"][href*="/visit/interaction"]');
      for(let i=0;i<await navigation.count();i++){
        const link=navigation.nth(i);
        if(await link.isVisible()){
          await link.click({timeout:3000});
          return true;
        }
      }
    }catch{}
  }
  return false;
}

async function needsLogin(page){
  if(new URL(page.url()).hostname.includes('passport')) return true;
  for(const frame of page.frames()){
    try{if(await frame.locator('input[type="password"]:visible').count())return true;}catch{}
  }
  return false;
}

async function autoLogin(context, cred, existingPage=null){
  const page=existingPage||await taskPage(context);
  let keepPage=false;
  try{
    await page.goto('https://passport2.chaoxing.com/login',{waitUntil:'domcontentloaded',timeout:45000});
    if(allowed(page)&&!await needsLogin(page))return {ok:true,status:'success'};
    if(new URL(page.url()).hostname!=='passport2.chaoxing.com')return {ok:false,status:'error',message:'登录地址发生变化，请手动登录。'};
    keepPage=true;
    const phone=page.locator('#phone');
    await phone.waitFor({state:'visible',timeout:10000});
    await phone.fill(cred.username);
    await page.locator('#pwd').fill(cred.password);
    await enableRememberLogin(page);
    await page.locator('#loginBtn').click();
    for(let i=0;i<40&&!stopped;i++){
      await delay(500);
      if(allowed(page) && !await needsLogin(page)) {keepPage=false;return {ok:true,status:'success'};}
      try{
        const err=await page.locator('#err_txt:visible, .err-txt:visible, .wrong-text:visible, .zc-alert:visible').first().innerText({timeout:300});
        if(err&&err.trim()){
          const status=classifyLoginError(err);
          return {ok:false,status,message:status==='invalid_credentials'?'账号或密码错误，请重新登录。':status==='captcha'?'需要验证码，请在网页完成验证。':'登录失败，请稍后重试。'};
        }
      }catch{}
      if(await hasCaptcha(page))return {ok:false,status:'captcha',message:'需要验证码，请在网页完成验证。'};
    }
    return {ok:false,status:stopped?'cancelled':'error',message:'登录未完成，请重试或打开网页登录。'};
  }catch{
    return {ok:false,status:stopped?'cancelled':'error',message:'登录页面打开失败，请重试。'};
  }finally{
    if(!existingPage&&(!keepPage||stopped))await page.close().catch(()=>{});
  }
}

function classifyLoginError(text){
  if(/验证码|验证失败|滑块|安全验证|短信验证/.test(text))return 'captcha';
  if(/密码.{0,8}(错误|不正确)|账号.{0,8}(错误|不存在)|用户名.{0,8}(错误|不存在)|帐号.{0,8}(错误|不存在)/.test(text))return 'invalid_credentials';
  return 'error';
}

async function hasCaptcha(page){
  for(const selector of ['#captcha', '#verifyCode', '.yidun_panel', '.geetest_panel', '.nc-container', '#captchaIframe', 'iframe[src*="captcha"]', 'iframe[src*="verify"]']){
    try{if(await page.locator(selector).first().isVisible())return true;}catch{}
  }
  return false;
}

async function loginAccount(context, cred, {checkOnly=false, visible=false}={}){
  if(checkOnly){
    const page=await taskPage(context);
    try{
      await page.goto('https://i.chaoxing.com/',{waitUntil:'domcontentloaded',timeout:45000});
      return {status:allowed(page)&&!await needsLogin(page)?'success':'not_logged_in'};
    }finally{await page.close().catch(()=>{});}
  }
  if(!cred)return {status:'error'};
  // Explicit account verification must not pass using a previous account's cookies.
  await context.clearCookies({domain:/(^|\.)chaoxing\.com$/});
  const existing=visible?context.pages().find(p=>p.url().startsWith('https://passport2.chaoxing.com/')):null;
  const result=await autoLogin(context,cred,existing||null);
  return {status:result.status||(result.ok?'success':'error')};
}

async function enableRememberLogin(page){
  const custom=page.locator('.auto-lg-next .check-input').first();
  if(await custom.count()){
    const isChecked=async()=>/(?:^|\s)checked-input(?:\s|$)/.test(await custom.getAttribute('class')||'');
    if(!await isChecked())await custom.click({timeout:3000});
    if(!await isChecked())throw new Error('自动登录选项未勾选成功');
    return;
  }
  // Prefer setting the checkbox state so an already checked option stays on.
  const checkbox=page.getByRole('checkbox',{name:/下次自动登录/}).first();
  if(await checkbox.count()){
    await checkbox.check({force:true,timeout:3000});
    return;
  }
  const label=page.getByText('下次自动登录').first();
  if(!await label.count())return;
  const nearby=label.locator('xpath=..').locator('input[type="checkbox"]').first();
  if(await nearby.count())await nearby.check({force:true,timeout:3000});
  else{
    // Custom controls may expose their state on the label or its parent.
    const checked=await label.evaluate(el=>{
      const nodes=[el,el.parentElement].filter(Boolean);
      return nodes.some(n=>n.getAttribute('aria-checked')==='true'||
        /(?:^|\s)(?:checked|selected|active)(?:\s|$)/.test(n.className));
    });
    if(!checked)await label.click({timeout:3000});
  }
}

async function counts(page){
  for(const frame of page.frames().filter(allowed)){
    try{
      const text=await frame.locator('body').innerText({timeout:1500});
      const result=parseProgress(text);
      if(result) return result;
    }catch{}
  }
  return null;
}

async function scan(context){
  const space=await taskPage(context);
  let keepSpace=false;
  try{
    emit('progress',{message:'正在读取超星学习空间…'});
    await space.goto('https://i.mooc.chaoxing.com/space/',{waitUntil:'domcontentloaded',timeout:45000});
    let found=new Map(), loginTried=false, courseNavigationOpened=false;
    for(let i=0;i<30&&!stopped;i++){
      for(const [key,value] of await links(space))found.set(key,value);
      if(found.size)break;
      if(await needsLogin(space)){
        keepSpace=true;
        if(credentials&&!loginTried){
          loginTried=true;
          emit('progress',{message:'检测到未登录，正在使用已保存的账号自动登录…'});
          const result=await autoLogin(context,credentials,space);
          if(result.ok){
            keepSpace=false;
            emit('progress',{message:'自动登录成功，正在读取课程…'});
            await space.goto('https://i.mooc.chaoxing.com/space/',{waitUntil:'domcontentloaded',timeout:45000}).catch(()=>{});
            continue;
          }
          return result.message;
        }
        await space.bringToFront();
        return '后台未登录。请到“平台账号 → 打开网页登录”登录超星，或保存账号后重新检查。';
      }
      if(!courseNavigationOpened && await openLearningCourses(space)){
        courseNavigationOpened=true;
        emit('progress',{message:'已进入“我学的课”，正在读取课程列表…'});
      }
      await delay(500);
    }
    if(stopped)return '已停止检查。';
    if(!found.size){keepSpace=true;return '未识别到课程，请确认学习空间已显示“我学的课”。原有记录未删除。';}
    let partial=false;
    for(const frame of space.frames().filter(allowed)){
      const all=frame.locator('a[onclick="getAllCourse()"]');
      if(await all.count() && await all.first().isVisible()){
        await all.first().click({timeout:3000}); await delay(400);
      }
    }
    for(let pageNumber=0;pageNumber<50&&!stopped;pageNumber++){
      let stable=0;
      for(let step=0;step<25&&!stopped;step++){
        const before=found.size;
        for(const frame of space.frames().filter(allowed)){
          try{await frame.evaluate(()=>{
            window.scrollTo(0,document.body.scrollHeight);
            for(const el of document.querySelectorAll('div'))
              if(el.scrollHeight>el.clientHeight+50 && ['auto','scroll'].includes(getComputedStyle(el).overflowY)) el.scrollTop=el.scrollHeight;
          });}catch{}
        }
        await delay(250);
        for(const [key,value] of await links(space))found.set(key,value);
        stable=found.size===before?stable+1:0;
        if(stable>=3)break;
        if(step===24)partial=true;
      }
      let next=null;
      for(const frame of space.frames().filter(allowed)){
        const candidates=frame.getByText('下一页',{exact:true});
        for(let i=0;i<await candidates.count();i++){
          const el=candidates.nth(i);
          if(await el.isVisible() && await el.isEnabled() && !/disable/i.test(await el.getAttribute('class')||''))next=el;
        }
        if(await frame.locator('li.folder:visible, li.file:visible, [folderid]:visible').count())partial=true;
      }
      if(!next)break;
      const before=found.size;
      try{await next.click({timeout:3000});await delay(400);}catch{partial=true;break;}
      for(const [key,value] of await links(space))found.set(key,value);
      if(before===found.size){partial=true;break;}
      if(pageNumber===49)partial=true;
    }
    const courses=[...found.values()];
    emit('catalog',{courses});
    let successful=0, done=0;
    // 单门课程内部：任务点 / 学习次数 / 任务·作业·讨论 三路并行读取。
    async function checkOne(item, i){
      const idx=i+1, total=courses.length;
      const stage=name=>emit('progress',{message:`检查 ${idx}/${total}：${item.name}（${name}）· 已完成 ${done}`});
      const detail=await taskPage(context);
      const visitsPage=await taskPage(context);
      const catsPage=await taskPage(context);
      let result=null, error='未读取到任务点总数，请打开课程确认页面。';
      const readCounts=(async()=>{
        try{
          await detail.goto(item.url,{waitUntil:'domcontentloaded',timeout:30000});
          stage('任务点');
          const chapter=detail.getByText('章节',{exact:true});
          await chapter.first().waitFor({state:'visible',timeout:10000});
          await chapter.first().click({timeout:3000});
          for(let attempt=0;attempt<40&&!stopped;attempt++){
            if(await needsLogin(detail)){error='登录已失效，请重新登录。';break;}
            const r=await counts(detail);
            if(r){result=r;break;}
            await delay(150);
          }
        }catch{error='页面加载失败或章节入口不可用，请打开课程检查。';}
      })();
      const readVisits=(async()=>{
        stage('学习次数');
        // 先进入课程内页（点「章节」），「学习记录」菜单才在内页。
        await visitsPage.goto(item.url,{waitUntil:'domcontentloaded',timeout:30000}).catch(()=>{});
        const chapter=visitsPage.getByText('章节',{exact:true}).first();
        await chapter.waitFor({state:'visible',timeout:10000}).catch(()=>{});
        await chapter.click({timeout:3000}).catch(()=>{});
        return readStudyRecords(visitsPage,trackPage,()=>stopped);
      })();
      const readCats=(async()=>{
        stage('任务·作业·讨论');
        // 先进入课程内页（点「章节」），任务/作业/讨论菜单才在内页。
        await catsPage.goto(item.url,{waitUntil:'domcontentloaded',timeout:30000}).catch(()=>{});
        const chapter=catsPage.getByText('章节',{exact:true}).first();
        await chapter.waitFor({state:'visible',timeout:10000}).catch(()=>{});
        await chapter.click({timeout:3000}).catch(()=>{});
        // Load this page while study records are being read; only the final
        // category calculation depends on the discussion requirement there.
        const v=await readVisits;
        return readOtherCategories(catsPage,v.discussion,()=>stopped);
      })();
      await readCounts;
      const visits=await readVisits;
      const metrics=await readCats;
      await detail.close().catch(()=>{});
      await visitsPage.close().catch(()=>{});
      await catsPage.close().catch(()=>{});
      if(result)successful++;
      done++;
      emit('course',{url:item.url,counts:result,visits,metrics,error:result?'':error,message:`已检查 ${done}/${total}，成功读取任务点 ${successful} 门。`});
    }
    // 跨课程并发：同时处理多门，总耗时接近「最慢一门 × 批次数」。
    const CONCURRENCY=4;
    let cursor=0;
    async function worker(){
      while(!stopped){
        const i=cursor++;
        if(i>=courses.length)return;
        const item=courses[i];
        await checkOne(item, i);
      }
    }
    await Promise.all(Array.from({length:Math.min(CONCURRENCY,courses.length)},()=>worker()));
    if(stopped)return '已停止检查，已读取结果已保存，其余课程待确认。';
    return `检查结束：发现 ${courses.length} 门，成功读取 ${successful} 门，${courses.length-successful} 门待确认。${partial?'分页或课程文件夹未全部确认，请检查学习空间。':''}`;
  }finally{
    if(!keepSpace&&!space.isClosed())await space.close().catch(()=>{});
  }
}
async function manualLogin(context, {platform='chaoxing', stopped:cancelled=()=>stopped,
    newPage=()=>taskPage(context), timeout=300000, wait=delay, progress=()=>{}}={}){
  let page;
  try{
    page=await newPage();
    await page.bringToFront();
    if(context.newCDPSession){
      const session=await context.newCDPSession(page);
      try{
        const {windowId}=await session.send('Browser.getWindowForTarget');
        await session.send('Browser.setWindowBounds',{windowId,bounds:{windowState:'normal'}});
      }finally{await session.detach();}
    }
    progress('登录窗口已打开，正在加载登录页…');
    try{
      await page.goto(platform==='mooc'?'https://www.icourse163.org/':'https://passport2.chaoxing.com/login',
        {waitUntil:'domcontentloaded',timeout:45000});
    }catch(error){
      // The login service may replace its initial navigation during redirects.
      if(!String(error.message).includes('net::ERR_ABORTED') || page.isClosed())throw error;
    }
    await page.bringToFront();
    progress('请在浏览器中自行登录，完成后会自动保存登录状态。');
    const deadline=Date.now()+timeout;
    while(!cancelled() && !page.isClosed() && Date.now()<deadline){
      for(const candidate of context.pages()){
        try{
          if(platform==='mooc'){
            if(await moocAuthenticated(candidate))return {status:'success'};
            if(/^https:\/\/([\w-]+\.)?icourse163\.org\//.test(candidate.url())){
              for(const frame of candidate.frames()){
                const remember=frame.getByRole('checkbox',{name:/十天内免登录/}).first();
                if(await remember.count())await remember.check({timeout:1000}).catch(()=>{});
              }
            }
          }else{
            const url=new URL(candidate.url());
            if(url.protocol==='https:' && ['i.chaoxing.com','i.mooc.chaoxing.com'].includes(url.hostname)
                && !await needsLogin(candidate)
                && (await context.cookies('https://i.chaoxing.com/')).some(c=>c.name==='_uid' && /^[1-9]\d*$/.test(c.value))){
              return {status:'success'};
            }
            if(url.protocol==='https:' && url.hostname==='passport2.chaoxing.com'){
              await enableRememberLogin(candidate).catch(()=>{});
            }
          }
        }catch{} // A redirect can replace the execution context during this check.
      }
      await wait(750);
    }
    return {status:cancelled()||page.isClosed()?'cancelled':'not_logged_in'};
  }catch(error){
    const code=String(error.message||'').match(/net::ERR_[A-Z_]+/)?.[0];
    return {status:cancelled()||page?.isClosed()?'cancelled':'error',reason:'open_page',
      ...(code?{error_code:code}:{})};
  }
  // Keep the user's login page and the persistent browser profile intact.
}
function failureReason(error){
 const message=String(error?.message||error||'');
 if(/ERR_NAME_NOT_RESOLVED|ERR_INTERNET_DISCONNECTED|ERR_CONNECTION|ERR_PROXY|ERR_TUNNEL|ERR_TIMED_OUT|ECONN|ENOTFOUND/i.test(message))return '无法连接课程平台，请检查网络或代理设置后重试。';
 if(/timeout|timed out/i.test(message))return '课程页面加载超时，请稍后重试；若反复失败，请重新登录。';
 if(/未找到 Chrome|未找到.*Edge/.test(message))return '未找到 Chrome 或 Edge，请安装浏览器后重试。';
 if(/登录|课程分页|课件列表|测验|作业|未开课|尚未开课|课程列表/.test(message)&&!message.includes('://'))return message;
 return '课程页面读取失败，平台可能调整了页面或登录已失效。请重新登录后重试，已有记录保留。';
}
function scanOutcome(message){return {message,ok:/^检查结束/.test(message)};}
async function boundedLogin(login,timeout=45000){
 let timer;
 try{return await Promise.race([login(),new Promise(resolve=>{timer=setTimeout(()=>resolve({status:'timeout'}),timeout);})]);}
 finally{clearTimeout(timer);}
}
async function scanAfterSuccessfulLogin(result,{cancelled,saveSession,scan,progress}){
 if(result.status!=='success'||cancelled())return;
 await saveSession();
 if(cancelled())return;
 progress();
 return await scan();
}
module.exports={failureReason,scanOutcome,boundedLogin,scanAfterSuccessfulLogin,parseProgress,classifyLoginError,enableRememberLogin,loginAccount,manualLogin,scan};
if(require.main===module){
 const input=readline.createInterface({input:process.stdin});
 let boot;
 const ready=new Promise(resolve=>boot=resolve);
 input.on('line',line=>{try{const event=JSON.parse(line);if(event.type==='bootstrap')boot(event);if(event.type==='stop')stopTask();}catch{}});
 (async()=>{
  let browser,context,sessionSafe=true;
  const platform=process.argv[2], action=process.argv[3];
  try{
   if(!['chaoxing','mooc'].includes(platform)||!['scan','login','manual','authenticate'].includes(action))throw Error('不支持的操作。');
   const data=await ready;credentials=data.credentials;
   browser=await chromium.launch({executablePath:browserExecutable(),headless:action!=='manual',timeout:15000,args:['--no-first-run']});
   context=await browser.newContext({storageState:data.state||undefined,viewport:{width:1440,height:1000}});
   context.setDefaultTimeout(10000);
   if(action==='scan'){
    const check=platform==='mooc'?await loginMooc(context,null,{checkOnly:true,stopped:()=>stopped,newPage:()=>taskPage(context)}):await loginAccount(context,null,{checkOnly:true});
    if(check.status!=='success'){
     if(!credentials?.username){emit('done',{ok:false,message:'尚未登录或登录已失效，请点击“登录账号”完成登录后重试。'});return;}
     emit('progress',{message:'正在后台自动登录…'});
     const result=await boundedLogin(()=>platform==='mooc'?loginMooc(context,credentials,{visible:false,stopped:()=>stopped,newPage:()=>taskPage(context)}):loginAccount(context,credentials));
     sessionSafe=result.status==='success';
     if(!sessionSafe){const reasons={invalid_credentials:'账号或密码错误，请点击“登录账号”重新输入。',captcha:'平台要求验证码，请在登录弹窗中使用网页登录完成验证。',timeout:'平台登录超时，请检查网络后重试。'};emit('done',{ok:false,message:reasons[result.status]||'登录已失效，无法读取课程。请点击“登录账号”重新登录。'});return;}
     emit('session_state',{state:await context.storageState()});
    }
    credentials=null; // Scans cannot attempt another invisible login.
    const message=platform==='mooc'?await scanMooc(context,{stopped:()=>stopped,newPage:()=>taskPage(context),emit,credentials}):await scan(context);
    emit('done',scanOutcome(message));
   }else{
    const result=action==='manual'?await manualLogin(context,{platform,progress:message=>emit('progress',{message})}):
     await boundedLogin(()=>platform==='mooc'?loginMooc(context,credentials,{visible:false,stopped:()=>stopped,newPage:()=>taskPage(context)}):loginAccount(context,credentials));
    sessionSafe=result.status==='success';
    emit('login_result',result);
    const messages={success:'登录成功，已保存自动登录状态。',invalid_credentials:'账号或密码错误，请重新输入。',cancelled:'登录已取消。',captcha:'验证未完成，请重试。'};
    emit('done',{message:messages[result.status]||'登录未完成，请重试或使用网页登录。'});
    if(action!=='authenticate')await scanAfterSuccessfulLogin(result,{
     cancelled:()=>stopped,
     saveSession:async()=>emit('session_state',{state:await context.storageState()}),
     progress:()=>emit('progress',{message:'登录成功，正在自动读取课程任务…'}),
     scan:async()=>{
      const message=platform==='mooc'?await scanMooc(context,{stopped:()=>stopped,newPage:()=>taskPage(context),emit,credentials:action==='manual'?null:credentials}):await scan(context);
      emit('done',scanOutcome(message));
     }
    });
   }
  }catch(error){emit('error',{message:stopped?'操作已取消。':failureReason(error)});process.exitCode=stopped?0:1;}
  finally{
   if(context){if(sessionSafe&&!stopped)await context.storageState().then(state=>emit('session_state',{state})).catch(()=>{});await context.close().catch(()=>{});}
   if(browser)await browser.close().catch(()=>{});input.close();process.stdin.destroy();
  }
 })();
}

