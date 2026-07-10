using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProjectTango.Application.Clients;
using ProjectTango.Application.Common;
using ProjectTango.Application.Employees;
using ProjectTango.Application.Preferences;
using ProjectTango.Application.Projects;
using ProjectTango.Application.Roles;
using ProjectTango.Application.TimeEntries;
using ProjectTango.Infrastructure.Persistence;
using ProjectTango.Infrastructure.Persistence.Repositories;

namespace ProjectTango.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        DapperConfig.Apply();

        var connectionString = configuration.GetConnectionString("ProjectTango")
            ?? throw new InvalidOperationException("Connection string 'ProjectTango' is missing.");

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IRateCardRepository, RateCardRepository>();
        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddScoped<IAssignmentRepository, AssignmentRepository>();
        services.AddScoped<ITimeEntryRepository, TimeEntryRepository>();
        services.AddScoped<ITimesheetPeriodRepository, TimesheetPeriodRepository>();
        services.AddScoped<IEmployeePreferenceRepository, EmployeePreferenceRepository>();
        services.AddScoped<IAuditLog, AuditLogRepository>();

        return services;
    }
}
