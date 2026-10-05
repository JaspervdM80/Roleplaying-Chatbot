namespace RoleplayStudio.Domain.Authoring;

public class Character : OwnedEntity
{
    public const int MinimumAge = 18;

    public string Name { get; set; } = "";
    public int Age { get; set; } = MinimumAge;
    public string? Gender { get; set; }
    public string? ShortDescription { get; set; }
    public string? Personality { get; set; }
    public string? SpeechStyle { get; set; }
    public string? Backstory { get; set; }
    public string? Boundaries { get; set; }

    public Appearance Appearance { get; set; } = new();
    public Outfit DefaultOutfit { get; set; } = new();

    /// <summary>Extra comma-separated tags appended to every image prompt for this character.</summary>
    public string? ImageTags { get; set; }
    public long? ImageSeed { get; set; }
    public Guid? AvatarImageId { get; set; }
    public Guid? ReferenceImageId { get; set; }
}
