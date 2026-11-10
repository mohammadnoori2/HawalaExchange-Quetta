import test from 'node:test';
import assert from 'node:assert/strict';
import { installNumberInputGuard } from '../../HawalaExchange.Web/wwwroot/js/number-input.js';

test('wheel never changes a focused numeric input; elsewhere the page scrolls normally', () => {
    const handlers = {};
    const root = { addEventListener: (n, f) => handlers[n] = f, removeEventListener: n => delete handlers[n] };
    const stop = installNumberInputGuard(root);
    let prevented = false;
    const input = {};
    root.activeElement = input;
    const event = { target: { closest: () => input }, preventDefault: () => prevented = true };
    handlers.wheel(event);
    assert.equal(prevented, true);
    prevented = false;
    handlers.wheel({ ...event, target: { closest: () => null } });
    assert.equal(prevented, false);
    root.activeElement = null;
    handlers.wheel(event);
    assert.equal(prevented, false);
    stop();
    assert.deepEqual(Object.keys(handlers), []);
});

test('arrow keys cannot silently step numeric amounts', () => {
    const handlers = {};
    installNumberInputGuard({ addEventListener: (n, f) => handlers[n] = f });
    for (const key of ['ArrowUp', 'ArrowDown', '1', 'Backspace']) {
        let prevented = false;
        handlers.keydown({ key, target: { closest: () => ({}) }, preventDefault: () => prevented = true });
        assert.equal(prevented, key.startsWith('Arrow'));
    }
});
