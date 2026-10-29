// Purpose: One building on a recurring job's standing building list (the RecurringJobBuildings table).
// Each new job in the series starts with these buildings.
namespace FireAlarmScheduler.Entities;

public class RecurringJobBuilding
{
    public Guid Id { get; set; }
    public Guid RecurringJobId { get; set; }
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
}
