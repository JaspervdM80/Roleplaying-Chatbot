using System.Text.Json;
using Microsoft.AspNetCore.Components;
using MudBlazor;
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

    private static readonly Dictionary<string, EditorSection> FieldSections = new()
    {
        [nameof(Character.Name)] = Identity,
        [nameof(Character.Age)] = Identity,
    };

    private EditorSheet _sheet = null!;
    private Character _character = new();
    private string _saved = "";
    private EditProblem? _problem;
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

    private string Title => Character is null ? "New character" : Character.Name;

    private string DiscardMessage => Character is null ? "This character hasn't been saved." : $"Your edits to {Character.Name} haven't been saved.";

    private string? ProblemSection => _problem is null ? null : FieldSections.GetValueOrDefault(_problem.Field)?.Id;

    protected override void OnInitialized()
    {
        if (Character is not null)
        {
            _character = new Character { Id = Character.Id };
            _character.CopyEditableFieldsFrom(Character);
        }

        _saved = Snapshot();
    }

    // Compares what a save would store, so a trailing space or a field emptied again is not an edit.
    private string Snapshot()
    {
        var stored = new Character { Id = _character.Id, CreatedAt = default, UpdatedAt = default };
        stored.CopyEditableFieldsFrom(_character);
        return JsonSerializer.Serialize(stored);
    }

    private bool IsDirty() => Snapshot() != _saved;

    private bool IsProblem(string field) => _problem?.Field == field;

    private void Changed()
    {
        if (_problem is not null && _character.FindProblem()?.Field != _problem.Field)
        {
            _problem = null;
        }
    }

    private async Task SaveAsync()
    {
        _problem = _character.FindProblem();
        if (_problem is not null)
        {
            await _sheet.ShowFieldAsync(_problem.Field);
            return;
        }

        _saving = true;
        var result = Character is null ? await Characters.CreateAsync(_character) : await Characters.UpdateAsync(_character);
        _saving = false;
        if (Snackbar.Report(result, "Character saved"))
        {
            Dialog.Close(result.Value);
        }
    }
}
