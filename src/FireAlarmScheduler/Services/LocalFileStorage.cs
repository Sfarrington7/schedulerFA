// Purpose: Keeps uploaded files in one local folder outside wwwroot.
// Only plain file names are accepted, so a stored name can never reach a file outside that folder.
namespace FireAlarmScheduler.Services;

public class LocalFileStorage : IFileStorage
{
    private readonly string _rootDirectory;

    public LocalFileStorage(string rootDirectory)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public async Task SaveAsync(string storedFileName, Stream content, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(storedFileName);
        Directory.CreateDirectory(_rootDirectory);

        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        await content.CopyToAsync(output, cancellationToken);
    }

    public Stream? OpenRead(string storedFileName)
    {
        var path = ResolvePath(storedFileName);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    public void Delete(string storedFileName)
    {
        var path = ResolvePath(storedFileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Maps a stored name to a path inside the storage folder. Rejects anything containing
    /// directory parts or invalid characters (for example "../secrets.txt").
    /// </summary>
    private string ResolvePath(string storedFileName)
    {
        if (string.IsNullOrWhiteSpace(storedFileName)
            || storedFileName.IndexOfAny(['/', '\\']) >= 0
            || storedFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || storedFileName is "." or "..")
        {
            throw new ArgumentException($"'{storedFileName}' is not a valid stored file name.", nameof(storedFileName));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_rootDirectory, storedFileName));
        if (Path.GetDirectoryName(fullPath) != _rootDirectory)
        {
            throw new ArgumentException($"'{storedFileName}' is not a valid stored file name.", nameof(storedFileName));
        }

        return fullPath;
    }
}
