using RoleplayStudio.AI.Images;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;
using IImageGenerator = RoleplayStudio.AI.Images.IImageGenerator;

namespace RoleplayStudio.AI.Providers;

internal sealed class RunwareProvider(ProviderKeys keys, RunwareModelCatalog models) : IModelProvider
{
    public ProviderKind Kind => ProviderKind.Runware;

    Result<IImageGenerator> IModelProvider.CreateImageGenerator(ModelProfile profile)
    {
        if (profile.ApiKeySetting is null)
        {
            return Result.Failure<IImageGenerator>("Runware needs an API key setting, such as Providers:Runware:ApiKey");
        }

        var baseUrl = profile.Address!;
        var key = keys.Find(profile.ApiKeySetting, baseUrl);
        return key.IsSuccess
            ? Result.Success<IImageGenerator>(new RunwareImageGenerator(ProviderHttp.Client(TimeSpan.FromMinutes(2)), new Uri(baseUrl), key.Value, profile.ModelId, models))
            : key.To<IImageGenerator>();
    }
}
