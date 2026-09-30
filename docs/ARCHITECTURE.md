# Beacon: Architecture

How Beacon is built and where each kind of logic lives. This file describes the present;
the reasons behind settled choices are in DECISIONS.md.

## Overview

Beacon is a self-hosted personal finance dashboard. Users upload bank statement PDFs,
salary slip PDFs and grocery receipts; the app extracts the transactions, categorises them
with rules and charts spending. It runs on a home Ubuntu server behind Nginx and is reached
over Tailscale (ADR-001).

A request goes from the Angular client to `/api/*` with the `X-Api-Key` header, through
`ApiKeyMiddleware` and `ExceptionHandlingMiddleware`, to a controller that calls one feature
handler, which works on SQL Server through EF Core. For an upload, `PdfExtractorService` runs
`scripts/pdfExtractor.py` (pdfplumber) to get the page text, a parser turns it into domain
objects, and `ParseVerifier` checks the result before it is saved.

## Stack

| Layer | Technology |
|---|---|
| PDF extraction | Python 3 + `pdfplumber` |
| Backend API | ASP.NET Core 8 (.NET 8) |
| Database | SQL Server + EF Core 8 (code-first) |
| Frontend | Angular 21 (standalone components, signals) |
| Charts | chart.js 4.5 |
| Testing (backend) | xUnit + EF Core InMemory |
| Testing (frontend) | Vitest 4 |
| Formatting | Prettier 3.8 |

## Repository layout

```
beacon/
├── api/
│   ├── Beacon.Api/
│   │   ├── Controllers/          # REST endpoint handlers
│   │   ├── Data/                 # AppDbContext (EF Core)
│   │   ├── Features/             # Feature-driven CQRS (see below)
│   │   │   ├── Backup/
│   │   │   ├── Categories/
│   │   │   │   └── Shared/       # ApplyRuleService
│   │   │   ├── Groceries/
│   │   │   │   └── Shared/       # GroceryApplyRuleService
│   │   │   ├── GroceryCategories/
│   │   │   ├── Investments/
│   │   │   │   └── Shared/       # SavingsPlanImportService
│   │   │   ├── Salary/
│   │   │   ├── Shared/           # ExcludedCategory, ProtectedEntityHelper, ValidationExtensions
│   │   │   ├── Statements/
│   │   │   ├── Transactions/
│   │   │   └── Upload/           # UnifiedUploadBatch (multi-type batch upload)
│   │   ├── Middleware/           # ApiKeyMiddleware, ExceptionHandlingMiddleware
│   │   ├── Migrations/           # EF Core generated migrations
│   │   ├── Models/               # Domain entities
│   │   ├── Services/             # Upload services, storage, Google, Alpha Vantage, PDF extractor
│   │   │   └── Parsing/          # Bank, salary & grocery parsers; MealCardTextParser; ParseVerifier
│   │   ├── Validation/           # ValidationResult
│   │   ├── appsettings.template.json
│   │   └── Program.cs            # DI registration, middleware pipeline, startup seeding and PDF cleanup
│   └── Beacon.Tests/             # xUnit test project (EF InMemory)
│       ├── Handlers/             # CQRS handler tests
│       ├── Middleware/           # Middleware tests
│       ├── Parsing/              # Parser tests (bank, salary slip, grocery)
│       ├── Services/             # Service-level tests
│       └── Validation/           # Validator tests
├── web/
│   └── src/app/
│       ├── core/
│       │   ├── components/       # Shared components
│       │   ├── constants/        # Shared constants (e.g. category colours)
│       │   ├── interceptors/     # apiKeyInterceptor (adds X-Api-Key header)
│       │   ├── models/           # TypeScript interfaces
│       │   ├── services/         # finance, categories, salary, groceries, grocery-categories, calendar, tasks, google-auth, investments
│       │   └── utils/            # date-utils, http-params, rule-match
│       ├── pages/                # Lazy-loaded routed components
│       │   ├── analytics/
│       │   ├── calendar/         # incl. event-modal + task-modal components
│       │   ├── dashboard/
│       │   ├── investments/
│       │   ├── rules/
│       │   ├── salary/
│       │   ├── settings/
│       │   ├── transactions/
│       │   └── upload/
│       ├── app.ts                # Root component + navigation
│       ├── app.routes.ts         # Route definitions
│       └── app.config.ts         # Angular bootstrap config
├── scripts/
│   ├── pdfExtractor.py           # PDF → JSON page text (run by PdfExtractorService)
│   ├── requirements.txt          # pdfplumber
│   ├── deploy.sh                 # Build and run: --development (default), --production, --rollback
│   ├── reset-db.sh / .ps1        # Drop and recreate the local database (reads appsettings.json)
│   ├── run-backend.ps1           # Load local/environment.dev, apply migrations, start the API on :5098
│   ├── run-frontend.ps1          # Wait for the API, then ng serve on :4200
│   ├── run-backend-demo.ps1      # (WIP) API against the demo database (BeaconDemo, local/environment.demo)
│   ├── seed-demo.sql             # (WIP) Synthetic demo data
│   ├── seed-demo.ps1 / .sh       # (WIP) Seed the demo database (Windows, through SeedRunner / Linux server)
│   ├── SeedRunner/               # (WIP) Console app: runs a SQL file against a connection string
│   ├── setup.sh / .ps1           # Once per clone: git config core.hooksPath .githooks
│   └── readPdf.py                # Print a PDF's extracted text page by page (parser debugging)
├── docs/
│   ├── ARCHITECTURE.md           # This file
│   ├── DECISIONS.md              # Settled decisions (ADRs)
│   ├── ROADMAP.md                # What is planned, by theme
│   ├── tasks/                    # git-ignored: private task files, one per piece of work
│   └── screenshots/              # README images
├── .claude/                      # agents/ (scaffolders), skills/task/, settings.json (shared permissions)
├── .githooks/                    # commit-msg (subject rules), pre-push (protected and task branches)
├── .gitattributes                # Shell scripts and hooks stay LF on every platform
├── .vscode/                      # tasks.json ("Beacon: Start All"), launch.json
├── local/                        # git-ignored: environment.dev/.demo, uploads/, backups/, sample PDFs
├── .github/workflows/ci.yml      # Backend tests; frontend tests and production build
└── beacon.sln
```

## Backend

### Feature-driven CQRS

Each feature lives under `Features/{Feature}/`. Commands mutate state; queries read it. Each
use case is its own folder:

```
Features/Transactions/
├── Commands/
│   ├── SetTransactionCategory/
│   │   ├── SetTransactionCategoryCommand.cs   # Input DTO
│   │   ├── SetTransactionCategoryCommandHandler.cs
│   │   └── SetTransactionCategoryResponse.cs  # Output DTO (optional)
│   └── MarkTransfers/
└── Queries/
    └── GetTransactions/
```

Every handler is registered manually in `Program.cs` as `AddScoped<THandler>()` (ADR-002).

### Excluded transactions and grocery items

`IsExcluded` is what actually drops a row from spending/income totals and analytics. The
protected **`Excluded`** category (one in `Categories`, one in `GroceryCategories`, both
seeded in `Program.cs`) is *authoritative* for that flag (ADR-006): **every code path that
assigns a category goes through `ExcludedCategory.ApplyCategory`
(`Features/Shared/ExcludedCategory.cs`)**, which sets the flag when a row moves into the
Excluded category and clears it when the row moves out. It is generic over
`ICategorisedEntity`, implemented by both `Transaction` and `GroceryItem`; the two category
tables mean two id lookups (`GetIdAsync` / `GetGroceryIdAsync`) but one shared category name.

Call sites. Transactions: `SetTransactionCategory`, `UpdateTransaction`, `CreateTransaction`,
`MarkTransfers`, `ApplyRuleService`. Groceries: `SetGroceryItemCategory`,
`CreateGroceryItem`, `MarkGroceryItemsExcluded`, `GroceryApplyRuleService`,
`GroceryReceiptUploadService` (which also routes receipt-category **mappings** through it, so
mapping a receipt section to Excluded genuinely excludes its items on import). Never write
`.CategoryId = …` directly: a label and a flag that drift apart mean a row shows up as a
spending line labelled "Excluded".

A row can also be excluded with **no** category (Trade Republic savings-plan buys, set by
`StatementUploadService`); `ApplyCategory` deliberately leaves such a flag alone when a
category is later assigned, and only clears it for rows actually leaving the Excluded
category.

The frontend mirrors this defensively: `finance.service.ts` and `groceries.service.ts` each
treat a row as excluded when `isExcluded` **or** its category is named `Excluded`, so a
stale row can never be counted. On the grocery side the raw `allItems` signal keeps
everything (the item list shows excluded rows) and **`countedItems` is the one to use for any
total or chart**, mirroring `allTransactionsRaw` vs `allTransactions`. The Excluded category
is never offered as a plain category pick (`assignableCats` / `gAssignableCats` on the
transactions page), since excluding is its own action, but it stays in *filter* dropdowns so
excluded rows remain findable. Rules *may* target it; both rule services set the flag when
they match.

There is no `Internal Transfer` category. It was the pre-rename name of this concept;
migration `MergeInternalTransferIntoExcluded` folds any surviving rows, rules and
transactions into `Excluded` (marking them excluded) and deletes it. Do not re-introduce it.

### Bank statement parsers

All parsers implement `IBankStatementParser`:

```csharp
public interface IBankStatementParser
{
    string BankName { get; }
    bool CanParse(string fullText);
    ParsedStatement Parse(string fileName, IReadOnlyList<string> pages);
}
```

`BankStatementParserFactory` auto-discovers registered parsers via DI (ADR-004). **To add a
new bank:**

1. Create `Services/Parsing/MyBankParser.cs` implementing `IBankStatementParser`.
2. Register in `Program.cs`: `builder.Services.AddSingleton<IBankStatementParser, MyBankParser>();`
3. No other changes needed: detection is automatic.

Meal-card statements are imported as pasted text, not PDF (ADR-010): `MealCardTextParser`
(static class, not DI-registered) parses the raw text, and the `ImportMealCardText` command
(`POST /api/statements/import-text`) stores the result under bank name `MEAL CARD`. The
closing balance is optional: when left empty it is derived from the period-adjacent previous
meal-card statement (previous closing + credits − debits); with no adjacent previous
statement the import is rejected and the user must supply it. Backfilling before an existing
statement returns a warning that the later statement's balance was not recomputed.

**Statement uploads are EUR-only** (ADR-005): `StatementUploadService` rejects any parsed
statement whose currency is not EUR (non-EUR Revolut exports get a specific error from the
parser itself). All aggregates assume EUR.

### Salary slip parsers

All salary slip parsers implement `ISalarySlipParser`:

```csharp
public interface ISalarySlipParser
{
    string ParserName { get; }
    bool CanParse(string fullText);
    ParsedSalarySlip Parse(string fileName, IReadOnlyList<string> pages);
}
```

`SalarySlipParserFactory` auto-discovers registered parsers via DI. `ParsedSalarySlip` stores
only financial data (employer name/NIF, period, gross, net, base, hours, rate, especie, line
items), no personal data.

| Parser | Detection signal | Format notes |
|---|---|---|
| `CentralGestParser` | `"CentralGest Software"` footer | Two-column (original+duplicate); mixed PT/US number formats |
| `DomirestParser` | `"DOMIREST"` company name | Stacked original+duplicate; PT number format |
| `Micro1InvoiceParser` | `"Micro1 Inc."` (USD invoice) | **Not** factory-registered; paired with a Deel withdrawal → EUR (see below) |

**To add a new salary slip parser:**

1. Create `Services/Parsing/MyFormatParser.cs` implementing `ISalarySlipParser`.
2. Register in `Program.cs`: `builder.Services.AddSingleton<ISalarySlipParser, MyFormatParser>();`
3. No other code changes needed: the factory picks up the parser automatically.

#### micro1: two PDFs, auto-paired, EUR from USD

A micro1 paycheck arrives as **two** PDFs that only together form a EUR slip (ADR-007): a
**USD invoice** (`Micro1InvoiceParser`, detects `"Micro1 Inc."`; produces USD gross/net +
`Base Pay`/`Other` income items) and a Deel **"Confirmation Statement"**
(`DeelWithdrawalParser`, plain class, detects `"Deel transaction ID"`; yields
`DeelWithdrawal(SourceAmountUsd, ExchangeFeeUsd, ExchangeRate, TotalEur)`). Neither is a
complete slip alone, so **`Micro1InvoiceParser` is deliberately NOT registered in
`SalarySlipParserFactory`** (registered as a concrete singleton only): a lone invoice can
never be imported as fake EUR.

The pairing happens **inside `UnifiedUploadBatchCommandHandler`** (the multi-file bulk-upload
handler), not via the `/api/salary/*` endpoints: it classifies each file once, then
correlates invoices and withdrawals by **cent-equal USD amount** (invoice `Total USD` ==
withdrawal `Source amount`). A matched pair becomes one `SalarySlip` result (stored under the
invoice PDF); an unpaired or amount-ambiguous micro1 file is emitted as a **`Micro1Unpaired`**
result, flagged and **never imported** (shown in a dedicated blocked section on the upload
page).

`Micro1Reconciler.Reconcile(invoiceUsd, withdrawal)` converts USD → EUR at Deel's real rate:
`gross = round(totalUsd × rate)`, `basePay = round(basePayUsd × rate)`,
`other = gross − basePay` (absorbs rounding), `net = withdrawal.TotalEur` (the EUR that
actually arrived), and the shortfall `fee = gross − net` is booked as a **`Deel exchange fee`
deduction** so `ParseVerifier.VerifySalarySlip` reconciles.

The invoice summary line (`Other: Project → … | Hours → … | Pay Rate → … | Base Pay → …`)
**omits the `| Other → $x` segment entirely** when the paycheck is base pay only, so that
segment is optional: when it is missing the parser derives `other = total − basePay` (also
folding in any segment it does not recognise) and emits **no `Other` line item** when that is
zero. For such a base-pay-only invoice the reconciler puts the whole EUR gross in `Base Pay`
rather than converting base and gross separately, so FX rounding cannot leak out as a stray
one-cent `Other` the user would have to categorise. Header wording also varies between
invoices (`Invoice #`/`Sub total` vs `Document`/`Subtotal`): no regex may depend on it.

#### Profiles, categories and the parse flow

Each `SalaryProfile` stores an `HourlyRateFormula` (`hours` | `workdays` | `days`, default
`days`) selecting how the true hourly rate is computed on the salary page; it is editable in
the profile modal.

**Categories are per profile** (ADR-009). Each `SalaryProfile` has its own isolated
`SalaryItemCategories`. The `GET /api/salary/item-categories?profileId=X` endpoint requires a
`profileId`.

Salary parse flow:

1. `POST /api/salary/upload-pdf`: stores the PDF, returns `pdfPath` (the stored file name).
2. `POST /api/salary/parse-pdf`: accepts `{ pdfPath }`, resolves it under the storage root and
   returns pre-filled financial data for review.
3. `POST /api/salary/slips`: the user submits the reviewed data to persist.

#### Merging a second pay run into a month

`SalarySlips` has a unique `(SalaryProfileId, Period)` index and `Period` is always the 1st
of the month, so a cycle that pays **twice a calendar month** (micro1/Deel invoices each
half-month) cannot create two rows (ADR-008). `POST /api/salary/slips/{id}/merge`
(`MergeSalarySlipCommandHandler`) folds a second pay run into the existing slip instead:
gross/net/base/hours/`TotalEspecie` are summed null-safely, `HourlyRate` is re-averaged
**weighted by hours** (it is a rate, not a total), line items are combined **per category**
(one `Base Pay` line per month, new categories appended after the existing `SortOrder`), and
per-unit detail (`UnitValue`, `Percentage`) survives only when both sides agree. `Period` and
`SalaryProfileId` are never touched, and incoming line-item categories are validated against
the target slip's profile exactly as in `CreateSalarySlip`. A slip holds **one** PDF: the
first stays authoritative and a superseded second PDF is deleted from storage rather than
orphaned, with both file names kept in `SourceFile` (`"a.pdf; b.pdf"`).

The upload page drives this: when the slip review modal opens it re-fetches that profile's
slips (an earlier review *in the same batch* may have just created the slip this one merges
into) and shows a merge notice with the combined totals plus an "Add to existing slip"
toggle. Unticking it blocks the save client-side rather than letting the server reject it.
`CreateSalarySlip` keeps its duplicate guard: merging is always explicit, never a silent
fallback.

### Grocery receipt parsers

All grocery receipt parsers implement `IGroceryReceiptParser`:

```csharp
public interface IGroceryReceiptParser
{
    string ParserName { get; }
    bool CanParse(string fullText);
    ParsedGroceryReceipt Parse(string fileName, IReadOnlyList<string> pages);
}
```

`GroceryReceiptParserFactory` auto-discovers registered parsers via DI.

| Parser | Detection signal | Format notes |
|---|---|---|
| `ContinenteParser` | `"Modelo Continente"` in receipt text | VAT-prefixed items; multi-quantity lines; NS deposit items; category headers ending with `:` |

**To add a new grocery receipt parser:**

1. Create `Services/Parsing/MyStoreParser.cs` implementing `IGroceryReceiptParser`.
2. Register in `Program.cs`: `builder.Services.AddSingleton<IGroceryReceiptParser, MyStoreParser>();`
3. No other changes needed: the factory picks up the parser automatically.

### Parse verification

`Services/Parsing/ParseVerifier.cs` is a static post-parse sanity-check layer that returns
user-facing warnings: `VerifyStatement` (opening + credits − debits vs closing balance),
`VerifySalarySlip` (line-item sums vs gross/net), `VerifyGroceryReceipt` (item sum vs receipt
total). Called from `StatementUploadService`, `ParseSalarySlipCommandHandler`,
`GroceryReceiptUploadService` and `UnifiedUploadBatchCommandHandler`.

### PDF storage

`FileStorageService` (singleton) keeps uploaded PDFs in one flat folder, `Storage__Path`
(default `statements/` next to the binaries), as `<guid>.pdf`.

- `PdfPath` (`MonthlyStatements`, `SalarySlips`, `GroceryReceipts`) holds only the file name
  (ADR-023). `SaveAsync` writes the file and returns its name, which the upload flows store and
  `upload-pdf` returns to the client; no absolute server path leaves the API. Create, update
  and merge of a salary slip store `FileNameOf` whatever `pdfPath` the client sends.
- `GetFullPath`, `GetFile` and `Delete` resolve a file name (or a legacy absolute path) under
  the storage root and refuse anything that resolves outside it. Anything that opens a stored
  file goes through them: `parse-pdf` hands the extractor `GetFullPath(pdfPath)`.
- Older data: the `StorePdfPathsAsFileNames` migration cut existing rows down to their file
  names, and a backup restore does the same to every restored `PdfPath`
  (`RestoreBackupCommandHandler.StorePdfPathsAsFileNames`), so an old backup cannot bring
  absolute paths back.
- At startup `Program.cs` runs `OrphanedPdfCleanup` (scoped, in `Services/`) when
  `Storage__Path` is set: every `*.pdf` in the storage root that no `PdfPath` references and
  that is older than 24 hours is deleted; a file that cannot be deleted is logged and
  skipped. A row references a file by the name its `PdfPath` ends in
  (`FileStorageService.FileNameOf`, which splits on both `/` and `\`), so a relative path, an
  absolute path under the root and an absolute path written on another machine (a restored
  backup) all protect their file.

### Investments

Entities: `InvestmentAsset` (`AssetType` is `ETF` or `Gold`; optional `Isin` with a filtered
unique index, used to match auto-imported holdings), `InvestmentLot` (signed `Quantity`
`decimal(18,6)`: positive = buy, negative = sell), `InvestmentPriceSnapshot` (unique
`(AssetId, Date)` index). Endpoints live in `Controllers/InvestmentsController.cs` under
`/api/investments`; handlers follow the tuple-result pattern `(Result?, Error?)` where
`(null, null)` maps to 404.

Conventions:

- **Gold is tracked in grams** (ADR-011): `Quantity` = grams, `PricePerUnit` = EUR/gram.
  Alpha Vantage removed XAU from its currency endpoints, so gold is priced via an EUR-listed
  physical gold ETC proxy (`AlphaVantage__GoldProxyTicker`, default `4GLD.DEX` = Xetra-Gold,
  1 unit = 1 gram → quotes are already EUR/gram, no troy-ounce conversion). The proxy trades
  at a small premium/discount to spot.
- **ETFs are assumed EUR-listed (UCITS)**: quotes are stored as EUR with no FX conversion. Do
  not add non-EUR-listed tickers.
- Sells are validated against net holdings (server and client); editing a lot preserves its
  buy/sell sign.
- P&L uses **average cost basis** (ADR-013), computed client-side in `investments.service.ts`
  (`assetMetrics`): buys update the weighted average (fees included), sells book realised
  P&L against it.
- **Trade Republic savings-plan auto-import** (ADR-014): `SavingsPlanImportService`
  (`Features/Investments/Shared/`, scoped) runs after `StatementUploadService` persists a
  `TRADE REPUBLIC` statement. It turns each `Savings plan execution` row (already excluded
  from spending) into an `InvestmentLot`: ISIN + quantity parsed from the description,
  `PricePerUnit = amount / quantity`, `Fees = 0`. The ETF asset is created on first sight,
  matched by `Isin`, with `Ticker` left null (the user sets it to enable Alpha Vantage
  pricing; e.g. `VWCE.DEX` for `IE00BK5BQT80`). Idempotent: lots dedup by
  `(AssetId, Date, Quantity)`, and import failures are caught so they never fail the upload.

Pricing (`AlphaVantageService`, free tier 25 requests/day, ADR-012):

- `GLOBAL_QUOTE` for both ETFs (own ticker) and gold (proxy ticker).
- `POST /api/investments/assets/{id}/backfill` imports the full daily close history since the
  asset's earliest lot (`TIME_SERIES_DAILY`: own ticker for ETFs, proxy ticker for gold; one
  request per call, guarded against re-billing when history already reaches the first lot).
  Free-tier closes are unadjusted; splits/distributions can step the history.
- Malformed responses surface Alpha Vantage's `Error Message`/`Note`/`Information` fields as
  user-facing errors (invalid key, rate limit, bad ticker).
- `InvestmentPriceRefreshService` (hosted) refreshes all assets during US market hours,
  spacing requests 13 s apart and spreading `DailyQuota - ReservedForManual` across the
  session; the startup refresh is skipped when every asset already has a snapshot for today.
  All failures are caught and logged; the service can never stop the host.

### Google OAuth, Calendar and Tasks

`Services/GoogleOAuthService.cs` manages Google OAuth tokens (ADR-017). It stores a single
access + refresh token in the `GoogleOAuthTokens` table. Call `GetValidAccessTokenAsync()`
from any service that needs to call Google APIs; it handles token refresh automatically.

`/api/auth/google/callback` is **exempt from API key validation** (the redirect comes from
Google's servers, with no `X-Api-Key` header). All other `/api/auth/google/*` endpoints
require the key as normal.

`Services/GoogleCalendarService.cs` wraps the Google Calendar REST API v3
(`https://www.googleapis.com/calendar/v3/calendars/primary/events`). Methods:
`GetEventsAsync`, `CreateEventAsync`, `UpdateEventAsync`, `DeleteEventAsync`. Endpoints:
`Controllers/CalendarController.cs` at `/api/calendar/events` (GET with `?start=&end=`,
POST, PUT `/{id}`, DELETE `/{id}`).

`Services/GoogleTasksService.cs` wraps the Google Tasks REST API v1
(`https://tasks.googleapis.com/tasks/v1/`) with the same auth. Methods: `GetTaskListsAsync`,
`GetTasksAsync`, `CreateTaskAsync`, `UpdateTaskAsync`, `DeleteTaskAsync`. Endpoints:
`Controllers/TasksController.cs` at `/api/tasks` (GET lists at `/lists`, GET/POST tasks, PUT
`/{id}`, DELETE `/{id}?listId=`).

Tasks are displayed **in the Calendar page**: tasks with a due date appear as chips on the
calendar grid, and all tasks (including undated ones) appear in a panel below the grid. No
separate route exists for tasks.

One-time setup:

1. Create a Google Cloud project and enable the Calendar and Tasks APIs.
2. Create OAuth 2.0 Web Client credentials.
3. Add the redirect URI to `appsettings.json` under `GoogleServices:RedirectUri`.
4. Set `GoogleServices:ClientId` and `GoogleServices:ClientSecret`.

For local development use `http://localhost:5098/api/auth/google/callback`; for Tailscale
access use `http://<tailscale-ip>:5098/api/auth/google/callback`. Both can be registered in
Google Cloud Console at the same time.

### DI lifetimes

| Service type | Lifetime |
|---|---|
| Parsers, `BankStatementParserFactory`, `SalarySlipParserFactory`, `GroceryReceiptParserFactory`, `Micro1InvoiceParser`, `DeelWithdrawalParser` (concrete singletons, not factory-registered), `FileStorageService` | Singleton |
| Feature handlers, `PdfExtractorService` (as `IPdfExtractor`), `StatementUploadService`, `GroceryReceiptUploadService`, `OrphanedPdfCleanup`, `ApplyRuleService`, `GroceryApplyRuleService`, `SavingsPlanImportService`, `GoogleOAuthService`, `GoogleCalendarService`, `GoogleTasksService`, `AlphaVantageService` | Scoped |
| `InvestmentPriceRefreshService` | Hosted service (`AddHostedService`) |
| `MealCardTextParser`, `ParseVerifier` | Static classes, not registered in DI |
| `AppDbContext` | Scoped (EF default) |

## Frontend

### Signal-based state

`FinanceService` (`core/services/finance.service.ts`) is the single source of truth
(ADR-016):

- `statements`: writable signal holding all loaded statements (plus `loading` / `error` signals).
- Computed signals: `banks`, `latestPerBank`, `totalBalance`, `allTransactions`,
  `allTransactionsRaw`, `monthlySummaries`.
- Call `reload()` after any mutation to refresh state.

All pages are lazy-loaded standalone components via `app.routes.ts`. No NgModules.

### HTTP authentication

`core/interceptors/api-key.interceptor.ts` injects `X-Api-Key: <apiKey>` on every request
whose URL starts with `/api`. The interceptor is registered in `app.config.ts`.

## Database

Schema (17 tables): `MonthlyStatements`, `Transactions`, `Categories`, `CategoryRules`,
`SalaryProfiles`, `SalarySlips`, `SalaryItemCategories`, `SalaryLineItems`,
`GroceryReceipts`, `GroceryItems`, `GroceryCategories`, `GroceryCategoryRules`,
`GroceryReceiptCategoryMappings`, `GoogleOAuthTokens`, `InvestmentAssets`, `InvestmentLots`,
`InvestmentPriceSnapshots`.

`AppDbContext` is in `Data/AppDbContext.cs`; add a `DbSet<T>` there for a new entity, then a
migration:

```bash
cd api/Beacon.Api
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

At startup `Program.cs` seeds default data (including the protected Excluded categories)
and then runs the PDF cleanup (see "PDF storage").

## API surface

All endpoints require the `X-Api-Key` header, except `/swagger` in development and
`GET /api/auth/google/callback`. Use Swagger (`http://localhost:5098/swagger`) or read
`Controllers/` for the full surface.

## Supported banks

| Bank | Detection signal |
|---|---|
| ActivoBank | BIC `ACTVPTPL` or "EXTRATO COMBINADO" |
| BPI | SWIFT `BBPIPTPL` or "EXTRACTO INTEGRADO" |
| Revolut | BIC `REVOPTP2` or "Revolut Bank UAB" |
| Trade Republic | BIC `TRBKPTP2` or "TRADE REPUBLIC BANK GMBH" |

Trade Republic statements use a jumbled multi-line table layout (each row spans a date line,
a money line and a year line; the type column can wrap), so `TradeRepublicParser` is
block-based rather than single-line-regex. Savings-plan ETF buys (`Savings plan execution …`)
stay debits so the balance reconciles, but `StatementUploadService` sets `IsExcluded = true`
on them (guarded to bank `TRADE REPUBLIC`) so they drop out of spending: they are
cash→investment transfers. `SavingsPlanImportService` then imports them as `InvestmentLot`s
(see "Investments").

Meal-card statements have no PDF parser: they are imported as pasted text (see "Bank
statement parsers") and stored under bank name `MEAL CARD`.

## Environment variables

| Variable | Description |
|---|---|
| `ApiKey` | Secret for `X-Api-Key` header validation |
| `Storage__Path` | Directory where uploaded PDFs are stored |
| `Backup__Path` | Directory where backups are stored |
| `ConnectionStrings__DefaultConnection` | SQL Server connection string |
| `Python__Executable` | Python binary (`python` on Windows, `python3` on Linux) |
| `Python__ExtractorScript` | Absolute path to `scripts/pdfExtractor.py` |
| `Python__TimeoutSeconds` | PDF extraction timeout (default 60); the Python process is killed on expiry |
| `AlphaVantage__ApiKey` | Alpha Vantage API key for investment price fetching |
| `AlphaVantage__DailyQuota` | Alpha Vantage daily request quota (default 25) |
| `AlphaVantage__ReservedForManual` | Quota reserved for manual price refreshes (default 5) |
| `AlphaVantage__GoldProxyTicker` | EUR-listed gold ETC ticker used to price gold (default `4GLD.DEX`, 1 unit = 1 gram) |
| `GoogleServices__ClientId` | Google OAuth 2.0 client ID |
| `GoogleServices__ClientSecret` | Google OAuth 2.0 client secret |
| `GoogleServices__RedirectUri` | OAuth redirect URI (localhost for development, Tailscale IP for remote) |
| `GoogleServices__FrontendUrl` | Angular app origin the OAuth callback redirects to (e.g. `http://localhost:4200`) |

Never commit these values. Locally they live in `local/environment.dev` (loaded by
`scripts/run-backend.ps1`) and `local/environment.demo`; in production in
`/etc/beacon/environment` (loaded by systemd `EnvironmentFile`).

## Tests

Backend: `api/Beacon.Tests/` (xUnit, EF Core InMemory, ADR-015). One SQL-backed
backup/restore round-trip test runs only when `BEACON_TEST_SQLSERVER` is set to a SQL Server
connection string; it is skipped otherwise. Coverage: all bank/salary/grocery parsers (incl.
Trade Republic's block-based multi-line layout, and the micro1
`Micro1InvoiceParser`/`DeelWithdrawalParser`/`Micro1Reconciler` two-PDF USD→EUR flow, with
`UnifiedUploadBatch` pairing/unpaired/ambiguous cases), `ParseVerifier`, `ApiKeyMiddleware`,
`ExceptionHandlingMiddleware`, `ApplyRuleService` (incl. Excluded-category rules setting
`IsExcluded`), `FileStorageService`, `OrphanedPdfCleanup` (relative, foreign and absolute
stored paths), `SavingsPlanImportService`, `StatementUploadService` (PPR recompute helper,
Trade Republic savings-plan exclusion), `AlphaVantageService` (incl. request-URI pinning),
CQRS handlers for Backup (incl. investment tables), Categories,
Transactions, Groceries (incl. Excluded-category sync across `SetGroceryItemCategory`,
`CreateGroceryItem` and `GroceryApplyRuleService`), Salary (incl. `MergeSalarySlip`),
Statements (incl. meal-card text import), Investments (assets, lots, prices, oversell
validation, price backfill), input validation, `GoogleOAuthService`,
`GoogleCalendarService`, `GoogleTasksService`.

Frontend: Vitest specs next to the code (`*.spec.ts`), run by `ng test`.
