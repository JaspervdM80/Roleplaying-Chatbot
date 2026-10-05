using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using RoleplayStudio.AI.Playground;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Playground;

public partial class PlaygroundPage
{
    private static readonly TimeSpan RenderThrottle = TimeSpan.FromMilliseconds(50);

    private readonly List<ChatTurnView> _turns = [];
    private IReadOnlyList<ModelProfile>? _profiles;
    private Guid? _profileId;
    private string _characterName = "Mira";
    private string? _characterDescription;
    private string? _draft;
    private bool _streaming;
    private CancellationTokenSource? _stop;
    private ChatTranscript? _transcript;

    [Inject]
    private ModelProfileService Profiles { get; set; } = null!;

    [Inject]
    private PlaygroundChatService Chat { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

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

        _profiles = result.Value.Where(p => p.IsChatModel).ToList();
        _profileId = _profiles.FirstOrDefault(p => p.Id == RequestedProfileId)?.Id
            ?? _profiles.FirstOrDefault(p => p.IsDefault)?.Id
            ?? _profiles.FirstOrDefault()?.Id;
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

        _turns.Add(new ChatTurnView(true, "You", _draft!.Trim()));
        _draft = null;
        _transcript?.RequestScrollToEnd();
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
        var reply = new ChatTurnView(false, _characterName.Trim(), "");
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
        base.Dispose();
    }

}
