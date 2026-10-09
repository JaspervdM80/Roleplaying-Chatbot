namespace RoleplayStudio.AI.Images;

public sealed record ReferenceImage(byte[] Data, string ContentType);

/// <summary>A picture to draw; a generator leaves out what its model does not take, and draws at the nearest size it allows.</summary>
public sealed record ImageRequest(string Prompt, string? NegativePrompt, int Width, int Height, long Seed, IReadOnlyList<ReferenceImage>? References = null);

/// <summary>The picture as drawn; <see cref="Seed"/> is null when the model takes no seed, so the picture cannot be drawn the same way again.</summary>
public sealed record GeneratedPicture(byte[] Data, string ContentType, long? Seed, int Width, int Height);

public sealed record ImageSize(int Width, int Height);

/// <summary>What an image model accepts. <see cref="Sizes"/> is empty when it draws at any size asked for.</summary>
public sealed record ImageModelTraits(bool TakesNegativePrompt, bool TakesSeed, int MaxReferenceImages, IReadOnlyList<ImageSize> Sizes)
{
    /// <summary>A model nothing is known about: diffusion models of the Stable Diffusion family take a negative prompt and a seed.</summary>
    public static readonly ImageModelTraits Unknown = new(true, true, 0, []);

    /// <summary>The size asked for, or of the allowed sizes nearly as close to its shape as the closest, the one nearest its area.</summary>
    public ImageSize Fit(int width, int height)
    {
        if (Sizes.Count == 0)
        {
            return new ImageSize(width, height);
        }

        double ShapeGap(ImageSize size) => Math.Abs(Math.Log((double)size.Width / size.Height) - Math.Log((double)width / height));
        var closest = Sizes.Min(ShapeGap);

        // Presets come at several resolutions, some only roughly in shape; an exact 3:4 at 4K is no answer to a 3:4 at 1K.
        return Sizes
            .Where(s => ShapeGap(s) <= closest + 0.05)
            .MinBy(s => Math.Abs(((long)s.Width * s.Height) - ((long)width * height)))!;
    }
}

public interface IImageGenerator
{
    string ModelId { get; }

    Task<ImageModelTraits> TraitsAsync(CancellationToken cancellationToken = default);

    Task<GeneratedPicture> GenerateAsync(ImageRequest request, CancellationToken cancellationToken = default);
}
