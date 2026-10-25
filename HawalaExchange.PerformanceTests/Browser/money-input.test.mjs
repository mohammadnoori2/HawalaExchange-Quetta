import test from 'node:test';
import assert from 'node:assert/strict';
const { formatMoneyDraft, installMoneyInputs } = await import('../../HawalaExchange.Web/wwwroot/js/money-input.js');

test('formatting is exact and normalizes Persian/Arabic digits without numeric conversion', () => {
    assert.equal(formatMoneyDraft('۲۱۵۶۰٫۵', 8), '21,560.5');
    assert.equal(formatMoneyDraft('١٢٣٤٥٦٧', 0), '1,234,567');
    assert.equal(formatMoneyDraft('79228162514264337593543950335', 8), '79,228,162,514,264,337,593,543,950,335');
    assert.equal(formatMoneyDraft('', 8), '');
    assert.equal(formatMoneyDraft('-', 8), '-');
    assert.equal(formatMoneyDraft('12.', 8), '12.');
    assert.equal(formatMoneyDraft('-1234.123456789', 8), '-1,234.12345678');
});

function setup(initial = '1,234', nullable = false) {
    const handlers = new Map(), changes = [], observers = [];
    const input = { dataset: { moneyInput: '', moneyValue: initial, moneyDecimals: '0', moneyNullable: String(nullable) },
        value: '', selectionStart: 0, selectionEnd: 0, disabled: false, readOnly: false,
        matches: selector => selector === 'input[data-money-input]', querySelectorAll: () => [],
        setSelectionRange(start, end) { this.selectionStart = start; this.selectionEnd = end; },
        dispatchEvent(e) { changes.push(this.value); root.fire(e.type, this); } };
    const root = { activeElement: null, querySelectorAll: () => [input],
        addEventListener(type, fn) { handlers.set(type, fn); }, removeEventListener(type) { handlers.delete(type); },
        fire(type, target = input, extras = {}) { handlers.get(type)?.({ target, ...extras }); } };
    const stop = installMoneyInputs(root, class { constructor(fn) { this.fn = fn; observers.push(this); } observe() {} disconnect() { this.disconnected = true; } });
    const type = (text, caret = text.length) => { input.value = text; input.setSelectionRange(caret, caret); root.fire('input'); };
    return { root, input, observers, type, changes, stop };
}

test('typing formats locally with no server change per keystroke and preserves caret', () => {
    const s = setup(); assert.equal(s.input.value, '1,234');
    s.root.activeElement = s.input; s.root.fire('focusin');
    s.type('192,345', 2);
    assert.equal(s.input.value, '192,345'); assert.equal(s.input.selectionStart, 2);
    s.type('1923,456', 4);
    assert.equal(s.input.value, '1,923,456'); assert.equal(s.input.selectionStart, 5);
    assert.deepEqual(s.changes, []);
});

test('action buttons flush before click even when focus is retained', () => {
    const s = setup(); s.root.activeElement = s.input; s.type('7000');
    s.root.fire('click', { closest: () => ({}) }); assert.deepEqual(s.changes, ['7,000']);
});

test('composition, pasted fractions, added fields and cleanup are supported', () => {
    const s = setup(); s.input.value = '۱۲۳۴٫۵۶۷'; s.input.dataset.moneyDecimals = '2';
    s.input.setSelectionRange(8, 8); s.root.fire('input', s.input, { isComposing: true });
    assert.equal(s.input.value, '۱۲۳۴٫۵۶۷');
    s.root.fire('compositionend'); assert.equal(s.input.value, '1,234.56');
    s.root.fire('change'); s.input.dataset.moneyValue = '8,000'; s.input.dataset.moneyRevision = '1';
    s.observers[0].fn([{ type: 'childList', addedNodes: [s.input] }]); assert.equal(s.input.value, '8,000');
    s.stop(); assert.equal(s.observers[0].disconnected, true);
    s.input.value = '1234'; s.root.fire('input'); assert.equal(s.input.value, '1234');
});

test('empty input stays empty and Enter flushes the latest value before submission', () => {
    const s = setup(); s.root.activeElement = s.input; s.root.fire('focusin'); s.type('');
    assert.equal(s.input.value, '');
    s.root.fire('keydown', s.input, { key: 'Enter', isComposing: false });
    assert.deepEqual(s.changes, ['']);
    s.root.fire('keydown', s.input, { key: 'Enter', isComposing: false });
    assert.equal(s.changes.length, 1);
});

test('late server values never overwrite an actively edited field', () => {
    const s = setup(); s.root.activeElement = s.input; s.root.fire('focusin'); s.type('45678');
    s.input.dataset.moneyValue = '2,000';
    s.observers[0].fn([{ type: 'attributes', target: s.input }]);
    assert.equal(s.input.value, '45,678');
    s.root.activeElement = null; s.root.fire('change');
    s.input.dataset.moneyValue = '45,678';
    s.input.dataset.moneyRevision = '1';
    s.observers[0].fn([{ type: 'attributes', target: s.input }]);
    assert.equal(s.input.value, '45,678');
});

test('submit flushes a focused edit and disabled fields are untouched', () => {
    const s = setup(); s.root.activeElement = s.input; s.type('5000'); s.root.fire('submit', {});
    assert.deepEqual(s.changes, ['5,000']);
    s.input.disabled = true; s.type('12'); assert.equal(s.input.value, '12');
});

test('external model updates reset an idle input and zero clears on focus only for required money', () => {
    const s = setup('0'); s.root.activeElement = s.input; s.root.fire('focusin'); assert.equal(s.input.value, '');
    s.root.activeElement = null; s.root.fire('focusout');
    s.input.dataset.moneyValue = '9,000'; s.input.dataset.moneyRevision = '1';
    s.observers[0].fn([{ type: 'attributes', target: s.input }]); assert.equal(s.input.value, '9,000');
    const optional = setup('0', true); optional.root.fire('focusin'); assert.equal(optional.input.value, '0');
});

test('older commit acknowledgements do not replace newer edits after blur', () => {
    const s = setup(); s.type('2000'); s.root.fire('change'); s.type('3000'); s.root.fire('change');
    s.input.dataset.moneyValue = '2,000'; s.input.dataset.moneyRevision = '1';
    s.observers[0].fn([{ type: 'attributes', target: s.input }]); assert.equal(s.input.value, '3,000');
    s.input.dataset.moneyValue = '3,000'; s.input.dataset.moneyRevision = '2';
    s.observers[0].fn([{ type: 'attributes', target: s.input }]); assert.equal(s.input.value, '3,000');
});
