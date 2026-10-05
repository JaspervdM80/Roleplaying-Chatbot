using Microsoft.AspNetCore.Components;
using MudBlazor;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Models;

public partial class ModelProfileDialog
{
    private ModelProfile _profile = new();
    private ProviderPreset _preset = ProviderPreset.All[0];
    private bool _saving;

    [CascadingParameter]
    private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject]
    private ModelProfileService Profiles { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    /// <summary>The profile to edit; it is copied, so cancelling leaves the caller's instance untouched.</summary>
    [Parameter]
    public ModelProfile? Profile { get; set; }

    private bool IsNew => Profile is null;

    protected override void OnInitialized()
    {
        if (Profile is null)
        {
            ApplyPreset(_preset);
            return;
        }

        _profile = new ModelProfile { Id = Profile.Id };
        _profile.CopyEditableFieldsFrom(Profile);
    }

    private void ApplyPreset(ProviderPreset preset)
    {
        _preset = preset;
        _profile.Provider = preset.Provider;
        _profile.BaseUrl = preset.BaseUrl;
        _profile.ApiKeySetting = preset.ApiKeySetting;
        if (string.IsNullOrWhiteSpace(_profile.Name) || ProviderPreset.All.Any(p => p.Label == _profile.Name))
        {
            _profile.Name = preset.Label;
        }
    }

    private async Task SaveAsync()
    {
        _saving = true;
        var result = IsNew ? await Profiles.CreateAsync(_profile) : await Profiles.UpdateAsync(_profile);
        _saving = false;
        if (Snackbar.Report(result, "Model saved"))
        {
            Dialog.Close(result.Value);
        }
    }
}
