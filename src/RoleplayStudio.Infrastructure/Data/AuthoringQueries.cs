using Microsoft.EntityFrameworkCore;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.Infrastructure.Data;

public static class AuthoringQueries
{
    public static IQueryable<Chatbot> WithCast(this IQueryable<Chatbot> chatbots) =>
        chatbots.Include(c => c.Cast).ThenInclude(m => m.Character);

    public static IQueryable<Chatbot> WithCastAndScenarios(this IQueryable<Chatbot> chatbots) =>
        chatbots.WithCast().Include(c => c.Scenarios);

    public static IQueryable<ChatSession> WithScenarioAndPersona(this IQueryable<ChatSession> sessions) =>
        sessions
            .Include(s => s.Scenario).ThenInclude(s => s.Chatbot)
            .Include(s => s.Persona)
            .Include(s => s.CharacterStates).ThenInclude(s => s.Character);

    /// <summary>Sessions started from any scenario of the chatbot.</summary>
    public static IQueryable<ChatSession> OfChatbot(this IQueryable<ChatSession> sessions, Guid chatbotId) =>
        sessions.Where(s => s.Scenario.ChatbotId == chatbotId);
}
