using Microsoft.AspNetCore.Components;
using RoleplayStudio.AI.Memory;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Memory;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Chats;

public partial class MemoryDialog
{
    private static readonly Dictionary<string, EditorSection> Fields = [];

    [Inject]
    private MemoryService Memories { get; set; } = null!;

    [Parameter, EditorRequired]
    public Guid SessionId { get; set; }

    [Parameter, EditorRequired]
    public string ChatTitle { get; set; } = "";

    /// <summary>Everyone met in the chat, the only characters a memory can be about.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<Character> Characters { get; set; } = [];

    [Parameter]
    public MemoryEntry? Memory { get; set; }

    protected override MemoryEntry? Original => Memory;

    protected override IReadOnlyDictionary<string, EditorSection> FieldSections => Fields;

    protected override string Noun => "memory";

    protected override string NameOf(MemoryEntry entity) => "this memory";

    protected override void CopyEditableFields(MemoryEntry target, MemoryEntry source)
    {
        target.CopyEditableFieldsFrom(source);
        // A new MemoryEntry stamps CreatedAt with the current time, which would make every dirty-check snapshot differ.
        target.CreatedAt = source.CreatedAt;
    }

    protected override EditProblem? FindProblem(MemoryEntry entity) => entity.FindProblem(Characters.Select(c => c.Id).ToList());

    protected override Task<Result<MemoryEntry>> StoreAsync(MemoryEntry entity, bool isNew) =>
        isNew ? Memories.AddAsync(SessionId, entity) : Memories.UpdateAsync(SessionId, entity);

    private void Toggle(Guid characterId)
    {
        if (!Edited.RelatedCharacterIds.Remove(characterId))
        {
            Edited.RelatedCharacterIds.Add(characterId);
        }

        Changed();
    }
}
