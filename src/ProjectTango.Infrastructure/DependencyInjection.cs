using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProjectTango.Application.Roles;
using ProjectTango.Infrastructure.Persistence.Repositories;

namespace ProjectTango.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Dapper maps snake_case columns (is_billable) onto PascalCase properties (IsBillable).
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var connectionString = configuration.GetConnectionString("ProjectTango")
            ?? throw new InvalidOperationException("Connection string 'ProjectTango' is missing.");

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        services.AddScoped<IRoleRepository, RoleRepository>();

        return services;
    }
}
