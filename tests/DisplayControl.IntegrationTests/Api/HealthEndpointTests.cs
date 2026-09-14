using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DisplayControl.IntegrationTests.Api;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    [Fact]
    public async Task LiveEndpointReturnsOnlyGenericHealthAndSecurityHeaders()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/_health/live");
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Equal("{\"status\":\"healthy\"}", body);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.False(string.IsNullOrWhiteSpace(response.Headers.GetValues("X-Correlation-ID").Single()));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.DoesNotContain("version", body, StringComparison.OrdinalIgnoreCase);
    }
}
