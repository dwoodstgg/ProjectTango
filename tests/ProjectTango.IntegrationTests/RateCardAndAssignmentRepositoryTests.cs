using Dapper;
using Npgsql;
using ProjectTango.Domain.Entities;
using ProjectTango.Infrastructure.Persistence;
using ProjectTango.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ProjectTango.IntegrationTests;

public sealed class RateCardAndAssignmentRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private NpgsqlDataSource _dataSource = null!;
    private RateCardRepository _rateCards = null!;
    private AssignmentRepository _assignments = null!;

    public async Task InitializeAsync()
    {
        DapperConfig.Apply();
        await _postgres.StartAsync();
        DatabaseMigrator.MigrateToLatest(_postgres.GetConnectionString());
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        _rateCards = new RateCardRepository(_dataSource);
        _assignments = new AssignmentRepository(_dataSource);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Rate_lifecycle_close_then_resolve_at_boundaries()
    {
        var first = new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            RoleId = SeedData.DeveloperRoleId,
            HourlyRate = 150.00m,
            EffectiveFrom = new DateOnly(2026, 1, 1),
        };
        await _rateCards.AddAsync(first);
        await _rateCards.CloseAsync(first.Id, new DateOnly(2026, 6, 30));
        await _rateCards.AddAsync(new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            RoleId = SeedData.DeveloperRoleId,
            HourlyRate = 165.00m,
            EffectiveFrom = new DateOnly(2026, 7, 1),
        });

        Assert.Null(await _rateCards.ResolveAsync(SeedData.LeaveProjectId, SeedData.DeveloperRoleId, new DateOnly(2025, 12, 31)));
        Assert.Equal(150.00m, await _rateCards.ResolveAsync(SeedData.LeaveProjectId, SeedData.DeveloperRoleId, new DateOnly(2026, 6, 30)));
        Assert.Equal(165.00m, await _rateCards.ResolveAsync(SeedData.LeaveProjectId, SeedData.DeveloperRoleId, new DateOnly(2026, 7, 1)));

        var summaries = await _rateCards.GetForProjectAsync(SeedData.LeaveProjectId);
        Assert.Equal(2, summaries.Count);
        Assert.All(summaries, s => Assert.Equal("Developer", s.RoleName));
    }

    [Fact]
    public async Task Overlapping_rates_are_rejected_by_the_database()
    {
        await _rateCards.AddAsync(new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            RoleId = SeedData.ProjectManagerRoleId,
            HourlyRate = 175.00m,
            EffectiveFrom = new DateOnly(2026, 1, 1),
        });

        // Open-ended row already covers 2026-03-01 → the exclusion constraint fires.
        await Assert.ThrowsAsync<PostgresException>(() => _rateCards.AddAsync(new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            RoleId = SeedData.ProjectManagerRoleId,
            HourlyRate = 185.00m,
            EffectiveFrom = new DateOnly(2026, 3, 1),
        }));
    }

    [Fact]
    public async Task Assignment_roundtrip_and_unique_row_per_person()
    {
        var assignment = new ProjectAssignment
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            EmployeeId = SeedData.AdminEmployeeId,
            DefaultBillingRoleId = SeedData.DeveloperRoleId,
            StartDate = new DateOnly(2026, 1, 1),
        };
        await _assignments.AddAsync(assignment);

        var fetched = await _assignments.GetByProjectAndEmployeeAsync(SeedData.LeaveProjectId, SeedData.AdminEmployeeId);
        Assert.NotNull(fetched);
        Assert.Equal(assignment.Id, fetched.Id);
        Assert.True(fetched.IsActiveOn(new DateOnly(2026, 7, 8)));

        fetched.EndDate = new DateOnly(2026, 7, 8);
        await _assignments.UpdateAsync(fetched);
        var ended = await _assignments.GetAsync(assignment.Id);
        Assert.False(ended!.IsActiveOn(new DateOnly(2026, 7, 9)));

        var summaries = await _assignments.GetForProjectAsync(SeedData.LeaveProjectId);
        var summary = Assert.Single(summaries);
        Assert.Equal("Don Woods", summary.EmployeeName);
        Assert.Equal("Developer", summary.DefaultRoleName);

        // unique (project_id, employee_id)
        await Assert.ThrowsAsync<PostgresException>(() => _assignments.AddAsync(new ProjectAssignment
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            EmployeeId = SeedData.AdminEmployeeId,
        }));
    }
}
