---
name: task
description: Draft, start, finish or drop a Beacon task file in docs/tasks/. Use for "/task draft <the work>", "/task start 004", "/task finish 004", "/task drop 004 <reason>", or whenever the user wants to plan, pick up or close a piece of work.
---

# Tasks

Task files live in `docs/tasks/`, which is git-ignored: they are private and never committed
(ADR-018, ADR-020). Never link to a task file or cite its number in a tracked file (ROADMAP.md,
ARCHITECTURE.md, CLAUDE.md, code, commit messages beyond the `prefix(NNN):` subject). The
template, statuses, priorities and numbering are in `docs/tasks/README.md`; branch and commit
rules are in CLAUDE.md, "Git workflow". Follow CLAUDE.md for everything not said here. Commit,
push or open a PR only when the user asks.

The first word of the arguments picks the mode. With no mode, list the open tasks (number,
title, status, priority) from `docs/tasks/` and say which one is next.

## draft <the work>

1. Read the code the work touches first, so every path in the task is real.
2. One purpose per task. When the work has several, draft several tasks and record what
   depends on what in `depends-on`.
3. Number: one more than the highest in `docs/tasks/` and `docs/tasks/done/`.
4. Write `docs/tasks/NNN-work-name.md` from the template with `status: todo`. "What" must
   stand on its own: a session with no other context should be able to do the task from it.
5. If the work comes from a ROADMAP.md line, copy that line into `roadmap:`. Leave ROADMAP.md
   itself alone. If the work is new and belongs on the public plan, offer to add a line to
   ROADMAP.md (a planning commit), described in words, without the task number.
6. Reply with a summary of each task and what you did not verify in the code. Do not start
   the work.

## start NNN

1. Read the task and its "Read first" list. If `depends-on` names a task that is not done,
   stop and say so.
2. The working tree must be clean; otherwise stop and list what is pending.
3. `git switch development`, `git pull --ff-only`, then `git switch -c <branch from the task>`.
4. Set `status: doing`. Do the work, staying out of "Out of scope", and stop and report when
   an assumption in the task turns out false.
5. Work found along the way that the task does not cover becomes a new drafted task, not part
   of this branch's change.

## finish NNN

1. Tick each "Done when" item that is true, citing the evidence (test name, command output).
   Stop and report any that is not true.
2. Run the checks in CLAUDE.md, "Definition of done". If the task has a `roadmap:` line,
   remove that line from ROADMAP.md on the branch, so the PR carries it. Add an ADR to
   DECISIONS.md for any lasting decision.
3. Draft the PR: title `prefix(NNN): summary`; description with What, Why, How tested, and
   either screenshots or the line `No visual change.` The description is public: summarise the
   work, don't paste the task file. Screenshots are taken at 1440 px and 390 px against the demo
   database (`scripts/run-backend-demo.ps1`), committed to the task's
   `screenshots/NNN-work-name` branch, pushed with the task branch and linked from the
   description (CLAUDE.md, "Git workflow", "Screenshot branches").
4. Once the user says the PR is merged: set `status: done` and move the file to
   `docs/tasks/done/`.

## drop NNN <reason>

Set `status: dropped`, write the reason in Notes and move the file to `docs/tasks/done/`. If
the task came from ROADMAP.md, ask whether its line should go too.
