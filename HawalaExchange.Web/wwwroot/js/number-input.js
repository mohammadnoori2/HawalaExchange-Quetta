// Protect financial values from accidental mouse-wheel and arrow-key stepping.
export function installNumberInputGuard(root = document) {
    const onWheel = event => {
        const input = event.target?.closest?.('input[type="number"]');
        if (input && root.activeElement === input) event.preventDefault();
    };
    const onKeyDown = event => {
        if ((event.key === 'ArrowUp' || event.key === 'ArrowDown') &&
            event.target?.closest?.('input[type="number"]')) event.preventDefault();
    };
    root.addEventListener('wheel', onWheel, { passive: false, capture: true });
    root.addEventListener('keydown', onKeyDown, true);
    return () => {
        root.removeEventListener('wheel', onWheel, true);
        root.removeEventListener('keydown', onKeyDown, true);
    };
}

if (typeof document !== 'undefined') installNumberInputGuard();
