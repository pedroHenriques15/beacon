# Beacon — Frontend

Angular 21 frontend for the Beacon personal finance dashboard.

## Development

```bash
# Install dependencies (first time)
npm install

# Start dev server — proxies /api/* to http://localhost:5098
npx ng serve
```

Open http://localhost:4200. The API must be running separately — see the root README.

## Commands

| Command                  | Description                        |
| ------------------------ | ---------------------------------- |
| `npx ng serve`           | Dev server with hot reload         |
| `npm run build`          | Production build → `dist/browser/` |
| `npm test -- --run`      | Vitest unit tests (one-shot)       |
| `npm test`               | Vitest with watcher                |
| `npx prettier --write .` | Format before committing           |

## Architecture

- Standalone Angular components, no NgModules
- Signal-based state — `FinanceService` is the single source of truth
- Lazy-loaded routes defined in `app.routes.ts`
- `apiKeyInterceptor` injects `X-Api-Key` on every `/api` request
