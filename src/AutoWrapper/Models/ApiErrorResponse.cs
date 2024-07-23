using System.Collections.Generic;

namespace AutoWrapper.Models;

internal class ApiErrorResponse
{
    public ApiErrorResponse(string message, string code, string? details = null)
    {
        Error = new ApiError
        {
            Message = message,
            Code = code,
            Details = details ?? null,
        };
    }

    public ApiErrorResponse(IEnumerable<ValidationError> validationErrors)
    {
        Error = new ApiError
        {
            Message = "Your request parameters did not validate.",
            Code = "ModelStateError",
            ValidationErrors = validationErrors,
        };
    }

    public ApiErrorResponse(object errorModel)
    {
        Error = new ApiError
        {
            InnerError = errorModel,
        };
    }

    public bool? IsError { get; set; } = true;

    public ApiError? Error { get; set; }
}
