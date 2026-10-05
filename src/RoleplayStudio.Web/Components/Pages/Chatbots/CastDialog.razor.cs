using Microsoft.AspNetCore.Components;
using MudBlazor;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Chatbots;

public partial class CastDialog
{
    private List<Row>? _rows;
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
    public IReadOnlyList<ChatbotCharacter> Cast { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        var result = await Characters.ListAsync(Cancellation);
        if (Snackbar.Report(result))
        {
            _rows = result.Value
                .Select(c => Cast.FirstOrDefault(m => m.CharacterId == c.Id) is { } member
                    ? new Row(c) { InCast = true, Role = member.Role }
                    : new Row(c))
                .ToList();
        }
    }

    private async Task SaveAsync()
    {
        _saving = true;
        var cast = _rows!.Where(r => r.InCast).Select(r => new CastMember(r.Character.Id, r.Role)).ToList();
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
