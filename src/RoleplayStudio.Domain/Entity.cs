namespace RoleplayStudio.Domain;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}

/// <summary>An entity that belongs to a single user and is only visible to them.</summary>
public abstract class OwnedEntity : Entity
{
    public string OwnerId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
