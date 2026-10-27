// Purpose: Stored record for one building within a job (the Buildings table), with its uploaded spreadsheets.
namespace FireAlarmScheduler.Entities;

public class Building
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string Name { get; set; } = "";
    public string? Notes { get; set; }

    /// <summary>Keeps buildings in the order they were added; SQL rows have no inherent order.</summary>
    public int SortOrder { get; set; }

    public List<UploadedFile> UploadedFiles { get; set; } = [];
}
