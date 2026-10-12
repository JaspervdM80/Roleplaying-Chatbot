using RoleplayStudio.AI.Images;
using RoleplayStudio.Domain.Media;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Chats;

/// <summary>The pictures of the chat a page shows, and those still being drawn for it.</summary>
public sealed class ChatPictures(ImageService images, PictureService pictures)
{
    public IReadOnlyList<GeneratedImage> Drawn { get; private set; } = [];

    public IReadOnlyList<PendingPicture> Pending { get; private set; } = [];

    public IReadOnlyList<GeneratedImage> Of(ChatTurnView turn) =>
        Drawn.Where(p => p.MessageId == turn.MessageId).OrderBy(p => p.CreatedAt).ToList();

    public IReadOnlyList<PendingPicture> PendingOf(ChatTurnView turn) => Pending.Where(p => p.MessageId == turn.MessageId).ToList();

    public async Task LoadAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var drawn = await images.ListForChatAsync(sessionId, cancellationToken);
        if (drawn.IsSuccess)
        {
            Drawn = drawn.Value;
        }

        var pending = await pictures.PendingAsync();
        if (pending.IsSuccess)
        {
            Pending = pending.Value.Where(p => p.SessionId == sessionId).ToList();
        }
    }

    public async Task<Result> PictureAsync(Guid sessionId, Guid messageId, Guid? characterId, CancellationToken cancellationToken)
    {
        Result asked = await pictures.PictureMessageAsync(sessionId, messageId, characterId);
        if (asked.IsSuccess)
        {
            await LoadAsync(sessionId, cancellationToken);
        }

        return asked;
    }

    public async Task<Result> CancelAsync(Guid sessionId, PendingPicture pending, CancellationToken cancellationToken)
    {
        var cancelled = await pictures.CancelAsync(pending.Id);
        await LoadAsync(sessionId, cancellationToken);
        return cancelled;
    }
}
