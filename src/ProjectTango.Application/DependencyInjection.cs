using Microsoft.Extensions.DependencyInjection;

namespace ProjectTango.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Application services register here as features land (see design-doc.md §9 roadmap).
        return services;
    }
}
