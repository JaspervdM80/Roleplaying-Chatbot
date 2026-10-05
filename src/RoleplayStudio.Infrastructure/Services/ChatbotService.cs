using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoleplayStudio.Domain;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Data;

namespace RoleplayStudio.Infrastructure.Services;

public sealed class ChatbotService(IDbContextFactory<ApplicationDbContext> dbFactory, ICurrentUser currentUser, ILogger<ChatbotService> logger)
{
    public Task<Result<IReadOnlyList<Chatbot>>> ListAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunOwnerAsync<IReadOnlyList<Chatbot>>(currentUser, logger, "list chatbots", cancellationToken, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId, cancellationToken);
            var chatbots = await db.Chatbots.WithCastAndScenarios().AsNoTracking().AsSplitQuery().OrderBy(c => c.Name).ToListAsync(cancellationToken);
            logger.LogDebug("Listed {ChatbotCount} chatbots", chatbots.Count);
            return Result.Success<IReadOnlyList<Chatbot>>(chatbots);
        });

    /// <summary>The chatbot with its cast (ordered by name) and scenarios (ordered by title).</summary>
    public Task<Result<Chatbot>> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "load the chatbot", cancellationToken, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId, cancellationToken);
            var chatbot = await db.Chatbots.WithCastAndScenarios().AsNoTracking().AsSplitQuery().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (chatbot is null)
            {
                return NotFound(id).To<Chatbot>();
            }

            chatbot.Cast = chatbot.Cast.OrderBy(m => m.Character.Name).ToList();
            chatbot.Scenarios = chatbot.Scenarios.OrderBy(s => s.Title).ToList();
            return chatbot;
        });

    public Task<Result<Chatbot>> CreateAsync(Chatbot input) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "create the chatbot", CancellationToken.None, async ownerId =>
        {
            var chatbot = new Chatbot();
            chatbot.CopyEditableFieldsFrom(input);
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            if (await FindProblemAsync(db, chatbot) is { } problem)
            {
                logger.LogWarning("Refused to create a chatbot: {Problem}", problem);
                return Result.Failure<Chatbot>(problem);
            }

            db.Chatbots.Add(chatbot);
            await db.SaveChangesAsync();
            logger.LogInformation("Created chatbot {ChatbotId}", chatbot.Id);
            return chatbot;
        });

    public Task<Result<Chatbot>> UpdateAsync(Chatbot input) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "save the chatbot", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var chatbot = await db.Chatbots.SingleOrDefaultAsync(c => c.Id == input.Id);
            if (chatbot is null)
            {
                return NotFound(input.Id).To<Chatbot>();
            }

            chatbot.CopyEditableFieldsFrom(input);
            if (await FindProblemAsync(db, chatbot) is { } problem)
            {
                logger.LogWarning("Refused to save chatbot {ChatbotId}: {Problem}", input.Id, problem);
                return Result.Failure<Chatbot>(problem);
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Updated chatbot {ChatbotId}", chatbot.Id);
            return chatbot;
        });

    public Task<Result> DeleteAsync(Guid id) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "delete the chatbot", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var chatbot = await db.Chatbots.SingleOrDefaultAsync(c => c.Id == id);
            if (chatbot is null)
            {
                return NotFound(id);
            }

            var chats = await db.ChatSessions.OfChatbot(id).CountAsync();
            if (chats > 0)
            {
                logger.LogWarning("Refused to delete chatbot {ChatbotId}: {ChatCount} chats use it", id, chats);
                return Result.Failure("{0} has {1} chats; delete those first", chatbot.Name, chats);
            }

            db.Chatbots.Remove(chatbot);
            await db.SaveChangesAsync();
            logger.LogInformation("Deleted chatbot {ChatbotId}", id);
            return Result.Success();
        });

    /// <summary>Replaces the cast; a character who leaves it also leaves every scenario's starting line-up.</summary>
    public Task<Result> SetCastAsync(Guid chatbotId, IReadOnlyList<CastMember> cast) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "save the cast", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var chatbot = await db.Chatbots.WithCastAndScenarios().AsSplitQuery().SingleOrDefaultAsync(c => c.Id == chatbotId);
            if (chatbot is null)
            {
                return NotFound(chatbotId);
            }

            var wanted = cast.DistinctBy(m => m.CharacterId).ToDictionary(m => m.CharacterId);
            var known = await db.Characters.CountAsync(c => wanted.Keys.Contains(c.Id));
            if (known != wanted.Count)
            {
                logger.LogWarning("Refused to save the cast of chatbot {ChatbotId}: unknown characters", chatbotId);
                return Result.Failure("One of those characters no longer exists");
            }

            chatbot.Cast.RemoveAll(m => !wanted.ContainsKey(m.CharacterId));
            foreach (var member in wanted.Values)
            {
                var role = TextFields.Clean(member.Role);
                if (chatbot.Cast.Find(m => m.CharacterId == member.CharacterId) is { } existing)
                {
                    existing.Role = role;
                }
                else
                {
                    chatbot.Cast.Add(new ChatbotCharacter { CharacterId = member.CharacterId, Role = role });
                }
            }

            foreach (var scenario in chatbot.Scenarios.Where(s => s.StartingCharacterIds.Any(id => !wanted.ContainsKey(id))))
            {
                scenario.StartingCharacterIds = scenario.StartingCharacterIds.Where(wanted.ContainsKey).ToList();
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Saved a cast of {CastCount} for chatbot {ChatbotId}", wanted.Count, chatbotId);
            return Result.Success();
        });

    public Task<Result<Scenario>> AddScenarioAsync(Guid chatbotId, Scenario input) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "create the scenario", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var chatbot = await db.Chatbots.Include(c => c.Cast).SingleOrDefaultAsync(c => c.Id == chatbotId);
            if (chatbot is null)
            {
                return NotFound(chatbotId).To<Scenario>();
            }

            var scenario = new Scenario { ChatbotId = chatbotId };
            scenario.CopyEditableFieldsFrom(input);
            if (scenario.FindProblem(CastIds(chatbot)) is { } problem)
            {
                logger.LogWarning("Refused to create a scenario in chatbot {ChatbotId}: {Problem}", chatbotId, problem);
                return Result.Failure<Scenario>(problem);
            }

            db.Scenarios.Add(scenario);
            await db.SaveChangesAsync();
            logger.LogInformation("Created scenario {ScenarioId} in chatbot {ChatbotId}", scenario.Id, chatbotId);
            return scenario;
        });

    public Task<Result<Scenario>> UpdateScenarioAsync(Guid chatbotId, Scenario input) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "save the scenario", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var chatbot = await db.Chatbots.Include(c => c.Cast).Include(c => c.Scenarios).SingleOrDefaultAsync(c => c.Id == chatbotId);
            var scenario = chatbot?.Scenarios.Find(s => s.Id == input.Id);
            if (chatbot is null || scenario is null)
            {
                return ScenarioNotFound(input.Id).To<Scenario>();
            }

            scenario.CopyEditableFieldsFrom(input);
            if (scenario.FindProblem(CastIds(chatbot)) is { } problem)
            {
                logger.LogWarning("Refused to save scenario {ScenarioId}: {Problem}", input.Id, problem);
                return Result.Failure<Scenario>(problem);
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Updated scenario {ScenarioId}", scenario.Id);
            return scenario;
        });

    public Task<Result> DeleteScenarioAsync(Guid chatbotId, Guid scenarioId) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "delete the scenario", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var scenario = await db.Chatbots.Where(c => c.Id == chatbotId).SelectMany(c => c.Scenarios).SingleOrDefaultAsync(s => s.Id == scenarioId);
            if (scenario is null)
            {
                return ScenarioNotFound(scenarioId);
            }

            var chats = await db.ChatSessions.CountAsync(s => s.ScenarioId == scenarioId);
            if (chats > 0)
            {
                logger.LogWarning("Refused to delete scenario {ScenarioId}: {ChatCount} chats use it", scenarioId, chats);
                return Result.Failure("{0} has {1} chats; delete those first", scenario.Title, chats);
            }

            db.Scenarios.Remove(scenario);
            await db.SaveChangesAsync();
            logger.LogInformation("Deleted scenario {ScenarioId}", scenarioId);
            return Result.Success();
        });

    private static HashSet<Guid> CastIds(Chatbot chatbot) => chatbot.Cast.Select(m => m.CharacterId).ToHashSet();

    private static async Task<string?> FindProblemAsync(ApplicationDbContext db, Chatbot chatbot)
    {
        if (chatbot.FindProblem() is { } problem)
        {
            return problem;
        }

        if (chatbot.DefaultChatModelProfileId is { } profileId && !await db.ModelProfiles.AnyAsync(p => p.Id == profileId && p.Role == ModelRole.Chat))
        {
            return "That chat model no longer exists";
        }

        return null;
    }

    private Result NotFound(Guid id)
    {
        logger.LogWarning("Chatbot {ChatbotId} was not found", id);
        return Result.Failure("That chatbot no longer exists");
    }

    private Result ScenarioNotFound(Guid id)
    {
        logger.LogWarning("Scenario {ScenarioId} was not found", id);
        return Result.Failure("That scenario no longer exists");
    }
}
