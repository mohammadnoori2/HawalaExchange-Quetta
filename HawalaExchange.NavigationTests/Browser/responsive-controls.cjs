const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const origin = process.env.HAWALA_UI_ORIGIN || 'http://127.0.0.1:5112';
const output = process.env.HAWALA_UI_OUTPUT || path.join(require('node:os').tmpdir(), 'HawalaResponsiveAudit');
const info = JSON.parse(fs.readFileSync(process.env.HAWALA_UI_CONTEXT, 'utf8'));

async function main() {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    const checks = [];
    try {
        const context = await browser.newContext({ hasTouch: true });
        for (const host of ['cdn.jsdelivr.net', 'fonts.googleapis.com', 'fonts.gstatic.com']) {
            await context.route(`https://${host}/**`, route => route.abort());
        }
        if (process.env.HAWALA_UI_CHART_SCRIPT) {
            await context.route('https://cdn.jsdelivr.net/npm/chart.js@4.4.7/dist/chart.umd.min.js',
                route => route.fulfill({ path: process.env.HAWALA_UI_CHART_SCRIPT, contentType: 'application/javascript' }));
        }
        const page = await context.newPage();
        await page.goto(`${origin}/Account/Login?ReturnUrl=%2Faccounts`);
        for (const width of [320, 375, 768, 1024, 1440]) {
            await page.setViewportSize({ width, height: 812 });
            const fields = await page.locator('.login-page .form-control-modern').evaluateAll(elements => elements.map(element => {
                const box = element.getBoundingClientRect();
                return { left: box.left, right: box.right, fontSize: parseFloat(getComputedStyle(element).fontSize) };
            }));
            assert.ok(fields.length >= 2);
            assert.ok(fields.every(field => field.left >= 0 && field.right <= width), `Login fields fit at ${width}px`);
            if (width <= 1024) assert.ok(fields.every(field => field.fontSize >= 16), 'Login input text remains readable');
            checks.push({ viewport: width, loginFields: 'passed' });
        }
        await page.locator('[name="Input.UserName"]').fill(info.user);
        await page.locator('[name="Input.Password"]').fill(info.password);
        await page.locator('button[type="submit"]').click();
        await page.waitForURL('**/accounts');
        await page.locator('.accounts-page-compact tbody tr').first().waitFor();

        for (const width of [320, 375, 768, 1024]) {
            await page.setViewportSize({ width, height: 812 });
            const toggle = page.locator('#mobile-nav-toggle');
            await toggle.focus();
            await toggle.press('Space');
            await page.waitForFunction(() => document.querySelector('.app-main').inert);
            const box = await toggle.boundingBox();
            assert.ok(box.width >= 44 && box.height >= 44, `Menu target at ${width}px`);
            assert.equal(await toggle.getAttribute('aria-expanded'), 'true');
            assert.equal(await page.locator('#app-sidebar').evaluate(element => element.inert), false);
            await page.keyboard.press('Shift+Tab');
            assert.equal(await page.evaluate(() => document.activeElement.closest('.app-main') !== null), false);
            if (width === 375) await page.screenshot({ path: path.join(output, 'responsive-menu-375.png'), animations: 'disabled' });
            await page.keyboard.press('Escape');
            assert.equal(await toggle.isChecked(), false);
            assert.equal(await page.locator('.app-main').evaluate(element => element.inert), false);
            assert.equal(await page.evaluate(() => document.activeElement.id), 'mobile-nav-toggle');
            checks.push({ viewport: width, menuKeyboardAndFocus: 'passed' });
        }

        for (const route of ['/accounts', '/customers', '/correspondents', '/hawala-transactions', '/transfers', '/deposit-withdraw', '/money-exchange']) {
            await page.setViewportSize({ width: 375, height: 812 });
            await page.goto(origin + route, { waitUntil: 'domcontentloaded' });
            await page.locator('.app-main').waitFor();
            await page.waitForFunction(() => document.querySelector('.app-content')?.children.length && !document.querySelector('.loading-state'));
            const create = page.locator('.page-wrapper .btn-primary-modern, .page-wrapper .page-actions .btn-primary').first();
            if (!await create.count()) continue;
            await create.click();
            const modal = page.locator('.modal.show').first();
            await modal.waitFor();
            await page.waitForTimeout(250);
            const geometry = await modal.evaluate(element => {
                const content = element.querySelector('.modal-content');
                const box = content.getBoundingClientRect();
                const offenders = [...element.querySelectorAll('input:not([type=hidden]), select, textarea, .modal-header, .modal-footer')]
                    .filter(field => {
                        const rect = field.getBoundingClientRect();
                        return rect.width && (rect.left < -1 || rect.right > innerWidth + 1);
                    }).map(field => field.className);
                return { left: box.left, right: box.right, width: innerWidth, offenders };
            });
            assert.ok(geometry.left >= -1 && geometry.right <= geometry.width + 1, `${route} dialog fits`);
            assert.deepEqual(geometry.offenders, [], `${route} form fields fit`);
            if (['/accounts', '/hawala-transactions'].includes(route)) {
                await page.screenshot({ path: path.join(output, `responsive-modal${route.replaceAll('/', '-')}-375.png`), animations: 'disabled' });
            }
            const close = modal.locator('.modal-header .btn-close, .modal-header .btn-close-white').first();
            if (await close.count()) {
                await close.click({ timeout: 5000 });
                await modal.waitFor({ state: 'hidden' });
            }
            checks.push({ route, viewport: 375, modalFields: 'passed' });
        }

        await page.goto(`${origin}/accounts`);
        await page.locator('.accounts-page-compact tbody tr').first().waitFor();
        await page.setViewportSize({ width: 812, height: 375 });
        await page.emulateMedia({ reducedMotion: 'reduce', colorScheme: 'dark' });
        assert.equal(await page.locator('#app-sidebar').evaluate(element => getComputedStyle(element).transitionDuration), '0s');
        await page.addStyleTag({ content: 'html { font-size: 200% !important; }' });
        await page.setViewportSize({ width: 375, height: 812 });
        const overflow = await page.locator('.app-main .page-header').evaluate(element => element.scrollWidth > element.clientWidth + 1);
        assert.equal(overflow, false, 'Header remains readable with enlarged text');
        checks.push({ landscapeAndReducedMotionAndLargeText: 'passed' });
        fs.writeFileSync(path.join(output, 'responsive-controls.json'), JSON.stringify(checks, null, 2));
        console.log(JSON.stringify(checks, null, 2));
    } finally {
        await browser.close();
    }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
