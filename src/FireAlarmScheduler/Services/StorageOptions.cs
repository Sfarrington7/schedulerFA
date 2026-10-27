// Purpose: Configurable location and size limits for uploaded files.
// Bound from the "Storage" section of appsettings.json.
namespace FireAlarmScheduler.Services;

public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Upper bound for one upload request (all files together). Attributes need a compile-time constant.</summary>
    public const long MaxUploadRequestBytes = 100L * 1024 * 1024;

    /// <summary>Folder for uploaded spreadsheets. Relative paths resolve against the app's content root.</summary>
    public string UploadsDirectory { get; set; } = "App_Data/uploads";

    public long MaxFileSizeBytes { get; set; } = 20L * 1024 * 1024;
}
