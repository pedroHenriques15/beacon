# Beacon: Roadmap

What is planned, by theme. When an item is picked up, its detail goes into a private task
file (git-ignored, ADR-020) and the line stays here until the PR that finishes the work
removes it. Settled decisions go to DECISIONS.md.

## Status

| Area | State |
| --- | --- |
| Statements, rules, transactions, analytics | built |
| Salary | built |
| Groceries | built |
| Investments | built · how transfers count in totals is open |
| Calendar and tasks (Google) | built · reconnect bug open |
| Workflow and tooling | markdown tasks in place · hooks and CI checks open |

## Now

- Git hooks for the protected branches, commit subjects and task branches.
- CI formatting checks, a PR template and GitHub settings that match the branch model.

## Next

Quality:

- Test edge cases thoroughly (large values, for example).
- Firefox testing.
- An error and non-error logging system.
- Improve the demo database so it covers many cases, and build a database with real data
  for personal use.

Tooling:

- Vulnerability scanning in CI (GitHub Actions).

Integrations:

- Google appears connected, but the calendar does not load after a long time without use.
- The Angular dev server warns that it is for local testing only when it is bound with
  `--host` ("It hasn't been reviewed for security issues"); decide how to serve the client
  to other devices without it.

## Later

- Import and export data from several pages.
- Database replication.
- Users and OAuth: user configuration that works across devices and is easy to set up and
  connect. Decide whether this makes sense for an open-source repository (ADR-001).
- Calendar: offline tables synced with Google, with full support for a local-only setup.
- Investments: decide how investment transfers and transfers between accounts count in
  analytics and totals, if at all.
- Settings: more options.
- Update the README, including the screenshots.
- Better Docker containers. The dev container is gone (ADR-019); decide whether this still
  means anything for production.
- Android app.
- Automatic email scanning and better email rule filters (research).
