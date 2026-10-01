namespace Beacon.Api.Validation;

public sealed class ValidationResult
{
    public bool IsValid { get; private init; }
    public IReadOnlyList<string> Errors { get; private init; } = [];

    public static ValidationResult Success() => new() { IsValid = true, Errors = [] };

    public static ValidationResult Failure(IEnumerable<string> errors)
    {
        var list = errors.ToList();
        return new() { IsValid = false, Errors = list };
    }
}
