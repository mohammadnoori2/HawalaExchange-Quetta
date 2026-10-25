import test from 'node:test';
import assert from 'node:assert/strict';
import { convertPreview, quotePreview, moneyWords, refreshMoneyPreview } from '../../HawalaExchange.Web/wwwroot/js/money-preview.js';
const usd = { id: '1', code: 'USD', priority: 1, name: 'دالر' };
const afn = { id: '2', code: 'AFN', priority: 2, name: 'افغانی' };
test('conversion uses exact decimal strings with away-from-zero whole money rounding', () => {
    assert.equal(convertPreview('100', '65', usd, afn), '6500');
    assert.equal(convertPreview('6500', '65', afn, usd), '100');
    assert.equal(convertPreview('100.5', '1', usd, afn), '101');
    assert.equal(convertPreview('9007199254740993', '1', usd, afn), '9007199254740993');
    assert.equal(convertPreview('3250', '65', afn, usd), '50');
    assert.equal(convertPreview('', '65', usd, afn), null);
    assert.equal(convertPreview('100', '0', usd, afn), null);
    assert.equal(convertPreview('100', '65', usd, usd), '100');
});
test('rate uses canonical priorities and midpoint-to-even at eight decimals', () => {
    assert.equal(quotePreview('100', '6500', usd, afn), '65');
    assert.equal(quotePreview('6500', '100', afn, usd), '65');
    assert.equal(quotePreview('200000000', '1', usd, afn), '0');
    assert.equal(quotePreview('200000000', '3', usd, afn), '0.00000002');
    assert.equal(quotePreview('0', '100', usd, afn), null);
});
test('Dari words preserve fractions and large amounts without Number money conversion', () => {
    assert.equal(moneyWords('21560'), 'بیست و یک هزار و پنج صد و شصت');
    assert.equal(moneyWords('0'), 'صفر');
    assert.equal(moneyWords('-101'), 'منفی صد و یک');
    assert.equal(moneyWords('0.05'), 'صفر و پنج صدم');
    assert.equal(moneyWords('1000000000'), 'یک میلیارد');
    assert.equal(moneyWords(''), '');
    assert.equal(moneyWords('<img src=x>'), '');
});

test('typing updates calculated payment and currency words synchronously without server change events', () => {
    const from = { value: '100', dataset: { moneyDecimals: '0' } };
    const to = { value: '', dataset: { moneyDecimals: '0' } };
    const rate = { value: '65', dataset: { moneyDecimals: '8' } };
    const fromSelect = { value: '1', selectedOptions: [{ dataset: { code: 'USD', priority: '1' }, textContent: 'دالر' }] };
    const toSelect = { value: '2', selectedOptions: [{ dataset: { code: 'AFN', priority: '2' }, textContent: 'افغانی' }] };
    const words = ['from', 'to'].map(key => ({ dataset: { liveWords: key }, textContent: '', hidden: true }));
    const nodes = new Map([['[data-live-amount="from"]', from], ['[data-live-amount="to"]', to],
        ['[data-live-amount="rate"]', rate], ['[data-live-currency="from"]', fromSelect], ['[data-live-currency="to"]', toSelect]]);
    const scope = { dataset: { liveFinance: 'hawala' }, querySelector: s => nodes.get(s), querySelectorAll: () => words };
    from.closest = () => scope;
    refreshMoneyPreview(from);
    assert.equal(to.value, '6,500'); assert.equal(words[1].textContent, 'شش هزار و پنج صد افغانی');
    from.value = ''; refreshMoneyPreview(from); assert.equal(to.value, ''); assert.equal(words[1].hidden, true);
    from.value = '10'; toSelect.value = '1'; toSelect.selectedOptions = fromSelect.selectedOptions;
    refreshMoneyPreview(from); assert.equal(to.value, '10'); assert.equal(rate.value, '1');
    assert.equal(words[1].textContent, 'ده دالر');
});

test('standalone words stay scoped to their own row and update on currency changes', () => {
    const input = { value: '21560', dataset: { moneyDecimals: '0' } };
    const output = { textContent: '', hidden: true };
    const select = { value: '2', selectedOptions: [{ textContent: 'افغانی' }] };
    const group = { querySelector: s => s === '[data-money-words]' ? output : select,
        querySelectorAll: () => [input] };
    const trigger = { closest: () => null, parentElement: group };
    refreshMoneyPreview(trigger); assert.equal(output.textContent, 'بیست و یک هزار و پنج صد و شصت افغانی');
    select.selectedOptions[0].textContent = 'دالر'; refreshMoneyPreview(trigger);
    assert.equal(output.textContent, 'بیست و یک هزار و پنج صد و شصت دالر');
    input.value = ''; refreshMoneyPreview(trigger); assert.equal(output.textContent, ''); assert.equal(output.hidden, true);
    group.querySelectorAll = () => [input, input]; output.textContent = 'untouched';
    refreshMoneyPreview(trigger); assert.equal(output.textContent, 'untouched');
});

test('exchange calculates from the last edited side and hawala reverse mode updates the rate', () => {
    const from = { value: '100', dataset: { liveAmount: 'from', moneyDecimals: '2' } };
    const to = { value: '6500', dataset: { liveAmount: 'to', moneyDecimals: '2' } };
    const rate = { value: '65', dataset: { liveAmount: 'rate', moneyDecimals: '8' } };
    const trade = { value: 'Sell' };
    const fromCurrency = { value: '1', selectedOptions: [{ dataset: { code: 'USD', priority: '1' }, textContent: 'دالر' }] };
    const toCurrency = { value: '2', selectedOptions: [{ dataset: { code: 'AFN', priority: '2' }, textContent: 'افغانی' }] };
    const nodes = { '[data-live-amount="from"]': from, '[data-live-amount="to"]': to, '[data-live-amount="rate"]': rate,
        '[data-live-currency="from"]': fromCurrency, '[data-live-currency="to"]': toCurrency, '[data-live-trade]': trade };
    const scope = { dataset: { liveFinance: 'exchange' }, querySelector: key => nodes[key], querySelectorAll: () => [] };
    for (const node of [from, to, rate, trade]) node.closest = () => scope;
    refreshMoneyPreview(from, true); assert.equal(to.value, '6,500');
    to.value = '13000'; refreshMoneyPreview(to, true); assert.equal(from.value, '200'); assert.equal(trade.value, 'Buy');
    rate.value = '130'; refreshMoneyPreview(rate, true); assert.equal(from.value, '100');
    trade.value = 'Sell'; refreshMoneyPreview(trade); assert.equal(to.value, '13,000');
    scope.dataset.liveFinance = 'hawala'; nodes['[data-live-mode]:checked'] = { value: 'AmountToRate' };
    to.value = '6500'; refreshMoneyPreview(to, true); assert.equal(rate.value, '65');
    toCurrency.value = '0'; refreshMoneyPreview(from); assert.equal(to.value, '');
});
