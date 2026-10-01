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
| Calendar and tasks (Google) | built |
| Workflow and tooling | markdown tasks, git hooks and CI formatting checks in place |

## Now

Nothing is in progress; the next items come from "Next".

## Next

Quality:

- Test edge cases thoroughly (large values, for example).
- Firefox testing.
- An error and non-error logging system.
- Improve the demo database so it covers many cases, and build a database with real data
  for personal use.

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
- Android app.
- Automatic email scanning and better email rule filters (research).
