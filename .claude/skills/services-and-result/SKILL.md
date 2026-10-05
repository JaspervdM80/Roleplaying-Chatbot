---
name: services-and-result
description: Writing or changing an application service — the Result type, ServiceOperation.RunAsync/RunOwnerAsync, owner scoping at the service boundary, cancellation, logging levels, registration, and how a page consumes a Result. Use when adding a service method, handling a failure message, or wiring a call site that reads Result.Value.
---

# Services and Result

This is the shape services take from the first one onward. Every service method returns `Result` or
`Result<T>`. Services **never** throw to their caller and **never** write their own try/catch.

## The shape

Wrap the body in `ServiceOperation.RunOwnerAsync` whenever it touches owned rows, read or write; it
hands the lambda the signed-in user's id and refuses a signed-out caller. `RunAsync` is the same
wrapper without a user, for work that reaches owned rows only through another service (the AI
services call `ModelProfileService`). The signed-in check is a property of the shape, not something
each method remembers. `Result`, `ServiceOperation` and `ICurrentUser` live in
`Infrastructure/Services/`.

```csharp
public Task<Result<Character>> CreateAsync(Character character) =>
    ServiceOperation.RunOwnerAsync(currentUser, logger, "create the character", CancellationToken.None, async ownerId =>
    {
        await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
        db.Characters.Add(character);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Created character {CharacterName} (ID: {CharacterId})", character.Name, character.Id);
        return Result.Success(character);
    });
```

Expected misses return `Result.Failure(...)` explicitly from inside the lambda, after a `LogWarning`.
Only unexpected exceptions fall through to the wrapper, which logs the error once. The one sanctioned
catch is `ProviderErrors.TranslateAsync` in the AI project: a wrong key, model or address is an
expected miss with a readable message, not an error.

## Failure messages are templates

```csharp
Result.Failure("Chatbot {0} still has {1} chats", name, count)   // yes
Result.Failure($"Chatbot {name} still has {count} chats")        // no — cannot be translated or grouped in logs
```

## Ownership is enforced at the service, not in the markup

Every row a user creates belongs to them (`OwnedEntity.OwnerId`). `OwnerId` comes from `ICurrentUser`
— **never from a parameter or a form field**: `SaveChanges` stamps it from the context's owner scope,
and every read sees only the caller's rows through the owner query filter (the `ef-core-and-queries`
skill). An update copies the editable fields onto the row it loaded, never attaches the input. Hiding a button with
`<AuthorizeView>` is enforcement in the render tree only.

Child rows (`Scenario`, `Message`, `CharacterState`, `MemoryEntry`) have no `OwnerId`; a write that
reaches one by its own id first confirms its parent session or chatbot is the caller's, and treats a
foreign id as "not found".

## Cancellation is the third outcome

Every public method takes a trailing `CancellationToken cancellationToken = default` and hands it to
every EF and AI call underneath — not just the outermost. `RunAsync` catches
`OperationCanceledException` `when (cancellationToken.IsCancellationRequested)` *ahead of* the general
handler and returns `Result.Cancelled()`: nothing above Debug, no stack trace, no error on a page the
user already left. An `OperationCanceledException` with the caller's token untouched is a bug (often an
`HttpClient` timeout) and still logs.

`Result.Cancelled()` is still `IsFailure`; what sets it apart is `IsCancelled`.

**Reads get a token, writes usually do not.** A message the user sent must still be saved if they
navigate away. A streamed reply is the exception: stopping generation is a real user action, and the
partial reply is saved as it stands.

## At the call site

- Never read `Result<T>.Value` without an `IsSuccess` check. Reading a failed value throws by design.
- **Check `IsCancelled` before a redirect.** A cancelled load that redirects throws the user off the
  page they just navigated to.
- `Result.To<T>()` must carry the cancellation flag; dropping it delivers a messageless failure.
- Report through one snackbar/notice extension, never a hand-rolled if/else per page.

## Logging levels

`LogDebug` for a read, `LogInformation` for a mutation including the entity id, `LogWarning` for an
expected miss. `LogError` belongs to `ServiceOperation` — do not raise one yourself. Always structured
placeholders (`{CharacterId}`), never interpolation. **Never log prompt text, message content, memory
text or API keys** above Debug — it is the user's private roleplay.

## No interfaces for services

Services are injected as concrete types. Do not add `ICharacterService` unless a second
implementation exists. The deliberate seams are where something genuinely varies or is supplied by
the host: `ICurrentUser`, `TimeProvider`, `IChatClient`/`IEmbeddingGenerator`, `IImageGenerator`,
`IImageStore`.

Anything shared across circuits (a notifier, a cache, a background queue) is a singleton and must not
take a scoped service.

## When a service gets long, split by use case, not into layers

Cut along what happens (authoring characters, running a chat turn, extracting memories, generating an
image), never into a data-access layer under the domain. Pure helpers over an entity move **onto the
entity**; shared query shapes get **named once** (`ChatQueries`); anything every method has to
remember becomes **part of the operation shape**. A page injecting several services is expected; a
*facade* over them is the signal the split was cut along the wrong line.
