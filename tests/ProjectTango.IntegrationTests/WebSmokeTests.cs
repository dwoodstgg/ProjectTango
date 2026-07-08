using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ProjectTango.IntegrationTests;

public sealed class WebSmokeTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private HttpClient CreateClient() =>
        // WebApplicationFactory runs in the Development environment, where
        // MigrateOnStartup is on; these tests boot the host without a database.
        factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Database:MigrateOnStartup", "false"))
        .CreateClient();

    [Fact]
    public async Task Home_page_returns_success()
    {
        var response = await CreateClient().GetAsync("/");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Health_endpoint_returns_success()
    {
        var response = await CreateClient().GetAsync("/health");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Api_v1_meta_returns_success()
    {
        var response = await CreateClient().GetAsync("/api/v1/meta");

        response.EnsureSuccessStatusCode();
    }
}
