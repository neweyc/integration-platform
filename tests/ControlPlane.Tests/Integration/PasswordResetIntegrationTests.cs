using System.Net;
using System.Net.Http.Json;

namespace ControlPlane.Tests.IntegrationTests;

public class PasswordResetIntegrationTests
{
    [Fact]
    public async Task ForgotPassword_ReturnsNoContent_ForBothKnownAndUnknownEmails()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        // Create a tenant + admin so one known email exists.
        var adminEmail = $"admin-{Guid.NewGuid():N}@acme.com";
        using var setup = await client.PostAsJsonAsync("/api/setup", new
        {
            tenantName = "Acme Corp",
            tenantSlug = $"acme-{Guid.NewGuid():N}".Substring(0, 20),
            adminEmail,
            adminPassword = "securepass123"
        });
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);

        // Known and unknown emails both return 204 — no enumeration signal.
        using var known = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = adminEmail });
        using var unknown = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "nobody@acme.com" });

        Assert.Equal(HttpStatusCode.NoContent, known.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WithInvalidToken_Returns400()
    {
        await using var database = await IntegrationTestDatabase.CreateAsync();
        if (database is null)
            return;

        await using var factory = new ControlPlaneWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/reset-password",
            new { token = "not-a-real-token", newPassword = "brand-new-pass" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
