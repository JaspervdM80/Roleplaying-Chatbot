---
name: pre-pr
description: Get a finished change ready for a pull request — up to date with origin/main, Release build, tests, a UI check for UI changes, the code-reviewer agent, the docs and skills it touches, and a PR body in the repository's template. Use when a change is done and before asking to commit, push or open a pull request; also as /pre-pr.
---

# Before the pull request

Everything that can be checked before a change leaves this machine, in the order that fails
cheapest. **It stops before committing**: a commit, a push or a pull request happens only when asked.

## 1. Start from main

```bash
git fetch origin main
git rev-list --count HEAD..origin/main      # 0, or rebase onto origin/main first
git branch --show-current                   # feature/…, bug/… or ci/… — rename anything else
```

Skip this while the repository has no remote yet, and say so.

## 2. Build and test

```bash
dotnet build -c Release        # warnings are errors there; the push hook runs this too
dotnet test
```

Integration tests need Docker (Testcontainers Postgres). If Docker is not running, say which tests
did not run rather than reporting the suite green.

## 3. UI changes

For a change to a `.razor` file or any CSS, work through the **`verify-ui`** skill over the pages it
can reach. For a change to `tests/ui`, run the specs it touches.

## 4. Review

Run the **`code-reviewer`** agent over the branch and fix every Blocking finding. A Should-fix you
leave is said out loud in the pull request, with why.

## 5. The record

- Behaviour changed → the skill that states the rule changed in the same branch.
- A non-obvious bug's cause → a `docs/known_issues/` entry.
- A migration → its generated `Up()` read line by line (the `migrations` skill), and said in the body.

## 6. The pull request body

Follow `.github/pull_request_template.md`: prose about what changed, why, and how it was checked, as
long as the change deserves. Say out loud when it applies:

- **a migration** — the app applies it on startup;
- **anything another user can now see or do differently** — every row belongs to one owner;
- **a change to what is sent to an AI provider** — prompts, memories and images leave the machine.

List what was run and its result, and what was not, with why. End with the attribution line the
session gives.

## 7. Stop

Report what is ready and what is not. Commit, push and open the pull request only when asked;
never push to `main`, and merging is a person's call.
