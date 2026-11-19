const {chromium}=require('playwright');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../../HawalaExchange.Web/wwwroot');
const fixture=`<div class="page-header-stat"><span data-check>تعداد حواله</span><strong data-check>1,234</strong></div>
<div class="money-exchange-table-wrap"><span class="exchange-type-badge" data-check>تبدیل نمایندگی</span></div>
<div class="correspondent-title"><span data-check>جزئیات نمایندگی</span></div>
${['active','inactive','archived','pending','success','danger','warning','info','secondary','dark','light','primary'].map(t=>`<span class="badge-modern badge-${t}" data-check>${t} · نوع حساب</span>`).join('')}
<div class="platform-workspace">${['success','warning','danger','info','secondary'].map(t=>`<span class="badge-modern badge-${t}" data-check>platform ${t}</span>`).join('')}</div>
${['secondary','info','warning','primary','success','danger'].map(t=>`<p class="text-${t}" data-check>${t}</p><button class="btn btn-outline-${t}" data-check>${t}</button>`).join('')}
<div class="entity-profile-item"><span data-check>مشخصات مشتری</span></div><div class="account-view-page-header"><div class="account-view-title"><small data-check>جزئیات حساب</small></div></div><span class="badge text-bg-light" data-check>وضعیت</span><p class="text-dark" data-check>شرح معامله</p><label class="form-label" data-check>مبلغ</label>
<table class="table"><thead class="table-light"><tr><th data-check>عنوان جدول</th></tr></thead><tbody><tr><td data-check>10,000</td></tr></tbody></table>`;
function light(rgb){return rgb.match(/[\d.]+/g).slice(0,3).map(Number).map(x=>{x/=255;return x<=.04045?x/12.92:((x+.055)/1.055)**2.4}).reduce((a,x,i)=>a+x*[.2126,.7152,.0722][i],0)}
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
const page=await browser.newPage();await page.route('**/*',route=>{const u=new URL(route.request().url());if(u.hostname!=='contrast.test')return route.abort();if(u.pathname==='/')return route.fulfill({contentType:'text/html',body:`<!doctype html><html data-bs-theme="dark" dir="rtl"><head><meta charset="utf-8"><link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.rtl.min.css"><link rel="stylesheet" href="/styles/app.css"><link rel="stylesheet" href="/styles/foundation/themes.css"></head><body><main class="card p-3">${fixture}</main></body></html>`});const file=path.resolve(root,'.'+u.pathname);return file.startsWith(root+path.sep)&&fs.existsSync(file)?route.fulfill({path:file,contentType:'text/css'}):route.abort()});
await page.goto('http://contrast.test/');
const failures=[];
for(const element of await page.locator('[data-check]').all()){
if((await element.getAttribute('class')||'').includes('btn ')){await element.hover();await element.evaluate(e=>Promise.all(e.getAnimations().map(a=>a.finished)));}
const paint=await element.evaluate(e=>{const s=getComputedStyle(e);let chain=[],parent=e;while(parent){chain.unshift(getComputedStyle(parent).backgroundColor);parent=parent.parentElement;}let rgb=[255,255,255];for(const bg of chain){const values=bg.match(/[\d.]+/g)?.map(Number);if(!values)continue;const alpha=values[3]??1;rgb=rgb.map((v,i)=>values[i]*alpha+v*(1-alpha));}return {name:e.className||e.textContent,color:s.color,bg:`rgb(${rgb.join(',')})`}});
const [a,b]=[light(paint.color),light(paint.bg)].sort((x,y)=>y-x);const contrast=(a+.05)/(b+.05);if(contrast<4.5)failures.push(`${paint.name}: ${contrast.toFixed(2)} (${paint.color} on ${paint.bg})`);
}
assert.deepEqual(failures,[],'Dark text contrast must meet 4.5:1');console.log('PASS: dark-mode statistics, descriptions, semantic text, outline actions, labels and tables meet 4.5:1 contrast.');
}finally{await browser.close()}})().catch(e=>{console.error(e);process.exitCode=1});
