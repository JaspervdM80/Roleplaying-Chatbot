using Microsoft.AspNetCore.Components;
using MudBlazor;
using RoleplayStudio.AI.Memory;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Memory;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Chats;

public partial class MemoriesPage
{
    private ChatSession? _session;
    private IReadOnlyList<MemoryEntry>? _memories;
    private IReadOnlyList<Character> _characters = [];
    private string? _search;
    private MemoryType? _type;
    private Guid? _characterId;
    private bool _editingSummary;
    private string? _summaryDraft;
    private long _summaryDraftCovers;
    private bool _rebuilding;

    [Inject]
    private ChatSessionService Sessions { get; set; } = null!;

    [Inject]
    private MemoryService Memories { get; set; } = null!;

    [Inject]
    private MemoryNotifier MemoryNotifier { get; set; } = null!;

    [Inject]
    private IDialogService Dialogs { get; set; } = null!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [Parameter]
    public Guid Id { get; set; }

    private IReadOnlyList<MemoryEntry> Shown => _memories!.Where(m => m.Matches(_search, _type, _characterId)).ToList();

    private string CountLine => Shown.Count == _memories!.Count ? Wording.Plural(_memories.Count, "memory") : $"{Shown.Count} of {_memories.Count}";

    protected override void OnInitialized() => MemoryNotifier.Changed += OnMemoriesChanged;

    protected override async Task OnParametersSetAsync()
    {
        var result = await Sessions.GetSceneAsync(Id, Cancellation);
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
        _characters = _session.CharacterStates.Select(s => s.Character).OrderBy(c => c.Name).ToList();
        await LoadMemoriesAsync();
    }

    private async Task LoadMemoriesAsync()
    {
        var result = await Memories.ListAsync(Id, Cancellation);
        if (Snackbar.Report(result))
        {
            _memories = result.Value;
        }
    }

    private IEnumerable<string> NamesOf(MemoryEntry memory) =>
        _characters.Where(c => memory.RelatedCharacterIds.Contains(c.Id)).Select(c => c.Name);

    private static string SourceLine(MemoryEntry memory) =>
        memory.SourceFromSequence is { } from && memory.SourceToSequence is { } to
            ? from == to ? $"From message {from}" : $"From messages {from} to {to}"
            : "Added by you";

    private Task AddAsync() => OpenEditorAsync(null);

    private Task EditAsync(MemoryEntry memory) => OpenEditorAsync(memory);

    private async Task OpenEditorAsync(MemoryEntry? memory)
    {
        var parameters = new DialogParameters<MemoryDialog>
        {
            { d => d.SessionId, Id },
            { d => d.ChatTitle, _session!.Title },
            { d => d.Characters, _characters },
            { d => d.Memory, memory },
        };
        var dialog = await Dialogs.ShowAsync<MemoryDialog>("", parameters, DialogServiceExtensions.EditorOptions with { MaxWidth = MaxWidth.Small });
        if (await dialog.Result is { Canceled: false })
        {
            await LoadMemoriesAsync();
        }
    }

    private async Task TogglePinAsync(MemoryEntry memory)
    {
        if (Snackbar.Report(await Memories.SetPinnedAsync(Id, memory.Id, !memory.IsPinned)))
        {
            await LoadMemoriesAsync();
        }
    }

    private async Task DeleteAsync(MemoryEntry memory)
    {
        if (await Dialogs.ConfirmAsync("Delete memory", "Forget this memory? The chat will not recall it again.", "Delete")
            && Snackbar.Report(await Memories.DeleteAsync(Id, memory.Id), "Memory deleted"))
        {
            await LoadMemoriesAsync();
        }
    }

    private void EditSummary()
    {
        _summaryDraft = _session!.Summary.Text;
        _summaryDraftCovers = _session.Summary.CoveredUpToSequence;
        _editingSummary = true;
    }

    private void CancelSummary() => _editingSummary = false;

    private async Task SaveSummaryAsync()
    {
        var saved = await Memories.SaveSummaryAsync(Id, _summaryDraft, _summaryDraftCovers);
        if (Snackbar.Report(saved, "Summary saved"))
        {
            _session!.Summary = saved.Value;
            _editingSummary = false;
        }
    }

    private async Task RebuildSummaryAsync()
    {
        var question = "Write the summary again from the whole chat? Your edits to it are lost, and a long chat takes a while.";
        if (await Dialogs.ConfirmAsync("Rebuild summary", question, "Rebuild")
            && Snackbar.Report(await Memories.RebuildSummaryAsync(Id), "Rebuilding the summary in the background"))
        {
            _rebuilding = true;
        }
    }

    private void OnMemoriesChanged(Guid sessionId, bool rebuildEnded)
    {
        if (sessionId == Id)
        {
            _ = InvokeAsync(() => ReloadAsync(rebuildEnded));
        }
    }

    private async Task ReloadAsync(bool rebuildEnded)
    {
        var result = await Sessions.GetSceneAsync(Id, Cancellation);
        if (result.IsSuccess && _session?.Id == result.Value.Id)
        {
            _session.Summary = result.Value.Summary;
        }

        if (rebuildEnded)
        {
            _rebuilding = false;
        }

        await LoadMemoriesAsync();
        StateHasChanged();
    }

    public override void Dispose()
    {
        MemoryNotifier.Changed -= OnMemoriesChanged;
        base.Dispose();
    }
}
