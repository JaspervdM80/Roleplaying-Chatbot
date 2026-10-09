---
name: styling-and-css
description: Writing CSS, picking a colour, or fighting a MudBlazor 9.x style. Covers the silent scoped-CSS failure, app.css vs .razor.css, theme tokens, and the global .mud-* rules that broke layout before. Use whenever a .razor.css or app.css rule is added or changed.
---

# Styling and CSS

UI components are MudBlazor 9.x. The Identity pages still use the template's Bootstrap.

## Scoped CSS has a silent failure mode

A class in `Foo.razor.css` compiles to `.cls[b-<fooHash>]` and **will not match identical markup on
another page** — no warning, it just renders unstyled.

**The same trap catches a rule that never leaves its own page: a child component's root element has
no scope attribute.** `.row > *` in a page's `.razor.css` matches nothing when every child is a
`MudButton`, because the `<button>` MudBlazor renders carries no `b-<hash>`. The tell is that the
*container* is styled and the *children* are not. Use `::deep` from a scoped wrapper, or `app.css`.

**Anything used by more than one page, or selecting past a MudBlazor component's root element, goes
in `wwwroot/app.css`.**

**It cuts the other way too: a class in `app.css` is global.** Check a page's `.razor.css` before
reusing a name, or a global rule (say a `display`) quietly reshapes that page's block.

## Colours come from tokens

One palette, defined once, feeds both the MudBlazor theme and CSS custom properties. Pages use the
custom properties (`var(--...)`) or MudBlazor's palette, never a hex literal or an ad-hoc
`color-mix` percentage. Muted text uses a named ink ramp, not per-page opacity. If the page colour
changes, the `theme-color` meta changes with it.

The palette is `src/RoleplayStudio.Web/Theming/AppTheme.cs`: add a token there, never in a stylesheet.
The Identity pages are Bootstrap in `data-bs-theme="dark"`; they take the tokens through the `--bs-*`
mappings at the top of `app.css`, so restyle Bootstrap through its variables, not its properties.
Keep `font-size` off `html` — the root `rem` stays 16px so `1.0625rem` means 17px.

## MudBlazor 9.x rules that have already cost time

- **Never let a global `.mud-*` rule touch layout.** A card rule setting `position: relative` on
  `.mud-paper` also hit `MudPopover` (same specificity, later in source), and every dropdown rendered
  as a full-width band across the top of the page. Scope it:
  `.mud-paper:not(.mud-popover):not(.mud-dialog)`. **Excluding the popover beats overriding it back.**
- **`MudMenu`'s `Class` lands on the root wrapper, not the activator button.** Style
  `.<your-class>.mud-menu .mud-button-root` instead. That wrapper also carries `align-self: center`,
  so in a column flex container the menu sits centred until you set `align-self` on it.
- **The popover, dialog and snackbar providers must render inside an interactive render mode.** They
  live in `MainLayout`, which is interactive on app pages only (see `razor-pages-and-circuit`).
- `MudForm.Validate()` is obsolete — use `ValidateAsync()`.
- Multi-select binding takes `IReadOnlyCollection<T>`, not `IEnumerable<T>`.
- `ShowMessageBox` is gone — use a custom confirm dialog. Dialogs must not close on backdrop click.
- A domain type named like a MudBlazor type (`Position`, `Color`, `Size`) collides in `.razor` files
  that `@using MudBlazor`; name the domain type something else.

## Measuring geometry from the DOM

- **An open dialog shrinks `<body>`** (MudBlazor's scroll lock). An ancestor's overflow only clips a
  descendant it is a containing block for, and the dialog container is `position: fixed` — do not
  report a dialog as clipped from that.
- **Animated MudBlazor parts (pickers, dialogs, popovers) report mid-transition values.** Wait for the
  value to change or the box to be stable across two frames before reading it.
