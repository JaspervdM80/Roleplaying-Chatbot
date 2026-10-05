using MudBlazor;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.Web.Components.Shared;

public static class ResultNotices
{
    /// <summary>Shows a failure, stays quiet on cancellation, and returns whether the result succeeded.</summary>
    public static bool Report(this ISnackbar snackbar, Result result, string? success = null)
    {
        if (result.IsSuccess)
        {
            if (success is not null)
            {
                snackbar.Add(success, Severity.Success);
            }

            return true;
        }

        if (!result.IsCancelled)
        {
            snackbar.Add(result.Error ?? "Something went wrong", Severity.Error);
        }

        return false;
    }
}
