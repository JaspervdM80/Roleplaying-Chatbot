using Microsoft.AspNetCore.Components;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Personas;

public partial class PersonaDialog
{
    private static readonly EditorSection Identity = new("identity", "Identity", "Identity");
    private static readonly EditorSection About = new("about", "About you", "About");
    private static readonly EditorSection Looks = new("appearance", "Appearance", "Appearance");
    private static readonly EditorSection Outfit = new("outfit", "Usual outfit", "Outfit");
    private static readonly IReadOnlyList<EditorSection> Sections = [Identity, About, Looks, Outfit];

    private static readonly Dictionary<string, EditorSection> Fields = new()
    {
        [nameof(Persona.Name)] = Identity,
        [nameof(Persona.Age)] = Identity,
    };

    [Inject]
    private PersonaService Personas { get; set; } = null!;

    [Parameter]
    public Persona? Persona { get; set; }

    protected override Persona? Original => Persona;

    protected override IReadOnlyDictionary<string, EditorSection> FieldSections => Fields;

    protected override string Noun => "persona";

    protected override string NameOf(Persona entity) => entity.Name;

    protected override void CopyEditableFields(Persona target, Persona source) => target.CopyEditableFieldsFrom(source);

    protected override EditProblem? FindProblem(Persona entity) => entity.FindProblem();

    protected override Task<Result<Persona>> StoreAsync(Persona entity, bool isNew) =>
        isNew ? Personas.CreateAsync(entity) : Personas.UpdateAsync(entity);
}
