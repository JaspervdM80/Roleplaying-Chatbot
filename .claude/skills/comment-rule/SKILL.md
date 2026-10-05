---
name: comment-rule
description: When Claude should and should NOT write code comments, plus the repository facts that change how the rule applies here. Apply on any code edit/write/refactor; the SessionStart hook points at it every session.
---
# The code commenting rule

**Default: write no comments.** A comment must justify its existence; the absence of a comment is the right answer almost always.

## When a comment IS warranted

Only when the **WHY** is non-obvious and a future reader would otherwise be confused or break the code:

- A hidden constraint (e.g. "API requires this header lowercased — server rejects mixed case").
- A subtle invariant (e.g. "must run before X is mounted, otherwise the event listener attaches to the wrong target").
- A workaround for a specific bug or spec quirk (e.g. "Ollama returns an empty final chunk — skip it").
- Behavior that would surprise a competent reader of this code.

Keep it to **one line** wherever possible. Two lines max. Never a paragraph, never a multi-line block, never a docstring with sections.

## When a comment is NOT warranted (delete or skip)

- Restating WHAT the code does ("// increment counter", "// loop through users") — well-named identifiers already do that.
- Narrating the change or its origin ("// added to fix bug #123", "// used by the X flow", "// previously called Y"). That belongs in the PR description and rots as the codebase evolves.
- Section headers ("// === Helpers ===", "// --- Validation ---") inside a normal function body.
- Restating type annotations or signatures.
- TODO/FIXME without a tracked issue or owner — either fix it now or file it; don't leave drift markers.
- Commented-out code. Delete it; git remembers.

## How to apply

1. Before writing a comment, ask: *would removing this comment confuse a future reader who doesn't know about the current task?* If no, don't write it.
2. When editing existing code, treat redundant comments around your change as fair game to remove (don't go on a comment-stripping crusade in unrelated files).
3. If the user explicitly asks for verbose comments, follow the user — they override this rule.

## XML docs

**No documentation file is generated.** `GenerateDocumentationFile` is set nowhere and nothing is
packed, so a `///` block is read by whoever opens the file and by nobody else. It buys nothing a `//`
does not, and the rule against docstrings with sections applies to it in full. Where a signature
genuinely hides something — what null means, the failure mode, who owns the lifetime — one line
`/// <summary>` above the member says it. The bare `<inheritdoc />` in the migrations is scaffolded.

## Conventions have one canonical home

A convention is explained once — in the skill for its area, or beside the code that enforces it —
and elsewhere it is a pointer or nothing. Point at a skill or a `docs/` page, never paraphrase it, or
the two drift and both have to be edited together.

Comments are English.

## How it loads here

`.claude/hooks/session-start.sh` names this skill at session start and
`.claude/hooks/comment-rule-reminder.sh` repeats it before every code edit. The `code-reviewer` agent
reviews a finished diff against it.

This file is the repository's own copy of the rule. A plugin or marketplace skill of the same name
(`mcpmarket-me:comment-rule`) is not this repository's — do not load it.
