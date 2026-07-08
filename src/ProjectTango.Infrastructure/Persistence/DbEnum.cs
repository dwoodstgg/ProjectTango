using System.Text;

namespace ProjectTango.Infrastructure.Persistence;

/// <summary>Converts between C# enum members (OnHold) and the snake_case text values
/// stored in the database ('on_hold', per design-doc §5). Dapper cannot auto-parse
/// multi-word snake_case into enums, so repositories map explicitly with this.</summary>
public static class DbEnum
{
    public static string ToDb<T>(T value) where T : struct, Enum
    {
        var name = value.ToString();
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(name[i]));
        }

        return sb.ToString();
    }

    public static T FromDb<T>(string value) where T : struct, Enum =>
        Enum.Parse<T>(value.Replace("_", string.Empty), ignoreCase: true);
}
