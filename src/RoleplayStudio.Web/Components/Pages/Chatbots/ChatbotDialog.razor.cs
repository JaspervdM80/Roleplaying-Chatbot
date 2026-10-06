using Microsoft.AspNetCore.Components;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Chatbots;

public partial class ChatbotDialog
{
    private static readonly EditorSection About = new("about", "About", "About");
    private static readonly EditorSection World = new("world", "World and tone", "World");
    private static readonly EditorSection Settings = new("settings", "Model and pictures", "Model");
    private static readonly IReadOnlyList<EditorSection> Sections = [About, World, Settings];

    private static readonly Dictionary<string, EditorSection> Fields = new()
    {
        [nameof(Chatbot.Name)] = About,
    };

    private IReadOnlyList<ModelProfile> _chatModels = [];

    [Inject]
    private ChatbotService Chatbots { get; set; } = null!;

    [Inject]
    private ModelProfileService Profiles { get; set; } = null!;

    [Parameter]
    public Chatbot? Chatbot { get; set; }

    protected override Chatbot? Original => Chatbot;

    protected override IReadOnlyDictionary<string, EditorSection> FieldSections => Fields;

    protected override string Noun => "chatbot";

    protected override string NameOf(Chatbot entity) => entity.Name;

    protected override void CopyEditableFields(Chatbot target, Chatbot source) => target.CopyEditableFieldsFrom(source);

    protected override EditProblem? FindProblem(Chatbot entity) => entity.FindProblem();

    protected override Task<Result<Chatbot>> StoreAsync(Chatbot entity, bool isNew) =>
        isNew ? Chatbots.CreateAsync(entity) : Chatbots.UpdateAsync(entity);

    protected override async Task OnInitializedAsync()
    {
        var profiles = await Profiles.ListAsync(Cancellation);
        if (Snackbar.Report(profiles))
        {
            _chatModels = profiles.Value.Where(p => p.IsChatModel).ToList();
        }
    }
}
