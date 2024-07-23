using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AutoFixture;
using AutoWrapper.Constants;
using AutoWrapper.Exceptions;
using AutoWrapper.Models;
using AutoWrapper.Models.ResponseTypes;
using AutoWrapper.Tests.Extensions;
using AutoWrapper.Tests.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AutoWrapper.Tests;

public class AutoWrapperMiddlewareTests
{
    private readonly Fixture _fixture = new();

    private readonly JsonSerializerOptions _jsonSerializerOptionsDefault = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    [Fact]
    public async Task WhenResult_IsEmpty_Returns_200Async()
    {
        var webhostBuilder = new WebHostBuilder()
            .ConfigureAutoWrapper(context => Task.CompletedTask);

        using var server = new TestServer(webhostBuilder);

        using var request = new HttpRequestMessage(HttpMethod.Get, string.Empty);
        using var client = server.CreateClient();
        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        var apiResponse = ApiResponseBuilder($"{request.Method} {ResponseMessage.Success}", string.Empty);

        var expectedJson = apiResponse.ToJson(_jsonSerializerOptionsDefault);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be(expectedJson);
    }

    [Fact]
    public async Task WhenResult_HasData_Returns_200Async()
    {
        var testData = "HueiFeng";

        var webhostBuilder = new WebHostBuilder()
            .ConfigureAutoWrapper(context => context.Response.WriteAsync(testData));

        using var server = new TestServer(webhostBuilder);

        using var request = new HttpRequestMessage(HttpMethod.Get, string.Empty);
        using var client = server.CreateClient();
        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        var apiResponse = ApiResponseBuilder($"{request.Method} {ResponseMessage.Success}", testData);

        var expectedJson = apiResponse.ToJson(_jsonSerializerOptionsDefault);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be(expectedJson);
    }

    [Fact]
    public async Task WhenCustomModel_MatchesTheResponse_Returns200Async()
    {
        var testData = new TestModel
        {
            Id = Guid.NewGuid(),
            FirstName = _fixture.Create<string>(),
            LastName = _fixture.Create<string>(),
            DateOfBirth = _fixture.Create<DateTime>(),
        };

        var webhostBuilder = new WebHostBuilder()
            .ConfigureAutoWrapper(
                context => context.Response.WriteAsync(testData.ToJson(_jsonSerializerOptionsDefault)));

        using var server = new TestServer(webhostBuilder);

        using var request = new HttpRequestMessage(HttpMethod.Get, string.Empty);
        using var client = server.CreateClient();
        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        var apiResponse = ApiResponseBuilder($"{request.Method} {ResponseMessage.Success}", testData);
        var expectedJson = apiResponse.ToJson(_jsonSerializerOptionsDefault);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be(expectedJson);
    }

    [Fact(DisplayName = "CapturingModelStateApiException")]
    public async Task AutoWrapperCapturingModelState_ApiException_TestAsync()
    {
        var dictionary = new ModelStateDictionary();
        dictionary.AddModelError("name", "some error");
        var builder = new WebHostBuilder()
            .ConfigureServices(services => { services.AddMvcCore(); })
            .Configure(app =>
            {
                app.UseAutoWrapper();

                app.Run(context => throw new ApiException(dictionary["name"]));
            });
        Exception ex;
        try
        {
            throw new ApiException(dictionary["name"]);
        }
        catch (Exception e)
        {
            ex = e;
        }

        using var server = new TestServer(builder);
        using var req = new HttpRequestMessage(HttpMethod.Get, string.Empty);
        using var rep = await server.CreateClient().SendAsync(req);
        var content = await rep.Content.ReadAsStringAsync();
        ex.Should().NotBeNull();
        content.Should().NotBeNull();
    }

    [Fact(DisplayName = "CapturingModelStateApiProblemDetailsException")]
    public async Task AutoWrapperCapturingModelState_ApiProblemDetailsException_TestAsync()
    {
        var dictionary = new ModelStateDictionary();
        dictionary.AddModelError("name", "some error");
        var builder = new WebHostBuilder()
            .ConfigureServices(services => { services.AddMvcCore(); })
            .Configure(app =>
            {
                app.UseAutoWrapper(new AutoWrapperOptions { DisableProblemDetailsException = false });
                app.Run(context => throw new ApiProblemDetailsException(dictionary));
            });

        using var server = new TestServer(builder);

        using var request = new HttpRequestMessage(HttpMethod.Get, string.Empty);
        using var client = server.CreateClient();
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var content = await response.Content.ReadAsStringAsync();

        var actual = JsonSerializer.Deserialize<ApiProblemDetailsValidationErrorResponse>(content);

        actual?.Detail.Should().Be("Your request parameters did not validate.");
    }

    [Fact(DisplayName = "ThrowingExceptionMessageApiException", Skip = "for now")]
    public async Task AutoWrapperThrowingExceptionMessage_ApiException_TestAsync()
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services => services.AddMvcCore())
            .Configure(app =>
            {
                var options = new AutoWrapperOptions
                {
                    DisableProblemDetailsException = false,
                    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                };
                app.UseAutoWrapper(options);
                app.Run(_ => throw new ApiException("does not exist.", 404, string.Empty));
            });

        using var server = new TestServer(builder);
        using var req = new HttpRequestMessage(HttpMethod.Get, string.Empty);
        using var client = server.CreateClient();
        using var rep = await client.SendAsync(req);
        var content = await rep.Content.ReadAsStringAsync();
        rep.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var json = JsonHelper.ToJson(
            new ApiProblemDetailsExceptionResponse
            {
                IsError = true,
                Errors = null,
                Extensions = { ["validationErrors"] = null },
                Type = "https://httpstatuses.com/500",
                Title = "Internal Server Error",
                Status = 500,
                Detail = "does not exist.",
                Instance = "/",
            },
            null);
        content.Should().Be(json);
    }

    [Fact(DisplayName = "ThrowingExceptionMessageApiProblemDetailsException")]
    public async Task AutoWrapperThrowingExceptionMessage_ApiProblemDetailsException_TestAsync()
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services => { services.AddMvcCore(); })
            .Configure(app =>
            {
                app.UseAutoWrapper(new AutoWrapperOptions { DisableProblemDetailsException = false });
                app.Run(context => throw new ApiProblemDetailsException("does not exist.", 404));
            });
        using var server = new TestServer(builder);
        using var request = new HttpRequestMessage(HttpMethod.Get, string.Empty);
        using var client = server.CreateClient();
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

#pragma warning disable S1144 // Unused private types or members should be removed
    private IWebHostBuilder ConfigureAutoWrapper(
        IWebHostBuilder webHostBuilder,
        RequestDelegate requestDelegate)
#pragma warning restore S1144 // Unused private types or members should be removed
    {
        return webHostBuilder
            .ConfigureServices(services => { services.AddControllers(); })
            .Configure(
                app =>
                {
                    app.UseAutoWrapper();
                    app.Run(requestDelegate);
                });
    }

    private ApiResponse ApiResponseBuilder(
        string message,
        object result,
        bool showStatusCode = false,
        int statusCode = 200)
    {
        if (showStatusCode)
        {
            return _fixture.Build<ApiResponse>()
                .Without(p => p.IsError)
                .With(p => p.StatusCode, statusCode)
                .With(p => p.Message, message)
                .With(p => p.Result, result)
                .Create();
        }

        return _fixture.Build<ApiResponse>()
            .Without(p => p.IsError)
            .Without(p => p.StatusCode)
            .With(p => p.Message, message)
            .With(p => p.Result, result)
            .Create();
    }
}
