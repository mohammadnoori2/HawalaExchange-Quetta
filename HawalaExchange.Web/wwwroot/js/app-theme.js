// Set before styles load to avoid a bright flash. Preferences stay on this browser.
(() => {
    const key = 'hawala.theme';
    const root = document.documentElement;
    const valid = value => value === 'light' || value === 'dark';
    function apply(theme) {
        root.dataset.bsTheme = valid(theme) ? theme : 'light';
        document.querySelectorAll('[data-theme-toggle]').forEach(button => {
            const dark = root.dataset.bsTheme === 'dark';
            button.setAttribute('aria-pressed', String(dark));
            button.setAttribute('aria-label', dark ? 'حالت روشن' : 'حالت تاریک');
            const label = button.querySelector('[data-theme-label]');
            if (label) label.textContent = dark ? 'حالت روشن' : 'حالت تاریک';
            const icon = button.querySelector('i');
            if (icon) icon.className = dark ? 'bi bi-sun' : 'bi bi-moon-stars';
        });
        if (window.Chart) {
            const style = getComputedStyle(root);
            const ink = style.getPropertyValue('--app-muted').trim();
            const border = style.getPropertyValue('--app-border').trim();
            window.Chart.defaults.color = ink;
            window.Chart.defaults.borderColor = border;
            if (!window.Chart.hawalaThemeRegistered) {
                window.Chart.register({ id: 'hawalaTheme', beforeUpdate(chart) {
                    const colors = getComputedStyle(root);
                    const text = colors.getPropertyValue('--app-muted').trim();
                    Object.values(chart.options.scales || {}).forEach(scale => {
                        if (scale.ticks) scale.ticks.color = text;
                        if (scale.grid) scale.grid.color = colors.getPropertyValue('--app-border').trim();
                    });
                    if (chart.options.plugins?.legend?.labels) chart.options.plugins.legend.labels.color = text;
                }});
                window.Chart.hawalaThemeRegistered = true;
            }
            Object.values(window.Chart.instances || {}).forEach(chart => {
                Object.values(chart.options.scales || {}).forEach(scale => {
                    if (scale.ticks) scale.ticks.color = ink;
                    if (scale.grid) scale.grid.color = border;
                });
                if (chart.options.plugins?.legend?.labels) chart.options.plugins.legend.labels.color = ink;
                chart.update('none');
            });
        }
    }
    let stored;
    try { stored = localStorage.getItem(key); } catch { /* Storage may be disabled. */ }
    apply(valid(stored) ? stored : 'light');
    document.addEventListener('click', event => {
        if (!event.target.closest?.('[data-theme-toggle]')) return;
        const theme = root.dataset.bsTheme === 'dark' ? 'light' : 'dark';
        try { localStorage.setItem(key, theme); } catch { }
        apply(theme);
    });
    window.addEventListener('storage', event => {
        if (event.key === key) apply(valid(event.newValue) ? event.newValue : 'light');
    });
    document.addEventListener('DOMContentLoaded', () => {
        apply(root.dataset.bsTheme);
        // Blazor enhanced navigation may replace the menu without reloading the document.
        new MutationObserver(records => {
            if (records.some(record => [...record.addedNodes].some(node => node.nodeType === 1 &&
                (node.matches('[data-theme-toggle],canvas') || node.querySelector('[data-theme-toggle],canvas')))))
                apply(root.dataset.bsTheme);
        }).observe(document.body, { childList: true, subtree: true });
    });
})();
