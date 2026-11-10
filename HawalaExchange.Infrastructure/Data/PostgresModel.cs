using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HawalaExchange.Infrastructure.Data;

internal static class PostgresModel
{
    private static readonly ValueConverter<DateTime, DateTime> Timestamp = new(
        value => DateTime.SpecifyKind(value, DateTimeKind.Unspecified),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static readonly ValueConverter<DateTimeOffset, DateTimeOffset> Offset = new(
        value => value.ToUniversalTime(), value => value.ToUniversalTime());

    public static void Configure(ModelBuilder builder)
    {
        builder.UseIdentityByDefaultColumns();
        builder.UseCollation("hawala_sqlserver_ci");
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            foreach (var annotation in entity.GetAnnotations().Where(a => a.Name.StartsWith("SqlServer:")).ToArray())
                entity.RemoveAnnotation(annotation.Name);
            foreach (var property in entity.GetProperties())
            {
                foreach (var annotation in property.GetAnnotations().Where(a => a.Name.StartsWith("SqlServer:")).ToArray())
                    property.RemoveAnnotation(annotation.Name);
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (type == typeof(DateTime))
                {
                    property.SetValueConverter(Timestamp);
                    if (property.GetColumnType() != "date") property.SetColumnType("timestamp without time zone");
                }
                else if (type == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(Offset);
                    property.SetColumnType("timestamp with time zone");
                }
                else if (type == typeof(byte[]) && property.IsConcurrencyToken)
                    property.SetColumnType("bytea");
                if (property.GetDefaultValueSql() is { } sql)
                {
                    if (sql.Equals("GETUTCDATE()", StringComparison.OrdinalIgnoreCase) || sql.Equals("SYSUTCDATETIME()", StringComparison.OrdinalIgnoreCase))
                        property.SetDefaultValueSql("(CURRENT_TIMESTAMP AT TIME ZONE 'UTC')");
                    else throw new InvalidOperationException($"Unported PostgreSQL model default: {entity.Name}.{property.Name}");
                }
            }
            foreach (var index in entity.GetIndexes())
                if (index.GetFilter() is { } filter) index.SetFilter(Expression(filter, entity));
            foreach (var check in entity.GetCheckConstraints().ToArray())
            {
                var name = check.ModelName;
                var sql = Expression(check.Sql, entity);
                entity.RemoveCheckConstraint(name);
                entity.AddCheckConstraint(name, sql);
            }
        }
    }

    internal static string Expression(string expression, IMutableEntityType entity)
    {
        var sql = Regex.Replace(expression, @"\[([^\]]+)\]", "\"$1\"");
        foreach (var property in entity.GetProperties().Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType) == typeof(bool)))
            sql = Regex.Replace(sql, Regex.Escape("\"" + property.Name + "\"") + @"\s*=\s*\(?([01])\)?",
                m => "\"" + property.Name + "\" = " + (m.Groups[1].Value == "1" ? "true" : "false"));
        return sql;
    }
}
