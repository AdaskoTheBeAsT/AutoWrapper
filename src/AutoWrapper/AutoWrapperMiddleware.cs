using System.Threading.Tasks;
using AutoWrapper.Base;
using AutoWrapper.Handlers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;

namespace AutoWrapper;

internal class AutoWrapperMiddleware
    : AutoWrapperBase
{
    private readonly ApiRequestHandler _handler;

    public AutoWrapperMiddleware(
        RequestDelegate next,
        AutoWrapperOptions options,
        ILoggerFactory loggerFactory,
        IActionResultExecutor<ObjectResult> executor)
        : base(next, options, loggerFactory, executor)
    {
        var jsonOptions = Configurations
            .JsonSettingsConfiguration
            .GetJsonSerializerOptions(options.JsonNamingPolicy, options.DefaultIgnoreCondition);

        _handler = new ApiRequestHandler(options, loggerFactory, jsonOptions);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await InvokeBaseAsync(context, _handler).ConfigureAwait(continueOnCapturedContext: false);
    }
}
