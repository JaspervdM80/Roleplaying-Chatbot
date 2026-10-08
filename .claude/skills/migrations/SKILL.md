---
name: migrations
description: Adding or reviewing an EF Core migration against Postgres. The app applies migrations on startup in Development, so a bad Up() is a bad database. Use whenever a migration is scaffolded, a column is dropped or renamed, the embedding dimension changes, or a backfill is written.
---

# Migrations

```bash
dotnet ef migrations add <Name> --project src/RoleplayStudio.Infrastructure --output-dir Data/Migrations
dotnet ef migrations remove     --project src/RoleplayStudio.Infrastructure
```

`DesignTimeDbContextFactory` means no `--startup-project`; migrations are created from
`Infrastructure` alone. It never connects to a database to scaffold. If `dotnet ef` is not on PATH,
call `~/.dotnet/tools/dotnet-ef` (`.exe` on Windows) and pass `--project` an absolute path.

**The web app migrates itself on startup in Development** (`Program.cs`). Any environment that
migrates on boot turns a merge into a schema change, so treat every `Up()` as something that will run
unattended.

## Read the generated `Up()` and reorder it

The scaffolder does not know what a backfill needs to read. It will happily emit a `DropColumn` before
the `AddColumn` + copy that should have read it first. Order operations:
**`AddColumn` (with `defaultValue`) → backfill SQL → `CreateIndex`/`AddForeignKey` → drops.** Reads
happen before drops.

A **rename** scaffolds as drop + add unless you rename by hand — that is data loss. Check every
`DropColumn` in a diff is meant.

## Backfills belong in the migration, not in startup code

`migrationBuilder.Sql(...)` in `Up()`. The migrations history runs it exactly once, and it is the only
place a new required column can be populated *before* its constraint is added.

## Postgres specifics

- Postgres DDL is transactional, so a failed migration rolls back — but `CREATE INDEX CONCURRENTLY`
  cannot run in a transaction. A large HNSW index build on a big table locks writes; build it in its
  own migration with `migrationBuilder.Sql(..., suppressTransaction: true)` when that matters.
- The `vector` extension is declared by `HasPostgresExtension("vector")`; the container image is
  `pgvector/pgvector`. A plain `postgres` image fails the first migration.
- **Changing `MemoryEntry.EmbeddingDimensions` is not a column alter.** Existing vectors cannot be
  cast to another dimension: null them (or drop and re-add the column), rebuild the index, and
  re-embed in a background job. Say so in the pull request.
- JSON-mapped owned types: adding a property to `SceneState`/`SessionSummary` needs no migration, but
  existing rows read the new property as its default.

## Check before you trust it

```bash
dotnet ef migrations script --idempotent --project src/RoleplayStudio.Infrastructure
```

Read the SQL for anything destructive. Rehearse a destructive change against a copy of a real
database before it reaches one that matters; the data volume is the only copy.
