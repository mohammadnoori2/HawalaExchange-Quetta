// Keyboard support for existing Blazor platform dialogs; no financial or auth actions.
let activeDialog = null;
const returnFocus = new WeakMap();
let dialogSequence = 0;
const focusableSelector = 'a[href], button, input, select, textarea, summary, [tabindex]';

function focusable(dialog) {
    return [...dialog.querySelectorAll(focusableSelector)].filter(element =>
        !element.disabled && element.tabIndex >= 0 && element.getClientRects().length > 0);
}

function currentDialog() {
    return [...document.querySelectorAll('.platform-workspace .modal.show')]
        .filter(element => element.getClientRects().length > 0)
        .sort((a, b) => (Number(getComputedStyle(a).zIndex) || 1055) - (Number(getComputedStyle(b).zIndex) || 1055))
        .at(-1) ?? null;
}

function synchronizeDialog() {
    const dialog = currentDialog();
    if (dialog === activeDialog) return;
    const previous = activeDialog;
    activeDialog = dialog;
    previous?.removeAttribute('aria-modal');
    if (dialog) {
        if (!returnFocus.has(dialog)) returnFocus.set(dialog, document.activeElement);
        dialog.setAttribute('role', 'dialog');
        dialog.setAttribute('aria-modal', 'true');
        dialog.setAttribute('tabindex', '-1');
        const heading = dialog.querySelector('.modal-header :is(h1,h2,h3,h4,h5,h6)');
        if (heading) {
            heading.id ||= `platform-dialog-title-${++dialogSequence}`;
            dialog.setAttribute('aria-labelledby', heading.id);
        }
        // Prefer the field over the close button; use the dialog itself for confirmations.
        const firstField = focusable(dialog).find(element => element.matches('input:not([type="checkbox"]),select,textarea'));
        (firstField ?? dialog).focus({ preventScroll: true });
    } else {
        const target = previous && returnFocus.get(previous);
        if (target?.isConnected && !target.disabled) target.focus({ preventScroll: true });
    }
}

document.addEventListener('keydown', event => {
    if (event.key === 'Enter' && event.target.id === 'managed-permission-search') {
        event.preventDefault(); // Local filtering must not submit the surrounding user form.
        return;
    }
    const dialog = activeDialog;
    if (!dialog?.isConnected) return;
    if (event.key === 'Tab') {
        const items = focusable(dialog);
        const first = items[0];
        const last = items.at(-1);
        if (!first) { event.preventDefault(); dialog.focus(); return; }
        if (event.shiftKey && (document.activeElement === first || !items.includes(document.activeElement))) {
            event.preventDefault(); last.focus();
        } else if (!event.shiftKey && (document.activeElement === last || !items.includes(document.activeElement))) {
            event.preventDefault(); first.focus();
        }
    }
    if (event.key === 'Escape') {
        const close = [...dialog.querySelectorAll('button')].find(button =>
            !button.disabled && (button.classList.contains('btn-close') || ['انصراف', 'بستن'].includes(button.textContent.trim())));
        if (close) { event.preventDefault(); event.stopPropagation(); close.click(); }
    }
});

// Handles interactive rendering and enhanced navigation without a polling timer.
new MutationObserver(synchronizeDialog).observe(document.body, { childList: true, subtree: true });
synchronizeDialog();
