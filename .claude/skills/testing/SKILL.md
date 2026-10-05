---
name: testing
description: Writing or changing an xUnit test in tests/RoleplayStudio.Tests. Covers sentence-style names, a real Postgres (Testcontainers) instead of the in-memory provider, fake AI clients with canned output, FakeTimeProvider, tests that read the repository, and what not to test. Use when adding a test or deciding where one goes.
---

# Testing

`dotnet test` from the repo root.

## Conventions

- **Test names are sentences.**
  `A_memory_from_another_session_is_never_retrieved` says what the rule is, so a failure names the
  rule that broke.
- **A real Postgres, not the in-memory provider.** Integration tests run against Testcontainers'
  `pgvector/pgvector` image. The services lean on foreign keys, unique indexes, cascades, `jsonb` and
  vector distance — the in-memory provider and SQLite enforce or support none of it, so a test passing
  there can still fail against the database the app ships with. These tests need Docker; when it is
  not running, say which tests did not run.
- **AI is faked at the abstraction.** A fake `IChatClient` returning canned text or JSON, a fake
  `IEmbeddingGenerator` returning fixed vectors, a fake `IImageGenerator` returning a tiny PNG. No
  test calls a real provider, costs money, or depends on a model's wording.
- **Time is injected.** Services take `TimeProvider`; tests drive `FakeTimeProvider` and advance it
  rather than sleep.
- **Arrange with shared builders.** A chat session is a deep graph (owner → chatbot → cast → scenario
  → persona → session → messages); building one inline buries the single fact the test is about.
  Repetition inside `Arrange` is still fine — tests are meant to read standalone.
- **Comment the why, not the what.** A test pinning a subtle rule says what would break without it.

## Where a test goes

- Pure logic — prompt assembly and its token budgets, speaker-tag parsing, retrieval re-ranking,
  anything on an entity — needs no database. Most of the AI pipeline should be testable this way.
- Anything touching the database is an integration test on the Postgres fixture.
- **Ownership is tested with two users.** Seed two owners and assert every read returns only the
  caller's rows; a new read belongs there.
- EF model shape (column types, JSON mapping, check constraints) is asserted from the model without a
  database — `Data/ApplicationDbContextModelTests.cs`. Check constraints exist only in the
  design-time model (`db.GetService<IDesignTimeModel>().Model`); the runtime model drops them.

## Tests that read the repository

A test that judges the source, the EF model or the instructions rather than running behaviour
asserts an absence, so it is only as good as its scan. It comes in parts: the rule; **a presence twin**
showing the scan found what it judges (a count floor, or a known case it must see), because a scan
that matches nothing passes every rule; and, where it has an exemption list, **a stale-exemption
test**, each entry carrying its reason. Plant a violation before trusting a new one.

## Not here

- **No bUnit / component tests.** A Razor component is covered by the browser (`verify-ui`, and
  `tests/ui` once it exists), not rendered in isolation.
- No test asserting a model's prose. Assert the structure the code relies on (parsed JSON, speaker
  segments, a memory row written), with canned input.

Coverage is a floor, not a target. 100% of a change whose only test asserts it doesn't throw is worse
than 85% with the rule pinned. Never quote a coverage number you did not measure.
