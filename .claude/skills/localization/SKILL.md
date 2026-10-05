---
name: localization
description: Adding localization or a user-facing string once the UI is translated. Every string goes through IStringLocalizer with the English text as the resource key, resx keys are case-insensitive, and a collision must fail the build. Use when adding a resx file, an L["..."] key, or translating the UI.
---

# Localization

The UI is English-only for now. When it gets a second language (Dutch is the likely one), set it up
this way — every rule here was learned the hard way in a previous app.

## The English text is the key

Every user-facing string goes through `IStringLocalizer<Strings>` (`L`) — in pages and dialogs, not
only in services — and the English text is the resource key, so only the translation resx exists
(`Strings.nl.resx`).

```csharp
L["{0} joined the scene", character.Name]
Result.Failure("Chatbot {0} still has {1} chats", name, count)   // the template is the key
```

Translate the template, not the arguments: a character named "Start" stays "Start".

**AI output is not localized by the UI.** The roleplay language is a prompt setting (persona or
chatbot), not the UI culture.

## A missing key renders English, silently

Nothing warns. Add a test that scans `src/` for literal `L["..."]` keys and fails on any the resx
lacks. It cannot see a key built at runtime (a `Result.Failure` template), so check those by hand.

## Resx keys are case-insensitive

A lowercase action phrase ("delete chat") collides with a capitalised button label ("Delete chat").
MSBuild warns `MSB3568: Duplicate resource name ... ignored` and **the first entry silently wins**.
Promote it in `Directory.Build.props` with `<MSBuildWarningsAsErrors>MSB3568</MSBuildWarningsAsErrors>`
unconditionally — `TreatWarningsAsErrors` does not cover `MSB####` codes. `.claude/hooks/resx-duplicates.sh`
also blocks a colliding edit at once. Reuse the key, or word the two so they genuinely differ.

Watch homographs: one English word with two meanings in context needs two keys.

## Culture switching

The culture lives in a cookie and changes with a full page reload — a circuit's culture is fixed when
it starts, so it cannot be swapped in place.
