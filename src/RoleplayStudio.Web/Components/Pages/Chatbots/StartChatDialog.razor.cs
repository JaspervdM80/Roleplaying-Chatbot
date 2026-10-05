using Microsoft.AspNetCore.Components;
using MudBlazor;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Chatbots;

public partial class StartChatDialog
{
    private IReadOnlyList<Persona>? _personas;
    private Guid? _personaId;
    private bool _starting;

    [CascadingParameter]
    private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject]
    private PersonaService Personas { get; set; } = null!;

    [Inject]
    private ChatSessionService Sessions { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    [Parameter, EditorRequired]
    public Guid ChatbotId { get; set; }

    [Parameter, EditorRequired]
    public Guid ScenarioId { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var result = await Personas.ListAsync(Cancellation);
        if (Snackbar.Report(result))
        {
            _personas = result.Value;
            _personaId = _personas.FirstOrDefault()?.Id;
        }
    }

    private async Task StartAsync()
    {
        _starting = true;
        var result = await Sessions.StartAsync(ChatbotId, ScenarioId, _personaId!.Value);
        _starting = false;
        if (Snackbar.Report(result))
        {
            Dialog.Close(result.Value.Id);
        }
    }
}
