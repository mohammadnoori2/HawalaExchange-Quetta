# Stage 01 manual test checklist

## Before starting

Stop any earlier running instance, then start the Quetta edition again so the new migration is applied:

```powershell
cd D:\DailyWork\projects\HawalaExchange-Quetta
dotnet run --project HawalaExchange.Web
```

## Payment locations

1. Open **محل‌های پرداخت**.
2. Create a location named `غزنی` without selecting a correspondent.
3. Add aliases such as `غزنی شهر، مرکز غزنی`.
4. Edit the location and verify the aliases are preserved.
5. Try to create `  غزنی  ` or the Arabic-character equivalent. The duplicate must be rejected.
6. Create another location and verify it is also independent of correspondents.

Existing locations with the same normalized name are merged by the migration. Existing hawalas are moved to the oldest matching location before duplicate rows are removed.

## Hawala and payout account

1. Create an incoming hawala with Quetta as the source correspondent and Ghazni as the payment location.
2. Change the correspondent and verify the selected payment location is not cleared or filtered.
3. Save the hawala as pending.
4. Execute it and select the actual payer's account in **حساب پرداخت‌کننده**.
5. Open **مشاهده/جزئیات حواله** and verify it shows Quetta as correspondent, Ghazni as payment location, and the selected payer account separately.

## Expected boundaries

- This stage does not calculate payout-agent commission.
- This stage does not import Excel files.
- Those features are delivered in later stages after this stage is approved.
