using Microsoft.AspNetCore.Components;
using MudBlazor;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Characters;

public partial class CharacterDialog
{
    private Character _character = new();
    private bool _saving;

    [CascadingParameter]
    private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject]
    private CharacterService Characters { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    /// <summary>The character to edit; it is copied, so cancelling leaves the caller's instance untouched.</summary>
    [Parameter]
    public Character? Character { get; set; }

    protected override void OnInitialized()
    {
        if (Character is not null)
        {
            _character = new Character { Id = Character.Id };
            _character.CopyEditableFieldsFrom(Character);
        }
    }

    private async Task SaveAsync()
    {
        _saving = true;
        var result = Character is null ? await Characters.CreateAsync(_character) : await Characters.UpdateAsync(_character);
        _saving = false;
        if (Snackbar.Report(result, "Character saved"))
        {
            Dialog.Close(result.Value);
        }
    }
}
