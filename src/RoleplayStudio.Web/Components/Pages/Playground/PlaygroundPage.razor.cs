using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;
using RoleplayStudio.AI.Playground;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Playground;

public partial class PlaygroundPage
{
    private static readonly TimeSpan RenderThrottle = TimeSpan.FromMilliseconds(50);

    private readonly List<Turn> _turns = [];
    private IReadOnlyList<ModelProfile>? _profiles;
    private Guid? _profileId;
    private string _characterName = "Mira";
    private string? _characterDescription;
    private string? _draft;
    private bool _streaming;
    private bool _scrollToEnd;
    private CancellationTokenSource? _stop;
    private ElementReference _messageList;
    private IJSObjectReference? _scroll;

    [Inject]
    private ModelProfileService Profiles { get; set; } = null!;

    [Inject]
    private PlaygroundChatService Chat { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "profile")]
    public Guid? RequestedProfileId { get; set; }

    private bool CanSend => _profileId is not null && !string.IsNullOrWhiteSpace(_draft) && !string.IsNullOrWhiteSpace(_characterName);

    private string SetupTitle => _profiles?.FirstOrDefault(p => p.Id == _profileId) is { } profile
        ? $"{_characterName} · {profile.Name}"
        : "Set up the character";

    protected override async Task OnInitializedAsync()
    {
        var result = await Profiles.ListAsync(Cancellation);
        if (!Snackbar.Report(result))
        {
            return;
        }

        _profiles = result.Value.Where(p => p.Role == ModelRole.Chat && ModelProfile.CanChat(p.Provider)).ToList();
        _profileId = _profiles.FirstOrDefault(p => p.Id == RequestedProfileId)?.Id
            ?? _profiles.FirstOrDefault(p => p.IsDefault)?.Id
            ?? _profiles.FirstOrDefault()?.Id;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _scroll = await JS.InvokeAsync<IJSObjectReference>("import", Cancellation, "./js/chat-scroll.js");
            await _scroll.InvokeVoidAsync("track", Cancellation, _messageList);
        }

        if (_scroll is not null && _turns.Count > 0)
        {
            await _scroll.InvokeVoidAsync("follow", Cancellation, _messageList, _scrollToEnd);
            _scrollToEnd = false;
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
        if (!CanSend || _streaming)
        {
            return;
        }

        _turns.Add(new Turn(true, "", _draft!.Trim()));
        _draft = null;
        _scrollToEnd = true;
        await StreamReplyAsync();
    }

    private async Task RetryAsync()
    {
        if (_turns.Count > 0 && !_turns[^1].FromUser)
        {
            _turns.RemoveAt(_turns.Count - 1);
        }

        if (_turns.Count > 0 && _profileId is not null)
        {
            await StreamReplyAsync();
        }
    }

    private async Task StreamReplyAsync()
    {
        var history = _turns.Select(t => new PlaygroundTurn(t.FromUser, t.Text)).ToList();
        var reply = new Turn(false, _characterName.Trim(), "");
        _turns.Add(reply);
        _streaming = true;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var sinceRender = Stopwatch.StartNew();

        var result = await Chat.StreamReplyAsync(
            _profileId!.Value,
            new PlaygroundCharacter(_characterName, _characterDescription),
            history,
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

        _streaming = false;
        _stop.Dispose();
        _stop = null;
        if (reply.Text.Length == 0)
        {
            _turns.Remove(reply);
        }

        Snackbar.Report(result);
    }

    private void Stop() => _stop?.Cancel();

    private void Clear() => _turns.Clear();

    public override void Dispose()
    {
        _stop?.Cancel();
        if (_scroll is not null)
        {
            _ = DisposeScrollAsync(_scroll);
        }

        base.Dispose();
    }

    private static async Task DisposeScrollAsync(IJSObjectReference scroll)
    {
        try
        {
            await scroll.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
    }

    private sealed class Turn(bool fromUser, string speaker, string text)
    {
        private readonly StringBuilder _text = new(text);

        public bool FromUser { get; } = fromUser;
        public string Speaker { get; } = speaker;
        public string Text => _text.ToString();

        public void Append(string text) => _text.Append(text);
    }
}
