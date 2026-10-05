namespace RoleplayStudio.Domain.Chats;

public enum MessageRole
{
    User,
    Narrator,
    Character,
}

public class Message : Entity
{
    public Guid SessionId { get; set; }
    public long Sequence { get; set; }
    public MessageRole Role { get; set; }
    public Guid? SpeakerCharacterId { get; set; }
    public string SpeakerName { get; set; } = "";
    public string Content { get; set; } = "";
    public int TokenCount { get; set; }
    public Guid? ImageId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
