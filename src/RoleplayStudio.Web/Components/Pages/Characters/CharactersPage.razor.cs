using Microsoft.AspNetCore.Components;
using MudBlazor;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Characters;

public partial class CharactersPage
{
    private IReadOnlyList<Character>? _characters;

    [Inject]
    private CharacterService Characters { get; set; } = null!;

    [Inject]
    private IDialogService Dialogs { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var result = await Characters.ListAsync(Cancellation);
        if (Snackbar.Report(result))
        {
            _characters = result.Value;
        }
    }

    private static string Summary(Character character) =>
        character.Gender is null ? $"{character.Age}" : $"{character.Age} · {character.Gender}";

    private Task AddAsync() => OpenEditorAsync(null);

    private Task EditAsync(Character character) => OpenEditorAsync(character);

    private async Task OpenEditorAsync(Character? character)
    {
        var parameters = new DialogParameters<CharacterDialog> { { d => d.Character, character } };
        var dialog = await Dialogs.ShowAsync<CharacterDialog>("", parameters, DialogServiceExtensions.EditorOptions);
        if (await dialog.Result is { Canceled: false })
        {
            await LoadAsync();
        }
    }

    private async Task DuplicateAsync(Character character)
    {
        var copy = new Character();
        copy.CopyEditableFieldsFrom(character);
        copy.Name = $"{character.Name} (copy)";
        if (Snackbar.Report(await Characters.CreateAsync(copy), "Character duplicated"))
        {
            await LoadAsync();
        }
    }

    private async Task DeleteAsync(Character character)
    {
        if (await Dialogs.ConfirmAsync("Delete character", $"Delete {character.Name}? They also leave every chatbot's cast.", "Delete")
            && Snackbar.Report(await Characters.DeleteAsync(character.Id), "Character deleted"))
        {
            await LoadAsync();
        }
    }
}
