using MudBlazor;

namespace RoleplayStudio.Web.Components.Shared;

public static class DialogServiceExtensions
{
    public static readonly DialogOptions Options = new() { BackdropClick = false, CloseOnEscapeKey = true, FullWidth = true, MaxWidth = MaxWidth.Small };

    // EditorSheet draws its own header and handles Escape itself, so it can ask before unsaved edits are lost.
    public static readonly DialogOptions EditorOptions = Options with { NoHeader = true, CloseOnEscapeKey = false, MaxWidth = MaxWidth.Medium };

    public static async Task<bool> ConfirmAsync(this IDialogService dialogs, string title, string message, string confirmText)
    {
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { d => d.Message, message },
            { d => d.ConfirmText, confirmText },
        };
        return await ShowConfirmAsync(dialogs, title, parameters);
    }

    public static async Task<bool> ConfirmDiscardAsync(this IDialogService dialogs, string message)
    {
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { d => d.Message, message },
            { d => d.ConfirmText, "Discard" },
            { d => d.CancelText, "Keep editing" },
            { d => d.CancelIsMain, true },
        };
        return await ShowConfirmAsync(dialogs, "Discard your changes?", parameters);
    }

    private static async Task<bool> ShowConfirmAsync(IDialogService dialogs, string title, DialogParameters<ConfirmDialog> parameters)
    {
        var dialog = await dialogs.ShowAsync<ConfirmDialog>(title, parameters, Options with { MaxWidth = MaxWidth.ExtraSmall });
        var result = await dialog.Result;
        return result is { Canceled: false };
    }
}
