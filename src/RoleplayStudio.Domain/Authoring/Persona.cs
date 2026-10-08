namespace RoleplayStudio.Domain.Authoring;

/// <summary>Who the user plays as. Reusable across chatbots.</summary>
public class Persona : OwnedEntity
{
    public string Name { get; set; } = "";
    public int? Age { get; set; }
    public string? Gender { get; set; }
    public string? Description { get; set; }
    public string? Personality { get; set; }
    public string? Appearance { get; set; }
    public string? DefaultOutfit { get; set; }
    public Guid? AvatarImageId { get; set; }

    /// <summary>Copies the fields a user edits, cleaned, leaving identity and ownership alone.</summary>
    public void CopyEditableFieldsFrom(Persona other)
    {
        Name = other.Name.Trim();
        Age = other.Age;
        Gender = TextFields.Clean(other.Gender);
        Description = TextFields.Clean(other.Description);
        Personality = TextFields.Clean(other.Personality);
        Appearance = TextFields.Clean(other.Appearance);
        DefaultOutfit = TextFields.Clean(other.DefaultOutfit);
    }

    /// <summary>The first reason this persona cannot be saved, or null when it can.</summary>
    public EditProblem? FindProblem()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return new EditProblem(nameof(Name), "Give the persona a name");
        }

        return Age is < 0 ? new EditProblem(nameof(Age), "The age cannot be negative") : null;
    }
}
