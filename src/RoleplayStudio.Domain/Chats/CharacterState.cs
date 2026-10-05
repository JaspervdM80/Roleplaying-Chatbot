using RoleplayStudio.Domain.Authoring;

namespace RoleplayStudio.Domain.Chats;

/// <summary>How a character currently looks and feels within one chat session.</summary>
public class CharacterState : Entity
{
    public Guid SessionId { get; set; }
    public Guid CharacterId { get; set; }
    public Character Character { get; set; } = null!;
    public Outfit CurrentOutfit { get; set; } = new();
    public string? AppearanceChanges { get; set; }
    public string? RelationshipToPersona { get; set; }
    public int Affinity { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
