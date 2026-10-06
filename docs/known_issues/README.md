# Known issues

Traps that already cost someone time, one entry each: the symptom, the cause, and the rule that keeps
it from coming back. Not a changelog. Add an entry in the same change that fixes a non-obvious bug.

## EF Core check constraints are missing from `db.Model`

**Symptom:** a test asserting `GetCheckConstraints()` on `db.Model` finds none, although the migration
creates the constraint.
**Cause:** the runtime model is read-optimised and drops design-time-only metadata.
**Rule:** read check constraints from `db.GetService<IDesignTimeModel>().Model`.

## The app refuses to start with "pending model changes" while `dotnet ef` says there are none

**Symptom:** in Development the web app throws `PendingModelChangesWarning` on startup, yet
`dotnet ef migrations has-pending-model-changes` reports nothing and a new migration is empty.
**Cause:** ASP.NET Core Identity picks its tables from `IdentityOptions.Stores.SchemaVersion` in the
app's services, and `dotnet ef` always prefers `DesignTimeDbContextFactory`, which had no such
services — so the tools built a schema without `AspNetUserPasskeys` and the app expected one.
Overriding the context's `SchemaVersion` does not help. `--startup-project` does not either: the
design-time factory still wins.
**Rule:** `DesignTimeDbContextFactory.CreateOptions` supplies the Identity options, and `Program.cs`
and the test fixture use the same `IdentitySchemaVersion`.

## A Blazor Server app started through Aspire cannot be opened in the desktop Browser pane

**Symptom:** navigating the Browser pane to the web endpoint fails with "denied or failed".
**Cause:** the pane does not run where the shell runs, so it cannot reach a localhost server the agent
started.
**Rule:** drive the running app with a headless Playwright script from the scratchpad instead.

## Escape does nothing in an editor opened from a row's menu

**Symptom:** an `EditorSheet` opened from a `MudMenuItem` ignores Escape, even with no edits; opened
from a plain button it closes.
**Cause:** the menu item awaits its `OnClick` before the menu closes, and the handler awaits the dialog,
so the menu's popover stays `mud-popover-open` behind the editor. The sheet skips Escape while a
popover is open, so a dropdown inside the editor gets it first.
**Rule:** `editor-sheet.js` counts only popovers opened after the editor. Any other Escape or
outside-click logic that looks for an open popover has to do the same.
