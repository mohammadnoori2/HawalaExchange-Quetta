# Quetta edition implementation plan

## Delivery rules

1. Implement one stage on a `codex/stage-*` branch.
2. Build and run the relevant automated checks locally.
3. Provide a focused manual test checklist.
4. Fix findings within the same stage.
5. Push or merge only after explicit customer approval.

## Confirmed business rules

- The baseline is commit `49a2481` from the original repository.
- The new repository, database, runtime settings, uploads, and migrations stay independent.
- Calculated results default to zero decimal places.
- Rounding uses half-up behavior: a discarded digit of 5 or more rounds away from zero; below 5 rounds toward zero.
- Calculation precision and rounding mode should be configurable, with zero decimals and half-up as defaults.
- A correspondent has a configurable commission method:
  - `PerTransaction`: commission may be entered on each transaction.
  - `PeriodicPerLakh`: transaction commission is not entered; uncalculated incoming hawalas are commissioned in a periodic batch.
- Periodic commission is available to any correspondent configured for it, not only Quetta.
- The default periodic rate is AFN 200 per AFN 100,000 and remains editable.
- Periodic commission is converted to USD and posted to the correspondent account only after preview and confirmation.
- All non-USD transactions or balances are settled to USD at the end of the day without overwriting original currency facts.
- Payment locations are geography, not financial accounts or correspondents.
- Imported locations are normalized and matched automatically; unmatched locations require preview confirmation before creation.
- Payout agents and their commission are selected when a hawala is executed.
- AED transactions are available globally and within a correspondent page, where the correspondent is preselected.
- AED uses a fixed base rate of 3.67 unless a later approved configuration rule changes it.
- AED markers use the same currency as the transaction amount.
- The AED page does not show the internal Dubai/Quetta adjustment breakdown to ordinary users.

## Stage 00: repository and runtime isolation

- Create an independent checkout from commit `49a2481`.
- Configure the new GitHub repository as `origin` and the original repository as `upstream`.
- Use `HawalaExchangeQuettaDb` in Development.
- Use development ports 5089 and 7128.
- Verify restore and build without changing the original checkout.

Acceptance: the project builds from the selected baseline and cannot connect to the original development database by default.

## Stage 01: payment locations and payout agents

- Decouple payment locations from the source correspondent.
- Normalize location names and prevent tenant-level duplicates.
- Support aliases such as spreadsheet spellings.
- Keep the incoming correspondent, payout location, and payout agent as separate concepts.
- Select the payout agent/account when executing a hawala.

Acceptance: a Quetta incoming hawala can target Ghazni and later be paid by an independently selected payout agent.

## Stage 02: bulk Excel import

- Import the confirmed seven-column, headerless layout: sequence, external reference, sender, receiver, payment location, amount, currency.
- Preview rows, validation errors, totals, duplicates, and location mappings before posting.
- Match known locations and propose unmatched locations for explicit creation.
- Store an import batch and prevent duplicate re-import.
- Post the batch atomically.

Acceptance: the supplied 42-row workbook reconciles by row count, currency count, and amount totals.

## Stage 03: payout accounting and agent commission

- Record the actual payout agent/account at execution.
- Keep principal and payout-agent commission separate.
- Post agent commission to an expense account.
- Support reversals and preserve audit history.

Acceptance: principal and agent commission produce distinct, balanced ledger effects.

## Stage 04: correspondent commission configuration and batches

- Add commission method selection to correspondent create/edit.
- Hide per-transaction commission for `PeriodicPerLakh` correspondents.
- Add periodic commission calculation to every eligible correspondent page.
- Select a period and include only valid, uncalculated incoming hawalas.
- Enter currency-to-AFN and USD-to-AFN rates, with snapshots.
- Calculate AFN commission per lakh, convert to USD, preview, confirm, post, and reverse.
- Link every commissioned hawala to its batch to prevent double counting.

Acceptance: weekly or monthly commission batches reconcile and cannot include a hawala twice.

Implementation status: completed locally on `codex/stage-04-periodic-commission` (awaiting customer test and approval before push).

Implemented calculation decisions:

- Include registered incoming hawalas in the selected date period, except cancelled hawalas and hawalas that already have a per-transaction commission.
- Use one user-entered source-to-AFN rate per currency for the batch and store that rate on every batch item as an immutable snapshot.
- Calculate partial lakhs proportionally (`AFN equivalent / 100,000 × commission per lakh`).
- Round the final AFN commission and final USD commission to whole units using half-up (`MidpointRounding.AwayFromZero`).
- Post only after preview and explicit confirmation; reversal creates an opposite accounting transaction and releases the hawalas for a future batch.

## Stage 05: correspondent balance settlement and complete daily journal

- Use each correspondent's configured settlement currency; do not force every correspondent to USD.
- Preview the correspondent's current non-settlement-currency balances and allow individual currency selection.
- Provide select-all and clear-all controls, then require one rate snapshot for every selected currency.
- Convert only the selected balances through the settlement clearing account and retain the original transaction facts.
- Leave unselected currencies unchanged and allow them to be converted in a later batch.
- Show every posted accounting operation in the daily journal while retaining the separate cash receipt and withdrawal sections.

Acceptance: selected balances become zero in their source currencies and are transferred to the correspondent's settlement currency with a balanced, traceable posting; unselected balances remain unchanged, and the journal includes cash and non-cash accounting documents.

## Stage 06: AED transactions

- Provide global AED transaction access.
- Provide correspondent-scoped access with the correspondent preselected.
- Support the 3.67 base rate and same-currency markers.
- Calculate the final USD amount and profit using configurable precision and rounding.
- Restrict internal marker breakdown fields by UI and permission requirements.
- Support holding, full conversion, partial conversion, and reversal if confirmed in the stage review.

Acceptance: known examples reconcile under zero-decimal half-up rounding and authorized users see only approved fields.

## Stage 07: reports, permissions, and audit

- Report import batches, payout locations, payout agents, agent commission, correspondent commission, daily settlements, AED transactions, and profit.
- Add appropriate permissions for calculation, confirmation, reversal, rate changes, and profit visibility.
- Export approved reports to Excel/PDF.

Acceptance: reports reconcile to ledger entries and sensitive profit/marker fields obey permissions.

## Stage 08: hardening and delivery

- Run migration tests against an empty database.
- Add concurrency, duplicate-submission, rounding, and reversal tests.
- Verify backup/restore and production configuration.
- Prepare installation and operator documentation.

Acceptance: a clean environment can be installed, migrated, tested, and operated without the original repository or database.

## Decisions to confirm during the relevant stage

- Revisit whether eligibility should change from all registered incoming hawalas to paid-only after customer testing.
- Revisit whether future daily closing should replace the batch rate snapshot with an end-of-day rate per hawala.
- Which roles may see or edit the internal AED markers and profit.
- Whether end-of-day processing is settlement only or also locks the accounting date.
