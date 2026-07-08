using Microsoft.EntityFrameworkCore;

namespace ProjectTango.Infrastructure.Persistence;

public class TangoDbContext(DbContextOptions<TangoDbContext> options) : DbContext(options)
{
    // DbSets are added feature by feature; tables/columns are snake_case via
    // UseSnakeCaseNamingConvention to match design-doc.md §5.

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TangoDbContext).Assembly);
    }
}
