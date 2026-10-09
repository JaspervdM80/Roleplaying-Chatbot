using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoleplayStudio.Domain.Media;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Infrastructure.Storage;

namespace RoleplayStudio.Infrastructure.Services;

public sealed record ImageFile(Stream Content, string ContentType);

public sealed class ImageService(IDbContextFactory<ApplicationDbContext> dbFactory, IImageStore store, ICurrentUser currentUser, ILogger<ImageService> logger)
{
    /// <summary>Stores the picture and records it; the session, character and message it names must be the caller's.</summary>
    public Task<Result<GeneratedImage>> SaveAsync(GeneratedImage input, byte[] data) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "save the image", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var saved = await StoreAsync(db, store, ownerId, input, data, CancellationToken.None);
            if (saved.IsSuccess)
            {
                logger.LogInformation("Saved image {ImageId} from {Provider}", saved.Value.Id, saved.Value.Provider);
            }
            else
            {
                logger.LogWarning("Refused to save an image: {Problem}", saved.Error);
            }

            return saved;
        });

    /// <summary>
    /// Writes the file, then the row, in a context already scoped to <paramref name="ownerId"/>; the file is removed if the row fails.
    /// Background jobs, which have no signed-in user, save through this.
    /// </summary>
    public static async Task<Result<GeneratedImage>> StoreAsync(ApplicationDbContext db, IImageStore store, string ownerId, GeneratedImage input, byte[] data, CancellationToken cancellationToken)
    {
        if (await FindForeignLinkAsync(db, input, cancellationToken) is { } problem)
        {
            return Result.Failure<GeneratedImage>(problem);
        }

        var image = new GeneratedImage
        {
            SessionId = input.SessionId,
            CharacterId = input.CharacterId,
            MessageId = input.MessageId,
            Prompt = input.Prompt,
            Caption = input.Caption,
            NegativePrompt = input.NegativePrompt,
            SourceMemoryIds = [.. input.SourceMemoryIds],
            ReferenceImageIds = [.. input.ReferenceImageIds],
            Provider = input.Provider,
            Model = input.Model,
            Seed = input.Seed,
            ContentType = input.ContentType,
            Width = input.Width,
            Height = input.Height,
            CreatedAt = input.CreatedAt,
        };
        image.StoragePath = $"{ownerId}/{image.Id:N}{ExtensionFor(image.ContentType)}";

        await store.SaveAsync(image.StoragePath, data, cancellationToken);
        try
        {
            db.Images.Add(image);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            store.Delete(image.StoragePath);
            throw;
        }

        return Result.Success(image);
    }

    public Task<Result<ImageFile>> OpenAsync(Guid id, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "load the image", cancellationToken, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId, cancellationToken);
            var image = await db.Images.AsNoTracking().Where(i => i.Id == id).Select(i => new { i.StoragePath, i.ContentType }).SingleOrDefaultAsync(cancellationToken);
            if (image is null || store.OpenRead(image.StoragePath) is not { } content)
            {
                return NotFound(id).To<ImageFile>();
            }

            return Result.Success(new ImageFile(content, image.ContentType));
        });

    public Task<Result<GeneratedImage>> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "load the image", cancellationToken, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId, cancellationToken);
            var image = await db.Images.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
            return image is null ? NotFound(id).To<GeneratedImage>() : Result.Success(image);
        });

    /// <summary>The chat's pictures, newest first.</summary>
    public Task<Result<IReadOnlyList<GeneratedImage>>> ListForChatAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        ListAsync(i => i.SessionId == sessionId, cancellationToken);

    /// <summary>The character's portraits and the chat pictures drawn of them, newest first.</summary>
    public Task<Result<IReadOnlyList<GeneratedImage>>> ListForCharacterAsync(Guid characterId, CancellationToken cancellationToken = default) =>
        ListAsync(i => i.CharacterId == characterId, cancellationToken);

    /// <summary>Makes the picture the reference portrait of the character it shows, and adopts its seed when the character has none.</summary>
    public Task<Result> SetReferenceAsync(Guid id) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "set the reference portrait", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var image = await db.Images.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id);
            if (image is null)
            {
                return NotFound(id);
            }

            var character = image.CharacterId is { } characterId ? await db.Characters.SingleOrDefaultAsync(c => c.Id == characterId) : null;
            if (character is null)
            {
                logger.LogWarning("Image {ImageId} shows no character of the caller's", id);
                return Result.Failure("That picture is not of one character");
            }

            character.ReferenceImageId = image.Id;
            character.ImageSeed ??= image.Seed;
            await db.SaveChangesAsync();
            logger.LogInformation("Set image {ImageId} as the reference of character {CharacterId}", id, character.Id);
            return Result.Success();
        });

    /// <summary>Deletes the picture and its file; a character it was the reference of goes back to having none.</summary>
    public Task<Result> DeleteAsync(Guid id) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "delete the image", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var image = await db.Images.SingleOrDefaultAsync(i => i.Id == id);
            if (image is null)
            {
                return NotFound(id);
            }

            foreach (var character in await db.Characters.Where(c => c.ReferenceImageId == id).ToListAsync())
            {
                character.ReferenceImageId = null;
            }

            foreach (var persona in await db.Personas.Where(p => p.AvatarImageId == id).ToListAsync())
            {
                persona.AvatarImageId = null;
            }

            db.Images.Remove(image);
            await db.SaveChangesAsync();
            store.Delete(image.StoragePath);
            logger.LogInformation("Deleted image {ImageId}", id);
            return Result.Success();
        });

    private Task<Result<IReadOnlyList<GeneratedImage>>> ListAsync(System.Linq.Expressions.Expression<Func<GeneratedImage, bool>> filter, CancellationToken cancellationToken) =>
        ServiceOperation.RunOwnerAsync<IReadOnlyList<GeneratedImage>>(currentUser, logger, "list the pictures", cancellationToken, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId, cancellationToken);
            var images = await db.Images.Where(filter).OrderByDescending(i => i.CreatedAt).AsNoTracking().ToListAsync(cancellationToken);
            logger.LogDebug("Listed {ImageCount} pictures", images.Count);
            return Result.Success<IReadOnlyList<GeneratedImage>>(images);
        });

    private static async Task<string?> FindForeignLinkAsync(ApplicationDbContext db, GeneratedImage image, CancellationToken cancellationToken)
    {
        if (image.SessionId is { } sessionId && !await db.ChatSessions.AnyAsync(s => s.Id == sessionId, cancellationToken))
        {
            return "That chat no longer exists";
        }

        if (image.MessageId is { } messageId && !await db.ChatSessions.AnyAsync(s => s.Id == image.SessionId && s.Messages.Any(m => m.Id == messageId), cancellationToken))
        {
            return "That message no longer exists";
        }

        if (image.CharacterId is { } characterId && !await db.Characters.AnyAsync(c => c.Id == characterId, cancellationToken))
        {
            return "That character no longer exists";
        }

        return null;
    }

    private Result NotFound(Guid id)
    {
        logger.LogWarning("Image {ImageId} was not found", id);
        return Result.Failure("That image no longer exists");
    }

    public static string ExtensionFor(string contentType) => contentType switch
    {
        "image/webp" => ".webp",
        "image/jpeg" => ".jpg",
        _ => ".png",
    };
}
