using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;

namespace HawalaExchange.Infrastructure.Data;

internal static class PostgresResultReader
{
    public static DbDataReader Create(string json)
    {
        using var document=JsonDocument.Parse(json);
        var dataset=new DataSet();
        foreach(var set in document.RootElement.EnumerateArray())
        {
            var table=new DataTable();
            foreach(var row in set.EnumerateArray())
            {
                if(table.Columns.Count==0)
                    foreach(var property in row.EnumerateObject())table.Columns.Add(property.Name,TypeFor(property));
                var values=new object[table.Columns.Count];
                for(var i=0;i<values.Length;i++)
                {
                    var value=row.GetProperty(table.Columns[i].ColumnName);var type=table.Columns[i].DataType;
                    values[i]=value.ValueKind==JsonValueKind.Null?DBNull.Value:
                        type==typeof(long)?value.GetInt64():type==typeof(int)?value.GetInt32():type==typeof(decimal)?value.GetDecimal():
                        type==typeof(DateTime)?DateTime.Parse(value.GetString()!,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind):
                        type==typeof(bool)?value.GetBoolean():value.GetString()!;
                }
                table.Rows.Add(values);
            }
            dataset.Tables.Add(table);
        }
        return dataset.CreateDataReader();
    }
    private static Type TypeFor(JsonProperty property)
    {
        var name=property.Name;
        if(name=="Id" || name.EndsWith("Id",StringComparison.Ordinal) || name is "HawalaNumber" or "TotalCount")return typeof(long);
        if(name.EndsWith("Count",StringComparison.Ordinal))return typeof(int);
        if(name.EndsWith("Date",StringComparison.Ordinal)||name.EndsWith("At",StringComparison.Ordinal)||name is "PeriodFrom" or "PeriodTo")return typeof(DateTime);
        return property.Value.ValueKind switch
        {JsonValueKind.Number=>typeof(decimal),JsonValueKind.True or JsonValueKind.False=>typeof(bool),_=>typeof(string)};
    }
}
