using Beacon.Api.Validation;

namespace Beacon.Api.Features.Categories.Commands.UpdateCategory;

public class UpdateCategoryCommandValidator
{
    public ValidationResult Validate(UpdateCategoryCommand cmd)
    {
        var errors = new List<string>();

        if (cmd.Id <= 0)
            errors.Add("Id must be a positive integer.");

        if (string.IsNullOrWhiteSpace(cmd.Name) && string.IsNullOrWhiteSpace(cmd.Color))
            errors.Add("At least one of Name or Color must be provided.");

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }
}
