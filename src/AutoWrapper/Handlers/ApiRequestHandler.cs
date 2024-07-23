using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AutoWrapper.Constants;
using AutoWrapper.Exceptions;
using AutoWrapper.Extensions;
using AutoWrapper.Interface;
using AutoWrapper.Models;
using HelpMate.Core.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;

namespace AutoWrapper.Handlers;

internal class ApiRequestHandler
    : ApiRequestHandlerMember
{
    private readonly AutoWrapperOptions _options;
    private readonly ILogger<ApiRequestHandler> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    internal ApiRequestHandler(
        AutoWrapperOptions options,
        ILoggerFactory loggerFactory,
        JsonSerializerOptions jsonOptions)
        : base(options, loggerFactory, jsonOptions)
    {
        _options = options;
        _logger = loggerFactory.CreateLogger<ApiRequestHandler>();
        _jsonOptions = jsonOptions;
    }

    public static bool IsRequestSuccessful(int statusCode)
        => statusCode is >= 200 and < 400;

    public async Task<string?> GetRequestBodyAsync(HttpRequest request)
    {
        var httpMethodsWithRequestBody = new[] { "POST", "PUT", "PATCH" };
        var hasRequestBody =
            Array.Exists(httpMethodsWithRequestBody, x => x.Equals(request.Method.ToUpper(CultureInfo.InvariantCulture)));
        string? requestBody = null;

        if (hasRequestBody)
        {
            request.EnableBuffering();

            using var memoryStream = new MemoryStream();
            await request.Body.CopyToAsync(memoryStream!).ConfigureAwait(continueOnCapturedContext: false);
            requestBody = Encoding.UTF8.GetString(memoryStream.ToArray());
            request.Body.Seek(0, SeekOrigin.Begin);
        }

        return requestBody;
    }

    public async Task<(bool IsEncoded, string ParsedText, JsonDocument? JsonDoc)> ReadResponseBodyStreamAsync(
        Stream bodyStream)
    {
        bodyStream.Seek(0, SeekOrigin.Begin);
        using var sr = new StreamReader(bodyStream!);
        var responseBody = await sr.ReadToEndAsync()
            .ConfigureAwait(continueOnCapturedContext: false);
        bodyStream.Seek(0, SeekOrigin.Begin);

        return responseBody.VerifyAndParseBodyContentToJson();
    }

    public async Task RevertResponseBodyStreamAsync(
        Stream bodyStream,
        Stream originalBodyStream)
    {
        bodyStream.Seek(0, SeekOrigin.Begin);
        await bodyStream.CopyToAsync(originalBodyStream!).ConfigureAwait(continueOnCapturedContext: false);
    }

    public async Task HandleExceptionAsync(
        HttpContext context,
        Exception exception)
    {
        switch (exception)
        {
            case ApiException:
                await HandleApiExceptionAsync(context, exception).ConfigureAwait(continueOnCapturedContext: false);
                break;
            case UnauthorizedAccessException:
                await HandleUnAuthorizedErrorAsync(context, exception).ConfigureAwait(continueOnCapturedContext: false);
                break;
            default:
                await HandleDefaultErrorAsync(context, exception).ConfigureAwait(continueOnCapturedContext: false);
                break;
        }
    }

    public async Task HandleUnsuccessfulRequestAsync(
        HttpContext context,
        object body,
        int httpStatusCode)
    {
        var isJsonShape = body.ToString()!.IsValidJson();
        var bodyText = body.ToString()!;
        var message = isJsonShape && !string.IsNullOrEmpty(bodyText) ? null : bodyText;

        var response = WrapErrorResponse(httpStatusCode!, message);
        var jsonString = JsonSerializer.Serialize(response, _jsonOptions);
        await WriteFormattedResponseToHttpContextAsync(context, httpStatusCode, jsonString)
            .ConfigureAwait(continueOnCapturedContext: false);
    }

    public async Task HandleSuccessfulRequestAsync(
        HttpContext context,
        string bodyAsText,
        int httpStatusCode,
        JsonDocument? jsonDocument)
    {
        string wrappedJsonString;

        if (jsonDocument is null || !bodyAsText.IsValidJson())
        {
            var (isValidated, validatedValue) = ValidateSingleValueType(bodyAsText);
            var result = isValidated ? validatedValue : bodyAsText;
            wrappedJsonString = ConvertToJSONString(httpStatusCode, result, context.Request.Method);

            await WriteFormattedResponseToHttpContextAsync(context, httpStatusCode, wrappedJsonString, jsonDocument)
                .ConfigureAwait(continueOnCapturedContext: false);
            return;
        }

        var root = jsonDocument.RootElement;

        if (root.ValueKind == JsonValueKind.Object || root.ValueKind == JsonValueKind.Array)
        {
            var endpoint = context.GetEndpoint();
            var actionDescriptor = endpoint?.Metadata?.GetMetadata<ControllerActionDescriptor>();

            if (actionDescriptor != null)
            {
                var returnType = actionDescriptor.MethodInfo.ReturnType;

                if (returnType == typeof(IApiResponse))
                {
                    await WriteFormattedResponseToHttpContextAsync(
                            context,
                            httpStatusCode,
                            root.GetRawText(),
                            jsonDocument)
                        .ConfigureAwait(continueOnCapturedContext: false);
                    return;
                }
            }

            wrappedJsonString = ConvertToJSONString(httpStatusCode, root, context.Request.Method);
            await WriteFormattedResponseToHttpContextAsync(context, httpStatusCode, wrappedJsonString, jsonDocument)
                .ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    public async Task HandleNotApiRequestAsync(HttpContext context)
    {
        var configErrorText = ResponseMessage.NotApiOnly;
        context.Response.ContentLength = Encoding.UTF8.GetByteCount(configErrorText!);
        await context.Response.WriteAsync(configErrorText!).ConfigureAwait(continueOnCapturedContext: false);
    }

    public bool ShouldIgnoreRequest(
        HttpContext context,
        IEnumerable<ExcludePath>? excludePaths)
    {
        if (IsDefaultSwaggerPath(context))
        {
            return true;
        }

        if (!IsApiRoute(context))
        {
            return true;
        }

        if (excludePaths is null || !excludePaths.Any())
        {
            return false;
        }

        return excludePaths.Any(
            x =>
            {
                var path = x.Path ?? string.Empty;

                return x.ExcludeMode switch
                {
                    ExcludeMode.Strict => context.Request.Path.Value?.Equals(x.Path, StringComparison.OrdinalIgnoreCase) ?? false,
                    ExcludeMode.StartsWith => context.Request.Path.StartsWithSegments(new PathString(x.Path), StringComparison.OrdinalIgnoreCase),
                    ExcludeMode.Regex => IsPathMatched(context, path),
                    _ => false,
                };
            });
    }

    public async Task WrapIgnoreAsync(
        HttpContext context,
        object body)
    {
        var bodyText = body.ToString();
        context.Response.ContentLength = bodyText != null ? Encoding.UTF8.GetByteCount(bodyText!) : 0;
        await context.Response.WriteAsync(bodyText!).ConfigureAwait(continueOnCapturedContext: false);
    }

    public async Task HandleProblemDetailsExceptionAsync(
        HttpContext context,
        IActionResultExecutor<ObjectResult> executor,
        object? body,
        Exception? exception = null)
    {
        await ApiProblemDetailsHandler.HandleProblemDetailsAsync(context, executor, body, exception, _options.IsDebug)
            .ConfigureAwait(continueOnCapturedContext: false);

        if (_options.EnableExceptionLogging && exception != null)
        {
            _logger.Log(
                LogLevel.Error,
                exception!,
                "[{StatusCode}]: {Message}",
                context.Response.StatusCode,
                exception.GetBaseException().Message);
        }
    }
}
