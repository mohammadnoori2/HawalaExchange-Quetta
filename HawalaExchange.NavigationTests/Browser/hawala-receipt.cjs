// This test executes one pending hawala. Use ONLY the disposable receipt-review fixture.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const assert = require('node:assert/strict');
if (!process.env.HAWALA_UI_CONTEXT || process.env.HAWALA_RECEIPT_DISPOSABLE !== 'true')
    throw new Error('A disposable receipt fixture and HAWALA_RECEIPT_DISPOSABLE=true are required.');
const info = JSON.parse(fs.readFileSync(process.env.HAWALA_UI_CONTEXT));
if (!info.database?.startsWith('HawalaExchangeQuettaPerformanceTests_')) throw new Error('Unsafe test database.');
const out = process.env.HAWALA_UI_OUTPUT || path.dirname(process.env.HAWALA_UI_CONTEXT);

(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
        await page.route('https://fonts.googleapis.com/**', r => r.abort());
        await page.route('https://fonts.gstatic.com/**', r => r.abort());
        await page.goto((process.env.HAWALA_UI_ORIGIN || 'http://127.0.0.1:5112') + '/Account/Login?ReturnUrl=%2Fhawala-transactions');
        await page.locator('[name="Input.UserName"]').fill(info.user);
        await page.locator('[name="Input.Password"]').fill(info.password);
        await page.locator('button[type=submit]').click();
        await page.waitForURL('**/hawala-transactions');
        const row = page.locator('tbody tr').filter({ hasText: '26001' });
        await row.waitFor();
        await page.evaluate(() => {
            window.receiptPrints = [];
            window.receiptPopups = 0;
            window.open = () => { window.receiptPopups++; return null; };
            const original = window.hawalaTools.printElement;
            window.hawalaTools.printElement = function (...args) {
                original.apply(this, args);
                const frame = [...document.querySelectorAll('iframe')].find(x => x.title === 'پیش‌نمایش رسید اجرای حواله' && !x.dataset.observed);
                if (frame) {
                    frame.dataset.observed = 'true';
                    frame.contentWindow.print = () => window.receiptPrints.push(frame.contentDocument.body.innerText);
                }
            };
        });
        // The pay action may be configured as a quick action or be inside the menu.
        const pay = row.locator('[data-action-key="pay"]');
        if (!await pay.isVisible()) await row.locator('.operation-dot-btn').click();
        await pay.click();
        const modal = page.locator('.modal.show').filter({ has: page.locator('input[placeholder="نام، کد مشتری، تلفن یا شماره تذکره"]') });
        await modal.locator('input[type=search]').fill('TEST-ID-2601');
        await modal.getByRole('button').filter({ hasText: 'Receipt Receiver' }).click();
        await modal.getByRole('button', { name: 'چاپ رسید اجرا' }).click();
        await page.waitForFunction(() => window.receiptPrints.length === 1);
        const verify = text => {
            for (const value of ['TEST-ID-2601', '0700123456', 'Kabul Test Address', 'Receiver Father', 'Receipt Receiver', '26001'])
                assert.ok(text.includes(value), `Receipt missing ${value}`);
        };
        verify(await page.evaluate(() => window.receiptPrints[0]));
        await page.evaluate(() => [...document.querySelectorAll('iframe[data-observed]')].forEach(x => x.contentWindow.dispatchEvent(new Event('afterprint'))));
        await modal.locator('input[placeholder="جستجوی حساب پرداخت‌کننده..."]').fill('Receipt Test Cash');
        await page.locator('.searchable-dropdown-item').filter({ hasText: 'Receipt Test Cash' }).click();
        await page.waitForFunction(() => document.querySelector('input[placeholder="جستجوی حساب پرداخت‌کننده..."]')?.value.includes('(RECEIPT-CASH)'));
        await page.waitForTimeout(300);
        await modal.getByRole('button', { name: /تأیید پرداخت/ }).click();
        await page.waitForTimeout(1500);
        const errors = await page.locator('.alert-danger').allTextContents();
        assert.equal(errors.length, 0, errors.join('\n'));
        try { await modal.waitFor({ state: 'hidden', timeout: 10000 }); }
        catch (error) {
            console.error(await page.locator('.page-wrapper, .modal.show').allTextContents());
            await page.screenshot({ path: path.join(out, 'receipt-failure.png'), fullPage: true });
            throw error;
        }
        await page.waitForFunction(() => window.receiptPrints.length === 2);
        verify(await page.evaluate(() => window.receiptPrints[1]));
        assert.equal(await page.evaluate(() => window.receiptPopups), 0);
        const frame = page.frames().find(x => x !== page.mainFrame() && x.url() === 'about:blank');
        const receiptHtml = await frame.evaluate(() => {
            const html = document.documentElement.cloneNode(true);
            html.querySelectorAll('script').forEach(x => x.remove());
            return '<!doctype html>' + html.outerHTML;
        });
        const preview = await browser.newPage({ viewport: { width: 420, height: 900 } });
        await preview.setContent(receiptHtml);
        await preview.screenshot({ path: path.join(out, 'execution-receipt.png'), animations: 'disabled', fullPage: true });
        await preview.close();
        await page.waitForTimeout(500);
        assert.equal(await page.evaluate(() => window.receiptPrints.length), 2, 'Unexpected repeated print');
        await page.evaluate(() => [...document.querySelectorAll('iframe[data-observed]')].forEach(x => x.contentWindow.dispatchEvent(new Event('afterprint'))));
        assert.equal(await page.locator('iframe[data-observed]').count(), 0, 'Print frame was not cleaned up');
        fs.writeFileSync(path.join(out, 'receipt-result.json'), JSON.stringify({
            selectedCustomerDetails: 'passed', manualReceipt: 'passed', automaticReceiptAfterPayment: 'passed',
            popupIndependent: 'passed', singleAutomaticPrint: 'passed', frameCleanup: 'passed'
        }, null, 2));
        console.log('Receipt browser flow passed.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
