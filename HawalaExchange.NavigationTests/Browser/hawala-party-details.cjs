const {chromium}=require('playwright');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const repo=path.resolve(__dirname,'../..'), root=path.join(repo,'HawalaExchange.Web/wwwroot');
const razor=fs.readFileSync(path.join(repo,'HawalaExchange.Web/Components/Shared/Modals/CreateHawalaModal.razor'),'utf8');
let body=razor.slice(razor.indexOf('<!-- اطلاعات فرستنده و گیرنده -->'),razor.indexOf('<!-- اطلاعات مالی -->'));
body=body.replace(/@if \(!string.IsNullOrEmpty\(CurrentHawala\.(?:Sender|Receiver)TazkiraImagePath\)\)\s*\{[\s\S]*?\n\s*\}/g,'')
.replace(/<InputFile/g,'<input type="file"').replace(/@bind="CurrentHawala\.([^"]+)"/g,'data-field="$1"')
.replace(/@bind="showPartyDetails"/g,'data-party="shared"')
.replace(/hidden="@\(!showPartyDetails\)"/g,'hidden');
const html=`<!doctype html><html dir="rtl"><head><meta charset="utf-8"><link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.rtl.min.css"><link rel="stylesheet" href="/styles/app.css"><link rel="stylesheet" href="/styles/foundation/themes.css"></head><body><div class="modal show d-block"><div class="modal-dialog modal-xl modal-dialog-scrollable"><div class="modal-content hawala-create-modal"><div class="modal-body">${body}</div></div></div></div></body></html>`;
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
const page=await browser.newPage({viewport:{width:1440,height:1000}});
await page.route('**/*',route=>{const u=new URL(route.request().url());if(u.hostname!=='party.test')return route.abort();if(u.pathname==='/')return route.fulfill({contentType:'text/html',body:html});const file=path.resolve(root,'.'+u.pathname);return file.startsWith(root+path.sep)&&fs.existsSync(file)?route.fulfill({path:file,contentType:'text/css'}):route.abort()});
await page.goto('http://party.test/');
const output=path.join(root,'../obj/party-details-qa');fs.mkdirSync(output,{recursive:true});
await page.locator('.hawala-create-modal').screenshot({path:path.join(output,'collapsed.png')});
await page.evaluate(()=>document.querySelectorAll('[data-party]').forEach(box=>box.addEventListener('change',()=>box.closest('.modern-card').querySelectorAll('.hawala-party-extra').forEach(group=>group.hidden=!box.checked))));
for(const party of ['Sender','Receiver']){
assert.equal(await page.locator(`[data-field="${party}Name"]`).isVisible(),true);
assert.equal(await page.locator(`[data-field="${party}Phone"]`).isVisible(),false);
}
assert.equal(await page.locator('[data-party]').count(),1);
assert.ok(await page.evaluate(()=>document.querySelector('[data-field="SenderName"]').closest('.row')===document.querySelector('[data-field="ReceiverName"]').closest('.row')));
await page.locator('[data-party="shared"]').check();
await page.locator('[data-field="SenderPhone"]').fill('0700000000');
assert.equal(await page.locator('[data-field="ReceiverPhone"]').isVisible(),true);
await page.locator('[data-field="ReceiverAddress"]').fill('Kabul');
await page.locator('[data-party="shared"]').uncheck();
for(const party of ['Sender','Receiver'])assert.equal(await page.locator(`[data-field="${party}Phone"]`).isVisible(),false);
await page.locator('[data-party="shared"]').check();
assert.equal(await page.locator('[data-field="SenderPhone"]').inputValue(),'0700000000');
assert.equal(await page.locator('[data-field="ReceiverAddress"]').inputValue(),'Kabul');
await page.locator('.hawala-create-modal').screenshot({path:path.join(output,'expanded.png')});
await page.setViewportSize({width:375,height:900});
assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
await page.locator('[data-party="shared"]').uncheck();
assert.equal(await page.locator('[data-field="ReceiverAddress"]').isVisible(),false);
console.log('PASS: shared name row, one toggle for both parties, retained inputs and narrow-screen layout.');
}finally{await browser.close()}})().catch(e=>{console.error(e);process.exitCode=1});
