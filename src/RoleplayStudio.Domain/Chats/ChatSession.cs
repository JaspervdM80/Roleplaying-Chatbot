using RoleplayStudio.Domain.Authoring;

namespace RoleplayStudio.Domain.Chats;

public class ChatSession : OwnedEntity
{
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
