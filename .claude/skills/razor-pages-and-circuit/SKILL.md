---
name: razor-pages-and-circuit
description: Building or changing a Razor page, dialog or component in RoleplayStudio.Web. Covers the render-mode setup, code-behind and the CS0263 @inherits rule, Razor parser traps, cancelling reads when the user leaves, streaming into a component, and the circuit-lifecycle leaks. Use for any .razor or .razor.cs work.
---

# Razor pages and the circuit

## The render mode

The app is a Blazor Web App with **global Interactive Server**: `Routes` and `HeadOutlet` get
`InteractiveServer` from `App.razor`, except the Identity pages under `Components/Account`, which are
static server-rendered (`[ExcludeFromInteractiveRouting]`) because they set cookies.

Consequences:

- `MainLayout` and the MudBlazor providers in it are interactive on app pages and **static on the
  Account pages**. On a static page `ISnackbar` and dialogs report into nothing and
  `OnAfterRenderAsync`/`IJSRuntime` never run — keep Account pages to plain forms.
- **A page renders twice: a prerender, then the circuit.** The prerender is complete-looking and wired
  to nothing. `OnInitializedAsync` runs in both, so a page that calls an AI provider or writes on
  init does it twice. Load data on init; start generation from a user action or from
  `OnAfterRenderAsync(firstRender)`.

## File shape

Pages use `.razor` + `.razor.cs` code-behind partial classes once they have real logic.

**A page's base class goes in the `.razor` as `@inherits CancellableComponent`, never on the partial
class.** `: CancellableComponent` on the `public partial class` gives *CS0263: Partial declarations
must not specify different base classes*, because the generated Razor partial already declares one.

**`section` is a reserved word in a `.razor` file.** `@foreach (var section in …)` then
`@section.Title` parses as the `@section` directive and fails with `RZ2005`. Rename it or write
`@(section.Title)`.

**A `RenderFragment` in code-behind** needs the `=> __builder =>` lambda pattern in an `@code` block.

## Navigation

- Build URLs from one routes class (`AppRoutes.Chat(id)`), never an interpolated literal scattered
  across pages. The `@page` directives are the one exception — Razor needs a compile-time constant.
- Redirect away from a page that failed to load with `NavigateTo(..., replace: true)`, so the back
  button does not walk straight back into it.

## Reads stop when the user leaves

A component has no request lifetime, so a page navigated away from leaves its query — or its
generation — running with nobody to render it. A `CancellableComponent` base owns a
`CancellationTokenSource` cancelled on disposal and exposes `Cancellation`.

- The token goes on **reads and on generation**. A user's own message is saved with `default`.
- **Check `IsCancelled` before anything the user would notice**, a redirect above all.
- **Overriding `Dispose` means calling `base.Dispose()`**.

## Streaming into a component

A chat reply arrives token by token from `IChatClient.GetStreamingResponseAsync`.

- Append to a buffer and call `StateHasChanged` on a **throttle** (every ~50ms or per segment), not
  per token — every call ships a render diff over SignalR.
- A "Stop" button cancels its own `CancellationTokenSource` linked to `Cancellation`; the partial
  reply is saved as it stands.
- Disable the send box while a reply streams, or two turns interleave in one session.

## Circuit-lifecycle leaks are cross-user, not per-page

A circuit outlives a request and a singleton outlives the circuit.

- **Every `+=` needs its `-=` in `Dispose`**, and the component must actually implement
  `IDisposable`. A singleton notifier (memories extracted, image ready) with a handler never removed
  keeps a dead circuit's component alive and re-entered for every future event.
- **A callback arriving from outside the circuit** — a background job, a notifier, a timer — must
  re-enter through `InvokeAsync` before touching component state or calling `StateHasChanged`.
- **A page that shows live-updating rows reads with `AsNoTracking`.** A context kept for the
  circuit's life keeps returning its first load of a tracked row while new rows appear beside it.

## Dialogs

Dialogs must not close on backdrop click. A generic dialog result cannot tell `default` from
"cancelled", so a generic prompt helper is constrained to `class`; a value-typed dialog hands back
`T?`.
