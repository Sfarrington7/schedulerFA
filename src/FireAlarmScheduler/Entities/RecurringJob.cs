// Purpose: A saved recurring job (the RecurringJobs table): the details reused each time the job comes around,
// such as an annual inspection. Jobs created from it link back to it, and the newest one supplies its building list.
namespace FireAlarmScheduler.Entities;

public class RecurringJob
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? CustomerName { get; set; }
    public string? SiteName { get; set; }
    public string Address { get; set; } = "";
}
