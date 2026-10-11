namespace RoleplayStudio.Domain.Chats;

/// <summary>The scene and everyone's state as they were before scene tracking read a message, so rewinding the chat can put them back.</summary>
public class SceneSnapshot
{
    public string? Location { get; set; }
    public string? TimeOfDay { get; set; }
    public string? Mood { get; set; }
    public List<Guid> PresentCharacterIds { get; set; } = [];
    public List<CharacterSnapshot> Characters { get; set; } = [];

    public SceneSnapshot Copy() => new()
    {
        Location = Location,
        TimeOfDay = TimeOfDay,
        Mood = Mood,
        PresentCharacterIds = [.. PresentCharacterIds],
        Characters = Characters.Select(c => CharacterSnapshot.Of(c.ApplyTo(new CharacterState { CharacterId = c.CharacterId }))).ToList(),
    };
}

/// <summary>The part of a <see cref="CharacterState"/> a chat changes as it goes; the one place that lists those fields.</summary>
public class CharacterSnapshot
{
    public Guid CharacterId { get; set; }
    public string? CurrentOutfit { get; set; }
    public string? AppearanceChanges { get; set; }
    public string? RelationshipToPersona { get; set; }
    public int Affinity { get; set; }
    public string? Status { get; set; }

    public static CharacterSnapshot Of(CharacterState state) => new()
    {
        CharacterId = state.CharacterId,
        CurrentOutfit = state.CurrentOutfit,
        AppearanceChanges = state.AppearanceChanges,
        RelationshipToPersona = state.RelationshipToPersona,
        Affinity = state.Affinity,
        Status = state.Status,
    };

    public CharacterState ApplyTo(CharacterState state)
    {
        state.CurrentOutfit = CurrentOutfit;
        state.AppearanceChanges = AppearanceChanges;
        state.RelationshipToPersona = RelationshipToPersona;
        state.Affinity = Affinity;
        state.Status = Status;
        return state;
    }
}
