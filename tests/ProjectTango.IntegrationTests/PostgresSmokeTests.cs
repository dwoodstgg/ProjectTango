using Microsoft.EntityFrameworkCore;
using ProjectTango.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace ProjectTango.IntegrationTests;

public sealed class PostgresSmokeTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task TangoDbContext_connects_to_postgres()
    {
        var options = new DbContextOptionsBuilder<TangoDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var db = new TangoDbContext(options);

        Assert.True(await db.Database.CanConnectAsync());
    }
}
