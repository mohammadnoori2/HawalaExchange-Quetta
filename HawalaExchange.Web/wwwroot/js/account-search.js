// Query text and caret belong to the browser. Only selection changes go to Blazor.
const selector = '[data-account-search]';
const states = new WeakMap();
let active = null;

function stateFor(root) {
    let s = states.get(root);
    if (!s) {
        s = { root, input: root.querySelector('[data-account-query]'), menu: root.querySelector('[data-account-results]'),
            commit: root.querySelector('[data-account-commit]'), revision: Number(root.dataset.accountRevision || 0), editing: false };
        states.set(root, s);
    }
    if (s.json !== root.dataset.accountOptions) {
        s.json = root.dataset.accountOptions;
        s.options = JSON.parse(s.json || '[]');
    }
    return s;
}

function sync(root) {
    const s = stateFor(root);
    if (s.input.disabled) close(s);
    if (Number(root.dataset.accountRevision || 0) < s.revision || s.editing) return;
    s.selected = root.dataset.accountValue || '0';
    s.input.value = s.options.find(x => x.id === s.selected)?.display || '';
}

function commit(s, id) {
    s.selected = id;
    s.commit.value = JSON.stringify({ id, revision: ++s.revision });
    s.commit.dispatchEvent(new Event('change', { bubbles: true }));
}

function close(s) {
    if (s.menu.matches(':popover-open')) s.menu.hidePopover();
    s.input.setAttribute('aria-expanded', 'false');
    s.input.removeAttribute('aria-activedescendant');
    if (active === s) active = null;
}

function select(s, id) {
    const option = s.options.find(x => x.id === id);
    if (s.input.disabled || (id !== '0' && !option)) return;
    s.editing = false;
    s.input.value = option?.display || '';
    close(s);
    commit(s, id);
}

function render(s) {
    s.highlight = -1;
    s.input.removeAttribute('aria-activedescendant');
    const query = s.editing ? s.input.value.trim().toLocaleLowerCase() : '';
    s.matches = s.options.filter(x => [x.name, x.code, x.reference].some(v => String(v || '').toLocaleLowerCase().includes(query)));
    const fragment = document.createDocumentFragment();
    const add = (id, name, detail, clear = false) => {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'searchable-dropdown-item' + (clear ? ' searchable-dropdown-clear' : '');
        button.dataset.accountId = id;
        button.id = `${s.menu.id}-option-${fragment.childNodes.length}`;
        button.setAttribute('role', 'option');
        button.setAttribute('aria-selected', String(id === s.selected));
        if (clear) {
            const icon = document.createElement('i'); icon.className = 'bi bi-x-circle me-1';
            button.append(icon, document.createTextNode(name));
        } else {
            const title = document.createElement('div'); title.className = 'fw-bold'; title.textContent = name;
            button.appendChild(title);
        }
        if (detail) { const small = document.createElement('small'); small.className = 'text-muted'; small.textContent = detail; button.appendChild(small); }
        fragment.appendChild(button);
    };
    if (s.root.dataset.accountClear === 'true') add('0', s.root.dataset.accountClearText, '', true);
    for (const option of s.matches.slice(0, Math.max(1, Number(s.root.dataset.accountLimit) || 30)))
        add(option.id, option.name, option.code && s.root.dataset.accountShowCode !== 'false' ? `${option.code} - ${option.type}` : option.type);
    if (!s.matches.length) {
        const empty = document.createElement('div'); empty.className = 'searchable-dropdown-item text-muted';
        empty.textContent = 'حسابی یافت نشد'; fragment.appendChild(empty);
    }
    s.menu.replaceChildren(fragment);
    window.hawalaTools.openFloatingDropdown(s.root, s.menu);
    s.input.setAttribute('aria-expanded', 'true');
}

function open(s) {
    if (s.input.disabled) return;
    if (active && active !== s) close(active);
    active = s;
    render(s);
}

function scan(node) {
    if (node.matches?.(selector)) sync(node);
    node.querySelectorAll?.(selector).forEach(sync);
}
scan(document);
new MutationObserver(records => {
    for (const record of records) {
        if (record.type === 'attributes') {
            const root = record.target.closest(selector);
            if (root) { sync(root); if (active?.root === root) render(active); }
        } else record.addedNodes.forEach(scan);
    }
    if (active && !active.root.isConnected) close(active);
}).observe(document, { subtree: true, childList: true, attributes: true,
    attributeFilter: ['data-account-options', 'data-account-value', 'data-account-revision', 'disabled'] });

document.addEventListener('focusin', event => {
    if (event.target.matches?.('[data-account-query]')) open(stateFor(event.target.closest(selector)));
}, true);
document.addEventListener('input', event => {
    if (!event.target.matches?.('[data-account-query]') || event.target.disabled) return;
    const s = stateFor(event.target.closest(selector));
    s.editing = true;
    if (s.selected && s.selected !== '0') commit(s, '0');
    if (!event.isComposing) open(s);
}, true);
document.addEventListener('compositionend', event => {
    if (event.target.matches?.('[data-account-query]')) open(stateFor(event.target.closest(selector)));
}, true);
document.addEventListener('pointerdown', event => {
    if (event.target.closest?.('[data-account-results] [data-account-id]')) event.preventDefault();
    else if (active && !active.root.contains(event.target)) close(active);
}, true);
document.addEventListener('click', event => {
    const option = event.target.closest?.('[data-account-results] [data-account-id]');
    if (option) { select(stateFor(option.closest(selector)), option.dataset.accountId); return; }
    if (event.target.matches?.('[data-account-query]')) open(stateFor(event.target.closest(selector)));
}, true);
document.addEventListener('focusout', event => {
    if (!event.target.matches?.('[data-account-query]')) return;
    const s = stateFor(event.target.closest(selector));
    s.editing = false; close(s);
    s.input.value = s.options.find(x => x.id === s.selected)?.display || '';
}, true);
document.addEventListener('keydown', event => {
    if (!event.target.matches?.('[data-account-query]')) return;
    if (event.isComposing) return;
    event.stopPropagation();
    const s = stateFor(event.target.closest(selector));
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        const previous = active === s ? s.highlight : -1;
        open(s);
        const buttons = [...s.menu.querySelectorAll('[data-account-id]')];
        if (buttons.length) {
            s.highlight = previous < 0 ? (event.key === 'ArrowDown' ? 0 : buttons.length - 1)
                : (previous + (event.key === 'ArrowDown' ? 1 : -1) + buttons.length) % buttons.length;
            const button = buttons[s.highlight];
            button.dataset.accountActive = 'true';
            s.input.setAttribute('aria-activedescendant', button.id);
            button.scrollIntoView({ block: 'nearest' });
        }
    }
    if (event.key === 'Escape') { event.preventDefault(); s.editing = false; close(s); s.input.value = s.options.find(x => x.id === s.selected)?.display || ''; }
    if (event.key === 'Enter' && !event.isComposing) {
        event.preventDefault();
        const highlighted = s.menu.querySelector('[data-account-active]');
        if (highlighted && active === s) select(s, highlighted.dataset.accountId);
        else if (s.matches?.length === 1) select(s, s.matches[0].id);
    }
}, true);
window.addEventListener('resize', () => { if (active) render(active); });
window.addEventListener('scroll', event => { if (active && !active.menu.contains(event.target)) close(active); }, true);
