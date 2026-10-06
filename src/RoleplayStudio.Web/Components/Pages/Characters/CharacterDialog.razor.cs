using Microsoft.AspNetCore.Components;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Characters;

public partial class CharacterDialog
{
    private static readonly EditorSection Identity = new("identity", "Identity", "Identity");
    private static readonly EditorSection Voice = new("voice", "Personality and voice", "Personality");
    private static readonly EditorSection Looks = new("appearance", "Appearance", "Appearance");
    private static readonly EditorSection Outfit = new("outfit", "Default outfit", "Outfit");
    private static readonly EditorSection Images = new("images", "Image settings", "Images");
    private static readonly IReadOnlyList<EditorSection> Sections = [Identity, Voice, Looks, Outfit, Images];

    private static readonly Dictionary<string, EditorSection> Fields = new()
    {
        [nameof(Character.Name)] = Identity,
        [nameof(Character.Age)] = Identity,
    };

    [Inject]
    private CharacterService Characters { get; set; } = null!;

    [Parameter]
    public Character? Character { get; set; }

    protected override Character? Original => Character;

    protected override IReadOnlyDictionary<string, EditorSection> FieldSections => Fields;

    protected override string Noun => "character";

    protected override string NameOf(Character entity) => entity.Name;

    protected override void CopyEditableFields(Character target, Character source) => target.CopyEditableFieldsFrom(source);

    protected override EditProblem? FindProblem(Character entity) => entity.FindProblem();

    protected override Task<Result<Character>> StoreAsync(Character entity, bool isNew) =>
        isNew ? Characters.CreateAsync(entity) : Characters.UpdateAsync(entity);
}
