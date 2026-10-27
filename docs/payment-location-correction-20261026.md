# Imported hawala payment location correction — 2026-10-26

The edit form and save path previously looked for a correspondent whose name matched the payment location. This rejected `lashkargah` even though its recorded responsible correspondent had an active account under a different name. The deployed database was inspected read-only to confirm that relationship; no production hawala or journal was edited.

Both paths now call the same tenant-scoped resolver. It selects the assignment valid today, using the existing inclusive start/exclusive end date rule, then finds an unarchived correspondent account. An explicit assignment takes precedence over a same-name correspondent. Expired/future assignment history cannot silently fall back to a different same-name owner. Name matching is retained only for legacy locations without assignment history. Inactive locations, inactive owners, missing accounts and ambiguous current owners produce validation errors. The new read operation is explicitly covered by the existing permission boundary.

The form clears the previous account/number while resolving a new location, reloads account details if the reference list is stale, and ignores late responses from earlier selections. Resolution and dependent number lookup are serialized within the form. Browser reference lookup and outgoing-number preview use separate read contexts, avoiding interference with concurrent balance readers; the save path continues to resolve the account within its original correction transaction. Changing the location of a pending hawala still does not execute it automatically.

## Accounting behavior

The existing imported-hawala correction transaction is retained. It posts compensating journals for the previous net effect, posts the new destination journals, updates the linked outgoing hawala and import row, and applies the existing commission/settlement correction rules. Physical deletion of historical journals is unnecessary. A failed correction rolls back the complete transaction.

Tests verify that the old destination loses this payment's net credit and the new responsible account receives it, the source correspondent is retained, the linked outgoing identity remains stable, and the original journal amounts and closed-period balance snapshots remain unchanged. Missing accounts leave the hawala, import row and journals unchanged. Existing imported amount/commission correction coverage also passed.

## Validation and delivery

- 42 UI/navigation/payment component tests passed, including new differently named owner, resolution failure, rapid location changes and pending-status cases.
- 11 targeted SQL Server integration tests passed using disposable databases, including correction before/after period closure, rollback, and reference lookups with an active reader and MARS disabled.
- 11 permission/catalog tests passed.
- The browser imported a real test workbook, changed an executed imported hawala to `lashkargah`, selected its differently named assigned account, saved, and reloaded the record. A separate SQL check confirmed the old destination net was zero and the new destination net was 100. Evidence: [browser results](payment-location-20261026/browser-results.json), [before save](payment-location-20261026/before-save.png), [saved record](payment-location-20261026/saved.png).
- Release output: `publish/payment-location-20261026`.

No production database writes, schema migration, or IIS deployment were performed. The complete publish includes previous responsive, receipt, operation-menu and account-search fixes. Preserve production configuration, `App_Data` and uploaded files when deploying it.
