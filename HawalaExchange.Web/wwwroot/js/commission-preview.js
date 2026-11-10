// Block stale accounting actions locally, before the Blazor server round trip.
export function installCommissionPreviewGuard(root = document, Observer = MutationObserver) {
    const dirtyForms = new Map();
    const enforce = () => {
        for (const [form, version] of dirtyForms) {
            if (form.isConnected === false) {
                dirtyForms.delete(form);
                continue;
            }
            const post = form.querySelector('[data-commission-post]');
            const calculate = form.querySelector('[data-commission-recalculate]');
            const busy = form.dataset.commissionBusy === 'true';
            if (version !== form.dataset.commissionPreviewVersion) {
                dirtyForms.delete(form);
                const canPost = form.dataset.commissionCanPost === 'true';
                if (post) post.disabled = !canPost;
                if (calculate) calculate.disabled = busy || canPost;
                continue;
            }
            if (post && !post.disabled) post.disabled = true;
            if (calculate && calculate.disabled !== busy) calculate.disabled = busy;
        }
    };
    const onInput = event => {
        const input = event.target?.closest?.('[data-commission-rate]');
        const form = input?.closest('[data-commission-form]');
        if (!form) return;
        dirtyForms.set(form, form.dataset.commissionPreviewVersion);
        enforce();
    };
    const onClick = event => {
        const button = event.target?.closest?.('[data-commission-post]');
        if (!button) return;
        enforce();
        for (const form of dirtyForms.keys()) {
            if (form.querySelector('[data-commission-post]') !== button) continue;
            event.preventDefault();
            event.stopImmediatePropagation();
            return;
        }
    };
    const observer = new Observer(enforce);
    observer.observe(root, { subtree: true, childList: true, attributes: true,
        attributeFilter: ['disabled', 'data-commission-preview-version', 'data-commission-busy', 'data-commission-can-post'] });
    root.addEventListener('input', onInput, true);
    root.addEventListener('click', onClick, true);
    return () => {
        observer.disconnect();
        root.removeEventListener('input', onInput, true);
        root.removeEventListener('click', onClick, true);
        dirtyForms.clear();
    };
}

if (typeof document !== 'undefined') installCommissionPreviewGuard();
