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

    /// <summary>Copies the fields a user edits, cleaned, leaving identity and ownership alone.</summary>
    public void CopyEditableFieldsFrom(Persona other)
    {
        Name = other.Name.Trim();
        Age = other.Age;
        Gender = TextFields.Clean(other.Gender);
        Description = TextFields.Clean(other.Description);
        Personality = TextFields.Clean(other.Personality);
        Appearance = other.Appearance.Copy();
        DefaultOutfit = other.DefaultOutfit.Copy();
    }

    /// <summary>The first reason this persona cannot be saved, as a message template, or null when it can.</summary>
    public string? FindProblem()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "Give the persona a name";
        }

        return Age is < 0 ? "The age cannot be negative" : null;
    }
}
