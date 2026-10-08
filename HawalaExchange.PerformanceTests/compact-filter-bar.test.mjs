import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const source = readFileSync(new URL('../HawalaExchange.Web/wwwroot/js/compact-filter-bar.js', import.meta.url), 'utf8');
const { fitInlineFields, initialize, dispose } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

assert.equal(fitInlineFields(1200, [280, 160, 38], [180, 180, 180, 180], 150), 2);
assert.equal(fitInlineFields(1600, [280, 160, 38], [180, 180, 180, 180], 150), 4);
assert.equal(fitInlineFields(650, [280, 160, 38], [180, 180, 180, 180], 150), 0);
assert.equal(fitInlineFields(900, [280], [180, Infinity, 180], 150), 1);
assert.equal(fitInlineFields(600, [280], [], 150), 0);

let frames = [], observers = [], listeners = new Set();
globalThis.requestAnimationFrame = fn => { frames.push(fn); return frames.length; };
globalThis.cancelAnimationFrame = () => {};
globalThis.ResizeObserver = class { constructor(fn) { this.fn = fn; this.disconnected = false; observers.push(this); } observe() {} disconnect() { this.disconnected = true; } };
globalThis.MutationObserver = globalThis.ResizeObserver;
globalThis.window = { innerWidth: 1600, addEventListener: (_, fn) => listeners.add(fn), removeEventListener: (_, fn) => listeners.delete(fn) };
globalThis.getComputedStyle = () => ({ display: 'block', columnGap: '8px' });
const element = width => ({ dataset: {}, style: {}, classList: { contains: () => true }, getBoundingClientRect: () => ({ width }) });
const fields = [element(180), element(180), element(180), element(180)];
const group = { children: fields };
const button = element(150);
const main = { clientWidth: 1200, children: [element(280), element(160), group, button, element(38)], querySelector: () => button };
const root = { isConnected: true, dataset: {}, querySelector: selector => selector === '.compact-filter-main' ? main : group };
const flush = () => { const pending = frames; frames = []; pending.forEach(fn => fn()); };
initialize(root); flush();
assert.deepEqual(fields.map(x => x.dataset.inlineVisible), ['true', 'true', 'false', 'false']);
assert.equal(root.dataset.filterOverflow, 'true');
initialize(root); assert.equal(observers.length, 2); // No duplicate observers on re-render.
main.clientWidth = 1600; observers[0].fn(); flush();
assert(fields.every(x => x.dataset.inlineVisible === 'true'));
assert.equal(root.dataset.filterOverflow, 'false');
window.innerWidth = 500; observers[0].fn(); flush();
assert(fields.every(x => x.dataset.inlineVisible === 'false'));
assert.equal(root.dataset.filterOverflow, 'true');
dispose(root);
assert(observers.every(x => x.disconnected));
assert.equal(listeners.size, 0);
console.log('Adaptive filter layout: 5 allocation cases, resize, mobile, initialization and disposal passed.');
