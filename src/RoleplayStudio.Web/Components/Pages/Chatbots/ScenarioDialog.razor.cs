using Microsoft.AspNetCore.Components;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Chatbots;

public partial class ScenarioDialog
{
    private static readonly Dictionary<string, EditorSection> Fields = [];

    [Inject]
    private ChatbotService Chatbots { get; set; } = null!;

    [Parameter, EditorRequired]
    public Guid ChatbotId { get; set; }

    [Parameter, EditorRequired]
    public string ChatbotName { get; set; } = "";

    [Parameter, EditorRequired]
    public IReadOnlyList<Character> Cast { get; set; } = [];

    [Parameter]
    public Scenario? Scenario { get; set; }

    protected override Scenario? Original => Scenario;

    protected override IReadOnlyDictionary<string, EditorSection> FieldSections => Fields;

    protected override string Noun => "scenario";

    protected override string NameOf(Scenario entity) => entity.Title;

    protected override void CopyEditableFields(Scenario target, Scenario source) => target.CopyEditableFieldsFrom(source);

    protected override EditProblem? FindProblem(Scenario entity) => entity.FindProblem(Cast.Select(c => c.Id).ToList());

    protected override Task<Result<Scenario>> StoreAsync(Scenario entity, bool isNew) =>
        isNew ? Chatbots.AddScenarioAsync(ChatbotId, entity) : Chatbots.UpdateScenarioAsync(ChatbotId, entity);

    private string PresentHint
    {
        get
        {
            var later = Cast.Where(c => !Edited.StartingCharacterIds.Contains(c.Id)).Select(c => c.Name).ToList();
            if (later.Count == 0 || later.Count == Cast.Count)
            {
                return "Pick nobody to start with the whole cast.";
            }

            var names = later.Count == 1 ? later[0] : $"{string.Join(", ", later[..^1])} and {later[^1]}";
            return $"{names} {(later.Count == 1 ? "joins" : "join")} later. Pick nobody to start with the whole cast.";
        }
    }

    private void Toggle(Guid characterId)
    {
        if (!Edited.StartingCharacterIds.Remove(characterId))
        {
            Edited.StartingCharacterIds.Add(characterId);
        }

        Changed();
    }
}
