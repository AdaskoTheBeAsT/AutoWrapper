using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AutoWrapper.Tests;

public static class WebHostBuilderExtension
{
    public static IWebHostBuilder ConfigureAutoWrapper(
        this IWebHostBuilder webHostBuilder,
        RequestDelegate requestDelegate)
    {
        return webHostBuilder
            .ConfigureServices(
                services =>
                {
                    services.AddControllers();
                })
            .Configure(
                app =>
                {
                    app.UseAutoWrapper();
                    app.Run(requestDelegate);
                });
    }
}
