// Purpose: Storage boundary for uploaded file bytes. The local-disk implementation can later be swapped for cloud storage.
namespace FireAlarmScheduler.Services;

public interface IFileStorage
{
    /// <summary>Writes a new file under <paramref name="storedFileName"/>. Never overwrites an existing file.</summary>
    Task SaveAsync(string storedFileName, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Opens the stored file for reading, or returns null if it is missing.</summary>
    Stream? OpenRead(string storedFileName);

    /// <summary>Deletes the stored file if it exists.</summary>
    void Delete(string storedFileName);
}
