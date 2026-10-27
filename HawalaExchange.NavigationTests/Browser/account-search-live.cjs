const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const assert = require('node:assert/strict');
if (!process.env.HAWALA_UI_CONTEXT) throw new Error('Supply isolated test context.');
const info = JSON.parse(fs.readFileSync(process.env.HAWALA_UI_CONTEXT));
if (!info.database?.startsWith('HawalaExchangeQuettaPerformanceTests_')) throw new Error('Use a disposable fixture.');
const out = process.env.HAWALA_UI_OUTPUT || path.dirname(process.env.HAWALA_UI_CONTEXT);
(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    let page;
    try {
        const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, hasTouch: true });
        await context.routeWebSocket(/\/_blazor/, ws => {
            const server = ws.connectToServer();
            ws.onMessage(m => setTimeout(() => { try { server.send(m); } catch {} }, 150));
            server.onMessage(m => setTimeout(() => { try { ws.send(m); } catch {} }, 150));
        });
        await context.route('https://fonts.googleapis.com/**', r => r.abort());
        await context.route('https://fonts.gstatic.com/**', r => r.abort());
        page = await context.newPage();
        const origin = process.env.HAWALA_UI_ORIGIN || 'http://127.0.0.1:5112';
        await page.goto(origin + '/Account/Login?ReturnUrl=%2Faccounts');
        await page.locator('[name="Input.UserName"]').fill(info.user);
        await page.locator('[name="Input.Password"]').fill(info.password);
        await page.locator('button[type=submit]').click();
        await page.waitForURL('**/accounts');
        const checks = [];
        for (const [route, label] of [['/hawala-transactions', 'حواله جدید'], ['/transfers', 'انتقال جدید'], ['/deposit-withdraw', 'عملیات جدید']]) {
            console.log('PAGE', route);
            await page.goto(origin + route, { waitUntil: 'domcontentloaded' });
            await page.locator('.page-actions button').filter({ hasText: label }).click();
            const modal = page.locator('.modal.show').first();
            await modal.waitFor();
            if (route === '/hawala-transactions') await modal.locator('select').filter({ has: page.locator('option[value="HawalaSend"]') }).selectOption('HawalaSend');
            await modal.locator('[data-account-query]').first().waitFor();
            const count = await modal.locator('[data-account-search]').count();
            for (let i = 0; i < count; i++) {
                const root = modal.locator('[data-account-search]').nth(i);
                const input = root.locator('[data-account-query]');
                const options = await root.evaluate(x => JSON.parse(x.dataset.accountOptions));
                assert.ok(options.length > 0);
                const choice = options[0];
                const initialInvalidation = await root.getAttribute('data-account-value') !== '0' ? 1 : 0;
                await root.evaluate(x => { x.commitCount = 0; x.querySelector('[data-account-commit]').addEventListener('change', () => x.commitCount++); });
                await input.fill('حساب مرکزی کابل');
                await input.press('ControlOrMeta+A'); await input.press('Backspace');
                await input.pressSequentially('تست پاک کردن', { delay: 1 });
                assert.equal(await input.inputValue(), 'تست پاک کردن');
                assert.equal(await root.evaluate(x => x.commitCount), initialInvalidation);
                await input.fill(choice.code || choice.name);
                await root.locator(`[data-account-id="${choice.id}"]`).tap();
                await page.waitForFunction(({ selector, id }) => document.querySelector(selector)?.dataset.accountValue === id,
                    { selector: `[data-account-search]:has(#${await root.locator('[data-account-results]').getAttribute('id')})`, id: choice.id });
                assert.equal(await input.inputValue(), choice.display);
                await input.fill('تایپ سریع فارسی');
                await input.press('ArrowLeft'); await input.press('Backspace');
                const draft = await input.inputValue();
                const caret = await input.evaluate(x => x.selectionStart);
                await page.waitForTimeout(1100); // Includes the real invalidation round trip and parent rerender.
                assert.equal(await input.inputValue(), draft);
                assert.equal(await input.evaluate(x => x.selectionStart), caret);
                assert.equal(await root.getAttribute('data-account-value'), '0');
                await input.press('ControlOrMeta+A'); await input.press('Backspace');
                assert.equal(await input.inputValue(), '');
                assert.equal(await root.evaluate(x => x.commitCount), initialInvalidation + 2, 'Per-key changes or duplicate invalidation');
                checks.push({ route, field: i + 1, options: options.length, selection: 'passed', fastTypingAndDeletion: 'passed', staleEchoAndCaret: 'passed' });
            }
            if (route === '/transfers') {
                await page.setViewportSize({ width: 375, height: 812 });
                const input = modal.locator('[data-account-query]').first();
                await input.fill('NAV-A-1000');
                await page.screenshot({ path: path.join(out, 'account-search-mobile.png'), animations: 'disabled' });
                const menu = modal.locator('[data-account-results]:popover-open').first();
                const box = await menu.boundingBox();
                assert.ok(box.x >= 0 && box.x + box.width <= 376);
                await input.press('Escape');
                await page.setViewportSize({ width: 1440, height: 1000 });
            }
            await modal.locator('.modal-header .btn-close, .modal-header .btn-close-white').first().click();
            await modal.waitFor({ state: 'hidden' });
        }
        fs.writeFileSync(path.join(out, 'account-search-results.json'), JSON.stringify(checks, null, 2));
        console.log(JSON.stringify(checks));
    } catch (error) {
        if (page) { console.error(await page.locator('body').innerText()); await page.screenshot({ path: path.join(out, 'search-failure.png') }); }
        throw error;
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
