// Purpose: Metadata for a spreadsheet uploaded to a building.
// The file bytes live in local file storage under StoredFileName; OriginalFileName is display-only.
namespace FireAlarmScheduler.Entities;

public class UploadedFile
{
    public Guid Id { get; set; }
    public string OriginalFileName { get; set; } = "";
    public string StoredFileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime UploadDate { get; set; }
    public Guid BuildingId { get; set; }
}
