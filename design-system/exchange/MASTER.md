# Exchange operations UI preview

Status: the user rejected this initial redesign. Do not apply it to production.
Use `current-structure.md` and `exchange-current.*` for the revised, test-only
refinement that preserves the current application structure.

Scope: `HawalaExchange.Web/wwwroot/previews/exchange.*` only. No production
components, accounting services, authorization or database settings are changed.
The prototype is standalone HTML/CSS/JS with synthetic data and no network APIs.

UI/UX Pro Max was consulted. Both design-system searches returned an enterprise
marketing pattern and dark trading styling, unsuitable for the existing Persian
daytime accounting workspace. Do not treat those results as an approved palette.
Use the skill's general accessibility, form grouping, primary action, stable field
geometry, navigation consistency and progressive-disclosure guidance instead.

Direction: calm light workspace, local Vazirmatn font, existing blue family with
a restrained deeper cyan-blue action color. No gradients, entrance animation,
external dependencies, remote fonts or invented logo. The textual company mark
explicitly represents a fictional sample company, not a real tenant identity.
Desktop controls 38px; mobile 44px and 16px field text. Table cells centered,
English numerals, separate currency totals, textual status + semantic colors.
Actions follow row number, creation is at the right edge of the toolbar.

Native date fields are geometry prototypes, not a replacement for the production
Persian/Gregorian date component. The advanced filter explains this distinction.
Native modal dialog provides focus containment and Escape dismissal; bounded
flex form keeps its actions reachable while its body scrolls.

Interactions: synthetic in-memory filters, paging, selection and details; clipboard
copies synthetic rows, CSV download exports synthetic data. Mock posting/upload
dialogs always state that they do not write or execute. No form adds even a mock
financial transaction, to avoid implying a successful production posting.
