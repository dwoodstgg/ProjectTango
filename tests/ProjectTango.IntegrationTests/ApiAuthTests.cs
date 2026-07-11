using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ProjectTango.IntegrationTests;

/// <summary>The /api/v1 resource controllers authenticate via JWT bearer (<see cref="Program"/>'s
/// ApiControllerBase). An unauthenticated call must be rejected with 401 — proving the bearer
/// scheme is selected over the UI's cookie/redirect scheme — before any controller or database
/// work runs. These boot the host without a database (auth fails first).</summary>
public sealed class ApiAuthTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private HttpClient CreateClient() =>
        factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Database:MigrateOnStartup", "false"))
        .CreateClient();

    [Fact]
    public async Task Timesheet_endpoint_requires_bearer_token()
    {
        var response = await CreateClient().GetAsync("/api/v1/timesheet?from=2026-01-01&to=2026-01-31");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Time_entries_endpoint_requires_bearer_token()
    {
        var response = await CreateClient().PostAsJsonAsync(
            "/api/v1/time-entries",
            new { projectId = Guid.NewGuid(), date = "2026-01-05", hours = 1.0m, billingRoleId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Openapi_document_is_published()
    {
        // The OpenAPI document is the contract for API/mobile clients and is served anonymously.
        var response = await CreateClient().GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();
    }
}
