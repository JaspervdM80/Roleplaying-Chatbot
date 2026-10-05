using RoleplayStudio.Domain.Authoring;

namespace RoleplayStudio.Domain.Chats;

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
            Scene = new SceneState
            {
                Location = TextFields.Clean(scenario.StartingLocation),
                PresentCharacterIds = present.Select(c => c.Id).ToList(),
            },
            CharacterStates = present
                .Select(c => new CharacterState { CharacterId = c.Id, CurrentOutfit = c.DefaultOutfit.Copy(), UpdatedAt = now })
                .ToList(),
        };

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

    /// <summary>The states of the characters in the scene now, in the order they appear in it.</summary>
    public IReadOnlyList<CharacterState> PresentStates(IReadOnlyList<CharacterState> states) =>
        Scene.PresentCharacterIds
            .Select(id => states.FirstOrDefault(s => s.CharacterId == id))
            .OfType<CharacterState>()
            .ToList();

    public IReadOnlyList<Character> PresentCharacters(IReadOnlyList<CharacterState> states) =>
        PresentStates(states).Select(s => s.Character).ToList();

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
}

/// <summary>Rolling summary of the messages that no longer fit in the short-term window.</summary>
public class SessionSummary
{
    public string Text { get; set; } = "";
    public long CoveredUpToSequence { get; set; }
}
