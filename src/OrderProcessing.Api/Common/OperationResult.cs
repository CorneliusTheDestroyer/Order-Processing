namespace OrderProcessing.Api.Common;

/// <summary>
/// The shape a business operation finished in. Lets services signal expected, non-exceptional
/// outcomes (not found, bad input, a business-rule conflict) that controllers map to specific HTTP
/// status codes — reserving actual exceptions, and the global exception-handling middleware, for
/// genuinely unexpected failures.
/// </summary>
public enum OperationOutcome
{
    Success,
    NotFound,
    ValidationFailed,
    Conflict,

    /// <summary>A downstream dependency (another controller, reached over HTTP) could not be
    /// reached or timed out. Distinct from Conflict: this is "try again later", not "your request
    /// was rejected" — lets the Inventory/Payment HTTP clients report the assessment's "service
    /// unavailability" error scenario distinctly from a business rule failure.</summary>
    Unavailable
}

/// <summary>Result of a service-layer operation that can fail in one of the ways above.</summary>
public class OperationResult<T>
{
    public OperationOutcome Outcome { get; }
    public T? Value { get; }
    public string? Error { get; }

    private OperationResult(OperationOutcome outcome, T? value, string? error)
    {
        Outcome = outcome;
        Value = value;
        Error = error;
    }

    public bool IsSuccess => Outcome == OperationOutcome.Success;

    public static OperationResult<T> Success(T value) => new(OperationOutcome.Success, value, error: null);

    public static OperationResult<T> NotFound(string error) => new(OperationOutcome.NotFound, default, error);

    public static OperationResult<T> ValidationFailed(string error) => new(OperationOutcome.ValidationFailed, default, error);

    public static OperationResult<T> Conflict(string error) => new(OperationOutcome.Conflict, default, error);

    public static OperationResult<T> Unavailable(string error) => new(OperationOutcome.Unavailable, default, error);
}
