import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
const page = fs.readFileSync(new URL('../../HawalaExchange.Web/Components/Pages/HawalaTransactions/HawalaTransactions.razor', import.meta.url), 'utf8');
test('both date pickers are visible quick filters with their original bindings', () => {
    const quick = page.match(/<QuickFilters>([\s\S]*?)<\/QuickFilters>/)[1];
    const advanced = page.match(/<AdvancedFilters>([\s\S]*?)<\/AdvancedFilters>/)[1];
    for (const name of ['filterFromDate', 'filterToDate']) {
        assert.match(quick, new RegExp(`@bind-Value="${name}" @bind-Value:after="OnFilterChangedAsync" AllowEmpty="true"`));
        assert.ok(!advanced.includes(name));
        assert.equal(page.split(`@bind-Value="${name}"`).length - 1, 1);
    }
});
test('dates still enable reset but no longer inflate the hidden-filter count', () => {
    const count = page.match(/private int AdvancedFilterCount =>([\s\S]*?);/)[1];
    const active = page.match(/private bool HasActiveFilters =>([\s\S]*?);/)[1];
    for (const name of ['filterFromDate', 'filterToDate']) {
        assert.ok(!count.includes(name)); assert.ok(active.includes(name));
    }
});

test('main hawala page hides only text fields already covered by the main search', () => {
    assert.match(page, /<HawalaExtraFilters[^>]*ShowTextFilters="false"/);
    const shared = fs.readFileSync(new URL('../../HawalaExchange.Web/Components/Shared/HawalaExtraFilters.razor', import.meta.url), 'utf8');
    const text = shared.match(/@if \(ShowTextFilters\)\s*\{([\s\S]*?)\n\s*\}/)[1];
    for (const name of ['Sender', 'Receiver', 'Reference']) assert.ok(text.includes(`Filter.${name}`));
    for (const name of ['MinNumber', 'MaxNumber', 'Registration', 'Commission']) assert.ok(!text.includes(`Filter.${name}`));
    assert.match(shared, /bool ShowTextFilters \{ get; set; \} = true/);
    const service = fs.readFileSync(new URL('../../HawalaExchange.Infrastructure/Services/HawalaService.cs', import.meta.url), 'utf8');
    const search = service.slice(service.indexOf('var term = filter.SearchTerm.Trim();'));
    for (const field of ['SenderName', 'ReceiverName', 'ReferenceNumber']) assert.ok(search.includes(`h.${field}.Contains(term)`));
});
