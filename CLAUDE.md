# CLAUDE.md

Guidance for AI assistants working in this repository.

## What this is

Roleplay Studio: a Blazor Server app where a user builds **characters** and **personas**, groups
characters into **chatbots** (a world plus a cast), and starts chats from **scenarios**. A chat has
short-term memory (recent messages and a rolling summary), long-term vector memory, per-session
character state (what everyone is wearing, where the scene is), and images generated from that state.
Models are pluggable: OpenAI-compatible cloud endpoints (OpenRouter, Featherless) and local Ollama, plus
an image provider. It is a private app for a small group of friends: invite-only sign-up, and every row
belongs to one user.

The roadmap and milestones are in the plan the project started from; ask for it if you need it.

## Commands

```bash
dotnet build -c Release                          # warnings are errors in Release
dotnet test
dotnet run --project src/RoleplayStudio.AppHost  # Aspire: Postgres+pgvector in Docker, the web app, the dashboard
dotnet ef migrations add <Name> --project src/RoleplayStudio.Infrastructure --output-dir Data/Migrations
dotnet user-secrets set "Registration:InviteCode" "<code>" --project src/RoleplayStudio.Web
dotnet user-secrets set "DevelopmentUser:Email" "<email>" --project src/RoleplayStudio.Web     # created on startup in Development
dotnet user-secrets set "DevelopmentUser:Password" "<password>" --project src/RoleplayStudio.Web
```

Docker Desktop must be running for the AppHost and for integration tests.

## Layout

```
src/RoleplayStudio.AppHost/          Aspire orchestration (Postgres with pgvector, the web app)
src/RoleplayStudio.ServiceDefaults/  Aspire defaults: telemetry, health checks, resilience
src/RoleplayStudio.Domain/           Entities and value objects; no EF, ASP.NET or AI references
src/RoleplayStudio.Infrastructure/   ApplicationDbContext, migrations, Identity user, storage
src/RoleplayStudio.AI/               Provider factory, prompt building, director, memory, images
src/RoleplayStudio.Web/              Blazor Web App: pages, Identity UI, DI wiring
tests/RoleplayStudio.Tests/          xUnit
docs/known_issues/                   Traps that already cost time
docs/design/                         Static HTML mockups of the screens; open index.html
```

Dependencies point one way: `Web → AI → Infrastructure → Domain`. Keep domain and prompt logic out of
the Razor project. The solution file is `RoleplayStudio.slnx`.

## Where the rules live

`.claude/skills/` holds the working rules, one skill per area: services and `Result`, EF Core and
queries, migrations, the domain model, the AI pipeline and background work, Razor pages and the
circuit, styling, touch and breakpoints, verifying a UI change, browser tests, testing, localization,
build and release, and `pre-pr` for getting a finished change ready. **Load the skill for the area you
are touching before changing it.** `.claude/hooks/skill-gate.sh` refuses the first edit in a mapped area
until its skill has been loaded that session, and lets a retry through.

**`comment-rule` applies to every change**: default to no comments, one only for a non-obvious *why*,
never a paragraph. The one in `.claude/skills/` is the rule here; a plugin or marketplace skill of the
same name is not this repository's.

**Skills state rules, not inventories.** No counts and no hand-kept lists the code can grow; give the
`grep` that produces the list instead.

`docs/known_issues/` is a list of traps that already cost someone time, not a changelog. Add to it when
you find a new one, and update the skill for an area in the same change that alters its behaviour.

## The ones that must not wait for a skill to load

1. **Every row belongs to one user, and the service enforces it.** `OwnerId` comes from `ICurrentUser`,
   never from input; reads go through the owner query filter; a child row reached by its own id is
   checked against its parent. Hiding a control is not enforcement.
2. **Services use `IDbContextFactory`, one short-lived context per operation.** A Blazor circuit
   outlives a request; a shared context throws the moment the chat streams while another panel loads.
3. **Private text stays private.** Message content, memories, prompts and API keys never go in a log
   line above Debug, an exception message, or the database (keys live in user secrets; a
   `ModelProfile` stores only the setting name).
4. **A chat turn never waits on background work.** Memory extraction, summarization and image
   generation are queued after the reply is saved and must not fail the turn.
5. **Build Release before pushing.** Warnings are errors only there; the push hook runs it.

## Workflow

- Branch names: **`feature/…`**, **`bug/…`** or **`ci/…`**. Start from the latest `main` once there is
  a remote.
- Commit messages are plain imperative sentences describing intent, not conventional-commit prefixes.
- `.claude/settings.json` denies pushing to `main`, force-pushing, `gh pr merge` and bare `git stash`;
  those stay a person's call.
- `.editorconfig` codifies the style (CRLF, 4 spaces, file-scoped namespaces, `_camelCase` private
  fields, braces on their own line). Don't let a formatter reformat files you didn't change.
- Before opening a pull request, work through the **`pre-pr`** skill — it ends with the
  **`code-reviewer`** agent over the change.
