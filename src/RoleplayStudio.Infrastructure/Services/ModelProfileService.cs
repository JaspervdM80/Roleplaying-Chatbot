using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Data;

namespace RoleplayStudio.Infrastructure.Services;

public sealed class ModelProfileService(IDbContextFactory<ApplicationDbContext> dbFactory, ICurrentUser currentUser, ILogger<ModelProfileService> logger)
{
    public Task<Result<IReadOnlyList<ModelProfile>>> ListAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunOwnerAsync<IReadOnlyList<ModelProfile>>(currentUser, logger, "list model profiles", cancellationToken, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId, cancellationToken);
            var profiles = await db.ModelProfiles
                .AsNoTracking()
                .OrderBy(p => p.Role)
                .ThenByDescending(p => p.IsDefault)
                .ThenBy(p => p.Name)
                .ToListAsync(cancellationToken);
            logger.LogDebug("Listed {ProfileCount} model profiles", profiles.Count);
            return Result.Success<IReadOnlyList<ModelProfile>>(profiles);
        });

    public Task<Result<ModelProfile>> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "load the model profile", cancellationToken, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId, cancellationToken);
            var profile = await db.ModelProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
            return profile ?? NotFound(id).To<ModelProfile>();
        });

    public Task<Result<ModelProfile>> CreateAsync(ModelProfile input) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "create the model profile", CancellationToken.None, async ownerId =>
        {
            input.Normalize();
            if (input.FindProblem() is { } problem)
            {
                logger.LogWarning("Refused to create a model profile: {Problem}", problem);
                return Result.Failure<ModelProfile>(problem);
            }

            var profile = new ModelProfile();
            profile.CopyEditableFieldsFrom(input);

            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            await SaveWithSingleDefaultAsync(db, profile, () => db.ModelProfiles.Add(profile));
            logger.LogInformation("Created model profile {ProfileId}", profile.Id);
            return profile;
        });

    public Task<Result<ModelProfile>> UpdateAsync(ModelProfile input) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "save the model profile", CancellationToken.None, async ownerId =>
        {
            input.Normalize();
            if (input.FindProblem() is { } problem)
            {
                logger.LogWarning("Refused to save model profile {ProfileId}: {Problem}", input.Id, problem);
                return Result.Failure<ModelProfile>(problem);
            }

            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var profile = await db.ModelProfiles.SingleOrDefaultAsync(p => p.Id == input.Id);
            if (profile is null)
            {
                return NotFound(input.Id).To<ModelProfile>();
            }

            profile.CopyEditableFieldsFrom(input);
            await SaveWithSingleDefaultAsync(db, profile, () => { });
            logger.LogInformation("Updated model profile {ProfileId}", profile.Id);
            return profile;
        });

    public Task<Result> MakeDefaultAsync(Guid id) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "make the model profile the default", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var profile = await db.ModelProfiles.SingleOrDefaultAsync(p => p.Id == id);
            if (profile is null)
            {
                return NotFound(id);
            }

            profile.IsDefault = true;
            await SaveWithSingleDefaultAsync(db, profile, () => { });
            logger.LogInformation("Made model profile {ProfileId} the default for {Role}", profile.Id, profile.Role);
            return Result.Success();
        });

    public Task<Result> DeleteAsync(Guid id) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "delete the model profile", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var deleted = await db.ModelProfiles.Where(p => p.Id == id).ExecuteDeleteAsync();
            if (deleted == 0)
            {
                return NotFound(id);
            }

            logger.LogInformation("Deleted model profile {ProfileId}", id);
            return Result.Success();
        });

    // The unique index allows one default per role, and Postgres checks it per statement, so the old default is cleared first.
    private static async Task SaveWithSingleDefaultAsync(ApplicationDbContext db, ModelProfile profile, Action stage)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            if (profile.IsDefault)
            {
                await db.ModelProfiles
                    .Where(p => p.Role == profile.Role && p.IsDefault && p.Id != profile.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(p => p.IsDefault, false));
            }

            stage();
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        });
    }

    private Result NotFound(Guid id)
    {
        logger.LogWarning("Model profile {ProfileId} was not found", id);
        return Result.Failure("That model profile no longer exists");
    }
}
