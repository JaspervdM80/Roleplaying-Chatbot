namespace RoleplayStudio.Domain.Authoring;

public class Character : OwnedEntity
{
    public const int MinimumAge = 12;

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

    /// <summary>Copies the fields a user edits, cleaned, leaving identity, ownership and images alone.</summary>
    public void CopyEditableFieldsFrom(Character other)
    {
        Name = other.Name.Trim();
        Age = other.Age;
        Gender = TextFields.Clean(other.Gender);
        ShortDescription = TextFields.Clean(other.ShortDescription);
        Personality = TextFields.Clean(other.Personality);
        SpeechStyle = TextFields.Clean(other.SpeechStyle);
        Backstory = TextFields.Clean(other.Backstory);
        Boundaries = TextFields.Clean(other.Boundaries);
        Appearance = other.Appearance.Copy();
        DefaultOutfit = other.DefaultOutfit.Copy();
        ImageTags = TextFields.Clean(other.ImageTags);
        ImageSeed = other.ImageSeed;
    }

    /// <summary>The first reason this character cannot be saved, as a message template, or null when it can.</summary>
    public string? FindProblem()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "Give the character a name";
        }

        return Age < 0 ? "The age cannot be negative" : null;
    }
}
