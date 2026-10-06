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

    [Inject]
    private TimeProvider Time { get; set; } = null!;

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
        var question = $"Delete your {chat.ScenarioTitle} chat in {chat.ChatbotName}? Everything said in it and everything it remembers goes too.";
        if (await Dialogs.ConfirmAsync("Delete chat", question, "Delete")
            && Snackbar.Report(await Sessions.DeleteAsync(chat.Id), "Chat deleted"))
        {
            await LoadAsync();
        }
    }
}
