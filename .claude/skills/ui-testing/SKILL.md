---
name: ui-testing
description: Writing or debugging a Playwright browser test (tests/ui) or a browser-driving script (scripts/*.mjs). Covers the Blazor prerender trap and a circuit-readiness marker, no fixed sleeps, timeouts never as the expected path, tests that cannot fail, .count() failing open, isolation by naming, and the lockfile. Use before adding or changing any browser test.
---

# Browser tests

There is no `tests/ui` yet. These are the rules that a previous Blazor Server + MudBlazor suite paid
for; set the suite up this way from the start.

## The one thing to know first

**A Blazor Server page renders twice, and the first one is a lie.** The prerender is complete and
correct-looking, every button visible and enabled and none of them wired to anything. A click landing
in that window is swallowed with no error; a `fill()` writes into an input the server never hears
about.

Two obvious readiness signals are both wrong: `domcontentloaded` with `window.Blazor` present (zero
handlers attached), and the circuit's first WebSocket frame (that frame is the handshake).

**The signal is a hidden `data-circuit` marker** rendered by a small component in the layout:
`pending` in the prerender, `live` in the circuit's first render (`OnAfterRender(firstRender)`),
which arrives with every handler attached. One `goto()` helper waits until no marker reads `pending`,
then for web fonts; a `settle()` does the same after a form post or redirect. Both hold after a full
load only.

## No sleeps

**Not a single fixed sleep, anywhere.** It is how a suite starts failing on a slow machine. A wait is
a condition, or the tick of a `while (Date.now() < deadline)` poll.

- **A timeout must never be the expected path.** Acting, then waiting for a condition only the *last*
  action makes true costs a full timeout per test. The tell in CI is a test whose time is identical
  every run.
- **Don't build a state to read one thing off it.** Tests that read different things off the same
  state share it (`test.describe.serial`).
- **Streaming replies:** wait for the reply's "done" marker, never for a fixed time, and fake the
  model (a test-only `IChatClient` returning canned chunks) so a run is fast and deterministic.

## A test that cannot fail is worse than none

- **Its name has to be what it proves.** Set up the precondition the name claims, or delete it.
- **An absence needs a presence.** `toHaveCount(0)` on a class also passes once the class is renamed.
  Assert the element exists where it should before asserting it is gone.
- **Selectors are string literals**, so a scan can check every class still exists in `src`.
- **A second browser comes from a fixture** that carries the console-error check, never a hand-made
  `browser.newContext()`.
- **`.count()` is the one locator call that does not wait, and it fails open.**
  `if (await dialog.count()) …` reads zero before the dialog renders and silently skips the step. Use
  `toHaveCount(0)` for absence and a waiting locator for everything else.
- **Waiting for a consequence is not waiting for the navigation it causes.** A flow ending in a
  server-driven redirect waits on `page.waitForURL`.

## Isolation

The app runs against a throwaway database per run. Specs share it, so each stays out of the others'
way by **naming what it creates after itself**, never by counting rows. A CI retry does not get a
clean database. If CI shards by file, each spec creates what it reads.

**Commit `package-lock.json`**, run `npm ci` in CI, and key caches on the lockfile.
