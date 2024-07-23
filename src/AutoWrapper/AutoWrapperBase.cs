using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AutoWrapper.Attributes;
using AutoWrapper.Handlers;
using HelpMate.Core.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace AutoWrapper.Base;

internal abstract class AutoWrapperBase
{
    private readonly RequestDelegate _next;
    private readonly AutoWrapperOptions _options;
    private readonly ILogger<AutoWrapperBase> _logger;
    private bool _isRequestOk;

    private protected AutoWrapperBase(
        RequestDelegate next,
        AutoWrapperOptions options,
        ILoggerFactory loggerFactory,
        IActionResultExecutor<ObjectResult> executor)
    {
        _next = next;
        _options = options;
        _logger = loggerFactory.CreateLogger<AutoWrapperBase>();
        Executor = executor;
    }

    private IActionResultExecutor<ObjectResult> Executor { get; }

    public virtual async Task InvokeBaseAsync(
        HttpContext context,
        ApiRequestHandler requestHandler)
    {
        if (requestHandler.ShouldIgnoreRequest(context, _options.ExcludePaths))
        {
            await _next(context).ConfigureAwait(continueOnCapturedContext: false);
        }
        else
        {
            await InvokeNextAsync(context, requestHandler).ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    private async Task InvokeNextAsync(HttpContext context, ApiRequestHandler requestHandler)
    {
        if (context.Response.HasStarted)
        {
            LogResponseHasStartedError();
            return;
        }

        var stopWatch = Stopwatch.StartNew();
        var requestBody = await requestHandler.GetRequestBodyAsync(context.Request).ConfigureAwait(continueOnCapturedContext: false);
        var originalResponseBodyStream = context.Response.Body;

        using var memoryStream = new MemoryStream();

        try
        {
            context.Response.Body = memoryStream;
            await _next.Invoke(context).ConfigureAwait(continueOnCapturedContext: false);

            var endpoint = context.GetEndpoint();

            if (endpoint?.Metadata?.GetMetadata<AutoWrapIgnoreAttribute>() is object)
            {
                await requestHandler.RevertResponseBodyStreamAsync(memoryStream, originalResponseBodyStream).ConfigureAwait(continueOnCapturedContext: false);
                return;
            }

            if (context.Response.StatusCode != Status304NotModified && context.Response.StatusCode != Status204NoContent)
            {
                await HandleRequestAsync(context, requestHandler, memoryStream, originalResponseBodyStream).ConfigureAwait(continueOnCapturedContext: false);
            }
        }
        catch (Exception exception)
        {
            if (_options.DisableProblemDetailsException)
            {
                await requestHandler.HandleExceptionAsync(context, exception).ConfigureAwait(continueOnCapturedContext: false);
            }
            else
            {
                await requestHandler.HandleProblemDetailsExceptionAsync(context, Executor, null, exception)
                    .ConfigureAwait(continueOnCapturedContext: false);
            }

            await requestHandler.RevertResponseBodyStreamAsync(memoryStream, originalResponseBodyStream)
                .ConfigureAwait(continueOnCapturedContext: false);
        }
        finally
        {
            LogHttpRequest(context, requestBody, stopWatch, _isRequestOk);
        }
    }

    private async Task HandleRequestAsync(HttpContext context, ApiRequestHandler requestHandler, MemoryStream memoryStream, Stream bodyStream)
    {
        var (_, parsedText, jsonDoc) = await requestHandler.ReadResponseBodyStreamAsync(memoryStream)
            .ConfigureAwait(continueOnCapturedContext: false);
        var bodyAsText = parsedText;

        context.Response.Body = bodyStream;

        var isPageRequest = !_options.IsApiOnly
                            && (bodyAsText.IsHtml()
                                && !_options.BypassHTMLValidation)
                            && context.Response.StatusCode == Status200OK;

        if (isPageRequest)
        {
            context.Response.StatusCode = Status404NotFound;
        }

        if (isPageRequest &&
            !context.Request.Path.StartsWithSegments(new PathString(_options.WrapWhenApiPathStartsWith), StringComparison.OrdinalIgnoreCase) &&
            memoryStream.Length > 0)
        {
            await requestHandler.HandleNotApiRequestAsync(context).ConfigureAwait(continueOnCapturedContext: false);
            return;
        }

        _isRequestOk = ApiRequestHandler.IsRequestSuccessful(context.Response.StatusCode);

        if (_isRequestOk)
        {
            if (_options.IgnoreWrapForOkRequests)
            {
                await requestHandler.WrapIgnoreAsync(context, bodyAsText)
                    .ConfigureAwait(continueOnCapturedContext: false);
                return;
            }

            await requestHandler.HandleSuccessfulRequestAsync(context, bodyAsText, context.Response.StatusCode, jsonDoc)
                .ConfigureAwait(continueOnCapturedContext: false);
            return;
        }

        if (_options.DisableProblemDetailsException)
        {
            await requestHandler.HandleUnsuccessfulRequestAsync(context, bodyAsText, context.Response.StatusCode)
                .ConfigureAwait(continueOnCapturedContext: false);
            return;
        }

        await requestHandler.HandleProblemDetailsExceptionAsync(context, Executor, bodyAsText)
            .ConfigureAwait(continueOnCapturedContext: false);
    }

    private bool ShouldLogRequestData(HttpContext context)
    {
        if (_options.ShouldLogRequestData)
        {
            var endpoint = context.GetEndpoint();
            return !(endpoint?.Metadata?.GetMetadata<RequestDataLogIgnoreAttribute>() is object);
        }

        return false;
    }

    private void LogHttpRequest(HttpContext context, string? requestBody, Stopwatch stopWatch, bool isRequestOk)
    {
        stopWatch.Stop();
        if (_options.EnableResponseLogging)
        {
            var shouldLogRequestData = ShouldLogRequestData(context);

            string request;
            if (shouldLogRequestData)
            {
                if (isRequestOk)
                {
                    request = $"{context.Request.Method} {context.Request.Scheme} {context.Request.Host}{context.Request.Path} {context.Request.QueryString} {requestBody}";
                }
                else if (_options.LogRequestDataOnException)
                {
                    request = $"{context.Request.Method} {context.Request.Scheme} {context.Request.Host}{context.Request.Path} {context.Request.QueryString} {requestBody}";
                }
                else
                {
                    request = $"{context.Request.Method} {context.Request.Scheme} {context.Request.Host}{context.Request.Path}";
                }
            }
            else
            {
                request = $"{context.Request.Method} {context.Request.Scheme} {context.Request.Host}{context.Request.Path}";
            }

            _logger.Log(
                LogLevel.Information,
                "Source:[{RemoteIpAddress}] Request: {Request} Responded with [{StatusCode}] in {ElapsedMilliseconds}ms",
                context.Connection.RemoteIpAddress,
                request,
                context.Response.StatusCode,
                stopWatch.ElapsedMilliseconds);
        }
    }

    private void LogResponseHasStartedError()
    {
        _logger.Log(LogLevel.Warning, "The response has already started, the AutoWrapper middleware will not be executed.");
    }
}
