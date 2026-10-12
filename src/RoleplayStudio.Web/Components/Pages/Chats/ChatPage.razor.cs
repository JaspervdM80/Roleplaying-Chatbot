using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using RoleplayStudio.AI.Chat;
using RoleplayStudio.AI.Images;
using RoleplayStudio.AI.Scene;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Media;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Chats;

public partial class ChatPage
{
    private static readonly TimeSpan RenderThrottle = TimeSpan.FromMilliseconds(50);

    private readonly List<ChatTurnView> _turns = [];
    private ChatSession? _session;
    private IReadOnlyList<ModelProfile> _chatModels = [];
    private string? _draft;
    private bool _streaming;
    private bool _sceneToggled;
    private CancellationTokenSource? _stop;
    private ChatTranscript? _transcript;
    private ChatPictures _pictures = null!;
    private IReadOnlyList<(Guid Id, string Name)> _subjects = [];
    private IReadOnlyDictionary<Guid, Guid?> _portraits = new Dictionary<Guid, Guid?>();

    [Inject]
    private ChatSessionService Sessions { get; set; } = null!;

    [Inject]
    private ChatTurnService Turns { get; set; } = null!;

    [Inject]
    private ModelProfileService Profiles { get; set; } = null!;

    [Inject]
    private SceneNotifier SceneNotifier { get; set; } = null!;

    [Inject]
    private ImageService Images { get; set; } = null!;

    [Inject]
    private PictureService Pictures { get; set; } = null!;

    [Inject]
    private PictureNotifier PictureNotifier { get; set; } = null!;

    [Inject]
    private IDialogService Dialogs { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [Parameter]
    public Guid Id { get; set; }

    private IReadOnlyList<Character> _cast = [];

    private bool CanSend => _session is not null && !_streaming && !string.IsNullOrWhiteSpace(_draft);

    private bool CanDirect => _session is not null && !_streaming;

    private IReadOnlyList<Character> Offstage => _cast.Where(c => _subjects.All(s => s.Id != c.Id)).ToList();

    protected override void OnInitialized()
    {
        _pictures = new ChatPictures(Images, Pictures);
        SceneNotifier.Changed += OnSceneChanged;
        PictureNotifier.Changed += OnPictureChanged;
    }

    protected override async Task OnParametersSetAsync()
    {
        var result = await LoadChatAsync();
        if (result.IsCancelled)
        {
            return;
        }

        if (!Snackbar.Report(result))
        {
            Navigation.NavigateTo(AppRoutes.Chats, replace: true);
            return;
        }

        var profiles = await Profiles.ListAsync(Cancellation);
        if (profiles.IsSuccess)
        {
            _chatModels = profiles.Value.Where(p => p.IsChatModel).ToList();
        }
    }

    private async Task<Result> LoadChatAsync()
    {
        var result = await Sessions.GetAsync(Id, Cancellation);
        if (result.IsFailure)
        {
            return result;
        }

        _session = result.Value;
        _turns.Clear();
        _turns.AddRange(_session.Messages.Select(ChatTurnView.Of));
        _transcript?.RequestScrollToEnd();
        ShowCast(_session);
        await LoadCastAsync();
        await _pictures.LoadAsync(Id, Cancellation);
        return result;
    }

    private async Task SetModelAsync(Guid? profileId)
    {
        if (_session is not null && Snackbar.Report(await Sessions.SetModelAsync(Id, profileId)))
        {
            _session.ChatModelProfileId = profileId;
        }
    }

    private async Task OnDraftKeyDown(KeyboardEventArgs e)
    {
        if (e is { Key: "Enter", ShiftKey: false })
        {
            await SendAsync();
        }
    }

    private async Task SendAsync()
    {
        if (CanSend && await SendDraftAsync())
        {
            await StreamReplyAsync();
        }
    }

    private async Task<bool> SendDraftAsync()
    {
        var text = _draft!.Trim();
        _draft = null;
        var sent = await WhileStreamingAsync(() => Sessions.AddUserMessageAsync(Id, text));
        if (!Snackbar.Report(sent))
        {
            _draft = text;
            return false;
        }

        _turns.Add(ChatTurnView.Of(sent.Value));
        _transcript?.RequestScrollToEnd();
        return true;
    }

    /// <summary>Sends what the user has written first, so the hint steers the answer to it.</summary>
    private async Task DirectAsync(DirectorHint hint)
    {
        if (!CanDirect || (!string.IsNullOrWhiteSpace(_draft) && !await SendDraftAsync()))
        {
            return;
        }

        var directed = await WhileStreamingAsync(() => Turns.DirectAsync(Id, hint));
        if (!Snackbar.Report(directed))
        {
            return;
        }

        await ReloadSceneAsync();
        _transcript?.RequestScrollToEnd();
        await StreamReplyAsync(hint);
    }

    // A menu item awaits its handler before the menu closes, which would keep it open for the whole reply.
    private void FromMenu(Func<Task> action) => _ = InvokeAsync(action);

    private async Task EditAsync(ChatTurnView turn)
    {
        if (!CanDirect || turn.MessageId is not { } messageId)
        {
            return;
        }

        var parameters = new DialogParameters<EditMessageDialog> { { d => d.Text, turn.Text } };
        var dialog = await Dialogs.ShowAsync<EditMessageDialog>("Edit your message", parameters, DialogServiceExtensions.Options);
        if (await dialog.Result is { Canceled: false, Data: string text })
        {
            await RewriteAsync(() => Turns.EditAsync(Id, messageId, text));
        }
    }

    private async Task RegenerateAsync()
    {
        if (CanDirect)
        {
            await RewriteAsync(() => Turns.TakeBackReplyAsync(Id));
        }
    }

    /// <summary>Rewrites the chat's past, shows it as it now stands, and streams the reply that follows.</summary>
    private async Task RewriteAsync(Func<Task<Result>> rewrite)
    {
        var rewritten = await WhileStreamingAsync(rewrite);
        if (Snackbar.Report(rewritten) && Snackbar.Report(await LoadChatAsync()))
        {
            await StreamReplyAsync();
        }
    }

    private async Task BranchAsync(ChatTurnView turn)
    {
        if (CanDirect && turn.MessageId is { } messageId)
        {
            var branched = await Turns.BranchAsync(Id, messageId);
            if (Snackbar.Report(branched, "Branched into a new chat"))
            {
                Navigation.NavigateTo(AppRoutes.Chat(branched.Value.Id));
            }
        }
    }

    private async Task ContinueAsync()
    {
        if (_session is not null && !_streaming)
        {
            _transcript?.RequestScrollToEnd();
            await StreamReplyAsync();
        }
    }

    private Task StreamReplyAsync(DirectorHint? hint = null) => WhileStreamingAsync(() => StreamAndSaveReplyAsync(hint));

    private async Task StreamAndSaveReplyAsync(DirectorHint? hint)
    {
        var reply = new LiveReply(_session!.PresentCharacters(), [.. _session.CharacterStates.Select(s => s.Character)], _turns);
        _stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var sinceRender = Stopwatch.StartNew();

        var result = await Turns.StreamReplyAsync(
            Id,
            text =>
            {
                reply.Append(text);
                if (sinceRender.Elapsed >= RenderThrottle)
                {
                    sinceRender.Restart();
                    reply.Show();
                    StateHasChanged();
                }
            },
            hint,
            _stop.Token);

        _stop.Dispose();
        _stop = null;
        reply.Remove();
        if (reply.Text.Trim().Length > 0)
        {
            // Saved even when stopped or the user left: what was streamed is what they saw.
            var saved = await Turns.SaveReplyAsync(Id, reply.Text);
            if (Snackbar.Report(saved))
            {
                _turns.AddRange(saved.Value.Select(ChatTurnView.Of));
            }
        }

        Snackbar.Report(result);
    }

    /// <summary>Keeps the send box and turn actions off while <paramref name="work"/> runs, and turns them back on even when it throws.</summary>
    private Task WhileStreamingAsync(Func<Task> work) => WhileStreamingAsync(async () =>
    {
        await work();
        return true;
    });

    private async Task<T> WhileStreamingAsync<T>(Func<Task<T>> work)
    {
        _streaming = true;
        try
        {
            return await work();
        }
        finally
        {
            _streaming = false;
        }
    }

    private void Stop() => _stop?.Cancel();

    private void ToggleScene() => _sceneToggled = !_sceneToggled;

    private void OnSceneChanged(Guid sessionId)
    {
        if (sessionId == Id)
        {
            _ = InvokeAsync(ReloadSceneAsync);
        }
    }

    private async Task ReloadSceneAsync()
    {
        var result = await Sessions.GetSceneAsync(Id, Cancellation);
        if (result.IsSuccess && _session?.Id == result.Value.Id)
        {
            _session.Scene = result.Value.Scene;
            _session.CharacterStates = result.Value.CharacterStates;
            ShowCast(_session);
            await LoadCastAsync();
            var names = _session.CharacterStates.ToDictionary(s => s.CharacterId, s => s.Character.Name);
            foreach (var turn in _turns)
            {
                if (turn.SpeakerId is { } speakerId && names.TryGetValue(speakerId, out var name))
                {
                    turn.Speaker = name;
                }
            }

            StateHasChanged();
        }
    }

    // Scene tracking can add a newcomer to the cast, so the cast is read again with the scene.
    private async Task LoadCastAsync()
    {
        var cast = await Sessions.ListCastAsync(Id, Cancellation);
        if (cast.IsSuccess)
        {
            _cast = cast.Value;
        }
    }

    private void ShowCast(ChatSession session)
    {
        _subjects = session.PresentCharacters().Select(c => (c.Id, c.Name)).ToList();
        _portraits = session.CharacterStates.ToDictionary(s => s.CharacterId, s => s.Character.ReferenceImageId);
    }

    private async Task PictureAsync(ChatTurnView turn, Guid? characterId)
    {
        if (turn.MessageId is { } messageId)
        {
            Snackbar.Report(await _pictures.PictureAsync(Id, messageId, characterId, Cancellation));
        }
    }

    private async Task CancelPictureAsync(PendingPicture pending) => Snackbar.Report(await _pictures.CancelAsync(Id, pending, Cancellation));

    private async Task OpenAsync(GeneratedImage picture)
    {
        if (_session is null)
        {
            return;
        }

        var names = _session.CharacterStates.ToDictionary(s => s.CharacterId, s => s.Character.Name);
        var references = _session.CharacterStates.Select(s => s.Character.ReferenceImageId).OfType<Guid>().ToHashSet();
        var pictures = _pictures.Drawn.OrderBy(p => p.CreatedAt).ToList();
        await Dialogs.ShowPicturesAsync(pictures, pictures.IndexOf(picture), names, references);
        await _pictures.LoadAsync(Id, Cancellation);
        await ReloadSceneAsync();
    }

    private void OnPictureChanged(PictureChange change)
    {
        if (change.SessionId == Id)
        {
            _ = InvokeAsync(async () =>
            {
                await _pictures.LoadAsync(Id, Cancellation);
                if (change.Outcome is { } outcome)
                {
                    Snackbar.Report(outcome);
                }

                StateHasChanged();
            });
        }
        else if (change.SessionId is null && change.Outcome is { IsSuccess: true } && change.CharacterId is { } characterId)
        {
            // A portrait drawn for someone met in this chat may be their new avatar.
            _ = InvokeAsync(async () =>
            {
                if (_session?.CharacterStates.Any(s => s.CharacterId == characterId) == true)
                {
                    await ReloadSceneAsync();
                }
            });
        }
    }

    public override void Dispose()
    {
        SceneNotifier.Changed -= OnSceneChanged;
        PictureNotifier.Changed -= OnPictureChanged;
        _stop?.Cancel();
        base.Dispose();
    }
}
