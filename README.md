# HawalaExchange Quetta

Customer-specific edition of HawalaExchange for Quetta-origin remittances, bulk Excel import, payout-agent accounting, periodic correspondent commission, daily USD settlement, and AED transactions.

## Local isolation

- Source repository: `https://github.com/mohammadnoori2/HawalaExchange-Quetta.git`
- Upstream reference: `https://github.com/kabulcodelab/HawalaExchange.git`
- Development database: `HawalaExchangeQuettaDb`
- Development URLs: `http://localhost:5089` and `https://localhost:7128`

The original HawalaExchange checkout and database are not used by this edition.

## Run locally

```powershell
dotnet restore HawalaExchange.slnx
dotnet build HawalaExchange.slnx
dotnet run --project HawalaExchange.Web
```

## Delivery workflow

Work is delivered in independent stages. Each stage is built and tested locally, reviewed by the customer, and pushed only after explicit approval.

See `docs/IMPLEMENTATION_PLAN.md` for scope, business rules, and acceptance checks.
