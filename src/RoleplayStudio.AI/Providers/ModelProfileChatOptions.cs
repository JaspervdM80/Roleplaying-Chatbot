using Microsoft.Extensions.AI;
using RoleplayStudio.Domain.Models;

namespace RoleplayStudio.AI.Providers;

public static class ModelProfileChatOptions
{
    public static ChatOptions ToChatOptions(this ModelProfile profile) => new()
    {
        Temperature = (float?)profile.Temperature,
        MaxOutputTokens = profile.MaxOutputTokens,
    };
}
