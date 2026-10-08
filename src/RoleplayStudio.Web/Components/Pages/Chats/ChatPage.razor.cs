using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using RoleplayStudio.AI.Chat;
using RoleplayStudio.AI.Images;
using RoleplayStudio.AI.Scene;
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
    private IReadOnlyList<GeneratedImage> _pictures = [];
    private IReadOnlyList<PendingPicture> _pending = [];
    private IReadOnlyList<(Guid Id, string Name)> _subjects = [];

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

    private bool CanSend => _session is not null && !_streaming && !string.IsNullOrWhiteSpace(_draft);

    protected override void OnInitialized()
    {
        SceneNotifier.Changed += OnSceneChanged;
        PictureNotifier.Changed += OnPictureChanged;
    }

    protected override async Task OnParametersSetAsync()
    {
        var result = await Sessions.GetAsync(Id, Cancellation);
        if (result.IsCancelled)
        {
            return;
        }

        if (!Snackbar.Report(result))
        {
            Navigation.NavigateTo(AppRoutes.Chats, replace: true);
            return;
        }

        _session = result.Value;
        _turns.Clear();
        _turns.AddRange(_session.Messages.Select(m => new ChatTurnView(m.Role == MessageRole.User, m.SpeakerName, m.Content, m.Id)));
        _transcript?.RequestScrollToEnd();
        _subjects = Subjects(_session);
        await LoadPicturesAsync();

        var profiles = await Profiles.ListAsync(Cancellation);
        if (profiles.IsSuccess)
        {
            _chatModels = profiles.Value.Where(p => p.IsChatModel).ToList();
        }
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
        if (!CanSend)
        {
            return;
        }

        var text = _draft!.Trim();
        _draft = null;
        _streaming = true;
        var sent = await Sessions.AddUserMessageAsync(Id, text);
        _streaming = false;
        if (!Snackbar.Report(sent))
        {
            _draft = text;
            return;
        }

        _turns.Add(new ChatTurnView(true, sent.Value.SpeakerName, sent.Value.Content, sent.Value.Id));
        _transcript?.RequestScrollToEnd();
        await StreamReplyAsync();
    }

    private async Task ContinueAsync()
    {
        if (_session is not null && !_streaming)
        {
            _transcript?.RequestScrollToEnd();
            await StreamReplyAsync();
        }
    }

    private async Task StreamReplyAsync()
    {
        var speaker = ChatSession.ReplySpeaker(_session!.PresentCharacters(_session.CharacterStates)).Name;
        var reply = new ChatTurnView(false, speaker, "");
        _turns.Add(reply);
        _streaming = true;
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
                    StateHasChanged();
                }
            },
            _stop.Token);

        _stop.Dispose();
        _stop = null;
        _turns.Remove(reply);
        if (reply.Text.Trim().Length > 0)
        {
            // Saved even when stopped or the user left: what was streamed is what they saw.
            var saved = await Turns.SaveReplyAsync(Id, reply.Text);
            if (Snackbar.Report(saved))
            {
                _turns.Add(new ChatTurnView(false, saved.Value.SpeakerName, saved.Value.Content, saved.Value.Id));
            }
        }

        _streaming = false;
        Snackbar.Report(result);
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
            _subjects = Subjects(_session);
            StateHasChanged();
        }
    }

    private static IReadOnlyList<(Guid, string)> Subjects(ChatSession session) =>
        session.PresentCharacters(session.CharacterStates).Select(c => (c.Id, c.Name)).ToList();

    private IReadOnlyList<GeneratedImage> PicturesOf(ChatTurnView turn) =>
        _pictures.Where(p => p.MessageId == turn.MessageId).OrderBy(p => p.CreatedAt).ToList();

    private IReadOnlyList<PendingPicture> PendingOf(ChatTurnView turn) => _pending.Where(p => p.MessageId == turn.MessageId).ToList();

    private async Task LoadPicturesAsync()
    {
        var pictures = await Images.ListForChatAsync(Id, Cancellation);
        if (pictures.IsSuccess)
        {
            _pictures = pictures.Value;
        }

        var pending = await Pictures.PendingAsync();
        if (pending.IsSuccess)
        {
            _pending = pending.Value.Where(p => p.SessionId == Id).ToList();
        }
    }

    private async Task PictureAsync(ChatTurnView turn, Guid? characterId)
    {
        if (turn.MessageId is { } messageId && Snackbar.Report(await Pictures.PictureMessageAsync(Id, messageId, characterId)))
        {
            await LoadPicturesAsync();
        }
    }

    private async Task CancelPictureAsync(PendingPicture pending)
    {
        Snackbar.Report(await Pictures.CancelAsync(pending.Id));
        await LoadPicturesAsync();
    }

    private async Task OpenAsync(GeneratedImage picture)
    {
        if (_session is null)
        {
            return;
        }

        var names = _session.CharacterStates.ToDictionary(s => s.CharacterId, s => s.Character.Name);
        var references = _session.CharacterStates.Select(s => s.Character.ReferenceImageId).OfType<Guid>().ToHashSet();
        var pictures = _pictures.OrderBy(p => p.CreatedAt).ToList();
        await Dialogs.ShowPicturesAsync(pictures, pictures.IndexOf(picture), names, references);
        await LoadPicturesAsync();
        await ReloadSceneAsync();
    }

    private void OnPictureChanged(PictureChange change)
    {
        if (change.SessionId == Id)
        {
            _ = InvokeAsync(async () =>
            {
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
        SceneNotifier.Changed -= OnSceneChanged;
        PictureNotifier.Changed -= OnPictureChanged;
        _stop?.Cancel();
        base.Dispose();
    }
}
