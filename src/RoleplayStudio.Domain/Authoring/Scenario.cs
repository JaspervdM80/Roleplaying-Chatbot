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

    public void CopyEditableFieldsFrom(Scenario other)
    {
        Title = other.Title.Trim();
        Premise = TextFields.Clean(other.Premise);
        StartingLocation = TextFields.Clean(other.StartingLocation);
        OpeningMessage = TextFields.Clean(other.OpeningMessage);
        Goals = TextFields.Clean(other.Goals);
        StartingCharacterIds = other.StartingCharacterIds.Distinct().ToList();
    }

    /// <summary>The first reason this scenario cannot be saved, as a message template, or null when it can.</summary>
    public string? FindProblem(IReadOnlyCollection<Guid> castCharacterIds)
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            return "Give the scenario a title";
        }

        return StartingCharacterIds.All(castCharacterIds.Contains) ? null : "Everyone present at the start must be in the chatbot's cast";
    }

    /// <summary>Who is present when a chat starts: the chosen starting characters, or the whole cast when none were chosen.</summary>
    public IReadOnlyList<Character> PresentAtStart(IReadOnlyList<Character> cast) =>
        StartingCharacterIds.Count == 0 ? cast : cast.Where(c => StartingCharacterIds.Contains(c.Id)).ToList();
}
