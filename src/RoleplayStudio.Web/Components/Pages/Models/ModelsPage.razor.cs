using Microsoft.AspNetCore.Components;
using MudBlazor;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Models;

public partial class ModelsPage
{
    private readonly Dictionary<Guid, (bool Ok, string Text, Guid? ImageId)> _checks = [];
    private readonly HashSet<Guid> _testing = [];
    private IReadOnlyList<ModelProfile>? _profiles;

    [Inject]
    private ModelProfileService Profiles { get; set; } = null!;

    [Inject]
    private ModelConnectionService Connections { get; set; } = null!;

    [Inject]
    private IDialogService Dialogs { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var result = await Profiles.ListAsync(Cancellation);
        if (Snackbar.Report(result))
        {
            _profiles = result.Value;
        }
    }

    private static string ProviderLabel(ModelProfile profile) => profile.Provider switch
    {
        ProviderKind.Ollama => "Ollama",
        ProviderKind.OpenAICompatible when Uri.TryCreate(profile.BaseUrl, UriKind.Absolute, out var url) => url.Host,
        _ => profile.Provider.ToString(),
    };

    private Task AddAsync() => OpenEditorAsync("Add model", null);

    private Task EditAsync(ModelProfile profile) => OpenEditorAsync("Edit model", profile);

    private async Task OpenEditorAsync(string title, ModelProfile? profile)
    {
        var parameters = new DialogParameters<ModelProfileDialog> { { d => d.Profile, profile } };
        var dialog = await Dialogs.ShowAsync<ModelProfileDialog>(title, parameters, DialogServiceExtensions.Options);
        var result = await dialog.Result;
        if (result is { Canceled: false })
        {
            if (profile is not null)
            {
                _checks.Remove(profile.Id);
            }

            await LoadAsync();
        }
    }

    private async Task TestAsync(ModelProfile profile)
    {
        _testing.Add(profile.Id);
        _checks.Remove(profile.Id);
        var result = await Connections.TestAsync(profile.Id, Cancellation);
        _testing.Remove(profile.Id);
        if (result.IsCancelled)
        {
            return;
        }

        _checks[profile.Id] = result switch
        {
            { IsSuccess: true, Value.ImageId: { } imageId } => (true, $"Drew a test picture in {result.Value.Latency.TotalSeconds:0.0}s.", imageId),
            { IsSuccess: true } => (true, $"Connected in {result.Value.Latency.TotalSeconds:0.0}s. Reply: “{Shorten(result.Value.Reply)}”", null),
            _ => (false, result.Error ?? "", null),
        };
    }

    private static string Shorten(string text) => text.Length <= 80 ? text : text[..80] + "…";

    private async Task MakeDefaultAsync(ModelProfile profile)
    {
        if (Snackbar.Report(await Profiles.MakeDefaultAsync(profile.Id)))
        {
            await LoadAsync();
        }
    }

    private async Task DeleteAsync(ModelProfile profile)
    {
        if (!await Dialogs.ConfirmAsync("Delete model", $"Delete {profile.Name}? Chatbots and chats that use it lose their model setting.", "Delete"))
        {
            return;
        }

        if (Snackbar.Report(await Profiles.DeleteAsync(profile.Id), "Model deleted"))
        {
            _checks.Remove(profile.Id);
            await LoadAsync();
        }
    }
}
