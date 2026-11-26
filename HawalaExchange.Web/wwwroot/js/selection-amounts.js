const scale = 100000000n;
const exactAmounts = new WeakMap();
export function decimalUnits(value) {
    const match = /^(-?)(\d+)(?:\.(\d{1,8}))?$/.exec(String(value));
    if (!match) throw new Error('Invalid exact monetary amount');
    return (match[1] ? -1n : 1n) * (BigInt(match[2]) * scale + BigInt((match[3] || '').padEnd(8, '0')));
}
export function money(units) {
    const negative = units < 0n;
    const value = ((negative ? -units : units) + scale / 2n) / scale;
    return (negative && value ? '-' : '') + new Intl.NumberFormat('en-US').format(value);
}
function currencyTone(code) {
    const known = { USD: 0, AFN: 1, PKR: 2, EUR: 3, AED: 4, IRR: 5, IRT: 5, GBP: 6, SAR: 7 };
    if (Object.hasOwn(known, code)) return known[code];
    return [...code].reduce((hash, char) => (hash * 31 + char.charCodeAt(0)) % 8, 0);
}
export function summarize(ids, cache) {
    const originals = new Map(), conversions = new Map(), agreements = new Map(), grand = new Map(), unconverted = new Map();
    const effectiveTotals = new Map(), independentConversions = new Map();
    let independentIncomplete = 0;
    let missing = 0, pending = 0, incomplete = 0, withoutAgreement = 0;
    for (const id of ids) {
        const row = cache.get(id);
        if (!row) { missing++; continue; }
        let exact = exactAmounts.get(row);
        if (!exact) { exact = { original: decimalUnits(row.original), converted: row.converted == null ? null : decimalUnits(row.converted) }; exactAmounts.set(row, exact); }
        const amount = exact.original, cancelled = !!row.cancelled;
        const hasIndependent = !!row.hasIndependentExchange;
        if (hasIndependent && row.independentConverted != null && row.independentCurrency) {
            const converted = decimalUnits(row.independentConverted);
            const key = JSON.stringify([row.currency, row.independentCurrency, cancelled]);
            const conversion = independentConversions.get(key) || { from: row.currency, to: row.independentCurrency, cancelled, source: 0n, target: 0n };
            conversion.source += amount; conversion.target += converted; independentConversions.set(key, conversion);
            if (!cancelled) effectiveTotals.set(row.independentCurrency, (effectiveTotals.get(row.independentCurrency) || 0n) + converted);
        } else if (!cancelled) {
            if (hasIndependent) independentIncomplete++;
            else effectiveTotals.set(row.currency, (effectiveTotals.get(row.currency) || 0n) + amount);
        }
        const originalKey = JSON.stringify([row.currency, cancelled]);
        const original = originals.get(originalKey) || { currency: row.currency, cancelled, amount: 0n };
        original.amount += amount; originals.set(originalKey, original);
        if (!cancelled && !row.hasConversion && row.converted == null && row.currency !== row.settlementCurrency)
            unconverted.set(row.currency, (unconverted.get(row.currency) || 0n) + amount);
        if (!row.settlementCurrency) { if (!cancelled) withoutAgreement++; continue; }
        const key = JSON.stringify([row.correspondentId, row.settlementCurrency, cancelled]);
        const agreement = agreements.get(key) || { correspondent: row.correspondent, currency: row.settlementCurrency, cancelled, native: 0n, converted: 0n };
        if (row.currency === row.settlementCurrency) agreement.native += amount;
        else if (row.converted != null) {
            const converted = exact.converted;
            agreement.converted += converted;
            const conversionKey = JSON.stringify([row.currency, row.settlementCurrency, cancelled]);
            const conversion = conversions.get(conversionKey) || { from: row.currency, to: row.settlementCurrency, cancelled, source: 0n, target: 0n };
            conversion.source += amount; conversion.target += converted; conversions.set(conversionKey, conversion);
        } else if (!cancelled) {
            if (row.hasConversion) incomplete++; else pending++;
        }
        agreements.set(key, agreement);
    }
    for (const row of agreements.values()) if (!row.cancelled)
        grand.set(row.currency, (grand.get(row.currency) || 0n) + row.native + row.converted);
    return { effectiveTotals: [...effectiveTotals].map(([currency, amount]) => ({currency, amount})), independentConversions: [...independentConversions.values()], independentIncomplete, count: ids.size, missing, pending, incomplete, withoutAgreement, unconverted: [...unconverted].map(([currency, amount]) => ({ currency, amount })), originals: [...originals.values()], conversions: [...conversions.values()], agreements: [...agreements.values()], grand: [...grand].map(([currency, amount]) => ({ currency, amount })) };
}
export function paintAmountStatus(scopeId, ids, cache, unavailable = false) {
    const target = document.getElementById(`${scopeId}-amount-status`);
    if (!target) return;
    target.hidden = ids.size === 0;
    if (!ids.size) { target.replaceChildren(); return; }
    const result = summarize(ids, cache), content = document.createDocumentFragment();
    const line = (title, values, strong = false, highlight = false) => {
        const element = document.createElement('div'); element.className = highlight ? 'record-amount-status-line record-amount-status-total' : 'record-amount-status-line';
        const label = document.createElement(strong ? 'strong' : 'span'); label.textContent = title; element.append(label);
        for (const value of values) {
            const text = document.createElement(strong ? 'strong' : 'span');
            text.textContent = value.text; text.className = value.cancelled ? 'text-danger' : '';
            if (highlight && value.currency) {
                const code = value.currency.trim().toUpperCase();
                text.classList.add('currency-badge', `currency-tone-${currencyTone(code)}`);
                text.dataset.currency = code;
            }
            if (value.ltr) text.dir = 'ltr'; element.append(text);
        }
        content.append(element);
    };
    line('انتخاب:', [{ text: new Intl.NumberFormat('en-US').format(result.count) + ' حواله' }]);
    if (result.missing) {
        line('', [{ text: unavailable ? 'اطلاعات بعضی انتخاب‌ها در دسترس نیست؛ فهرست را تازه کنید.' : `در حال دریافت اطلاعات ${result.missing} انتخاب…` }]);
    } else {
        line('اصل مبالغ:', result.originals.map(x => ({ text: `${money(x.amount)} ${x.currency}${x.cancelled ? ' (لغوشده)' : ''}`, cancelled: x.cancelled, ltr: true })));
        if (result.unconverted.length) line('مبلغ تبدیل‌نشده به ارز توافقی:', result.unconverted.map(x => ({ text: `${money(x.amount)} ${x.currency}`, ltr: true })));
        if (result.conversions.length) line('تبدیل به ارز توافقی:', result.conversions.map(x => ({ text: `${money(x.source)} ${x.from} → ${money(x.target)} ${x.to}${x.cancelled ? ' (لغوشده)' : ''}`, cancelled: x.cancelled, ltr: true })));
        if (result.independentConversions.length) line('تبدیل مستقل:', result.independentConversions.map(x => ({ text: `${money(x.source)} ${x.from} → ${money(x.target)} ${x.to}${x.cancelled ? ' (لغوشده)' : ''}`, cancelled: x.cancelled, ltr: true })));
        const activeAgreements = result.agreements.filter(x => !x.cancelled);
        if (activeAgreements.length > 1) line('به تفکیک نمایندگی:', activeAgreements.map(x => ({ text: `${x.correspondent}: ${money(x.native + x.converted)} ${x.currency}` })));
        line('مجموع کل به ارز توافقی:', result.grand.length ? result.grand.map(x => ({ text: `${money(x.amount)} ${x.currency}`, ltr: true })) : [{ text: '—' }], true);
        line('مجموع نهایی مبالغ:', result.independentIncomplete ? [{ text: 'اطلاعات تبدیل بعضی حواله‌ها ناقص است؛ فهرست را تازه کنید.' }] : result.effectiveTotals.length ? result.effectiveTotals.map(x => ({ text: `${money(x.amount)} ${x.currency}`, currency: x.currency, ltr: true })) : [{ text: '—' }], true, true);
        if (result.pending || result.incomplete || result.withoutAgreement) line('', [{ text: `تبدیل‌نشده: ${result.pending} · اطلاعات تبدیل ناقص: ${result.incomplete} · بدون ارز توافقی: ${result.withoutAgreement}` }]);
    }
    target.replaceChildren(content);
}
