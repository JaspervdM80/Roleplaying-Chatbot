using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Images;

/// <summary>Asks for pictures from a page; they are drawn in the background and announced through <see cref="PictureNotifier"/>.</summary>
public sealed class PictureService(IDbContextFactory<ApplicationDbContext> dbFactory, PictureQueue queue, ICurrentUser currentUser, ILogger<PictureService> logger)
{
    public const string NoImageModel = "Add an image model on the models page to draw pictures";
    public const string NoUtilityModel = "Add a utility model on the models page; it writes the picture's prompt";

    /// <summary>Queues a picture of the chat as it was at this message: of one character met in it, or of the whole scene when none is given.</summary>
    public Task<Result<Guid>> PictureMessageAsync(Guid sessionId, Guid messageId, Guid? characterId) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "picture this", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var session = await db.ChatSessions
                .Where(s => s.Id == sessionId)
                .Select(s => new { HasMessage = s.Messages.Any(m => m.Id == messageId), HasCharacter = characterId == null || s.CharacterStates.Any(c => c.CharacterId == characterId) })
                .SingleOrDefaultAsync();
            if (session is null || !session.HasMessage)
            {
                logger.LogWarning("Message {MessageId} of chat {SessionId} was not found", messageId, sessionId);
                return Result.Failure<Guid>("That message no longer exists");
            }

            if (!session.HasCharacter)
            {
                logger.LogWarning("Character {CharacterId} has not been met in chat {SessionId}", characterId, sessionId);
                return Result.Failure<Guid>("That character has not been met in this chat");
            }

            return await EnqueueAsync(db, new PictureJob(ownerId, sessionId, messageId, characterId), needsPrompt: true);
        });

    /// <summary>Queues a portrait of the character; their first one becomes the reference later pictures are drawn after.</summary>
    public Task<Result<Guid>> DrawPortraitAsync(Guid characterId) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "draw a portrait", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            if (!await db.Characters.AnyAsync(c => c.Id == characterId))
            {
                logger.LogWarning("Character {CharacterId} was not found", characterId);
                return Result.Failure<Guid>("That character no longer exists");
            }

            return await EnqueueAsync(db, new PictureJob(ownerId, null, null, characterId), needsPrompt: true);
        });

    /// <summary>Queues the same prompt again with a new seed, filed beside the original.</summary>
    public Task<Result<Guid>> RedrawAsync(Guid imageId) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "draw the picture again", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var image = await db.Images.AsNoTracking().SingleOrDefaultAsync(i => i.Id == imageId);
            if (image is null)
            {
                logger.LogWarning("Image {ImageId} was not found", imageId);
                return Result.Failure<Guid>("That image no longer exists");
            }

            return await EnqueueAsync(db, new PictureJob(ownerId, image.SessionId, image.MessageId, image.CharacterId, image.Id), needsPrompt: false);
        });

    public Task<Result<IReadOnlyList<PendingPicture>>> PendingAsync() =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "list the pictures being drawn", CancellationToken.None, ownerId =>
            Task.FromResult(Result.Success(queue.PendingFor(ownerId))));

    public Task<Result> CancelAsync(Guid id) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "cancel the picture", CancellationToken.None, ownerId =>
        {
            if (queue.Cancel(ownerId, id))
            {
                logger.LogInformation("Cancelled picture {JobId}", id);
            }

            // A picture that finished a moment ago cannot be cancelled; it is already in the gallery.
            return Task.FromResult(Result.Success());
        });

    private async Task<Result<Guid>> EnqueueAsync(ApplicationDbContext db, PictureJob job, bool needsPrompt)
    {
        if (!await db.ModelProfiles.PreferredFor(ModelRole.Image).AnyAsync())
        {
            logger.LogWarning("Refused a picture: no image model");
            return Result.Failure<Guid>(NoImageModel);
        }

        if (needsPrompt && !await db.ModelProfiles.PreferredFor(ModelRole.Utility).AnyAsync())
        {
            logger.LogWarning("Refused a picture: no utility model");
            return Result.Failure<Guid>(NoUtilityModel);
        }

        if (!queue.Enqueue(job))
        {
            return Result.Failure<Guid>("Too many pictures are waiting; try again in a minute");
        }

        logger.LogInformation("Queued picture {JobId}", job.Id);
        return Result.Success(job.Id);
    }
}
