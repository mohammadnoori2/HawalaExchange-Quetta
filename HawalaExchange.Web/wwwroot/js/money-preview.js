// Display-only arithmetic. Authoritative decimal calculation/validation remains on the server.
const clean = value => String(value ?? '').replace(/[۰-۹٠-٩]/g, c => String(c.charCodeAt(0) - (c >= '۰' ? 0x6f0 : 0x660)))
    .replace(/[,٬\s]/g, '').replace(/٫/g, '.');
function decimal(value) {
    const text = clean(value);
    if (!/^-?\d{1,29}(?:\.\d{0,28})?$/.test(text)) return null;
    const [whole, fraction = ''] = text.split('.');
    return { n: BigInt(whole + fraction), d: 10n ** BigInt(fraction.length) };
}
function rounded(n, d, even = false) {
    const sign = n < 0n ? -1n : 1n;
    n *= sign;
    let whole = n / d;
    const remainder = n % d;
    if (remainder * 2n > d || (remainder * 2n === d && (!even || whole % 2n !== 0n))) whole++;
    return whole * sign;
}
const base = (from, to) => from.priority < to.priority ||
    (from.priority === to.priority && from.code.toUpperCase() < to.code.toUpperCase());
const positive = value => value && value.n > 0n;
const grouped = value => String(value).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
export function convertPreview(amount, rate, from, to) {
    const a = decimal(amount), r = decimal(rate);
    if (!positive(a)) return null;
    if (from.id === to.id) return rounded(a.n, a.d).toString();
    if (!positive(r)) return null;
    return (base(from, to) ? rounded(a.n * r.n, a.d * r.d) : rounded(a.n * r.d, a.d * r.n)).toString();
}
export function quotePreview(fromAmount, toAmount, from, to) {
    const a = decimal(fromAmount), b = decimal(toAmount);
    if (!positive(a) || !positive(b)) return null;
    if (from.id === to.id) return '1';
    const n = base(from, to) ? b.n * a.d : a.n * b.d;
    const d = base(from, to) ? b.d * a.n : a.d * b.n;
    const result = rounded(n * 100000000n, d, true).toString().padStart(9, '0');
    return `${result.slice(0, -8)}.${result.slice(-8)}`.replace(/\.?0+$/, '') || '0';
}
const ones = ['', 'یک', 'دو', 'سه', 'چهار', 'پنج', 'شش', 'هفت', 'هشت', 'نه'];
const teens = ['ده', 'یازده', 'دوازده', 'سیزده', 'چهارده', 'پانزده', 'شانزده', 'هفده', 'هجده', 'نوزده'];
const tens = ['', '', 'بیست', 'سی', 'چهل', 'پنجاه', 'شصت', 'هفتاد', 'هشتاد', 'نود'];
const hundreds = ['', 'صد', 'دو صد', 'سه صد', 'چهار صد', 'پنج صد', 'شش صد', 'هفت صد', 'هشت صد', 'نه صد'];
const scales = ['', 'هزار', 'میلیون', 'میلیارد', 'تریلیون', 'کوادریلیون', 'کوینتیلیون', 'سکستیلیون', 'سپتیلیون', 'اکتیلیون'];
const fractions = ['', 'دهم', 'صدم', 'هزارم', 'ده هزارم', 'صد هزارم', 'میلیونم', 'ده میلیونم', 'صد میلیونم'];
function integerWords(value) {
    if (!value) return 'صفر';
    const groups = [];
    for (let scale = 0; value > 0n; scale++, value /= 1000n) {
        const group = Number(value % 1000n);
        if (!group) continue;
        const parts = [], remainder = group % 100;
        if (group >= 100) parts.push(hundreds[Math.floor(group / 100)]);
        if (remainder >= 10 && remainder < 20) parts.push(teens[remainder - 10]);
        else {
            if (remainder >= 20) parts.push(tens[Math.floor(remainder / 10)]);
            if (remainder % 10) parts.push(ones[remainder % 10]);
        }
        groups.unshift(parts.join(' و ') + (scales[scale] ? ' ' + scales[scale] : ''));
    }
    return groups.join(' و ');
}
export function moneyWords(value) {
    const parsed = decimal(value);
    if (!parsed) return '';
    const [whole, fraction = ''] = clean(value).replace(/^-/, '').split('.');
    const digits = fraction.replace(/0+$/, '');
    const words = integerWords(BigInt(whole)) + (digits ? ` و ${integerWords(BigInt(digits))} ${fractions[digits.length] || 'جزء اعشاری'}` : '');
    return (parsed.n < 0n ? 'منفی ' : '') + words;
}
const previewStates = new WeakMap();
export function refreshMoneyPreview(trigger, edited = false) {
    const scope = trigger?.closest?.('[data-live-finance]');
    if (!scope) { refreshStandaloneWords(trigger); return; }
    const input = key => scope.querySelector(`[data-live-amount="${key}"]`);
    const currency = key => {
        const select = scope.querySelector(`[data-live-currency="${key}"]`);
        const option = select?.selectedOptions?.[0];
        return option && select.value !== '0' ? { id: select.value, code: option.dataset.code || '',
            priority: Number(option.dataset.priority || 0), name: option.textContent.trim() } : null;
    };
    const amount = key => {
        const node = input(key), value = decimal(node?.value);
        return value && node.dataset.moneyDecimals === '0' ? rounded(value.n, value.d).toString() : node?.value || '';
    };
    const from = currency('from'), to = currency('to'), rate = input('rate');
    const set = (key, value, isRate = false) => {
        const node = input(key);
        if (node) {
            node.value = value === null ? '' : isRate ? value : grouped(value);
            node.dataset.moneyPreview = 'true';
        }
    };
    const state = previewStates.get(scope) || { source: scope.dataset.liveSource || 'from' };
    previewStates.set(scope, state);
    if (edited && ['from', 'to'].includes(trigger.dataset?.liveAmount)) state.source = trigger.dataset.liveAmount;
    const trade = scope.querySelector('[data-live-trade]');
    if (trigger === trade) state.source = trade.value === 'Buy' ? 'to' : 'from';
    if (from && to && rate) {
        const mode = scope.querySelector('[data-live-mode]:checked')?.value || 'RateToAmount';
        if (scope.dataset.liveFinance === 'hawala') {
            if (from.id === to.id) { set('to', convertPreview(amount('from'), '1', from, to)); set('rate', '1', true); }
            else if (mode === 'AmountToRate') set('rate', quotePreview(amount('from'), amount('to'), from, to), true);
            else set('to', convertPreview(amount('from'), rate.value, from, to));
        } else if (scope.dataset.liveFinance === 'exchange') {
            if (trade && edited && ['from', 'to'].includes(trigger.dataset?.liveAmount)) trade.value = state.source === 'from' ? 'Sell' : 'Buy';
            if (from.id === to.id) set(state.source === 'from' ? 'to' : 'from', null);
            else if (state.source === 'from') set('to', convertPreview(amount('from'), rate.value, from, to));
            else set('from', convertPreview(amount('to'), rate.value, to, from));
        }
    } else if (scope.dataset.liveFinance === 'hawala') set('to', null);
    else if (scope.dataset.liveFinance === 'exchange') set(state.source === 'from' ? 'to' : 'from', null);
    for (const output of scope.querySelectorAll('[data-live-words]')) {
        const key = output.dataset.liveWords;
        const curr = currency(key);
        const value = decimal(amount(key));
        const text = value && value.n > 0n ? moneyWords(amount(key)) : '';
        const content = text ? `${text}${curr ? ' ' + curr.name : ''}` : '';
        if (output.textContent !== content) output.textContent = content;
        output.hidden = !text;
    }
}

function refreshStandaloneWords(trigger) {
    // Each existing amount/currency group has its own words output, including opening-balance rows.
    let group = trigger?.parentElement;
    for (let depth = 0; group && depth < 5; depth++, group = group.parentElement) {
        const output = group.querySelector?.('[data-money-words]');
        if (!output) continue;
        const inputs = group.querySelectorAll('input[data-money-input]');
        if (inputs.length !== 1) return;
        const input = inputs[0], value = decimal(input.value);
        const amount = value && input.dataset.moneyDecimals === '0' ? rounded(value.n, value.d).toString() : input.value;
        const select = group.querySelector('select');
        const currency = select?.value !== '0' ? select?.selectedOptions?.[0]?.textContent.trim() : '';
        const words = value && value.n > 0n ? moneyWords(amount) : '';
        const text = words ? `${words}${currency ? ' ' + currency : ''}` : '';
        if (output.textContent !== text) output.textContent = text;
        output.hidden = !words;
        return;
    }
}
