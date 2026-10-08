using Pgvector;
using RoleplayStudio.Domain.Authoring;

namespace RoleplayStudio.Domain.Memory;

public enum MemoryType
{
    Event,
    Fact,
    Relationship,
    Appearance,
    Preference,
    World,
}

public class MemoryEntry : Entity
{
    public const int EmbeddingDimensions = 768;
    public const int MinImportance = 1;
    public const int MaxImportance = 10;

    public Guid SessionId { get; set; }
    public MemoryType Type { get; set; }
    public string Text { get; set; } = "";
    public Vector? Embedding { get; set; }
    public int Importance { get; set; } = 5;
    public List<Guid> RelatedCharacterIds { get; set; } = [];
    public long? SourceFromSequence { get; set; }
    public long? SourceToSequence { get; set; }
    public bool IsPinned { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastRecalledAt { get; set; }

    public void CopyEditableFieldsFrom(MemoryEntry other)
    {
        Type = other.Type;
        Text = other.Text.Trim();
        Importance = Math.Clamp(other.Importance, MinImportance, MaxImportance);
        RelatedCharacterIds = other.RelatedCharacterIds.Distinct().Order().ToList();
        IsPinned = other.IsPinned;
    }

    /// <summary>The first reason this memory cannot be saved, or null when it can.</summary>
    public EditProblem? FindProblem(IReadOnlyCollection<Guid> chatCharacterIds)
    {
        if (string.IsNullOrWhiteSpace(Text))
        {
            return new EditProblem(nameof(Text), "Write what should be remembered");
        }

        return RelatedCharacterIds.All(chatCharacterIds.Contains)
            ? null
            : new EditProblem(nameof(RelatedCharacterIds), "Only characters met in this chat can be linked to a memory");
    }

    /// <summary>Whether the memory passes a kind, character and text filter; a null filter lets everything through.</summary>
    public bool Matches(string? search, MemoryType? type, Guid? characterId) =>
        (type is null || Type == type)
        && (characterId is null || RelatedCharacterIds.Contains(characterId.Value))
        && (string.IsNullOrWhiteSpace(search) || Text.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase));
}
