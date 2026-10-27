const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const assert = require('node:assert/strict');
const repo = path.resolve(__dirname, '../..');
(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 375, height: 812 } });
        await page.goto('http://localhost.invalid').catch(() => {});
        await page.route('http://menu.test/**', r => r.fulfill({ contentType: 'text/html', body: '<html><body></body></html>' }));
        await page.goto('http://menu.test');
        await page.addStyleTag({ path: path.join(repo, 'HawalaExchange.Web/wwwroot/styles/foundation/site.css') });
        await page.evaluate(() => {
            for (let i = 1; i <= 2; i++) document.body.insertAdjacentHTML('beforeend', `
                <div class="configurable-action-menu" data-action-menu-id="menu${i}" data-action-quick-host-id="quick${i}">
                <div id="quick${i}"></div><button id="button${i}" data-operation-menu-toggle="menu${i}" aria-expanded="false">Menu ${i}</button>
                <div id="menu${i}" class="operation-menu operation-menu-closed" role="menu"><div class="operation-menu-items">
                <button class="operation-menu-item" data-action-key="view">View</button>
                <button class="operation-menu-item" data-action-key="edit" disabled>Edit</button>
                </div><button class="operation-menu-configure-button">Settings</button></div></div>`);
            window.actionCount = 0;
            document.querySelector('#menu1 [data-action-key=view]').onclick = () => window.actionCount++;
        });
        await page.addScriptTag({ path: path.join(repo, 'HawalaExchange.Web/wwwroot/js/hawala-tools.js') });
        const visible = id => page.evaluate(id => getComputedStyle(document.getElementById(id)).visibility === 'visible', id);
        const timings = [];
        for (const id of [1, 2, 1]) {
            const elapsed = await page.evaluate(id => {
                const start = performance.now();
                document.getElementById('button' + id).click();
                return performance.now() - start;
            }, id);
            assert.ok(await visible('menu' + id), 'Menu did not open synchronously without a server');
            timings.push(elapsed);
        }
        assert.equal(await visible('menu2'), false);
        await page.keyboard.press('Escape');
        assert.equal(await visible('menu1'), false);
        assert.equal(await page.locator('#button1').getAttribute('aria-expanded'), 'false');
        await page.keyboard.press('Enter');
        assert.ok(await visible('menu1'));
        await page.locator('#menu1 [data-action-key=view]').click();
        assert.equal(await page.evaluate(() => window.actionCount), 1);
        assert.equal(await visible('menu1'), false);
        await page.locator('#button1').click();
        await page.mouse.click(350, 750);
        assert.equal(await visible('menu1'), false);
        // A stalled callback from a previously open settings menu must not delay the next menu.
        await page.evaluate(() => {
            window.hawalaTools.openOperationMenu('button1', 'menu1', { invokeMethodAsync: () => new Promise(() => {}) });
            document.getElementById('button2').click();
        });
        assert.ok(await visible('menu2'));
        assert.equal(await visible('menu1'), false);
        const bounds = await page.locator('#menu2').boundingBox();
        assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= 375);
        await page.locator('#button2').click();
        assert.equal(await visible('menu2'), false);
        await page.evaluate(() => window.hawalaTools.refreshOperationMenu('button2', 'menu2', null));
        assert.equal(await visible('menu2'), false, 'A late server update reopened a closed menu');
        await page.locator('#button2').click();
        await page.evaluate(() => document.getElementById('menu2').dispatchEvent(new Event('scroll')));
        assert.ok(await visible('menu2'), 'Scrolling inside a menu closed it');
        await page.evaluate(() => document.querySelector('[data-action-menu-id="menu2"]').remove());
        await page.waitForTimeout(30);
        await page.locator('#button1').click();
        assert.ok(await visible('menu1'));
        await page.keyboard.press('Escape');
        await page.evaluate(() => {
            window.hawalaTools.syncOperationMenuQuickActions('menu1', 'quick1', 'test', ['view']);
        });
        await page.locator('#quick1 button').click();
        assert.equal(await page.evaluate(() => window.actionCount), 2);
        console.log(JSON.stringify({ synchronousOpenMs: timings, keyboard: 'passed', outsideClose: 'passed',
            actionOnce: 'passed', stalledServer: 'passed', viewport: 'passed', quickAction: 'passed' }));
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
