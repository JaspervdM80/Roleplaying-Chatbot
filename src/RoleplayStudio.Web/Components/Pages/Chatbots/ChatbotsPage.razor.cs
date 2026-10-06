using Microsoft.AspNetCore.Components;
using MudBlazor;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Chatbots;

public partial class ChatbotsPage
{
    private IReadOnlyList<Chatbot>? _chatbots;

    [Inject]
    private ChatbotService Chatbots { get; set; } = null!;

    [Inject]
    private IDialogService Dialogs { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        var result = await Chatbots.ListAsync(Cancellation);
        if (Snackbar.Report(result))
        {
            _chatbots = result.Value;
        }
    }

    private static string CastLine(Chatbot chatbot) => chatbot.Cast.Count switch
    {
        0 => "No cast yet",
        <= 3 => string.Join(", ", chatbot.Cast.Select(m => m.Character.Name).Order()),
        _ => Wording.Plural(chatbot.Cast.Count, "character"),
    };

    private async Task AddAsync()
    {
        var dialog = await Dialogs.ShowAsync<ChatbotDialog>("", DialogServiceExtensions.EditorOptions);
        if (await dialog.Result is { Canceled: false, Data: Chatbot created })
        {
            Navigation.NavigateTo(AppRoutes.Chatbot(created.Id));
        }
    }
}
