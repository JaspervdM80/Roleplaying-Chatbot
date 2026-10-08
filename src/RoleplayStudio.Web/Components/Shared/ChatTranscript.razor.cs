using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace RoleplayStudio.Web.Components.Shared;

/// <summary>One turn as the transcript shows it; <see cref="MessageId"/> is null until the turn is saved.</summary>
public sealed class ChatTurnView(bool fromUser, string speaker, string text, Guid? messageId = null)
{
    private readonly StringBuilder _text = new(text);

    public Guid? MessageId { get; } = messageId;
    public bool FromUser { get; } = fromUser;
    public string Speaker { get; } = speaker;
    public string Text => _text.ToString();

    public void Append(string text) => _text.Append(text);
}

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

    /// <summary>Shown under each saved turn, such as its pictures.</summary>
    [Parameter]
    public RenderFragment<ChatTurnView>? TurnFooter { get; set; }

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
