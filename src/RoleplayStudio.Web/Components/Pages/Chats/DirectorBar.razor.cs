using Microsoft.AspNetCore.Components;
using MudBlazor;
using RoleplayStudio.AI.Chat;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Chats;

public partial class DirectorBar
{
    [Inject]
    private IDialogService Dialogs { get; set; } = null!;

    [Parameter, EditorRequired]
    public IReadOnlyList<(Guid Id, string Name)> Present { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<Character> Offstage { get; set; } = [];

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public EventCallback<DirectorHint> OnDirect { get; set; }

    private void FromMenu(DirectorHint hint) => _ = InvokeAsync(() => OnDirect.InvokeAsync(hint));

    private async Task MoveSceneAsync()
    {
        var dialog = await Dialogs.ShowAsync<MoveSceneDialog>("Move the scene", DialogServiceExtensions.Options with { MaxWidth = MaxWidth.ExtraSmall });
        if (await dialog.Result is { Canceled: false, Data: string place })
        {
            await OnDirect.InvokeAsync(new DirectorHint.MoveTo(place));
        }
    }
}
