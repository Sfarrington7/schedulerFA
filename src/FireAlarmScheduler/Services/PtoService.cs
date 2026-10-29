// Purpose: PTO rules: technicians submit and cancel their own requests; administrators approve or deny them.
// Callers pass the technician from the signed-in account, never from form input.
using FireAlarmScheduler.Entities;

namespace FireAlarmScheduler.Services;

public enum PtoCancelResult
{
    Cancelled,
    NotFound,
    NotPending
}

public class PtoService
{
    private const int MaxReasonLength = 500;

    private readonly IPtoRepository _requests;
    private readonly ITechnicianRepository _technicians;
    private readonly TimeProvider _timeProvider;

    public PtoService(IPtoRepository requests, ITechnicianRepository technicians, TimeProvider timeProvider)
    {
        _requests = requests;
        _technicians = technicians;
        _timeProvider = timeProvider;
    }

    /// <summary>The technician's requests, newest period first.</summary>
    public async Task<List<PtoRequest>> GetForTechnicianAsync(Guid technicianId) =>
        (await _requests.GetForTechnicianAsync(technicianId)).OrderByDescending(p => p.StartsAt).ToList();

    /// <summary>Every request, pending ones first, then by start of the time off.</summary>
    public async Task<List<PtoRequest>> GetAllAsync() =>
        (await _requests.GetAllAsync())
            .OrderBy(p => p.Status != PtoStatus.Pending)
            .ThenBy(p => p.StartsAt)
            .ToList();

    /// <summary>
    /// Records a pending request. Rejects periods that end before they start, start before today, or overlap
    /// one of the technician's own pending or approved requests.
    /// </summary>
    public async Task<PtoRequest> SubmitAsync(Guid technicianId, PtoInput input)
    {
        if (await _technicians.GetByIdAsync(technicianId) is null)
        {
            throw new ValidationFailedException("", "Your account isn't linked to a technician.");
        }

        var request = new PtoRequest
        {
            Id = Guid.NewGuid(),
            TechnicianId = technicianId,
            StartDate = input.StartDate,
            StartTime = input.StartTime,
            EndDate = input.EndDate,
            EndTime = input.EndTime,
            Reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim(),
            Status = PtoStatus.Pending,
            RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        };

        var errors = new Dictionary<string, string>();
        if (request.EndsAt <= request.StartsAt)
        {
            errors["EndDate"] = "The end must be after the start.";
        }

        if (request.StartDate < ScheduleCalendar.Today(_timeProvider))
        {
            errors["StartDate"] = "PTO can't start in the past.";
        }

        if (request.Reason?.Length > MaxReasonLength)
        {
            errors["Reason"] = $"Reason must be {MaxReasonLength} characters or fewer.";
        }

        if (errors.Count == 0)
        {
            var clash = (await _requests.GetForTechnicianAsync(technicianId))
                .FirstOrDefault(p => p.IsActive && p.Overlaps(request.StartsAt, request.EndsAt));
            if (clash is not null)
            {
                errors[""] = $"This overlaps your {clash.Status.ToString().ToLowerInvariant()} request for " +
                             $"{clash.StartsAt:MMM d h:mm tt} – {clash.EndsAt:MMM d h:mm tt}.";
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        await _requests.AddAsync(request);
        return request;
    }

    /// <summary>Withdraws one of the technician's own requests while it is still pending.</summary>
    public async Task<PtoCancelResult> CancelAsync(Guid technicianId, Guid requestId)
    {
        var request = await _requests.GetByIdAsync(requestId);
        if (request is null || request.TechnicianId != technicianId)
        {
            return PtoCancelResult.NotFound;
        }

        if (request.Status != PtoStatus.Pending)
        {
            return PtoCancelResult.NotPending;
        }

        return await _requests.DeleteAsync(requestId) ? PtoCancelResult.Cancelled : PtoCancelResult.NotFound;
    }

    /// <summary>
    /// Approves or denies a request (denying an approved request revokes it). Returns false if it doesn't exist.
    /// </summary>
    public async Task<bool> ReviewAsync(Guid requestId, PtoStatus decision, string reviewer)
    {
        if (decision == PtoStatus.Pending || !Enum.IsDefined(decision))
        {
            throw new ArgumentOutOfRangeException(nameof(decision), "A review must approve or deny.");
        }

        return await _requests.UpdateAsync(requestId, request =>
        {
            request.Status = decision;
            request.ReviewedBy = reviewer;
            request.ReviewedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            return true;
        });
    }
}
