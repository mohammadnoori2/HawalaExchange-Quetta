const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../HawalaExchange.Web/wwwroot');
const output = path.resolve(__dirname, '../../HawalaExchange.Web/obj/theme-qa');
const badge = (code, tone) => `<bdi class="currency-badge currency-tone-${tone}" data-currency="${code}">${code}</bdi>`;
const button = '<button type="button" class="btn btn-outline-secondary app-theme-toggle" data-theme-toggle aria-pressed="false"><i></i><span data-theme-label>حالت تاریک</span></button>';
const html = `<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><script src="/js/app-theme.js"></script><link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.rtl.min.css"><link rel="stylesheet" href="/styles/app.css"><link rel="stylesheet" href="/styles/foundation/themes.css"></head><body>
<div class="page app-shell"><aside class="sidebar"><div class="nav-sidebar p-3"><h3>صرافی</h3>${button}<a class="nav-link-custom" href="#">حواله‌ها</a></div></aside><main class="app-main"><article class="app-content p-3"><div class="page-header"><h1 class="page-title">داشبورد</h1></div><div class="dashboard-wrapper"><div class="provision-row"><div class="provision-card"><div class="provision-label">Profit / loss (AFN)</div><div class="provision-amount text-danger">-24,948</div><div class="provision-sub">Income and expenses</div><a class="btn btn-sm btn-outline-primary mt-3" href="#">Profit / loss report</a></div><div class="top-remain-card p-3"><h2>بیشترین بدهکاران</h2><div class="counterparty-currency-heading">${badge('USD',0)} <span>مجموع: 25,500</span></div><div class="remain-item"><span>نمایندگی کابل برچی</span><span>نمایندگی</span><strong>15,000</strong></div><div class="counterparty-currency-heading">${badge('AFN',1)} <span>مجموع: 100,000</span></div><div class="remain-item"><span>مشتری</span><span>مشتری</span><strong>100,000</strong></div></div></div></div>
<div class="card mt-3"><div class="card-body"><label class="form-label" for="amount">مبلغ</label><input id="amount" class="form-control" value="15,700"><select class="form-select mt-2" aria-label="ارز"><option>AFN</option><option>USD</option></select><table class="table table-striped table-hover mt-3"><thead><tr><th>نمبر مسلسل</th><th>مبلغ</th><th>وضعیت</th></tr></thead><tbody><tr><td>1251</td><td class="text-danger">15,700 ${badge('AFN',1)}</td><td>اجرا‌شده</td></tr><tr><td>1252</td><td>1,250 ${badge('USD',0)}</td><td>اجرا‌نشده</td></tr><tr><td>1253</td><td>140,000 ${badge('PKR',2)}</td><td>اجرا‌شده</td></tr><tr class="hawala-cancelled"><td>1254</td><td>100 ${badge('EUR',3)}</td><td>لغو‌شده</td></tr></tbody></table><p class="text-muted">رنگ ارز در تمام فهرست‌ها ثابت است.</p><button class="btn btn-primary">ثبت حسابداری</button></div></div><div class="hawala-receipt mt-3"><strong>رسید چاپی</strong><p>15,700 AFN</p></div></article></main></div></body></html>`;
function luminance(rgb) {
    const a = rgb.match(/[\d.]+/g).slice(0,3).map(Number).map(x => { x/=255; return x<=0.04045?x/12.92:((x+0.055)/1.055)**2.4; });
    return .2126*a[0]+.7152*a[1]+.0722*a[2];
}
async function main() {
    fs.mkdirSync(output,{recursive:true});
    const browser=await chromium.launch({channel:'msedge',headless:true});
    try {
        const context=await browser.newContext();
        await context.route('**/*',route=>{
            const url=new URL(route.request().url());
            if(url.hostname!=='theme.test') return route.abort();
            if(url.pathname==='/')return route.fulfill({contentType:'text/html',body:html});
            const file=path.resolve(root,'.'+decodeURIComponent(url.pathname));
            if(!file.startsWith(root+path.sep)||!fs.existsSync(file))return route.fulfill({status:404,body:''});
            return route.fulfill({path:file,contentType:file.endsWith('.css')?'text/css':file.endsWith('.js')?'application/javascript':undefined});
        });
        const page=await context.newPage();await page.goto('http://theme.test/');
        for(const theme of ['light','dark']){
            if(theme==='dark')await page.locator('[data-theme-toggle]').click();
            await page.locator('.form-control').evaluate(e=>Promise.all(e.getAnimations().map(animation=>animation.finished)));
            assert.equal(await page.locator('html').getAttribute('data-bs-theme'),theme);
            const cells=await page.locator('tbody tr:first-child td').evaluateAll(elements=>elements.map(e=>({display:getComputedStyle(e).display,y:e.getBoundingClientRect().y})));
            assert.ok(cells.every(c=>c.display==='table-cell'),'Numeric status colors must not change table cells into blocks');
            assert.ok(cells.every(c=>Math.abs(c.y-cells[0].y)<1),'All amounts stay in the same horizontal row');
            for(const selector of ['.card','.form-control','.btn-primary','.counterparty-currency-heading',...Array.from({length:4},(_,i)=>`.currency-tone-${i}`)]){
                const paint=await page.locator(selector).first().evaluate(e=>{const s=getComputedStyle(e);return {color:s.color,bg:s.backgroundColor};});
                const [a,b]=[luminance(paint.color),luminance(paint.bg)].sort((x,y)=>y-x);
                assert.ok((a+.05)/(b+.05)>=4.5,`${theme} ${selector} contrast ${(a+.05)/(b+.05)}`);
            }
            assert.equal(await page.locator('.hawala-cancelled td').first().evaluate(e=>getComputedStyle(e).backgroundColor),'rgb(127, 29, 29)');
            assert.equal(await page.locator('.hawala-receipt').evaluate(e=>getComputedStyle(e).backgroundColor),'rgb(255, 255, 255)');
            await page.screenshot({path:path.join(output,`${theme}.png`),fullPage:true});
            for(const width of [375,768,1440]){
                await page.setViewportSize({width,height:900});
                assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),`${theme}: no horizontal page overflow at ${width}`);
                const layout=await page.evaluate(()=>{const profit=document.querySelector('.provision-card').getBoundingClientRect();const table=document.querySelector('.top-remain-card').getBoundingClientRect();return {height:profit.height,profitWidth:profit.width,tableWidth:table.width,bottom:profit.bottom,top:table.top};});
                assert.ok(layout.height<180,`Profit summary remains compact at ${width}px`);
                assert.ok(Math.abs(layout.profitWidth-layout.tableWidth)<2 && layout.bottom<=layout.top,'Summary sits above the full-width balance table without an empty side column');
            }
        }
        await page.reload();assert.equal(await page.locator('html').getAttribute('data-bs-theme'),'dark');
        assert.equal(await page.locator('[data-theme-toggle]').getAttribute('aria-pressed'),'true');
        const second=await context.newPage();await second.goto('http://theme.test/');
        await page.locator('[data-theme-toggle]').click();
        await second.waitForFunction(()=>document.documentElement.dataset.bsTheme==='light');
        await page.emulateMedia({media:'print'});
        assert.equal(await page.locator('[data-theme-toggle]').evaluate(e=>getComputedStyle(e).display),'none');
        console.log('PASS: light/dark contrast, currency colors, canceled rows, receipt paper, responsive layout, persistence, tab synchronization and print.');
    } finally {await browser.close();}
}
main().catch(error=>{console.error(error);process.exitCode=1;});
