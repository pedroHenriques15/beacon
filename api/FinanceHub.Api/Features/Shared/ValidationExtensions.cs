using FinanceHub.Api.Validation;

namespace FinanceHub.Api.Features.Shared;

public static class ValidationExtensions
{
    public static void ThrowIfInvalid(this ValidationResult result)
    {
        if (!result.IsValid)
            throw new ArgumentException(string.Join("; ", result.Errors));
    }
}
