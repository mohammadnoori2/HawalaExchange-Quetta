// Saves one imported hawala in the disposable location-review fixture only.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const assert = require('node:assert/strict');
if (!process.env.HAWALA_UI_CONTEXT || process.env.HAWALA_LOCATION_DISPOSABLE !== 'true') throw new Error('Explicit disposable fixture required.');
const info = JSON.parse(fs.readFileSync(process.env.HAWALA_UI_CONTEXT));
if (!info.database?.startsWith('HawalaExchangeQuettaPerformanceTests_')) throw new Error('Unsafe database.');
const out = process.env.HAWALA_UI_OUTPUT || path.dirname(process.env.HAWALA_UI_CONTEXT);
(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    let page;
    try {
        page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
        await page.route('https://fonts.googleapis.com/**', r => r.abort());
        await page.route('https://fonts.gstatic.com/**', r => r.abort());
        await page.goto((process.env.HAWALA_UI_ORIGIN || 'http://127.0.0.1:5112') + '/Account/Login?ReturnUrl=%2Fhawala-transactions');
        await page.locator('[name="Input.UserName"]').fill(info.user);
        await page.locator('[name="Input.Password"]').fill(info.password);
        await page.locator('button[type=submit]').click();
        await page.waitForURL('**/hawala-transactions');
        const row = page.locator('tbody tr').filter({ hasText: '98000002' });
        await row.waitFor();
        const quickEdit = row.locator('.operation-quick-action[aria-label="ویرایش"]');
        if (await quickEdit.isVisible()) await quickEdit.click();
        else {
            await row.locator('.operation-dot-btn').click();
            await row.locator('.operation-quick-action[aria-label="ویرایش"]:visible, [data-action-key=edit]:not(.operation-menu-promoted):visible').first().click();
        }
        const modal = page.locator('.modal.show').first();
        await modal.waitFor();
        const location = modal.locator('select').filter({ has: page.locator('option').filter({ hasText: /^lashkargah\s*-/ }) });
        await location.selectOption(String(info.newLocationId));
        await page.waitForFunction(id => document.querySelector('.modal.show [data-account-search]')?.dataset.accountValue === String(id), info.newAccountId);
        const account = modal.locator('[data-account-query]').first();
        assert.equal(await account.inputValue(), 'Assigned Lashkargah Receiver');
        assert.equal(await modal.locator('.alert-danger').count(), 0);
        await page.screenshot({ path: path.join(out, 'location-edit-before-save.png'), animations: 'disabled', fullPage: true });
        await modal.locator('.modal-footer .btn-primary-modern').click();
        await modal.waitFor({ state: 'hidden', timeout: 20000 });
        await page.reload({ waitUntil: 'domcontentloaded' });
        await row.waitFor();
        assert.ok((await row.innerText()).includes('lashkargah'));
        await page.screenshot({ path: path.join(out, 'location-edit-saved.png'), animations: 'disabled', fullPage: true });
        fs.writeFileSync(path.join(out, 'location-edit-results.json'), JSON.stringify({
            importedHawalaEdit: 'passed', differentlyNamedAssignedAccount: 'passed', validation: 'passed', saveAndReload: 'passed'
        }, null, 2));
        console.log('Imported payment location edit passed: assigned account selected, saved and reloaded.');
    } catch (error) {
        if (page) { console.error(await page.locator('body').innerText()); await page.screenshot({ path: path.join(out, 'location-edit-failure.png'), fullPage: true }); }
        throw error;
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
