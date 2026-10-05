using Microsoft.AspNetCore.Components;
using MudBlazor;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Personas;

public partial class PersonaDialog
{
    private Persona _persona = new();
    private bool _saving;

    [CascadingParameter]
    private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject]
    private PersonaService Personas { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    /// <summary>The persona to edit; it is copied, so cancelling leaves the caller's instance untouched.</summary>
    [Parameter]
    public Persona? Persona { get; set; }

    protected override void OnInitialized()
    {
        if (Persona is not null)
        {
            _persona = new Persona { Id = Persona.Id };
            _persona.CopyEditableFieldsFrom(Persona);
        }
    }

    private async Task SaveAsync()
    {
        _saving = true;
        var result = Persona is null ? await Personas.CreateAsync(_persona) : await Personas.UpdateAsync(_persona);
        _saving = false;
        if (Snackbar.Report(result, "Persona saved"))
        {
            Dialog.Close(result.Value);
        }
    }
}
