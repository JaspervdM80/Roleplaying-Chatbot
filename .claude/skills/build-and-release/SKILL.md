---
name: build-and-release
description: Building, the Aspire AppHost, the SDK pin, warnings-as-errors in Release, package versions, branch and commit conventions, and the push gate. Use before pushing, when a build is red, or when touching global.json, Directory.Build.props, a workflow, a Dockerfile or the AppHost.
---

# Build and release

```bash
dotnet build -c Release                          # what a push is gated on — warnings are errors here
dotnet test
dotnet run --project src/RoleplayStudio.AppHost  # Aspire: Postgres (Docker) + the web app + dashboard
```

## Build Release before pushing

`Directory.Build.props` sets `TreatWarningsAsErrors` in **Release only** — a Debug build that looks
clean can still fail. `.claude/hooks/release-gate.sh` holds this for an agent: a `git push` is refused
until `dotnet build -c Release` passes on the working tree being pushed, untracked files included. It
remembers the tree it built, so pushing the same one again costs nothing.

If resx localization is added, `MSB3568` (duplicate resource name) must be promoted with
`MSBuildWarningsAsErrors` too — `TreatWarningsAsErrors` does not cover `MSB####` codes, and a
colliding key otherwise builds green and silently shows the wrong text (the `localization` skill).

## The SDK

`global.json` pins the SDK with `rollForward: latestFeature`. A Claude Code web container installs
`dotnet-sdk-10.0` from Ubuntu's archive (`session-start.sh`), which may be an older feature band than
the pin. If it cannot satisfy `global.json`, the hook says so; raise it with the user rather than
editing `global.json` for one build.

## Aspire

The AppHost owns local infrastructure: Postgres from the `pgvector/pgvector` image with a persistent
volume, and the web project with `WithReference`/`WaitFor`. Connection strings reach the web app by
resource name (`roleplaydb`). New infrastructure (Ollama, MinIO, a ComfyUI container) is added in
`AppHost.cs`, not as a hard-coded connection string. Docker Desktop must be running.

**Secrets never go in `appsettings.json` or the database.** API keys live in user secrets locally
(`dotnet user-secrets`) and in the host's secret store when deployed; a `ModelProfile` stores only the
*name* of the setting that holds its key.

## Conventions

- Branches start with `feature/`, `bug/` or `ci/` — a behaviour change, a fix, or the build, workflows
  and tooling. Start from the latest `main` once there is a remote.
- Commit messages are plain imperative sentences describing intent, not conventional-commit prefixes:
  *"Keep the memory panel open while a reply streams"*.
- `.claude/settings.json` denies pushing to `main`, force-pushing and `gh pr merge`; those stay a
  person's call.
- `.editorconfig` codifies the style (CRLF, 4 spaces, file-scoped namespaces, `_camelCase` private
  fields, braces on their own line). Don't let a formatter reformat files you didn't change.
- The solution file is `RoleplayStudio.slnx`.
- Package versions are in each csproj for now. If they move to `Directory.Packages.props` (central
  package management), a `Version=` in a csproj becomes a mistake.
