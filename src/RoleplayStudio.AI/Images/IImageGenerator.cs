namespace RoleplayStudio.AI.Images;

public sealed record ReferenceImage(byte[] Data, string ContentType);

public sealed record ImageRequest(string Prompt, string? NegativePrompt, int Width, int Height, long Seed, ReferenceImage? Reference = null);

public sealed record GeneratedPicture(byte[] Data, string ContentType, long Seed);

public interface IImageGenerator
{
    string ModelId { get; }

    Task<GeneratedPicture> GenerateAsync(ImageRequest request, CancellationToken cancellationToken = default);
}
