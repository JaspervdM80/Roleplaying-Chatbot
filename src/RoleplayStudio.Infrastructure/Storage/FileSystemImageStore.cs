namespace RoleplayStudio.Infrastructure.Storage;

public sealed class FileSystemImageStore(string root) : IImageStore
{
    private readonly string _root = Path.GetFullPath(root);

    public async Task SaveAsync(string path, byte[] data, CancellationToken cancellationToken = default)
    {
        var file = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, data, cancellationToken);
    }

    public Stream? OpenRead(string path)
    {
        var file = Resolve(path);
        return File.Exists(file) ? File.OpenRead(file) : null;
    }

    public void Delete(string path) => File.Delete(Resolve(path));

    private string Resolve(string path)
    {
        var file = Path.GetFullPath(Path.Combine(_root, path));
        return file.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? file
            : throw new ArgumentException("An image path must stay inside the image folder.", nameof(path));
    }
}
