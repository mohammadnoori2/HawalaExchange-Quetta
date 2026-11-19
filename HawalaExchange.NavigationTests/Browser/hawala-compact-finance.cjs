const {chromium}=require('playwright');const fs=require('node:fs');const path=require('node:path');const assert=require('node:assert/strict');
const repo=path.resolve(__dirname,'../..'),root=path.join(repo,'HawalaExchange.Web/wwwroot'),qa=path.join(root,'../obj/compact-finance-qa');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
const page=await browser.newPage();await page.route('**/*',route=>{const u=new URL(route.request().url());if(u.hostname!=='compact.test')return route.abort();if(!path.extname(u.pathname)){const fragment=fs.readFileSync(path.join(qa,u.pathname.slice(1)+'.html'),'utf8');return route.fulfill({contentType:'text/html',body:`<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.rtl.min.css"><link rel="stylesheet" href="/styles/app.css"><link rel="stylesheet" href="/styles/foundation/themes.css"></head><body><div class="app-shell"><main class="app-main">${fragment}</main></div></body></html>`});}const file=path.resolve(root,'.'+u.pathname);return file.startsWith(root+path.sep)&&fs.existsSync(file)?route.fulfill({path:file,contentType:file.endsWith('.js')?'application/javascript':file.endsWith('.css')?'text/css':undefined}):route.abort()});
const measurements=[];
for(const scenario of ['HawalaSend-Paid','HawalaReceive-Paid','HawalaReceive-Pending','HawalaReceive-Paid-Referral']){
for(const size of [{width:1366,height:768},{width:1920,height:1080}]){
await page.setViewportSize(size);await page.goto('http://compact.test/'+scenario);await page.evaluate(()=>document.fonts.ready);
assert.equal(await page.locator('[data-live-conversion]').isChecked(),false);
assert.equal(await page.locator('[data-live-currency="to"]').isVisible(),false);
const dimensions=await page.locator('.hawala-create-modal > .modal-body').evaluate(e=>({content:e.scrollHeight,visible:e.clientHeight}));
measurements.push({scenario,width:size.width,...dimensions});assert.ok(dimensions.content<=dimensions.visible+1,`${scenario} at ${size.width}: normal form scrolls (${JSON.stringify(dimensions)})`);
const box=await page.locator('.hawala-create-modal').boundingBox();assert.ok(box.y>=0&&box.y+box.height<=size.height+1);
if(scenario!=='HawalaReceive-Pending'){
const row=page.locator('.hawala-account-notes-row');const a=await row.locator('[data-account-query]').boundingBox(),n=await row.locator('input[placeholder="یادداشت"]').boundingBox();assert.ok(Math.abs(a.y-n.y)<2,'Account and notes must align in one row');
}
}
}
await page.setViewportSize({width:1366,height:768});await page.goto('http://compact.test/HawalaSend-Paid');
await page.locator('.hawala-create-modal').screenshot({path:path.join(qa,'normal.png')});
await page.evaluate(async()=>{await import('/js/money-input.js');document.querySelector('[data-live-conversion]').addEventListener('change',event=>document.querySelectorAll('.hawala-conversion-extra').forEach(e=>e.hidden=!event.target.checked));});
await page.locator('[data-live-currency="from"]').selectOption('2');assert.equal(await page.locator('[data-live-currency="to"]').inputValue(),'2');
await page.locator('[data-live-amount="from"]').fill('3250');assert.equal(await page.locator('[data-live-amount="to"]').inputValue(),'3,250');
await page.locator('[data-live-conversion]').check();assert.equal(await page.locator('[data-live-currency="to"]').isVisible(),true);
await page.locator('[data-live-currency="to"]').selectOption('1');assert.equal(await page.locator('[data-live-amount="rate"]').inputValue(),'');assert.equal(await page.locator('[data-live-amount="to"]').inputValue(),'');
// Static HTML fixtures simulate the enabled state that Blazor renders for a different currency.
await page.locator('[data-live-amount="rate"]').evaluate(e=>e.disabled=false);
await page.locator('[data-live-amount="rate"]').fill('65');assert.equal(await page.locator('[data-live-amount="to"]').inputValue(),'211,250');
await page.locator('.hawala-create-modal').screenshot({path:path.join(qa,'conversion.png')});
await page.locator('[data-live-conversion]').uncheck();assert.equal(await page.locator('[data-live-currency="to"]').isVisible(),false);assert.equal(await page.locator('[data-live-currency="to"]').inputValue(),'2');assert.equal(await page.locator('[data-live-amount="rate"]').inputValue(),'1');assert.equal(await page.locator('[data-live-amount="to"]').inputValue(),'3,250');
await page.setViewportSize({width:375,height:812});assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
console.log(JSON.stringify({passed:true,measurements,liveSameCurrency:true,conversionToggle:true,rateReset:true}));
}finally{await browser.close()}})().catch(e=>{console.error(e);process.exitCode=1});
