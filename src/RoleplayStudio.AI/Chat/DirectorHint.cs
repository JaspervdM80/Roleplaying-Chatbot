namespace RoleplayStudio.AI.Chat;

/// <summary>The user steering the next reply without writing it; it holds for that one reply only.</summary>
public abstract record DirectorHint
{
    public sealed record LetRespond(Guid CharacterId) : DirectorHint;

    public sealed record BringIn(Guid CharacterId) : DirectorHint;

    public sealed record SkipAhead : DirectorHint;

    public sealed record NarrationOnly : DirectorHint;

    public sealed record MoveTo(string Location) : DirectorHint;
}
