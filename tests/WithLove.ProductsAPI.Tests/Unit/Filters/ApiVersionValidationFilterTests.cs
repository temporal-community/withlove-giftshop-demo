namespace WithLove.ProductsAPI.Tests.Unit.Filters;

using FakeItEasy;
using WithLove.ProductsAPI.Filters;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Unit tests for ApiVersionValidationFilter.
/// Focus: Validation of X-WITHLOVE-API-VERSION header format (YYYY-MM-DD).
/// Uses FakeItEasy to mock dependencies.
/// </summary>
public class ApiVersionValidationFilterTests
{
    private readonly ILogger<ApiVersionValidationFilter> _fakeLogger;
    private readonly ApiVersionValidationFilter _filter;

    public ApiVersionValidationFilterTests()
    {
        _fakeLogger = A.Fake<ILogger<ApiVersionValidationFilter>>();
        _filter = new ApiVersionValidationFilter(_fakeLogger);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ApiVersion)]
    public async Task InvokeAsync_WithValidVersionHeader_CallsNext()
    {
        // Arrange
        var context = CreateEndpointFilterInvocationContext(
            addHeader: true,
            headerValue: "2026-02-25");

        var nextCalled = false;
        var next = new EndpointFilterDelegate(_ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(null);
        });

        // Act
        await _filter.InvokeAsync(context, next);

        // Assert
        nextCalled.Should().BeTrue();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ApiVersion)]
    public async Task InvokeAsync_WithMissingVersionHeader_ReturnsBadRequest()
    {
        // Arrange
        var context = CreateEndpointFilterInvocationContext(addHeader: false);

        var next = A.Fake<EndpointFilterDelegate>();

        // Act
        var result = await _filter.InvokeAsync(context, next);

        // Assert
        result.Should().NotBeNull();
        // Verify next was not called
        A.CallTo(() => next(A<EndpointFilterInvocationContext>._))
            .MustNotHaveHappened();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ApiVersion)]
    public async Task InvokeAsync_WithEmptyVersionHeader_ReturnsBadRequest()
    {
        // Arrange
        var context = CreateEndpointFilterInvocationContext(
            addHeader: true,
            headerValue: "");

        var next = A.Fake<EndpointFilterDelegate>();

        // Act
        var result = await _filter.InvokeAsync(context, next);

        // Assert
        result.Should().NotBeNull();
        // Verify next was not called
        A.CallTo(() => next(A<EndpointFilterInvocationContext>._))
            .MustNotHaveHappened();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ApiVersion)]
    public async Task InvokeAsync_WithWhitespaceVersionHeader_ReturnsBadRequest()
    {
        // Arrange
        var context = CreateEndpointFilterInvocationContext(
            addHeader: true,
            headerValue: "   ");

        var next = A.Fake<EndpointFilterDelegate>();

        // Act
        var result = await _filter.InvokeAsync(context, next);

        // Assert
        result.Should().NotBeNull();
    }

    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ApiVersion)]
    [InlineData("2026-02-25")] // Valid
    [InlineData("2025-01-01")] // Valid
    [InlineData("2000-12-31")] // Valid
    public async Task InvokeAsync_WithValidDateFormats_CallsNext(string validDate)
    {
        // Arrange
        var context = CreateEndpointFilterInvocationContext(
            addHeader: true,
            headerValue: validDate);

        var nextCalled = false;
        var next = new EndpointFilterDelegate(_ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(null);
        });

        // Act
        await _filter.InvokeAsync(context, next);

        // Assert
        nextCalled.Should().BeTrue();
    }

    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ApiVersion)]
    [InlineData("2026/02/25")]     // Wrong separator
    [InlineData("02-25-2026")]     // Wrong order
    [InlineData("2026-2-25")]      // Missing leading zero
    [InlineData("2026-02-5")]      // Missing leading zero
    [InlineData("26-02-25")]       // Wrong year format
    [InlineData("invalid")]        // Not a date
    [InlineData("2026-02")]        // Missing day
    [InlineData("2026")]           // Missing month and day
    public async Task InvokeAsync_WithInvalidDateFormats_ReturnsBadRequest(string invalidDate)
    {
        // Arrange
        var context = CreateEndpointFilterInvocationContext(
            addHeader: true,
            headerValue: invalidDate);

        var next = A.Fake<EndpointFilterDelegate>();

        // Act
        var result = await _filter.InvokeAsync(context, next);

        // Assert
        result.Should().NotBeNull();
        // Verify next was not called
        A.CallTo(() => next(A<EndpointFilterInvocationContext>._))
            .MustNotHaveHappened();
    }

    private static EndpointFilterInvocationContext CreateEndpointFilterInvocationContext(
        bool addHeader = false,
        string? headerValue = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/products";

        if (addHeader && headerValue != null)
        {
            httpContext.Request.Headers["X-WITHLOVE-API-VERSION"] = headerValue;
        }

        var context = A.Fake<EndpointFilterInvocationContext>();
        A.CallTo(() => context.HttpContext).Returns(httpContext);

        return context;
    }
}
