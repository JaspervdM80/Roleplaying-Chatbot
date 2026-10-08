namespace RoleplayStudio.Web;

public static class AppRoutes
{
    public const string Home = "/";
    public const string Models = "/models";
    public const string Playground = "/playground";
    public const string Personas = "/personas";
    public const string Characters = "/characters";
    public const string Chatbots = "/chatbots";
    public const string Chats = "/chats";

    public static string PlaygroundWith(Guid profileId) => $"{Playground}?profile={profileId}";

    public static string Chatbot(Guid id) => $"{Chatbots}/{id}";

    public static string Chat(Guid id) => $"{Chats}/{id}";

    public static string ChatMemories(Guid id) => $"{Chat(id)}/memories";
}
