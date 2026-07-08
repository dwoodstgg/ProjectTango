using Microsoft.Extensions.DependencyInjection;
using ProjectTango.Application.Clients;
using ProjectTango.Application.Employees;
using ProjectTango.Application.Projects;

namespace ProjectTango.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<EmployeeProvisioningService>();
        services.AddScoped<EmployeeAdminService>();
        services.AddScoped<ClientAdminService>();
        services.AddScoped<ProjectAdminService>();
        services.AddScoped<RateCardService>();
        services.AddScoped<AssignmentService>();
        return services;
    }
}
