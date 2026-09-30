# Beacon: Decision Log (ADRs)

Settled decisions with their rationale. Don't re-litigate these in a session; if one truly
needs revisiting, add a superseding entry rather than silently diverging. How each decision
shows up in the code is in ARCHITECTURE.md.

## ADR-001 · Self-hosted, reached only over a private network

Beacon holds a person's full financial history, so it runs on a home Ubuntu server and is
reached over Tailscale, never exposed to the internet. Reachability is the trust boundary: a
single shared API key (`X-Api-Key`, embedded in the built frontend) protects every endpoint,
and there are no user accounts. Exposing the app publicly would need real authentication
first (ROADMAP, "Users and OAuth").

## ADR-002 · Feature-driven CQRS with plain handler classes

One folder per use case under `Features/{Feature}/Commands|Queries/`, holding its command or
query, its handler and an optional response. Handlers are plain classes registered one by one
in `Program.cs`; no MediatR or other CQRS library. The folder name is the index, so finding a
use case needs no search, and the wiring stays explicit: no reflection, no pipeline
behaviours, and one file that lists every handler.

## ADR-003 · PDF text extraction in Python with pdfplumber

.NET runs `scripts/pdfExtractor.py` as a child process and receives the page text as JSON;
every parser works on that text. The parsers match on pdfplumber's text layout (line order,
column spacing), so changing the extractor means re-verifying every parser. The process is
killed after `Python__TimeoutSeconds`, so a malformed PDF cannot hang a request. Cost
accepted: Python with pdfplumber is a runtime dependency wherever the API runs.

## ADR-004 · One parser per document format, discovered through DI

Bank statements, salary slips and grocery receipts each have an interface
(`IBankStatementParser`, `ISalarySlipParser`, `IGroceryReceiptParser`) with
`CanParse(fullText)` and `Parse(...)`. A factory receives every registered implementation and
picks the one whose `CanParse` matches, so a new format is one class and one `AddSingleton`
line, with no switch to extend. Each detection signal must belong to exactly one format.

## ADR-005 · Statements are EUR-only

`StatementUploadService` rejects a parsed statement whose currency is not EUR, and every
aggregate assumes EUR. There is no FX layer; the one conversion in the app is the micro1
paycheck (ADR-007), which uses the rate of the real withdrawal. Supporting another currency
means adding currency to the aggregates first.

## ADR-006 · The Excluded category is authoritative for `IsExcluded`

`IsExcluded` is what removes a row from totals and analytics. The protected Excluded category
(one in `Categories`, one in `GroceryCategories`) drives that flag: every category assignment
goes through `ExcludedCategory.ApplyCategory`, which sets the flag when a row enters Excluded
and clears it when the row leaves. A label and a flag that drift apart show up as a spending
line labelled "Excluded". A row excluded with no category (Trade Republic savings-plan buys)
keeps its flag when a category is assigned later. The old "Internal Transfer" category was
the same concept under another name; migration `MergeInternalTransferIntoExcluded` folded it
into Excluded, and it does not come back.

## ADR-007 · A micro1 paycheck is imported only as an invoice and withdrawal pair

A micro1 paycheck arrives as a USD invoice plus a Deel withdrawal confirmation, and neither
is a complete EUR slip alone. `Micro1InvoiceParser` is therefore not registered in the
salary parser factory, so a lone invoice can never be imported as fake EUR.
`UnifiedUploadBatchCommandHandler` pairs the two by cent-equal USD amount; an unpaired or
ambiguous file is shown as blocked and never imported. EUR comes from Deel's real rate, net is
the EUR that actually arrived, and the difference is booked as a "Deel exchange fee"
deduction so the slip reconciles.

## ADR-008 · One salary slip per profile per month; a second pay run is merged explicitly

`SalarySlips` has a unique `(SalaryProfileId, Period)` index, with `Period` on the 1st of the
month. A cycle that pays twice a month folds the second run into the existing slip through
`POST /api/salary/slips/{id}/merge`, while `CreateSalarySlip` keeps rejecting duplicates.
Merging is always the user's explicit choice, never a silent fallback, so a mistaken
duplicate upload cannot double a month.

## ADR-009 · Salary item categories belong to one profile

Each `SalaryProfile` has its own `SalaryItemCategories`. The list endpoint requires
`profileId`, and incoming line items are validated against the target slip's profile.
Employers name and group pay items differently; one shared list would mix them.

## ADR-010 · Meal-card statements are imported as pasted text

Meal-card movements are pasted as text (`POST /api/statements/import-text`, parsed by the
static `MealCardTextParser`) rather than uploaded as a PDF. The closing balance may be left
empty: it is then derived from the adjacent previous meal-card statement. With no such
statement the import is rejected rather than guessed.

## ADR-011 · Investments are priced in EUR without FX; gold is tracked in grams

ETFs are assumed EUR-listed (UCITS) and their quotes are stored as EUR. Gold quantities are
grams, priced through an EUR-listed physical gold ETC (`AlphaVantage__GoldProxyTicker`,
default `4GLD.DEX`, one unit = one gram), because Alpha Vantage removed XAU from its currency
endpoints. The proxy trades at a small premium or discount to spot; accepted. A
non-EUR-listed ticker must not be added.

## ADR-012 · Alpha Vantage free tier, spread across the trading day

Prices come from Alpha Vantage's free tier (25 requests a day).
`InvestmentPriceRefreshService` spreads `DailyQuota - ReservedForManual` across US market
hours, 13 s apart, and skips the startup refresh when every asset already has today's
snapshot; a history backfill is one request per asset and refuses to re-bill. Failures are
logged and never stop the host. Free-tier closes are unadjusted, so splits and distributions
can step the history; accepted.

## ADR-013 · P&L uses average cost basis, computed in the client

`investments.service.ts` (`assetMetrics`) keeps a weighted average cost per asset, fees
included; a sell books realised P&L against it. Lots store signed quantities (positive buy,
negative sell), and sells are validated against net holdings on both server and client.

## ADR-014 · Trade Republic savings-plan buys are transfers, not spending

`Savings plan execution` rows stay debits so the statement balance reconciles, but
`StatementUploadService` marks them excluded (bank `TRADE REPUBLIC` only): they move cash
into an investment. `SavingsPlanImportService` then turns each one into an `InvestmentLot`,
creating the ETF asset on first sight by ISIN. The import is idempotent and its failures
never fail the upload.

## ADR-015 · Backend tests use EF Core InMemory, never a mocked AppDbContext

Handler and service tests run against the InMemory provider with a unique database name per
test, so state never leaks between tests. A mocked `AppDbContext` tests the mock; InMemory
runs the real queries, within its limits. The one test that needs SQL Server (backup and
restore round trip) runs only when `BEACON_TEST_SQLSERVER` is set. Superseded by ADR-025.

## ADR-016 · Angular standalone components and signals

No NgModules. Pages are lazy-loaded standalone components, and state lives in signals:
`FinanceService` is the single source of truth for statements and everything derived from
them, reloaded after every mutation.

## ADR-017 · One Google account through a single stored OAuth token

Calendar and Tasks share one access and refresh token pair in `GoogleOAuthTokens`, refreshed
by `GoogleOAuthService`, which fits a single-user app (ADR-001). The OAuth callback is the one
API route exempt from the API key, because Google's redirect cannot carry it. Tasks appear
inside the Calendar page; there is no separate route.

## ADR-018 · Everything is public except secrets, personal data and task files

Beacon is open source (MIT), and so is the way it is built: `CLAUDE.md`, the docs (this file,
ARCHITECTURE.md, ROADMAP.md), the Claude agents and skills, the dev scripts and the VS Code
tasks all live in the public repository. `.gitignore` keeps out what would leak secrets or a
person's data: `local/` (environment files, real statements, uploads, backups),
`appsettings.json` and its variants, machine-specific Claude permissions
(`.claude/settings.local.json`) and every PDF. It also keeps out `docs/tasks/`: task files are
private working notes, and the public record of the work is the PRs, ROADMAP.md and this file
(ADR-020). Docs, fixtures and seed data use synthetic data only. Decided on 2026-09-30,
replacing the earlier setup in which `CLAUDE.md`, `TODO.md` and the Claude config were hidden
with `.git/info/exclude` and copied by hand to a separate private repository. Consequence:
task files exist only on the machine where they are written, and CI cannot read them, so task
checks run in local git hooks.

## ADR-019 · Development runs on the host; no dev container

Removed on 2026-09-30. The dev container duplicated the host toolchain (.NET 8 SDK, Node,
Python with pdfplumber) while SQL Server already ran on the host, and it leaked Linux-only
state into the shared working tree: Linux binaries in `web/node_modules`, and `/workspaces/...`
paths stored in the database. Beacon runs directly on the host: `scripts/run-backend.ps1`
loads `local/environment.dev` (SQL Server on `localhost`) and `scripts/run-frontend.ps1`
serves the client.

## ADR-020 · Tasks are private markdown files, one per piece of work

Each piece of work is a file `docs/tasks/NNN-work-name.md` (rules and template in
`docs/tasks/README.md`), created with the `task` skill. The folder is git-ignored (ADR-018):
a task holds working notes (the detail, the dead ends, the state of the dev machine), and none
of that needs publishing. What is published is the result: the PR, the ROADMAP.md line it
removes and any ADR it adds, so public docs never link to a task file. This replaced
`TODO.md` on 2026-09-30. The files work offline and a Claude session reads and writes them
directly; GitHub Issues would be public, and a chat-based board such as KAZAU's is team
tooling.

## ADR-021 · `main` runs on the server; `development` is integration

Task branches (`prefix/NNN-work-name`) are cut from `development` and squash-merged back
through a PR. `main` changes only through a release PR from `development`, merged with a merge
commit (a squashed release makes the branches diverge), and the server is deployed from
`main`. Direct pushes to either branch are not allowed, with one exception: a planning commit,
which changes only `docs/ROADMAP.md`, may go straight to `development`, so updating the plan
needs no PR.

## ADR-022 · No generated code graph

The knowledge-graph tool (graphify) was removed on 2026-09-30: it had to be regenerated after
every change, it was installed only in the dev container, and its hooks ran on every search
and file read. The folder convention (`Features/<Area>/Commands|Queries/<UseCase>/`) already
locates code in one step, and the reasoning a graph cannot capture lives in this file and in
ARCHITECTURE.md.

## ADR-023 · Stored PDF paths are file names under `Storage__Path`

`PdfPath` holds only the stored file's name (`<guid>.pdf`), and `FileStorageService` resolves
it against `Storage__Path` whenever a file is opened. Decided on 2026-09-30. Absolute paths had
tied the database to one machine's storage root: moving from the dev container to the host,
or restoring a backup on another machine, broke every PDF link, and a path written elsewhere
made the startup cleanup delete the file it named. The folder stays flat, so a name alone
identifies a file. Existing rows were rewritten by a migration, and backup restore keeps only
the file name of every restored path.

## ADR-024 · SQLite replaces SQL Server

Beacon has one user, one process and little data, yet SQL Server made it need a database
server: an x86-64 host with 2 GB of RAM for the server alone, a service to patch and secure,
and credentials in the connection string. Since 2026-09-30 the database is one SQLite file
inside the app's process, on EF Core 10, whose SQLite provider translates decimal sums,
comparisons and sorts. What SQL Server's collation and column types gave implicitly is now
explicit: text columns use `NOCASE`; lists shown in order sort with a `DISPLAY_ORDER`
collation registered on every connection and used only in queries, so a change in .NET's sort
rules can never invalidate an index; SQLite's `lower()`/`upper()` are replaced by .NET's so
searches fold accented letters; and `SaveChanges` rounds each decimal to its declared scale.
The migrations start over from one SQLite `InitialCreate`; an existing database moves with
`scripts/MigrateToSqlite`, which copies every table through EF, keeping ids, and compares the
result row by row. The backup round trip that needed SQL Server (ADR-015) now runs on every
test run. Costs accepted: one writer at a time, and `NOCASE` folds only ASCII letters in
equality and unique names.

## ADR-025 · Backend tests run on an in-memory SQLite database

Supersedes ADR-015. With SQLite in production (ADR-024), every handler and service test runs
on its own in-memory SQLite database with the production schema and connection setup
(`SqliteTestDatabase`, held in a field: xUnit creates the test class, and so the database, for
every test). EF Core InMemory ran queries as LINQ to Objects, so it could not catch a query
SQLite cannot translate, a foreign key pointing nowhere or a violated unique index (ADR-008's
one-slip-per-month index was never exercised); the move surfaced seven tests that seeded
foreign keys from ids InMemory had handed out before saving. A mocked `AppDbContext` still
tests the mock, so it stays out. The suite still runs in a few seconds.
