const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const assert = require('node:assert/strict');
if (!process.env.HAWALA_UI_CONTEXT) throw new Error('Supply isolated test login context.');
const info = JSON.parse(fs.readFileSync(process.env.HAWALA_UI_CONTEXT));
if (!info.database?.startsWith('HawalaExchangeQuettaPerformanceTests_')) throw new Error('Use an isolated fixture.');
const origin = process.env.HAWALA_UI_ORIGIN || 'http://127.0.0.1:5112';
const before = process.argv.includes('before');
const out = process.env.HAWALA_UI_OUTPUT || path.dirname(process.env.HAWALA_UI_CONTEXT);
(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    let page;
    try {
        const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
        // Simulate 300ms network round trips without changing the machine or tunnel.
        await context.routeWebSocket(/\/_blazor/, ws => {
            const server = ws.connectToServer();
            ws.onMessage(m => setTimeout(() => { try { server.send(m); } catch {} }, 150));
            server.onMessage(m => setTimeout(() => { try { ws.send(m); } catch {} }, 150));
        });
        await context.route('https://fonts.googleapis.com/**', r => r.abort());
        await context.route('https://fonts.gstatic.com/**', r => r.abort());
        page = await context.newPage();
        page.on('console', m => { if (m.type() === 'error') console.log('BROWSER', m.text()); });
        await page.goto(origin + '/Account/Login?ReturnUrl=%2Faccounts');
        await page.locator('[name="Input.UserName"]').fill(info.user);
        await page.locator('[name="Input.Password"]').fill(info.password);
        await page.locator('button[type=submit]').click();
        await page.waitForURL('**/accounts');
        const results = [];
        for (const route of ['/accounts', '/customers', '/correspondents']) {
            console.log('PAGE', route);
            await page.goto(origin + route, { waitUntil: 'domcontentloaded' });
            if (route === '/customers') await page.getByRole('button', { name: 'گیرندگان ثبت‌شده', exact: true }).click();
            await page.locator('.operation-dot-btn').first().waitFor();
            await page.waitForTimeout(1600); // Let initial permissions/quick actions settle under latency.
            for (let i = 0; i < (before ? 1 : 3); i++) {
                const result = await page.evaluate(async () => {
                    const button = document.querySelector('.operation-dot-btn');
                    const root = button.closest('.configurable-action-menu');
                    const menu = document.getElementById(root.dataset.actionMenuId);
                    const start = performance.now();
                    button.click();
                    const immediate = getComputedStyle(menu).visibility === 'visible';
                    while (getComputedStyle(menu).visibility !== 'visible' && performance.now() - start < 8000)
                        await new Promise(requestAnimationFrame);
                    return { elapsedMs: performance.now() - start, immediate, expanded: button.getAttribute('aria-expanded') };
                });
                if (!before) { assert.ok(result.immediate); assert.equal(result.expanded, 'true'); }
                assert.ok(result.elapsedMs < 8000);
                results.push({ route, ...result });
                await page.keyboard.press('Escape');
                await page.waitForTimeout(before ? 700 : 30);
            }
            if (!before) {
                // The settings editor remains a server operation; opening/closing the shell stays local.
                await page.locator('.operation-dot-btn').first().click();
                await page.locator('.operation-menu-configure-button:visible').click();
                await page.locator('.operation-menu-settings:visible').waitFor();
                await page.getByRole('button', { name: 'انصراف', exact: true }).click();
                await page.locator('.operation-menu-settings:visible').waitFor({ state: 'hidden' });
                await page.keyboard.press('Escape');
            }
        }
        if (!before) {
            await page.setViewportSize({ width: 375, height: 812 });
            await page.locator('.operation-dot-btn').first().scrollIntoViewIfNeeded();
            await page.waitForTimeout(400);
            await page.locator('.operation-dot-btn').first().click();
            const menu = page.locator('.operation-menu:visible').first();
            const box = await menu.boundingBox();
            assert.ok(box.x >= 0 && box.x + box.width <= 376);
            await page.screenshot({ path: path.join(out, 'action-menu-mobile.png'), animations: 'disabled' });
        }
        fs.writeFileSync(path.join(out, `menu-${before ? 'before' : 'after'}.json`), JSON.stringify(results, null, 2));
        console.log(JSON.stringify(results));
    } catch (error) {
        console.error(await page?.locator('body').innerText());
        if (page) await page.screenshot({ path: path.join(out, 'menu-' + (before ? 'before' : 'after') + '-failure.png') });
        throw error;
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
