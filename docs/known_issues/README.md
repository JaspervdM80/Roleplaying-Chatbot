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

## A transaction that works in every test throws in the running app

**Symptom:** code that calls `BeginTransactionAsync` passes its integration tests, but in the app
throws `InvalidOperationException`: the execution strategy "does not support user-initiated
transactions". Memory recall swallowed it and sent replies with no memories at all.
**Cause:** `EnrichNpgsqlDbContext` in `Program.cs` turns on Npgsql's retry-on-failure strategy, and
the test fixture's options (from `DesignTimeDbContextFactory`) had none, so tests never met it.
**Rule:** open a transaction inside `db.Database.CreateExecutionStrategy().ExecuteAsync(...)`, and
keep `EnableRetryOnFailure()` in `DesignTimeDbContextFactory.CreateOptions` so tests run under the
same strategy as the app.

## A child row added to a loaded parent's collection is saved as an update

**Symptom:** `SaveChangesAsync` throws `DbUpdateConcurrencyException` ("expected to affect 1 row, but
actually affected 0") after a domain method added a `CharacterState` to a tracked session's
`CharacterStates`.
**Cause:** every `Entity` makes its own `Guid` id, and EF treats a key that is already set on an entity
it finds through a navigation as an existing row, so it issues an `UPDATE` for a row that is not there.
Adding a whole new graph (`db.ChatSessions.Add`) is fine; growing one that was loaded is not.
**Rule:** add a new child row to its `DbSet` explicitly (`db.CharacterStates.AddRange(...)`), as
`SceneUpkeep` does with the states a scene update creates.

## An editor reports unsaved changes the moment it opens

**Symptom:** the memory editor showed "Unsaved changes" with nothing touched, and Escape asked to
discard instead of closing.
**Cause:** `EntityEditor` decides dirty by comparing JSON snapshots of fresh `new T()` copies, and
`MemoryEntry.CreatedAt` defaults to `DateTimeOffset.UtcNow`, so every snapshot carried a new time. The
editor resets the timestamps of an `OwnedEntity` only.
**Rule:** an editor whose entity has a property initialised from the clock copies it in
`CopyEditableFields` (as `MemoryDialog` does), so the snapshot is stable.
