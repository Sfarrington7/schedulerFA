// Purpose: Stored record for a field technician on the team.
namespace FireAlarmScheduler.Entities;

public class Technician
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";

    public string FullName => $"{FirstName} {LastName}";
}
