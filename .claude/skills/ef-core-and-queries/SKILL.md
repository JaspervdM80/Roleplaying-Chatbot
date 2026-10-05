---
name: ef-core-and-queries
description: Writing or changing an EF Core query against ApplicationDbContext — context-per-operation in Blazor Server, the owner query filter and the child-row gate, JSON-mapped owned types, pgvector similarity queries, named include chains, and the graph-update and list-comparer traps. Use whenever a LINQ query, an Include, an ordering or a SaveChanges is involved.
---

# EF Core and queries

Postgres via Npgsql, with pgvector. The context is `Infrastructure/Data/ApplicationDbContext.cs`.

## Each operation opens its own short-lived context

Services take `IDbContextFactory<ApplicationDbContext>`, never an injected `ApplicationDbContext`. A
Blazor Server circuit outlives a request, so a scoped context is shared by every component on the page
and two concurrent queries throw *"A second operation was started on this context."* The chat page
streams a reply while the memory panel loads — that is exactly two at once.

```csharp
await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
```

The scoped context registered in `Program.cs` exists for ASP.NET Core Identity's stores. Nothing else
injects it.

## Every read is scoped to its owner, and you do not write the `Where`

The pattern, as the first service lands: an owner-scoped factory stamps the current user id onto each
context, and `ApplicationDbContext` carries a `HasQueryFilter` on every `OwnedEntity`. A query that
never mentions the owner still returns only the caller's rows; an unstamped context returns *nothing*,
never another user's rows.

- **Child rows carry no filter.** `Scenario`, `Message`, `CharacterState` and `MemoryEntry` are reached
  through a filtered parent. A write that loads one by its own id gates on its parent being in scope
  first, which turns another user's id into "not found".
- **`IgnoreQueryFilters()` is a decision to argue for.** Background jobs run with no user in scope:
  they take the session id they were queued with, read its `OwnerId`, and stamp that — never a raw,
  unfiltered read of everything.
- A test seeding two users and asserting each read returns only the caller's rows belongs beside any
  new read.

## Owned types are JSON columns

`Appearance`, `Outfit`, `SceneState` and `SessionSummary` are `OwnsOne(...).ToJson()` → `jsonb`.

- Replace the owned object or mutate it on a **tracked** entity; on an untracked one EF does not see
  the change.
- Querying into JSON works but cannot use a normal index. If a field becomes a frequent filter, give it
  a real column.

## Vector search

`MemoryEntry.Embedding` is `vector(768)` with an HNSW `vector_cosine_ops` index. Query with
Pgvector's `CosineDistance` so the index is used, filter by `SessionId` in the same query, and take a
bounded top-k:

```csharp
var hits = await db.Memories
    .Where(m => m.SessionId == sessionId && m.Embedding != null)
    .OrderBy(m => m.Embedding!.CosineDistance(queryVector))
    .Take(k)
    .AsNoTracking()
    .ToListAsync(cancellationToken);
```

Re-rank by importance and recency **in memory** after the vector query. Changing the embedding model
changes the dimension: that is a migration plus a re-embed of every memory, never a silent mix of two
models' vectors in one column.

## When two rows have to agree, one context writes both

One `SaveChangesAsync` is a transaction. A service method calling another service's write is two
transactions with a gap. A chat turn that saves the reply and advances the session's
`LastActivityAt` does both in one context.

**Recount, never increment.** A derived value (a message count, an affinity score rebuilt from
events) is recomputed from the rows on file, not nudged by one — two concurrent writers would each
write *n+1*.

## Include chains are named, not respelled

Shapes an aggregate is loaded in live as composable `IQueryable` extensions in one `*Queries.cs`
file (`WithCast()`, `WithScenarioAndPersona()`), not repeated `.Include` chains in every service. They
stay `IQueryable` — not a repository — so tracking, filtering and tagging remain the caller's
decision. EF rejects a filtered and an unfiltered include of the same navigation in one query.

## Traps

- **`DbSet.Update` on an entity loaded with its graph** walks the whole graph and marks every row
  `Modified`. Load, change the properties you mean, save.
- **List value converters need a `ValueComparer`.** Without one EF never detects a change to a
  converted list. (Npgsql maps `List<Guid>` to `uuid[]` natively, which needs none.)
- **`ExecuteUpdate`/`ExecuteDelete` skip `SaveChanges`** and every interceptor on it.
- `AsNoTracking` on reads; the `CancellationToken` threaded to *every* EF call underneath.
- Enums are stored as strings (`ConfigureConventions`), so renaming a member is a data migration.
