# Current-structure refinement prototype

The user rejected the previous `exchange.html` redesign. Do not apply its layout
or palette to production. This supersedes its direction in MASTER.md.

Scope: `wwwroot/previews/exchange-current.*`, synthetic data only. Baseline imports
the application's existing Bootstrap RTL and complete `styles/app.css` unchanged.
MainLayout/NavMenu, HawalaTransactions, PageHeaderStats, CompactFilterBar and the
customer table markup were inspected directly. The existing filter-fitting JS is
reused without modification. No backend starts and no database is accessed.

Retain the existing sidebar order and nested groups, logo, compact page header,
inline header counts, title/action positions, filter sequence and complete table
column order (including the operation column at the end). No new cards/topbar,
no combined sender/receiver columns, no removed commission/rate columns, and no
new product navigation. The comparison strip is prototype tooling, not an app UI.

UI/UX Pro Max targeted `content jumping form fields` UX search returned the Web
Content Jumping rule. Apply stable field geometry and count slots; keep existing
spacing tokens and blue palette. Add local Vazirmatn precedence in the proposed
mode, common control corners/heights, visible focus and legible semantic badges.
All proposals are under `body.preview-polished`, toggled with an A/B control.
Prototype dialog plumbing is separately scoped and present in both modes.

The preview provides the hawala list and customer list as representative examples;
other menu items explicitly explain that they are not implemented in this fixture.
The date text field matches the production wrapper structure. Its modal uses a
native date selector plus Persian equivalent solely for mock testing, not a claim
that the Blazor calendar component was ported. Financial operations remain inert.
