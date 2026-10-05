using Microsoft.AspNetCore.Components;
using MudBlazor;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Chats;

public partial class ChatsPage
{
    private IReadOnlyList<ChatListItem>? _chats;

    [Inject]
    private ChatSessionService Sessions { get; set; } = null!;

    [Inject]
    private IDialogService Dialogs { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var result = await Sessions.ListAsync(Cancellation);
        if (Snackbar.Report(result))
        {
            _chats = result.Value;
        }
    }

    private async Task DeleteAsync(ChatListItem chat)
    {
        if (await Dialogs.ConfirmAsync("Delete chat", $"Delete {chat.Title} and everything said in it?", "Delete")
            && Snackbar.Report(await Sessions.DeleteAsync(chat.Id), "Chat deleted"))
        {
            await LoadAsync();
        }
    }
}
