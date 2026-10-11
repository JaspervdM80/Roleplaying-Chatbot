using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using RoleplayStudio.Web.Theming;

namespace RoleplayStudio.Web.Components.Shared;

/// <summary>A saved turn under which the transcript shows a footer; <see cref="EndsTurn"/> is true only for the last model turn before the next user message.</summary>
public readonly record struct TurnFooterContext(ChatTurnView Turn, bool EndsTurn);

/// <summary>The scrolling list of chat turns; it follows new text only while the reader is at the bottom.</summary>
public partial class ChatTranscript
{
    private ElementReference _messageList;
    private IJSObjectReference? _scroll;
    private bool _scrollToEnd = true;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    [Parameter, EditorRequired]
    public IReadOnlyList<ChatTurnView> Turns { get; set; } = [];

    [Parameter]
    public bool Streaming { get; set; }

    /// <summary>Each character's reference portrait by character id, for their avatar.</summary>
    [Parameter]
    public IReadOnlyDictionary<Guid, Guid?> Portraits { get; set; } = new Dictionary<Guid, Guid?>();

    /// <summary>Shown under each saved turn, such as its pictures.</summary>
    [Parameter]
    public RenderFragment<TurnFooterContext>? TurnFooter { get; set; }

    private bool Waiting => Streaming && (Turns.Count == 0 || Turns[^1].MessageId is not null || Turns[^1].Text.Length == 0);

    private bool IsLive(int index) => Streaming && index == Turns.Count - 1 && Turns[index].MessageId is null;

    private bool EndsTurn(int index) => index == Turns.Count - 1 || Turns[index + 1].FromUser;

    private Guid? PortraitOf(ChatTurnView turn) => turn.SpeakerId is { } id ? Portraits.GetValueOrDefault(id) : null;

    private static string SpeakerInk(ChatTurnView turn) =>
        turn.SpeakerId is { } id ? $"--speaker-ink: var({AppTheme.ToneOf(id)})" : "--speaker-ink: var(--ink-muted)";

    /// <summary>Jumps to the newest turn on the next render even if the reader had scrolled up, as after sending.</summary>
    public void RequestScrollToEnd() => _scrollToEnd = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _scroll = await JS.InvokeAsync<IJSObjectReference>("import", Cancellation, "./js/chat-scroll.js");
            await _scroll.InvokeVoidAsync("track", Cancellation, _messageList);
        }

        if (_scroll is not null && Turns.Count > 0)
        {
            await _scroll.InvokeVoidAsync("follow", Cancellation, _messageList, _scrollToEnd);
            _scrollToEnd = false;
        }
    }

    public override void Dispose()
    {
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
}
