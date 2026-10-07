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
handler, which works on SQLite through EF Core. For an upload, `PdfExtractorService` runs
`scripts/pdfExtractor.py` (pdfplumber) to get the page text (a CSV export is its own text, and
an XLSX export is read in .NET), a parser turns it into domain objects, and `ParseVerifier`
checks the result before it is saved.

## Stack

| Layer | Technology |
|---|---|
| PDF extraction | Python 3 + `pdfplumber` |
| XLSX reading | Open XML SDK (`DocumentFormat.OpenXml`, ADR-034) |
| Backend API | ASP.NET Core 10 (.NET 10) |
| Database | SQLite + EF Core 10 (code-first) |
| Frontend | Angular 22 (standalone components, signals) |
| Charts | chart.js 4.5 |
| Testing (backend) | xUnit + in-memory SQLite |
| Testing (frontend) | Vitest 5 |
| Formatting | Prettier 3.8 |

## Repository layout

```
beacon/
├── api/
│   ├── Beacon.Api/
│   │   ├── Controllers/          # REST endpoint handlers
│   │   ├── Data/                 # AppDbContext, SqliteSetup (connection setup)
│   │   ├── Features/             # Feature-driven CQRS (see below)
│   │   │   ├── Backup/
│   │   │   ├── Categories/
│   │   │   │   └── Shared/       # ApplyRuleService
│   │   │   ├── Groceries/
│   │   │   │   └── Shared/       # GroceryApplyRuleService
│   │   │   ├── GroceryCategories/
│   │   │   ├── Health/           # GetHealth (answers without the API key)
│   │   │   ├── Investments/
│   │   │   │   └── Shared/       # TradeImportService
│   │   │   ├── Logs/             # GetLogs, LogClientError
│   │   │   ├── Salary/
│   │   │   ├── Shared/           # ExcludedCategory, RuleMatch, ProtectedEntityHelper, ValidationExtensions
│   │   │   ├── Statements/
│   │   │   ├── Transactions/
│   │   │   └── Upload/           # UnifiedUploadBatch (multi-type batch upload)
│   │   ├── Middleware/           # ApiKeyMiddleware, ExceptionHandlingMiddleware, RequestLoggingMiddleware
│   │   ├── Migrations/           # EF Core generated migrations
│   │   ├── Models/               # Domain entities
│   │   ├── Services/             # Upload services, storage, Google, PDF extractor
│   │   │   ├── Logging/          # Log files (Serilog), level defaults, client-error rate limit
│   │   │   ├── Parsing/          # Bank, salary, grocery & XTB parsers; CsvText; XlsxWorkbook; MealCardTextParser; ParseVerifier
│   │   │   └── Pricing/          # Price source (Yahoo, OpenFIGI), daily sync, queue, Xetra calendar
│   │   ├── Validation/           # ValidationResult
│   │   ├── appsettings.template.json
│   │   └── Program.cs            # DI registration, middleware pipeline, startup seeding and PDF cleanup
│   └── Beacon.Tests/             # xUnit test project; SqliteTestDatabase gives each test an in-memory SQLite database
│       ├── Controllers/          # Controller responses (Google connection errors, health status codes)
│       ├── Data/                 # SQLite behaviour
│       ├── Handlers/             # CQRS handler tests
│       ├── Middleware/           # Middleware tests
│       ├── Parsing/              # Parser tests (bank, salary slip, grocery, XTB) and synthetic file builders
│       ├── Services/             # Service-level tests
│       └── Validation/           # Validator tests
├── web/
│   └── src/
│       ├── styles.scss           # :root design tokens, fonts, base elements
│       ├── _shared.scss          # Global classes; forwards the partials in styles/
│       ├── styles/               # _mixins (no CSS output), _buttons, _forms, _modals, _feedback
│       └── app/
│           ├── core/
│           │   ├── charts/       # chart-theme (chart.js defaults from the CSS variables)
│           │   ├── components/   # confirm-dialog, month-scrubber
│           │   ├── constants/    # Shared constants (e.g. category colours)
│           │   ├── interceptors/ # apiKeyInterceptor (adds X-Api-Key header)
│           │   ├── models/       # TypeScript interfaces
│           │   ├── services/     # finance, categories, salary, groceries, grocery-categories, calendar, tasks, google-auth, investments
│           │   └── utils/        # bank, category-net, date-utils, http-params, money, month-totals, rule-match
│           ├── pages/            # Lazy-loaded routed components
│           │   ├── analytics/    # Insights, incl. the category-bars component
│           │   ├── calendar/     # incl. event-modal + task-modal components
│           │   ├── dashboard/    # Home, incl. the River chart (river.ts) and six-months.ts
│           │   ├── investments/
│           │   ├── rules/
│           │   ├── salary/
│           │   ├── settings/
│           │   ├── transactions/
│           │   └── upload/       # incl. the statement-list component (open or delete a statement)
│           ├── app.ts            # Root component: the shell (top nav, bottom nav, Upload button)
│           ├── app.routes.ts     # Route definitions
│           └── app.config.ts     # Angular bootstrap config
├── scripts/
│   ├── pdfExtractor.py           # PDF → JSON page text (run by PdfExtractorService)
│   ├── requirements.txt          # pdfplumber
│   ├── reset-db.ps1              # Drop and recreate the local database (reads appsettings.json)
│   ├── run-backend.ps1           # Load local/environment.dev, apply migrations, start the API on :5098
│   ├── run-frontend.ps1          # Wait for the API, then ng serve on :4200
│   ├── run-backend-demo.ps1      # (WIP) API against the demo database (local/beacon-demo.db), with its own uploads-demo/ and backups-demo/
│   ├── seed-demo.sql             # Synthetic demo data covering every feature (SQLite), April 2025 to March 2026
│   ├── seed-demo.ps1             # Recreate the demo database and seed it through SeedRunner
│   ├── SeedRunner/               # Console app: runs a SQL file against a SQLite database, then moves every date so the newest month is the current one and rewrites decimals and dates as EF writes them
│   └── setup.sh / .ps1           # Once per clone: git config core.hooksPath .githooks
├── docs/
│   ├── ARCHITECTURE.md           # This file
│   ├── DECISIONS.md              # Settled decisions (ADRs)
│   ├── ROADMAP.md                # What is planned, by theme
│   ├── tasks/                    # git-ignored: private task files, one per piece of work
│   └── screenshots/              # README images
├── .claude/                      # agents/ (scaffolders), skills/task/, settings.json (shared permissions)
├── .githooks/                    # commit-msg (subject rules), pre-push (protected, task and screenshot branches)
├── .gitattributes                # Shell scripts and hooks stay LF on every platform
├── .vscode/                      # tasks.json ("Beacon: Start All"), launch.json
├── local/                        # git-ignored: environment.dev/.demo, beacon.db, uploads/, backups/ (demo: beacon-demo.db, uploads-demo/, backups-demo/), sample PDFs
├── .github/
│   ├── workflows/ci.yml          # Dependency audits (NuGet, npm), formatting checks (dotnet format, Prettier), tests, production build
│   ├── dependabot.yml            # Weekly grouped dependency updates against development
│   └── pull_request_template.md  # What, Why, How tested, screenshots or "No visual change."
├── dotnet-tools.json             # Pins dotnet-ef; the scripts run `dotnet tool restore`
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
`MarkTransfers`, `ApplyRuleService`, `StatementUploadService` (each parsed row's rule match,
and BPI's synthetic PPR rows, also when a backfill recomputes "BPI Reforma - Ganhos") and
`ImportMealCardText` (each row's rule match). Groceries: `SetGroceryItemCategory`,
`CreateGroceryItem`, `MarkGroceryItemsExcluded`, `GroceryApplyRuleService`,
`GroceryReceiptUploadService` (which also routes receipt-category **mappings** through it, so
mapping a receipt section to Excluded genuinely excludes its items on import) and
`CreateGroceryReceiptCategoryMapping` (the new mapping applied to unassigned items already
stored). Never write `.CategoryId = …` directly: a label and a flag that drift apart mean a
row shows up as a spending line labelled "Excluded". The statement and meal-card imports once
did, so rules that put a row in Excluded left it unflagged; migration
`FlagRowsInExcludedCategory` flagged every row already in Excluded, and never clears a flag.

A row can also be excluded with **no** category (an investment buy, a row its parser marks
as a trade, set by `StatementUploadService`; see "Bank statement parsers"); `ApplyCategory`
deliberately leaves such a flag alone when a category is later assigned, and only clears it
for rows actually leaving the Excluded category.

The frontend mirrors this defensively: `finance.service.ts` and `groceries.service.ts` each
treat a row as excluded when `isExcluded` **or** its category is named `Excluded`, so a
stale row can never be counted. On the grocery side the raw `allItems` signal keeps
everything (the item list shows excluded rows) and **`countedItems` is the one to use for any
total or chart**, mirroring `allTransactionsRaw` vs `allTransactions`. The Activity page
(`pages/transactions/`) lists excluded rows dimmed, with an "Excluded" chip, and under "Left
out of totals"; its In, Out and Kept come from `allTransactions` and `countedItems` with every
filter applied, so those rows never reach a figure. The Excluded category is never offered as
a plain category pick (`assignableCats` / `gAssignableCats` on the Activity page), since
excluding is its own action, but it stays in *filter* dropdowns so excluded rows remain
findable. Rules *may* target it; both rule services and every import set the flag when
they match.

Within the rows a figure counts, income and spending net each category (ADR-037): a category's
credits less its debits is income when above zero and spending otherwise, so money paid back (a
shared dinner, a refund) lowers the category's spending instead of counting as income. Rows
without a category stay gross, each credit income and each debit spending, and Kept is the same
either way. One pure helper does it, `categoryNet` (`core/utils/category-net.ts`, with
`spendingByCategory` and `incomeByCategory` for the per-category lists); no page keeps its own
credit and debit split for a total. A view nets over everything it shows: Home's month, Home's
last six months (their total and their "Top spending" and "Top income", netted once over the
six, while each month's row nets on its own) and Activity's filters across every bank (per bank
only within that bank: Home's account filter, Activity's "Totals by bank"), Insights over its
month, its range or all months, so a payback that arrives a month after its expense nets only
in a view that holds both months. Excluded rows and rows of an unclassified
type never reach the helper's figures, whatever a caller passes.

There is no `Internal Transfer` category. It was the pre-rename name of this concept; a
data migration (`MergeInternalTransferIntoExcluded`, before the move to SQLite) folded any
surviving rows, rules and transactions into `Excluded` (marking them excluded) and deleted
it. Do not re-introduce it.

### Category rules

A category rule (`CategoryRule`, `GroceryCategoryRule`) is a text (`Pattern`, may be empty),
how the text matches (`MatchWholeDescription`) and an amount (`Value`, optional); creating or
editing one needs a text or an amount. Its text matches a row whose description, trimmed,
**equals** it when `MatchWholeDescription` is set, and a row whose description **contains** it
otherwise; both are ordinal, so case-sensitive, and the text is trimmed on save. If the rule
has an amount, the row's amount must equal it too. An empty text is no text condition, and a
rule with neither matches nothing (ADR-035). Rules from before the choice existed match a part
of the description (the migration's default), and so does a request that leaves the flag out;
the rule dialogs offer the whole description by default, filled in from the row on the Activity
page. Every server path uses one matcher, `RuleMatch.Matches` (`Features/Shared/RuleMatch.cs`):
the statement upload (each parsed row, and BPI's synthetic PPR rows), the meal-card import, the
grocery receipt upload, adding a grocery item, and `ApplyRuleService` /
`GroceryApplyRuleService`. At import the lowest-id matching rule wins and the row records it in
`CategoryRuleId`. Creating a rule applies it once to every row with no category; editing one
applies nothing. The client's `matchesRule` (`core/utils/rule-match.ts`) mirrors the matcher
for the match count the rule dialogs show. A category a rule assigns goes through
`ExcludedCategory.ApplyCategory`, like any other.

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

**CSV exports** (ADR-031). The batch upload takes `.csv` files, alone or inside a ZIP, beside
PDFs. `UnifiedUploadBatchCommandHandler` reads a CSV as UTF-8 text (`CsvText.Decode`, which
drops a byte order mark and refuses other encodings) and passes it, as the single page, to the
same cascade as a PDF's text, without pdfplumber and without the micro1 and Mercor checks;
`StatementUploadService` does the same for a CSV posted to `/api/statements/upload`. A CSV
parser detects its format by the header line and splits rows with `CsvText.ReadRows`
(RFC 4180 quoting). The file is stored as `<guid>.csv` (see "File storage"). A parser refuses
a file it can't trust with a `FormatException`, which the upload reports as the file's error.

Three things a parser can hand the upload beside its rows:

- **Relative balances.** An export without balances returns `BalancesRelative`, opening at 0
  with every balance counted from it. `StatementUploadService.ChainBalancesAsync` then opens
  it at the closing balance of the bank's statement for the previous month and shifts every
  balance by it, as the meal card chains (ADR-010). With no earlier statement of the bank it
  opens at 0.00 and warns; a month missing in between is refused, naming the month to import
  first; an earlier month imported after a later one warns that the later one's balances were
  not recomputed. Such a statement may not overlap any other of its bank (not only one with
  the same `PeriodFrom`), so a re-export of a month, or a month a PDF statement covered from
  another first day, is refused as already imported.
- **Trades.** A row that buys an investment carries a `ParsedTrade` (ISIN, asset name, date,
  quantity, price, fees, the source's trade id; a broker's export names the asset by ticker
  instead, see "Supported brokers"). `StatementUploadService` stores the row as
  an excluded debit with no category and no rule match, since it is cash moved into an
  investment, not spending, and after saving the statement hands the trades to
  `TradeImportService` (see "Investments"). The row's own amount includes the fees.
- **A retirement plan (BPI's PPR).** `PprBalance` is what the plan is worth at the end of the
  period: `ACTIVOS` less the current account, so it still holds a redemption whose cash has not
  reached the account ("Posições a Liquidar"). `PprSubscriptions` are the plan section's
  subscriptions. `StatementUploadService` books each subscription as a credit on its own dates,
  "BPI Reforma - " and the section's wording ("BPI Reforma - SUBSCRICAO EMPRESA"). It then adds
  "BPI Reforma - Ganhos" at `PeriodTo`, what the market did: the change in `PprBalance` since
  the previous BPI statement, less the statement's subscriptions, plus its cash rows that
  redeem the plan (`RESGATE ... PPR`). The plan section lists a redemption too, but it is
  counted only by its cash row, the moment it leaves `PprBalance`. A statement with no earlier
  BPI one has no Ganhos row, and a Ganhos of zero adds none. Both rows match rules like any
  row. Importing a statement before a later one recomputes the later one's Ganhos with the
  same formula, from its stored rows; deleting a statement recomputes its successor's.

Parser warnings (`ParsedStatement.Warnings`, a skipped row, say) are returned with the
upload's result, after `ParseVerifier`'s and the balance chaining's.

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
| `CentralGestParser` | `"CentralGest Software"` footer | Two-column (original+duplicate); mixed PT/US number formats; subsidy-only runs (see below) |
| `DomirestParser` | `"DOMIREST"` company name | Stacked original+duplicate; PT number format |
| `Micro1InvoiceParser` | `"Micro1 Inc."` (USD invoice) | **Not** factory-registered; paired with a Deel withdrawal → EUR (see below) |
| `MercorStatementParser` | `"Mercor Line Item Statement"` (USD statement) | Plain class, **not** an `ISalarySlipParser`; converted at the EUR received (see below) |

`CentralGestParser` reads a fixed set of lines: `Vencimento`, `PPR`, `Tickets Refeição`,
`Segurança Social`, `IRS`, and the holiday and Christmas pay lines (`Subsídio de Férias` or
`de Natal`, `PPR Sub Férias` or `Sub Natal`), each under its own name. `HoursWorked` is the
month's weekdays × 8, except on a slip with no `Vencimento`: a subsidy-only pay run, which
has no hours (see "Merging a second pay run into a month"). `TotalEspecie` is the meal
tickets paid in kind; the net (`Total a Pagar`) leaves them out.

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

#### Mercor: one USD statement, converted at the EUR received

Mercor's monthly "Line Item Statement" (`MercorStatementParser`, plain class, detects
`"Mercor Line Item Statement"`) is in USD and says nothing about the euros that reached the
bank, so, like a micro1 invoice, it is never a slip on its own (ADR-033). The parser yields
`MercorStatement(Period, TotalPayUsd, ShiftPayUsd, HoursWorked, PayRateUsd)`. The period is
the 1st of the month "Statement Period" starts in. The hourly lines
(`... $$40.00 02:30 $$100.00`: a doubled `$$` before the rate and the amount, in no date
order) must sum to `Total Shift Pay`, or the file is refused naming both sums, and a
`Total Pay` below `Total Shift Pay` is refused too. Hours are Σ amount ÷ rate, rounded to two
decimals, because the `HOURS WORKED` column is cut to the minute (a few cents of pay show
`00:00`); the pay rate is shift pay ÷ those hours, which is the rate itself when every line
shares it.

`UnifiedUploadBatchCommandHandler` checks for a Mercor statement before the standard cascade
(after micro1's two checks), stores the PDF and, once every file of the batch is in, returns a
**`MercorNeedsEur`** result (`UnifiedMercorResult`): the USD figures, the stored file name and
a suggestion, the sum of the credits from any bank whose description contains "mercor"
(ignoring case) dated in the statement's month, listed with date, bank and amount. Waiting
for the end of the batch lets a bank statement uploaded alongside count. A statement that
fails to parse comes back as a failed `SalarySlip` with the reason rather than going to the
cascade, since its title belongs to no other document. The batch saves no slip.

The Upload page asks for the EUR received, pre-filled with the suggestion, and the slip review
cannot open without it. `POST /api/salary/parse-mercor` (`ParseMercorStatement`) takes
`{ pdfPath, eurReceived }`, reads the stored statement again and returns the EUR slip from
`MercorReconciler.Reconcile(statement, eurReceived)`: gross = net = the EUR received (no fee
is known, so no deduction), `rate = EUR ÷ TotalPayUsd`, `Base Pay = round(ShiftPayUsd × rate)`
(the whole EUR when the statement is hourly pay only), `Other = EUR − Base Pay` (pay beyond
the hourly lines, plus the rounding) and `HourlyRate = round(PayRateUsd × rate)`. The usual
slip review follows: the profile is matched by the employer, "Mercor", and one created there
starts with the `hours` formula; a second statement for a month offers the merge (ADR-008).
The payouts are matched by date only, so a payout for late-month work, which lands the next
month, is corrected by hand in the field.

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
of the month, so a cycle that pays **twice a calendar month** cannot create two rows
(ADR-008). Two do: micro1/Deel invoices each half-month, and a CentralGest employer pays
holiday or Christmas pay (subsídio de férias, de Natal) as a second slip for the month.
`POST /api/salary/slips/{id}/merge` (`MergeSalarySlipCommandHandler`) folds a second pay run
into the existing slip instead: gross/net/base/hours/`TotalEspecie` are summed null-safely,
`HourlyRate` is re-averaged **weighted by hours** (it is a rate, not a total), line items are
combined **per category** (one `Base Pay` line per month, new categories appended after the
existing `SortOrder`), and per-unit detail (`UnitValue`, `Percentage`) survives only when both
sides agree. A subsidy-only slip parses with no hours and no base, so merging it leaves the
month's as they were, and its subsidy lines join the month under their own categories.
`Period` and `SalaryProfileId` are never touched, and incoming line-item categories are
validated against the target slip's profile exactly as in `CreateSalarySlip`. A slip holds
**one** PDF: the first stays authoritative and a superseded second PDF is deleted from
storage rather than orphaned, with both file names kept in `SourceFile` (`"a.pdf; b.pdf"`).

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
`VerifySalarySlip` (income items vs gross; income − deductions − tax − `TotalEspecie` vs net,
since what is paid in kind never reaches the bank), `VerifyGroceryReceipt` (item sum vs
receipt total). Called from `StatementUploadService`, `ParseSalarySlipCommandHandler`,
`GroceryReceiptUploadService` and `UnifiedUploadBatchCommandHandler`.

### File storage

`FileStorageService` (singleton) keeps uploaded PDFs and CSV exports in one flat folder,
`Storage__Path` (default `statements/` next to the binaries), as `<guid>.pdf` or
`<guid>.csv`: `SaveAsync` keeps an upload's extension when it is one of those two, and stores
anything else as `.pdf`. `GetFile` serves each with its content type (`application/pdf`,
`text/csv`). The column keeps the name `PdfPath`. An XLSX export (XTB, ADR-034) is not stored:
its trades become lots, and no row would reference the file.

- `PdfPath` (`MonthlyStatements`, `SalarySlips`, `GroceryReceipts`) holds only the file name
  (ADR-023). `SaveAsync` writes the file and returns its name, which the upload flows store and
  `upload-pdf` returns to the client; no absolute server path leaves the API. Create, update
  and merge of a salary slip store `FileNameOf` whatever `pdfPath` the client sends.
- `GetFullPath`, `GetFile` and `Delete` resolve a file name (or a legacy absolute path) under
  the storage root and refuse anything that resolves outside it. Anything that opens a stored
  file goes through them: `parse-pdf` hands the extractor `GetFullPath(pdfPath)`.
- Older data: a data migration (`StorePdfPathsAsFileNames`, before the move to SQLite) cut
  existing rows down to their file names, and a backup restore does the same to every restored
  `PdfPath`
  (`RestoreBackupCommandHandler.StorePdfPathsAsFileNames`), so an old backup cannot bring
  absolute paths back.
- At startup `Program.cs` runs `OrphanedPdfCleanup` (scoped, in `Services/`) when
  `Storage__Path` is set: every stored file (`*.pdf`, `*.csv`) in the storage root that no
  `PdfPath` references and that is older than 24 hours is deleted; a file that cannot be
  deleted is logged and skipped. A row references a file by the name its `PdfPath` ends in
  (`FileStorageService.FileNameOf`, which splits on both `/` and `\`), so a relative path, an
  absolute path under the root and an absolute path written on another machine (a restored
  backup) all protect their file.
- The cleanup deletes nothing, and logs a warning, when the database references none of the
  files in the folder: the two do not belong together. That covers the demo database (no
  `PdfPath` at all) or a restored database pointed at another machine's uploads. The cost:
  files left behind after every row is gone stay until a new upload is referenced.
- The demo backend never shares the folder: `scripts/run-backend-demo.ps1` sets
  `Storage__Path` and `Backup__Path` to `local/uploads-demo` and `local/backups-demo`,
  whatever `local/environment.demo` says.

### Investments

Entities: `InvestmentAsset` (`AssetType` is `ETF` or `Gold`; optional `Isin` with a unique
index; an imported trade finds its asset by `Isin`, or by `Ticker` and then `PricesSymbol`),
`InvestmentLot` (signed `Quantity` `decimal(18,6)`: positive = buy, negative = sell; optional
`ExternalId` with a unique index, the source's id of an imported trade, `XTB:<id>` for XTB's),
`InvestmentPriceSnapshot` (one price per
asset per day, unique `(AssetId, Date)` index; `Source` is `Manual`, `Synced` or `Legacy`).
Endpoints live in `Controllers/InvestmentsController.cs` under `/api/investments`; handlers
follow the tuple-result pattern `(Result?, Error?)` where `(null, null)` maps to 404.

Conventions:

- **Gold is tracked in grams** (ADR-011): `Quantity` = grams, `PricePerUnit` = EUR/gram.
  Gold is priced via an EUR-listed physical gold ETC proxy (`Prices__GoldProxySymbol`,
  default `4GLD.DE` = Xetra-Gold, 1 unit = 1 gram → quotes are already EUR/gram, no
  troy-ounce conversion). The proxy trades at a small premium/discount to spot.
- **ETFs are assumed EUR-listed (UCITS)**: quotes are stored as EUR with no FX conversion. Do
  not add non-EUR-listed tickers.
- Sells are validated against net holdings (server and client); editing a lot preserves its
  buy/sell sign.
- P&L uses **average cost basis** (ADR-013), computed client-side in `investments.service.ts`
  (`assetMetrics`): buys update the weighted average (fees included), sells book realised
  P&L against it.
- **Returns** (ADR-038) are counted since the first buy. Per asset, `assetMetrics` adds the
  money put in (`invested`: every buy with its fees, sold since or not), `totalReturn`
  (realised plus unrealised; null while units are held without a price), `totalReturnPct`
  (against the money put in, simple, not annualised) and `firstBuyDate`. The portfolio's
  `totalInvested`, `totalReturn`, `totalReturnPct` and `firstBuyDate` cover every asset, one
  sold out included; its unrealised part is value less cost, so a held asset without a price
  counts as worth nothing. The Invest page's `portfolioSummary` (`investments-view.ts`) does
  the same for the tab's assets. The page leads with the total return in € and % "since" the
  first buy's month, its tiles split it into money put in, unrealised and realised, its
  changes start with "All time" before 1 month, 1 week and 1 day, each holding's "Return" is
  its own total return, and the value chart opens on All. The chart's title is what prices did
  over the shown range, the money put in or taken out left out (`valueChange`,
  `investments.service.ts`); the line under it gives the value's change and that money. Home's
  Investments row shows the total return after the value, and today's change after it. Home's
  net worth change since the end of the previous month splits the same way: investment growth
  (`valueChange` between the history's point at that date and its latest), and the rest, what
  the accounts kept; a buy paid from cash moves neither.
- **Trade import** (ADR-031, ADR-034): `TradeImportService` (`Features/Investments/Shared/`,
  scoped) runs after `StatementUploadService` persists a statement whose parser found buys (the
  Trade Republic CSV's `BUY` rows, savings plans and one-off buys alike; their rows are already
  excluded from spending), and for each XTB export (see "Broker exports" below). Each
  `ParsedTrade` becomes an `InvestmentLot` with the source's quantity (negative for a sell),
  price per unit and fees (Trade Republic's fee plus tax), its id in `ExternalId` and a note
  saying where it came from. Lots from several sources share one asset. A trade with an ISIN
  finds its asset by `Isin`; one with a ticker by `Ticker` (any case), then by `PricesSymbol`,
  so XTB's `VWCE.DE` finds the ETF that Trade Republic's buys created from its ISIN once its
  prices have synced. An asset not found is created as an ETF, named from the source and
  queued for a price sync: from an ISIN with `Ticker` left null, which the sync finds (e.g.
  `VWCE.DE` for `IE00BK5BQT80`); from a ticker with that ticker. An asset created from a ticker
  is not found later by an ISIN. Idempotent: a trade whose id a lot already holds is skipped; a
  trade with a new id that matches a lot without one by `(AssetId, Date, Quantity)` (a lot typed
  by hand) gives that lot its id instead of adding a twin; a trade without an id dedups by
  `(AssetId, Date, Quantity)`. A sell is checked against the asset's holdings, its lots so far
  plus the import's earlier trades, as a manual sell is; one beyond them throws, and nothing of
  the import is saved. After a statement, the lots added are returned as `LotsAdded`, and its
  failures are caught, logged and returned as a warning, so they never fail the upload.
- **Broker exports** (ADR-034): an XTB export is uploaded through the batch like any file, and
  `XtbUploadService` (`Services/`, scoped) hands its trades to `TradeImportService`. XTB is no
  account in Beacon: no statement, no transactions, and the file is not stored. The batch holds
  every XTB export until all its files are read, then applies them oldest period first (a
  second download of a month after the first), each all or nothing, so a June sell never comes
  before May's buy; a refused file is that file's error and the others still import. Then
  `CheckHoldingsAsync` compares the newest imported export's Open Positions with Beacon: per
  asset, the lots from XTB (`ExternalId` starting `XTB:`) and those typed by hand, never another
  source's, against the quantity XTB lists for its tickers. A difference is a warning on that
  file. XTB lists the holdings when the file is generated, not at the period's end, so when an
  XTB lot in Beacon is dated after the export's period, nothing is compared.

Pricing (ADR-028): daily closes are stored, not fetched on demand.

- **Source**: `Services/Pricing/IPriceHistorySource`, implemented by
  `YahooPriceHistorySource`. Closes come from Yahoo Finance's chart endpoint
  (`/v8/finance/chart/{symbol}?period1&period2&interval=1d`) through the `yahoo-finance`
  `HttpClient`, which sends a browser-like `User-Agent` (without one Yahoo answers 429). Each
  bar's date is the exchange's (`meta.exchangeTimezoneName`); a bar without a close is skipped;
  a listing not in EUR is refused (ADR-011). Yahoo's `close` is adjusted for splits but not for
  distributions: after a split, closes before it no longer match the prices the lots were
  bought at. An ISIN maps to a symbol through OpenFIGI (`openfigi` client, no key): the ticker
  of the German composite listing (`exchCode` `GR`) plus `.DE`, kept only if Yahoo prices it in
  EUR. Network errors, 429 and 5xx are retried up to three times (2 s, 5 s, 15 s); other
  failures become a `PriceSourceException` whose message the user sees. One that never got an
  answer is marked `Unavailable`, so an ISIN lookup fails with it rather than report that the
  ISIN has no EUR listing.
- **Sync** (`Features/Investments/Commands/SyncPriceHistory/`, `POST
  /api/investments/prices/sync`, optional `assetId`): one asset, or every asset with net
  quantity above zero. Gold uses `Prices__GoldProxySymbol`; an ETF its ticker, or, with only
  an ISIN, the symbol found from it (never overwriting a ticker the user set). With no `Synced`
  price yet it asks for `Prices__HistoryYears` (15) years; otherwise from seven days before the
  latest `Synced` close, so gaps fill themselves. One request per asset. A close is inserted on
  a new date, replaces a `Synced` or `Legacy` price, and never a `Manual` one
  (`InvestmentPriceSnapshot.Source`; `UpsertInvestmentPrice` writes `Manual`, the migration
  marked older rows `Legacy`). A move over `Prices__JumpWarningPercent` (20) from the previous
  close is stored and logged as a warning. The asset keeps `PricesSyncedAt`, the last failure
  in `PriceSyncError`, and the symbol its synced closes came from in `PricesSymbol`. When the
  symbol changes (a corrected ticker, another gold proxy), the next sync fetches the whole
  window again and removes the synced closes the new symbol has no close for; if it fails, the
  old history stays. One asset's failure never stops the others. Syncs are serialised
  in-process, since the `(AssetId, Date)` index allows one writer.
- **Schedule**: `PriceHistorySyncService` (hosted) syncs every held asset at startup and daily
  at `Prices__DailyRunTime` (22:00 UTC, after the European close), and any asset queued in
  `PriceSyncQueue`: a new asset, a changed ticker, a first buy, an ETF created by a savings
  plan. Queuing never makes the request wait for the price source. `Prices__Enabled=false`
  turns it all off (the demo does): the sync endpoint answers 400, and
  `GET /api/investments/prices/status` says `enabled: false`, so the page hides its sync
  controls. Failures are logged and never stop the host.
- **Serving**: `GET /api/investments/assets` carries each asset's `priceCount` and only the
  prices its metrics read (`RecentPrices`, queried per asset: 40 days before the latest price,
  plus the latest price at or before a week ago, a month ago and the first lot). The portfolio
  value chart loads `GET /api/investments/prices/history?from=` (every asset's prices from the
  chart's range, each series starting with the asset's latest price before it so a sparsely
  priced asset is valued from the range's first day, as parallel `dates`/`prices` lists); an
  expanded asset loads all its prices from `GET /api/investments/assets/{id}/prices`, where
  each row shows its source (a close, `Manual`, or `Earlier` for `Legacy`). With 15 years for
  three assets the asset list is about 8 KB instead of 790 KB.
- **Staleness**: a held asset is stale when its last sync failed, or its latest price is
  missing or more than one trading day behind: a close is synced the evening of its day, so
  the newest to expect is the previous trading day's, and one missed sync is allowed
  (`PriceSyncSettings.IsStale`). Trading days follow Xetra's calendar (`XetraCalendar`:
  weekdays except 1 January, Good Friday, Easter Monday, 1 May and 24, 25, 26 and 31
  December), which every `.DE` listing trades on; the client has a copy
  (`core/utils/xetra-calendar.ts`). The Investments page marks a stale asset and shows the
  error; `/api/health` reports `prices` as `ok`, `stale` or `disabled`, without changing its
  status.

### Google OAuth, Calendar and Tasks

`Services/GoogleOAuthService.cs` manages Google OAuth tokens (ADR-017). It stores a single
access + refresh token in the `GoogleOAuthTokens` table. Call `GetValidAccessTokenAsync()`
from any service that needs to call Google APIs: it refreshes an expired access token, and
throws `GoogleConnectionException` when there is no token to use.

The connection has four states (`GoogleConnectionState`), reported by
`GET /api/auth/google/status` as `state` (with `connected`, true for the second and fourth):

| State | Meaning |
|---|---|
| `notConnected` | No row in `GoogleOAuthTokens`. |
| `connected` | The access token is valid, or was just refreshed. |
| `reconnectRequired` | Google rejected the refresh token (`invalid_grant`: it expired or was revoked). |
| `unreachable` | The access token expired and the refresh failed for another reason (network, timeout, Google error); the token is kept for the next attempt. |

The status endpoint refreshes an expired access token before answering, so it tells the
truth, not just whether a row exists. On `invalid_grant` the row is kept with both tokens
blanked: a blank refresh token means `reconnectRequired` on every later check, until the
account is connected again (which also resets `ConnectedAt`) or disconnected. Refresh failures
are logged as warnings. Google expires a refresh token after 7 days while the OAuth consent
screen's publishing status is "Testing", and after six months without use.

The Calendar and Tasks endpoints map `GoogleConnectionException` through
`Controllers/GoogleConnectionErrors.cs`, with a `code` next to `error`:

| Response | When |
|---|---|
| 401 `google_not_connected` | No account is connected. |
| 401 `google_reconnect_required` | The account must be connected again. |
| 503 `google_unreachable` | Google could not be reached to refresh the token. |
| 502 (no code) | Google's API answered with an error. |

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
3. Set the OAuth consent screen's publishing status to "In production", so refresh tokens do
   not expire after 7 days. The app stays unverified: Google shows a warning screen when you
   connect, which is fine for personal use.
4. Add the redirect URI to `appsettings.json` under `GoogleServices:RedirectUri`, and
   register the same URI on the client in Google Cloud Console.
5. Set `GoogleServices:ClientId`, `GoogleServices:ClientSecret` and
   `GoogleServices:FrontendUrl`.

Google accepts plain HTTP and IP addresses only for localhost: any other redirect URI must be
HTTPS with a domain name. For local development use
`http://localhost:5098/api/auth/google/callback`. For access over Tailscale, serve Beacon on
its Tailscale HTTPS name (`tailscale serve`) and use
`https://<device>.<tailnet>.ts.net/api/auth/google/callback`, with
`GoogleServices:FrontendUrl` set to `https://<device>.<tailnet>.ts.net`. Both URIs can be
registered in Google Cloud Console at the same time.

### Logging

Logs go to the console as before (journald on the server) and, when `Logs__Path` is set, to
files (ADR-029). `Services/Logging/LogFiles` adds Serilog (`Serilog.Extensions.Logging`,
`Serilog.Sinks.File`) as a second provider of the usual `ILogger`, so the ~90 log calls and the
console stay unchanged. The files are Serilog's compact JSON (CLEF): one object per line with
`@t`, `@mt` (the message template), `@l` (left out for Information), `@x` (an exception) and
the template's properties, plus `SourceContext`. A file a day, `beacon-yyyyMMdd.json` (a day
over 100 MB rolls to `_001` and on), and files older than `Logs__Keep` days (14) are removed.
`Logs__Path` is optional: unset, or a folder that can't be created or written, leaves the
console only, with one warning at startup, so a server whose environment predates the setting
still deploys.

- **Levels** (`LogLevels`): set in code, since production has no `appsettings.json`:
  Information by default, `Microsoft`, `Microsoft.EntityFrameworkCore` and `System` at Warning
  (no per-request framework lines, no SQL), `Microsoft.Hosting.Lifetime` at Information. Each
  is overridden by `Logging__LogLevel__<Category>` (`Default` for the rest), which applies to
  the console and the files alike.
- **Requests**: `RequestLoggingMiddleware`, first in the pipeline, logs one line per request
  (method, path, status, duration; never the query string, which can carry search terms), at
  Error for a 5xx.
- **Reading** (`Features/Logs/Queries/GetLogs/`, `GET /api/logs`): the newest entries first,
  filtered by `minLevel` (Serilog's names: Verbose, Debug, Information, Warning, Error, Fatal),
  a `from`/`to` window and a text `search` over message, details and source, at most `limit`
  (200, capped at 1000; `more` says older ones matched). It reads the newest files first, each
  from its last line back, and stops at the limit or the window's start. Messages are rendered
  from the template, strings unquoted. `enabled: false` when the server writes no files.
- **Client errors** (`Features/Logs/Commands/LogClientError/`, `POST
  /api/logs/client-errors`): message, stack and route, logged at Error under the category
  `Beacon.Client` with the stack as `ClientStack`. Each field is cut to its cap (1,000, 8,000
  and 300 characters), the body to 16 KB (413 beyond, from Kestrel) and the rate to 30 a minute
  (`ClientErrorRateLimit`, 429 beyond).
- The Settings page's "Logs" section (`pages/settings/settings-logs.ts`) lists the entries with
  a level filter, a time window and a search, errors highlighted and details expandable.

### DI lifetimes

| Service type | Lifetime |
|---|---|
| Parsers, `BankStatementParserFactory`, `SalarySlipParserFactory`, `GroceryReceiptParserFactory`, `Micro1InvoiceParser`, `DeelWithdrawalParser`, `MercorStatementParser`, `XtbExportParser` (concrete singletons, not factory-registered), `FileStorageService`, `YahooPriceHistorySource` (as `IPriceHistorySource`), `PriceSyncQueue`, `TimeProvider`, `LogFiles` | Singleton |
| Feature handlers, `PdfExtractorService` (as `IPdfExtractor`), `StatementUploadService`, `GroceryReceiptUploadService`, `XtbUploadService`, `OrphanedPdfCleanup`, `ApplyRuleService`, `GroceryApplyRuleService`, `TradeImportService`, `GoogleOAuthService`, `GoogleCalendarService`, `GoogleTasksService` | Scoped |
| `PriceHistorySyncService` | Hosted service (`AddHostedService`) |
| `MealCardTextParser`, `ParseVerifier`, `CsvText`, `Micro1Reconciler`, `MercorReconciler` | Static classes, not registered in DI |
| `AppDbContext` | Scoped (EF default) |

## Frontend

### Signal-based state

`FinanceService` (`core/services/finance.service.ts`) is the single source of truth
(ADR-016):

- `statements`: writable signal holding all loaded statements (plus `loading` / `error` signals).
- Computed signals: `banks`, `latestPerBank`, `totalBalance`, `allTransactions`,
  `allTransactionsRaw`, `monthTotals` (each month's income and spending, every bank together,
  each category netted across banks) and `monthlySummaries` (per month and bank, netted within
  the bank, for a view of one bank; adding them up does not give `monthTotals`).
- Call `reload()` after any mutation to refresh state.

`InvestmentsService` holds the investment assets the same way, loaded once when first used
(the Invest page or the dashboard); its `load()` refreshes them. The Invest page calls it after
its own changes, and the Upload page after an upload that added lots: a statement's buys
(`lotsAdded` on its result) or a broker's export (`tradesResult.added`). It gets the service
from the injector only then, so opening the Upload page loads no investments.

All pages are lazy-loaded standalone components via `app.routes.ts`. No NgModules.

Insights shows one month, a range of calendar months ending at the scrubber's month (3, 6 or
12, or the year so far; `periodKeys` in `pages/analytics/insights.ts`, from the first month
with money on), or all months; a range has no comparison with the previous month. Home's "Last
six months" are the same six calendar months (a month without money is an empty row), so
Home's totals match Insights' six months for the same end month. The savings rate, Kept as a
share of In rounded to a whole percent, comes from one helper, `keptShare`
(`core/utils/month-totals.ts`): Home's Kept tile and six-month table and Insights' headline use
it.

Activity and Insights take their view from the URL's query, so other pages can link to one.
Activity reads `month`, `category` (an id, or `unknown`), `bank`, `type`, and for groceries
`tab=groceries` with `categoryId`. Insights reads `month` (`YYYY-MM`, or `all`), `months` (`3`,
`6`, `12` or `ytd`), `side` (`in` or `out`, the "By category" filter; with `category=unknown`,
also which Unknown), `category`, and `tab=groceries` with `categoryId`, and writes its view back
with `replaceUrl`, so a reload or a shared link opens the same view. Home links to Insights this
way: each category of "Where it went" on the month, and the six months and each of their top
categories on `months=6` (not while an account is picked, since Insights has no bank filter).
Activity shows one month, so Insights links a range to every month of it.

Angular 22 made OnPush the default change detection and `fetch` the default HTTP backend. The
upgrade kept the earlier behaviour: every component declares
`changeDetection: ChangeDetectionStrategy.Eager`, and `app.config.ts` passes `withXhr()` to
`provideHttpClient`. A component without the line gets OnPush.

### Design system

The client follows the River design (ADR-030). Everything below lives in `web/src/`.

- Tokens: `styles.scss` declares every colour, font and radius as a CSS variable on `:root`
  (`--bg`, `--surface`, `--surface-raised`, `--border`, `--border-soft`, `--chart-grid`, the
  text greys, `--primary` and `--primary-tint`, `--on-primary` for text on light fills,
  `--credit`, `--debit`, `--warning`, `--danger`, `--overlay`, `--category-fallback`,
  `--font-body` / `--font-display` / `--font-figures` / `--font-mono`, `--radius-md` / `-lg` /
  `-xl`). Components use the variables, never colour literals; chart code keeps only data
  colours (a category's stored colour).
- Fonts: Manrope (text), Unbounded (headings, page titles), Bricolage Grotesque (figures, with
  `font-variant-numeric: tabular-nums` through the `figures` mixin), one Google Fonts `<link>`
  in `index.html`.
- Shared styles: `_shared.scss` holds the global classes (`.page-header`, `.section`,
  `.figures`) and forwards the partials in `styles/`: `_buttons` (`.btn-primary` light fill
  for the main action, `.btn-accent`, `.btn-secondary`, `.btn-danger`, `.btn-icon` and its
  quiet variant for rows, `.link-btn`), `_forms` (fields, the segmented `.tab-bar`, `.switch`), `_modals`, `_feedback`
  (banners, the `.review-row` with hollow `.ring`s, `.cat-dot`, `.chip`). `styles.scss` emits
  them once. A component that needs a mixin uses `styles/_mixins.scss` (`card`, `figures`,
  `below-desktop`, `phone`, `visually-hidden`), which emits no CSS, so the global classes are
  never copied into a component's styles.
- Shell and breakpoints (`app.html|ts|scss`): from 1024 px a header with the brand, the
  sections as a pill group (Home, Activity, Insights, Invest, Salary, Calendar, Categories),
  a Settings icon button and Upload PDF; content capped at 1376 px. Below 1024 px a sticky top
  bar with the page title (route `data.label`) and a "More" button whose sheet holds Categories
  and Settings, a fixed bottom nav with the six main sections, and a round Upload button above
  it on every page but Upload. The phone layouts start below 640 px.
- Modals: every dialog uses `.modal-overlay > .modal` and sets its width with `--modal-width`;
  below 640 px the same markup becomes a bottom sheet with a handle.
- Month scrubber (`core/components/month-scrubber/`): one button per month with its money in
  and out as two small bars (`aria-pressed`, an `aria-label` that reads both), scaled to the
  months shown. On desktop it shows as many months as its width holds (up to six) and
  "Earlier" reveals older ones; on phones the months are a sideways-scrolling row of chips. An
  "All months" choice comes last where a page allows it. Its months come from `monthCells()`
  (`core/utils/month-totals.ts`) over `FinanceService.monthTotals`. Home, Activity and
  Insights use it.
- Charts: `core/charts/chart-theme.ts` reads the tokens with `getComputedStyle` and sets
  chart.js defaults (`applyChartTheme()`: tick and legend text, gridlines, tooltip), plus
  `axisOptions()`, `withAlpha()` and `categoryColor()`, which falls back to
  `--category-fallback`. Small charts are hand-drawn SVG: Home's River chart
  (`pages/dashboard/river-chart.ts`, its series computed by the pure, tested `river.ts`) and
  the Investments sparkline. Sorted horizontal bars replace pies. The River's line is the
  month's spending as the totals count it (ADR-037): money paid back into a category that nets
  to spending takes it down on its day, a category that nets to income stays off it, and it
  ends at the month's spending; every row keeps its dot. Insights' "By category"
  (`categoryLines`, `pages/analytics/insights.ts`) lists every category's net on the side it
  falls, money in with a plus and money out with a minus, largest first, both sides together or
  one picked with All / In / Out; a category that had money both in and out gives both, before
  netting, under its name, one paid back in full comes last at zero on the spending side
  (Home's "Where it went" leaves it out), and rows without a category show as Unknown on each
  side. Nothing compares with the previous month there. A picked category's trend shows its net
  each month, above the line when it brought money in and below when it cost money (Unknown,
  never netted, shows money in and out apart).
- Money is formatted by `core/utils/money.ts`: outflows in neutral text with a true minus
  sign, inflows in `--credit` with a plus.
- The scrubber and the River chart are OnPush components driven by signal inputs; the pages
  keep `ChangeDetectionStrategy.Eager`.

### HTTP authentication

`core/interceptors/api-key.interceptor.ts` injects `X-Api-Key: <apiKey>` on every request
whose URL starts with `/api`. The interceptor is registered in `app.config.ts`.

### Google connection

`GoogleAuthService.status` holds the connection state (see "Google OAuth, Calendar and
Tasks"); the Calendar and Settings pages call `loadStatus()` when they open.
`core/interceptors/google-connection.interceptor.ts` watches `/api/calendar` and `/api/tasks`
calls: an error with code `google_reconnect_required` or `google_not_connected` reloads the
status while it still says connected, so both pages switch to their reconnect or connect state
instead of showing an empty calendar.

### Client errors

`core/services/client-error-handler.ts` replaces Angular's `ErrorHandler` (`app.config.ts`):
it logs to the console as before and posts each uncaught error (message, stack, route; URLs
without their query strings) to `POST /api/logs/client-errors`, at most ten a minute. A report
that fails is dropped, never reported in turn. `provideBrowserGlobalErrorListeners()` sends
the window's errors and unhandled rejections to it.

## Database

SQLite (ADR-024): one file, named by `ConnectionStrings__DefaultConnection`
(`Data Source=<path>`); its folder is created if missing.

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

`Data/SqliteSetup.cs` sets up every connection the same way: the API, the tests and the
tools in `scripts/` all go through `UseBeaconSqlite`.

- Text columns use SQLite's `NOCASE` collation (a convention in `AppDbContext`), so equality,
  sorting and unique names ignore case, as they did under SQL Server. `NOCASE` folds ASCII
  letters only.
- Lists shown to people sort with the `DISPLAY_ORDER` collation: case ignored, accented letters
  beside their base letter ("Água" among the A's). Queries apply it in `ORDER BY` with
  `EF.Functions.Collate(x, SqliteSetup.DisplayOrder)`. It is registered on each connection and
  never used in the schema, so the file opens in any SQLite tool and a change in .NET's sort
  rules cannot invalidate an index.
- SQLite's `lower()` and `upper()` are replaced by .NET's, so a search written as
  `x.ToLower().Contains(term.ToLowerInvariant())` also matches accented letters.
- Decimals are stored as exact text in EF's form (`12.5`, `1000.0`); EF Core 10 translates sums,
  comparisons and sorts on them. `AppDbContext.SaveChanges` rounds each decimal to its column's
  scale (`HasPrecision`), half away from zero, as SQL Server stored them. Declare a decimal
  column with `HasPrecision`, never `HasColumnType("decimal(...)")`: a type name gives the
  column numeric affinity, and SQLite would store floating point.
- EF creates the file in WAL mode and enforces foreign keys. One writer at a time is enough
  for one user.

At startup `Program.cs` seeds default data (including the protected Excluded categories)
and then runs the orphaned file cleanup (see "File storage").

## API surface

All endpoints require the `X-Api-Key` header, except `/swagger` in development,
`GET /api/auth/google/callback` (ADR-017) and `GET /api/health` (ADR-026). Use Swagger
(`http://localhost:5098/swagger`) or read `Controllers/` for the full surface.

`POST /api/upload/batch` takes every kind of document at once (PDFs, CSV and XLSX exports,
ZIPs of them) and answers one result per file, by `documentType`: `BankStatement`,
`GroceryReceipt`, `SalarySlip` (parsed, saved only after review), `Micro1Unpaired`,
`MercorNeedsEur`, `BrokerExport` (an XTB export: its trades added as lots, counted in
`tradesResult`, with the holdings check's warnings) or `Unknown`. A Mercor statement then goes through `POST /api/salary/parse-mercor` with the EUR
received (see "Mercor" above).

`GET /api/health` is for deploy scripts and monitors. It answers 200 with `status: "ok"` when
the database file exists, its migration history reads and no migration is pending, and 503
with `status: "degraded"` and a `reason` otherwise: `database unreachable` (no file, or one
SQLite cannot read) or `migrations pending`. Both carry `version` and `commit`, split from the
version the SDK stamps on the build (`1.0.0+<commit>` from a git checkout; `commit` is null
otherwise), and `newestMigration`, the newest migration applied to the database, which after a
rollback can be newer than the running release's. A healthy answer also carries `prices`:
`ok`, `stale` (a held asset's latest price is missing or more than one Xetra trading day
behind, or its last sync failed; see "Investments") or `disabled`; it never changes the
status, since deploys gate on it. `GetHealthQueryHandler` checks that the file exists before
reading it, since a query would make SQLite create a missing file, and logs only a degraded
answer.

## Supported banks

| Bank | Detection signal |
|---|---|
| ActivoBank | BIC `ACTVPTPL` or "EXTRATO COMBINADO" |
| BPI | SWIFT `BBPIPTPL` or "EXTRACTO INTEGRADO" |
| Revolut | BIC `REVOPTP2` or "Revolut Bank UAB" |
| Trade Republic (CSV) | The header line of the transaction export ("Extrato de transações") |

BPI's integrated statement holds the current account and, when the holder has one, a
retirement savings plan (PPR) section. The current account's rows are the statement's rows,
and its closing balance is `ACTIVOS`, everything held at BPI. Rows give a day and a month only;
the year is whichever of the period's years puts the date nearest the period, so a statement
from December into January dates each row in its own year. `BpiParser` reads each movement
in the plan section (dates, wording, units, average cost and `VALOR APLICADO`, the amount) and
knows four wordings: `SUBSCRICAO EMPRESA`, a subscription; `RESG.FORA COND.GERAL`, a
redemption, counted by its cash row instead; `SUBS.TRANSF.CLASSE` and `RESGATE POR ERRO`, a
class transfer and a correction, which move no money in or out. It refuses the file, naming the
line, for any other wording or a movement it can't read, so a new kind of movement is never
taken for a market change. The rows the upload adds are in "Bank statement parsers".

Trade Republic is imported from its transaction export, a CSV, one calendar month per file
(ADR-031); its PDF statement is not read. `TradeRepublicCsvParser` maps every row to one
transaction dated by `date`, in `datetime` order (the file's rows come in no order), with a
cash effect of `amount + fee + tax` (all signed: a fee charged on its own has `amount` 0 and
a negative `fee`) and the export's `description`. A row that moves no money is skipped with a warning.
It refuses, naming the row's date and type, any `account_type` but `DEFAULT`, any
`(category, type)` pair not seen in a real export (`CASH`: `CARD_TRANSACTION`,
`CARD_TRANSACTION_INTERNATIONAL`, `CARD_ORDERING_FEE`, `TRANSFER_INSTANT_INBOUND`,
`TRANSFER_INSTANT_OUTBOUND`, `INTEREST_PAYMENT`, `BENEFITS_SAVEBACK`; `TRADING`: `BUY`), any
row not in EUR (ADR-005), a buy of anything but a `FUND`, and a file whose rows span two
months. The statement is the calendar month (`PeriodFrom` the 1st), with an empty `Account`,
since the export has no IBAN, and relative balances (see "Bank statement parsers"). A `BUY`
row (`symbol` is the ISIN) carries its trade, with fees the absolute `fee + tax` and
`transaction_id` as its id.

Meal-card statements have no PDF parser: they are imported as pasted text (see "Bank
statement parsers") and stored under bank name `MEAL CARD`.

## Supported brokers

A broker is no account in Beacon: its export's trades become investment lots, with no
statement and no transactions (ADR-034; see "Investments", "Broker exports").

| Broker | Export | Detection signal |
|---|---|---|
| XTB | Monthly account export (XLSX), `EUR_<account>_<from>_<to>.xlsx` | Sheets "Cash Operations", "Closed Positions" and "Open Positions", each opening with "Account number" |

`XtbExportParser` reads the workbook through `XlsxWorkbook` (`Services/Parsing/`), which gives
each sheet's rows as cell text: shared strings resolved, numbers as stored, dates as Excel
serial numbers. Times are UTC, and a trade is dated by its day in Lisbon; the period is the
Cash Operations sheet's "Date from (UTC)" to "Date to (UTC)", Lisbon's first and last day of
the month. From Cash Operations, a `Stock purchase` is a buy and a `Stock sell` a sell, matched
by `Ticker` and named by `Instrument`. Quantity and price come from the comment
(`OPEN BUY 0.5 @ 600.00`; a split fill, `OPEN BUY 2/2.5 @ 100.00`, is the fill's 2, and a
sell reads `CLOSE BUY ...`), the operation's `ID` becomes `XTB:<ID>`, and there are no fees.
`Deposit` and `Subaccount transfer` rows (the bank transfer in, and cash moved between the "My
Trades" and "Investment Plans" subaccounts) are skipped. It refuses the file, naming the row,
for any other type, a category other than `ETF`, a comment it can't read, and an amount that
is not the quantity at the price, give or take a cent plus the price times 0.0001, the
rounding of the quantity's fourth decimal (more would be a commission or a currency
conversion, not seen yet). It also refuses an account not in EUR, by the file name's prefix or
the currencies in Open Positions (ADR-005). From Open Positions it reads "Data as of report
generated" and the bought positions summed by ticker, skipping each instrument's total row,
and refuses a position of another type (a short). Closed Positions is only part of the
detection.

## Environment variables

| Variable | Description |
|---|---|
| `ApiKey` | Secret for `X-Api-Key` header validation |
| `Storage__Path` | Directory where uploaded PDFs are stored |
| `Backup__Path` | Directory where backups are stored |
| `ConnectionStrings__DefaultConnection` | The SQLite file: `Data Source=<path>` |
| `Python__Executable` | Python binary (`python` on Windows, `python3` on Linux) |
| `Python__ExtractorScript` | Absolute path to `scripts/pdfExtractor.py` |
| `Python__TimeoutSeconds` | PDF extraction timeout (default 60); the Python process is killed on expiry |
| `Prices__Enabled` | Sync investment prices (default `true`; `false` in the demo) |
| `Prices__HistoryYears` | Years of daily closes the first sync of an asset stores (default 15) |
| `Prices__DailyRunTime` | Time of the daily price sync, UTC (default `22:00`, after the European close) |
| `Prices__GoldProxySymbol` | EUR-listed gold ETC used to price gold (default `4GLD.DE`, 1 unit = 1 gram) |
| `Prices__JumpWarningPercent` | Day-to-day move that logs a warning (default 20) |
| `Logs__Path` | Folder for the log files (optional; without it, or when it can't be written, logs go to the console only) |
| `Logs__Keep` | Days of log files kept (default 14) |
| `Logging__LogLevel__<Category>` | Minimum level for a category (`Default` for the rest), overriding the defaults in "Logging" |
| `GoogleServices__ClientId` | Google OAuth 2.0 client ID |
| `GoogleServices__ClientSecret` | Google OAuth 2.0 client secret |
| `GoogleServices__RedirectUri` | OAuth redirect URI: `http://localhost:5098/...` for development, the Tailscale HTTPS name for remote access (see "Google OAuth, Calendar and Tasks") |
| `GoogleServices__FrontendUrl` | Angular app origin the OAuth callback redirects to (e.g. `http://localhost:4200`, or `https://<device>.<tailnet>.ts.net`) |

Never commit these values. Locally they live in `local/environment.dev` (loaded by
`scripts/run-backend.ps1`) and `local/environment.demo` (loaded by
`scripts/run-backend-demo.ps1`, which always points the database at `local/beacon-demo.db`,
`Storage__Path` at `local/uploads-demo`, `Backup__Path` at `local/backups-demo` and
`Logs__Path` at `local/logs-demo`); in production in `local/environment` in the server's
checkout of `main` (loaded by systemd `EnvironmentFile`; the server has no `appsettings.json`).
How the server deploys is in README.md, "Deployment" (ADR-027).

## Tests

Backend: `api/Beacon.Tests/` (xUnit, ADR-025). A test class holds a `SqliteTestDatabase` in a
field, so every test gets its own in-memory SQLite database with the production schema and
connection setup; `CreateDb()` again gives a fresh context on the same database. A test of a
missing or damaged database file (the health handler's) points a context at a temporary folder
instead, since an in-memory database can be neither. Seed related
rows through navigations (`Category = cat`), not through ids read before `SaveChanges`: ids are
assigned on save. `Data/` pins the engine behaviour the app relies on (decimal sums and sorts
in SQL, searches and sorts with accents, `NOCASE` unique names, decimal scale, foreign keys).
Coverage: all bank/salary/grocery parsers (incl. the Trade Republic CSV export's row types,
sums, order and refusals, and the micro1
`Micro1InvoiceParser`/`DeelWithdrawalParser`/`Micro1Reconciler` two-PDF USD→EUR flow, with
`UnifiedUploadBatch` pairing/unpaired/ambiguous cases, the Mercor statement's sums, hours and
refusals with `MercorReconciler`, the batch's EUR suggestion (that month's Mercor credits,
from any bank, in the same batch too) and `ParseMercorStatement`, CSV files skipping the
extractor, and XTB exports applied oldest first, again, over lots typed by hand, refused, with
the holdings warning on the newest only, and an XLSX that is not XTB's), `XtbExportParser`
(purchases, split fills, sells, skipped rows, Lisbon dates, holdings and refusals, on synthetic
workbooks written with the Open XML SDK) and `XlsxWorkbook`,
`ParseVerifier`, `ApiKeyMiddleware`, `ExceptionHandlingMiddleware` (incl. a body over its
limit), `RequestLoggingMiddleware`, the log files (missing or unwritable folder, retention,
level defaults and overrides, JSON lines read back), `RuleMatch` (whole or partial text,
amount, both, neither), `ApplyRuleService` (incl. Excluded-category rules setting `IsExcluded`),
`GroceryReceiptUploadService` (rules with an amount), `FileStorageService`, `OrphanedPdfCleanup`
(relative, foreign and absolute stored paths, stored CSVs), `TradeImportService` (fees, dedup
by trade id, tickers and prices symbols, a lot typed by hand, sells within and beyond the
holdings), `XtbUploadService` (the holdings check), `StatementUploadService` (PPR recompute helper, Trade Republic balance chaining,
overlap and buys, rules with an amount), `YahooPriceHistorySource` (chart and OpenFIGI parsing on synthetic
responses, EUR check, retries, an unavailable source), `PriceHistorySyncService` (schedule,
queue, turned off), `XetraCalendar` and stale prices, the price-source migration, CQRS handlers
for Backup (incl. investment tables), Categories, Health (a missing, damaged or unmigrated
database; prices ok, stale or disabled), Transactions, Groceries (incl. Excluded-category sync
across `SetGroceryItemCategory`, `CreateGroceryItem` and `GroceryApplyRuleService`), Salary
(incl. `MergeSalarySlip`), Statements (incl. meal-card text import), Logs (level, time and text
filters, the limit, client errors with their caps), Investments (assets, lots, prices, oversell
validation, price sync with its sources, ISIN lookup and failures, a changed symbol, recent
prices and history queries with the price carried into a range, sync status, sync triggers),
input validation, `GoogleOAuthService` (each connection state), `GoogleCalendarService`,
`GoogleTasksService`, the Calendar and Tasks controllers' Google error responses, the health
route's status codes and the client-error route's size and rate limits on Kestrel
(`Controllers/`).

Frontend: Vitest specs next to the code (`*.spec.ts`), run by `ng test`.
