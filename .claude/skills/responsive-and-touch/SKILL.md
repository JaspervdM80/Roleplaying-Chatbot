---
name: responsive-and-touch
description: Anything a thumb touches or that changes at a breakpoint — tap-target sizing, the 44px/8px floors, phone dialogs as full-screen sheets, the chat input on a phone keyboard, and why a width-only media query misses a landscape phone. Use when adding an interactive control or writing a media query.
---

# Responsive and touch

Chatting happens on a phone as much as at a desk. Every rule here was a real bug in a previous
Blazor + MudBlazor app.

## Breakpoints

Use MudBlazor's own boundaries, written as **`599.98px`** (`xs`) and **`959.98px`** (`md`) — never
`599`/`600`. `600` fires *at* the boundary MudBlazor is switching on; `599` leaves a fractional gap
reachable by browser zoom where half the page has restacked and half has not. A `min-width`
complement is the next hundredth up.

## A width-only media query does not cover a phone in landscape

Turned sideways a phone is **short rather than narrow** (844×390) but it is still a thumb. Anything
about *touch* rather than *layout* keys off:

```css
@media (max-width: 599.98px), (max-height: 559.98px)
```

Layout (a full-screen sheet, a stacked column) stays width-only.

## Two floors, both measured

- **Size** — every hit-testable element is at least **44×44** CSS px.
- **Clearance** — the gap to its nearest neighbour is either **zero** or at least **8px**. Anything
  between is a dead gutter: too narrow to aim around, wide enough to swallow a tap, and awarded by the
  browser to whichever neighbour has the larger contact area. `elementFromPoint` cannot see this.

Buttons need clear space above them too: a `MudSelect`'s hit box reaches ~10px past its underline, so
an action row tucked under the last field opens the dropdown instead of pressing the button.

## Touch states come in pairs

`pointer: coarse` blocks grow tap targets; `@media (hover: hover)` guards hover states so they do not
stick after a tap. Keep both halves. Give buttons, links and inputs `touch-action: manipulation` via a
zero-specificity `:where()` so a double tap is two taps, not a zoom.

## Dialogs on a phone

A long form dialog becomes a **full-screen sheet below 599.98px**: full
width, `overflow: hidden` on the dialog so one element is the scroller, a real footer with the bottom
safe-area inset, and the title taking the top inset. `EditorSheet` does this for a long authoring
form and below 959.98px trades its side nav for a row of section chips; a dialog not yet on it uses
`app-sheet-dialog`. On a phone held sideways the sheet takes the full height and drops the portrait
row, or the form opens showing little but the portrait. A MudBlazor dialog is 64px
narrower than the phone by default, which pushes wide content (a date picker) off-screen.

A numeric field's spin buttons are a third of the floor and flush together; hide them on a phone —
the field is a number typed behind a numeric keyboard.

## The chat screen on a phone

- The message input stays pinned above the on-screen keyboard: size the chat column with `100dvh`, not
  `100vh`, and respect `env(safe-area-inset-bottom)`.
- The message list is the one scroller; auto-scroll to the newest message only while the user is
  already at the bottom, or reading back up gets yanked down by every streamed token.
- Per-message actions (regenerate, picture this, edit) need the 44px floor too — an overflow menu
  beats a row of tiny icons.
