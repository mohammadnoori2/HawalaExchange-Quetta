import test from 'node:test';
import assert from 'node:assert/strict';

class Surface {
    handlers = new Map();
    addEventListener(name, callback, options = {}) {
        const list = this.handlers.get(name) || [];
        list.push({ callback, signal: options.signal }); this.handlers.set(name, list);
    }
    fire(name, props = {}) {
        const event = { preventDefault() {}, stopPropagation() {}, ...props };
        for (const handler of this.handlers.get(name) || [])
            if (!handler.signal?.aborted) handler.callback(event);
    }
}
let table, container, cells, frames, nextFrame, calls, header;
globalThis.window = new Surface();
globalThis.document = new Surface();
document.body = { style: { userSelect: 'text' } };
document.getElementById = () => table;
document.elementFromPoint = (_x, y) => cells[Math.floor((y - 100 + container.scrollTop) / 24)] || null;
globalThis.getComputedStyle = () => ({ overflowY: 'auto' });
globalThis.requestAnimationFrame = callback => { frames.set(++nextFrame, callback); return nextFrame; };
globalThis.cancelAnimationFrame = id => frames.delete(id);
const { mount, unmount, share } = await import('../../HawalaExchange.Web/wwwroot/js/record-selection.js');

function setup(ids = [1, 2, 3, 4, 5, 6], selected = []) {
    calls = []; frames = new Map(); nextFrame = 0;
    container = { scrollTop: 0, clientHeight: 72, scrollHeight: 144, parentElement: null, getBoundingClientRect: () => ({ top: 100, bottom: 172 }) };
    table = new Surface(); table.parentElement = container;
    cells = ids.map(id => {
        const input = { checked: false };
        const row = { style: {}, classList: { toggle() {} }, setAttribute() {}, querySelector: () => cell };
        const cell = { dataset: { recordId: String(id) }, querySelector: () => input,
            closest: name => name === 'tr' ? row : cell };
        return cell;
    });
    table.querySelectorAll = () => cells;
    header = { checked: false, indeterminate: false, closest: selector => selector === '.record-selection-all' ? header : null };
    table.querySelector = () => header;
    table.contains = cell => cells.includes(cell);
    mount('test', { invokeMethodAsync: async (...args) => calls.push(args) }, selected, ids, 'version');
}
const settle = () => new Promise(resolve => setImmediate(resolve));
function click(index, extra = {}) {
    table.fire('mousedown', { target: cells[index], button: 0, clientX: 10, clientY: 105 + index * 24, ...extra });
    document.fire('mouseup');
}
test.afterEach(() => unmount('test'));

test('left drag selects a range and restores text selection on mouse up', async () => {
    setup();
    table.fire('mousedown', { target: cells[0], button: 0, clientX: 10, clientY: 105 });
    document.fire('mousemove', { buttons: 1, clientX: 10, clientY: 153 });
    assert.deepEqual(cells.map(x => x.querySelector().checked), [true, true, true, false, false, false]);
    document.fire('mouseup'); await settle();
    assert.deepEqual(calls[0], ['UpdateVisibleSelection', [1, 2, 3], [], 'version']);
    assert.equal(document.body.style.userSelect, 'text');
});
test('scroll while held extends selection; ordinary scroll does not select', async () => {
    setup(); container.scrollTop = 24; document.fire('scroll');
    assert.equal(cells.filter(x => x.querySelector().checked).length, 0);
    container.scrollTop = 0;
    table.fire('mousedown', { target: cells[0], button: 0, clientX: 10, clientY: 105 });
    container.scrollTop = 48; document.fire('scroll');
    document.fire('mouseup'); await settle();
    assert.deepEqual(calls[0][1], [1, 2, 3]);
});
test('edge auto-scroll continues the drag selection', () => {
    setup();
    table.fire('mousedown', { target: cells[0], button: 0, clientX: 10, clientY: 169 });
    const frame = frames.values().next().value; frames.clear(); frame();
    assert.equal(container.scrollTop, 12);
    assert.equal(cells[3].querySelector().checked, true);
});
test('shift range and ctrl toggle preserve independent selections', async () => {
    setup(); click(0); await settle();
    click(3, { shiftKey: true }); await settle();
    assert.deepEqual(cells.map(x => x.querySelector().checked), [true, true, true, true, false, false]);
    click(1, { ctrlKey: true }); await settle();
    assert.equal(cells[1].querySelector().checked, false);
    assert.equal(cells[3].querySelector().checked, true);
});
test('selection messages are chunked below the SignalR inbound limit', async () => {
    setup(Array.from({ length: 550 }, (_, index) => index + 1));
    click(0); await settle(); click(549, { shiftKey: true }); await settle();
    assert.ok(calls.every(x => x[1].length <= 200 && x[2].length <= 200));
});
test('whatsapp and telegram URLs encode user text and do not auto-send', () => {
    const opened = [];
    window.open = (url, target) => { opened.push({ url, target }); return {}; };
    share('whatsapp', 'Name & 100 AFN'); share('telegram', 'Name & 100 AFN');
    assert.ok(opened[0].url.startsWith('https://wa.me/?text='));
    assert.ok(opened[1].url.includes('text=Name%20%26%20100%20AFN'));
    assert.equal(opened[0].target, '_blank');
});

test('header selects only this page, shows partial state, and preserves other pages when clearing', async () => {
    setup([1, 2, 3], [99, 1]);
    assert.equal(header.indeterminate, true); assert.equal(header.checked, false);
    table.fire('click', { target: header }); await settle();
    assert.equal(header.checked, true); assert.equal(header.indeterminate, false);
    assert.deepEqual(calls[0][1], [2, 3]);
    table.fire('click', { target: header }); await settle();
    assert.equal(header.checked, false);
    assert.deepEqual(calls[1][2], [99, 1, 2, 3].filter(x => x !== 99));
    mount('test', { invokeMethodAsync: async (...args) => calls.push(args) }, [99], [99], 'version');
    assert.equal(header.checked, true);
});
