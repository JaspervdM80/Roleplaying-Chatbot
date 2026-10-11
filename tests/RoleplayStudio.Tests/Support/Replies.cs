using RoleplayStudio.AI.Chat;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.Tests.Support;

public static class Replies
{
    public static Task<Result<IReadOnlyList<Message>>> ReplyAsync(this ChatSessionService sessions, Guid sessionId, string text) =>
        sessions.AddReplyAsync(sessionId, present => SpeakerTagParser.Parse(text, present));
}
