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
    public async Task HasInvoicedTime_binds_open_ended_window_and_respects_bounds()
    {
        var from = new DateOnly(2026, 1, 1);

        // Regression: an open-ended rate passes effectiveTo = null. The query must still bind
        // the parameter's type (::date), or Postgres raises 42P08 "could not determine data type".
        Assert.False(await _rateCards.HasInvoicedTimeAsync(
            SeedData.LeaveProjectId, SeedData.DeveloperRoleId, from, null));

        await InsertInvoicedEntryAsync(new DateOnly(2026, 3, 15));

        Assert.True(await _rateCards.HasInvoicedTimeAsync(
            SeedData.LeaveProjectId, SeedData.DeveloperRoleId, from, null));
        // A closed window ending before the entry excludes it.
        Assert.False(await _rateCards.HasInvoicedTimeAsync(
            SeedData.LeaveProjectId, SeedData.DeveloperRoleId, from, new DateOnly(2026, 2, 28)));
        // A window covering the entry includes it.
        Assert.True(await _rateCards.HasInvoicedTimeAsync(
            SeedData.LeaveProjectId, SeedData.DeveloperRoleId, from, new DateOnly(2026, 3, 31)));
        // A different billing role is unaffected.
        Assert.False(await _rateCards.HasInvoicedTimeAsync(
            SeedData.LeaveProjectId, SeedData.ProjectManagerRoleId, from, null));
    }

    [Fact]
    public async Task GetForProject_flags_rows_with_invoiced_time()
    {
        await _rateCards.AddAsync(new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            RoleId = SeedData.DeveloperRoleId,
            HourlyRate = 150.00m,
            EffectiveFrom = new DateOnly(2026, 1, 1),
        });
        await InsertInvoicedEntryAsync(new DateOnly(2026, 5, 1));

        var summary = Assert.Single(await _rateCards.GetForProjectAsync(SeedData.LeaveProjectId));
        Assert.True(summary.HasBilledTime);
    }

    [Fact]
    public async Task Correct_shifts_start_and_recloses_predecessor_in_one_transaction()
    {
        var prior = new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            RoleId = SeedData.DeveloperRoleId,
            HourlyRate = 150.00m,
            EffectiveFrom = new DateOnly(2026, 1, 1),
        };
        await _rateCards.AddAsync(prior);
        await _rateCards.CloseAsync(prior.Id, new DateOnly(2026, 6, 30));
        var current = new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            RoleId = SeedData.DeveloperRoleId,
            HourlyRate = 165.00m,
            EffectiveFrom = new DateOnly(2026, 7, 1),
        };
        await _rateCards.AddAsync(current);

        // Slide the change a month earlier and re-close the predecessor to abut it. The
        // exclusion constraint is deferred so the intermediate overlap is tolerated.
        await _rateCards.CorrectAsync(current.Id, 170.00m, new DateOnly(2026, 6, 1), prior.Id, new DateOnly(2026, 5, 31));

        Assert.Equal(150.00m, await _rateCards.ResolveAsync(SeedData.LeaveProjectId, SeedData.DeveloperRoleId, new DateOnly(2026, 5, 31)));
        Assert.Equal(170.00m, await _rateCards.ResolveAsync(SeedData.LeaveProjectId, SeedData.DeveloperRoleId, new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public async Task SoftDelete_hides_current_row_and_reopens_predecessor()
    {
        var prior = new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            RoleId = SeedData.DeveloperRoleId,
            HourlyRate = 150.00m,
            EffectiveFrom = new DateOnly(2026, 1, 1),
        };
        await _rateCards.AddAsync(prior);
        await _rateCards.CloseAsync(prior.Id, new DateOnly(2026, 6, 30));
        var current = new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            RoleId = SeedData.DeveloperRoleId,
            HourlyRate = 165.00m,
            EffectiveFrom = new DateOnly(2026, 7, 1),
        };
        await _rateCards.AddAsync(current);

        await _rateCards.SoftDeleteAsync(current.Id, prior.Id);

        // Removed row is invisible to every read path.
        Assert.Null(await _rateCards.GetByIdAsync(current.Id));
        Assert.Single(await _rateCards.GetForProjectAsync(SeedData.LeaveProjectId));
        // Predecessor reopened → it now resolves past the old boundary.
        Assert.Equal(150.00m, await _rateCards.ResolveAsync(SeedData.LeaveProjectId, SeedData.DeveloperRoleId, new DateOnly(2026, 8, 1)));
    }

    [Fact]
    public async Task SoftDelete_frees_the_window_for_reuse()
    {
        var mistake = new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            RoleId = SeedData.DeveloperRoleId,
            HourlyRate = 99.00m,
            EffectiveFrom = new DateOnly(2026, 7, 8),
        };
        await _rateCards.AddAsync(mistake);
        await _rateCards.SoftDeleteAsync(mistake.Id, null);

        Assert.Null(await _rateCards.GetByIdAsync(mistake.Id));

        // The exclusion constraint ignores soft-deleted rows, so the same window is free.
        await _rateCards.AddAsync(new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            RoleId = SeedData.DeveloperRoleId,
            HourlyRate = 135.00m,
            EffectiveFrom = new DateOnly(2026, 7, 8),
        });
        Assert.Equal(135.00m, await _rateCards.ResolveAsync(SeedData.LeaveProjectId, SeedData.DeveloperRoleId, new DateOnly(2026, 7, 8)));
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

    private async Task InsertInvoicedEntryAsync(DateOnly date)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO time_entries
                (id, project_id, employee_id, billing_role_id, entry_date, hours_worked, hours_billed, status)
            VALUES (@id, @projectId, @employeeId, @roleId, @date, 8, 8, 'invoiced')
            """,
            new
            {
                id = Guid.NewGuid(),
                projectId = SeedData.LeaveProjectId,
                employeeId = SeedData.AdminEmployeeId,
                roleId = SeedData.DeveloperRoleId,
                date,
            });
    }
}
