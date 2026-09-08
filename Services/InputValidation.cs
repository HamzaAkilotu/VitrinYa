using System.ComponentModel.DataAnnotations;

namespace VitrinYa.Services;

internal static class InputValidation
{
    public static void Validate(object input)
    {
        var errors = new List<ValidationResult>();
        if (Validator.TryValidateObject(input, new ValidationContext(input), errors, validateAllProperties: true)) return;
        var error = errors[0];
        throw new ServiceException(error.ErrorMessage ?? "Geçersiz bilgi.", field: error.MemberNames.FirstOrDefault() ?? "");
    }
}
