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
            if (await FindForeignLinkAsync(db, input) is { } problem)
            {
                logger.LogWarning("Refused to save an image: {Problem}", problem);
                return Result.Failure<GeneratedImage>(problem);
            }

            var image = new GeneratedImage
            {
                SessionId = input.SessionId,
                CharacterId = input.CharacterId,
                MessageId = input.MessageId,
                Prompt = input.Prompt,
                NegativePrompt = input.NegativePrompt,
                SourceMemoryIds = [.. input.SourceMemoryIds],
                Provider = input.Provider,
                Model = input.Model,
                Seed = input.Seed,
                ContentType = input.ContentType,
                Width = input.Width,
                Height = input.Height,
            };
            image.StoragePath = $"{ownerId}/{image.Id:N}{Extension(image.ContentType)}";

            await store.SaveAsync(image.StoragePath, data);
            try
            {
                db.Images.Add(image);
                await db.SaveChangesAsync();
            }
            catch
            {
                store.Delete(image.StoragePath);
                throw;
            }

            logger.LogInformation("Saved image {ImageId} from {Provider}", image.Id, image.Provider);
            return Result.Success(image);
        });

    public Task<Result<ImageFile>> OpenAsync(Guid id, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "load the image", cancellationToken, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId, cancellationToken);
            var image = await db.Images.AsNoTracking().Where(i => i.Id == id).Select(i => new { i.StoragePath, i.ContentType }).SingleOrDefaultAsync(cancellationToken);
            if (image is null || store.OpenRead(image.StoragePath) is not { } content)
            {
                logger.LogWarning("Image {ImageId} was not found", id);
                return Result.Failure<ImageFile>("That image no longer exists");
            }

            return Result.Success(new ImageFile(content, image.ContentType));
        });

    private static async Task<string?> FindForeignLinkAsync(ApplicationDbContext db, GeneratedImage image)
    {
        if (image.SessionId is { } sessionId && !await db.ChatSessions.AnyAsync(s => s.Id == sessionId))
        {
            return "That chat no longer exists";
        }

        if (image.MessageId is { } messageId && !await db.ChatSessions.AnyAsync(s => s.Id == image.SessionId && s.Messages.Any(m => m.Id == messageId)))
        {
            return "That message no longer exists";
        }

        if (image.CharacterId is { } characterId && !await db.Characters.AnyAsync(c => c.Id == characterId))
        {
            return "That character no longer exists";
        }

        return null;
    }

    private static string Extension(string contentType) => contentType switch
    {
        "image/webp" => ".webp",
        "image/jpeg" => ".jpg",
        _ => ".png",
    };
}
