using System.Collections.Generic;

namespace AutoWrapper.Models;

internal class ApiError
{
    public string? Message { get; set; }

    public string? Code { get; set; }

    public IEnumerable<ValidationError>? ValidationErrors { get; set; }

    public string? Details { get; set; }

    public object? InnerError { get; set; }
}
