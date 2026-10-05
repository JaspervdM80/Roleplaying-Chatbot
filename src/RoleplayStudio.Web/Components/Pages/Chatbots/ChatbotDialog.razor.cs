using Microsoft.AspNetCore.Components;
using MudBlazor;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Chatbots;

public partial class ChatbotDialog
{
    private Chatbot _chatbot = new();
    private IReadOnlyList<ModelProfile> _chatModels = [];
    private bool _saving;

    [CascadingParameter]
    private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject]
    private ChatbotService Chatbots { get; set; } = null!;

    [Inject]
    private ModelProfileService Profiles { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    /// <summary>The chatbot to edit; it is copied, so cancelling leaves the caller's instance untouched.</summary>
    [Parameter]
    public Chatbot? Chatbot { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Chatbot is not null)
        {
            _chatbot = new Chatbot { Id = Chatbot.Id };
            _chatbot.CopyEditableFieldsFrom(Chatbot);
        }

        var profiles = await Profiles.ListAsync(Cancellation);
        if (Snackbar.Report(profiles))
        {
            _chatModels = profiles.Value.Where(p => p.IsChatModel).ToList();
        }
    }

    private async Task SaveAsync()
    {
        _saving = true;
        var result = Chatbot is null ? await Chatbots.CreateAsync(_chatbot) : await Chatbots.UpdateAsync(_chatbot);
        _saving = false;
        if (Snackbar.Report(result, "Chatbot saved"))
        {
            Dialog.Close(result.Value);
        }
    }
}
