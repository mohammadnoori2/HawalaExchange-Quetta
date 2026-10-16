# Platform administration design system

Scope: `/platform/**` and `/tenants` only. Blazor Server + Bootstrap RTL.
Business logic, financial calculations and authorization policies stay unchanged.

## Direction

Calm, task-first minimalism for long workdays. UI UX Pro Max's verified
Minimalism & Swiss Style recommendation applies; its marketing/landing-page
pattern and Latin-only font suggestions do not fit this Persian operations UI.
No GSAP, entrance animations, gradients, glass effects or new UI dependencies.
The disclosure search returned no verified relevant match after one retry;
grouped forms and secondary-action disclosures follow the skill's general
Forms & Feedback guidance, not a claimed search match.

## Brand and tokens

- Existing Kabul Code Lab logo: preserve proportions and asset unchanged.
- Brand accent: cyan `#27A9D9`, used decoratively, not white small-text buttons.
- Primary/action: `#086A91`; hover `#075675`; text on primary white.
- Canvas `#F4F7F9`, surface `#FFFFFF`, subtle surface `#F8FAFC`.
- Body text `#243746`; muted text `#526675`; border `#D6E0E7`.
- Success `#176447` / `#EAF6EF`; warning `#815000` / `#FFF6E4`;
  danger `#AA3434` / `#FFF0EF`. Always accompany state color with text.
- Local Vazirmatn for all platform text, including navigation, forms, tables,
  dialogs and audit details; Tahoma/sans-serif only if the font fails to load.
  Icon pseudo-elements retain their icon font. Body 15–16px, line-height 1.7;
  clear title hierarchy and tabular figures. No additional external font calls.
- User-requested 80% sizing above 768px: the platform root is 12.8px instead of
  16px; component sizes, spacing, icons and borders use rem so they scale together.
  Based on the compact layout, this gives body 11.2px, controls about 30.4px and
  sidebar 182.4px. This smaller typography is an explicit user preference.
  Mobile remains at 16px root with the original 44px controls and 16px input text.
  Do not use CSS zoom or transforms; viewport height limits and modal body scrolling
  must continue to fit the actual screen rather than shrinking the viewport itself.
- Cards 12px radius, thin border, minimal shadow. No movement on hover.

## Interaction

Group platform navigation by task; exact-match dashboard, visible active link.
Keep one primary action per section. User rows show edit and a secondary native
disclosure for security/account actions. Preserve all confirmations and backend
guards. Destructive controls are separated and red, not primary actions.
Tenant context remains visible above local section navigation. Native section
buttons expose current state and control their panel; they are not fake ARIA tabs.
Search submits explicitly (Enter or button), avoiding a SQL request per keystroke.
Permission groups expand on demand; checkboxes still enforce existing View rules.
Dialogs constrain their height to the dynamic viewport. EditForm and optional
busy fieldsets propagate that constraint with flex/min-height:0; only the body
scrolls, keeping the header and action footer reachable at small window heights.
Subscription expiry is shown as calendar days remaining/elapsed in Kabul time,
with explicit same-day and missing-date wording and English digits. The exact
end date remains in the accessible label/title and in editable date controls.
Reports/export freeze relative wording at report generation time. Keep suspended,
cancelled and trial statuses explicit; display formatting never changes access rules.

## Accessibility and verification

Visible focus, semantic buttons/labels, status/error announcements, no color-only
statuses. Layout at 375/768/1024/1440px, keyboard operation and 200% zoom.
Tables may scroll inside labelled regions; page itself must not overflow.
Respect reduced motion. Print hides chrome and preserves report content.
Use a local test database for visual/functional checks; never start against the
live local database as part of UI verification.
