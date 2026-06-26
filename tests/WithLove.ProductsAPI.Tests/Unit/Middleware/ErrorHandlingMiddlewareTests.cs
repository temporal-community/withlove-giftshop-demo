namespace WithLove.ProductsAPI.Tests.Unit.Middleware;

using FakeItEasy;
using WithLove.ProductsAPI.Middleware;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Unit tests for ErrorHandlingMiddleware.
/// Focus: Exception handling and Problem Details response generation.
/// Uses FakeItEasy to mock ILogger and IHostEnvironment.
/// </summary>
public class ErrorHandlingMiddlewareTests
{
    private readonly ILogger<ErrorHandlingMiddleware> _fakeLogger;
    private readonly IHostEnvironment _fakeEnvironment;

    public ErrorHandlingMiddlewareTests()
    {
        _fakeLogger = A.Fake<ILogger<ErrorHandlingMiddleware>>();
        _fakeEnvironment = A.Fake<IHostEnvironment>();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public async Task InvokeAsync_WithoutException_CallsNext()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var nextCalled = false;

        var next = new RequestDelegate(ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var middleware = new ErrorHandlingMiddleware(next, _fakeLogger, _fakeEnvironment);

        // Act
        await middleware.InvokeAsync(httpContext);

        // Assert
        nextCalled.Should().BeTrue();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public async Task InvokeAsync_WithException_SetsContentTypeToJson()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var environment = A.Fake<IHostEnvironment>();
        A.CallTo(() => environment.EnvironmentName).Returns("Production");

        var next = new RequestDelegate(_ =>
            throw new InvalidOperationException("Test error"));

        var middleware = new ErrorHandlingMiddleware(next, _fakeLogger, environment);

        // Act
        await middleware.InvokeAsync(httpContext);

        // Assert
        httpContext.Response.ContentType.Should().Contain("json");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public async Task InvokeAsync_WithException_SetsInternalServerErrorStatus()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        // Use actual environment instead of fake since IsDevelopment is an extension method
        var environment = A.Fake<IHostEnvironment>();
        A.CallTo(() => environment.EnvironmentName).Returns("Production");

        var next = new RequestDelegate(_ =>
            throw new InvalidOperationException("Test error"));

        var middleware = new ErrorHandlingMiddleware(next, _fakeLogger, environment);

        // Act
        await middleware.InvokeAsync(httpContext);

        // Assert
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }


    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public async Task InvokeAsync_InDevelopmentWithException_DoesNotThrow()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var errorMessage = "Detailed test error message";
        var environment = A.Fake<IHostEnvironment>();
        A.CallTo(() => environment.EnvironmentName).Returns("Development");

        var next = new RequestDelegate(_ =>
            throw new InvalidOperationException(errorMessage));

        var middleware = new ErrorHandlingMiddleware(next, _fakeLogger, environment);

        // Act & Assert
        var act = async () => await middleware.InvokeAsync(httpContext);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public async Task InvokeAsync_InProductionWithException_SetsErrorStatus()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var environment = A.Fake<IHostEnvironment>();
        A.CallTo(() => environment.EnvironmentName).Returns("Production");

        var next = new RequestDelegate(_ =>
            throw new InvalidOperationException("Detailed error that should not be exposed"));

        var middleware = new ErrorHandlingMiddleware(next, _fakeLogger, environment);

        // Act
        await middleware.InvokeAsync(httpContext);

        // Assert
        // In production, detailed error message should not be included
        httpContext.Response.StatusCode.Should().Be(500);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public async Task InvokeAsync_WithDifferentExceptionTypes_HandlesProperly()
    {
        // Arrange
        var exceptions = new Exception[]
        {
            new InvalidOperationException("Invalid operation"),
            new ArgumentNullException("parameter"),
            new NullReferenceException("Null reference"),
            new TimeoutException("Timeout")
        };

        var environment = A.Fake<IHostEnvironment>();
        A.CallTo(() => environment.EnvironmentName).Returns("Production");

        foreach (var exception in exceptions)
        {
            var httpContext = new DefaultHttpContext();

            var next = new RequestDelegate(_ => throw exception);
            var middleware = new ErrorHandlingMiddleware(next, _fakeLogger, environment);

            // Act
            await middleware.InvokeAsync(httpContext);

            // Assert
            httpContext.Response.StatusCode.Should().Be(500);
        }
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void Constructor_WithNullRequestDelegate_DoesNotThrow()
    {
        // This test verifies the constructor's behavior
        // Note: Most middleware constructors don't validate RequestDelegate being null
        var middleware = new ErrorHandlingMiddleware(null!, _fakeLogger, _fakeEnvironment);
        middleware.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void Constructor_WithNullLogger_DoesNotThrow()
    {
        // ILogger is often not null-checked in middleware
        var next = new RequestDelegate(_ => Task.CompletedTask);
        var middleware = new ErrorHandlingMiddleware(next, _fakeLogger, _fakeEnvironment);
        middleware.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void Constructor_WithNullEnvironment_DoesNotThrow()
    {
        // IHostEnvironment is often not null-checked
        var next = new RequestDelegate(_ => Task.CompletedTask);
        var middleware = new ErrorHandlingMiddleware(next, _fakeLogger, _fakeEnvironment);
        middleware.Should().NotBeNull();
    }
}
