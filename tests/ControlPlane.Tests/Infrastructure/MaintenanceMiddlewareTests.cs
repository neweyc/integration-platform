using ControlPlane.Infrastructure.Maintenance;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ControlPlane.Tests.Infrastructure;

public class MaintenanceMiddlewareTests
{
    private static (MaintenanceMiddleware Middleware, Func<bool> WasNextCalled) Build(bool enabled)
    {
        var called = false;
        var options = Substitute.For<IOptionsMonitor<MaintenanceOptions>>();
        options.CurrentValue.Returns(new MaintenanceOptions { Enabled = enabled });
        var middleware = new MaintenanceMiddleware(_ => { called = true; return Task.CompletedTask; }, options);
        return (middleware, () => called);
    }

    private static DefaultHttpContext Context(string method)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Enabled_BlocksWrites_With503(string method)
    {
        var (middleware, wasNextCalled) = Build(enabled: true);
        var context = Context(method);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.False(wasNextCalled());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task Enabled_AllowsSafeMethods(string method)
    {
        var (middleware, wasNextCalled) = Build(enabled: true);

        await middleware.InvokeAsync(Context(method));

        Assert.True(wasNextCalled());
    }

    [Fact]
    public async Task Disabled_AllowsWrites()
    {
        var (middleware, wasNextCalled) = Build(enabled: false);

        await middleware.InvokeAsync(Context("POST"));

        Assert.True(wasNextCalled());
    }
}
