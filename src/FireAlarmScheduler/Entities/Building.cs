// Purpose: Stored record for one building within a job, with its uploaded spreadsheets.
namespace FireAlarmScheduler.Entities;

public class Building
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    public List<UploadedFile> UploadedFiles { get; set; } = [];
}
