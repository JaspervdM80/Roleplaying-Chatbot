using Pgvector;

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
}
