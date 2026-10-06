using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using RoleplayStudio.AI.Chat;
using RoleplayStudio.Domain.Chats;
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
    private CancellationTokenSource? _stop;
    private ChatTranscript? _transcript;

    [Inject]
    private ChatSessionService Sessions { get; set; } = null!;

    [Inject]
    private ChatTurnService Turns { get; set; } = null!;

    [Inject]
    private ModelProfileService Profiles { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [Parameter]
    public Guid Id { get; set; }

    private bool CanSend => _session is not null && !_streaming && !string.IsNullOrWhiteSpace(_draft);

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
        _turns.AddRange(_session.Messages.Select(m => new ChatTurnView(m.Role == MessageRole.User, m.SpeakerName, m.Content)));
        _transcript?.RequestScrollToEnd();

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

        _turns.Add(new ChatTurnView(true, sent.Value.SpeakerName, sent.Value.Content));
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
                _turns.Add(new ChatTurnView(false, saved.Value.SpeakerName, saved.Value.Content));
            }
        }

        _streaming = false;
        Snackbar.Report(result);
    }

    private void Stop() => _stop?.Cancel();

    public override void Dispose()
    {
        _stop?.Cancel();
        base.Dispose();
    }

}
