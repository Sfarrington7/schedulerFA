// Purpose: Upload, download, and delete the spreadsheets attached to a job's building.
// Enforces allowed extensions and size limits and stores files under generated names.
using FireAlarmScheduler.Entities;
using Microsoft.Extensions.Options;

namespace FireAlarmScheduler.Services;

public record FileDownload(string FileName, string ContentType, Stream Content);

public class BuildingFileService
{
    private const int MaxOriginalNameLength = 150;

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".xls"] = "application/vnd.ms-excel",
        [".csv"] = "text/csv"
    };

    private readonly IJobRepository _jobs;
    private readonly IFileStorage _fileStorage;
    private readonly TimeProvider _timeProvider;
    private readonly long _maxFileSizeBytes;

    public BuildingFileService(IJobRepository jobs, IFileStorage fileStorage, IOptions<StorageOptions> options, TimeProvider timeProvider)
    {
        _jobs = jobs;
        _fileStorage = fileStorage;
        _timeProvider = timeProvider;
        _maxFileSizeBytes = options.Value.MaxFileSizeBytes;
    }

    public static IReadOnlyCollection<string> AllowedExtensions => ContentTypes.Keys;

    public long MaxFileSizeBytes => _maxFileSizeBytes;

    /// <summary>
    /// Validates and stores one uploaded file, then records it on the building.
    /// Returns null if the job or building does not exist. Throws ValidationFailedException for
    /// a disallowed extension, an empty file, or a file over the size limit.
    /// </summary>
    public async Task<UploadedFile?> UploadAsync(Guid jobId, Guid buildingId, string? originalFileName, Stream content, long length)
    {
        var job = await _jobs.GetByIdAsync(jobId);
        if (job?.Buildings.Any(b => b.Id == buildingId) != true)
        {
            return null;
        }

        var displayName = SanitizeFileName(originalFileName);
        var extension = Path.GetExtension(displayName).ToLowerInvariant();
        if (!ContentTypes.ContainsKey(extension))
        {
            throw new ValidationFailedException("files",
                $"\"{displayName}\" is not allowed. Upload {string.Join(", ", AllowedExtensions)} files only.");
        }

        if (length <= 0)
        {
            throw new ValidationFailedException("files", $"\"{displayName}\" is empty.");
        }

        if (length > _maxFileSizeBytes)
        {
            throw new ValidationFailedException("files",
                $"\"{displayName}\" is larger than the {_maxFileSizeBytes / (1024 * 1024)} MB limit.");
        }

        var file = new UploadedFile
        {
            Id = Guid.NewGuid(),
            OriginalFileName = displayName,
            StoredFileName = $"{Guid.NewGuid():N}{extension}",
            SizeBytes = length,
            UploadDate = _timeProvider.GetUtcNow().UtcDateTime,
            BuildingId = buildingId
        };

        await _fileStorage.SaveAsync(file.StoredFileName, content);

        var recorded = await _jobs.UpdateAsync(jobId, j =>
        {
            var building = j.Buildings.FirstOrDefault(b => b.Id == buildingId);
            building?.UploadedFiles.Add(file);
            return building is not null;
        });

        if (!recorded)
        {
            // The building was deleted while the file was uploading; don't leave an orphaned file behind.
            _fileStorage.Delete(file.StoredFileName);
            return null;
        }

        return file;
    }

    /// <summary>Opens a stored file for download, or returns null if its record or bytes are missing.</summary>
    public async Task<FileDownload?> OpenDownloadAsync(Guid jobId, Guid buildingId, Guid fileId)
    {
        var job = await _jobs.GetByIdAsync(jobId);
        var file = job?.Buildings.FirstOrDefault(b => b.Id == buildingId)?.UploadedFiles.FirstOrDefault(f => f.Id == fileId);
        if (file is null)
        {
            return null;
        }

        var stream = _fileStorage.OpenRead(file.StoredFileName);
        if (stream is null)
        {
            return null;
        }

        var contentType = ContentTypes.GetValueOrDefault(Path.GetExtension(file.StoredFileName), "application/octet-stream");
        return new FileDownload(file.OriginalFileName, contentType, stream);
    }

    /// <summary>Removes the file record and its stored bytes. Returns false if the record does not exist.</summary>
    public async Task<bool> DeleteAsync(Guid jobId, Guid buildingId, Guid fileId)
    {
        var removed = await _jobs.UpdateAsync(jobId, job =>
        {
            var building = job.Buildings.FirstOrDefault(b => b.Id == buildingId);
            var file = building?.UploadedFiles.FirstOrDefault(f => f.Id == fileId);
            if (file is not null)
            {
                building!.UploadedFiles.Remove(file);
            }

            return file;
        });

        if (removed is null)
        {
            return false;
        }

        _fileStorage.Delete(removed.StoredFileName);
        return true;
    }

    /// <summary>
    /// Reduces a client-supplied name to a safe display name: drops any folder parts
    /// (both / and \), strips invalid characters, and limits the length while keeping the extension.
    /// </summary>
    public static string SanitizeFileName(string? originalFileName)
    {
        var name = (originalFileName ?? "").Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];

        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray()).Trim().TrimStart('.');

        if (name.Length > MaxOriginalNameLength)
        {
            var extension = Path.GetExtension(name);
            name = name[..(MaxOriginalNameLength - extension.Length)] + extension;
        }

        return name.Length == 0 ? "file" : name;
    }
}
