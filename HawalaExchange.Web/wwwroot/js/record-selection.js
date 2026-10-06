const scopes = new Map();

export function mount(id, dotnet, selectedIds, visibleIds, resetKey = '') {
    const table = document.getElementById(id);
    if (!table) return;
    let state = scopes.get(id);
    if (state && state.table !== table) { unmount(id); state = null; }
    if (!state) {
        const controller = new AbortController();
        state = { table, controller, selected: new Set(), visible: new Set(), anchor: null, dragging: false, frame: 0 };
        scopes.set(id, state);
        const options = { signal: controller.signal };
        const cells = () => [...table.querySelectorAll('.record-selection-cell[data-record-id]')];
        const key = cell => Number(cell.dataset.recordId);
        const paint = () => {
            cells().forEach(cell => {
            const checked = state.selected.has(key(cell));
            cell.querySelector('input').checked = checked;
            cell.closest('tr').classList.toggle('record-selected', checked);
            cell.closest('tr').setAttribute('aria-selected', String(checked));
            });
            const header = table.querySelector('.record-selection-all');
            if (header) {
                const count = [...state.visible].filter(id => state.selected.has(id)).length;
                header.checked = state.visible.size > 0 && count === state.visible.size;
                header.indeterminate = count > 0 && count < state.visible.size;
                header.disabled = state.visible.size === 0;
            }
        };
        const notify = () => {
            const added = [...state.selected].filter(x => state.visible.has(x) && !state.synced.has(x));
            const removed = [...state.synced].filter(x => state.visible.has(x) && !state.selected.has(x));
            const ref = state.dotnet, version = state.resetKey;
            state.synced = new Set(state.selected);
            // Keep each inbound SignalR message comfortably below its default 32 KiB limit.
            state.queue = (state.queue || Promise.resolve()).then(async () => {
                for (let index = 0; index < Math.max(added.length, removed.length); index += 200)
                    await ref.invokeMethodAsync('UpdateVisibleSelection', added.slice(index, index + 200), removed.slice(index, index + 200), version);
            }).catch(() => {});
        };
        const range = end => {
            const ordered = cells().map(key);
            const startIndex = ordered.indexOf(state.start);
            const endIndex = ordered.indexOf(end);
            if (startIndex < 0 || endIndex < 0) return;
            state.selected = new Set(state.baseline);
            for (const id of ordered.slice(Math.min(startIndex, endIndex), Math.max(startIndex, endIndex) + 1)) {
                if (state.add) state.selected.add(id); else state.selected.delete(id);
            }
            paint();
        };
        const follow = () => {
            const element = document.elementFromPoint(state.x, state.y);
            const row = element?.closest('tr');
            const cell = row?.querySelector('.record-selection-cell');
            if (cell && table.contains(cell)) range(key(cell));
        };
        const finish = () => {
            if (!state.dragging) return;
            state.dragging = false;
            cancelAnimationFrame(state.frame);
            document.body.style.userSelect = state.previousUserSelect;
            notify();
        };
        const scrollParent = () => {
            for (let parent = table.parentElement; parent; parent = parent.parentElement) {
                const style = getComputedStyle(parent);
                if (/(auto|scroll)/.test(style.overflowY) && parent.scrollHeight > parent.clientHeight) return parent;
            }
            return document.scrollingElement;
        };
        const autoScroll = () => {
            if (!state.dragging) return;
            const parent = scrollParent();
            const windowScroll = parent === document.scrollingElement;
            const rect = windowScroll ? { top: 0, bottom: window.innerHeight } : parent.getBoundingClientRect();
            const delta = state.y < rect.top + 35 ? -12 : state.y > rect.bottom - 35 ? 12 : 0;
            if (delta) {
                parent.scrollTop += delta;
                // Keep hit testing inside the scrolling viewport while the cursor is at its edge.
                const y = state.y;
                state.y = Math.max(rect.top + 1, Math.min(rect.bottom - 1, y));
                follow(); state.y = y;
            }
            state.frame = requestAnimationFrame(autoScroll);
        };
        table.addEventListener('mousedown', event => {
            const cell = event.target.closest('.record-selection-cell');
            if (!cell || event.button !== 0 || !state.visible.has(key(cell))) return;
            event.preventDefault(); event.stopPropagation();
            state.start = event.shiftKey && state.anchor != null ? state.anchor : key(cell);
            state.baseline = new Set(state.selected);
            state.add = event.shiftKey || !state.selected.has(key(cell));
            state.anchor = state.start;
            state.x = event.clientX; state.y = event.clientY;
            state.dragging = true;
            state.previousUserSelect = document.body.style.userSelect;
            document.body.style.userSelect = 'none';
            range(key(cell));
            state.frame = requestAnimationFrame(autoScroll);
        }, options);
        table.addEventListener('click', event => {
            if (event.target.closest('.record-selection-all')) {
                event.stopPropagation();
                const all = state.visible.size > 0 && [...state.visible].every(id => state.selected.has(id));
                for (const id of state.visible) { if (all) state.selected.delete(id); else state.selected.add(id); }
                paint(); notify(); return;
            }
            if (event.target.closest('.record-selection-cell')) { event.preventDefault(); event.stopPropagation(); paint(); }
        }, options);
        table.addEventListener('keydown', event => {
            const cell = event.target.closest('.record-selection-cell');
            if (!cell || event.key !== ' ') return;
            event.preventDefault();
            const id = key(cell);
            if (state.selected.has(id)) state.selected.delete(id); else state.selected.add(id);
            state.anchor = id; paint(); notify();
        }, options);
        document.addEventListener('mousemove', event => {
            if (!state.dragging) return;
            if ((event.buttons & 1) === 0) { finish(); return; }
            state.x = event.clientX; state.y = event.clientY; follow();
        }, options);
        document.addEventListener('mouseup', finish, options);
        document.addEventListener('scroll', () => { if (state.dragging) follow(); }, { ...options, capture: true });
        window.addEventListener('blur', finish, options);
        state.paint = paint; state.finish = finish;
    }
    state.dotnet = dotnet;
    state.resetKey = resetKey;
    state.synced = new Set(selectedIds);
    state.visible = new Set(visibleIds);
    if (!state.dragging) state.selected = new Set(selectedIds);
    state.paint();
}

export function unmount(id) {
    const state = scopes.get(id);
    if (!state) return;
    state.finish(); state.controller.abort(); cancelAnimationFrame(state.frame); scopes.delete(id);
}
export async function copy(text) {
    if (navigator.clipboard && window.isSecureContext) { await navigator.clipboard.writeText(text); return; }
    const input = document.createElement('textarea');
    input.value = text; input.style.position = 'fixed'; input.style.opacity = '0';
    document.body.appendChild(input); input.select();
    try { if (!document.execCommand('copy')) throw new Error('Copy denied'); }
    finally { input.remove(); }
}
export function share(target, text) {
    const url = target === 'whatsapp' ? `https://wa.me/?text=${encodeURIComponent(text)}`
        : `https://t.me/share/url?url=&text=${encodeURIComponent(text)}`;
    const popup = window.open(url, '_blank');
    if (!popup) throw new Error('Popup blocked');
    popup.opener = null;
}
export function download(name, mime, base64) {
    const bytes = Uint8Array.from(atob(base64), char => char.charCodeAt(0));
    const url = URL.createObjectURL(new Blob([bytes], { type: mime }));
    const link = document.createElement('a'); link.href = url; link.download = name;
    document.body.appendChild(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
}
