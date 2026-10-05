namespace RoleplayStudio.Domain.Authoring;

/// <summary>A world with a cast of characters, rules and model settings.</summary>
public class Chatbot : OwnedEntity
{
    public string Name { get; set; } = "";
    public string? Tagline { get; set; }
    public string? WorldDescription { get; set; }
    public string? ToneAndRules { get; set; }
    public string? ImageStylePreset { get; set; }
    public Guid? DefaultChatModelProfileId { get; set; }

    public List<ChatbotCharacter> Cast { get; set; } = [];
    public List<Scenario> Scenarios { get; set; } = [];
}

public class ChatbotCharacter
{
    public Guid ChatbotId { get; set; }
    public Guid CharacterId { get; set; }
    public Character Character { get; set; } = null!;
    public string? Role { get; set; }
}
