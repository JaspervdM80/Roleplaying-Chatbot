using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Infrastructure.Storage;

namespace RoleplayStudio.AI.Images;

internal sealed class PictureReferences(IImageStore store, ILogger logger)
{
    /// <summary>The reference images that can still be read, by image id; one that cannot is logged and left out.</summary>
    public async Task<IReadOnlyDictionary<Guid, ReferenceImage>> LoadAsync(ApplicationDbContext db, IReadOnlyList<Guid> imageIds, CancellationToken cancellationToken)
    {
        var loaded = new Dictionary<Guid, ReferenceImage>();
        if (imageIds.Count == 0)
        {
            return loaded;
        }

        var stored = await db.Images.AsNoTracking().Where(i => imageIds.Contains(i.Id)).Select(i => new { i.Id, i.StoragePath, i.ContentType }).ToListAsync(cancellationToken);
        foreach (var imageId in imageIds.Distinct())
        {
            if (stored.FirstOrDefault(i => i.Id == imageId) is not { } reference || store.OpenRead(reference.StoragePath) is not { } content)
            {
                logger.LogWarning("Reference image {ImageId} is gone; drawing without it", imageId);
                continue;
            }

            await using (content)
            {
                using var copy = new MemoryStream();
                await content.CopyToAsync(copy, cancellationToken);
                loaded[imageId] = new ReferenceImage(copy.ToArray(), reference.ContentType);
            }
        }

        return loaded;
    }
}

/// <summary>Numbers, from 1, the people whose reference portrait loaded, while the model takes more.</summary>
internal sealed class ReferenceNumbering(IReadOnlyDictionary<Guid, ReferenceImage> loaded, int max)
{
    private readonly List<Guid> _ids = [];

    public IReadOnlyList<Guid> Ids => _ids;

    public IReadOnlyList<ReferenceImage> Images => [.. _ids.Select(id => loaded[id])];

    public int? Number(Character character)
    {
        if (character.ReferenceImageId is not { } id || !loaded.ContainsKey(id))
        {
            return null;
        }

        if (!_ids.Contains(id))
        {
            if (_ids.Count >= max)
            {
                return null;
            }

            _ids.Add(id);
        }

        return _ids.IndexOf(id) + 1;
    }
}
