using System.Text.Json;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using RoleplayStudio.Domain;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.Web.Components.Shared;

/// <summary>The state behind an <see cref="EditorSheet"/>: a copy to edit, the dirty check, the field a blocked save points at, and saving.</summary>
public abstract class EntityEditor<T> : CancellableComponent where T : Entity, new()
{
    private string _saved = "";

    [CascadingParameter]
    private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject]
    protected ISnackbar Snackbar { get; set; } = null!;

    protected EditorSheet Sheet { get; set; } = null!;

    protected T Edited { get; private set; } = new();

    protected EditProblem? Problem { get; private set; }

    protected bool Saving { get; private set; }

    /// <summary>The entity being edited, or null for a new one; it is copied, so cancelling leaves it untouched.</summary>
    protected abstract T? Original { get; }

    protected abstract IReadOnlyDictionary<string, EditorSection> FieldSections { get; }

    /// <summary>What the editor calls the entity, in lower case: "persona".</summary>
    protected abstract string Noun { get; }

    protected abstract string NameOf(T entity);

    protected abstract void CopyEditableFields(T target, T source);

    protected abstract EditProblem? FindProblem(T entity);

    protected abstract Task<Result<T>> StoreAsync(T entity, bool isNew);

    protected string Kind => char.ToUpperInvariant(Noun[0]) + Noun[1..];

    protected string Title => Original is null ? $"New {Noun}" : NameOf(Original);

    protected string DiscardMessage => Original is null ? $"This {Noun} hasn't been saved." : $"Your edits to {NameOf(Original)} haven't been saved.";

    protected string? ProblemSection => Problem is null ? null : FieldSections.GetValueOrDefault(Problem.Field)?.Id;

    protected sealed override void OnInitialized()
    {
        if (Original is not null)
        {
            Edited = new T { Id = Original.Id };
            CopyEditableFields(Edited, Original);
        }

        _saved = Snapshot();
    }

    // Compares what a save would store, so a trailing space or a field emptied again is not an edit.
    private string Snapshot()
    {
        var stored = new T { Id = Edited.Id };
        CopyEditableFields(stored, Edited);
        if (stored is OwnedEntity owned)
        {
            owned.CreatedAt = default;
            owned.UpdatedAt = default;
        }

        return JsonSerializer.Serialize(stored);
    }

    protected bool IsDirty() => Snapshot() != _saved;

    protected bool IsProblem(string field) => Problem?.Field == field;

    protected void Changed()
    {
        if (Problem is not null && FindProblem(Edited)?.Field != Problem.Field)
        {
            Problem = null;
        }
    }

    protected async Task SaveAsync()
    {
        Problem = FindProblem(Edited);
        if (Problem is not null)
        {
            await Sheet.ShowFieldAsync(Problem.Field);
            return;
        }

        Saving = true;
        var result = await StoreAsync(Edited, Original is null);
        Saving = false;
        if (Snackbar.Report(result, $"{Kind} saved"))
        {
            Dialog.Close(result.Value);
        }
    }
}
