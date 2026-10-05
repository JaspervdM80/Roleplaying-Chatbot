using Microsoft.AspNetCore.Components;
using MudBlazor;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Chatbots;

public partial class ScenarioDialog
{
    private readonly HashSet<Guid> _starting = [];
    private Scenario _scenario = new();
    private bool _saving;

    [CascadingParameter]
    private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject]
    private ChatbotService Chatbots { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    [Parameter, EditorRequired]
    public Guid ChatbotId { get; set; }

    [Parameter, EditorRequired]
    public IReadOnlyList<Character> Cast { get; set; } = [];

    /// <summary>The scenario to edit; it is copied, so cancelling leaves the caller's instance untouched.</summary>
    [Parameter]
    public Scenario? Scenario { get; set; }

    protected override void OnInitialized()
    {
        if (Scenario is not null)
        {
            _scenario = new Scenario { Id = Scenario.Id };
            _scenario.CopyEditableFieldsFrom(Scenario);
            _starting.UnionWith(Scenario.StartingCharacterIds);
        }
    }

    private void Toggle(Guid characterId, bool present)
    {
        if (present)
        {
            _starting.Add(characterId);
        }
        else
        {
            _starting.Remove(characterId);
        }
    }

    private async Task SaveAsync()
    {
        _saving = true;
        _scenario.StartingCharacterIds = Cast.Select(c => c.Id).Where(_starting.Contains).ToList();
        var result = Scenario is null
            ? await Chatbots.AddScenarioAsync(ChatbotId, _scenario)
            : await Chatbots.UpdateScenarioAsync(ChatbotId, _scenario);
        _saving = false;
        if (Snackbar.Report(result, "Scenario saved"))
        {
            Dialog.Close(result.Value);
        }
    }
}
