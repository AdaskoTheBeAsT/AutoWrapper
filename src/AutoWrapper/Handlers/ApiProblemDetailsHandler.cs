using System;
using System.Text.Json;
using System.Threading.Tasks;
using AutoWrapper.Constants;
using AutoWrapper.Exceptions;
using AutoWrapper.Extensions;
using AutoWrapper.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace AutoWrapper.Handlers;

internal static class ApiProblemDetailsHandler
{
    private static readonly RouteData _emptyRouteData = new();
    private static readonly ActionDescriptor _emptyActionDescriptor = new();

    public static Task HandleProblemDetailsAsync(
        HttpContext context,
        IActionResultExecutor<ObjectResult> executor,
        object? body,
        Exception? exception,
        bool isDebug = false)
    {
        var statusCode = context.Response.StatusCode;
        var details = exception == null ? DelegateResponse(body, statusCode) : GetProblemDetails(exception, isDebug);

        if (details is ProblemDetails problemDetails)
        {
            problemDetails.Instance = context.Request.Path;
        }

        var routeData = context.GetRouteData() ?? _emptyRouteData;

        var actionContext = new ActionContext(context, routeData, _emptyActionDescriptor);

        var result = new ObjectResult(details)
        {
            StatusCode = (details is ProblemDetails problem) ? problem.Status : statusCode,
            DeclaredType = details.GetType(),
        };

        result.ContentTypes.Add(ContentMediaTypes.ProblemJSONHttpContentMediaType);
        result.ContentTypes.Add(ContentMediaTypes.ProblemXMLHttpContentMediaType);

        return executor.ExecuteAsync(actionContext, result);
    }

    private static object DelegateResponse(
        object? body,
        int statusCode)
    {
        var content = body ?? string.Empty;
        var (isEncoded, parsedText) = content.ToString()!.VerifyBodyContent();
        var result = isEncoded
            ? JsonSerializer.Deserialize<dynamic>(parsedText)
            : new ApiProblemDetails(statusCode)
            {
                Detail = content.ToString(),
            };

        return result ?? string.Empty;
    }

    private static ProblemDetails GetProblemDetails(
        Exception exception,
        bool isDebug)
    {
        if (exception is ApiProblemDetailsException problem)
        {
            return problem.Problem.Details;
        }

        var defaultException = new ExceptionFallback(exception);

        if (isDebug)
        {
            return new DebugExceptionDetails(defaultException);
        }

        return new ApiProblemDetails((int)defaultException.Status!)
        {
            Detail = defaultException.Exception.Message,
        };
    }

    internal class ErrorDetails
    {
        public ErrorDetails(ExceptionFallback detail)
        {
            Source = detail.Exception.Source ?? string.Empty;
            Raw = detail.Exception.StackTrace ?? string.Empty;
            Message = detail.Exception.Message;
            Type = detail.Exception.GetType().Name;
        }

        public string Message { get; set; }

        public string Type { get; set; }

        public string Source { get; set; }

        public string Raw { get; set; }
    }

    internal class ExceptionFallback : ApiProblemDetails
    {
        public ExceptionFallback(Exception exception)
            : this(exception, Status500InternalServerError)
        {
            Detail = exception.Message;
        }

        public ExceptionFallback(
            Exception exception,
            int statusCode)
            : base(statusCode)
        {
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        }

        public Exception Exception { get; }
    }

    internal class DebugExceptionDetails : ApiProblemDetails
    {
        public DebugExceptionDetails(ExceptionFallback problem)
            : base(problem.Status ?? Status500InternalServerError)
        {
            Detail = problem.Detail ?? problem.Exception.Message;
            Title = problem.Title ?? problem.Exception.GetType().Name;
            Instance = problem.Instance ?? GetHelpLink(problem.Exception);

            if (!string.IsNullOrEmpty(problem.Type))
            {
                Type = problem.Type;
            }

            Errors = new ErrorDetails(problem);
        }

        private static string? GetHelpLink(Exception exception)
        {
            var link = exception.HelpLink;

            if (string.IsNullOrEmpty(link))
            {
                return null;
            }

            if (Uri.TryCreate(link, UriKind.Absolute, out var result))
            {
                return result.ToString();
            }

            return null;
        }
    }
}
