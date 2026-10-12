using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Memory;

namespace RoleplayStudio.Domain.Chats;

/// <summary>What a rewind took out besides the messages: memories mined from them, and states of anyone met in them.</summary>
public sealed record RewoundHistory(IReadOnlyList<MemoryEntry> DroppedMemories, IReadOnlyList<CharacterState> DroppedStates);

public class ChatSession : OwnedEntity
{
    public const string NarratorName = "Narrator";

    public string Title { get; set; } = "";
    public Guid ScenarioId { get; set; }
    public Scenario Scenario { get; set; } = null!;
    public Guid PersonaId { get; set; }
    public Persona Persona { get; set; } = null!;
    public Guid? ChatModelProfileId { get; set; }
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;

    public SceneState Scene { get; set; } = new();
    public SessionSummary Summary { get; set; } = new();

    /// <summary>The last message memory extraction has read; later messages have not been mined for memories yet.</summary>
    public long MemoriesExtractedUpToSequence { get; set; }

    /// <summary>Moved on by every rewind, so upkeep that read the chat before one cannot save over it.</summary>
    public int HistoryVersion { get; set; }

    public List<Message> Messages { get; set; } = [];
    public List<CharacterState> CharacterStates { get; set; } = [];

    /// <summary>A new chat from the scenario: who is present wears their default outfit, and the opening message comes first.</summary>
    public static ChatSession Start(Scenario scenario, Persona persona, IReadOnlyList<Character> cast, Guid? chatModelProfileId, DateTimeOffset now)
    {
        var present = scenario.PresentAtStart(cast);
        var session = new ChatSession
        {
            Title = scenario.Title,
            ScenarioId = scenario.Id,
            PersonaId = persona.Id,
            ChatModelProfileId = chatModelProfileId,
            LastActivityAt = now,
            Scene = new SceneState { Location = TextFields.Clean(scenario.StartingLocation) },
        };
        session.SetPresent(present, now);

        if (TextFields.Clean(scenario.OpeningMessage) is { } opening)
        {
            var (role, speakerId, speakerName) = ReplySpeaker(present);
            session.Messages.Add(new Message
            {
                Sequence = 1,
                Role = role,
                SpeakerCharacterId = speakerId,
                SpeakerName = speakerName,
                Content = opening,
                CreatedAt = now,
            });
        }

        return session;
    }

    /// <summary>Puts exactly these characters in the scene; anyone who leaves keeps their state for when they come back.</summary>
    public void SetPresent(IReadOnlyList<Character> present, DateTimeOffset now)
    {
        foreach (var character in present)
        {
            MeetIfNew(character, now);
        }

        Scene.PresentCharacterIds = present.Select(c => c.Id).Distinct().ToList();
    }

    /// <summary>Adds one character to the scene, in the clothes they left in if they were met before.</summary>
    public void BringIn(Character character, DateTimeOffset now)
    {
        MeetIfNew(character, now);
        if (!Scene.PresentCharacterIds.Contains(character.Id))
        {
            Scene.PresentCharacterIds = [.. Scene.PresentCharacterIds, character.Id];
        }
    }

    private void MeetIfNew(Character character, DateTimeOffset now)
    {
        if (CharacterStates.All(s => s.CharacterId != character.Id))
        {
            CharacterStates.Add(new CharacterState { CharacterId = character.Id, CurrentOutfit = character.DefaultOutfit, UpdatedAt = now });
        }
    }

    /// <summary>Characters met earlier in this chat who are not in the scene now.</summary>
    public IReadOnlyList<CharacterState> AbsentStates() =>
        CharacterStates.Where(s => !Scene.PresentCharacterIds.Contains(s.CharacterId)).OrderBy(s => s.Character.Name).ToList();

    /// <summary>The states of the characters in the scene now, in the order they appear in it.</summary>
    public IReadOnlyList<CharacterState> PresentStates() =>
        Scene.PresentCharacterIds
            .Select(id => CharacterStates.FirstOrDefault(s => s.CharacterId == id))
            .OfType<CharacterState>()
            .ToList();

    public IReadOnlyList<Character> PresentCharacters() =>
        PresentStates().Select(s => s.Character).ToList();

    public SceneSnapshot Snapshot() => SceneSnapshot.Of(Scene, CharacterStates);

    /// <summary>
    /// Undoes what the messages from <paramref name="fromSequence"/> on left in the scene, the summary and memory, so
    /// upkeep reads the messages before them again; removing those messages is the caller's step.
    /// </summary>
    public RewoundHistory Rewind(long fromSequence, IReadOnlyList<Message> messages, IReadOnlyList<MemoryEntry> memories)
    {
        HistoryVersion++;

        // One extraction pass shares its source range, so a pass that read any removed message is read again whole.
        var reread = memories.Where(m => m.SourceToSequence >= fromSequence).ToList();
        var droppedMemories = reread.Where(m => !m.IsPinned).ToList();
        var rereadFrom = reread.Select(m => m.SourceFromSequence ?? fromSequence).Append(fromSequence).Min();
        MemoriesExtractedUpToSequence = Math.Min(MemoriesExtractedUpToSequence, rereadFrom - 1);

        if (Summary.CoveredUpToSequence >= fromSequence)
        {
            Summary = new SessionSummary();
        }

        var droppedStates = Scene.TrackedUpToSequence >= fromSequence ? RestoreScene(fromSequence, messages) : [];
        return new RewoundHistory(droppedMemories, droppedStates);
    }

    private IReadOnlyList<CharacterState> RestoreScene(long fromSequence, IReadOnlyList<Message> messages)
    {
        var checkpoint = messages.Where(m => m.Sequence <= fromSequence && m.SceneBefore is not null).MaxBy(m => m.Sequence);
        if (checkpoint?.SceneBefore is not { } snapshot)
        {
            // Tracked before snapshots were kept: what it changed cannot be undone, only tracked on from here.
            Scene.TrackedUpToSequence = fromSequence - 1;
            return [];
        }

        Scene = snapshot.ToScene(trackedUpToSequence: checkpoint.Sequence - 1);

        var dropped = new List<CharacterState>();
        foreach (var state in CharacterStates.ToList())
        {
            if (snapshot.Characters.Find(c => c.CharacterId == state.CharacterId) is not { } saved)
            {
                CharacterStates.Remove(state);
                dropped.Add(state);
                continue;
            }

            saved.ApplyTo(state);
        }

        return dropped;
    }

    /// <summary>
    /// A new chat holding this one's <paramref name="messages"/> up to and including <paramref name="upToSequence"/>,
    /// with the scene, summary and the <paramref name="memories"/> as they stood there. The message after that point,
    /// when given, is read only for the scene it was tracked from.
    /// </summary>
    public (ChatSession Branch, IReadOnlyList<MemoryEntry> Memories) Branch(long upToSequence, IReadOnlyList<Message> messages, IReadOnlyList<MemoryEntry> memories, DateTimeOffset now)
    {
        var branch = new ChatSession
        {
            Title = $"{Title} (branch)",
            ScenarioId = ScenarioId,
            PersonaId = PersonaId,
            ChatModelProfileId = ChatModelProfileId,
            LastActivityAt = now,
            Scene = Scene.Copy(),
            Summary = new SessionSummary { Text = Summary.Text, CoveredUpToSequence = Summary.CoveredUpToSequence },
            MemoriesExtractedUpToSequence = MemoriesExtractedUpToSequence,
        };

        branch.Messages = messages.Where(m => m.Sequence <= upToSequence).Select(m => new Message
        {
            SessionId = branch.Id,
            Sequence = m.Sequence,
            Role = m.Role,
            SpeakerCharacterId = m.SpeakerCharacterId,
            SpeakerName = m.SpeakerName,
            Content = m.Content,
            TokenCount = m.TokenCount,
            CreatedAt = m.CreatedAt,
            SceneBefore = m.SceneBefore?.Copy(),
        }).ToList();

        // The character is linked by id only: setting the navigation would add a copy of them too.
        branch.CharacterStates = CharacterStates
            .Select(s => CharacterSnapshot.Of(s).ApplyTo(new CharacterState { SessionId = branch.Id, CharacterId = s.CharacterId, UpdatedAt = s.UpdatedAt }))
            .ToList();

        var copies = memories.Select(m => new MemoryEntry
        {
            SessionId = branch.Id,
            Type = m.Type,
            Text = m.Text,
            Embedding = m.Embedding,
            Importance = m.Importance,
            RelatedCharacterIds = [.. m.RelatedCharacterIds],
            SourceFromSequence = m.SourceFromSequence,
            SourceToSequence = m.SourceToSequence,
            IsPinned = m.IsPinned,
            CreatedAt = m.CreatedAt,
            LastRecalledAt = m.LastRecalledAt,
        }).ToList();

        var rewound = branch.Rewind(upToSequence + 1, messages, copies);
        return (branch, copies.Except(rewound.DroppedMemories).ToList());
    }

    /// <summary>Who a model reply is attributed to: the one character present, or the narrator for a group or an empty scene.</summary>
    public static (MessageRole Role, Guid? CharacterId, string Name) ReplySpeaker(IReadOnlyList<Character> present) =>
        present.Count == 1
            ? (MessageRole.Character, present[0].Id, present[0].Name)
            : (MessageRole.Narrator, null, NarratorName);
}

public class SceneState
{
    public string? Location { get; set; }
    public string? TimeOfDay { get; set; }
    public string? Mood { get; set; }
    public List<Guid> PresentCharacterIds { get; set; } = [];

    /// <summary>The last message scene tracking has read; later messages may have moved people, clothes or the place.</summary>
    public long TrackedUpToSequence { get; set; }

    public SceneState Copy() => new()
    {
        Location = Location,
        TimeOfDay = TimeOfDay,
        Mood = Mood,
        PresentCharacterIds = [.. PresentCharacterIds],
        TrackedUpToSequence = TrackedUpToSequence,
    };
}

/// <summary>Rolling summary of the messages that no longer fit in the short-term window.</summary>
public class SessionSummary
{
    public string Text { get; set; } = "";
    public long CoveredUpToSequence { get; set; }
}
