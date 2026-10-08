using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Infrastructure.Data;

namespace RoleplayStudio.Infrastructure.Services;

public sealed class CharacterService(IDbContextFactory<ApplicationDbContext> dbFactory, ICurrentUser currentUser, ILogger<CharacterService> logger)
{
    public Task<Result<IReadOnlyList<Character>>> ListAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunOwnerAsync<IReadOnlyList<Character>>(currentUser, logger, "list characters", cancellationToken, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId, cancellationToken);
            var characters = await db.Characters.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);
            logger.LogDebug("Listed {CharacterCount} characters", characters.Count);
            return Result.Success<IReadOnlyList<Character>>(characters);
        });

    public Task<Result<Character>> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "load the character", cancellationToken, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId, cancellationToken);
            var character = await db.Characters.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            return character ?? NotFound(id).To<Character>();
        });

    public Task<Result<Character>> CreateAsync(Character input) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "create the character", CancellationToken.None, async ownerId =>
        {
            var character = new Character();
            character.CopyEditableFieldsFrom(input);
            if (character.FindProblem() is { } problem)
            {
                logger.LogWarning("Refused to create a character: {Problem}", problem.Message);
                return Result.Failure<Character>(problem.Message);
            }

            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            db.Characters.Add(character);
            await db.SaveChangesAsync();
            logger.LogInformation("Created character {CharacterId}", character.Id);
            return character;
        });

    public Task<Result<Character>> UpdateAsync(Character input) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "save the character", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var character = await db.Characters.SingleOrDefaultAsync(c => c.Id == input.Id);
            if (character is null)
            {
                return NotFound(input.Id).To<Character>();
            }

            character.CopyEditableFieldsFrom(input);
            if (character.FindProblem() is { } problem)
            {
                logger.LogWarning("Refused to save character {CharacterId}: {Problem}", input.Id, problem.Message);
                return Result.Failure<Character>(problem.Message);
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Updated character {CharacterId}", character.Id);
            return character;
        });

    /// <summary>Refused while a chat still has the character in it; otherwise it also leaves every cast and starting line-up.</summary>
    public Task<Result> DeleteAsync(Guid id) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "delete the character", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var character = await db.Characters.SingleOrDefaultAsync(c => c.Id == id);
            if (character is null)
            {
                return NotFound(id);
            }

            var chats = await db.ChatSessions.CountAsync(s => s.CharacterStates.Any(state => state.CharacterId == id));
            if (chats > 0)
            {
                logger.LogWarning("Refused to delete character {CharacterId}: {ChatCount} chats include them", id, chats);
                return Result.Failure("{0} is in {1} chats; delete those first", character.Name, chats);
            }

            var scenarios = await db.Chatbots
                .SelectMany(c => c.Scenarios)
                .Where(s => s.StartingCharacterIds.Contains(id))
                .ToListAsync();
            foreach (var scenario in scenarios)
            {
                scenario.StartingCharacterIds = scenario.StartingCharacterIds.Where(c => c != id).ToList();
            }

            db.Characters.Remove(character);
            await db.SaveChangesAsync();
            logger.LogInformation("Deleted character {CharacterId}", id);
            return Result.Success();
        });

    private Result NotFound(Guid id)
    {
        logger.LogWarning("Character {CharacterId} was not found", id);
        return Result.Failure("That character no longer exists");
    }
}
