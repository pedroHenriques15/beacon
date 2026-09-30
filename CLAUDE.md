# CLAUDE.md: Beacon

Beacon is a self-hosted personal finance dashboard. Users upload bank statement, salary slip
and grocery receipt PDFs; the app extracts the transactions, categorises them and charts
spending. It runs on a home Ubuntu server reached over Tailscale. Everything is open source
(MIT), docs included; only secrets, personal data and the task files stay out of git
(ADR-018).

Docs move with the code: a change to how something works updates the doc that describes it,
in the same task (see "Definition of done").

## Read before working

- `docs/ARCHITECTURE.md`: how Beacon is built and where each kind of logic lives. Read the
  section for the area you touch before changing it.
- `docs/DECISIONS.md`: settled decisions (ADRs) with their rationale. Don't re-litigate; if
  one must change, add a superseding ADR.
- `docs/ROADMAP.md`: what is planned, by theme.
- `docs/tasks/`: one private file per piece of work, rules in its README. Work happens on a
  task (see "Tasks").

## What stays out of git

Everything is public except secrets, personal data and the task files (ADR-018). `.gitignore`
keeps out:

- `docs/tasks/`: the task files, private working notes that exist only locally.
- `local/`: environment files with secrets, the SQLite databases, real statements and uploads,
  backups.
- `appsettings.json` and `appsettings.*.json` (all but the template): connection strings, keys.
- `.claude/settings.local.json`: machine-specific permissions (server paths, `sudo` rules).
- `.vscode/`, except the shared `tasks.json` and `launch.json`.
- Every PDF, and every SQLite file (`*.db`, `*.db-wal`, `*.db-shm`) wherever it lands.

Anything tracked is published, docs included. No real names, IBANs, NIFs, emails, figures
from real statements, personal paths, hostnames, IPs or keys in code, docs, fixtures or seed
data. Never `git add -f` an ignored file.

## Repository map

```
api/Beacon.Api/     ASP.NET Core 10 API: Controllers/, Features/ (one folder per use case),
                    Services/ (uploads, storage, Google, pricing), Services/Parsing/ (parsers),
                    Models/, Data/ (AppDbContext), Migrations/, Program.cs (DI + startup)
api/Beacon.Tests/   xUnit tests on EF Core InMemory
web/src/app/        Angular 21 client: core/ (services, models, interceptors), pages/ (routes)
scripts/            pdfExtractor.py (run by the API), deploy.sh, reset-db, run-backend/-frontend,
                    setup (enables the git hooks), MigrateToSqlite/ (SQL Server database to SQLite)
.githooks/          commit-msg and pre-push: the "Git workflow" rules, enforced locally
docs/               ARCHITECTURE, DECISIONS, ROADMAP, screenshots/; tasks/ (git-ignored)
.claude/            agents/ (scaffolders), skills/task/ (task workflow), settings.json (shared)
local/              git-ignored: environment.dev/.demo, beacon.db, uploads/, backups/ (the
                    demo's own: beacon-demo.db, uploads-demo/, backups-demo/), sample PDFs
```

The full tree is in ARCHITECTURE.md, "Repository layout". Update both when the layout changes.

## Invariants: never violate

1. **No real personal data in the repository**, docs included. Fixtures, seed data and
   screenshots are synthetic or come from the demo database.
2. **No secrets in tracked files.** Local config lives in `local/environment.*` and
   `appsettings.json`; production config in `/etc/beacon/environment`.
3. **Every category assignment goes through `ExcludedCategory.ApplyCategory`** (ADR-006).
   Never write `.CategoryId = …` directly.
4. **Totals and charts count only non-excluded rows**: use `allTransactions` and
   `countedItems`, never `allTransactionsRaw` or `allItems`.
5. **Statements are EUR-only** (ADR-005). No aggregate mixes currencies.
6. **A micro1 invoice is never imported without its Deel withdrawal** (ADR-007).
7. **One salary slip per profile per month** (ADR-008). A second pay run is merged
   explicitly, never created alongside.
8. **Salary item categories belong to one profile** (ADR-009). Validate line items against
   the target slip's profile.
9. **Backend tests use EF Core InMemory with a unique database name per test.** Never mock
   `AppDbContext` (ADR-015).
10. **Angular uses standalone components and signals only.** No NgModules (ADR-016).

## Conventions

Backend:

- One folder per use case, `Features/{Feature}/Commands|Queries/{UseCase}/`, with files named
  `{UseCase}{Command|Query|CommandHandler|QueryHandler|Response}.cs`, matching the use case
  exactly.
- Register every new handler in `Program.cs` as `AddScoped<THandler>()`. No MediatR or other
  CQRS library (ADR-002).
- A new document format is one parser class plus one `AddSingleton` line (ADR-004); the steps
  are in ARCHITECTURE.md. DI lifetimes follow the table there.
- A new entity gets a `DbSet<T>` in `Data/AppDbContext.cs` and a migration.
- The database is SQLite (ADR-024; details in ARCHITECTURE.md, "Database"). Declare a decimal
  column with `HasPrecision`, never `HasColumnType`. Sort text shown to people with
  `EF.Functions.Collate(x, SqliteSetup.DisplayOrder)`, and search text with
  `x.ToLower().Contains(term.ToLowerInvariant())`. Contexts come from `UseBeaconSqlite`.
- The `beacon-feature-scaffolder` and `beacon-parser-scaffolder` agents generate new use cases
  and parsers in this shape.
- Run `dotnet format beacon.sln` (repository root) before committing. It uses the default .NET
  style, so don't align columns with extra spaces; the formatter removes them.

Frontend:

- File names have no `.component.ts` suffix (plain `.ts`).
- Pages are lazy-loaded standalone components registered in `app.routes.ts`.
- `FinanceService` is the single source of truth for statements; call `reload()` after any
  mutation.
- Run `npx prettier --write .` in `web/` before committing.
- Production budgets: 500 KB initial warning, 1 MB error. Keep bundles small.
- CORS whitelists only `http://localhost:4200`; production is same-origin behind Nginx.

SCSS:

- File size limits: page entry file at most 200 lines (split into partials beyond that);
  `_shared.scss` at most 300 lines; a partial at most 200 lines.
- Partials are split by UI concern, `_{page}-{concern}.scss`, next to the component's main
  `.scss`; the entry file imports them with `@use '{partial}' as *;`.
- `@extend` does not cross `@use` boundaries in Dart Sass: a partial that extends a
  placeholder must `@use` the file defining it.
- Where a class goes: one component only, that component's SCSS or partials; two or more
  components, `_shared.scss`; layout (sidebar, nav, content wrapper), `app/app.scss`; CSS
  custom properties, `:root` in `styles.scss`.
- Never hard-code a color that has a CSS variable; check `:root` first. A new color used in
  more than two places becomes a variable named for its meaning (`--danger`, not `--red`).
  Current variables: `--bg`, `--surface`, `--border`, `--text-muted`, `--text-secondary`,
  `--text-primary`, `--text-heading`, `--text-body`, `--primary`, `--primary-light`,
  `--success`, `--warning`, `--danger`, `--credit`, `--debit`, `--surface-raised`, `--focus`.
- `%placeholder` rules live in a `_{page}-base.scss` partial, not the entry file.
- `@keyframes spin` is defined once in `_shared.scss`; `.page-header` is defined in
  `_shared.scss`. Don't redeclare either.
- Use `@mixin card($radius: 12px)` instead of repeating background, border and radius.
- Always `@use`, never `@import`.

## Tasks

- Every change belongs to a task file, `docs/tasks/NNN-work-name.md`. Use the `task` skill:
  `/task draft <the work>`, `/task start NNN`, `/task finish NNN`, `/task drop NNN <reason>`,
  or `/task` alone to list open tasks.
- Task files are private: `docs/tasks/` is git-ignored, so drafting a task, changing its
  status and moving it to `docs/tasks/done/` are local edits, never commits. Public docs
  (ROADMAP.md, ARCHITECTURE.md, this file) never link to a task file or cite its number; the
  public record of the work is the PR, the ROADMAP.md line it removes and any ADR it adds.
- Respect the task's "Out of scope". Work found along the way becomes a new task.
- Status lives in the task file: `todo`, `doing`, `blocked`, `done`, `dropped`.

## Definition of done

Before a task's PR:

- New logic has tests. `dotnet test Beacon.Tests/` (in `api/`) and `ng test --watch=false`
  (in `web/`) pass, and `npm run build` stays within its budgets.
- The formatters have run: `npx prettier --write .` in `web/` and `dotnet format beacon.sln`.
  CI fails otherwise (`prettier --check`, `dotnet format --verify-no-changes`).
- If the work was a ROADMAP.md item, the PR removes its line.
- The docs follow the code: ARCHITECTURE.md for how things work, a new ADR in DECISIONS.md
  for a settled decision, README.md (public) for setup, commands or environment variables,
  and this file for workflow and conventions.
- A visible change is shown in the PR at 1440 px and 390 px wide, captured against the demo
  database (`scripts/run-backend-demo.ps1`), never real data. Otherwise the description says
  `No visual change.`

## Git workflow

- **`main` is what runs on the server** (ADR-021). It changes only through a release PR from
  `development`, merged with a merge commit, never squashed (a squashed release makes the
  branches diverge). Deploy from a checkout of `main` with `scripts/deploy.sh --production`.
- **`development` is integration.** Task branches are cut from it and come back through a PR,
  squash-merged.
- **Branch names**: `prefix/NNN-work-name`, for example `fix/012-upload-timeout`.
  Prefixes: `feature`, `fix`, `refactor`, `tests`, `chore`, `merge`.
- **Commit subjects and PR titles**: `prefix(NNN): imperative summary`, at most 72
  characters, for example `fix(012): stop large uploads timing out`. The body says why,
  not what. A squash-merged PR's title becomes the commit subject.
- **Hotfix**: branch from `main`, PR into `main`, then bring `main` into `development`
  through a `merge/NNN-main-into-development` branch merged with a merge commit.
- **Never push directly to `main` or `development`**, with one exception: a planning commit,
  which changes only `docs/ROADMAP.md`, may go straight to `development` (subject like
  `chore: update roadmap`), so updating the plan needs no PR.
- **Hooks** in `.githooks/` enforce these rules locally once a clone has run
  `scripts/setup.ps1` or `scripts/setup.sh`. `commit-msg` rejects a subject over 72
  characters and, on a task branch, one that does not start with the branch's
  `prefix(NNN): `. `pre-push` refuses deleting `main` or `development`, any push to `main`,
  a push to `development` with anything but planning commits, and a task branch whose task
  file is missing or `dropped` (skipped in a clone without `docs/tasks/`). Never bypass them
  with `--no-verify`.
- **Pull requests** open with `.github/pull_request_template.md` (What, Why, How tested,
  screenshots or `No visual change.`). CI checks formatting, runs the tests and builds the
  client. On GitHub the default branch is `development`, squash and merge commits are
  allowed (rebase merging is off), and head branches are deleted after the merge.
- Commit, push or open PRs only when asked to.

## Commands

Development runs on the host (ADR-019): .NET 10 SDK, Node 22 or newer, Python 3 with
`pdfplumber`. The database is a SQLite file, `local/beacon.db`. Config: `local/environment.dev`
(demo: `local/environment.demo`).

```
scripts/setup.ps1              once per clone: enable the git hooks (scripts/setup.sh on Linux)
scripts/run-backend.ps1        load local/environment.dev, apply migrations, API on :5098 (/swagger)
scripts/run-frontend.ps1       wait for the API, then ng serve on :4200
scripts/run-backend-demo.ps1   API against the demo database (local/beacon-demo.db), uploads-demo and backups-demo
VS Code "Beacon: Start All"    backend and frontend together (also "Start All (Demo)")
scripts/reset-db.ps1           drop and recreate the local database (reads appsettings.json)

cd api && dotnet test Beacon.Tests/                     backend tests
dotnet format beacon.sln                                format the backend (repository root)
dotnet tool restore                                     dotnet-ef pinned in dotnet-tools.json (scripts run it)
cd api/Beacon.Api && dotnet ef migrations add <Name>    new migration
cd api/Beacon.Api && dotnet ef database update          apply migrations (run-backend does it on start)
cd web && ng test --watch=false                         frontend tests
cd web && npm run build                                 production build (dist/)
cd web && npx prettier --write .                        format the frontend
```
