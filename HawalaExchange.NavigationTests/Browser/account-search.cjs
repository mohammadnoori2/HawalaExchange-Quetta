const { chromium } = require('playwright');
const path = require('path');
const assert = require('node:assert/strict');
const repo = path.resolve(__dirname, '../..');
(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage();
        await page.route('http://search.test/**', r => r.fulfill({ contentType: 'text/html', body: '<html><body></body></html>' }));
        await page.goto('http://search.test');
        await page.setContent(`<div data-account-search data-account-value="1" data-account-revision="0" data-account-limit="30">
            <input data-account-query><input type="hidden" data-account-commit>
            <div id="results" data-account-results popover="manual"></div></div>`);
        await page.evaluate(() => {
            const root = document.querySelector('[data-account-search]');
            root.dataset.accountOptions = JSON.stringify([
                { id: '1', name: 'صندوق مرکزی', code: 'C1', reference: 'کابل', type: 'صندوق', display: 'صندوق مرکزی (C1)' },
                { id: '2', name: 'مشتری کابل', code: 'C2', reference: 'احمد', type: 'مشتری', display: 'مشتری کابل (C2)' },
                { id: '3', name: '<img src=x onerror=alert(1)>', code: 'SAFE', type: 'صندوق', display: '<img src=x onerror=alert(1)>' }
            ]);
            window.commits = [];
            root.querySelector('[data-account-commit]').addEventListener('change', e => window.commits.push(JSON.parse(e.target.value)));
        });
        await page.addScriptTag({ path: path.join(repo, 'HawalaExchange.Web/wwwroot/js/hawala-tools.js') });
        await page.addScriptTag({ type: 'module', path: path.join(repo, 'HawalaExchange.Web/wwwroot/js/account-search.js') });
        const input = page.locator('[data-account-query]');
        await page.waitForFunction(() => document.querySelector('[data-account-query]').value.includes('C1'));
        await input.fill('مشتری کابل');
        assert.equal(await input.inputValue(), 'مشتری کابل');
        assert.equal(await page.locator('[data-account-id]').count(), 1);
        // A delayed response and unrelated options refresh must not overwrite a live draft/caret.
        await input.press('ArrowLeft');
        const caret = await input.evaluate(x => x.selectionStart);
        await page.evaluate(() => {
            const root = document.querySelector('[data-account-search]');
            root.dataset.accountValue = '1'; root.dataset.accountRevision = '0';
            root.dataset.accountOptions = root.dataset.accountOptions + ' ';
        });
        await page.waitForTimeout(30);
        assert.equal(await input.inputValue(), 'مشتری کابل');
        assert.equal(await input.evaluate(x => x.selectionStart), caret);
        await input.press('ControlOrMeta+A'); await input.press('Backspace');
        await input.pressSequentially('صندوق مرکزی', { delay: 1 });
        assert.equal(await input.inputValue(), 'صندوق مرکزی');
        await input.press('ControlOrMeta+A'); await input.press('Backspace');
        assert.equal(await input.inputValue(), '');
        assert.equal(await page.evaluate(() => window.commits.length), 1, 'Typing emitted per-key server changes');
        await input.fill('C2'); await input.press('Enter');
        assert.equal(await input.inputValue(), 'مشتری کابل (C2)');
        assert.deepEqual(await page.evaluate(() => window.commits), [{ id: '0', revision: 1 }, { id: '2', revision: 2 }]);
        await page.evaluate(() => {
            const root = document.querySelector('[data-account-search]');
            root.dataset.accountRevision = '1'; root.dataset.accountValue = '0';
        });
        await page.waitForTimeout(30);
        assert.equal(await input.inputValue(), 'مشتری کابل (C2)', 'Late clear erased a newer selection');
        await page.evaluate(() => {
            const root = document.querySelector('[data-account-search]');
            root.dataset.accountRevision = '2'; root.dataset.accountValue = '2';
        });
        await input.fill('SAFE');
        assert.equal(await page.locator('[data-account-results] img').count(), 0);
        await input.press('ArrowDown'); await input.press('Enter');
        assert.equal(await input.inputValue(), '<img src=x onerror=alert(1)>');
        await input.fill('ناشناخته');
        assert.ok(await page.locator('[data-account-results]').innerText().then(t => t.includes('حسابی یافت نشد')));
        await input.press('Escape');
        assert.equal(await input.getAttribute('aria-expanded'), 'false');
        console.log('Account search: typing, deletion, replacement, caret, stale responses, selection, keyboard and text encoding passed.');
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
