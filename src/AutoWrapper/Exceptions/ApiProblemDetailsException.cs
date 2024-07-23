using System;
using System.Text;
using AutoWrapper.Extensions;
using AutoWrapper.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace AutoWrapper.Exceptions;

#pragma warning disable RCS1194 // Implement exception constructors
public class ApiProblemDetailsException
#pragma warning restore RCS1194 // Implement exception constructors
    : Exception
{
    public ApiProblemDetailsException(int statusCode)
        : this(new ApiProblemDetails(statusCode))
    {
    }

    public ApiProblemDetailsException(string title, int statusCode)
        : this(new ApiProblemDetails(statusCode) { Title = title })
    {
    }

    public ApiProblemDetailsException(ProblemDetails details)
        : base($"{details.Type} : {details.Title}")
    {
        Problem = new ApiProblemDetails(details);
    }

    public ApiProblemDetailsException(ModelStateDictionary modelState, int statusCode = Status422UnprocessableEntity)
        : this(new ApiProblemDetails(statusCode) { Detail = "Your request parameters did not validate.", ValidationErrors = modelState.AllErrors() })
    {
    }

    public int StatusCode => Problem.Details.Status ?? 0;

    internal ApiProblemDetails Problem { get; }

    public override string ToString()
    {
        var stringBuilder = new StringBuilder();

        stringBuilder.AppendLine($"Type    : {Problem.Details.Type}");
        stringBuilder.AppendLine($"Title   : {Problem.Details.Title}");
        stringBuilder.AppendLine($"Status  : {Problem.Details.Status}");
        stringBuilder.AppendLine($"Detail  : {Problem.Details.Detail}");
        stringBuilder.AppendLine($"Instance: {Problem.Details.Instance}");

        return stringBuilder.ToString();
    }
}
