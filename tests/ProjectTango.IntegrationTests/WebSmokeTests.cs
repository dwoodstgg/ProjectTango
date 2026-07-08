using Microsoft.AspNetCore.Mvc.Testing;

namespace ProjectTango.IntegrationTests;

public sealed class WebSmokeTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Home_page_returns_success()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Health_endpoint_returns_success()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Api_v1_meta_returns_success()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/meta");

        response.EnsureSuccessStatusCode();
    }
}
