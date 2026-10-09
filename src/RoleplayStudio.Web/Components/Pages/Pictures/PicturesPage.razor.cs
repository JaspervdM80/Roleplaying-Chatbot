using Microsoft.AspNetCore.Components;
using MudBlazor;
using RoleplayStudio.AI.Images;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Media;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Pages.Characters;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Pictures;

/// <summary>Every picture of one character, or every picture in one chat.</summary>
public partial class PicturesPage
{
    private sealed record PictureFilter(string Label, Func<GeneratedImage, bool> Matches);

    private static readonly PictureFilter All = new("All", _ => true);

    private IReadOnlyList<GeneratedImage>? _pictures;
    private IReadOnlyList<PendingPicture> _pending = [];
    private IReadOnlyList<PictureFilter> _filters = [All];
    private PictureFilter _filter = All;
    private Dictionary<Guid, string> _names = [];
    private HashSet<Guid> _references = [];
    private Character? _character;
    private string? _title;
    private string? _subtitle;

    [Inject]
    private ImageService Images { get; set; } = null!;

    [Inject]
    private PictureService PictureRequests { get; set; } = null!;

    [Inject]
    private PictureNotifier Notifier { get; set; } = null!;

    [Inject]
    private CharacterService Characters { get; set; } = null!;

    [Inject]
    private ChatSessionService Sessions { get; set; } = null!;

    [Inject]
    private IDialogService Dialogs { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [Parameter]
    public Guid? CharacterId { get; set; }

    [Parameter]
    public Guid? SessionId { get; set; }

    private IReadOnlyList<GeneratedImage> Shown => _pictures?.Where(_filter.Matches).ToList() ?? [];

    private string BackUrl => SessionId is { } sessionId ? AppRoutes.Chat(sessionId) : AppRoutes.Characters;

    private string BackLabel => SessionId is null ? "Back to characters" : "Back to the chat";

    protected override void OnInitialized() => Notifier.Changed += OnPictureChanged;

    protected override async Task OnParametersSetAsync()
    {
        _filter = All;
        var loaded = SessionId is { } sessionId ? await LoadChatAsync(sessionId) : await LoadCharacterAsync(CharacterId!.Value);
        if (loaded.IsCancelled)
        {
            return;
        }

        if (!Snackbar.Report(loaded))
        {
            Navigation.NavigateTo(BackUrl, replace: true);
            return;
        }

        await LoadPicturesAsync();
    }

    private async Task<Result> LoadCharacterAsync(Guid characterId)
    {
        var result = await Characters.GetAsync(characterId, Cancellation);
        if (result.IsFailure)
        {
            return result;
        }

        _character = result.Value;
        _title = _character.Name;
        _names = new() { [_character.Id] = _character.Name };
        _references = _character.ReferenceImageId is { } referenceId ? [referenceId] : [];
        _filters = [All, new("Portraits", p => p.IsPortrait), new("From chats", p => !p.IsPortrait)];
        return Result.Success();
    }

    private async Task<Result> LoadChatAsync(Guid sessionId)
    {
        var result = await Sessions.GetSceneAsync(sessionId, Cancellation);
        if (result.IsFailure)
        {
            return result;
        }

        var session = result.Value;
        _character = null;
        _title = $"Pictures · {session.Title}";
        _subtitle = $"{session.Scenario.Chatbot.Name} · {session.Title}";
        var met = session.CharacterStates.Select(s => s.Character).OrderBy(c => c.Name).ToList();
        _names = met.ToDictionary(c => c.Id, c => c.Name);
        _references = met.Select(c => c.ReferenceImageId).OfType<Guid>().ToHashSet();
        _filters = [All, .. met.Select(c => new PictureFilter(c.Name, p => p.CharacterId == c.Id))];
        return Result.Success();
    }

    private async Task LoadPicturesAsync()
    {
        var pictures = SessionId is { } sessionId
            ? await Images.ListForChatAsync(sessionId, Cancellation)
            : await Images.ListForCharacterAsync(CharacterId!.Value, Cancellation);
        if (pictures.IsCancelled || !Snackbar.Report(pictures))
        {
            return;
        }

        _pictures = pictures.Value;
        if (_character is not null)
        {
            _subtitle = $"Pictures · {_pictures.Count}";
        }

        var pending = await PictureRequests.PendingAsync();
        if (pending.IsSuccess)
        {
            _pending = pending.Value.Where(Belongs).ToList();
        }
    }

    private bool Belongs(PendingPicture pending) =>
        SessionId is { } sessionId ? pending.SessionId == sessionId : pending.CharacterId == CharacterId;

    private async Task DrawPortraitAsync()
    {
        if (_character is not null && Snackbar.Report(await PictureRequests.DrawPortraitAsync(_character.Id)))
        {
            await LoadPicturesAsync();
        }
    }

    private async Task EditCharacterAsync()
    {
        var parameters = new DialogParameters<CharacterDialog> { { d => d.Character, _character } };
        var dialog = await Dialogs.ShowAsync<CharacterDialog>("", parameters, DialogServiceExtensions.EditorOptions);
        if (await dialog.Result is { Canceled: false } && CharacterId is { } characterId)
        {
            Snackbar.Report(await LoadCharacterAsync(characterId));
        }
    }

    private async Task OpenAsync(GeneratedImage picture)
    {
        var shown = Shown;
        await Dialogs.ShowPicturesAsync(shown, shown.ToList().IndexOf(picture), _names, _references);
        if (SessionId is { } sessionId)
        {
            await LoadChatAsync(sessionId);
        }
        else
        {
            await LoadCharacterAsync(CharacterId!.Value);
        }

        await LoadPicturesAsync();
    }

    private void OnPictureChanged(PictureChange change)
    {
        if ((SessionId is { } sessionId && change.SessionId == sessionId) || (SessionId is null && change.CharacterId == CharacterId))
        {
            _ = InvokeAsync(async () =>
            {
                if (_character is not null && change.Outcome is { IsSuccess: true })
                {
                    await LoadCharacterAsync(_character.Id);
                }

                await LoadPicturesAsync();
                if (change.Outcome is { } outcome)
                {
                    Snackbar.Report(outcome);
                }

                StateHasChanged();
            });
        }
    }

    public override void Dispose()
    {
        Notifier.Changed -= OnPictureChanged;
        base.Dispose();
    }
}
