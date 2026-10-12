using RoleplayStudio.AI.Images.ComfyUI;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;
using IImageGenerator = RoleplayStudio.AI.Images.IImageGenerator;

namespace RoleplayStudio.AI.Providers;

internal sealed class ComfyUIProvider : IModelProvider
{
    public ProviderKind Kind => ProviderKind.ComfyUI;

    Result<IImageGenerator> IModelProvider.CreateImageGenerator(ModelProfile profile) =>
        ComfyUIWorkflows.Find(profile.ModelId) is { } workflows
            ? Result.Success<IImageGenerator>(new ComfyUIImageGenerator(ProviderHttp.Client(Timeout.InfiniteTimeSpan), new Uri(profile.Address!), workflows))
            : Result.Failure<IImageGenerator>("There is no ComfyUI workflow set called {0}; use one of {1}", profile.ModelId, string.Join(", ", ComfyUIWorkflows.Names));
}
