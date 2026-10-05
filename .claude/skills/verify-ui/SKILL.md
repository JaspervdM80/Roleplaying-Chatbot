---
name: verify-ui
description: Verify a UI change across the matrix — every affected page at phone and desktop width, signed in and signed out, in the running app. Use after any change to a .razor file, app.css or a scoped .razor.css, before reporting the work as done.
---

# Verifying a UI change

Every page renders **four** ways: two widths × two auth states. A change verified in one cell
routinely breaks another. Check all four, or say which you skipped and why.

|  | Signed out | Signed in |
|---|---|---|
| **Desktop** (1280×800) | redirected to login, or the public page | full page |
| **Phone** (375×812) | same, at phone width | drawer nav, stacked layout, sheet dialogs |

Ownership adds a fifth case worth one check whenever a page loads by id: **another user's id** must
read as not found, not as their data.

## Running it

Start the app through Aspire (Docker must be running) with the `apphost` entry in
`.claude/launch.json`, then open the web endpoint from the dashboard. Use the Browser pane: resize to
the phone preset and back, and read the page rather than eyeballing screenshots.

**Never type a real password into the login form.** Use a test account created for this database, and
record its credentials in user secrets or a seed file, not in chat. A Development-only, loopback-only
`/dev/login` endpoint is the better long-term answer once there is more than one test account.

## What to check in each cell

1. Console clean and no horizontal overflow (`document.documentElement.scrollWidth <= innerWidth`).
2. The expected content is present — read the page.
3. Anything restyled: read the **computed** value, don't eyeball it.
4. A class added to markup actually matches a rule. Scoped CSS fails **silently** (the
   `styling-and-css` skill).
5. Interactive pages: the control works after the circuit connects, not only in the prerender.

```js
getComputedStyle(document.querySelector('.message-actions')).display
[...document.querySelectorAll('.row > button')].map(b => b.getBoundingClientRect().height)
```

Check the pages a change can reach. A change to `app.css` or `MainLayout` reaches all of them.

## Reporting

State which cells you checked and what you measured. If you skipped one, say so — "verified on
desktop, not on phone" is useful; silence reads as "all fine".
