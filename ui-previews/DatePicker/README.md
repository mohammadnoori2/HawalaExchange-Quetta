# Date picker preview

Links the actual shared PersianDatePicker source from the Web project, not a JavaScript
replica. No connection string, authentication, tenant data or main app startup is
used. Tenant 0 prevents AppDateDisplay from opening a database context.

From the repository root:

    dotnet run --project ui-previews/DatePicker --urls http://127.0.0.1:5292

Only public Web assets are served under `/assets`. Bound to loopback for local
testing. This project is not part of the production solution or Web publish.

## Field alignment preview

Open `/fields/accounts`, `/fields/customers`, or `/fields/hawalas`.
These routes preserve the AppPageHeader/header-stat markup and reuse the actual
filter bar, money input and Persian/Gregorian date picker. Every preview page has its refresh action in
the page header beside create/archive, including the original calendar page.

The list rows and form models are synthetic. Refresh recreates only the in-memory
fixture and preserves filters and drafts. Forms validate only; they never post.
The sample dialogs keep their headers/footers reachable and reserve inline error
space. All field alignment styles are isolated to `wwwroot/preview-fields.css`;
no production header/refresh component or global CSS is changed by this demo.

UI/UX Pro Max: verified targeted UX matches for field error placement, blur-time
validation and focusable error feedback. Preserve the established blue palette,
compact header/stat layout, local Vazirmatn, and the approved one-piece date field.
