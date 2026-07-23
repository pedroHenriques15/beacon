using Beacon.Api.Validation;

namespace Beacon.Api.Features.Categories.Commands.CreateCategory;

public class CreateCategoryCommandValidator
{
    public ValidationResult Validate(CreateCategoryCommand cmd)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(cmd.Name))
            errors.Add("Name is required.");

        if (cmd.Value is not null && cmd.Value < 0)
            errors.Add("Value must be non-negative.");

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }
}
