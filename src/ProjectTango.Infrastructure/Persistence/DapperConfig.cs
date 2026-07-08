using System.Data;
using Dapper;

namespace ProjectTango.Infrastructure.Persistence;

/// <summary>Global Dapper configuration. Applied by AddInfrastructure at startup;
/// tests that construct repositories directly must call <see cref="Apply"/> too.</summary>
public static class DapperConfig
{
    public static void Apply()
    {
        // snake_case columns (is_billable) → PascalCase properties (IsBillable).
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        // Dapper has no built-in parameter support for DateOnly (Postgres `date`).
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
    }

    private sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value;
        }

        public override DateOnly Parse(object value) => value switch
        {
            DateOnly dateOnly => dateOnly,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            _ => throw new InvalidCastException($"Cannot convert {value.GetType()} to DateOnly."),
        };
    }
}
