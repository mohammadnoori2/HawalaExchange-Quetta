import test from 'node:test';
import assert from 'node:assert/strict';
import { installCommissionPreviewGuard } from '../../HawalaExchange.Web/wwwroot/js/commission-preview.js';

function setup() {
    const handlers = {}, observers = [];
    const post = { disabled: false }, calculate = { disabled: true };
    const form = { dataset: { commissionPreviewVersion: '1', commissionBusy: 'false', commissionCanPost: 'true' },
        querySelector: s => s === '[data-commission-post]' ? post : calculate };
    const input = { disabled: false, closest: s => s === '[data-commission-rate]' ? input : form };
    const root = { addEventListener: (name, fn) => { handlers[name] = fn; }, removeEventListener: name => delete handlers[name] };
    const stop = installCommissionPreviewGuard(root, class {
        constructor(fn) { observers.push(fn); } observe() {} disconnect() {}
    });
    return { post, calculate, form, input, handlers, observers, stop };
}

test('editing a rate blocks posting immediately and enables recalculation', () => {
    const s = setup();
    s.handlers.input({ target: s.input });
    assert.equal(s.post.disabled, true);
    assert.equal(s.calculate.disabled, false);
    // An older server render must not reopen the posting button.
    s.post.disabled = false;
    s.observers[0]();
    assert.equal(s.post.disabled, true);
});

test('only a new successful preview releases the browser guard', () => {
    const s = setup();
    s.handlers.input({ target: s.input });
    let blocked = false;
    s.handlers.click({ target: { closest: () => s.post }, preventDefault() { blocked = true; }, stopImmediatePropagation() {} });
    assert.equal(blocked, true);
    s.form.dataset.commissionPreviewVersion = '2';
    s.post.disabled = false;
    s.observers[0]();
    assert.equal(s.post.disabled, false);
    blocked = false;
    s.handlers.click({ target: { closest: () => s.post }, preventDefault() { blocked = true; }, stopImmediatePropagation() {} });
    assert.equal(blocked, false);
    s.stop();
    assert.deepEqual(Object.keys(s.handlers), []);
});

test('while calculating, inputs do not enable duplicate calculation requests', () => {
    const s = setup();
    s.form.dataset.commissionBusy = 'true';
    s.handlers.input({ target: s.input });
    assert.equal(s.post.disabled, true);
    assert.equal(s.calculate.disabled, true);
});

test('unrelated inputs and buttons are unaffected', () => {
    const s = setup();
    s.handlers.input({ target: {} });
    s.handlers.input({ target: { closest: () => null } });
    s.handlers.click({ target: {} });
    assert.equal(s.post.disabled, false);
    s.handlers.input({ target: s.input });
    s.handlers.click({ target: { closest: () => ({}) }, preventDefault() { assert.fail('unrelated button'); } });
});

test('empty or invalid recalculation cannot enable posting', () => {
    const s = setup();
    s.handlers.input({ target: s.input });
    s.form.dataset.commissionPreviewVersion = '2';
    s.form.dataset.commissionCanPost = 'false';
    s.observers[0]();
    assert.equal(s.post.disabled, true);
    assert.equal(s.calculate.disabled, false);
});

test('navigation discards the guard for a removed commission page', () => {
    const s = setup();
    s.handlers.input({ target: s.input });
    s.form.isConnected = false;
    s.observers[0]();
    s.handlers.click({ target: { closest: () => s.post }, preventDefault() { assert.fail('detached page'); } });
});
