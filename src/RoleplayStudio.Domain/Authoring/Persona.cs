namespace RoleplayStudio.Domain.Authoring;

/// <summary>Who the user plays as. Reusable across chatbots.</summary>
public class Persona : OwnedEntity
{
    public string Name { get; set; } = "";
    public int? Age { get; set; }
    public string? Gender { get; set; }
    public string? Description { get; set; }
    public string? Personality { get; set; }
    public Appearance Appearance { get; set; } = new();
    public Outfit DefaultOutfit { get; set; } = new();
    public Guid? AvatarImageId { get; set; }
}
