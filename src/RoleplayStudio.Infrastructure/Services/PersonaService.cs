using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Infrastructure.Data;

namespace RoleplayStudio.Infrastructure.Services;

public sealed class PersonaService(IDbContextFactory<ApplicationDbContext> dbFactory, ICurrentUser currentUser, ILogger<PersonaService> logger)
{
    public Task<Result<IReadOnlyList<Persona>>> ListAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunOwnerAsync<IReadOnlyList<Persona>>(currentUser, logger, "list personas", cancellationToken, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId, cancellationToken);
            var personas = await db.Personas.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken);
            logger.LogDebug("Listed {PersonaCount} personas", personas.Count);
            return Result.Success<IReadOnlyList<Persona>>(personas);
        });

    public Task<Result<Persona>> CreateAsync(Persona input) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "create the persona", CancellationToken.None, async ownerId =>
        {
            var persona = new Persona();
            persona.CopyEditableFieldsFrom(input);
            if (persona.FindProblem() is { } problem)
            {
                logger.LogWarning("Refused to create a persona: {Problem}", problem);
                return Result.Failure<Persona>(problem);
            }

            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            db.Personas.Add(persona);
            await db.SaveChangesAsync();
            logger.LogInformation("Created persona {PersonaId}", persona.Id);
            return persona;
        });

    public Task<Result<Persona>> UpdateAsync(Persona input) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "save the persona", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var persona = await db.Personas.SingleOrDefaultAsync(p => p.Id == input.Id);
            if (persona is null)
            {
                return NotFound(input.Id).To<Persona>();
            }

            persona.CopyEditableFieldsFrom(input);
            if (persona.FindProblem() is { } problem)
            {
                logger.LogWarning("Refused to save persona {PersonaId}: {Problem}", input.Id, problem);
                return Result.Failure<Persona>(problem);
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Updated persona {PersonaId}", persona.Id);
            return persona;
        });

    public Task<Result> DeleteAsync(Guid id) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "delete the persona", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var persona = await db.Personas.SingleOrDefaultAsync(p => p.Id == id);
            if (persona is null)
            {
                return NotFound(id);
            }

            var chats = await db.ChatSessions.CountAsync(s => s.PersonaId == id);
            if (chats > 0)
            {
                logger.LogWarning("Refused to delete persona {PersonaId}: {ChatCount} chats use it", id, chats);
                return Result.Failure("{0} is used by {1} chats; delete those first", persona.Name, chats);
            }

            db.Personas.Remove(persona);
            await db.SaveChangesAsync();
            logger.LogInformation("Deleted persona {PersonaId}", id);
            return Result.Success();
        });

    private Result NotFound(Guid id)
    {
        logger.LogWarning("Persona {PersonaId} was not found", id);
        return Result.Failure("That persona no longer exists");
    }
}
