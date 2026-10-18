using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.Web.Components.Pages.Chats;

/// <summary>Someone in a chat as the scene panel shows them: a character as they are in this chat, or the user's persona.</summary>
public sealed record ScenePerson(
    Guid Id,
    string Name,
    Guid? PortraitId,
    int? Age,
    string? Gender,
    string? Description,
    string? Personality,
    string? Looks,
    string? Wearing,
    string? Status,
    string? Changes,
    string? Relationship,
    bool IsPersona)
{
    public static ScenePerson Of(CharacterState state) => new(
        state.Character.Id,
        state.Character.Name,
        state.Character.ReferenceImageId,
        state.Character.Age,
        state.Character.Gender,
        state.Character.ShortDescription,
        state.Character.Personality,
        state.Character.Appearance,
        state.CurrentOutfit,
        state.Status,
        state.AppearanceChanges,
        state.Relationship,
        false);

    public static ScenePerson Of(Persona persona, SceneState scene) => new(
        persona.Id,
        persona.Name,
        null,
        persona.Age,
        persona.Gender,
        persona.Description,
        persona.Personality,
        persona.Appearance,
        scene.PersonaWears(persona),
        null,
        null,
        null,
        true);
}
