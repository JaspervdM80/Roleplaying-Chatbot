namespace RoleplayStudio.Infrastructure.Storage;

public interface IImageStore
{
    Task SaveAsync(string path, byte[] data, CancellationToken cancellationToken = default);

    /// <summary>The stored file, or null when nothing is stored under that path.</summary>
    Stream? OpenRead(string path);

    void Delete(string path);
}
