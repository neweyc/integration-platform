using System.Net;
using Serto.Connectors.Http;
using Serto.Testing;

namespace Connectors.Tests;

public class TestingHelpersTests
{
    [Fact]
    public void TestContextBuilder_SetsSecretsAndPayload()
    {
        var context = new TestContextBuilder()
            .WithSecret("API_TOKEN", "token-value")
            .WithPayload("body")
            .Build();

        Assert.Equal("token-value", context.Secrets["API_TOKEN"]);
        Assert.Equal("body", context.Payload);
    }

    [Fact]
    public async Task TestHttp_RespondingJson_FeedsTheConnector()
    {
        var context = new TestContextBuilder()
            .WithHttp(TestHttp.RespondingJson(new { ok = true }))
            .Build();

        var result = await context.HttpConnector("https://api.example.com")
            .GetJsonAsync<HelperResponse>("/data");

        Assert.True(result!.Ok);
    }

    [Fact]
    public async Task TestHttp_Recording_CapturesRequests()
    {
        var http = TestHttp.Recording(out var handler, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var context = new TestContextBuilder().WithHttp(http).Build();

        await context.HttpConnector("https://api.example.com").DeleteAsync("/orders/1");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("https://api.example.com/orders/1", request.RequestUri!.ToString());
    }

    private sealed record HelperResponse(bool Ok);
}
