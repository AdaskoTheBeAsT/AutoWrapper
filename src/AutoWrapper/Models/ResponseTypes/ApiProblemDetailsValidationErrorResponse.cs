using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;

namespace AutoWrapper.Models.ResponseTypes;

public class ApiProblemDetailsValidationErrorResponse
    : ProblemDetails
{
    public bool IsError { get; set; }

    public IEnumerable<ValidationError>? ValidationErrors { get; set; }
}
