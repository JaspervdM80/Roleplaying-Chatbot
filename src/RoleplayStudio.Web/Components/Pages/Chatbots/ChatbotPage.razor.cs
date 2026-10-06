using Microsoft.AspNetCore.Components;
using MudBlazor;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Chatbots;

public partial class ChatbotPage
{
    private Chatbot? _chatbot;

    [Inject]
    private ChatbotService Chatbots { get; set; } = null!;

    [Inject]
    private IDialogService Dialogs { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [Parameter]
    public Guid Id { get; set; }

    private IReadOnlyList<Character> Cast => _chatbot!.Cast.Select(m => m.Character).ToList();

    protected override Task OnParametersSetAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var result = await Chatbots.GetAsync(Id, Cancellation);
        if (result.IsCancelled)
        {
            return;
        }

        if (Snackbar.Report(result))
        {
            _chatbot = result.Value;
        }
        else
        {
            Navigation.NavigateTo(AppRoutes.Chatbots, replace: true);
        }
    }

    private string StartingLine(Scenario scenario)
    {
        var present = scenario.PresentAtStart(Cast);
        return present.Count == 0 ? "Starts with nobody but you" : $"Starts with {string.Join(", ", present.Select(c => c.Name))}";
    }

    private async Task EditAsync()
    {
        var parameters = new DialogParameters<ChatbotDialog> { { d => d.Chatbot, _chatbot } };
        await ShowAndReloadAsync<ChatbotDialog>("", parameters, DialogServiceExtensions.EditorOptions);
    }

    private async Task DeleteAsync()
    {
        if (await Dialogs.ConfirmAsync("Delete chatbot", $"Delete {_chatbot!.Name} and all its scenarios? Your characters stay.", "Delete")
            && Snackbar.Report(await Chatbots.DeleteAsync(Id), "Chatbot deleted"))
        {
            Navigation.NavigateTo(AppRoutes.Chatbots, replace: true);
        }
    }

    private async Task EditCastAsync()
    {
        var parameters = new DialogParameters<CastDialog>
        {
            { d => d.ChatbotId, Id },
            { d => d.ChatbotName, _chatbot!.Name },
            { d => d.Cast, _chatbot.Cast },
        };
        await ShowAndReloadAsync<CastDialog>("", parameters, DialogServiceExtensions.EditorOptions with { MaxWidth = MaxWidth.Small });
    }

    private Task AddScenarioAsync() => OpenScenarioEditorAsync("Add scenario", null);

    private Task EditScenarioAsync(Scenario scenario) => OpenScenarioEditorAsync("Edit scenario", scenario);

    private async Task OpenScenarioEditorAsync(string title, Scenario? scenario)
    {
        var parameters = new DialogParameters<ScenarioDialog>
        {
            { d => d.ChatbotId, Id },
            { d => d.Cast, Cast },
            { d => d.Scenario, scenario },
        };
        await ShowAndReloadAsync<ScenarioDialog>(title, parameters, DialogServiceExtensions.Options);
    }

    private async Task DeleteScenarioAsync(Scenario scenario)
    {
        if (await Dialogs.ConfirmAsync("Delete scenario", $"Delete {scenario.Title}?", "Delete")
            && Snackbar.Report(await Chatbots.DeleteScenarioAsync(Id, scenario.Id), "Scenario deleted"))
        {
            await LoadAsync();
        }
    }

    private async Task StartChatAsync(Scenario scenario)
    {
        var parameters = new DialogParameters<StartChatDialog>
        {
            { d => d.ChatbotId, Id },
            { d => d.ScenarioId, scenario.Id },
        };
        var dialog = await Dialogs.ShowAsync<StartChatDialog>($"Start {scenario.Title}", parameters, DialogServiceExtensions.Options with { MaxWidth = MaxWidth.ExtraSmall });
        if (await dialog.Result is { Canceled: false, Data: Guid sessionId })
        {
            Navigation.NavigateTo(AppRoutes.Chat(sessionId));
        }
    }

    private async Task ShowAndReloadAsync<TDialog>(string title, DialogParameters parameters, DialogOptions options)
        where TDialog : IComponent
    {
        var dialog = await Dialogs.ShowAsync<TDialog>(title, parameters, options);
        if (await dialog.Result is { Canceled: false })
        {
            await LoadAsync();
        }
    }
}
