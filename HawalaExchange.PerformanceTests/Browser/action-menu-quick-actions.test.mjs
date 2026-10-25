import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const source = fs.readFileSync(new URL('../../HawalaExchange.Web/wwwroot/js/hawala-tools.js', import.meta.url), 'utf8');
function element() {
    const classes = new Set();
    return { children: [], dataset: {}, disabled: false, isConnected: true, clicks: 0,
        classList: { contains: k => classes.has(k), toggle: (k, on) => on ? classes.add(k) : classes.delete(k) },
        querySelector: () => null, setAttribute() {},
        replaceChildren() { this.children = []; }, appendChild(child) { this.children.push(child); },
        addEventListener(name, fn) { this[name] = fn; }, click() { this.clicks++; } };
}
function setup() {
    const observers = [], frames = new Map(), nodes = new Map(), storage = new Map();
    let next = 0;
    const context = { window: {}, document: { getElementById: id => nodes.get(id), createElement: element },
        localStorage: { getItem: k => storage.get(k) ?? null },
        requestAnimationFrame: fn => { frames.set(++next, fn); return next; },
        cancelAnimationFrame: id => frames.delete(id),
        MutationObserver: class {
            constructor(callback) { this.callback = callback; observers.push(this); }
            observe(target, options) { this.target = target; this.options = options; }
            disconnect() { this.disconnected = true; }
        } };
    vm.runInNewContext(source, context);
    const menu = element(), host = element();
    menu.querySelectorAll = () => menu.children;
    menu.querySelector = () => ({ children: menu.children });
    nodes.set('menu', menu); nodes.set('host', host);
    const sync = () => context.window.hawalaTools.syncOperationMenuQuickActions('menu', 'host', 'customer', ['report']);
    const mutate = () => observers.forEach(o => { if (!o.disconnected) o.callback([]); });
    const flush = () => { const pending = [...frames.values()]; frames.clear(); pending.forEach(fn => fn()); };
    const action = () => Object.assign(element(), { textContent: 'Report', dataset: { actionKey: 'report' } });
    return { menu, host, observers, frames, sync, mutate, flush, action, storage };
}

test('permission-delayed actions appear without opening the menu and observers are not duplicated', () => {
    const s = setup(); s.sync(); s.sync();
    assert.equal(s.observers.length, 1);
    assert.equal(s.observers[0].target, s.menu);
    assert.deepEqual([...s.observers[0].options.attributeFilter], ['disabled']);
    s.menu.children.push(s.action()); s.mutate(); s.mutate();
    assert.equal(s.frames.size, 1); s.flush();
    assert.equal(s.host.children.length, 1);
});

test('disabled and revoked actions cannot execute through stale shortcuts', () => {
    const s = setup(), action = s.action(); s.menu.children.push(action); s.sync();
    const stale = s.host.children[0];
    action.disabled = true; s.mutate(); s.flush();
    assert.equal(s.host.children[0].disabled, true);
    stale.click({ stopPropagation() {} }); assert.equal(action.clicks, 0);
    action.disabled = false; action.isConnected = false; s.menu.children = [];
    stale.click({ stopPropagation() {} }); assert.equal(action.clicks, 0);
    s.mutate(); s.flush(); assert.equal(s.host.children.length, 0);
});

test('stored preferences are preserved and detached menus stop observing', () => {
    const s = setup(); s.storage.set('hawala:action-menu:customer', '[]');
    s.menu.children.push(s.action()); s.sync(); assert.equal(s.host.children.length, 0);
    s.mutate(); s.menu.isConnected = false; s.flush();
    assert.equal(s.observers[0].disconnected, true);
});
