using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProjectTango.Infrastructure.Persistence;

namespace ProjectTango.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TangoDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("ProjectTango"))
                   .UseSnakeCaseNamingConvention());

        return services;
    }
}
