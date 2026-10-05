namespace RoleplayStudio.Domain.Authoring;

/// <summary>A starting situation inside a chatbot from which chats are started.</summary>
public class Scenario : Entity
{
    public Guid ChatbotId { get; set; }
    public Chatbot Chatbot { get; set; } = null!;
    public string Title { get; set; } = "";
    public string? Premise { get; set; }
    public string? StartingLocation { get; set; }
    public string? OpeningMessage { get; set; }
    public string? Goals { get; set; }

    /// <summary>Characters present when the scenario starts; must be part of the chatbot's cast.</summary>
    public List<Guid> StartingCharacterIds { get; set; } = [];
}
