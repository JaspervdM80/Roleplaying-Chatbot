namespace RoleplayStudio.Domain.Media;

public class GeneratedImage : OwnedEntity
{
    public Guid? SessionId { get; set; }
    public Guid? CharacterId { get; set; }
    public Guid? MessageId { get; set; }
    public string Prompt { get; set; } = "";

    /// <summary>A few words on what the picture shows, for the gallery and the chat.</summary>
    public string? Caption { get; set; }
    public string? NegativePrompt { get; set; }
    public List<Guid> SourceMemoryIds { get; set; } = [];
    public string Provider { get; set; } = "";
    public string? Model { get; set; }
    public long? Seed { get; set; }
    public string StoragePath { get; set; } = "";
    public string ContentType { get; set; } = "image/png";
    public int Width { get; set; }
    public int Height { get; set; }

    public bool IsPortrait => SessionId is null && CharacterId is not null;
}
