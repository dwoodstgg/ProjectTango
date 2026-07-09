using Dapper;
using Npgsql;
using ProjectTango.Application.Preferences;

namespace ProjectTango.Infrastructure.Persistence.Repositories;

public class EmployeePreferenceRepository(NpgsqlDataSource dataSource) : IEmployeePreferenceRepository
{
    public async Task<string?> GetAsync(Guid employeeId, string key, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT pref_value FROM employee_preferences WHERE employee_id = @employeeId AND pref_key = @key",
            new { employeeId, key },
            cancellationToken: cancellationToken));
    }

    public async Task SetAsync(Guid employeeId, string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO employee_preferences (employee_id, pref_key, pref_value, updated_at)
            VALUES (@employeeId, @key, @value, now())
            ON CONFLICT (employee_id, pref_key) DO UPDATE SET
                pref_value = EXCLUDED.pref_value,
                updated_at = now()
            """,
            new { employeeId, key, value },
            cancellationToken: cancellationToken));
    }
}
