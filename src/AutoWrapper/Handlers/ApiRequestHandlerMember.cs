using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoWrapper.Constants;
using AutoWrapper.Exceptions;
using AutoWrapper.Models;
using HelpMate.Core.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace AutoWrapper.Handlers;

internal class ApiRequestHandlerMember
{
    private readonly AutoWrapperOptions _options;
    private readonly ILogger<ApiRequestHandlerMember> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public ApiRequestHandlerMember(
        AutoWrapperOptions options,
        ILoggerFactory loggerFactory,
        JsonSerializerOptions jsonOptions)
    {
        _options = options;
        _logger = loggerFactory.CreateLogger<ApiRequestHandlerMember>();
        _jsonOptions = jsonOptions;
    }

    protected static bool IsDefaultSwaggerPath(HttpContext context)
        => context.Request.Path.StartsWithSegments(new PathString("/swagger"), StringComparison.OrdinalIgnoreCase);

    protected static bool IsPathMatched(
        HttpContext context,
        string path)
    {
        var regExclue = new Regex(path, RegexOptions.None, TimeSpan.FromSeconds(1));
        return regExclue.IsMatch(context.Request.Path.Value!);
    }

    protected static async Task WriteFormattedResponseToHttpContextAsync(
        HttpContext context,
        int httpStatusCode,
        string jsonString,
        JsonDocument? jsonDocument = null)
    {
        context.Response.StatusCode = httpStatusCode;
        context.Response.ContentType = ContentMediaTypes.JSONHttpContentMediaType;
        context.Response.ContentLength = jsonString != null ? Encoding.UTF8.GetByteCount(jsonString!) : 0;

#pragma warning disable IDISP007
        jsonDocument?.Dispose();
#pragma warning restore IDISP007

        await context.Response.WriteAsync(jsonString!)
            .ConfigureAwait(continueOnCapturedContext: false);
    }

    protected static ApiErrorResponse WrapErrorResponse(
        int statusCode,
        string? message = null) =>
        statusCode switch
        {
            Status204NoContent => new ApiErrorResponse(
                message ?? ResponseMessage.NoContent,
                nameof(ResponseMessage.NoContent)),
            Status400BadRequest => new ApiErrorResponse(
                message ?? ResponseMessage.BadRequest,
                nameof(ResponseMessage.BadRequest)),
            Status401Unauthorized => new ApiErrorResponse(
                message ?? ResponseMessage.UnAuthorized,
                nameof(ResponseMessage.UnAuthorized)),
            Status404NotFound => new ApiErrorResponse(
                message ?? ResponseMessage.NotFound,
                nameof(ResponseMessage.NotFound)),
            Status405MethodNotAllowed => new ApiErrorResponse(
                ResponseMessage.MethodNotAllowed,
                nameof(ResponseMessage.MethodNotAllowed)),
            Status415UnsupportedMediaType => new ApiErrorResponse(
                ResponseMessage.MediaTypeNotSupported,
                nameof(ResponseMessage.MediaTypeNotSupported)),
            _ => new ApiErrorResponse(ResponseMessage.Unknown, nameof(ResponseMessage.Unknown)),
        };

    protected static ApiResponse WrapSuccessfulResponse(
        ApiResponse apiResponse,
        string httpMethod)
    {
        apiResponse.Message ??= $"{httpMethod} {ResponseMessage.Success}";
        return apiResponse;
    }

    protected static (bool IsValidated, object ValidatedValue) ValidateSingleValueType(object value)
    {
        var result = value.ToString() ?? string.Empty;
        if (result.IsWholeNumber())
        {
            return (true, result.ToInt64());
        }

        if (result.IsDecimalNumber())
        {
            return (true, result.ToDecimal());
        }

        if (result.IsBoolean())
        {
            return (true, result.ToBoolean());
        }

        if (result.Contains('"', StringComparison.OrdinalIgnoreCase))
        {
            return (true, result.Replace("\"", string.Empty, StringComparison.OrdinalIgnoreCase));
        }

        return (false, value!);
    }

    protected bool IsApiRoute(HttpContext context)
    {
        var fileTypes = new[] { ".js", ".html", ".css" };

        if (_options.IsApiOnly && !Array.Exists(fileTypes, s => context.Request.Path.Value!.Contains(s, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return context.Request.Path.StartsWithSegments(new PathString(_options.WrapWhenApiPathStartsWith), StringComparison.OrdinalIgnoreCase);
    }

    protected void LogExceptionWhenEnabled(
        Exception exception,
        string message,
        int statusCode)
    {
        if (_options.EnableExceptionLogging)
        {
            _logger.Log(LogLevel.Error, exception!, "[{StatusCode}]: {Message}", statusCode, message);
        }
    }

    protected async Task HandleApiExceptionAsync(
        HttpContext context,
        Exception exception)
    {
        var ex = exception as ApiException;

        if (ex?.ValidationErrors is not null)
        {
            await HandleValidationErrorAsync(context, ex)
                .ConfigureAwait(continueOnCapturedContext: false);
            return;
        }

        if (ex?.CustomErrorModel is not null)
        {
            await HandleCustomErrorAsync(context, ex)
                .ConfigureAwait(continueOnCapturedContext: false);
            return;
        }

        await HandleApiErrorAsync(context, ex!)
            .ConfigureAwait(continueOnCapturedContext: false);
    }

    protected async Task HandleUnAuthorizedErrorAsync(
        HttpContext context,
        Exception ex)
    {
        var response = new ApiErrorResponse(ResponseMessage.UnAuthorized);

        LogExceptionWhenEnabled(ex, ex.Message, Status401Unauthorized);

        var jsonString = JsonSerializer.Serialize(response, _jsonOptions!);

        await WriteFormattedResponseToHttpContextAsync(context!, Status401Unauthorized, jsonString!)
            .ConfigureAwait(continueOnCapturedContext: false);
    }

    protected async Task HandleDefaultErrorAsync(
        HttpContext context,
        Exception ex)
    {
        string? details = null;
        string message;

        if (_options.IsDebug)
        {
            message = ex.GetBaseException().Message;
            details = ex.StackTrace;
        }
        else
        {
            message = ResponseMessage.Unhandled;
        }

        var response = new ApiErrorResponse(message, ex.GetType().Name, details);

        LogExceptionWhenEnabled(ex, ex.Message, Status500InternalServerError);

        var jsonString = JsonSerializer.Serialize(response, _jsonOptions!);

        await WriteFormattedResponseToHttpContextAsync(context!, Status500InternalServerError, jsonString!)
            .ConfigureAwait(continueOnCapturedContext: false);
    }

    protected string ConvertToJSONString(
        int httpStatusCode,
        object content,
        string httpMethod)
    {
        var result = content.ToString() ?? string.Empty;
        var statusCode = (!_options.ShowStatusCode) ? null : (int?)httpStatusCode;
        var apiResponse = new ApiResponse($"{httpMethod} {ResponseMessage.Success}", content, statusCode);

        var serialized = JsonSerializer.Serialize(apiResponse, _jsonOptions!);

        return result.IsHtml() ? Regex.Unescape(serialized) : serialized;
    }

    protected string ConvertToJSONString(ApiResponse apiResponse)
        => JsonSerializer.Serialize(apiResponse!, _jsonOptions!);

    protected string ConvertToJSONString(object rawJSON) => JsonSerializer.Serialize(rawJSON!, _jsonOptions!);

    private async Task HandleValidationErrorAsync(
        HttpContext context,
        ApiException ex)
    {
        var response = new ApiErrorResponse(ex.ValidationErrors!);

        LogExceptionWhenEnabled(ex, ex.Message, ex.StatusCode);

        var jsonString = JsonSerializer.Serialize(response, _jsonOptions!);

        await WriteFormattedResponseToHttpContextAsync(context!, ex.StatusCode, jsonString!)
            .ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task HandleCustomErrorAsync(
        HttpContext context,
        ApiException ex)
    {
        var response = new ApiErrorResponse(ex.CustomErrorModel!);

        LogExceptionWhenEnabled(ex, ex.Message, ex.StatusCode);

        var jsonString = JsonSerializer.Serialize(response, _jsonOptions!);

        await WriteFormattedResponseToHttpContextAsync(context!, ex.StatusCode, jsonString!)
            .ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task HandleApiErrorAsync(
        HttpContext context,
        ApiException ex)
    {
        var response = new ApiErrorResponse(ex.Message, ex.ErrorCode ?? ex.GetType().Name);

        LogExceptionWhenEnabled(ex, ex.Message, ex.StatusCode);

        var jsonString = JsonSerializer.Serialize(response, _jsonOptions!);

        await WriteFormattedResponseToHttpContextAsync(context!, ex.StatusCode, jsonString!)
            .ConfigureAwait(continueOnCapturedContext: false);
    }
}
