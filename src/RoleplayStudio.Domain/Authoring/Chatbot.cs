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

    /// <summary>Copies the fields a user edits, cleaned; the cast and scenarios are changed on their own.</summary>
    public void CopyEditableFieldsFrom(Chatbot other)
    {
        Name = other.Name.Trim();
        Tagline = TextFields.Clean(other.Tagline);
        WorldDescription = TextFields.Clean(other.WorldDescription);
        ToneAndRules = TextFields.Clean(other.ToneAndRules);
        ImageStylePreset = TextFields.Clean(other.ImageStylePreset);
        DefaultChatModelProfileId = other.DefaultChatModelProfileId;
    }

    /// <summary>The first reason this chatbot cannot be saved, or null when it can.</summary>
    public EditProblem? FindProblem() => string.IsNullOrWhiteSpace(Name) ? new EditProblem(nameof(Name), "Give the chatbot a name") : null;
}

public class ChatbotCharacter
{
    public Guid ChatbotId { get; set; }
    public Guid CharacterId { get; set; }
    public Character Character { get; set; } = null!;
    public string? Role { get; set; }
}

/// <summary>One character in a chatbot's cast, as the cast editor hands it over.</summary>
public sealed record CastMember(Guid CharacterId, string? Role);
