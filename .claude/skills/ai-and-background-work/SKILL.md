---
name: ai-and-background-work
description: Working in RoleplayStudio.AI — model providers behind IChatClient/IEmbeddingGenerator/IImageGenerator, prompt assembly and token budgets, the director turn and speaker parsing, memory extraction, summarization and retrieval, image prompts, and background jobs that run outside any request or circuit. Use for any provider, prompt, memory, image or background-queue change.
---

# AI and background work

## Providers sit behind abstractions

- Chat and utility models are `Microsoft.Extensions.AI.IChatClient`; embeddings are
  `IEmbeddingGenerator<string, Embedding<float>>`; images are our own `IImageGenerator`.
- A `ModelProfile` (provider, base URL, model id, `ApiKeySetting`) is turned into a client by one
  factory. OpenRouter, Featherless and OpenAI are all `OpenAICompatible` with a different base URL —
  not separate code paths.
- Nothing outside the factory knows which provider it talks to. A provider-specific quirk is handled
  in the factory or a small decorating client, not at the call site.
- **The API key is read from configuration by its setting name at call time.** It never enters the
  database, a log line, an exception message or the page. The setting must sit under `Providers:`
  (`ModelProfile.ApiKeySettingPrefix`) — otherwise a profile pointed at a URL of the user's choosing
  could send any configuration value (a connection string, the invite code) as its bearer token.
  And because every user's profiles share the configured keys, a key only travels to the scheme and
  host in the `BaseUrl` setting beside it (`Providers:OpenRouter:ApiKey` → `Providers:OpenRouter:BaseUrl`,
  in `appsettings.json`); otherwise any signed-in friend could point a profile at their own server.
- `IChatClientFactory` is the seam tests fake; `ChatClientFactory` builds the real clients.
- **Provider HTTP never goes through `IHttpClientFactory`.** The service defaults put the standard
  resilience handler on every factory client, with a 10-second attempt timeout that kills any
  generation. The factory shares one `SocketsHttpHandler` instead.
- Provider failures a user can fix (401, 404, unreachable, out of credit) are translated by
  `ProviderErrors.TranslateAsync` into readable failures; anything else still reaches
  `ServiceOperation`.

## Prompts are assembled by pure code

`PromptBuilder` takes loaded entities and returns the message list, with no I/O, so it is unit-tested
without a model. Each section (rules, world, persona, present characters + state, scenario, summary,
retrieved memories, recent messages, director instruction) has a **token budget**; when over budget
the oldest recent messages go first and the system sections never get truncated mid-sentence.

## Structured output is parsed defensively

Memory extraction, state deltas and image prompts ask the utility model for JSON. Models break JSON:

- Parse with a tolerant step (strip code fences, take the outermost object) and validate the shape.
- A parse failure is a logged warning and a skipped extraction, never an exception that fails the
  chat turn, and never a half-applied delta.
- Ids in model output (a character to update) are checked against the session's characters — a model
  can name someone who is not there.

Speaker tags in a director reply (`**Name:**`) are parsed against the present cast; an unknown name
becomes narration rather than a new character.

## Background work never blocks the user

Scene tracking, memory extraction and summarization run **after** the reply is saved, through a
bounded `Channel<T>` (`UpkeepQueue`) read by a `BackgroundService` (`UpkeepWorker`), which runs each
step in turn so one failing does not stop the next. Pictures have their own queue and worker
(`PictureQueue`, `PictureWorker`), so a slow drawing never holds a chat's upkeep back. Both follow
the same rules:

- **The producer only `TryWrite`s and returns.** A chat turn never waits on extraction, and a failing
  job never fails the turn.
- **A job has no user and no circuit.** It carries the owner id captured from `ICurrentUser` when
  it was queued, beside the session id, and opens a context scoped to that owner — so a mismatched
  pair finds no session, and no unfiltered read is needed. It works within that owner's rows (the
  `ef-core-and-queries` skill). It cannot take a scoped service — it creates its own scope or
  context per job.
- **It never throws out of the loop.** One bad job is logged and dropped; the reader keeps going.
- Completion is announced through a singleton notifier; pages subscribe and re-enter with
  `InvokeAsync` (the `razor-pages-and-circuit` skill).
- In-process only: one app instance. Scaling out needs a real queue.
- Jobs for one session are processed in order, or a summary can be written over messages a later
  extraction has not seen.

## Scene tracking

- **The scene follows the story.** `SceneUpkeep` asks the utility model, after each turn, for the
  place, time and mood, who is present, and what changed about anyone's clothes, looks or feelings.
  `SceneTracking` holds the prompt, the parse and `Apply`, all pure. It moves
  `SceneState.TrackedUpToSequence` like extraction moves its marker, and announces the change through
  `SceneNotifier`.
- **A newcomer becomes a real `Character`**, owned by the chat's owner and added to the chatbot's
  cast with the role the model gave, marked with `IntroducedInSessionId`. That chat's tracking may
  rename them and fill in their age, gender and lasting looks as the story reveals them; a character
  the user wrote, or one they have since edited (the edit clears the mark), is never changed. A new
  portrait is queued for a newcomer, and one that renews the reference when their looks were filled
  in (`PictureJob.RenewsReference`). A name already in the cast or met in the chat (exactly, or by a
  first name only one character has) is that character, never a copy. The persona, and a name no
  newcomer entry describes, are never made into characters.
- **Leaving is not forgetting.** Who is present is replaced wholesale; a `CharacterState` stays for
  anyone who leaves, so they come back in the clothes they left in. The chat prompt names them under
  "Met earlier".
- An update without a `present` list leaves who is present alone; a field the model left out keeps
  its value. An outfit comes back whole, as everything worn now, and replaces the last one.

## Memory

- **Short-term memory is the messages after `Summary.CoveredUpToSequence`.** Upkeep folds the oldest
  of them into the summary once they outgrow a threshold (`SessionSummarizing`), a bounded piece per
  model call, until what is left fits; the prompt never sends a message the summary covers.
- **Extraction moves `ChatSession.MemoriesExtractedUpToSequence`.** A provider failure leaves it, so
  the next turn retries; unreadable JSON moves it past those messages, so a model that cannot write
  the JSON does not pay for them every turn.
- **A reply is saved through `ChatTurnService.SaveReplyAsync`**, which queues the upkeep. A page that
  calls `ChatSessionService.AddReplyAsync` directly saves a reply no memory will ever see.
- **Recall never fails a turn.** Pinned memories come first; without an embedding model the most
  important memories stand in for the closest. A vector of the wrong dimension is dropped and the
  memory kept without one — never padded or cut — and a memory without a vector competes in
  recall on importance alone. Every memory recall brings back gets `LastRecalledAt` stamped.
- **The user corrects memory through `MemoryService`.** A memory added or re-worded there is embedded
  at once, so recall finds it by what it says now; it can only be about characters met in the chat.
  An edited summary keeps covering the same messages, so the next fold builds on the user's text.
- **Rebuilding the summary is an upkeep job** (`UpkeepJob.RebuildSummary`), not a reset done from the
  page: jobs for one chat run in order, so a fold already in flight cannot write the old summary back
  over the cleared one. It is refused without a utility model, since the job would do nothing, and
  when the queue is full, since no later turn's job rebuilds.
- **A user's summary edit wins over a fold.** The save is refused when the summary has moved past what
  the user was editing, and a fold re-reads the stored summary before writing and stops if it changed.
- `MemoryUpkeep` announces new memories and a changed summary through `MemoryNotifier`, as scene
  tracking does through `SceneNotifier`; the end of a rebuild is always announced, even one that
  changed nothing, so a page waiting on it stops waiting.

## Cost and latency

- Background jobs use the cheap utility profile, never the chat model by default.
- Embed once, at write time; never re-embed memories on read.
- Pass the `CancellationToken` into every provider call, so a user leaving stops a stream rather than
  paying for tokens nobody reads.
- Tests never call a real provider (the `testing` skill).

## Images

The image prompt is written by the utility model from the character's `Appearance`, the session's
`CharacterState.CurrentOutfit`, `SceneState` and a few related memories, then sent to the
`IImageGenerator`. Store the final prompt, seed,
provider and source memory ids on `GeneratedImage`, so an image can be explained and regenerated.
Files go through `IImageStore`, never a path built in a page.

- `IImageGenerator` is `RoleplayStudio.AI.Images`'s, not the one Microsoft.Extensions.AI ships under
  the same name; a file using both namespaces aliases ours.
- The factory's `CreateImageGenerator` builds it; only a provider `ModelProfile.CanDraw` accepts may
  be an `Image` profile. The Runware key follows the same host rule as every other key
  (`Providers:Runware:BaseUrl`).
- **The seed is chosen by us and sent when the model takes one**, never left to the provider, so
  `GeneratedImage.Seed` can regenerate the picture; a model that takes no seed stores none.
- `ImageService.SaveAsync` writes the file and the row together (the file is removed if the row fails)
  and refuses a session, message or character that is not the caller's. `/images/{id}` serves
  through `ImageService.OpenAsync`, so another user's id is a 404; the endpoint hands the request's
  user to the `AuthenticationStateProvider` first, since outside a component nothing else does.
- Testing an `Image` profile on the models page draws and stores a real picture.
- **Pages ask through `PictureService`; `PictureDrawing` does the work** in the background. A job
  carries its owner like an upkeep job and saves with `ImageService.StoreAsync` in a context scoped to
  that owner. `PictureQueue` also holds what is waiting or being drawn, so a page shows placeholders
  (with the caption once the prompt is written) and can cancel; `PictureNotifier` announces the caption
  and the end. Picture this and portraits are refused up front without an image model, or without a
  utility model to write the prompt.
- **`ImagePrompts` is pure**: the brief (`PictureBrief`), the utility-model prompt, the parse and
  `Compose`. Stable looks come from the `Character`, clothes and changes from the chat's
  `CharacterState`; a portrait wears the default outfit. A picture is of the moment it was asked on:
  the messages up to that one go in, not later ones.
- **What a model takes comes from its schema, not from settings.** `RunwareModelCatalog` reads the
  schema Runware publishes per model and caches what it found; a model missing from the index (read
  again after an hour) is treated as the SD family: negative prompt and seed, no references.
  `RunwareImageGenerator` leaves out what the model does not declare, puts references in
  `inputs.referenceImages` and snaps to an allowed size; `GeneratedPicture` reports the size drawn.
- **Reference portraits** (`Character.ReferenceImageId`) of everyone pictured are sent, in the order
  the prompt numbers them ("reference image N"), up to the model's limit, with the subject's
  `ImageSeed`. Only a portrait that loaded gets a number, so the numbers always match what is sent.
  `GeneratedImage.ReferenceImageIds` keeps them; drawing again sends the same ones, and is refused
  when one is gone or the model now takes fewer. A character's first portrait becomes the reference
  and gives them its seed; the user can pick another picture of them, which like any edit hands a
  character the chat introduced over to the user. The avatar
  is that portrait cropped in CSS (`.app-avatar-picture`), never a second stored image. Drawing again
  keeps the prompt and takes a new seed, since the stored one would give back the same picture.
