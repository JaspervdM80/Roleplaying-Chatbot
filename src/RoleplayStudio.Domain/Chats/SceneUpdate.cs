using RoleplayStudio.Domain.Authoring;

namespace RoleplayStudio.Domain.Chats;

/// <summary>
/// A field the model left out is null and keeps its value. <see cref="NewName"/>, <see cref="Age"/>, <see cref="Gender"/> and
/// <see cref="Appearance"/> are what the story revealed about a character it introduced, and change nobody else.
/// </summary>
public sealed record CharacterChange(
    string Name,
    string? Status,
    string? AppearanceChanges,
    string? Outfit,
    string? NewName = null,
    int? Age = null,
    string? Gender = null,
    string? Appearance = null,
    string? Relationship = null);

public sealed record Newcomer(string Name, string? Role, int? Age, string? Gender, string? Description, string? Personality, string? SpeechStyle, string? Appearance, string? Outfit);

/// <summary>What the utility model read from the latest messages; <see cref="Present"/> is null when it did not say who is there.</summary>
public sealed record SceneUpdate(
    string? Location,
    string? TimeOfDay,
    string? Mood,
    IReadOnlyList<string>? Present,
    IReadOnlyList<CharacterChange> Changes,
    IReadOnlyList<Newcomer> Newcomers,
    string? PersonaOutfit = null)
{
    public const double RestyleGrowth = 1.2;

    /// <summary>Applies the update to a tracked session; newcomers are already present.</summary>
    public SceneOutcome ApplyTo(ChatSession session, SceneCast cast, string personaName, DateTimeOffset now)
    {
        session.Scene.Location = Location ?? session.Scene.Location;
        session.Scene.TimeOfDay = TimeOfDay ?? session.Scene.TimeOfDay;
        session.Scene.Mood = Mood ?? session.Scene.Mood;
        session.Scene.PersonaOutfit = PersonaOutfit ?? session.Scene.PersonaOutfit;

        var known = cast.Met.Select(s => s.Character).Concat(cast.Cast).DistinctBy(c => c.Id).ToList();

        // Renamed first, so the rest of the update finds them by either name.
        var formerNames = new Dictionary<string, Character>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in Changes.Where(c => c.NewName is not null))
        {
            if (CharacterNames.Find(known, change.Name) is { } character
                && character.IntroducedInSessionId == session.Id
                && Fit(change.NewName!) is { } newName
                && !CharacterNames.Names(personaName, newName)
                && (CharacterNames.Find(known, newName) ?? character) == character)
            {
                formerNames[character.Name.Trim()] = character;
                character.Name = newName;
            }
        }

        Character? Resolve(string name) => CharacterNames.Find(known, name) ?? formerNames.GetValueOrDefault(name);

        var created = new List<(Character, string?)>();
        foreach (var newcomer in Newcomers.Where(n => !CharacterNames.Names(personaName, n.Name) && Resolve(n.Name) is null))
        {
            var character = new Character
            {
                IntroducedInSessionId = session.Id,
                Name = Fit(newcomer.Name)!,
                Gender = newcomer.Gender,
                ShortDescription = newcomer.Description,
                Personality = newcomer.Personality,
                SpeechStyle = newcomer.SpeechStyle,
                Appearance = newcomer.Appearance,
                DefaultOutfit = newcomer.Outfit,
                Age = newcomer.Age,
            };

            known.Add(character);
            created.Add((character, newcomer.Role));
        }

        if (Present is { } names)
        {
            var present = names
                .Where(n => !CharacterNames.Names(personaName, n))
                .Select(Resolve)
                .OfType<Character>()
                .Concat(created.Select(c => c.Item1))
                .DistinctBy(c => c.Id)
                .ToList();
            session.SetPresent(present, now);
        }
        else if (created.Count > 0)
        {
            session.SetPresent([.. session.Scene.PresentCharacterIds.Select(id => known.First(c => c.Id == id)), .. created.Select(c => c.Item1)], now);
        }

        var restyled = new List<Character>();
        foreach (var change in Changes)
        {
            if (Resolve(change.Name) is not { } character || session.CharacterStates.FirstOrDefault(s => s.CharacterId == character.Id) is not { } state)
            {
                continue;
            }

            state.Status = change.Status ?? state.Status;
            state.AppearanceChanges = change.AppearanceChanges ?? state.AppearanceChanges;
            state.CurrentOutfit = change.Outfit ?? state.CurrentOutfit;
            state.Relationship = change.Relationship ?? state.Relationship;
            state.UpdatedAt = now;

            if (character.IntroducedInSessionId != session.Id)
            {
                continue;
            }

            character.Age = change.Age ?? character.Age;
            character.Gender = change.Gender ?? character.Gender;
            if (change.Appearance is { } appearance && !string.Equals(appearance, character.Appearance?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                // A model rewording the same looks each turn would redraw and re-reference them each turn; only more to go on earns a portrait.
                if (appearance.Length >= (character.Appearance?.Trim().Length ?? 0) * RestyleGrowth)
                {
                    restyled.Add(character);
                }

                character.Appearance = appearance;
            }
        }

        var newcomers = created.Select(c => c.Item1).ToList();
        return new SceneOutcome(created, formerNames.Values.Distinct().ToList(), restyled.Except(newcomers).Distinct().ToList());
    }

    private static string? Fit(string name)
    {
        var trimmed = name.Trim();
        return trimmed.Length == 0 ? null : trimmed.Length > Character.MaxNameLength ? trimmed[..Character.MaxNameLength].TrimEnd() : trimmed;
    }
}

/// <summary>The characters a tracked scene can name: those met in this chat, and the rest of the chatbot's cast.</summary>
public sealed record SceneCast(IReadOnlyList<CharacterState> Met, IReadOnlyList<Character> Cast);

/// <summary>What applying an update did to characters: newcomers made (not yet saved or cast), those renamed, and those whose lasting looks were filled in.</summary>
public sealed record SceneOutcome(IReadOnlyList<(Character Character, string? Role)> Newcomers, IReadOnlyList<Character> Renamed, IReadOnlyList<Character> Restyled);
