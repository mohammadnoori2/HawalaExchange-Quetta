import { refreshMoneyPreview } from './money-preview.js';
// Drafts belong to the browser: no per-key server rendering or floating-point arithmetic.
const selector = 'input[data-money-input]';
const normalize = text => String(text ?? '').replace(/[۰-۹٠-٩]/g, c =>
    String(c.charCodeAt(0) - (c >= '۰' ? 0x6f0 : 0x660))).replace(/[,٬\s]/g, '').replace(/٫/g, '.');

export function formatMoneyDraft(text, decimals = 8) {
    const raw = normalize(text);
    if (!raw) return '';
    const negative = raw.startsWith('-');
    const unsigned = negative ? raw.slice(1) : raw;
    const dot = unsigned.indexOf('.');
    let integer = (dot < 0 ? unsigned : unsigned.slice(0, dot)).replace(/[^0-9]/g, '');
    const fraction = dot < 0 ? '' : unsigned.slice(dot + 1).replace(/[^0-9]/g, '').slice(0, Math.max(0, Math.min(8, decimals)));
    if (!integer && dot < 0) return negative ? '-' : '';
    integer ||= '0';
    return (negative ? '-' : '') + integer.replace(/\B(?=(\d{3})+(?!\d))/g, ',') +
        (dot >= 0 && decimals > 0 ? '.' + fraction : '');
}

export function installMoneyInputs(root = document, Observer = MutationObserver) {
    const states = new WeakMap();
    const isMoney = input => input?.matches?.(selector);
    function stateFor(input) {
        let state = states.get(input);
        if (!state) {
            input.value = input.dataset.moneyValue || '';
            state = { committed: input.value, dirty: false, revision: Number(input.dataset.moneyRevision || 0) };
            states.set(input, state);
        }
        return state;
    }
    function synchronize(input) {
        if (!isMoney(input)) return;
        const state = stateFor(input);
        if (root.activeElement === input || state.dirty || Number(input.dataset.moneyRevision || 0) < state.revision) return;
        input.value = input.dataset.moneyValue || '';
        state.committed = input.value;
        delete input.dataset.moneyPreview;
        refreshMoneyPreview(input);
    }
    function scan(node) {
        if (isMoney(node)) synchronize(node);
        node.querySelectorAll?.(selector).forEach(synchronize);
    }
    scan(root);
    const observer = new Observer(records => {
        for (const record of records) {
            if (record.type === 'attributes') synchronize(record.target);
            else record.addedNodes?.forEach(scan);
        }
    });
    observer.observe(root, { childList: true, subtree: true, attributes: true,
        attributeFilter: ['data-money-value', 'data-money-revision'] });
    const handlers = [];
    function on(type, callback) { root.addEventListener(type, callback, true); handlers.push([type, callback]); }
    const editable = input => isMoney(input) && !input.disabled && !input.readOnly;
    function flush(input) {
        if (!editable(input)) return;
        const state = stateFor(input);
        if (state.dirty || (input.dataset.moneyPreview !== 'true' && input.value !== state.committed))
            input.dispatchEvent(new Event('change', { bubbles: true }));
    }
    on('focusin', ({ target }) => {
        if (!editable(target)) return;
        const state = stateFor(target);
        if (target.dataset.moneyNullable === 'false' && /^0(?:\.0+)?$/.test(normalize(target.value))) {
            target.value = ''; state.dirty = true;
        }
    });
    on('input', event => {
        const input = event.target;
        if (!editable(input) || event.isComposing) return;
        const state = stateFor(input);
        const raw = input.value;
        const start = input.selectionStart ?? raw.length, end = input.selectionEnd ?? start;
        const formatted = formatMoneyDraft(raw, Number(input.dataset.moneyDecimals) || 8);
        const position = offset => {
            const count = normalize(raw.slice(0, offset)).replace(/[^0-9.\-]/g, '').length;
            if (!count) return 0;
            let seen = 0;
            for (let i = 0; i < formatted.length; i++)
                if (formatted[i] !== ',' && ++seen === count) return i + 1;
            return formatted.length;
        };
        input.value = formatted;
        delete input.dataset.moneyPreview;
        input.setSelectionRange(position(start), position(end));
        state.dirty = true;
        refreshMoneyPreview(input, true);
    });
    on('compositionend', event => handlers.find(([type]) => type === 'input')[1](event));
    on('change', ({ target }) => {
        if (!editable(target)) { refreshMoneyPreview(target); return; }
        const state = stateFor(target);
        state.committed = target.value;
        state.dirty = false;
        state.revision = Math.max(state.revision, Number(target.dataset.moneyRevision || 0)) + 1;
    });
    on('focusout', ({ target }) => { flush(target); synchronize(target); });
    on('keydown', event => { if (event.key === 'Enter' && !event.isComposing) flush(event.target); });
    // Some action buttons prevent focus/blur. Commit before their Blazor click handler too.
    on('click', event => {
        if (event.target?.closest?.('button, a, [role="button"], input[type="submit"]')) flush(root.activeElement);
    });
    // Before Blazor's bubbling submit handler, including Enter without blur.
    on('submit', () => flush(root.activeElement));
    return () => { observer.disconnect(); handlers.forEach(([type, callback]) => root.removeEventListener(type, callback, true)); };
}

if (typeof document !== 'undefined') installMoneyInputs();
