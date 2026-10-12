using RoleplayStudio.AI.Scene;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.Tests.Support;

/// <summary>Small tracked scenes shared by the scene-tracking and scene-update tests.</summary>
public static class Scenes
{
    public static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    public static readonly Character Mira = new() { Name = "Mira", DefaultOutfit = "An apron and clogs" };
    public static readonly Character Thorne = new() { Name = "Thorne Ashby", DefaultOutfit = "An oilskin coat" };

    /// <summary>A chat with Mira present, and Thorne in the cast but not met yet.</summary>
    public static (ChatSession Session, SceneCast Cast) MiraAlone()
    {
        var session = new ChatSession { Scene = new SceneState { Location = "The bar" } };
        session.SetPresent([Mira], Now);
        session.CharacterStates[0].Character = Mira;
        return (session, new SceneCast(session.CharacterStates, [Mira, Thorne]));
    }

    public static SceneUpdate Parsed(string json) => SceneTracking.Parse(json) ?? throw new InvalidOperationException("The test JSON did not parse");

    /// <summary>A chat whose scene tracking met someone it could only call "The stablehand", with the looks it guessed.</summary>
    public static (ChatSession Session, SceneCast Cast, Character Stablehand) WithStablehand()
    {
        var session = new ChatSession { Scene = new SceneState { Location = "The yard" } };
        var stablehand = new Character { Name = "The stablehand", Age = 20, Appearance = "Lanky", IntroducedInSessionId = session.Id };
        session.SetPresent([stablehand], Now);
        session.CharacterStates[0].Character = stablehand;
        return (session, new SceneCast(session.CharacterStates, [Mira]), stablehand);
    }
}
