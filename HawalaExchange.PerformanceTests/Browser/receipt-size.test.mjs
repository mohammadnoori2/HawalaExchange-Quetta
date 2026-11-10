import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const script = fs.readFileSync(new URL('../../HawalaExchange.Web/wwwroot/js/hawala-tools.js', import.meta.url), 'utf8');
function print(width) {
    let html = '';
    const popup = { document: { open() {}, write(value) { html = value; }, close() {} } };
    const element = { cloneNode: () => ({ querySelectorAll: () => [], innerHTML: '<article class="hawala-receipt">رسید</article>' }) };
    const context = { window: { open: () => popup }, document: { getElementById: () => element, addEventListener() {} }, setTimeout() {} };
    vm.runInNewContext(script, context);
    context.window.hawalaTools.printElement('receipt', width);
    return html;
}
test('default receipt matches 72mm paper and 64mm printable area without clipping', () => {
    const html = print();
    assert.match(html, /width: 72mm/);
    assert.match(html, /width: 64mm/);
    assert.match(html, /padding: 0\.45mm 1mm/);
    assert.match(html, /min-height: 0/);
    assert.match(html, /font-size: 18px/);
    assert.match(html, /white-space: nowrap/);
    assert.match(html, /padding: 0 2mm 0/);
    assert.ok(!html.includes('transform: scaleY'));
    assert.ok(!html.includes('overflow: hidden'));
    assert.ok(html.includes('رسید'));
    assert.ok(html.includes("'@page { size: 72mm ' + heightMm"));
    assert.ok(!html.includes('size: 72mm auto'));
});
test('explicit 58mm paper remains supported', () => {
    const html = print(58); assert.match(html, /width: 58mm/); assert.match(html, /width: 48mm/);
});
test('ready receipt button prints locally and does not dispatch a server click', () => {
    let onClick, invoked, stopped = false, prevented = false;
    const context = { window: {}, document: { addEventListener(name, fn) { if (name === 'click' && !onClick) onClick = fn; }, getElementById: () => ({}) } };
    vm.runInNewContext(script, context);
    context.window.hawalaTools.printElement = (...args) => { invoked = args; };
    onClick({ target: { closest: () => ({ disabled: false, getAttribute: () => 'ready-receipt' }) },
        preventDefault() { prevented = true; }, stopImmediatePropagation() { stopped = true; } });
    assert.deepEqual(invoked, ['ready-receipt', 72, true]);
    assert.ok(stopped); assert.ok(prevented);
});
test('print page length follows all content rather than clipping long receipts', () => {
    const html = print();
    const inline = html.match(/<script>([\s\S]*?)<\/script>/)[1];
    let height = 200, printed = 0;
    const style = { textContent: '' };
    const context = { window: { focus() {}, print() { printed++; }, addEventListener() {} }, document: {
        body: { getBoundingClientRect: () => ({ height }) }, getElementById: () => style } };
    vm.runInNewContext(inline, context);
    context.printNow(); assert.equal(style.textContent, '@page { size: 72mm 54mm; margin: 0; }');
    height = 1000; context.printNow();
    assert.equal(style.textContent, '@page { size: 72mm 266mm; margin: 0; }');
    assert.equal(printed, 2);
});
test('printing starts after DOM readiness without the old 800ms delay or waiting for window load', async () => {
    const inline = print().match(/<script>([\s\S]*?)<\/script>/)[1];
    let ready, printed = 0, budget = 0;
    const context = {
        window: { addEventListener(name, fn) { assert.equal(name, 'DOMContentLoaded'); ready = fn; },
            focus() {}, print() { printed++; }, setTimeout(fn) { fn(); } },
        document: { images: [], body: { getBoundingClientRect: () => ({ height: 200 }) }, getElementById: () => ({ textContent: '' }) },
        setTimeout(fn, ms) { budget = ms; return 1; }, clearTimeout() {}
    };
    vm.runInNewContext(inline, context);
    ready(); ready();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(printed, 1); assert.equal(budget, 200);
    assert.ok(!inline.includes('window.onload'));
    assert.ok(!inline.includes('800'));
});
test('a stalled logo is stopped at the 200ms budget and cannot hold the print dialog', async () => {
    const inline = print().match(/<script>([\s\S]*?)<\/script>/)[1];
    let ready, deadline, printed = 0, cancelled = false;
    const image = { complete: false, addEventListener() {}, removeAttribute(name) { if (name === 'src') cancelled = true; } };
    const parent = { setTimeout(fn) { fn(); } };
    const context = {
        window: { parent, addEventListener(name, fn) { ready = fn; }, focus() {}, print() { printed++; }, requestAnimationFrame() { throw new Error('Offscreen RAF must not be used'); } },
        document: { images: [image], body: { getBoundingClientRect: () => ({ height: 200 }) }, getElementById: () => ({ textContent: '' }) },
        setTimeout(fn, ms) { assert.equal(ms, 200); deadline = fn; return 1; }, clearTimeout() {}
    };
    vm.runInNewContext(inline, context);
    ready(); assert.equal(printed, 0); deadline();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(printed, 1); assert.ok(cancelled); assert.equal(image.hidden, true);
});
test('receipt previews and print callers use the same 72mm paper', () => {
    const css = fs.readFileSync(new URL('../../HawalaExchange.Web/wwwroot/styles/components/shared/HawalaReceipt.css', import.meta.url), 'utf8');
    assert.ok(!css.includes('80mm')); assert.ok(css.includes('width: 72mm'));
    for (const path of ['Shared/HawalaExecutionReceiptPrint.razor', 'Shared/Modals/CreateHawalaModal.razor', 'Pages/HawalaTransactions/HawalaTransactions.razor', 'Pages/Correspondents/CorrespondentHawalaPanel.razor']) {
        const source = fs.readFileSync(new URL('../../HawalaExchange.Web/Components/' + path, import.meta.url), 'utf8');
        assert.ok(!/"hawalaTools\.printElement"[^\n]*, 80\b/.test(source));
    }
});
