import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
const read = path => fs.readFileSync(new URL('../../' + path, import.meta.url), 'utf8');
test('forwarding commission uses the new title across page, action, permissions and reports', () => {
    for (const path of [
        'HawalaExchange.Web/Components/Pages/Correspondents/CorrespondentDetails.razor',
        'HawalaExchange.Web/Components/Pages/Correspondents/CorrespondentCommission.razor',
        'HawalaExchange.Application/Security/PermissionCatalog.cs',
        'HawalaExchange.Application/DTOs/HawalaCommissionReportDtos.cs']) {
        const source = read(path);
        assert.ok(source.includes('کمیشن پرداختی ولایات'), path);
        assert.ok(!source.includes('کمیشن ارسالی'), path);
    }
});
test('permission keys, routes and the separate destination commission are unchanged', () => {
    assert.ok(read('HawalaExchange.Application/Security/PermissionCatalog.cs').includes('new("ForwardingCommission","کمیشن پرداختی ولایات",["View","Calculate","Reverse","Export"])'));
    const page = read('HawalaExchange.Web/Components/Pages/Correspondents/CorrespondentCommission.razor');
    assert.ok(page.includes('/forwarding-commission'));
    assert.ok(page.includes('isOutgoing ? "کمیشن پرداختی به نمایندگی‌ها"'));
});
