using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace RoleplayStudio.Web.Components.Shared;

public sealed record EditorSection(string Id, string Title, string ShortTitle);

/// <summary>The one layout for the long authoring forms: a section nav, one scroller and a footer that asks before unsaved edits are lost.</summary>
public partial class EditorSheet : IAsyncDisposable
{
    private ElementReference _root;
    private IJSObjectReference? _module;
    private IJSObjectReference? _attachment;
    private DotNetObjectReference<EditorSheet>? _self;
    private bool _closing;

    [CascadingParameter]
    private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject]
    private IDialogService Dialogs { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    [Parameter, EditorRequired]
    public string Kind { get; set; } = "";

    [Parameter, EditorRequired]
    public string Title { get; set; } = "";

    [Parameter, EditorRequired]
    public IReadOnlyList<EditorSection> Sections { get; set; } = [];

    /// <summary>Asked when the footer renders and again before closing, so it sees edits made in child components.</summary>
    [Parameter, EditorRequired]
    public Func<bool> IsDirty { get; set; } = () => false;

    [Parameter, EditorRequired]
    public string DiscardMessage { get; set; } = "";

    [Parameter]
    public string? ProblemSection { get; set; }

    [Parameter]
    public bool Saving { get; set; }

    [Parameter]
    public EventCallback OnSave { get; set; }

    [Parameter]
    public RenderFragment? Portrait { get; set; }

    /// <summary>Stays above the scroller, like a search field over a list.</summary>
    [Parameter]
    public RenderFragment? Toolbar { get; set; }

    /// <summary>Replaces the footer's "Unsaved changes" line.</summary>
    [Parameter]
    public RenderFragment? Status { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [JSInvokable]
    public async Task RequestCloseAsync()
    {
        if (_closing || Saving)
        {
            return;
        }

        _closing = true;
        try
        {
            if (!IsDirty() || await Dialogs.ConfirmDiscardAsync(DiscardMessage))
            {
                Dialog.Cancel();
            }
        }
        finally
        {
            _closing = false;
        }
    }

    public async Task ShowFieldAsync(string field)
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("showField", _root, field);
        }
    }

    private async Task ShowSectionAsync(string id)
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("showSection", _root, id);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _self = DotNetObjectReference.Create(this);
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/editor-sheet.js");
            _attachment = await _module.InvokeAsync<IJSObjectReference>("attach", _root, _self);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_attachment is not null)
            {
                await _attachment.InvokeVoidAsync("dispose");
                await _attachment.DisposeAsync();
            }

            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }

        _self?.Dispose();
    }
}
