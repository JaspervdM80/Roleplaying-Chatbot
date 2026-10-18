namespace RoleplayStudio.Domain.Chats;

/// <summary>The scene and everyone's state as they were before scene tracking read a message, so rewinding the chat can put them back.</summary>
public class SceneSnapshot
{
    public string? Location { get; set; }
    public string? TimeOfDay { get; set; }
    public string? Mood { get; set; }
    public string? PersonaOutfit { get; set; }
    public List<Guid> PresentCharacterIds { get; set; } = [];
    public List<CharacterSnapshot> Characters { get; set; } = [];

    public static SceneSnapshot Of(SceneState scene, IEnumerable<CharacterState> states) => new()
    {
        Location = scene.Location,
        TimeOfDay = scene.TimeOfDay,
        Mood = scene.Mood,
        PersonaOutfit = scene.PersonaOutfit,
        PresentCharacterIds = [.. scene.PresentCharacterIds],
        Characters = states.Select(CharacterSnapshot.Of).ToList(),
    };

    public SceneState ToScene(long trackedUpToSequence) => new()
    {
        Location = Location,
        TimeOfDay = TimeOfDay,
        Mood = Mood,
        PersonaOutfit = PersonaOutfit,
        PresentCharacterIds = [.. PresentCharacterIds],
        TrackedUpToSequence = trackedUpToSequence,
    };

    public SceneSnapshot Copy() => new()
    {
        Location = Location,
        TimeOfDay = TimeOfDay,
        Mood = Mood,
        PersonaOutfit = PersonaOutfit,
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
    public string? Status { get; set; }
    public string? Relationship { get; set; }

    public static CharacterSnapshot Of(CharacterState state) => new()
    {
        CharacterId = state.CharacterId,
        CurrentOutfit = state.CurrentOutfit,
        AppearanceChanges = state.AppearanceChanges,
        Status = state.Status,
        Relationship = state.Relationship,
    };

    /// <summary>Applies only what the user changed from <paramref name="opened"/>, so what tracking saved meanwhile stays.</summary>
    public void ApplyEditsTo(CharacterState state, CharacterSnapshot opened)
    {
        state.CurrentOutfit = TextFields.Edited(state.CurrentOutfit, opened.CurrentOutfit, CurrentOutfit);
        state.AppearanceChanges = TextFields.Edited(state.AppearanceChanges, opened.AppearanceChanges, AppearanceChanges);
        state.Status = TextFields.Edited(state.Status, opened.Status, Status);
        state.Relationship = TextFields.Edited(state.Relationship, opened.Relationship, Relationship);
    }

    public CharacterState ApplyTo(CharacterState state)
    {
        state.CurrentOutfit = CurrentOutfit;
        state.AppearanceChanges = AppearanceChanges;
        state.Status = Status;
        state.Relationship = Relationship;
        return state;
    }
}
