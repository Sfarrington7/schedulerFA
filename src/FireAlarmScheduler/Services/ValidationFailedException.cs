// Purpose: Raised by services when user input breaks a business rule.
// Carries field-keyed messages so controllers can show them next to the matching form inputs.
namespace FireAlarmScheduler.Services;

public class ValidationFailedException : Exception
{
    public ValidationFailedException(IReadOnlyDictionary<string, string> errors)
        : base(string.Join(" ", errors.Values))
    {
        Errors = errors;
    }

    public ValidationFailedException(string field, string message)
        : this(new Dictionary<string, string> { [field] = message })
    {
    }

    /// <summary>Messages keyed by the input field name ("" for errors not tied to one field).</summary>
    public IReadOnlyDictionary<string, string> Errors { get; }
}
