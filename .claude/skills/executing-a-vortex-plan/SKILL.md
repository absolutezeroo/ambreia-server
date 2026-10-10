---
name: executing-a-vortex-plan
description: The owner's way of running an implementation plan in the Vortex emulator — Claude implements every task itself, test first, commits each task in French with explicit paths, keeps the ledger in .superpowers/sdd/<plan>/progress.md, and dispatches a fresh reviewer after every two tasks whose Critical and Important findings become a "relecture des tâches N–M" commit; it ends on the full gate and a live driver proof. Use whenever the owner says to execute, run, continue or resume a plan from docs/superpowers/plans/, picks up at "Task N", or asks where a plan stands.
---

# Executing a Vortex plan

This is the owner's decision (2026-10-08: « c'est toi qui fait tout et un agent relit toutes les 2
tâches »). It layers on `superpowers:executing-plans`: load that one for the generic steps, this one
says what is specific here. No implementer subagents, no branch, no worktree: work on `main`.

## Before task 1

1. Read the plan **and** its spec (the plan's header names it). Where they disagree, ask.
2. Create the ledger `.superpowers/sdd/<plan-file-name>/progress.md` (the folder is not tracked):

   ```
   # SDD ledger — plan: docs/superpowers/plans/<plan>.md
   Spec: docs/superpowers/specs/<spec>.md
   Pre-flight: <what each task produces that a later one consumes, and whether the names agree>
   ```

   Resuming? Read the ledger first: it is the truth about what is done, not the plan's checkboxes.
3. Load the skills the tasks need (`adding-a-packet`, `adding-rpg-content`, `adding-an-rpg-extension`,
   `changing-the-schema`…) as each task reaches them.

## Each task

1. Copy the task into `task-<n>-brief.md` in the ledger folder: it is what the reviewer reads.
2. **Test first**: write the failing tests, run them, and keep the red output (`t<n>-red.txt`). A test
   that passes before the change is not a failure to hide. Note it as a Ruling: it pins behaviour
   the change must keep.
3. Implement with `Edit`/`Write` (the owner wants visible diffs, never scripted edits). Run the task's
   tests with output to a file (`task-<n>-tests.log`), never through a pipe.
4. A choice the plan did not make is a **Ruling**, one line in the ledger with its cost if wrong:
   `Task 3: Ruling: <what and why> — cost if wrong: <…>`. Only stop to ask when the cost is real.
5. **Commit the task**: French message, `<domaine> : <ce que ça fait>` (`compétences : télégraphes —
   cases figées, impact par cases`), explicit paths (`git commit -o <paths>`), never `git add -A`.
6. Ledger line: `Task <n>: complete (commits <from>..<to>, tests: <command> → <summary line>)`.

## Every two tasks: the review

1. `git diff <before task N>..<after task N+1> > .superpowers/sdd/<plan>/review-<from>..<to>.diff`.
2. Dispatch **one fresh agent** over that range, with both briefs and the spec sections they cite:
   `grain-rules-reviewer` when the diff touches `Grains/`, `Vortex.Rooms` or `Vortex.Players`, else
   `feature-dev:code-reviewer`; add `modularity-reviewer` when the tasks add an extension point or
   a piece. Ask for findings ranked Critical / Important / Minor, with file:line.
3. Verify each finding before acting (`superpowers:receiving-code-review`): a reviewer can be wrong.
   - **Critical / Important**: fix with a failing test first, RED→GREEN, then one commit
     `<domaine> : relecture des tâches N–M — <les corrections en quelques mots>`.
   - **Minor**: ledger only, `minor (deferred)`.
   - **Wrong**: ledger, `declined — <why>`.
4. Ledger lines `Review T<N>-<M>: fixed …` / `minor (deferred): …` / `declined — …`.

An odd last task gets its own review.

## The end

1. `VortexCloudQualityGate` in the background (`run_in_background`), output to a file. Green, or fix.
2. **Live proof**: the plan's driver scenario (`run-vortex-emulator`), and if the plan adds one, write
   it into the driver and its table in that skill's `SKILL.md`. A plan that changes what a player sees
   is not done on a green build.
3. Docs the plan names (`docs/architecture.md`, AGENTS.md tables, the driver table), in the same
   commits as the code they describe.
4. For a large plan, a `docs/superpowers/plans/<plan>.bilan.md` with before/after numbers and the
   deferred minors.
5. Report to the owner in French: tasks done, review fixes, deferred minors, gate and live results
   (commands and output), and what the next milestone is.
