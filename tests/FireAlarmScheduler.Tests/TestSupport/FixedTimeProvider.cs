// Purpose: A TimeProvider frozen at a chosen instant (in UTC) so date-dependent code is deterministic.
namespace FireAlarmScheduler.Tests.TestSupport;

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
