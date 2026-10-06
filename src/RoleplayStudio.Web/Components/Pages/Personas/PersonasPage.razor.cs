using Microsoft.AspNetCore.Components;
using MudBlazor;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Personas;

public partial class PersonasPage
{
    private IReadOnlyList<Persona>? _personas;

    [Inject]
    private PersonaService Personas { get; set; } = null!;

    [Inject]
    private IDialogService Dialogs { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var result = await Personas.ListAsync(Cancellation);
        if (Snackbar.Report(result))
        {
            _personas = result.Value;
        }
    }

    private Task AddAsync() => OpenEditorAsync(null);

    private Task EditAsync(Persona persona) => OpenEditorAsync(persona);

    private async Task OpenEditorAsync(Persona? persona)
    {
        var parameters = new DialogParameters<PersonaDialog> { { d => d.Persona, persona } };
        var dialog = await Dialogs.ShowAsync<PersonaDialog>("", parameters, DialogServiceExtensions.EditorOptions);
        if (await dialog.Result is { Canceled: false })
        {
            await LoadAsync();
        }
    }

    private async Task DeleteAsync(Persona persona)
    {
        if (await Dialogs.ConfirmAsync("Delete persona", $"Delete {persona.Name}?", "Delete")
            && Snackbar.Report(await Personas.DeleteAsync(persona.Id), "Persona deleted"))
        {
            await LoadAsync();
        }
    }
}
