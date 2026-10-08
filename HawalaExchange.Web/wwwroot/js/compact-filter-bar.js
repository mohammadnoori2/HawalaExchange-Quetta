const instances = new WeakMap();

// Allocate a single row without moving Blazor-owned DOM nodes or duplicating inputs.
export function fitInlineFields(width, fixedWidths, fieldWidths, moreWidth, gap = 8) {
    const base = fixedWidths.reduce((sum, value) => sum + value, 0);
    const required = count => base + fieldWidths.slice(0, count).reduce((sum, value) => sum + value, 0)
        + gap * Math.max(0, fixedWidths.length + count - 1);
    if (required(fieldWidths.length) <= width) return fieldWidths.length;
    for (let count = fieldWidths.length - 1; count >= 0; count--) {
        if (required(count) + moreWidth + (fixedWidths.length + count > 0 ? gap : 0) <= width) return count;
    }
    return 0;
}

export function initialize(root) {
    if (!root || instances.has(root)) return;
    const main = root.querySelector('.compact-filter-main');
    const group = root.querySelector('.compact-adaptive-fields');
    if (!main || !group) return;
    const state = { frame: 0, moreWidth: 180 };
    const update = () => {
        state.frame = 0;
        if (!root.isConnected) return;
        const fields = Array.from(group.children);
        const button = main.querySelector('.compact-more-filter');
        if (button?.getBoundingClientRect().width) state.moreWidth = button.getBoundingClientRect().width;
        const fixed = Array.from(main.children).filter(x => x !== group && x !== button && getComputedStyle(x).display !== 'none');
        const gap = parseFloat(getComputedStyle(main).columnGap) || 8;
        // Wide checkbox groups/alerts must stay in the expanded section.
        const widths = fields.map(x => x.classList.contains('compact-filter-field') && !x.style.gridColumn ? 180 : Infinity);
        const count = window.innerWidth < 768 ? 0 : fitInlineFields(main.clientWidth,
            fixed.map(x => x.getBoundingClientRect().width), widths, state.moreWidth, gap);
        fields.forEach((field, index) => {
            const value = index < count ? 'true' : 'false';
            if (field.dataset.inlineVisible !== value) field.dataset.inlineVisible = value;
        });
        const overflow = count < fields.length ? 'true' : 'false';
        if (root.dataset.filterOverflow !== overflow) root.dataset.filterOverflow = overflow;
    };
    const schedule = () => { if (!state.frame) state.frame = requestAnimationFrame(update); };
    state.resize = new ResizeObserver(schedule);
    state.resize.observe(main);
    state.mutations = new MutationObserver(schedule);
    state.mutations.observe(main, { childList: true, subtree: true });
    state.onResize = schedule;
    window.addEventListener('resize', schedule);
    instances.set(root, state);
    schedule();
}

export function dispose(root) {
    const state = instances.get(root);
    if (!state) return;
    state.resize.disconnect();
    state.mutations.disconnect();
    window.removeEventListener('resize', state.onResize);
    if (state.frame) cancelAnimationFrame(state.frame);
    instances.delete(root);
}
