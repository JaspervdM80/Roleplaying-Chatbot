namespace RoleplayStudio.Domain.Chats;

/// <summary>One speaker's part of a model reply; a null <see cref="CharacterId"/> is narration.</summary>
public sealed record ReplySegment(Guid? CharacterId, string Text);
