// Purpose: The fields a technician enters when requesting PTO, passed from the controller to PtoService.
namespace FireAlarmScheduler.Services;

public record PtoInput(DateOnly StartDate, TimeOnly StartTime, DateOnly EndDate, TimeOnly EndTime, string? Reason);
