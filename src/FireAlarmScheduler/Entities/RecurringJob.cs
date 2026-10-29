// Purpose: A saved recurring job (the RecurringJobs table): the details and standing building list reused each
// time the job comes around, such as an annual inspection. Jobs created from it link back to it.
namespace FireAlarmScheduler.Entities;

public class RecurringJob
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? CustomerName { get; set; }
    public string? SiteName { get; set; }
    public string Address { get; set; } = "";

    /// <summary>The buildings every new job in this series starts with, in display order.</summary>
    public List<RecurringJobBuilding> Buildings { get; set; } = [];

    /// <summary>
    /// Adds a building to the standing list unless one with the same name (ignoring case) is already there.
    /// Returns true if it was added.
    /// </summary>
    public bool AddBuildingIfMissing(string name, string? notes)
    {
        if (Buildings.Any(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        Buildings.Add(new RecurringJobBuilding
        {
            Id = Guid.NewGuid(),
            RecurringJobId = Id,
            Name = name,
            Notes = notes,
            SortOrder = Buildings.Count == 0 ? 0 : Buildings.Max(b => b.SortOrder) + 1
        });
        return true;
    }
}
