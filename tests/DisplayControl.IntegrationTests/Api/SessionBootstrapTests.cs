using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using DisplayControl.Api.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DisplayControl.IntegrationTests.Api;

public sealed class SessionBootstrapTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SessionBootstrapTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    [Fact]
    public async Task AnonymousBootstrapReturnsCsrfTokenAndHardenedCookie()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });

        using var response = await client.GetAsync("/api/v1/session");
        var body = await response.Content.ReadFromJsonAsync<SessionResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.False(body.Authenticated);
        Assert.True(body.MfaRequired);
        Assert.NotEmpty(body.CsrfToken);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("__Host-dc.csrf=", cookie, StringComparison.Ordinal);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnsafeCookieSurfaceRejectsMissingAntiforgeryTokenBeforeAuthenticationWork()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/sign-in",
            new { email = "unknown@example.test", password = "NotARealPassword123" });
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "antiforgery_validation_failed",
            body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RootServesEmbeddedAdminShellWithBrowserCompatibleStrictCsp()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<div id=\"root\"></div>", body, StringComparison.Ordinal);
        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-inline'", csp, StringComparison.Ordinal);
    }
}
