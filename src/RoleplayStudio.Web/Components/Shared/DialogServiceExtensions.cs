using MudBlazor;

namespace RoleplayStudio.Web.Components.Shared;

public static class DialogServiceExtensions
{
    public static readonly DialogOptions Options = new() { BackdropClick = false, CloseOnEscapeKey = true, FullWidth = true, MaxWidth = MaxWidth.Small };

    public static async Task<bool> ConfirmAsync(this IDialogService dialogs, string title, string message, string confirmText)
    {
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { d => d.Message, message },
            { d => d.ConfirmText, confirmText },
        };
        var dialog = await dialogs.ShowAsync<ConfirmDialog>(title, parameters, Options with { MaxWidth = MaxWidth.ExtraSmall });
        var result = await dialog.Result;
        return result is { Canceled: false };
    }
}
