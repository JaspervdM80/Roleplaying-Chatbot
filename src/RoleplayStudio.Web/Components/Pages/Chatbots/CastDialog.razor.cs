using Microsoft.AspNetCore.Components;
using MudBlazor;
using RoleplayStudio.Domain;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Chatbots;

public partial class CastDialog
{
    private List<Row>? _rows;
    private string _search = "";
    private string _saved = "";
    private bool _saving;

    [CascadingParameter]
    private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject]
    private ChatbotService Chatbots { get; set; } = null!;

    [Inject]
    private CharacterService Characters { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    [Parameter, EditorRequired]
    public Guid ChatbotId { get; set; }

    [Parameter, EditorRequired]
    public string ChatbotName { get; set; } = "";

    [Parameter, EditorRequired]
    public IReadOnlyList<ChatbotCharacter> Cast { get; set; } = [];

    private List<Row> Shown => _rows!.Where(r => r.Character.Name.Contains(_search.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList();

    private string CastCount => _rows is null ? "" : $"{_rows.Count(r => r.InCast)} in the cast";

    protected override async Task OnInitializedAsync()
    {
        var result = await Characters.ListAsync(Cancellation);
        if (Snackbar.Report(result))
        {
            // The cast comes first so it is in view; rows keep their place while boxes are ticked.
            _rows = result.Value
                .Select(c => Cast.FirstOrDefault(m => m.CharacterId == c.Id) is { } member
                    ? new Row(c) { InCast = true, Role = member.Role }
                    : new Row(c))
                .OrderByDescending(r => r.InCast)
                .ToList();
            _saved = Snapshot();
        }
    }

    // Compares what a save would store, so a role emptied again or a trailing space is not an edit.
    private string Snapshot() => string.Join(';', _rows!.Where(r => r.InCast).Select(r => $"{r.Character.Id}:{TextFields.Clean(r.Role)}"));

    private bool IsDirty() => _rows is not null && Snapshot() != _saved;

    private async Task SaveAsync()
    {
        if (_rows is null)
        {
            return;
        }

        _saving = true;
        var cast = _rows.Where(r => r.InCast).Select(r => new CastMember(r.Character.Id, r.Role)).ToList();
        var result = await Chatbots.SetCastAsync(ChatbotId, cast);
        _saving = false;
        if (Snackbar.Report(result, "Cast saved"))
        {
            Dialog.Close(true);
        }
    }

    private sealed class Row(Character character)
    {
        public Character Character { get; } = character;
        public bool InCast { get; set; }
        public string? Role { get; set; }
    }
}
