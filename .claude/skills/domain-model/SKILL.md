---
name: domain-model
description: The entities, value objects and cascade rules of the roleplay domain — personas, characters, chatbots, scenarios, chat sessions, per-session state, memories, images and model profiles — and where domain logic belongs. Use when adding or changing a property or entity in RoleplayStudio.Domain, when a rule needs a computed member, or when a delete/cascade decision is involved.
---

# Domain model

`RoleplayStudio.Domain` has no reference to EF Core, ASP.NET or any AI package (Pgvector's `Vector`
struct is the one exception). Mapping lives in `Infrastructure/Data/ApplicationDbContext.cs`.

## The shape

```
User 1──* Persona, Character, Chatbot, ModelProfile, ChatSession, GeneratedImage   (OwnedEntity.OwnerId)
Chatbot 1──* ChatbotCharacter *──1 Character        (the cast)
Chatbot 1──* Scenario                               (StartingCharacterIds ⊆ cast)
ChatSession *──1 Scenario, *──1 Persona
ChatSession 1──* Message, CharacterState, MemoryEntry
```

- **Authoring** (Persona, Character, Chatbot, Scenario) is the reusable template.
- **A chat session is an instance of it**, with its own mutable state: `SceneState`,
  `CharacterState` per character, `SessionSummary`, memories. Changing a character's
  `DefaultOutfit` never rewrites a running chat; the session's `CharacterState.CurrentOutfit` does.
- `Appearance` (stable looks) and `Outfit` (changes during a chat) are separate on purpose: the image
  prompt reads stable looks from the character and clothes from the session state.

## Domain logic lives on the model

Anything computable without the database or an AI call goes on the entity or a pure static helper —
e.g. which characters are present, whether a scenario's starting characters are all in the cast,
building the outfit text for a prompt. Not in a page and not in a service. Prompt assembly is pure
too: it takes loaded entities and returns messages, with no I/O.

## Pass a value object, don't trust a navigation

A method that needs the cast takes the cast, not `chatbot.Cast` hoping the caller `.Include`d it. A
forgotten `Include` makes a navigation an empty list with no compile-time signal — "this chatbot has
no characters" — and the director prompt silently loses its cast.

## Rules worth knowing before changing a member

- **`Message.Sequence` orders a session**, unique per session — never `CreatedAt`, which ties under
  a fast stream and moves with clock skew.
- **`SessionSummary.CoveredUpToSequence`** marks which messages the summary already contains; the
  short-term window starts after it. Rewriting history (edit, regenerate, branch) must move it back.
- **`MemoryEntry.Embedding` has a fixed dimension** (`EmbeddingDimensions`). See the `migrations`
  skill before changing the embedding model.
- **Who is present is `Scene.PresentCharacterIds`**, read through `ChatSession.PresentStates`. The
  prompt and the speaker a reply is saved under both go through it, so they cannot disagree. Until
  speaker tags are parsed, a reply belongs to the one character present, otherwise to the narrator
  (`ChatSession.ReplySpeaker`).
- **Secrets are not domain data.** `ModelProfile.ApiKeySetting` is a configuration key name, never
  the key.
- Enums are persisted **as strings**: adding a member is free, renaming one is a data migration.

## Cascades

- Deleting a **chat session** cascades its messages, character states and memories.
- A **scenario or persona** used by a session is `Restrict`: deleting it must not silently take chats
  with it. The service refuses with a readable message rather than surfacing a `DbUpdateException`.
- Deleting a **chatbot** cascades its scenarios and cast links — and is therefore refused while any
  session still uses one of its scenarios.
- Deleting a **character** is refused while any chat has a `CharacterState` for them; otherwise it
  cascades their cast links and the service also takes them out of every scenario's
  `StartingCharacterIds` (a plain `uuid[]`, which no foreign key cleans up). Leaving a cast does the same.
- A `ModelProfile` delete sets referencing defaults to null.
