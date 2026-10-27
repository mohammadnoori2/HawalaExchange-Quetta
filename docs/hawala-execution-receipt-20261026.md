# Hawala execution receipt — 2026-10-26

The execution receipt now includes the receiver's identity number, phone and address, alongside the existing name and father name. A receipt opened from the payment form uses the current receiver fields, including values populated by customer search. Automatic printing uses the successful payment result, retaining its saved identity details and execution time.

The hawala list, pending hawalas, received hawalas and correspondent panel all request the execution receipt after successful payment. The print component remains mounted after the payment form closes, prints once per executed hawala, and retains a manual print button. Rejected payments do not request receipts. Repeated execute clicks share the pending action rather than submitting it twice.

Execution receipts use a same-origin print frame so that asynchronous payment completion does not require a popup window. The frame is removed after printing or closing the print dialog. Other print callers retain their existing behavior. Printing failures display a retry message and do not replay or reverse a payment. Customer values remain Razor encoded; no new endpoint, schema change or accounting posting is introduced.

## Validation

- 30 component/lifecycle tests passed, covering receipt content, encoding, automatic print timing, no repeated automatic print, print errors, and payment success/failure/repeated clicks across all four entry points.
- An isolated SQL Server browser fixture exercises customer selection, manual receipt printing and execution with one automatic receipt. The test blocks popups and intercepts native printing; it does not send output to a physical printer.
- Release publish succeeded in `publish/receipt-20261026`.

Evidence: [browser results](hawala-receipt-20261026/browser-results.json), [rendered execution receipt](hawala-receipt-20261026/execution-receipt.png).

The new publish output includes the earlier responsive changes. IIS has not been updated by this task. Deploy the complete output while preserving the production configuration, `App_Data` and uploaded files; no database migration command is required for this change.
